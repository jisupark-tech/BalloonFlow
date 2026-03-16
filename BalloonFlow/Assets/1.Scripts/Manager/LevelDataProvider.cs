using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Single source of truth for level configuration data.
    /// Parses and exposes LevelConfig, HolderSetup[], BalloonLayout[], and RailLayout
    /// from a LevelDatabase ScriptableObject assigned in the Inspector.
    /// Publishes no events — pure data provider.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Provider | Phase: 1
    /// DB Reference: No DB match — generated from L3 YAML logicFlow
    /// </remarks>
    public class LevelDataProvider : MonoBehaviour
    {
        #region Fields

        [SerializeField]
        private LevelDatabase _levelDatabase;

        #endregion

        #region Properties

        /// <summary>
        /// Whether the database asset is assigned and contains at least one level.
        /// </summary>
        public bool IsReady => _levelDatabase != null
                               && _levelDatabase.levels != null
                               && _levelDatabase.levels.Length > 0;

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns the full LevelConfig for the given level ID (1-based).
        /// Returns null and logs a warning if the level does not exist.
        /// </summary>
        public LevelConfig GetLevelData(int levelId)
        {
            if (!ValidateDatabaseLoaded())
            {
                return null;
            }

            int index = levelId - 1;
            if (index < 0 || index >= _levelDatabase.levels.Length)
            {
                Debug.LogWarning($"[LevelDataProvider] Level {levelId} is out of range " +
                                 $"(database has {_levelDatabase.levels.Length} levels).");
                return null;
            }

            return _levelDatabase.levels[index];
        }

        /// <summary>
        /// Returns the holder setup array for the given level ID.
        /// Returns an empty array if the level does not exist or has no holders.
        /// </summary>
        public HolderSetup[] GetHolderSetup(int levelId)
        {
            LevelConfig config = GetLevelData(levelId);
            if (config == null)
            {
                return System.Array.Empty<HolderSetup>();
            }

            return config.holders ?? System.Array.Empty<HolderSetup>();
        }

        /// <summary>
        /// Returns the balloon layout array for the given level ID.
        /// Returns an empty array if the level does not exist or has no balloons.
        /// </summary>
        public BalloonLayout[] GetBalloonLayout(int levelId)
        {
            LevelConfig config = GetLevelData(levelId);
            if (config == null)
            {
                return System.Array.Empty<BalloonLayout>();
            }

            return config.balloons ?? System.Array.Empty<BalloonLayout>();
        }

        /// <summary>
        /// Returns the rail layout for the given level ID.
        /// Returns null if the level does not exist.
        /// </summary>
        public RailLayout GetRailLayout(int levelId)
        {
            LevelConfig config = GetLevelData(levelId);
            return config?.rail;
        }

        /// <summary>
        /// Returns the total number of levels in the database.
        /// Returns 0 if the database is not loaded.
        /// </summary>
        public int GetLevelCount()
        {
            if (_levelDatabase == null || _levelDatabase.levels == null)
            {
                return 0;
            }

            return _levelDatabase.levels.Length;
        }

        #endregion

        #region Private Methods

        private bool ValidateDatabaseLoaded()
        {
            if (_levelDatabase == null)
            {
                // Auto-load from Resources if not wired via Inspector
                _levelDatabase = Resources.Load<LevelDatabase>("LevelDatabase");
                if (_levelDatabase != null)
                {
                    Debug.Log($"[LevelDataProvider] Auto-loaded LevelDatabase from Resources ({_levelDatabase.levels?.Length ?? 0} levels).");
                }
            }

            if (_levelDatabase == null)
            {
                Debug.LogWarning("[LevelDataProvider] LevelDatabase not found. Run BalloonFlow > Generate 50 Levels.");
                return false;
            }

            if (_levelDatabase.levels == null || _levelDatabase.levels.Length == 0)
            {
                Debug.LogWarning("[LevelDataProvider] LevelDatabase has no levels.");
                return false;
            }

            return true;
        }

        #endregion
    }
}
