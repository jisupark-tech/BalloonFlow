using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Manages 6 booster types aligned with design spec (outgame.yaml §19-45).
    /// Pre-play boosters: BF_PRE_01 (500 coin), BF_PRE_02 (800 coin), BF_PRE_03 (20 gem).
    /// In-play boosters: BF_IN_01 (600 coin), BF_IN_02 (700 coin), BF_IN_03 (15 gem).
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Manager | Phase: 3
    /// Design ref: outgame.yaml §19-45, economy.yaml §booster_costs
    /// </remarks>
    public class BoosterManager : Singleton<BoosterManager>
    {
        #region Constants — Design-aligned booster IDs

        // Pre-play boosters
        public const string BF_PRE_01 = "bf_pre_01"; // Extra tray slot
        public const string BF_PRE_02 = "bf_pre_02"; // Board shuffle
        public const string BF_PRE_03 = "bf_pre_03"; // Color hint (gem)

        // In-play boosters
        public const string BF_IN_01 = "bf_in_01";   // Extra magazine
        public const string BF_IN_02 = "bf_in_02";   // Pop any color
        public const string BF_IN_03 = "bf_in_03";   // Remove color (gem)

        private const string PrefsKeyPrefix = "BalloonFlow_Booster_";

        #endregion

        #region Types

        private struct BoosterDef
        {
            public int cost;
            public bool isGemCost; // true = gem, false = coin
        }

        #endregion

        #region Fields

        private readonly Dictionary<string, BoosterDef> _boosterDefs = new Dictionary<string, BoosterDef>
        {
            { BF_PRE_01, new BoosterDef { cost = 500,  isGemCost = false } },
            { BF_PRE_02, new BoosterDef { cost = 800,  isGemCost = false } },
            { BF_PRE_03, new BoosterDef { cost = 20,   isGemCost = true  } },
            { BF_IN_01,  new BoosterDef { cost = 600,  isGemCost = false } },
            { BF_IN_02,  new BoosterDef { cost = 700,  isGemCost = false } },
            { BF_IN_03,  new BoosterDef { cost = 15,   isGemCost = true  } }
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
        /// Attempts to purchase one booster. Coin boosters use CurrencyManager,
        /// gem boosters use GemManager.
        /// </summary>
        public bool PurchaseBooster(string boosterType)
        {
            if (!IsValidType(boosterType))
            {
                Debug.LogWarning($"[BoosterManager] PurchaseBooster — unknown type: {boosterType}");
                return false;
            }

            var def = _boosterDefs[boosterType];

            if (def.isGemCost)
            {
                if (!GemManager.HasInstance)
                {
                    Debug.LogWarning("[BoosterManager] GemManager not available.");
                    return false;
                }

                if (!GemManager.Instance.SpendGems(def.cost, GemManager.GemSink.PremiumBooster))
                {
                    Debug.LogWarning($"[BoosterManager] Not enough gems for {boosterType} (needs {def.cost}).");
                    return false;
                }
            }
            else
            {
                if (!CurrencyManager.HasInstance)
                {
                    Debug.LogWarning("[BoosterManager] CurrencyManager not available.");
                    return false;
                }

                CurrencyManager.CoinSink sink = boosterType switch
                {
                    BF_PRE_01 => CurrencyManager.CoinSink.BoosterTrayAdd,
                    BF_PRE_02 => CurrencyManager.CoinSink.BoosterShuffle,
                    BF_IN_01  => CurrencyManager.CoinSink.BoosterHand,
                    BF_IN_02  => CurrencyManager.CoinSink.BoosterColorRemove,
                    _         => CurrencyManager.CoinSink.Other
                };

                if (!CurrencyManager.Instance.SpendCoins(def.cost, sink))
                {
                    Debug.LogWarning($"[BoosterManager] Not enough coins for {boosterType} (needs {def.cost}).");
                    return false;
                }
            }

            AddBooster(boosterType, 1);
            Debug.Log($"[BoosterManager] Purchased {boosterType} for {def.cost} {(def.isGemCost ? "gems" : "coins")}.");
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
        /// Returns true if the booster costs gems (premium), false if coins.
        /// </summary>
        public bool IsGemBooster(string boosterType)
        {
            return _boosterDefs.TryGetValue(boosterType, out var def) && def.isGemCost;
        }

        /// <summary>
        /// Returns true if the booster is a pre-play type (used before level starts).
        /// </summary>
        public bool IsPrePlayBooster(string boosterType)
        {
            return boosterType == BF_PRE_01 || boosterType == BF_PRE_02 || boosterType == BF_PRE_03;
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
