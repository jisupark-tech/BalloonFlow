using System;
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
    /// 보상 지급은 자동이 아니라 슬롯의 BtnReward 클릭으로만. (사양 협의)
    /// 이벤트 해금: highestClearedLevel >= unlockLevel (Firestore config 에서 동적).
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

        protected override void OnSingletonAwake()
        {
            // Config service 가 fetch 끝나면 UI 자동 갱신.
            if (WinningStreakConfigService.HasInstance)
                WinningStreakConfigService.Instance.OnConfigLoaded += HandleConfigLoaded;

            // 레벨 클리어/실패 이벤트 자동 hook.
            EventBus.Subscribe<OnLevelCompleted>(HandleLevelCompletedEvent);
            EventBus.Subscribe<OnLevelFailed>(HandleLevelFailedEvent);

            // 보상 수령 시 WinningStreakGetReward.prefab spawn 연출 자동 hook.
            WinningStreakGetRewardSpawner.EnsureSubscribed();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (WinningStreakConfigService.HasInstance)
                WinningStreakConfigService.Instance.OnConfigLoaded -= HandleConfigLoaded;

            EventBus.Unsubscribe<OnLevelCompleted>(HandleLevelCompletedEvent);
            EventBus.Unsubscribe<OnLevelFailed>(HandleLevelFailedEvent);
        }

        private void HandleConfigLoaded() => OnStateChanged?.Invoke();

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
                return u.highestClearedLevel >= cfg.unlockLevel;
            }
        }

        public int TotalStageCount => Config?.stages?.Count ?? 0;

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

            s.currentStreak += 1;

            if (!IsUnlocked || s.eventFinished)
            {
                SaveProgressFireAndForget();
                OnStateChanged?.Invoke();
                return;
            }

            var svc = WinningStreakConfigService.Instance;
            int streakMult = svc.ResolveStreakMultiplier(s.currentStreak);
            int diffMult = svc.ResolveDifficultyMultiplier(difficulty);
            int gained = Mathf.Max(0, streakMult * diffMult);

            if (gained > 0)
                AddPointsInternal(gained);

            SaveProgressFireAndForget();
            OnStateChanged?.Invoke();
        }

        /// <summary>레벨 실패 시 streak 리셋. 포인트는 보존.</summary>
        public void OnLevelFailed()
        {
            var s = State;
            if (s == null) return;
            if (s.currentStreak == 0) return;
            s.currentStreak = 0;
            SaveProgressFireAndForget();
            OnStateChanged?.Invoke();
        }

        // ── Points / overflow ─────────────────────────────────────

        private void AddPointsInternal(int amount)
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

                if (s.currentStage > totalStages)
                {
                    s.eventFinished = true;
                    s.currentStage = totalStages; // UI 표시용 — currentStage 가 array 범위 밖으로 못 가게.
                    break;
                }
            }
        }

        // ── Claim ────────────────────────────────────────────────

        /// <summary>달성 완료된 stage 보상을 수령. 이미 수령했거나 미달성이면 false.</summary>
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
    }
}
