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
        // 정본: BalloonFlow_기믹명세 (2026-03-17) — 13종 기믹 도입 레벨
        private const int UNLOCK_LEVEL_HIDDEN        = 11;   // PKG1 Lv.11
        private const int UNLOCK_LEVEL_CHAIN         = 21;   // PKG2 Lv.21
        private const int UNLOCK_LEVEL_PINATA        = 31;   // PKG2 Lv.31
        private const int UNLOCK_LEVEL_SPAWNER_T     = 41;   // PKG3 Lv.41
        private const int UNLOCK_LEVEL_PIN           = 61;   // PKG4 Lv.61
        private const int UNLOCK_LEVEL_LOCK_KEY      = 81;   // PKG5 Lv.81
        private const int UNLOCK_LEVEL_SURPRISE      = 101;  // PKG6 Lv.101
        private const int UNLOCK_LEVEL_WALL          = 121;  // PKG7 Lv.121
        private const int UNLOCK_LEVEL_SPAWNER_O     = 141;  // PKG8 Lv.141
        private const int UNLOCK_LEVEL_PINATA_BOX    = 161;  // PKG9 Lv.161
        private const int UNLOCK_LEVEL_ICE           = 201;  // PKG11 Lv.201
        private const int UNLOCK_LEVEL_FROZEN_DART   = 241;  // PKG13 Lv.241
        private const int UNLOCK_LEVEL_COLOR_CURTAIN = 281;  // PKG15 Lv.281

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
                    return "보관함 색상이 숨겨져 있어 터치 가능 상태가 될 때 공개됩니다.";
                case GIMMICK_CHAIN:
                    return "2~4개 보관함이 연결되어 순차적으로 배치됩니다.";
                case GIMMICK_PINATA:
                    return "1×1~6×6 크기. HP만큼 다트를 맞아야 파괴됩니다.";
                case GIMMICK_SPAWNER_T:
                    return "투명 스포너 — 소진 시 큐에 새 보관함을 생성합니다. 다음 색상이 보입니다.";
                case GIMMICK_PIN:
                    return "1×N 장애물. 같은 색 다트 직격으로만 1칸씩 점진 제거됩니다.";
                case GIMMICK_LOCK_KEY:
                    return "Key 풍선을 먼저 터뜨려야 Lock 풍선이 해제됩니다.";
                case GIMMICK_SURPRISE:
                    return "필드 풍선의 색상이 숨겨져 있어 인접 팝으로 공개됩니다.";
                case GIMMICK_WALL:
                    return "파괴 불가 벽. 다트를 완전 차단합니다.";
                case GIMMICK_SPAWNER_O:
                    return "불투명 스포너 — 소진 시 큐에 새 보관함을 생성합니다. 다음 색상이 숨겨집니다.";
                case GIMMICK_PINATA_BOX:
                    return "다중 셀 피냐타. 각 셀마다 HP를 가지며 파괴 시 보상 드롭.";
                case GIMMICK_ICE:
                    return "얼음 풍선 — 모든 풍선 팝에 의해 간접적으로 HP가 감소합니다.";
                case GIMMICK_FROZEN_DART:
                    return "동결 다트 — 레일의 첫 N발이 발사되지 않고 자리를 차지합니다.";
                case GIMMICK_COLOR_CURTAIN:
                    return "지정 색상 다트로만 간접 제거 가능한 컬러 커튼.";
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
