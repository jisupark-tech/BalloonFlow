using System.Collections.Generic;
using System.Text;
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
        // [Optimization 2026-05-10] 기존 _outsideCells/_floodQueue 의 Vector2Int hash 연산 (HashSet/Dictionary/Queue) 누적 부하 제거.
        // byte[] grid + int[] queue 기반 BFS 로 재구현 — Profiler 71ms 의 핵심 원인 (BuildEdgeTargetCache → FloodOutside → EnqueueOutsideCell).
        // 0 = unvisited, 1 = outside, 2 = occupied. 매 frame Array.Clear 후 occupied 표시 + BFS.
        // 롤백: 아래 grid 필드 + IsOutside 헬퍼 + 새 FloodOutside 구현 제거 + 주석 처리된 원본 (HashSet/Queue 기반) 복원.
        private static byte[] _cellState;
        private static int[] _bfsQueue;
        private static int _floodMinX, _floodMinY, _floodWidth, _floodHeight;
        private static readonly HashSet<Vector2Int> _outsideCells = new HashSet<Vector2Int>(); // 미사용 (롤백 호환 위해 보존)
        private static readonly Queue<Vector2Int> _floodQueue = new Queue<Vector2Int>();       // 미사용 (롤백 호환 위해 보존)

        // [Outline 2026-05-10] BalloonController 가 outline 적용 시 사용 — 공격 가능한 외곽 풍선 ID 집합.
        // BuildEdgeTargetCache 매 frame build 시 함께 채움. 외부 read-only 접근.
        private static readonly HashSet<int> _attackableContourIds = new HashSet<int>(64);
        // [2026-05-13] 현재 4 contour map (left/right/top/bottom) 에 등장하는 color 집합.
        // FindTarget 의 inner-stuck fallback 판단용 — dart color 가 이 set 에 없으면
        // contour 어디에도 같은 색 후보 없음 → 영구 대기 방지로 inner scan 진입.
        // BuildShellLineMaps 끝에서 4 map union 으로 갱신. 매 frame 1회 build, FindTarget 은 O(1) 조회.
        private static readonly HashSet<int> _contourColors = new HashSet<int>(16);
        public static IReadOnlyCollection<int> GetAttackableContourIds()
        {
            BuildEdgeTargetCache();
            return _attackableContourIds;
        }

        private static readonly Dictionary<int, EdgeTarget> _leftContourByRow = new Dictionary<int, EdgeTarget>(64);
        private static readonly Dictionary<int, EdgeTarget> _rightContourByRow = new Dictionary<int, EdgeTarget>(64);
        private static readonly Dictionary<int, EdgeTarget> _bottomContourByCol = new Dictionary<int, EdgeTarget>(64);
        private static readonly Dictionary<int, EdgeTarget> _topContourByCol = new Dictionary<int, EdgeTarget>(64);
        private static readonly List<EdgeTarget> _contourCandidates = new List<EdgeTarget>(256);
        private static readonly HashSet<int> _currentShellIds = new HashSet<int>();
        private static readonly Dictionary<int, int> _recentLineUseFrame = new Dictionary<int, int>(32);
        // ROLLBACK_CONTOUR_TARGET_DIAG:
        private static readonly StringBuilder _diagBuilder = new StringBuilder(1024);
        private static int _edgeCacheFrame = -1;
        // ROLLBACK_CONTOUR_TARGET_DIAG:
        public static string LastFindTargetDiag { get; private set; } = string.Empty;

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
            _attackableContourIds.Clear(); // [Outline 2026-05-10]
            _contourColors.Clear();        // [2026-05-13]
            ClearShellSnapshot();
            _edgeCacheFrame = -1;
        }

        // [2026-05-13 Diag] 진단 로그는 실측 완료 후 제거. 필요 시 아래 주석을 풀어 재활성:
        // public static int LastEdgesConsidered, LastRejectTargetable, LastRejectColor,
        //                   LastRejectExclude, LastRejectFiringDist, LastRejectPerpDist,
        //                   LastBestId, LastExcludedSameColorId;
        // public static float LastBestScore, LastBestPerpDist, LastTolerance;
        // public static ScanDirection LastScanDir; public static Vector2Int LastDartCell;
        // public static string FormatLastDiag() => $"[FindTargetDiag] scan={LastScanDir} ...";

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
            _diagBuilder.Length = 0;

            // Check a narrow band around the aligned line. This keeps non-rectangular
            // motifs targetable when rail smoothing or mobile precision shifts the dart
            // slightly away from the exact grid line.
            for (int offset = -LINE_SEARCH_RADIUS; offset <= LINE_SEARCH_RADIUS; offset++)
            {
                if (!TryGetEdgeTarget(scanDir, dartCell, offset, out EdgeTarget edge))
                {
                    AppendFindTargetCandidateDiag(offset, default, false, false, false, 0f, 0f, 0, 0f, "none");
                    continue;
                }

                bool reserved = excludeIds != null && excludeIds.Contains(edge.balloonId);
                if (!edge.targetable)
                {
                    AppendFindTargetCandidateDiag(offset, edge, true, false, reserved, 0f, 0f, 0, 0f, "notTargetable");
                    continue;
                }
                if (edge.color != color)
                {
                    AppendFindTargetCandidateDiag(offset, edge, true, false, reserved, 0f, 0f, 0, 0f, "color");
                    continue;
                }
                if (reserved)
                {
                    AppendFindTargetCandidateDiag(offset, edge, true, false, true, 0f, 0f, 0, 0f, "reserved");
                    continue;
                }

                float firingDist = GetFiringAxisDistance(dartPosition, edge.worldPos, scanDir);
                if (firingDist < 0f)
                {
                    AppendFindTargetCandidateDiag(offset, edge, true, false, reserved, firingDist, 0f, 0, 0f, "behind");
                    continue;
                }

                float perpDist = GetPerpendicularDistance(dartPosition, edge.worldPos, scanDir);
                if (perpDist > perpendicularTolerance)
                {
                    AppendFindTargetCandidateDiag(offset, edge, true, false, reserved, firingDist, perpDist, 0, 0f, "perp");
                    continue;
                }

                int line = GetLineKey(scanDir, edge.cell);
                float score = perpDist + GetRecentLinePenalty(scanDir, line);
                AppendFindTargetCandidateDiag(offset, edge, true, true, reserved, firingDist, perpDist, line, score, "ok");
                if (score < bestScore || (Mathf.Approximately(score, bestScore) && firingDist < bestFiringDist))
                {
                    bestScore = score;
                    bestFiringDist = firingDist;
                    bestLine = line;
                    bestId = edge.balloonId;
                }
            }

            // [2026-05-13 rolled back] Inner-stuck color fallback — 관통 이슈로 비활성.
            //   contour 에 dart color 가 없을 때 inner 풍선을 target 으로 하면 projectile 이
            //   직선 DOMove 로 다른 색 contour 를 시각 관통. 게임 디자인/레벨 차원에서 해결.
            //   재활성 옵션: D안 (projectile 아크/페이드) 추가 후 _contourColors 검사 + FindInnerFallback 호출 복원.
            // if (bestId < 0 && !_contourColors.Contains(color))
            //     bestId = FindInnerFallback(dartCell, scanDir, color, excludeIds);

            if (bestId >= 0)
            {
                _recentLineUseFrame[GetRecentLineKey(scanDir, bestLine)] = Time.frameCount;
            }

            LastFindTargetDiag =
                $"frame={Time.frameCount} cacheFrame={_edgeCacheFrame} color={color} dartCell={dartCell} scan={scanDir} " +
                $"tol={perpendicularTolerance:F3} chosen={bestId} line={bestLine} " +
                $"score={(bestScore < float.MaxValue ? bestScore.ToString("F3") : "none")} " +
                $"fireDist={(bestFiringDist < float.MaxValue ? bestFiringDist.ToString("F3") : "none")} candidates={_diagBuilder}";

            return bestId;
        }

        // [2026-05-13 rolled back] FindInnerFallback — 관통 이슈로 비활성. 재활성 시 복원.
        // private static int FindInnerFallback(Vector2Int dartCell, ScanDirection scanDir, int color, HashSet<int> excludeIds)
        // {
        //     int dx = 0, dy = 0;
        //     switch (scanDir)
        //     {
        //         case ScanDirection.Right: dx = +1; break;
        //         case ScanDirection.Left:  dx = -1; break;
        //         case ScanDirection.Up:    dy = +1; break;
        //         case ScanDirection.Down:  dy = -1; break;
        //     }
        //     for (int offset = -LINE_SEARCH_RADIUS; offset <= LINE_SEARCH_RADIUS; offset++)
        //     {
        //         if (!TryGetEdgeTarget(scanDir, dartCell, offset, out EdgeTarget contourEdge)) continue;
        //         int x = contourEdge.cell.x + dx;
        //         int y = contourEdge.cell.y + dy;
        //         for (int step = 0; step < 64; step++)
        //         {
        //             if (!_occupiedCells.TryGetValue(new Vector2Int(x, y), out EdgeTarget e)) break;
        //             if (e.targetable && e.color == color
        //                 && (excludeIds == null || !excludeIds.Contains(e.balloonId)))
        //                 return e.balloonId;
        //             x += dx; y += dy;
        //         }
        //     }
        //     return -1;
        // }

        // ROLLBACK_CONTOUR_TARGET_DIAG:
        // Store compact candidate diagnostics for the latest FindTarget call.
        private static void AppendFindTargetCandidateDiag(
            int offset,
            EdgeTarget edge,
            bool hasEdge,
            bool accepted,
            bool reserved,
            float firingDist,
            float perpDist,
            int line,
            float score,
            string reason)
        {
            if (_diagBuilder.Length > 0)
                _diagBuilder.Append(" | ");

            _diagBuilder.Append("off=").Append(offset);
            if (!hasEdge)
            {
                _diagBuilder.Append("/none");
                return;
            }

            _diagBuilder
                .Append("/id=").Append(edge.balloonId)
                .Append("/cell=").Append(edge.cell)
                .Append("/color=").Append(edge.color)
                .Append("/targetable=").Append(edge.targetable)
                .Append("/reserved=").Append(reserved);

            if (accepted || firingDist != 0f || perpDist != 0f)
            {
                _diagBuilder
                    .Append("/fd=").Append(firingDist.ToString("F2"))
                    .Append("/pd=").Append(perpDist.ToString("F2"));
            }

            if (accepted)
            {
                _diagBuilder
                    .Append("/line=").Append(line)
                    .Append("/score=").Append(score.ToString("F2"));
            }

            _diagBuilder.Append("/").Append(reason);
        }

        // ROLLBACK_CONTOUR_TARGET_DIAG:
        // Logs the contour cache state after a pop without forcing a cache rebuild.
        public static void LogContourAfterPop(int poppedId, int color, Vector3 worldPos, string gimmickType)
        {
            BuildEdgeTargetCache();

            Vector2Int poppedCell = WorldToGrid(worldPos);
            bool cacheHasPoppedCell = _occupiedCells.TryGetValue(poppedCell, out EdgeTarget cachedEdge)
                                      && cachedEdge.balloonId == poppedId;
            int shellAlive = 0;
            foreach (int shellId in _currentShellIds)
            {
                if (!BalloonController.HasInstance) continue;
                BalloonData balloon = BalloonController.Instance.GetBalloon(shellId);
                if (balloon != null && !balloon.isPopped)
                    shellAlive++;
            }

            Debug.Log(
                $"[ContourAfterPop] frame={Time.frameCount} cacheFrame={_edgeCacheFrame} cacheCurrent={_edgeCacheFrame == Time.frameCount} " +
                $"popped={poppedId} color={color} gimmick={gimmickType} cell={poppedCell} cacheHasPoppedCell={cacheHasPoppedCell} " +
                $"cached={(cacheHasPoppedCell ? $"id{cachedEdge.balloonId}/color{cachedEdge.color}/targetable{cachedEdge.targetable}" : "none")} " +
                $"occupied={_occupiedCells.Count} shell={_currentShellIds.Count} shellAlive={shellAlive} " +
                $"left={_leftContourByRow.Count} right={_rightContourByRow.Count} bottom={_bottomContourByCol.Count} top={_topContourByCol.Count} " +
                $"sameColor={FormatSameColorContourSummary(color)}");
        }

        private static string FormatSameColorContourSummary(int color)
        {
            _diagBuilder.Length = 0;
            AppendSameColorMapSummary("L", _leftContourByRow, color);
            AppendSameColorMapSummary("R", _rightContourByRow, color);
            AppendSameColorMapSummary("B", _bottomContourByCol, color);
            AppendSameColorMapSummary("T", _topContourByCol, color);
            return _diagBuilder.Length > 0 ? _diagBuilder.ToString() : "none";
        }

        private static void AppendSameColorMapSummary(string label, Dictionary<int, EdgeTarget> map, int color)
        {
            int count = 0;
            foreach (var kvp in map)
            {
                EdgeTarget edge = kvp.Value;
                if (edge.color != color) continue;

                if (_diagBuilder.Length > 0)
                    _diagBuilder.Append(" | ");

                _diagBuilder
                    .Append(label).Append(kvp.Key)
                    .Append(":id=").Append(edge.balloonId)
                    .Append("/cell=").Append(edge.cell);

                count++;
                if (count >= 8)
                {
                    _diagBuilder.Append("/...");
                    break;
                }
            }
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
            _attackableContourIds.Clear(); // [Outline 2026-05-10]
            _contourColors.Clear();        // [2026-05-13]

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

            // [Optimization 2026-05-10] _outsideCells.Contains (HashSet hash 연산) → IsOutside (배열 인덱싱) 로 대체.
            foreach (KeyValuePair<Vector2Int, EdgeTarget> kvp in _occupiedCells)
            {
                Vector2Int cell = kvp.Key;
                EdgeTarget edge = kvp.Value;

                if (IsOutside(cell.x - 1, cell.y)) AddContourCandidate(edge);
                if (IsOutside(cell.x + 1, cell.y)) AddContourCandidate(edge);
                if (IsOutside(cell.x, cell.y - 1)) AddContourCandidate(edge);
                if (IsOutside(cell.x, cell.y + 1)) AddContourCandidate(edge);
            }
            // [Outline 2026-05-10 fix] _attackableContourIds 는 BuildShellLineMaps 가 4 contour map 채운 후 union 으로 채움.
            // 이전 "outside neighbor 1+ + targetable" 방식은 hole 인접 풍선까지 포함 → board 에 hole 많으면 거의 전부 contour.
            // 사용자 의도: dart 가 실제 fire 가능한 "각 row/col 의 가장 외곽 한 줄" — 4 contour map 의 union 이 정확.

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
            // [Optimization 2026-05-10] _outsideCells.Contains → IsOutside. 롤백: 주석 처리된 원본 4 분기 복원.
            foreach (KeyValuePair<Vector2Int, EdgeTarget> kvp in _occupiedCells)
            {
                Vector2Int cell = kvp.Key;
                EdgeTarget edge = kvp.Value;
                if (!edge.targetable) continue;

                // 원본 4 분기:
                // if (_outsideCells.Contains(new Vector2Int(cell.x - 1, cell.y))) { if (!_leftContourByRow.TryGetValue(cell.y, out EdgeTarget existing) || cell.x < existing.cell.x) _leftContourByRow[cell.y] = edge; }
                // if (_outsideCells.Contains(new Vector2Int(cell.x + 1, cell.y))) { if (!_rightContourByRow.TryGetValue(cell.y, out EdgeTarget existing) || cell.x > existing.cell.x) _rightContourByRow[cell.y] = edge; }
                // if (_outsideCells.Contains(new Vector2Int(cell.x, cell.y - 1))) { if (!_bottomContourByCol.TryGetValue(cell.x, out EdgeTarget existing) || cell.y < existing.cell.y) _bottomContourByCol[cell.x] = edge; }
                // if (_outsideCells.Contains(new Vector2Int(cell.x, cell.y + 1))) { if (!_topContourByCol.TryGetValue(cell.x, out EdgeTarget existing) || cell.y > existing.cell.y) _topContourByCol[cell.x] = edge; }
                if (IsOutside(cell.x - 1, cell.y))
                {
                    if (!_leftContourByRow.TryGetValue(cell.y, out EdgeTarget existing) || cell.x < existing.cell.x)
                        _leftContourByRow[cell.y] = edge;
                }
                if (IsOutside(cell.x + 1, cell.y))
                {
                    if (!_rightContourByRow.TryGetValue(cell.y, out EdgeTarget existing) || cell.x > existing.cell.x)
                        _rightContourByRow[cell.y] = edge;
                }
                if (IsOutside(cell.x, cell.y - 1))
                {
                    if (!_bottomContourByCol.TryGetValue(cell.x, out EdgeTarget existing) || cell.y < existing.cell.y)
                        _bottomContourByCol[cell.x] = edge;
                }
                if (IsOutside(cell.x, cell.y + 1))
                {
                    if (!_topContourByCol.TryGetValue(cell.x, out EdgeTarget existing) || cell.y > existing.cell.y)
                        _topContourByCol[cell.x] = edge;
                }
            }

            // [Outline 2026-05-10 fix] _attackableContourIds = 4 contour map (left/right/top/bottom) 의 union.
            // 각 row/col 의 가장 외곽 1개 풍선 = dart 가 실제 fire 가능한 외곽 한 줄.
            // BalloonController.RefreshOutermostRendererState 가 이 set 으로 outline 적용.
            // [2026-05-13] _contourColors 도 함께 채움 — FindTarget inner fallback 판단용.
            foreach (var kvp in _leftContourByRow)   { _attackableContourIds.Add(kvp.Value.balloonId); _contourColors.Add(kvp.Value.color); }
            foreach (var kvp in _rightContourByRow)  { _attackableContourIds.Add(kvp.Value.balloonId); _contourColors.Add(kvp.Value.color); }
            foreach (var kvp in _bottomContourByCol) { _attackableContourIds.Add(kvp.Value.balloonId); _contourColors.Add(kvp.Value.color); }
            foreach (var kvp in _topContourByCol)    { _attackableContourIds.Add(kvp.Value.balloonId); _contourColors.Add(kvp.Value.color); }
        }

        private static void ClearShellSnapshot()
        {
            _currentShellIds.Clear();
            _recentLineUseFrame.Clear();
        }

        // [Optimization 2026-05-10] FloodOutside / EnqueueOutsideCell 을 byte[] grid + int[] queue 기반 BFS 로 재구현.
        // 기존: Vector2Int HashSet/Dictionary/Queue → 매 cell 마다 GetHashCode + Equals (수만 회 누적 → 71ms).
        // 새: 1D 배열 인덱싱만 사용. occupied = 2, outside = 1, unvisited = 0. Array.Clear 로 매 frame reset.
        // 동작 보존: BFS 알고리즘 동일 (4-방향 flood, occupied 차단, bbox 안에서만 진행).
        // 결과: BuildEdgeTargetCache → contour 검사가 IsOutside(x,y) 로 grid lookup → ms 단위 절감.
        // 롤백: 새 구현 제거 + 주석 처리된 원본 (HashSet/Queue 기반) 두 메서드 복원 + 새 IsOutside 헬퍼 제거 + contour 검사 _outsideCells.Contains 로 복원.
        private static void FloodOutside(int minX, int maxX, int minY, int maxY)
        {
            // 원본:
            // EnqueueOutsideCell(new Vector2Int(minX, minY), minX, maxX, minY, maxY);
            // while (_floodQueue.Count > 0) {
            //     Vector2Int cell = _floodQueue.Dequeue();
            //     EnqueueOutsideCell(new Vector2Int(cell.x + 1, cell.y), minX, maxX, minY, maxY);
            //     EnqueueOutsideCell(new Vector2Int(cell.x - 1, cell.y), minX, maxX, minY, maxY);
            //     EnqueueOutsideCell(new Vector2Int(cell.x, cell.y + 1), minX, maxX, minY, maxY);
            //     EnqueueOutsideCell(new Vector2Int(cell.x, cell.y - 1), minX, maxX, minY, maxY);
            // }

            _floodMinX = minX;
            _floodMinY = minY;
            _floodWidth = maxX - minX + 1;
            _floodHeight = maxY - minY + 1;
            int total = _floodWidth * _floodHeight;
            if (total <= 0) return;

            // 버퍼 보장 (정적 재사용)
            if (_cellState == null || _cellState.Length < total)
                _cellState = new byte[Mathf.NextPowerOfTwo(total)];
            else
                System.Array.Clear(_cellState, 0, total);

            if (_bfsQueue == null || _bfsQueue.Length < total)
                _bfsQueue = new int[Mathf.NextPowerOfTwo(total)];

            // occupied cell 표시 (= 2). bbox 가 [minX-1, maxX+1] 라 모든 occupied 가 bbox 안.
            foreach (KeyValuePair<Vector2Int, EdgeTarget> kvp in _occupiedCells)
            {
                int dx = kvp.Key.x - minX;
                int dy = kvp.Key.y - minY;
                if ((uint)dx >= (uint)_floodWidth || (uint)dy >= (uint)_floodHeight) continue;
                _cellState[dy * _floodWidth + dx] = 2;
            }

            // BFS: 시작 (minX, minY) corner. 항상 occupied 아님 (bbox = bbox-of-occupied + 1).
            int startIdx = 0;
            if (_cellState[startIdx] != 0) return; // 만일 start 가 막혔다면 BFS 불가
            _cellState[startIdx] = 1;
            _bfsQueue[0] = startIdx;
            int head = 0, tail = 1;
            int width = _floodWidth;
            int widthMinus1 = width - 1;
            int heightMinus1 = _floodHeight - 1;

            while (head < tail)
            {
                int curIdx = _bfsQueue[head++];
                int cy = curIdx / width;
                int cx = curIdx - cy * width;

                // +X
                if (cx < widthMinus1)
                {
                    int nIdx = curIdx + 1;
                    if (_cellState[nIdx] == 0) { _cellState[nIdx] = 1; _bfsQueue[tail++] = nIdx; }
                }
                // -X
                if (cx > 0)
                {
                    int nIdx = curIdx - 1;
                    if (_cellState[nIdx] == 0) { _cellState[nIdx] = 1; _bfsQueue[tail++] = nIdx; }
                }
                // +Y
                if (cy < heightMinus1)
                {
                    int nIdx = curIdx + width;
                    if (_cellState[nIdx] == 0) { _cellState[nIdx] = 1; _bfsQueue[tail++] = nIdx; }
                }
                // -Y
                if (cy > 0)
                {
                    int nIdx = curIdx - width;
                    if (_cellState[nIdx] == 0) { _cellState[nIdx] = 1; _bfsQueue[tail++] = nIdx; }
                }
            }
        }

        // [Optimization 2026-05-10] EnqueueOutsideCell 은 새 BFS 에서 미사용 (FloodOutside 안에 인라인됨).
        // 원본 보존 — 롤백 시 위 FloodOutside 와 함께 복원.
        // private static void EnqueueOutsideCell(Vector2Int cell, int minX, int maxX, int minY, int maxY)
        // {
        //     if (cell.x < minX || cell.x > maxX || cell.y < minY || cell.y > maxY) return;
        //     if (_outsideCells.Contains(cell)) return;
        //     if (_occupiedCells.ContainsKey(cell)) return;
        //     _outsideCells.Add(cell);
        //     _floodQueue.Enqueue(cell);
        // }

        /// <summary>[Optimization 2026-05-10] grid 기반 outside cell 조회. _outsideCells.Contains(new Vector2Int(x,y)) 대체.</summary>
        private static bool IsOutside(int x, int y)
        {
            int dx = x - _floodMinX;
            int dy = y - _floodMinY;
            if ((uint)dx >= (uint)_floodWidth || (uint)dy >= (uint)_floodHeight) return false;
            return _cellState[dy * _floodWidth + dx] == 1;
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
