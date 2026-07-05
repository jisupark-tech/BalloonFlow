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
        public int sourceRow;         // authored holder row in MapMaker queue grid.
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
        /// <summary>Spawner color sequence cursor. Keeps spawnerColors in authored order.</summary>
        public int spawnerSpawnedCount;
        // ROLLBACK_PIPE_PAYLOAD_RELEASE_20260624:
        // Pipe(Spawner_O) releases authored holders below the pipe anchor instead of
        // creating new holders, preserving each payload holder's color/mag/gimmick data.
        public int pipeOwnerId = -1;
        public int pipeOrder = -1;
        public bool isPipePayload;
        public bool isPipePayloadReleased = true;
        /// <summary>Chain 그룹 ID. -1 = Chain 아님. 같은 ID끼리 연결 발동.</summary>
        public int chainGroupId = -1;
        public int lockPairId = -1;
        public bool isLocked;
        public bool isLockObject;

        public bool IsQueueVisible => !isPipePayload || isPipePayloadReleased;
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

        #endregion

        #region Fields

        private readonly List<HolderData> _holders = new List<HolderData>();
        private readonly List<int> _lastSpawnerChangedColumns = new List<int>(MAX_QUEUE_COLUMNS);
        private int _nextHolderId;
        private int _queueColumns = 5;
        private int _magazineMax = 50; // current level's magazine cap (set by rail capacity)

        // Per-column tracking: which holder is deploying, which is waiting
        private readonly int[] _deployingHolderId = new int[MAX_QUEUE_COLUMNS];
        private readonly int[] _waitingHolderId = new int[MAX_QUEUE_COLUMNS];

        #endregion

        #region Properties

        public int QueueColumns => _queueColumns;
        public IReadOnlyList<int> LastSpawnerChangedColumns => _lastSpawnerChangedColumns;

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
                    sourceRow = i / _queueColumns,
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
                    sourceRow = 0,
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
                string normalizedGimmick = GimmickDisplayName.Normalize(setup.queueGimmick);
                if (normalizedGimmick == "none")
                    normalizedGimmick = "";
                setup.queueGimmick = normalizedGimmick;

                // [ROLLBACK_LOCKKEY_DEPRECATE]
                // Lock_Key holder dead 처리 — 기존 LevelData 호환을 위해 정규화: Lock_Key → 일반 holder.
                if (setup.queueGimmick == GimmickManager.GIMMICK_LOCK_KEY)
                {
                    setup.queueGimmick = "";
                    setup.lockPairId = -1;
                }

                // position.x = MapMaker 그리드의 열 번호 (빈 칸 포함 원래 위치)
                int col = Mathf.Clamp((int)setup.position.x, 0, _queueColumns - 1);
                int row = Mathf.Max(0, Mathf.RoundToInt(setup.position.y));

                string gimmick = setup.queueGimmick ?? "";
                bool hidden = gimmick == GimmickManager.GIMMICK_HIDDEN;
                bool frozen = gimmick == GimmickManager.GIMMICK_FROZEN_DART;

                int fHP = frozen ? (setup.frozenHP > 0 ? setup.frozenHP : 3) : 0;
                bool isSpawner = gimmick == GimmickManager.GIMMICK_SPAWNER_T || gimmick == GimmickManager.GIMMICK_SPAWNER_O;
                var holder = new HolderData
                {
                    holderId = _nextHolderId++,
                    color = setup.color,
                    // ROLLBACK_HOLDER_MAG_PREVIEW_MATCH_20260625: MapMaker 미리보기(저작값) 그대로 사용 —
                    //   레일 capacity 상한(_magazineMax=30/40/50)으로 깎던 clamp 제거. Rail Overflow 모드라 초과 다트도 수용.
                    //   (기존: Mathf.Min(setup.magazineCount, _magazineMax) → MapMaker 표시값보다 작게 배치됨.)
                    magazineCount = isSpawner ? 0 : Mathf.Max(0, setup.magazineCount), // Spawner는 다트 없음
                    column = col,
                    sourceRow = row,
                    isDeploying = false,
                    isWaiting = false,
                    isMovingToRail = false,
                    isConsumed = false,
                    queueGimmick = gimmick,
                    isHidden = hidden,
                    isFrozen = frozen,
                    frozenHP = fHP,
                    // [Chain fix] Chain(Linked) holder 에만 chainGroupId 적용 — 비-Chain holder 의 stale 그룹값
                    // (구버전 레벨 데이터)으로 인한 의도치 않은 연결 차단. GetChainGroup 은 chainGroupId 매칭이라 -1 이면 비연결.
                    chainGroupId = (gimmick == GimmickManager.GIMMICK_CHAIN) ? setup.chainGroupId : -1,
                    lockPairId = setup.lockPairId,
                    isLocked = (gimmick == GimmickManager.GIMMICK_LOCK_KEY) && setup.lockPairId >= 0,
                    isLockObject = (gimmick == GimmickManager.GIMMICK_LOCK_KEY) && setup.lockPairId >= 0,
                    spawnerHP = setup.spawnerHP,
                    spawnerColors = setup.spawnerColors,
                    spawnerMag = setup.spawnerMag > 0 ? setup.spawnerMag : 20
                };
                _holders.Add(holder);
            }

            BindAuthoredPipePayloads();

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
                if (_holders[i].chainGroupId == chainGroupId && !_holders[i].isConsumed && _holders[i].IsQueueVisible)
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
            if (holder == null || !holder.IsQueueVisible || holder.isDeploying || holder.isWaiting || holder.isMovingToRail || holder.isConsumed)
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

            // Hidden → 해금 (연출 포함). Hand/SelectTool 은 Hidden 도 강제 선택 가능하므로,
            // 자동 앞줄 해금(RevealHiddenHolder)과 동일한 해금 연출(OnHolderRevealed)을 내보낸다.
            // (기존엔 isDeploying 만 세팅 → 기능은 동작하나 해금 연출 누락.)
            if (holder.isHidden)
            {
                holder.isHidden = false;
                EventBus.Publish(new OnHolderRevealed { holderId = holderId });
            }

            int col = holder.column;

            // ROLLBACK_HOLDER_FORCESELECT_COLUMN_GUARD_20260615: START
            // 기존엔 컬럼 점유를 무시하고 _deployingHolderId[col] 을 무조건 덮어써서, 이미 deployer 가 있던
            // 컬럼이면 그 deployer 슬롯이 누수(영구 점유) + 같은 컬럼 deploying holder 2개(더블파이어)였다.
            // SelectHolder(472-484) 와 동일하게 컬럼 상태에 따라 분기한다:
            //   deploying+waiting 둘 다 참 → 배치 불가(false). 인벤토리 환불은 호출측(BoosterExecutor) 책임.
            //   deploying 만 참 → 이 holder 는 waiting 으로.  비어있음 → 즉시 deploying.
            // (이벤트 OnHolderSelected 는 두 경우 모두 발행 — visual 이 isWaiting 으로 큐 배치 처리, SelectHolder 동일.)
            // 롤백: 아래 START~END 를 다음 3줄로 교체:
            //   holder.isDeploying = true;
            //   _deployingHolderId[col] = holder.holderId;
            if (_deployingHolderId[col] >= 0 && _waitingHolderId[col] >= 0)
            {
                return false; // 컬럼 가득 — 강제 배치 불가(슬롯 덮어쓰기 금지)
            }
            if (_deployingHolderId[col] >= 0)
            {
                holder.isWaiting = true;
                _waitingHolderId[col] = holder.holderId;
            }
            else
            {
                holder.isDeploying = true;
                holder.isMovingToRail = true;
                _deployingHolderId[col] = holder.holderId;
            }
            // ROLLBACK_HOLDER_FORCESELECT_COLUMN_GUARD_20260615: END

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
                        // ROLLBACK_FORCESELECT_CHAIN_COLUMN_GUARD_20260616: 멤버도 리더(위 324-351)와 동일하게 컬럼 점유 가드.
                        //   기존엔 멤버가 _deployingHolderId 확인 없이 무조건 isDeploying=true → 같은 컬럼에 deployer 2개
                        //   = 슬롯 누수/더블 디플로이. SelectHolder/리더와 동일 분기로 deploying/waiting 슬롯을 정확히 점유.
                        //   롤백: 아래 분기를 m.isDeploying = true; 한 줄로 환원.
                        int mcol = m.column;
                        if (_deployingHolderId[mcol] >= 0 && _waitingHolderId[mcol] >= 0)
                            continue; // 멤버 컬럼이 가득 — skip (슬롯 덮어쓰기 금지)
                        if (m.isFrozen) { m.isFrozen = false; EventBus.Publish(new OnHolderThawed { holderId = mid }); }
                        if (m.isHidden) { m.isHidden = false; EventBus.Publish(new OnHolderRevealed { holderId = mid }); }
                        if (_deployingHolderId[mcol] >= 0)
                        {
                            m.isWaiting = true;
                            _waitingHolderId[mcol] = m.holderId;
                        }
                        else
                        {
                            m.isDeploying = true;
                            m.isMovingToRail = true;
                            _deployingHolderId[mcol] = m.holderId;
                        }
                        EventBus.Publish(new OnHolderSelected { holderId = mid, color = m.color, magazineCount = m.magazineCount });
                    }
                }
            }

            // ROLLBACK_SPAWNER_RELEASE_VISUAL_REFRESH_20260630:
            // Force-selected holders also vacate the pipe front immediately, so release/spawn and redraw now.
            if (ProcessSpawners() && HolderVisualManager.HasInstance)
                HolderVisualManager.Instance.RefreshSpawnerChangedColumns(LastSpawnerChangedColumns);

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
            if (holder == null || !holder.IsQueueVisible || holder.isDeploying || holder.isWaiting || holder.isMovingToRail || holder.isConsumed)
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
            {
                if (AudioManager.HasInstance) AudioManager.Instance.PlayDeny();
                return false;
            }

            // Lock에 의해 차단된 보관함은 선택 불가
            if (IsBlockedByLock(holder))
            {
                if (AudioManager.HasInstance) AudioManager.Instance.PlayDeny();
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
                if (AudioManager.HasInstance) AudioManager.Instance.PlayDeny();
                return false;
            }

            // 큐 기믹 체크: Frozen 상태면 터치 불가 (인접 보관함 사용 시 해동)
            if (holder.isFrozen)
            {
                Debug.Log($"[HolderManager] Holder {holderId} is Frozen — cannot select.");
                if (AudioManager.HasInstance) AudioManager.Instance.PlayDeny();
                return false;
            }

            // Chain/Linked Dart Box: validate the whole group before mutating any holder state.
            // This prevents split loads such as 1 member moving first and the remaining members later.
            if (holder.chainGroupId >= 0)
            {
                List<int> chainMembers = GetChainGroup(holder.chainGroupId);
                if (chainMembers.Count > 1)
                    return TrySelectChainGroup(holder, chainMembers);
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
                EventBus.Publish(new OnHolderColumnBlocked
                {
                    holderId = holder.holderId,
                    column = col
                });
                EventBus.Publish(new OnHolderWarning
                {
                    waitingCount = 2,
                    maxSlots = 2,
                    isDanger = true
                });
                return false;
            }

            // Chain: 그룹 전원 앞줄 검증 (AND 조건, 2026-05-18).
            // InputHandler 가 1차 차단하지만, 부스터 / 다른 경로 (자동 선택) 에서도 일관 차단 보장.
            // 정책 진실 소스 = HolderManager.
            if (holder.chainGroupId >= 0 && HolderVisualManager.HasInstance)
            {
                List<int> chainMembers = GetChainGroup(holder.chainGroupId);
                for (int i = 0; i < chainMembers.Count; i++)
                {
                    int mid = chainMembers[i];
                    if (mid == holder.holderId) continue;
                    if (!HolderVisualManager.Instance.IsInFrontRow(mid))
                    {
                        Debug.Log($"[HolderManager] Chain blocked — member {mid} not in front row (groupId={holder.chainGroupId})");
                        EventBus.Publish(new OnHolderColumnBlocked
                        {
                            holderId = holder.holderId,
                            column = col
                        });
                        return false;
                    }
                }
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

        private bool TrySelectChainGroup(HolderData triggerHolder, List<int> chainMemberIds)
        {
            if (triggerHolder == null || chainMemberIds == null || chainMemberIds.Count <= 1)
                return false;

            var members = new List<HolderData>(chainMemberIds.Count);
            for (int i = 0; i < chainMemberIds.Count; i++)
            {
                HolderData member = FindHolder(chainMemberIds[i]);
                if (member == null || member.isDeploying || member.isWaiting || member.isMovingToRail || member.isConsumed)
                    return false;

                if (member.queueGimmick == GimmickManager.GIMMICK_SPAWNER_T ||
                    member.queueGimmick == GimmickManager.GIMMICK_SPAWNER_O)
                    return false;

                if (member.isLockObject || IsBlockedByLock(member))
                    return false;

                if (member.magazineCount <= 0 || member.isHidden || member.isFrozen)
                    return false;

                // ROLLBACK_VERTICAL_CHAIN_DEPLOY_20260625: 멤버별 '앞줄' 검증을 여기서 제거.
                //   세로 체인은 같은 열에 스택돼 뒤 멤버(예: A-2 row1)가 앞줄이 아니므로 이 검증에 걸려 막혔다.
                //   앞줄 검증은 아래 '열별 그룹화'에서 열마다 선두 1명 기준으로 처리한다(가로 체인 동작 동일).
                members.Add(member);
            }

            int activeAfterSelect = GetActiveHolderCount() + members.Count;
            if (activeAfterSelect > MAX_ACTIVE_TOTAL)
            {
                Debug.LogWarning($"[HolderManager] Chain group {triggerHolder.chainGroupId} blocked - active holders would exceed {MAX_ACTIVE_TOTAL} ({activeAfterSelect}).");
                EventBus.Publish(new OnHolderWarning
                {
                    waitingCount = activeAfterSelect,
                    maxSlots = MAX_ACTIVE_TOTAL,
                    isDanger = true
                });
                return false;
            }

            // ROLLBACK_VERTICAL_CHAIN_DEPLOY_20260625: 세로 체인(같은 열에 스택된 멤버) 지원.
            //   기존엔 (a) 모든 멤버가 앞줄이어야 하고, (b) 한 열에 멤버 2명 이상(chainMembersPerColumn>1)이면
            //   무조건 차단 → 세로 체인(A-1 row0 / A-2 row1, 같은 열)이 정상 탭으로 배포 불가였다.
            //   (가로 체인은 열당 1명·모두 앞줄이라 통과. Hand/ForceSelectHolder 는 순차 점유라 됐는데
            //    정상 탭만 막혀 불일치였음.)
            //   변경: 멤버를 열별로 묶어 각 열에서 '앞줄 선두 1명 = deploying, 바로 뒤 1명 = waiting' 으로
            //   순차 점유(Hand 와 동일). 열 슬롯이 deploying+waiting 2개라 한 열당 최대 2명까지 동시 적재.
            var membersByColumn = new Dictionary<int, List<HolderData>>();
            for (int i = 0; i < members.Count; i++)
            {
                int mcol = members[i].column;
                if (mcol < 0 || mcol >= _queueColumns) return false;
                if (!membersByColumn.TryGetValue(mcol, out var list))
                {
                    list = new List<HolderData>(2);
                    membersByColumn[mcol] = list;
                }
                list.Add(members[i]);
            }

            // 열별 검증: 열이 비어있어야 하고(분할 적재 방지), 열당 멤버 ≤ 2(슬롯 deploying+waiting), 앞줄 선두 멤버 존재.
            foreach (var kvp in membersByColumn)
            {
                int col = kvp.Key;
                List<HolderData> colMembers = kvp.Value;

                if (_deployingHolderId[col] >= 0 || _waitingHolderId[col] >= 0 || colMembers.Count > 2)
                {
                    EventBus.Publish(new OnHolderColumnBlocked { holderId = triggerHolder.holderId, column = col });
                    EventBus.Publish(new OnHolderWarning { waitingCount = colMembers.Count, maxSlots = 2, isDanger = true });
                    return false;
                }

                // 그 열에 앞줄(선두) 멤버가 있어야 배포 가능 — 가로=유일 멤버, 세로=맨 위(row0) 멤버.
                bool hasFront = !HolderVisualManager.HasInstance; // visual 없으면 검증 스킵
                if (HolderVisualManager.HasInstance)
                    for (int i = 0; i < colMembers.Count; i++)
                        if (HolderVisualManager.Instance.IsInFrontRow(colMembers[i].holderId)) { hasFront = true; break; }
                if (!hasFront)
                {
                    Debug.Log($"[HolderManager] Chain blocked - column {col} has no front-row member (groupId={triggerHolder.chainGroupId})");
                    EventBus.Publish(new OnHolderColumnBlocked { holderId = triggerHolder.holderId, column = col });
                    return false;
                }
            }

            // 배포 등록: 열별로 앞줄 선두 = deploying, 나머지(최대 1명) = waiting(선두 끝나면 이어서 배포 → 레일에 함께 붙음).
            foreach (var kvp in membersByColumn)
            {
                int col = kvp.Key;
                List<HolderData> colMembers = kvp.Value;

                HolderData front = colMembers[0];
                if (HolderVisualManager.HasInstance)
                    for (int i = 0; i < colMembers.Count; i++)
                        if (HolderVisualManager.Instance.IsInFrontRow(colMembers[i].holderId)) { front = colMembers[i]; break; }

                front.isDeploying = true;
                front.isMovingToRail = true;
                _deployingHolderId[col] = front.holderId;
                EventBus.Publish(new OnHolderSelected { holderId = front.holderId, color = front.color, magazineCount = front.magazineCount });

                for (int i = 0; i < colMembers.Count; i++)
                {
                    if (colMembers[i] == front) continue;
                    colMembers[i].isWaiting = true;
                    _waitingHolderId[col] = colMembers[i].holderId;
                    EventBus.Publish(new OnHolderSelected { holderId = colMembers[i].holderId, color = colMembers[i].color, magazineCount = colMembers[i].magazineCount });
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
                if (_holders[i].column == column && !_holders[i].isConsumed && _holders[i].IsQueueVisible)
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
                if (!_holders[i].IsQueueVisible) continue;
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
                if (_holders[i].IsQueueVisible && !_holders[i].isDeploying && !_holders[i].isWaiting && !_holders[i].isMovingToRail && !_holders[i].isConsumed)
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

        /// <summary>Pipe(Spawner_O)·Glass Pipe(Spawner_T) anchor below-row authored holders as sequential payloads.
        /// ROLLBACK_GLASSPIPE_PARITY_20260625: 둘은 기능 동일(머티리얼만 다름) — bind 도 동일 적용.</summary>
        private void BindAuthoredPipePayloads()
        {
            for (int i = 0; i < _holders.Count; i++)
            {
                HolderData pipe = _holders[i];
                if ((pipe.queueGimmick != GimmickManager.GIMMICK_SPAWNER_O
                  && pipe.queueGimmick != GimmickManager.GIMMICK_SPAWNER_T) || pipe.spawnerHP <= 0)
                    continue;

                var payloads = new List<HolderData>();
                for (int j = 0; j < _holders.Count; j++)
                {
                    HolderData candidate = _holders[j];
                    if (candidate == pipe || candidate.isConsumed) continue;
                    if (candidate.column != pipe.column) continue;
                    if (candidate.sourceRow <= pipe.sourceRow) continue;
                    if (candidate.queueGimmick == GimmickManager.GIMMICK_SPAWNER_T ||
                        candidate.queueGimmick == GimmickManager.GIMMICK_SPAWNER_O)
                        continue;
                    payloads.Add(candidate);
                }

                payloads.Sort((a, b) =>
                {
                    int rowCompare = a.sourceRow.CompareTo(b.sourceRow);
                    return rowCompare != 0 ? rowCompare : a.holderId.CompareTo(b.holderId);
                });

                int count = Mathf.Min(pipe.spawnerHP, payloads.Count);
                if (count <= 0)
                    continue; // Old Pipe data without authored payloads keeps generated spawner behavior.

                pipe.spawnerHP = count;
                pipe.spawnerSpawnedCount = 0;
                for (int n = 0; n < count; n++)
                {
                    HolderData payload = payloads[n];
                    payload.pipeOwnerId = pipe.holderId;
                    payload.pipeOrder = n;
                    payload.isPipePayload = true;
                    payload.isPipePayloadReleased = false;
                }
            }
        }

        private bool HasAuthoredPipePayload(HolderData pipe)
        {
            if (pipe == null) return false;
            for (int i = 0; i < _holders.Count; i++)
            {
                HolderData holder = _holders[i];
                if (holder.pipeOwnerId == pipe.holderId && holder.isPipePayload)
                    return true;
            }
            return false;
        }

        private HolderData GetNextPipePayload(HolderData pipe)
        {
            if (pipe == null) return null;
            HolderData best = null;
            for (int i = 0; i < _holders.Count; i++)
            {
                HolderData holder = _holders[i];
                if (holder.isConsumed || !holder.isPipePayload || holder.isPipePayloadReleased) continue;
                if (holder.pipeOwnerId != pipe.holderId) continue;
                if (best == null || holder.pipeOrder < best.pipeOrder)
                    best = holder;
            }
            return best;
        }

        private int CountVisibleNormalHoldersInColumn(int column)
        {
            int count = 0;
            for (int i = 0; i < _holders.Count; i++)
            {
                HolderData holder = _holders[i];
                if (holder.column != column || holder.isConsumed || !holder.IsQueueVisible) continue;
                // ROLLBACK_PIPE_FAST_RELEASE_20260625: 배포 시작(레일로 이동 중)한 홀더는 이미 '앞 큐'를 떠난 것으로 보고
                //   카운트에서 제외 → 파이프가 그 즉시 다음 payload 를 release(다트박스 이동과 동시에 생성).
                //   (기존엔 레일 도착·소비될 때까지 앞칸을 차지해 다음 생성이 늦었음.)
                if (holder.isDeploying || holder.isMovingToRail) continue;
                if (holder.queueGimmick == GimmickManager.GIMMICK_SPAWNER_T ||
                    holder.queueGimmick == GimmickManager.GIMMICK_SPAWNER_O)
                    continue;
                // ROLLBACK_PIPE_HELD_BEHIND_20260624: 파이프 뒤(미방출) 대기 홀더는 '앞 큐'가 아니므로 제외 —
                // 안 그러면 파이프 아래 잔여 홀더가 앞을 찼다고 판단해 release 가 영영 안 됨(normalCount 과대).
                if (IsHeldBehindPipe(holder)) continue;
                count++;
            }
            return count;
        }

        /// <summary>ROLLBACK_PIPE_HELD_BEHIND_20260624: 활성 파이프보다 뒤(sourceRow 큰)에 있고 아직
        /// 방출 안 된 홀더 = 파이프가 막고 있는 대기분. 화면 숨김 + 앞 카운트 제외 + 파이프 소멸 후 해제.</summary>
        public bool IsHeldBehindPipe(HolderData h)
        {
            if (h == null || h.isConsumed) return false;
            if (h.isPipePayload) return !h.isPipePayloadReleased; // 미방출 payload = 막힘 / 방출됨 = 자유
            // 일반 홀더: 같은 열에 (비소비) 활성 파이프가 있고, 그 파이프보다 뒤면 막힘.
            for (int i = 0; i < _holders.Count; i++)
            {
                HolderData p = _holders[i];
                if (p.isConsumed || p.column != h.column) continue;
                if (p.queueGimmick != GimmickManager.GIMMICK_SPAWNER_O &&
                    p.queueGimmick != GimmickManager.GIMMICK_SPAWNER_T) continue;
                if (h.sourceRow > p.sourceRow) return true;
            }
            return false;
        }

        private void PublishSpawnerRemaining(HolderData spawner)
        {
            if (spawner == null) return;
            EventBus.Publish(new OnFrozenHPChanged
            {
                holderId = spawner.holderId,
                remainingHP = spawner.spawnerHP
            });
        }

        /// <summary>Spawner 자동 소환 처리. 매 프레임 또는 배치 완료 시 호출.</summary>
        public bool ProcessSpawners()
        {
            bool changed = false;
            _lastSpawnerChangedColumns.Clear();
            for (int i = 0; i < _holders.Count; i++)
            {
                var spawner = _holders[i];
                if (spawner.isConsumed) continue;
                if (spawner.queueGimmick != GimmickManager.GIMMICK_SPAWNER_T &&
                    spawner.queueGimmick != GimmickManager.GIMMICK_SPAWNER_O) continue;
                if (spawner.spawnerHP <= 0) continue;

                // 같은 열에 소환된 일반 보관함이 몇 개인지 확인
                // 1개까지 허용 (앞에 보관함 + Spawner 위치에 대기 보관함)
                int normalCount = CountVisibleNormalHoldersInColumn(spawner.column);
                /*
                ROLLBACK_PIPE_PAYLOAD_RELEASE_20260624: previous generated-spawner count scanned
                every non-spawner holder in the column, including hidden pipe payloads.
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
                */

                // ROLLBACK_PIPE_HELD_BEHIND_20260624: 파이프 '앞 칸 수' 만큼만 채운다. 파이프가 row N 이면
                // 앞(레일 쪽)에 0..N-1 = N 칸. 그만큼만 release 해야 방출분이 파이프를 안 덮고 앞에 안착.
                // (normalCount 는 파이프 뒤 대기분 제외된 '앞 큐' 수.) 최소 1.
                int frontCapacity = Mathf.Max(1, spawner.sourceRow);
                if (normalCount >= frontCapacity) continue;

                // ROLLBACK_PIPE_PAYLOAD_RELEASE_20260624 / GLASSPIPE_PARITY_20260625:
                // Pipe(Spawner_O)·Glass Pipe(Spawner_T) 둘 다 anchor 아래 authored holder 를 순서대로 release.
                // payload 가 없는 (구버전) 데이터만 아래 generated-spawner 경로로 폴백.
                if ((spawner.queueGimmick == GimmickManager.GIMMICK_SPAWNER_O
                  || spawner.queueGimmick == GimmickManager.GIMMICK_SPAWNER_T) && HasAuthoredPipePayload(spawner))
                {
                    HolderData payload = GetNextPipePayload(spawner);
                    if (payload == null)
                    {
                        spawner.spawnerHP = 0;
                        spawner.isConsumed = true;
                        PublishSpawnerRemaining(spawner);
                        MarkSpawnerColumnChanged(spawner.column);
                        changed = true;
                        continue;
                    }

                    payload.isPipePayloadReleased = true;
                    spawner.spawnerHP--;
                    spawner.spawnerSpawnedCount++;
                    PublishSpawnerRemaining(spawner);
                    if (spawner.spawnerHP <= 0)
                        spawner.isConsumed = true;
                    MarkSpawnerColumnChanged(spawner.column);
                    changed = true;
                    continue;
                }

                // 소환!
                spawner.spawnerHP--;

                // 색상 결정: 명시 색상 → 인게임 풍선 색상에서 랜덤
                // ROLLBACK_SPAWNER_COLOR_SEQUENCE:
                // Keep explicit spawnerColors in authored order even when spawnerHP differs
                // from the color array length.
                int spawnIndex = spawner.spawnerSpawnedCount;
                int newColor;
                if (spawner.spawnerColors != null && spawnIndex >= 0 && spawnIndex < spawner.spawnerColors.Length)
                    newColor = spawner.spawnerColors[spawnIndex];
                else
                    newColor = PickRandomBalloonColor();
                spawner.spawnerSpawnedCount++;

                int newMag = spawner.spawnerMag > 0 ? spawner.spawnerMag : 20;
                AddHolder(newColor, newMag, spawner.column);
                MarkSpawnerColumnChanged(spawner.column);
                changed = true;

                // HP 텍스트 갱신
                PublishSpawnerRemaining(spawner);

                // HP 0이면 Spawner 소멸
                if (spawner.spawnerHP <= 0)
                {
                    spawner.isConsumed = true;
                }
            }
            return changed;
        }

        /// <summary>Spawner의 다음 소환 색상 조회 (미리보기용). -1 = 소환 불가.</summary>
        private void MarkSpawnerColumnChanged(int column)
        {
            if (column < 0) return;
            if (!_lastSpawnerChangedColumns.Contains(column))
                _lastSpawnerChangedColumns.Add(column);
        }

        public int GetSpawnerNextColor(int holderId)
        {
            var spawner = FindHolder(holderId);
            if (spawner == null || spawner.spawnerHP <= 0) return -1;
            // GLASSPIPE_PARITY_20260625: Glass Pipe(Spawner_T)도 authored payload 미리보기 동일.
            if ((spawner.queueGimmick == GimmickManager.GIMMICK_SPAWNER_O
              || spawner.queueGimmick == GimmickManager.GIMMICK_SPAWNER_T) && HasAuthoredPipePayload(spawner))
            {
                HolderData payload = GetNextPipePayload(spawner);
                return payload != null ? payload.color : -1;
            }
            int spawnIndex = spawner.spawnerSpawnedCount;
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
            // PIPE_COMPACT_PRESERVE_COLUMN_20260628: 파이프(Spawner)가 있는 열은 압축 재분배에서 제외하고 현재 열을 보존한다.
            // 기존엔 파이프 자신(IsQueueVisible==true)도 round-robin 으로 다른 열로 옮겨졌고, 그 unreleased payload 들은
            // IsQueueVisible==false 라 제자리에 남아 파이프-payload 가 분리됐다. 그 결과 ProcessSpawners 의
            // CountVisibleNormalHoldersInColumn(spawner.column) 판정이 깨져 다음 payload 가 영영 release 되지 않는
            // (스폰 정지) 버그가 있었다. 비-파이프 홀더만 비-파이프 열에 압축한다.
            var pipeColumns = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < _holders.Count; i++)
                if (!_holders[i].isConsumed
                    && (_holders[i].queueGimmick == GimmickManager.GIMMICK_SPAWNER_T
                     || _holders[i].queueGimmick == GimmickManager.GIMMICK_SPAWNER_O))
                    pipeColumns.Add(_holders[i].column);

            // 재분배 대상: 파이프 열에 속하지 않은, 큐에 보이는 비-소비 홀더만.
            var active = new System.Collections.Generic.List<HolderData>();
            for (int i = 0; i < _holders.Count; i++)
            {
                if (!_holders[i].isConsumed && _holders[i].IsQueueVisible
                    && !pipeColumns.Contains(_holders[i].column))
                    active.Add(_holders[i]);
            }

            // 분배할 열 = 파이프가 없는 열만 (파이프 열은 통째로 보존).
            var freeColumns = new System.Collections.Generic.List<int>();
            for (int c = 0; c < _queueColumns; c++)
                if (!pipeColumns.Contains(c)) freeColumns.Add(c);

            // Re-distribute round-robin across non-pipe columns
            if (active.Count > 0 && freeColumns.Count > 0)
            {
                for (int i = 0; i < active.Count; i++)
                    active[i].column = freeColumns[i % freeColumns.Count];
            }

            // Reset column deploy/wait tracking and re-assign from current state.
            // 파이프 열 홀더도 deploy/wait 상태일 수 있어 모든 비-소비 홀더를 대상으로 재구성한다(파이프 없는 레벨은 기존과 동일).
            ResetColumnTracking();
            for (int i = 0; i < _holders.Count; i++)
            {
                if (_holders[i].isConsumed) continue;
                int col = _holders[i].column;
                if (col < 0 || col >= _queueColumns) continue;
                if (_holders[i].isDeploying && _deployingHolderId[col] < 0)
                    _deployingHolderId[col] = _holders[i].holderId;
                else if (_holders[i].isWaiting && _waitingHolderId[col] < 0)
                    _waitingHolderId[col] = _holders[i].holderId;
            }

            Debug.Log($"[HolderManager] CompactColumns: {active.Count} holders redistributed across {freeColumns.Count} non-pipe columns (pipe columns: {pipeColumns.Count}).");
        }

        // ROLLBACK_ZAP_ADVANCE_QUEUE_20260705: Zap(Color-Remove)이 배포중 홀더를 제거해 deploy 슬롯이 빈 열에서,
        //   대기(waiting) 홀더를 배포로 승격시켜 큐가 '클릭 없이' 자동 전진하게 한다.
        //   원인: UndoDeploy 는 deploy 슬롯만 비우고(_deployingHolderId[col]=-1) 승격을 하지 않아, Zap 후 배포중 홀더가
        //   사라진 열의 대기 홀더가 다음 탭 전까지 레일로 안 나오던 버그. HandleDeploymentDone 의 wait→deploy 승격과 동일.
        //   (정상 tap 배포로 채워둔 wait 슬롯만 승격 — 큐 뒷줄의 미탭 홀더까지 자동배포하진 않음: 탭-투-디플로이 유지.)
        public void AdvanceEmptyDeploySlots()
        {
            for (int col = 0; col < _queueColumns; col++)
            {
                if (_deployingHolderId[col] >= 0) continue;   // 이미 배포중 홀더 있음
                if (_waitingHolderId[col] < 0) continue;      // 승격할 대기 홀더 없음

                int waitId = _waitingHolderId[col];
                _waitingHolderId[col] = -1;

                HolderData waitHolder = FindHolder(waitId);
                if (waitHolder == null || waitHolder.isConsumed) continue;

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
            // 컬럼 내 큐 위치(=_holders 리스트에서 같은 column 의 non-consumed 만 모은 순서)
            // 로 비교. holderId 비교는 Spawner 가 새 holder 추가 시 ID 가 끝에 매겨져 실제
            // 컬럼 위치와 어긋날 수 있음 → 누수 방지.
            int holderColPos = GetColumnQueuePosition(holder);
            if (holderColPos < 0) return false;

            for (int i = 0; i < _holders.Count; i++)
            {
                if (_holders[i].isConsumed) continue;
                if (_holders[i].column != holder.column) continue;
                if (!_holders[i].isLockObject) continue;
                int lockColPos = GetColumnQueuePosition(_holders[i]);
                // Lock이 같은 컬럼에서 holder 보다 앞쪽에 있으면 차단
                if (lockColPos >= 0 && lockColPos < holderColPos)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns 0-based position of holder in its column queue (excluding consumed).
        /// _holders 리스트 순서 (= 추가 순서) 가 컬럼 큐 순서와 일치한다고 가정.
        /// </summary>
        private int GetColumnQueuePosition(HolderData h)
        {
            int pos = 0;
            for (int i = 0; i < _holders.Count; i++)
            {
                if (_holders[i].column != h.column) continue;
                if (_holders[i].isConsumed) continue;
                if (!_holders[i].IsQueueVisible) continue;
                if (_holders[i] == h) return pos;
                pos++;
            }
            return -1;
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
                    if (_holders[i].column == col && !_holders[i].isConsumed && _holders[i].IsQueueVisible)
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
        /// Design: 40→30, 80→40, 120→50, 160→50.
        /// </summary>
        private static int GetMagazineMaxForCapacity(int railCapacity)
        {
            return RailManager.GetMagazineMaxForCapacity(railCapacity);
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

            bool selected = SelectHolder(evt.holderId);

            // 배포 불가해도 Click 애니메이션만 재생 (3열 이후 등)
            if (!selected)
            {
                // ROLLBACK_SPAWNER_INSIDE_NO_CLICK_ANIM_20260630: Spawner(Glass Pipe) 안 미방출 payload 는 클릭해도 무반응.
                HolderData tappedHd = FindHolder(evt.holderId);
                bool insideSpawner = tappedHd != null && tappedHd.isPipePayload && !tappedHd.isPipePayloadReleased;
                if (!insideSpawner)
                    EventBus.Publish(new OnHolderClickAnim { holderId = evt.holderId });
            }
            else
            {
                // ROLLBACK_PIPE_FAST_RELEASE_20260625: 탭으로 배포가 시작되면 즉시 파이프 다음 payload release.
                //   배포중 홀더는 CountVisibleNormalHoldersInColumn 에서 제외되므로 앞칸이 비어 다음이 바로 나옴
                //   (= 다트박스 이동과 동시에 생성). 기존엔 배포 '완료'(HandleDeploymentDone) 때만 release 돼 느렸음.
                // ROLLBACK_SPAWNER_RELEASE_VISUAL_REFRESH_20260630:
                // When a front holder starts moving to rail, release/spawn the next Pipe holder and refresh
                // visuals immediately so it emerges at that same timing.
                if (ProcessSpawners() && HolderVisualManager.HasInstance)
                    HolderVisualManager.Instance.RefreshSpawnerChangedColumns(LastSpawnerChangedColumns);
            }
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

            // ROLLBACK_HOLDER_DEPLOYDONE_COLFROM_HOLDER_20260615: START
            // evt.column 은 visual.column 인데, CompactColumns(Color-Remove의 RemoveRailAndQueueColor 경로)가
            // 배포중 홀더의 data column 을 newCol 로 재배치하고 _deployingHolderId[newCol] 를 재구축(979)해도,
            // RefreshAllPositions 가 배포중 visual 을 skip(606)해 visual.column 이 stale(oldCol)로 남는다.
            // 그러면 done 이벤트가 oldCol 을 실어와 _deployingHolderId[newCol] 가 영영 해제 안 됨 → 컬럼 데드락.
            // 홀더의 현재 data column 을 진실원본으로 사용(holder null 이면 evt.column 폴백).
            // 정상 경로에선 holder.column == evt.column 이라 동작 동일 → 무회귀.
            // 롤백: 아래 한 줄을  int col = evt.column;  으로 교체.
            int col = (holder != null) ? holder.column : evt.column;
            // ROLLBACK_HOLDER_DEPLOYDONE_COLFROM_HOLDER_20260615: END

            // ROLLBACK_HOLDER_DEPLOYDONE_OWNERSHIP_GUARD_20260615: START
            // 기존엔 소유권 확인 없이 _deployingHolderId[col] = -1 했다. evt.holderId 가 현재 컬럼 deployer
            // 가 아니면(이미 다른 holder 가 promotion 으로 점유) stale done 이벤트가 남의 예약을 지우고
            // 곧이어 waiter 를 promotion → 같은 컬럼에 deploying holder 2개 → 병렬 디플로이(연속공격).
            // UndoDeploy(714) 와 동일하게 owner 일치 시에만 clear+promotion 수행. 정상 동작 시엔 항상 일치하므로
            // 동작 동일, 버그 경로(stale)에서만 skip. (위 holder-state/frozen/AllEmpty 처리는 evt.holderId
            //  기준이라 가드 밖에서 항상 실행.)
            // 롤백: 아래 if (_deployingHolderId[col] == evt.holderId) 래퍼 제거하고 본문을 무조건 실행.
            if (_deployingHolderId[col] == evt.holderId)
            {
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
            }
            // ROLLBACK_HOLDER_DEPLOYDONE_OWNERSHIP_GUARD_20260615: END

            // Check if all holders are consumed
            if (AreAllHoldersEmpty())
            {
                EventBus.Publish(new OnAllHoldersEmpty());
            }
        }

        #endregion
    }
}
