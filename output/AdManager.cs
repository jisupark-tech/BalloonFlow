#if GOOGLE_MOBILE_ADS
using GoogleMobileAds.Api;
#endif

using System;
using System.Collections;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Manages rewarded and interstitial ad delivery.
    /// Uses Google AdMob when GOOGLE_MOBILE_ADS is defined; otherwise simulates ad flow.
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Manager | Phase: 3
    /// domain_owner: BM (placed in Domain layer for dependency proximity to OutGame)
    /// DB Reference: No DB match for puzzle ad manager — generated from L3 YAML logicFlow
    /// </remarks>
    public class AdManager : Singleton<AdManager>
    {
        #region Constants

        private const int AdProtectionLevelThreshold = 20;
        private const int InterstitialFailInterval    = 3;
        private const float SimulatedAdDurationSeconds = 1f;

        #endregion

        #region Serialized Fields

        [SerializeField] private string _rewardedAdUnitId    = "ca-app-pub-3940256099942544/5224354917"; // test ID
        [SerializeField] private string _interstitialAdUnitId = "ca-app-pub-3940256099942544/1033173712"; // test ID

        #endregion

        #region Fields

        private int  _failCount;
        private int  _currentLevel;
        private bool _isRewardedAdReady;
        private bool _isInterstitialAdReady;
        private bool _isShowingAd;

#if GOOGLE_MOBILE_ADS
        private RewardedAd      _rewardedAd;
        private InterstitialAd  _interstitialAd;
#endif

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            _failCount             = 0;
            _currentLevel          = 1;
            _isRewardedAdReady     = false;
            _isInterstitialAdReady = false;
            _isShowingAd           = false;

#if GOOGLE_MOBILE_ADS
            MobileAds.Initialize(_ => LoadRewardedAd());
#else
            // Simulation: treat ads as always ready after init.
            _isRewardedAdReady     = true;
            _isInterstitialAdReady = true;
#endif
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Subscribe<OnLevelFailed>(HandleLevelFailed);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnLevelFailed>(HandleLevelFailed);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Shows a rewarded ad. On success, invokes <paramref name="rewardCallback"/>.
        /// Does nothing if ad protection is active (below Level 20) or no ad is ready.
        /// </summary>
        /// <param name="rewardCallback">Invoked once reward is earned.</param>
        public void ShowRewardedAd(Action rewardCallback)
        {
            if (GetAdProtectionLevel() < AdProtectionLevelThreshold)
            {
                Debug.Log("[AdManager] Ad protection active — skipping rewarded ad.");
                return;
            }

            if (!IsRewardedAdReady())
            {
                Debug.LogWarning("[AdManager] Rewarded ad not ready.");
                return;
            }

            if (_isShowingAd) return;

#if GOOGLE_MOBILE_ADS
            if (_rewardedAd == null) return;
            _isShowingAd = true;

            _rewardedAd.Show(reward =>
            {
                _isRewardedAdReady = false;
                _isShowingAd       = false;
                rewardCallback?.Invoke();
                LoadRewardedAd();
            });
#else
            StartCoroutine(SimulateRewardedAd(rewardCallback));
#endif
        }

        /// <summary>
        /// Shows an interstitial ad. Enforces: not during gameplay, every 3rd fail, Level &gt;= 20.
        /// </summary>
        public void ShowInterstitialAd()
        {
            if (GetAdProtectionLevel() < AdProtectionLevelThreshold) return;
            if (!IsInterstitialAdReady()) return;
            if (_isShowingAd) return;

            // Interstitial only after gameplay, not mid-session
            if (BoardStateManager.HasInstance &&
                BoardStateManager.Instance.GetBoardState() == BoardState.Playing)
            {
                Debug.LogWarning("[AdManager] Cannot show interstitial during gameplay.");
                return;
            }

#if GOOGLE_MOBILE_ADS
            if (_interstitialAd == null) return;
            _isShowingAd = true;

            _interstitialAd.Show();
            _isInterstitialAdReady = false;
            _isShowingAd           = false;
            _interstitialAd.Destroy();
            LoadInterstitialAd();
#else
            Debug.Log("[AdManager][Sim] Interstitial ad shown.");
            _isInterstitialAdReady = false;
            StartCoroutine(RearmInterstitialAfterDelay());
#endif
        }

        /// <summary>Returns true if a rewarded ad is loaded and ready to show.</summary>
        public bool IsRewardedAdReady()
        {
#if GOOGLE_MOBILE_ADS
            return _rewardedAd != null && _rewardedAd.CanShowAd();
#else
            return _isRewardedAdReady;
#endif
        }

        /// <summary>Returns true if an interstitial ad is loaded and ready to show.</summary>
        public bool IsInterstitialAdReady()
        {
#if GOOGLE_MOBILE_ADS
            return _interstitialAd != null && _interstitialAd.CanShowAd();
#else
            return _isInterstitialAdReady;
#endif
        }

        /// <summary>
        /// Returns the current level used for ad-protection evaluation.
        /// Ad protection: no ads below Level 20.
        /// </summary>
        public int GetAdProtectionLevel()
        {
            return _currentLevel;
        }

        #endregion

        #region Private Methods — Ad Loading

#if GOOGLE_MOBILE_ADS
        private void LoadRewardedAd()
        {
            _rewardedAd?.Destroy();
            _rewardedAd = null;
            _isRewardedAdReady = false;

            var adRequest = new AdRequest();
            RewardedAd.Load(_rewardedAdUnitId, adRequest, (ad, error) =>
            {
                if (error != null)
                {
                    Debug.LogWarning($"[AdManager] Rewarded load failed: {error.GetMessage()}");
                    return;
                }
                _rewardedAd        = ad;
                _isRewardedAdReady = true;
                Debug.Log("[AdManager] Rewarded ad loaded.");
            });
        }

        private void LoadInterstitialAd()
        {
            _interstitialAd?.Destroy();
            _interstitialAd        = null;
            _isInterstitialAdReady = false;

            var adRequest = new AdRequest();
            InterstitialAd.Load(_interstitialAdUnitId, adRequest, (ad, error) =>
            {
                if (error != null)
                {
                    Debug.LogWarning($"[AdManager] Interstitial load failed: {error.GetMessage()}");
                    return;
                }
                _interstitialAd        = ad;
                _isInterstitialAdReady = true;
                Debug.Log("[AdManager] Interstitial ad loaded.");
            });
        }
#endif

        #endregion

        #region Private Methods — Simulation

#if !GOOGLE_MOBILE_ADS
        private IEnumerator SimulateRewardedAd(Action rewardCallback)
        {
            _isShowingAd       = true;
            _isRewardedAdReady = false;
            Debug.Log("[AdManager][Sim] Rewarded ad started.");
            yield return new WaitForSeconds(SimulatedAdDurationSeconds);
            _isShowingAd = false;
            Debug.Log("[AdManager][Sim] Rewarded ad complete — reward granted.");
            rewardCallback?.Invoke();
            // Re-arm for next use.
            _isRewardedAdReady = true;
        }

        private IEnumerator RearmInterstitialAfterDelay()
        {
            yield return new WaitForSeconds(SimulatedAdDurationSeconds);
            _isInterstitialAdReady = true;
        }
#endif

        #endregion

        #region Private Methods — Event Handlers

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            _currentLevel = evt.levelId;
            _failCount    = 0;

#if GOOGLE_MOBILE_ADS
            // Pre-load ads for the new level.
            if (_rewardedAd == null) LoadRewardedAd();
            if (_interstitialAd == null) LoadInterstitialAd();
#endif
        }

        private void HandleLevelFailed(OnLevelFailed evt)
        {
            _failCount++;

            // Show interstitial every 3rd fail (but not during level — board is failed state).
            if (_failCount % InterstitialFailInterval == 0)
            {
                ShowInterstitialAd();
            }
        }

        #endregion
    }
}
