using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Validates observed clear rates against design targets for each package.
    /// Reports whether a package is balanced, too easy, or too hard.
    /// Requires actual clear rates to be supplied by the caller (typically BalanceProcessor).
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Validator | Phase: 2
    /// DB Reference: No direct DB match for ClearRateValidator — generated from L3 YAML logicFlow.
    ///               Target CR values sourced from Expert DB balance_system entry for Puzzle genre.
    ///               Dependency: BalanceProcessor.GetAverageClearRate(packageId) → not yet generated;
    ///               caller is responsible for supplying actualCR via ValidateClearRate(packageId, actualCR).
    /// </remarks>
    public static class ClearRateValidator
    {
        #region Constants

        private const float TOLERANCE = 0.10f;

        private const string RECOMMENDATION_BALANCED  = "balanced";
        private const string RECOMMENDATION_TOO_EASY  = "too_easy";
        private const string RECOMMENDATION_TOO_HARD  = "too_hard";

        #endregion

        #region Fields

        // Target clear rates indexed by packageId (1-based).
        // Index 0 is unused; packages 1–5 occupy indices 1–5.
        private static readonly float[] s_targetClearRates = new float[]
        {
            0f,     // [0] unused
            0.83f,  // PKG1
            0.68f,  // PKG2
            0.64f,  // PKG3
            0.61f,  // PKG4
            0.58f   // PKG5
        };

        private const int MIN_PACKAGE_ID = 1;
        private const int MAX_PACKAGE_ID = 5;

        #endregion

        #region Public Methods

        /// <summary>
        /// Validates the observed clear rate for a package against the design target.
        /// </summary>
        /// <param name="packageId">Package identifier (1–5).</param>
        /// <param name="actualClearRate">
        ///     Observed average clear rate [0.0–1.0].
        ///     Supply via BalanceProcessor.GetAverageClearRate(packageId) once that system exists.
        /// </param>
        /// <returns>
        ///     A <see cref="ValidationResult"/> with target, deviation, health flag, and recommendation.
        ///     Returns a default (unhealthy) result with a warning if packageId is out of range.
        /// </returns>
        public static ValidationResult ValidateClearRate(int packageId, float actualClearRate)
        {
            if (!IsValidPackageId(packageId))
            {
                Debug.LogWarning(
                    $"[ClearRateValidator] packageId {packageId} is out of range " +
                    $"(expected {MIN_PACKAGE_ID}–{MAX_PACKAGE_ID}).");

                return new ValidationResult
                {
                    packageId      = packageId,
                    targetCR       = 0f,
                    actualCR       = actualClearRate,
                    deviation      = actualClearRate,
                    isHealthy      = false,
                    recommendation = RECOMMENDATION_TOO_HARD
                };
            }

            float target    = s_targetClearRates[packageId];
            float deviation = actualClearRate - target;
            bool  isHealthy = Mathf.Abs(deviation) <= TOLERANCE;

            string recommendation;
            if (actualClearRate > target + TOLERANCE)
            {
                recommendation = RECOMMENDATION_TOO_EASY;
            }
            else if (actualClearRate < target - TOLERANCE)
            {
                recommendation = RECOMMENDATION_TOO_HARD;
            }
            else
            {
                recommendation = RECOMMENDATION_BALANCED;
            }

            return new ValidationResult
            {
                packageId      = packageId,
                targetCR       = target,
                actualCR       = actualClearRate,
                deviation      = deviation,
                isHealthy      = isHealthy,
                recommendation = recommendation
            };
        }

        /// <summary>
        /// Returns the design-target clear rate for the given package (1–5).
        /// Returns -1 and logs a warning for invalid IDs.
        /// </summary>
        public static float GetTargetClearRate(int packageId)
        {
            if (!IsValidPackageId(packageId))
            {
                Debug.LogWarning(
                    $"[ClearRateValidator] GetTargetClearRate: packageId {packageId} is out of range.");
                return -1f;
            }

            return s_targetClearRates[packageId];
        }

        /// <summary>
        /// Returns true if the given actual clear rate falls within acceptable tolerance
        /// of the design target for <paramref name="packageId"/>.
        /// Returns false for invalid package IDs.
        /// </summary>
        /// <param name="packageId">Package identifier (1–5).</param>
        /// <param name="actualClearRate">
        ///     Observed average clear rate [0.0–1.0].
        ///     Supply via BalanceProcessor.GetAverageClearRate(packageId).
        /// </param>
        public static bool IsBalanceHealthy(int packageId, float actualClearRate)
        {
            if (!IsValidPackageId(packageId))
            {
                return false;
            }

            float target = s_targetClearRates[packageId];
            return Mathf.Abs(actualClearRate - target) <= TOLERANCE;
        }

        /// <summary>
        /// Validates all 5 packages and returns a list of results.
        /// Caller must supply a map of packageId → actualClearRate.
        /// Packages without an entry in <paramref name="actualRates"/> are skipped.
        /// </summary>
        public static List<ValidationResult> ValidateAll(Dictionary<int, float> actualRates)
        {
            var results = new List<ValidationResult>(MAX_PACKAGE_ID);

            if (actualRates == null || actualRates.Count == 0)
            {
                Debug.LogWarning("[ClearRateValidator] ValidateAll received an empty rate dictionary.");
                return results;
            }

            for (int pkgId = MIN_PACKAGE_ID; pkgId <= MAX_PACKAGE_ID; pkgId++)
            {
                if (!actualRates.TryGetValue(pkgId, out float actualCR))
                {
                    continue;
                }

                results.Add(ValidateClearRate(pkgId, actualCR));
            }

            return results;
        }

        #endregion

        #region Private Methods

        private static bool IsValidPackageId(int packageId)
        {
            return packageId >= MIN_PACKAGE_ID && packageId <= MAX_PACKAGE_ID;
        }

        #endregion
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Result Struct
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Holds the outcome of a clear-rate validation pass for a single package.
    /// </summary>
    public struct ValidationResult
    {
        /// <summary>Package identifier (1–5).</summary>
        public int packageId;

        /// <summary>Design-target clear rate for this package [0.0–1.0].</summary>
        public float targetCR;

        /// <summary>Observed average clear rate [0.0–1.0].</summary>
        public float actualCR;

        /// <summary>Signed deviation: actualCR − targetCR. Positive = easier than target.</summary>
        public float deviation;

        /// <summary>True if |deviation| ≤ tolerance (±0.10).</summary>
        public bool isHealthy;

        /// <summary>
        /// One of: "balanced", "too_easy", "too_hard".
        /// </summary>
        public string recommendation;
    }
}
