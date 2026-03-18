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
        [MenuItem("BalloonFlow/Inject Test Level (Fail Condition)")]
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

        [MenuItem("BalloonFlow/Inject Test Level (Continue)")]
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

        [MenuItem("BalloonFlow/Restore Level 1 (Original)")]
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
    }
}
