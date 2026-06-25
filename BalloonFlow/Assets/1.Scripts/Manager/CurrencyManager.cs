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

        // 레벨 클리어 코인 보상 — 난이도 기준 (명세: 노말 20 / 하드 60(×3) / 슈퍼하드 100(×5)).
        // 2026-04-07 PO 결정: 별점(성과) 기준 50/75/100 → 난이도 기준 20/60/100 으로 변경.
        // (아웃게임디렉션 §코인 소스, 인수인계서 §3-1, WS hard_coef 1/3/5 정합)
        private const int COINS_CLEAR_NORMAL    = 20;   // Normal / Tutorial / Rest / Intro
        private const int COINS_CLEAR_HARD      = 60;   // Hard (= 20 × 3)
        private const int COINS_CLEAR_SUPERHARD = 100;  // SuperHard (= 20 × 5)

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
        private int _pendingServerCoinDelta;
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
            TrySubscribeUserData();

            EventBus.Subscribe<OnLevelCompleted>(HandleLevelCompleted);
        }

        protected override void OnDestroy()
        {
            EventBus.Unsubscribe<OnLevelCompleted>(HandleLevelCompleted);
            if (UserDataService.HasInstance)
                UserDataService.Instance.OnUserDataReady -= ReconcileFromFirestore;

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
        /// Adds coins from a specified source. Publishes OnCoinChanged unless suppressed.
        /// </summary>
        /// <param name="amount">Positive amount to add.</param>
        /// <param name="source">Source category for tracking.</param>
        /// <param name="suppressEvent">true 면 OnCoinChanged 발행 안 함 — IAP 보상 연출에서 UILobby 즉시 갱신 방지용. 연출 끝 후 PublishCoinSync() 호출 책임.</param>
        public void AddCoins(int amount, CoinSource source, bool suppressEvent = false)
        {
            if (amount <= 0)
            {
                Debug.LogWarning($"[CurrencyManager] AddCoins called with non-positive amount: {amount}");
                return;
            }

            _currentCoins += amount;
            SaveCoins();
            RecordTransaction(amount, true, source.ToString());

            // Firestore 동기화 (atomic increment). UserDataService 미준비 시 무시 (offline).
            SyncCoinsToFirestore(amount, $"src:{source}");
            Analytics.AnalyticsItemEconomyTracker.EmitCoinEarn(source.ToString(), amount, _currentCoins);
            Analytics.AnalyticsLevelTracker.NotifyCoinEarned(amount); // ROLLBACK_ANALYTICS_NULLFILL_20260625: coin_earned 누적(활성 play 가드 내부)

            if (!suppressEvent)
            {
                EventBus.Publish(new OnCoinChanged
                {
                    currentCoins = _currentCoins,
                    delta = amount
                });
            }
        }

        /// <summary>
        /// 현재 잔액으로 OnCoinChanged 발행 — suppressEvent=true 로 더한 코인을 UI 와 sync 시킬 때 호출.
        /// 연출 (FxGold fly + GoldPanel 펄스) 종료 시점에 호출.
        /// </summary>
        public void PublishCoinSync()
        {
            EventBus.Publish(new OnCoinChanged
            {
                currentCoins = _currentCoins,
                delta = 0
            });
        }

        public bool RefreshFromUserDataCache()
        {
            // ROLLBACK_CURRENCY_USERDATA_CACHE_REFRESH_20260609:
            // Coin spends still use CurrencyManager as the source of truth. This only pulls the
            // already-loaded UserData cache into CurrencyManager before a purchase affordability check.
            if (!UserDataService.HasInstance || !UserDataService.Instance.IsReady)
                return false;

            var user = UserDataService.Instance.CurrentUser;
            if (user == null) return false;

            if (_pendingServerCoinDelta != 0)
            {
                ReconcileFromFirestore();
                return false;
            }

            int serverCoins = Mathf.Max(0, user.coins);
            if (serverCoins == _currentCoins) return true;

            int delta = serverCoins - _currentCoins;
            _currentCoins = serverCoins;
            SaveCoins();
            EventBus.Publish(new OnCoinChanged
            {
                currentCoins = _currentCoins,
                delta = delta
            });
            Debug.Log($"[CurrencyManager] Refreshed coins from UserData cache. coins={_currentCoins}, delta={delta}");
            return true;
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

            // Firestore 동기화 (atomic decrement)
            SyncCoinsToFirestore(-amount, $"sink:{sink}");
            Analytics.AnalyticsItemEconomyTracker.EmitCoinSpend(sink.ToString(), amount, _currentCoins);
            Analytics.AnalyticsLevelTracker.NotifyCoinSpent(amount); // ROLLBACK_ANALYTICS_NULLFILL_20260625: coin_spent 누적(활성 play 가드 내부)

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
        /// 레벨 난이도(DifficultyPurpose)에 따른 클리어 코인 보상.
        /// 명세: 노말 20 / 하드 60 / 슈퍼하드 100. Tutorial·Rest·Intro 는 노말과 동일(20).
        /// 보상은 성과(별점)가 아닌 레벨 난이도에 고정 — 별점 무관.
        /// </summary>
        public int GetCoinRewardForDifficulty(DifficultyPurpose difficulty)
        {
            switch (difficulty)
            {
                case DifficultyPurpose.SuperHard: return COINS_CLEAR_SUPERHARD;
                case DifficultyPurpose.Hard:      return COINS_CLEAR_HARD;
                // Normal / Tutorial / Rest / Intro
                default:                          return COINS_CLEAR_NORMAL;
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

        /// <summary>Debug/user reset path: wipe local coins to zero without granting the new-user starter balance.</summary>
        public void ResetForDebugWipe()
        {
            // ROLLBACK_RESET_USERDATA_ZERO_COINS_20260619:
            // Reset UserData is used as a QA wipe. ResetToInitial() grants the configured
            // starter balance, so keep that for real new-user flows and use this for debug reset.
            _currentCoins = 0;
            _pendingServerCoinDelta = 0;
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

        private void TrySubscribeUserData()
        {
            if (!UserDataService.HasInstance) return;

            UserDataService.Instance.OnUserDataReady += ReconcileFromFirestore;
            if (UserDataService.Instance.IsReady)
                ReconcileFromFirestore();
        }

        private void ReconcileFromFirestore()
        {
            if (!UserDataService.HasInstance || !UserDataService.Instance.IsReady) return;
            var user = UserDataService.Instance.CurrentUser;
            if (user == null) return;

            if (_pendingServerCoinDelta != 0)
            {
                UserDataService.Instance.AdjustCoins(_pendingServerCoinDelta, "CurrencyManager.ReconcilePending");
                _pendingServerCoinDelta = 0;
                return;
            }

            if (user.coins == _currentCoins) return;

            int delta = user.coins - _currentCoins;
            _currentCoins = Mathf.Max(0, user.coins);
            SaveCoins();
            EventBus.Publish(new OnCoinChanged
            {
                currentCoins = _currentCoins,
                delta = delta
            });
        }

        private void SyncCoinsToFirestore(int delta, string reason)
        {
            if (delta == 0) return;

            if (UserDataService.HasInstance && UserDataService.Instance.IsReady)
            {
                UserDataService.Instance.AdjustCoins(delta, reason);
                return;
            }

            _pendingServerCoinDelta += delta;
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
            // 보상은 레벨 난이도 기준 (성과 별점 아님). LevelManager 미존재 시 Normal 로 폴백.
            DifficultyPurpose diff = LevelManager.HasInstance
                ? LevelManager.Instance.GetLevelDifficulty(evt.levelId)
                : DifficultyPurpose.Normal;
            int reward = GetCoinRewardForDifficulty(diff);
            if (reward > 0)
            {
                AddCoins(reward, CoinSource.LevelClear);
            }
        }

        #endregion
    }
}
