// =============================================================================
// Mod10.cs — 10배수 정합 모듈 (유니티 C# 이식용 통짜 코드)
// 파이썬 레퍼런스 mod10.py 와 1:1 동일 로직. 모든 분기 포함(요약본 아님).
//
// 역할: 색은 '고르지' 않음. 색매핑이 끝난 grid(셀=색ID 0~27, 빈칸=EMPTY)를 받아
//       모든 색 카운트를 10의 배수로 보정. (탄창 분해/클리어 가능성 보장)
// 핵심 분기:
//   STEP A. 총 다트 10배수 — 부족분은 좌상단/우상단 구석을 EMPTY로.
//   STEP B. 색별 목표 카운트 — 버퍼(가장 많이 연결된 색)가 잔여 흡수.
//   STEP C. relay(다중 홉) — 색 인접 그래프에서 grower→shrinker 경로로 셀을 이어 전달.
//           (단일 홉만 하면 무한 핑퐁/실패! 반드시 BFS 다중 홉.)
//   STEP D. 셀 선택 — 그 색 본체에 깊이 붙고(=같은색 이웃 많음) 가장자리(저현저도)인 셀.
//
// 사용:
//   var (outGrid, rep) = Mod10.EnforceMod10(grid);            // frameColor 없음
//   var (outGrid, rep) = Mod10.EnforceMod10(grid, frameColor:6); // 흰 프레임 등 불가침 색
// =============================================================================
using System;
using System.Collections.Generic;
using System.Linq;

public static class Mod10
{
    public const int EMPTY = -1;
    public const int NO_FRAME = int.MinValue;

    // BL 28색 팔레트 (R,G,B). 인덱스 = 색ID 0~27.
    public static readonly int[][] PALETTE = new int[][] {
        new[]{252,106,175}, new[]{80,232,246}, new[]{137,80,248}, new[]{254,213,85},
        new[]{115,254,102}, new[]{253,161,76}, new[]{255,255,255}, new[]{65,65,65},
        new[]{110,168,250}, new[]{57,174,46},  new[]{252,94,94},   new[]{50,107,248},
        new[]{58,165,139},  new[]{231,167,250},new[]{183,199,251}, new[]{106,74,48},
        new[]{254,227,169}, new[]{253,183,193},new[]{158,61,94},   new[]{167,221,148},
        new[]{89,46,126},   new[]{220,120,129},new[]{217,217,231}, new[]{111,114,127},
        new[]{252,56,165},  new[]{253,194,122}, new[]{137,10,8},    new[]{111,175,177},
    };

    public class Report
    {
        public bool AllMod10;
        public Dictionary<int,int> ColorCounts;
        public int Total;
        public int EmptyCornerCells;
        public int ChangedCells;
    }

    static readonly int[] DY = { 1, -1, 0, 0 };
    static readonly int[] DX = { 0, 0, 1, -1 };

    // ---------- 메인 진입점 ----------
    public static (int[,], Report) EnforceMod10(int[,] gridIn, int frameColor = NO_FRAME)
    {
        int H = gridIn.GetLength(0), W = gridIn.GetLength(1);
        int[,] orig = (int[,])gridIn.Clone();

        int removed;
        int[,] g = FixTotal(gridIn, out removed);          // STEP A
        g = FixPerColor(g, frameColor);                    // STEP B~D

        // 보고
        var counts = ColorCounts(g);
        int total = counts.Values.Sum();
        bool ok = total % 10 == 0 && counts.Values.All(n => n % 10 == 0);
        int changed = 0;
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (g[y, x] != orig[y, x]) changed++;

        var rep = new Report {
            AllMod10 = ok, ColorCounts = counts, Total = total,
            EmptyCornerCells = removed, ChangedCells = changed
        };
        return (g, rep);
    }

