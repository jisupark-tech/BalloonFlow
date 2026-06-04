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
        public const int     COIN_REFILL_COST        = 900;
        private const string PREFS_CURRENT_LIVES     = "BF_CurrentLives";
        private const string PREFS_LAST_RECHARGE_UTC = "BF_LastRechargeUtc";

        #endregion

        #region Fields

        private int  _currentLives;
        private long _lastRechargeUtcTicks;   // stored as long ticks for precision
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
            TrySubscribeUserData();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelFailed>(HandleLevelFailed);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelFailed>(HandleLevelFailed);
            if (UserDataService.HasInstance)
                UserDataService.Instance.OnUserDataReady -= ApplyInfiniteHeartsFromUserData;
        }

        /// <summary>
        /// UserDataService 가 ready 되면 Firestore.infiniteHeartsUntil 을 잔여시간에 반영.
        /// 이미 ready 면 즉시 적용. cross-device 동기화용.
        /// </summary>
        private void TrySubscribeUserData()
        {
            if (!UserDataService.HasInstance) return;

            UserDataService.Instance.OnUserDataReady += ApplyInfiniteHeartsFromUserData;
            if (UserDataService.Instance.IsReady)
                ApplyInfiniteHeartsFromUserData();
        }

        private void ApplyInfiniteHeartsFromUserData()
        {
            if (!UserDataService.HasInstance || !UserDataService.Instance.IsReady) return;
            var user = UserDataService.Instance.CurrentUser;
            if (user == null) return;

            // Firestore Unity SDK 13.10.0 의 Timestamp 는 Seconds 프로퍼티 미공개 — ToDateTime() 으로 비교.
            // sentinel(default) = epoch(1970), 만료 = 과거 시각. 둘 다 remaining <= 0 으로 처리.
            var until = user.infiniteHeartsUntil.ToDateTime();
            double remaining = (until - DateTime.UtcNow).TotalSeconds;
            if (remaining <= 0) return;

            _infiniteHeartsEndTime = Time.realtimeSinceStartup + (float)remaining;
            _currentLives = MAX_LIVES;
            SaveToPrefs();
            PublishLifeChanged();
            Debug.Log($"[LifeManager] Firestore infiniteHeartsUntil 적용 — 잔여 {remaining / 3600:F1}h");
        }

        /// <summary>
        /// Periodic recharge check. Only runs while lives are missing.
        /// </summary>
        private void Update()
        {
            if (_currentLives >= MAX_LIVES) return;

            // 단일 시계(wall-clock) 기준 — UI 타이머(GetTimeToNextLife)와 동일 소스로 통일.
            // 기존 _rechargeTimer(Time.deltaTime)는 일시정지/timeScale/백그라운드에서 wall-clock 과 어긋나
            // "타이머는 0(미표시)인데 충전은 안 되는" 상태를 유발했음.
            DateTime lastRecharge = new DateTime(_lastRechargeUtcTicks, DateTimeKind.Utc);
            double elapsed = (DateTime.UtcNow - lastRecharge).TotalSeconds;
            if (elapsed < RECHARGE_SECONDS) return;

            int earned = Mathf.Min((int)(elapsed / RECHARGE_SECONDS), MAX_LIVES - _currentLives);
            if (earned <= 0) return;

            _currentLives += earned;
            _lastRechargeUtcTicks = lastRecharge.AddSeconds((double)earned * RECHARGE_SECONDS).Ticks;
            SaveToPrefs();
            SyncToFirestore("Recharge");
            PublishLifeChanged();
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
                Debug.Log($"[LifeManager] Not enough coins for life refill. have={CurrencyManager.Instance.Coins}, need={COIN_REFILL_COST}");
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
        /// [2026-05-19] 기존 잔여 시간에 누적 가산 — 기존 2h + 12h = 14h (overwrite X).
        /// </summary>
        public void ActivateInfiniteHearts(float durationSeconds)
        {
            // 기존 활성 상태면 잔여 시간 + 신규 duration. 비활성이면 now + duration.
            float now = Time.realtimeSinceStartup;
            float baseTime = (_infiniteHeartsEndTime > now) ? _infiniteHeartsEndTime : now;
            _infiniteHeartsEndTime = baseTime + durationSeconds;

            _currentLives = MAX_LIVES;
            SaveToPrefs();
            SyncToFirestore($"InfiniteHearts(+{durationSeconds}s)");

            // Firestore infiniteHeartsUntil — 절대 시각(UTC) 으로 저장. 누적 가산 동일 적용.
            if (UserDataService.HasInstance && UserDataService.Instance.IsReady)
            {
                DateTime baseUtc;
                var user = UserDataService.Instance.CurrentUser;
                if (user != null && user.infiniteHeartsUntil != null)
                {
                    DateTime existingUntil = user.infiniteHeartsUntil.ToDateTime();
                    baseUtc = existingUntil > DateTime.UtcNow ? existingUntil : DateTime.UtcNow;
                }
                else
                {
                    baseUtc = DateTime.UtcNow;
                }
                var until = Firebase.Firestore.Timestamp.FromDateTime(baseUtc.AddSeconds(durationSeconds));
                UserDataService.Instance.SetInfiniteHeartsUntil(until);
            }

            PublishLifeChanged();
            float totalRemaining = _infiniteHeartsEndTime - now;
            Debug.Log($"[LifeManager] Infinite hearts +{durationSeconds / 3600f:F1}h → total remaining {totalRemaining / 3600f:F1}h");
        }

        /// <summary>Resets local lives and clears any active infinite-hearts state.</summary>
        public void ResetToInitial()
        {
            _currentLives = MAX_LIVES;
            _lastRechargeUtcTicks = DateTime.UtcNow.Ticks;
            _infiniteHeartsEndTime = 0f;

            SaveToPrefs();

            if (UserDataService.HasInstance && UserDataService.Instance.IsReady)
            {
                SyncToFirestore("ResetToInitial");
                UserDataService.Instance.SetInfiniteHeartsUntil(default(Firebase.Firestore.Timestamp));
            }

            PublishLifeChanged();
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

        /// <summary>
        /// 모든 하트가 풀충전될 예상 UTC 시각. 이미 풀이면 DateTime.MinValue 반환.
        /// 로컬 푸시 알림(#1) 스케줄링 용도.
        /// </summary>
        public DateTime PredictFullLivesUtc()
        {
            if (_currentLives >= MAX_LIVES) return DateTime.MinValue;
            int missing = MAX_LIVES - _currentLives;
            DateTime lastRecharge = new DateTime(_lastRechargeUtcTicks, DateTimeKind.Utc);
            return lastRecharge.AddSeconds((double)RECHARGE_SECONDS * missing);
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
            if (_currentLives >= MAX_LIVES) return;

            // wall-clock 기준 — Update 와 동일 로직. 콜드 스타트 시 즉시 catch-up.
            DateTime lastRecharge = new DateTime(_lastRechargeUtcTicks, DateTimeKind.Utc);
            double   elapsedSecs  = (DateTime.UtcNow - lastRecharge).TotalSeconds;
            if (elapsedSecs < RECHARGE_SECONDS) return;

            int livesEarned = Mathf.Min((int)(elapsedSecs / RECHARGE_SECONDS), MAX_LIVES - _currentLives);
            if (livesEarned > 0)
            {
                _currentLives += livesEarned;
                _lastRechargeUtcTicks = lastRecharge.AddSeconds((double)livesEarned * RECHARGE_SECONDS).Ticks;
                SaveToPrefs();
                PublishLifeChanged();
            }
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
