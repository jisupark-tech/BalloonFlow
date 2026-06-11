using System;
using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// WinningStreak 이벤트 로직 — 포인트 누적/overflow carry, stage 진행, 수동 Claim.
    /// 외부 호출 흐름:
    ///   - 레벨 클리어:  WinningStreakManager.Instance.OnLevelCleared(difficulty)
    ///   - 레벨 실패:    WinningStreakManager.Instance.OnLevelFailed()
    ///   - 슬롯 Claim:   WinningStreakManager.Instance.ClaimStage(stage1Based) → 보상 지급
    ///
    /// 보상 지급은 stage 임계 도달 즉시 자동 지급 (명세 §11.4). 슬롯 ClaimStage 는 이미 지급된 건 no-op.
    /// 회차(round): config.activeRoundId 가 State.activeRoundId 와 다르면 새 회차 → 상태 리셋 (명세 §2.3·§11.1).
    /// 이벤트 해금: highestClearedLevel >= unlockLevel (Firestore config 에서 동적, 명세 §2.2 = 35).
    /// 진행 상태는 UserData.winningStreak (Firestore 동기화).
    /// </summary>
    public class WinningStreakManager : Singleton<WinningStreakManager>
    {
        private const string LOG_TAG = "[WinningStreakManager]";

        /// <summary>state / config 변경 (포인트 추가, claim, fetch 완료 등) 발생 시 UI 갱신용.</summary>
        public event Action OnStateChanged;
        /// <summary>특정 stage 가 새로 달성되었을 때 (currentStage 가 N → N+1 넘어가는 순간). UI 알림용.</summary>
        public event Action<int> OnStageAchieved;
        /// <summary>특정 stage 보상이 수령되었을 때.</summary>
        public event Action<int> OnStageClaimed;

        private readonly Queue<PendingLobbyAnimation> _pendingLobbyAnimations = new Queue<PendingLobbyAnimation>();

        protected override void OnSingletonAwake()
        {
            // Config service 가 fetch 끝나면 UI 자동 갱신.
            if (WinningStreakConfigService.HasInstance)
                WinningStreakConfigService.Instance.OnConfigLoaded += HandleConfigLoaded;

            // 레벨 클리어/실패 이벤트 자동 hook.
            EventBus.Subscribe<OnLevelCompleted>(HandleLevelCompletedEvent);
            EventBus.Subscribe<OnLevelFailed>(HandleLevelFailedEvent);

            // 보상 수령 시 WinningStreakGetReward.prefab spawn 연출 자동 hook.
            // ROLLBACK_WS_LOBBY_REWARD_FLOW_20260602:
            // Rewards are now granted after the lobby fire/slider/reward animation, not in-game.
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (WinningStreakConfigService.HasInstance)
                WinningStreakConfigService.Instance.OnConfigLoaded -= HandleConfigLoaded;

            EventBus.Unsubscribe<OnLevelCompleted>(HandleLevelCompletedEvent);
            EventBus.Unsubscribe<OnLevelFailed>(HandleLevelFailedEvent);
        }

        private void HandleConfigLoaded()
        {
            EnsureActiveRound();   // config 도착 시 회차 경계 판정 (새 회차면 상태 리셋)
            OnStateChanged?.Invoke();
        }

        /// <summary>
        /// 회차(round) 경계 판정. 클라 UTC 스케줄(월/금 00:00 경계, [[WinningStreakSchedule]])로 회차 ID 를 산출하고,
        /// State.activeRoundId 와 다르면 새 회차로 보고 streak/진행도/단계/수령내역을 전부 0으로 리셋한다(다음 회차 0단계부터).
        /// 서버 config.activeRoundId 는 수동 강제리셋 override 로만 사용(평소 고정값) — 스케줄 ID 와 결합해 비교.
        /// lifetimePoints 는 통계용이라 회차 무관 누적 유지.
        /// </summary>
        private void EnsureActiveRound()
        {
            var cfg = Config;
            var s = State;
            if (cfg == null || s == null) return;

            // 스케줄 회차(UTC) + 서버 override(고정값) 결합. 둘 중 하나라도 바뀌면 새 회차.
            string roundId = WinningStreakSchedule.GetCurrentRoundId()
                             + "|" + (cfg.activeRoundId ?? string.Empty);
            if (s.activeRoundId == roundId) return;     // 동일 회차 → 유지

            Debug.Log($"{LOG_TAG} 새 회차 감지: '{s.activeRoundId}' → '{roundId}'. 진행 상태 리셋.");
            s.activeRoundId      = roundId;
            s.currentStreak      = 0;
            s.currentStage       = 1;
            s.currentStagePoints = 0;
            s.eventFinished      = false;
            if (s.claimedStages != null) s.claimedStages.Clear();
            else s.claimedStages = new System.Collections.Generic.List<int>();

            SaveProgressFireAndForget();
            if (UserDataService.HasInstance)
                UserDataService.Instance.SaveWinningStreakClaimedStages();
        }

        private void HandleLevelCompletedEvent(OnLevelCompleted evt)
        {
            var difficulty = ResolveDifficultyForLevel(evt.levelId);
            OnLevelCleared(difficulty);
        }

        private void HandleLevelFailedEvent(OnLevelFailed evt) => OnLevelFailed();

        private static DifficultyPurpose ResolveDifficultyForLevel(int levelId)
        {
            if (!LevelEpisodeService.HasInstance) return DifficultyPurpose.Normal;
            var lvl = LevelEpisodeService.Instance.GetLevel(levelId);
            return lvl != null ? lvl.difficultyPurpose : DifficultyPurpose.Normal;
        }

        // ── Public state accessors (UI binding) ──────────────────

        public WinningStreakConfigDoc Config
            => WinningStreakConfigService.HasInstance ? WinningStreakConfigService.Instance.Config : null;

        public WinningStreakState State
            => UserDataService.HasInstance ? UserDataService.Instance.CurrentUser?.winningStreak : null;

        /// <summary>이벤트 해금 여부. unlockLevel 보다 highestClearedLevel 이 작으면 lobby 에서 안 노출.</summary>
        public bool IsUnlocked
        {
            get
            {
                var cfg = Config;
                var u = UserDataService.HasInstance ? UserDataService.Instance.CurrentUser : null;
                if (cfg == null || u == null) return false;
                int reachedLevel = Mathf.Max(1, u.highestClearedLevel + 1);
                return reachedLevel >= cfg.unlockLevel;
            }
        }

        /// <summary>점수/보상 적립 가능 여부 — 해금 레벨(unlockLevel)을 실제로 클리어한 뒤부터.
        /// 노출/로비복귀 게이트(IsUnlocked)는 unlockLevel-1 클리어 시 true(한 단계 빠름)지만,
        /// 적립은 unlockLevel 클리어부터 시작한다. (명세: unlockLevel=35 → 34 클리어=활성화/로비복귀만, 35 클리어부터 적립.)</summary>
        public bool IsScoringActive
        {
            get
            {
                var cfg = Config;
                var u = UserDataService.HasInstance ? UserDataService.Instance.CurrentUser : null;
                if (cfg == null || u == null) return false;
                return u.highestClearedLevel >= cfg.unlockLevel;
            }
        }

        public int TotalStageCount => Config?.stages?.Count ?? 0;

        /// <summary>이벤트 진행 중 여부 — 해금 상태면 상시 진행(스케줄이 한 주를 빈틈없이 덮음). UX(로비 복귀) 게이트용.</summary>
        public bool IsEventActive => IsUnlocked;

        public bool TryDequeuePendingLobbyAnimation(out PendingLobbyAnimation animation)
        {
            while (_pendingLobbyAnimations.Count > 0)
            {
                animation = _pendingLobbyAnimations.Dequeue();
                if (animation != null && animation.gainedPoints > 0)
                    return true;
            }

            animation = null;
            return false;
        }

        /// <summary>현재 회차 종료 UTC 시각(타이머용).</summary>
        public System.DateTime RoundEndUtc => WinningStreakSchedule.GetCurrentRoundEndUtc();

        /// <summary>현재 회차 종료까지 남은 시간(타이머용, 0 미만이면 0).</summary>
        public System.TimeSpan RoundRemaining => WinningStreakSchedule.GetRemaining();

        /// <summary>회차 경계 통과 여부를 외부(로비/팝업 주기 체크)에서 트리거. 경계 넘었으면 리셋 후 UI 갱신 알림.</summary>
        public void CheckRoundBoundary()
        {
            var s = State;
            if (s == null) return;
            string before = s.activeRoundId;
            EnsureActiveRound();
            if (s.activeRoundId != before)
                OnStateChanged?.Invoke();
        }

        /// <summary>stage1Based 가 이미 수령 완료된 stage 인지.</summary>
        public bool IsStageClaimed(int stage1Based)
        {
            var s = State;
            return s != null && s.claimedStages != null && s.claimedStages.Contains(stage1Based);
        }

        /// <summary>stage1Based 가 currentStage 미만이면 (또는 eventFinished) — 달성 완료 (Claim 가능 또는 완료).</summary>
        public bool IsStageAchieved(int stage1Based)
        {
            var s = State;
            if (s == null) return false;
            if (s.eventFinished) return true;
            return stage1Based < s.currentStage;
        }

        // ── Game flow hooks ───────────────────────────────────────

        /// <summary>레벨 클리어 시 호출. streak +1 + 포인트 누적 + Firestore 저장.
        /// 이벤트가 해금돼 있지 않거나 이미 종료된 경우 streak 만 보존하고 포인트는 적용 안 함.</summary>
        public void OnLevelCleared(DifficultyPurpose difficulty)
        {
            var s = State;
            if (s == null) return;

            EnsureActiveRound();   // 회차 경계면 먼저 리셋 후 이번 클리어를 새 회차에 반영

            // [적립은 unlockLevel 클리어부터] IsScoringActive 게이트.
            //   - 노출(IsUnlocked)은 unlockLevel-1(=34) 클리어 시 켜져 로비에 WS UI 가 활성화되지만,
            //     streak 증가·포인트 적립은 unlockLevel(=35) 클리어부터 시작한다.
            //   - 즉 34 클리어 = 활성화/로비복귀만, 35 클리어부터 점수·보상 누적.
            if (!IsScoringActive || s.eventFinished)
            {
                SaveProgressFireAndForget();   // EnsureActiveRound 로 회차 리셋됐을 수 있어 저장.
                OnStateChanged?.Invoke();
                return;
            }

            // [WS 배수 증가 연출 2026-06-11] 클리어 전/후 배수를 캡처 — 로비 FX 가 from→to 이동으로 표현.
            int startMultiplier = ResolveMultiplierForStreak(s.currentStreak);
            s.currentStreak += 1;

            var svc = WinningStreakConfigService.Instance;
            int streakMult = svc.ResolveStreakMultiplier(s.currentStreak);
            int diffMult = svc.ResolveDifficultyMultiplier(difficulty);
            int gained = Mathf.Max(0, streakMult * diffMult);

            if (gained > 0)
            {
                int startStage = s.currentStage;
                int startPoints = s.currentStagePoints;
                var achievedStages = new List<int>(2);

                AddPointsInternal(gained, achievedStages);

                _pendingLobbyAnimations.Enqueue(new PendingLobbyAnimation
                {
                    startStage = startStage,
                    startPoints = startPoints,
                    endStage = s.currentStage,
                    endPoints = s.currentStagePoints,
                    gainedPoints = gained,
                    achievedStages = achievedStages,
                    startMultiplier = startMultiplier,
                    endMultiplier = ResolveMultiplierForStreak(s.currentStreak)
                });
            }

            SaveProgressFireAndForget();
            OnStateChanged?.Invoke();
        }

        /// <summary>레벨 실패 시 streak 리셋. 포인트는 보존.
        /// [WS quit-fail 2026-06-10] 리셋 전 배수가 2 이상이면 로비 실패 연출(배수 드롭)을 예약 — UILobby 가 로비 진입 시 1회 소비.</summary>
        public void OnLevelFailed()
        {
            var s = State;
            if (s == null) return;
            if (s.currentStreak == 0) return;
            int fromMultiplier = WinningStreakUI.ResolveCurrentMultiplier(); // 미해금/비활성이면 1 → 연출 미예약
            if (fromMultiplier > 1) _pendingFailFxMultiplier = fromMultiplier;
            s.currentStreak = 0;
            SaveProgressFireAndForget();
            OnStateChanged?.Invoke();
        }

        // [WS quit-fail 2026-06-10] 인게임 중도 이탈(미클리어 상태로 로비 이동) = 실패.
        //   호출부: HUDController(PopupQuit/Settings Home), PopupContinue(포기 2단계) — GoToLobby 직전.
        private int _pendingFailFxMultiplier;

        /// <summary>중도 포기(quit→로비) — 레벨 실패와 동일 처리 (streak 리셋 + 실패 연출 예약). 멱등.</summary>
        public void OnLevelAbandoned() => OnLevelFailed();

        /// <summary>로비 실패 연출(배수 N→1 드롭) 예약 1회 소비. 예약 없으면 false.</summary>
        public bool TryConsumePendingFailFx(out int fromMultiplier)
        {
            fromMultiplier = _pendingFailFxMultiplier;
            _pendingFailFxMultiplier = 0;
            return fromMultiplier > 1;
        }

        // ── Points / overflow ─────────────────────────────────────

        private void AddPointsInternal(int amount, List<int> achievedStages)
        {
            var s = State;
            if (s == null || amount <= 0) return;

            var cfg = Config;
            if (cfg == null || cfg.stages == null || cfg.stages.Count == 0)
            {
                // config 미준비 시 lifetime 만 누적 (포인트 손실 방지)
                s.lifetimePoints += amount;
                return;
            }

            s.lifetimePoints += amount;
            int remaining = amount;
            int totalStages = cfg.stages.Count;

            while (remaining > 0 && s.currentStage <= totalStages)
            {
                var stage = cfg.stages[s.currentStage - 1];
                int need = Mathf.Max(0, stage.requiredPoints - s.currentStagePoints);

                if (remaining < need)
                {
                    s.currentStagePoints += remaining;
                    remaining = 0;
                    break;
                }

                // 현재 stage 완료 — 다음으로 carry
                remaining -= need;
                int achievedStage = s.currentStage;
                s.currentStage += 1;
                s.currentStagePoints = 0;
                OnStageAchieved?.Invoke(achievedStage);
                if (achievedStages != null && !achievedStages.Contains(achievedStage))
                    achievedStages.Add(achievedStage);

                if (s.currentStage > totalStages)
                {
                    s.eventFinished = true;
                    s.currentStage = totalStages; // UI 표시용 — currentStage 가 array 범위 밖으로 못 가게.
                    break;
                }
            }
        }

        // ── Auto-grant / Claim ───────────────────────────────────

        /// <summary>
        /// [#6/7] stage 임계 도달 즉시 보상 자동 지급 (명세 §11.4). AddPointsInternal 에서 호출.
        /// 이미 수령된 stage 면 무시 (중복 방지). 수령 후 claimedStages 기록 + OnStageClaimed 연출 트리거.
        /// </summary>
        /// <summary>달성 완료된 stage 보상을 수령 (자동 지급 도입 후엔 대부분 이미 지급됨 → no-op).
        /// 슬롯 BtnReward 폴백/구버전 호환용. 이미 수령했거나 미달성이면 false.</summary>
        public bool ClaimStage(int stage1Based)
        {
            var s = State;
            var svc = WinningStreakConfigService.HasInstance ? WinningStreakConfigService.Instance : null;
            if (s == null || svc == null)
            {
                Debug.LogWarning($"{LOG_TAG} ClaimStage({stage1Based}) — state 또는 config 미준비.");
                return false;
            }

            if (!IsStageAchieved(stage1Based))
            {
                Debug.Log($"{LOG_TAG} ClaimStage({stage1Based}) — 아직 미달성.");
                return false;
            }

            if (IsStageClaimed(stage1Based))
            {
                Debug.Log($"{LOG_TAG} ClaimStage({stage1Based}) — 이미 수령됨.");
                return false;
            }

            var stage = svc.GetStage(stage1Based);
            if (stage == null || stage.rewards == null)
            {
                Debug.LogWarning($"{LOG_TAG} ClaimStage({stage1Based}) — stage doc null.");
                return false;
            }

            GrantRewards(stage.rewards, $"WinningStreak.stage{stage1Based}");

            s.claimedStages.Add(stage1Based);
            if (UserDataService.HasInstance)
                UserDataService.Instance.SaveWinningStreakClaimedStages();

            OnStageClaimed?.Invoke(stage1Based);
            OnStateChanged?.Invoke();
            return true;
        }

        private void GrantRewards(ShopRewards rewards, string reason)
        {
            if (rewards == null || !UserDataService.HasInstance) return;
            var uds = UserDataService.Instance;

            if (rewards.coins > 0)
                uds.AdjustCoins(rewards.coins, reason);

            if (rewards.boosters != null)
            {
                if (rewards.boosters.hand > 0)
                    uds.AdjustBooster("hand", rewards.boosters.hand, reason);
                if (rewards.boosters.shuffle > 0)
                    uds.AdjustBooster("shuffle", rewards.boosters.shuffle, reason);
                if (rewards.boosters.zap > 0)
                    uds.AdjustBooster("zap", rewards.boosters.zap, reason);
            }

            if (rewards.infiniteHeartsSeconds > 0 && LifeManager.HasInstance)
            {
                // LifeManager 가 잔여 시간 누적 + Firestore 동기화까지 일괄 처리.
                LifeManager.Instance.ActivateInfiniteHearts(rewards.infiniteHeartsSeconds);
            }
        }

        // ── Persistence ──────────────────────────────────────────

        private void SaveProgressFireAndForget()
        {
            if (UserDataService.HasInstance)
                UserDataService.Instance.SaveWinningStreakProgress();
        }

        public class PendingLobbyAnimation
        {
            public int startStage;
            public int startPoints;
            public int endStage;
            public int endPoints;
            public int gainedPoints;
            public List<int> achievedStages;
            // [WS 배수 증가 연출 2026-06-11] 클리어 전/후 배수 (1/5/10/25/100). 0 = 미캡처(현재값 사용).
            public int startMultiplier;
            public int endMultiplier;
        }

        /// <summary>streak 값의 배수 해석 — config 우선, 미준비 시 UI fallback 티어 (1/5/10/25/100).</summary>
        private static int ResolveMultiplierForStreak(int streak)
        {
            streak = Mathf.Max(1, streak);
            if (WinningStreakConfigService.HasInstance)
                return WinningStreakConfigService.Instance.ResolveStreakMultiplier(streak);
            return WinningStreakUI.TierFromStreak(streak);
        }
    }
}
