using System;
using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow.Analytics
{
    /// <summary>
    /// 레벨 이벤트 추적 → level_play_start_event / level_play_event 발사.
    ///
    /// 매핑:
    ///   OnLevelLoaded  → level_play_start_event (play_id 발급, 보관)
    ///   OnLevelCompleted → level_play_event (result=clear, end_reason=clear)
    ///   OnLevelFailed    → level_play_event (result=fail, end_reason=fail_out_of_resource)
    ///   OnApplicationQuit while playing → level_play_event (result=quit, end_reason=quit_by_user)
    ///
    /// 파라미터 overflow (25 limit) 대응:
    ///   덜 중요한 필드는 extra_json (string) 으로 통합 — schema 매핑은 ETL 측에서 JSON_EXTRACT
    ///
    /// attempt_number:
    ///   PlayerPrefs key = "BF_Attempt_<appVersion>_<levelNumber>" — app_version 바뀌면 자연 reset
    /// </summary>
    public class AnalyticsLevelTracker : Singleton<AnalyticsLevelTracker>
    {
        private const string PREFS_ATTEMPT_PREFIX = "BF_Attempt_";

        // 현재 진행 중인 play 추적
        private string _activePlayId;
        private int    _activeLevelNumber;
        private int    _activeAttemptNumber;
        private bool   _activeIsFirstPlay;
        private int    _activeHardTier;
        private bool   _activeIsTutorial;
        private int    _activeLivesBefore;
        private bool   _activeIsInfiniteLives;
        private DateTime _activeStartedUtc;
        private float  _activeBackgroundSec;
        private DateTime? _bgEnteredUtc;

        // ROLLBACK_ANALYTICS_NULLFILL_20260625: play_event NULL 채우기용 레벨당 누적기(가산만 — 기존 타이밍/구조 불변).
        private int _movesUsed;          // 홀더 배포 횟수(OnHolderDeploymentDone)
        private int _continuePopupCount; // 이어하기 팝업 표시 횟수(PopupContinue.OnEnable)
        private int _coinEarned;         // 레벨 중 코인 획득 누적(CurrencyManager.AddCoins)
        private int _coinSpent;          // 레벨 중 코인 사용 누적(CurrencyManager.SpendCoins)

        // ROLLBACK_ANALYTICS_FAIL_RESULT_20260707: result='fail' 이 BQ 에 전혀 안 찍히던 버그.
        //   이어하기 무제한 설계로 ContinueHandler.CanContinue()==true 고정 → LevelManager.HandleBoardFailed 가
        //   FailLevel 을 영구 유예 → OnLevelFailed 미발행. 실패 후 Retry/Home 은 레벨 리로드 경로라 직전 play 가
        //   전부 quit 으로 종결됐다. 보드 실패(OnBoardFailed)를 pending 마킹, 이어하기(OnContinueApplied) 시 해제 —
        //   pending 인 채 play 가 종결되면(리로드/앱종료) result=fail 로 발사. 이어하기 후 클리어는 기존대로 clear.
        private bool _boardFailedPending;

        public string CurrentPlayId => _activePlayId ?? "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            DiagLog("LevelTracker.AutoCreate fired");
            if (HasInstance) { DiagLog("LevelTracker already exists — skip"); return; }
            var go = new GameObject("AnalyticsLevelTracker");
            go.AddComponent<AnalyticsLevelTracker>();
        }

        protected override void OnSingletonAwake()
        {
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Subscribe<OnLevelCompleted>(HandleLevelCompleted);
            EventBus.Subscribe<OnLevelFailed>(HandleLevelFailed);
            // ROLLBACK_ANALYTICS_FAIL_RESULT_20260707: 보드 실패/이어하기 구독 — fail pending 마킹용.
            EventBus.Subscribe<OnBoardFailed>(HandleBoardFailedForResult);
            EventBus.Subscribe<OnContinueApplied>(HandleContinueApplied);
            // ROLLBACK_ANALYTICS_NULLFILL_20260625: 홀더 배포 = moves_used 계측(가산만).
            EventBus.Subscribe<OnHolderDeploymentDone>(HandleHolderDeploymentDone);
            DiagLog("LevelTracker.OnSingletonAwake — subscribed to OnLevelLoaded/Completed/Failed");
        }

        // ROLLBACK_ANALYTICS_NULLFILL_20260625: 홀더 1회 배포 = 1 move. 활성 play 중에만 누적.
        private void HandleHolderDeploymentDone(OnHolderDeploymentDone evt)
        {
            if (!string.IsNullOrEmpty(_activePlayId)) _movesUsed++;
        }

        // ROLLBACK_ANALYTICS_NULLFILL_20260625: 외부(팝업/통화 매니저)에서 호출하는 누적 notify (활성 play 중에만).
        public static void NotifyContinuePopupShown()
        {
            if (HasInstance && !string.IsNullOrEmpty(Instance._activePlayId)) Instance._continuePopupCount++;
        }

        public static void NotifyCoinEarned(int amount)
        {
            if (amount > 0 && HasInstance && !string.IsNullOrEmpty(Instance._activePlayId)) Instance._coinEarned += amount;
        }

        public static void NotifyCoinSpent(int amount)
        {
            if (amount > 0 && HasInstance && !string.IsNullOrEmpty(Instance._activePlayId)) Instance._coinSpent += amount;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void DiagLog(string msg) => Debug.Log("[Analytics] " + msg);

        protected override void OnDestroy()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnLevelCompleted>(HandleLevelCompleted);
            EventBus.Unsubscribe<OnLevelFailed>(HandleLevelFailed);
            EventBus.Unsubscribe<OnBoardFailed>(HandleBoardFailedForResult);   // ROLLBACK_ANALYTICS_FAIL_RESULT_20260707
            EventBus.Unsubscribe<OnContinueApplied>(HandleContinueApplied);    // ROLLBACK_ANALYTICS_FAIL_RESULT_20260707
            EventBus.Unsubscribe<OnHolderDeploymentDone>(HandleHolderDeploymentDone); // ROLLBACK_ANALYTICS_NULLFILL_20260625
            base.OnDestroy();
        }

        // ─── App lifecycle (background time + quit_by_user) ───

        private void OnApplicationPause(bool pause)
        {
            if (string.IsNullOrEmpty(_activePlayId)) return;

            if (pause) _bgEnteredUtc = DateTime.UtcNow;
            else if (_bgEnteredUtc.HasValue)
            {
                _activeBackgroundSec += (float)(DateTime.UtcNow - _bgEnteredUtc.Value).TotalSeconds;
                _bgEnteredUtc = null;
            }
        }

        private void OnApplicationQuit()
        {
            if (string.IsNullOrEmpty(_activePlayId)) return;
            // 활성 play 가 있는 상태에서 종료 → quit_by_user (보드 실패 pending 이면 fail)
            FireUnresolvedPlayEnd();
        }

        // ROLLBACK_ANALYTICS_FAIL_RESULT_20260707: 미종결 play 종결 — 보드 실패 후 이어하기 없이 떠난
        //   케이스(Retry 리로드/홈/앱종료)는 비즈니스 결과가 'fail', 그 외 중도 이탈은 'quit'.
        private void FireUnresolvedPlayEnd()
        {
            if (_boardFailedPending)
                FirePlayEnd(AnalyticsConsts.RESULT_FAIL, AnalyticsConsts.END_FAIL_OUT_OF_RESOURCE, 0, 0);
            else
                FirePlayEnd(AnalyticsConsts.RESULT_QUIT, AnalyticsConsts.END_QUIT_BY_USER, 0, 0);
        }

        private void HandleBoardFailedForResult(OnBoardFailed evt)
        {
            if (!string.IsNullOrEmpty(_activePlayId)) _boardFailedPending = true;
        }

        private void HandleContinueApplied(OnContinueApplied evt)
        {
            _boardFailedPending = false; // 이어하기로 재개 — 이 판의 결과는 이후 clear/fail/quit 로 다시 결정
        }

        // ─── EventBus handlers ───

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            // OnLevelLoaded 는 짧은 시간 내 다중 발화될 수 있음 (scene 전환/loadingFlow — TutorialController·
            // GameBootstrap 도 각자 latch 로 방어 중). dedupe 없이는 같은 레벨에 play_start 2발 + attempt +2 +
            // 직전 play 유령 quit 이 그대로 적재됨 (2026-07-07 BQ 실데이터로 확인). 같은 레벨의 2초 내 재발화는 무시.
            if (!string.IsNullOrEmpty(_activePlayId)
                && evt.levelId == _activeLevelNumber
                && (DateTime.UtcNow - _activeStartedUtc).TotalSeconds < 2.0)
            {
                DiagLog($"LevelTracker duplicate OnLevelLoaded ignored — level={evt.levelId}");
                return;
            }

            // 이전 미종결 play 가 있다면 (예: 씬 재로드) 종결 후 신규 시작
            // (보드 실패 pending 이면 fail — 실패 후 Retry 가 이 경로로 리로드됨. ROLLBACK_ANALYTICS_FAIL_RESULT_20260707)
            if (!string.IsNullOrEmpty(_activePlayId))
                FireUnresolvedPlayEnd();

            _boardFailedPending = false;
            _activePlayId = Guid.NewGuid().ToString("N");
            _activeLevelNumber = evt.levelId;
            _activeStartedUtc = DateTime.UtcNow;
            _activeBackgroundSec = 0f;
            _bgEnteredUtc = null;
            // ROLLBACK_ANALYTICS_NULLFILL_20260625: 신규 play 시작 시 누적기 리셋.
            _movesUsed = 0;
            _continuePopupCount = 0;
            _coinEarned = 0;
            _coinSpent = 0;

            // attempt_number: app_version 변경 시 reset (PlayerPrefs key 에 app_version 포함)
            string attemptKey = PREFS_ATTEMPT_PREFIX + Application.version + "_" + evt.levelId;
            _activeAttemptNumber = PlayerPrefs.GetInt(attemptKey, 0) + 1;
            PlayerPrefs.SetInt(attemptKey, _activeAttemptNumber);
            PlayerPrefs.Save();
            _activeIsFirstPlay = (_activeAttemptNumber == 1);

            // hard_tier (LevelConfig.difficultyPurpose 매핑)
            _activeHardTier = ResolveHardTier();
            _activeIsTutorial = (LevelManager.HasInstance && LevelManager.Instance.CurrentLevel != null
                                  && LevelManager.Instance.CurrentLevel.difficultyPurpose == DifficultyPurpose.Tutorial);

            _activeLivesBefore = LifeManager.HasInstance ? LifeManager.Instance.GetLives() : -1;
            _activeIsInfiniteLives = LifeManager.HasInstance && LifeManager.Instance.IsInfiniteHeartsActive;

            FireLevelPlayStart();
        }

        private void HandleLevelCompleted(OnLevelCompleted evt)
        {
            FirePlayEnd(AnalyticsConsts.RESULT_CLEAR, AnalyticsConsts.END_CLEAR, evt.score, evt.starCount);
        }

        private void HandleLevelFailed(OnLevelFailed evt)
        {
            FirePlayEnd(AnalyticsConsts.RESULT_FAIL, AnalyticsConsts.END_FAIL_OUT_OF_RESOURCE, 0, 0);
        }

        // ─── Event emission ───

        private void FireLevelPlayStart()
        {
            var p = new Dictionary<string, object>(24);
            p[AnalyticsConsts.P_EVENT_ID]              = Guid.NewGuid().ToString("N");
            p[AnalyticsConsts.P_PLAY_ID]               = _activePlayId;
            p[AnalyticsConsts.P_SESSION_ID]            = AnalyticsSessionTracker.HasInstance
                ? AnalyticsSessionTracker.Instance.CurrentSessionId : "";
            p[AnalyticsConsts.P_GAME_ID]               = AnalyticsConsts.GAME_ID;
            p[AnalyticsConsts.P_UID]                   = AnalyticsSessionTracker.ResolveUid();
            p[AnalyticsConsts.P_EVENT_TS]              = _activeStartedUtc.ToString("o");
            p[AnalyticsConsts.P_APP_VERSION]           = Application.version;
            p[AnalyticsConsts.P_GEO_COUNTRY]           = AnalyticsSessionTracker.ResolveGeoCountry();
            p[AnalyticsConsts.P_PLATFORM]              = AnalyticsSessionTracker.ResolvePlatform();
            p[AnalyticsConsts.P_DEVICE_MODEL]          = SystemInfo.deviceModel;
            p[AnalyticsConsts.P_LEVEL_NUMBER]          = _activeLevelNumber;
            p[AnalyticsConsts.P_IS_TUTORIAL]           = _activeIsTutorial;
            p[AnalyticsConsts.P_HARD_TIER]             = _activeHardTier;
            p[AnalyticsConsts.P_ATTEMPT_NUMBER]        = _activeAttemptNumber;
            p[AnalyticsConsts.P_IS_FIRST_PLAY]         = _activeIsFirstPlay;
            p[AnalyticsConsts.P_LIVES_BEFORE]          = _activeLivesBefore;
            p[AnalyticsConsts.P_IS_INFINITE_LIVES]     = _activeIsInfiniteLives;

            if (UserSnapshotCache.HasInstance)
                UserSnapshotCache.Instance.Stamp(p);

            AnalyticsSessionTracker.EmitEvent(AnalyticsConsts.EVT_LEVEL_PLAY_START, p);
        }

        private void FirePlayEnd(string result, string endReason, int finalScore, int starCount)
        {
            if (string.IsNullOrEmpty(_activePlayId)) return;

            int playTimeSec = (int)Math.Max(0, (DateTime.UtcNow - _activeStartedUtc).TotalSeconds);
            int bgSec = (int)Math.Max(0, _activeBackgroundSec);
            int livesAfter = LifeManager.HasInstance ? LifeManager.Instance.GetLives() : -1;

            var p = new Dictionary<string, object>(36);
            p[AnalyticsConsts.P_EVENT_ID]              = Guid.NewGuid().ToString("N");
            p[AnalyticsConsts.P_PLAY_ID]               = _activePlayId;
            p[AnalyticsConsts.P_SESSION_ID]            = AnalyticsSessionTracker.HasInstance
                ? AnalyticsSessionTracker.Instance.CurrentSessionId : "";
            p[AnalyticsConsts.P_GAME_ID]               = AnalyticsConsts.GAME_ID;
            p[AnalyticsConsts.P_UID]                   = AnalyticsSessionTracker.ResolveUid();
            p[AnalyticsConsts.P_EVENT_TS]              = DateTime.UtcNow.ToString("o");
            p[AnalyticsConsts.P_APP_VERSION]           = Application.version;
            p[AnalyticsConsts.P_GEO_COUNTRY]           = AnalyticsSessionTracker.ResolveGeoCountry();
            p[AnalyticsConsts.P_PLATFORM]              = AnalyticsSessionTracker.ResolvePlatform();
            p[AnalyticsConsts.P_LEVEL_NUMBER]          = _activeLevelNumber;
            p[AnalyticsConsts.P_IS_TUTORIAL]           = _activeIsTutorial;
            p[AnalyticsConsts.P_HARD_TIER]             = _activeHardTier;
            p[AnalyticsConsts.P_ATTEMPT_NUMBER]        = _activeAttemptNumber;
            p[AnalyticsConsts.P_IS_FIRST_PLAY]         = _activeIsFirstPlay;
            p[AnalyticsConsts.P_IS_REPLAY_AFTER_CLEAR] = false; // BL 현 단계 재도전 없음
            p[AnalyticsConsts.P_RESULT]                = result;
            p[AnalyticsConsts.P_END_REASON]            = endReason;
            p[AnalyticsConsts.P_LIVES_AFTER]           = livesAfter;
            // peak_resource_usage_ratio (0.0~1.0) — RailManager 가 레벨 동안 기록한 레일 최대 점유율.
            // §20: ≥0.8 → narrow_clear. (RailManager.PeakOccupancyRatio = EffectiveOccupiedCount/PhysicalCapacity max)
            // BQ NUMERIC(소수 9자리 상한)에 float 노이즈가 넘어가면 행 거절 → 라운딩 후 emit.
            p[AnalyticsConsts.P_PEAK_RESOURCE]         = Math.Round(RailManager.HasInstance ? RailManager.Instance.PeakOccupancyRatio : 0.0, 4);

            if (UserSnapshotCache.HasInstance)
                UserSnapshotCache.Instance.Stamp(p);

            // [BQ_DIRECT 2026-06-16] 직접 적재라 GA4 25-param 제한 없음 → extra_json 통합 폐기,
            //   play_event 테이블의 개별 컬럼으로 직접 emit. (롤백: 아래 4줄을 P_EXTRA_JSON=BuildExtraJson 로 환원)
            p[AnalyticsConsts.P_PLAY_TIME_SEC]       = playTimeSec;
            p[AnalyticsConsts.P_BACKGROUND_TIME_SEC] = bgSec;
            p[AnalyticsConsts.P_SCORE]               = finalScore;
            p[AnalyticsConsts.P_STAR_COUNT]          = starCount;

            // ROLLBACK_ANALYTICS_NULLFILL_20260625: play_event NULL 9개 채우기 — 누적기 + 매니저 getter 읽기(가산만).
            p[AnalyticsConsts.P_MOVES_USED]          = _movesUsed;
            // objective_total/done — BL=풍선 제거형. total = 종료 시점 (남은 + 터뜨린), done = 터뜨린 수.
            p[AnalyticsConsts.P_OBJECTIVE_TOTAL]     = BalloonController.HasInstance
                ? BalloonController.Instance.RemainingCount + BalloonController.Instance.PoppedCount : 0;
            p[AnalyticsConsts.P_OBJECTIVE_DONE]      = BalloonController.HasInstance ? BalloonController.Instance.PoppedCount : 0;
            p[AnalyticsConsts.P_AVG_RESOURCE]        = Math.Round(RailManager.HasInstance ? RailManager.Instance.AverageOccupancyRatio : 0.0, 4);
            p[AnalyticsConsts.P_CONTINUE_POPUP_COUNT] = _continuePopupCount;
            p[AnalyticsConsts.P_CONTINUE_COUNT]      = ContinueHandler.HasInstance ? ContinueHandler.Instance.GetContinueCount() : 0;
            p[AnalyticsConsts.P_COIN_EARNED]         = _coinEarned;
            p[AnalyticsConsts.P_COIN_SPENT]          = _coinSpent;
            p[AnalyticsConsts.P_FINAL_COIN_BALANCE]  = CurrencyManager.HasInstance ? CurrencyManager.Instance.Coins : 0;

            AnalyticsSessionTracker.EmitEvent(AnalyticsConsts.EVT_LEVEL_PLAY, p);

            // reset
            _activePlayId = null;
            _activeBackgroundSec = 0f;
            _bgEnteredUtc = null;
        }

        // ─── Helpers ───

        private static int ResolveHardTier()
        {
            if (!LevelManager.HasInstance) return 0;
            var cfg = LevelManager.Instance.CurrentLevel;
            if (cfg == null) return 0;
            switch (cfg.difficultyPurpose)
            {
                case DifficultyPurpose.Hard:      return 1;
                case DifficultyPurpose.SuperHard: return 2;
                default:                          return 0;
            }
        }

    }
}