    // ---------- STEP A. 총 다트 10배수 (구석 빈칸) ----------
    static int[,] FixTotal(int[,] gridIn, out int removed)
    {
        int H = gridIn.GetLength(0), W = gridIn.GetLength(1);
        int[,] g = (int[,])gridIn.Clone();
        int total = 0;
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (g[y, x] != EMPTY) total++;
        int rem = total % 10;
        removed = 0;
        if (rem == 0) return g;
        int row = 0;
        while (removed < rem && row < H)
        {
            int left = 0, right = W - 1;
            while (removed < rem && left <= right)
            {
                foreach (int x in new[] { left, right })
                {
                    if (removed >= rem) break;
                    if (g[row, x] != EMPTY) { g[row, x] = EMPTY; removed++; }
                }
                left++; right--;
            }
            row++;
        }
        return g;
    }

    // ---------- 색별 카운트 ----------
    static Dictionary<int,int> ColorCounts(int[,] g)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        var d = new Dictionary<int,int>();
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            int c = g[y, x];
            if (c == EMPTY) continue;
            d[c] = d.TryGetValue(c, out int v) ? v + 1 : 1;
        }
        return d;
    }

    // ---------- 현저도(salience) ----------
    static Dictionary<int,double> Salience(int[,] g)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        int total = 0;
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (g[y, x] != EMPTY) total++;
        // 외곽 테두리 셀 모음 (Python: concat(top,bottom,left,right) — 모서리 중복 포함)
        var border = new List<int>();
        for (int x = 0; x < W; x++) { border.Add(g[0, x]); border.Add(g[H - 1, x]); }
        for (int y = 0; y < H; y++) { border.Add(g[y, 0]); border.Add(g[y, W - 1]); }

        double cy = H / 2.0, cx = W / 2.0, maxd = Math.Sqrt(cy * cy + cx * cx);
        var sal = new Dictionary<int,double>();
        foreach (int c in ColorCounts(g).Keys)
        {
            int n = 0, ymin = int.MaxValue, ymax = int.MinValue, xmin = int.MaxValue, xmax = int.MinValue;
            double sy = 0, sx = 0;
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                if (g[y, x] == c) { n++; sy += y; sx += x;
                    if (y < ymin) ymin = y; if (y > ymax) ymax = y;
                    if (x < xmin) xmin = x; if (x > xmax) xmax = x; }
            double bb = (ymax - ymin + 1) * (xmax - xmin + 1);
            double fill = n / bb;
            double my = sy / n, mx = sx / n;
            double central = 1.0 - Math.Sqrt((my - cy) * (my - cy) + (mx - cx) * (mx - cx)) / maxd;
            int bcnt = border.Count(v => v == c);
            double bshare = (double)bcnt / border.Count;
            sal[c] = fill * (0.4 + 0.6 * central) * (1.0 / (1.0 + (double)n / total * 4.0)) * (1.0 - 0.5 * bshare);
        }
        return sal;
    }

    // ---------- 색 인접 그래프 ----------
    static Dictionary<int,HashSet<int>> ColorAdj(int[,] g)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        var adj = new Dictionary<int,HashSet<int>>();
        foreach (int c in ColorCounts(g).Keys) adj[c] = new HashSet<int>();
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            int c = g[y, x];
            if (c == EMPTY) continue;
            // 오른쪽/아래 두 방향만 검사(대칭으로 양방향 추가)
            if (y + 1 < H) { int d = g[y + 1, x]; if (d != EMPTY && d != c) { adj[c].Add(d); adj[d].Add(c); } }
            if (x + 1 < W) { int d = g[y, x + 1]; if (d != EMPTY && d != c) { adj[c].Add(d); adj[d].Add(c); } }
        }
        return adj;
    }

    // 파이썬 round()와 동일하게 '짝수 반올림'(round-half-to-even). 예: 25→20, 35→40.
    static int Round10(int x) => (int)Math.Round(x / 10.0, MidpointRounding.ToEven) * 10;

    // ---------- STEP B~D. 색별 10배수 (relay 다중홉 + 모티프 안전 배치) ----------
    static int[,] FixPerColor(int[,] gridIn, int frameColor)
    {
        int H = gridIn.GetLength(0), W = gridIn.GetLength(1);
        int[,] g = (int[,])gridIn.Clone();

        var cnt = ColorCounts(g);
        var cols = cnt.Keys.OrderBy(c => c).ToList();
        int total = cnt.Values.Sum();
        var sal = Salience(g);
        var adj0 = ColorAdj(g);

        var cand = cols.Where(c => c != frameColor).ToList();
        // 버퍼 = 가장 많이 연결된 색 (동률이면 현저도 낮은 색)
        int buffer = cand[0];
        {
            double bestDeg = -1, bestSalNeg = double.NegativeInfinity;
            foreach (int c in cand)
            {
                double deg = adj0.ContainsKey(c) ? adj0[c].Count : 0;
                double salNeg = -(sal.ContainsKey(c) ? sal[c] : 0);
                if (deg > bestDeg || (deg == bestDeg && salNeg > bestSalNeg))
                { bestDeg = deg; bestSalNeg = salNeg; buffer = c; }
            }
        }

        // STEP B. 목표 카운트
        var target = new Dictionary<int,int>();
        if (frameColor != NO_FRAME && cnt.ContainsKey(frameColor)) target[frameColor] = cnt[frameColor];
        foreach (int c in cand) if (c != buffer) target[c] = Round10(cnt[c]);
        int sumOther = target.Where(kv => kv.Key != buffer).Sum(kv => kv.Value);
        target[buffer] = total - sumOther;

        var delta = new Dictionary<int,int>();
        foreach (int c in cols) delta[c] = (target.ContainsKey(c) ? target[c] : cnt[c]) - cnt[c];

        // STEP C. relay (다중 홉)
        int guard = 0;
        while (delta.Values.Any(d => d > 0) && guard < 60000)
        {
            guard++;
            int G = cols.First(c => delta[c] > 0);

            // 색 인접 그래프에서 G로부터 가장 가까운 shrinker(delta<0)까지 BFS
            var adj = ColorAdj(g);
            var prev = new Dictionary<int,int>();
            prev[G] = int.MinValue; // null 표시
            var q = new Queue<int>(); q.Enqueue(G);
            int found = int.MinValue; bool ffound = false;
            while (q.Count > 0)
            {
                int u = q.Dequeue();
                if (u != G && delta.ContainsKey(u) && delta[u] < 0) { found = u; ffound = true; break; }
                if (adj.ContainsKey(u))
                    foreach (int v in adj[u])
                        if (!prev.ContainsKey(v) && v != frameColor) { prev[v] = u; q.Enqueue(v); }
            }
            if (!ffound) break;

            // 경로 복원: G ... S(found)
            var path = new List<int> { found };
            while (path[path.Count - 1] != G) path.Add(prev[path[path.Count - 1]]);
            path.Reverse(); // G .. S

            bool ok = true;
            for (int i = 0; i < path.Count - 1; i++)
            {
                var cell = FindCell(g, path[i], path[i + 1]);   // STEP D
                if (cell == null) { ok = false; break; }
                g[cell.Value.Item1, cell.Value.Item2] = path[i];
            }
            if (!ok) break;
            delta[G] -= 1; delta[found] += 1;
        }
        return g;
    }

    // ---------- STEP D. 셀 선택: A로 바꿀 B셀 (A본체에 깊이 붙고 가장자리) ----------
    static (int,int)? FindCell(int[,] g, int Acol, int Bcol)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        double cy = H / 2.0, cx = W / 2.0;
        (int,int)? best = null; double bestScore = double.NegativeInfinity;
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            if (g[y, x] != Bcol) continue;
            int nA = 0; bool adjA = false;
            for (int k = 0; k < 4; k++)
            {
                int ny = y + DY[k], nx = x + DX[k];
                if (ny >= 0 && ny < H && nx >= 0 && nx < W && g[ny, nx] == Acol) { nA++; adjA = true; }
            }
            if (!adjA) continue;
            double dist = Math.Sqrt((y - cy) * (y - cy) + (x - cx) * (x - cx));
            double score = nA * 3 + dist * 0.05;
            // mod10.py: `if bs is None or sc>bs: bs=sc; best=(y,x)` — bestScore=-inf 초기화로 동치.
            if (score > bestScore) { bestScore = score; best = (y, x); }
        }
        return best;
    }
}
