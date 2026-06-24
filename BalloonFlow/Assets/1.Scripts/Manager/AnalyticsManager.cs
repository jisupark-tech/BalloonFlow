using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Firebase.Auth;
using Facebook.Unity;

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

        private bool _facebookReady;
        public bool FacebookReady => _facebookReady;

        protected override void OnSingletonAwake()
        {
            InitFacebook();
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

        private void EnqueueBigQuery(string eventName, Dictionary<string, object> parameters)
        {
            // 호출자 dict 를 그대로 보관하면 이후 변형 위험 → 얕은 복사로 스냅샷.
            var data = parameters != null
                ? new Dictionary<string, object>(parameters)
                : new Dictionary<string, object>(1);

            if (_bqBatch.Count >= BQ_MAX_BUFFER)
            {
                _bqBatch.RemoveAt(0); // oldest drop (오프라인 장기화 메모리 상한)
                Debug.LogWarning($"{LOG_TAG} BQ buffer full ({BQ_MAX_BUFFER}). Dropping oldest event.");
            }
            _bqBatch.Add(new BqEvent { name = eventName, data = data });

            // [ANALYTICS_PLAYEND_FLUSH 2026-06-24] 레벨 종료(play_end)는 즉시 flush.
            // 패배→빠른 재시도(타이머 15s/카운트 20 임계 전) 시 씬 전환되면 적재가 다음 판까지 밀리던
            // '한 판 지연' 버그 방지. 같이 대기 중이던 play_start 도 이때 함께 전송됨.
            if (eventName == Analytics.AnalyticsConsts.EVT_LEVEL_PLAY || _bqBatch.Count >= BQ_BATCH_FLUSH_COUNT)
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
            if (paused) TryFlush(); // 백그라운드 전환 직전 best-effort (확실한 보장은 디스크 영속화 필요 — 1.1+)
        }

        private void OnApplicationQuit()
        {
            TryFlush();
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"{LOG_TAG} BQ ingest OK — {sendCount}건 적재 (resp={code}, 잔여 {_bqBatch.Count}) {req.downloadHandler.text}");
#endif
                }
                else if (!networkErr && code >= 400 && code < 500)
                {
                    // 클라 오류(스키마/인증 등) — 재시도해도 동일. 해당 배치 폐기(무한 재시도 방지).
                    Debug.LogWarning($"{LOG_TAG} BQ ingest 4xx({code}) — {sendCount}건 폐기. resp={req.downloadHandler.text}");
                    _bqBatch.RemoveRange(0, sendCount);
                }
                else
                {
                    // 네트워크/5xx — 버퍼 유지, 다음 주기 재시도.
                    Debug.LogWarning($"{LOG_TAG} BQ ingest 실패(code={code}, net={networkErr}) — {sendCount}건 재시도 대기.");
                }
            }

            _flushing = false;
        }

        private static string BuildBatchJson(List<BqEvent> sending)
        {
            var events = new List<object>(sending.Count);
            for (int i = 0; i < sending.Count; i++)
                events.Add(new { name = sending[i].name, data = sending[i].data });
            return JsonConvert.SerializeObject(new { events });
        }

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
