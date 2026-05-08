using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Direction-based targeting for darts on the rail.
    /// Builds a per-frame edge cache so each dart checks only the outermost balloon
    /// on its aligned row/column instead of scanning all same-color balloons.
    /// </summary>
    public static class DirectionalTargeting
    {
        private const float DEFAULT_GRID_CELL_SIZE = 0.55f;
        private const int LINE_SEARCH_RADIUS = 2;
        private const int RECENT_LINE_PENALTY_FRAMES = 18;
        // 사용자 요구 (2026-05-07): "2행만 공격해야 하는데 1행도 공격" 이슈.
        // 1.35 → 0.7 로 strict 화 — dart 가 자기 perp 정렬 line 외 다른 line 의 balloon 후보로 안 잡힘.
        // line penalty 가 across-row 로 redirect 하는 부작용 차단. cellSpacing 만큼 떨어진 인접 row 는 제외 (perpDist > 0.7 × cellSpacing).
        private const float PERPENDICULAR_TOLERANCE_MULTIPLIER = 0.7f;
        private const float RECENT_LINE_PENALTY_MULTIPLIER = 2.25f;

        private static float _gridCellSize = DEFAULT_GRID_CELL_SIZE;

        private struct EdgeTarget
        {
            public int balloonId;
            public int color;
            public Vector3 worldPos;
            public Vector2Int cell;
            public bool targetable;
        }

        private static readonly Dictionary<Vector2Int, EdgeTarget> _occupiedCells = new Dictionary<Vector2Int, EdgeTarget>(2048);
        private static readonly HashSet<Vector2Int> _outsideCells = new HashSet<Vector2Int>();
        private static readonly Queue<Vector2Int> _floodQueue = new Queue<Vector2Int>();

        private static readonly Dictionary<int, EdgeTarget> _leftContourByRow = new Dictionary<int, EdgeTarget>(64);
        private static readonly Dictionary<int, EdgeTarget> _rightContourByRow = new Dictionary<int, EdgeTarget>(64);
        private static readonly Dictionary<int, EdgeTarget> _bottomContourByCol = new Dictionary<int, EdgeTarget>(64);
        private static readonly Dictionary<int, EdgeTarget> _topContourByCol = new Dictionary<int, EdgeTarget>(64);
        private static readonly List<EdgeTarget> _contourCandidates = new List<EdgeTarget>(256);
        private static readonly HashSet<int> _currentShellIds = new HashSet<int>();
        private static readonly Dictionary<int, int> _recentLineUseFrame = new Dictionary<int, int>(32);
        private static int _edgeCacheFrame = -1;

        public enum ScanDirection
        {
            Right,
            Up,
            Left,
            Down
        }

        public static void ResetCache()
        {
            _occupiedCells.Clear();
            _outsideCells.Clear();
            _floodQueue.Clear();
            _leftContourByRow.Clear();
            _rightContourByRow.Clear();
            _bottomContourByCol.Clear();
            _topContourByCol.Clear();
            _contourCandidates.Clear();
            ClearShellSnapshot();
            _edgeCacheFrame = -1;
        }

        public static int FindTarget(Vector3 dartPosition, Vector3 firingDirection, int color, HashSet<int> excludeIds = null)
        {
            if (!BalloonController.HasInstance) return -1;

            BuildEdgeTargetCache();

            ScanDirection scanDir = DetermineScanDirection(firingDirection);
            Vector2Int dartCell = WorldToGrid(dartPosition);

            int bestId = -1;
            int bestLine = 0;
            float bestScore = float.MaxValue;
            float bestFiringDist = float.MaxValue;
            float perpendicularTolerance = _gridCellSize * PERPENDICULAR_TOLERANCE_MULTIPLIER;

            // Check a narrow band around the aligned line. This keeps non-rectangular
            // motifs targetable when rail smoothing or mobile precision shifts the dart
            // slightly away from the exact grid line.
            for (int offset = -LINE_SEARCH_RADIUS; offset <= LINE_SEARCH_RADIUS; offset++)
            {
                if (!TryGetEdgeTarget(scanDir, dartCell, offset, out EdgeTarget edge))
                    continue;

                if (!edge.targetable) continue;
                if (edge.color != color) continue;
                if (excludeIds != null && excludeIds.Contains(edge.balloonId)) continue;

                float firingDist = GetFiringAxisDistance(dartPosition, edge.worldPos, scanDir);
                if (firingDist < 0f) continue;

                float perpDist = GetPerpendicularDistance(dartPosition, edge.worldPos, scanDir);
                if (perpDist > perpendicularTolerance) continue;

                int line = GetLineKey(scanDir, edge.cell);
                float score = perpDist + GetRecentLinePenalty(scanDir, line);
                if (score < bestScore || (Mathf.Approximately(score, bestScore) && firingDist < bestFiringDist))
                {
                    bestScore = score;
                    bestFiringDist = firingDist;
                    bestLine = line;
                    bestId = edge.balloonId;
                }
            }

            if (bestId >= 0)
            {
                _recentLineUseFrame[GetRecentLineKey(scanDir, bestLine)] = Time.frameCount;
            }

            return bestId;
        }

        public static ScanDirection DetermineScanDirection(Vector3 movementDirection)
        {
            float absX = Mathf.Abs(movementDirection.x);
            float absZ = Mathf.Abs(movementDirection.z);

            if (absX >= absZ)
                return movementDirection.x >= 0f ? ScanDirection.Right : ScanDirection.Left;

            return movementDirection.z >= 0f ? ScanDirection.Up : ScanDirection.Down;
        }

        private static void BuildEdgeTargetCache()
        {
            int currentFrame = Time.frameCount;
            if (_edgeCacheFrame == currentFrame) return;

            _occupiedCells.Clear();
            _outsideCells.Clear();
            _floodQueue.Clear();
            _leftContourByRow.Clear();
            _rightContourByRow.Clear();
            _bottomContourByCol.Clear();
            _topContourByCol.Clear();
            _contourCandidates.Clear();

            if (GameManager.HasInstance)
                _gridCellSize = GameManager.Instance.Board.cellSpacing;

            if (!BalloonController.HasInstance)
            {
                _edgeCacheFrame = currentFrame;
                return;
            }

            BalloonData[] all = BalloonController.Instance.GetAllBalloons();
            if (all == null)
            {
                _edgeCacheFrame = currentFrame;
                return;
            }

            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minY = int.MaxValue;
            int maxY = int.MinValue;

            for (int i = 0; i < all.Length; i++)
            {
                BalloonData balloon = all[i];
                if (balloon == null || balloon.isPopped) continue;

                Vector3 worldPos = BalloonController.Instance.GetBalloonWorldPositionCached(balloon.balloonId);
                Vector2Int cell = WorldToGrid(worldPos);
                EdgeTarget edge = new EdgeTarget
                {
                    balloonId = balloon.balloonId,
                    color = balloon.color,
                    worldPos = worldPos,
                    cell = cell,
                    targetable = IsDirectlyTargetable(balloon)
                };

                _occupiedCells[cell] = edge;
                if (cell.x < minX) minX = cell.x;
                if (cell.x > maxX) maxX = cell.x;
                if (cell.y < minY) minY = cell.y;
                if (cell.y > maxY) maxY = cell.y;
            }

            if (_occupiedCells.Count == 0)
            {
                ClearShellSnapshot();
                _edgeCacheFrame = currentFrame;
                return;
            }

            int floodMinX = minX - 1;
            int floodMaxX = maxX + 1;
            int floodMinY = minY - 1;
            int floodMaxY = maxY + 1;
            FloodOutside(floodMinX, floodMaxX, floodMinY, floodMaxY);

            foreach (KeyValuePair<Vector2Int, EdgeTarget> kvp in _occupiedCells)
            {
                Vector2Int cell = kvp.Key;
                EdgeTarget edge = kvp.Value;

                if (_outsideCells.Contains(new Vector2Int(cell.x - 1, cell.y)))
                {
                    AddContourCandidate(edge);
                }

                if (_outsideCells.Contains(new Vector2Int(cell.x + 1, cell.y)))
                {
                    AddContourCandidate(edge);
                }

                if (_outsideCells.Contains(new Vector2Int(cell.x, cell.y - 1)))
                {
                    AddContourCandidate(edge);
                }

                if (_outsideCells.Contains(new Vector2Int(cell.x, cell.y + 1)))
                {
                    AddContourCandidate(edge);
                }
            }

            EnsureShellSnapshot();
            BuildShellLineMaps();

            _edgeCacheFrame = currentFrame;
        }

        private static void AddContourCandidate(EdgeTarget edge)
        {
            if (!edge.targetable) return;
            if (_currentShellIds.Contains(edge.balloonId)) return;
            for (int i = 0; i < _contourCandidates.Count; i++)
            {
                if (_contourCandidates[i].balloonId == edge.balloonId)
                    return;
            }

            _contourCandidates.Add(edge);
        }

        private static void EnsureShellSnapshot()
        {
            if (!IsShellDepleted()) return;

            _currentShellIds.Clear();
            for (int i = 0; i < _contourCandidates.Count; i++)
                _currentShellIds.Add(_contourCandidates[i].balloonId);

            _recentLineUseFrame.Clear();
        }

        private static bool IsShellDepleted()
        {
            if (_currentShellIds.Count == 0) return true;

            foreach (int balloonId in _currentShellIds)
            {
                if (BalloonController.HasInstance)
                {
                    BalloonData balloon = BalloonController.Instance.GetBalloon(balloonId);
                    if (balloon != null && !balloon.isPopped)
                        return false;
                }
            }

            return true;
        }

        private static void BuildShellLineMaps()
        {
            // 사용자 요구 (옵션 C): shell snapshot freeze 가 새 contour 발견을 막아 "특정 풍선 탐지 못함" 발생.
            // _currentShellIds.Contains 필터 제거 — 현재 contour 모든 풍선을 line maps 에 등록.
            // 단 targetable (= Wall/Ice/ColorCurtain 아님) 만. shell freeze 의 "한 ring 우선 청소" 의도는
            // RecentLinePenalty (line 99) 의 line spreading 으로 대체.
            foreach (KeyValuePair<Vector2Int, EdgeTarget> kvp in _occupiedCells)
            {
                Vector2Int cell = kvp.Key;
                EdgeTarget edge = kvp.Value;
                if (!edge.targetable) continue;

                if (_outsideCells.Contains(new Vector2Int(cell.x - 1, cell.y)))
                {
                    if (!_leftContourByRow.TryGetValue(cell.y, out EdgeTarget existing) || cell.x < existing.cell.x)
                        _leftContourByRow[cell.y] = edge;
                }

                if (_outsideCells.Contains(new Vector2Int(cell.x + 1, cell.y)))
                {
                    if (!_rightContourByRow.TryGetValue(cell.y, out EdgeTarget existing) || cell.x > existing.cell.x)
                        _rightContourByRow[cell.y] = edge;
                }

                if (_outsideCells.Contains(new Vector2Int(cell.x, cell.y - 1)))
                {
                    if (!_bottomContourByCol.TryGetValue(cell.x, out EdgeTarget existing) || cell.y < existing.cell.y)
                        _bottomContourByCol[cell.x] = edge;
                }

                if (_outsideCells.Contains(new Vector2Int(cell.x, cell.y + 1)))
                {
                    if (!_topContourByCol.TryGetValue(cell.x, out EdgeTarget existing) || cell.y > existing.cell.y)
                        _topContourByCol[cell.x] = edge;
                }
            }
        }

        private static void ClearShellSnapshot()
        {
            _currentShellIds.Clear();
            _recentLineUseFrame.Clear();
        }

        private static void FloodOutside(int minX, int maxX, int minY, int maxY)
        {
            EnqueueOutsideCell(new Vector2Int(minX, minY), minX, maxX, minY, maxY);

            while (_floodQueue.Count > 0)
            {
                Vector2Int cell = _floodQueue.Dequeue();

                EnqueueOutsideCell(new Vector2Int(cell.x + 1, cell.y), minX, maxX, minY, maxY);
                EnqueueOutsideCell(new Vector2Int(cell.x - 1, cell.y), minX, maxX, minY, maxY);
                EnqueueOutsideCell(new Vector2Int(cell.x, cell.y + 1), minX, maxX, minY, maxY);
                EnqueueOutsideCell(new Vector2Int(cell.x, cell.y - 1), minX, maxX, minY, maxY);
            }
        }

        private static void EnqueueOutsideCell(Vector2Int cell, int minX, int maxX, int minY, int maxY)
        {
            if (cell.x < minX || cell.x > maxX || cell.y < minY || cell.y > maxY) return;
            if (_outsideCells.Contains(cell)) return;
            if (_occupiedCells.ContainsKey(cell)) return;

            _outsideCells.Add(cell);
            _floodQueue.Enqueue(cell);
        }

        private static bool TryGetEdgeTarget(ScanDirection scanDir, Vector2Int dartCell, int lineOffset, out EdgeTarget edge)
        {
            switch (scanDir)
            {
                case ScanDirection.Right:
                    return _leftContourByRow.TryGetValue(dartCell.y + lineOffset, out edge);
                case ScanDirection.Left:
                    return _rightContourByRow.TryGetValue(dartCell.y + lineOffset, out edge);
                case ScanDirection.Up:
                    return _bottomContourByCol.TryGetValue(dartCell.x + lineOffset, out edge);
                case ScanDirection.Down:
                    return _topContourByCol.TryGetValue(dartCell.x + lineOffset, out edge);
                default:
                    edge = default;
                    return false;
            }
        }

        private static int GetLineKey(ScanDirection scanDir, Vector2Int cell)
        {
            switch (scanDir)
            {
                case ScanDirection.Right:
                case ScanDirection.Left:
                    return cell.y;
                case ScanDirection.Up:
                case ScanDirection.Down:
                    return cell.x;
                default:
                    return 0;
            }
        }

        private static int GetRecentLineKey(ScanDirection scanDir, int line)
        {
            return ((int)scanDir * 100000) + line;
        }

        private static float GetRecentLinePenalty(ScanDirection scanDir, int line)
        {
            int key = GetRecentLineKey(scanDir, line);
            if (!_recentLineUseFrame.TryGetValue(key, out int frame)) return 0f;

            int age = Time.frameCount - frame;
            if (age > RECENT_LINE_PENALTY_FRAMES) return 0f;

            return _gridCellSize * Mathf.Lerp(RECENT_LINE_PENALTY_MULTIPLIER, 0f, age / (float)RECENT_LINE_PENALTY_FRAMES);
        }

        private static bool IsDirectlyTargetable(BalloonData balloon)
        {
            if (balloon.gimmickType == BalloonController.GimmickWall) return false;
            if (balloon.gimmickType == BalloonController.GimmickIce) return false;
            if (balloon.gimmickType == BalloonController.GimmickColorCurtain) return false;
            return true;
        }

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

        private static Vector2Int WorldToGrid(Vector3 worldPos)
        {
            float cs = _gridCellSize > 0.01f ? _gridCellSize : DEFAULT_GRID_CELL_SIZE;
            return new Vector2Int(
                Mathf.RoundToInt(worldPos.x / cs),
                Mathf.RoundToInt(worldPos.z / cs));
        }
    }
}
