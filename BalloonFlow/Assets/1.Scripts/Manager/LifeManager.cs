using System;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Heart/life system. Manages up to 5 lives that recharge at 1 per 30 minutes.
    /// Lives are lost on level failure and can be restored via coins or ad rewards.
    /// State is persisted via PlayerPrefs.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Manager | Phase: 3
    /// DB Reference: No DB match — generated from L3 YAML logicFlow
    /// Rules: Max 5 lives, lose 1 on fail, +1 per 30 min, coin refill = 900 coins
    /// </remarks>
    public class LifeManager : Singleton<LifeManager>
    {
        #region Constants

        private const int    MAX_LIVES              = 5;
        private const int    RECHARGE_SECONDS        = 1800;   // 30 minutes
        private const int    COIN_REFILL_COST        = 900;
        private const string PREFS_CURRENT_LIVES     = "BF_CurrentLives";
        private const string PREFS_LAST_RECHARGE_UTC = "BF_LastRechargeUtc";

        #endregion

        #region Fields

        private int  _currentLives;
        private long _lastRechargeUtcTicks;   // stored as long ticks for precision
        private float _rechargeTimer;
        private float _infiniteHeartsEndTime; // realtimeSinceStartup when infinite hearts expire

        #endregion

        #region Properties

        /// <summary>Current life count (0 – MaxLives).</summary>
        public int CurrentLives => _currentLives;

        /// <summary>Maximum allowed lives.</summary>
        public int MaxLives => MAX_LIVES;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            LoadFromPrefs();
            ProcessOfflineRecharge();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelFailed>(HandleLevelFailed);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelFailed>(HandleLevelFailed);
        }

        /// <summary>
        /// Periodic recharge check. Only runs while lives are missing.
        /// </summary>
        private void Update()
        {
            if (_currentLives >= MAX_LIVES)
            {
                return;
            }

            _rechargeTimer += Time.deltaTime;

            if (_rechargeTimer >= RECHARGE_SECONDS)
            {
                _rechargeTimer -= RECHARGE_SECONDS;
                AddLife(1);
                _lastRechargeUtcTicks = DateTime.UtcNow.Ticks;
                SaveToPrefs();
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns the current life count.
        /// </summary>
        public int GetLives()
        {
            return _currentLives;
        }

        /// <summary>
        /// Returns the maximum life count.
        /// </summary>
        public int GetMaxLives()
        {
            return MAX_LIVES;
        }

        /// <summary>
        /// Returns true if the player has at least one life available.
        /// </summary>
        public bool HasLife()
        {
            return _currentLives > 0;
        }

        /// <summary>
        /// Returns true if lives are at maximum capacity.
        /// </summary>
        public bool IsFullLives()
        {
            return _currentLives >= MAX_LIVES;
        }

        /// <summary>
        /// Attempts to consume one life. Returns true on success, false if no lives remain.
        /// </summary>
        public bool UseLive()
        {
            // Infinite hearts: always succeed without consuming
            if (IsInfiniteHeartsActive)
                return true;

            if (_currentLives <= 0)
            {
                Debug.Log("[LifeManager] No lives remaining.");
                return false;
            }

            _currentLives--;

            // Begin tracking recharge if not already running
            if (_currentLives < MAX_LIVES)
            {
                _lastRechargeUtcTicks = DateTime.UtcNow.Ticks;
                _rechargeTimer = 0f;
            }

            SaveToPrefs();
            SyncToFirestore("UseLive");
            PublishLifeChanged();
            return true;
        }

        /// <summary>
        /// Adds the specified number of lives, capped at MAX_LIVES.
        /// </summary>
        public void AddLife(int count)
        {
            if (count <= 0)
            {
                return;
            }

            _currentLives = Mathf.Min(_currentLives + count, MAX_LIVES);
            SaveToPrefs();
            SyncToFirestore($"AddLife({count})");
            PublishLifeChanged();
        }

        /// <summary>
        /// Instantly refills all lives to maximum.
        /// </summary>
        public void RefillLives()
        {
            _currentLives = MAX_LIVES;
            _rechargeTimer = 0f;
            SaveToPrefs();
            SyncToFirestore("RefillLives");
            PublishLifeChanged();
        }

        /// <summary>
        /// Attempts to purchase a full life refill for COIN_REFILL_COST coins.
        /// Returns true if purchase succeeded. Requires CurrencyManager.
        /// </summary>
        public bool PurchaseRefillWithCoins()
        {
            if (!CurrencyManager.HasInstance)
            {
                Debug.LogWarning("[LifeManager] CurrencyManager not available for coin refill.");
                return false;
            }

            if (!CurrencyManager.Instance.SpendCoins(COIN_REFILL_COST, CurrencyManager.CoinSink.HeartRefill))
            {
                Debug.Log("[LifeManager] Not enough coins for life refill.");
                return false;
            }

            RefillLives();
            return true;
        }

        /// <summary>
        /// Grants one life via a rewarded advertisement.
        /// </summary>
        public void GrantAdRewardLife()
        {
            AddLife(1);
        }

        /// <summary>
        /// Activates infinite hearts for the given duration in seconds.
        /// While active, UseLive() always succeeds without consuming lives.
        /// </summary>
        public void ActivateInfiniteHearts(float durationSeconds)
        {
            _infiniteHeartsEndTime = Time.realtimeSinceStartup + durationSeconds;
            _currentLives = MAX_LIVES;
            SaveToPrefs();
            SyncToFirestore($"InfiniteHearts({durationSeconds}s)");

            // Firestore infiniteHeartsUntil — 절대 시각(UTC)으로 저장 (cross-device 동기화)
            if (UserDataService.HasInstance && UserDataService.Instance.IsReady)
            {
                var until = Firebase.Firestore.Timestamp.FromDateTime(
                    DateTime.UtcNow.AddSeconds(durationSeconds));
                UserDataService.Instance.SetInfiniteHeartsUntil(until);
            }

            PublishLifeChanged();
            Debug.Log($"[LifeManager] Infinite hearts activated for {durationSeconds / 3600f:F1}h");
        }

        /// <summary>True while infinite hearts are active.</summary>
        public bool IsInfiniteHeartsActive => Time.realtimeSinceStartup < _infiniteHeartsEndTime;

        /// <summary>Remaining duration of infinite hearts in seconds. 0 if inactive.</summary>
        public float GetRemainingInfiniteSeconds()
        {
            if (!IsInfiniteHeartsActive) return 0f;
            return _infiniteHeartsEndTime - Time.realtimeSinceStartup;
        }

        /// <summary>
        /// Returns the time remaining until the next life recharges.
        /// Returns TimeSpan.Zero if lives are already at maximum.
        /// </summary>
        public TimeSpan GetTimeToNextLife()
        {
            if (_currentLives >= MAX_LIVES)
            {
                return TimeSpan.Zero;
            }

            DateTime lastRecharge = new DateTime(_lastRechargeUtcTicks, DateTimeKind.Utc);
            DateTime nextRecharge = lastRecharge.AddSeconds(RECHARGE_SECONDS);
            TimeSpan remaining    = nextRecharge - DateTime.UtcNow;

            return remaining.TotalSeconds > 0 ? remaining : TimeSpan.Zero;
        }

        #endregion

        #region Private Methods

        private void LoadFromPrefs()
        {
            _currentLives         = PlayerPrefs.GetInt(PREFS_CURRENT_LIVES, MAX_LIVES);
            _currentLives         = Mathf.Clamp(_currentLives, 0, MAX_LIVES);
            _lastRechargeUtcTicks = PlayerPrefs.HasKey(PREFS_LAST_RECHARGE_UTC)
                ? long.Parse(PlayerPrefs.GetString(PREFS_LAST_RECHARGE_UTC))
                : DateTime.UtcNow.Ticks;
        }

        private void SaveToPrefs()
        {
            PlayerPrefs.SetInt(PREFS_CURRENT_LIVES, _currentLives);
            PlayerPrefs.SetString(PREFS_LAST_RECHARGE_UTC, _lastRechargeUtcTicks.ToString());
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Firestore 동기화. UserDataService 미준비 시 무시 (offline cache로 PlayerPrefs).
        /// lives 와 nextLifeAt 둘 다 절대값으로 set (atomic increment 아님 — race condition 회피).
        /// </summary>
        private void SyncToFirestore(string reason)
        {
            if (!UserDataService.HasInstance || !UserDataService.Instance.IsReady) return;
            var svc = UserDataService.Instance;

            // lives는 직접 set
            svc.UpdateField("lives", _currentLives);

            // nextLifeAt = lastRecharge + 30분 (FULL이면 default(Timestamp) = unset)
            if (_currentLives < MAX_LIVES)
            {
                var lastRecharge = new DateTime(_lastRechargeUtcTicks, DateTimeKind.Utc);
                var nextLifeAt   = Firebase.Firestore.Timestamp.FromDateTime(
                    lastRecharge.AddSeconds(RECHARGE_SECONDS));
                svc.SetNextLifeAt(nextLifeAt);
            }
            else
            {
                svc.SetNextLifeAt(default);
            }
        }

        private void ProcessOfflineRecharge()
        {
            if (_currentLives >= MAX_LIVES)
            {
                return;
            }

            DateTime lastRecharge = new DateTime(_lastRechargeUtcTicks, DateTimeKind.Utc);
            double   elapsedSecs  = (DateTime.UtcNow - lastRecharge).TotalSeconds;

            if (elapsedSecs < RECHARGE_SECONDS)
            {
                // Prime the timer with elapsed time already accumulated
                _rechargeTimer = (float)elapsedSecs;
                return;
            }

            int livesEarned = (int)(elapsedSecs / RECHARGE_SECONDS);
            livesEarned     = Mathf.Min(livesEarned, MAX_LIVES - _currentLives);

            if (livesEarned > 0)
            {
                _currentLives = Mathf.Min(_currentLives + livesEarned, MAX_LIVES);
                double usedSecs = livesEarned * RECHARGE_SECONDS;
                _lastRechargeUtcTicks = lastRecharge.AddSeconds(usedSecs).Ticks;
                SaveToPrefs();
                PublishLifeChanged();
            }

            // Set timer for partial time toward next life
            DateTime updatedLast = new DateTime(_lastRechargeUtcTicks, DateTimeKind.Utc);
            _rechargeTimer = (float)(DateTime.UtcNow - updatedLast).TotalSeconds;
            _rechargeTimer = Mathf.Max(_rechargeTimer, 0f);
        }

        private void PublishLifeChanged()
        {
            EventBus.Publish(new OnLifeChanged
            {
                currentLives = _currentLives,
                maxLives     = MAX_LIVES
            });
        }

        private void HandleLevelFailed(OnLevelFailed evt)
        {
            // 하트 소모는 최종 실패 확정 시(PopupFail02)에서만 처리
            // 여기서 소모하면 이어하기 선택 시 이중 소모 BUG
        }

        #endregion
    }
}
