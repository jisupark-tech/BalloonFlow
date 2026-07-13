using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Firebase.Auth;
using Facebook.Unity;
using BalloonFlow.Analytics;

namespace BalloonFlow
{
    /// <summary>
    /// 통합 analytics 매니저.
    /// [2026-06-16 BQ_DIRECT] Firebase Analytics→BigQuery 자동 export 를 폐기하고, 커스텀 이벤트를
    ///   Cloud Function(ingestAnalyticsEvents)으로 직접 배치 전송 → BigQuery streaming insert 로 적재.
    ///   Facebook / AppsFlyer(마케팅·어트리뷰션)는 그대로 유지.
    /// 인증: Firebase Auth ID 토큰(Bearer). CurrentUser 없으면 익명 로그인 lazy 폴백(메모리상 Anonymous 결정).
    ///   외부 인증 도입 시 CurrentUser 가 채워져 동일 경로로 무변경 동작.
    /// 전송 전 이벤트는 _bqBatch 에 버퍼링(앱 시작~Firebase ready 사이 포함). 실패 시 재시도, 무한 버퍼 방지 cap.
    /// </summary>
    public class AnalyticsManager : Singleton<AnalyticsManager>
    {
        private const string LOG_TAG = "[AnalyticsManager]";

        // ─── 직접 적재 엔드포인트 (배포 후 실제 URL 로 확인/교체) ───
        // v2 onRequest 기본 별칭: https://<region>-<project>.cloudfunctions.net/<fn>
        // gen2 는 Cloud Run URL(...run.app)도 가짐 — 배포 로그의 URL 로 검증 후 고정.
        private const string INGEST_URL =
            "https://us-central1-balloonloop-d855d.cloudfunctions.net/ingestAnalyticsEvents";

        // 배치 정책
        private const int   BQ_BATCH_FLUSH_COUNT   = 20;    // 이만큼 쌓이면 즉시 flush
        private const float BQ_FLUSH_INTERVAL_SEC  = 15f;   // 주기적 flush
        private const int   BQ_MAX_EVENTS_PER_POST = 100;   // Express body limit(100kb) 안전 여유. 서버 상한은 500
        private const int   BQ_MAX_BUFFER          = 512;   // 오프라인 장기화 시 메모리 상한(초과분 oldest drop)

        private readonly List<BqEvent> _bqBatch = new List<BqEvent>(64);
        private float _flushTimer;
        private bool  _flushing;
        private bool  _anonSignInInFlight;

        // ROLLBACK_ANALYTICS_DISK_PERSIST_20260707: START — 전송 전 버퍼 디스크 영속화.
        // 프로세스 킬(스와이프킬/OS킬/Editor 정지) 시 미전송 이벤트가 통째로 유실되던 문제.
        // pause/quit 에 버퍼를 파일로 스냅샷 → 다음 실행 시 로드해 재전송. event_id 가 BQ insertId 라
        // 재전송 중복은 서버 streaming dedup 이 흡수. 롤백: 이 필드/PersistBuffer/LoadPersistedEvents
        // 및 호출부 제거.
        private static string PendingFilePath => Path.Combine(Application.persistentDataPath, "bq_pending_events.json");
        private bool _hasPersistedFile;
        private bool _quitting;
        // ROLLBACK_ANALYTICS_DISK_PERSIST_20260707: END (필드)

        private bool _facebookReady;
        public bool FacebookReady => _facebookReady;

        protected override void OnSingletonAwake()
        {
            InitFacebook();
            LoadPersistedEvents();
        }

        #region Init (Facebook)

        private void InitFacebook()
        {
            if (FB.IsInitialized)
            {
                FB.ActivateApp();
                _facebookReady = true;
                Debug.Log($"{LOG_TAG} Facebook already initialized.");
                return;
            }

            FB.Init(
                onInitComplete: OnFacebookInitComplete,
                onHideUnity:    OnFacebookHidden);
        }

        private void OnFacebookInitComplete()
        {
            if (FB.IsInitialized)
            {
                FB.ActivateApp();
                _facebookReady = true;
                Debug.Log($"{LOG_TAG} Facebook initialized.");
            }
            else
            {
                Debug.LogError($"{LOG_TAG} Facebook init failed.");
            }
        }

        private void OnFacebookHidden(bool isUnityShown) { /* App resumed/paused */ }

        #endregion

        #region LogEvent — 통합 인터페이스

        /// <summary>이벤트 발행. BigQuery(직접 적재) + Facebook + AppsFlyer 전송.</summary>
        public void LogEvent(string eventName, Dictionary<string, object> parameters = null)
        {
            if (string.IsNullOrEmpty(eventName)) return;

            EnqueueBigQuery(eventName, parameters);
            LogToFacebook(eventName, parameters);
            LogToAppsFlyer(eventName, parameters);
        }

        /// <summary>단일 string 파라미터 편의 오버로드.</summary>
        public void LogEvent(string eventName, string paramName, string paramValue)
        {
            var p = new Dictionary<string, object> { [paramName] = paramValue };
            LogEvent(eventName, p);
        }

        #endregion

        #region BigQuery — 배치 enqueue / flush

