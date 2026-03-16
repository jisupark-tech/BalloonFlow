#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// Creates Balloon, Dart, and Holder prefabs in Assets/Resources/Prefabs/.
    /// These are required for ObjectPoolManager to function at runtime.
    /// Runs once via [InitializeOnLoad]. Force re-run: BalloonFlow > Rebuild Prefabs.
    /// </summary>
    [InitializeOnLoad]
    public static class PrefabBuilder
    {
        private const string PREFS_KEY = "BalloonFlow_PrefabsBuilt_v5";
        private const string PREFAB_FOLDER = "Assets/Resources/Prefabs";

        // Balloon colors for the default material tints
        private static readonly Color[] BalloonColors = new Color[]
        {
            new Color(0.95f, 0.25f, 0.25f), // Red
            new Color(0.25f, 0.55f, 0.95f), // Blue
            new Color(0.25f, 0.85f, 0.35f), // Green
            new Color(0.95f, 0.85f, 0.15f), // Yellow
            new Color(0.80f, 0.30f, 0.90f), // Purple
            new Color(0.95f, 0.55f, 0.15f), // Orange
            new Color(0.40f, 0.90f, 0.90f), // Cyan
            new Color(0.95f, 0.50f, 0.70f), // Pink
        };

        static PrefabBuilder()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(PREFS_KEY, false)) return;
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                BuildAllPrefabs();
                EditorPrefs.SetBool(PREFS_KEY, true);
                Debug.Log("[PrefabBuilder] BalloonFlow prefabs created (v5 — 3D).");
            };
        }

        [MenuItem("BalloonFlow/Rebuild Prefabs")]
        private static void RebuildPrefabs()
        {
            BuildAllPrefabs();
            EditorPrefs.SetBool(PREFS_KEY, true);
            Debug.Log("[PrefabBuilder] Prefabs rebuilt.");
        }

        [MenuItem("BalloonFlow/Reset Prefab Builder")]
        private static void ResetPrefs()
        {
            EditorPrefs.DeleteKey(PREFS_KEY);
            Debug.Log("[PrefabBuilder] Prefs reset. Prefabs will rebuild on next domain reload.");
        }

        private static void BuildAllPrefabs()
        {
            EnsureFolder(PREFAB_FOLDER);

            BuildBalloonPrefab();
            BuildDartPrefab();
            BuildHolderPrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Balloon prefab: Sphere primitive + SphereCollider + BalloonIdentifier.
        /// Color is set at runtime by BalloonController via MeshRenderer.material.color.
        /// Used by ObjectPoolManager with key "Balloon".
        /// </summary>
        private static void BuildBalloonPrefab()
        {
            string path = PREFAB_FOLDER + "/Balloon.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                // Already exists — skip to avoid overwriting user modifications
                return;
            }

            // CreatePrimitive auto-adds MeshFilter, MeshRenderer, SphereCollider
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Balloon";

            // Assign Standard material with white placeholder color (tinted at runtime)
            var mr = go.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Standard"));
            mat.color = Color.white;
            mr.material = mat;

            // SphereCollider is auto-added by CreatePrimitive — configure it
            var col = go.GetComponent<SphereCollider>();
            col.isTrigger = false;

            // BalloonIdentifier for dart hit detection
            go.AddComponent<BalloonIdentifier>();

            // Scale to appropriate game size
            go.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);

            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);

            Debug.Log($"[PrefabBuilder] Created Balloon prefab (3D Sphere) at {path}");
        }

        /// <summary>
        /// Dart prefab: Cylinder primitive shaped as a pin/needle.
        /// SphereCollider (trigger) for pop detection.
        /// Used by ObjectPoolManager with key "Dart".
        /// </summary>
        private static void BuildDartPrefab()
        {
            string path = PREFAB_FOLDER + "/Dart.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                return;
            }

            // CreatePrimitive auto-adds MeshFilter, MeshRenderer, CapsuleCollider
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Dart";

            // Assign Standard material — dark metallic look for a pin/needle
            var mr = go.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.85f, 0.85f, 0.9f);
            mr.material = mat;

            // Remove auto-added CapsuleCollider; replace with SphereCollider trigger at the tip
            var capsuleCol = go.GetComponent<CapsuleCollider>();
            if (capsuleCol != null) Object.DestroyImmediate(capsuleCol);

            var sphereCol = go.AddComponent<SphereCollider>();
            sphereCol.radius = 0.15f;
            sphereCol.isTrigger = true;

            // Scale to pin/needle proportions: thin and tall
            go.transform.localScale = new Vector3(0.15f, 0.4f, 0.15f);

            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);

            Debug.Log($"[PrefabBuilder] Created Dart prefab (3D Cylinder pin) at {path}");
        }

        /// <summary>
        /// Holder prefab: Cube primitive + BoxCollider + HolderIdentifier.
        /// MagazineText floats above via TextMesh child.
        /// Used for visual holder representation. InputHandler raycasts for HolderIdentifier.
        /// </summary>
        private static void BuildHolderPrefab()
        {
            string path = PREFAB_FOLDER + "/Holder.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                return;
            }

            // CreatePrimitive auto-adds MeshFilter, MeshRenderer, BoxCollider
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Holder";

            // Assign Standard material — dark slate color for the holder body
            var mr = go.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.3f, 0.3f, 0.4f);
            mr.material = mat;

            // BoxCollider is auto-added by CreatePrimitive — keep as-is (not trigger)
            var col = go.GetComponent<BoxCollider>();
            col.isTrigger = false;

            // HolderIdentifier for InputHandler raycast identification
            go.AddComponent<HolderIdentifier>();

            // Scale to a flat platform shape
            go.transform.localScale = new Vector3(0.8f, 0.5f, 0.8f);

            // Magazine count text display — floats above the holder
            var textGO = new GameObject("MagazineText");
            textGO.transform.SetParent(go.transform, false);
            textGO.transform.localPosition = new Vector3(0, 0.5f, 0);
            var tm = textGO.AddComponent<TextMesh>();
            tm.text = "0";
            tm.fontSize = 48;
            tm.characterSize = 0.15f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            tm.fontStyle = FontStyle.Bold;

            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);

            Debug.Log($"[PrefabBuilder] Created Holder prefab (3D Cube) at {path}");
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string[] parts = path.Split('/');
                string current = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                    {
                        AssetDatabase.CreateFolder(current, parts[i]);
                    }
                    current = next;
                }
            }
        }
    }
}
#endif
