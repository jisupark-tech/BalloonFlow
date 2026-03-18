using System;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 7-day sequential daily reward cycle. Awards coins and bonus items once per
    /// calendar day. After day 7 the cycle resets to day 1.
    /// Persistence via PlayerPrefs. Distributes rewards through CurrencyManager
    /// and LifeManager.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Manager | Phase: 3
    /// DB Reference: No DB match — generated from L3 YAML logicFlow
    /// Reward schedule: Day1=100c, Day2=150c, Day3=200c+tray, Day4=300c,
    ///                  Day5=500c+heart, Day6=400c+shuffle, Day7=1000c+3boosters
    /// </remarks>
    public class DailyRewardManager : Singleton<DailyRewardManager>
    {
        #region Constants

        private const int    CYCLE_LENGTH              = 7;
        private const string PREFS_LAST_CLAIM_DATE     = "BF_DailyReward_LastClaim";
        private const string PREFS_CURRENT_STREAK_DAY  = "BF_DailyReward_StreakDay";

        // Bonus type string constants
        public const string BONUS_NONE           = "none";
        public const string BONUS_BOOSTER_SELECT  = BoosterManager.SELECT_TOOL;
        public const string BONUS_BOOSTER_SHUFFLE = BoosterManager.SHUFFLE;
        public const string BONUS_HEART_REFILL   = "heart_refill";
        public const string BONUS_MIXED          = "booster_mixed";

        #endregion

        #region Fields

        private int      _currentStreakDay;   // 1-based, wraps at CYCLE_LENGTH
        private DateTime _lastClaimDate;      // Date-only (no time component)

        private static readonly DailyReward[] _rewardSchedule = new DailyReward[]
        {
            new DailyReward { day = 1, coins = 100,  bonusType = BONUS_NONE,           bonusCount = 0 },
            new DailyReward { day = 2, coins = 150,  bonusType = BONUS_NONE,           bonusCount = 0 },
            new DailyReward { day = 3, coins = 200,  bonusType = BONUS_BOOSTER_SELECT,   bonusCount = 1 },
            new DailyReward { day = 4, coins = 300,  bonusType = BONUS_NONE,           bonusCount = 0 },
            new DailyReward { day = 5, coins = 500,  bonusType = BONUS_HEART_REFILL,   bonusCount = 1 },
            new DailyReward { day = 6, coins = 400,  bonusType = BONUS_BOOSTER_SHUFFLE,bonusCount = 1 },
            new DailyReward { day = 7, coins = 1000, bonusType = BONUS_MIXED,          bonusCount = 3 },
        };

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            LoadFromPrefs();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns true if the player has not yet claimed a reward today.
        /// </summary>
        public bool CanClaimToday()
        {
            return DateTime.UtcNow.Date > _lastClaimDate.Date;
        }

        /// <summary>
        /// Claims today's reward if available. Returns the reward data that was granted,
        /// or null if no reward is available (already claimed today).
        /// </summary>
        public DailyReward ClaimReward()
        {
            if (!CanClaimToday())
            {
                Debug.Log("[DailyRewardManager] Reward already claimed today.");
                return null;
            }

            DailyReward reward = GetRewardForDay(_currentStreakDay);
            GrantReward(reward);

            // Advance streak day and wrap
            int claimedDay = _currentStreakDay;
            _currentStreakDay = (_currentStreakDay % CYCLE_LENGTH) + 1;
            _lastClaimDate    = DateTime.UtcNow.Date;

            SaveToPrefs();

            Debug.Log($"[DailyRewardManager] Claimed day {claimedDay}: {reward.coins} coins, bonus={reward.bonusType} x{reward.bonusCount}");
            return reward;
        }

        /// <summary>
        /// Returns the current day in the 7-day cycle (1–7).
        /// </summary>
        public int GetCurrentDay()
        {
            return _currentStreakDay;
        }

        /// <summary>
        /// Returns the reward data for a specific day (1–7).
        /// Returns day 1 reward for out-of-range values.
        /// </summary>
        public DailyReward GetRewardForDay(int day)
        {
            int index = Mathf.Clamp(day - 1, 0, CYCLE_LENGTH - 1);
            return _rewardSchedule[index];
        }

        /// <summary>
        /// Returns the current streak day count (same as GetCurrentDay for this implementation).
        /// </summary>
        public int GetStreak()
        {
            return _currentStreakDay;
        }

        /// <summary>
        /// Returns the number of seconds remaining until the next claimable reward.
        /// Returns 0 if a reward is already claimable.
        /// </summary>
        public double GetSecondsUntilNextClaim()
        {
            if (CanClaimToday())
            {
                return 0.0;
            }

            DateTime nextMidnight = _lastClaimDate.Date.AddDays(1);
            return (nextMidnight - DateTime.UtcNow).TotalSeconds;
        }

        #endregion

        #region Private Methods

        private void GrantReward(DailyReward reward)
        {
            if (reward == null)
            {
                return;
            }

            // Grant coins
            if (reward.coins > 0 && CurrencyManager.HasInstance)
            {
                CurrencyManager.Instance.AddCoins(reward.coins, CurrencyManager.CoinSource.DailyReward);
            }

            // Grant bonus
            if (reward.bonusCount <= 0 || reward.bonusType == BONUS_NONE)
            {
                return;
            }

            switch (reward.bonusType)
            {
                case BONUS_HEART_REFILL:
                    GrantHeartRefill(reward.bonusCount);
                    break;

                case BONUS_BOOSTER_SELECT:
                case BONUS_BOOSTER_SHUFFLE:
                case BONUS_MIXED:
                    GrantBoosters(reward.bonusType, reward.bonusCount);
                    break;

                default:
                    Debug.LogWarning($"[DailyRewardManager] Unknown bonus type: {reward.bonusType}");
                    break;
            }
        }

        private void GrantHeartRefill(int count)
        {
            if (!LifeManager.HasInstance)
            {
                Debug.LogWarning("[DailyRewardManager] LifeManager not available for heart refill.");
                return;
            }

            for (int i = 0; i < count; i++)
            {
                LifeManager.Instance.AddLife(1);
            }
        }

        private void GrantBoosters(string boosterType, int count)
        {
            if (BoosterManager.HasInstance)
            {
                BoosterManager.Instance.AddBooster(boosterType, count);
            }

            Debug.Log($"[DailyRewardManager] Granted {count}x booster: {boosterType}");
        }

        private void LoadFromPrefs()
        {
            _currentStreakDay = PlayerPrefs.GetInt(PREFS_CURRENT_STREAK_DAY, 1);
            _currentStreakDay = Mathf.Clamp(_currentStreakDay, 1, CYCLE_LENGTH);

            if (PlayerPrefs.HasKey(PREFS_LAST_CLAIM_DATE))
            {
                string stored = PlayerPrefs.GetString(PREFS_LAST_CLAIM_DATE);
                if (!DateTime.TryParse(stored, out _lastClaimDate))
                {
                    _lastClaimDate = DateTime.MinValue;
                }
            }
            else
            {
                _lastClaimDate = DateTime.MinValue;
            }
        }

        private void SaveToPrefs()
        {
            PlayerPrefs.SetInt(PREFS_CURRENT_STREAK_DAY, _currentStreakDay);
            PlayerPrefs.SetString(PREFS_LAST_CLAIM_DATE, _lastClaimDate.Date.ToString("o"));
            PlayerPrefs.Save();
        }

        #endregion
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Data class
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Defines a single day's reward entry in the 7-day daily reward cycle.
    /// </summary>
    [System.Serializable]
    public class DailyReward
    {
        /// <summary>Day number in the cycle (1–7).</summary>
        public int day;

        /// <summary>Coins awarded on this day.</summary>
        public int coins;

        /// <summary>
        /// Bonus type identifier.
        /// Values: "none", "select_tool", "shuffle", "heart_refill", "booster_mixed"
        /// </summary>
        public string bonusType;

        /// <summary>Number of bonus items to grant (0 if no bonus).</summary>
        public int bonusCount;
    }
}
