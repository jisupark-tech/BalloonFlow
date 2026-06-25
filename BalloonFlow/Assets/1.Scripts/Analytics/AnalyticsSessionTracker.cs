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
                // [BQ_DIRECT 2026-06-16] 이벤트는 AnalyticsManager 가 버퍼링 후 BigQuery 로 직접 전송하므로
                //   Firebase Analytics ready 대기 불필요 — 인스턴스 존재만으로 세션 시작 게이트 통과(uid 만 별도 대기).
                bool analyticsReady = analyticsHas;
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
