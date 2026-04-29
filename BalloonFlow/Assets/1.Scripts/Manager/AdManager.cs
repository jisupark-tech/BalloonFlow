using System;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// AppLovin MAX 기반 광고 매니저. Rewarded / Interstitial 노출 + Admob/FAN mediation.
    /// 이전 Admob-direct 구현을 MAX로 교체. 시그니처(ShowRewardedAd/ShowInterstitialAd 등)는
    /// 외부 호출자 영향 없도록 보존.
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Manager | Phase: 3
    /// 광고 Unit ID는 SdkConfig (SdkConfig.local.cs, .gitignore) 에서 주입.
    /// </remarks>
    public class AdManager : Singleton<AdManager>
    {
        #region Constants

        private const int    AD_PROTECTION_LEVEL_THRESHOLD = 20;
        private const int    INTERSTITIAL_FAIL_INTERVAL    = 3;
        private const int    MAX_RETRY_EXPONENT            = 6; // 2^6 = 64s
        private const string LOG_TAG                       = "[AdManager]";

        #endregion

        #region Public Events (MAX-native)

        public event Action                    OnRewardedAdLoaded;
        public event Action<string>            OnRewardedAdFailedToLoad;
        public event Action                    OnRewardedAdDisplayed;
        public event Action                    OnRewardedAdHidden;
        public event Action<MaxSdkBase.Reward> OnRewardedAdRewarded;
        public event Action<string>            OnRewardedAdFailedToShow;

        public event Action                    OnInterstitialAdLoaded;
        public event Action<string>            OnInterstitialAdFailedToLoad;
        public event Action                    OnInterstitialAdDisplayed;
        public event Action                    OnInterstitialAdHidden;

        #endregion

        #region Fields

        private bool   _isInitialized;
        private int    _rewardedRetryAttempt;
        private int    _interstitialRetryAttempt;
        private int    _failCount;
        private int    _currentLevel = 1;
        private bool   _isShowingAd;
        private Action _pendingRewardCallback;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            string sdkKey = SdkConfig.AppLovinSdkKey;
            if (string.IsNullOrEmpty(sdkKey))
            {
                Debug.LogWarning($"{LOG_TAG} AppLovin SDK Key is empty. Skipping init. (SdkConfig.local.cs 누락 가능성)");
                return;
            }

            MaxSdkCallbacks.OnSdkInitializedEvent += OnSdkInitialized;
            MaxSdk.SetSdkKey(sdkKey);
            MaxSdk.InitializeSdk();
            Debug.Log($"{LOG_TAG} AppLovin MAX initializing...");
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

        #region SDK Init

        private void OnSdkInitialized(MaxSdkBase.SdkConfiguration cfg)
        {
            _isInitialized = true;
            Debug.Log($"{LOG_TAG} MAX SDK initialized. consentDialogState={cfg.ConsentDialogState}");

            // Rewarded
            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent          += OnRewardedLoadedCb;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent      += OnRewardedLoadFailedCb;
            MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent       += OnRewardedDisplayedCb;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent          += OnRewardedHiddenCb;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent  += OnRewardedReceivedRewardCb;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent   += OnRewardedDisplayFailedCb;

            // Interstitial
            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent        += OnInterstitialLoadedCb;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent    += OnInterstitialLoadFailedCb;
            MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent     += OnInterstitialDisplayedCb;
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent        += OnInterstitialHiddenCb;
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += OnInterstitialDisplayFailedCb;

            LoadRewardedAd();
            LoadInterstitialAd();
        }

        #endregion

        #region Public API — Compatibility (Action-based, 기존 시그니처 유지)

        /// <summary>
        /// Rewarded 광고 표시. 보상 시점에 callback 실행.
        /// Lv20 미만은 기본적으로 ad protection 적용. outgame UI(Lives 충전 등)에서는 ignoreAdProtection=true.
        /// </summary>
        public void ShowRewardedAd(Action rewardCallback, bool ignoreAdProtection = false)
        {
            if (!ignoreAdProtection && GetAdProtectionLevel() < AD_PROTECTION_LEVEL_THRESHOLD)
            {
                Debug.Log($"{LOG_TAG} Ad protection active — skipping rewarded ad.");
                return;
            }
            if (!IsRewardedAdReady())
            {
                Debug.LogWarning($"{LOG_TAG} Rewarded not ready.");
                return;
            }
            if (_isShowingAd) return;

            _pendingRewardCallback = rewardCallback;
            _isShowingAd = true;
            MaxSdk.ShowRewardedAd(SdkConfig.AppLovinRewardedAdUnitId);
        }

        /// <summary>Interstitial 광고 표시. 게임 중에는 무시. Lv20 미만 ad protection.</summary>
        public void ShowInterstitialAd()
        {
            if (GetAdProtectionLevel() < AD_PROTECTION_LEVEL_THRESHOLD) return;
            if (!IsInterstitialAdReady()) return;
            if (_isShowingAd) return;

            if (BoardStateManager.HasInstance &&
                BoardStateManager.Instance.GetBoardState() == BoardState.Playing)
            {
                Debug.LogWarning($"{LOG_TAG} Cannot show interstitial during gameplay.");
                return;
            }

            _isShowingAd = true;
            MaxSdk.ShowInterstitial(SdkConfig.AppLovinInterstitialAdUnitId);
        }

        public bool IsRewardedAdReady() =>
            _isInitialized
            && !string.IsNullOrEmpty(SdkConfig.AppLovinRewardedAdUnitId)
            && MaxSdk.IsRewardedAdReady(SdkConfig.AppLovinRewardedAdUnitId);

        public bool IsInterstitialAdReady() =>
            _isInitialized
            && !string.IsNullOrEmpty(SdkConfig.AppLovinInterstitialAdUnitId)
            && MaxSdk.IsInterstitialReady(SdkConfig.AppLovinInterstitialAdUnitId);

        public int GetAdProtectionLevel() => _currentLevel;

        #endregion

        #region Ad Loading

        private void LoadRewardedAd()
        {
            if (!_isInitialized) return;
            if (string.IsNullOrEmpty(SdkConfig.AppLovinRewardedAdUnitId))
            {
                Debug.LogWarning($"{LOG_TAG} Rewarded Ad Unit ID is empty.");
                return;
            }
            MaxSdk.LoadRewardedAd(SdkConfig.AppLovinRewardedAdUnitId);
        }

        private void LoadInterstitialAd()
        {
            if (!_isInitialized) return;
            if (string.IsNullOrEmpty(SdkConfig.AppLovinInterstitialAdUnitId))
            {
                // Interstitial Ad Unit 미설정 — Rewarded만 사용 가능
                return;
            }
            MaxSdk.LoadInterstitial(SdkConfig.AppLovinInterstitialAdUnitId);
        }

        #endregion

        #region Rewarded Callbacks

        private void OnRewardedLoadedCb(string adUnitId, MaxSdkBase.AdInfo info)
        {
            _rewardedRetryAttempt = 0;
            OnRewardedAdLoaded?.Invoke();
        }

        private void OnRewardedLoadFailedCb(string adUnitId, MaxSdkBase.ErrorInfo error)
        {
            _rewardedRetryAttempt++;
            float retryDelay = (float)Math.Pow(2, Math.Min(MAX_RETRY_EXPONENT, _rewardedRetryAttempt));
            Invoke(nameof(LoadRewardedAd), retryDelay);
            OnRewardedAdFailedToLoad?.Invoke(error.Message);
        }

        private void OnRewardedDisplayedCb(string adUnitId, MaxSdkBase.AdInfo info)
            => OnRewardedAdDisplayed?.Invoke();

        private void OnRewardedHiddenCb(string adUnitId, MaxSdkBase.AdInfo info)
        {
            _isShowingAd = false;
            _pendingRewardCallback = null;
            OnRewardedAdHidden?.Invoke();
            LoadRewardedAd();
        }

        private void OnRewardedReceivedRewardCb(string adUnitId, MaxSdkBase.Reward reward, MaxSdkBase.AdInfo info)
        {
            _pendingRewardCallback?.Invoke();
            _pendingRewardCallback = null;
            OnRewardedAdRewarded?.Invoke(reward);
        }

        private void OnRewardedDisplayFailedCb(string adUnitId, MaxSdkBase.ErrorInfo error, MaxSdkBase.AdInfo info)
        {
            _isShowingAd = false;
            _pendingRewardCallback = null;
            OnRewardedAdFailedToShow?.Invoke(error.Message);
            LoadRewardedAd();
        }

        #endregion

        #region Interstitial Callbacks

        private void OnInterstitialLoadedCb(string adUnitId, MaxSdkBase.AdInfo info)
        {
            _interstitialRetryAttempt = 0;
            OnInterstitialAdLoaded?.Invoke();
        }

        private void OnInterstitialLoadFailedCb(string adUnitId, MaxSdkBase.ErrorInfo error)
        {
            _interstitialRetryAttempt++;
            float retryDelay = (float)Math.Pow(2, Math.Min(MAX_RETRY_EXPONENT, _interstitialRetryAttempt));
            Invoke(nameof(LoadInterstitialAd), retryDelay);
            OnInterstitialAdFailedToLoad?.Invoke(error.Message);
        }

        private void OnInterstitialDisplayedCb(string adUnitId, MaxSdkBase.AdInfo info)
            => OnInterstitialAdDisplayed?.Invoke();

        private void OnInterstitialHiddenCb(string adUnitId, MaxSdkBase.AdInfo info)
        {
            _isShowingAd = false;
            OnInterstitialAdHidden?.Invoke();
            LoadInterstitialAd();
        }

        private void OnInterstitialDisplayFailedCb(string adUnitId, MaxSdkBase.ErrorInfo error, MaxSdkBase.AdInfo info)
        {
            _isShowingAd = false;
            LoadInterstitialAd();
        }

        #endregion

        #region Event Handlers

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            _currentLevel = evt.levelId;
            _failCount    = 0;

            if (!IsRewardedAdReady())     LoadRewardedAd();
            if (!IsInterstitialAdReady()) LoadInterstitialAd();
        }

        private void HandleLevelFailed(OnLevelFailed evt)
        {
            _failCount++;
            if (_failCount % INTERSTITIAL_FAIL_INTERVAL == 0)
            {
                ShowInterstitialAd();
            }
        }

        #endregion
    }
}
