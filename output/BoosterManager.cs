using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Manages 4 in-play booster types (all coin-based).
    /// Design ref: 아웃게임디렉션 §부스터
    ///   Extra Tray (300 coin, Lv.10) — +1 rail tray slot
    ///   Select Tool (1900 coin, Lv.12) — pick any queue container
    ///   Shuffle (1500 coin, Lv.15) — randomize queue order
    ///   Color Remove (2900 coin, Lv.18) — remove all of one color
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Manager | Phase: 3
    /// </remarks>
    public class BoosterManager : Singleton<BoosterManager>
    {
        #region Constants — Design-aligned booster IDs

        public const string EXTRA_TRAY   = "extra_tray";     // +1 rail tray slot
        public const string SELECT_TOOL  = "select_tool";    // pick any container
        public const string SHUFFLE      = "shuffle";        // randomize queue order
        public const string COLOR_REMOVE = "color_remove";   // remove all of one color

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
            { EXTRA_TRAY,   new BoosterDef { cost = 300,  unlockLevel = 10 } },
            { SELECT_TOOL,  new BoosterDef { cost = 1900, unlockLevel = 12 } },
            { SHUFFLE,      new BoosterDef { cost = 1500, unlockLevel = 15 } },
            { COLOR_REMOVE, new BoosterDef { cost = 2900, unlockLevel = 18 } }
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
                EXTRA_TRAY   => CurrencyManager.CoinSink.BoosterExtraTray,
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
        /// Design: Extra Tray Lv.10, Select Tool Lv.12, Shuffle Lv.15, Color Remove Lv.18.
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