        // ROLLBACK_ANALYTICS_EDITOR_INGEST_BLOCK_20260709: 에디터 세션의 BQ 적재 차단 토글.
        //   에디터는 pause 콜백 부재 + 재생 중지 워크플로 특성으로 session end 유실이 구조적
        //   (2026-07-09 실측: editor 진짜 누수 46건/16.9% vs android 5건/1.96%) → 지표 오염 원천 차단.
        //   파이프라인 E2E 검증(에디터에서 BQ 적재 확인)이 필요할 때만 true 로 잠깐 변경.
        private const bool ALLOW_EDITOR_BQ_INGEST = false;

        private void EnqueueBigQuery(string eventName, Dictionary<string, object> parameters)
        {
#if UNITY_EDITOR
            // ROLLBACK_ANALYTICS_EDITOR_INGEST_BLOCK_20260709: 에디터 → BQ 차단 (FB/AppsFlyer 경로는 무관).
#pragma warning disable CS0162
            if (!ALLOW_EDITOR_BQ_INGEST) return;
#pragma warning restore CS0162
#endif
            // 호출자 dict 를 그대로 보관하면 이후 변형 위험 → 얕은 복사로 스냅샷.
            var data = NormalizeBigQueryEvent(eventName, parameters);

            if (_bqBatch.Count >= BQ_MAX_BUFFER)
            {
                _bqBatch.RemoveAt(0); // oldest drop (오프라인 장기화 메모리 상한)
                Debug.LogWarning($"{LOG_TAG} BQ buffer full ({BQ_MAX_BUFFER}). Dropping oldest event.");
            }
            _bqBatch.Add(new BqEvent { name = eventName, data = data });

            // quit 진행 중 enqueue(SessionTracker 의 session_end 등) — OnApplicationQuit 호출 순서가
            // 컴포넌트 간 비결정적이라, 늦게 들어온 이벤트도 파일에 반영되도록 즉시 재영속화.
            if (_quitting)
            {
                PersistBuffer();
                return;
            }

            // ROLLBACK_ANALYTICS_START_PERSIST_20260709: session_start 는 즉시 디스크 영속(세션당 1회 IO).
            //   포그라운드 크래시(퍼즈 콜백 없이 사망) 시 15s 배치 창 안의 start 가 메모리에서 유실되어
            //   orphan 소급 end 만 남는 역이격(2026-07-09 실측 9건) 방어. 롤백: 이 2줄 제거.
            if (eventName == Analytics.AnalyticsConsts.EVT_SESSION_START)
                PersistBuffer();

            // [ANALYTICS_PLAYEND_FLUSH 2026-06-24] 레벨 종료(play_end)는 즉시 flush.
            // 패배→빠른 재시도(타이머 15s/카운트 20 임계 전) 시 씬 전환되면 적재가 다음 판까지 밀리던
            // '한 판 지연' 버그 방지. 같이 대기 중이던 play_start 도 이때 함께 전송됨.
            // ROLLBACK_SESSION_END_FLUSH_20260713: session_end 도 즉시 flush 대상에 추가.
            //   기존엔 15s 타이머/20개 임계까지 버퍼에 머물러, 백그라운드 진입/종료 직후 프로세스가
            //   킬되면 디스크 영속에만 의존(다음 부트까지 미전송)했다 — session_end 유실의 한 축.
            //   생성 즉시 flush + (호출부 pause/quit 의 PersistBuffer) 로 이중 방어. 롤백: EVT_SESSION_END 조건 제거.
            if (eventName == Analytics.AnalyticsConsts.EVT_LEVEL_PLAY
                || eventName == Analytics.AnalyticsConsts.EVT_SESSION_END
                || _bqBatch.Count >= BQ_BATCH_FLUSH_COUNT)
                TryFlush();
        }

        private void Update()
        {
            if (_bqBatch.Count == 0) return;
            _flushTimer += Time.unscaledDeltaTime;
            if (_flushTimer >= BQ_FLUSH_INTERVAL_SEC)
                TryFlush();
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            // 백그라운드에서 그대로 킬당해도 유실 없도록 먼저 영속화, 이후 best-effort 전송.
            PersistBuffer();
            TryFlush();
        }

        private void OnApplicationQuit()
        {
            _quitting = true;
            PersistBuffer();
            TryFlush(); // best-effort — 프로세스 종료 전 완료 보장 없음. 못 보낸 분량은 파일로 복구.
        }

        private void TryFlush()
        {
            _flushTimer = 0f;
            if (_flushing || _bqBatch.Count == 0) return;
            if (!HasInstance) return;
            StartCoroutine(FlushRoutine());
        }

