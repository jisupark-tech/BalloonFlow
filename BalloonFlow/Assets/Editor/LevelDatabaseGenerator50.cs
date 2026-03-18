#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// Generates 50 pre-authored levels (Lv1-50) with shaped balloon grids.
    /// Grid: up to 30×30. Shapes: rectangle, triangle, diamond, star, cross, etc.
    /// All levels have exact dart/balloon symmetry — zero surplus.
    /// Gimmicks start from Lv11: Hidden(11), Spawner_T(21), Spawner_O(31), Pinata(41).
    /// Menu: BalloonFlow > Generate 50 Levels
    /// </summary>
    public static class LevelDatabaseGenerator50
    {
        #region Constants

        private const int MAX_GRID = 30;
        private const float BOARD_WORLD_SIZE = 8f;
        private const float BOARD_CENTER_X = 0f;
        private const float BOARD_CENTER_Z = 2f;
        private const float RAIL_PADDING = 1.5f;
        private const int HOLDER_QUEUE_SLOTS = 5;

        #endregion

        #region Shape Enum

        private enum ShapeType
        {
            Rectangle,
            Triangle,
            Diamond,
            Cross,
            Star,
            Circle,
            HollowRect,
            Arrow
        }

        #endregion

        #region Public

        [MenuItem("BalloonFlow/DON'T USE/Generate 50 Levels")]
        public static void Generate()
        {
            var configs = new LevelConfig[50];

            for (int lv = 1; lv <= 50; lv++)
            {
                configs[lv - 1] = BuildLevel(lv);
                if (lv % 10 == 0)
                    EditorUtility.DisplayProgressBar("Generating Levels", $"Level {lv}/50...", lv / 50f);
            }

            EditorUtility.ClearProgressBar();

            string path = "Assets/Resources/LevelDatabase.asset";
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            var db = AssetDatabase.LoadAssetAtPath<LevelDatabase>(path);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<LevelDatabase>();
                db.levels = configs;
                AssetDatabase.CreateAsset(db, path);
            }
            else
            {
                db.levels = configs;
                EditorUtility.SetDirty(db);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[LevelDatabaseGenerator50] Generated 50 levels (30×30 grid, shaped) → {path}");
            EditorUtility.DisplayDialog("Level Database Generated",
                $"50 levels created (up to 30×30 grid)\n" +
                $"Shapes: Rectangle, Triangle, Diamond, Cross, Star, Circle\n" +
                $"Gimmicks: from Lv11 (design spec)\n\n{path}", "OK");
        }

        #endregion

        #region Level Building

        private static LevelConfig BuildLevel(int levelId)
        {
            var rng = new System.Random(levelId * 37 + 13);

            // Grid size scales: lv1=8, lv50=30
            int gridSize = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(8f, 30f, (levelId - 1f) / 49f)), 5, MAX_GRID);
            float cellSpacing = BOARD_WORLD_SIZE / gridSize;
            float balloonScale = cellSpacing * 0.85f;

            // Color count: scales from 2 (lv1) to 8 (lv50)
            int numColors = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(2f, 8f, (levelId - 1f) / 49f)), 2, 8);

            // Package / position
            int packageId = Mathf.Clamp(((levelId - 1) / 20) + 1, 1, 15);
            int posInPkg = ((levelId - 1) % 20) + 1;
            DifficultyPurpose purpose = DeterminePurpose(packageId, posInPkg, levelId);

            // Choose shape based on level
            ShapeType shape = PickShape(levelId, rng);

            // Generate shape mask
            bool[,] mask = GenerateShapeMask(gridSize, gridSize, shape);

            // Count active cells
            int balloonCount = 0;
            for (int r = 0; r < gridSize; r++)
                for (int c = 0; c < gridSize; c++)
                    if (mask[r, c]) balloonCount++;

            // Ensure minimum balloon count
            if (balloonCount < 10)
            {
                mask = GenerateShapeMask(gridSize, gridSize, ShapeType.Rectangle);
                balloonCount = 0;
                for (int r = 0; r < gridSize; r++)
                    for (int c = 0; c < gridSize; c++)
                        if (mask[r, c]) balloonCount++;
            }

            // Generate balloons on shape
            var balloons = GenerateBalloonGrid(gridSize, cellSpacing, mask, numColors, balloonCount, rng);

            // Gimmicks — from level 2+ (BEFORE holder generation so dart counts include gimmick life)
            string[] gimmickTypes;
            AssignGimmicks(levelId, balloons, rng, out gimmickTypes);

            // Count required darts per color (balloon life: normal=1, Pinata=3, Pin=2)
            int[] dartsNeededPerColor = new int[numColors];
            int totalDartsNeeded = 0;
            for (int i = 0; i < balloons.Length; i++)
            {
                int col = balloons[i].color;
                if (col < 0 || col >= numColors) continue;
                int life = GetGimmickLife(balloons[i].gimmickType);
                dartsNeededPerColor[col] += life;
                totalDartsNeeded += life;
            }

            // Holders with EXACT dart symmetry (uses dartsNeededPerColor, not raw balloon count)
            var holders = GenerateHolders(numColors, dartsNeededPerColor, levelId, rng);

            // Rail
            var rail = GenerateRail(gridSize, cellSpacing);

            // Validation: total darts in holders must equal totalDartsNeeded
            int totalDartsInHolders = 0;
            for (int i = 0; i < holders.Length; i++)
                totalDartsInHolders += holders[i].magazineCount;
            if (totalDartsInHolders != totalDartsNeeded)
            {
                Debug.LogWarning($"[LevelGen50] Lv{levelId} dart mismatch! " +
                    $"Darts in holders={totalDartsInHolders}, needed={totalDartsNeeded}");
            }

            int star1 = balloonCount * 100;

            return new LevelConfig
            {
                levelId = levelId,
                packageId = packageId,
                positionInPackage = posInPkg,
                numColors = numColors,
                balloonCount = balloonCount,
                balloonScale = balloonScale,
                queueColumns = CalculateQueueColumns(purpose, rng),
                targetClearRate = CalculateClearRate(packageId, purpose, rng),
                difficultyPurpose = purpose,
                gimmickTypes = gimmickTypes,
                holders = holders,
                balloons = balloons,
                rail = rail,
                gridCols = gridSize,
                gridRows = gridSize,
                conveyorPositions = System.Array.Empty<Vector2Int>(),
                star1Threshold = star1,
                star2Threshold = Mathf.CeilToInt(star1 * 1.5f),
                star3Threshold = Mathf.CeilToInt(star1 * 2.2f)
            };
        }

        #endregion

        #region Shape Generation

        private static ShapeType PickShape(int levelId, System.Random rng)
        {
            if (levelId <= 3)  return ShapeType.Rectangle;   // Tutorial: simple rect
            if (levelId <= 6)  return ShapeType.Triangle;
            if (levelId <= 10) return ShapeType.Diamond;

            // After lv10, pick from all shapes
            ShapeType[] shapes = {
                ShapeType.Rectangle, ShapeType.Triangle, ShapeType.Diamond,
                ShapeType.Cross, ShapeType.Star, ShapeType.Circle,
                ShapeType.HollowRect, ShapeType.Arrow
            };
            return shapes[rng.Next(shapes.Length)];
        }

        private static bool[,] GenerateShapeMask(int cols, int rows, ShapeType shape)
        {
            bool[,] mask = new bool[rows, cols];
            float cx = (cols - 1) * 0.5f;
            float cy = (rows - 1) * 0.5f;
            float radius = Mathf.Min(cx, cy);

            switch (shape)
            {
                case ShapeType.Rectangle:
                    // Full rectangle (optionally smaller)
                    int marginC = Mathf.Max(0, cols / 6);
                    int marginR = Mathf.Max(0, rows / 6);
                    for (int r = marginR; r < rows - marginR; r++)
                        for (int c = marginC; c < cols - marginC; c++)
                            mask[r, c] = true;
                    break;

                case ShapeType.Triangle:
                    for (int r = 0; r < rows; r++)
                    {
                        float progress = (float)r / (rows - 1);
                        int halfWidth = Mathf.RoundToInt(progress * cx);
                        int center = Mathf.RoundToInt(cx);
                        for (int c = center - halfWidth; c <= center + halfWidth; c++)
                            if (c >= 0 && c < cols) mask[r, c] = true;
                    }
                    break;

                case ShapeType.Diamond:
                    for (int r = 0; r < rows; r++)
                    {
                        float dist = Mathf.Abs(r - cy);
                        int halfWidth = Mathf.RoundToInt((1f - dist / cy) * cx);
                        int center = Mathf.RoundToInt(cx);
                        for (int c = center - halfWidth; c <= center + halfWidth; c++)
                            if (c >= 0 && c < cols) mask[r, c] = true;
                    }
                    break;

                case ShapeType.Cross:
                    int armW = Mathf.Max(2, cols / 4);
                    int armH = Mathf.Max(2, rows / 4);
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                        {
                            bool inHBar = Mathf.Abs(r - cy) <= armH;
                            bool inVBar = Mathf.Abs(c - cx) <= armW;
                            if (inHBar || inVBar) mask[r, c] = true;
                        }
                    break;

                case ShapeType.Star:
                    // 5-point star approximation
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                        {
                            float dx = (c - cx) / cx;
                            float dy = (r - cy) / cy;
                            float angle = Mathf.Atan2(dy, dx);
                            float dist2 = Mathf.Sqrt(dx * dx + dy * dy);
                            float starR = 0.5f + 0.5f * Mathf.Cos(5f * angle);
                            if (dist2 <= starR) mask[r, c] = true;
                        }
                    break;

                case ShapeType.Circle:
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                        {
                            float dx = (c - cx) / cx;
                            float dy = (r - cy) / cy;
                            if (dx * dx + dy * dy <= 1.0f) mask[r, c] = true;
                        }
                    break;

                case ShapeType.HollowRect:
                    int border = Mathf.Max(2, cols / 5);
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                        {
                            bool onEdge = r < border || r >= rows - border || c < border || c >= cols - border;
                            if (onEdge) mask[r, c] = true;
                        }
                    break;

                case ShapeType.Arrow:
                    // Arrow pointing up: triangle top + rectangle stem
                    int stemW = Mathf.Max(2, cols / 4);
                    int headH = rows / 2;
                    int centerC = Mathf.RoundToInt(cx);
                    // Head (triangle)
                    for (int r = 0; r < headH; r++)
                    {
                        float progress = (float)r / headH;
                        int halfWidth = Mathf.RoundToInt(progress * cx);
                        for (int c = centerC - halfWidth; c <= centerC + halfWidth; c++)
                            if (c >= 0 && c < cols) mask[rows - 1 - r, c] = true;
                    }
                    // Stem (rectangle)
                    for (int r = 0; r < rows - headH; r++)
                        for (int c = centerC - stemW; c <= centerC + stemW; c++)
                            if (c >= 0 && c < cols) mask[r, c] = true;
                    break;
            }

            return mask;
        }

        #endregion

        #region Balloon Grid

        private static BalloonLayout[] GenerateBalloonGrid(int gridSize, float cellSpacing,
            bool[,] mask, int numColors, int balloonCount, System.Random rng)
        {
            // Build even color pool
            int[] pool = new int[balloonCount];
            int perColor = balloonCount / numColors;
            int remainder = balloonCount % numColors;
            int idx = 0;
            for (int c = 0; c < numColors; c++)
            {
                int count = perColor + (c < remainder ? 1 : 0);
                for (int i = 0; i < count; i++)
                    pool[idx++] = c;
            }

            // Fisher-Yates shuffle
            for (int i = pool.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int tmp = pool[i]; pool[i] = pool[j]; pool[j] = tmp;
            }

            // Place on grid — use cellSpacing for world positions, fixed board extent
            float halfGrid = BOARD_WORLD_SIZE * 0.5f - cellSpacing * 0.5f;
            var balloons = new BalloonLayout[balloonCount];
            int bid = 0;

            for (int row = 0; row < gridSize; row++)
            {
                for (int col = 0; col < gridSize; col++)
                {
                    if (!mask[row, col]) continue;

                    float wx = BOARD_CENTER_X + col * cellSpacing - halfGrid;
                    float wz = BOARD_CENTER_Z + row * cellSpacing - halfGrid;

                    balloons[bid] = new BalloonLayout
                    {
                        balloonId = bid,
                        color = pool[bid],
                        gridPosition = new Vector2(wx, wz),
                        gimmickType = ""
                    };
                    bid++;
                }
            }

            return balloons;
        }

        #endregion

        #region Gimmicks

        private static void AssignGimmicks(int levelId, BalloonLayout[] balloons, System.Random rng,
            out string[] gimmickTypes)
        {
            if (levelId < 11 || balloons.Length < 5)
            {
                gimmickTypes = System.Array.Empty<string>();
                return;
            }

            var activeGimmicks = new List<string>();

            // Progressive gimmick introduction — 정본: gimmick_spec.yaml 도입 레벨 기준
            if (levelId >= 11) activeGimmicks.Add(GimmickManager.GIMMICK_HIDDEN);      // PKG1 pos11
            if (levelId >= 21) activeGimmicks.Add(GimmickManager.GIMMICK_SPAWNER_T);   // PKG2 pos1
            if (levelId >= 31) activeGimmicks.Add(GimmickManager.GIMMICK_SPAWNER_O);   // PKG2 pos11
            if (levelId >= 41) activeGimmicks.Add(GimmickManager.GIMMICK_PINATA);      // PKG3 pos1 (Big Object)
            // Chain은 Lv61이지만 Generator는 50레벨까지만 생성하므로 미포함

            gimmickTypes = activeGimmicks.ToArray();

            // Assign gimmicks to random balloons
            // Percentage of balloons with gimmicks: 5% (lv11) → 20% (lv50)
            float gimmickRate = Mathf.Lerp(0.05f, 0.20f, (levelId - 11f) / 39f);
            int gimmickCount = Mathf.Max(1, Mathf.RoundToInt(balloons.Length * gimmickRate));

            // Create shuffled indices
            int[] indices = new int[balloons.Length];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            for (int i = indices.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int tmp = indices[i]; indices[i] = indices[j]; indices[j] = tmp;
            }

            // Assign gimmicks to first N shuffled balloons
            for (int i = 0; i < gimmickCount && i < indices.Length; i++)
            {
                string gimmick = activeGimmicks[rng.Next(activeGimmicks.Count)];
                balloons[indices[i]].gimmickType = gimmick;
            }
        }

        #endregion

        #region Holders — Exact Dart Symmetry

        private static HolderSetup[] GenerateHolders(int numColors, int[] balloonsPerColor,
            int levelId, System.Random rng)
        {
            // Holder count scales: lv1=5, lv50=30
            int baseHolders = Mathf.RoundToInt(Mathf.Lerp(5f, 30f, (levelId - 1f) / 49f));
            baseHolders = Mathf.Max(baseHolders, numColors);

            var holderColors = new List<int>();

            // One holder per active color
            for (int c = 0; c < numColors; c++)
            {
                if (balloonsPerColor[c] > 0)
                    holderColors.Add(c);
            }

            // Fill remaining with weighted random
            while (holderColors.Count < baseHolders)
                holderColors.Add(PickWeightedColor(balloonsPerColor, rng));

            // Shuffle
            for (int i = holderColors.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int tmp = holderColors[i]; holderColors[i] = holderColors[j]; holderColors[j] = tmp;
            }

            // Group holders by color
            var colorToIndices = new Dictionary<int, List<int>>();
            for (int i = 0; i < holderColors.Count; i++)
            {
                int c = holderColors[i];
                if (!colorToIndices.ContainsKey(c))
                    colorToIndices[c] = new List<int>();
                colorToIndices[c].Add(i);
            }

            // Distribute magazines: EXACT match per color
            int[] mags = new int[holderColors.Count];

            foreach (var kvp in colorToIndices)
            {
                int color = kvp.Key;
                var idxList = kvp.Value;
                int needed = balloonsPerColor[color];

                if (idxList.Count == 0) continue;

                int perHolder = needed / idxList.Count;
                int leftover = needed % idxList.Count;

                for (int i = 0; i < idxList.Count; i++)
                    mags[idxList[i]] = perHolder + (i < leftover ? 1 : 0);

                // Validation
                int totalMag = 0;
                for (int i = 0; i < idxList.Count; i++) totalMag += mags[idxList[i]];
                if (totalMag != needed)
                {
                    int baseMag = needed / idxList.Count;
                    int extra = needed % idxList.Count;
                    for (int i = 0; i < idxList.Count; i++)
                        mags[idxList[i]] = baseMag + (i < extra ? 1 : 0);
                }
            }

            // Filter out holders with 0 darts (surplus holders from rounding)
            var holderList = new List<HolderSetup>();
            int hid = 0;
            for (int i = 0; i < holderColors.Count; i++)
            {
                if (mags[i] <= 0) continue; // skip empty holders — no surplus darts
                holderList.Add(new HolderSetup
                {
                    holderId = hid++,
                    color = holderColors[i],
                    magazineCount = mags[i],
                    position = Vector2.zero
                });
            }

            return holderList.ToArray();
        }

        private static int PickWeightedColor(int[] balloonsPerColor, System.Random rng)
        {
            int total = 0;
            for (int i = 0; i < balloonsPerColor.Length; i++) total += balloonsPerColor[i];
            if (total <= 0) return rng.Next(balloonsPerColor.Length);

            int roll = rng.Next(total);
            int cum = 0;
            for (int i = 0; i < balloonsPerColor.Length; i++)
            {
                cum += balloonsPerColor[i];
                if (roll < cum) return i;
            }
            return balloonsPerColor.Length - 1;
        }

        #endregion

        #region Rail

        private static RailLayout GenerateRail(int gridSize, float cellSpacing)
        {
            // Fixed rail bounds regardless of grid size — map size stays constant
            float halfBoard = BOARD_WORLD_SIZE * 0.5f;
            float left   = BOARD_CENTER_X - halfBoard - RAIL_PADDING;
            float right  = BOARD_CENTER_X + halfBoard + RAIL_PADDING;
            float bottom = BOARD_CENTER_Z - halfBoard - RAIL_PADDING;
            float top    = BOARD_CENTER_Z + halfBoard + RAIL_PADDING;

            var wp = new List<Vector3>(12);
            // Counter-clockwise: bottom-left → bottom-right → top-right → top-left
            wp.Add(new Vector3(left, 0.5f, bottom));
            wp.Add(new Vector3(Mathf.Lerp(left, right, 0.33f), 0.5f, bottom));
            wp.Add(new Vector3(Mathf.Lerp(left, right, 0.67f), 0.5f, bottom));
            wp.Add(new Vector3(right, 0.5f, bottom));
            wp.Add(new Vector3(right, 0.5f, Mathf.Lerp(bottom, top, 0.33f)));
            wp.Add(new Vector3(right, 0.5f, Mathf.Lerp(bottom, top, 0.67f)));
            wp.Add(new Vector3(right, 0.5f, top));
            wp.Add(new Vector3(Mathf.Lerp(right, left, 0.33f), 0.5f, top));
            wp.Add(new Vector3(Mathf.Lerp(right, left, 0.67f), 0.5f, top));
            wp.Add(new Vector3(left, 0.5f, top));
            wp.Add(new Vector3(left, 0.5f, Mathf.Lerp(top, bottom, 0.33f)));
            wp.Add(new Vector3(left, 0.5f, Mathf.Lerp(top, bottom, 0.67f)));

            var dp = new Vector3[HOLDER_QUEUE_SLOTS];
            for (int i = 0; i < HOLDER_QUEUE_SLOTS; i++)
            {
                float t = (i + 1f) / (HOLDER_QUEUE_SLOTS + 1f);
                dp[i] = new Vector3(Mathf.Lerp(left, right, t), 0.5f, bottom);
            }

            return new RailLayout
            {
                waypoints = wp.ToArray(),
                slotCount = 200,
                visualType = RailRenderer.VISUAL_SPRITE_TILE,
                deployPoints = dp
            };
        }

        #endregion

        #region Difficulty

        private static DifficultyPurpose DeterminePurpose(int packageId, int posInPkg, int levelId)
        {
            if (packageId == 1)
            {
                if (posInPkg <= 3) return DifficultyPurpose.Tutorial;
                if (posInPkg == 19) return DifficultyPurpose.Hard;
                if (posInPkg == 20) return DifficultyPurpose.Rest;
                if (posInPkg == 5 || posInPkg == 10 || posInPkg == 15) return DifficultyPurpose.Rest;
                if (posInPkg == 4 || posInPkg == 9 || posInPkg == 14) return DifficultyPurpose.Hard;
                return DifficultyPurpose.Normal;
            }

            if (posInPkg == 1 || posInPkg == 11) return DifficultyPurpose.Tutorial;
            if (posInPkg == 4 || posInPkg == 14) return DifficultyPurpose.Hard;
            if (posInPkg == 9 || posInPkg == 19) return DifficultyPurpose.SuperHard;
            if (posInPkg == 5 || posInPkg == 10 || posInPkg == 15 || posInPkg == 20) return DifficultyPurpose.Rest;
            return DifficultyPurpose.Normal;
        }

        private static int CalculateQueueColumns(DifficultyPurpose purpose, System.Random rng)
        {
            switch (purpose)
            {
                case DifficultyPurpose.Tutorial: return 2;
                case DifficultyPurpose.Rest:     return rng.Next(2, 4);
                case DifficultyPurpose.Normal:   return rng.Next(2, 5);
                case DifficultyPurpose.Hard:
                case DifficultyPurpose.SuperHard: return rng.Next(3, 6);
                default: return 3;
            }
        }

        private static float CalculateClearRate(int pkgId, DifficultyPurpose purpose, System.Random rng)
        {
            float pkg = Mathf.Clamp01((pkgId - 1f) / 14f);
            float min, max;
            switch (purpose)
            {
                case DifficultyPurpose.Tutorial:   min = 0.88f; max = 0.95f; break;
                case DifficultyPurpose.Rest:       min = 0.60f; max = 0.90f; break;
                case DifficultyPurpose.Normal:     min = Mathf.Lerp(0.60f, 0.40f, pkg); max = Mathf.Lerp(0.80f, 0.55f, pkg); break;
                case DifficultyPurpose.Hard:       min = Mathf.Lerp(0.40f, 0.20f, pkg); max = Mathf.Lerp(0.60f, 0.35f, pkg); break;
                case DifficultyPurpose.SuperHard:  min = Mathf.Lerp(0.30f, 0.12f, pkg); max = Mathf.Lerp(0.45f, 0.20f, pkg); break;
                default:                           min = 0.50f; max = 0.70f; break;
            }
            float rate = min + (float)rng.NextDouble() * (max - min);
            return Mathf.Round(rate * 100f) / 100f;
        }

        #endregion

        #region Gimmick Life

        /// <summary>
        /// Must match BalloonController hit requirements exactly:
        /// Pinata/PinataBox = PinataRequiredHits (2), all others = 1 standard pop.
        /// Pin, Ice, etc. are destroyed in 1 hit (no multi-hit logic in BalloonController).
        /// </summary>
        private static int GetGimmickLife(string gimmickType)
        {
            if (string.IsNullOrEmpty(gimmickType)) return 1;

            switch (gimmickType)
            {
                case GimmickManager.GIMMICK_PINATA:     return 2; // matches BalloonController.PinataRequiredHits
                case GimmickManager.GIMMICK_PINATA_BOX: return 2;
                case GimmickManager.GIMMICK_WALL:       return 0; // indestructible — no dart needed
                case GimmickManager.GIMMICK_PIN:        return 0; // removed by adjacent pop — no dart needed
                case GimmickManager.GIMMICK_ICE:        return 0; // thawed by adjacent pop — no dart targets it directly
                default:                                return 1;
            }
        }

        #endregion
    }
}
#endif
