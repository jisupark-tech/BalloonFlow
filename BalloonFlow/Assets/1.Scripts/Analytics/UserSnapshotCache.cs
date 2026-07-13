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

        // ROLLBACK_USER_PROPERTY_PIPELINE_20260708: user_property 테이블용 추가 영속 필드.
        //   install_*(버전/국가/플랫폼/디바이스)는 최초 1회 고정(불변), play 카운터/최근 플레이는 누적.
        private const string PREFS_INSTALL_VERSION   = "BF_Analytics_InstallVersion";
        private const string PREFS_INSTALL_COUNTRY   = "BF_Analytics_InstallCountry";
        private const string PREFS_INSTALL_PLATFORM  = "BF_Analytics_InstallPlatform";
        private const string PREFS_INSTALL_DEVICE    = "BF_Analytics_InstallDevice";
        private const string PREFS_TOTAL_PLAY_COUNT  = "BF_Analytics_TotalPlayCount";
        private const string PREFS_LAST_PLAYED_AT    = "BF_Analytics_LastPlayedAt";

        // ROLLBACK_INSTALL_MEDIA_SOURCE_20260713: AppsFlyer 미디어소스는 설치 첫 실행의 conversion 콜백이
        //   유일한 진실원 → 최초 1회 영속(불변)해 이후 세션까지 생존. 콜백 타이밍이 늦어 처음엔 빈 값일 수 있음.
        private const string PREFS_INSTALL_MEDIA_SOURCE = "BF_Analytics_InstallMediaSource";
        // ROLLBACK_GAID_AID_20260713: Android 광고 ID(GAID). 네이티브 AdvertisingIdClient 비동기 조회 → 최초 1회 영속.
        private const string PREFS_AID = "BF_Analytics_Aid";

        private string _installAtIso;
        private double _totalSpendUsd;
        private double _totalAdRevenueUsd;
        private string _installVersion;
        private string _installCountry;
        private string _installPlatform;
        private string _installDevice;
        private int _totalPlayCount;
        private string _lastPlayedAtIso;
        private string _installMediaSource; // ROLLBACK_INSTALL_MEDIA_SOURCE_20260713
        private string _aid;                // ROLLBACK_GAID_AID_20260713 (메인스레드에서만 write)
        private volatile string _pendingAid; // 백그라운드 스레드→메인스레드 마샬용

        public string InstallAt => _installAtIso ?? "";
        public int MaxReachedLevel => LevelManager.HasInstance
            ? Math.Max(1, LevelManager.Instance.GetHighestCompletedLevel() + 1)
            : 1;
        public double TotalSpendUsd => _totalSpendUsd;
        public double TotalAdRevenueUsd => _totalAdRevenueUsd;

        // ROLLBACK_USER_PROPERTY_PIPELINE_20260708 accessors
        public string InstallVersion  => _installVersion ?? "";
        public string InstallCountry  => _installCountry ?? "";
        public string InstallPlatform => _installPlatform ?? "";
        public string InstallDevice   => _installDevice ?? "";
        public int TotalPlayCount     => _totalPlayCount;
        public string LastPlayedAt    => _lastPlayedAtIso ?? "";
        // ROLLBACK_INSTALL_MEDIA_SOURCE_20260713: 유입 미디어소스(organic/네트워크명). 미수신 시 "".
        public string InstallMediaSource => _installMediaSource ?? "";
        // ROLLBACK_GAID_AID_20260713: Android GAID. 비동기 조회 완료 전/에디터/iOS 는 "".
        public string Aid => _aid ?? "";
        /// <summary>스키마 비고: total_clear_count = max_reached_level - 1 (첫클리어 건수).</summary>
        public int TotalClearCount    => Math.Max(0, MaxReachedLevel - 1);
        /// <summary>결제자 라벨 — verified 결제 누적이 있으면 영구 TRUE (스키마 §20).</summary>
        public bool IsPayer           => _totalSpendUsd > 0;

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

            // ROLLBACK_USER_PROPERTY_PIPELINE_20260708: install_* 불변 필드 최초 1회 고정.
            _installVersion  = GetOrInitPref(PREFS_INSTALL_VERSION,  Application.version);
            _installCountry  = GetOrInitPref(PREFS_INSTALL_COUNTRY,  AnalyticsSessionTracker.ResolveGeoCountry());
            _installPlatform = GetOrInitPref(PREFS_INSTALL_PLATFORM, AnalyticsSessionTracker.ResolvePlatform());
            _installDevice   = GetOrInitPref(PREFS_INSTALL_DEVICE,   SystemInfo.deviceModel);
            _totalPlayCount  = PlayerPrefs.GetInt(PREFS_TOTAL_PLAY_COUNT, 0);
            _lastPlayedAtIso = PlayerPrefs.GetString(PREFS_LAST_PLAYED_AT, "");
            // ROLLBACK_INSTALL_MEDIA_SOURCE_20260713: 저장된 미디어소스 복원(콜백 지연 대비 이전 세션 값 유지).
            _installMediaSource = PlayerPrefs.GetString(PREFS_INSTALL_MEDIA_SOURCE, "");
            // ROLLBACK_GAID_AID_20260713: 저장된 GAID 복원. 없으면 네이티브 비동기 조회 착수(첫 실행/최초 수집).
            _aid = PlayerPrefs.GetString(PREFS_AID, "");
            if (string.IsNullOrEmpty(_aid))
                BeginResolveAdvertisingId();

            DiagLog($"UserSnapshotCache.OnSingletonAwake — installAt={_installAtIso} spend=${_totalSpendUsd:F2} adRev=${_totalAdRevenueUsd:F4}");
        }

        // ─── GAID (ROLLBACK_GAID_AID_20260713) ───

        /// <summary>백그라운드 스레드에서 마샬된 GAID 를 메인스레드에서 영속. PlayerPrefs 는 메인스레드 전용.</summary>
        private void Update()
        {
            if (_pendingAid != null)
            {
                string v = _pendingAid;
                _pendingAid = null;
                SetAid(v);
            }
        }

        private void SetAid(string aid)
        {
            if (string.IsNullOrEmpty(aid)) return;
            if (!string.IsNullOrEmpty(_aid)) return;               // first-write-wins
            if (aid == "00000000-0000-0000-0000-000000000000") return; // opt-out/limit ad tracking = 무효값
            _aid = aid;
            PlayerPrefs.SetString(PREFS_AID, aid);
            PlayerPrefs.Save();
            DiagLog($"UserSnapshotCache.SetAid → {aid}");
        }

        /// <summary>
        /// Android GAID 를 Play Services AdvertisingIdClient 로 비동기 조회(블로킹 호출이라 워커 스레드).
        /// 결과는 _pendingAid 로 넘겨 Update()가 메인스레드에서 영속. 실패/에디터/iOS 는 무동작(aid="").
        /// AD_ID 권한은 SDK 매니페스트 머지로 이미 포함(targetSdk 35). 예외는 전부 삼켜 안전.
        /// </summary>
        private void BeginResolveAdvertisingId()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            System.Threading.Tasks.Task.Run(() =>
            {
                bool attached = false;
                try
                {
                    UnityEngine.AndroidJNI.AttachCurrentThread();
                    attached = true;
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    using (var client = new AndroidJavaClass("com.google.android.gms.ads.identifier.AdvertisingIdClient"))
                    using (var adInfo = client.CallStatic<AndroidJavaObject>("getAdvertisingIdInfo", activity))
                    {
                        bool limited = adInfo.Call<bool>("isLimitAdTrackingEnabled");
                        string id = limited ? "" : adInfo.Call<string>("getId");
                        if (!string.IsNullOrEmpty(id)) _pendingAid = id;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Analytics] GAID 조회 실패(무시): {e.Message}");
                }
                finally
                {
                    if (attached) UnityEngine.AndroidJNI.DetachCurrentThread();
                }
            });
