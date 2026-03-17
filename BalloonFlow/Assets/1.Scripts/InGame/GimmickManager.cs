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
    public class GimmickManager : SceneSingleton<GimmickManager>
    {
        #region Constants

        // Global level IDs at which each gimmick type is first introduced
        // 정본: gimmick_spec.yaml — 5종 기믹 도입 레벨
        private const int UNLOCK_LEVEL_HIDDEN        = 11;   // PKG1 pos11
        private const int UNLOCK_LEVEL_SPAWNER_T     = 21;   // PKG2 pos1
        private const int UNLOCK_LEVEL_SPAWNER_O     = 31;   // PKG2 pos11
        private const int UNLOCK_LEVEL_PINATA        = 41;   // PKG3 pos1 (Big Object)
        private const int UNLOCK_LEVEL_CHAIN         = 61;   // PKG4 pos1
        // 미래 확장 기믹 (기획서 미정의 — 높은 레벨에 배치)
        private const int UNLOCK_LEVEL_PIN           = 81;
        private const int UNLOCK_LEVEL_LOCK_KEY      = 101;
        private const int UNLOCK_LEVEL_SURPRISE      = 121;
        private const int UNLOCK_LEVEL_WALL          = 141;
        private const int UNLOCK_LEVEL_PINATA_BOX    = 161;
        private const int UNLOCK_LEVEL_ICE           = 181;
        private const int UNLOCK_LEVEL_FROZEN_DART   = 201;
        private const int UNLOCK_LEVEL_COLOR_CURTAIN = 221;

        // String identifiers that match LevelConfig.gimmickTypes values
        public const string GIMMICK_HIDDEN        = "Hidden";
        public const string GIMMICK_CHAIN         = "Chain";
        public const string GIMMICK_PINATA        = "Pinata";
        public const string GIMMICK_SPAWNER_T     = "Spawner_T";
        public const string GIMMICK_PIN           = "Pin";
        public const string GIMMICK_LOCK_KEY      = "Lock_Key";
        public const string GIMMICK_SURPRISE      = "Surprise";
        public const string GIMMICK_WALL          = "Wall";
        public const string GIMMICK_SPAWNER_O     = "Spawner_O";
        public const string GIMMICK_PINATA_BOX    = "Pinata_Box";
        public const string GIMMICK_ICE           = "Ice";
        public const string GIMMICK_FROZEN_DART   = "Frozen_Dart";
        public const string GIMMICK_COLOR_CURTAIN = "Color_Curtain";

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
                    return "풍선 색상이 숨겨져 있어 인접 팝으로 공개됩니다.";
                case GIMMICK_CHAIN:
                    return "연결된 컨테이너가 순차적으로 배치됩니다.";
                case GIMMICK_PINATA:
                    return "여러 번 맞춰야 터지는 단단한 풍선입니다.";
                case GIMMICK_SPAWNER_T:
                    return "투명 스포너 — 다음 생성 색상이 보입니다.";
                case GIMMICK_PIN:
                    return "다트를 차단하는 장애물. 인접 팝으로 제거.";
                case GIMMICK_LOCK_KEY:
                    return "Key 풍선을 먼저 터뜨려야 Lock 풍선 해제.";
                case GIMMICK_SURPRISE:
                    return "팝 시 랜덤 색상으로 변경되는 깜짝 풍선.";
                case GIMMICK_WALL:
                    return "파괴 불가 벽. 다트를 완전 차단.";
                case GIMMICK_SPAWNER_O:
                    return "불투명 스포너 — 다음 생성 색상이 숨겨집니다.";
                case GIMMICK_PINATA_BOX:
                    return "다중 히트 박스. 파괴 시 보상 드롭.";
                case GIMMICK_ICE:
                    return "얼음 풍선 — 인접 팝으로 해동 후 파괴 가능.";
                case GIMMICK_FROZEN_DART:
                    return "동결 다트 — 첫 N발이 발사되지 않음.";
                case GIMMICK_COLOR_CURTAIN:
                    return "특정 색상 다트로만 제거 가능한 컬러 커튼.";
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
                case GIMMICK_HIDDEN:        return UNLOCK_LEVEL_HIDDEN;
                case GIMMICK_CHAIN:         return UNLOCK_LEVEL_CHAIN;
                case GIMMICK_PINATA:        return UNLOCK_LEVEL_PINATA;
                case GIMMICK_SPAWNER_T:     return UNLOCK_LEVEL_SPAWNER_T;
                case GIMMICK_PIN:           return UNLOCK_LEVEL_PIN;
                case GIMMICK_LOCK_KEY:      return UNLOCK_LEVEL_LOCK_KEY;
                case GIMMICK_SURPRISE:      return UNLOCK_LEVEL_SURPRISE;
                case GIMMICK_WALL:          return UNLOCK_LEVEL_WALL;
                case GIMMICK_SPAWNER_O:     return UNLOCK_LEVEL_SPAWNER_O;
                case GIMMICK_PINATA_BOX:    return UNLOCK_LEVEL_PINATA_BOX;
                case GIMMICK_ICE:           return UNLOCK_LEVEL_ICE;
                case GIMMICK_FROZEN_DART:   return UNLOCK_LEVEL_FROZEN_DART;
                case GIMMICK_COLOR_CURTAIN: return UNLOCK_LEVEL_COLOR_CURTAIN;
                default:
                    Debug.LogWarning($"[GimmickManager] Unknown gimmick type: '{gimmickType}'.");
                    return int.MaxValue;
            }
        }

        #endregion
    }
}
