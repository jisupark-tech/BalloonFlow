using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Editor tool that generates a LevelDatabase ScriptableObject with 30 pre-authored levels
    /// following the beat_chart.yaml design specifications.
    /// Menu: BalloonFlow > Generate Level Database (30 Levels)
    /// </summary>
    public static class LevelDatabaseGenerator
    {
        #region Constants

        private const float CellSpacing = 0.55f;
        private const float BoardCenterX = 0f;
        private const float BoardCenterZ = 2f;
        private const float RailPadding = 1.5f;
        private const float MagazineSurplusRatio = 1.0f;
        private const int HolderQueueSlotCount = 5;

        #endregion

        #region Level Definitions

        /// <summary>
        /// Per-level design parameters from beat_chart.yaml.
        /// </summary>
        private struct LevelDef
        {
            public int levelId;
            public int packageId;
            public int positionInPkg;
            public DifficultyPurpose purpose;
            public int numColors;
            public int gridCols;
            public int gridRows;
            public int holderCount;
            public string gimmick;     // "", "hidden", "spawner_transparent"
            public float gimmickRatio; // fraction of balloons with gimmick
        }

        private static LevelDef[] GetLevelDefinitions()
        {
            return new LevelDef[]
            {
                // ═══ PACKAGE 1: LEARN (Levels 1-20) ═══
                // Pos 1-2: Tutorial (2 colors, very small)
                new LevelDef { levelId=1,  packageId=1, positionInPkg=1,  purpose=DifficultyPurpose.Tutorial, numColors=2, gridCols=3, gridRows=3,  holderCount=6,  gimmick="",       gimmickRatio=0f },
                new LevelDef { levelId=2,  packageId=1, positionInPkg=2,  purpose=DifficultyPurpose.Tutorial, numColors=2, gridCols=3, gridRows=3,  holderCount=7,  gimmick="",       gimmickRatio=0f },
                // Pos 3: Intro 3色 (reduced objects ~20-30%)
                new LevelDef { levelId=3,  packageId=1, positionInPkg=3,  purpose=DifficultyPurpose.Intro,    numColors=3, gridCols=4, gridRows=3,  holderCount=8,  gimmick="",       gimmickRatio=0f },
                // Pos 4: Hard
                new LevelDef { levelId=4,  packageId=1, positionInPkg=4,  purpose=DifficultyPurpose.Hard,     numColors=3, gridCols=5, gridRows=4,  holderCount=12, gimmick="",       gimmickRatio=0f },
                // Pos 5: Rest
                new LevelDef { levelId=5,  packageId=1, positionInPkg=5,  purpose=DifficultyPurpose.Rest,     numColors=2, gridCols=3, gridRows=3,  holderCount=6,  gimmick="",       gimmickRatio=0f },
                // Pos 6: Intro 4色
                new LevelDef { levelId=6,  packageId=1, positionInPkg=6,  purpose=DifficultyPurpose.Intro,    numColors=4, gridCols=4, gridRows=4,  holderCount=10, gimmick="",       gimmickRatio=0f },
                // Pos 7-8: Normal
                new LevelDef { levelId=7,  packageId=1, positionInPkg=7,  purpose=DifficultyPurpose.Normal,   numColors=4, gridCols=5, gridRows=4,  holderCount=12, gimmick="",       gimmickRatio=0f },
                // Pos 8: Intro 5色
                new LevelDef { levelId=8,  packageId=1, positionInPkg=8,  purpose=DifficultyPurpose.Intro,    numColors=5, gridCols=4, gridRows=4,  holderCount=10, gimmick="",       gimmickRatio=0f },
                // Pos 9: Hard
                new LevelDef { levelId=9,  packageId=1, positionInPkg=9,  purpose=DifficultyPurpose.Hard,     numColors=5, gridCols=5, gridRows=5,  holderCount=14, gimmick="",       gimmickRatio=0f },
                // Pos 10: Rest
                new LevelDef { levelId=10, packageId=1, positionInPkg=10, purpose=DifficultyPurpose.Rest,     numColors=3, gridCols=3, gridRows=3,  holderCount=6,  gimmick="",       gimmickRatio=0f },
                // Pos 11: Tutorial — Hidden gimmick intro (single gimmick only)
                new LevelDef { levelId=11, packageId=1, positionInPkg=11, purpose=DifficultyPurpose.Tutorial, numColors=4, gridCols=4, gridRows=4,  holderCount=10, gimmick="hidden", gimmickRatio=0.3f },
                // Pos 12: Normal
                new LevelDef { levelId=12, packageId=1, positionInPkg=12, purpose=DifficultyPurpose.Normal,   numColors=4, gridCols=5, gridRows=4,  holderCount=12, gimmick="",       gimmickRatio=0f },
                // Pos 13: Normal + Hidden
                new LevelDef { levelId=13, packageId=1, positionInPkg=13, purpose=DifficultyPurpose.Normal,   numColors=4, gridCols=5, gridRows=4,  holderCount=12, gimmick="hidden", gimmickRatio=0.25f },
                // Pos 14: Normal
                new LevelDef { levelId=14, packageId=1, positionInPkg=14, purpose=DifficultyPurpose.Normal,   numColors=5, gridCols=5, gridRows=5,  holderCount=14, gimmick="",       gimmickRatio=0f },
                // Pos 15: Rest
                new LevelDef { levelId=15, packageId=1, positionInPkg=15, purpose=DifficultyPurpose.Rest,     numColors=3, gridCols=4, gridRows=3,  holderCount=8,  gimmick="",       gimmickRatio=0f },
                // Pos 16: Normal + Hidden
                new LevelDef { levelId=16, packageId=1, positionInPkg=16, purpose=DifficultyPurpose.Normal,   numColors=5, gridCols=5, gridRows=5,  holderCount=14, gimmick="hidden", gimmickRatio=0.3f },
                // Pos 17: Normal
                new LevelDef { levelId=17, packageId=1, positionInPkg=17, purpose=DifficultyPurpose.Normal,   numColors=5, gridCols=6, gridRows=5,  holderCount=16, gimmick="",       gimmickRatio=0f },
                // Pos 18: Normal + Hidden
                new LevelDef { levelId=18, packageId=1, positionInPkg=18, purpose=DifficultyPurpose.Normal,   numColors=5, gridCols=6, gridRows=5,  holderCount=16, gimmick="hidden", gimmickRatio=0.35f },
                // Pos 19: Hard + Hidden (first expected failure)
                new LevelDef { levelId=19, packageId=1, positionInPkg=19, purpose=DifficultyPurpose.Hard,     numColors=5, gridCols=6, gridRows=6,  holderCount=18, gimmick="hidden", gimmickRatio=0.4f },
                // Pos 20: Rest (PKG1 close → smooth transition)
                new LevelDef { levelId=20, packageId=1, positionInPkg=20, purpose=DifficultyPurpose.Rest,     numColors=3, gridCols=4, gridRows=3,  holderCount=8,  gimmick="",       gimmickRatio=0f },

                // ═══ PACKAGE 2: GROW (Levels 21-30) — first 10 of PKG2 ═══
                // Pos 1: Tutorial — Spawner_transparent intro
                new LevelDef { levelId=21, packageId=2, positionInPkg=1,  purpose=DifficultyPurpose.Tutorial, numColors=4, gridCols=5, gridRows=4,  holderCount=12, gimmick="spawner_transparent", gimmickRatio=0.15f },
                // Pos 2: Normal
                new LevelDef { levelId=22, packageId=2, positionInPkg=2,  purpose=DifficultyPurpose.Normal,   numColors=5, gridCols=5, gridRows=5,  holderCount=14, gimmick="",                    gimmickRatio=0f },
                // Pos 3: Normal + Spawner
                new LevelDef { levelId=23, packageId=2, positionInPkg=3,  purpose=DifficultyPurpose.Normal,   numColors=5, gridCols=6, gridRows=5,  holderCount=16, gimmick="spawner_transparent", gimmickRatio=0.1f },
                // Pos 4: Hard + Hidden
                new LevelDef { levelId=24, packageId=2, positionInPkg=4,  purpose=DifficultyPurpose.Hard,     numColors=5, gridCols=6, gridRows=6,  holderCount=18, gimmick="hidden",              gimmickRatio=0.35f },
                // Pos 5: Rest
                new LevelDef { levelId=25, packageId=2, positionInPkg=5,  purpose=DifficultyPurpose.Rest,     numColors=4, gridCols=4, gridRows=4,  holderCount=10, gimmick="",                    gimmickRatio=0f },
                // Pos 6: Intro 6色
                new LevelDef { levelId=26, packageId=2, positionInPkg=6,  purpose=DifficultyPurpose.Intro,    numColors=6, gridCols=5, gridRows=5,  holderCount=14, gimmick="",                    gimmickRatio=0f },
                // Pos 7: Normal + Hidden
                new LevelDef { levelId=27, packageId=2, positionInPkg=7,  purpose=DifficultyPurpose.Normal,   numColors=5, gridCols=6, gridRows=5,  holderCount=16, gimmick="hidden",              gimmickRatio=0.3f },
                // Pos 8: Normal + Spawner
                new LevelDef { levelId=28, packageId=2, positionInPkg=8,  purpose=DifficultyPurpose.Normal,   numColors=6, gridCols=6, gridRows=6,  holderCount=18, gimmick="spawner_transparent", gimmickRatio=0.12f },
                // Pos 9: Normal + Hidden
                new LevelDef { levelId=29, packageId=2, positionInPkg=9,  purpose=DifficultyPurpose.Normal,   numColors=5, gridCols=7, gridRows=5,  holderCount=18, gimmick="hidden",              gimmickRatio=0.35f },
                // Pos 10: Normal
                new LevelDef { levelId=30, packageId=2, positionInPkg=10, purpose=DifficultyPurpose.Normal,   numColors=6, gridCols=6, gridRows=6,  holderCount=20, gimmick="hidden",              gimmickRatio=0.3f },
            };
        }

        #endregion

        #region Menu Item

        // MenuItem 삭제됨 — Level Editor만 BalloonFlow 탭에 표시
        public static void GenerateLevelDatabase()
        {
            LevelDef[] defs = GetLevelDefinitions();
            LevelConfig[] configs = new LevelConfig[defs.Length];

            for (int i = 0; i < defs.Length; i++)
            {
                configs[i] = BuildLevelConfig(defs[i]);
            }

            // Create or update ScriptableObject
            string assetPath = "Assets/Resources/LevelDatabase.asset";
            LevelDatabase db = AssetDatabase.LoadAssetAtPath<LevelDatabase>(assetPath);

            if (db == null)
            {
                // Ensure directory exists
                if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                {
                    AssetDatabase.CreateFolder("Assets", "Resources");
                }

                db = ScriptableObject.CreateInstance<LevelDatabase>();
                db.levels = configs;
                AssetDatabase.CreateAsset(db, assetPath);
            }
            else
            {
                db.levels = configs;
                EditorUtility.SetDirty(db);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[LevelDatabaseGenerator] Generated {configs.Length} levels → {assetPath}");
            EditorUtility.DisplayDialog("Level Database Generated",
                $"{configs.Length} levels created at:\n{assetPath}", "OK");
        }

        #endregion

        #region Level Building

        private static LevelConfig BuildLevelConfig(LevelDef def)
        {
            // Use levelId as seed for consistent balloon color distribution
            System.Random rng = new System.Random(def.levelId * 31 + 7);

            BalloonLayout[] balloons = GenerateBalloonGrid(def, rng);
            int balloonCount = balloons.Length;
            HolderSetup[] holders = GenerateHolders(def, balloons, rng);
            RailLayout rail = GenerateRail(def.gridCols, def.gridRows);

            int star1 = balloonCount * 100;

            return new LevelConfig
            {
                levelId = def.levelId,
                packageId = def.packageId,
                positionInPackage = def.positionInPkg,
                numColors = def.numColors,
                balloonCount = balloonCount,
                difficultyPurpose = def.purpose,
                gimmickTypes = string.IsNullOrEmpty(def.gimmick) ? new string[0] : new string[] { def.gimmick },
                holders = holders,
                balloons = balloons,
                rail = rail,
                star1Threshold = star1,
                star2Threshold = Mathf.CeilToInt(star1 * 1.5f),
                star3Threshold = Mathf.CeilToInt(star1 * 2.2f)
            };
        }

        #endregion

        #region Balloon Grid Generation

        private static BalloonLayout[] GenerateBalloonGrid(LevelDef def, System.Random rng)
        {
            int cols = def.gridCols;
            int rows = def.gridRows;
            int total = cols * rows;

            // Build color pool with roughly even distribution
            int[] colorPool = BuildColorPool(total, def.numColors, rng);

            // Determine which balloons get gimmicks
            bool[] hasGimmick = new bool[total];
            if (!string.IsNullOrEmpty(def.gimmick) && def.gimmickRatio > 0f)
            {
                int gimmickCount = Mathf.Max(1, Mathf.RoundToInt(total * def.gimmickRatio));
                // Shuffle indices and pick first N
                int[] indices = new int[total];
                for (int i = 0; i < total; i++) indices[i] = i;
                Shuffle(indices, rng);
                for (int i = 0; i < gimmickCount && i < total; i++)
                {
                    hasGimmick[indices[i]] = true;
                }
            }

            float halfColWorld = (cols - 1) * CellSpacing * 0.5f;
            float halfRowWorld = (rows - 1) * CellSpacing * 0.5f;

            BalloonLayout[] balloons = new BalloonLayout[total];
            int id = 0;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float worldX = BoardCenterX + (c * CellSpacing) - halfColWorld;
                    float worldZ = BoardCenterZ + (r * CellSpacing) - halfRowWorld;

                    balloons[id] = new BalloonLayout
                    {
                        balloonId = id,
                        color = colorPool[id],
                        gridPosition = new Vector2(worldX, worldZ),
                        gimmickType = hasGimmick[id] ? def.gimmick : ""
                    };
                    id++;
                }
            }

            return balloons;
        }

        private static int[] BuildColorPool(int total, int numColors, System.Random rng)
        {
            int[] pool = new int[total];
            int perColor = total / numColors;
            int remainder = total % numColors;
            int idx = 0;

            for (int c = 0; c < numColors; c++)
            {
                int count = perColor + (c < remainder ? 1 : 0);
                for (int i = 0; i < count; i++)
                {
                    pool[idx++] = c;
                }
            }

            Shuffle(pool, rng);
            return pool;
        }

        #endregion

        #region Holder Generation

        private static HolderSetup[] GenerateHolders(LevelDef def, BalloonLayout[] balloons, System.Random rng)
        {
            int numColors = def.numColors;
            int holderCount = def.holderCount;

            // Count balloons per color
            int[] balloonsPerColor = new int[numColors];
            for (int i = 0; i < balloons.Length; i++)
            {
                int c = balloons[i].color;
                if (c >= 0 && c < numColors)
                    balloonsPerColor[c]++;
            }

            // Assign colors — ensure every active color gets at least one holder
            List<int> holderColors = new List<int>(holderCount);
            for (int c = 0; c < numColors; c++)
            {
                if (balloonsPerColor[c] > 0)
                    holderColors.Add(c);
            }

            // Fill remaining with weighted random
            while (holderColors.Count < holderCount)
            {
                holderColors.Add(PickWeightedColor(balloonsPerColor, rng));
            }

            if (holderColors.Count > holderCount)
                holderColors.RemoveRange(holderCount, holderColors.Count - holderCount);

            // Shuffle
            int[] arr = holderColors.ToArray();
            Shuffle(arr, rng);
            holderColors = new List<int>(arr);

            // Magazine distribution — ensure solvability per color
            Dictionary<int, List<int>> colorToIndices = new Dictionary<int, List<int>>();
            for (int i = 0; i < holderColors.Count; i++)
            {
                int c = holderColors[i];
                if (!colorToIndices.ContainsKey(c))
                    colorToIndices[c] = new List<int>();
                colorToIndices[c].Add(i);
            }

            int[] mags = new int[holderColors.Count];
            foreach (var kvp in colorToIndices)
            {
                int color = kvp.Key;
                List<int> indices = kvp.Value;
                int needed = balloonsPerColor[color]; // exact match, no surplus

                if (indices.Count == 0) continue;

                int perH = needed / indices.Count;
                int leftover = needed % indices.Count;

                // Assign evenly, last holder absorbs remainder for exact total
                int assigned = 0;
                for (int i = 0; i < indices.Count - 1; i++)
                {
                    int mag = perH + (i < leftover ? 1 : 0);
                    mags[indices[i]] = Mathf.Max(1, mag);
                    assigned += mags[indices[i]];
                }
                int lastMag = needed - assigned;
                mags[indices[indices.Count - 1]] = Mathf.Max(1, lastMag);
            }

            HolderSetup[] holders = new HolderSetup[holderColors.Count];
            for (int i = 0; i < holderColors.Count; i++)
            {
                holders[i] = new HolderSetup
                {
                    holderId = i,
                    color = holderColors[i],
                    magazineCount = Mathf.Max(1, mags[i]),
                    position = Vector2.zero // Position assigned by HolderVisualManager
                };
            }

            return holders;
        }

        private static int PickWeightedColor(int[] balloonsPerColor, System.Random rng)
        {
            int total = 0;
            for (int i = 0; i < balloonsPerColor.Length; i++) total += balloonsPerColor[i];
            if (total <= 0) return rng.Next(balloonsPerColor.Length);

            int roll = rng.Next(total);
            int cumulative = 0;
            for (int i = 0; i < balloonsPerColor.Length; i++)
            {
                cumulative += balloonsPerColor[i];
                if (roll < cumulative) return i;
            }
            return balloonsPerColor.Length - 1;
        }

        #endregion

        #region Rail Generation

        private static RailLayout GenerateRail(int gridCols, int gridRows)
        {
            float halfColWorld = (gridCols - 1) * CellSpacing * 0.5f;
            float halfRowWorld = (gridRows - 1) * CellSpacing * 0.5f;

            float left   = BoardCenterX - halfColWorld - RailPadding;
            float right  = BoardCenterX + halfColWorld + RailPadding;
            float bottom = BoardCenterZ - halfRowWorld - RailPadding;
            float top    = BoardCenterZ + halfRowWorld + RailPadding;

            List<Vector3> wp = new List<Vector3>(16);

            // Near edge (left→right)
            wp.Add(new Vector3(left, 0.5f, bottom));
            wp.Add(new Vector3(Mathf.Lerp(left, right, 0.33f), 0.5f, bottom));
            wp.Add(new Vector3(Mathf.Lerp(left, right, 0.67f), 0.5f, bottom));
            // Right edge (bottom→top)
            wp.Add(new Vector3(right, 0.5f, bottom));
            wp.Add(new Vector3(right, 0.5f, Mathf.Lerp(bottom, top, 0.33f)));
            wp.Add(new Vector3(right, 0.5f, Mathf.Lerp(bottom, top, 0.67f)));
            // Far edge (right→left)
            wp.Add(new Vector3(right, 0.5f, top));
            wp.Add(new Vector3(Mathf.Lerp(right, left, 0.33f), 0.5f, top));
            wp.Add(new Vector3(Mathf.Lerp(right, left, 0.67f), 0.5f, top));
            // Left edge (top→bottom)
            wp.Add(new Vector3(left, 0.5f, top));
            wp.Add(new Vector3(left, 0.5f, Mathf.Lerp(top, bottom, 0.33f)));
            wp.Add(new Vector3(left, 0.5f, Mathf.Lerp(top, bottom, 0.67f)));

            Vector3[] holderPositions = new Vector3[HolderQueueSlotCount];
            for (int i = 0; i < HolderQueueSlotCount; i++)
            {
                float t = (i + 1f) / (HolderQueueSlotCount + 1f);
                holderPositions[i] = new Vector3(Mathf.Lerp(left, right, t), 0.5f, bottom);
            }

            return new RailLayout { waypoints = wp.ToArray(), holderPositions = holderPositions };
        }

        #endregion

        #region Utility

        private static void Shuffle(int[] array, System.Random rng)
        {
            for (int i = array.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int tmp = array[i];
                array[i] = array[j];
                array[j] = tmp;
            }
        }

        #endregion
    }
}
