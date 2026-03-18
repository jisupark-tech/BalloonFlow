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
        public Sprite tileV;   // vertical straight

        [Header("Corner Tiles (center-aligned)")]
        public Sprite tileBL;  // bottom-left corner
        public Sprite tileBR;  // bottom-right corner
        public Sprite tileTL;  // top-left corner
        public Sprite tileTR;  // top-right corner

        // Legacy references — kept for backward compatibility with old RailTileSet assets
        [HideInInspector] public Sprite tileBH;
        [HideInInspector] public Sprite tileTH;
        [HideInInspector] public Sprite tileVL;
        [HideInInspector] public Sprite tileVR;

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

            // Corners: exactly 2 neighbors at a right angle
            if (hasRight && hasUp    && !hasLeft && !hasDown) return tileBL;
            if (hasLeft  && hasUp    && !hasRight && !hasDown) return tileBR;
            if (hasRight && hasDown  && !hasLeft && !hasUp)   return tileTL;
            if (hasLeft  && hasDown  && !hasRight && !hasUp)  return tileTR;

            // Straight segments
            if (hasLeft && hasRight) return GetH();
            if (hasUp   && hasDown)  return GetV();

            // Single-neighbor fallback
            if (hasLeft || hasRight) return GetH();
            if (hasUp   || hasDown)  return GetV();

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

        /// <summary>Returns vertical straight tile (new tileV, fallback to legacy tileVL).</summary>
        public Sprite GetV() => tileV != null ? tileV : tileVL;
    }
}
