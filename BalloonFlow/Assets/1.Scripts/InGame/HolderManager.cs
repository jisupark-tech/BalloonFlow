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

        // ── 큐 기믹 상태 ──
        /// <summary>큐 기믹 타입. 빈 문자열 = 없음.</summary>
        public string queueGimmick = "";
        /// <summary>Hidden: 색상이 숨겨진 상태 (큐 앞줄 도달 시 공개).</summary>
        public bool isHidden;
        /// <summary>Frozen: 얼어있어 터치 불가 (인접 보관함 사용 시 해동).</summary>
        public bool isFrozen;
        /// <summary>Chain 그룹 ID. -1 = Chain 아님. 같은 ID끼리 연결 발동.</summary>
        public int chainGroupId = -1;
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

        /// <summary>Max active holders across all columns. Design ref: "최대 10개 활성".</summary>
        private const int MAX_ACTIVE_TOTAL = 10;

        // Magazine max per rail capacity tier.
        // Design ref: 레일초과_코어메카닉_명세 — "50→max15, 100→max30, 150/200→max50"
        private static readonly int[] MAG_CAP_TIERS      = { 50, 100, 150, 200 };
        private static readonly int[] MAG_CAP_MAX_VALUES  = { 15,  30,  50,  50 };

        #endregion

        #region Fields

        private readonly List<HolderData> _holders = new List<HolderData>();
        private int _nextHolderId;
        private int _queueColumns = 5;
        private int _magazineMax = 50; // current level's magazine cap (set by rail capacity)

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
        public void InitializeHolders(List<(int color, int magazineCount)> holderSetup, int queueColumns = 5, int railCapacity = 0)
        {
            _holders.Clear();
            _nextHolderId = 0;
            _queueColumns = Mathf.Clamp(queueColumns, 1, MAX_QUEUE_COLUMNS);
            _magazineMax = GetMagazineMaxForCapacity(railCapacity);
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
                    magazineCount = Mathf.Min(setup.magazineCount, _magazineMax),
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
        public void InitializeHoldersWithColumns(List<(int color, int magazineCount, int column)> holderSetup, int queueColumns = 5, int railCapacity = 0)
        {
            _holders.Clear();
            _nextHolderId = 0;
            _queueColumns = Mathf.Clamp(queueColumns, 1, MAX_QUEUE_COLUMNS);
            _magazineMax = GetMagazineMaxForCapacity(railCapacity);
            ResetColumnTracking();

            if (holderSetup == null || holderSetup.Count == 0) return;

            foreach (var setup in holderSetup)
            {
                var holder = new HolderData
                {
                    holderId = _nextHolderId++,
                    color = setup.color,
                    magazineCount = Mathf.Min(setup.magazineCount, _magazineMax),
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
        /// Initializes holders from LevelConfig.HolderSetup with queue gimmick support.
        /// </summary>
        public void InitializeHoldersFromConfig(HolderSetup[] holderSetups, int queueColumns = 5, int railCapacity = 0)
        {
            _holders.Clear();
            _nextHolderId = 0;
            _queueColumns = Mathf.Clamp(queueColumns, 1, MAX_QUEUE_COLUMNS);
            _magazineMax = GetMagazineMaxForCapacity(railCapacity);
            ResetColumnTracking();

            if (holderSetups == null || holderSetups.Length == 0) return;

            for (int i = 0; i < holderSetups.Length; i++)
            {
                var setup = holderSetups[i];
                int col = i % _queueColumns;

                string gimmick = setup.queueGimmick ?? "";
                bool hidden = gimmick == GimmickManager.GIMMICK_HIDDEN;
                bool frozen = gimmick == GimmickManager.GIMMICK_FROZEN_DART;

                var holder = new HolderData
                {
                    holderId = _nextHolderId++,
                    color = setup.color,
                    magazineCount = Mathf.Min(setup.magazineCount, _magazineMax),
                    column = col,
                    isDeploying = false,
                    isWaiting = false,
                    isMovingToRail = false,
                    isConsumed = false,
                    queueGimmick = gimmick,
                    isHidden = hidden,
                    isFrozen = frozen,
                    chainGroupId = setup.chainGroupId
                };
                _holders.Add(holder);
            }
        }

        /// <summary>
        /// Hidden 보관함의 색상 공개. 큐 앞줄 도달 시 호출.
        /// </summary>
        public void RevealHiddenHolder(int holderId)
        {
            HolderData holder = FindHolder(holderId);
            if (holder == null || !holder.isHidden) return;
            holder.isHidden = false;
            EventBus.Publish(new OnHolderRevealed { holderId = holderId });
            Debug.Log($"[HolderManager] Hidden holder {holderId} revealed — color {holder.color}");
        }

        /// <summary>
        /// Frozen 보관함 해동. 인접 보관함(같은 열) 사용 시 호출.
        /// </summary>
        public void ThawFrozenHolder(int holderId)
        {
            HolderData holder = FindHolder(holderId);
            if (holder == null || !holder.isFrozen) return;
            holder.isFrozen = false;
            Debug.Log($"[HolderManager] Frozen holder {holderId} thawed");
            EventBus.Publish(new OnHolderThawed { holderId = holderId });
        }

        /// <summary>
        /// Chain 그룹에 속한 모든 보관함 ID 반환.
        /// </summary>
        public List<int> GetChainGroup(int chainGroupId)
        {
            var result = new List<int>();
            if (chainGroupId < 0) return result;
            for (int i = 0; i < _holders.Count; i++)
            {
                if (_holders[i].chainGroupId == chainGroupId && !_holders[i].isConsumed)
                    result.Add(_holders[i].holderId);
            }
            return result;
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

            // 큐 기믹 체크: Hidden 상태면 터치 불가 (앞줄 도달 시 자동 공개)
            if (holder.isHidden)
            {
                Debug.Log($"[HolderManager] Holder {holderId} is Hidden — cannot select.");
                return false;
            }

            // 큐 기믹 체크: Frozen 상태면 터치 불가 (인접 보관함 사용 시 해동)
            if (holder.isFrozen)
            {
                Debug.Log($"[HolderManager] Holder {holderId} is Frozen — cannot select.");
                return false;
            }

            // Check global active limit: "최대 10개 활성"
            int activeCount = GetActiveHolderCount();
            if (activeCount >= MAX_ACTIVE_TOTAL)
            {
                Debug.LogWarning($"[HolderManager] Max active holders ({MAX_ACTIVE_TOTAL}) reached.");
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

            // Chain: 연결된 보관함도 순차 배치 등록
            if (holder.chainGroupId >= 0)
            {
                List<int> chainMembers = GetChainGroup(holder.chainGroupId);
                foreach (int memberId in chainMembers)
                {
                    if (memberId == holder.holderId) continue; // 이미 처리됨
                    HolderData member = FindHolder(memberId);
                    if (member == null || member.isDeploying || member.isWaiting ||
                        member.isMovingToRail || member.isConsumed) continue;

                    member.isMovingToRail = true;
                    EventBus.Publish(new OnHolderSelected
                    {
                        holderId = member.holderId,
                        color = member.color,
                        magazineCount = member.magazineCount
                    });
                }
            }

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
                magazineCount = Mathf.Min(magazineCount, _magazineMax),
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
        /// Re-distributes surviving (non-consumed) holders across columns evenly.
        /// Called after Color Remove deletes holders, leaving gaps in some columns.
        /// </summary>
        public void CompactColumns()
        {
            // Collect all non-consumed holders
            var active = new System.Collections.Generic.List<HolderData>();
            for (int i = 0; i < _holders.Count; i++)
            {
                if (!_holders[i].isConsumed)
                    active.Add(_holders[i]);
            }

            if (active.Count == 0) return;

            // Re-distribute round-robin across columns
            for (int i = 0; i < active.Count; i++)
            {
                active[i].column = i % _queueColumns;
            }

            // Reset column deploy/wait tracking and re-assign from current state
            ResetColumnTracking();
            for (int i = 0; i < active.Count; i++)
            {
                int col = active[i].column;
                if (active[i].isDeploying && _deployingHolderId[col] < 0)
                    _deployingHolderId[col] = active[i].holderId;
                else if (active[i].isWaiting && _waitingHolderId[col] < 0)
                    _waitingHolderId[col] = active[i].holderId;
            }

            Debug.Log($"[HolderManager] CompactColumns: {active.Count} holders redistributed across {_queueColumns} columns.");
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

        public HolderData FindHolderPublic(int holderId) => FindHolder(holderId);

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

        /// <summary>
        /// Returns the magazine max for a given rail capacity tier.
        /// Design: 50→15, 100→30, 150→50, 200→50.
        /// </summary>
        private static int GetMagazineMaxForCapacity(int railCapacity)
        {
            if (railCapacity <= 0) return MAG_CAP_MAX_VALUES[MAG_CAP_MAX_VALUES.Length - 1];
            for (int i = 0; i < MAG_CAP_TIERS.Length; i++)
            {
                if (railCapacity <= MAG_CAP_TIERS[i])
                    return MAG_CAP_MAX_VALUES[i];
            }
            return MAG_CAP_MAX_VALUES[MAG_CAP_MAX_VALUES.Length - 1];
        }

        /// <summary>
        /// Returns the number of currently active holders (deploying + waiting + moving to rail).
        /// </summary>
        private int GetActiveHolderCount()
        {
            int count = 0;
            for (int i = 0; i < _holders.Count; i++)
            {
                if (_holders[i].isDeploying || _holders[i].isWaiting || _holders[i].isMovingToRail)
                    count++;
            }
            return count;
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

                // Spawner: 보관함 소모 시 새 보관함 큐에 추가
                if (holder.queueGimmick == GimmickManager.GIMMICK_SPAWNER_T ||
                    holder.queueGimmick == GimmickManager.GIMMICK_SPAWNER_O)
                {
                    // 랜덤 색상으로 새 보관함 생성 (같은 열)
                    int newColor = UnityEngine.Random.Range(0, 4); // 기본 4색
                    int newMag = 20; // 기본 탄창
                    int newId = AddHolder(newColor, newMag, holder.column);
                    Debug.Log($"[HolderManager] Spawner triggered — new holder {newId} (color={newColor}) added to column {holder.column}");
                }
            }

            // 같은 열의 Frozen 보관함 해동
            int col = evt.column;
            for (int i = 0; i < _holders.Count; i++)
            {
                if (_holders[i].column == col && _holders[i].isFrozen && !_holders[i].isConsumed)
                {
                    ThawFrozenHolder(_holders[i].holderId);
                    break; // 한 번에 하나만 해동
                }
            }
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
