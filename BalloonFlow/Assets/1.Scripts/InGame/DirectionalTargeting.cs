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

            // 수직 정렬 허용치: cellSpacing의 75% — 벨트 고속 시에도 타겟 놓치지 않도록
            float perpendicularTolerance = _gridCellSize * 0.75f;

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

                // Pin — 같은 색 다트로 직접 타격 가능 (타겟팅에서 제외하지 않음)

                // Ice balloons must be thawed by adjacent pop first — not directly targetable
                if (balloon.gimmickType == BalloonController.GimmickIce) continue;

                // Color Curtain — 간접 제거만 가능 (해당 색 풍선 팝 시 카운터 감소)
                if (balloon.gimmickType == BalloonController.GimmickColorCurtain) continue;

                if (excludeIds != null && excludeIds.Contains(balloon.balloonId)) continue;

                // 실제 월드 위치 사용 (배율/오프셋 적용 후)
                Vector3 balloonWorldPos = BalloonController.HasInstance
                    ? BalloonController.Instance.GetBalloonWorldPosition(balloon.balloonId)
                    : balloon.position;

                // Check perpendicular distance (strict column alignment)
                float perpDist = GetPerpendicularDistance(dartPosition, balloonWorldPos, scanDir);
                if (perpDist > perpendicularTolerance) continue;

                // Check that balloon is in front of the dart (in firing direction)
                float firingDist = GetFiringAxisDistance(dartPosition, balloonWorldPos, scanDir);
                if (firingDist < 0f) continue;

                // outermost 규칙 — 앞에 풍선(아무 색)이 있으면 타겟 불가
                if (IsPathBlocked(dartPosition, balloonWorldPos, scanDir, occupancy)) continue;

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
        /// <summary>재사용 Vector2Int (GC 방지)</summary>
        private static Vector2Int _reusableScanCell;

        /// <summary>캐시된 maxScan (프레임당 1회 계산)</summary>
        private static int _cachedMaxScan;
        private static int _cachedMaxScanFrame = -1;

        /// <summary>
        /// 다트와 타겟 사이에 다른 풍선(아무 색)이 있는지 월드 좌표 기반으로 체크.
        /// 그리드 반올림 없이 직접 거리 비교 — 관통 불가.
        /// </summary>
        private static bool IsPathBlocked(Vector3 dartPos, Vector3 targetPos, ScanDirection direction, HashSet<Vector2Int> occupancy)
        {
            if (!BalloonController.HasInstance) return false;

            float perpTolerance = _gridCellSize * 0.75f;
            float targetFiringDist = GetFiringAxisDistance(dartPos, targetPos, direction);
            if (targetFiringDist <= 0f) return false;

            BalloonData[] all = BalloonController.Instance.GetAllBalloons();
            if (all == null) return false;

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].isPopped) continue;

                // 실제 월드 위치 사용
                Vector3 bPos = BalloonController.Instance.GetBalloonWorldPosition(all[i].balloonId);
                if (Vector3.Distance(bPos, targetPos) < 0.01f) continue;

                // 수직 거리 체크 (같은 열/행인지)
                float perpDist = GetPerpendicularDistance(dartPos, bPos, direction);
                if (perpDist > perpTolerance) continue;

                // 발사축 거리 체크 (다트와 타겟 사이에 있는지)
                float firingDist = GetFiringAxisDistance(dartPos, bPos, direction);
                if (firingDist > 0f && firingDist < targetFiringDist)
                    return true; // 다트와 타겟 사이에 풍선 있음 → 차단
            }

            return false;
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