#endif
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

        /// <summary>ROLLBACK_USER_PROPERTY_PIPELINE_20260708: play_start 발사 시 호출 —
        /// 누적 판 수(첫클리어까지 스펙이나 BL 은 재도전 없음 = 전 판) + 마지막 플레이 시각 갱신.</summary>
        public void OnLevelPlayStarted()
        {
            _totalPlayCount++;
            _lastPlayedAtIso = DateTime.UtcNow.ToString("o");
            PlayerPrefs.SetInt(PREFS_TOTAL_PLAY_COUNT, _totalPlayCount);
            PlayerPrefs.SetString(PREFS_LAST_PLAYED_AT, _lastPlayedAtIso);
            PlayerPrefs.Save();
        }

        /// <summary>ROLLBACK_INSTALL_MEDIA_SOURCE_20260713: AppsFlyer conversion 콜백에서 1회 호출.
        /// 최초 비어있지 않은 값만 영속(불변) — 이후 콜백/세션에서 덮어쓰지 않음.</summary>
        public void SetInstallMediaSource(string mediaSource)
        {
            if (string.IsNullOrEmpty(mediaSource)) return;
            if (!string.IsNullOrEmpty(_installMediaSource)) return; // 이미 확정 — first-write-wins
            _installMediaSource = mediaSource;
            PlayerPrefs.SetString(PREFS_INSTALL_MEDIA_SOURCE, mediaSource);
            PlayerPrefs.Save();
            DiagLog($"UserSnapshotCache.SetInstallMediaSource → {mediaSource}");
        }

        private static string GetOrInitPref(string key, string initValue)
        {
            string v = PlayerPrefs.GetString(key, "");
            if (!string.IsNullOrEmpty(v)) return v;
            v = initValue ?? "";
            PlayerPrefs.SetString(key, v);
            PlayerPrefs.Save();
            return v;
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
