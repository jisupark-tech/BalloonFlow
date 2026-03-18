using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Manages the single coin currency — source/sink tracking, persistence,
    /// and transaction history for debugging.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Manager | Phase: 3
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow (outgame_life_booster + BM monetization)
    /// </remarks>
    public class CurrencyManager : Singleton<CurrencyManager>
    {
        #region Constants

        private const string PREFS_KEY_COINS = "BalloonFlow_Coins";
        private const int DEFAULT_INITIAL_COINS = 1000;
        private const int MAX_TRANSACTION_HISTORY = 50;

        // Level clear coin rewards (design: Normal 50, Hard 75, SuperHard 100)
        private const int COINS_CLEAR_NORMAL    = 50;
        private const int COINS_CLEAR_HARD      = 75;
        private const int COINS_CLEAR_SUPERHARD = 100;

        #endregion

        #region Types

        /// <summary>
        /// Categorizes coin sources for analytics and tracking.
        /// </summary>
        public enum CoinSource
        {
            LevelClear,
            RewardedAd,
            IAP,
            DailyReward,
            Other
        }

        /// <summary>
        /// Categorizes coin sinks for analytics and tracking.
        /// </summary>
        public enum CoinSink
        {
            BoosterSelectTool,
            BoosterShuffle,
            BoosterColorRemove,
            BoosterHand,
            HeartRefill,
            Continue,
            Other
        }

        /// <summary>
        /// Records a single currency transaction for debugging.
        /// </summary>
        public struct Transaction
        {
            public int amount;
            public int balanceAfter;
            public bool isSource;
            public string label;
            public float timestamp;
        }

        #endregion

        #region Fields

        [SerializeField] private int _initialCoins = DEFAULT_INITIAL_COINS;

        private int _currentCoins;
        private readonly List<Transaction> _transactionHistory = new List<Transaction>();

        #endregion

        #region Properties

        /// <summary>
        /// Current coin balance (read-only).
        /// </summary>
        public int Coins => _currentCoins;

        /// <summary>
        /// Read-only copy of recent transactions for debugging.
        /// </summary>
        public IReadOnlyList<Transaction> TransactionHistory => _transactionHistory;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            LoadCoins();

            EventBus.Subscribe<OnLevelCompleted>(HandleLevelCompleted);
        }

        protected override void OnDestroy()
        {
            EventBus.Unsubscribe<OnLevelCompleted>(HandleLevelCompleted);

            base.OnDestroy();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns the current coin count.
        /// </summary>
        public int GetCoins()
        {
            return _currentCoins;
        }

        /// <summary>
        /// Adds coins from a specified source. Publishes OnCoinChanged.
        /// </summary>
        /// <param name="amount">Positive amount to add.</param>
        /// <param name="source">Source category for tracking.</param>
        public void AddCoins(int amount, CoinSource source)
        {
            if (amount <= 0)
            {
                Debug.LogWarning($"[CurrencyManager] AddCoins called with non-positive amount: {amount}");
                return;
            }

            _currentCoins += amount;
            SaveCoins();
            RecordTransaction(amount, true, source.ToString());

            EventBus.Publish(new OnCoinChanged
            {
                currentCoins = _currentCoins,
                delta = amount
            });
        }

        /// <summary>
        /// Attempts to spend coins on a specified sink. Returns false if insufficient.
        /// Publishes OnCoinChanged on success.
        /// </summary>
        /// <param name="amount">Positive amount to spend.</param>
        /// <param name="sink">Sink category for tracking.</param>
        /// <returns>True if spend succeeded, false if insufficient coins.</returns>
        public bool SpendCoins(int amount, CoinSink sink)
        {
            if (amount <= 0)
            {
                Debug.LogWarning($"[CurrencyManager] SpendCoins called with non-positive amount: {amount}");
                return false;
            }

            if (_currentCoins < amount)
            {
                Debug.Log($"[CurrencyManager] Insufficient coins: have {_currentCoins}, need {amount}");
                return false;
            }

            _currentCoins -= amount;
            SaveCoins();
            RecordTransaction(amount, false, sink.ToString());

            EventBus.Publish(new OnCoinChanged
            {
                currentCoins = _currentCoins,
                delta = -amount
            });

            return true;
        }

        /// <summary>
        /// Checks whether the player can afford a given amount.
        /// </summary>
        /// <param name="amount">Amount to check.</param>
        /// <returns>True if current coins >= amount.</returns>
        public bool HasEnoughCoins(int amount)
        {
            return _currentCoins >= amount;
        }

        /// <summary>
        /// Returns the configured initial coin amount for new players.
        /// </summary>
        public int GetInitialCoins()
        {
            return _initialCoins;
        }

        /// <summary>
        /// Returns the coin reward for a given star count.
        /// </summary>
        /// <param name="starCount">Stars earned (1-3).</param>
        /// <returns>Coin reward amount.</returns>
        /// <summary>
        /// Returns coin reward based on star count.
        /// Design: Normal clear 50, Hard clear 75, SuperHard clear 100.
        /// Maps stars as proxy: 1★=Normal(50), 2★=Hard(75), 3★=SuperHard(100).
        /// </summary>
        public int GetCoinRewardForStars(int starCount)
        {
            switch (starCount)
            {
                case 1: return COINS_CLEAR_NORMAL;
                case 2: return COINS_CLEAR_HARD;
                case 3: return COINS_CLEAR_SUPERHARD;
                default: return 0;
            }
        }

        /// <summary>
        /// Forces a save of current coins to PlayerPrefs.
        /// Normally called automatically on every transaction.
        /// </summary>
        public void ForceSave()
        {
            SaveCoins();
        }

        /// <summary>
        /// Resets coins to initial value (for testing or new game).
        /// </summary>
        public void ResetToInitial()
        {
            _currentCoins = _initialCoins;
            _transactionHistory.Clear();
            SaveCoins();

            EventBus.Publish(new OnCoinChanged
            {
                currentCoins = _currentCoins,
                delta = 0
            });
        }

        #endregion

        #region Private Methods — Persistence

        private void LoadCoins()
        {
            if (PlayerPrefs.HasKey(PREFS_KEY_COINS))
            {
                _currentCoins = PlayerPrefs.GetInt(PREFS_KEY_COINS, _initialCoins);
            }
            else
            {
                _currentCoins = _initialCoins;
                SaveCoins();
            }
        }

        private void SaveCoins()
        {
            PlayerPrefs.SetInt(PREFS_KEY_COINS, _currentCoins);
            PlayerPrefs.Save();
        }

        #endregion

        #region Private Methods — Transaction Tracking

        private void RecordTransaction(int amount, bool isSource, string label)
        {
            Transaction tx = new Transaction
            {
                amount = amount,
                balanceAfter = _currentCoins,
                isSource = isSource,
                label = label,
                timestamp = Time.realtimeSinceStartup
            };

            _transactionHistory.Add(tx);

            // Trim history to prevent unbounded growth
            while (_transactionHistory.Count > MAX_TRANSACTION_HISTORY)
            {
                _transactionHistory.RemoveAt(0);
            }
        }

        #endregion

        #region Private Methods — Event Handlers

        private void HandleLevelCompleted(OnLevelCompleted evt)
        {
            int reward = GetCoinRewardForStars(evt.starCount);
            if (reward > 0)
            {
                AddCoins(reward, CoinSource.LevelClear);
            }
        }

        #endregion
    }
}
