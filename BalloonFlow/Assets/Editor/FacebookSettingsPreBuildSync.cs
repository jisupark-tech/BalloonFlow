#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BalloonFlow.EditorTools
{
    [InitializeOnLoad]
    public sealed class FacebookSettingsPreBuildSync : IPreprocessBuildWithReport
    {
        private const string LOG_TAG = "[FacebookSettingsPreBuildSync]";
        private const string FB_ASSET = "Assets/FacebookSDK/SDK/Resources/FacebookSettings.asset";

        public int callbackOrder => -10000;

        static FacebookSettingsPreBuildSync()
        {
            EnsureOpenSslOnPath();
            EditorApplication.delayCall += SyncFacebookSettings;
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report == null || report.summary.platform != BuildTarget.Android) return;

            // ROLLBACK_FACEBOOK_PREBUILD_SYNC_20260619:
            // Facebook postprocess validates FacebookSettings.asset, not only AndroidManifest.xml.
            // Sync local SDK config into FacebookSettings before the SDK's post-build validation runs.
            EnsureOpenSslOnPath();
            SyncFacebookSettings();
        }

        [MenuItem("BalloonFlow/SDK/Sync Facebook Settings")]
        private static void SyncFacebookSettingsFromMenu()
        {
            EnsureOpenSslOnPath();
            SyncFacebookSettings();
        }

        private static void EnsureOpenSslOnPath()
        {
            if (CanFindExecutableOnPath("openssl.exe") || CanFindExecutableOnPath("openssl"))
                return;

            // ROLLBACK_FACEBOOK_PREBUILD_SYNC_20260619:
            // Facebook SDK computes the Android key hash by launching openssl from PATH.
            // Git for Windows already ships openssl on this machine, but Unity's process PATH
            // does not include it by default, so expose it only for the current Editor process.
            string[] candidates =
            {
                "C:/Program Files/Git/usr/bin/openssl.exe",
                "C:/Program Files/Git/mingw64/bin/openssl.exe",
                "C:/Program Files/OpenSSL-Win64/bin/openssl.exe",
                "C:/Program Files/OpenSSL-Win32/bin/openssl.exe"
            };

            foreach (string candidate in candidates)
            {
                if (!File.Exists(candidate)) continue;

                string dir = Path.GetDirectoryName(candidate);
                string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                bool alreadyAdded = path
                    .Split(Path.PathSeparator)
                    .Any(entry => string.Equals(
                        NormalizePath(entry),
                        NormalizePath(dir),
                        StringComparison.OrdinalIgnoreCase));

                if (!alreadyAdded)
                    Environment.SetEnvironmentVariable("PATH", path + Path.PathSeparator + dir);

                Debug.Log($"{LOG_TAG} OpenSSL path added for Unity Editor process: {dir}");
                return;
            }

            Debug.LogWarning($"{LOG_TAG} OpenSSL executable was not found. Facebook Android setup validation may fail.");
        }

        private static bool CanFindExecutableOnPath(string executableName)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string entry in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;

                string candidate = Path.Combine(entry.Trim(), executableName);
                if (File.Exists(candidate)) return true;
            }

            return false;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return Path.GetFullPath(path.Trim().Replace('\\', '/')).TrimEnd('/', '\\');
        }

        private static void SyncFacebookSettings()
        {
            string appId = SdkConfig.FacebookAppId;
            string clientToken = SdkConfig.FacebookClientToken;

            if (string.IsNullOrEmpty(appId))
            {
                Debug.LogWarning($"{LOG_TAG} FacebookAppId is empty. Skipped.");
                return;
            }

            if (string.IsNullOrEmpty(clientToken))
                Debug.LogWarning($"{LOG_TAG} FacebookClientToken is empty. Android Facebook setup may fail.");

            Type settingsType = ResolveType(
                "Facebook.Unity.Settings.FacebookSettings",
                "Facebook.Unity.Settings",
                "Facebook.Unity.Editor",
                "Facebook.Unity",
                "Assembly-CSharp-Editor");

            if (settingsType == null)
            {
                Debug.LogWarning($"{LOG_TAG} FacebookSettings type not found.");
                return;
            }

            SetList(settingsType, "AppIds", new List<string> { appId });
            SetList(settingsType, "ClientTokens", new List<string> { clientToken ?? string.Empty });
            SetList(settingsType, "AppLabels", new List<string> { "Balloon Loop" });
            SetStatic(settingsType, "SelectedAppIndex", 0);

            // ROLLBACK_FACEBOOK_PREBUILD_SYNC_20260619:
            // Facebook SDK 18 Android postprocess also validates the keystore/key hash setup.
            // Keep this pointed at the actual Unity signing keystore; fall back to the debug
            // keystore only for local/dev builds where no custom keystore is configured.
            string keystorePath = ResolveAndroidKeystorePath();
            if (!string.IsNullOrEmpty(keystorePath))
                SetStatic(settingsType, "AndroidKeystorePath", keystorePath);
            else
                Debug.LogWarning($"{LOG_TAG} Android keystore not found. Facebook Android setup may still fail.");

            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(FB_ASSET);
            if (asset != null) EditorUtility.SetDirty(asset);

            RegenerateManifest();
            AssetDatabase.SaveAssets();

            Debug.Log($"{LOG_TAG} Facebook settings synced. appId={appId}, clientTokenSet={!string.IsNullOrEmpty(clientToken)}, keystoreSet={!string.IsNullOrEmpty(keystorePath)}");
        }

        private static string ResolveAndroidKeystorePath()
        {
            string customPath = null;

            try
            {
                if (PlayerSettings.Android.useCustomKeystore)
                    customPath = PlayerSettings.Android.keystoreName;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LOG_TAG} Failed to read PlayerSettings Android keystore: {ex.Message}");
            }

            string resolvedCustom = ResolveExistingPath(customPath);
            if (!string.IsNullOrEmpty(resolvedCustom))
                return resolvedCustom;

            string debugPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".android",
                "debug.keystore");

            return ResolveExistingPath(debugPath);
        }

        private static string ResolveExistingPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            string normalized = path.Replace('\\', '/');
            string fullPath = Path.IsPathRooted(normalized)
                ? normalized
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), normalized));

            return File.Exists(fullPath) ? fullPath.Replace('\\', '/') : string.Empty;
        }

        private static void RegenerateManifest()
        {
            Type manifestType = ResolveType(
                "Facebook.Unity.Editor.ManifestMod",
                "Facebook.Unity.Editor",
                "Assembly-CSharp-Editor");

            MethodInfo generate = manifestType?.GetMethod(
                "GenerateManifest",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (generate == null)
            {
                Debug.LogWarning($"{LOG_TAG} ManifestMod.GenerateManifest not found.");
                return;
            }

            try
            {
                generate.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LOG_TAG} GenerateManifest failed: {ex.Message}");
            }
        }

        private static Type ResolveType(string typeName, params string[] assemblyHints)
        {
            foreach (string asm in assemblyHints)
            {
                Type type = Type.GetType($"{typeName}, {asm}", false);
                if (type != null) return type;
            }

            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(asm => asm.GetType(typeName, false))
                .FirstOrDefault(type => type != null);
        }

        private static void SetStatic(Type type, string name, object value)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo prop = type.GetProperty(name, flags);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(null, value);
                return;
            }

            FieldInfo field = type.GetField(name, flags);
            field?.SetValue(null, value);
        }

        private static void SetList(Type type, string name, List<string> values)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo prop = type.GetProperty(name, flags);
            if (prop != null && prop.CanWrite)
            {
                if (prop.PropertyType == typeof(List<string>)) prop.SetValue(null, values);
                else if (prop.PropertyType == typeof(string[])) prop.SetValue(null, values.ToArray());
                return;
            }

            FieldInfo field = type.GetField(name, flags);
            if (field == null) return;

            if (field.FieldType == typeof(List<string>)) field.SetValue(null, values);
            else if (field.FieldType == typeof(string[])) field.SetValue(null, values.ToArray());
        }
    }
}
#endif
