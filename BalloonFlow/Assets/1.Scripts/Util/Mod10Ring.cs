// =============================================================================
// Mod10Ring.cs — 아웃라인 링 (별도 버튼, 사람 판단) — 유니티 C# 이식용 통짜 코드
// 파이썬 레퍼런스 mod10_ring.py 와 1:1 동일 로직. 모든 분기 포함(요약본 아님).
//
// 역할: 사용자가 지정한 색 1종을, 배치된 픽셀 덩어리 '바깥 테두리'에 둘러
//       그 색 개수를 10의 배수로 만든다. (의도적으로 눈에 보이는 테두리 → 난이도 상승)
//       Round x10(Mod10, 전 색 보정)·Assist x10(Mod10Micro, 미세조정)과 독립.
//
// 규칙(사용자 명세):
//   · 색은 사용자가 지정(맵에디터 브러시 색). 그리드에 이미 있든 없든 결과에서 그 색=10배수.
//   · 원래 덩어리(모든 픽셀)를 '적어도 1겹' 감싼다. 링은 '바깥(테두리 연결) 빈칸'에만.
//   · 순서: ① 대각 없이 1겹(4-인접) → %10 맞으면 끝
//           ② 대각까지(8-인접 코너 추가) → %10 맞으면 끝
//           ③ 안 맞으면 한 칸씩 깎아 10배수(대각·팁·바깥부터, 매번 재점검)
//   · 링은 '원래 덩어리' 기준 한 번만 계산(무한루프 방지). 그리드 끝은 패스(in-bounds).
//   · 선택 색 1종만 배치/체크. 나머지 색은 절대 안 건드림(EMPTY 칸에만 색칠).
//
// 사용:
//   var (outGrid, rep) = Mod10Ring.RingSolve(grid, color);   // grid = [H,W] 행우선, color = 색ID
// =============================================================================
using System;
using System.Collections.Generic;

public static class Mod10Ring
{
    public const int EMPTY = -1;

    static readonly int[] D4Y = { 1, -1, 0, 0 };
    static readonly int[] D4X = { 0, 0, 1, -1 };
    static readonly int[] DDY = { 1, 1, -1, -1 };
    static readonly int[] DDX = { 1, -1, 1, -1 };

    public class Report
    {
        public int ColorId;
        public int Before;
        public int After;
        public int Added;
        public int Removed;
        public string Mode;
        public bool TargetMult10;
        public string Note = "";
    }

    // ---------- 바깥 빈칸: 테두리에서 4방향 연결된 빈칸(내부 고립 빈칸 제외) ----------
    static bool[,] ExteriorEmpty(int[,] grid)
    {
        int H = grid.GetLength(0), W = grid.GetLength(1);
        var ext = new bool[H, W];
        var q = new Queue<(int, int)>();
        // 시드: 가장자리 열(0, W-1)의 빈칸
        for (int y = 0; y < H; y++)
            foreach (int x in new[] { 0, W - 1 })
                if (grid[y, x] == EMPTY && !ext[y, x]) { ext[y, x] = true; q.Enqueue((y, x)); }
        // 시드: 가장자리 행(0, H-1)의 빈칸
        for (int x = 0; x < W; x++)
            foreach (int y in new[] { 0, H - 1 })
                if (grid[y, x] == EMPTY && !ext[y, x]) { ext[y, x] = true; q.Enqueue((y, x)); }
        while (q.Count > 0)
        {
            var (y, x) = q.Dequeue();
            for (int k = 0; k < 4; k++)
            {
                int ny = y + D4Y[k], nx = x + D4X[k];
                if (ny >= 0 && ny < H && nx >= 0 && nx < W && grid[ny, nx] == EMPTY && !ext[ny, nx])
                { ext[ny, nx] = true; q.Enqueue((ny, nx)); }
            }
        }
        return ext;
    }

