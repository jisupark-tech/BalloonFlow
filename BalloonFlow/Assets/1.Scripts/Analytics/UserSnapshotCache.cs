using System;
using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow.Analytics
{
    /// <summary>
    /// user_property 의 4개 P3 snapshot 필드를 in-memory 캐시.
    /// 이벤트 발사 시점에 Stamp(params) 로 주입 — schema P3 의 "이벤트 시점 유저 상태 스냅샷" 요건 충족.
    ///
    /// 캐시 필드 (6개 이벤트 테이블 공통):
    ///   - install_at            (ISO UTC string)
    ///   - max_reached_level     (int, 한 판도 안 함 = 1)
    ///   - total_spend_usd       (decimal as double)
    ///   - total_ad_revenue_usd  (decimal as double)
    ///
    /// 업데이트 트리거:
    ///   - install_at: 최초 1회 set (재설치 90일 정책 별도)
    ///   - max_reached_level: LevelManager.GetHighestCompletedLevel + 1 기반 갱신
    ///   - total_spend_usd: purchase_event verified 시 += price_usd
    ///   - total_ad_revenue_usd: ad_event impression revenue 도착 시 += revenue_usd
    /// </summary>
    public class UserSnapshotCache : Singleton<UserSnapshotCache>
    {
        private const string PREFS_INSTALL_AT       = "BF_Analytics_InstallAt";
        private const string PREFS_TOTAL_SPEND_USD  = "BF_Analytics_TotalSpendUsd";
        private const string PREFS_TOTAL_AD_REV_USD = "BF_Analytics_TotalAdRevUsd";

        private string _installAtIso;
        private double _totalSpendUsd;
        private double _totalAdRevenueUsd;

        public string InstallAt => _installAtIso ?? "";
        public int MaxReachedLevel => LevelManager.HasInstance
            ? Math.Max(1, LevelManager.Instance.GetHighestCompletedLevel() + 1)
            : 1;
        public double TotalSpendUsd => _totalSpendUsd;
        public double TotalAdRevenueUsd => _totalAdRevenueUsd;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoCreate()
        {
            DiagLog("UserSnapshotCache.AutoCreate fired");
            if (HasInstance) { DiagLog("UserSnapshotCache already exists — skip"); return; }
            var go = new GameObject("UserSnapshotCache");
            go.AddComponent<UserSnapshotCache>();
        }

        protected override void OnSingletonAwake()
        {
            // install_at: 최초 1회 only. 90일 정책에 따른 재발급은 UserDataService 측이 PlayerPrefs reset.
            _installAtIso = PlayerPrefs.GetString(PREFS_INSTALL_AT, "");
            if (string.IsNullOrEmpty(_installAtIso))
            {
                _installAtIso = DateTime.UtcNow.ToString("o");
                PlayerPrefs.SetString(PREFS_INSTALL_AT, _installAtIso);
                PlayerPrefs.Save();
            }

            _totalSpendUsd     = PlayerPrefsGetDouble(PREFS_TOTAL_SPEND_USD, 0);
            _totalAdRevenueUsd = PlayerPrefsGetDouble(PREFS_TOTAL_AD_REV_USD, 0);

            DiagLog($"UserSnapshotCache.OnSingletonAwake — installAt={_installAtIso} spend=${_totalSpendUsd:F2} adRev=${_totalAdRevenueUsd:F4}");
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void DiagLog(string msg) => Debug.Log("[Analytics] " + msg);

        /// <summary>이벤트 params 에 4개 snapshot 필드 주입. Phase 1 (purchase/ad/economy) 에서도 동일 사용.</summary>
        public void Stamp(Dictionary<string, object> p)
        {
            if (p == null) return;
            p[AnalyticsConsts.P_INSTALL_AT]           = InstallAt;
            p[AnalyticsConsts.P_MAX_REACHED_LEVEL]    = MaxReachedLevel;
            // BQ NUMERIC 은 소수 9자리 상한 — double 누적 노이즈(115.94999999999999)가 그대로 나가면
            // 행 전체가 거절되므로 emit 경계에서 라운딩 (2026-07-07 android 적재 전면 차단 사고).
            p[AnalyticsConsts.P_TOTAL_SPEND_USD]      = Math.Round(_totalSpendUsd, 6);
            p[AnalyticsConsts.P_TOTAL_AD_REVENUE_USD] = Math.Round(_totalAdRevenueUsd, 6);
        }

        // ─── 누적 갱신 (Phase 1 wiring 시 호출) ───

        public void OnPurchaseVerified(double priceUsd)
        {
            if (priceUsd <= 0) return;
            _totalSpendUsd = Math.Round(_totalSpendUsd + priceUsd, 6);
            PlayerPrefsSetDouble(PREFS_TOTAL_SPEND_USD, _totalSpendUsd);
            PlayerPrefs.Save();
        }

        public void OnAdRevenueGranted(double revenueUsd)
        {
            if (revenueUsd <= 0) return;
            _totalAdRevenueUsd = Math.Round(_totalAdRevenueUsd + revenueUsd, 6);
            PlayerPrefsSetDouble(PREFS_TOTAL_AD_REV_USD, _totalAdRevenueUsd);
            PlayerPrefs.Save();
        }

        private static double PlayerPrefsGetDouble(string key, double def)
        {
            string s = PlayerPrefs.GetString(key, "");
            return double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : def;
        }

        private static void PlayerPrefsSetDouble(string key, double value)
        {
            PlayerPrefs.SetString(key,
                value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
