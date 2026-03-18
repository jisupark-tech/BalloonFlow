#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.Tilemaps;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// Creates the RailTileSet ScriptableObject asset in Resources,
    /// wiring the 6 rail tile sprites from Assets/2.Sprite/UI/tile/.
    /// Also creates Tile assets in Resources/Tiles/ for BoardTileManager.
    /// Menu: BalloonFlow > Setup Rail Tiles
    /// </summary>
    public static class RailTileSetup
    {
        private const string SPRITE_BASE = "Assets/2.Sprite/UI/tile/";
        private const string ASSET_PATH  = "Assets/Resources/RailTileSet.asset";
        private const string TILES_DIR   = "Assets/Resources/Tiles/";

        [MenuItem("BalloonFlow/DON'T USE/Setup Rail Tiles")]
        public static void Setup()
        {
            var tileSet = AssetDatabase.LoadAssetAtPath<RailTileSet>(ASSET_PATH);
            if (tileSet == null)
            {
                tileSet = ScriptableObject.CreateInstance<RailTileSet>();
                AssetDatabase.CreateAsset(tileSet, ASSET_PATH);
            }

            // New 6-tile system (h, v, bl, br, tl, tr) — from git-updated art
            tileSet.tileH  = LoadSprite("rail_corner_h");
            tileSet.tileV  = LoadSprite("rail_corner_v");
            tileSet.tileBL = LoadSprite("rail_corner_bl");
            tileSet.tileBR = LoadSprite("rail_corner_br");
            tileSet.tileTL = LoadSprite("rail_corner_tl");
            tileSet.tileTR = LoadSprite("rail_corner_tr");

            // Legacy fields — same sprites for backward compatibility
            tileSet.tileBH = tileSet.tileH;
            tileSet.tileTH = tileSet.tileH;
            tileSet.tileVL = tileSet.tileV;
            tileSet.tileVR = tileSet.tileV;

            EditorUtility.SetDirty(tileSet);
            AssetDatabase.SaveAssets();

            int count = 0;
            if (tileSet.tileH)  count++;
            if (tileSet.tileV)  count++;
            if (tileSet.tileBL) count++;
            if (tileSet.tileBR) count++;
            if (tileSet.tileTL) count++;
            if (tileSet.tileTR) count++;

            Debug.Log($"[RailTileSetup] RailTileSet updated at {ASSET_PATH} — {count}/6 sprites wired.");

            if (count < 6)
            {
                Debug.LogWarning($"[RailTileSetup] Missing sprites! Check {SPRITE_BASE} folder for: rail_corner_h, rail_corner_v, rail_corner_bl, rail_corner_br, rail_corner_tl, rail_corner_tr");
            }

            // Also create Tile assets in Resources/Tiles/ for BoardTileManager
            CreateTileAssets(tileSet);
        }

        /// <summary>
        /// Creates 6 Tile assets in Resources/Tiles/ so BoardTileManager
        /// can load them via Resources.Load at runtime.
        /// </summary>
        private static void CreateTileAssets(RailTileSet tileSet)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Tiles"))
                AssetDatabase.CreateFolder("Assets/Resources", "Tiles");

            string[] names = { "h", "v", "bl", "br", "tl", "tr" };
            Sprite[] sprites = { tileSet.tileH, tileSet.tileV, tileSet.tileBL, tileSet.tileBR, tileSet.tileTL, tileSet.tileTR };

            int created = 0;
            for (int i = 0; i < names.Length; i++)
            {
                if (sprites[i] == null) continue;
                string tilePath = TILES_DIR + "rail_corner_" + names[i] + ".asset";
                var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
                if (tile == null)
                {
                    tile = ScriptableObject.CreateInstance<Tile>();
                    AssetDatabase.CreateAsset(tile, tilePath);
                }
                tile.sprite = sprites[i];
                tile.color = Color.white;
                EditorUtility.SetDirty(tile);
                created++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[RailTileSetup] Created/updated {created} Tile assets in {TILES_DIR}");
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
