using System.Collections.Generic;
using UnityEngine;
using AppsFlyerSDK;

namespace BalloonFlow
{
    /// <summary>
    /// AppsFlyer attribution wrapper. AppsFlyer.initSDK + startSDK 호출,
    /// IAppsFlyerConversionData 콜백으로 conversion 데이터 수신.
    /// </summary>
    public class AttributionManager : Singleton<AttributionManager>, IAppsFlyerConversionData
    {
        private const string LOG_TAG = "[AttributionManager]";

        protected override void OnSingletonAwake()
        {
            string devKey = SdkConfig.AppsFlyerDevKey;
            string appId  = SdkConfig.AppsFlyerAppId;

            if (string.IsNullOrEmpty(devKey))
            {
                Debug.LogWarning($"{LOG_TAG} AppsFlyer Dev Key is empty. Skipping init. (SdkConfig.local.cs 누락 가능성)");
                return;
            }

#if UNITY_EDITOR
            AppsFlyer.setIsDebug(true);
#else
            AppsFlyer.setIsDebug(false);
#endif
            AppsFlyer.initSDK(devKey, appId, this);
            AppsFlyer.startSDK();
            Debug.Log($"{LOG_TAG} AppsFlyer initialized. devKey=***{devKey.Substring(devKey.Length - 4)}");
        }

        /// <summary>커스텀 이벤트 발행. Dictionary value는 string 변환됨.</summary>
        public void LogEvent(string eventName, Dictionary<string, string> values = null)
        {
            if (string.IsNullOrEmpty(eventName)) return;
            AppsFlyer.sendEvent(eventName, values);
        }

        #region IAppsFlyerConversionData callbacks

        public void onConversionDataSuccess(string conversionData)
        {
            AppsFlyer.AFLog("onConversionDataSuccess", conversionData);
            // TODO: deferred deeplink, organic vs paid 분기 등 처리
        }

        public void onConversionDataFail(string error)
        {
            AppsFlyer.AFLog("onConversionDataFail", error);
        }

        public void onAppOpenAttribution(string attributionData)
        {
            AppsFlyer.AFLog("onAppOpenAttribution", attributionData);
            // TODO: direct deeplink 처리
        }

        public void onAppOpenAttributionFailure(string error)
        {
            AppsFlyer.AFLog("onAppOpenAttributionFailure", error);
        }

        #endregion
    }
}
