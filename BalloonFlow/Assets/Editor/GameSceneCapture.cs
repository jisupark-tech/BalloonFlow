#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BalloonFlow.Editor
{
    public static class GameSceneCapture
    {
        private const string CaptureDirName = "BalloonFlow_Shot";

        [MenuItem("BalloonFlow/Capture Game Scene #F12", false, 210)]
        public static void CaptureGameScene()
        {
            string dir = ResolveCaptureDirectory();
            Directory.CreateDirectory(dir);

            string fileName = $"BalloonFlow_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string fullPath = Path.Combine(dir, fileName);

            EditorApplication.ExecuteMenuItem("Window/General/Game");

            Texture2D tex = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null || tex.width <= 1)
                {
                    ScreenCapture.CaptureScreenshot(fullPath);
                    Debug.Log($"[BalloonFlow] Capture scheduled (next frame): {fullPath}");
                }
                else
                {
                    File.WriteAllBytes(fullPath, tex.EncodeToPNG());
                    Debug.Log($"[BalloonFlow] Captured: {fullPath} ({tex.width}x{tex.height})");
                    EditorUtility.RevealInFinder(fullPath);
                }
            }
            finally
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        [MenuItem("BalloonFlow/Open Capture Folder", false, 211)]
        private static void OpenCaptureFolder()
        {
            string dir = ResolveCaptureDirectory();
            Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir + Path.DirectorySeparatorChar);
        }

        private static string ResolveCaptureDirectory()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string parent = Path.GetDirectoryName(projectRoot);
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
