using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Static utility for direction-dependent outermost balloon targeting.
    /// Determines which balloon a holder should fire at based on movement direction
    /// and color matching. Scans from the edge inward per direction to find
    /// the outermost unobstructed balloon of the matching color.
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Helper | Phase: 1
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow
    /// </remarks>
    public static class DirectionalTargeting
    {
        #region Constants

        private const float GRID_CELL_SIZE = 1f;
        private const float PERPENDICULAR_TOLERANCE = 0.4f;
        private const float LOS_CHECK_RADIUS = 0.4f; // Radius for line-of-sight obstruction check

        #endregion

        #region Enums

        /// <summary>
        /// Cardinal direction for targeting scan.
        /// In 3D XZ space: Up = Forward (positive Z), Down = Back (negative Z).
        /// </summary>
        public enum ScanDirection
        {
            Right,
            Up,    // Forward (positive Z)
            Left,
            Down   // Back (negative Z)
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns the best target balloon ID for a holder at the given position
        /// moving in the given direction, matching the specified color.
        /// Returns -1 if no valid target is found.
        /// </summary>
        /// <param name="holderPosition">Current world position of the holder on the rail.</param>
        /// <param name="movementDirection">Current movement direction along the rail.</param>
        /// <param name="color">Color index to match against balloons.</param>
        /// <returns>BalloonId of the best target, or -1 if none found.</returns>
        public static int FindTarget(Vector3 holderPosition, Vector3 movementDirection, int color, HashSet<int> excludeIds = null)
        {
            if (!BalloonController.HasInstance)
            {
                return -1;
            }

            BalloonData[] candidates = BalloonController.Instance.GetBalloonsByColor(color);
            if (candidates == null || candidates.Length == 0)
            {
                return -1;
            }

            ScanDirection scanDir = DetermineScanDirection(movementDirection);

            // Build occupancy set from all non-popped balloons for line-of-sight checks
            HashSet<Vector2Int> occupancy = BuildOccupancyMap();

            // Find outermost valid targets along the scan direction
            int bestId = -1;
            float bestPerpendicularDist = float.MaxValue;

            for (int i = 0; i < candidates.Length; i++)
            {
                BalloonData balloon = candidates[i];
                if (balloon.isPopped)
                {
                    continue;
                }

                // Skip balloons already fired at this side pass
                if (excludeIds != null && excludeIds.Contains(balloon.balloonId))
                {
                    continue;
                }

                // Check if this balloon is outermost (no other balloon blocks from the scan edge)
                if (!IsOutermost(balloon.position, scanDir, occupancy))
                {
                    continue;
                }

                // Among valid outermost targets, pick the one closest perpendicularly to the holder
                float perpDist = GetPerpendicularDistance(holderPosition, balloon.position, scanDir);
                if (perpDist > PERPENDICULAR_TOLERANCE)
                {
                    continue;
                }

                // Line-of-sight check: reject if any different-color balloon blocks the dart path
                if (!HasClearLineOfSight(holderPosition, balloon.position, color))
                {
                    continue;
                }

                if (perpDist < bestPerpendicularDist)
                {
                    bestPerpendicularDist = perpDist;
                    bestId = balloon.balloonId;
                }
            }

            return bestId;
        }

        /// <summary>
        /// Determines the primary cardinal scan direction from a movement vector.
        /// </summary>
        public static ScanDirection DetermineScanDirection(Vector3 movementDirection)
        {
            float absX = Mathf.Abs(movementDirection.x);
            float absY = Mathf.Abs(movementDirection.z);  // XZ plane: use Z for depth axis

            if (absX >= absY)
            {
                return movementDirection.x >= 0f ? ScanDirection.Right : ScanDirection.Left;
            }
            else
            {
                return movementDirection.z >= 0f ? ScanDirection.Up : ScanDirection.Down;  // Z >= 0 = Forward
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Checks if the dart flight path from holder to target balloon is clear of different-color balloons.
        /// Returns true if no different-color balloon obstructs the path.
        /// </summary>
        private static bool HasClearLineOfSight(Vector3 from, Vector3 to, int targetColor)
        {
            if (!BalloonController.HasInstance)
            {
                return true;
            }

            BalloonData[] allBalloons = BalloonController.Instance.GetAllBalloons();
            if (allBalloons == null)
            {
                return true;
            }

            Vector3 dir = to - from;
            float pathLength = dir.magnitude;
            if (pathLength < 0.01f)
            {
                return true;
            }

            Vector3 pathDir = dir / pathLength;

            for (int i = 0; i < allBalloons.Length; i++)
            {
                BalloonData balloon = allBalloons[i];
                if (balloon.isPopped || balloon.color == targetColor)
                {
                    continue; // Same-color balloons don't block
                }

                // Project balloon center onto the line segment from→to
                Vector3 balloonPos = balloon.position;
                Vector3 fromToBalloon = balloonPos - from;
                float projT = Vector3.Dot(fromToBalloon, pathDir);

                // Only check balloons between holder and target (with small margin)
                if (projT < 0.5f || projT > pathLength - 0.3f)
                {
                    continue;
                }

                // Perpendicular distance from balloon center to the line
                Vector3 closestPoint = from + pathDir * projT;
                float perpDist = Vector3.Distance(balloonPos, closestPoint);

                if (perpDist < LOS_CHECK_RADIUS)
                {
                    return false; // Different-color balloon blocks the path
                }
            }

            return true;
        }

        /// <summary>
        /// Builds a 2D occupancy grid from all active (non-popped) balloons.
        /// </summary>
        private static HashSet<Vector2Int> BuildOccupancyMap()
        {
            HashSet<Vector2Int> occupancy = new HashSet<Vector2Int>();

            if (!BalloonController.HasInstance)
            {
                return occupancy;
            }

            BalloonData[] allBalloons = BalloonController.Instance.GetAllBalloons();
            if (allBalloons == null)
            {
                return occupancy;
            }

            for (int i = 0; i < allBalloons.Length; i++)
            {
                if (!allBalloons[i].isPopped)
                {
                    Vector2Int cell = WorldToGrid(allBalloons[i].position);
                    occupancy.Add(cell);
                }
            }

            return occupancy;
        }

        /// <summary>
        /// Checks whether a balloon at the given position is outermost relative to the scan direction.
        /// "Outermost" means no other balloon blocks the path between the rail edge and this balloon.
        ///
        /// For example, scanning from the RIGHT means we look for balloons that have no other
        /// balloon to their right (higher X) in the same row.
        /// </summary>
        private static bool IsOutermost(Vector3 balloonPosition, ScanDirection direction, HashSet<Vector2Int> occupancy)
        {
            Vector2Int cell = WorldToGrid(balloonPosition);

            // Determine the scan axis and step direction
            // We scan FROM the balloon TOWARD the edge. If any occupied cell is found between
            // the balloon and the edge, this balloon is NOT outermost.
            //
            // Actually, the correct interpretation: scan from the EDGE inward.
            // A balloon is outermost if scanning from the edge along the scan axis,
            // it is the first occupied cell in its row/column.
            //
            // Simplified: check if any occupied cell exists between the balloon and the
            // direction edge. We scan a reasonable range (up to 20 cells).

            const int MAX_SCAN_RANGE = 20;

            switch (direction)
            {
                case ScanDirection.Right:
                    // Moving right means darts approach from the right side.
                    // Outermost = no balloon with higher X in the same row.
                    for (int x = cell.x + 1; x <= cell.x + MAX_SCAN_RANGE; x++)
                    {
                        if (occupancy.Contains(new Vector2Int(x, cell.y)))
                        {
                            return false;
                        }
                    }
                    return true;

                case ScanDirection.Left:
                    // Outermost from left = no balloon with lower X in the same row.
                    for (int x = cell.x - 1; x >= cell.x - MAX_SCAN_RANGE; x--)
                    {
                        if (occupancy.Contains(new Vector2Int(x, cell.y)))
                        {
                            return false;
                        }
                    }
                    return true;

                case ScanDirection.Up:
                    // Outermost from top = no balloon with higher Y in the same column.
                    for (int y = cell.y + 1; y <= cell.y + MAX_SCAN_RANGE; y++)
                    {
                        if (occupancy.Contains(new Vector2Int(cell.x, y)))
                        {
                            return false;
                        }
                    }
                    return true;

                case ScanDirection.Down:
                    // Outermost from bottom = no balloon with lower Y in the same column.
                    for (int y = cell.y - 1; y >= cell.y - MAX_SCAN_RANGE; y--)
                    {
                        if (occupancy.Contains(new Vector2Int(cell.x, y)))
                        {
                            return false;
                        }
                    }
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns the perpendicular distance between the holder and a balloon
        /// relative to the scan direction axis (XZ plane).
        /// For horizontal movement (Right/Left), perpendicular = Z difference.
        /// For depth movement (Up=Forward/Down=Back), perpendicular = X difference.
        /// </summary>
        private static float GetPerpendicularDistance(Vector3 holderPos, Vector3 balloonPos, ScanDirection direction)
        {
            switch (direction)
            {
                case ScanDirection.Right:
                case ScanDirection.Left:
                    return Mathf.Abs(holderPos.z - balloonPos.z);  // XZ plane: Z is the depth/perpendicular axis

                case ScanDirection.Up:
                case ScanDirection.Down:
                    return Mathf.Abs(holderPos.x - balloonPos.x);

                default:
                    return float.MaxValue;
            }
        }

        /// <summary>
        /// Converts a world XZ position to a 2D grid cell coordinate.
        /// worldPos.x maps to grid.x; worldPos.z maps to grid.y (depth axis).
        /// </summary>
        private static Vector2Int WorldToGrid(Vector3 worldPos)
        {
            return new Vector2Int(
                Mathf.RoundToInt(worldPos.x / GRID_CELL_SIZE),
                Mathf.RoundToInt(worldPos.z / GRID_CELL_SIZE)  // XZ plane: Z → grid Y
            );
        }

        #endregion
    }
}
