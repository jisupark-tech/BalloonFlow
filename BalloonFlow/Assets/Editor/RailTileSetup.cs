#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// Creates the RailTileSet ScriptableObject asset in Resources,
    /// wiring the 8 rail tile sprites from Assets/2.Sprite/UI/tile/.
    /// Menu: BalloonFlow > Setup Rail Tiles
    /// </summary>
    public static class RailTileSetup
    {
        private const string SPRITE_BASE = "Assets/2.Sprite/UI/tile/";
        private const string ASSET_PATH  = "Assets/Resources/RailTileSet.asset";

        [MenuItem("BalloonFlow/Setup Rail Tiles")]
        public static void Setup()
        {
            var tileSet = AssetDatabase.LoadAssetAtPath<RailTileSet>(ASSET_PATH);
            if (tileSet == null)
            {
                tileSet = ScriptableObject.CreateInstance<RailTileSet>();
                AssetDatabase.CreateAsset(tileSet, ASSET_PATH);
            }

            tileSet.tileBH = LoadSprite("rail_corner_bh");
            tileSet.tileTH = LoadSprite("rail_corner_th");
            tileSet.tileVL = LoadSprite("rail_corner_vl");
            tileSet.tileVR = LoadSprite("rail_corner_vr");
            tileSet.tileBL = LoadSprite("rail_corner_bl");
            tileSet.tileBR = LoadSprite("rail_corner_br");
            tileSet.tileTL = LoadSprite("rail_corner_tl");
            tileSet.tileTR = LoadSprite("rail_corner_tr");

            EditorUtility.SetDirty(tileSet);
            AssetDatabase.SaveAssets();

            int count = 0;
            if (tileSet.tileBH) count++;
            if (tileSet.tileTH) count++;
            if (tileSet.tileVL) count++;
            if (tileSet.tileVR) count++;
            if (tileSet.tileBL) count++;
            if (tileSet.tileBR) count++;
            if (tileSet.tileTL) count++;
            if (tileSet.tileTR) count++;

            Debug.Log($"[RailTileSetup] RailTileSet created at {ASSET_PATH} — {count}/8 sprites wired.");

            if (count < 8)
            {
                Debug.LogWarning($"[RailTileSetup] Missing sprites! Check {SPRITE_BASE} folder.");
            }
        }

        private static Sprite LoadSprite(string name)
        {
            string path = SPRITE_BASE + name + ".png";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                Debug.LogWarning($"[RailTileSetup] Sprite not found: {path}");
            }
            return sprite;
        }
    }
}
#endif
