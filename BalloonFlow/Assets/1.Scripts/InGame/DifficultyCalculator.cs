using System;
using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Pure-math static utility that computes difficulty score, magazine count,
    /// predicted clear rate, and sawtooth modulation for any LevelConfig.
    /// No MonoBehaviour dependencies — all methods are deterministic.
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Calculator | Phase: 2
    /// DB Reference: No DB match found (Puzzle/Calculator/Balance: 0 results,
    ///               Generic/Calculator/Balance: 0 results) — generated from L3 YAML logicFlow
    /// </remarks>
    public static class DifficultyCalculator
    {
        #region Constants — Difficulty Weights

        /// <summary>Weight for color count contribution (max 20).</summary>
        private const float W_COLOR = 20f;

        /// <summary>Weight for balloon count contribution (max 20).</summary>
        private const float W_COUNT = 20f;

        /// <summary>Weight for gimmick difficulty contribution (max 25).</summary>
        private const float W_GIMMICK = 25f;

        /// <summary>Weight for estimated return rate contribution (max 20).</summary>
        private const float W_RETURN = 20f;

        /// <summary>Weight for color skew contribution (max 15).</summary>
        private const float W_SKEW = 15f;

        #endregion

        #region Constants — Gimmick Weights

        private const float GIMMICK_HIDDEN = 0.15f;
        private const float GIMMICK_SPAWNER_T = 0.20f;
        private const float GIMMICK_SPAWNER_O = 0.25f;
        private const float GIMMICK_BIG_OBJECT = 0.30f;
        private const float GIMMICK_CHAIN = 0.40f;
        private const float GIMMICK_WEIGHT_MAX = 1.0f;

        #endregion

        #region Constants — Sawtooth Modulation

        private const int SAWTOOTH_CYCLE_LENGTH = 5;

        private static readonly float[] SawtoothModifiers = new float[]
        {
            0.7f, 0.85f, 0.95f, 1.0f, 0.6f
        };

        #endregion

        #region Constants — Base Progression

        private const float BASE_DIFFICULTY_INTERCEPT = 5f;
        private const float BASE_DIFFICULTY_SLOPE = 0.96f;
        private const float DIFFICULTY_MIN = 0f;
        private const float DIFFICULTY_MAX = 100f;

        #endregion

        #region Constants — Magazine

        private const float OPTIMAL_DART_RATIO = 0.7f;
        private const float BUFFER_TUTORIAL = 2.0f;
        private const float BUFFER_NORMAL = 1.4f;
        private const float BUFFER_HARD = 1.15f;
        private const float BUFFER_SUPER_HARD = 1.05f;
        private const float BUFFER_REST = 1.5f;
        private const float BUFFER_DEFAULT = 1.4f;

        #endregion

        #region Constants — Clear Rate Sigmoid

        private const float SIGMOID_K = 6.0f;
        private const float SIGMOID_THRESHOLD = 0.15f;

        #endregion

        #region Constants — Normalization Bounds

        private const float COLOR_RANGE_MIN = 2f;
        private const float COLOR_RANGE_MAX = 8f;
        private const float BALLOON_COUNT_NORM = 80f;

        #endregion

        #region Public Methods

        /// <summary>
        /// Calculates the composite difficulty score D(L) for a level,
        /// then applies sawtooth modulation based on level number.
        /// Result is clamped to [0, 100].
        /// </summary>
        /// <param name="levelConfig">The level configuration to evaluate.</param>
        /// <returns>Final modulated difficulty score in [0, 100], or -1 if config is null.</returns>
        public static float CalculateDifficulty(LevelConfig levelConfig)
        {
            if (levelConfig == null)
            {
                Debug.LogWarning("[DifficultyCalculator] CalculateDifficulty received null LevelConfig.");
                return -1f;
            }

            float colorTerm = W_COLOR * Mathf.Clamp01((levelConfig.numColors - COLOR_RANGE_MIN)
                              / (COLOR_RANGE_MAX - COLOR_RANGE_MIN));

            float countTerm = W_COUNT * Mathf.Clamp01(levelConfig.balloonCount / BALLOON_COUNT_NORM);

            float gimmickTerm = W_GIMMICK * GetGimmickWeight(levelConfig.gimmickTypes);

            float returnRate = EstimateReturnRate(levelConfig);
            float returnTerm = W_RETURN * returnRate;

            float skew = CalculateColorSkew(levelConfig);
            float skewTerm = W_SKEW * skew;

            float rawDifficulty = colorTerm + countTerm + gimmickTerm + returnTerm + skewTerm;

            float sawtoothMod = GetSawtoothModifier(levelConfig.levelId);
            float finalDifficulty = rawDifficulty * sawtoothMod;

            return Mathf.Clamp(finalDifficulty, DIFFICULTY_MIN, DIFFICULTY_MAX);
        }

        /// <summary>
        /// Calculates the total magazine (dart) count for a level based on
        /// balloon count and difficulty purpose.
        /// Formula: ceil(ceil(balloonCount * 0.7) * buffer_mult)
        /// </summary>
        /// <param name="levelConfig">The level configuration.</param>
        /// <returns>Total magazine count, or 0 if config is null.</returns>
        public static int CalculateMagazineCount(LevelConfig levelConfig)
        {
            if (levelConfig == null)
            {
                Debug.LogWarning("[DifficultyCalculator] CalculateMagazineCount received null LevelConfig.");
                return 0;
            }

            int optimalDarts = Mathf.CeilToInt(levelConfig.balloonCount * OPTIMAL_DART_RATIO);
            float bufferMult = GetBufferMultiplier(levelConfig.difficultyPurpose);
            int totalMagazine = Mathf.CeilToInt(optimalDarts * bufferMult);

            return Mathf.Max(1, totalMagazine);
        }

        /// <summary>
        /// Predicts the clear rate for a level using a sigmoid model.
        /// predicted_clear_rate = sigmoid(k * (excess_margin - threshold))
        /// where excess_margin = (total_magazine - optimal_darts) / optimal_darts
        /// </summary>
        /// <param name="levelConfig">The level configuration.</param>
        /// <returns>Predicted clear rate in [0, 1], or -1 if config is null.</returns>
        public static float PredictClearRate(LevelConfig levelConfig)
        {
            if (levelConfig == null)
            {
                Debug.LogWarning("[DifficultyCalculator] PredictClearRate received null LevelConfig.");
                return -1f;
            }

            int optimalDarts = Mathf.CeilToInt(levelConfig.balloonCount * OPTIMAL_DART_RATIO);
            if (optimalDarts <= 0)
            {
                return 1f;
            }

            int totalMagazine = CalculateMagazineCount(levelConfig);
            float excessMargin = (totalMagazine - optimalDarts) / (float)optimalDarts;
            float sigmoidInput = SIGMOID_K * (excessMargin - SIGMOID_THRESHOLD);

            return Sigmoid(sigmoidInput);
        }

        /// <summary>
        /// Computes the combined gimmick weight for a set of gimmick type names.
        /// Multiple gimmicks are summed and clamped to [0, 1].
        /// </summary>
        /// <param name="gimmickTypes">Array of gimmick type strings (e.g. "Hidden", "Chain").</param>
        /// <returns>Combined gimmick weight in [0, 1].</returns>
        public static float GetGimmickWeight(string[] gimmickTypes)
        {
            if (gimmickTypes == null || gimmickTypes.Length == 0)
            {
                return 0f;
            }

            float totalWeight = 0f;

            for (int i = 0; i < gimmickTypes.Length; i++)
            {
                totalWeight += GetSingleGimmickWeight(gimmickTypes[i]);
            }

            return Mathf.Clamp(totalWeight, 0f, GIMMICK_WEIGHT_MAX);
        }

        /// <summary>
        /// Returns the sawtooth modulation multiplier for a given level number.
        /// Cycle length is 5 with modifiers [0.7, 0.85, 0.95, 1.0, 0.6].
        /// </summary>
        /// <param name="levelNumber">1-based level number.</param>
        /// <returns>Sawtooth modifier in [0.6, 1.0].</returns>
        public static float GetSawtoothModifier(int levelNumber)
        {
            if (levelNumber <= 0)
            {
                return SawtoothModifiers[0];
            }

            int position = (levelNumber - 1) % SAWTOOTH_CYCLE_LENGTH;
            return SawtoothModifiers[position];
        }

        /// <summary>
        /// Returns the base difficulty for a level number using linear progression.
        /// base_difficulty(L) = 5 + (L-1) * 0.96, clamped to [0, 100].
        /// </summary>
        /// <param name="levelNumber">1-based level number.</param>
        /// <returns>Base difficulty in [0, 100].</returns>
        public static float GetBaseDifficulty(int levelNumber)
        {
            float raw = BASE_DIFFICULTY_INTERCEPT + (levelNumber - 1) * BASE_DIFFICULTY_SLOPE;
            return Mathf.Clamp(raw, DIFFICULTY_MIN, DIFFICULTY_MAX);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Returns the weight for a single gimmick type string.
        /// Case-insensitive matching.
        /// </summary>
        private static float GetSingleGimmickWeight(string gimmickType)
        {
            if (string.IsNullOrEmpty(gimmickType))
            {
                return 0f;
            }

            string normalized = gimmickType.Trim().ToLowerInvariant();

            switch (normalized)
            {
                case "none":
                    return 0f;
                case "hidden":
                    return GIMMICK_HIDDEN;
                case "spawner_t":
                    return GIMMICK_SPAWNER_T;
                case "spawner_o":
                    return GIMMICK_SPAWNER_O;
                case "bigobject":
                    return GIMMICK_BIG_OBJECT;
                case "chain":
                    return GIMMICK_CHAIN;
                default:
                    Debug.LogWarning($"[DifficultyCalculator] Unknown gimmick type: {gimmickType}");
                    return 0f;
            }
        }

        /// <summary>
        /// Estimates the average return rate across all colors in a level.
        /// per_color_return = max(0, 1 - matching_balloons(color) / magazine(color))
        /// estimated_return_rate = avg(per_color_return for each color)
        /// </summary>
        private static float EstimateReturnRate(LevelConfig levelConfig)
        {
            if (levelConfig.balloons == null || levelConfig.balloons.Length == 0)
            {
                return 0f;
            }

            if (levelConfig.holders == null || levelConfig.holders.Length == 0)
            {
                return 1f;
            }

            // Count balloons per color
            Dictionary<int, int> balloonsByColor = new Dictionary<int, int>();
            for (int i = 0; i < levelConfig.balloons.Length; i++)
            {
                int color = levelConfig.balloons[i].color;
                if (balloonsByColor.ContainsKey(color))
                {
                    balloonsByColor[color]++;
                }
                else
                {
                    balloonsByColor[color] = 1;
                }
            }

            // Count magazine per color
            Dictionary<int, int> magazineByColor = new Dictionary<int, int>();
            for (int i = 0; i < levelConfig.holders.Length; i++)
            {
                int color = levelConfig.holders[i].color;
                int mag = levelConfig.holders[i].magazineCount;
                if (magazineByColor.ContainsKey(color))
                {
                    magazineByColor[color] += mag;
                }
                else
                {
                    magazineByColor[color] = mag;
                }
            }

            // Compute per-color return rate
            float totalReturn = 0f;
            int colorCount = 0;

            // Iterate all colors present in balloons
            foreach (KeyValuePair<int, int> kvp in balloonsByColor)
            {
                int color = kvp.Key;
                int balloonCountForColor = kvp.Value;

                int magazineForColor = 0;
                if (magazineByColor.ContainsKey(color))
                {
                    magazineForColor = magazineByColor[color];
                }

                float perColorReturn;
                if (magazineForColor <= 0)
                {
                    perColorReturn = 1f;
                }
                else
                {
                    perColorReturn = Mathf.Max(0f, 1f - (float)balloonCountForColor / magazineForColor);
                }

                totalReturn += perColorReturn;
                colorCount++;
            }

            if (colorCount <= 0)
            {
                return 0f;
            }

            return totalReturn / colorCount;
        }

        /// <summary>
        /// Calculates color skew = min(1.0, std(color_counts) / mean(color_counts)).
        /// </summary>
        private static float CalculateColorSkew(LevelConfig levelConfig)
        {
            if (levelConfig.balloons == null || levelConfig.balloons.Length == 0)
            {
                return 0f;
            }

            // Count balloons per color
            Dictionary<int, int> colorCounts = new Dictionary<int, int>();
            for (int i = 0; i < levelConfig.balloons.Length; i++)
            {
                int color = levelConfig.balloons[i].color;
                if (colorCounts.ContainsKey(color))
                {
                    colorCounts[color]++;
                }
                else
                {
                    colorCounts[color] = 1;
                }
            }

            int numDistinctColors = colorCounts.Count;
            if (numDistinctColors <= 1)
            {
                return 0f;
            }

            // Calculate mean
            float sum = 0f;
            foreach (KeyValuePair<int, int> kvp in colorCounts)
            {
                sum += kvp.Value;
            }
            float mean = sum / numDistinctColors;

            if (mean <= 0f)
            {
                return 0f;
            }

            // Calculate standard deviation
            float varianceSum = 0f;
            foreach (KeyValuePair<int, int> kvp in colorCounts)
            {
                float diff = kvp.Value - mean;
                varianceSum += diff * diff;
            }
            float stdDev = Mathf.Sqrt(varianceSum / numDistinctColors);

            return Mathf.Min(1f, stdDev / mean);
        }

        /// <summary>
        /// Returns the buffer multiplier for a given difficulty purpose string.
        /// </summary>
        private static float GetBufferMultiplier(DifficultyPurpose difficultyPurpose)
        {
            switch (difficultyPurpose)
            {
                case DifficultyPurpose.Tutorial:
                    return BUFFER_TUTORIAL;
                case DifficultyPurpose.Normal:
                    return BUFFER_NORMAL;
                case DifficultyPurpose.Hard:
                    return BUFFER_HARD;
                case DifficultyPurpose.SuperHard:
                    return BUFFER_SUPER_HARD;
                case DifficultyPurpose.Rest:
                    return BUFFER_REST;
                default:
                    return BUFFER_DEFAULT;
            }
        }

        /// <summary>
        /// Standard sigmoid function: 1 / (1 + e^(-x)).
        /// </summary>
        private static float Sigmoid(float x)
        {
            return 1f / (1f + Mathf.Exp(-x));
        }

        #endregion
    }
}