    // ---------- 링 셀: 원래 덩어리(비-EMPTY)를 감싸는 바깥 빈칸 ----------
    //   ring4 = 덩어리에 4-인접 / ring8 = 대각(8-only)-인접(4-인접 아님). 그리드 끝은 자동 제외.
    static void RingCells(int[,] grid, out List<(int, int)> ring4, out List<(int, int)> ring8)
    {
        int H = grid.GetLength(0), W = grid.GetLength(1);
        var ext = ExteriorEmpty(grid);
        ring4 = new List<(int, int)>();
        ring8 = new List<(int, int)>();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (!ext[y, x]) continue;
                bool near4 = false;
                for (int k = 0; k < 4; k++)
                {
                    int ny = y + D4Y[k], nx = x + D4X[k];
                    if (ny >= 0 && ny < H && nx >= 0 && nx < W && grid[ny, nx] != EMPTY) { near4 = true; break; }
                }
                if (near4) { ring4.Add((y, x)); continue; }
                bool near8 = false;
                for (int k = 0; k < 4; k++)
                {
                    int ny = y + DDY[k], nx = x + DDX[k];
                    if (ny >= 0 && ny < H && nx >= 0 && nx < W && grid[ny, nx] != EMPTY) { near8 = true; break; }
                }
                if (near8) ring8.Add((y, x));
            }
    }

    static int ColorNeighbors(int[,] grid, int y, int x, int color)
    {
        int H = grid.GetLength(0), W = grid.GetLength(1);
        int n = 0;
        for (int k = 0; k < 4; k++)
        {
            int ny = y + D4Y[k], nx = x + D4X[k];
            if (ny >= 0 && ny < H && nx >= 0 && nx < W && grid[ny, nx] == color) n++;
        }
        return n;
    }

    static int CountColor(int[,] g, int color)
    {
        int H = g.GetLength(0), W = g.GetLength(1), n = 0;
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (g[y, x] == color) n++;
        return n;
    }

    // ---------- 메인: color(색ID)를 덩어리 바깥 링으로 둘러 그 색을 10배수로 (grid 비파괴) ----------
    public static (int[,], Report) RingSolve(int[,] gridIn, int color)
    {
        int H = gridIn.GetLength(0), W = gridIn.GetLength(1);
        int[,] g = (int[,])gridIn.Clone();
        int orig = CountColor(g, color);
        RingCells(g, out var ring4, out var ring8);
        int r4 = ring4.Count, r8 = ring8.Count;

        // ① 대각 없이 1겹
        if ((orig + r4) % 10 == 0 && r4 > 0)
        {
            foreach (var (y, x) in ring4) g[y, x] = color;
            return (g, new Report {
                ColorId = color, Before = orig, After = orig + r4,
                Added = r4, Removed = 0, Mode = "4-ring(대각X)", TargetMult10 = true });
        }

        // ② 대각까지
        if ((orig + r4 + r8) % 10 == 0 && (r4 + r8) > 0)
        {
            foreach (var (y, x) in ring4) g[y, x] = color;
            foreach (var (y, x) in ring8) g[y, x] = color;
            return (g, new Report {
                ColorId = color, Before = orig, After = orig + r4 + r8,
                Added = r4 + r8, Removed = 0, Mode = "8-ring(대각O)", TargetMult10 = true });
        }

        // ③ 8-ring 배치 후, 아래쪽 10배수까지 한 칸씩 깎기
        var placed = new List<(int, int)>(ring4); placed.AddRange(ring8);
        var ring8set = new HashSet<(int, int)>(ring8);
        foreach (var (y, x) in placed) g[y, x] = color;
        int countB = orig + placed.Count;
        int need = countB % 10;                       // 이만큼 깎으면 floor10
        var placedSet = new HashSet<(int, int)>(placed);
        int removed = 0;
        double cy = H / 2.0, cx = W / 2.0;

        while (need > 0 && placedSet.Count > 0)
        {
            // 후보 정렬키(작을수록 먼저 깎음): (대각0/4방향1, 같은색이웃수, 중심서 먼 것 우선)
            (int, int) best = default; bool hasBest = false;
            int bestDiag = 0, bestCn = 0; double bestFar = 0;
            foreach (var (y, x) in placedSet)
            {
                int cn = ColorNeighbors(g, y, x, color);     // ★ 매번 재계산(재점검)
                int diag = ring8set.Contains((y, x)) ? 0 : 1;
                double far = -((y - cy) * (y - cy) + (x - cx) * (x - cx)); // 먼 것 = 더 작은 값
                bool better;
                if (!hasBest) better = true;
                else if (diag != bestDiag) better = diag < bestDiag;
                else if (cn != bestCn) better = cn < bestCn;
                else better = far < bestFar;
                if (better) { hasBest = true; best = (y, x); bestDiag = diag; bestCn = cn; bestFar = far; }
            }
            g[best.Item1, best.Item2] = EMPTY;
            placedSet.Remove(best); removed++; need--;
        }

        int final = CountColor(g, color);
        bool ok = final % 10 == 0;
        string note = ok ? "" : "공간 부족: 링 셀이 모자라 10배수 미달 (그리드 확장 필요)";
        return (g, new Report {
            ColorId = color, Before = orig, After = final,
            Added = placed.Count - removed, Removed = removed,
            Mode = "8-ring+깎기", TargetMult10 = ok, Note = note });
    }
}
