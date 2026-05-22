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
        // ROLLBACK_DART_ADJACENT_LINE_RESCUE:
        // Adjacent-line rescue reintroduced continuous cross-line attacks. Keep target picking on
        // the exact scan line; remaining misses should be fixed by scan timing/cache, not by letting
        // darts choose neighboring rows/columns.
        private const int LINE_SEARCH_RADIUS = 0;
        // ROLLBACK_DART_LIVE_LINE_CONTOUR:
        // The old frozen-shell snapshot kept a whole contour layer until every shell cell died.
        // That made a row/column go blind after its first edge cell popped, so nearby lines won
        // the score race and produced penetration, misses, and staggered peeling. Live contour
        // keeps each exact line advancing to its current first-hit cell.
#pragma warning disable 0414
        private static readonly bool USE_FROZEN_SHELL_SNAPSHOT = false;
#pragma warning restore 0414
        private const int RECENT_LINE_PENALTY_FRAMES = 18;
        // 사용자 요구 (2026-05-07): "2행만 공격해야 하는데 1행도 공격" 이슈.
        // 1.35 → 0.7 로 strict 화 — dart 가 자기 perp 정렬 line 외 다른 line 의 balloon 후보로 안 잡힘.
        // line penalty 가 across-row 로 redirect 하는 부작용 차단. cellSpacing 만큼 떨어진 인접 row 는 제외 (perpDist > 0.7 × cellSpacing).
        // ROLLBACK_DART_EXACT_LINE_TOLERANCE:
        // Miss diagnostics showed exact-line candidates rejected at pd=0.17~0.19 while the next
        // frame reported the previous/next line as wouldHit. Keep tolerance narrow enough to block
        // cross-line peeling, while adjacent-line rescue handles pure quantization misses.
        private const float PERPENDICULAR_TOLERANCE_MULTIPLIER = 0.9f;
        private const float RECENT_LINE_PENALTY_MULTIPLIER = 2.25f;
        // ROLLBACK_DART_STABLE_OUTER_HIT:
        // Full candidate/pop diagnostics are very expensive on large boards. Enable only when
        // actively investigating targeting, not during normal play.
        private static readonly bool TARGETING_DIAG = false;
        private static readonly bool CONTOUR_AFTER_POP_DIAG = false;

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
        // ROLLBACK_DART_MISS_SUSPECT_DIAG:
        // Normal targeting diagnostics are too expensive and noisy for play mode. This diagnostic is
        // called only after a head dart crosses a scan line and no target is chosen. It logs only when
        // a same-color outer contour exists near that line, which is the useful "miss suspect" case.
        public static bool TryBuildMissSuspectDiag(
            Vector3 dartPosition,
            Vector3 firingDirection,
            int color,
            HashSet<int> excludeIds,
            int radius,
            out string diag)
        {
            diag = string.Empty;
            if (!BalloonController.HasInstance) return false;

            BuildEdgeTargetCache();

            ScanDirection scanDir = DetermineScanDirection(firingDirection);
            Vector2Int dartCell = WorldToGrid(dartPosition);
            float tolerance = _gridCellSize * PERPENDICULAR_TOLERANCE_MULTIPLIER;
            radius = Mathf.Max(0, radius);

            _missDiagBuilder.Length = 0;
            bool suspect = false;
            int exactId = -1;
            string exactReason = "none";

            for (int offset = -radius; offset <= radius; offset++)
            {
                if (!TryGetEdgeTarget(scanDir, dartCell, offset, out EdgeTarget edge))
                {
                    if (offset == 0)
                        exactReason = "noEdge";
                    continue;
                }

                bool reserved = excludeIds != null && excludeIds.Contains(edge.balloonId);
                float firingDist = GetFiringAxisDistance(dartPosition, edge.worldPos, scanDir);
                float perpDist = GetPerpendicularDistance(dartPosition, edge.worldPos, scanDir);
                bool sameColor = edge.color == color;
                bool ahead = firingDist >= 0f;
                bool inTolerance = perpDist <= tolerance;
                bool viableSameColor = sameColor && edge.targetable && !reserved && ahead;

                string reason;
                if (!ahead) reason = "behind";
                else if (!edge.targetable) reason = "notTargetable";
                else if (!sameColor) reason = "color";
                else if (reserved) reason = "reserved";
                else if (!inTolerance) reason = "perp";
                else reason = "wouldHit";

                if (offset == 0)
                {
                    exactId = edge.balloonId;
                    exactReason = reason;
                }

                if (!viableSameColor) continue;

                // ROLLBACK_DART_MISS_SUSPECT_DIAG:
                // Reserved-front entries are usually normal in-flight protection and were spamming
                // logs. Report only actionable misses: an exact-line candidate barely outside the
                // tolerance, or a nearby line that would have been hittable.
                bool nearTolerance = perpDist <= tolerance + _gridCellSize * 0.25f;
                bool actionableMiss = (offset == 0 && !inTolerance && nearTolerance)
                    || (offset != 0 && inTolerance);
                if (actionableMiss)
                    suspect = true;

                if (_missDiagBuilder.Length > 0)
                    _missDiagBuilder.Append(" | ");
                _missDiagBuilder
                    .Append("off=").Append(offset)
                    .Append("/id=").Append(edge.balloonId)
                    .Append("/cell=").Append(edge.cell)
                    .Append("/fd=").Append(firingDist.ToString("F2"))
                    .Append("/pd=").Append(perpDist.ToString("F2"))
                    .Append("/").Append(reason);
            }

            if (!suspect) return false;

            diag = $"mode=miss frame={Time.frameCount} cacheFrame={_edgeCacheFrame} color={color} " +
                   $"dartCell={dartCell} scan={scanDir} tol={tolerance:F3} " +
                   $"exact={exactId}/{exactReason} nearby={_missDiagBuilder}";
            return true;
        }

        // ROLLBACK_CONTOUR_TARGET_DIAG:
        private static readonly StringBuilder _diagBuilder = new StringBuilder(1024);
        private static readonly StringBuilder _missDiagBuilder = new StringBuilder(512);
        private static int _edgeCacheFrame = -1;
        private static bool _edgeCacheDirty = true;
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
            _edgeCacheDirty = true;
        }

        public static void InvalidateCache()
        {
            _edgeCacheFrame = -1;
            _edgeCacheDirty = true;
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
            int targetId;
            ScanDirection scanDir;
            int targetLine;
            Vector3 targetWorldPos;
            if (TryFindTarget(dartPosition, firingDirection, color, excludeIds, out targetId, out scanDir, out targetLine, out targetWorldPos))
                return targetId;

            return -1;
        }

        public static bool TryFindTarget(
            Vector3 dartPosition,
            Vector3 firingDirection,
            int color,
            HashSet<int> excludeIds,
            out int targetId,
            out ScanDirection scanDir,
            out int targetLine,
            out Vector3 targetWorldPos)
        {
            targetId = -1;
            targetLine = 0;
            targetWorldPos = Vector3.zero;
            scanDir = DetermineScanDirection(firingDirection);

            if (!BalloonController.HasInstance) return false;

            BuildEdgeTargetCache();

            Vector2Int dartCell = WorldToGrid(dartPosition);

            int bestId = -1;
            int bestLine = 0;
            Vector3 bestWorldPos = Vector3.zero;
            float bestScore = float.MaxValue;
            float bestFiringDist = float.MaxValue;
            // ROLLBACK_DART_FRONT_BLOCKER:
            // A non-matching/untargetable/reserved shell cell in front is a physical blocker.
            // Treating it as a simple reject lets a same-color candidate on a nearby line win,
            // which creates the observed color penetration.
            int blockerId = -1;
            int blockerLine = 0;
            float blockerScore = float.MaxValue;
            float blockerFiringDist = float.MaxValue;
            string blockerReason = string.Empty;
            float perpendicularTolerance = _gridCellSize * PERPENDICULAR_TOLERANCE_MULTIPLIER;
            if (TARGETING_DIAG)
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
                float score = Mathf.Abs(offset) * _gridCellSize + perpDist;

                if (!edge.targetable || edge.color != color || reserved)
                {
                    string reason = !edge.targetable ? "blockedTargetable" : (edge.color != color ? "blockedColor" : "reservedFront");
                    AppendFindTargetCandidateDiag(offset, edge, true, false, reserved, firingDist, perpDist, line, score, reason);
                    if (score < blockerScore || (Mathf.Approximately(score, blockerScore) && firingDist < blockerFiringDist))
                    {
                        blockerScore = score;
                        blockerFiringDist = firingDist;
                        blockerLine = line;
                        blockerId = edge.balloonId;
                        blockerReason = reason;
                    }
                    continue;
                }

                AppendFindTargetCandidateDiag(offset, edge, true, true, false, firingDist, perpDist, line, score, "ok");
                if (score < bestScore || (Mathf.Approximately(score, bestScore) && firingDist < bestFiringDist))
                {
                    bestScore = score;
                    bestFiringDist = firingDist;
                    bestLine = line;
                    bestWorldPos = edge.worldPos;
                    bestId = edge.balloonId;
                }
            }

            // [2026-05-13 rolled back] Inner-stuck color fallback — 관통 이슈로 비활성.
            //   contour 에 dart color 가 없을 때 inner 풍선을 target 으로 하면 projectile 이
            //   직선 DOMove 로 다른 색 contour 를 시각 관통. 게임 디자인/레벨 차원에서 해결.
            //   재활성 옵션: D안 (projectile 아크/페이드) 추가 후 _contourColors 검사 + FindInnerFallback 호출 복원.
            // if (bestId < 0 && !_contourColors.Contains(color))
            //     bestId = FindInnerFallback(dartCell, scanDir, color, excludeIds);

            bool blockedByFront = blockerId >= 0 && (bestId < 0 || blockerScore <= bestScore + 0.0001f);
            if (bestId >= 0 && !blockedByFront)
            {
                _recentLineUseFrame[GetRecentLineKey(scanDir, bestLine)] = Time.frameCount;
                targetId = bestId;
                targetLine = bestLine;
                targetWorldPos = bestWorldPos;
            }

            if (TARGETING_DIAG)
            {
                LastFindTargetDiag =
                    $"mode=auth frame={Time.frameCount} cacheFrame={_edgeCacheFrame} color={color} dartCell={dartCell} scan={scanDir} " +
                    $"tol={perpendicularTolerance:F3} chosen={targetId} line={targetLine} " +
                    $"score={(bestScore < float.MaxValue ? bestScore.ToString("F3") : "none")} " +
                    $"fireDist={(bestFiringDist < float.MaxValue ? bestFiringDist.ToString("F3") : "none")} " +
                    $"frontBlocker={blockerId}/line={blockerLine}/reason={blockerReason}/score={(blockerScore < float.MaxValue ? blockerScore.ToString("F3") : "none")} " +
                    $"blockedFront={blockedByFront} candidates={_diagBuilder}";
            }
            else
            {
                LastFindTargetDiag = string.Empty;
            }

            return targetId >= 0;
        }

        // ROLLBACK_DART_EMPTY_LINE_ADJACENT_RESCUE:
        // Keep normal targeting exact-line only. At x2 speed a dart can quantize onto a line with
        // no contour edge while the physically aligned edge is exactly one grid line beside it.
        // Rescue only that empty-line case; if the exact line has any edge, color/front/reserve
        // rules stay authoritative and adjacent lines cannot steal the shot.
        public static bool TryFindTargetOnAdjacentLineWhenExactLineEmpty(
            Vector3 dartPosition,
            Vector3 firingDirection,
            int color,
            HashSet<int> excludeIds,
            int maxLineOffset,
            out int targetId,
            out ScanDirection scanDir,
            out int targetLine,
            out Vector3 targetWorldPos)
        {
            targetId = -1;
            targetLine = 0;
            targetWorldPos = Vector3.zero;
            scanDir = DetermineScanDirection(firingDirection);

            if (!BalloonController.HasInstance) return false;

            BuildEdgeTargetCache();

            Vector2Int dartCell = WorldToGrid(dartPosition);
            if (TryGetEdgeTarget(scanDir, dartCell, 0, out _))
                return false;

            maxLineOffset = Mathf.Max(0, maxLineOffset);
            if (maxLineOffset == 0)
                return false;

            float perpendicularTolerance = _gridCellSize * PERPENDICULAR_TOLERANCE_MULTIPLIER;
            int bestId = -1;
            int bestLine = 0;
            Vector3 bestWorldPos = Vector3.zero;
            float bestScore = float.MaxValue;
            float bestFiringDist = float.MaxValue;

            for (int offset = -maxLineOffset; offset <= maxLineOffset; offset++)
            {
                if (offset == 0) continue;
                if (!TryGetEdgeTarget(scanDir, dartCell, offset, out EdgeTarget edge))
                    continue;

                bool reserved = excludeIds != null && excludeIds.Contains(edge.balloonId);
                if (!edge.targetable || edge.color != color || reserved)
                    continue;

                float firingDist = GetFiringAxisDistance(dartPosition, edge.worldPos, scanDir);
                if (firingDist < 0f)
                    continue;

                float perpDist = GetPerpendicularDistance(dartPosition, edge.worldPos, scanDir);
                // ROLLBACK_DART_ADJACENT_EMPTY_LINE_RESCUE_PERP:
                // This rescue only runs when the exact scan line has no contour edge. The previous
                // check compared adjacent-line targets against the exact-line tolerance, so a target
                // one grid line away (perpDist ~= 1 cell) was rejected by a 0.9-cell limit. Compare
                // against the expected offset distance instead, keeping the normal TryFindTarget path
                // exact-line only to avoid same-line continuous attacks.
                float expectedPerpDist = Mathf.Abs(offset) * _gridCellSize;
                if (Mathf.Abs(perpDist - expectedPerpDist) > perpendicularTolerance)
                    continue;

                int line = GetLineKey(scanDir, edge.cell);
                float score = Mathf.Abs(offset) * _gridCellSize + perpDist;
                if (score < bestScore || (Mathf.Approximately(score, bestScore) && firingDist < bestFiringDist))
                {
                    bestScore = score;
                    bestFiringDist = firingDist;
                    bestLine = line;
                    bestWorldPos = edge.worldPos;
                    bestId = edge.balloonId;
                }
            }

            if (bestId < 0)
                return false;

            _recentLineUseFrame[GetRecentLineKey(scanDir, bestLine)] = Time.frameCount;
            targetId = bestId;
            targetLine = bestLine;
            targetWorldPos = bestWorldPos;
            return true;
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
            if (!TARGETING_DIAG) return;

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
            if (!CONTOUR_AFTER_POP_DIAG) return;

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
            // ROLLBACK_DART_TARGET_CACHE_DIRTY:
            // Rebuilding this cache every frame is expensive and can make a stable line resolve
            // differently without any board change. Contours only need rebuilding after a pop or
            // level reset, so use explicit invalidation and keep _edgeCacheFrame for diagnostics.
            if (!_edgeCacheDirty) return;

            float __buildStamp = InGamePerfLogger.StartStampMs();
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
            _edgeCacheDirty = false;

            if (GameManager.HasInstance)
                _gridCellSize = GameManager.Instance.Board.cellSpacing;

            if (!BalloonController.HasInstance)
            {
                _edgeCacheFrame = currentFrame;
                InGamePerfLogger.EndSection(__buildStamp, "DirectionalTargeting.BuildEdgeCache");
                return;
            }

            BalloonData[] all = BalloonController.Instance.GetAllBalloons();
            if (all == null)
            {
                _edgeCacheFrame = currentFrame;
                InGamePerfLogger.EndSection(__buildStamp, "DirectionalTargeting.BuildEdgeCache");
                return;
            }

            // ROLLBACK_DIRECTIONAL_LINE_EDGE_CACHE:
            // Previous code rebuilt an outside flood-fill grid for every dirty contour cache.
            // Darts attack along four straight cardinal lines, so keep only each row/column's
            // first occupied blocker. Non-targetable cells remain in the maps as blockers.
            for (int i = 0; i < all.Length; i++)
            {
                BalloonData balloon = all[i];
                if (balloon == null || balloon.isPopped) continue;

                Vector3 worldPos = BalloonController.Instance.GetBalloonWorldPositionCached(balloon.balloonId);
                bool targetable = IsDirectlyTargetable(balloon);

                // ROLLBACK_BARRICADE_MULTI_CELL_OCCUPANCY:
                // Sized field gimmicks are one object, but they occupy every cell in sizeW x sizeH.
                if (IsMultiCellSizedFieldGimmick(balloon))
                {
                    int width = Mathf.Max(1, balloon.sizeW);
                    int height = Mathf.Max(1, balloon.sizeH);
                    Vector3 anchor = BalloonController.Instance.GetAdjustedBoardPosition(balloon.position);
                    BalloonController.Instance.GetAdjustedCellSize(out float cellSizeX, out float cellSizeZ);
                    for (int dx = 0; dx < width; dx++)
                    {
                        for (int dz = 0; dz < height; dz++)
                        {
                            Vector3 cellWorld = new Vector3(
                                anchor.x + dx * cellSizeX,
                                worldPos.y,
                                anchor.z + dz * cellSizeZ);
                            AddEdgeTarget(new EdgeTarget
                            {
                                balloonId = balloon.balloonId,
                                color = balloon.color,
                                worldPos = cellWorld,
                                cell = WorldToGrid(cellWorld),
                                targetable = targetable
                            });
                        }
                    }
                    continue;
                }

                AddEdgeTarget(new EdgeTarget
                {
                    balloonId = balloon.balloonId,
                    color = balloon.color,
                    worldPos = worldPos,
                    cell = WorldToGrid(worldPos),
                    targetable = targetable
                });
            }

            if (_occupiedCells.Count == 0)
            {
                ClearShellSnapshot();
                _edgeCacheFrame = currentFrame;
                InGamePerfLogger.EndSection(__buildStamp, "DirectionalTargeting.BuildEdgeCache");
                return;
            }

            ClearShellSnapshot();
            RebuildAttackableContourIdsFromLineMaps();

#if false // ROLLBACK_DIRECTIONAL_LINE_EDGE_CACHE: restore this flood-fill block if line-edge cache is rolled back.
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

            if (USE_FROZEN_SHELL_SNAPSHOT)
                EnsureShellSnapshot();
            else
                ClearShellSnapshot();
            BuildShellLineMaps();
#endif

            _edgeCacheFrame = currentFrame;
            InGamePerfLogger.EndSection(__buildStamp, "DirectionalTargeting.BuildEdgeCache");
        }

        private static void AddStraightLineEdge(
            Dictionary<int, EdgeTarget> map,
            int line,
            EdgeTarget edge,
            bool preferLower,
            bool useX)
        {
            if (!map.TryGetValue(line, out EdgeTarget existing))
            {
                map[line] = edge;
                return;
            }

            int current = useX ? edge.cell.x : edge.cell.y;
            int previous = useX ? existing.cell.x : existing.cell.y;
            if (preferLower ? current < previous : current > previous)
                map[line] = edge;
        }

        private static void AddEdgeTarget(EdgeTarget edge)
        {
            _occupiedCells[edge.cell] = edge;
            AddStraightLineEdge(_leftContourByRow, edge.cell.y, edge, preferLower: true, useX: true);
            AddStraightLineEdge(_rightContourByRow, edge.cell.y, edge, preferLower: false, useX: true);
            AddStraightLineEdge(_bottomContourByCol, edge.cell.x, edge, preferLower: true, useX: false);
            AddStraightLineEdge(_topContourByCol, edge.cell.x, edge, preferLower: false, useX: false);
        }

        private static bool IsMultiCellSizedFieldGimmick(BalloonData balloon)
        {
            return balloon != null
                && BalloonController.IsSizedFieldGimmick(balloon.gimmickType)
                && (balloon.sizeW > 1 || balloon.sizeH > 1);
        }

        private static void RebuildAttackableContourIdsFromLineMaps()
        {
            AppendAttackableContourIds(_leftContourByRow);
            AppendAttackableContourIds(_rightContourByRow);
            AppendAttackableContourIds(_bottomContourByCol);
            AppendAttackableContourIds(_topContourByCol);
        }

        private static void AppendAttackableContourIds(Dictionary<int, EdgeTarget> map)
        {
            foreach (var kvp in map)
            {
                EdgeTarget edge = kvp.Value;
                if (!edge.targetable) continue;
                _attackableContourIds.Add(edge.balloonId);
                _contourColors.Add(edge.color);
            }
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
            // ROLLBACK_DART_STABLE_OUTER_HIT:
            // Build line maps from the frozen visible shell only. Registering newly exposed
            // inner cells during the same volley causes penetration and ragged multi-line hits.
            // [Optimization 2026-05-10] _outsideCells.Contains -> IsOutside. Rollback restores the original 4 branches below.
            foreach (KeyValuePair<Vector2Int, EdgeTarget> kvp in _occupiedCells)
            {
                Vector2Int cell = kvp.Key;
                EdgeTarget edge = kvp.Value;
                if (!edge.targetable) continue;
                if (_currentShellIds.Count > 0 && !_currentShellIds.Contains(edge.balloonId))
                    continue;

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
            int line = 0;
            switch (scanDir)
            {
                case ScanDirection.Right:
                case ScanDirection.Left:
                    line = dartCell.y + lineOffset;
                    break;
                case ScanDirection.Up:
                case ScanDirection.Down:
                    line = dartCell.x + lineOffset;
                    break;
                default:
                    edge = default;
                    return false;
            }

            return TryGetEdgeTargetOnLine(scanDir, line, out edge);
        }

        private static bool TryGetEdgeTargetOnLine(ScanDirection scanDir, int line, out EdgeTarget edge)
        {
            switch (scanDir)
            {
                case ScanDirection.Right:
                    return _leftContourByRow.TryGetValue(line, out edge);
                case ScanDirection.Left:
                    return _rightContourByRow.TryGetValue(line, out edge);
                case ScanDirection.Up:
                    return _bottomContourByCol.TryGetValue(line, out edge);
                case ScanDirection.Down:
                    return _topContourByCol.TryGetValue(line, out edge);
                default:
                    edge = default;
                    return false;
            }
        }

        // [2026-05-22 DBG-TopLeft] 외부에서 contour map 상태 조회 — DartManager 좌상단 진단용. 캡쳐 끝나면 제거.
        public static bool TryGetContourEdgeForDirection(
            ScanDirection scanDir, int line,
            out int balloonId, out int cellX, out int cellY, out int color, out bool targetable)
        {
            BuildEdgeTargetCache();
            if (TryGetEdgeTargetOnLine(scanDir, line, out EdgeTarget edge))
            {
                balloonId = edge.balloonId;
                cellX = edge.cell.x;
                cellY = edge.cell.y;
                color = edge.color;
                targetable = edge.targetable;
                return true;
            }
            balloonId = -1; cellX = 0; cellY = 0; color = -1; targetable = false;
            return false;
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
            if (BalloonController.HasInstance && BalloonController.Instance.IsBalloonConcealed(balloon.balloonId)) return false;
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
