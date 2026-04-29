using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// SDK 통합 진입점. Scene 로드 전에 RuntimeInitializeOnLoadMethod 로 호출되어,
    /// AttributionManager / AdManager / AnalyticsManager 매니저들을 한 GameObject에 묶어 부트.
    /// 매니저 각자의 OnSingletonAwake 에서 자체 SDK init 수행.
    /// </summary>
    /// <remarks>
    /// Bootstrap GameObject 는 DontDestroyOnLoad (Singleton 베이스 클래스 처리).
    /// 키가 비어있으면 (SdkConfig.local.cs 누락) 각 매니저가 LogWarning 후 init 스킵.
    /// </remarks>
    public static class SdkBootstrap
    {
        private const string BOOT_OBJECT_NAME = "[SdkBootstrap]";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // 중복 생성 방지 (도메인 리로드 시)
            var existing = GameObject.Find(BOOT_OBJECT_NAME);
            if (existing != null) return;

            var go = new GameObject(BOOT_OBJECT_NAME);
            Object.DontDestroyOnLoad(go);

            // 순서:
            //   - UserDataService: Firebase Auth(Anon) + Firestore /users/{uid} 로드. 다른 매니저가 IsReady 를 기다림
            //   - Attribution(AppsFlyer): 다른 매니저가 AttributionManager.Instance 참조 가능
            //   - Ad(MAX): SDK init은 비동기, 콜백 후 광고 로드
            //   - Analytics(Firebase + Facebook): 비동기 init, 준비 전 LogEvent 는 drop
            go.AddComponent<UserDataService>();
            go.AddComponent<ShopCatalogService>();
            go.AddComponent<AttributionManager>();
            go.AddComponent<AdManager>();
            go.AddComponent<AnalyticsManager>();

            Debug.Log("[SdkBootstrap] Boot object created. Managers attached.");
        }
    }
}
