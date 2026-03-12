using System;
using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Manages gimmick unlock states and activation for a given level.
    /// Gimmicks are feature-gated by global level ID. Each gimmick type becomes
    /// available once the player reaches its unlock level; inactive gimmick types
    /// are stripped from the active set when a level is initialized.
    /// Does NOT handle gimmick gameplay logic (handled by BalloonController).
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Manager | Phase: 2
    /// DB Reference: No direct DB match for GimmickManager — generated from L3 YAML logicFlow.
    ///               Gimmick unlock thresholds sourced from Expert DB gimmick_spec (Puzzle/content).
    /// </remarks>
    public class GimmickManager : Singleton<GimmickManager>
    {
        #region Constants

        // Global level IDs at which each gimmick type is first introduced
        private const int UNLOCK_LEVEL_HIDDEN      = 11;
        private const int UNLOCK_LEVEL_SPAWNER_T   = 21;
        private const int UNLOCK_LEVEL_SPAWNER_O   = 31;
        private const int UNLOCK_LEVEL_BIG_OBJECT  = 41;
        private const int UNLOCK_LEVEL_CHAIN        = 61;

        // String identifiers that match LevelConfig.gimmickTypes values
        public const string GIMMICK_HIDDEN      = "Hidden";
        public const string GIMMICK_SPAWNER_T   = "Spawner_T";
        public const string GIMMICK_SPAWNER_O   = "Spawner_O";
        public const string GIMMICK_BIG_OBJECT  = "BigObject";
        public const string GIMMICK_CHAIN        = "Chain";

        #endregion

        #region Fields

        // Populated during InitializeGimmicks(); cleared on each new level
        private readonly List<string> _activeGimmicks = new List<string>();

        // Cached current level ID so repeated calls to GetActiveGimmicks don't re-evaluate
        private int _lastInitializedLevelId = -1;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            _activeGimmicks.Clear();
            _lastInitializedLevelId = -1;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns whether the given gimmick type has been unlocked for the
        /// specified global level ID. Unlock is permanent once the threshold is passed.
        /// </summary>
        /// <param name="gimmickType">One of the GIMMICK_* constants.</param>
        /// <param name="levelId">Global level ID (1-based).</param>
        public bool IsGimmickUnlocked(string gimmickType, int levelId)
        {
            if (string.IsNullOrEmpty(gimmickType))
            {
                return false;
            }

            int unlockLevel = GetUnlockLevel(gimmickType);
            return levelId >= unlockLevel;
        }

        /// <summary>
        /// Overload using the level ID from the most recently initialized level.
        /// Returns false if <see cref="InitializeGimmicks"/> has not been called.
        /// </summary>
        public bool IsGimmickUnlocked(string gimmickType)
        {
            return IsGimmickUnlocked(gimmickType, _lastInitializedLevelId);
        }

        /// <summary>
        /// Returns the array of gimmick type strings that are both present in the
        /// level config AND unlocked at the given level ID.
        /// Returns an empty array if the config is null or no gimmicks qualify.
        /// </summary>
        /// <param name="levelId">Global level ID — used to filter by unlock state.</param>
        public string[] GetActiveGimmicks(int levelId)
        {
            if (levelId != _lastInitializedLevelId)
            {
                Debug.LogWarning(
                    $"[GimmickManager] GetActiveGimmicks called for levelId {levelId} " +
                    $"but last initialized was {_lastInitializedLevelId}. " +
                    "Call InitializeGimmicks first.");
            }

            return _activeGimmicks.ToArray();
        }

        /// <summary>
        /// Returns a human-readable description of what the given gimmick does.
        /// Returns an empty string for unrecognized types.
        /// </summary>
        public string GetGimmickDescription(string gimmickType)
        {
            if (string.IsNullOrEmpty(gimmickType))
            {
                return string.Empty;
            }

            switch (gimmickType)
            {
                case GIMMICK_HIDDEN:
                    return "Balloon is hidden until a dart lands nearby.";
                case GIMMICK_SPAWNER_T:
                    return "Balloon spawns additional T-shaped balloons when popped.";
                case GIMMICK_SPAWNER_O:
                    return "Balloon spawns additional ring-shaped balloons when popped.";
                case GIMMICK_BIG_OBJECT:
                    return "Oversized balloon requiring multiple direct hits to pop.";
                case GIMMICK_CHAIN:
                    return "Popping this balloon triggers a chain reaction on connected balloons.";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Reads the level config to build the active gimmick list for this level.
        /// Only gimmicks whose type is both listed in <paramref name="levelConfig"/>.gimmickTypes
        /// AND has been unlocked at this level ID are added to the active set.
        /// Must be called before <see cref="GetActiveGimmicks"/> for accurate results.
        /// </summary>
        /// <param name="levelConfig">The config for the level being set up. Must not be null.</param>
        public void InitializeGimmicks(LevelConfig levelConfig)
        {
            _activeGimmicks.Clear();

            if (levelConfig == null)
            {
                Debug.LogWarning("[GimmickManager] InitializeGimmicks received a null LevelConfig.");
                _lastInitializedLevelId = -1;
                return;
            }

            _lastInitializedLevelId = levelConfig.levelId;

            if (levelConfig.gimmickTypes == null || levelConfig.gimmickTypes.Length == 0)
            {
                // Level has no gimmicks — nothing to activate
                return;
            }

            foreach (string gimmickType in levelConfig.gimmickTypes)
            {
                if (string.IsNullOrEmpty(gimmickType))
                {
                    continue;
                }

                if (IsGimmickUnlocked(gimmickType, levelConfig.levelId))
                {
                    _activeGimmicks.Add(gimmickType);
                }
                else
                {
                    Debug.Log(
                        $"[GimmickManager] Gimmick '{gimmickType}' in level {levelConfig.levelId} " +
                        $"is not yet unlocked (unlocks at level {GetUnlockLevel(gimmickType)}).");
                }
            }

            if (_activeGimmicks.Count > 0)
            {
                Debug.Log(
                    $"[GimmickManager] Level {levelConfig.levelId} active gimmicks: " +
                    string.Join(", ", _activeGimmicks));
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Returns the global level ID at which <paramref name="gimmickType"/> unlocks.
        /// Returns <c>int.MaxValue</c> for unknown types so they are never considered unlocked.
        /// </summary>
        private int GetUnlockLevel(string gimmickType)
        {
            switch (gimmickType)
            {
                case GIMMICK_HIDDEN:     return UNLOCK_LEVEL_HIDDEN;
                case GIMMICK_SPAWNER_T:  return UNLOCK_LEVEL_SPAWNER_T;
                case GIMMICK_SPAWNER_O:  return UNLOCK_LEVEL_SPAWNER_O;
                case GIMMICK_BIG_OBJECT: return UNLOCK_LEVEL_BIG_OBJECT;
                case GIMMICK_CHAIN:      return UNLOCK_LEVEL_CHAIN;
                default:
                    Debug.LogWarning($"[GimmickManager] Unknown gimmick type: '{gimmickType}'.");
                    return int.MaxValue;
            }
        }

        #endregion
    }
}
