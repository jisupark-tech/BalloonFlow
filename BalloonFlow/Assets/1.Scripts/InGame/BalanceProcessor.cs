using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Aggregates balance metrics (attempts, clears, scores, stars) for monitoring
    /// and tuning. Stores data in PlayerPrefs for persistence across sessions
    /// and in-memory for fast access. Provides per-level and per-package metrics.
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Processor | Phase: 2
    /// DB Reference: No DB match found (Puzzle/Processor/Balance: 0 results,
    ///               Generic/Processor/Balance: 0 results) — generated from L3 YAML logicFlow
    /// </remarks>
    public class BalanceProcessor : SceneSingleton<BalanceProcessor>
    {
        #region Constants

        private const string PREFS_PREFIX = "BF_Balance_";
        private const string PREFS_ATTEMPTS_SUFFIX = "_Attempts";
        private const string PREFS_CLEARS_SUFFIX = "_Clears";
        private const string PREFS_BEST_SCORE_SUFFIX = "_BestScore";
        private const string PREFS_BEST_STARS_SUFFIX = "_BestStars";
        private const string PREFS_TOTAL_SCORE_SUFFIX = "_TotalScore";

        private const int PACKAGE_SIZE = 20;
        private const int TOTAL_PACKAGES = 5;

        #endregion

        #region Nested Types

        /// <summary>
        /// Aggregated metrics for a single level.
        /// </summary>
        public struct LevelMetrics
        {
            public int levelId;
            public int attempts;
            public int clears;
            public int bestScore;
            public int bestStars;
            public float averageScore;
            public float clearRate;
        }

        /// <summary>
        /// Aggregated metrics for a package (group of 20 levels).
        /// </summary>
        public struct PackageMetrics
        {
            public int packageId;
            public int totalAttempts;
            public int totalClears;
            public float averageClearRate;
            public float averageStars;
        }

        /// <summary>
        /// Target clear-rate and attempt-count benchmarks per package from Expert DB.
        /// </summary>
        private struct PackageTarget
        {
            public float crAvg;
            public float attemptsAvg;

            public PackageTarget(float cr, float attempts)
            {
                crAvg = cr;
                attemptsAvg = attempts;
            }
        }

        #endregion

        #region Fields

        /// <summary>
        /// In-memory cache of level attempt data. Key = levelId.
        /// </summary>
        private readonly Dictionary<int, LevelAttemptData> _levelData =
            new Dictionary<int, LevelAttemptData>();

        /// <summary>
        /// Per-package target benchmarks from Expert DB.
        /// </summary>
        private static readonly PackageTarget[] PackageTargets = new PackageTarget[]
        {
            new PackageTarget(0.83f, 1.4f),  // PKG1
            new PackageTarget(0.68f, 2.2f),  // PKG2
            new PackageTarget(0.64f, 2.5f),  // PKG3
            new PackageTarget(0.61f, 2.8f),  // PKG4
            new PackageTarget(0.58f, 3.2f),  // PKG5
        };

        #endregion

        #region Internal Data Class

        /// <summary>
        /// Mutable runtime data for tracking a single level's attempts.
        /// </summary>
        private class LevelAttemptData
        {
            public int attempts;
            public int clears;
            public int bestScore;
            public int bestStars;
            public long totalScore;
        }

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            EventBus.Subscribe<OnLevelCompleted>(HandleLevelCompleted);
            EventBus.Subscribe<OnLevelFailed>(HandleLevelFailed);
        }

        protected override void OnDestroy()
        {
            EventBus.Unsubscribe<OnLevelCompleted>(HandleLevelCompleted);
            EventBus.Unsubscribe<OnLevelFailed>(HandleLevelFailed);
            base.OnDestroy();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns aggregated metrics for a single level.
        /// Loads from PlayerPrefs on first access.
        /// </summary>
        /// <param name="levelId">1-based level identifier.</param>
        /// <returns>LevelMetrics struct with all accumulated data.</returns>
        public LevelMetrics GetLevelMetrics(int levelId)
        {
            LevelAttemptData data = GetOrLoadLevelData(levelId);

            float avgScore = data.attempts > 0
                ? (float)data.totalScore / data.attempts
                : 0f;

            float cr = data.attempts > 0
                ? (float)data.clears / data.attempts
                : 0f;

            return new LevelMetrics
            {
                levelId = levelId,
                attempts = data.attempts,
                clears = data.clears,
                bestScore = data.bestScore,
                bestStars = data.bestStars,
                averageScore = avgScore,
                clearRate = cr
            };
        }

        /// <summary>
        /// Returns aggregated metrics for a package (group of 20 levels).
        /// Package IDs are 1-based: PKG1 = levels 1–20, PKG2 = levels 21–40, etc.
        /// </summary>
        /// <param name="packageId">1-based package identifier (1–5).</param>
        /// <returns>PackageMetrics struct with averaged data across all levels in the package.</returns>
        public PackageMetrics GetPackageMetrics(int packageId)
        {
            if (packageId < 1 || packageId > TOTAL_PACKAGES)
            {
                Debug.LogWarning($"[BalanceProcessor] Invalid packageId: {packageId}. Must be 1–{TOTAL_PACKAGES}.");
                return default;
            }

            int startLevel = (packageId - 1) * PACKAGE_SIZE + 1;
            int endLevel = startLevel + PACKAGE_SIZE;

            int totalAttempts = 0;
            int totalClears = 0;
            float totalClearRate = 0f;
            float totalStars = 0f;
            int levelsWithData = 0;

            for (int levelId = startLevel; levelId < endLevel; levelId++)
            {
                LevelMetrics metrics = GetLevelMetrics(levelId);
                totalAttempts += metrics.attempts;
                totalClears += metrics.clears;

                if (metrics.attempts > 0)
                {
                    totalClearRate += metrics.clearRate;
                    totalStars += metrics.bestStars;
                    levelsWithData++;
                }
            }

            float avgCR = levelsWithData > 0
                ? totalClearRate / levelsWithData
                : 0f;

            float avgStars = levelsWithData > 0
                ? totalStars / levelsWithData
                : 0f;

            return new PackageMetrics
            {
                packageId = packageId,
                totalAttempts = totalAttempts,
                totalClears = totalClears,
                averageClearRate = avgCR,
                averageStars = avgStars
            };
        }

        /// <summary>
        /// Records a single level attempt (clear or fail).
        /// Updates in-memory cache and persists to PlayerPrefs.
        /// </summary>
        /// <param name="levelId">1-based level identifier.</param>
        /// <param name="cleared">Whether the level was cleared.</param>
        /// <param name="score">Score achieved in this attempt.</param>
        /// <param name="stars">Stars earned in this attempt (0–3).</param>
        public void RecordLevelAttempt(int levelId, bool cleared, int score, int stars)
        {
            LevelAttemptData data = GetOrLoadLevelData(levelId);

            data.attempts++;
            data.totalScore += score;

            if (cleared)
            {
                data.clears++;

                if (score > data.bestScore)
                {
                    data.bestScore = score;
                }

                if (stars > data.bestStars)
                {
                    data.bestStars = stars;
                }
            }

            SaveLevelData(levelId, data);
        }

        /// <summary>
        /// Returns the average clear rate across all levels in a package
        /// that have at least one attempt recorded.
        /// </summary>
        /// <param name="packageId">1-based package identifier (1–5).</param>
        /// <returns>Average clear rate in [0, 1], or 0 if no data exists.</returns>
        public float GetAverageClearRate(int packageId)
        {
            PackageMetrics metrics = GetPackageMetrics(packageId);
            return metrics.averageClearRate;
        }

        /// <summary>
        /// Returns the Expert DB target clear rate for a given package.
        /// </summary>
        /// <param name="packageId">1-based package identifier (1–5).</param>
        /// <returns>Target CR from Expert DB, or 0 if invalid packageId.</returns>
        public float GetTargetClearRate(int packageId)
        {
            if (packageId < 1 || packageId > TOTAL_PACKAGES)
            {
                return 0f;
            }

            return PackageTargets[packageId - 1].crAvg;
        }

        /// <summary>
        /// Returns the Expert DB target average attempts for a given package.
        /// </summary>
        /// <param name="packageId">1-based package identifier (1–5).</param>
        /// <returns>Target attempts from Expert DB, or 0 if invalid packageId.</returns>
        public float GetTargetAttempts(int packageId)
        {
            if (packageId < 1 || packageId > TOTAL_PACKAGES)
            {
                return 0f;
            }

            return PackageTargets[packageId - 1].attemptsAvg;
        }

        /// <summary>
        /// Returns the deviation between actual and target clear rate for a package.
        /// Positive = easier than target, negative = harder than target.
        /// </summary>
        /// <param name="packageId">1-based package identifier (1–5).</param>
        /// <returns>CR deviation, or 0 if no data or invalid packageId.</returns>
        public float GetClearRateDeviation(int packageId)
        {
            float actual = GetAverageClearRate(packageId);
            float target = GetTargetClearRate(packageId);

            return actual - target;
        }

        /// <summary>
        /// Clears all balance data from memory and PlayerPrefs.
        /// Use with caution — intended for dev/debug reset.
        /// </summary>
        public void ClearAllData()
        {
            foreach (KeyValuePair<int, LevelAttemptData> kvp in _levelData)
            {
                DeleteLevelPrefs(kvp.Key);
            }

            _levelData.Clear();
        }

        #endregion

        #region Private Methods — Event Handlers

        private void HandleLevelCompleted(OnLevelCompleted evt)
        {
            int stars = evt.starCount;
            RecordLevelAttempt(evt.levelId, true, evt.score, stars);
        }

        private void HandleLevelFailed(OnLevelFailed evt)
        {
            RecordLevelAttempt(evt.levelId, false, 0, 0);
        }

        #endregion

        #region Private Methods — Data Access

        /// <summary>
        /// Returns cached data or loads from PlayerPrefs on first access.
        /// </summary>
        private LevelAttemptData GetOrLoadLevelData(int levelId)
        {
            if (_levelData.TryGetValue(levelId, out LevelAttemptData existing))
            {
                return existing;
            }

            LevelAttemptData loaded = LoadLevelData(levelId);
            _levelData[levelId] = loaded;
            return loaded;
        }

        /// <summary>
        /// Loads level attempt data from PlayerPrefs.
        /// Returns zeroed data if no prefs exist.
        /// </summary>
        private LevelAttemptData LoadLevelData(int levelId)
        {
            string prefix = PREFS_PREFIX + levelId;

            return new LevelAttemptData
            {
                attempts = PlayerPrefs.GetInt(prefix + PREFS_ATTEMPTS_SUFFIX, 0),
                clears = PlayerPrefs.GetInt(prefix + PREFS_CLEARS_SUFFIX, 0),
                bestScore = PlayerPrefs.GetInt(prefix + PREFS_BEST_SCORE_SUFFIX, 0),
                bestStars = PlayerPrefs.GetInt(prefix + PREFS_BEST_STARS_SUFFIX, 0),
                totalScore = PlayerPrefs.GetInt(prefix + PREFS_TOTAL_SCORE_SUFFIX, 0)
            };
        }

        /// <summary>
        /// Persists level attempt data to PlayerPrefs.
        /// </summary>
        private void SaveLevelData(int levelId, LevelAttemptData data)
        {
            string prefix = PREFS_PREFIX + levelId;

            PlayerPrefs.SetInt(prefix + PREFS_ATTEMPTS_SUFFIX, data.attempts);
            PlayerPrefs.SetInt(prefix + PREFS_CLEARS_SUFFIX, data.clears);
            PlayerPrefs.SetInt(prefix + PREFS_BEST_SCORE_SUFFIX, data.bestScore);
            PlayerPrefs.SetInt(prefix + PREFS_BEST_STARS_SUFFIX, data.bestStars);

            // PlayerPrefs only supports int; clamp totalScore to int range for persistence
            int totalScoreClamped = (int)Mathf.Clamp(data.totalScore, int.MinValue, int.MaxValue);
            PlayerPrefs.SetInt(prefix + PREFS_TOTAL_SCORE_SUFFIX, totalScoreClamped);

            PlayerPrefs.Save();
        }

        /// <summary>
        /// Deletes all PlayerPrefs keys for a given level.
        /// </summary>
        private void DeleteLevelPrefs(int levelId)
        {
            string prefix = PREFS_PREFIX + levelId;

            PlayerPrefs.DeleteKey(prefix + PREFS_ATTEMPTS_SUFFIX);
            PlayerPrefs.DeleteKey(prefix + PREFS_CLEARS_SUFFIX);
            PlayerPrefs.DeleteKey(prefix + PREFS_BEST_SCORE_SUFFIX);
            PlayerPrefs.DeleteKey(prefix + PREFS_BEST_STARS_SUFFIX);
            PlayerPrefs.DeleteKey(prefix + PREFS_TOTAL_SCORE_SUFFIX);
        }

        #endregion
    }
}
