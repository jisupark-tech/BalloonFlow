using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// Editor tool: Injects a fail-condition test level into LevelDatabase slot 1.
    /// Menu: BalloonFlow > Inject Test Level (Fail Condition)
    ///
    /// Test Level Design:
    ///   railCapacity = 20 (small for quick testing)
    ///   Balloons: 12ea, colors 0 and 1 only (3x4 grid)
    ///   Holders:
    ///     Color 2 x3, magazine 8 each = 24 unmatchable darts (will overflow capacity 20)
    ///     Color 0 x1, magazine 3 = 3 matchable darts (for recovery test)
    ///   Total darts: 27
    ///
    /// Expected behavior:
    ///   - Tap color-2 holders -> rail fills with unmatchable darts
    ///   - At 19 darts (capacity-1) + no outermost match -> 1.5s grace starts
    ///   - If grace expires -> OnBoardFailed (RailOverflow)
    ///   - If you tap color-0 holder first and pop some balloons -> recovery possible
    /// </summary>
    public static class TestLevelInjector
    {
        [MenuItem("BalloonFlow/DON'T USE/Inject Test Level (Fail Condition)")]
        public static void InjectTestLevel()
        {
            var db = Resources.Load<LevelDatabase>("LevelDatabase");
            if (db == null || db.levels == null || db.levels.Length == 0)
            {
                Debug.LogError("[TestLevelInjector] LevelDatabase not found or empty.");
                return;
            }

            // Backup original level 1
            var originalName = db.levels[0].levelId;
            Debug.Log($"[TestLevelInjector] Replacing Level {originalName} with fail-condition test level.");

            db.levels[0] = CreateFailTestLevel();

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Debug.Log("[TestLevelInjector] Test level injected as Level 1. Run the game and test!");
        }

        [MenuItem("BalloonFlow/DON'T USE/Inject Test Level (Continue)")]
        public static void InjectContinueTestLevel()
        {
            var db = Resources.Load<LevelDatabase>("LevelDatabase");
            if (db == null || db.levels == null || db.levels.Length == 0)
            {
                Debug.LogError("[TestLevelInjector] LevelDatabase not found or empty.");
                return;
            }

            // Same as fail test but with more holders to allow multiple fail→continue cycles
            db.levels[0] = CreateContinueTestLevel();

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Debug.Log("[TestLevelInjector] Continue test level injected as Level 1.\n" +
                      "Test steps:\n" +
                      "1) Tap color-2 holders until rail overflows → fail triggers\n" +
                      "2) Continue popup should appear (or use Console: ContinueHandler.Instance.Continue())\n" +
                      "3) Verify: darts removed, holders returned to queue, game resumes\n" +
                      "4) Repeat to test escalating costs (FREE → 900 → 1900 → 2900)");
        }

        [MenuItem("BalloonFlow/DON'T USE/Restore Level 1 (Original)")]
        public static void RestoreOriginal()
        {
            var db = Resources.Load<LevelDatabase>("LevelDatabase");
            if (db == null || db.levels == null || db.levels.Length == 0) return;

            db.levels[0] = CreateOriginalLevel1();

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Debug.Log("[TestLevelInjector] Level 1 restored to original.");
        }

        private static LevelConfig CreateFailTestLevel()
        {
            var cfg = new LevelConfig
            {
                levelId = 1,
                packageId = 1,
                positionInPackage = 1,
                railCapacity = 20,      // small capacity for quick fail test
                numColors = 3,
                balloonCount = 12,
                balloonScale = 1.0f,
                queueColumns = 3,
                targetClearRate = 0.5f,
                difficultyPurpose = DifficultyPurpose.Normal,
                gimmickTypes = new string[0],
                gridCols = 4,
                gridRows = 3,
                star1Threshold = 1200,
                star2Threshold = 1800,
                star3Threshold = 2640,
            };

            // ── Balloons: 3x4 grid, colors 0 and 1 only ──
            // Color 0 = top two rows (8 balloons)
            // Color 1 = bottom row (4 balloons)
            // Color 2 = NOT on field (unmatchable darts)
            cfg.balloons = new BalloonLayout[12];
            int bid = 0;
            float spacing = 1.6f;
            float baseX = -2.4f;
            float baseY = 0.4f;

            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    int color = (row < 2) ? 0 : 1;
                    cfg.balloons[bid] = new BalloonLayout
                    {
                        balloonId = bid,
                        color = color,
                        gridPosition = new Vector2(baseX + col * spacing, baseY + row * spacing),
                        gimmickType = ""
                    };
                    bid++;
                }
            }

            // ── Holders ──
            // 3x color 2 (unmatchable) + 1x color 0 (matchable for recovery)
            cfg.holders = new HolderSetup[]
            {
                // Unmatchable holders - these will overflow the rail
                new HolderSetup { holderId = 0, color = 2, magazineCount = 8, position = new Vector2(-1.6f, 0) },
                new HolderSetup { holderId = 1, color = 2, magazineCount = 8, position = new Vector2(0, 0) },
                new HolderSetup { holderId = 2, color = 2, magazineCount = 8, position = new Vector2(1.6f, 0) },
                // Matchable holder - use this to test recovery
                new HolderSetup { holderId = 3, color = 0, magazineCount = 3, position = new Vector2(3.2f, 0) },
            };

            // ── Rail: rectangular loop around the balloon field ──
            cfg.rail = new RailLayout
            {
                waypoints = new Vector3[]
                {
                    new Vector3(-4.7f, 0.5f, -2.7f),
                    new Vector3(4.7f, 0.5f, -2.7f),
                    new Vector3(4.7f, 0.5f, 6.7f),
                    new Vector3(-4.7f, 0.5f, 6.7f),
                },
                slotCount = 0,  // will use railCapacity = 20
                visualType = 3,
                deployPoints = new Vector3[]
                {
                    new Vector3(-1.6f, 0.5f, -2.7f),
                    new Vector3(0f, 0.5f, -2.7f),
                    new Vector3(1.6f, 0.5f, -2.7f),
                },
                smoothCorners = false,
                cornerRadius = 1f,
            };

            cfg.conveyorPositions = new Vector2Int[0];
            return cfg;
        }

        /// <summary>
        /// Recreates the original Level 1 (approximate — adjust if needed).
        /// </summary>
        private static LevelConfig CreateOriginalLevel1()
        {
            var cfg = new LevelConfig
            {
                levelId = 1,
                packageId = 1,
                positionInPackage = 1,
                railCapacity = 200,
                numColors = 4,
                balloonCount = 12,
                balloonScale = 1.44f,
                queueColumns = 0,
                targetClearRate = 0,
                difficultyPurpose = DifficultyPurpose.Normal,
                gimmickTypes = new string[0],
                gridCols = 5,
                gridRows = 5,
                star1Threshold = 1200,
                star2Threshold = 1800,
                star3Threshold = 2640,
            };

            // Single holder
            cfg.holders = new HolderSetup[]
            {
                new HolderSetup { holderId = 0, color = 0, magazineCount = 3, position = Vector2.zero },
            };

            // 12 balloons all color 0
            cfg.balloons = new BalloonLayout[12];
            float[] xs = { -1.6f, 0f, 1.6f };
            float[] ys = { 0.4f, 2f, 3.6f, 5.2f };
            int id = 0;
            for (int xi = 0; xi < xs.Length; xi++)
            {
                for (int yi = 0; yi < ys.Length; yi++)
                {
                    cfg.balloons[id] = new BalloonLayout
                    {
                        balloonId = id,
                        color = 0,
                        gridPosition = new Vector2(xs[xi], ys[yi]),
                        gimmickType = ""
                    };
                    id++;
                }
            }

            cfg.rail = new RailLayout
            {
                waypoints = new Vector3[]
                {
                    new Vector3(-4.7f, 0.5f, -2.7f),
                    new Vector3(-1.598f, 0.5f, -2.7f),
                    new Vector3(1.598f, 0.5f, -2.7f),
                    new Vector3(4.7f, 0.5f, -2.7f),
                    new Vector3(4.7f, 0.5f, 0.402f),
                    new Vector3(4.7f, 0.5f, 3.598f),
                    new Vector3(4.7f, 0.5f, 6.7f),
                    new Vector3(1.598f, 0.5f, 6.7f),
                    new Vector3(-1.598f, 0.5f, 6.7f),
                    new Vector3(-4.7f, 0.5f, 6.7f),
                    new Vector3(-4.7f, 0.5f, 3.598f),
                    new Vector3(-4.7f, 0.5f, 0.402f),
                },
                slotCount = 200,
                visualType = 3,
                deployPoints = new Vector3[]
                {
                    new Vector3(-3.133f, 0.5f, -2.7f),
                    new Vector3(-1.567f, 0.5f, -2.7f),
                    new Vector3(0f, 0.5f, -2.7f),
                    new Vector3(1.567f, 0.5f, -2.7f),
                    new Vector3(3.133f, 0.5f, -2.7f),
                },
                smoothCorners = false,
                cornerRadius = 1f,
            };

            cfg.conveyorPositions = new Vector2Int[0];
            return cfg;
        }

        /// <summary>
        /// Test level for continue system: small capacity, many unmatchable darts,
        /// enough to trigger fail multiple times for testing 4 continue cycles.
        /// </summary>
        private static LevelConfig CreateContinueTestLevel()
        {
            var cfg = new LevelConfig
            {
                levelId = 1,
                packageId = 1,
                positionInPackage = 1,
                railCapacity = 20,      // small for quick fail
                numColors = 3,
                balloonCount = 12,
                balloonScale = 1.0f,
                queueColumns = 3,
                targetClearRate = 0.5f,
                difficultyPurpose = DifficultyPurpose.Normal,
                gimmickTypes = new string[0],
                gridCols = 4,
                gridRows = 3,
                star1Threshold = 1200,
                star2Threshold = 1800,
                star3Threshold = 2640,
            };

            // Balloons: 3x4 grid, colors 0 and 1
            cfg.balloons = new BalloonLayout[12];
            int bid = 0;
            float spacing = 1.6f;
            float baseX = -2.4f;
            float baseY = 0.4f;
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    cfg.balloons[bid] = new BalloonLayout
                    {
                        balloonId = bid,
                        color = (row < 2) ? 0 : 1,
                        gridPosition = new Vector2(baseX + col * spacing, baseY + row * spacing),
                        gimmickType = ""
                    };
                    bid++;
                }
            }

            // Holders: many unmatchable (color 2) to trigger fail repeatedly + matchable (color 0)
            cfg.holders = new HolderSetup[]
            {
                // Unmatchable — each triggers ~8 darts on rail
                new HolderSetup { holderId = 0, color = 2, magazineCount = 8, position = new Vector2(-1.6f, 0) },
                new HolderSetup { holderId = 1, color = 2, magazineCount = 8, position = new Vector2(0, 0) },
                new HolderSetup { holderId = 2, color = 2, magazineCount = 8, position = new Vector2(1.6f, 0) },
                new HolderSetup { holderId = 3, color = 2, magazineCount = 8, position = new Vector2(-1.6f, -1.6f) },
                new HolderSetup { holderId = 4, color = 2, magazineCount = 8, position = new Vector2(0, -1.6f) },
                // Matchable — for clearing after continue
                new HolderSetup { holderId = 5, color = 0, magazineCount = 5, position = new Vector2(1.6f, -1.6f) },
                new HolderSetup { holderId = 6, color = 1, magazineCount = 3, position = new Vector2(3.2f, 0) },
            };

            cfg.rail = new RailLayout
            {
                waypoints = new Vector3[]
                {
                    new Vector3(-4.7f, 0.5f, -2.7f),
                    new Vector3(4.7f, 0.5f, -2.7f),
                    new Vector3(4.7f, 0.5f, 6.7f),
                    new Vector3(-4.7f, 0.5f, 6.7f),
                },
                slotCount = 0,
                visualType = 3,
                deployPoints = new Vector3[]
                {
                    new Vector3(-1.6f, 0.5f, -2.7f),
                    new Vector3(0f, 0.5f, -2.7f),
                    new Vector3(1.6f, 0.5f, -2.7f),
                },
                smoothCorners = false,
                cornerRadius = 1f,
            };

            cfg.conveyorPositions = new Vector2Int[0];
            return cfg;
        }

        // ══════════════════════════════════════════════════════════
        //  JSON FieldMap → LevelDatabase Level 1 Injector
        // ══════════════════════════════════════════════════════════

        [MenuItem("BalloonFlow/DON'T USE/Inject JSON Test Level (Level 1)")]
        public static void InjectJsonTestLevel()
        {
            string jsonPath = EditorUtility.OpenFilePanel(
                "Select JSON Level File", @"E:\BalloonFlow\BalloonFlow_Level", "json");
            if (string.IsNullOrEmpty(jsonPath)) return;

            try
            {
                var config = BuildLevelFromJson(jsonPath);
                InjectAsLevel1(config, Path.GetFileName(jsonPath));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TestLevelInjector] Failed to inject JSON level: {ex}");
                EditorUtility.DisplayDialog("Error", ex.Message, "OK");
            }
        }

        /// <summary>Quick inject for the known test file without file picker.</summary>
        [MenuItem("BalloonFlow/DON'T USE/Inject TEST_LEVEL_15 as Level 1")]
        public static void InjectTestLevel15()
        {
            const string path = @"E:\BalloonFlow\BalloonFlow_Level\TEST_LEVEL_15.json";
            if (!File.Exists(path))
            {
                Debug.LogError($"[TestLevelInjector] File not found: {path}");
                return;
            }

            try
            {
                var config = BuildLevelFromJson(path);
                InjectAsLevel1(config, "TEST_LEVEL_15.json");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TestLevelInjector] Failed: {ex}");
            }
        }

        private static void InjectAsLevel1(LevelConfig config, string fileName)
        {
            string dbPath = "Assets/Resources/LevelDatabase.asset";
            var db = Resources.Load<LevelDatabase>("LevelDatabase");

            if (db == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                    AssetDatabase.CreateFolder("Assets", "Resources");
                db = ScriptableObject.CreateInstance<LevelDatabase>();
                db.levels = new LevelConfig[] { config };
                AssetDatabase.CreateAsset(db, dbPath);
            }
            else
            {
                // Prepend as level 1 (index 0), keep existing levels shifted
                var list = new List<LevelConfig>();
                list.Add(config);
                if (db.levels != null)
                {
                    foreach (var lv in db.levels)
                    {
                        if (lv != null && lv.levelId != config.levelId)
                            list.Add(lv);
                    }
                }
                db.levels = list.ToArray();
                EditorUtility.SetDirty(db);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[TestLevelInjector] '{fileName}' injected as Level 1.\n" +
                      $"  Grid: {config.gridCols}x{config.gridRows}, " +
                      $"Balloons: {config.balloonCount}, Colors: {config.numColors}, " +
                      $"Holders: {config.holders?.Length ?? 0}, Rail: {config.railCapacity}");
            EditorUtility.DisplayDialog("Injected",
                $"{fileName} → Level 1\n" +
                $"{config.gridCols}x{config.gridRows}, {config.balloonCount} balloons, " +
                $"{config.numColors} colors, {config.holders?.Length ?? 0} holders",
                "OK");
        }

        private static LevelConfig BuildLevelFromJson(string jsonPath)
        {
            string json = File.ReadAllText(jsonPath);

            // ── Parse JSON fields (minimal parser — no external dependency) ──
            int levelNumber   = JsonInt(json, "level_number");
            int pkg           = JsonInt(json, "pkg");
            int pos           = JsonInt(json, "pos");
            int numColors     = JsonInt(json, "num_colors");
            int fieldRows     = JsonInt(json, "field_rows");
            int fieldCols     = JsonInt(json, "field_columns");
            int railCapacity  = JsonInt(json, "rail_capacity");
            int queueColumns  = Mathf.Max(JsonInt(json, "queue_columns"), 2);
            int totalDarts    = JsonInt(json, "total_darts");
            string purposeStr = JsonString(json, "purpose_type");
            string note       = JsonString(json, "designer_note");
            string dartRange  = JsonString(json, "dart_capacity_range");

            // ── Parse FieldMap from designer_note ──
            // Format: [FieldMap]\n rows of space-separated color IDs
            int fmIdx = note.IndexOf("[FieldMap]");
            if (fmIdx < 0)
                throw new Exception("designer_note does not contain [FieldMap]");

            string fieldMapStr = note.Substring(fmIdx + "[FieldMap]".Length).Trim();
            string[] mapLines = fieldMapStr.Split(new[] { '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries);

            // Parse grid: each line is space-separated color IDs
            int gridRows = mapLines.Length;
            int[][] grid = new int[gridRows][];
            int gridCols = 0;

            for (int r = 0; r < gridRows; r++)
            {
                string[] tokens = mapLines[r].Trim().Split(new[] { ' ', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);
                grid[r] = new int[tokens.Length];
                for (int c = 0; c < tokens.Length; c++)
                    int.TryParse(tokens[c], out grid[r][c]);
                gridCols = Mathf.Max(gridCols, tokens.Length);
            }

            // ── Collect unique color IDs and map to 0-based indices ──
            var colorIdSet = new SortedSet<int>();
            for (int r = 0; r < gridRows; r++)
                for (int c = 0; c < grid[r].Length; c++)
                    colorIdSet.Add(grid[r][c]);

            var colorIdList = colorIdSet.ToList();
            var colorIdToIndex = new Dictionary<int, int>();
            for (int i = 0; i < colorIdList.Count; i++)
                colorIdToIndex[colorIdList[i]] = i;

            int actualColors = colorIdList.Count;

            // ── Build balloons from FieldMap ──
            // World positioning: center grid in play area
            const float BOARD_SIZE = 8f;
            const float CENTER_X = 0f;
            const float CENTER_Z = 2f;
            int maxDim = Mathf.Max(gridCols, gridRows);
            float cellSpacing = BOARD_SIZE / maxDim;
            float halfW = (gridCols - 1) * cellSpacing * 0.5f;
            float halfH = (gridRows - 1) * cellSpacing * 0.5f;

            var balloons = new List<BalloonLayout>();
            int bid = 0;

            for (int r = 0; r < gridRows; r++)
            {
                for (int c = 0; c < grid[r].Length; c++)
                {
                    int colorId = grid[r][c];
                    int colorIdx = colorIdToIndex[colorId];

                    float wx = CENTER_X - halfW + c * cellSpacing;
                    float wz = CENTER_Z - halfH + r * cellSpacing;

                    balloons.Add(new BalloonLayout
                    {
                        balloonId = bid++,
                        color = colorIdx,
                        gridPosition = new Vector2(wx, wz),
                        gimmickType = ""
                    });
                }
            }

            // ── Count darts per color ──
            int[] dartsPerColor = new int[actualColors];
            foreach (var b in balloons)
                dartsPerColor[b.color]++;

            // ── Build holders ──
            int[] allowedMags = ParseMagSizes(dartRange);
            int maxHolders = queueColumns * Mathf.Max(JsonInt(json, "queue_rows"), 1);
            if (maxHolders < actualColors) maxHolders = actualColors;
            // If queue_rows=0, use a reasonable default based on balloon count
            if (maxHolders < 4) maxHolders = Mathf.Max(actualColors, totalDarts / 50);

            var holders = BuildHoldersStatic(dartsPerColor, allowedMags, queueColumns, maxHolders);

            // ── Rail ──
            const float RAIL_PAD = 1.5f;
            float halfBoard = BOARD_SIZE * 0.5f;
            float left   = CENTER_X - halfBoard - RAIL_PAD;
            float right  = CENTER_X + halfBoard + RAIL_PAD;
            float bottom = CENTER_Z - halfBoard - RAIL_PAD;
            float top    = CENTER_Z + halfBoard + RAIL_PAD;

            var wp = new Vector3[]
            {
                new Vector3(left, 0.5f, bottom),
                new Vector3(Mathf.Lerp(left, right, 0.33f), 0.5f, bottom),
                new Vector3(Mathf.Lerp(left, right, 0.67f), 0.5f, bottom),
                new Vector3(right, 0.5f, bottom),
                new Vector3(right, 0.5f, Mathf.Lerp(bottom, top, 0.33f)),
                new Vector3(right, 0.5f, Mathf.Lerp(bottom, top, 0.67f)),
                new Vector3(right, 0.5f, top),
                new Vector3(Mathf.Lerp(right, left, 0.33f), 0.5f, top),
                new Vector3(Mathf.Lerp(right, left, 0.67f), 0.5f, top),
                new Vector3(left, 0.5f, top),
                new Vector3(left, 0.5f, Mathf.Lerp(top, bottom, 0.33f)),
                new Vector3(left, 0.5f, Mathf.Lerp(top, bottom, 0.67f)),
            };

            var dp = new Vector3[queueColumns];
            for (int i = 0; i < queueColumns; i++)
            {
                float t = (i + 1f) / (queueColumns + 1f);
                dp[i] = new Vector3(Mathf.Lerp(left, right, t), 0.5f, bottom - 1f);
            }

            // ── Purpose mapping ──
            DifficultyPurpose purpose = DifficultyPurpose.Normal;
            if (purposeStr.Contains("튜토리얼") || purposeStr.Contains("Tutorial")) purpose = DifficultyPurpose.Tutorial;
            else if (purposeStr.Contains("슈퍼하드") || purposeStr.Contains("SuperHard")) purpose = DifficultyPurpose.SuperHard;
            else if (purposeStr.Contains("하드") || purposeStr.Contains("Hard")) purpose = DifficultyPurpose.Hard;
            else if (purposeStr.Contains("휴식") || purposeStr.Contains("Rest")) purpose = DifficultyPurpose.Rest;

            // ── Assemble config ──
            var cfg = new LevelConfig
            {
                levelId = 1, // inject as level 1
                packageId = pkg,
                positionInPackage = 1,
                railCapacity = railCapacity > 0 ? railCapacity : 200,
                numColors = actualColors,
                balloonCount = balloons.Count,
                balloonScale = cellSpacing * 0.85f,
                queueColumns = queueColumns,
                targetClearRate = 0.5f,
                difficultyPurpose = purpose,
                gimmickTypes = new string[0],
                gridCols = gridCols,
                gridRows = gridRows,
                star1Threshold = balloons.Count * 100,
                star2Threshold = balloons.Count * 150,
                star3Threshold = balloons.Count * 220,
                balloons = balloons.ToArray(),
                holders = holders,
                rail = new RailLayout
                {
                    waypoints = wp,
                    slotCount = railCapacity > 0 ? railCapacity : 200,
                    visualType = 3,
                    deployPoints = dp,
                    smoothCorners = true,
                    cornerRadius = 1f
                },
                conveyorPositions = new Vector2Int[0]
            };

            return cfg;
        }

        // ── Static holder builder (same logic as LevelDesignImporterWindow) ──
        private static HolderSetup[] BuildHoldersStatic(int[] dartsPerColor,
            int[] allowedMags, int queueColumns, int maxHolders)
        {
            var holders = new List<HolderSetup>();
            int hid = 0;

            var mags = allowedMags.OrderByDescending(m => m).ToArray();
            if (mags.Length == 0) mags = new[] { 5 };

            int colorsWithDarts = dartsPerColor.Count(d => d > 0);
            if (maxHolders < colorsWithDarts) maxHolders = colorsWithDarts;

            int totalDarts = dartsPerColor.Sum();
            int[] slotBudget = new int[dartsPerColor.Length];
            int assignedSlots = 0;
            for (int c = 0; c < dartsPerColor.Length; c++)
            {
                if (dartsPerColor[c] <= 0) continue;
                slotBudget[c] = Mathf.Max(1,
                    Mathf.RoundToInt((float)dartsPerColor[c] / totalDarts * maxHolders));
                assignedSlots += slotBudget[c];
            }
            while (assignedSlots > maxHolders)
            {
                int maxIdx = 0;
                for (int c = 1; c < slotBudget.Length; c++)
                    if (slotBudget[c] > slotBudget[maxIdx]) maxIdx = c;
                slotBudget[maxIdx]--;
                assignedSlots--;
            }

            for (int color = 0; color < dartsPerColor.Length; color++)
            {
                int remaining = dartsPerColor[color];
                if (remaining <= 0) continue;
                int budget = slotBudget[color];

                if (budget <= 1)
                {
                    holders.Add(new HolderSetup
                    {
                        holderId = hid++,
                        color = color,
                        magazineCount = remaining,
                        position = Vector2.zero
                    });
                    continue;
                }

                int holdersUsed = 0;
                while (remaining > 0 && holdersUsed < budget)
                {
                    int holdersLeft = budget - holdersUsed;
                    if (holdersLeft == 1)
                    {
                        holders.Add(new HolderSetup
                        {
                            holderId = hid++, color = color,
                            magazineCount = remaining, position = Vector2.zero
                        });
                        remaining = 0;
                        holdersUsed++;
                        break;
                    }

                    int bestMag = mags[mags.Length - 1];
                    int minSmallest = mags[mags.Length - 1];
                    foreach (int mag in mags)
                    {
                        if (mag <= remaining &&
                            (remaining - mag) >= (holdersLeft - 1) * minSmallest)
                        {
                            bestMag = mag;
                            break;
                        }
                    }
                    if (bestMag > remaining) bestMag = remaining;

                    holders.Add(new HolderSetup
                    {
                        holderId = hid++, color = color,
                        magazineCount = bestMag, position = Vector2.zero
                    });
                    remaining -= bestMag;
                    holdersUsed++;
                }

                if (remaining > 0 && holders.Count > 0)
                {
                    var last = holders[holders.Count - 1];
                    last.magazineCount += remaining;
                    holders[holders.Count - 1] = last;
                }
            }

            // Shuffle
            var rng = new System.Random(42);
            for (int i = holders.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var tmp = holders[i]; holders[i] = holders[j]; holders[j] = tmp;
            }

            // Assign positions
            for (int i = 0; i < holders.Count; i++)
            {
                int col = i % Mathf.Max(queueColumns, 1);
                int row = i / Mathf.Max(queueColumns, 1);
                holders[i] = new HolderSetup
                {
                    holderId = i,
                    color = holders[i].color,
                    magazineCount = holders[i].magazineCount,
                    position = new Vector2(col, row)
                };
            }

            return holders.ToArray();
        }

        // ── Minimal JSON helpers (no external dependency) ──
        private static int JsonInt(string json, string key)
        {
            var m = Regex.Match(json, $"\"{key}\"\\s*:\\s*(-?\\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }

        private static string JsonString(string json, string key)
        {
            // Match "key": "value" — handle escaped chars
            var m = Regex.Match(json, $"\"{key}\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success) return "";
            string raw = m.Groups[1].Value;
            // Unescape common JSON escapes
            raw = raw.Replace("\\r", "\r").Replace("\\n", "\n")
                      .Replace("\\t", "\t").Replace("\\\"", "\"")
                      .Replace("\\\\", "\\");
            // Unescape unicode: \uXXXX
            raw = Regex.Replace(raw, @"\\u([0-9A-Fa-f]{4})",
                match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());
            return raw;
        }

        private static int[] ParseMagSizes(string dartRange)
        {
            if (string.IsNullOrEmpty(dartRange)) return new[] { 5, 10, 20, 30, 40, 50 };
            var sizes = new List<int>();
            foreach (var s in dartRange.Split(','))
            {
                if (int.TryParse(s.Trim(), out int v) && v > 0)
                    sizes.Add(v);
            }
            return sizes.Count > 0 ? sizes.ToArray() : new[] { 5, 10, 20, 30, 40, 50 };
        }
    }
}
