using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Procedural level generator that creates LevelConfig data for any level ID.
    /// Uses seeded random for reproducibility. Generates board grid, rail waypoints,
    /// holder setups, and balloon layouts scaled by difficulty progression.
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Manager | Phase: 1
    /// DB Reference: No DB match found (Puzzle/Manager/LevelGenerator: 0 results,
    ///               Generic/Manager/LevelGenerator: 0 results) — generated from L3 YAML logicFlow
    /// </remarks>
    public class LevelGenerator : SceneSingleton<LevelGenerator>
    {
        #region Constants — Grid Scaling

        /// <summary>Minimum grid dimension for earliest levels.</summary>
        private const int GridMinSize = 4;

        /// <summary>Maximum grid dimension for latest levels (Doc: up to 27×32).</summary>
        private const int GridMaxSize = 27;

        /// <summary>World-space distance between adjacent balloon cells. GameManager.Board에서 읽어옴.</summary>
        private float CellSpacing => GameManager.HasInstance ? GameManager.Instance.Board.cellSpacing : 0.55f;

        /// <summary>Z offset for the board center.</summary>
        private float BoardCenterZ => GameManager.HasInstance ? GameManager.Instance.Board.boardCenterZ : 2.0f;

        /// <summary>X offset for the board center.</summary>
        private float BoardCenterX => GameManager.HasInstance ? GameManager.Instance.Board.boardCenterX : 0.0f;

        /// <summary>Padding between board edge and rail path.</summary>
        private float RailPadding => GameManager.HasInstance ? GameManager.Instance.Board.railPadding : 1.5f;

        #endregion

        #region Constants — Color Scaling

        /// <summary>Minimum number of distinct colors.</summary>
        private const int MinColors = 2;

        /// <summary>Maximum number of distinct colors.</summary>
        private const int MaxColors = 11;

        #endregion

        #region Constants — Holder Scaling

        /// <summary>Minimum number of holders per level.</summary>
        private const int MinHolders = 10;

        /// <summary>Maximum number of holders per level.</summary>
        private const int MaxHolders = 50;

        /// <summary>Z range for holder waiting area (near edge).</summary>
        private const float HolderAreaZStart = -5.0f;

        /// <summary>Z range for holder waiting area (far edge).</summary>
        private const float HolderAreaZEnd = -7.0f;

        /// <summary>Exact magazine ratio to balloon count (1.0 = no surplus darts).</summary>
        private const float MagazineSurplusRatio = 1.0f;

        /// <summary>Number of holder queue staging slots along rail bottom edge.</summary>
        private const int HolderQueueSlotCount = 5;

        #endregion

        #region Constants — Level Progression Breakpoints (5-Phase for 300 Levels)

        /// <summary>Phase 1 cap: PKG 1-2, 4×4 to 8×8, 2-5 colors.</summary>
        private const int Phase1MaxLevel = 40;

        /// <summary>Phase 2 cap: PKG 3-5, 8×8 to 12×12, 4-7 colors.</summary>
        private const int Phase2MaxLevel = 100;

        /// <summary>Phase 3 cap: PKG 6-9, 12×12 to 18×18, 5-9 colors.</summary>
        private const int Phase3MaxLevel = 180;

        /// <summary>Phase 4 cap: PKG 10-13, 18×18 to 24×24, 6-10 colors.</summary>
        private const int Phase4MaxLevel = 260;

        // Phase 5: 261-300, PKG 14-15, 20×20 to 27×27, 7-11 colors

        /// <summary>Levels per package.</summary>
        private const int LevelsPerPackage = 20;

        /// <summary>Total packages.</summary>
        private const int TotalPackages = 15;

        #endregion

        #region Public Methods

        /// <summary>
        /// Generates a complete LevelConfig for the given level ID.
        /// Uses levelId as the random seed for full reproducibility.
        /// </summary>
        /// <param name="levelId">1-based level identifier.</param>
        /// <returns>A fully populated LevelConfig ready for gameplay.</returns>
        public LevelConfig GenerateLevel(int levelId)
        {
            if (levelId < 1)
            {
                Debug.LogWarning("[LevelGenerator] levelId must be >= 1. Clamping to 1.");
                levelId = 1;
            }

            System.Random rng = new System.Random(levelId);

            int gridSize = CalculateGridSize(levelId, rng);
            int numColors = CalculateColorCount(levelId, rng);
            int holderCount = CalculateHolderCount(levelId, rng);

            BalloonLayout[] balloons = GenerateBalloonGrid(gridSize, numColors, rng);
            int balloonCount = balloons.Length;

            HolderSetup[] holders = GenerateHolders(holderCount, numColors, balloonCount, balloons, rng);
            RailLayout rail = GenerateRailLayout(gridSize);

            int star1 = balloonCount * 100;
            int star2 = Mathf.CeilToInt(star1 * 1.5f);
            int star3 = Mathf.CeilToInt(star1 * 2.2f);

            int packageId = Mathf.Clamp(((levelId - 1) / LevelsPerPackage) + 1, 1, TotalPackages);
            int positionInPackage = ((levelId - 1) % LevelsPerPackage) + 1;

            DifficultyPurpose difficultyPurpose = DeterminePurpose(packageId, positionInPackage);
            int queueColumns = CalculateQueueColumns(difficultyPurpose, rng);
            float targetClearRate = CalculateTargetClearRate(packageId, difficultyPurpose, rng);

            LevelConfig config = new LevelConfig
            {
                levelId = levelId,
                packageId = packageId,
                positionInPackage = positionInPackage,
                numColors = numColors,
                balloonCount = balloonCount,
                queueColumns = queueColumns,
                targetClearRate = targetClearRate,
                difficultyPurpose = difficultyPurpose,
                gimmickTypes = System.Array.Empty<string>(),
                holders = holders,
                balloons = balloons,
                rail = rail,
                star1Threshold = star1,
                star2Threshold = star2,
                star3Threshold = star3
            };

            Debug.Log($"[LevelGenerator] Generated level {levelId} (PKG{packageId}): " +
                      $"grid={gridSize}x{gridSize}, colors={numColors}, " +
                      $"balloons={balloonCount}, holders={holderCount}, " +
                      $"queue={queueColumns}, clearRate={targetClearRate:F2}, " +
                      $"purpose={difficultyPurpose}");

            return config;
        }

        #endregion

        #region Private Methods — Grid Size

        /// <summary>
        /// Calculates the grid dimension based on 5-phase level progression.
        /// Phase 1 (1-40):   4×4 → 8×8
        /// Phase 2 (41-100): 8×8 → 12×12
        /// Phase 3 (101-180): 12×12 → 18×18
        /// Phase 4 (181-260): 18×18 → 24×24
        /// Phase 5 (261-300): 20×20 → 27×27
        /// </summary>
        private int CalculateGridSize(int levelId, System.Random rng)
        {
            int minSize;
            int maxSize;
            float t;

            if (levelId <= Phase1MaxLevel)
            {
                minSize = 4;
                maxSize = 8;
                t = (levelId - 1f) / Mathf.Max(1f, Phase1MaxLevel - 1f);
            }
            else if (levelId <= Phase2MaxLevel)
            {
                minSize = 8;
                maxSize = 12;
                t = (levelId - Phase1MaxLevel - 1f) /
                    Mathf.Max(1f, Phase2MaxLevel - Phase1MaxLevel - 1f);
            }
            else if (levelId <= Phase3MaxLevel)
            {
                minSize = 12;
                maxSize = 18;
                t = (levelId - Phase2MaxLevel - 1f) /
                    Mathf.Max(1f, Phase3MaxLevel - Phase2MaxLevel - 1f);
            }
            else if (levelId <= Phase4MaxLevel)
            {
                minSize = 18;
                maxSize = 24;
                t = (levelId - Phase3MaxLevel - 1f) /
                    Mathf.Max(1f, Phase4MaxLevel - Phase3MaxLevel - 1f);
            }
            else
            {
                minSize = 20;
                maxSize = 27;
                t = (levelId - Phase4MaxLevel - 1f) /
                    Mathf.Max(1f, 300f - Phase4MaxLevel - 1f);
                t = Mathf.Clamp01(t);
            }

            int baseSize = Mathf.RoundToInt(Mathf.Lerp(minSize, maxSize, t));

            // Small random variance (+/- 1) for variety
            int variance = rng.Next(-1, 2);
            return Mathf.Clamp(baseSize + variance, GridMinSize, GridMaxSize);
        }

        #endregion

        #region Private Methods — Color Count

        /// <summary>
        /// Determines the number of distinct colors for a level.
        /// PKG 1 avg ~3.6 → PKG 15 avg ~7.0.
        /// Phase 1 (1-40):   2-5 colors
        /// Phase 2 (41-100): 4-7 colors
        /// Phase 3 (101-180): 5-9 colors
        /// Phase 4 (181-260): 6-10 colors
        /// Phase 5 (261-300): 7-11 colors
        /// </summary>
        private int CalculateColorCount(int levelId, System.Random rng)
        {
            int phaseMin;
            int phaseMax;

            if (levelId <= Phase1MaxLevel)
            {
                phaseMin = 2;
                phaseMax = 5;
            }
            else if (levelId <= Phase2MaxLevel)
            {
                phaseMin = 4;
                phaseMax = 7;
            }
            else if (levelId <= Phase3MaxLevel)
            {
                phaseMin = 5;
                phaseMax = 9;
            }
            else if (levelId <= Phase4MaxLevel)
            {
                phaseMin = 6;
                phaseMax = 10;
            }
            else
            {
                phaseMin = 7;
                phaseMax = 11;
            }

            // Base color count from level progression within phase
            float normalizedLevel = Mathf.Clamp01((levelId - 1f) / 299f);
            int baseColors = Mathf.RoundToInt(Mathf.Lerp(phaseMin, phaseMax, normalizedLevel));

            // Small random variance
            int variance = rng.Next(0, 2);
            return Mathf.Clamp(baseColors + variance, MinColors, MaxColors);
        }

        #endregion

        #region Private Methods — Balloon Grid

        /// <summary>
        /// Generates a grid of balloons with roughly even color distribution.
        /// Grid is centered at (BoardCenterX, BoardCenterZ) on the XZ plane.
        /// gridPosition.x = world X, gridPosition.y = world Z.
        /// </summary>
        private BalloonLayout[] GenerateBalloonGrid(int gridSize, int numColors, System.Random rng)
        {
            List<BalloonLayout> balloons = new List<BalloonLayout>(gridSize * gridSize);

            float halfGridWorld = (gridSize - 1) * CellSpacing * 0.5f;
            int balloonId = 0;

            // Build a color pool for roughly even distribution
            int totalCells = gridSize * gridSize;
            int[] colorPool = BuildColorPool(totalCells, numColors, rng);

            for (int row = 0; row < gridSize; row++)
            {
                for (int col = 0; col < gridSize; col++)
                {
                    float worldX = BoardCenterX + (col * CellSpacing) - halfGridWorld;
                    float worldZ = BoardCenterZ + (row * CellSpacing) - halfGridWorld;

                    balloons.Add(new BalloonLayout
                    {
                        balloonId = balloonId,
                        color = colorPool[balloonId],
                        gridPosition = new Vector2(worldX, worldZ),  // x=world X, y=world Z
                        gimmickType = ""
                    });

                    balloonId++;
                }
            }

            return balloons.ToArray();
        }

        /// <summary>
        /// Builds a shuffled color pool ensuring roughly even distribution.
        /// </summary>
        private int[] BuildColorPool(int totalCount, int numColors, System.Random rng)
        {
            int[] pool = new int[totalCount];
            int perColor = totalCount / numColors;
            int remainder = totalCount % numColors;

            int index = 0;
            for (int c = 0; c < numColors; c++)
            {
                int count = perColor + (c < remainder ? 1 : 0);
                for (int i = 0; i < count; i++)
                {
                    pool[index++] = c;
                }
            }

            // Fisher-Yates shuffle
            for (int i = pool.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int temp = pool[i];
                pool[i] = pool[j];
                pool[j] = temp;
            }

            return pool;
        }

        #endregion

        #region Private Methods — Holders

        /// <summary>
        /// Determines the number of holders for a level. Scales across 300 levels.
        /// </summary>
        private int CalculateHolderCount(int levelId, System.Random rng)
        {
            float normalizedLevel = Mathf.Clamp01((levelId - 1f) / 299f);
            int baseCount = Mathf.RoundToInt(Mathf.Lerp(MinHolders, MaxHolders, normalizedLevel));
            int variance = rng.Next(0, 3);
            return Mathf.Clamp(baseCount + variance, MinHolders, MaxHolders);
        }

        /// <summary>
        /// Generates holder setups with colors matching board balloons.
        /// Ensures total magazine >= total balloon count for solvability.
        /// </summary>
        private HolderSetup[] GenerateHolders(
            int holderCount,
            int numColors,
            int balloonCount,
            BalloonLayout[] balloons,
            System.Random rng)
        {
            // Count balloons per color
            int[] balloonsPerColor = new int[numColors];
            if (balloons != null)
            {
                for (int i = 0; i < balloons.Length; i++)
                {
                    int c = balloons[i].color;
                    if (c >= 0 && c < numColors)
                    {
                        balloonsPerColor[c]++;
                    }
                }
            }

            // Assign colors to holders — ensure every color with balloons gets at least one holder
            List<int> holderColors = new List<int>(holderCount);

            // First pass: one holder per active color
            for (int c = 0; c < numColors; c++)
            {
                if (balloonsPerColor[c] > 0)
                {
                    holderColors.Add(c);
                }
            }

            // Fill remaining slots with weighted random colors
            while (holderColors.Count < holderCount)
            {
                // Pick a color weighted by balloon count
                int colorPick = PickWeightedColor(balloonsPerColor, rng);
                holderColors.Add(colorPick);
            }

            // If we have more colors than holders, truncate (rare edge case)
            if (holderColors.Count > holderCount)
            {
                holderColors.RemoveRange(holderCount, holderColors.Count - holderCount);
            }

            // Shuffle holder order
            for (int i = holderColors.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int temp = holderColors[i];
                holderColors[i] = holderColors[j];
                holderColors[j] = temp;
            }

            // Count how many holders per color
            Dictionary<int, List<int>> colorToHolderIndices = new Dictionary<int, List<int>>();
            for (int i = 0; i < holderColors.Count; i++)
            {
                int c = holderColors[i];
                if (!colorToHolderIndices.ContainsKey(c))
                {
                    colorToHolderIndices[c] = new List<int>();
                }
                colorToHolderIndices[c].Add(i);
            }

            int[] magazineCounts = new int[holderColors.Count];

            // Distribute magazine: each color's total darts must EXACTLY match that color's balloon count.
            // MagazineCount rounded to nearest 5, range 5-50 (Doc: containers hold 5-50 darts).
            // After rounding, the last holder for each color absorbs the difference to guarantee symmetry.
            foreach (var kvp in colorToHolderIndices)
            {
                int color = kvp.Key;
                List<int> indices = kvp.Value;
                int needed = balloonsPerColor[color]; // exact match, no surplus
                int perHolder = Mathf.Max(1, needed / indices.Count);
                int leftover = needed - (perHolder * indices.Count);

                int sumSoFar = 0;
                for (int i = 0; i < indices.Count; i++)
                {
                    if (i < indices.Count - 1)
                    {
                        int rawMag = perHolder + (i < leftover ? 1 : 0);
                        // Round to nearest multiple of 5, clamp to [5, 50]
                        int mag = Mathf.Clamp(((rawMag + 2) / 5) * 5, 5, 50);
                        magazineCounts[indices[i]] = mag;
                        sumSoFar += mag;
                    }
                    else
                    {
                        // Last holder absorbs the remainder to ensure exact total
                        int remaining = needed - sumSoFar;
                        int mag = Mathf.Clamp(((remaining + 2) / 5) * 5, 5, 50);
                        // If rounding causes mismatch, use exact value (allow non-multiple-of-5)
                        if (sumSoFar + mag != needed)
                        {
                            mag = Mathf.Max(1, remaining);
                        }
                        magazineCounts[indices[i]] = mag;
                        sumSoFar += mag;
                    }
                }

                // Final validation: if sum still doesn't match (edge case from clamping),
                // adjust the last holder directly
                if (sumSoFar != needed && indices.Count > 0)
                {
                    int lastIdx = indices[indices.Count - 1];
                    magazineCounts[lastIdx] += (needed - sumSoFar);
                    if (magazineCounts[lastIdx] < 1) magazineCounts[lastIdx] = 1;
                }
            }

            // Build holder setups with positions in the waiting area
            HolderSetup[] holders = new HolderSetup[holderColors.Count];
            float holderSpacing = holderColors.Count > 1
                ? 6f / (holderColors.Count - 1)
                : 0f;
            float holderStartX = -3f;

            for (int i = 0; i < holderColors.Count; i++)
            {
                float xPos = holderColors.Count > 1
                    ? holderStartX + (i * holderSpacing)
                    : 0f;
                float zPos = Mathf.Lerp(HolderAreaZStart, HolderAreaZEnd, (i % 2) * 0.5f);

                holders[i] = new HolderSetup
                {
                    holderId = i,
                    color = holderColors[i],
                    magazineCount = magazineCounts[i],
                    position = new Vector2(xPos, zPos)  // x=world X, y=world Z
                };
            }

            return holders;
        }

        /// <summary>
        /// Picks a color index weighted by the balloon count per color.
        /// </summary>
        private int PickWeightedColor(int[] balloonsPerColor, System.Random rng)
        {
            int total = 0;
            for (int i = 0; i < balloonsPerColor.Length; i++)
            {
                total += balloonsPerColor[i];
            }

            if (total <= 0)
            {
                return rng.Next(balloonsPerColor.Length);
            }

            int roll = rng.Next(total);
            int cumulative = 0;
            for (int i = 0; i < balloonsPerColor.Length; i++)
            {
                cumulative += balloonsPerColor[i];
                if (roll < cumulative)
                {
                    return i;
                }
            }

            return balloonsPerColor.Length - 1;
        }

        #endregion

        #region Private Methods — Rail Layout

        /// <summary>
        /// Generates a rectangular closed-loop rail surrounding the board on the XZ plane.
        /// Waypoints use Y=0.5f (rail height above board). X is the horizontal axis,
        /// Z is the depth axis (forward). "bottom/top" refer to Z values.
        /// Also generates holder queue staging positions along the near (bottom-Z) edge.
        /// </summary>
        private RailLayout GenerateRailLayout(int gridSize)
        {
            float halfGrid = (gridSize - 1) * CellSpacing * 0.5f;

            // Rail rectangle bounds with padding (XZ plane)
            float left   = BoardCenterX - halfGrid - RailPadding;
            float right  = BoardCenterX + halfGrid + RailPadding;
            float bottom = BoardCenterZ - halfGrid - RailPadding;  // near Z
            float top    = BoardCenterZ + halfGrid + RailPadding;  // far Z

            // Build waypoints: corners + 2 intermediate points per side (clockwise from bottom-left)
            // Format: new Vector3(x, 0.5f, z)  — Y=0.5f is the rail height above the board
            List<Vector3> waypoints = new List<Vector3>(16);

            // Near edge / bottom-Z (left to right)
            waypoints.Add(new Vector3(left,                          0.5f, bottom));
            waypoints.Add(new Vector3(Mathf.Lerp(left, right, 0.33f), 0.5f, bottom));
            waypoints.Add(new Vector3(Mathf.Lerp(left, right, 0.67f), 0.5f, bottom));

            // Right edge (near-Z to far-Z)
            waypoints.Add(new Vector3(right, 0.5f, bottom));
            waypoints.Add(new Vector3(right, 0.5f, Mathf.Lerp(bottom, top, 0.33f)));
            waypoints.Add(new Vector3(right, 0.5f, Mathf.Lerp(bottom, top, 0.67f)));

            // Far edge / top-Z (right to left)
            waypoints.Add(new Vector3(right,                          0.5f, top));
            waypoints.Add(new Vector3(Mathf.Lerp(right, left, 0.33f), 0.5f, top));
            waypoints.Add(new Vector3(Mathf.Lerp(right, left, 0.67f), 0.5f, top));

            // Left edge (far-Z to near-Z)
            waypoints.Add(new Vector3(left, 0.5f, top));
            waypoints.Add(new Vector3(left, 0.5f, Mathf.Lerp(top, bottom, 0.33f)));
            waypoints.Add(new Vector3(left, 0.5f, Mathf.Lerp(top, bottom, 0.67f)));

            // Holder queue staging positions: 5 slots evenly spaced along the near (bottom-Z) edge
            Vector3[] holderPositions = new Vector3[HolderQueueSlotCount];
            for (int i = 0; i < HolderQueueSlotCount; i++)
            {
                float t = (i + 1f) / (HolderQueueSlotCount + 1f);
                holderPositions[i] = new Vector3(
                    Mathf.Lerp(left, right, t),
                    0.5f,
                    bottom
                );
            }

            return new RailLayout
            {
                waypoints = waypoints.ToArray(),
                holderPositions = holderPositions
            };
        }

        #endregion

        #region Private Methods — Difficulty Purpose

        /// <summary>
        /// Determines the pacing role of a level based on Doc BeatChart positioning rules.
        /// Pos 4, 9, 14, 19: Hard or Super Hard
        /// Pos 5, 10, 15, 20: Rest (recovery after peak)
        /// Pos 1 or 11: Gimmick introduction (Tutorial)
        /// Remaining: Normal
        /// PKG 1 exception: Pos 1-3 tutorial, only Pos 19 is hard.
        /// </summary>
        private DifficultyPurpose DeterminePurpose(int packageId, int posInPackage)
        {
            if (packageId == 1)
            {
                if (posInPackage <= 3) return DifficultyPurpose.Tutorial;
                if (posInPackage == 19) return DifficultyPurpose.Hard;
                if (posInPackage == 20) return DifficultyPurpose.Rest;
                return DifficultyPurpose.Normal;
            }

            if (posInPackage == 1 || posInPackage == 11) return DifficultyPurpose.Tutorial;
            if (posInPackage == 4 || posInPackage == 14) return DifficultyPurpose.Hard;
            if (posInPackage == 9) return DifficultyPurpose.SuperHard;
            if (posInPackage == 19) return DifficultyPurpose.SuperHard;
            if (posInPackage == 5 || posInPackage == 10 || posInPackage == 15 || posInPackage == 20) return DifficultyPurpose.Rest;
            return DifficultyPurpose.Normal;
        }

        #endregion

        #region Private Methods — Queue Columns

        /// <summary>
        /// Calculates holder queue column count (2–5) based on difficulty purpose.
        /// Tutorial: always 2, Normal: 2-4, Hard/Super Hard: 3-5.
        /// </summary>
        private int CalculateQueueColumns(DifficultyPurpose purpose, System.Random rng)
        {
            switch (purpose)
            {
                case DifficultyPurpose.Tutorial:
                    return 2;
                case DifficultyPurpose.Normal:
                    return rng.Next(2, 5);
                case DifficultyPurpose.Rest:
                    return rng.Next(2, 4);
                case DifficultyPurpose.Hard:
                case DifficultyPurpose.SuperHard:
                    return rng.Next(3, 6);
                default:
                    return 3;
            }
        }

        #endregion

        #region Private Methods — Target Clear Rate

        /// <summary>
        /// Calculates target clear rate based on package and difficulty purpose.
        /// Tutorial: 0.88-0.95, Normal: 0.40-0.80 (decreasing by PKG),
        /// Hard: 0.20-0.60, Super Hard: 0.12-0.45, Rest: 0.60-0.90.
        /// </summary>
        private float CalculateTargetClearRate(int packageId, DifficultyPurpose purpose, System.Random rng)
        {
            float pkgProgression = Mathf.Clamp01((packageId - 1f) / 14f); // 0.0 at PKG1, 1.0 at PKG15

            float minRate;
            float maxRate;

            switch (purpose)
            {
                case DifficultyPurpose.Tutorial:
                    minRate = 0.88f;
                    maxRate = 0.95f;
                    break;
                case DifficultyPurpose.Normal:
                    // Decreasing range as packages progress
                    minRate = Mathf.Lerp(0.60f, 0.40f, pkgProgression);
                    maxRate = Mathf.Lerp(0.80f, 0.55f, pkgProgression);
                    break;
                case DifficultyPurpose.Hard:
                    minRate = Mathf.Lerp(0.40f, 0.20f, pkgProgression);
                    maxRate = Mathf.Lerp(0.60f, 0.35f, pkgProgression);
                    break;
                case DifficultyPurpose.SuperHard:
                    minRate = Mathf.Lerp(0.30f, 0.12f, pkgProgression);
                    maxRate = Mathf.Lerp(0.45f, 0.20f, pkgProgression);
                    break;
                case DifficultyPurpose.Rest:
                    minRate = 0.60f;
                    maxRate = 0.90f;
                    break;
                default:
                    minRate = 0.50f;
                    maxRate = 0.70f;
                    break;
            }

            // Random float within [minRate, maxRate]
            float rate = minRate + (float)rng.NextDouble() * (maxRate - minRate);
            return Mathf.Round(rate * 100f) / 100f; // Round to 2 decimal places
        }

        #endregion
    }
}
