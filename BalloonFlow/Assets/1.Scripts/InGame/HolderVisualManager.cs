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
        private const string SPAWNER_POOL_KEY = "Spawner";
        private const int MAX_COLUMNS = 5;
        private const int MAGAZINE_FONT_SIZE = 6;
        private const float DEPLOY_MOVE_SPEED = 12f;

        // 보관함 배치 수치 — 절대 최소값 보장 (프리팹 스케일 1.04 기준)
        private const float MIN_COL_SPACING      = 2.16f;    // 보관함 좌우 최소 간격 (+20%)
        private const float MIN_ROW_SPACING       = 2.59f;    // 보관함 앞뒤 최소 간격 (+20%)
        private const float MIN_DEPLOY_GAP        = 2.0f;     // 컨베이어 ~ 도착위치 최소 거리
        private const float MIN_RAIL_TO_QUEUE     = 3.5f;     // 컨베이어 ~ 보관함 1열 최소 거리

        // 비율 기준 (큰 필드에서 비례 확장)
        private const float RATIO_COL_SPACING     = 0.352f;   // 필드 폭 × (보관함+간격) (+20%)
        private const float RATIO_ROW_SPACING     = 0.374f;   // 필드 폭 × 행 간격 (+20%)
        private const float RATIO_DEPLOY_GAP      = 0.35f;    // 필드 폭 × 도착 거리
        private const float RATIO_RAIL_TO_QUEUE   = 0.65f;    // 필드 폭 × 보관함 거리

        #endregion

        #region Color Palette

        /// <summary>PixelArtConverter 28색 팔레트와 동기화.</summary>
        private static readonly Color[] COLORS =
        {
            new Color(252/255f, 106/255f, 175/255f),  //  0: HotPink
            new Color( 80/255f, 232/255f, 246/255f),  //  1: Cyan
            new Color(137/255f,  80/255f, 248/255f),  //  2: Purple
            new Color(254/255f, 213/255f,  85/255f),  //  3: Yellow
            new Color(115/255f, 254/255f, 102/255f),  //  4: Green
            new Color(253/255f, 161/255f,  76/255f),  //  5: Orange
            new Color(255/255f, 255/255f, 255/255f),  //  6: White
            new Color( 65/255f,  65/255f,  65/255f),  //  7: DarkGray
            new Color(110/255f, 168/255f, 250/255f),  //  8: SkyBlue
            new Color( 57/255f, 174/255f,  46/255f),  //  9: Forest
            new Color(252/255f,  94/255f,  94/255f),  // 10: Red
            new Color( 50/255f, 107/255f, 248/255f),  // 11: Blue
            new Color( 58/255f, 165/255f, 139/255f),  // 12: Teal
            new Color(231/255f, 167/255f, 250/255f),  // 13: Lavender
            new Color(183/255f, 199/255f, 251/255f),  // 14: Periwinkle
            new Color(106/255f,  74/255f,  48/255f),  // 15: Brown
            new Color(254/255f, 227/255f, 169/255f),  // 16: Cream
            new Color(253/255f, 183/255f, 193/255f),  // 17: Pink
            new Color(158/255f,  61/255f,  94/255f),  // 18: Wine
            new Color(167/255f, 221/255f, 148/255f),  // 19: Mint
            new Color( 89/255f,  46/255f, 126/255f),  // 20: Indigo
            new Color(220/255f, 120/255f, 129/255f),  // 21: Rose
            new Color(217/255f, 217/255f, 231/255f),  // 22: Silver
            new Color(111/255f, 114/255f, 127/255f),  // 23: Gray
            new Color(252/255f,  56/255f, 165/255f),  // 24: Magenta
            new Color(253/255f, 180/255f,  88/255f),  // 25: Amber
            new Color(137/255f,  10/255f,   8/255f),  // 26: Crimson
            new Color(111/255f, 175/255f, 177/255f),  // 27: Sage
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

        /// <summary>Chain 연결선: "id1_id2" → LineRenderer GameObject</summary>
        private readonly Dictionary<string, GameObject> _chainLines = new Dictionary<string, GameObject>();
        private int _queueColumns = 5;

        /// <summary>동적 계산: 풍선 필드 너비에 맞춘 열 간격</summary>
        private float _columnSpacing = 1.4f;
        /// <summary>동적 계산: 레일 바닥 - 갭</summary>
        private float _queueBaseZ = -5.0f;

        /// <summary>열별 독립 배치 큐. 열 단위로 순차, 열 간 동시 배치 가능.</summary>
        private Queue<int>[] _colQueues;
        private bool[] _colBusy;



        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            InitColArrays(5);
        }

        private void InitColArrays(int cols)
        {
            if (_colQueues != null && _colQueues.Length >= cols) return;
            _colQueues = new Queue<int>[cols];
            _colBusy = new bool[cols];
            for (int i = 0; i < cols; i++)
                _colQueues[i] = new Queue<int>();
        }

        private bool _boardFinished;

        private void LateUpdate()
        {
            if (_chainLines.Count > 0)
                UpdateChainLines();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Subscribe<OnHolderSelected>(HandleHolderSelected);
            EventBus.Subscribe<OnMagazineEmpty>(HandleMagazineEmpty);
            EventBus.Subscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Subscribe<OnBoardFailed>(HandleBoardFailed);
            EventBus.Subscribe<OnContinueApplied>(HandleContinueApplied);
            EventBus.Subscribe<OnHolderThawed>(HandleHolderThawed);
            EventBus.Subscribe<OnHolderRevealed>(HandleHolderRevealed);
            EventBus.Subscribe<OnFrozenHPChanged>(HandleFrozenHPChanged);
            EventBus.Subscribe<OnHolderUnlocked>(HandleHolderUnlocked);
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
            EventBus.Unsubscribe<OnHolderRevealed>(HandleHolderRevealed);
            EventBus.Unsubscribe<OnFrozenHPChanged>(HandleFrozenHPChanged);
            EventBus.Unsubscribe<OnHolderUnlocked>(HandleHolderUnlocked);
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
            InitColArrays(_queueColumns);

            // 보관함 가로폭 = 풍선 필드 가로폭에 맞춤
            ComputeDynamicLayout();

            // Group by column — Spawner는 열 맨 뒤에 배치 (관통 방지)
            var columnQueues = new Dictionary<int, List<HolderData>>();
            var columnSpawners = new Dictionary<int, List<HolderData>>();
            for (int i = 0; i < holders.Length; i++)
            {
                HolderData data = holders[i];
                if (data.isConsumed) continue;

                bool isSpawner = data.queueGimmick == GimmickManager.GIMMICK_SPAWNER_T
                              || data.queueGimmick == GimmickManager.GIMMICK_SPAWNER_O;

                var target = isSpawner ? columnSpawners : columnQueues;
                if (!target.ContainsKey(data.column))
                    target[data.column] = new List<HolderData>();
                target[data.column].Add(data);
            }

            // Spawn per column: regular holders first, then spawners
            int spawnedCount = 0;
            var allColumns = new HashSet<int>(columnQueues.Keys);
            foreach (var col in columnSpawners.Keys) allColumns.Add(col);

            foreach (int col in allColumns)
            {
                // 일반 보관함 + Spawner를 합쳐서 원래 row 순서대로 배치
                var allInCol = new List<HolderData>();
                if (columnQueues.TryGetValue(col, out var regularHolders))
                    allInCol.AddRange(regularHolders);
                if (columnSpawners.TryGetValue(col, out var spawners))
                    allInCol.AddRange(spawners);

                // holderId 순 (MapMaker 저장 순서 = row 순서 보존)
                allInCol.Sort((a, b) => a.holderId.CompareTo(b.holderId));

                for (int row = 0; row < allInCol.Count; row++)
                {
                    Vector3 pos = CalculateQueuePosition(col, row);
                    HolderVisual visual = CreateHolderVisual(allInCol[row], pos, col);
                    if (visual != null)
                    {
                        _holderVisuals[allInCol[row].holderId] = visual;
                        spawnedCount++;
                    }
                }
            }

            // Spawner 소환: 앞 보관함 + 대기 보관함 (풍선 생성 후이므로 색상 참조 가능)
            if (HolderManager.HasInstance)
            {
                HolderManager.Instance.ProcessSpawners(); // 앞 보관함
                HolderManager.Instance.ProcessSpawners(); // Spawner 안 대기 보관함
                for (int col = 0; col < _queueColumns; col++)
                    RepositionColumnHolders(col);
            }

            // Chain 연결선 생성
            RebuildChainLines();
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
            ClearChainLines();
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

            // Sync visual column from data column + 포물선 이동
            var columnRows = new Dictionary<int, int>();
            for (int i = 0; i < holders.Length; i++)
            {
                if (!_holderVisuals.TryGetValue(holders[i].holderId, out HolderVisual visual)) continue;
                if (visual.isDeploying || visual.isMovingToRail || visual.gameObject == null) continue;

                visual.column = holders[i].column;

                // 새 위치 계산
                if (!columnRows.ContainsKey(visual.column)) columnRows[visual.column] = 0;
                int row = columnRows[visual.column]++;
                Vector3 targetPos = CalculateQueuePosition(visual.column, row);

                // 포물선 비행 (랜덤 높이 + 랜덤 좌우 곡선)
                visual.gameObject.transform.DOKill();
                Vector3 startPos = visual.gameObject.transform.position;
                float arcHeight = Random.Range(1.5f, 3f);
                float sideOffset = Random.Range(-1.5f, 1.5f);
                Vector3 mid = (startPos + targetPos) * 0.5f;
                mid.y += arcHeight;
                mid.x += sideOffset;

                Vector3[] path = { startPos, mid, targetPos };
                float duration = Random.Range(0.4f, 0.7f);
                visual.gameObject.transform.DOPath(path, duration, PathType.CatmullRom)
                    .SetEase(Ease.OutQuad);

                visual.queuePosition = targetPos;

                // Apply front-row shader: row 0 = active outline, row 1+ = inactive + text alpha 25%
                if (visual.identifier != null)
                {
                    if (row == 0)
                        visual.identifier.SetActiveFrontRow();
                    else
                        visual.identifier.SetInactiveRow();
                }
                if (visual.magazineText != null)
                {
                    visual.magazineText.color = row == 0
                        ? Color.white
                        : new Color(1f, 1f, 1f, 0.25f);
                }
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
        /// 필드 폭 비율 기반 + 최소값 보장으로 보관함 배치 계산.
        /// 작은 필드에서도 보관함끼리 겹치지 않음.
        /// </summary>
        private float _rowSpacing = 1.8f;
        private float _deployGap = 2.0f;

        private void ComputeDynamicLayout()
        {
            CacheRailBottom();

            float fw = 8f;
            if (BoardTileManager.HasInstance)
                fw = BoardTileManager.Instance.FieldWidth;

            // 비율 vs 최소값 중 큰 값 사용
            _columnSpacing = Mathf.Max(fw * RATIO_COL_SPACING, MIN_COL_SPACING);
            _rowSpacing = Mathf.Max(fw * RATIO_ROW_SPACING, MIN_ROW_SPACING);
            _deployGap = Mathf.Max(fw * RATIO_DEPLOY_GAP, MIN_DEPLOY_GAP);
            float railToQueue = Mathf.Max(fw * RATIO_RAIL_TO_QUEUE, MIN_RAIL_TO_QUEUE);

            // 전체 보관함 폭이 필드 폭을 초과하면 축소 (단, MIN 이하로는 안 줄임)
            if (_queueColumns > 1)
            {
                float neededWidth = (_queueColumns - 1) * _columnSpacing;
                if (neededWidth > fw * 1.2f) // 필드 120%까지 허용
                    _columnSpacing = Mathf.Max(fw * 1.2f / (_queueColumns - 1), MIN_COL_SPACING);
            }

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

            // Spawner에 의해 새로 추가된 보관함 — Spawner 위치에서 생성, 정상 스케일
            // Spawner 위치 찾기
            Vector3 spawnerPos = CalculateQueuePosition(column, 1); // fallback
            foreach (var kvp2 in _holderVisuals)
            {
                if (kvp2.Value.column == column && kvp2.Value.gameObject != null)
                {
                    var spData = HolderManager.Instance.FindHolderPublic(kvp2.Value.holderId);
                    if (spData != null && (spData.queueGimmick == GimmickManager.GIMMICK_SPAWNER_T
                                        || spData.queueGimmick == GimmickManager.GIMMICK_SPAWNER_O))
                    {
                        spawnerPos = kvp2.Value.gameObject.transform.position;
                        break;
                    }
                }
            }

            // 비주얼 없는 일반 보관함 생성 (Spawner에서 소환된 보관함)
            HolderData[] allHolders = HolderManager.Instance.GetHolders();
            for (int i = 0; i < allHolders.Length; i++)
            {
                var hd = allHolders[i];
                if (hd.column != column || hd.isConsumed) continue;
                if (_holderVisuals.ContainsKey(hd.holderId)) continue;
                // Spawner 자체는 SpawnWaitingHolders에서 생성됨
                if (hd.queueGimmick == GimmickManager.GIMMICK_SPAWNER_T
                 || hd.queueGimmick == GimmickManager.GIMMICK_SPAWNER_O) continue;

                // Spawner 위치에서 생성 → 아래 리포지셔닝으로 앞 칸 이동
                Vector3 startPos = spawnerPos;
                HolderVisual newVisual = CreateHolderVisual(hd, startPos, column, false);
                if (newVisual != null)
                    _holderVisuals[hd.holderId] = newVisual;
            }


            // Spawner는 고정 위치 — 일반 보관함만 리포지셔닝
            var colHolders = _tempColumnHolders;
            colHolders.Clear();
            int spawnerCount = 0;
            foreach (var kvp in _holderVisuals)
            {
                HolderVisual v = kvp.Value;
                if (v.column == column && !v.isDeploying && !v.isMovingToRail && v.gameObject != null)
                {
                    var hData = HolderManager.Instance.FindHolderPublic(v.holderId);
                    if (hData != null && (hData.queueGimmick == GimmickManager.GIMMICK_SPAWNER_T
                                       || hData.queueGimmick == GimmickManager.GIMMICK_SPAWNER_O))
                    {
                        spawnerCount++;
                        continue;
                    }

                    colHolders.Add(v);
                }
            }

            // Sort regular holders by current Z descending (front first)
            colHolders.Sort((a, b) =>
            {
                if (a.gameObject == null || b.gameObject == null) return 0;
                return b.gameObject.transform.position.z.CompareTo(a.gameObject.transform.position.z);
            });

            // 일반 보관함 배치
            for (int row = 0; row < colHolders.Count; row++)
            {
                if (colHolders[row].gameObject == null) continue;

                Vector3 targetPos;
                if (row == 0)
                {
                    // 앞줄: 정상 위치
                    targetPos = CalculateQueuePosition(column, 0);
                }
                else if (spawnerCount > 0)
                {
                    // Spawner보다 살짝 앞에 배치
                    targetPos = spawnerPos + new Vector3(0f, 0f, 0.3f);
                }
                else
                {
                    targetPos = CalculateQueuePosition(column, row);
                }

                bool insideSpawner = row > 0 && spawnerCount > 0;

                colHolders[row].gameObject.transform.DOKill(false);
                colHolders[row].gameObject.transform.localScale = Vector3.one;

                // Spawner 안 대기: TEXT 숨김 / 앞줄: TEXT 보이기
                if (colHolders[row].magazineText != null)
                {
                    colHolders[row].magazineText.gameObject.SetActive(!insideSpawner);
                    // 비활성화(row 1+): 텍스트 투명도 50%
                    if (!insideSpawner && colHolders[row].magazineText != null)
                    {
                        colHolders[row].magazineText.color = row == 0
                            ? Color.white
                            : new Color(1f, 1f, 1f, 0.25f);
                    }
                }

                // 보관함 상태별 아웃라인
                if (colHolders[row].identifier != null)
                {
                    if (row == 0)
                        colHolders[row].identifier.SetActiveFrontRow(); // 검은 아웃라인
                    else
                        colHolders[row].identifier.SetInactiveRow(); // 아웃라인 없음
                }

                if (Vector3.Distance(colHolders[row].gameObject.transform.position, targetPos) > 0.05f)
                {
                    float dist = Vector3.Distance(colHolders[row].gameObject.transform.position, targetPos);
                    colHolders[row].gameObject.transform.DOMove(targetPos, dist / 4f).SetEase(Ease.OutQuad);
                }

                colHolders[row].queuePosition = targetPos;

                if (row == 0 && HolderManager.HasInstance)
                {
                    var data = HolderManager.Instance.FindHolderPublic(colHolders[row].holderId);
                    if (data != null && data.isHidden)
                        HolderManager.Instance.RevealHiddenHolder(colHolders[row].holderId);
                }
            }
        }

        #endregion


        #region Private Methods — Holder Visual Creation

        private HolderVisual CreateHolderVisual(HolderData data, Vector3 position, int column, bool spawnAnimation = false)
        {
            if (!ObjectPoolManager.HasInstance) return null;

            // Spawner 기믹이면 Spawner 프리팹 사용
            bool isSpawner = data.queueGimmick == GimmickManager.GIMMICK_SPAWNER_T
                          || data.queueGimmick == GimmickManager.GIMMICK_SPAWNER_O;
            bool isLockObj = data.isLockObject;
            string poolKey = isLockObj ? "Lock" : (isSpawner ? SPAWNER_POOL_KEY : HOLDER_POOL_KEY);
            GameObject obj = ObjectPoolManager.Instance.Get(poolKey, position, Quaternion.identity);
            if (obj == null) return null;

            obj.SetActive(true);
            obj.transform.localScale = Vector3.one; // 풀 재사용 시 스케일 초기화

            if (isSpawner)
            {
                obj.transform.localScale = Vector3.one * 0.7f;
            }
            else if (isLockObj)
            {
                // Lock: 보관함과 같은 크기
                obj.transform.localScale = Vector3.one;
            }

            HolderIdentifier ident = obj.GetComponent<HolderIdentifier>();
            if (ident != null)
            {
                ident.SetHolderId(data.holderId);
                ident.ShowDarts(data.magazineCount);
                ident.SetFrozen(data.isFrozen);
                if (data.isHidden)
                {
                    ident.SetHidden(true);
                    ident.SetHiddenAnim(true);
                }
                // Chain 기믹이면 Loop 활성화 (chainGroupId > 0)
                bool isChain = data.chainGroupId > 0;
                ident.SetChainLoop(isChain);
            }

            // Spawner visual
            if (data.queueGimmick == GimmickManager.GIMMICK_SPAWNER_T && ident != null)
                ident.SetSpawnerTransparent(true);
            else if (data.queueGimmick == GimmickManager.GIMMICK_SPAWNER_O && ident != null)
                ident.SetSpawnerTransparent(false); // opaque = default, but mark for identification

            // Hidden: Hidden Material 적용됨 (색상 건너뜀) / Frozen: 하늘색 톤 / 일반: 원래 색
            Color holderColor;
            if (data.isHidden)
                holderColor = Color.clear; // Hidden Material이 적용되었으므로 색상 스킵
            else if (data.isFrozen)
                holderColor = new Color(0.6f, 0.85f, 1f);
            else
                holderColor = GetColor(data.color);

            // Hidden이면 SetHidden에서 Material 적용 완료 → 색상 스킵
            if (!data.isHidden)
            {
                if (ident != null && ident.HasColorRenderers)
                    ident.ApplyColor(holderColor);
                else if (!isSpawner && !isLockObj)
                    ApplyColorToRenderers(obj, holderColor);
                // Spawner/Lock: 색상 적용 안 함 (프리팹 원본 유지)
            }

            TMP_Text textMesh = obj.GetComponentInChildren<TMP_Text>(true);
            if (textMesh != null)
            {
                // Frozen: frozenHP / Hidden: "?" / Spawner: 소환횟수 / 일반: 탄창 수
                string displayText;
                if (data.isHidden)
                    displayText = "?";
                else if (data.isFrozen)
                    displayText = data.frozenHP.ToString();
                else if (data.spawnerHP > 0)
                    displayText = data.spawnerHP.ToString();
                else
                    displayText = data.magazineCount.ToString();
                textMesh.text = displayText;
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
                // Dart + Box + 블러 + 애니메이터 원복 (풀 재사용 대비)
                if (visual.identifier != null)
                {
                    visual.identifier.ResetDarts();
                    visual.identifier.ResetBox();
                    visual.identifier.SetSelected(); // MPB 초기화
                    visual.identifier.ResetAnimator(); // 뚜껑 닫기
                    visual.identifier.SetChainLoop(false); // Chain Loop 비활성화
                }

                if (ObjectPoolManager.HasInstance)
                {
                    // Spawner 프리팹이면 Spawner 풀로 반환
                    bool isSpawnerVisual = visual.gameObject.name.Contains("Spawner");
                    ObjectPoolManager.Instance.Return(isSpawnerVisual ? SPAWNER_POOL_KEY : HOLDER_POOL_KEY, visual.gameObject);
                }
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

            _colQueues[visual.column].Enqueue(holderId);

            // 선택됨 → 블러 해제 + 원래 색상 표시 + 뚜껑 열기
            if (visual.identifier != null)
            {
                visual.identifier.SetSelected();
                visual.identifier.StartDeploy(); // 터치 시 바로 뚜껑 열림
            }

            // 즉시 이동 시작 (대기 없이)
            visual.isMovingToRail = true;

            // 기존 DOTween 킬 — RepositionColumnHolders의 DOMove와 충돌 방지
            if (visual.gameObject != null)
                visual.gameObject.transform.DOKill();

            RepositionColumnHolders(visual.column);
            StartCoroutine(DeployCoroutine(visual));
        }

        private IEnumerator DeployCoroutine(HolderVisual visual)
        {
            if (!RailManager.HasInstance || visual.gameObject == null)
            {
                yield break;
            }

            // ── Phase 1: Move holder to deploy point (또는 대기 위치) ──
            Vector3 deployPoint = GetDeployPoint(visual.column);

            // 같은 열에 이미 배치 중인 보관함이 있으면 바로 뒤에 대기
            bool hasDeploying = _colBusy[visual.column];
            Vector3 targetPoint = hasDeploying
                ? deployPoint + Vector3.back * _rowSpacing
                : deployPoint;

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
                float dist = Vector3.Distance(current, targetPoint);
                if (dist < 0.15f) break;

                Vector3 dir = (targetPoint - current).normalized;
                visual.gameObject.transform.position = current + dir * DEPLOY_MOVE_SPEED * Time.deltaTime;
                yield return null;
            }

            if (visual.gameObject != null)
            {
                visual.gameObject.transform.position = targetPoint;
                // deploy point 도착 펀치 (1회)
                visual.gameObject.transform.localScale = Vector3.one;
                visual.gameObject.transform.DOPunchScale(Vector3.one * 0.08f, 0.15f, 4, 0.3f);
            }

            visual.isMovingToRail = false;

            // ── Phase 1.5: 전역 순차 배치 — 다른 보관함 배치 완료까지 대기 ──
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

                int c = visual.column;
                // 열 내 순서 확인
                if (!_colBusy[c] && _colQueues[c].Count > 0 && _colQueues[c].Peek() == visual.holderId)
                {
                    _colQueues[c].Dequeue();
                    _colBusy[c] = true;
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

            // 대기 위치에서 실제 deploy point로 이동 (대기했던 경우)
            if (visual.gameObject != null && Vector3.Distance(visual.gameObject.transform.position, deployPoint) > 0.1f)
            {
                visual.gameObject.transform.DOKill();
                float moveDist = Vector3.Distance(visual.gameObject.transform.position, deployPoint);
                visual.gameObject.transform.DOMove(deployPoint, moveDist / DEPLOY_MOVE_SPEED).SetEase(Ease.OutQuad);
                yield return new WaitForSeconds(moveDist / DEPLOY_MOVE_SPEED);
            }

            // ── Phase 2: 배치 시작 (열 순차 — 내 차례) ──
            visual.isDeploying = true;
            // 뚜껑은 이미 터치 시 열림 (StartDeploy에서 호출됨)

            if (HolderManager.HasInstance)
                HolderManager.Instance.ConfirmOnRail(visual.holderId);

            if (!RailManager.HasInstance)
            {
                _colBusy[visual.column] = false;

                yield break;
            }
            RailManager rail = RailManager.Instance;

            // 다트 배치 기준점 = 레일 바닥 (보관함은 중간 지점에 서있지만, 다트는 레일로 Pop)
            float totalWidth = (_queueColumns - 1) * _columnSpacing;
            float startX = -totalWidth * 0.5f;
            float railX = startX + visual.column * _columnSpacing;
            Vector3 railAttachPoint = new Vector3(railX, _cachedRailY, _cachedRailZ);

            bool deployStarted = false;

            // deploy point progress를 한 번만 계산 (고정 위치)
            float fixedDeployProgress = rail.GetProgressAtWorldPos(railAttachPoint);
            rail.RegisterDeployPoint(visual.holderId, fixedDeployProgress);

            while (visual.magazineRemaining > 0 && visual.gameObject != null && !_boardFinished)
            {
                // 취소 체크
                if (_cancelledHolders.Contains(visual.holderId))
                {
                    _cancelledHolders.Remove(visual.holderId);
                    rail.UnregisterDeployPoint(visual.holderId);
                    _colBusy[visual.column] = false;
    
                    yield break;
                }

                // deploy point에 빈 공간이 있는지 체크 (slotSpacing 이내에 다트 없음)
                if (rail.IsProgressClear(fixedDeployProgress, visual.holderId))
                {
                    int dartId = rail.PlaceDartAtProgress(fixedDeployProgress, visual.color, visual.holderId);
                    if (dartId >= 0)
                    {
                        visual.magazineRemaining--;

                        if (!deployStarted)
                        {
                            deployStarted = true;
                            // 첫 다트 배치 → deploy point 활성화 (장애물로 전환)
                            rail.ActivateDeployPoint(visual.holderId);
                            if (visual.gameObject != null)
                            {
                                visual.gameObject.transform.localScale = Vector3.one;
                                visual.gameObject.transform.DOPunchScale(Vector3.one * 0.08f, 0.15f, 4, 0.3f);
                            }
                        }

                        LaunchDartChild(visual, rail.GetPositionAtDistance(fixedDeployProgress));

                        if (visual.magazineText != null)
                            visual.magazineText.SetText("{0}", visual.magazineRemaining);

                        if (HolderManager.HasInstance)
                            HolderManager.Instance.ConsumeMagazine(visual.holderId);

                        EventBus.Publish(new OnDartPlaced
                        {
                            dartId = dartId,
                            color = visual.color,
                            holderId = visual.holderId,
                            progress = fixedDeployProgress
                        });
                    }
                }

                yield return null;
            }

            // ── Phase 3: deploy point 해제 → frozen 다트 unfreeze ──
            rail.UnregisterDeployPoint(visual.holderId);

            // ── Phase 4: Cleanup ──
            CompleteDeployment(visual);
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

            _colBusy[col] = false;

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

        #region Private Methods — Chain Lines

        /// <summary>Chain 그룹 연결선 전체 재생성.</summary>
        private void RebuildChainLines()
        {
            ClearChainLines();
            if (!HolderManager.HasInstance) return;

            var processed = new HashSet<string>();
            foreach (var kvp in _holderVisuals)
            {
                var hData = HolderManager.Instance.FindHolderPublic(kvp.Value.holderId);
                if (hData == null || hData.chainGroupId < 0 || hData.isConsumed) continue;

                var members = HolderManager.Instance.GetChainGroup(hData.chainGroupId);
                for (int i = 0; i < members.Count; i++)
                {
                    for (int j = i + 1; j < members.Count; j++)
                    {
                        int idA = members[i], idB = members[j];
                        string key = idA < idB ? $"{idA}_{idB}" : $"{idB}_{idA}";
                        if (processed.Contains(key)) continue;
                        processed.Add(key);

                        if (!_holderVisuals.TryGetValue(idA, out HolderVisual vA) || vA.gameObject == null) continue;
                        if (!_holderVisuals.TryGetValue(idB, out HolderVisual vB) || vB.gameObject == null) continue;

                        CreateChainLine(key, vA, vB);
                    }
                }
            }
        }

        private void CreateChainLine(string key, HolderVisual a, HolderVisual b)
        {
            Color colorA = GetColor(a.color);
            Color colorB = GetColor(b.color);
            var mat = new Material(Shader.Find("Sprites/Default"));

            var go = new GameObject($"ChainLine_{key}");

            // A색 절반
            var lrA = go.AddComponent<LineRenderer>();
            lrA.positionCount = 2;
            lrA.startWidth = 0.15f;
            lrA.endWidth = 0.15f;
            lrA.useWorldSpace = true;
            lrA.sortingOrder = 5;
            lrA.startColor = colorA;
            lrA.endColor = colorA;
            lrA.material = mat;

            // B색 절반 — 별도 자식 오브젝트
            var goB = new GameObject($"ChainLineB_{key}");
            goB.transform.SetParent(go.transform, false);
            var lrB = goB.AddComponent<LineRenderer>();
            lrB.positionCount = 2;
            lrB.startWidth = 0.15f;
            lrB.endWidth = 0.15f;
            lrB.useWorldSpace = true;
            lrB.sortingOrder = 5;
            lrB.startColor = colorB;
            lrB.endColor = colorB;
            lrB.material = mat;

            _chainLines[key] = go;
        }

        /// <summary>매 프레임 Chain 연결선 위치 갱신.</summary>
        private void UpdateChainLines()
        {
            var removeKeys = new List<string>();
            foreach (var kvp in _chainLines)
            {
                string[] ids = kvp.Key.Split('_');
                if (ids.Length != 2) continue;
                int idA = int.Parse(ids[0]), idB = int.Parse(ids[1]);

                bool validA = _holderVisuals.TryGetValue(idA, out HolderVisual vA) && vA.gameObject != null;
                bool validB = _holderVisuals.TryGetValue(idB, out HolderVisual vB) && vB.gameObject != null;

                if (!validA || !validB)
                {
                    // 한쪽이 사라짐 → 연결선 제거
                    if (kvp.Value != null) Destroy(kvp.Value);
                    removeKeys.Add(kvp.Key);
                    continue;
                }

                // Chain 연결 위치: Y 높이 + 상대 방향으로 좌우 오프셋
                Vector3 baseA = vA.gameObject.transform.position;
                Vector3 baseB = vB.gameObject.transform.position;
                Vector3 dirAtoB = (baseB - baseA).normalized;
                Vector3 sideOffset = new Vector3(dirAtoB.x, 0f, dirAtoB.z).normalized * 0.4f;

                Vector3 posA = baseA + Vector3.up * 0.8f + sideOffset;  // A → B 방향 쪽
                Vector3 posB = baseB + Vector3.up * 0.8f - sideOffset;  // B → A 방향 쪽
                Vector3 mid = (posA + posB) * 0.5f;

                var lrA = kvp.Value.GetComponent<LineRenderer>();
                if (lrA != null)
                {
                    lrA.SetPosition(0, posA);
                    lrA.SetPosition(1, mid);
                }

                var lrB = kvp.Value.GetComponentInChildren<LineRenderer>();
                // GetComponentInChildren은 자기 자신도 반환하므로 자식만 찾기
                if (kvp.Value.transform.childCount > 0)
                {
                    var childLr = kvp.Value.transform.GetChild(0).GetComponent<LineRenderer>();
                    if (childLr != null)
                    {
                        childLr.SetPosition(0, mid);
                        childLr.SetPosition(1, posB);
                    }
                }
            }
            foreach (var k in removeKeys)
                _chainLines.Remove(k);
        }

        private void ClearChainLines()
        {
            foreach (var kvp in _chainLines)
            {
                if (kvp.Value != null) Destroy(kvp.Value);
            }
            _chainLines.Clear();
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

                // Chain 연결 보관함에 검은 아웃라인 표시
                if (holderData != null && holderData.chainGroupId >= 0)
                {
                    var chainMembers = HolderManager.Instance.GetChainGroup(holderData.chainGroupId);
                    foreach (int memberId in chainMembers)
                    {
                        if (_holderVisuals.TryGetValue(memberId, out HolderVisual memberVisual)
                            && memberVisual.identifier != null)
                        {
                            memberVisual.identifier.SetChainHighlight(true);
                        }
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
            if (_colQueues != null) for (int i = 0; i < _colQueues.Length; i++) { _colQueues[i].Clear(); _colBusy[i] = false; }
            StopAllCoroutines();
            ClearAllVisuals();
        }

        private void HandleBoardFailed(OnBoardFailed evt)
        {
            _boardFinished = true;
            if (_colQueues != null) for (int i = 0; i < _colQueues.Length; i++) { _colQueues[i].Clear(); _colBusy[i] = false; }
            StopAllCoroutines();
        }

        private void HandleHolderThawed(OnHolderThawed evt)
        {
            if (!_holderVisuals.TryGetValue(evt.holderId, out HolderVisual visual)) return;

            if (visual.identifier != null)
                visual.identifier.SetFrozen(false);

            // 해동 시 텍스트를 탄창 수로 복원
            if (visual.magazineText != null)
                visual.magazineText.text = visual.magazineRemaining.ToString();

            Color originalColor = GetColor(visual.color);
            if (visual.identifier != null && visual.identifier.HasColorRenderers)
                visual.identifier.ApplyColor(originalColor);
            else if (visual.gameObject != null)
                ApplyColorToRenderers(visual.gameObject, originalColor);
        }

        private void HandleFrozenHPChanged(OnFrozenHPChanged evt)
        {
            if (!_holderVisuals.TryGetValue(evt.holderId, out HolderVisual visual)) return;
            if (visual.magazineText != null)
                visual.magazineText.text = evt.remainingHP.ToString();

        }

        private void HandleHolderUnlocked(OnHolderUnlocked evt)
        {
            if (!_holderVisuals.TryGetValue(evt.holderId, out HolderVisual visual)) return;
            if (visual.gameObject == null) return;

            int col = visual.column;

            // Lock removal animation
            visual.gameObject.transform.DOScale(Vector3.zero, 0.3f).SetEase(DG.Tweening.Ease.InBack)
                .OnComplete(() =>
                {
                    ReturnHolderToPool(visual);
                    _holderVisuals.Remove(evt.holderId);
                    // Reposition holders in this column (fill the gap)
                    RepositionColumnHolders(col);
                });
        }

        private void HandleHolderRevealed(OnHolderRevealed evt)
        {
            if (!_holderVisuals.TryGetValue(evt.holderId, out HolderVisual visual)) return;

            // Hidden 해금 애니메이션
            if (visual.identifier != null)
                visual.identifier.TriggerHiddenEnd();

            // Hidden Material → 원래 색상 복원
            Color originalColor = GetColor(visual.color);
            if (visual.identifier != null && visual.identifier.HasColorRenderers)
                visual.identifier.ApplyColor(originalColor);
            else if (visual.gameObject != null)
                ApplyColorToRenderers(visual.gameObject, originalColor);

            // 텍스트도 "?" → 실제 탄창 수로 변경
            if (visual.magazineText != null)
                visual.magazineText.text = visual.magazineRemaining.ToString();
        }

        private void HandleContinueApplied(OnContinueApplied evt)
        {
            _boardFinished = false;
            if (_colQueues != null) for (int i = 0; i < _colQueues.Length; i++) { _colQueues[i].Clear(); _colBusy[i] = false; }

            // 비주얼 + 데이터 상태 동시 리셋
            foreach (var kvp in _holderVisuals)
            {
                HolderVisual visual = kvp.Value;

                // deploy point 해제
                if (visual.isDeploying && RailManager.HasInstance)
                    RailManager.Instance.UnregisterDeployPoint(visual.holderId);

                // 비주얼 상태 리셋
                visual.isDeploying = false;
                visual.isMovingToRail = false;
                visual.isWaiting = false;

                if (visual.gameObject != null)
                {
                    visual.gameObject.transform.DOKill();
                    visual.gameObject.transform.localScale = Vector3.one;
                }

                // HolderManager 데이터도 강제 리셋
                if (HolderManager.HasInstance)
                    HolderManager.Instance.UndoDeploy(visual.holderId);
            }

            // 모든 열 리포지셔닝 (큐 위치 복원 — 즉시 이동)
            for (int col = 0; col < _queueColumns; col++)
                RepositionColumnHolders(col);
        }

        #endregion
    }
}
