namespace BalloonFlow
{
    /// <summary>
    /// SDK 키/ID 보관소. 실제 값은 SdkConfig.local.cs (.gitignore)에 채움.
    /// 이 파일은 커밋되며 키 노출 없는 placeholder만 가짐.
    /// 향후 Phase 2: Firebase Remote Config로 서버 fetch 대체 예정.
    ///
    /// 사용 예: SdkConfig.AppLovinSdkKey
    /// 빈 값이면 SdkConfig.local.cs 가 누락된 상태 → 빌드 시 SDK init 실패.
    /// </summary>
    public static partial class SdkConfig
    {
        // Static auto-property는 기본값 빈 문자열. SdkConfig.local.cs 의 LocalSetup() 에서 채움.
        public static string FacebookAppId               { get; private set; } = "";
        public static string FacebookClientToken         { get; private set; } = "";
        public static string AppLovinSdkKey              { get; private set; } = "";
        public static string AppLovinRewardedAdUnitId    { get; private set; } = "";
        public static string AppLovinBannerAdUnitId      { get; private set; } = "";
        public static string AppLovinInterstitialAdUnitId { get; private set; } = "";
        public static string AdmobAndroidAppId           { get; private set; } = "";
        public static string AppsFlyerDevKey             { get; private set; } = "";
        public static string AppsFlyerAppId              { get; private set; } = "";

        static SdkConfig()
        {
            LocalSetup();
        }

        /// <summary>
        /// SdkConfig.local.cs 가 partial 메서드를 구현하지 않으면 컴파일러가 호출 자체를 제거.
        /// 따라서 .local.cs 누락 시 모든 키가 빈 문자열로 남음 (의도된 placeholder).
        /// </summary>
        static partial void LocalSetup();
    }
}
