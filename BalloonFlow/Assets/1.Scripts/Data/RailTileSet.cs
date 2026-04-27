using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Holds the 6 rail tile sprites for conveyor belt visualization.
    /// 4 corners (bl, br, tl, tr) + 2 straight (h, v).
    /// Tiles are center-aligned and placed seamlessly along the conveyor path.
    /// Loaded at runtime via Resources.Load("RailTileSet").
    /// Created by editor menu: BalloonFlow > Setup Rail Tiles
    /// </summary>
    [CreateAssetMenu(fileName = "RailTileSet", menuName = "BalloonFlow/Rail Tile Set")]
    public class RailTileSet : ScriptableObject
    {
        [Header("Straight Tiles (center-aligned)")]
        public Sprite tileH;   // horizontal straight
        public Sprite tileHTop;     // horizontal straight — top row of conveyor (rail_corner_h_t)
        public Sprite tileHBottom;  // horizontal straight — bottom row of conveyor (rail_corner_h_b)
        public Sprite tileVL;       // vertical straight — left column of conveyor
        public Sprite tileVR;       // vertical straight — right column of conveyor

        [Header("Corner Tiles (center-aligned)")]
        public Sprite tileBL;  // bottom-left corner
        public Sprite tileBR;  // bottom-right corner
        public Sprite tileTL;  // top-left corner
        public Sprite tileTR;  // top-right corner

        [Header("Cap Tiles (개방 경로 끝점)")]
        public Sprite capB;    // 위→아래 끝점 (하단 마침)
        public Sprite capT;    // 아래→위 끝점 (상단 마침)
        public Sprite capL;    // 오른쪽→왼쪽 끝점 (좌측 마침)
        public Sprite capR;    // 왼쪽→오른쪽 끝점 (우측 마침)

        [Header("Cave Tiles (터널 — 개방 끝점 위에 덮음)")]
        public Sprite caveB;   // rail_cave_b
        public Sprite caveT;   // rail_cave_t
        public Sprite caveL;   // rail_cave_l
        public Sprite caveR;   // rail_cave_r

        [Header("Danger Tiles (위급 알람 — 기존 타일 위에 겹침)")]
        public Sprite dangerH;     // rail_corner_h_danger
        public Sprite dangerV;     // rail_corner_v_danger
        public Sprite dangerBL;    // rail_corner_bl_danger
        public Sprite dangerBR;    // rail_corner_br_danger
        public Sprite dangerTL;    // rail_corner_tl_danger
        public Sprite dangerTR;    // rail_corner_tr_danger
        public Sprite dangerCapB;  // rail_cap_b_danger
        public Sprite dangerCapT;  // rail_cap_t_danger
        public Sprite dangerCapL;  // rail_cap_l_danger
        public Sprite dangerCapR;  // rail_cap_r_danger

        // Legacy references — kept for backward compatibility with old RailTileSet assets
        [HideInInspector] public Sprite tileBH;
        [HideInInspector] public Sprite tileTH;
        [HideInInspector] public Sprite tileV;   // legacy: single vertical sprite, fallback when VL/VR not set

        /// <summary>
        /// Returns the correct tile sprite for a conveyor path segment.
        /// Analyzes neighbors to determine corner vs straight.
        /// </summary>
        public Sprite GetTileForCell(bool[,] grid, int col, int row, int cols, int rows)
        {
            bool hasUp    = (row + 1 < rows) && grid[col, row + 1];
            bool hasDown  = (row - 1 >= 0)   && grid[col, row - 1];
            bool hasLeft  = (col - 1 >= 0)   && grid[col - 1, row];
            bool hasRight = (col + 1 < cols)  && grid[col + 1, row];

            int midCol = (cols - 1) / 2;

            // Corners: exactly 2 neighbors at a right angle
            if (hasRight && hasUp    && !hasLeft && !hasDown) return tileBL;
            if (hasLeft  && hasUp    && !hasRight && !hasDown) return tileBR;
            if (hasRight && hasDown  && !hasLeft && !hasUp)   return tileTL;
            if (hasLeft  && hasDown  && !hasRight && !hasUp)  return tileTR;

            // Straight segments
            if (hasLeft && hasRight) return GetH();
            if (hasUp   && hasDown)  return col <= midCol ? GetVL() : GetVR();

            // Single-neighbor fallback
            if (hasLeft || hasRight) return GetH();
            if (hasUp   || hasDown)  return col <= midCol ? GetVL() : GetVR();

            return GetH(); // default
        }

        /// <summary>
        /// Returns the correct tile for a waypoint-based path segment.
        /// inDir/outDir are normalized direction vectors of the path at this point.
        /// </summary>
        public Sprite GetTileForDirections(Vector3 inDir, Vector3 outDir)
        {
            bool inH = Mathf.Abs(inDir.x) > Mathf.Abs(inDir.z);
            bool outH = Mathf.Abs(outDir.x) > Mathf.Abs(outDir.z);

            // Straight: same axis
            if (inH && outH) return GetH();
            if (!inH && !outH) return GetV();

            // Corner: determine which one based on directions
            // inDir → outDir turn direction
            float cross = inDir.x * outDir.z - inDir.z * outDir.x;

            if (inH)
            {
                // Was going horizontal, now turning vertical
                if (inDir.x > 0) // going right
                    return cross > 0 ? tileTR : tileBR;
                else // going left
                    return cross > 0 ? tileBL : tileTL;
            }
            else
            {
                // Was going vertical, now turning horizontal
                if (inDir.z > 0) // going up
                    return cross > 0 ? tileTL : tileTR;
                else // going down
                    return cross > 0 ? tileBR : tileBL;
            }
        }

        /// <summary>Returns horizontal straight tile (new tileH, fallback to legacy tileBH).</summary>
        public Sprite GetH() => tileH != null ? tileH : tileBH;

        /// <summary>Left vertical column tile. Falls back to legacy tileV, then tileVR.</summary>
        public Sprite GetVL() => tileVL != null ? tileVL : (tileV != null ? tileV : tileVR);

        /// <summary>Right vertical column tile. Falls back to legacy tileV, then tileVL.</summary>
        public Sprite GetVR() => tileVR != null ? tileVR : (tileV != null ? tileV : tileVL);

        /// <summary>Backward-compat vertical accessor — delegates to GetVL().</summary>
        public Sprite GetV() => GetVL();

        // Resources 폴백 캐시 — 매번 Load를 타지 않도록 저장.
        private static Sprite _cachedHTop;
        private static Sprite _cachedHBottom;

        /// <summary>컨베이어 상단 수평 타일. 직렬화 필드가 비어 있으면 Resources/Tiles/rail_corner_h_t 폴백.</summary>
        public Sprite GetHTop()
        {
            if (tileHTop != null) return tileHTop;
            if (_cachedHTop == null)
            {
                var tile = Resources.Load<UnityEngine.Tilemaps.Tile>("Tiles/rail_corner_h_t");
                if (tile != null) _cachedHTop = tile.sprite;
            }
            return _cachedHTop != null ? _cachedHTop : GetH();
        }

        /// <summary>컨베이어 하단 수평 타일. 직렬화 필드가 비어 있으면 Resources/Tiles/rail_corner_h_b 폴백.</summary>
        public Sprite GetHBottom()
        {
            if (tileHBottom != null) return tileHBottom;
            if (_cachedHBottom == null)
            {
                var tile = Resources.Load<UnityEngine.Tilemaps.Tile>("Tiles/rail_corner_h_b");
                if (tile != null) _cachedHBottom = tile.sprite;
            }
            return _cachedHBottom != null ? _cachedHBottom : GetH();
        }
    }
}
