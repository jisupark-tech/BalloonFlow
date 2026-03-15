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
        private const float PERPENDICULAR_TOLERANCE = 0.55f;
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

            // Find the NEAREST balloon along the firing direction that is:
            // 1. In the same column/row (within perpendicular tolerance)
            // 2. The first one the dart would hit (closest along firing axis)
            // 3. Not blocked by a different-color balloon
            int bestId = -1;
            float bestPerpendicularDist = float.MaxValue;
            float bestFiringDist = float.MaxValue;

            Debug.Log($"[DirectionalTargeting] FindTarget: pos={holderPosition}, dir={movementDirection}, scanDir={scanDir}, color={color}, candidates={candidates.Length}");

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

                // Check perpendicular distance (column/row alignment)
                float perpDist = GetPerpendicularDistance(holderPosition, balloon.position, scanDir);
                if (perpDist > PERPENDICULAR_TOLERANCE)
                {
                    continue;
                }

                // Check that balloon is in the firing direction (not behind the holder)
                float firingDist = GetFiringAxisDistance(holderPosition, balloon.position, scanDir);
                if (firingDist < 0f)
                {
                    continue; // Balloon is behind the holder
                }

                // Check the balloon is the nearest outermost from the holder's perspective
                // (no same-color or different-color balloon closer along the firing axis in same column)
                if (!IsNearestInColumn(balloon.position, scanDir, occupancy, firingDist))
                {
                    continue;
                }

                // Line-of-sight check: reject if any different-color balloon blocks the dart path
                if (!HasClearLineOfSight(holderPosition, balloon.position, color))
                {
                    continue;
                }

                // Prefer closest along firing axis, then closest perpendicular
                if (firingDist < bestFiringDist || (Mathf.Approximately(firingDist, bestFiringDist) && perpDist < bestPerpendicularDist))
                {
                    bestFiringDist = firingDist;
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

        // IsOutermost removed — replaced by IsNearestInColumn which scans from holder side inward

        /// <summary>
        /// Returns the signed distance along the firing axis from holder to balloon.
        /// Positive means the balloon is in front (in firing direction), negative means behind.
        /// </summary>
        private static float GetFiringAxisDistance(Vector3 holderPos, Vector3 balloonPos, ScanDirection direction)
        {
            switch (direction)
            {
                case ScanDirection.Right:
                    return balloonPos.x - holderPos.x;
                case ScanDirection.Left:
                    return holderPos.x - balloonPos.x;
                case ScanDirection.Up:
                    return balloonPos.z - holderPos.z;
                case ScanDirection.Down:
                    return holderPos.z - balloonPos.z;
                default:
                    return float.MaxValue;
            }
        }

        /// <summary>
        /// Checks if a balloon is the nearest (first hit) in its column/row from the holder's side.
        /// Returns true if no other non-popped balloon is closer to the holder along the firing axis
        /// in the same grid column/row.
        /// </summary>
        private static bool IsNearestInColumn(Vector3 balloonPosition, ScanDirection direction, HashSet<Vector2Int> occupancy, float balloonFiringDist)
        {
            Vector2Int cell = WorldToGrid(balloonPosition);

            // Check if any occupied cell is between the rail edge and this balloon
            // along the firing axis. If so, this balloon is NOT the nearest.
            switch (direction)
            {
                case ScanDirection.Up: // Firing north (+Z), nearest = lowest Z first
                    for (int y = cell.y - 1; y >= cell.y - 20; y--)
                    {
                        if (occupancy.Contains(new Vector2Int(cell.x, y)))
                            return false; // Something closer to the south rail
                    }
                    return true;

                case ScanDirection.Down: // Firing south (-Z), nearest = highest Z first
                    for (int y = cell.y + 1; y <= cell.y + 20; y++)
                    {
                        if (occupancy.Contains(new Vector2Int(cell.x, y)))
                            return false;
                    }
                    return true;

                case ScanDirection.Right: // Firing east (+X), nearest = lowest X first
                    for (int x = cell.x - 1; x >= cell.x - 20; x--)
                    {
                        if (occupancy.Contains(new Vector2Int(x, cell.y)))
                            return false;
                    }
                    return true;

                case ScanDirection.Left: // Firing west (-X), nearest = highest X first
                    for (int x = cell.x + 1; x <= cell.x + 20; x++)
                    {
                        if (occupancy.Contains(new Vector2Int(x, cell.y)))
                            return false;
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
