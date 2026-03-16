using System;
using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Manages the 5-package × 20-level progression structure.
    /// Tracks package unlock status, per-package star totals, and level completion counts.
    /// Progress is persisted via PlayerPrefs. Package 1 is always unlocked; subsequent
    /// packages unlock when the preceding package has at least 15 levels completed.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Manager | Phase: 2
    /// DB Reference: Expert DB "LevelManager" (Puzzle/Domain/Manager, score 0.6) — package
    ///               progression pattern adapted from IsLevelUnlocked / GetTotalStars.
    ///               No direct DB match for PackageManager — generated from L3 YAML logicFlow.
    /// </remarks>
    public class PackageManager : Singleton<PackageManager>
    {
        #region Constants

        private const int TOTAL_PACKAGES          = 5;
        private const int LEVELS_PER_PACKAGE      = 20;
        private const int MAX_STARS_PER_LEVEL      = 3;
        private const int UNLOCK_THRESHOLD_LEVELS  = 15;  // levels completed in previous pkg to unlock next

        private const string PREFS_COMPLETED_PREFIX = "BF_PkgCompleted_";
        private const string PREFS_STARS_PREFIX      = "BF_PkgStars_";

        #endregion

        #region Fields

        // Cached package data — rebuilt from PlayerPrefs on startup and refreshed on level complete
        private readonly PackageInfo[] _packages = new PackageInfo[TOTAL_PACKAGES];

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            InitializePackageCache();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelCompleted>(HandleLevelCompleted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelCompleted>(HandleLevelCompleted);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns the PackageInfo snapshot for the given package ID (1–5).
        /// Returns null if the ID is out of range.
        /// </summary>
        public PackageInfo GetPackageInfo(int packageId)
        {
            int index = packageId - 1;
            if (index < 0 || index >= TOTAL_PACKAGES)
            {
                Debug.LogWarning($"[PackageManager] GetPackageInfo: packageId {packageId} is out of range.");
                return null;
            }

            return _packages[index];
        }

        /// <summary>
        /// Returns the PackageInfo for the package that contains the currently loaded level.
        /// Falls back to Package 1 if LevelManager has no level loaded.
        /// </summary>
        public PackageInfo GetCurrentPackage()
        {
            int levelId = LevelManager.HasInstance ? LevelManager.Instance.GetCurrentLevelId() : 0;
            int packageId = levelId > 0
                ? Mathf.Clamp(((levelId - 1) / LEVELS_PER_PACKAGE) + 1, 1, TOTAL_PACKAGES)
                : 1;

            return GetPackageInfo(packageId);
        }

        /// <summary>
        /// Whether the given package is currently unlocked.
        /// Package 1 is always unlocked; others require the previous package to
        /// have at least <c>UNLOCK_THRESHOLD_LEVELS</c> completed levels.
        /// </summary>
        public bool IsPackageUnlocked(int packageId)
        {
            PackageInfo info = GetPackageInfo(packageId);
            return info != null && info.isUnlocked;
        }

        /// <summary>
        /// Returns the completion ratio [0.0–1.0] for the given package.
        /// (completedLevels / totalLevels). Returns 0 for invalid IDs.
        /// </summary>
        public float GetPackageProgress(int packageId)
        {
            PackageInfo info = GetPackageInfo(packageId);
            if (info == null || info.totalLevels <= 0)
            {
                return 0f;
            }

            return (float)info.completedLevels / info.totalLevels;
        }

        /// <summary>
        /// Unlocks the next locked package if the current last-unlocked package
        /// meets the completion threshold. Persists the change immediately.
        /// </summary>
        public void UnlockNextPackage()
        {
            for (int i = 0; i < TOTAL_PACKAGES - 1; i++)
            {
                PackageInfo current = _packages[i];
                PackageInfo next    = _packages[i + 1];

                if (current.isUnlocked && !next.isUnlocked
                    && current.completedLevels >= UNLOCK_THRESHOLD_LEVELS)
                {
                    next.isUnlocked = true;
                    SavePackageUnlocked(next.packageId);
                    Debug.Log($"[PackageManager] Package {next.packageId} unlocked.");
                    break;
                }
            }
        }

        /// <summary>
        /// Returns the sum of all stars earned across every package.
        /// </summary>
        public int GetTotalStars()
        {
            int total = 0;
            for (int i = 0; i < TOTAL_PACKAGES; i++)
            {
                total += _packages[i].totalStars;
            }

            return total;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Builds the in-memory PackageInfo array from PlayerPrefs on startup.
        /// </summary>
        private void InitializePackageCache()
        {
            for (int i = 0; i < TOTAL_PACKAGES; i++)
            {
                int pkgId = i + 1;
                bool isUnlocked = pkgId == 1 || IsUnlockedInPrefs(pkgId);

                _packages[i] = new PackageInfo
                {
                    packageId       = pkgId,
                    name            = $"Package {pkgId}",
                    isUnlocked      = isUnlocked,
                    completedLevels = LoadCompletedLevels(pkgId),
                    totalLevels     = LEVELS_PER_PACKAGE,
                    totalStars      = LoadPackageStars(pkgId),
                    maxStars        = LEVELS_PER_PACKAGE * MAX_STARS_PER_LEVEL   // 60
                };
            }

            // Enforce unlock state from scratch (cleans up any inconsistency in prefs)
            ReevaluateUnlockStates();
        }

        /// <summary>
        /// Re-evaluates which packages should be unlocked based on completion counts.
        /// Does not save to PlayerPrefs — only updates the cache; persists only new unlocks.
        /// </summary>
        private void ReevaluateUnlockStates()
        {
            // Package 1 always unlocked
            _packages[0].isUnlocked = true;

            for (int i = 1; i < TOTAL_PACKAGES; i++)
            {
                bool previousMeetsThreshold =
                    _packages[i - 1].completedLevels >= UNLOCK_THRESHOLD_LEVELS;

                if (previousMeetsThreshold && !_packages[i].isUnlocked)
                {
                    _packages[i].isUnlocked = true;
                    SavePackageUnlocked(_packages[i].packageId);
                }
            }
        }

        /// <summary>
        /// Handles level completion: updates completed level count and star total for the package.
        /// </summary>
        private void HandleLevelCompleted(OnLevelCompleted evt)
        {
            int packageId = ResolvePackageId(evt.levelId);
            int index     = packageId - 1;

            if (index < 0 || index >= TOTAL_PACKAGES)
            {
                return;
            }

            PackageInfo pkg = _packages[index];

            // Recount completed levels from PlayerPrefs (LevelManager tracks per-level best stars)
            pkg.completedLevels = RecountCompletedLevels(packageId);
            pkg.totalStars      = RecountPackageStars(packageId);

            // Persist aggregates
            SaveCompletedLevels(packageId, pkg.completedLevels);
            SavePackageStars(packageId, pkg.totalStars);

            // Check if next package should be unlocked
            UnlockNextPackage();
        }

        // ── Calculation Helpers ────────────────────────────────────────────────

        /// <summary>
        /// Derives the package ID (1–5) for a given global level ID (1–100).
        /// </summary>
        private int ResolvePackageId(int levelId)
        {
            return Mathf.Clamp(((levelId - 1) / LEVELS_PER_PACKAGE) + 1, 1, TOTAL_PACKAGES);
        }

        /// <summary>
        /// Counts how many levels in <paramref name="packageId"/> have been completed
        /// (i.e. best stars > 0) by querying LevelManager per-level PlayerPrefs.
        /// </summary>
        private int RecountCompletedLevels(int packageId)
        {
            if (!LevelManager.HasInstance)
            {
                return LoadCompletedLevels(packageId);
            }

            int firstLevel = ((packageId - 1) * LEVELS_PER_PACKAGE) + 1;
            int count = 0;

            for (int i = 0; i < LEVELS_PER_PACKAGE; i++)
            {
                if (LevelManager.Instance.GetBestStars(firstLevel + i) > 0)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Sums all best-star values for levels in <paramref name="packageId"/>
        /// by querying LevelManager per-level PlayerPrefs.
        /// </summary>
        private int RecountPackageStars(int packageId)
        {
            if (!LevelManager.HasInstance)
            {
                return LoadPackageStars(packageId);
            }

            int firstLevel = ((packageId - 1) * LEVELS_PER_PACKAGE) + 1;
            int total = 0;

            for (int i = 0; i < LEVELS_PER_PACKAGE; i++)
            {
                total += LevelManager.Instance.GetBestStars(firstLevel + i);
            }

            return total;
        }

        // ── PlayerPrefs I/O ────────────────────────────────────────────────────

        private bool IsUnlockedInPrefs(int packageId)
        {
            return PlayerPrefs.GetInt($"BF_PkgUnlocked_{packageId}", 0) == 1;
        }

        private void SavePackageUnlocked(int packageId)
        {
            PlayerPrefs.SetInt($"BF_PkgUnlocked_{packageId}", 1);
            PlayerPrefs.Save();
        }

        private int LoadCompletedLevels(int packageId)
        {
            return PlayerPrefs.GetInt(PREFS_COMPLETED_PREFIX + packageId, 0);
        }

        private void SaveCompletedLevels(int packageId, int count)
        {
            PlayerPrefs.SetInt(PREFS_COMPLETED_PREFIX + packageId, count);
            PlayerPrefs.Save();
        }

        private int LoadPackageStars(int packageId)
        {
            return PlayerPrefs.GetInt(PREFS_STARS_PREFIX + packageId, 0);
        }

        private void SavePackageStars(int packageId, int stars)
        {
            PlayerPrefs.SetInt(PREFS_STARS_PREFIX + packageId, stars);
            PlayerPrefs.Save();
        }

        #endregion
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Data Classes
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Snapshot of a package's progression state.
    /// </summary>
    [Serializable]
    public class PackageInfo
    {
        /// <summary>Package identifier (1–5).</summary>
        public int packageId;

        /// <summary>Display name, e.g. "Package 1".</summary>
        public string name;

        /// <summary>Whether this package is available to play.</summary>
        public bool isUnlocked;

        /// <summary>Number of levels in this package that have been completed at least once.</summary>
        public int completedLevels;

        /// <summary>Always 20 — fixed levels per package.</summary>
        public int totalLevels;

        /// <summary>Total best-star count accumulated across all levels in this package.</summary>
        public int totalStars;

        /// <summary>Always 60 — maximum possible stars (20 levels × 3 stars).</summary>
        public int maxStars;
    }
}
