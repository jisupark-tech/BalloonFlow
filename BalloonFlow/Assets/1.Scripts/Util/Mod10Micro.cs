// Mod10Micro.cs
// ROLLBACK_MAPMAKER_MOD10_MICRO_20260706: mod10_micro v4 "미세조정" 포팅.
//   원본: 도구/mod10_micro.py (탐색→실행, 실행중 재점검). 짝 문서 미세조정_기술서/수도코드.
//   역할: 각 '주제(subject) 색'의 개수를 invisible 하게 10배수로 맞춰 사람이 손볼 색 '가짓수'를 줄인다.
//     · move 3종: shift(주제 B→A), shave(A→빈칸), grow(빈칸→A)
//     · invisible 검증: 곡률(curv)>=1.0 + (저대비 or 큰면 or 완벽곡률) + 외곽빈칸 접촉 + 덩어리 무분리
//     · 커밋 규칙: 어긋난 색 '가짓수'가 strict 하게 줄 때만 채택 → 종료 보장(핑퐁 없음)
//   Round x10(Mod10)과 별개 단계: 색은 그대로 두고 경계·외곽만 한 칸씩 조정. 못 풀면 데이터로 남김.
//   grid: [H,W] = [y,x], 셀=색ID(0..27) 또는 EMPTY(-1). 팔레트/EMPTY 는 Mod10 재사용.
using System;
using System.Collections.Generic;

public static class Mod10Micro
{
    public const int EMPTY = Mod10.EMPTY;
    public const float DEFAULT_THE = 50f;

    public class Report
    {
        public int Shifted;                                                  // 바뀐 셀 수
        public List<(int y, int x, int fromC, int toC)> ShiftedCells = new List<(int, int, int, int)>();
        public List<int> InternalResolved = new List<int>();                 // 이번에 10배수로 해결된 색ID
        public Dictionary<int, int> Unresolved = new Dictionary<int, int>(); // 색ID -> 나머지(사람에게 넘길 데이터)
    }

