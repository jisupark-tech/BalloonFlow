using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Static utility for direction-dependent outermost balloon targeting.
    /// Determines which balloon a dart on the rail should fire at based on its
    /// cardinal firing direction and color matching.
    ///
    /// Targeting rules (Rail Overflow spec):
    /// 1. Dart fires STRAIGHT inward (N/S/E/W cardinal only)
    /// 2. Target must be same color as dart
    /// 3. Target must be the outermost balloon in its grid column/row (no balloon of ANY color in front)
    /// 4. Target must be within half a cell spacing of the dart's perpendicular axis (tight column alignment)
    /// 5. No line-of-sight check needed — outermost guarantee means nothing blocks
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Helper | Phase: 1
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow
    /// </remarks>
    public static class DirectionalTargeting
    {
        #region Constants

        private const float DEFAULT_GRID_CELL_SIZE = 0.55f;

        #endregion

        #region Fields

        /// <summary>
        /// Cached grid cell size from GameManager.Board.cellSpacing.
        /// Updated once per frame on first FindTarget call.
        /// </summary>
        private static float _gridCellSize = DEFAULT_GRID_CELL_SIZE;

        /// <summary>
        /// Per-frame cached occupancy map. Rebuilt once per frame, reused across all FindTarget calls.
        /// </summary>
        private static HashSet<Vector2Int> _cachedOccupancy;
        private static int _cachedOccupancyFrame = -1;

        /// <summary>
        /// Per-frame cached same-color balloon lists to avoid repeated array allocation.
        /// Key = color index, Value = list of BalloonData.
        /// </summary>
        private static readonly Dictionary<int, List<BalloonData>> _cachedColorBalloons = new Dictionary<int, List<BalloonData>>();
        private static int _cachedColorFrame = -1;

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
        /// Returns the best target balloon ID for a dart at the given position
        /// firing in the given cardinal direction, matching the specified color.
        /// Returns -1 if no valid target is found.
        ///
        /// Rail Overflow mode: strict outermost-only + tight column alignment.
        /// </summary>
        public static int FindTarget(Vector3 dartPosition, Vector3 firingDirection, int color, HashSet<int> excludeIds = null)
        {
            if (!BalloonController.HasInstance)
            {
                return -1;
            }

            // Update cell spacing once per frame (프레임 캐시)
            if (_cachedOccupancyFrame != Time.frameCount && GameManager.HasInstance)
            {
                _gridCellSize = GameManager.Instance.Board.cellSpacing;
            }

            // Get cached same-color balloons (avoids array allocation per call)
            List<BalloonData> candidates = GetCachedBalloonsByColor(color);
            if (candidates == null || candidates.Count == 0)
            {
                return -1;
            }

            // Tight tolerance: half cell spacing — dart must be nearly aligned with balloon column
            float perpendicularTolerance = _gridCellSize * 0.5f;

            ScanDirection scanDir = DetermineScanDirection(firingDirection);

            // Build per-frame occupancy set
            HashSet<Vector2Int> occupancy = BuildOccupancyMap();

            int bestId = -1;
            float bestFiringDist = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                BalloonData balloon = candidates[i];
                if (balloon.isPopped) continue;

                // Wall balloons are indestructible — never target them
                if (balloon.gimmickType == BalloonController.GimmickWall) continue;

                // Pin balloons block darts — only removable by adjacent pop
                if (balloon.gimmickType == BalloonController.GimmickPin) continue;

                // Ice balloons must be thawed by adjacent pop first — not directly targetable
                if (balloon.gimmickType == BalloonController.GimmickIce) continue;

                if (excludeIds != null && excludeIds.Contains(balloon.balloonId)) continue;

                // Check perpendicular distance (strict column alignment)
                float perpDist = GetPerpendicularDistance(dartPosition, balloon.position, scanDir);
                if (perpDist > perpendicularTolerance) continue;

                // Check that balloon is in front of the dart (in firing direction)
                float firingDist = GetFiringAxisDistance(dartPosition, balloon.position, scanDir);
                if (firingDist < 0f) continue;

                // No penetration: check if any balloon (any color) blocks the path
                // between the dart and this target
                if (IsPathBlocked(dartPosition, balloon.position, scanDir, occupancy)) continue;

                // Closest target wins
                if (firingDist < bestFiringDist)
                {
                    bestFiringDist = firingDist;
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
            float absZ = Mathf.Abs(movementDirection.z);

            if (absX >= absZ)
            {
                return movementDirection.x >= 0f ? ScanDirection.Right : ScanDirection.Left;
            }
            else
            {
                return movementDirection.z >= 0f ? ScanDirection.Up : ScanDirection.Down;
            }
        }

        #endregion

        #region Private Methods — Caching

        /// <summary>
        /// Returns cached list of same-color non-popped balloons.
        /// Rebuilt once per frame to avoid array allocation on every FindTarget call.
        /// </summary>
        private static List<BalloonData> GetCachedBalloonsByColor(int color)
        {
            int currentFrame = Time.frameCount;
            if (_cachedColorFrame != currentFrame)
            {
                // 재사용: 기존 리스트 Clear. 사용되지 않는 색상 키 축적 방지.
                if (_cachedColorBalloons.Count > 16)
                    _cachedColorBalloons.Clear(); // 키 과다 축적 시 리셋
                else
                    foreach (var kvp in _cachedColorBalloons) kvp.Value.Clear();
                _cachedColorFrame = currentFrame;

                if (BalloonController.HasInstance)
                {
                    BalloonData[] all = BalloonController.Instance.GetAllBalloons();
                    if (all != null)
                    {
                        for (int i = 0; i < all.Length; i++)
                        {
                            if (all[i].isPopped) continue;

                            int c = all[i].color;
                            if (!_cachedColorBalloons.TryGetValue(c, out List<BalloonData> list))
                            {
                                list = new List<BalloonData>(64);
                                _cachedColorBalloons[c] = list;
                            }
                            list.Add(all[i]);
                        }
                    }
                }
            }

            _cachedColorBalloons.TryGetValue(color, out List<BalloonData> result);
            return result;
        }

        /// <summary>
        /// Builds a 2D occupancy grid from all active (non-popped) balloons.
        /// Cached per frame to avoid rebuilding for every slot scan.
        /// </summary>
        private static HashSet<Vector2Int> BuildOccupancyMap()
        {
            int currentFrame = Time.frameCount;
            if (_cachedOccupancy != null && _cachedOccupancyFrame == currentFrame)
            {
                return _cachedOccupancy;
            }

            // 재사용: new 대신 Clear
            if (_cachedOccupancy == null)
                _cachedOccupancy = new HashSet<Vector2Int>();
            else
                _cachedOccupancy.Clear();

            if (BalloonController.HasInstance)
            {
                BalloonData[] allBalloons = BalloonController.Instance.GetAllBalloons();
                if (allBalloons != null)
                {
                    for (int i = 0; i < allBalloons.Length; i++)
                    {
                        if (!allBalloons[i].isPopped)
                        {
                            _cachedOccupancy.Add(WorldToGrid(allBalloons[i].position));
                        }
                    }
                }
            }

            _cachedOccupancyFrame = currentFrame;
            return _cachedOccupancy;
        }

        #endregion

        #region Private Methods — Targeting Math

        /// <summary>
        /// Returns the signed distance along the firing axis from dart to balloon.
        /// Positive means the balloon is in front (in firing direction), negative means behind.
        /// </summary>
        private static float GetFiringAxisDistance(Vector3 dartPos, Vector3 balloonPos, ScanDirection direction)
        {
            switch (direction)
            {
                case ScanDirection.Right: return balloonPos.x - dartPos.x;
                case ScanDirection.Left:  return dartPos.x - balloonPos.x;
                case ScanDirection.Up:    return balloonPos.z - dartPos.z;
                case ScanDirection.Down:  return dartPos.z - balloonPos.z;
                default:                  return float.MaxValue;
            }
        }

        /// <summary>
        /// Checks if any balloon (any color) occupies a grid cell between
        /// the dart position and the target balloon along the firing axis.
        /// Returns true if the path is BLOCKED (dart cannot reach target).
        /// </summary>
        private static bool IsPathBlocked(Vector3 dartPos, Vector3 targetPos, ScanDirection direction, HashSet<Vector2Int> occupancy)
        {
            Vector2Int targetCell = WorldToGrid(targetPos);

            // 타겟 셀에서 레일 방향(바깥)으로 스캔. 타겟 앞에 다른 풍선이 있으면 차단.
            // (다트 위치는 레일 위라 그리드 밖 → 타겟 기준으로 스캔)
            switch (direction)
            {
                case ScanDirection.Up: // 레일이 아래, 위로 발사 → 타겟 아래에 뭐가 있으면 차단
                    for (int y = targetCell.y - 1; y >= targetCell.y - 30; y--)
                    {
                        if (occupancy.Contains(new Vector2Int(targetCell.x, y)))
                            return true;
                    }
                    return false;

                case ScanDirection.Down: // 레일이 위, 아래로 발사 → 타겟 위에 뭐가 있으면 차단
                    for (int y = targetCell.y + 1; y <= targetCell.y + 30; y++)
                    {
                        if (occupancy.Contains(new Vector2Int(targetCell.x, y)))
                            return true;
                    }
                    return false;

                case ScanDirection.Right: // 레일이 왼쪽, 오른쪽으로 발사 → 타겟 왼쪽에 뭐가 있으면 차단
                    for (int x = targetCell.x - 1; x >= targetCell.x - 30; x--)
                    {
                        if (occupancy.Contains(new Vector2Int(x, targetCell.y)))
                            return true;
                    }
                    return false;

                case ScanDirection.Left: // 레일이 오른쪽, 왼쪽으로 발사 → 타겟 오른쪽에 뭐가 있으면 차단
                    for (int x = targetCell.x + 1; x <= targetCell.x + 30; x++)
                    {
                        if (occupancy.Contains(new Vector2Int(x, targetCell.y)))
                            return true;
                    }
                    return false;

                default:
                    return true;
            }
        }

        /// <summary>
        /// Returns the perpendicular distance between the dart and a balloon
        /// relative to the scan direction axis (XZ plane).
        /// For horizontal firing (Right/Left), perpendicular = Z difference.
        /// For vertical firing (Up/Down), perpendicular = X difference.
        /// </summary>
        private static float GetPerpendicularDistance(Vector3 dartPos, Vector3 balloonPos, ScanDirection direction)
        {
            switch (direction)
            {
                case ScanDirection.Right:
                case ScanDirection.Left:
                    return Mathf.Abs(dartPos.z - balloonPos.z);

                case ScanDirection.Up:
                case ScanDirection.Down:
                    return Mathf.Abs(dartPos.x - balloonPos.x);

                default:
                    return float.MaxValue;
            }
        }

        /// <summary>
        /// Converts a world XZ position to a 2D grid cell coordinate.
        /// Uses actual cell spacing so that each balloon column/row maps to a unique grid cell.
        /// </summary>
        private static Vector2Int WorldToGrid(Vector3 worldPos)
        {
            float cs = _gridCellSize > 0.01f ? _gridCellSize : DEFAULT_GRID_CELL_SIZE;
            return new Vector2Int(
                Mathf.RoundToInt(worldPos.x / cs),
                Mathf.RoundToInt(worldPos.z / cs)
            );
        }

        #endregion
    }
}
