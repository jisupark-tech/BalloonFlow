using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow.Analytics
{
    /// <summary>
    /// 세션 라이프사이클 추적 → session_start_event / session_end_event 발사.
    ///
    /// 정책 (v3.2 P2-b 확정):
    ///   - 백그라운드 30분 초과 후 복귀 → 이전 세션 quit_by_user 종료 + 새 session_start
    ///   - 30분 이내 복귀 → 기존 세션 유지
    ///   - 명시 quit (앱 종료) → quit_by_user
    ///   - 크래시/bg_kill → 다음 실행 시 클라가 quit_by_system 소급 발행
    ///     (미종료 세션 스냅샷을 PlayerPrefs 에 유지 — ROLLBACK_ANALYTICS_DISK_PERSIST_20260707)
    ///
    /// session_id: Guid (서버 fallback 없음, 클라 단독)
    /// </summary>
    public class AnalyticsSessionTracker : Singleton<AnalyticsSessionTracker>
    {
        private string _sessionId;
        private DateTime _sessionStartedUtc;
        private DateTime? _backgroundEnteredUtc;

        // ROLLBACK_SESSION_HEARTBEAT_20260713: 포그라운드 하트비트 — 열린 세션의 last_seen 을 주기적 갱신.
        //   기존엔 last_seen 이 pause 시점에만 갱신돼, 포그라운드 크래시(퍼즈 콜백 없이 사망) 시 소급
        //   orphan session_end 의 duration 이 세션 시작 시각으로 과소 추정됐다. N초마다 갱신해 복원 정확도 확보.
        //   ※ '백그라운드 진입 시 즉시 session_end' 방식은 채택하지 않음 — 본 게임은 전면/보상 광고가
        //     OnApplicationPause(true) 를 유발하므로 매 광고마다 세션이 쪼개져(과다 카운트) 회귀가 된다.
        //     never-return 유저의 session_end 는 서버측 timeout_inferred(END_TIMEOUT_INFERRED) 잡으로 마감 권장.
        //   롤백: 이 필드 + Update() + HEARTBEAT_INTERVAL_SEC 제거.
        private float _heartbeatTimer;
        private const float HEARTBEAT_INTERVAL_SEC = 30f;

        // ROLLBACK_ANALYTICS_DISK_PERSIST_20260707: 미종료 세션 스냅샷 키.
        // 프로세스 킬(스와이프킬/OS킬)은 session_end 가 생성조차 안 되던 문제 — start/end 카운트 이격의
        // 최대 원인. 세션 시작 시 스냅샷 기록, pause 마다 last_seen 갱신, 정상 종료 시 삭제.
        // 다음 부트에 스냅샷이 남아 있으면 quit_by_system 으로 소급 발행.
        private const string PREFS_OPEN_SESSION_ID       = "BF_AN_OpenSessionId";
        private const string PREFS_OPEN_SESSION_START    = "BF_AN_OpenSessionStart";
        private const string PREFS_OPEN_SESSION_LASTSEEN = "BF_AN_OpenSessionLastSeen";

        public string CurrentSessionId => _sessionId ?? "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            DiagLog("SessionTracker.AutoCreate fired");
            if (HasInstance) { DiagLog("SessionTracker already exists — skip"); return; }
            var go = new GameObject("AnalyticsSessionTracker");
            go.AddComponent<AnalyticsSessionTracker>();
        }

        protected override void OnSingletonAwake()
        {
            DiagLog("SessionTracker.OnSingletonAwake — start WaitReady coroutine");
            // Firebase Analytics ready 대기 후 세션 시작
            StartCoroutine(WaitReadyThenStartSession());
        }

        private IEnumerator WaitReadyThenStartSession()
        {
            // Analytics + UserData 둘 다 ready 대기 (uid 필요)
            float waited = 0f;
            const float MAX_WAIT = 15f;
            int logEveryNTicks = 5; // 1초마다 진행 로그
            int tickIdx = 0;
            while (waited < MAX_WAIT)
            {
                bool analyticsHas = AnalyticsManager.HasInstance;
                // [BQ_DIRECT 2026-06-16] 이벤트는 AnalyticsManager 가 버퍼링 후 BigQuery 로 직접 전송하므로
                //   Firebase Analytics ready 대기 불필요 — 인스턴스 존재만으로 세션 시작 게이트 통과(uid 만 별도 대기).
                bool analyticsReady = analyticsHas;
                bool uidHas = UserDataService.HasInstance;
                bool uidReady = uidHas && !string.IsNullOrEmpty(UserDataService.Instance.Uid);

                if (tickIdx % logEveryNTicks == 0)
                    DiagLog($"SessionTracker.WaitReady t={waited:F1}s — analyticsHas={analyticsHas} analyticsReady={analyticsReady} uidHas={uidHas} uidReady={uidReady}");

                // ROLLBACK_SESSION_START_UID_GATE_20260714: uid(익명인증=네트워크)를 세션시작 게이트에서 제외.
                //   [배경] 첫 실행 시 uid 대기(≤15s)가 session_start 발화를 늦춰 event_ts 가 play_start 보다 역전되거나
                //     "설치 몇시간 뒤 첫 세션 등장"으로 과소계측됐다(#5). AnalyticsManager(로컬 싱글턴)만 준비되면 즉시
                //     세션 시작 → event_ts≈앱오픈. 빈 uid 는 flush 시점(인증 보장됨) backfill 로 채운다
                //     (ROLLBACK_SESSION_START_UID_LATEBIND_20260714). 재실행 유저는 uid 가 이미 캐시돼 emit 시 바로 실림.
                if (analyticsReady)
                {
                    DiagLog($"SessionTracker.WaitReady DONE at t={waited:F1}s (uidReady={uidReady}) — proceeding to StartSession");
                    break;
                }
                yield return new WaitForSeconds(0.2f);
                waited += 0.2f;
                tickIdx++;
            }
            if (waited >= MAX_WAIT)
                DiagLog("SessionTracker.WaitReady TIMEOUT — proceeding anyway, events may drop");

            EmitOrphanSessionEnd();
            StartSession();
        }

        private void StartSession()
        {
            _sessionId = Guid.NewGuid().ToString("N");
            _sessionStartedUtc = DateTime.UtcNow;
            _backgroundEnteredUtc = null;
            DiagLog($"SessionTracker.StartSession — sessionId={_sessionId.Substring(0, 8)}...");

            SaveOpenSessionSnapshot();
            FireSessionStartEvent();
            // ROLLBACK_USER_PROPERTY_PIPELINE_20260708: 세션 시작 시에도 UPSERT — 스펙은 '세션 종료 시'지만
            //   모바일에서 종료 이벤트는 유실 가능(프로세스 킬)하므로 시작 시 1회를 보험으로 함께 발사.
            //   서버 MERGE 가 last_updated_at 게이트로 멱등 처리하므로 중복 무해.
            FireUserPropertyEvent();
        }

        private void EndSession(string endReason)
        {
            if (string.IsNullOrEmpty(_sessionId)) return;

            int durationSec = (int)Math.Max(0, (DateTime.UtcNow - _sessionStartedUtc).TotalSeconds);
            FireSessionEndEvent(endReason, durationSec);
            // ROLLBACK_USER_PROPERTY_PIPELINE_20260708: 스키마 갱신주기 정의 지점 — 세션 종료 시 UPSERT.
            FireUserPropertyEvent();

            _sessionId = null;
            ClearOpenSessionSnapshot();
        }

        // ─── 미종료 세션 소급 처리 (ROLLBACK_ANALYTICS_DISK_PERSIST_20260707) ───

        /// <summary>
        /// 이전 실행이 세션을 정상 종료 못 하고 죽었으면(스냅샷 잔존) session_end 를 quit_by_system 으로
        /// 소급 발행. event_ts/duration 은 마지막 pause 시각 기준(포그라운드 크래시는 과소 추정 허용).
        /// </summary>
        private void EmitOrphanSessionEnd()
        {
            string prevId = PlayerPrefs.GetString(PREFS_OPEN_SESSION_ID, "");
            if (string.IsNullOrEmpty(prevId)) return;

            string startStr = PlayerPrefs.GetString(PREFS_OPEN_SESSION_START, "");
            string seenStr  = PlayerPrefs.GetString(PREFS_OPEN_SESSION_LASTSEEN, "");
            ClearOpenSessionSnapshot();

            if (!DateTime.TryParse(startStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime start))
                return;
            if (!DateTime.TryParse(seenStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime lastSeen)
                || lastSeen < start)
                lastSeen = start;

            int durationSec = (int)Math.Max(0, (lastSeen - start).TotalSeconds);
            FireSessionEndEventFor(prevId, lastSeen, AnalyticsConsts.END_QUIT_BY_SYSTEM, durationSec);
            DiagLog($"Orphan session_end 소급 발행 — sessionId={prevId.Substring(0, Math.Min(8, prevId.Length))}... dur={durationSec}s");
        }

        private void SaveOpenSessionSnapshot()
        {
            PlayerPrefs.SetString(PREFS_OPEN_SESSION_ID, _sessionId);
            PlayerPrefs.SetString(PREFS_OPEN_SESSION_START, _sessionStartedUtc.ToString("o"));
            PlayerPrefs.SetString(PREFS_OPEN_SESSION_LASTSEEN, DateTime.UtcNow.ToString("o"));
            PlayerPrefs.Save();
        }

        // ROLLBACK_ANR_PAUSE_IO_DIET_20260713: last_seen 갱신. persistToDisk=true 일 때만 디스크 커밋.
        //   [배경] GameActivity onPause 핸드셰이크 ANR — 백그라운드 전환 시 GameActivity(네이티브 app-glue)가
        //   Unity 게임 스레드의 pause ack 를 pthread_cond_wait 로 기다린다. 저사양기에서 그 순간 서드파티
        //   SDK(FB/AppsFlyer/MAX)가 네트워크로 폭주하며 CPU/스케줄러를 포화시키면 Unity 스레드가 5s 안에
        //   ack 를 못 내 ANR. 우리 메인스레드 동기 디스크 IO 는 그 여유를 갉아먹는 가중 요인이다.
        //   PlayerPrefs.Save() 는 전체 prefs 를 디스크에 '동기' flush → 30초 포그라운드 하트비트마다 호출하면
        //   상시 메인스레드 IO/잰크가 된다. 그래서 하트비트는 in-memory SetString 만(persistToDisk=false),
        //   실제 Save 는 pause/quit 에서만 수행한다. orphan session_end 복구는 '백그라운드 킬 직전'의 디스크
        //   값만 필요한데, OnApplicationPause(true) 에서 반드시 Save 하므로 그 보장은 유지된다.
        private void TouchOpenSessionLastSeen(bool persistToDisk = true)
        {
            if (string.IsNullOrEmpty(_sessionId)) return;
            PlayerPrefs.SetString(PREFS_OPEN_SESSION_LASTSEEN, DateTime.UtcNow.ToString("o"));
            if (persistToDisk) PlayerPrefs.Save();
        }

        private static void ClearOpenSessionSnapshot()
        {
            PlayerPrefs.DeleteKey(PREFS_OPEN_SESSION_ID);
            PlayerPrefs.DeleteKey(PREFS_OPEN_SESSION_START);
            PlayerPrefs.DeleteKey(PREFS_OPEN_SESSION_LASTSEEN);
            PlayerPrefs.Save();
        }

        // ─── App lifecycle ───

        // ROLLBACK_SESSION_HEARTBEAT_20260713: 포그라운드 하트비트 — HEARTBEAT_INTERVAL_SEC 마다 last_seen 갱신.
        // ROLLBACK_ANR_PAUSE_IO_DIET_20260713: 하트비트는 in-memory SetString 만(persistToDisk:false) — 매 틱
        //   PlayerPrefs.Save()(전체 prefs 동기 디스크 flush)를 제거해 상시 메인스레드 IO/잰크 + ANR 가중 제거.
        //   디스크 커밋은 OnApplicationPause(true)/OnApplicationQuit 에서만 (백그라운드 킬 직전 값이면 충분).
        private void Update()
        {
            if (string.IsNullOrEmpty(_sessionId)) return;
            _heartbeatTimer += Time.unscaledDeltaTime;
            if (_heartbeatTimer >= HEARTBEAT_INTERVAL_SEC)
            {
                _heartbeatTimer = 0f;
                TouchOpenSessionLastSeen(persistToDisk: false);
            }
        }

        private void OnApplicationPause(bool pause)
        {
            if (pause)
            {
                _backgroundEnteredUtc = DateTime.UtcNow;
                // 백그라운드에서 킬당하면 이 시각이 소급 session_end 의 event_ts/duration 기준이 됨.
                // ROLLBACK_ANR_PAUSE_IO_DIET_20260713: 하트비트가 in-memory 만 하므로 last_seen 의 '유일한'
                //   디스크 커밋 지점 = 여기(persistToDisk 기본 true). 백그라운드 킬 직전이라 orphan 복구 보장됨.
                TouchOpenSessionLastSeen();
            }
            else if (_backgroundEnteredUtc.HasValue)
            {
                double bgMin = (DateTime.UtcNow - _backgroundEnteredUtc.Value).TotalMinutes;
                _backgroundEnteredUtc = null;

                if (bgMin >= AnalyticsConsts.SESSION_BG_TIMEOUT_MIN)
                {
                    // 30분 초과 → 이전 세션 종료 + 새 세션 시작
                    EndSession(AnalyticsConsts.END_QUIT_BY_USER);
                    StartSession();
                }
                // else: 30분 이내 → 기존 세션 유지
            }
        }

        private void OnApplicationQuit()
        {
            EndSession(AnalyticsConsts.END_QUIT_BY_USER);
        }

        // ─── Event emission ───

        private void FireSessionStartEvent()
        {
            var p = new Dictionary<string, object>(20);
            p[AnalyticsConsts.P_EVENT_ID]    = Guid.NewGuid().ToString("N");
            p[AnalyticsConsts.P_SESSION_ID]  = _sessionId;
            p[AnalyticsConsts.P_GAME_ID]     = AnalyticsConsts.GAME_ID;
            p[AnalyticsConsts.P_UID]         = ResolveUid();
            p[AnalyticsConsts.P_EVENT_TS]    = _sessionStartedUtc.ToString("o");
            p[AnalyticsConsts.P_APP_VERSION] = Application.version;
            p[AnalyticsConsts.P_GEO_COUNTRY] = ResolveGeoCountry();
            p[AnalyticsConsts.P_PLATFORM]    = ResolvePlatform();
            p[AnalyticsConsts.P_DEVICE_MODEL]= SystemInfo.deviceModel;

            if (UserSnapshotCache.HasInstance)
                UserSnapshotCache.Instance.Stamp(p);

            EmitEvent(AnalyticsConsts.EVT_SESSION_START, p);
        }

        private void FireSessionEndEvent(string endReason, int durationSec)
            => FireSessionEndEventFor(_sessionId, DateTime.UtcNow, endReason, durationSec);

        /// <summary>세션 id/시각 명시 버전 — 이전 실행의 미종료 세션 소급 발행에도 사용.</summary>
        private void FireSessionEndEventFor(string sessionId, DateTime eventTsUtc, string endReason, int durationSec)
        {
            var p = new Dictionary<string, object>(8);
            p[AnalyticsConsts.P_EVENT_ID]     = Guid.NewGuid().ToString("N");
            p[AnalyticsConsts.P_SESSION_ID]   = sessionId;
            p[AnalyticsConsts.P_GAME_ID]      = AnalyticsConsts.GAME_ID;
            p[AnalyticsConsts.P_UID]          = ResolveUid();
            p[AnalyticsConsts.P_EVENT_TS]     = eventTsUtc.ToString("o");
            p[AnalyticsConsts.P_END_REASON]   = endReason;
            p[AnalyticsConsts.P_DURATION_SEC] = durationSec;

            EmitEvent(AnalyticsConsts.EVT_SESSION_END, p);
        }

        // ROLLBACK_USER_PROPERTY_PIPELINE_20260708: R_user_property UPSERT 페이로드.
        //   서버(ingestAnalyticsEvents)가 이 이벤트만 스트리밍 insert 대신 BQ MERGE 로 처리한다.
        //   uid 는 서버가 토큰 검증값으로 대체. 채우는 항목 추가(2026-07-13): install_media_source(conversion),
        //   aid(GAID 네이티브). 미포함(NULL 유지): campaign/adgroup/creative(MMP 자동), idfa(iOS ATT — 후속).
        private void FireUserPropertyEvent()
        {
            var p = new Dictionary<string, object>(24);
            DateTime now = DateTime.UtcNow;
            p[AnalyticsConsts.P_EVENT_ID]        = Guid.NewGuid().ToString("N");
            p[AnalyticsConsts.P_GAME_ID]         = AnalyticsConsts.GAME_ID;
            p[AnalyticsConsts.P_UID]             = ResolveUid();
            p[AnalyticsConsts.P_EVENT_TS]        = now.ToString("o");
            p[AnalyticsConsts.P_LAST_UPDATED_AT] = now.ToString("o");

            p[AnalyticsConsts.P_LAST_ACTIVE_AT]      = _sessionStartedUtc.ToString("o");
            p[AnalyticsConsts.P_LAST_ACTIVE_VERSION] = Application.version;
            p[AnalyticsConsts.P_LAST_ACTIVE_COUNTRY] = ResolveGeoCountry();

            if (UserSnapshotCache.HasInstance)
            {
                var c = UserSnapshotCache.Instance;
                p[AnalyticsConsts.P_INSTALL_AT]           = c.InstallAt;
                p[AnalyticsConsts.P_INSTALL_VERSION]      = c.InstallVersion;
                p[AnalyticsConsts.P_INSTALL_COUNTRY]      = c.InstallCountry;
                p[AnalyticsConsts.P_INSTALL_PLATFORM]     = c.InstallPlatform;
                p[AnalyticsConsts.P_INSTALL_DEVICE]       = c.InstallDevice;
                p[AnalyticsConsts.P_MAX_REACHED_LEVEL]    = c.MaxReachedLevel;
                p[AnalyticsConsts.P_TOTAL_PLAY_COUNT]     = c.TotalPlayCount;
                p[AnalyticsConsts.P_TOTAL_CLEAR_COUNT]    = c.TotalClearCount;
                p[AnalyticsConsts.P_TOTAL_SPEND_USD]      = Math.Round(c.TotalSpendUsd, 6);
                p[AnalyticsConsts.P_TOTAL_AD_REVENUE_USD] = Math.Round(c.TotalAdRevenueUsd, 6);
                p[AnalyticsConsts.P_IS_PAYER]             = c.IsPayer;
                if (!string.IsNullOrEmpty(c.LastPlayedAt))
                    p[AnalyticsConsts.P_LAST_PLAYED_AT] = c.LastPlayedAt;
                // ROLLBACK_INSTALL_MEDIA_SOURCE_20260713: 유입 미디어소스 stamp(값 있을 때만).
                //   ※ BqUserPropertyColumns 미등록 상태면 서버 전송 직전 normalize 에서 스트립됨(무해).
                //     서버 스키마 반영 후 화이트리스트 등록 시 실제 적재 시작.
                if (!string.IsNullOrEmpty(c.InstallMediaSource))
                    p[AnalyticsConsts.P_INSTALL_MEDIA_SOURCE] = c.InstallMediaSource;
                // ROLLBACK_GAID_AID_20260713: Android GAID stamp(비동기 수집 완료분만).
                if (!string.IsNullOrEmpty(c.Aid))
                    p[AnalyticsConsts.P_AID] = c.Aid;
            }

            // ROLLBACK_AB_EP1_20260713: A/B 활성 시에만 variant 기록(첫 읽기 시 lazy 배정+영속). BQ A/B 분리 분석용.
            //   비활성(기본)이면 미기록 → 비테스트 유저 ab_ep1_variant=NULL(전원 A 를 'A'로 오염 안 시킴).
            if (AbTestService.IsEnabled)
                p[AnalyticsConsts.P_AB_EP1_VARIANT] = AbTestService.Episode1Variant;

            if (CurrencyManager.HasInstance)
                p[AnalyticsConsts.P_TOTAL_COIN_BALANCE] = CurrencyManager.Instance.Coins;

            if (LifeManager.HasInstance && LifeManager.Instance.IsInfiniteHeartsActive)
                p[AnalyticsConsts.P_INFINITE_LIVES_EXPIRY] =
                    now.AddSeconds(LifeManager.Instance.GetRemainingInfiniteSeconds()).ToString("o");

            string afId = ResolveAppsFlyerId();
            if (!string.IsNullOrEmpty(afId))
                p[AnalyticsConsts.P_APPSFLYER_ID] = afId;

            EmitEvent(AnalyticsConsts.EVT_USER_PROPERTY, p);
        }

        /// <summary>ROLLBACK_LAST_PLAYED_AT_EMIT_20260713: 외부(레벨 플레이 시작)에서 user_property UPSERT 강제.
        //   last_played_at 등 플레이 중 갱신값이 세션종료(모바일 유실多)를 기다리지 않고 활성 세션 중 적재되게 함.
        //   서버 MERGE 라 멱등 — 중복/빈번 호출 무해. 롤백: 이 메서드 + AnalyticsLevelTracker 호출부 제거.</summary>
        public void EmitUserPropertySnapshot()
        {
            if (string.IsNullOrEmpty(_sessionId)) return; // 세션 미시작이면 skip(빈 uid 오염 방지)
            FireUserPropertyEvent();
        }

        /// <summary>AppsFlyer 유저 키 — SDK 미초기화/에디터/예외 시 빈 문자열(NULL 유지).</summary>
        private static string ResolveAppsFlyerId()
        {
#if UNITY_ANDROID || UNITY_IOS
            try { return AppsFlyerSDK.AppsFlyer.getAppsFlyerId() ?? ""; }
            catch { return ""; }
#else
            return "";
#endif
        }

        // ─── Helpers ───

        internal static string ResolveUid()
        {
            return UserDataService.HasInstance ? UserDataService.Instance.Uid : "";
        }

        internal static string ResolveGeoCountry()
        {
            // Firebase Analytics 가 BigQuery export 시 geo.country 자동 채움.
            // 이벤트 param 으로도 보내고 싶다면 SystemInfo / RegionInfo 활용 (Application.systemLanguage 는 언어).
            try
            {
                return System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName ?? "";
            }
            catch { return ""; }
        }

        internal static string ResolvePlatform()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.Android:        return "android";
                case RuntimePlatform.IPhonePlayer:   return "ios";
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.LinuxEditor:    return "editor";
                default:                             return Application.platform.ToString().ToLowerInvariant();
            }
        }

        internal static void EmitEvent(string evtName, Dictionary<string, object> p)
        {
            bool hasInstance = AnalyticsManager.HasInstance;
            if (hasInstance)
            {
                // [BQ_DIRECT] 준비 전이어도 AnalyticsManager 가 버퍼링 → drop 없음.
                AnalyticsManager.Instance.LogEvent(evtName, p);
                LogEventToConsole(evtName, p);
            }
            else
            {
                Debug.LogWarning($"[Analytics] {evtName} DROP — AnalyticsManager.HasInstance=false");
            }
        }

        // ROLLBACK_BOOT_CHECKPOINTS_20260713: 부팅 퍼널 체크포인트 발화(공통 필드 스탬프 + boot 파라미터).
        //   TitleController 등 비-트래커 컨텍스트에서 호출. 서버 라우팅 활성화 전엔 unknown 으로 안전 스킵.
        internal static void EmitBootCheckpoint(string stage, int stageIndex, int elapsedMs, bool netReachable)
        {
            var p = new Dictionary<string, object>(13);
            p[AnalyticsConsts.P_EVENT_ID]     = Guid.NewGuid().ToString("N");
            p[AnalyticsConsts.P_SESSION_ID]   = HasInstance ? Instance.CurrentSessionId : "";
            p[AnalyticsConsts.P_GAME_ID]      = AnalyticsConsts.GAME_ID;
            p[AnalyticsConsts.P_UID]          = ResolveUid();
            p[AnalyticsConsts.P_EVENT_TS]     = DateTime.UtcNow.ToString("o");
            p[AnalyticsConsts.P_APP_VERSION]  = Application.version;
            p[AnalyticsConsts.P_GEO_COUNTRY]  = ResolveGeoCountry();
            p[AnalyticsConsts.P_PLATFORM]     = ResolvePlatform();
            p[AnalyticsConsts.P_DEVICE_MODEL] = SystemInfo.deviceModel;
            p[AnalyticsConsts.P_STAGE]        = stage;
            p[AnalyticsConsts.P_STAGE_INDEX]  = stageIndex;
            p[AnalyticsConsts.P_ELAPSED_MS]   = elapsedMs;
            p[AnalyticsConsts.P_NET_REACHABLE] = netReachable;
            EmitEvent(AnalyticsConsts.EVT_BOOT_CHECKPOINT, p);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void DiagLog(string msg) => Debug.Log("[Analytics] " + msg);

        /// <summary>Editor/Development 빌드에서만 이벤트 발사 콘솔 출력. Production 자동 stripped.</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void LogEventToConsole(string evtName, Dictionary<string, object> p)
        {
            var sb = new System.Text.StringBuilder(256);
            sb.Append("[Analytics] FIRE → ").Append(evtName).Append(" { ");
            int i = 0;
            foreach (var kv in p)
            {
                if (i++ > 0) sb.Append(", ");
                sb.Append(kv.Key).Append("=").Append(kv.Value?.ToString() ?? "null");
            }
            sb.Append(" }");
            Debug.Log(sb.ToString());
        }
    }

    /// <summary>
    /// Emits item_use_event and economy_event for BigQuery export via Firebase Analytics.
    /// Kept in this compiled file so editor project regeneration is not required for CI-style builds.
    /// </summary>
    public class AnalyticsItemEconomyTracker : Singleton<AnalyticsItemEconomyTracker>
    {
        private const string ITEM_TYPE_BOOSTER = "booster";
        private const string ITEM_CONTEXT_IN_LEVEL = "in_level";
        private const string CURRENCY_COIN = "coin";
        private const string FLOW_EARN = "earn";
        private const string FLOW_SPEND = "spend";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (HasInstance) return;
            var go = new GameObject("AnalyticsItemEconomyTracker");
            go.AddComponent<AnalyticsItemEconomyTracker>();
        }

        protected override void OnSingletonAwake()
        {
            // ROLLBACK_ANALYTICS_ITEMUSE_ON_EFFECT_APPLIED_20260617: START
            //   기존엔 OnBoosterUsed(=UseBooster 선차감/arming 시점) 에 item_use 를 쐈다. 그런데 Hand/Zap 같은
            //   타겟 지정형은 arming 후 타겟을 안 고르고 취소하면 환불되는데도 item_use 가 이미 나가, '클릭만 해도
            //   프로토콜 발사' 가 됐다. → 실제 효과가 게임 상태에 적용된 OnBoosterEffectApplied(Shuffle/ColorRemove/
            //   SelectTool 성공) 로 전환해 '진짜 사용' 에만 1회 emit. 취소/실패/중복거부는 미발행.
            //   롤백: 아래 2줄 + HandleBoosterEffectApplied 시그니처를 OnBoosterUsed/HandleBoosterUsed 로 환원하고
            //         BoosterExecutor 의 ROLLBACK_BOOSTER_SELECTTOOL_EFFECT_APPLIED_20260617 도 함께 환원.
            EventBus.Subscribe<OnBoosterEffectApplied>(HandleBoosterEffectApplied);
            // ROLLBACK_ANALYTICS_ITEMUSE_ON_EFFECT_APPLIED_20260617: END
        }

        protected override void OnDestroy()
        {
            EventBus.Unsubscribe<OnBoosterEffectApplied>(HandleBoosterEffectApplied);
            base.OnDestroy();
        }

        public static void EmitCoinEarn(string source, int amount, int balanceAfter)
        {
            EmitCoinEconomy(FLOW_EARN, source, "", amount, balanceAfter);
        }

        public static void EmitCoinSpend(string sink, int amount, int balanceAfter)
        {
            EmitCoinEconomy(FLOW_SPEND, "", sink, amount, balanceAfter);
        }

        private static void EmitCoinEconomy(string flowType, string source, string sink, int amount, int balanceAfter)
        {
            if (amount <= 0) return;

            var p = BuildCommonParams(20);
            p[AnalyticsConsts.P_CURRENCY_TYPE] = CURRENCY_COIN;
            // [BQ_DIRECT 2026-06-16] economy 테이블은 부호 단일 change_amount 컬럼 — earn=+, spend=-.
            //   (flow_type/amount/sink 컬럼 없음 → 미emit. source 컬럼 하나로 earn 출처/spend 대상 통합.)
            p[AnalyticsConsts.P_CHANGE_AMOUNT] = flowType == FLOW_SPEND ? -amount : amount;
            p[AnalyticsConsts.P_BALANCE_AFTER] = balanceAfter;

            string src = !string.IsNullOrEmpty(source) ? source : sink;
            if (!string.IsNullOrEmpty(src))
                p[AnalyticsConsts.P_SOURCE] = src;

            AnalyticsSessionTracker.EmitEvent(AnalyticsConsts.EVT_ECONOMY, p);
        }

        // ROLLBACK_ANALYTICS_ITEMUSE_ON_EFFECT_APPLIED_20260617:
        //   OnBoosterUsed(arming) → OnBoosterEffectApplied(실제 사용 확정) 구독으로 변경. boosterType 필드 동일.
        private static void HandleBoosterEffectApplied(OnBoosterEffectApplied evt)
        {
            if (string.IsNullOrEmpty(evt.boosterType)) return;

            var p = BuildCommonParams(20);
            p[AnalyticsConsts.P_ITEM_ID] = evt.boosterType;
            // [BQ_DIRECT 2026-06-16] item_use 테이블 컬럼에 정렬: item_category(=booster).
            //   item_type/item_context/quantity/balance_after 컬럼 없음 → 미emit.
            //   acquisition_type/cost_amount/cost_currency_id 는 부스터 인벤토리 사용이라 사용시점 직접 비용 없음
            //   (획득 시 차감은 economy_event 추적) → 미설정(NULL). 제품 정의 시 보강.
            p[AnalyticsConsts.P_ITEM_CATEGORY] = ITEM_TYPE_BOOSTER;

            // ROLLBACK_ANALYTICS_NULLFILL_20260625: item_use NULL 채우기 — 그 부스터의 마지막 획득정보(경량 추적).
            //   BoosterManager 가 AddBooster 시 타입별로 acquisition_type/cost/currency 기록 → 사용 시점에 읽어 emit.
            if (BoosterManager.HasInstance)
            {
                var acq = BoosterManager.Instance.ConsumeLastAcquisition(evt.boosterType);
                p[AnalyticsConsts.P_ACQUISITION_TYPE] = acq.type;
                p[AnalyticsConsts.P_COST_AMOUNT]      = acq.cost;
                p[AnalyticsConsts.P_COST_CURRENCY_ID] = acq.currency;
            }

            AnalyticsSessionTracker.EmitEvent(AnalyticsConsts.EVT_ITEM_USE, p);
        }

        private static Dictionary<string, object> BuildCommonParams(int capacity)
        {
            var p = new Dictionary<string, object>(capacity);
            p[AnalyticsConsts.P_EVENT_ID] = Guid.NewGuid().ToString("N");
            p[AnalyticsConsts.P_SESSION_ID] = AnalyticsSessionTracker.HasInstance
                ? AnalyticsSessionTracker.Instance.CurrentSessionId : "";
            p[AnalyticsConsts.P_GAME_ID] = AnalyticsConsts.GAME_ID;
            p[AnalyticsConsts.P_UID] = AnalyticsSessionTracker.ResolveUid();
            p[AnalyticsConsts.P_EVENT_TS] = DateTime.UtcNow.ToString("o");
            p[AnalyticsConsts.P_APP_VERSION] = Application.version;
            p[AnalyticsConsts.P_GEO_COUNTRY] = AnalyticsSessionTracker.ResolveGeoCountry();
            p[AnalyticsConsts.P_PLATFORM] = AnalyticsSessionTracker.ResolvePlatform();
            p[AnalyticsConsts.P_DEVICE_MODEL] = SystemInfo.deviceModel;

            if (AnalyticsLevelTracker.HasInstance)
            {
                string playId = AnalyticsLevelTracker.Instance.CurrentPlayId;
                if (!string.IsNullOrEmpty(playId))
                    p[AnalyticsConsts.P_PLAY_ID] = playId;
            }

            int levelNumber = LevelManager.HasInstance ? LevelManager.Instance.GetCurrentLevelId() : 0;
            if (levelNumber > 0)
                p[AnalyticsConsts.P_LEVEL_NUMBER] = levelNumber;

            if (UserSnapshotCache.HasInstance)
                UserSnapshotCache.Instance.Stamp(p);

            return p;
        }
    }
}
