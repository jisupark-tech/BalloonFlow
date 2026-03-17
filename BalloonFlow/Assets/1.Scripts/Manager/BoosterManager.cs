using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Manages 3 in-play booster types (all coin-based).
    /// Design ref: 아웃게임디렉션 (2026-03-17) §부스터
    ///   Hand/Select Tool (1900 coin, Lv.9) — 큐에서 원하는 보관함 선택 배치
    ///   Shuffle (1500 coin, Lv.12) — 큐 보관함 순서 랜덤 셔플
    ///   Color Remove (2900 coin, Lv.15) — 필드+레일에서 지정 색상 전체 제거
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Manager | Phase: 3
    /// </remarks>
    public class BoosterManager : Singleton<BoosterManager>
    {
        #region Constants — Design-aligned booster IDs

        public const string SELECT_TOOL  = "select_tool";    // 큐에서 원하는 보관함 선택
        public const string SHUFFLE      = "shuffle";        // 큐 보관함 순서 랜덤 셔플
        public const string COLOR_REMOVE = "color_remove";   // 필드+레일 지정 색상 전체 제거

        private const string PrefsKeyPrefix = "BalloonFlow_Booster_";

        #endregion

        #region Types

        private struct BoosterDef
        {
            public int cost;       // all coin-based (v1.0 — no gems)
            public int unlockLevel; // level at which this booster becomes available
        }

        #endregion

        #region Fields

        private readonly Dictionary<string, BoosterDef> _boosterDefs = new Dictionary<string, BoosterDef>
        {
            { SELECT_TOOL,  new BoosterDef { cost = 1900, unlockLevel = 9 } },
            { SHUFFLE,      new BoosterDef { cost = 1500, unlockLevel = 12 } },
            { COLOR_REMOVE, new BoosterDef { cost = 2900, unlockLevel = 15 } }
        };

        private readonly Dictionary<string, int> _inventory = new Dictionary<string, int>();

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            LoadInventory();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Attempts to use one booster of the given type.
        /// Returns false if inventory is empty or gameplay is not active.
        /// </summary>
        public bool UseBooster(string boosterType)
        {
            if (!IsValidType(boosterType))
            {
                Debug.LogWarning($"[BoosterManager] Unknown booster type: {boosterType}");
                return false;
            }

            if (!BoardStateManager.HasInstance ||
                BoardStateManager.Instance.GetBoardState() != BoardState.Playing)
            {
                Debug.LogWarning("[BoosterManager] UseBooster called outside gameplay.");
                return false;
            }

            if (GetBoosterCount(boosterType) <= 0)
            {
                Debug.LogWarning($"[BoosterManager] No inventory for: {boosterType}");
                return false;
            }

            _inventory[boosterType]--;
            SaveInventory(boosterType);

            EventBus.Publish(new OnBoosterUsed { boosterType = boosterType });
            Debug.Log($"[BoosterManager] Used {boosterType}. Remaining: {_inventory[boosterType]}");
            return true;
        }

        /// <summary>
        /// Returns the current inventory count for the given booster type.
        /// </summary>
        public int GetBoosterCount(string boosterType)
        {
            if (!IsValidType(boosterType)) return 0;
            return _inventory.TryGetValue(boosterType, out int count) ? count : 0;
        }

        /// <summary>
        /// Directly adds boosters to inventory (e.g., from rewards or bundles).
        /// </summary>
        public void AddBooster(string boosterType, int count)
        {
            if (!IsValidType(boosterType))
            {
                Debug.LogWarning($"[BoosterManager] AddBooster — unknown type: {boosterType}");
                return;
            }

            if (count <= 0) return;

            if (!_inventory.ContainsKey(boosterType)) _inventory[boosterType] = 0;
            _inventory[boosterType] += count;
            SaveInventory(boosterType);
            Debug.Log($"[BoosterManager] Added {count}x {boosterType}. Total: {_inventory[boosterType]}");
        }

        /// <summary>
        /// Attempts to purchase one booster using coins.
        /// All boosters are coin-based in v1.0 (no gems).
        /// </summary>
        public bool PurchaseBooster(string boosterType)
        {
            if (!IsValidType(boosterType))
            {
                Debug.LogWarning($"[BoosterManager] PurchaseBooster — unknown type: {boosterType}");
                return false;
            }

            if (!IsBoosterUnlocked(boosterType))
            {
                Debug.LogWarning($"[BoosterManager] Booster {boosterType} not yet unlocked.");
                return false;
            }

            var def = _boosterDefs[boosterType];

            if (!CurrencyManager.HasInstance)
            {
                Debug.LogWarning("[BoosterManager] CurrencyManager not available.");
                return false;
            }

            CurrencyManager.CoinSink sink = boosterType switch
            {
                SELECT_TOOL  => CurrencyManager.CoinSink.BoosterSelectTool,
                SHUFFLE      => CurrencyManager.CoinSink.BoosterShuffle,
                COLOR_REMOVE => CurrencyManager.CoinSink.BoosterColorRemove,
                _            => CurrencyManager.CoinSink.Other
            };

            if (!CurrencyManager.Instance.SpendCoins(def.cost, sink))
            {
                Debug.LogWarning($"[BoosterManager] Not enough coins for {boosterType} (needs {def.cost}).");
                return false;
            }

            AddBooster(boosterType, 1);
            Debug.Log($"[BoosterManager] Purchased {boosterType} for {def.cost} coins.");
            return true;
        }

        /// <summary>
        /// Returns the cost of the given booster type.
        /// </summary>
        public int GetBoosterPrice(string boosterType)
        {
            return _boosterDefs.TryGetValue(boosterType, out var def) ? def.cost : 0;
        }

        /// <summary>
        /// Returns true if the booster is unlocked based on player's highest completed level.
        /// Design: Select Tool Lv.9, Shuffle Lv.12, Color Remove Lv.15.
        /// </summary>
        public bool IsBoosterUnlocked(string boosterType)
        {
            if (!_boosterDefs.TryGetValue(boosterType, out var def)) return false;

            int highestLevel = 0;
            if (LevelManager.HasInstance)
            {
                highestLevel = LevelManager.Instance.GetHighestCompletedLevel();
            }

            return highestLevel >= def.unlockLevel;
        }

        /// <summary>
        /// Returns all booster type IDs.
        /// </summary>
        public IEnumerable<string> GetAllBoosterTypes()
        {
            return _boosterDefs.Keys;
        }

        #endregion

        #region Private Methods

        private bool IsValidType(string boosterType)
        {
            return !string.IsNullOrEmpty(boosterType) && _boosterDefs.ContainsKey(boosterType);
        }

        private void LoadInventory()
        {
            foreach (var key in _boosterDefs.Keys)
            {
                _inventory[key] = PlayerPrefs.GetInt(PrefsKeyPrefix + key, 0);
            }
        }

        private void SaveInventory(string boosterType)
        {
            if (_inventory.TryGetValue(boosterType, out int count))
            {
                PlayerPrefs.SetInt(PrefsKeyPrefix + boosterType, count);
                PlayerPrefs.Save();
            }
        }

        #endregion
    }
}
