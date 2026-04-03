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
        /// <summary>Frozen: 얼어있어 터치 불가 (글로벌 배치 완료 카운트로 해동).</summary>
        public bool isFrozen;
        /// <summary>Frozen 해동에 필요한 보관함 배치 완료 횟수.</summary>
        public int frozenHP;
        /// <summary>Spawner 잔여 소환 횟수 (HP). 0이면 소환 종료.</summary>
        public int spawnerHP;
        /// <summary>Spawner가 소환할 보관함의 색상 목록 (순서대로). null이면 랜덤.</summary>
        public int[] spawnerColors;
        /// <summary>Spawner가 소환할 보관함의 탄창 수.</summary>
        public int spawnerMag = 20;
        /// <summary>Chain 그룹 ID. -1 = Chain 아님. 같은 ID끼리 연결 발동.</summary>
        public int chainGroupId = -1;
        public int lockPairId = -1;
        public bool isLocked;
        public bool isLockObject;
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
        // 허용량 기준: 50→max50, 100→max80, 150→max100, 200→max100
        // 명세: 40→max30, 80→max40, 120→max50, 160→max50
        private static readonly int[] MAG_CAP_TIERS      = { 40, 80, 120, 160 };
        private static readonly int[] MAG_CAP_MAX_VALUES  = { 30, 40,  50,  50 };

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
                // position.x = MapMaker 그리드의 열 번호 (빈 칸 포함 원래 위치)
                int col = Mathf.Clamp((int)setup.position.x, 0, _queueColumns - 1);

                string gimmick = setup.queueGimmick ?? "";
                bool hidden = gimmick == GimmickManager.GIMMICK_HIDDEN;
                bool frozen = gimmick == GimmickManager.GIMMICK_FROZEN_DART;

                int fHP = frozen ? (setup.frozenHP > 0 ? setup.frozenHP : 3) : 0;
                bool isSpawner = gimmick == GimmickManager.GIMMICK_SPAWNER_T || gimmick == GimmickManager.GIMMICK_SPAWNER_O;
                var holder = new HolderData
                {
                    holderId = _nextHolderId++,
                    color = setup.color,
                    magazineCount = isSpawner ? 0 : Mathf.Min(setup.magazineCount, _magazineMax), // Spawner는 다트 없음
                    column = col,
                    isDeploying = false,
                    isWaiting = false,
                    isMovingToRail = false,
                    isConsumed = false,
                    queueGimmick = gimmick,
                    isHidden = hidden,
                    isFrozen = frozen,
                    frozenHP = fHP,
                    chainGroupId = setup.chainGroupId,
                    lockPairId = setup.lockPairId,
                    isLocked = (gimmick == GimmickManager.GIMMICK_LOCK_KEY) && setup.lockPairId >= 0,
                    isLockObject = (gimmick == GimmickManager.GIMMICK_LOCK_KEY) && setup.lockPairId >= 0,
                    spawnerHP = setup.spawnerHP,
                    spawnerColors = setup.spawnerColors,
                    spawnerMag = setup.spawnerMag > 0 ? setup.spawnerMag : 20
                };
                _holders.Add(holder);
            }

            // 초기 Spawner 소환은 SpawnWaitingHolders에서 처리 (풍선 생성 후 색상 참조 가능)
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
        /// Hand 부스터용 — 줄 순서/Hidden/Frozen 무시하고 강제 배치.
        /// Spawner/Lock/consumed만 차단.
        /// </summary>
        public bool ForceSelectHolder(int holderId)
        {
            HolderData holder = FindHolder(holderId);
            if (holder == null || holder.isDeploying || holder.isWaiting || holder.isMovingToRail || holder.isConsumed)
                return false;
            if (holder.queueGimmick == GimmickManager.GIMMICK_SPAWNER_T ||
                holder.queueGimmick == GimmickManager.GIMMICK_SPAWNER_O)
                return false;
            if (holder.isLockObject) return false;
            if (holder.magazineCount <= 0) return false;

            // Frozen → 자동 해동
            if (holder.isFrozen)
            {
                holder.isFrozen = false;
                EventBus.Publish(new OnHolderThawed { holderId = holderId });
            }

            int col = holder.column;
            holder.isDeploying = true;
            _deployingHolderId[col] = holder.holderId;

            EventBus.Publish(new OnHolderSelected
            {
                holderId = holder.holderId,
                color = holder.color,
                magazineCount = holder.magazineCount
            });

            // Chain 연결 보관함도 함께 배치
            if (holder.chainGroupId >= 0)
            {
                List<int> chainMembers = GetChainGroup(holder.chainGroupId);
                foreach (int mid in chainMembers)
                {
                    if (mid == holder.holderId) continue;
                    HolderData m = FindHolder(mid);
                    if (m != null && !m.isDeploying && !m.isWaiting && !m.isMovingToRail && !m.isConsumed)
                    {
                        if (m.isFrozen) { m.isFrozen = false; EventBus.Publish(new OnHolderThawed { holderId = mid }); }
                        m.isDeploying = true;
                        EventBus.Publish(new OnHolderSelected { holderId = mid, color = m.color, magazineCount = m.magazineCount });
                    }
                }
            }

            return true;
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

            // Spawner는 클릭 불가 — 자동 소환만
            if (holder.queueGimmick == GimmickManager.GIMMICK_SPAWNER_T ||
                holder.queueGimmick == GimmickManager.GIMMICK_SPAWNER_O)
            {
                return false;
            }

            // Lock: Lock 자체는 선택 불가
            if (holder.isLockObject)
                return false;

            // Lock에 의해 차단된 보관함은 선택 불가
            if (IsBlockedByLock(holder))
                return false;

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
                // 레일 여유 용량 체크 — Chain 전체 다트 수 vs 빈 슬롯
                int chainTotalDarts = holder.magazineCount;
                List<int> chainMembers = GetChainGroup(holder.chainGroupId);
                foreach (int mid in chainMembers)
                {
                    if (mid == holder.holderId) continue;
                    HolderData m = FindHolder(mid);
                    if (m != null && !m.isDeploying && !m.isWaiting && !m.isMovingToRail && !m.isConsumed)
                        chainTotalDarts += m.magazineCount;
                }

                int emptySlots = 0;
                if (RailManager.HasInstance)
                    emptySlots = RailManager.Instance.SlotCount - RailManager.Instance.OccupiedCount;

                if (chainTotalDarts > emptySlots)
                {
                    Debug.LogWarning($"[HolderManager] Chain group {holder.chainGroupId}: need {chainTotalDarts} slots but only {emptySlots} empty — chain blocked.");
                    EventBus.Publish(new OnHolderWarning
                    {
                        waitingCount = chainTotalDarts,
                        maxSlots = emptySlots,
                        isDanger = true
                    });
                    // 리더만 배치, 체인 멤버는 등록 안 함
                }
                else
                {
                    foreach (int memberId in chainMembers)
                    {
                        if (memberId == holder.holderId) continue; // 이미 처리됨
                        HolderData member = FindHolder(memberId);
                        if (member == null || member.isDeploying || member.isWaiting ||
                            member.isMovingToRail || member.isConsumed) continue;

                        int memberCol = member.column;

                        // 같은 열 상태 체크 — deploying/waiting 슬롯 관리
                        if (_deployingHolderId[memberCol] >= 0 && _waitingHolderId[memberCol] >= 0)
                        {
                            // 열이 꽉 참 (deploying + waiting) — 이 멤버는 큐에서 대기
                            Debug.Log($"[HolderManager] Chain member {memberId} column {memberCol} full — stays in queue.");
                            continue;
                        }

                        if (_deployingHolderId[memberCol] >= 0)
                        {
                            // 열에 이미 배치 중인 보관함 있음 → waiting으로 등록
                            member.isWaiting = true;
                            member.isMovingToRail = true;
                            _waitingHolderId[memberCol] = member.holderId;
                        }
                        else
                        {
                            // 열이 비어있음 → 즉시 배치
                            member.isDeploying = true;
                            member.isMovingToRail = true;
                            _deployingHolderId[memberCol] = member.holderId;
                        }

                        EventBus.Publish(new OnHolderSelected
                        {
                            holderId = member.holderId,
                            color = member.color,
                            magazineCount = member.magazineCount
                        });
                    }
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
                if (_holders[i].isConsumed) continue;
                // Spawner는 HP 남아있으면 아직 끝 아님
                if (_holders[i].spawnerHP > 0) return false;
                // 일반 보관함은 탄창 남아있거나 배치 중이면 끝 아님
                if (_holders[i].magazineCount > 0 || _holders[i].isDeploying) return false;
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
        /// <summary>Lock_Key: pairId에 해당하는 Lock 보관함 잠금 해제.</summary>
        public void UnlockHolder(int pairId)
        {
            for (int i = 0; i < _holders.Count; i++)
            {
                if (_holders[i].lockPairId == pairId && _holders[i].isLockObject && !_holders[i].isConsumed)
                {
                    _holders[i].isConsumed = true;
                    EventBus.Publish(new OnHolderUnlocked { holderId = _holders[i].holderId, pairId = pairId });
                }
            }
        }

        /// <summary>Spawner 자동 소환 처리. 매 프레임 또는 배치 완료 시 호출.</summary>
        public void ProcessSpawners()
        {
            for (int i = 0; i < _holders.Count; i++)
            {
                var spawner = _holders[i];
                if (spawner.isConsumed) continue;
                if (spawner.queueGimmick != GimmickManager.GIMMICK_SPAWNER_T &&
                    spawner.queueGimmick != GimmickManager.GIMMICK_SPAWNER_O) continue;
                if (spawner.spawnerHP <= 0) continue;

                // 같은 열에 소환된 일반 보관함이 몇 개인지 확인
                // 1개까지 허용 (앞에 보관함 + Spawner 위치에 대기 보관함)
                int normalCount = 0;
                for (int j = 0; j < _holders.Count; j++)
                {
                    if (i == j) continue;
                    if (_holders[j].column != spawner.column) continue;
                    if (_holders[j].isConsumed) continue;
                    if (_holders[j].queueGimmick != GimmickManager.GIMMICK_SPAWNER_T &&
                        _holders[j].queueGimmick != GimmickManager.GIMMICK_SPAWNER_O)
                    {
                        normalCount++;
                    }
                }

                // 앞 보관함(1) + Spawner 안 대기(1) = 최대 2개
                if (normalCount >= 2) continue;

                // 소환!
                spawner.spawnerHP--;

                // 색상 결정: 명시 색상 → 인게임 풍선 색상에서 랜덤
                int totalHP = spawner.spawnerColors != null ? spawner.spawnerColors.Length : 0;
                int spawnIndex = totalHP - spawner.spawnerHP - 1;
                int newColor;
                if (spawner.spawnerColors != null && spawnIndex >= 0 && spawnIndex < spawner.spawnerColors.Length)
                    newColor = spawner.spawnerColors[spawnIndex];
                else
                    newColor = PickRandomBalloonColor();

                int newMag = spawner.spawnerMag > 0 ? spawner.spawnerMag : 20;
                AddHolder(newColor, newMag, spawner.column);

                // HP 텍스트 갱신
                EventBus.Publish(new OnFrozenHPChanged
                {
                    holderId = spawner.holderId,
                    remainingHP = spawner.spawnerHP
                });

                // HP 0이면 Spawner 소멸
                if (spawner.spawnerHP <= 0)
                {
                    spawner.isConsumed = true;
                }
            }
        }

        /// <summary>Spawner의 다음 소환 색상 조회 (미리보기용). -1 = 소환 불가.</summary>
        public int GetSpawnerNextColor(int holderId)
        {
            var spawner = FindHolder(holderId);
            if (spawner == null || spawner.spawnerHP <= 0) return -1;
            int totalHP = spawner.spawnerColors != null ? spawner.spawnerColors.Length : 0;
            int spawnIndex = totalHP - spawner.spawnerHP;
            if (spawner.spawnerColors != null && spawnIndex >= 0 && spawnIndex < spawner.spawnerColors.Length)
                return spawner.spawnerColors[spawnIndex];
            return spawner.color; // 기본: Spawner 자체 색상
        }

        /// <summary>인게임 풍선에서 실제 사용 중인 색상 하나를 랜덤 선택.</summary>
        private int PickRandomBalloonColor()
        {
            if (!BalloonController.HasInstance)
                return UnityEngine.Random.Range(0, 4);

            // 현재 남아있는 풍선의 색상 수집
            var colorSet = new HashSet<int>();
            var balloons = BalloonController.Instance.GetAllBalloons();
            if (balloons != null)
            {
                foreach (var b in balloons)
                {
                    if (!b.isPopped) colorSet.Add(b.color);
                }
            }
            if (colorSet.Count == 0) return UnityEngine.Random.Range(0, 4);

            // HashSet → 배열 변환 후 랜덤 선택
            var colorList = new List<int>(colorSet);
            return colorList[UnityEngine.Random.Range(0, colorList.Count)];
        }

        /// <summary>
        /// Adds a new holder to the queue.
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

        private bool IsBlockedByLock(HolderData holder)
        {
            // Check if any non-consumed Lock exists in the same column, positioned before this holder
            for (int i = 0; i < _holders.Count; i++)
            {
                if (_holders[i].isConsumed) continue;
                if (_holders[i].column != holder.column) continue;
                if (!_holders[i].isLockObject) continue;
                // Lock exists in this column and is not consumed = blocks everything behind it
                // Check if the Lock is positioned BEFORE this holder (lower holderId = earlier in queue)
                if (_holders[i].holderId < holder.holderId)
                    return true;
            }
            return false;
        }

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
            // 부스터(Hand/SelectTool) 대기 중이면 BoosterExecutor로 넘김
            if (BoosterExecutor.HasInstance && BoosterExecutor.Instance.IsAwaitingHolderSelection)
            {
                BoosterExecutor.Instance.OnHolderSelected(evt.holderId);
                return;
            }
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

                // Spawner 체크: 앞이 비면 소환
                ProcessSpawners();
            }

            // Frozen Dart: 글로벌 배치 완료 카운트 기반 해동
            for (int i = 0; i < _holders.Count; i++)
            {
                if (!_holders[i].isFrozen || _holders[i].isConsumed) continue;
                _holders[i].frozenHP--;
                if (_holders[i].frozenHP <= 0)
                {
                    ThawFrozenHolder(_holders[i].holderId);
                }
                else
                {
                    // HP 텍스트 갱신 이벤트
                    EventBus.Publish(new OnFrozenHPChanged
                    {
                        holderId = _holders[i].holderId,
                        remainingHP = _holders[i].frozenHP
                    });
                }
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
