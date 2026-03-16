using System;
using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Data container for a single holder (magazine slot) in the queue.
    /// </summary>
    [System.Serializable]
    public class HolderData
    {
        public int holderId;
        public int color;
        public int magazineCount;
        public int column;            // queue column (0..queueColumns-1)
        public bool isDeploying;      // currently at rail deploying darts
        public bool isWaiting;        // waiting behind a deploying holder (same column)
        public bool isMovingToRail;   // in transit from queue to rail
        public bool isConsumed;       // magazine=0, removed
    }

    /// <summary>
    /// Manages all holders in a column-based queue system.
    /// Rail Overflow mode: holders are queue items that deploy darts onto rail slots.
    /// Per column: max 1 deploying + 1 waiting. 3rd touch = bounce back (reject).
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Manager | Phase: 1
    /// DB Reference: Generated from Rail Overflow spec — column queue system
    /// </remarks>
    public class HolderManager : SceneSingleton<HolderManager>
    {
        #region Constants

        /// <summary>Maximum queue columns (matches spec: 5).</summary>
        private const int MAX_QUEUE_COLUMNS = 5;

        #endregion

        #region Fields

        private readonly List<HolderData> _holders = new List<HolderData>();
        private int _nextHolderId;
        private int _queueColumns = 5;

        // Per-column tracking: which holder is deploying, which is waiting
        private readonly int[] _deployingHolderId = new int[MAX_QUEUE_COLUMNS];
        private readonly int[] _waitingHolderId = new int[MAX_QUEUE_COLUMNS];

        #endregion

        #region Properties

        public int QueueColumns => _queueColumns;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            ResetColumnTracking();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnHolderTapped>(HandleHolderTapped);
            EventBus.Subscribe<OnHolderDeploymentDone>(HandleDeploymentDone);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnHolderTapped>(HandleHolderTapped);
            EventBus.Unsubscribe<OnHolderDeploymentDone>(HandleDeploymentDone);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initializes holders from level data. Call when a level is loaded.
        /// Holders are organized by column. Each entry is (color, magazineCount, column).
        /// </summary>
        public void InitializeHolders(List<(int color, int magazineCount)> holderSetup, int queueColumns = 5)
        {
            _holders.Clear();
            _nextHolderId = 0;
            _queueColumns = Mathf.Clamp(queueColumns, 1, MAX_QUEUE_COLUMNS);
            ResetColumnTracking();

            if (holderSetup == null || holderSetup.Count == 0)
            {
                Debug.LogWarning("[HolderManager] No holder setup data provided.");
                return;
            }

            // Distribute holders across columns round-robin
            for (int i = 0; i < holderSetup.Count; i++)
            {
                var setup = holderSetup[i];
                int col = i % _queueColumns;

                var holder = new HolderData
                {
                    holderId = _nextHolderId++,
                    color = setup.color,
                    magazineCount = setup.magazineCount,
                    column = col,
                    isDeploying = false,
                    isWaiting = false,
                    isMovingToRail = false,
                    isConsumed = false
                };
                _holders.Add(holder);
            }
        }

        /// <summary>
        /// Initializes holders with explicit column assignments.
        /// </summary>
        public void InitializeHoldersWithColumns(List<(int color, int magazineCount, int column)> holderSetup, int queueColumns = 5)
        {
            _holders.Clear();
            _nextHolderId = 0;
            _queueColumns = Mathf.Clamp(queueColumns, 1, MAX_QUEUE_COLUMNS);
            ResetColumnTracking();

            if (holderSetup == null || holderSetup.Count == 0) return;

            foreach (var setup in holderSetup)
            {
                var holder = new HolderData
                {
                    holderId = _nextHolderId++,
                    color = setup.color,
                    magazineCount = setup.magazineCount,
                    column = Mathf.Clamp(setup.column, 0, _queueColumns - 1),
                    isDeploying = false,
                    isWaiting = false,
                    isMovingToRail = false,
                    isConsumed = false
                };
                _holders.Add(holder);
            }
        }

        /// <summary>
        /// Returns all holders.
        /// </summary>
        public HolderData[] GetHolders()
        {
            return _holders.ToArray();
        }

        /// <summary>
        /// Attempts to select a holder by ID and deploy it.
        /// Returns false if:
        /// - Holder not found / already deployed / consumed
        /// - Column already has deploying+waiting (3rd touch → bounce)
        /// </summary>
        public bool SelectHolder(int holderId)
        {
            HolderData holder = FindHolder(holderId);
            if (holder == null || holder.isDeploying || holder.isWaiting || holder.isMovingToRail || holder.isConsumed)
            {
                return false;
            }

            if (holder.magazineCount <= 0)
            {
                return false;
            }

            int col = holder.column;

            // Check column state
            if (_deployingHolderId[col] >= 0 && _waitingHolderId[col] >= 0)
            {
                // Column full (deploying + waiting). 3rd touch = bounce back
                EventBus.Publish(new OnHolderWarning
                {
                    waitingCount = 2,
                    maxSlots = 2,
                    isDanger = true
                });
                return false;
            }

            if (_deployingHolderId[col] >= 0)
            {
                // Already deploying — this holder becomes the waiting holder
                holder.isWaiting = true;
                _waitingHolderId[col] = holder.holderId;
            }
            else
            {
                // No deployer — this holder starts deploying immediately
                holder.isDeploying = true;
                holder.isMovingToRail = true;
                _deployingHolderId[col] = holder.holderId;
            }

            EventBus.Publish(new OnHolderSelected
            {
                holderId = holder.holderId,
                color = holder.color,
                magazineCount = holder.magazineCount
            });

            return true;
        }

        /// <summary>
        /// Called when a holder reaches the rail and starts deploying darts.
        /// </summary>
        public void ConfirmOnRail(int holderId)
        {
            HolderData holder = FindHolder(holderId);
            if (holder != null)
            {
                holder.isMovingToRail = false;
                holder.isDeploying = true;
            }
        }

        /// <summary>
        /// Consumes one magazine from the specified holder.
        /// Returns the remaining magazine count.
        /// </summary>
        public int ConsumeMagazine(int holderId)
        {
            HolderData holder = FindHolder(holderId);
            if (holder == null || holder.magazineCount <= 0) return 0;

            holder.magazineCount--;

            if (holder.magazineCount <= 0)
            {
                EventBus.Publish(new OnMagazineEmpty { holderId = holderId });
            }

            return holder.magazineCount;
        }

        /// <summary>
        /// Reverts a holder's deploy state (e.g. when deploy was blocked).
        /// </summary>
        public void UndoDeploy(int holderId)
        {
            HolderData holder = FindHolder(holderId);
            if (holder == null) return;

            int col = holder.column;
            holder.isDeploying = false;
            holder.isWaiting = false;
            holder.isMovingToRail = false;

            if (_deployingHolderId[col] == holderId) _deployingHolderId[col] = -1;
            if (_waitingHolderId[col] == holderId) _waitingHolderId[col] = -1;
        }

        /// <summary>
        /// Returns holders in a specific column, ordered by queue position.
        /// </summary>
        public List<HolderData> GetColumnHolders(int column)
        {
            var result = new List<HolderData>();
            for (int i = 0; i < _holders.Count; i++)
            {
                if (_holders[i].column == column && !_holders[i].isConsumed)
                {
                    result.Add(_holders[i]);
                }
            }
            return result;
        }

        /// <summary>
        /// Returns true when all holders have zero magazine and none are deploying.
        /// </summary>
        public bool AreAllHoldersEmpty()
        {
            for (int i = 0; i < _holders.Count; i++)
            {
                if (!_holders[i].isConsumed && (_holders[i].magazineCount > 0 || _holders[i].isDeploying))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Returns total holder count (active + consumed).
        /// </summary>
        public int GetHolderCount()
        {
            return _holders.Count;
        }

        /// <summary>
        /// Returns the number of holders waiting in the queue (not deploying, not consumed).
        /// </summary>
        public int GetWaitingHolderCount()
        {
            int count = 0;
            for (int i = 0; i < _holders.Count; i++)
            {
                if (!_holders[i].isDeploying && !_holders[i].isWaiting && !_holders[i].isMovingToRail && !_holders[i].isConsumed)
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
        /// Adds a new holder to a column (used by gimmicks like Spawner).
        /// Returns the new holder's ID.
        /// </summary>
        public int AddHolder(int color, int magazineCount, int column = -1)
        {
            if (column < 0) column = FindShortestColumn();

            var holder = new HolderData
            {
                holderId = _nextHolderId++,
                color = color,
                magazineCount = magazineCount,
                column = Mathf.Clamp(column, 0, _queueColumns - 1),
                isDeploying = false,
                isWaiting = false,
                isMovingToRail = false,
                isConsumed = false
            };
            _holders.Add(holder);
            return holder.holderId;
        }

        /// <summary>
        /// Resets all holder state for a new level.
        /// </summary>
        public void ResetAll()
        {
            _holders.Clear();
            _nextHolderId = 0;
            ResetColumnTracking();
        }

        #endregion

        #region Private Methods

        private HolderData FindHolder(int holderId)
        {
            for (int i = 0; i < _holders.Count; i++)
            {
                if (_holders[i].holderId == holderId)
                    return _holders[i];
            }
            return null;
        }

        private void ResetColumnTracking()
        {
            for (int i = 0; i < MAX_QUEUE_COLUMNS; i++)
            {
                _deployingHolderId[i] = -1;
                _waitingHolderId[i] = -1;
            }
        }

        private int FindShortestColumn()
        {
            int minCount = int.MaxValue;
            int bestCol = 0;
            for (int col = 0; col < _queueColumns; col++)
            {
                int count = 0;
                for (int i = 0; i < _holders.Count; i++)
                {
                    if (_holders[i].column == col && !_holders[i].isConsumed)
                        count++;
                }
                if (count < minCount)
                {
                    minCount = count;
                    bestCol = col;
                }
            }
            return bestCol;
        }

        private void HandleHolderTapped(OnHolderTapped evt)
        {
            SelectHolder(evt.holderId);
        }

        private void HandleDeploymentDone(OnHolderDeploymentDone evt)
        {
            // Deploying holder finished (magazine=0)
            HolderData holder = FindHolder(evt.holderId);
            if (holder != null)
            {
                holder.isDeploying = false;
                holder.isConsumed = true;
            }

            int col = evt.column;
            _deployingHolderId[col] = -1;

            // Promote waiting holder to deploying
            if (_waitingHolderId[col] >= 0)
            {
                int waitId = _waitingHolderId[col];
                _waitingHolderId[col] = -1;

                HolderData waitHolder = FindHolder(waitId);
                if (waitHolder != null && !waitHolder.isConsumed)
                {
                    waitHolder.isWaiting = false;
                    waitHolder.isDeploying = true;
                    waitHolder.isMovingToRail = true;
                    _deployingHolderId[col] = waitId;

                    EventBus.Publish(new OnHolderSelected
                    {
                        holderId = waitHolder.holderId,
                        color = waitHolder.color,
                        magazineCount = waitHolder.magazineCount
                    });
                }
            }

            // Check if all holders are consumed
            if (AreAllHoldersEmpty())
            {
                EventBus.Publish(new OnAllHoldersEmpty());
            }
        }

        #endregion
    }
}
