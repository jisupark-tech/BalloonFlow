#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// Auto-configures Player Settings and Build Settings for BalloonFlow.
    /// Runs once via [InitializeOnLoad].
    /// </summary>
    /// <remarks>
    /// Layer: Core | Genre: Puzzle | Role: Config | Phase: 0
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow
    /// </remarks>
    [InitializeOnLoad]
    public static class ProjectConfigurator
    {
        private const string PREFS_KEY = "BalloonFlow_ProjectConfigured";

        static ProjectConfigurator()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(PREFS_KEY, false))
                {
                    return;
                }

                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    return;
                }

                ConfigureProject();
                EditorPrefs.SetBool(PREFS_KEY, true);
                Debug.Log("[ProjectConfigurator] BalloonFlow project settings configured.");
            };
        }

        private static void ConfigureProject()
        {
            // ── Company & Product ──
            PlayerSettings.companyName = "BalloonFlow Studio";
            PlayerSettings.productName = "BalloonFlow";

            // ── Resolution / Orientation ──
            PlayerSettings.defaultScreenWidth = 1080;
            PlayerSettings.defaultScreenHeight = 1920;
            PlayerSettings.defaultIsNativeResolution = false;

            // Portrait only
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = true;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            // ── Android Settings ──
#if UNITY_ANDROID
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)34;

            // ARM64 for production (value 2), add ARMv7 for wider support (value 5 = ARMv7+ARM64)
            PlayerSettings.Android.targetArchitectures =
                AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;

            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
#endif

            // ── iOS Settings ──
#if UNITY_IOS
            PlayerSettings.iOS.targetOSVersionString = "14.0";
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.iOS, ScriptingImplementation.IL2CPP);
#endif

            // ── Common Settings ──
            PlayerSettings.SetApiCompatibilityLevel(
                EditorUserBuildSettings.selectedBuildTargetGroup,
                ApiCompatibilityLevel.NET_Standard);

            // Color space
            PlayerSettings.colorSpace = ColorSpace.Linear;

            AssetDatabase.SaveAssets();
        }
    }
}
#endif
