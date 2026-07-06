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

        /// <summary>
        /// [2026-07-06 AF_PURCHASE] AppsFlyer 표준 구매 이벤트(af_purchase).
        /// 대시보드 매출/ROAS 리포트는 af_revenue/af_currency 표준 키로만 집계됨 —
        /// 커스텀 purchase_event 로는 잡히지 않는다. 서버 영수증 검증 통과 시점에만 호출할 것.
        /// af_order_id 로 AppsFlyer 측 중복 집계 제거.
        /// </summary>
        public void LogPurchase(double revenueUsd, string currencyCode, string productId, string orderId)
        {
            var values = new Dictionary<string, string>(5)
            {
                [AFInAppEvents.REVENUE]    = revenueUsd.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                [AFInAppEvents.CURRENCY]   = string.IsNullOrEmpty(currencyCode) ? "USD" : currencyCode,
                [AFInAppEvents.CONTENT_ID] = productId ?? "",
                [AFInAppEvents.QUANTITY]   = "1",
            };
            if (!string.IsNullOrEmpty(orderId))
                values[AFInAppEvents.ORDER_ID] = orderId;

            AppsFlyer.sendEvent(AFInAppEvents.PURCHASE, values);
            Debug.Log($"{LOG_TAG} af_purchase sent. product={productId} revenue={values[AFInAppEvents.REVENUE]} {values[AFInAppEvents.CURRENCY]}");
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
