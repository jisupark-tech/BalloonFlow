#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-time script: re-serializes LevelDatabase assets after switching to Mixed
/// serialization mode.  Run via  BalloonFlow ▸ Force Re-serialize Level Data,
/// then delete this file.
/// </summary>
public static class ForceReserializeLevelData
{
    [MenuItem("BalloonFlow/Force Re-serialize Level Data")]
    public static void Execute()
    {
        var paths = new[]
        {
            "Assets/Resources/LevelDatabase.asset",
            "Assets/EditorData/LevelDatabase_AI.asset",
            "Assets/EditorData/LevelDatabase_Transform.asset"
        };

        AssetDatabase.ForceReserializeAssets(paths);
        Debug.Log("[LevelData] Re-serialization complete – assets saved in Mixed mode.");
    }
}
#endif
