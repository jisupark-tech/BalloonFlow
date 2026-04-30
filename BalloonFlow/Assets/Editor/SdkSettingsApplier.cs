#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// SdkConfig 의 키들을 각 SDK 의 Settings asset 에 자동 주입.
    /// Admob/AppLovin/Facebook 의 Inspector 메뉴 작업을 한 번에 처리.
    /// 메뉴: BalloonFlow > SDK > Apply Settings From SdkConfig
    /// 또는 BalloonFlow.SdkConfig 키 변경 후 반복 실행 가능 (idempotent).
    /// Reflection 기반 — SDK 버전 업그레이드 시 클래스/필드 이름 변경되면 LogWarning 후 skip.
    /// </summary>
    public static class SdkSettingsApplier
    {
        private const string LOG_TAG = "[SdkSettingsApplier]";

        [MenuItem("BalloonFlow/SDK/Apply Settings From SdkConfig")]
        public static void ApplyAll()
        {
            ApplyAdmob();
            ApplyAppLovin();
            ApplyFacebook();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"{LOG_TAG} ✔ 적용 완료. 빌드 시도 가능.");
        }

        // ───────────────────────────────────────────────
        // Admob — GoogleMobileAds.Editor.GoogleMobileAdsSettings
        // ───────────────────────────────────────────────
        private static void ApplyAdmob()
        {
            string androidAppId = SdkConfig.AdmobAndroidAppId;
            string iosAppId     = SdkConfig.AdmobIOSAppId;
            if (string.IsNullOrEmpty(androidAppId))
            {
                Debug.LogWarning($"{LOG_TAG} Admob: AdmobAndroidAppId 비어있음. SdkConfig.local.cs 확인. skip.");
                return;
            }

            const string ASSET_PATH = "Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset";

            var t = ResolveType("GoogleMobileAds.Editor.GoogleMobileAdsSettings",
                                "GoogleMobileAds.Editor", "Assembly-CSharp-Editor");
            if (t == null)
            {
                Debug.LogWarning($"{LOG_TAG} Admob: Settings type 못 찾음. 메뉴 'Assets > Google Mobile Ads > Settings' 한 번 열어 .asset 자동 생성 후 재실행.");
                return;
            }

            ScriptableObject asset = AssetDatabase.LoadAssetAtPath(ASSET_PATH, t) as ScriptableObject;
            if (asset == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ASSET_PATH));
                asset = ScriptableObject.CreateInstance(t);
                AssetDatabase.CreateAsset(asset, ASSET_PATH);
            }

            SetMember(t, asset, "GoogleMobileAdsAndroidAppId", androidAppId);
            if (!string.IsNullOrEmpty(iosAppId))
                SetMember(t, asset, "GoogleMobileAdsIOSAppId", iosAppId);

            EditorUtility.SetDirty(asset);
            Debug.Log($"{LOG_TAG} Admob ✔ Android={androidAppId}");
        }

        // ───────────────────────────────────────────────
        // AppLovin MAX — AppLovinSettings.Instance.SdkKey (singleton ScriptableObject)
        // ───────────────────────────────────────────────
        private static void ApplyAppLovin()
        {
            string sdkKey = SdkConfig.AppLovinSdkKey;
            if (string.IsNullOrEmpty(sdkKey))
            {
                Debug.LogWarning($"{LOG_TAG} AppLovin: SdkKey 비어있음. skip.");
                return;
            }

            var t = ResolveType("AppLovinSettings",
                                "AppLovin.MaxSdk.Scripts.IntegrationManager.Editor",
                                "Assembly-CSharp-Editor",
                                "Assembly-CSharp");
            if (t == null)
            {
                Debug.LogWarning($"{LOG_TAG} AppLovin: AppLovinSettings type 못 찾음. 'AppLovin > Integration Manager' 메뉴 한 번 열어 asset 생성 후 재실행.");
                return;
            }

            var instanceProp = t.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
            object asset = instanceProp?.GetValue(null);
            if (asset == null)
            {
                Debug.LogWarning($"{LOG_TAG} AppLovin: Instance null. Integration Manager 메뉴 한 번 열기.");
                return;
            }

            SetMember(t, asset, "SdkKey", sdkKey);
            SetMember(t, asset, "QualityServiceEnabled", true);

            // AppLovin AdMob mediation 도 이쪽에 별도 필드. 같이 채움 — Quality Service 빌드 통과 위해.
            SetMember(t, asset, "AdMobAndroidAppId", SdkConfig.AdmobAndroidAppId);
            SetMember(t, asset, "AdMobIosAppId",     SdkConfig.AdmobIOSAppId);

            if (asset is UnityEngine.Object uo) EditorUtility.SetDirty(uo);
            Debug.Log($"{LOG_TAG} AppLovin ✔ SdkKey=***{sdkKey.Substring(Math.Max(0, sdkKey.Length - 6))}");
        }

        // ───────────────────────────────────────────────
        // Facebook — Facebook.Unity.Settings.FacebookSettings (static class with List<string> AppIds 등)
        // + Facebook.Unity.Editor.ManifestMod.GenerateManifest()
        // ───────────────────────────────────────────────
        private static void ApplyFacebook()
        {
            string appId = SdkConfig.FacebookAppId;
            if (string.IsNullOrEmpty(appId))
            {
                Debug.LogWarning($"{LOG_TAG} Facebook: AppId 비어있음. skip.");
                return;
            }

            var t = ResolveType("Facebook.Unity.Settings.FacebookSettings",
                                "Facebook.Unity.Editor", "Facebook.Unity", "Assembly-CSharp-Editor");
            if (t == null)
            {
                Debug.LogWarning($"{LOG_TAG} Facebook: FacebookSettings type 못 찾음. 'Facebook > Edit Settings' 메뉴 직접 입력 권장.");
                return;
            }

            // FacebookSettings 는 보통 list 형 (multi-app 지원). [0] 만 채움.
            SetListProperty(t, null, "AppIds",       new List<string> { appId });
            SetListProperty(t, null, "ClientTokens", new List<string> { SdkConfig.FacebookClientToken ?? "" });
            SetListProperty(t, null, "AppLabels",    new List<string> { "BalloonLoop" });
            SetStaticMember(t, "SelectedAppIndex", 0);

            // 커밋용 — FacebookSettings asset 이 SaveAsset 되도록 dirty 마크
            const string FB_ASSET = "Assets/FacebookSDK/SDK/Resources/FacebookSettings.asset";
            var fbAsset = AssetDatabase.LoadMainAssetAtPath(FB_ASSET);
            if (fbAsset != null) EditorUtility.SetDirty(fbAsset);

            // Manifest Regenerate — Facebook.Unity.Editor.ManifestMod.GenerateManifest()
            var manifestType = ResolveType("Facebook.Unity.Editor.ManifestMod",
                                            "Facebook.Unity.Editor", "Assembly-CSharp-Editor");
            var generateMethod = manifestType?.GetMethod("GenerateManifest",
                                                          BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (generateMethod != null)
            {
                try
                {
                    generateMethod.Invoke(null, null);
                    Debug.Log($"{LOG_TAG} Facebook ✔ AppId={appId} + AndroidManifest 재생성.");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{LOG_TAG} Facebook Manifest regenerate 실패: {ex.Message}. 'Facebook > Edit Settings > Regenerate Android Manifest' 직접 클릭.");
                }
            }
            else
            {
                Debug.LogWarning($"{LOG_TAG} Facebook: ManifestMod 못 찾음. 'Facebook > Edit Settings > Regenerate Android Manifest' 직접 클릭 필요.");
            }

            if (string.IsNullOrEmpty(SdkConfig.FacebookClientToken))
                Debug.LogWarning($"{LOG_TAG} ⚠ Facebook ClientToken 미수령. 빌드는 통과하지만 SDK 런타임 init 실패. Facebook Console > Settings > Advanced > Client Token 받아 SdkConfig.local.cs 채우기.");
        }

        // ───────────────────────────────────────────────
        // Reflection helpers
        // ───────────────────────────────────────────────
        private static Type ResolveType(string typeName, params string[] assemblyHints)
        {
            // 1) Assembly-Qualified hints
            foreach (var asm in assemblyHints)
            {
                var t = Type.GetType($"{typeName}, {asm}", false);
                if (t != null) return t;
            }
            // 2) 모든 로드된 어셈블리 검색
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(typeName, false);
                if (t != null) return t;
            }
            return null;
        }

        private static void SetMember(Type t, object instance, string name, object value)
        {
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var prop = t.GetProperty(name, F);
            if (prop != null && prop.CanWrite) { prop.SetValue(instance, value); return; }
            var field = t.GetField(name, F);
            field?.SetValue(instance, value);
        }

        private static void SetStaticMember(Type t, string name, object value)
        {
            const BindingFlags F = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var prop = t.GetProperty(name, F);
            if (prop != null && prop.CanWrite) { prop.SetValue(null, value); return; }
            var field = t.GetField(name, F);
            field?.SetValue(null, value);
        }

        private static void SetListProperty(Type t, object instance, string name, List<string> values)
        {
            // 정적 또는 인스턴스 프로퍼티 (List<string> 또는 array)
            const BindingFlags F = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var prop = t.GetProperty(name, F);
            if (prop != null && prop.CanWrite)
            {
                if (prop.PropertyType == typeof(List<string>))
                    prop.SetValue(instance, values);
                else if (prop.PropertyType == typeof(string[]))
                    prop.SetValue(instance, values.ToArray());
                return;
            }
            var field = t.GetField(name, F);
            if (field != null)
            {
                if (field.FieldType == typeof(List<string>))
                    field.SetValue(instance, values);
                else if (field.FieldType == typeof(string[]))
                    field.SetValue(instance, values.ToArray());
            }
        }
    }
}
#endif
