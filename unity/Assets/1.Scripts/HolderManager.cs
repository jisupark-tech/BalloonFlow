using System;
using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Data container for a single holder (magazine slot).
    /// </summary>
    [System.Serializable]
    public class HolderData
    {
        public int holderId;
        public int color;
        public int magazineCount;
        public bool isDeployed;
        public bool isOnRail;
    }

    /// <summary>
    /// Manages all holder slots. Player taps a holder to deploy its darts
    /// onto the circular rail. Holders return after a rail loop if they
    /// still have remaining magazine. Overflow (>5 waiting) triggers fail.
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Manager | Phase: 1
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow (ingame_holder_dart_pop)
    /// </remarks>
    public class HolderManager : Singleton<HolderManager>
    {
        #region Constants

        private const int MAX_HOLDER_SLOTS = 5;

        #endregion

        #region Fields

        private readonly List<HolderData> _holders = new List<HolderData>();
        private HolderData _currentHolder;
        private int _nextHolderId;

        #endregion

        #region Properties

        /// <summary>
        /// Maximum number of holders allowed in the waiting area before overflow.
        /// </summary>
        public int MaxHolderSlots => MAX_HOLDER_SLOTS;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            // Initialization handled by InitializeHolders() called from level loader
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnHolderTapped>(HandleHolderTapped);
            EventBus.Subscribe<OnRailLoopComplete>(HandleRailLoopComplete);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnHolderTapped>(HandleHolderTapped);
            EventBus.Unsubscribe<OnRailLoopComplete>(HandleRailLoopComplete);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initializes holders from level data. Call when a level is loaded.
        /// Each entry is (color, magazineCount).
        /// </summary>
        public void InitializeHolders(List<(int color, int magazineCount)> holderSetup)
        {
            _holders.Clear();
            _currentHolder = null;
            _nextHolderId = 0;

            if (holderSetup == null || holderSetup.Count == 0)
            {
                Debug.LogWarning("[HolderManager] No holder setup data provided.");
                return;
            }

            foreach (var setup in holderSetup)
            {
                var holder = new HolderData
                {
                    holderId = _nextHolderId++,
                    color = setup.color,
                    magazineCount = setup.magazineCount,
                    isDeployed = false,
                    isOnRail = false
                };
                _holders.Add(holder);
            }

            // Check initial overflow
            int waitingCount = GetWaitingHolderCount();
            if (waitingCount > MAX_HOLDER_SLOTS)
            {
                Debug.LogWarning($"[HolderManager] Initial holder count {waitingCount} exceeds max {MAX_HOLDER_SLOTS}.");
                PublishOverflow(waitingCount);
            }
        }

        /// <summary>
        /// Returns all holders (deployed and waiting).
        /// </summary>
        public HolderData[] GetHolders()
        {
            return _holders.ToArray();
        }

        /// <summary>
        /// Attempts to select a holder by ID and deploy it onto the rail.
        /// Returns false if the holder is not found, already deployed, or empty.
        /// </summary>
        public bool SelectHolder(int holderId)
        {
            HolderData holder = FindHolder(holderId);
            if (holder == null)
            {
                Debug.LogWarning($"[HolderManager] Holder {holderId} not found.");
                return false;
            }

            if (holder.isDeployed || holder.isOnRail)
            {
                Debug.LogWarning($"[HolderManager] Holder {holderId} is already deployed.");
                return false;
            }

            if (holder.magazineCount <= 0)
            {
                Debug.LogWarning($"[HolderManager] Holder {holderId} has no magazine.");
                return false;
            }

            holder.isDeployed = true;
            holder.isOnRail = true;
            _currentHolder = holder;

            EventBus.Publish(new OnHolderSelected
            {
                holderId = holder.holderId,
                color = holder.color,
                magazineCount = holder.magazineCount
            });

            return true;
        }

        /// <summary>
        /// Returns the currently active (deployed) holder, or null if none.
        /// </summary>
        public HolderData GetCurrentHolder()
        {
            return _currentHolder;
        }

        /// <summary>
        /// Returns total holder count (waiting + deployed).
        /// </summary>
        public int GetHolderCount()
        {
            return _holders.Count;
        }

        /// <summary>
        /// Returns the number of holders currently waiting (not deployed).
        /// </summary>
        public int GetWaitingHolderCount()
        {
            int count = 0;
            for (int i = 0; i < _holders.Count; i++)
            {
                if (!_holders[i].isDeployed && !_holders[i].isOnRail)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Gets magazine count for a specific holder.
        /// </summary>
        public int GetMagazineCount(int holderId)
        {
            HolderData holder = FindHolder(holderId);
            return holder?.magazineCount ?? 0;
        }

        /// <summary>
        /// Whether a specific holder has zero magazine.
        /// </summary>
        public bool IsHolderEmpty(int holderId)
        {
            HolderData holder = FindHolder(holderId);
            return holder == null || holder.magazineCount <= 0;
        }

        /// <summary>
        /// Consumes one magazine from the specified holder.
        /// Returns the remaining magazine count.
        /// Publishes OnMagazineEmpty if magazine reaches zero.
        /// </summary>
        public int ConsumeMagazine(int holderId)
        {
            HolderData holder = FindHolder(holderId);
            if (holder == null)
            {
                Debug.LogWarning($"[HolderManager] ConsumeMagazine: Holder {holderId} not found.");
                return 0;
            }

            if (holder.magazineCount <= 0)
            {
                return 0;
            }

            holder.magazineCount--;

            if (holder.magazineCount <= 0)
            {
                EventBus.Publish(new OnMagazineEmpty { holderId = holderId });
            }

            return holder.magazineCount;
        }

        /// <summary>
        /// Returns a holder to the waiting area after a rail loop with remaining magazine.
        /// Checks overflow condition after return.
        /// </summary>
        public void ReturnToHolder(int holderId, int remainingMagazine)
        {
            HolderData holder = FindHolder(holderId);
            if (holder == null)
            {
                Debug.LogWarning($"[HolderManager] ReturnToHolder: Holder {holderId} not found.");
                return;
            }

            holder.magazineCount = remainingMagazine;
            holder.isDeployed = false;
            holder.isOnRail = false;

            if (_currentHolder != null && _currentHolder.holderId == holderId)
            {
                _currentHolder = null;
            }

            EventBus.Publish(new OnHolderReturned
            {
                holderId = holderId,
                remainingMagazine = remainingMagazine
            });

            // Check overflow after return
            int waitingCount = GetWaitingHolderCount();
            if (waitingCount > MAX_HOLDER_SLOTS)
            {
                PublishOverflow(waitingCount);
            }
        }

        /// <summary>
        /// Removes a holder from the list (magazine fully consumed on rail).
        /// </summary>
        public void RemoveHolder(int holderId)
        {
            for (int i = _holders.Count - 1; i >= 0; i--)
            {
                if (_holders[i].holderId == holderId)
                {
                    if (_currentHolder != null && _currentHolder.holderId == holderId)
                    {
                        _currentHolder = null;
                    }
                    _holders.RemoveAt(i);
                    break;
                }
            }

            if (AreAllHoldersEmpty())
            {
                EventBus.Publish(new OnAllHoldersEmpty());
            }
        }

        /// <summary>
        /// Returns true when all holders have zero magazine and none are on the rail.
        /// </summary>
        public bool AreAllHoldersEmpty()
        {
            if (_holders.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < _holders.Count; i++)
            {
                if (_holders[i].magazineCount > 0 || _holders[i].isOnRail)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Adds a new holder to the waiting area (used by level spawning / gimmicks).
        /// Returns the new holder's ID. Checks overflow after adding.
        /// </summary>
        public int AddHolder(int color, int magazineCount)
        {
            var holder = new HolderData
            {
                holderId = _nextHolderId++,
                color = color,
                magazineCount = magazineCount,
                isDeployed = false,
                isOnRail = false
            };
            _holders.Add(holder);

            int waitingCount = GetWaitingHolderCount();
            if (waitingCount > MAX_HOLDER_SLOTS)
            {
                PublishOverflow(waitingCount);
            }

            return holder.holderId;
        }

        /// <summary>
        /// Resets all holder state for a new level.
        /// </summary>
        public void ResetAll()
        {
            _holders.Clear();
            _currentHolder = null;
            _nextHolderId = 0;
        }

        #endregion

        #region Private Methods

        private HolderData FindHolder(int holderId)
        {
            for (int i = 0; i < _holders.Count; i++)
            {
                if (_holders[i].holderId == holderId)
                {
                    return _holders[i];
                }
            }
            return null;
        }

        private void PublishOverflow(int holderCount)
        {
            EventBus.Publish(new OnHolderOverflow { holderCount = holderCount });

            EventBus.Publish(new OnBoardFailed
            {
                levelId = -1,
                reason = $"Holder overflow: {holderCount} > {MAX_HOLDER_SLOTS}"
            });
        }

        private void HandleHolderTapped(OnHolderTapped evt)
        {
            SelectHolder(evt.holderId);
        }

        private void HandleRailLoopComplete(OnRailLoopComplete evt)
        {
            if (evt.remainingMagazine > 0)
            {
                // Magazine still has ammo — return to holder waiting area
                ReturnToHolder(evt.holderId, evt.remainingMagazine);
            }
            else
            {
                // Magazine fully consumed — remove from holder list
                RemoveHolder(evt.holderId);
            }
        }

        #endregion
    }
}
