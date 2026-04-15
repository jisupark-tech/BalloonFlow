#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BalloonFlow.Editor
{
    public static class GameSceneCapture
    {
        private const string CaptureDirName = "BalloonFlow_Shot";

        [MenuItem("BalloonFlow/Capture Game Scene", false, 210)]
        [Shortcut("BalloonFlow/Capture Game Scene", KeyCode.F12, ShortcutModifiers.Shift)]
        public static void CaptureGameScene()
        {
            string dir = ResolveCaptureDirectory();
            Directory.CreateDirectory(dir);

            string sceneName = SceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(sceneName)) sceneName = "Scene";
            string fileName = $"BalloonFlow_{sceneName}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string fullPath = Path.Combine(dir, fileName);

            FocusGameView();

            ScreenCapture.CaptureScreenshot(fullPath);
            Debug.Log($"[BalloonFlow] Capture scheduled: {fullPath}");

            double deadline = EditorApplication.timeSinceStartup + 3.0;
            EditorApplication.CallbackFunction check = null;
            check = () =>
            {
                if (File.Exists(fullPath))
                {
                    EditorApplication.update -= check;
                    Debug.Log($"[BalloonFlow] Captured: {fullPath}");
                    EditorUtility.RevealInFinder(fullPath);
                }
                else if (EditorApplication.timeSinceStartup > deadline)
                {
                    EditorApplication.update -= check;
                    Debug.LogWarning($"[BalloonFlow] Capture timeout. Ensure Play mode is running. Target: {fullPath}");
                }
            };
            EditorApplication.update += check;
        }

        [MenuItem("BalloonFlow/Open Capture Folder", false, 211)]
        private static void OpenCaptureFolder()
        {
            string dir = ResolveCaptureDirectory();
            Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir + Path.DirectorySeparatorChar);
        }

        private static void FocusGameView()
        {
            var gameViewType = Type.GetType("UnityEditor.PlayModeView,UnityEditor")
                               ?? Type.GetType("UnityEditor.GameView,UnityEditor");
            if (gameViewType != null)
            {
                var window = EditorWindow.GetWindow(gameViewType, false, null, false);
                if (window != null) window.Focus();
            }
        }

        private static string ResolveCaptureDirectory()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string parent = projectRoot != null ? Path.GetDirectoryName(projectRoot) : null;
            string grandparent = parent != null ? Path.GetDirectoryName(parent) : null;

            foreach (var candidate in new[] { grandparent, parent, projectRoot })
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                string p = Path.Combine(candidate, CaptureDirName);
                if (Directory.Exists(p)) return p;
            }

            return Path.Combine(projectRoot ?? ".", CaptureDirName);
        }
    }
}
#endif
