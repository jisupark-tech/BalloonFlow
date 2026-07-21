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

            // [AF_APP_OPEN_RELIABLE_20260722] 신뢰성 이벤트 큐 가동 — 아래 region 주석 참조.
            _sdkReady = true;
            LoadPendingAfEvents();
            InvokeRepeating(nameof(FlushPendingAfEvents), 3f, AF_FLUSH_INTERVAL);
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

        #region Reliable AF events — PlayerPrefs 재송신 큐

        // [AF_APP_OPEN_RELIABLE_20260722] PH 등 저품질 회선에서 AF 리텐션 < BQ 리텐션(KR 정상) 관측.
        //   원인: AF 리텐션은 SDK 세션(launch) 기록 의존인데, launch 요청은 in-app 이벤트와 달리 프로세스 킬 전
        //   미전송분 보존이 약해 '짧은 세션 + 느린 회선'에서 유실된다. BQ 는 자체 체크포인트/재시도 파이프라인이라
        //   생존 → 리텐션 이격. 세션 자체는 클라에서 재전송 불가 → 회선 무관하게 보존되는 커스텀 app_open 을 병행:
        //     ① 세션 시작 시 PlayerPrefs 큐에 즉시 영속(Save) — 오프라인/프로세스 킬 생존
        //     ② 인터넷 reachable + SDK 초기화 완료일 때만 sendEvent 핸드오프 — 이후 전달은 AF SDK 내부 캐시가 보장
        //     ③ af_event_ts(원 발생 epoch)/af_seq(기기 누적 시퀀스)/af_late(60s+ 지연 발송 여부)로 지연분 구분
        //   중복 정책: 핸드오프 시점에 큐에서 제거 — 유실보다 중복이 낫고(리텐션은 유저-일 dedup 집계) 캡 50개.
        //   ※ 대시보드 리텐션/코호트를 이 app_open 이벤트 기준으로 집계하면 회선 유실 없는 지표가 된다.

        [System.Serializable] private class PendingAfEvent { public string n; public long t; public int s; }
        [System.Serializable] private class PendingAfEventList { public List<PendingAfEvent> items = new List<PendingAfEvent>(); }

        private const string PREFS_AF_QUEUE = "BF_AF_PendingEvents";
        private const string PREFS_AF_SEQ   = "BF_AF_EventSeq";
        private const int    AF_QUEUE_CAP   = 50;
        private const float  AF_FLUSH_INTERVAL = 5f;
        private const long   AF_LATE_THRESHOLD_SEC = 60;
        public  const string EVT_APP_OPEN = "app_open";

        private bool _sdkReady;
        private PendingAfEventList _pending;

        /// <summary>세션 시작 시 호출(AnalyticsSessionTracker) — app_open 을 유실 없이 적재.</summary>
        public void EnqueueAppOpen() => EnqueueReliableEvent(EVT_APP_OPEN);

        /// <summary>유실 방지가 필요한 AF 이벤트 공용 적재 — 즉시 디스크 영속 후 플러시 시도.</summary>
        public void EnqueueReliableEvent(string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return;
            LoadPendingAfEvents();

            int seq = PlayerPrefs.GetInt(PREFS_AF_SEQ, 0) + 1;
            PlayerPrefs.SetInt(PREFS_AF_SEQ, seq);

            _pending.items.Add(new PendingAfEvent
            {
                n = eventName,
                t = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                s = seq
            });
            while (_pending.items.Count > AF_QUEUE_CAP)
                _pending.items.RemoveAt(0);   // 캡 초과 시 오래된 것부터 드랍

            SavePendingAfEvents();   // 즉시 커밋 — 이 직후 프로세스가 죽어도 다음 실행에서 재송신
            FlushPendingAfEvents();  // 온라인이면 바로 핸드오프
        }

        private void LoadPendingAfEvents()
        {
            if (_pending != null) return;
            string json = PlayerPrefs.GetString(PREFS_AF_QUEUE, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { _pending = JsonUtility.FromJson<PendingAfEventList>(json); }
                catch { _pending = null; }
            }
            if (_pending == null || _pending.items == null)
                _pending = new PendingAfEventList();
        }

        private void SavePendingAfEvents()
        {
            PlayerPrefs.SetString(PREFS_AF_QUEUE, JsonUtility.ToJson(_pending));
            PlayerPrefs.Save();
        }

        /// <summary>주기 플러시(InvokeRepeating 5s) — SDK 준비 + 인터넷 reachable 일 때만 핸드오프.</summary>
        private void FlushPendingAfEvents()
        {
            if (!_sdkReady) return;
            if (Application.internetReachability == NetworkReachability.NotReachable) return;
            LoadPendingAfEvents();
            if (_pending.items.Count == 0) return;

            long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int sent = _pending.items.Count;
            for (int i = 0; i < _pending.items.Count; i++)
            {
                PendingAfEvent e = _pending.items[i];
                var values = new Dictionary<string, string>(3)
                {
                    ["af_event_ts"] = e.t.ToString(),
                    ["af_seq"]      = e.s.ToString(),
                    ["af_late"]     = (now - e.t > AF_LATE_THRESHOLD_SEC) ? "1" : "0"
                };
                AppsFlyer.sendEvent(e.n, values);
            }
            _pending.items.Clear();
            SavePendingAfEvents();
            Debug.Log($"{LOG_TAG} reliable AF events flushed: {sent}건");
        }

        #endregion

        #region IAppsFlyerConversionData callbacks

        // ROLLBACK_INSTALL_MEDIA_SOURCE_20260713: conversion JSON 최소 파싱용 — media_source/af_status 만 추출.
        [System.Serializable]
        private class ConversionPayload
        {
            public string af_status;    // "Organic" | "Non-organic"
            public string media_source; // 유료 유입 시 네트워크명 (organic 이면 보통 부재)
        }

        public void onConversionDataSuccess(string conversionData)
        {
            AppsFlyer.AFLog("onConversionDataSuccess", conversionData);

            // ROLLBACK_INSTALL_MEDIA_SOURCE_20260713: 설치 유입 미디어소스 캡처 → UserSnapshotCache 영속.
            //   기존엔 conversion 데이터를 로그만 찍고 버려 install_media_source 가 항상 NULL 이었다.
            //   media_source 있으면 그 값, 없고 Organic 이면 "organic". 최초 1회만 저장(first-write-wins).
            try
            {
                var cd = JsonUtility.FromJson<ConversionPayload>(conversionData);
                string ms = cd != null && !string.IsNullOrEmpty(cd.media_source)
                    ? cd.media_source
                    : (cd != null && string.Equals(cd.af_status, "Organic", System.StringComparison.OrdinalIgnoreCase)
                        ? "organic" : "");
                if (!string.IsNullOrEmpty(ms) && Analytics.UserSnapshotCache.HasInstance)
                    Analytics.UserSnapshotCache.Instance.SetInstallMediaSource(ms);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"{LOG_TAG} conversion 파싱 실패(media_source 미저장): {e.Message}");
            }
            // TODO: deferred deeplink, organic vs paid 세부 분기 등 처리
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
