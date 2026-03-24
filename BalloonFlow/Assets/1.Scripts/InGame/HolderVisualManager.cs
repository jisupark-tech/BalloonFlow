using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// Manages visual representation of holders in Rail Overflow mode.
    /// Holders sit in a column-based queue, move up to the rail when selected,
    /// deploy darts onto empty passing slots, then disappear when magazine=0.
    /// Per column: 1 deploying (at rail) + 1 waiting (just below).
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Manager | Phase: 1
    /// DB Reference: Generated from Rail Overflow spec — column queue visual system
    /// </remarks>
    public class HolderVisualManager : SceneSingleton<HolderVisualManager>
    {
        #region Constants

        private const string HOLDER_POOL_KEY = "Holder";
        private const int MAX_COLUMNS = 5;
        private const int MAGAZINE_FONT_SIZE = 5;
        private const float DEPLOY_MOVE_SPEED = 12f;

        // 이미지 비율 기준 (풍선 필드 가로 = 1.0 기준)
        private const float RATIO_HOLDER_SPACING = 0.12f;    // 보관함 좌우 간격 (여유 추가: 9% → 12%)
        private const float RATIO_HOLDER_SIZE    = 0.173f;   // 보관함 크기
        private const float RATIO_RAIL_TO_DEPLOY = 0.35f;    // 컨베이어 ~ 도착위치
        private const float RATIO_RAIL_TO_QUEUE  = 0.65f;    // 컨베이어 ~ 보관함 1열
        private const float RATIO_ROW_SPACING    = 0.26f;    // 보관함 행 간격 (앞뒤 마진 추가: 21% → 26%)

        #endregion

        #region Color Palette

        private static readonly Color[] COLORS =
        {
            new Color(0.95f, 0.25f, 0.25f), // 0: Red
            new Color(0.25f, 0.55f, 0.95f), // 1: Blue
            new Color(0.25f, 0.85f, 0.35f), // 2: Green
            new Color(0.95f, 0.85f, 0.15f), // 3: Yellow
            new Color(0.80f, 0.30f, 0.90f), // 4: Purple
            new Color(0.95f, 0.55f, 0.15f), // 5: Orange
            new Color(0.40f, 0.90f, 0.90f), // 6: Cyan
            new Color(0.95f, 0.50f, 0.70f), // 7: Pink
        };

        #endregion

        #region Nested Types

        private class HolderVisual
        {
            public int holderId;
            public int color;
            public int column;
            public int magazineRemaining;
            public GameObject gameObject;
            public Renderer meshRenderer;
            public TMP_Text magazineText;
            public Vector3 queuePosition;
            public bool isDeploying;     // at rail, deploying darts
            public bool isWaiting;       // just below deploying holder
            public bool isMovingToRail;
            public HolderIdentifier identifier;
        }

        #endregion

        #region Fields

        private readonly Dictionary<int, HolderVisual> _holderVisuals = new Dictionary<int, HolderVisual>();
        private readonly HashSet<int> _cancelledHolders = new HashSet<int>();
        private int _queueColumns = 5;

        /// <summary>동적 계산: 풍선 필드 너비에 맞춘 열 간격</summary>
        private float _columnSpacing = 1.4f;
        /// <summary>동적 계산: 레일 바닥 - 갭</summary>
        private float _queueBaseZ = -5.0f;

        /// <summary>Global sequential deploy queue. Only one holder deploys at a time.</summary>
        private readonly Queue<int> _deployQueue = new Queue<int>();
        private bool _isProcessingDeploy;


        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
        }

        private bool _boardFinished;

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Subscribe<OnHolderSelected>(HandleHolderSelected);
            EventBus.Subscribe<OnMagazineEmpty>(HandleMagazineEmpty);
            EventBus.Subscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Subscribe<OnBoardFailed>(HandleBoardFailed);
            EventBus.Subscribe<OnContinueApplied>(HandleContinueApplied);
            EventBus.Subscribe<OnHolderThawed>(HandleHolderThawed);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnHolderSelected>(HandleHolderSelected);
            EventBus.Unsubscribe<OnMagazineEmpty>(HandleMagazineEmpty);
            EventBus.Unsubscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Unsubscribe<OnBoardFailed>(HandleBoardFailed);
            EventBus.Unsubscribe<OnContinueApplied>(HandleContinueApplied);
            EventBus.Unsubscribe<OnHolderThawed>(HandleHolderThawed);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Spawns visual holder GameObjects in the queue based on HolderManager data.
        /// </summary>
        public void SpawnWaitingHolders()
        {
            _boardFinished = false;
            _railBottomCached = false; // 새 레벨에서 레일 바닥 재계산
            ClearAllVisuals();

            if (!HolderManager.HasInstance) return;

            HolderData[] holders = HolderManager.Instance.GetHolders();
            if (holders == null || holders.Length == 0) return;

            _queueColumns = HolderManager.Instance.QueueColumns;

            // 보관함 가로폭 = 풍선 필드 가로폭에 맞춤
            ComputeDynamicLayout();

            Debug.Log($"[HolderVisualManager] SpawnWaitingHolders: holders={holders.Length}, queueCols={_queueColumns}, " +
                $"colSpacing={_columnSpacing:F2}, baseZ={_queueBaseZ:F2}, railZ={_cachedRailZ:F2}");

            // Group by column
            var columnQueues = new Dictionary<int, List<HolderData>>();
            for (int i = 0; i < holders.Length; i++)
            {
                HolderData data = holders[i];
                if (data.isConsumed) continue;

                if (!columnQueues.ContainsKey(data.column))
                    columnQueues[data.column] = new List<HolderData>();
                columnQueues[data.column].Add(data);
            }

            // Spawn per column, row by row
            int spawnedCount = 0;
            foreach (var kvp in columnQueues)
            {
                int col = kvp.Key;
                var colHolders = kvp.Value;

                for (int row = 0; row < colHolders.Count; row++)
                {
                    Vector3 pos = CalculateQueuePosition(col, row);
                    HolderVisual visual = CreateHolderVisual(colHolders[row], pos, col);
                    if (visual != null)
                    {
                        _holderVisuals[colHolders[row].holderId] = visual;
                        spawnedCount++;
                    }
                }
            }
            Debug.Log($"[HolderVisualManager] Spawned {spawnedCount}/{holders.Length} holder visuals");
        }

        /// <summary>
        /// Returns the color from the palette for the given index.
        /// </summary>
        public static Color GetColor(int colorIndex)
        {
            if (colorIndex >= 0 && colorIndex < COLORS.Length)
                return COLORS[colorIndex];
            return Color.white;
        }

        /// <summary>
        /// Returns true if the holder is in the front row (row 0) of its column.
        /// Only front-row holders are clickable.
        /// </summary>
        public bool IsInFrontRow(int holderId)
        {
            if (!_holderVisuals.TryGetValue(holderId, out HolderVisual visual))
                return false;
            if (visual.isDeploying || visual.isMovingToRail || visual.gameObject == null)
                return false;

            float holderZ = visual.gameObject.transform.position.z;
            return holderZ >= _queueBaseZ - _rowSpacing * 0.5f;
        }

        /// <summary>보관함의 GameObject 반환 (다트 Pop 연출용).</summary>
        public GameObject GetHolderGameObject(int holderId)
        {
            if (_holderVisuals.TryGetValue(holderId, out HolderVisual visual))
                return visual.gameObject;
            return null;
        }

        /// <summary>
        /// Clears all holder visuals and returns objects to pool.
        /// </summary>
        public void ClearAllVisuals()
        {
            StopAllCoroutines();
            _cancelledHolders.Clear();

            foreach (var kvp in _holderVisuals)
            {
                ReturnHolderToPool(kvp.Value);
            }
            _holderVisuals.Clear();
        }

        /// <summary>
        /// Cancels an active deploy coroutine for the given holder and returns it to queue position.
        /// Called by ContinueHandler when reverting active holders.
        /// </summary>
        public void CancelDeployAndReturnToQueue(int holderId)
        {
            _cancelledHolders.Add(holderId);

            if (!_holderVisuals.TryGetValue(holderId, out HolderVisual visual)) return;

            visual.isDeploying = false;
            visual.isWaiting = false;
            visual.isMovingToRail = false;

            // Kill any active DOTween on this object
            if (visual.gameObject != null)
            {
                visual.gameObject.transform.DOKill();
            }

            // Move back to queue
            RepositionColumnHolders(visual.column);
        }

        /// <summary>
        /// Removes a holder visual immediately (e.g. Color Remove booster consumed it).
        /// </summary>
        public void RemoveHolderVisual(int holderId)
        {
            _cancelledHolders.Add(holderId);

            if (!_holderVisuals.TryGetValue(holderId, out HolderVisual visual)) return;

            if (visual.gameObject != null)
                visual.gameObject.transform.DOKill();

            ReturnHolderToPool(visual);
            _holderVisuals.Remove(holderId);
        }

        /// <summary>
        /// Refreshes all visual positions from HolderData columns.
        /// Called after Shuffle booster changes column assignments.
        /// </summary>
        public void RefreshAllPositions()
        {
            if (!HolderManager.HasInstance) return;
            HolderData[] holders = HolderManager.Instance.GetHolders();
            if (holders == null) return;

            // Sync visual column from data column
            for (int i = 0; i < holders.Length; i++)
            {
                if (_holderVisuals.TryGetValue(holders[i].holderId, out HolderVisual visual))
                {
                    visual.column = holders[i].column;
                }
            }

            // Reposition all columns
            for (int col = 0; col < _queueColumns; col++)
            {
                RepositionColumnHolders(col);
            }
        }

        /// <summary>
        /// IsRailFull is no longer relevant in Rail Overflow mode.
        /// Always returns false — dart deployment is gated by slot availability, not holder count.
        /// </summary>
        public bool IsRailFull()
        {
            return false;
        }

        public int GetOnRailCount()
        {
            int count = 0;
            foreach (var kvp in _holderVisuals)
            {
                if (kvp.Value.isDeploying || kvp.Value.isMovingToRail) count++;
            }
            return count;
        }

        #endregion

        /// <summary>Returns the center position of the queue area (for camera targeting).</summary>
        public Vector3 CalculateQueueCenterPosition()
        {
            return new Vector3(0f, 0.1f, _queueBaseZ);
        }

        #region Private Methods — Queue Positioning

        /// <summary>
        /// 필드 폭 비율 기반으로 보관함 배치 계산.
        /// 보관함 간격, 행 간격, 컨베이어~보관함 거리 전부 필드 폭에 비례.
        /// </summary>
        private float _rowSpacing = 1.5f;
        private float _deployGap;  // 컨베이어 ~ 도착 위치

        private void ComputeDynamicLayout()
        {
            CacheRailBottom();

            float fw = 8f; // fallback
            if (BoardTileManager.HasInstance)
                fw = BoardTileManager.Instance.FieldWidth;

            // 보관함 열 간격 = 보관함 크기 + 보관함 간 간격
            _columnSpacing = fw * (RATIO_HOLDER_SIZE + RATIO_HOLDER_SPACING);

            // 전체 보관함 폭이 필드 폭을 초과하면 축소
            if (_queueColumns > 1)
            {
                float neededWidth = (_queueColumns - 1) * _columnSpacing;
                if (neededWidth > fw)
                    _columnSpacing = fw / (_queueColumns - 1);
            }

            // 행 간격, 도착 갭
            _rowSpacing = fw * RATIO_ROW_SPACING;
            _deployGap = fw * RATIO_RAIL_TO_DEPLOY;

            // 보관함 1열 Z = 컨베이어 바닥 - (컨베이어~보관함 거리)
            float railToQueue = fw * RATIO_RAIL_TO_QUEUE;
            _queueBaseZ = _cachedRailZ - railToQueue;
        }

        private Vector3 CalculateQueuePosition(int column, int row)
        {
            float totalWidth = (_queueColumns - 1) * _columnSpacing;
            float startX = -totalWidth * 0.5f;

            float x = startX + column * _columnSpacing;
            float z = _queueBaseZ - row * _rowSpacing;

            return new Vector3(x, 0.1f, z);
        }

        /// <summary>
        /// Returns the deploy point — where a holder attaches to the rail bottom edge
        /// to start deploying darts onto passing empty slots.
        /// </summary>
        /// <summary>캐시된 레일 바닥 Y/Z (레벨당 1회 계산)</summary>
        private float _cachedRailY = 0.1f;
        private float _cachedRailZ = 0f;
        private bool _railBottomCached;

        private Vector3 GetDeployPoint(int column)
        {
            if (!RailManager.HasInstance) return CalculateQueuePosition(column, 0) + Vector3.forward * 2f;

            float totalWidth = (_queueColumns - 1) * _columnSpacing;
            float startX = -totalWidth * 0.5f;
            float x = startX + column * _columnSpacing;

            CacheRailBottom();

            // 도착 위치 = 컨베이어 바닥 - deployGap (비율 기반)
            float deployZ = _cachedRailZ - _deployGap;
            return new Vector3(x, _cachedRailY, deployZ);
        }

        /// <summary>레일 바닥 Z 좌표를 반복하여 캐시.</summary>
        private void CacheRailBottom()
        {
            if (_railBottomCached) return;
            if (!RailManager.HasInstance) return;

            Vector3[] path = RailManager.Instance.GetRailPath();
            if (path != null && path.Length > 0)
            {
                _cachedRailY = path[0].y;
                _cachedRailZ = float.MaxValue;
                for (int i = 0; i < path.Length; i++)
                {
                    if (path[i].z < _cachedRailZ)
                        _cachedRailZ = path[i].z;
                }
            }
            _railBottomCached = true;
        }

        /// <summary>재사용 리스트 (GC 방지)</summary>
        private readonly List<HolderVisual> _tempColumnHolders = new List<HolderVisual>();

        private void RepositionColumnHolders(int column)
        {
            if (!HolderManager.HasInstance) return;

            var colHolders = _tempColumnHolders;
            colHolders.Clear();
            foreach (var kvp in _holderVisuals)
            {
                HolderVisual v = kvp.Value;
                if (v.column == column && !v.isDeploying && !v.isMovingToRail && v.gameObject != null)
                {
                    colHolders.Add(v);
                }
            }

            // Sort by current Z descending (front first)
            colHolders.Sort((a, b) =>
            {
                if (a.gameObject == null || b.gameObject == null) return 0;
                return b.gameObject.transform.position.z.CompareTo(a.gameObject.transform.position.z);
            });

            for (int row = 0; row < colHolders.Count; row++)
            {
                if (colHolders[row].gameObject == null) continue;
                Vector3 targetPos = CalculateQueuePosition(column, row);
                if (Vector3.Distance(colHolders[row].gameObject.transform.position, targetPos) > 0.05f)
                {
                    // 기존 이동 트윈 킬 후 새 이동
                    colHolders[row].gameObject.transform.DOKill(false);
                    float dist = Vector3.Distance(colHolders[row].gameObject.transform.position, targetPos);
                    colHolders[row].gameObject.transform.DOMove(targetPos, dist / 4f).SetEase(Ease.OutQuad);
                }
                colHolders[row].queuePosition = targetPos;
            }
        }

        #endregion

        #region Private Methods — Holder Visual Creation

        private HolderVisual CreateHolderVisual(HolderData data, Vector3 position, int column)
        {
            if (!ObjectPoolManager.HasInstance) return null;

            GameObject obj = ObjectPoolManager.Instance.Get(HOLDER_POOL_KEY, position, Quaternion.identity);
            if (obj == null) return null;

            obj.SetActive(true);

            HolderIdentifier ident = obj.GetComponent<HolderIdentifier>();
            if (ident != null)
            {
                ident.SetHolderId(data.holderId); // Init() 포함 → Dart/Box 자동 수집
                ident.ShowDarts(data.magazineCount);
                ident.SetFrozen(data.isFrozen);
            }

            // Hidden 보관함: 회색 + "?" 표시 / Frozen: 하늘색 톤
            Color holderColor;
            if (data.isHidden)
                holderColor = new Color(0.5f, 0.5f, 0.5f);
            else if (data.isFrozen)
                holderColor = new Color(0.6f, 0.85f, 1f);
            else
                holderColor = GetColor(data.color);

            // HolderIdentifier에 색상 대상이 할당되어 있으면 그것만 적용, 아니면 fallback
            if (ident != null && ident.HasColorRenderers)
                ident.ApplyColor(holderColor);
            else
                ApplyColorToRenderers(obj, holderColor);

            TMP_Text textMesh = obj.GetComponentInChildren<TMP_Text>(true);
            if (textMesh != null)
            {
                textMesh.text = data.isHidden ? "?" : data.magazineCount.ToString();
                textMesh.color = Color.white;
                textMesh.fontSize = MAGAZINE_FONT_SIZE;
                textMesh.alignment = TextAlignmentOptions.Center;
            }

            // 미선택 상태: 흰색 블러 + 흰색 아웃라인
            if (ident != null)
                ident.SetUnselected(true);

            return new HolderVisual
            {
                holderId = data.holderId,
                color = data.color,
                column = column,
                magazineRemaining = data.magazineCount,
                gameObject = obj,
                meshRenderer = obj.GetComponent<Renderer>(),
                magazineText = textMesh,
                queuePosition = position,
                isDeploying = false,
                isWaiting = false,
                isMovingToRail = false,
                identifier = ident
            };
        }

        /// <summary>Renderer 캐시 — GetComponentsInChildren 반복 호출 방지</summary>
        private static readonly Dictionary<int, Renderer[]> _holderRendererCache = new Dictionary<int, Renderer[]>();
        private static void ApplyColorToRenderers(GameObject obj, Color color)
        {
            Material shared = BalloonController.GetOrCreateSharedMaterial(color);
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].GetComponent<TMPro.TMP_Text>() != null) continue;
                string name = renderers[i].gameObject.name;
                if (name == "Shadow" || name.Contains("Particle")) continue;
                renderers[i].sharedMaterial = shared;
            }
        }

        private void ReturnHolderToPool(HolderVisual visual)
        {
            if (visual.gameObject != null)
            {
                // Dart + Box + 블러 원복 (풀 재사용 대비)
                if (visual.identifier != null)
                {
                    visual.identifier.ResetDarts();
                    visual.identifier.ResetBox();
                    visual.identifier.SetSelected(); // MPB 초기화
                }

                _holderRendererCache.Remove(visual.gameObject.GetInstanceID());
                if (ObjectPoolManager.HasInstance)
                    ObjectPoolManager.Instance.Return(HOLDER_POOL_KEY, visual.gameObject);
            }
            visual.gameObject = null;
        }

        #endregion

        #region Private Methods — Deploy Flow

        /// <summary>
        /// Moves a holder to a waiting position (just behind the deploy point).
        /// Called when the column already has a deploying holder.
        /// </summary>
        private void MoveToWaitingPosition(int holderId)
        {
            if (!_holderVisuals.TryGetValue(holderId, out HolderVisual visual))
                return;

            visual.isWaiting = true;

            Vector3 deployPoint = GetDeployPoint(visual.column);
            // Waiting position = 1.5 units behind the deploy point (toward queue)
            Vector3 waitPos = deployPoint + Vector3.back * 1.5f;

            if (visual.gameObject != null)
            {
                float dist = Vector3.Distance(visual.gameObject.transform.position, waitPos);
                visual.gameObject.transform.DOMove(waitPos, dist / DEPLOY_MOVE_SPEED).SetEase(Ease.OutQuad)
                    .OnComplete(() =>
                    {
                        if (visual.gameObject != null)
                        {
                            visual.gameObject.transform.localScale = Vector3.one;
                            visual.gameObject.transform.DOPunchScale(Vector3.one * 0.08f, 0.15f, 4, 0.3f);
                        }
                    });
            }
        }

        /// <summary>
        /// 클릭된 보관함을 즉시 deploy point로 이동 시작 + 배치 큐에 등록.
        /// 이동은 동시에 가능, 배치만 순차.
        /// </summary>
        private void StartDeploy(int holderId)
        {
            if (!_holderVisuals.TryGetValue(holderId, out HolderVisual visual))
                return;

            if (visual.isDeploying || visual.isMovingToRail) return;

            // 배치 순서 큐에 등록
            _deployQueue.Enqueue(holderId);

            // 선택됨 → 블러 해제 + 원래 색상 표시
            if (visual.identifier != null)
                visual.identifier.SetSelected();

            // 즉시 이동 시작 (대기 없이)
            visual.isMovingToRail = true;

            // 기존 DOTween 킬 — RepositionColumnHolders의 DOMove와 충돌 방지
            if (visual.gameObject != null)
                visual.gameObject.transform.DOKill();

            RepositionColumnHolders(visual.column);
            StartCoroutine(DeployCoroutine(visual));
        }

        /// <summary>
        /// 배치 큐에서 다음 대기 중인 holder를 활성화.
        /// 이미 deploy point에 도착해서 대기 중인 holder가 있으면 깨움.
        /// </summary>
        private void TryProcessNextDeploy()
        {
            // _isProcessingDeploy를 false로 세팅한 뒤 호출됨
            // 대기 중인 코루틴이 알아서 _isProcessingDeploy 체크 후 진행
        }

        private IEnumerator DeployCoroutine(HolderVisual visual)
        {
            if (!RailManager.HasInstance || visual.gameObject == null)
            {
                yield break;
            }

            // ── Phase 1: Move holder to deploy point ──
            Vector3 deployPoint = GetDeployPoint(visual.column);

            // 기존 DOTween 전부 킬 (RepositionColumnHolders의 DOMove 등)
            if (visual.gameObject != null)
                visual.gameObject.transform.DOKill();

            while (visual.gameObject != null)
            {
                if (_cancelledHolders.Contains(visual.holderId))
                {
                    _cancelledHolders.Remove(visual.holderId);
                    yield break;
                }

                Vector3 current = visual.gameObject.transform.position;
                float dist = Vector3.Distance(current, deployPoint);
                if (dist < 0.15f) break;

                Vector3 dir = (deployPoint - current).normalized;
                visual.gameObject.transform.position = current + dir * DEPLOY_MOVE_SPEED * Time.deltaTime;
                yield return null;
            }

            if (visual.gameObject != null)
            {
                visual.gameObject.transform.position = deployPoint;
                // deploy point 도착 펀치 (1회)
                visual.gameObject.transform.localScale = Vector3.one;
                visual.gameObject.transform.DOPunchScale(Vector3.one * 0.08f, 0.15f, 4, 0.3f);
            }

            visual.isMovingToRail = false;

            // ── Phase 1.5: 배치 순서 대기 — 내 차례가 올 때까지 deploy point에서 대기 ──
            int waitFrames = 0;
            const int MAX_WAIT_FRAMES = 3600; // 60초 타임아웃 (60fps)
            while (waitFrames < MAX_WAIT_FRAMES)
            {
                if (_boardFinished) yield break;
                if (_cancelledHolders.Contains(visual.holderId))
                {
                    _cancelledHolders.Remove(visual.holderId);
                    yield break;
                }

                if (!_isProcessingDeploy && _deployQueue.Count > 0 && _deployQueue.Peek() == visual.holderId)
                {
                    _deployQueue.Dequeue();
                    _isProcessingDeploy = true;
                    break;
                }

                waitFrames++;
                yield return null;
            }

            if (waitFrames >= MAX_WAIT_FRAMES)
            {
                Debug.LogWarning($"[HolderVisualManager] Holder {visual.holderId} timed out waiting for deploy turn.");
                yield break;
            }

            // ── Phase 2: 배치 시작 (순차 — 내 차례) ──
            visual.isDeploying = true;

            // Deploy 애니메이션 시작
            if (visual.identifier != null)
                visual.identifier.StartDeploy();

            if (HolderManager.HasInstance)
                HolderManager.Instance.ConfirmOnRail(visual.holderId);

            if (!RailManager.HasInstance)
            {
                _isProcessingDeploy = false;
                TryProcessNextDeploy();
                yield break;
            }
            RailManager rail = RailManager.Instance;

            // 다트 배치 기준점 = 레일 바닥 (보관함은 중간 지점에 서있지만, 다트는 레일로 Pop)
            float totalWidth = (_queueColumns - 1) * _columnSpacing;
            float startX = -totalWidth * 0.5f;
            float railX = startX + visual.column * _columnSpacing;
            Vector3 railAttachPoint = new Vector3(railX, _cachedRailY, _cachedRailZ);

            bool deployStarted = false;

            while (visual.magazineRemaining > 0 && visual.gameObject != null && !_boardFinished)
            {
                // ── CHECK 1: 취소 여부 ──
                if (_cancelledHolders.Contains(visual.holderId))
                {
                    _cancelledHolders.Remove(visual.holderId);
                    if (deployStarted)
                    {
                        rail.ClearActiveDeploySlot();
                        rail.UnfreezeAndReinsertAll();
                    }
                    _isProcessingDeploy = false;
                    TryProcessNextDeploy();
                    yield break;
                }

                // ── CHECK 2: 레일 바닥의 nearest 슬롯이 비어있으면 배치 ──
                int deploySlot = rail.GetNearestSlotIndex(railAttachPoint);

                if (rail.IsSlotEmpty(deploySlot))
                {
                    if (deployStarted)
                    {
                        rail.SetActiveDeploySlot(deploySlot);
                        rail.SetActiveDeployHolderId(visual.holderId);
                        FreezeApproachingDarts(deploySlot, visual.holderId);
                    }

                    int dartId = rail.PlaceDart(deploySlot, visual.color, visual.holderId);
                    if (dartId >= 0)
                    {
                        visual.magazineRemaining--;

                        if (!deployStarted)
                        {
                            deployStarted = true;
                            rail.SetActiveDeploySlot(deploySlot);
                            rail.SetActiveDeployHolderId(visual.holderId);

                            if (visual.gameObject != null)
                            {
                                visual.gameObject.transform.localScale = Vector3.one;
                                visual.gameObject.transform.DOPunchScale(Vector3.one * 0.08f, 0.15f, 4, 0.3f);
                            }
                        }

                        // Dart 자식 하나를 보관함에서 분리 → 슬롯으로 날림
                        LaunchDartChild(visual, rail.GetSlotWorldPosition(deploySlot));

                        if (visual.magazineText != null)
                            visual.magazineText.SetText("{0}", visual.magazineRemaining);

                        if (HolderManager.HasInstance)
                            HolderManager.Instance.ConsumeMagazine(visual.holderId);

                        EventBus.Publish(new OnDartPlacedOnSlot
                        {
                            slotIndex = deploySlot,
                            color = visual.color,
                            holderId = visual.holderId
                        });
                    }
                }

                yield return null;
            }

            // ── Phase 3: Deployment done — freeze 해제 ──
            if (deployStarted)
            {
                rail.ClearActiveDeploySlot();
                rail.UnfreezeAndReinsertAll();
            }

            // ── Phase 4: Cleanup ──
            CompleteDeployment(visual);

            _isProcessingDeploy = false;
            TryProcessNextDeploy();
        }

        /// <summary>
        /// HolderIdentifier의 Dart 슬롯에서 하나를 꺼내 슬롯 위치로 날림.
        /// </summary>
        private void LaunchDartChild(HolderVisual visual, Vector3 slotWorldPos)
        {
            if (visual.identifier == null) return;

            float dist = Vector3.Distance(
                visual.gameObject != null ? visual.gameObject.transform.position : slotWorldPos,
                slotWorldPos);
            float duration = Mathf.Clamp(dist * 0.15f, 0.25f, 0.5f);

            visual.identifier.LaunchNextDart(slotWorldPos, duration);
        }

        /// <summary>
        /// deploy point 바로 뒤(deploySlot - 1) 한 칸만 체크.
        /// 다트가 있으면 freeze. 빈 슬롯이면 아무것도 안 함 (벨트가 가져올 때까지 대기).
        /// 체인 전파(PropagateFreezeChain)가 뒤쪽으로 자동 확장.
        /// </summary>
        private void FreezeApproachingDarts(int deploySlot, int deployingHolderId)
        {
            if (!RailManager.HasInstance) return;
            RailManager rail = RailManager.Instance;

            int checkSlot = (deploySlot - 1 + rail.SlotCount) % rail.SlotCount;

            if (rail.IsSlotEmpty(checkSlot)) return;

            RailManager.SlotData slotData = rail.GetSlot(checkSlot);
            if (slotData.holderId == deployingHolderId) return;

            rail.FreezeDart(checkSlot);
        }

        private void CompleteDeployment(HolderVisual visual)
        {
            int col = visual.column;
            visual.isDeploying = false;

            // End Deploy 애니메이션
            if (visual.identifier != null)
                visual.identifier.EndDeploy();

            // Publish deployment done
            EventBus.Publish(new OnHolderDeploymentDone
            {
                holderId = visual.holderId,
                column = col
            });

            // Remove visual
            ReturnHolderToPool(visual);
            _holderVisuals.Remove(visual.holderId);

            // Reposition remaining holders in this column
            RepositionColumnHolders(col);
        }

        #endregion

        #region Private Methods — Event Handlers

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            StartCoroutine(SpawnAfterDelay());
        }

        private IEnumerator SpawnAfterDelay()
        {
            yield return null;
            SpawnWaitingHolders();
        }

        private void HandleHolderSelected(OnHolderSelected evt)
        {
            // Check if this holder is in waiting state (another holder deploying in same column)
            if (HolderManager.HasInstance)
            {
                HolderData holderData = null;
                HolderData[] allHolders = HolderManager.Instance.GetHolders();
                for (int i = 0; i < allHolders.Length; i++)
                {
                    if (allHolders[i].holderId == evt.holderId)
                    {
                        holderData = allHolders[i];
                        break;
                    }
                }

                if (holderData != null && holderData.isWaiting)
                {
                    // Move to waiting position (just behind deploy point), do NOT start deploy
                    MoveToWaitingPosition(evt.holderId);
                    return;
                }
            }

            StartDeploy(evt.holderId);
        }

        private void HandleMagazineEmpty(OnMagazineEmpty evt)
        {
            // Magazine empty notification — deployment coroutine handles cleanup
        }

        private void HandleBoardCleared(OnBoardCleared evt)
        {
            _boardFinished = true;
            _isProcessingDeploy = false;
            _deployQueue.Clear();
            StopAllCoroutines();
            ClearAllVisuals();
        }

        private void HandleBoardFailed(OnBoardFailed evt)
        {
            _boardFinished = true;
            _isProcessingDeploy = false;
            _deployQueue.Clear();
            StopAllCoroutines();
        }

        private void HandleHolderThawed(OnHolderThawed evt)
        {
            if (!_holderVisuals.TryGetValue(evt.holderId, out HolderVisual visual)) return;

            // BoxFrozen → Box 전환
            if (visual.identifier != null)
                visual.identifier.SetFrozen(false);

            // 색상 복원 (Frozen 하늘색 → 원래 색)
            Color originalColor = GetColor(visual.color);
            if (visual.identifier != null && visual.identifier.HasColorRenderers)
                visual.identifier.ApplyColor(originalColor);
            else if (visual.gameObject != null)
                ApplyColorToRenderers(visual.gameObject, originalColor);
        }

        private void HandleContinueApplied(OnContinueApplied evt)
        {
            _boardFinished = false;
            _isProcessingDeploy = false;
            _deployQueue.Clear();
            Debug.Log("[HolderVisualManager] Continue applied — holder deployment resumed.");
        }

        #endregion
    }
}
