using System;
using System.Collections.Generic;
using System.Text;
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
            DiagLog("LevelTracker.OnSingletonAwake — subscribed to OnLevelLoaded/Completed/Failed");
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void DiagLog(string msg) => Debug.Log("[Analytics] " + msg);

        protected override void OnDestroy()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnLevelCompleted>(HandleLevelCompleted);
            EventBus.Unsubscribe<OnLevelFailed>(HandleLevelFailed);
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
            // 활성 play 가 있는 상태에서 종료 → quit_by_user
            FirePlayEnd(AnalyticsConsts.RESULT_QUIT, AnalyticsConsts.END_QUIT_BY_USER, finalScore: 0, starCount: 0);
        }

        // ─── EventBus handlers ───

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            // 이전 미종결 play 가 있다면 (예: 씬 재로드) quit 으로 마무리 후 신규 시작
            if (!string.IsNullOrEmpty(_activePlayId))
                FirePlayEnd(AnalyticsConsts.RESULT_QUIT, AnalyticsConsts.END_QUIT_BY_USER, 0, 0);

            _activePlayId = Guid.NewGuid().ToString("N");
            _activeLevelNumber = evt.levelId;
            _activeStartedUtc = DateTime.UtcNow;
            _activeBackgroundSec = 0f;
            _bgEnteredUtc = null;

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

            var p = new Dictionary<string, object>(25);
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
            p[AnalyticsConsts.P_PEAK_RESOURCE]         = 0; // TODO: BoardStateManager / RailManager 측 peak 점유율 노출 시 wiring

            if (UserSnapshotCache.HasInstance)
                UserSnapshotCache.Instance.Stamp(p);

            p[AnalyticsConsts.P_EXTRA_JSON] = BuildExtraJson(playTimeSec, bgSec, finalScore, starCount);

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

        /// <summary>
        /// 25-param overflow 회피용 통합 JSON. ETL 측에서 JSON_EXTRACT(...) 로 분해.
        /// Firebase Analytics param value 100자 limit 준수 — BL 미사용 0 고정 필드 (deadlock/shuffle/hint)는
        /// schema 기본값으로 처리하고 JSON 미포함. 최대 길이 약 80자.
        /// </summary>
        private string BuildExtraJson(int playTimeSec, int bgSec, int score, int starCount)
        {
            var sb = new StringBuilder(96);
            sb.Append("{\"play_time_sec\":").Append(playTimeSec);
            sb.Append(",\"background_time_sec\":").Append(bgSec);
            sb.Append(",\"score\":").Append(score);
            sb.Append(",\"star_count\":").Append(starCount);
            sb.Append("}");
            return sb.ToString();
        }
    }
}