    static readonly int[][] D4 = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };

    // ───────────────────────── Lab / ΔE (28색 캐시) ─────────────────────────
    static readonly double[][] LAB = BuildLab();
    static double[][] BuildLab()
    {
        var pal = Mod10.PALETTE;
        var lab = new double[pal.Length][];
        for (int i = 0; i < pal.Length; i++)
        {
            double r = pal[i][0] / 255.0, g = pal[i][1] / 255.0, b = pal[i][2] / 255.0;
            r = Lin(r); g = Lin(g); b = Lin(b);
            double X = r * 0.4124 + g * 0.3576 + b * 0.1805;
            double Y = r * 0.2126 + g * 0.7152 + b * 0.0722;
            double Z = r * 0.0193 + g * 0.1192 + b * 0.9505;
            X /= 0.95047; Z /= 1.08883;                 // D65
            double fx = Fl(X), fy = Fl(Y), fz = Fl(Z);
            lab[i] = new double[] { 116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz) };
        }
        return lab;
    }
    static double Lin(double c) => c > 0.04045 ? Math.Pow((c + 0.055) / 1.055, 2.4) : c / 12.92;
    static double Fl(double t) => t > 0.008856 ? Math.Pow(t, 1.0 / 3.0) : 7.787 * t + 16.0 / 116.0;
    static double DE(int a, int b)
    {
        if (a < 0 || b < 0 || a >= LAB.Length || b >= LAB.Length) return 999.0;
        var la = LAB[a]; var lb = LAB[b];
        double d0 = la[0] - lb[0], d1 = la[1] - lb[1], d2 = la[2] - lb[2];
        return Math.Sqrt(d0 * d0 + d1 * d1 + d2 * d2);
    }

    // ───────────────────────── 주제/배경 마스크 ─────────────────────────
    // background = 테두리에서 연결된 '지배 배경색' 영역(4-flood). subject = 비어있지않음 && !background.
    static bool[,] BackgroundMask(int[,] g)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        var bg = new bool[H, W];
        var borderCount = new Dictionary<int, int>();
        for (int x = 0; x < W; x++) { CountBorder(g, 0, x, borderCount); CountBorder(g, H - 1, x, borderCount); }
        for (int y = 0; y < H; y++) { CountBorder(g, y, 0, borderCount); CountBorder(g, y, W - 1, borderCount); }
        if (borderCount.Count == 0) return bg;          // 테두리 색 없음 → 배경 없음(전부 주제)
        int bgColor = -1, bgMax = -1;
        foreach (var kv in borderCount) if (kv.Value > bgMax) { bgMax = kv.Value; bgColor = kv.Key; }
        var q = new Queue<(int, int)>();
        for (int x = 0; x < W; x++) { Seed(g, 0, x, bgColor, bg, q); Seed(g, H - 1, x, bgColor, bg, q); }
        for (int y = 0; y < H; y++) { Seed(g, y, 0, bgColor, bg, q); Seed(g, y, W - 1, bgColor, bg, q); }
        while (q.Count > 0)
        {
            var (y, x) = q.Dequeue();
            foreach (var d in D4)
            {
                int ny = y + d[0], nx = x + d[1];
                if (ny >= 0 && ny < H && nx >= 0 && nx < W && !bg[ny, nx] && g[ny, nx] == bgColor)
                { bg[ny, nx] = true; q.Enqueue((ny, nx)); }
            }
        }
        return bg;
    }
    static void CountBorder(int[,] g, int y, int x, Dictionary<int, int> c)
    { int v = g[y, x]; if (v != EMPTY) { c.TryGetValue(v, out int n); c[v] = n + 1; } }
    static void Seed(int[,] g, int y, int x, int bgColor, bool[,] bg, Queue<(int, int)> q)
    { if (g[y, x] == bgColor && !bg[y, x]) { bg[y, x] = true; q.Enqueue((y, x)); } }

    static bool[,] SubjectMask(int[,] g)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        var bg = BackgroundMask(g);
        var s = new bool[H, W];
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) s[y, x] = g[y, x] != EMPTY && !bg[y, x];
        return s;
    }

    // ───────────────────────── 기하 헬퍼 ─────────────────────────
    static int Complexity(int[,] g, int y, int x, int R)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        var seen = new HashSet<int>();
        for (int dy = -R; dy <= R; dy++)
            for (int dx = -R; dx <= R; dx++)
            {
                int ny = y + dy, nx = x + dx;
                if (ny >= 0 && ny < H && nx >= 0 && nx < W && g[ny, nx] != EMPTY) seen.Add(g[ny, nx]);
            }
        return seen.Count;
    }

    // (y,x)를 A 로 바꿀 때 곡률. A 와 안 붙으면 null.
    static double? Curv(int[,] g, int y, int x, int A)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        bool IsA(int py, int px) => py >= 0 && py < H && px >= 0 && px < W && g[py, px] == A;
        bool N = IsA(y - 1, x), S = IsA(y + 1, x), E = IsA(y, x + 1), Wt = IsA(y, x - 1);
        int na = (N ? 1 : 0) + (S ? 1 : 0) + (E ? 1 : 0) + (Wt ? 1 : 0);
        if (na == 0) return null;
        if (na == 2)
        {
            if ((N && E) || (N && Wt) || (S && E) || (S && Wt)) return 3.0; // ㄱ자 코너(급대각계단)
            return 0.4;                                                      // 마주보는 두 변(얇은 다리)
        }
        if (na == 1) return 0.3;   // 평면 돌기
        if (na == 3) return 1.0;   // 오목 홈
        return 1.5;                // na==4, 완전 구멍
    }

    // 테두리에서 4방향 연결된 빈칸.
    static bool[,] ExteriorEmpty(int[,] g)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        var ext = new bool[H, W];
        var q = new Queue<(int, int)>();
        for (int y = 0; y < H; y++) foreach (int x in new[] { 0, W - 1 }) if (g[y, x] == EMPTY && !ext[y, x]) { ext[y, x] = true; q.Enqueue((y, x)); }
        for (int x = 0; x < W; x++) foreach (int y in new[] { 0, H - 1 }) if (g[y, x] == EMPTY && !ext[y, x]) { ext[y, x] = true; q.Enqueue((y, x)); }
        while (q.Count > 0)
        {
            var (y, x) = q.Dequeue();
            foreach (var d in D4)
            {
                int ny = y + d[0], nx = x + d[1];
                if (ny >= 0 && ny < H && nx >= 0 && nx < W && g[ny, nx] == EMPTY && !ext[ny, nx]) { ext[ny, nx] = true; q.Enqueue((ny, nx)); }
            }
        }
        return ext;
    }

    // (y,x) 빼도 같은색 4-이웃이 서로 8방향으로 여전히 연결되나.
    static bool ConnectedWithout(int[,] g, int y, int x)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        int c = g[y, x];
        var nb = new List<(int, int)>();
        foreach (var d in D4) { int ny = y + d[0], nx = x + d[1]; if (ny >= 0 && ny < H && nx >= 0 && nx < W && g[ny, nx] == c) nb.Add((ny, nx)); }
        if (nb.Count < 2) return false;
        var nbSet = new HashSet<(int, int)>(nb);
        var seen = new HashSet<(int, int)> { nb[0] };
        var st = new Stack<(int, int)>(); st.Push(nb[0]);
        while (st.Count > 0)
        {
            var (cy, cx) = st.Pop();
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var p = (cy + dy, cx + dx);
                    if (p == (y, x)) continue;
                    if (nbSet.Contains(p) && !seen.Contains(p)) { seen.Add(p); st.Push(p); }
                }
        }
        return seen.Count == nb.Count;
    }

    static bool NearAvoid(List<(int, int)> avoid, int y, int x)
    {
        foreach (var (py, px) in avoid) if (Math.Abs(y - py) + Math.Abs(x - px) <= 1) return true;
        return false;
    }

    // (y,x)→A move 의 invisible 점수. 불가면 null.
    static double? ViableScore(int[,] g, int y, int x, int A, int B, float thE, List<(int, int)> avoid)
    {
        double? cs = Curv(g, y, x, A);
        if (cs == null || cs.Value < 1.0) return null;              // 돌기(0.3)·얇은다리(0.4) 제외
        int cx3 = Complexity(g, y, x, 1);
        if (B != EMPTY && DE(A, B) > thE)                           // 고대비 shift
            if (cs.Value < 2.0 && cx3 >= 3) return null;            //   디테일부 고대비 경계 금지
        int pen = NearAvoid(avoid, y, x) ? 3 : 0;
        return cs.Value * 10 - Complexity(g, y, x, 2) - pen;
    }

    // ───────────────────────── 단일 move 후보 ─────────────────────────
    struct MoveCand { public bool ok; public int y, x, fromC; public double score; }

    // kind: "grow"(빈칸→to), "shift"(주제B→to), "shave"(to→빈칸). to 를 +1(또는 shave 면 -1) 하는 best 셀.
    static MoveCand FindMove(int[,] g, bool[,] subject, bool[,] ext, float thE, List<(int, int)> avoid, int toColor, string kind)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        var best = new MoveCand { ok = false, score = double.NegativeInfinity };
        if (kind == "grow")
        {
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            {
                if (!ext[y, x]) continue;
                if (!HasNeighborColor(g, y, x, toColor)) continue;
                double? s = ViableScore(g, y, x, toColor, EMPTY, thE, avoid);
                if (s != null && s.Value > best.score) best = new MoveCand { ok = true, y = y, x = x, fromC = EMPTY, score = s.Value };
            }
        }
        else if (kind == "shift")
        {
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            {
                if (!subject[y, x] || g[y, x] == toColor) continue;
                if (!HasNeighborColor(g, y, x, toColor)) continue;
                int B = g[y, x];
                double? s = ViableScore(g, y, x, toColor, B, thE, avoid);
                if (s != null && s.Value > best.score) best = new MoveCand { ok = true, y = y, x = x, fromC = B, score = s.Value };
            }
        }
        else // shave : toColor → 빈칸
        {
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            {
                if (!subject[y, x] || g[y, x] != toColor) continue;
                int en = 0, nc = 0;
                foreach (var d in D4)
                {
                    int ny = y + d[0], nx = x + d[1];
                    if (ny >= 0 && ny < H && nx >= 0 && nx < W)
                    {
                        if (ext[ny, nx]) en++;
                        if (g[ny, nx] == toColor) nc++;
                    }
                }
                if (en == 0) continue;
                if (nc < 2 || !ConnectedWithout(g, y, x)) continue;
                int pen = NearAvoid(avoid, y, x) ? 3 : 0;
                double s = en * 10 + nc - pen;
                if (s > best.score) best = new MoveCand { ok = true, y = y, x = x, fromC = toColor, score = s };
            }
        }
        return best;
    }
    static bool HasNeighborColor(int[,] g, int y, int x, int color)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        foreach (var d in D4) { int ny = y + d[0], nx = x + d[1]; if (ny >= 0 && ny < H && nx >= 0 && nx < W && g[ny, nx] == color) return true; }
        return false;
    }

    // 실제 적용될 move: (kind, y, x, fromC, toC)
    struct Move { public bool ok; public string kind; public int y, x, fromC, toC; }

    static Move BestReduceMove(int[,] g, bool[,] subject, bool[,] ext, int c, float thE, List<(int, int)> avoid, Dictionary<int, int> resid)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        var cand = new List<(double score, string kind, int y, int x, int A)>();
        var m = FindMove(g, subject, ext, thE, avoid, c, "shave");
        if (m.ok) cand.Add((m.score + 100, "shave", m.y, m.x, EMPTY));   // 빈칸행 +100 최우선
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            if (!subject[y, x] || g[y, x] != c) continue;
            var neigh = new HashSet<int>();
            foreach (var d in D4)
            {
                int ny = y + d[0], nx = x + d[1];
                if (ny >= 0 && ny < H && nx >= 0 && nx < W && subject[ny, nx] && g[ny, nx] != c) neigh.Add(g[ny, nx]);
            }
            foreach (int A in neigh)
            {
                double? s = ViableScore(g, y, x, A, c, thE, avoid);
                if (s == null) continue;
                int ra = resid.TryGetValue(A, out int rr) ? rr : 0;
                int bonus = ra > 5 ? 8 : 0;
                int pen = ra == 0 ? 12 : 0;
                cand.Add((s.Value + bonus - pen, "shift", y, x, A));
            }
        }
        if (cand.Count == 0) return new Move { ok = false };
        cand.Sort((a, b) => b.score.CompareTo(a.score));
        var top = cand[0];
        return new Move { ok = true, kind = top.kind, y = top.y, x = top.x, fromC = c, toC = top.kind == "shift" ? top.A : EMPTY };
    }

    static Move BestAddMove(int[,] g, bool[,] subject, bool[,] ext, int c, float thE, List<(int, int)> avoid, Dictionary<int, int> resid)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        var cand = new List<(double score, string kind, int y, int x, int B)>();
        var m = FindMove(g, subject, ext, thE, avoid, c, "grow");
        if (m.ok) cand.Add((m.score + 100, "grow", m.y, m.x, EMPTY));
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            if (!subject[y, x] || g[y, x] == c) continue;
            if (!HasNeighborColor(g, y, x, c)) continue;
            int B = g[y, x];
            double? s = ViableScore(g, y, x, c, B, thE, avoid);
            if (s == null) continue;
            int rb = resid.TryGetValue(B, out int rr) ? rr : 0;
            int bonus = (rb > 0 && rb <= 5) ? 8 : 0;
            int pen = rb == 0 ? 12 : 0;
            cand.Add((s.Value + bonus - pen, "shift", y, x, B));
        }
        if (cand.Count == 0) return new Move { ok = false };
        cand.Sort((a, b) => b.score.CompareTo(a.score));
        var top = cand[0];
        return new Move { ok = true, kind = top.kind, y = top.y, x = top.x, fromC = top.kind == "shift" ? top.B : EMPTY, toC = c };
    }

    // ───────────────────────── 한 색 해결 ─────────────────────────
    static bool ResolveColor(int[,] grid, bool[,] subject, int c, float thE, List<(int, int)> avoid, Dictionary<int, int> resid,
                             bool dry, out List<Move> moves, out int[,] outGrid, out bool[,] outSubject)
    {
        int H = grid.GetLength(0), W = grid.GetLength(1);
        int n = 0;
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (subject[y, x] && grid[y, x] == c) n++;
        int r = n % 10;
        moves = new List<Move>();
        outGrid = grid; outSubject = subject;
        if (r == 0) return true;

        bool reduce = r <= (10 - r);
        int k = Math.Min(r, 10 - r);
        int[,] G = dry ? (int[,])grid.Clone() : grid;
        bool[,] Sub = dry ? (bool[,])subject.Clone() : subject;
        var localAvoid = new List<(int, int)>(avoid);
        for (int step = 0; step < k; step++)
        {
            var ext = ExteriorEmpty(G);
            Move mv = reduce ? BestReduceMove(G, Sub, ext, c, thE, localAvoid, resid)
                             : BestAddMove(G, Sub, ext, c, thE, localAvoid, resid);
            if (!mv.ok) { outGrid = G; outSubject = Sub; return false; }
            G[mv.y, mv.x] = mv.toC == EMPTY ? EMPTY : mv.toC;
            Sub[mv.y, mv.x] = mv.toC != EMPTY;
            moves.Add(mv);
            localAvoid.Add((mv.y, mv.x));
        }
        outGrid = G; outSubject = Sub;
        return true;
    }

    static Dictionary<int, int> CountBySubject(int[,] g, bool[,] subject)
    {
        int H = g.GetLength(0), W = g.GetLength(1);
        var cnt = new Dictionary<int, int>();
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            if (subject[y, x]) { int v = g[y, x]; cnt.TryGetValue(v, out int c); cnt[v] = c + 1; }
        return cnt;
    }
    static int OffCount(Dictionary<int, int> cnt)
    {
        int o = 0;
        foreach (var kv in cnt) if (kv.Value % 10 != 0) o++;
        return o;
    }

    // ───────────────────────── 메인 (탐색→실행, 가짓수 strict 감소) ─────────────────────────
    public static (int[,], Report) MicroSolve(int[,] gridIn, float thE = DEFAULT_THE)
    {
        int H = gridIn.GetLength(0), W = gridIn.GetLength(1);
        var grid = (int[,])gridIn.Clone();                          // 비파괴
        var rep = new Report();

        var subj0 = SubjectMask(grid);
        var cnt0 = CountBySubject(grid, subj0);
        var off0 = new HashSet<int>();
        foreach (var kv in cnt0) if (kv.Value % 10 != 0) off0.Add(kv.Key);

        var avoid = new List<(int, int)>();
        int guard = 0;
        while (guard < 400)
        {
            guard++;
            var subj = SubjectMask(grid);
            var cnt = CountBySubject(grid, subj);
            var resid = new Dictionary<int, int>();
            foreach (var kv in cnt) resid[kv.Key] = kv.Value % 10;
            int cur = OffCount(cnt);
            if (cur == 0) break;

            // 탐색: 10배수 거리 d 오름차순(간당간당 색 먼저), 동점이면 색ID 순.
            var offs = new List<(int d, int c)>();
            foreach (var kv in cnt) if (kv.Value % 10 != 0) { int r = kv.Value % 10; offs.Add((Math.Min(r, 10 - r), kv.Key)); }
            offs.Sort((a, b) => a.d != b.d ? a.d.CompareTo(b.d) : a.c.CompareTo(b.c));

            int chosen = -1;
            foreach (var (d, c) in offs)
            {
                bool ok = ResolveColor(grid, subj, c, thE, avoid, resid, true, out _, out var simGrid, out var simSub);
                if (!ok) continue;
                if (OffCount(CountBySubject(simGrid, simSub)) < cur) { chosen = c; break; } // 가짓수 strict 감소만
            }
            if (chosen < 0) break;                                  // 더 못 줄임 → 나머지는 데이터

            ResolveColor(grid, subj, chosen, thE, avoid, resid, false, out var moves, out _, out _);
            foreach (var mv in moves) { rep.ShiftedCells.Add((mv.y, mv.x, mv.fromC, mv.toC)); avoid.Add((mv.y, mv.x)); }
        }

        var subjF = SubjectMask(grid);
        var cntF = CountBySubject(grid, subjF);
        var off1 = new HashSet<int>();
        foreach (var kv in cntF) if (kv.Value % 10 != 0) { off1.Add(kv.Key); rep.Unresolved[kv.Key] = kv.Value % 10; }
        foreach (int c in off0) if (!off1.Contains(c)) rep.InternalResolved.Add(c);
        rep.Shifted = rep.ShiftedCells.Count;
        return (grid, rep);
    }
}
