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

        /// <summary>Maximum grid dimension for latest levels.</summary>
        private const int GridMaxSize = 10;

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
        private const int MaxColors = 8;

        #endregion

        #region Constants — Holder Scaling

        /// <summary>Minimum number of holders per level.</summary>
        private const int MinHolders = 10;

        /// <summary>Maximum number of holders per level.</summary>
        private const int MaxHolders = 25;

        /// <summary>Z range for holder waiting area (near edge).</summary>
        private const float HolderAreaZStart = -5.0f;

        /// <summary>Z range for holder waiting area (far edge).</summary>
        private const float HolderAreaZEnd = -7.0f;

        /// <summary>Extra magazine ratio above balloon count to ensure solvability.</summary>
        private const float MagazineSurplusRatio = 1.25f;

        /// <summary>Number of holder queue staging slots along rail bottom edge.</summary>
        private const int HolderQueueSlotCount = 5;

        #endregion

        #region Constants — Level Progression Breakpoints

        /// <summary>Level range for small boards (4x4 to 6x6).</summary>
        private const int SmallBoardMaxLevel = 10;

        /// <summary>Level range for medium boards (6x6 to 8x8).</summary>
        private const int MediumBoardMaxLevel = 30;

        /// <summary>Levels per package.</summary>
        private const int LevelsPerPackage = 20;

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

            int packageId = ((levelId - 1) / LevelsPerPackage) + 1;
            int positionInPackage = ((levelId - 1) % LevelsPerPackage) + 1;

            string difficultyPurpose = DetermineDifficultyPurpose(levelId, positionInPackage);

            LevelConfig config = new LevelConfig
            {
                levelId = levelId,
                packageId = packageId,
                positionInPackage = positionInPackage,
                numColors = numColors,
                balloonCount = balloonCount,
                difficultyPurpose = difficultyPurpose,
                gimmickTypes = System.Array.Empty<string>(),
                holders = holders,
                balloons = balloons,
                rail = rail,
                star1Threshold = star1,
                star2Threshold = star2,
                star3Threshold = star3
            };

            Debug.Log($"[LevelGenerator] Generated level {levelId}: " +
                      $"grid={gridSize}x{gridSize}, colors={numColors}, " +
                      $"balloons={balloonCount}, holders={holderCount}, " +
                      $"purpose={difficultyPurpose}");

            return config;
        }

        #endregion

        #region Private Methods — Grid Size

        /// <summary>
        /// Calculates the grid dimension based on level progression.
        /// Levels 1-10: 4-6, Levels 11-30: 6-8, Levels 31+: 8-10.
        /// </summary>
        private int CalculateGridSize(int levelId, System.Random rng)
        {
            int minSize;
            int maxSize;

            if (levelId <= SmallBoardMaxLevel)
            {
                minSize = 4;
                maxSize = 6;
            }
            else if (levelId <= MediumBoardMaxLevel)
            {
                minSize = 6;
                maxSize = 8;
            }
            else
            {
                minSize = 8;
                maxSize = 10;
            }

            // Interpolate within the range based on position within the bracket
            float t = GetBracketProgress(levelId);
            int baseSize = Mathf.RoundToInt(Mathf.Lerp(minSize, maxSize, t));

            // Small random variance (+/- 1) for variety
            int variance = rng.Next(-1, 2);
            return Mathf.Clamp(baseSize + variance, GridMinSize, GridMaxSize);
        }

        /// <summary>
        /// Returns 0..1 progress within the current difficulty bracket.
        /// </summary>
        private float GetBracketProgress(int levelId)
        {
            if (levelId <= SmallBoardMaxLevel)
            {
                return (levelId - 1f) / Mathf.Max(1f, SmallBoardMaxLevel - 1f);
            }

            if (levelId <= MediumBoardMaxLevel)
            {
                return (levelId - SmallBoardMaxLevel - 1f) /
                       Mathf.Max(1f, MediumBoardMaxLevel - SmallBoardMaxLevel - 1f);
            }

            // Beyond medium: clamp at 1.0 for levels 31+
            float progress = (levelId - MediumBoardMaxLevel - 1f) / 20f;
            return Mathf.Clamp01(progress);
        }

        #endregion

        #region Private Methods — Color Count

        /// <summary>
        /// Determines the number of distinct colors for a level.
        /// Early levels use fewer colors for simplicity.
        /// </summary>
        private int CalculateColorCount(int levelId, System.Random rng)
        {
            // Base color count from level progression
            float normalizedLevel = Mathf.Clamp01((levelId - 1f) / 50f);
            int baseColors = Mathf.RoundToInt(Mathf.Lerp(MinColors, MaxColors, normalizedLevel));

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
        /// Determines the number of holders for a level.
        /// </summary>
        private int CalculateHolderCount(int levelId, System.Random rng)
        {
            float normalizedLevel = Mathf.Clamp01((levelId - 1f) / 40f);
            int baseCount = Mathf.RoundToInt(Mathf.Lerp(MinHolders, MaxHolders, normalizedLevel));
            int variance = rng.Next(0, 2);
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

            // Calculate magazine counts — ensure solvability
            int totalMagazineTarget = Mathf.CeilToInt(balloonCount * MagazineSurplusRatio);

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

            // Distribute magazine: each color's holders must cover that color's balloons
            foreach (var kvp in colorToHolderIndices)
            {
                int color = kvp.Key;
                List<int> indices = kvp.Value;
                int needed = Mathf.CeilToInt(balloonsPerColor[color] * MagazineSurplusRatio);
                int perHolder = Mathf.Max(1, needed / indices.Count);
                int leftover = needed - (perHolder * indices.Count);

                for (int i = 0; i < indices.Count; i++)
                {
                    magazineCounts[indices[i]] = perHolder + (i < leftover ? 1 : 0);
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
                    magazineCount = Mathf.Max(1, magazineCounts[i]),
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
        /// Determines the pacing role of a level based on its position within the package.
        /// Follows a sawtooth pattern: tutorial → normal → hard → rest → super_hard.
        /// </summary>
        private string DetermineDifficultyPurpose(int levelId, int positionInPackage)
        {
            // First 3 levels are always tutorial
            if (levelId <= 3)
            {
                return "tutorial";
            }

            // Sawtooth within package
            if (positionInPackage <= 2)
            {
                return "rest";
            }

            if (positionInPackage <= 8)
            {
                return "normal";
            }

            if (positionInPackage <= 14)
            {
                return "hard";
            }

            if (positionInPackage <= 17)
            {
                return "normal";
            }

            if (positionInPackage <= 19)
            {
                return "super_hard";
            }

            // Position 20 = package boss then rest
            return "hard";
        }

        #endregion
    }
}
