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

            // 모바일 60FPS 타겟 + VSync off (Optimized Frame Pacing 이 frame timing 제어)
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount  = 0;
            Screen.sleepTimeout         = SleepTimeout.NeverSleep;

            // Development Build / Editor 에서 Debug.Log* 가 logcat 에 항상 출력되도록 강제.
            // Unity 6 default 가 일부 LogType 의 stack trace 를 None 으로 두면 logcat output 누락 케이스 있음.
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.unityLogger.logEnabled = true;
            Application.SetStackTraceLogType(LogType.Log,       StackTraceLogType.ScriptOnly);
            Application.SetStackTraceLogType(LogType.Warning,   StackTraceLogType.ScriptOnly);
            Application.SetStackTraceLogType(LogType.Error,     StackTraceLogType.ScriptOnly);
            Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.Full);
#endif

            // DOTween capacity 미리 잡기 — 풍선 동시 pop 시 Tween 폭증으로 자동 increase 가 GC alloc 유발.
            // 풍선 격자 max ~150 + dart fly + UI tween 고려해 넉넉히.
            DG.Tweening.DOTween.SetTweensCapacity(2000, 500);

            var go = new GameObject(BOOT_OBJECT_NAME);
            Object.DontDestroyOnLoad(go);

            // 순서:
            //   - UserDataService: Firebase Auth(Anon) + Firestore /users/{uid} 로드. 다른 매니저가 IsReady 를 기다림
            //   - Attribution(AppsFlyer): 다른 매니저가 AttributionManager.Instance 참조 가능
            //   - Ad(MAX): SDK init은 비동기, 콜백 후 광고 로드
            //   - Analytics(Firebase + Facebook): 비동기 init, 준비 전 LogEvent 는 drop
            // FirebaseManager 가 가장 먼저 — CheckAndFixDependenciesAsync 단독 호출.
            // 다른 매니저들은 FirebaseManager.OnReady 이벤트 (또는 IsReady polling) 후에야 Firebase API 사용 가능.
            // 이 순서 보장이 안 되면 "Don't call other Firebase functions while CheckDependencies is running" InvalidOperationException 발생.
            go.AddComponent<FirebaseManager>();
            go.AddComponent<UserDataService>();
            go.AddComponent<ShopCatalogService>();
            go.AddComponent<AttributionManager>();
            go.AddComponent<AdManager>();
            go.AddComponent<AnalyticsManager>();
            go.AddComponent<PurchaseRewardEffect>();

            Debug.Log("[SdkBootstrap] Boot object created. Managers attached.");
        }
    }
}
