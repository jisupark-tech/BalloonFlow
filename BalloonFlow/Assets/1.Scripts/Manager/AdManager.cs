using System;
using UnityEngine;
using BalloonFlow.Analytics;

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
        // 전면 광고 공통 쿨다운 (UX플로우 §6-1·§7-2·§595 "모든 지면 공통 20초").
        private const float  INTERSTITIAL_COOLDOWN_SECONDS = 20f;
        private const int    MAX_RETRY_EXPONENT            = 6; // 2^6 = 64s
        private const string LOG_TAG                       = "[AdManager]";

        #endregion

        #region Types

        /// <summary>
        /// 전면 광고 지면 (UX플로우 §6-1·§7-1, v1.2.30/31 Yarn Loop 방식).
        /// 1.0 메인 지면 2종만 — Try Again·실패횟수 트리거는 폐기됨.
        /// </summary>
        public enum InterstitialPlacement
        {
            ClearNext, // Clear → Next 직후 (interstitial_clear_next)
            FailQuit   // ③ Level Failed 나가기 → 로비 직전 (interstitial_fail_quit)
        }

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

        /// <summary>ROLLBACK_NOADS_AUTO_POPUP_20260619: 첫 전면광고 종료 직후 발화(문서 §9-0 A경로). 1회성.</summary>
        public event Action                    OnFirstInterstitialEnded;

        #endregion

        #region Fields

        private bool   _isInitialized;
        private int    _rewardedRetryAttempt;
        private int    _interstitialRetryAttempt;
        private int    _currentLevel = 1;
        private bool   _isShowingAd;
        private Action _pendingRewardCallback;
        // 마지막 전면 광고 노출 종료 시각 (realtime). 20초 공통 쿨다운 판정용.
        private float  _lastInterstitialShownRealtime = -9999f;
        // ROLLBACK_NOADS_AUTO_POPUP_20260619: 첫 전면광고 NO ADS 자동팝업(A경로).
        //   _firstInterstitialJustShown: 현재 노출이 '최초'였는지(Displayed 에서 set 전 상태 캡처).
        //   _pendingFirstInterstitialNoAds: 종료 후 로비에서 1회 소비 대기(인게임 한복판 노출 방지).
        private bool   _firstInterstitialJustShown;
        private bool   _pendingFirstInterstitialNoAds;
        // ROLLBACK_AD_EVENT_FIELDS_20260707: ad_event placement/watch_duration_sec 계측.
        //   revenue 콜백(OnAdRevenuePaid)은 노출 '시작' 직후라 시청 시간을 알 수 없음 → 임프레션 정보를
        //   보류했다가 노출 종료(Hidden/DisplayFailed)에 emit 해 watch_duration_sec 를 채운다.
        //   (광고 도중 앱 강제종료 시 해당 임프레션 row 1건 유실 수용. ad_duration_sec 는 MAX SDK 가
        //   크리에이티브 길이를 제공하지 않아 미emit=NULL 유지.)
        private float             _adDisplayStartRealtime = -1f;
        private string            _pendingAdEventUnitId;
        private MaxSdkBase.AdInfo _pendingAdEventInfo;

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
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
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

            // [#17] impression-level 수익 → ad_event 발행. MAX 는 포맷별 중첩 콜백이라 사용 포맷(인터스티셜/보상형)에 각각 구독.
            MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += OnAdRevenuePaidCb;
            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent     += OnAdRevenuePaidCb;

            LoadRewardedAd();
            LoadInterstitialAd();
        }

        #endregion

        #region Public API — Compatibility (Action-based, 기존 시그니처 유지)

        /// <summary>
        /// Rewarded 광고 표시. 보상 시점에 callback 실행.
        /// Lv20 미만은 기본적으로 ad protection 적용. outgame UI(Lives 충전 등)에서는 ignoreAdProtection=true.
        /// </summary>
        /// <param name="placement">ad_event.ad_placement 규약 문자열 (morelives_popup / continue_popup 등).
        ///   MaxSdk.ShowRewardedAd 의 placement 로 전달되어 revenue 콜백의 AdInfo.Placement 로 회수된다.</param>
        public void ShowRewardedAd(Action rewardCallback, bool ignoreAdProtection = false, string placement = null)
        {
            // [2026-05-13] 광고 제거 구매 유저는 보상형 포함 모든 광고 차단 (정책 결정).
            // 보상은 즉시 callback 으로 지급 — 광고 시청 없이 결과만 부여.
            if (IAPManager.HasInstance && IAPManager.Instance.AdsRemoved)
            {
                Debug.Log($"{LOG_TAG} AdsRemoved=true — skipping rewarded ad, granting reward directly.");
                rewardCallback?.Invoke();
                return;
            }
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
            // ROLLBACK_AD_EVENT_FIELDS_20260707: placement 전달 — AdInfo.Placement 로 ad_event 에 적재.
            MaxSdk.ShowRewardedAd(SdkConfig.AppLovinRewardedAdUnitId, placement);
        }

        /// <summary>
        /// 명세 기반 전면 광고 노출 시도 (UX플로우 §6-1·§7-1, v1.2.30/31 Yarn Loop).
        /// 두 지면(Clear→Next, ③ 실패 나가기)에서만 호출. 다음 조건 모두 충족 시에만 노출:
        ///   - 광고 제거 미구매
        ///   - Lv.20 <b>클리어</b> 후 (highest cleared ≥ 20) — FTUE 보호
        ///   - 마지막 노출로부터 20초 경과 (공통 쿨다운)
        ///   - 광고 로드 완료 + 표시 중 아님 + 인게임 플레이 중 아님
        /// </summary>
        /// <returns>실제로 표시를 트리거했으면 true (호출 측이 광고 후 동선을 분기할 필요는 없음 — 광고는 오버레이).</returns>
        public bool TryShowInterstitial(InterstitialPlacement placement)
        {
            // [2026-05-13] 광고 제거 구매 유저 — interstitial 차단.
            if (IAPManager.HasInstance && IAPManager.Instance.AdsRemoved)
            {
                Debug.Log($"{LOG_TAG} AdsRemoved=true — skipping interstitial ({placement}).");
                return false;
            }
            // Lv.20 클리어 후 해금 (현재 진입 레벨이 아닌 최고 클리어 레벨 기준).
            if (GetHighestClearedLevel() < AD_PROTECTION_LEVEL_THRESHOLD) return false;
            // 공통 20초 쿨다운.
            if (Time.realtimeSinceStartup - _lastInterstitialShownRealtime < INTERSTITIAL_COOLDOWN_SECONDS) return false;
            if (_isShowingAd) return false;
            if (!IsInterstitialAdReady())
            {
                LoadInterstitialAd();
                return false;
            }
            if (BoardStateManager.HasInstance &&
                BoardStateManager.Instance.GetBoardState() == BoardState.Playing)
            {
                Debug.LogWarning($"{LOG_TAG} Cannot show interstitial during gameplay ({placement}).");
                return false;
            }

            _isShowingAd = true;
            Debug.Log($"{LOG_TAG} Showing interstitial — placement={placement}");
            // ROLLBACK_AD_EVENT_FIELDS_20260707: placement 전달 — AdInfo.Placement 로 ad_event 에 적재.
            MaxSdk.ShowInterstitial(SdkConfig.AppLovinInterstitialAdUnitId, ResolvePlacementName(placement));
            return true;
        }

        /// <summary>InterstitialPlacement → ad_event.ad_placement 규약 문자열 (스키마 v3.2 3도메인 placement).</summary>
        private static string ResolvePlacementName(InterstitialPlacement placement)
        {
            switch (placement)
            {
                case InterstitialPlacement.ClearNext: return "interstitial_between_level";
                case InterstitialPlacement.FailQuit:  return "interstitial_fail_quit";
                default:                              return placement.ToString();
            }
        }

        /// <summary>최고 클리어 레벨 (Lv.20 클리어 해금 판정용). LevelManager 미존재 시 0.</summary>
        private int GetHighestClearedLevel()
            => LevelManager.HasInstance ? LevelManager.Instance.GetHighestCompletedLevel() : 0;

        public bool IsRewardedAdReady() =>
            _isInitialized
            && !string.IsNullOrEmpty(SdkConfig.AppLovinRewardedAdUnitId)
            && MaxSdk.IsRewardedAdReady(SdkConfig.AppLovinRewardedAdUnitId);

        public bool IsInterstitialAdReady() =>
            _isInitialized
            && !string.IsNullOrEmpty(SdkConfig.AppLovinInterstitialAdUnitId)
            && MaxSdk.IsInterstitialReady(SdkConfig.AppLovinInterstitialAdUnitId);

        public int GetAdProtectionLevel() => _currentLevel;

        /// <summary>전면/보상형 광고가 현재 표시 중인지. 백버튼은 광고 중 SDK 가 처리하므로 라우터가 무시.</summary>
        public bool IsShowingAd => _isShowingAd;

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
        {
            _adDisplayStartRealtime = Time.realtimeSinceStartup; // ROLLBACK_AD_EVENT_FIELDS_20260707
            OnRewardedAdDisplayed?.Invoke();
        }

        private void OnRewardedHiddenCb(string adUnitId, MaxSdkBase.AdInfo info)
        {
            _isShowingAd = false;
            _pendingRewardCallback = null;
            FlushPendingAdEvent(); // ROLLBACK_AD_EVENT_FIELDS_20260707: 노출 종료 — watch_duration 확정 후 emit
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
            FlushPendingAdEvent(); // ROLLBACK_AD_EVENT_FIELDS_20260707
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
        {
            _adDisplayStartRealtime = Time.realtimeSinceStartup; // ROLLBACK_AD_EVENT_FIELDS_20260707
            // ROLLBACK_NOADS_AUTO_POPUP_20260619: SetFirstInterstitialShown 전에 '최초 노출' 여부 캡처
            //   (set 후엔 항상 true 라 종료 시점에 최초인지 구분 불가).
            _firstInterstitialJustShown = UserDataService.HasInstance
                && UserDataService.Instance.CurrentUser != null
                && !UserDataService.Instance.CurrentUser.firstInterstitialShown;

            if (UserDataService.HasInstance)
                UserDataService.Instance.SetFirstInterstitialShown(true);
            OnInterstitialAdDisplayed?.Invoke();
        }

        private void OnInterstitialHiddenCb(string adUnitId, MaxSdkBase.AdInfo info)
        {
            _isShowingAd = false;
            FlushPendingAdEvent(); // ROLLBACK_AD_EVENT_FIELDS_20260707: 노출 종료 — watch_duration 확정 후 emit
            // 노출 종료 시점에 쿨다운 타이머 시작 (명세: "show() end → last_shown_at = now → 20s cooldown").
            _lastInterstitialShownRealtime = Time.realtimeSinceStartup;

            // ROLLBACK_NOADS_AUTO_POPUP_20260619: 첫 전면광고 종료 직후 NO ADS 팝업 1회 자동(문서 §9-0 A경로).
            //   pending 으로 표시 → 로비 진입 시 UILobby 가 소비해 PopupNoAds 1회 노출
            //   (ClearNext 직후 다음 레벨 인게임 한복판 노출 방지, PopupNoAds 는 로비 컨텍스트 팝업).
            if (_firstInterstitialJustShown)
            {
                _firstInterstitialJustShown = false;
                _pendingFirstInterstitialNoAds = true;
                OnFirstInterstitialEnded?.Invoke();
            }

            OnInterstitialAdHidden?.Invoke();
            LoadInterstitialAd();
        }

        /// <summary>ROLLBACK_NOADS_AUTO_POPUP_20260619: 첫 전면광고 후 NO ADS 자동팝업 대기 중인지(비소모 조회).</summary>
        public bool HasPendingFirstInterstitialNoAds => _pendingFirstInterstitialNoAds;

        /// <summary>ROLLBACK_NOADS_AUTO_POPUP_20260619: 첫 전면광고 후 NO ADS 자동팝업 대기 중이면 true 반환+소비(1회).
        ///   로비 진입 시 호출 — A경로(noads_popup). 광고 제거 구매 유저는 대기 자체가 안 생기지만 방어적으로 차단.</summary>
        public bool ConsumePendingFirstInterstitialNoAds()
        {
            if (!_pendingFirstInterstitialNoAds) return false;
            if (IAPManager.HasInstance && IAPManager.Instance.AdsRemoved) { _pendingFirstInterstitialNoAds = false; return false; }
            _pendingFirstInterstitialNoAds = false;
            return true;
        }

        private void OnInterstitialDisplayFailedCb(string adUnitId, MaxSdkBase.ErrorInfo error, MaxSdkBase.AdInfo info)
        {
            _isShowingAd = false;
            FlushPendingAdEvent(); // ROLLBACK_AD_EVENT_FIELDS_20260707
            LoadInterstitialAd();
        }

        #endregion

        #region Ad Revenue → ad_event (#17)

        /// <summary>MAX impression-level 수익 콜백 — 누적 ad revenue 갱신 + ad_event 발행 (BigQuery 광고 분석).</summary>
        private void OnAdRevenuePaidCb(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (adInfo == null) return;
            // 누적 광고 수익 먼저 갱신 → 이벤트의 total_ad_revenue_usd 가 post-impression 반영.
            if (UserSnapshotCache.HasInstance)
                UserSnapshotCache.Instance.OnAdRevenueGranted(adInfo.Revenue);
            // ROLLBACK_AD_EVENT_FIELDS_20260707: 즉시 emit → 노출 종료 시 emit 으로 이연 (watch_duration_sec 확보).
            //   같은 노출에서 revenue 콜백이 중복되면 마지막 값으로 덮어 단일 row 유지.
            _pendingAdEventUnitId = adUnitId;
            _pendingAdEventInfo   = adInfo;
        }

        /// <summary>ROLLBACK_AD_EVENT_FIELDS_20260707: 보류된 임프레션을 노출 종료 시점에 emit (시청 시간 포함).</summary>
        private void FlushPendingAdEvent()
        {
            if (_pendingAdEventInfo == null) { _adDisplayStartRealtime = -1f; return; }
            int watchSec = _adDisplayStartRealtime >= 0f
                ? Mathf.Max(0, Mathf.RoundToInt(Time.realtimeSinceStartup - _adDisplayStartRealtime))
                : 0;
            EmitAdEvent(_pendingAdEventUnitId, _pendingAdEventInfo, watchSec);
            _pendingAdEventUnitId   = null;
            _pendingAdEventInfo     = null;
            _adDisplayStartRealtime = -1f;
        }

        private static void EmitAdEvent(string adUnitId, MaxSdkBase.AdInfo adInfo, int watchDurationSec)
        {
            string eventId = System.Guid.NewGuid().ToString("N");
            var p = new System.Collections.Generic.Dictionary<string, object>(32);
            p[AnalyticsConsts.P_EVENT_ID]       = eventId;
            p[AnalyticsConsts.P_SESSION_ID]     = AnalyticsSessionTracker.HasInstance
                ? AnalyticsSessionTracker.Instance.CurrentSessionId : "";
            p[AnalyticsConsts.P_GAME_ID]        = AnalyticsConsts.GAME_ID;
            p[AnalyticsConsts.P_UID]            = AnalyticsSessionTracker.ResolveUid();
            p[AnalyticsConsts.P_EVENT_TS]       = System.DateTime.UtcNow.ToString("o");
            p[AnalyticsConsts.P_APP_VERSION]    = Application.version;
            p[AnalyticsConsts.P_GEO_COUNTRY]    = AnalyticsSessionTracker.ResolveGeoCountry();
            p[AnalyticsConsts.P_PLATFORM]       = AnalyticsSessionTracker.ResolvePlatform();
            p[AnalyticsConsts.P_DEVICE_MODEL]   = SystemInfo.deviceModel;
            p[AnalyticsConsts.P_AD_REQUEST_ID]  = eventId;
            p[AnalyticsConsts.P_AD_TYPE]        = ResolveAdType(adInfo.AdFormat);
            p[AnalyticsConsts.P_AD_PLACEMENT]   = adInfo.Placement ?? "";
            p[AnalyticsConsts.P_AD_REVENUE_USD] = adInfo.Revenue;
            p[AnalyticsConsts.P_AD_NETWORK]     = adInfo.NetworkName ?? "";
            p[AnalyticsConsts.P_AD_UNIT_ID]     = adUnitId ?? "";
            p[AnalyticsConsts.P_MEDIATION_POSITION] = 0;
            p[AnalyticsConsts.P_EVENT_PHASE]     = "impression";
            // ROLLBACK_AD_EVENT_FIELDS_20260707: watch_duration_sec = Displayed→Hidden 실측(초).
            //   ad_duration_sec 는 MAX SDK 미제공 → 미emit(NULL). revenue_precision 은 AdInfo 값 사용.
            p[AnalyticsConsts.P_WATCH_DURATION_SEC] = watchDurationSec;
            p[AnalyticsConsts.P_REVENUE_PRECISION] = adInfo.RevenuePrecision ?? "";
            p[AnalyticsConsts.P_LEVEL_NUMBER]    = LevelManager.HasInstance ? LevelManager.Instance.GetCurrentLevelId() : 0;

            if (UserSnapshotCache.HasInstance)
                UserSnapshotCache.Instance.Stamp(p);

            AnalyticsSessionTracker.EmitEvent(AnalyticsConsts.EVT_AD, p);
        }

        /// <summary>MAX AdFormat 문자열("INTER"/"REWARDED"/"BANNER"/"MREC" 등) → 분석 스키마 ad_type 정규화.</summary>
        private static string ResolveAdType(string adFormat)
        {
            if (string.IsNullOrEmpty(adFormat)) return "";
            string f = adFormat.ToLowerInvariant();
            if (f.Contains("inter"))  return "interstitial";
            if (f.Contains("reward")) return "rewarded";
            if (f.Contains("banner")) return "banner";
            if (f.Contains("mrec"))   return "mrec";
            return f;
        }

        #endregion

        #region Event Handlers

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            _currentLevel = evt.levelId;

            if (!IsRewardedAdReady())     LoadRewardedAd();
            if (!IsInterstitialAdReady()) LoadInterstitialAd();
        }

        #endregion
    }
}
