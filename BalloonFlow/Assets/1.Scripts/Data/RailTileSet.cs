using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Holds the 8 rail tile sprites for conveyor belt visualization.
    /// Loaded at runtime via Resources.Load("RailTileSet").
    /// Created by editor menu: BalloonFlow > Setup Rail Tiles
    /// </summary>
    [CreateAssetMenu(fileName = "RailTileSet", menuName = "BalloonFlow/Rail Tile Set")]
    public class RailTileSet : ScriptableObject
    {
        [Header("Straight Tiles")]
        public Sprite tileBH;  // bottom horizontal
        public Sprite tileTH;  // top horizontal
        public Sprite tileVL;  // vertical left
        public Sprite tileVR;  // vertical right

        [Header("Corner Tiles")]
        public Sprite tileBL;  // bottom-left corner
        public Sprite tileBR;  // bottom-right corner
        public Sprite tileTL;  // top-left corner
        public Sprite tileTR;  // top-right corner

        /// <summary>
        /// Picks the correct tile for a conveyor grid cell based on its neighbors.
        /// </summary>
        public Sprite GetTileForCell(bool[,] grid, int col, int row, int cols, int rows)
        {
            bool hasUp    = (row + 1 < rows) && grid[col, row + 1];
            bool hasDown  = (row - 1 >= 0)   && grid[col, row - 1];
            bool hasLeft  = (col - 1 >= 0)   && grid[col - 1, row];
            bool hasRight = (col + 1 < cols)  && grid[col + 1, row];

            // Corners: exactly 2 neighbors at a right angle
            if (hasRight && hasUp    && !hasLeft && !hasDown) return tileBL;  // bottom-left corner
            if (hasLeft  && hasUp    && !hasRight && !hasDown) return tileBR; // bottom-right corner
            if (hasRight && hasDown  && !hasLeft && !hasUp)   return tileTL;  // top-left corner
            if (hasLeft  && hasDown  && !hasRight && !hasUp)  return tileTR;  // top-right corner

            // Straight segments
            // VL sprite has track on left side → use on RIGHT edge (track faces inward)
            // VR sprite has track on right side → use on LEFT edge (track faces inward)
            if (hasLeft && hasRight) return (row == 0 || !hasDown) ? tileBH : tileTH;  // horizontal
            if (hasUp   && hasDown)  return (col == 0 || !hasLeft) ? tileVR : tileVL;  // vertical: swap VL/VR

            // Fallback for edge cases
            if (hasLeft || hasRight) return tileBH;
            if (hasUp   || hasDown)  return tileVR;

            return tileBH; // default
        }
    }
}