        private IEnumerator FlushRoutine()
        {
            _flushing = true;

            // 1) Firebase / Auth 준비 확인 — 미준비면 이번 flush 스킵(버퍼 유지, 다음 주기 재시도).
            if (!FirebaseManager.HasInstance || !FirebaseManager.Instance.IsReady
                || FirebaseManager.Instance.Auth == null)
            {
                _flushing = false;
                yield break;
            }
            FirebaseAuth auth = FirebaseManager.Instance.Auth;

            // 2) 사용자 토큰 확보 — CurrentUser 없으면 익명 로그인 lazy 폴백.
            if (auth.CurrentUser == null)
            {
                if (!_anonSignInInFlight)
                {
                    _anonSignInInFlight = true;
                    var signInTask = auth.SignInAnonymouslyAsync();
                    while (!signInTask.IsCompleted) yield return null;
                    _anonSignInInFlight = false;

                    if (signInTask.IsFaulted || signInTask.IsCanceled || auth.CurrentUser == null)
                    {
                        Debug.LogWarning($"{LOG_TAG} Anonymous sign-in 실패 — flush 보류(다음 주기 재시도). " +
                                         "콘솔에서 Anonymous 인증 provider 활성화 확인 필요.");
                        _flushing = false;
                        yield break;
                    }
                }
                else { _flushing = false; yield break; }
            }

            var tokenTask = auth.CurrentUser.TokenAsync(false);
            while (!tokenTask.IsCompleted) yield return null;
            if (tokenTask.IsFaulted || tokenTask.IsCanceled || string.IsNullOrEmpty(tokenTask.Result))
            {
                Debug.LogWarning($"{LOG_TAG} ID 토큰 획득 실패 — flush 보류.");
                _flushing = false;
                yield break;
            }
            string idToken = tokenTask.Result;

            // 3) 전송 스냅샷 — 앞에서부터 최대 N개.
            int sendCount = Mathf.Min(_bqBatch.Count, BQ_MAX_EVENTS_PER_POST);
            var sending = new List<BqEvent>(sendCount);
            for (int i = 0; i < sendCount; i++) sending.Add(_bqBatch[i]);

            string json = BuildBatchJson(sending);
            byte[] body = Encoding.UTF8.GetBytes(json);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"{LOG_TAG} BQ flush 시도 → {sendCount}건 POST {INGEST_URL}");
#endif

            using (var req = new UnityWebRequest(INGEST_URL, "POST"))
            {
                req.uploadHandler   = new UploadHandlerRaw(body);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + idToken);
                req.timeout = 30;

                yield return req.SendWebRequest();

                long code = req.responseCode;
                bool networkErr =
#if UNITY_2020_1_OR_NEWER
                    req.result == UnityWebRequest.Result.ConnectionError ||
                    req.result == UnityWebRequest.Result.DataProcessingError;
#else
                    req.isNetworkError;
#endif

