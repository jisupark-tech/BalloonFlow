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
    ///   - 크래시/bg_kill → 서버 측 quit_by_system 소급 (클라 무관)
    ///
    /// session_id: Guid (서버 fallback 없음, 클라 단독)
    /// </summary>
    public class AnalyticsSessionTracker : Singleton<AnalyticsSessionTracker>
    {
        private string _sessionId;
        private DateTime _sessionStartedUtc;
        private DateTime? _backgroundEnteredUtc;

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
                bool analyticsReady = analyticsHas && AnalyticsManager.Instance.FirebaseReady;
                bool uidHas = UserDataService.HasInstance;
                bool uidReady = uidHas && !string.IsNullOrEmpty(UserDataService.Instance.Uid);

                if (tickIdx % logEveryNTicks == 0)
                    DiagLog($"SessionTracker.WaitReady t={waited:F1}s — analyticsHas={analyticsHas} analyticsReady={analyticsReady} uidHas={uidHas} uidReady={uidReady}");

                if (analyticsReady && uidReady)
                {
                    DiagLog($"SessionTracker.WaitReady DONE at t={waited:F1}s — proceeding to StartSession");
                    break;
                }
                yield return new WaitForSeconds(0.2f);
                waited += 0.2f;
                tickIdx++;
            }
            if (waited >= MAX_WAIT)
                DiagLog("SessionTracker.WaitReady TIMEOUT — proceeding anyway, events may drop");

            StartSession();
        }

        private void StartSession()
        {
            _sessionId = Guid.NewGuid().ToString("N");
            _sessionStartedUtc = DateTime.UtcNow;
            _backgroundEnteredUtc = null;
            DiagLog($"SessionTracker.StartSession — sessionId={_sessionId.Substring(0, 8)}...");

            FireSessionStartEvent();
        }

        private void EndSession(string endReason)
        {
            if (string.IsNullOrEmpty(_sessionId)) return;

            int durationSec = (int)Math.Max(0, (DateTime.UtcNow - _sessionStartedUtc).TotalSeconds);
            FireSessionEndEvent(endReason, durationSec);

            _sessionId = null;
        }

        // ─── App lifecycle ───

        private void OnApplicationPause(bool pause)
        {
            if (pause)
            {
                _backgroundEnteredUtc = DateTime.UtcNow;
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
        {
            var p = new Dictionary<string, object>(8);
            p[AnalyticsConsts.P_EVENT_ID]     = Guid.NewGuid().ToString("N");
            p[AnalyticsConsts.P_SESSION_ID]   = _sessionId;
            p[AnalyticsConsts.P_GAME_ID]      = AnalyticsConsts.GAME_ID;
            p[AnalyticsConsts.P_UID]          = ResolveUid();
            p[AnalyticsConsts.P_EVENT_TS]     = DateTime.UtcNow.ToString("o");
            p[AnalyticsConsts.P_END_REASON]   = endReason;
            p[AnalyticsConsts.P_DURATION_SEC] = durationSec;

            EmitEvent(AnalyticsConsts.EVT_SESSION_END, p);
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
            bool firebaseReady = hasInstance && AnalyticsManager.Instance.FirebaseReady;
            if (firebaseReady)
            {
                AnalyticsManager.Instance.LogEvent(evtName, p);
                LogEventToConsole(evtName, p);
            }
            else
            {
                Debug.LogWarning($"[Analytics] {evtName} DROP — AnalyticsManager.HasInstance={hasInstance} FirebaseReady={firebaseReady}");
            }
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
}