                if (!networkErr && code >= 200 && code < 300)
                {
                    // 성공 — 보낸 만큼 앞에서 제거.
                    _bqBatch.RemoveRange(0, sendCount);
                    SyncPersistedFile();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"{LOG_TAG} BQ ingest OK — {sendCount}건 적재 (resp={code}, 잔여 {_bqBatch.Count}) {req.downloadHandler.text}");
#endif
                }
                else if (!networkErr && code >= 400 && code < 500)
                {
                    // 클라 오류(스키마/인증 등) — 재시도해도 동일. 해당 배치 폐기(무한 재시도 방지).
                    Debug.LogWarning($"{LOG_TAG} BQ ingest 4xx({code}) — {sendCount}건 폐기. resp={req.downloadHandler.text}");
                    _bqBatch.RemoveRange(0, sendCount);
                    SyncPersistedFile();
                }
                else
                {
                    // 네트워크/5xx — 버퍼 유지, 다음 주기 재시도.
                    Debug.LogWarning($"{LOG_TAG} BQ ingest 실패(code={code}, net={networkErr}) — {sendCount}건 재시도 대기.");
                }
            }

            _flushing = false;
        }

        // ROLLBACK_ANALYTICS_DISK_PERSIST_20260707: START (메서드)

        /// <summary>현재 버퍼를 파일로 스냅샷. 비었으면 파일 삭제. pause/quit/quit-중-enqueue 에서 호출.</summary>
        private void PersistBuffer()
        {
            try
            {
                if (_bqBatch.Count == 0)
                {
                    if (_hasPersistedFile && File.Exists(PendingFilePath)) File.Delete(PendingFilePath);
                    _hasPersistedFile = false;
                    return;
                }
                File.WriteAllText(PendingFilePath, JsonConvert.SerializeObject(_bqBatch));
                _hasPersistedFile = true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"{LOG_TAG} BQ 버퍼 영속화 실패: {e.Message}");
            }
        }

        /// <summary>
        /// flush 로 버퍼가 줄었을 때 기존 스냅샷 파일을 동기화 — pause 영속화 후 복귀해 전송이
        /// 성공한 케이스에서 stale 파일이 남아 다음 실행에 재전송(중복)되는 것 방지.
        /// 파일이 없으면 아무것도 안 함(플레이 중 매 flush 마다 IO 하지 않도록).
        /// </summary>
        private void SyncPersistedFile()
        {
            if (_hasPersistedFile) PersistBuffer();
        }

        /// <summary>이전 실행에서 못 보낸 이벤트 로드(부트 1회). 읽는 즉시 파일 삭제 — 이후 pause 마다 재스냅샷.</summary>
        private void LoadPersistedEvents()
        {
            try
            {
                if (!File.Exists(PendingFilePath)) return;
                string json = File.ReadAllText(PendingFilePath);
                File.Delete(PendingFilePath);

                var restored = JsonConvert.DeserializeObject<List<BqEvent>>(json);
                if (restored == null || restored.Count == 0) return;

                // 복원분이 시간상 먼저 — 버퍼 앞에 삽입. cap 초과 시 oldest(복원분 앞쪽)부터 버림.
                int room = BQ_MAX_BUFFER - _bqBatch.Count;
                if (restored.Count > room && room >= 0)
                    restored.RemoveRange(0, restored.Count - room);
                _bqBatch.InsertRange(0, restored);
                Debug.Log($"{LOG_TAG} 이전 실행 미전송 이벤트 {restored.Count}건 복원 — 다음 flush 에 재전송.");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"{LOG_TAG} BQ 버퍼 복원 실패(폐기): {e.Message}");
            }
        }
        // ROLLBACK_ANALYTICS_DISK_PERSIST_20260707: END (메서드)

        private static string BuildBatchJson(List<BqEvent> sending)
        {
            var events = new List<object>(sending.Count);
            for (int i = 0; i < sending.Count; i++)
                events.Add(new { name = sending[i].name, data = sending[i].data });
            return JsonConvert.SerializeObject(new { events });
        }

        // ROLLBACK_BQ_SCHEMA_V32_20260626:
        // Keep this normalization isolated to BigQuery. Facebook/Appsflyer still receive the original
        // event payload, while the Cloud Function receives only columns from puzzle_game_data_schema_v3_2.
        private static Dictionary<string, object> NormalizeBigQueryEvent(string eventName, Dictionary<string, object> parameters)
        {
            var source = parameters != null
                ? new Dictionary<string, object>(parameters)
                : new Dictionary<string, object>(16);

            FillBigQueryCommonDefaults(source);
            NormalizeBigQueryAliases(eventName, source);

            string[] schema = GetBigQuerySchema(eventName);
            if (schema == null) return source;

            var filtered = new Dictionary<string, object>(schema.Length);
            for (int i = 0; i < schema.Length; i++)
            {
                string key = schema[i];
                if (source.TryGetValue(key, out object value))
                    filtered[key] = value;
            }
            return filtered;
        }

        private static void FillBigQueryCommonDefaults(Dictionary<string, object> data)
        {
            if (!data.ContainsKey(AnalyticsConsts.P_EVENT_ID))
                data[AnalyticsConsts.P_EVENT_ID] = System.Guid.NewGuid().ToString("N");
            if (!data.ContainsKey(AnalyticsConsts.P_GAME_ID))
                data[AnalyticsConsts.P_GAME_ID] = AnalyticsConsts.GAME_ID;
            if (!data.ContainsKey(AnalyticsConsts.P_UID))
                data[AnalyticsConsts.P_UID] = AnalyticsSessionTracker.ResolveUid();
            if (!data.ContainsKey(AnalyticsConsts.P_EVENT_TS))
                data[AnalyticsConsts.P_EVENT_TS] = System.DateTime.UtcNow.ToString("o");
            if (!data.ContainsKey(AnalyticsConsts.P_SESSION_ID))
                data[AnalyticsConsts.P_SESSION_ID] = AnalyticsSessionTracker.HasInstance ? AnalyticsSessionTracker.Instance.CurrentSessionId : "";
            if (!data.ContainsKey(AnalyticsConsts.P_APP_VERSION))
                data[AnalyticsConsts.P_APP_VERSION] = Application.version;
            if (!data.ContainsKey(AnalyticsConsts.P_GEO_COUNTRY))
                data[AnalyticsConsts.P_GEO_COUNTRY] = AnalyticsSessionTracker.ResolveGeoCountry();
            if (!data.ContainsKey(AnalyticsConsts.P_PLATFORM))
                data[AnalyticsConsts.P_PLATFORM] = AnalyticsSessionTracker.ResolvePlatform();
            if (!data.ContainsKey(AnalyticsConsts.P_DEVICE_MODEL))
                data[AnalyticsConsts.P_DEVICE_MODEL] = SystemInfo.deviceModel;

            if (UserSnapshotCache.HasInstance
                && (!data.ContainsKey(AnalyticsConsts.P_INSTALL_AT)
                    || !data.ContainsKey(AnalyticsConsts.P_MAX_REACHED_LEVEL)
                    || !data.ContainsKey(AnalyticsConsts.P_TOTAL_SPEND_USD)
                    || !data.ContainsKey(AnalyticsConsts.P_TOTAL_AD_REVENUE_USD)))
            {
                UserSnapshotCache.Instance.Stamp(data);
            }
        }

        private static void NormalizeBigQueryAliases(string eventName, Dictionary<string, object> data)
        {
            CopyAlias(data, "event_ts", AnalyticsConsts.P_EVENT_TS);
            CopyAlias(data, AnalyticsConsts.P_TRANSACTION_ID, AnalyticsConsts.P_RECEIPT_ID);
            CopyAlias(data, AnalyticsConsts.P_CURRENCY, AnalyticsConsts.P_CURRENCY_CODE);
            CopyAlias(data, "ad_revenue_usd", AnalyticsConsts.P_REVENUE_USD);

            if (eventName == AnalyticsConsts.EVT_SESSION_START)
            {
                CopyAlias(data, AnalyticsConsts.P_APP_VERSION, AnalyticsConsts.P_VERSION);
                CopyAlias(data, AnalyticsConsts.P_GEO_COUNTRY, AnalyticsConsts.P_COUNTRY);
            }

            if (eventName == AnalyticsConsts.EVT_AD)
            {
                if (!data.ContainsKey(AnalyticsConsts.P_AD_REQUEST_ID))
                    data[AnalyticsConsts.P_AD_REQUEST_ID] = data.TryGetValue(AnalyticsConsts.P_EVENT_ID, out object id) ? id : System.Guid.NewGuid().ToString("N");
                if (!data.ContainsKey(AnalyticsConsts.P_EVENT_PHASE))
                    data[AnalyticsConsts.P_EVENT_PHASE] = "impression";
                SetDefaultIfNullOrEmpty(data, AnalyticsConsts.P_AD_TYPE, "unknown");
                SetDefaultIfNullOrEmpty(data, AnalyticsConsts.P_AD_PLACEMENT, "unknown");
                SetDefaultIfMissing(data, AnalyticsConsts.P_LEVEL_NUMBER, LevelManager.HasInstance ? LevelManager.Instance.GetCurrentLevelId() : 0);
            }
            else if (eventName == AnalyticsConsts.EVT_PURCHASE)
            {
                SetDefaultIfNullOrEmpty(data, AnalyticsConsts.P_PRODUCT_ID, "unknown");
                SetDefaultIfNullOrEmpty(data, AnalyticsConsts.P_IAP_PLACEMENT, "shop_popup");
                if (!data.ContainsKey(AnalyticsConsts.P_IS_VERIFIED))
                    data[AnalyticsConsts.P_IS_VERIFIED] = false;
            }
            else if (eventName == AnalyticsConsts.EVT_ECONOMY)
            {
                if (!data.ContainsKey(AnalyticsConsts.P_REF_EVENT_ID) && data.TryGetValue(AnalyticsConsts.P_EVENT_ID, out object id))
                    data[AnalyticsConsts.P_REF_EVENT_ID] = id;
                if (!data.ContainsKey(AnalyticsConsts.P_ECONOMY_PLACEMENT) && data.TryGetValue(AnalyticsConsts.P_SOURCE, out object source))
                    data[AnalyticsConsts.P_ECONOMY_PLACEMENT] = source;
                SetDefaultIfNullOrEmpty(data, AnalyticsConsts.P_CURRENCY_TYPE, "unknown");
                SetDefaultIfMissing(data, AnalyticsConsts.P_CHANGE_AMOUNT, 0);
                SetDefaultIfNullOrEmpty(data, AnalyticsConsts.P_SOURCE, "unknown");
            }
            else if (eventName == AnalyticsConsts.EVT_ITEM_USE)
            {
                string activePlayId = AnalyticsLevelTracker.HasInstance ? AnalyticsLevelTracker.Instance.CurrentPlayId : "";
                SetDefaultIfNullOrEmpty(data, AnalyticsConsts.P_PLAY_ID, string.IsNullOrEmpty(activePlayId) ? "unknown" : activePlayId);
                SetDefaultIfMissing(data, AnalyticsConsts.P_LEVEL_NUMBER, LevelManager.HasInstance ? LevelManager.Instance.GetCurrentLevelId() : 0);
                SetDefaultIfNullOrEmpty(data, AnalyticsConsts.P_ITEM_ID, "unknown");
                SetDefaultIfNullOrEmpty(data, AnalyticsConsts.P_ITEM_CATEGORY, "unknown");
            }
            else if (eventName == AnalyticsConsts.EVT_LEVEL_PLAY_START || eventName == AnalyticsConsts.EVT_LEVEL_PLAY)
            {
                SetDefaultIfNullOrEmpty(data, AnalyticsConsts.P_PLAY_ID, "unknown");
                SetDefaultIfMissing(data, AnalyticsConsts.P_LEVEL_NUMBER, LevelManager.HasInstance ? LevelManager.Instance.GetCurrentLevelId() : 0);
                SetDefaultIfMissing(data, AnalyticsConsts.P_HARD_TIER, 0);
                SetDefaultIfMissing(data, AnalyticsConsts.P_ATTEMPT_NUMBER, 0);
                if (eventName == AnalyticsConsts.EVT_LEVEL_PLAY)
                    SetDefaultIfNullOrEmpty(data, AnalyticsConsts.P_RESULT, "unknown");
            }
            else if (eventName == AnalyticsConsts.EVT_SESSION_END)
            {
                SetDefaultIfNullOrEmpty(data, AnalyticsConsts.P_END_REASON, "unknown");
            }
        }

        private static void CopyAlias(Dictionary<string, object> data, string from, string to)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || data.ContainsKey(to)) return;
            if (data.TryGetValue(from, out object value)) data[to] = value;
        }

        private static void SetDefaultIfMissing(Dictionary<string, object> data, string key, object fallback)
        {
            if (!data.ContainsKey(key)) data[key] = fallback;
        }

        private static void SetDefaultIfNullOrEmpty(Dictionary<string, object> data, string key, object fallback)
        {
            if (!data.TryGetValue(key, out object value) || value == null || string.IsNullOrEmpty(value.ToString()))
                data[key] = fallback;
        }

        private static string[] GetBigQuerySchema(string eventName)
        {
            switch (eventName)
            {
                case AnalyticsConsts.EVT_LEVEL_PLAY_START: return BqLevelPlayStartColumns;
                case AnalyticsConsts.EVT_LEVEL_PLAY:       return BqLevelPlayColumns;
                case AnalyticsConsts.EVT_ITEM_USE:         return BqItemUseColumns;
                case AnalyticsConsts.EVT_PURCHASE:         return BqPurchaseColumns;
                case AnalyticsConsts.EVT_ECONOMY:          return BqEconomyColumns;
                case AnalyticsConsts.EVT_SESSION_START:    return BqSessionStartColumns;
                case AnalyticsConsts.EVT_SESSION_END:      return BqSessionEndColumns;
                case AnalyticsConsts.EVT_AD:               return BqAdColumns;
                case AnalyticsConsts.EVT_USER_PROPERTY:    return BqUserPropertyColumns; // ROLLBACK_USER_PROPERTY_PIPELINE_20260708
                default: return null;
            }
        }

        private static readonly string[] BqLevelPlayStartColumns =
        {
            AnalyticsConsts.P_EVENT_ID, AnalyticsConsts.P_PLAY_ID, AnalyticsConsts.P_SESSION_ID,
            AnalyticsConsts.P_GAME_ID, AnalyticsConsts.P_UID, AnalyticsConsts.P_EVENT_TS,
            AnalyticsConsts.P_APP_VERSION, AnalyticsConsts.P_INSTALL_VERSION, AnalyticsConsts.P_GEO_COUNTRY,
            AnalyticsConsts.P_PLATFORM, AnalyticsConsts.P_DEVICE_MODEL, AnalyticsConsts.P_LEVEL_NUMBER,
            AnalyticsConsts.P_IS_TUTORIAL, AnalyticsConsts.P_HARD_TIER, AnalyticsConsts.P_ATTEMPT_NUMBER,
            AnalyticsConsts.P_IS_FIRST_PLAY, AnalyticsConsts.P_PRE_PLAY_ITEM_IDS, AnalyticsConsts.P_PRE_PLAY_ITEM_COUNT,
            AnalyticsConsts.P_LIVES_BEFORE, AnalyticsConsts.P_IS_INFINITE_LIVES, AnalyticsConsts.P_INSTALL_AT,
            AnalyticsConsts.P_MAX_REACHED_LEVEL, AnalyticsConsts.P_TOTAL_SPEND_USD, AnalyticsConsts.P_TOTAL_AD_REVENUE_USD
        };

        private static readonly string[] BqLevelPlayColumns =
        {
            AnalyticsConsts.P_EVENT_ID, AnalyticsConsts.P_PLAY_ID, AnalyticsConsts.P_SESSION_ID,
            AnalyticsConsts.P_GAME_ID, AnalyticsConsts.P_UID, AnalyticsConsts.P_EVENT_TS,
            AnalyticsConsts.P_APP_VERSION, AnalyticsConsts.P_INSTALL_VERSION, AnalyticsConsts.P_GEO_COUNTRY,
            AnalyticsConsts.P_PLATFORM, AnalyticsConsts.P_DEVICE_MODEL, AnalyticsConsts.P_LEVEL_NUMBER,
            AnalyticsConsts.P_IS_TUTORIAL, AnalyticsConsts.P_HARD_TIER, AnalyticsConsts.P_ATTEMPT_NUMBER,
            AnalyticsConsts.P_IS_FIRST_PLAY, AnalyticsConsts.P_IS_REPLAY_AFTER_CLEAR, AnalyticsConsts.P_RESULT,
            AnalyticsConsts.P_END_REASON, AnalyticsConsts.P_MOVES_USED, AnalyticsConsts.P_MOVES_GIVEN,
            AnalyticsConsts.P_MOVES_REMAINING, AnalyticsConsts.P_UNDO_COUNT, AnalyticsConsts.P_DEADLOCK_COUNT,
            AnalyticsConsts.P_OBJECTIVE_TOTAL, AnalyticsConsts.P_OBJECTIVE_DONE, AnalyticsConsts.P_PEAK_RESOURCE,
            AnalyticsConsts.P_AVG_RESOURCE, AnalyticsConsts.P_FAIL_OUTERMOST_COLORS, AnalyticsConsts.P_FAIL_RAIL_COMPOSITION,
            AnalyticsConsts.P_PLAY_TIME_SEC, AnalyticsConsts.P_BACKGROUND_TIME_SEC, AnalyticsConsts.P_SCORE,
            AnalyticsConsts.P_STAR_COUNT, AnalyticsConsts.P_IN_PLAY_ITEM_IDS, AnalyticsConsts.P_IN_PLAY_ITEM_COUNT,
            AnalyticsConsts.P_CONTINUE_POPUP_COUNT, AnalyticsConsts.P_CONTINUE_COUNT, AnalyticsConsts.P_COIN_EARNED,
            AnalyticsConsts.P_COIN_SPENT, AnalyticsConsts.P_FINAL_COIN_BALANCE, AnalyticsConsts.P_SHUFFLE_COUNT,
            AnalyticsConsts.P_HINT_COUNT, AnalyticsConsts.P_LIVES_AFTER
        };

        private static readonly string[] BqItemUseColumns =
        {
            AnalyticsConsts.P_EVENT_ID, AnalyticsConsts.P_PLAY_ID, AnalyticsConsts.P_GAME_ID,
            AnalyticsConsts.P_UID, AnalyticsConsts.P_EVENT_TS, AnalyticsConsts.P_LEVEL_NUMBER,
            AnalyticsConsts.P_ITEM_ID, AnalyticsConsts.P_ITEM_CATEGORY, AnalyticsConsts.P_ACQUISITION_TYPE,
            AnalyticsConsts.P_COST_AMOUNT, AnalyticsConsts.P_COST_CURRENCY_ID, AnalyticsConsts.P_SESSION_ID,
            AnalyticsConsts.P_INSTALL_AT, AnalyticsConsts.P_MAX_REACHED_LEVEL, AnalyticsConsts.P_TOTAL_SPEND_USD,
            AnalyticsConsts.P_TOTAL_AD_REVENUE_USD
        };

        private static readonly string[] BqPurchaseColumns =
        {
            AnalyticsConsts.P_EVENT_ID, AnalyticsConsts.P_GAME_ID, AnalyticsConsts.P_UID,
            AnalyticsConsts.P_EVENT_TS, AnalyticsConsts.P_APP_VERSION, AnalyticsConsts.P_GEO_COUNTRY,
            AnalyticsConsts.P_PLATFORM, AnalyticsConsts.P_PRODUCT_ID, AnalyticsConsts.P_PRODUCT_NAME,
            AnalyticsConsts.P_PRODUCT_TYPE, AnalyticsConsts.P_PRICE_USD, AnalyticsConsts.P_PRICE_LOCAL,
            AnalyticsConsts.P_CURRENCY_CODE, AnalyticsConsts.P_IAP_PLACEMENT, AnalyticsConsts.P_LEVEL_NUMBER,
            AnalyticsConsts.P_COIN_GRANTED, AnalyticsConsts.P_ITEMS_GRANTED, AnalyticsConsts.P_LIVES_GRANTED,
            AnalyticsConsts.P_RECEIPT_ID, AnalyticsConsts.P_IS_VERIFIED, AnalyticsConsts.P_SESSION_ID,
            AnalyticsConsts.P_INSTALL_AT, AnalyticsConsts.P_MAX_REACHED_LEVEL, AnalyticsConsts.P_TOTAL_SPEND_USD,
            AnalyticsConsts.P_TOTAL_AD_REVENUE_USD
        };

        private static readonly string[] BqEconomyColumns =
        {
            AnalyticsConsts.P_EVENT_ID, AnalyticsConsts.P_GAME_ID, AnalyticsConsts.P_UID,
            AnalyticsConsts.P_EVENT_TS, AnalyticsConsts.P_CURRENCY_TYPE, AnalyticsConsts.P_CHANGE_AMOUNT,
            AnalyticsConsts.P_BALANCE_AFTER, AnalyticsConsts.P_SOURCE, AnalyticsConsts.P_REF_EVENT_ID,
            AnalyticsConsts.P_ECONOMY_PLACEMENT, AnalyticsConsts.P_LEVEL_NUMBER, AnalyticsConsts.P_SESSION_ID,
            AnalyticsConsts.P_INSTALL_AT, AnalyticsConsts.P_MAX_REACHED_LEVEL, AnalyticsConsts.P_TOTAL_SPEND_USD,
            AnalyticsConsts.P_TOTAL_AD_REVENUE_USD
        };

        private static readonly string[] BqSessionStartColumns =
        {
            AnalyticsConsts.P_EVENT_ID, AnalyticsConsts.P_SESSION_ID, AnalyticsConsts.P_GAME_ID,
            AnalyticsConsts.P_UID, AnalyticsConsts.P_EVENT_TS, AnalyticsConsts.P_VERSION,
            AnalyticsConsts.P_COUNTRY, AnalyticsConsts.P_PLATFORM, AnalyticsConsts.P_DEVICE_MODEL,
            AnalyticsConsts.P_INSTALL_AT, AnalyticsConsts.P_MAX_REACHED_LEVEL, AnalyticsConsts.P_TOTAL_SPEND_USD,
            AnalyticsConsts.P_TOTAL_AD_REVENUE_USD
        };

        private static readonly string[] BqSessionEndColumns =
        {
            AnalyticsConsts.P_EVENT_ID, AnalyticsConsts.P_SESSION_ID, AnalyticsConsts.P_GAME_ID,
            AnalyticsConsts.P_UID, AnalyticsConsts.P_EVENT_TS, AnalyticsConsts.P_END_REASON,
            AnalyticsConsts.P_DURATION_SEC
        };

        // ROLLBACK_USER_PROPERTY_PIPELINE_20260708: R_user_property v3.2 — 서버 MERGE 입력 컬럼.
        //   event_id/event_ts 는 배치·재시도 식별용(테이블 컬럼 아님 — 서버가 소비).
        private static readonly string[] BqUserPropertyColumns =
        {
            AnalyticsConsts.P_EVENT_ID, AnalyticsConsts.P_GAME_ID, AnalyticsConsts.P_UID,
            AnalyticsConsts.P_EVENT_TS,
            AnalyticsConsts.P_INSTALL_AT, AnalyticsConsts.P_INSTALL_VERSION, AnalyticsConsts.P_INSTALL_COUNTRY,
            AnalyticsConsts.P_INSTALL_PLATFORM, AnalyticsConsts.P_INSTALL_DEVICE,
            AnalyticsConsts.P_LAST_ACTIVE_AT, AnalyticsConsts.P_LAST_ACTIVE_VERSION, AnalyticsConsts.P_LAST_ACTIVE_COUNTRY,
            AnalyticsConsts.P_LAST_PLAYED_AT, AnalyticsConsts.P_MAX_REACHED_LEVEL,
            AnalyticsConsts.P_TOTAL_PLAY_COUNT, AnalyticsConsts.P_TOTAL_CLEAR_COUNT, AnalyticsConsts.P_TOTAL_COIN_BALANCE,
            AnalyticsConsts.P_TOTAL_SPEND_USD, AnalyticsConsts.P_TOTAL_AD_REVENUE_USD,
            AnalyticsConsts.P_INFINITE_LIVES_EXPIRY, AnalyticsConsts.P_IS_PAYER, AnalyticsConsts.P_LAST_UPDATED_AT,
            AnalyticsConsts.P_APPSFLYER_ID,
            // ROLLBACK_INSTALL_MEDIA_SOURCE_20260713: 서버 BQ user_property 테이블에 install_media_source 컬럼 +
            //   MERGE 반영 후 아래 주석 해제해 활성화. 그 전에 등록하면 미지원 컬럼으로 적재 리스크 → 게이팅 유지.
            //   (활성화 전에도 클라는 값을 캡처·영속 중이라, 켜는 순간 기존 설치분도 다음 UPSERT 부터 채워짐.)
            // AnalyticsConsts.P_INSTALL_MEDIA_SOURCE,
        };

        private static readonly string[] BqAdColumns =
        {
            AnalyticsConsts.P_EVENT_ID, AnalyticsConsts.P_GAME_ID, AnalyticsConsts.P_UID,
            AnalyticsConsts.P_SESSION_ID, AnalyticsConsts.P_EVENT_TS, AnalyticsConsts.P_AD_REQUEST_ID,
            AnalyticsConsts.P_AD_TYPE, AnalyticsConsts.P_AD_PLACEMENT, AnalyticsConsts.P_AD_NETWORK,
            AnalyticsConsts.P_AD_UNIT_ID, AnalyticsConsts.P_MEDIATION_POSITION, AnalyticsConsts.P_EVENT_PHASE,
            AnalyticsConsts.P_ERROR_CODE, AnalyticsConsts.P_ERROR_MESSAGE, AnalyticsConsts.P_LATENCY_MS,
            AnalyticsConsts.P_WATCH_DURATION_SEC, AnalyticsConsts.P_AD_DURATION_SEC, AnalyticsConsts.P_REVENUE_USD,
            AnalyticsConsts.P_REVENUE_PRECISION, AnalyticsConsts.P_REWARD_TYPE, AnalyticsConsts.P_REWARD_AMOUNT,
            AnalyticsConsts.P_REWARD_ITEM_ID, AnalyticsConsts.P_LEVEL_NUMBER, AnalyticsConsts.P_ATTEMPT_NUMBER,
            AnalyticsConsts.P_APP_VERSION, AnalyticsConsts.P_GEO_COUNTRY, AnalyticsConsts.P_PLATFORM,
            AnalyticsConsts.P_INSTALL_AT, AnalyticsConsts.P_MAX_REACHED_LEVEL, AnalyticsConsts.P_TOTAL_SPEND_USD,
            AnalyticsConsts.P_TOTAL_AD_REVENUE_USD
        };

        #endregion

        #region Facebook / AppsFlyer dispatchers (유지)

        private void LogToFacebook(string eventName, Dictionary<string, object> parameters)
        {
            if (!_facebookReady) return;

            if (parameters == null || parameters.Count == 0)
            {
                FB.LogAppEvent(eventName);
                return;
            }
            FB.LogAppEvent(eventName, parameters: parameters);
        }

        private void LogToAppsFlyer(string eventName, Dictionary<string, object> parameters)
        {
            if (!AttributionManager.HasInstance) return;

            Dictionary<string, string> stringParams = null;
            if (parameters != null && parameters.Count > 0)
            {
                stringParams = new Dictionary<string, string>(parameters.Count);
                foreach (var kv in parameters)
                    stringParams[kv.Key] = kv.Value?.ToString() ?? "";
            }
            AttributionManager.Instance.LogEvent(eventName, stringParams);
        }

        #endregion

        private struct BqEvent
        {
            public string name;
            public Dictionary<string, object> data;
        }
    }
}
