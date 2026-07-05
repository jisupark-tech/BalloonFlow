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
        private const int MAGAZINE_FONT_SIZE = 8;
        private const int HIDDEN_MAGAZINE_FONT_SIZE = 10;
        // [TMP 부하 2026-06-10] 탄창 텍스트 표시 행 게이트 — row 0~4 만 표시, row 5+ 는 GO 비활성.
        //   기존엔 큐 전체(5열 × n행 = 5n 개)의 TMP 가 전부 살아있었음. TMP SDF 는 투명 쿼드라 alpha 50% 라도
        //   드로우+오버드로우 발생 → 깊은 큐 레벨에서 부하. 큐 전진/재배치 시마다 행 기준 재평가됨.
        //   롤백: 이 const + ApplyMagazineTextRowVisibility 호출 3곳 제거.
        private const int MAGAZINE_TEXT_VISIBLE_ROWS = 5;

        // [HOLDER_LAZY_SPAWN 2026-06-11] 보관함 비주얼 지연 스폰 행 임계 — 레벨 시작 시 열당 앞 N행만
        // 풀에서 꺼내 활성화, 뒤 행은 행 당김으로 임계에 들어올 때 RepositionColumnHolders 가 생성.
        // 73홀더 레벨의 로드 스파이크(풀 Get + GetComponentsInChildren 다수 + 색/TMP 세팅 일괄) 제거 +
        // 상시 활성 렌더러 감소. 로직은 HolderManager 데이터 구동이라 게임플레이 무영향.
        // 예외(즉시 스폰): Spawner(위치 앵커), Chain 그룹(연결선). 롤백: 게이트 2곳(초기 스폰/재배치) 제거.
        private const int VISIBLE_HOLDER_ROWS = 5;
        private readonly List<HolderData> _tempLazyColumnData = new List<HolderData>(16);
        // ROLLBACK_PIPE_PAYLOAD_POP_20260624 (#3): released Pipe payload 가 파이프에서 팝업(Scale Up) 중인 동안
        // RepositionColumnHolders 의 즉시 scale 세팅/DOKill 이 트윈을 끊지 않도록 추적.
        private readonly HashSet<int> _poppingScale = new HashSet<int>();
        private readonly HashSet<int> _glassPipePreviewPayloads = new HashSet<int>();
        private readonly List<int> _glassPipePreviewScratch = new List<int>(8);

        private static void ApplyMagazineTextRowVisibility(HolderVisual visual, int row)
        {
            if (visual == null || visual.magazineText == null) return;
            bool show = row < MAGAZINE_TEXT_VISIBLE_ROWS;
            if (visual.magazineText.gameObject.activeSelf != show)
                visual.magazineText.gameObject.SetActive(show);
            RestoreSpawnerTextMaterialPreset(visual);
        }

        // ROLLBACK_SPAWNER_TEXT_MATERIAL_PRESET_20260625:
        // Spawner/Glass Pipe count text uses its authored TMP Material Preset on the prefab.
        // Generic holder row styling/count updates must not replace that preset.
        private static void RestoreSpawnerTextMaterialPreset(HolderVisual visual)
        {
            if (visual == null || !visual.isSpawnerVisual || visual.magazineText == null || visual.spawnerTextMaterialPreset == null)
                return;

            if (visual.magazineText.fontSharedMaterial != visual.spawnerTextMaterialPreset)
                visual.magazineText.fontSharedMaterial = visual.spawnerTextMaterialPreset;
        }
        private static readonly Color HIDDEN_MAGAZINE_COLOR = new Color(1f, 1f, 1f, 1f); // 명세: opacity 255 고정
        // ROLLBACK_DEPLOY_MOVE_SPEED_FASTER_20260615: 12 → 24.
        //   Hand 사용 시 홀더가 deploy point 로 상승하는 동안 카메라가 복귀(MoveBack 0.5s)하는데, 12 속도면
        //   queue→rail 거리(~6+units)에서 0.5s+ 걸려 홀더가 카메라보다 늦게 도착. 속도를 2x 올려 카메라(0.5s)
        //   보다 먼저 도착하게 함. (x2 모드에서 이미 24 로 스케일되던 값 = 검증된 속도.)
        //   주의: 모든 deploy(일반 탭 포함)가 2x 빨라짐(더 스내피). 롤백: 12f 로 환원.
        private static readonly Color INACTIVE_MAGAZINE_COLOR = new Color(1f, 1f, 1f, 0.5f);
        private static readonly Color FROZEN_MAGAZINE_COLOR = new Color(1f, 1f, 1f, 128f / 255f);

        private static bool IsFrozenHolderVisual(HolderVisual visual)
        {
            if (visual == null || visual.isSpawnerVisual || !HolderManager.HasInstance)
                return false;

            HolderData data = HolderManager.Instance.FindHolderPublic(visual.holderId);
            return data != null && data.isFrozen;
        }

        private static void ApplyHolderMagazineTextStyle(HolderVisual visual, bool isFrontActive, bool isHidden)
        {
            if (visual == null || visual.magazineText == null) return;

            if (visual.isSpawnerVisual)
            {
                RestoreSpawnerTextMaterialPreset(visual);
                return;
            }

            if (isHidden)
            {
                visual.magazineText.color = HIDDEN_MAGAZINE_COLOR;
                visual.magazineText.fontSize = HIDDEN_MAGAZINE_FONT_SIZE;
                return;
            }

            // ROLLBACK_FROZEN_BOX_HIT_FX_20260626:
            // Frozen Dart Box count text uses alpha 128/255 regardless of queue row.
            visual.magazineText.color = IsFrozenHolderVisual(visual)
                ? FROZEN_MAGAZINE_COLOR
                : (isFrontActive ? Color.white : INACTIVE_MAGAZINE_COLOR);
            visual.magazineText.fontSize = MAGAZINE_FONT_SIZE;
        }

        private const float DEPLOY_MOVE_SPEED = 24f;
        // ROLLBACK_DEPLOY_DEBUG_LOGS:
        // Set BALLOONFLOW_DEPLOY_DEBUG to restore verbose deploy/deadlock diagnostics. Keeping this
        // off in play builds prevents string formatting and logcat overhead during dense placement.
#if BALLOONFLOW_DEPLOY_DEBUG
        private static readonly bool DEPLOY_DEBUG_ENABLED = true;
#else
        private static readonly bool DEPLOY_DEBUG_ENABLED = false;
#endif

        /// <summary>유저 가속(홀드/x2 토글) 반영된 보관함 이동 속도.
        /// 벨트와 동일 배율로 스케일 → 2x 토글 시 보관함도 2x 빠르게 배치점 도달.</summary>
        private float EffectiveDeployMoveSpeed
            => DEPLOY_MOVE_SPEED * (RailManager.HasInstance ? RailManager.Instance.UserSpeedMultiplier : 1f);

        // 보관함 배치 수치 — 절대 최소값 보장 (프리팹 스케일 1.04 기준)
        private const float MIN_COL_SPACING      = 2.5f;    // 보관함 좌우 최소 간격
        private const float MIN_ROW_SPACING       = 2.5f;    // 보관함 앞뒤 최소 간격
        private const float MIN_DEPLOY_GAP        = 2f;     // 컨베이어 ~ 도착위치 최소 거리
        private const float MIN_RAIL_TO_QUEUE     = 5f;     // 컨베이어 ~ 보관함 1열 최소 거리

        // 같은 열에 대기 중인 보관함의 월드 Z 좌표(모든 레벨 공통) — _rowSpacing 동적 계산이 너무 멀어 보였던 문제 보정
        private const float WAITING_HOLDER_Z = -9.2f;

        // 비율 기준 (큰 필드에서 비례 확장)
        private const float RATIO_COL_SPACING     = 0.352f;   // 필드 폭 × (보관함+간격) (+20%)
        private const float RATIO_ROW_SPACING     = 0.374f;   // 필드 폭 × 행 간격 (+20%)
        private const float RATIO_DEPLOY_GAP      = 0.2f;    // 필드 폭 × 도착 거리
        private const float RATIO_RAIL_TO_QUEUE   = 0.65f;    // 필드 폭 × 보관함 거리

        private const float CHAIN_LINE_WIDTH      = 0.30f;   // Chain line thickness. Previous value was 0.15f.
        private const float CHAIN_LINE_Y_OFFSET   = 1.05f;
        private const float CHAIN_LINE_EDGE_RATIO = 0.34f;
        private const float CHAIN_LINE_MIN_LENGTH = 0.45f;
        private const float PIPE_INNER_Z_OFFSET   = 0.38f;
        private const float PIPE_INNER_SCALE      = 0.86f;

        #endregion

        [System.Diagnostics.Conditional("BALLOONFLOW_DEPLOY_DEBUG")]
        private static void LogDeployDebug(string message)
        {
            if (!DEPLOY_DEBUG_ENABLED) return;
            Debug.Log(message);
        }

        // ROLLBACK_HOLDER_DEPLOY_PUNCH:
        // Define BALLOONFLOW_HOLDER_DEPLOY_PUNCH or restore the direct DOPunchScale calls below if
        // the holder bounce visual is required. Keeping it symbol-gated removes DOTween allocations
        // during dense deploy without touching placement/attack/miss logic.
        [System.Diagnostics.Conditional("BALLOONFLOW_HOLDER_DEPLOY_PUNCH")]
        private static void PlayHolderPunch(Transform target, Vector3 strength, float duration, int vibrato, float elasticity)
        {
            if (target == null) return;
            target.DOPunchScale(strength, duration, vibrato, elasticity);
        }

        // ROLLBACK_HOLDER_DEPLOY_PUNCH:
        // Same rollback as PlayHolderPunch. This helper exists for sequence-based blocker feedback.
        [System.Diagnostics.Conditional("BALLOONFLOW_HOLDER_DEPLOY_PUNCH")]
        private static void AppendHolderPunch(Sequence seq, Transform target, Vector3 strength, float duration, int vibrato, float elasticity)
        {
            if (seq == null || target == null) return;
            seq.Append(target.DOPunchScale(strength, duration, vibrato, elasticity));
        }

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
            new Color(174/255f, 178/255f, 194/255f),  // 22: Silver — [2026-06-12] #D9D9E7→#AEB2C2, 흰색(6)과 구분 강화
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
            public bool isSpawnerVisual;
            public Material spawnerTextMaterialPreset;
            public Vector3 queuePosition;
            public bool isDeploying;     // at rail, deploying darts
            public bool isWaiting;       // just below deploying holder
            public bool isMovingToRail;
            public HolderIdentifier identifier;
            /// <summary>StartDeploy 마다 증가. DeployCoroutine 캡처 후 mismatch 시 stale로 간주, yield break.
            /// Continue/Cancel 시 visual을 무효화하지 않고도 OLD 코루틴을 안전하게 종료.</summary>
            public int deployGeneration;

            /// <summary>사용자 요구: 자기 holder 의 마지막 spawn dart ID. 다음 spawn 시 그 dart 의 현재 progress - physGap 위치에 spawn → cluster 자연 형성 + Deploy 연속성.</summary>
            public int lastSpawnedDartId;

            /// <summary>Phase 2 v1 — 자기 cluster head 가 다른 holder 의 활성 deploy point 도달 시 true.
            /// DeployCoroutine 이 자기 cluster freeze 후 spawn pause. blockingHolder 가 없어지면 unfreeze + spawn 재개.</summary>
            public bool isClusterFrozen;

            /// <summary>Deadlock 으로 pause 중일 때 1회 로그 후 true. 매 frame 로그 spam 방지.</summary>
            public bool deadlockPauseLogged;
        }

        #endregion

        #region Fields

        private readonly Dictionary<int, HolderVisual> _holderVisuals = new Dictionary<int, HolderVisual>();
        private readonly HashSet<int> _cancelledHolders = new HashSet<int>();

        /// <summary>특정 holderId를 컬럼 큐에서 제거 (취소/이탈 시 후속 보관함이 헤드에 진입 가능하도록).
        /// Queue<T>는 임의 제거를 직접 지원 안 해서 재빌드.</summary>
        private void RemoveFromColumnQueue(int column, int holderId)
        {
            if (_colQueues == null || column < 0 || column >= _colQueues.Length) return;
            var q = _colQueues[column];
            if (q == null || q.Count == 0) return;

            int n = q.Count;
            for (int i = 0; i < n; i++)
            {
                int id = q.Dequeue();
                if (id != holderId) q.Enqueue(id);
            }
        }

        /// <summary>특정 holderId가 컬럼 큐에 있는지 확인 (StartDeploy 중복 enqueue 방지).</summary>
        private bool ColumnQueueContains(int column, int holderId)
        {
            if (_colQueues == null || column < 0 || column >= _colQueues.Length) return false;
            var q = _colQueues[column];
            if (q == null || q.Count == 0) return false;
            foreach (int id in q) if (id == holderId) return true;
            return false;
        }

        private void ResetDeployQueues()
        {
            if (_colQueues != null)
            {
                for (int i = 0; i < _colQueues.Length; i++)
                {
                    _colQueues[i].Clear();
                    _colBusy[i] = false;
                }
            }
        }

        private void AbortDeploy(HolderVisual visual, bool undoHolderState)
        {
            if (visual == null) return;

            if (undoHolderState)
            {
                RemoveFromColumnQueue(visual.column, visual.holderId);
            }

            if (_colBusy != null && visual.column >= 0 && visual.column < _colBusy.Length)
                _colBusy[visual.column] = false;

            if (RailManager.HasInstance)
            {
                RailManager.Instance.UnregisterDeployPoint(visual.holderId);
                RailManager.Instance.ReleaseHolderReservation(visual.holderId);
                RailManager.Instance.ExitDeployPlacement(visual.holderId);
            }

            visual.isDeploying = false;
            visual.isMovingToRail = false;
            visual.isWaiting = false;
            visual.isClusterFrozen = false;

            if (undoHolderState && HolderManager.HasInstance)
                HolderManager.Instance.UndoDeploy(visual.holderId);
        }

        /// <summary>Chain 연결선: "id1_id2" → LineRenderer GameObject</summary>
        private readonly Dictionary<string, GameObject> _chainLines = new Dictionary<string, GameObject>();
        // ROLLBACK_CHAIN_LINK_TOPOLOGY_STABLE_20260630:
        //   체인 연결 토폴로지(어느 멤버끼리 잇는지)는 '초기 레이아웃'에서 한 번 정해 고정해야 한다.
        //   이전엔 RebuildChainLines 가 이동할 때마다 '현재 위치' 로 MST 를 재계산해, 비-멤버 홀더가 빠져
        //   체인 멤버가 당겨지면 새로 가까워진 엉뚱한 쌍(예: C1-1↔C1-3)을 이었다(대각 C1-2↔C1-3 누락).
        //   groupId 별로 '경로 순서' 를 초기 1회 계산해 캐시하고, 이후엔 그 순서대로 연속한 멤버만 잇는다
        //   → 멤버가 이동/소비돼도 시퀀스 유지(대각선 연결 정상). ClearAllVisuals 에서 캐시 초기화.
        private readonly Dictionary<int, List<int>> _chainOrderByGroup = new Dictionary<int, List<int>>();
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
            EventBus.Subscribe<OnHolderClickAnim>(HandleHolderClickAnim);
            EventBus.Subscribe<OnHolderColumnBlocked>(HandleHolderColumnBlocked);
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
            EventBus.Unsubscribe<OnHolderClickAnim>(HandleHolderClickAnim);
            EventBus.Unsubscribe<OnHolderColumnBlocked>(HandleHolderColumnBlocked);
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
                if (!isSpawner && !data.IsQueueVisible)
                    continue;

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

                // ROLLBACK_CHAIN_DEEP_EAGER_CONTIGUOUS_20260625: 깊은 행(≥VISIBLE_HOLDER_ROWS) Chain 은 즉시
                //   스폰되는데 그 앞 lazy 일반 홀더가 안 생기면 RepositionColumnHolders 의 activeRow(스폰된
                //   비주얼만 카운트)가 미스폰분을 누락 → Chain 이 앞으로 당겨져 순서가 꼬였다(세팅과 다르게 배치).
                //   → 그 열에 행 R 의 Chain 이 있으면 R 까지 연속 스폰(중간 lazy 포함)해 activeRow 정합 보장.
                int colVisibleRows = VISIBLE_HOLDER_ROWS;
                for (int k = allInCol.Count - 1; k >= VISIBLE_HOLDER_ROWS; k--)
                    if (allInCol[k].chainGroupId >= 0) { colVisibleRows = k + 1; break; }

                for (int row = 0; row < allInCol.Count; row++)
                {
                    // [HOLDER_LAZY_SPAWN 2026-06-11] 레벨 시작 시 앞 VISIBLE_HOLDER_ROWS 행만 비주얼 생성.
                    // 뒤 행은 행이 당겨져 임계 안에 들어올 때 RepositionColumnHolders 가 풀에서 꺼낸다.
                    // 로직(배포/실패/스포너/체인)은 HolderManager 데이터 구동이라 비주얼 지연은 게임플레이 무영향.
                    // 예외: Spawner(위치 앵커 + 고정 표시)·Chain 그룹(연결선이 멤버 비주얼 필요)은 즉시 생성.
                    HolderData hd = allInCol[row];
                    bool isSpawnerHolder = hd.queueGimmick == GimmickManager.GIMMICK_SPAWNER_T
                                        || hd.queueGimmick == GimmickManager.GIMMICK_SPAWNER_O;
                    if (row >= colVisibleRows && !isSpawnerHolder && hd.chainGroupId < 0)
                        continue; // 지연 스폰 대상 (colVisibleRows 가 깊은 Chain 까지 연속 보장)
                    // ROLLBACK_PIPE_BEHIND_VISIBLE_20260624 (#2): '미방출 payload(파이프 안)'만 숨김.
                    // 파이프에 맵핑 안 된 뒤 홀더는 일반 홀더처럼 보이고 5행까지 표시됨(아래 row 임계).
                    if (!isSpawnerHolder && !hd.IsQueueVisible)
                        continue;

                    Vector3 pos = CalculateQueuePosition(col, row);
                    HolderVisual visual = CreateHolderVisual(hd, pos, col);
                    if (visual != null)
                    {
                        _holderVisuals[hd.holderId] = visual;
                        spawnedCount++;
                        // [TMP 부하 2026-06-10] 초기 스폰부터 row 2+ 텍스트 비활성 (재배치 전까지 전부 켜져있던 문제).
                        ApplyMagazineTextRowVisibility(visual, row);
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

        /// <summary>Hand 부스터 하이라이트 대상 행 수 (각 column 의 앞쪽 N 행).</summary>
        private const int HAND_HIGHLIGHT_TOP_ROWS = 5;

        // 부스터 토글 시점 외에는 사용되지 않는 재사용 풀. 매 호출 Clear 후 재충전.
        private readonly Dictionary<int, List<HolderVisual>> _tempHighlightByCol = new Dictionary<int, List<HolderVisual>>();
        private static readonly System.Comparison<HolderVisual> s_highlightZDescComparer =
            (a, b) => b.gameObject.transform.position.z.CompareTo(a.gameObject.transform.position.z);

        /// <summary>
        /// Hand(SelectTool) 부스터 활성 동안 큐의 클릭 가능한 보관함에 stroke + yoyo idle 표시.
        /// active=true: column 별로 앞에서 HAND_HIGHLIGHT_TOP_ROWS 행까지 켬 (deploy/이동 중 holder 제외).
        /// active=false: 조건 무시 전체 끔 → 상태 누수 차단 (선택 완료/취소/팝업 close 시).
        /// </summary>
        public void SetHandSelectionHighlightActive(bool active)
        {
            // column 별 버킷 재구성 (active/inactive 양쪽에서 동일 분류 사용).
            foreach (var bucket in _tempHighlightByCol.Values)
                bucket.Clear();

            foreach (var kvp in _holderVisuals)
            {
                HolderVisual visual = kvp.Value;
                if (visual == null || visual.identifier == null) continue;
                if (visual.gameObject == null) continue;
                if (visual.isDeploying || visual.isMovingToRail) continue;

                if (!_tempHighlightByCol.TryGetValue(visual.column, out List<HolderVisual> bucket))
                {
                    bucket = new List<HolderVisual>(8);
                    _tempHighlightByCol[visual.column] = bucket;
                }
                bucket.Add(visual);
            }

            if (!active)
            {
                // Hand 부스터 종료 → row 기반 원상복구. 패턴: RepositionColumnHolders 행-스타일 (line 939-975).
                // ROLLBACK: 본 분기의 행 복원 루프를 삭제하고 `foreach _holderVisuals → SetControlBoxStrokeActive(false)` 만 남기면 이전 동작 복원.
                foreach (var bucket in _tempHighlightByCol.Values)
                {
                    if (bucket.Count == 0) continue;
                    bucket.Sort(s_highlightZDescComparer);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        HolderVisual visual = bucket[i];
                        visual.identifier.SetControlBoxStrokeActive(false);

                        bool isHidden = false;
                        if (HolderManager.HasInstance)
                        {
                            var data = HolderManager.Instance.FindHolderPublic(visual.holderId);
                            isHidden = data != null && data.isHidden;
                        }
                        if (i == 0)
                            visual.identifier.SetActiveFrontRow();
                        else
                            visual.identifier.SetInactiveRow();

                        if (visual.magazineText != null)
                        {
                            bool appliedSharedStyle = true;
                            if (appliedSharedStyle)
                                ApplyHolderMagazineTextStyle(visual, i == 0, isHidden);
                            else if (visual.isSpawnerVisual)
                            {
                                RestoreSpawnerTextMaterialPreset(visual);
                            }
                            else if (isHidden)
                                visual.magazineText.color = HIDDEN_MAGAZINE_COLOR; // 명세: hidden 은 row 무관 alpha 1.0
                            else
                                visual.magazineText.color = i == 0
                                    ? Color.white                          // row 0: 활성 alpha 1.0 (=255)
                                    : new Color(1f, 1f, 1f, 0.5f);          // row 1+: 비활성 alpha 0.5
                        }
                    }
                }

                // 분류에서 제외된 visual (isDeploying/isMovingToRail) 의 잔여 stroke 도 정리.
                foreach (var kvp in _holderVisuals)
                {
                    HolderVisual visual = kvp.Value;
                    if (visual == null || visual.identifier == null) continue;
                    if (!visual.isDeploying && !visual.isMovingToRail) continue;
                    visual.identifier.SetControlBoxStrokeActive(false);
                }
                return;
            }

            foreach (var bucket in _tempHighlightByCol.Values)
            {
                if (bucket.Count == 0) continue;
                bucket.Sort(s_highlightZDescComparer);
                int limit = bucket.Count < HAND_HIGHLIGHT_TOP_ROWS ? bucket.Count : HAND_HIGHLIGHT_TOP_ROWS;
                for (int i = 0; i < limit; i++)
                {
                    HolderVisual visual = bucket[i];
                    visual.identifier.SetControlBoxStrokeActive(true);
                    // ROLLBACK: 본 if 블록 내 SetActiveFrontRow + magazineText.color 라인 제거하면
                    //   ControlBoxStroke 만 토글되는 이전 동작 복원.
                    visual.identifier.SetActiveFrontRow(); // OutlineHull material swap + 검정 외곽선
                    if (visual.magazineText != null)
                    {
                        bool isHidden = false;
                        if (HolderManager.HasInstance)
                        {
                            var data = HolderManager.Instance.FindHolderPublic(visual.holderId);
                            isHidden = data != null && data.isHidden;
                        }
                        // Hidden 은 HIDDEN_MAGAZINE_COLOR(alpha 1.0) 우선. 일반은 Color.white = RGBA(1,1,1,1) = alpha 255.
                        if (visual.isSpawnerVisual)
                            RestoreSpawnerTextMaterialPreset(visual);
                        else
                            visual.magazineText.color = isHidden ? HIDDEN_MAGAZINE_COLOR : (IsFrozenHolderVisual(visual) ? FROZEN_MAGAZINE_COLOR : Color.white);
                            visual.magazineText.fontSize = isHidden ? HIDDEN_MAGAZINE_FONT_SIZE : MAGAZINE_FONT_SIZE;
                    }
                    // row<MAGAZINE_TEXT_VISIBLE_ROWS(5) 게이트는 이미 limit==HAND_HIGHLIGHT_TOP_ROWS(5)와 일치 → 토글 불필요.
                }
            }
        }

        /// <summary>
        /// Clears all holder visuals and returns objects to pool.
        /// </summary>
        public void ClearAllVisuals()
        {
            SetHandSelectionHighlightActive(false);
            StopAllCoroutines();
            _cancelledHolders.Clear();

            foreach (var kvp in _holderVisuals)
            {
                ReturnHolderToPool(kvp.Value);
            }
            _holderVisuals.Clear();
            ClearChainLines();
            _chainOrderByGroup.Clear(); // ROLLBACK_CHAIN_LINK_TOPOLOGY_STABLE_20260630: 새 레벨 → 토폴로지 캐시 초기화
        }

        /// <summary>
        /// Cancels an active deploy coroutine for the given holder and returns it to queue position.
        /// Called by ContinueHandler when reverting active holders.
        /// </summary>
        public void CancelDeployAndReturnToQueue(int holderId)
        {
            _cancelledHolders.Add(holderId);

            if (!_holderVisuals.TryGetValue(holderId, out HolderVisual visual)) return;

            // generation 증가 → 활성 OLD 코루틴 stale 처리 (cancel 플래그와 중복 안전장치)
            visual.deployGeneration++;

            visual.isDeploying = false;
            visual.isWaiting = false;
            visual.isMovingToRail = false;
            visual.isClusterFrozen = false;

            // Kill any active DOTween on this object
            if (visual.gameObject != null)
            {
                visual.gameObject.transform.DOKill();
            }

            // magazine이 이미 0이면 큐 복귀가 아니라 바로 제거 (이어하기 경합 시 잔존 방지).
            if (visual.magazineRemaining <= 0)
            {
                int col = visual.column;
                ReturnHolderToPool(visual);
                _holderVisuals.Remove(holderId);
                RepositionColumnHolders(col);
                RebuildChainLines();
                return;
            }

            // 큐 복귀 — 박스가 BoxOpenIdle 상태면 다음 클릭 시 BoxOpenDefault 부터 다시 시작되도록 openHold 해제.
            if (visual.identifier != null)
                visual.identifier.SetDartsOnRail(false);

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
            RebuildChainLines();
        }

        // ROLLBACK_ZAP_KEEP_GIMMICK_DARTS_20260701: 홀더의 남은 magazine 수를 비주얼에 동기화(부분 제거용).
        //   Zap 이 홀더 magazine 을 부분 감소시킬 때 표시 숫자(magazineText)를 갱신한다.
        public void SyncHolderMagazineDisplay(int holderId, int magazineRemaining)
        {
            if (!_holderVisuals.TryGetValue(holderId, out HolderVisual visual) || visual == null) return;
            visual.magazineRemaining = magazineRemaining;
            if (visual.magazineText != null)
            {
                if (magazineRemaining > 0) visual.magazineText.SetText("{0}", magazineRemaining);
                else visual.magazineText.SetText(string.Empty);
            }
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

            // ROLLBACK_PIPE_SHUFFLE_ZAP_GUARD_20260625: 파이프(Spawner)가 있는 열은 포물선 재배치에서 제외하고
            // 아래에서 파이프 인지 재배치(RepositionColumnHolders)에 위임한다. Shuffle/Zap 시 파이프 자신과
            // 'pipe 안 대기 홀더'가 튀지(반응하지) 않게. (배포 끝난 holder 는 일반 열 취급되어 정상 재배치.)
            var pipeColumns = new HashSet<int>();
            for (int i = 0; i < holders.Length; i++)
                if (!holders[i].isConsumed
                    && (holders[i].queueGimmick == GimmickManager.GIMMICK_SPAWNER_T
                     || holders[i].queueGimmick == GimmickManager.GIMMICK_SPAWNER_O))
                    pipeColumns.Add(holders[i].column);

            // Sync visual column from data column + 포물선 이동
            var columnRows = new Dictionary<int, int>();
            for (int i = 0; i < holders.Length; i++)
            {
                if (!_holderVisuals.TryGetValue(holders[i].holderId, out HolderVisual visual)) continue;
                if (visual.isDeploying || visual.isMovingToRail || visual.gameObject == null) continue;

                visual.column = holders[i].column;
                if (pipeColumns.Contains(visual.column)) continue; // 파이프 열은 아래에서 일괄 처리

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

                // [#6] 큐 재배치(Zap/Shuffle 등)로 hidden holder 가 앞줄(row 0)에 도달하면 색 공개.
                // reveal 이 RepositionColumnHolders 에만 있어 RefreshAllPositions 경로에서 누락되던 버그 수정.
                if (row == 0 && holders[i].isHidden && HolderManager.HasInstance)
                    HolderManager.Instance.RevealHiddenHolder(holders[i].holderId);

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
                    bool appliedSharedStyle = true;
                    if (appliedSharedStyle)
                    {
                        ApplyHolderMagazineTextStyle(visual, row == 0, holders[i].isHidden);
                    }
                    else if (visual.isSpawnerVisual)
                    {
                        RestoreSpawnerTextMaterialPreset(visual);
                    }
                    else if (holders[i].isHidden)
                    {
                        // Hidden: row 무관, alpha 1.0 + fontSize 10 강제 (명세)
                        visual.magazineText.color = HIDDEN_MAGAZINE_COLOR;
                        visual.magazineText.fontSize = HIDDEN_MAGAZINE_FONT_SIZE;
                    }
                    else
                    {
                        visual.magazineText.color = row == 0
                            ? Color.white
                            : new Color(1f, 1f, 1f, 0.5f);
                    }
                    // [TMP 부하 2026-06-10] row 2+ 텍스트 비활성 — Zap/Shuffle 재배치 경로에서도 게이트 유지.
                    ApplyMagazineTextRowVisibility(visual, row);
                }
            }

            // 파이프 열은 파이프 인지 재배치로 정리 — 파이프 고정 + 대기 홀더 핀(숨김) + 방출분만 전진.
            foreach (int col in pipeColumns)
                RepositionColumnHolders(col);
        }

        public void RefreshSpawnerChangedColumns(IReadOnlyList<int> columns)
        {
            if (columns == null) return;

            // ROLLBACK_SPAWNER_RELEASE_VISUAL_REFRESH_20260630:
            // Do not call RefreshAllPositions for Pipe timing. Only redraw the columns that actually
            // released/spawned a holder, preserving the rest of the holder queue layout.
            for (int i = 0; i < columns.Count; i++)
                RepositionColumnHolders(columns[i]);
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
            // ROLLBACK_HAND_CAMERA_QUEUE_VISUAL_CENTER:
            // Hand/Select Tool should focus the holder queue, not a static row anchor that can
            // sit too close to the balloon field. Use the live holder visuals so the camera
            // follows the actual visible queue area.
            bool found = false;
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;

            foreach (var kvp in _holderVisuals)
            {
                HolderVisual visual = kvp.Value;
                if (visual == null || visual.gameObject == null) continue;
                if (visual.isDeploying || visual.isMovingToRail) continue;

                Vector3 position = visual.gameObject.transform.position;
                if (!found)
                {
                    min = position;
                    max = position;
                    found = true;
                }
                else
                {
                    min = Vector3.Min(min, position);
                    max = Vector3.Max(max, position);
                }
            }

            if (!found)
                return new Vector3(0f, 0.1f, _queueBaseZ);

            Vector3 center = (min + max) * 0.5f;
            center.y = 0.1f;
            return center;
        }

        /// <summary>핸드/셀렉트 카메라 포커스 — 큐 앞쪽 rowCount 개 행이 화면에 들어오도록 그 행들의 중심 위치 반환.
        /// X = 대기 holder bbox 중심, Z = 앞 행(row 0..rowCount-1)의 중심(_queueBaseZ - (rowCount-1)/2 * _rowSpacing).
        /// (이전 핸드 카메라는 전체 holder bbox 중심이라 행이 많으면 너무 깊게 잡혀 앞쪽 행이 적게 보였음.)</summary>
        public Vector3 CalculateRowFocusPosition(int rowCount)
        {
            Vector3 c = CalculateQueueCenterPosition(); // X 중심 + y=0.1 재사용
            rowCount = Mathf.Max(1, rowCount);
            // 행은 _queueBaseZ(front=row0) 기준 뒤로(-Z) _rowSpacing 간격 → 앞 rowCount 행의 중심 Z.
            c.z = _queueBaseZ - (rowCount - 1) * 0.5f * _rowSpacing;
            return c;
        }

        #region Private Methods — Queue Positioning

        /// <summary>
        /// 필드 폭 비율 기반 + 최소값 보장으로 보관함 배치 계산.
        /// 작은 필드에서도 보관함끼리 겹치지 않음.
        /// </summary>
        private float _rowSpacing = 1.8f;
        private float _deployGap = 1.6f;

        /// <summary>[HAND_CAMERA_5ROWS] Hand 부스터 카메라 프레이밍용 — 큐 행 간격(레이아웃 계산 후 값).</summary>
        public float RowSpacing => _rowSpacing;

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

        private void SyncGlassPipePreview(int column, HolderData pipeData, Vector3 pipePosition, HolderData[] allHolders, float emergeDuration)
        {
            // ROLLBACK_GLASS_PIPE_NEXT_PAYLOAD_PREVIEW_20260625:
            // Glass Pipe shows only the next unreleased payload at half scale before it is released.
            _glassPipePreviewScratch.Clear();
            foreach (int holderId in _glassPipePreviewPayloads)
                _glassPipePreviewScratch.Add(holderId);

            for (int i = 0; i < _glassPipePreviewScratch.Count; i++)
            {
                int holderId = _glassPipePreviewScratch[i];
                HolderData data = HolderManager.Instance.FindHolderPublic(holderId);
                if (data == null || data.column != column)
                    continue;

                if (!_holderVisuals.TryGetValue(holderId, out HolderVisual visual) || visual == null || visual.gameObject == null)
                {
                    _glassPipePreviewPayloads.Remove(holderId);
                    continue;
                }

                if (data.isConsumed)
                {
                    ReturnHolderToPool(visual);
                    _holderVisuals.Remove(holderId);
                    _glassPipePreviewPayloads.Remove(holderId);
                    continue;
                }

                if (data.IsQueueVisible)
                {
                    // ROLLBACK_SPAWNER_EMERGE_SYNC_20260630: 방출 = 앞 홀더 전진과 동시에 emerge.
                    //   스케일 0.4→1.0 + 텍스트 0→정상, 이동시간(emergeDuration)에 맞춰 함께 키움. 위치 이동은 colHolders 가 처리(popping).
                    visual.gameObject.transform.DOKill(false);
                    _poppingScale.Add(holderId);
                    visual.gameObject.transform.DOScale(Vector3.one, emergeDuration).SetEase(Ease.OutQuad)
                        .OnComplete(() => _poppingScale.Remove(holderId));
                    if (visual.magazineText != null)
                    {
                        var tt = visual.magazineText.transform;
                        Vector3 baseScale = tt.localScale == Vector3.zero ? Vector3.one : tt.localScale;
                        visual.magazineText.gameObject.SetActive(true);
                        tt.localScale = Vector3.zero;
                        tt.DOScale(baseScale, emergeDuration).SetEase(Ease.OutQuad);
                    }
                    _glassPipePreviewPayloads.Remove(holderId);
                }
                else
                {
                    // 미방출 = 파이프 안 고정(0.4) + 텍스트 숨김. 등장(0→0.4) 애니 중이면 스케일 건드리지 않음.
                    visual.gameObject.transform.position = pipePosition;
                    if (!_poppingScale.Contains(holderId))
                        visual.gameObject.transform.localScale = Vector3.one * 0.4f;
                    if (visual.magazineText != null && visual.magazineText.gameObject.activeSelf)
                        visual.magazineText.gameObject.SetActive(false);
                }
            }

            if (pipeData == null
                || pipeData.isConsumed
                || pipeData.queueGimmick != GimmickManager.GIMMICK_SPAWNER_T
                || allHolders == null)
                return;

            HolderData nextPayload = null;
            for (int i = 0; i < allHolders.Length; i++)
            {
                HolderData candidate = allHolders[i];
                if (candidate == null || candidate.isConsumed) continue;
                if (!candidate.isPipePayload || candidate.isPipePayloadReleased) continue;
                if (candidate.pipeOwnerId != pipeData.holderId) continue;
                if (nextPayload == null || candidate.pipeOrder < nextPayload.pipeOrder)
                    nextPayload = candidate;
            }

            if (nextPayload == null || _glassPipePreviewPayloads.Contains(nextPayload.holderId))
                return;

            if (!_holderVisuals.TryGetValue(nextPayload.holderId, out HolderVisual preview) || preview == null || preview.gameObject == null)
            {
                preview = CreateHolderVisual(nextPayload, pipePosition, column, false);
                if (preview == null || preview.gameObject == null)
                    return;
                _holderVisuals[nextPayload.holderId] = preview;
            }

            // ROLLBACK_SPAWNER_INSIDE_APPEAR_20260630: 새 내부 홀더 = scale 0→0.4 팝 + 텍스트 숨김(겹침·즉시등장 인지난이 방지).
            int previewId = nextPayload.holderId;
            preview.gameObject.transform.DOKill(false);
            preview.gameObject.transform.position = pipePosition;
            preview.gameObject.transform.localScale = Vector3.zero;
            _poppingScale.Add(previewId);
            preview.gameObject.transform.DOScale(Vector3.one * 0.4f, 0.2f).SetEase(Ease.OutBack)
                .OnComplete(() => _poppingScale.Remove(previewId));
            if (preview.magazineText != null)
                preview.magazineText.gameObject.SetActive(false);
            _glassPipePreviewPayloads.Add(previewId);
        }

        private void RepositionColumnHolders(int column)
        {
            if (!HolderManager.HasInstance) return;

            // Spawner에 의해 새로 추가된 보관함 — Spawner 위치에서 생성, 정상 스케일
            // Spawner 위치 찾기
            Vector3 spawnerPos = CalculateQueuePosition(column, 1); // fallback
            bool columnHasSpawner = false;
            int pipeSourceRow = int.MinValue; // [#2] authored row of the pipe; holders with sourceRow > this stay blocked behind it
            HolderData pipeData = null;
            foreach (var kvp2 in _holderVisuals)
            {
                if (kvp2.Value.column == column && kvp2.Value.gameObject != null)
                {
                    var spData = HolderManager.Instance.FindHolderPublic(kvp2.Value.holderId);
                    if (spData != null && (spData.queueGimmick == GimmickManager.GIMMICK_SPAWNER_T
                                        || spData.queueGimmick == GimmickManager.GIMMICK_SPAWNER_O))
                    {
                        spawnerPos = kvp2.Value.gameObject.transform.position;
                        columnHasSpawner = true;
                        pipeSourceRow = spData.sourceRow;
                        pipeData = spData;
                        break;
                    }
                }
            }

            // 비주얼 없는 일반 보관함 생성 — ① Spawner 소환분 ② [HOLDER_LAZY_SPAWN] 지연 스폰분.
            // 열 잔여(미소비·비스포너) 순번 = holderId 순(초기 배치 row 순서 보존) — 임계 안만 생성.
            HolderData[] allHolders = HolderManager.Instance.GetHolders();
            // ROLLBACK_SPAWNER_EMERGE_SYNC_20260630: emerge 이동/스케일/텍스트를 '큐 한 칸 이동시간'(앞 홀더 전진과 동일)에 맞춤.
            float queueStepDur = Mathf.Max(0.05f,
                Vector3.Distance(CalculateQueuePosition(column, 0), CalculateQueuePosition(column, 1)) / 4f);
            SyncGlassPipePreview(column, pipeData, spawnerPos, allHolders, queueStepDur);
            _tempLazyColumnData.Clear();
            for (int i = 0; i < allHolders.Length; i++)
            {
                var hd = allHolders[i];
                if (hd.column != column || hd.isConsumed) continue;
                // Spawner 자체는 SpawnWaitingHolders에서 생성됨
                if (hd.queueGimmick == GimmickManager.GIMMICK_SPAWNER_T
                 || hd.queueGimmick == GimmickManager.GIMMICK_SPAWNER_O) continue;
                if (!hd.IsQueueVisible) continue; // 미방출 payload 만 숨김. 맵핑 안 된 뒤 홀더는 보임.
                _tempLazyColumnData.Add(hd);
            }
            _tempLazyColumnData.Sort((a, b) => a.holderId.CompareTo(b.holderId));

            // ROLLBACK_CHAIN_DEEP_EAGER_CONTIGUOUS_20260625: 초기 스폰과 동일 — 깊은 Chain 까지 연속 스폰해
            //   activeRow 누락(미스폰 lazy 홀더)으로 인한 Chain 순서 꼬임 방지.
            int colVisibleRows = VISIBLE_HOLDER_ROWS;
            for (int k = _tempLazyColumnData.Count - 1; k >= VISIBLE_HOLDER_ROWS; k--)
                if (_tempLazyColumnData[k].chainGroupId >= 0) { colVisibleRows = k + 1; break; }

            for (int row = 0; row < _tempLazyColumnData.Count; row++)
            {
                var hd = _tempLazyColumnData[row];
                if (_holderVisuals.ContainsKey(hd.holderId)) continue;
                // 아직 임계 밖이면 지연 유지 (Chain 멤버는 연결선 때문에 항상 생성).
                if (row >= colVisibleRows && hd.chainGroupId < 0) continue;

                // 스폰 위치: Spawner 열은 기존처럼 Spawner 배출 위치(연출 유지),
                // 일반 열의 지연 스폰분은 목표보다 한 행 뒤에서 등장 → 아래 리포지셔닝 트윈으로
                // 다른 홀더와 같이 자연스럽게 당겨짐 (가시 경계 팝인 방지).
                // ROLLBACK_PIPE_HELD_BEHIND_20260624 (#2): 파이프에서 방출된 payload 만 파이프 위치에서 emerge.
                // 파이프 소멸 후 풀린 일반 뒤 홀더는 '뿌려지는' 느낌 없이 자기 정상 큐 위치에 등장(다른 홀더와 동일, 5행까지).
                bool emergeFromPipe = columnHasSpawner && hd.isPipePayload && hd.isPipePayloadReleased;
                Vector3 startPos = emergeFromPipe ? spawnerPos : CalculateQueuePosition(column, row + 1);
                HolderVisual newVisual = CreateHolderVisual(hd, startPos, column, false);
                if (newVisual != null)
                {
                    _holderVisuals[hd.holderId] = newVisual;
                    // ROLLBACK_PIPE_PAYLOAD_POP_20260624 (#1): 방출된 payload 는 파이프에서 사이즈 0으로 시작 →
                    // 앞으로 나아가는 동안엔 작게 유지되다가 '거의 도착했을 때' 원본 사이즈로 커짐(InQuad: 느린 시작
                    // → 후반 급상승). 파이프 근처에서 정사이즈가 되어 파이프와 겹쳐 보이는 것 방지.
                    if (emergeFromPipe && newVisual.gameObject != null)
                    {
                        int hid = hd.holderId;
                        HolderData pipeOwner = HolderManager.HasInstance
                            ? HolderManager.Instance.FindHolderPublic(hd.pipeOwnerId)
                            : null;
                        bool emergeFromGlassPipe = pipeOwner != null
                            && pipeOwner.queueGimmick == GimmickManager.GIMMICK_SPAWNER_T;
                        // ROLLBACK_SPAWNER_EMERGE_SYNC_20260630: 내부 대기 크기 0.4 에 맞춰 폴백(미리보기 없이 방출) 시작도 0.4.
                        Vector3 startScale = emergeFromGlassPipe ? Vector3.one * 0.4f : Vector3.zero;
                        _poppingScale.Add(hid);
                        // ROLLBACK_GLASS_PIPE_PAYLOAD_PREVIEW_SCALE_20260625:
                        // Glass Pipe is transparent, so the next holder should be partially readable before popping out.
                        newVisual.gameObject.transform.localScale = startScale;
                        // PIPE_EMERGE_FASTER_20260628: 배출 payload 가 더 빨리 보이도록 pop 시간 단축(0.4→0.25).
                        //   탭과 동시에 data release 는 이미 즉시 일어나지만, InQuad 느린 시작이 박스를 0.2~0.3s 늦게
                        //   보이게 해 '동시 생성' 느낌이 약했음. 파이프 겹침 방지를 위해 InQuad 는 유지하고 시간만 줄임.
                        newVisual.gameObject.transform.DOScale(Vector3.one, 0.25f).SetEase(Ease.InQuad)
                            .OnComplete(() => _poppingScale.Remove(hid));
                    }
                }
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
                    if (hData != null && !hData.IsQueueVisible)
                        continue; // 미방출 payload 만 큐에서 제외(숨김). 맵핑 안 된 뒤 홀더는 포함(보임).

                    colHolders.Add(v);
                }
            }

            // Sort regular holders by current Z descending (front first)
            colHolders.Sort((a, b) =>
            {
                if (a.gameObject == null || b.gameObject == null) return 0;
                return b.gameObject.transform.position.z.CompareTo(a.gameObject.transform.position.z);
            });

            // 일반 보관함 배치 — [#2] active(앞/방출됨)는 정상 큐로 전진, blocked(파이프 뒤 미방출)만 파이프에 고정
            int activeRow = 0, blockedRow = 0;
            for (int row = 0; row < colHolders.Count; row++)
            {
                if (colHolders[row].gameObject == null) continue;

                // ROLLBACK_PIPE_RELEASE_ADVANCE_20260624 (#2/#5): a holder is "held inside the pipe"
                // (pinned + shrunk) ONLY if authored BEHIND the active pipe (sourceRow > pipe) AND not yet
                // released. A released Pipe payload — and everything in front of the pipe — flows to the
                // normal front queue (advances to row 0 = deployable) instead of clustering at the pipe.
                // When the pipe is consumed/despawned spawnerCount→0 so nothing is blocked → behind holders advance.
                bool insideSpawner = false;
                if (spawnerCount > 0 && pipeSourceRow != int.MinValue && HolderManager.HasInstance)
                {
                    var hdRow = HolderManager.Instance.FindHolderPublic(colHolders[row].holderId);
                    bool releasedPayload = hdRow != null && hdRow.isPipePayload && hdRow.isPipePayloadReleased;
                    insideSpawner = hdRow != null && hdRow.sourceRow > pipeSourceRow && !releasedPayload;
                }

                Vector3 targetPos;
                if (insideSpawner)
                    // ROLLBACK_PIPE_BEHIND_VISIBLE_20260624 (#2): 파이프에 '맵핑 안 된' 뒤 홀더는 숨기지 않고
                    // 파이프 뒤 정상 행(pipeRow+1, +2 …)에 고정 — 일반 홀더처럼 보이되 파이프 앞으론 못 넘어옴(5행까지).
                    targetPos = CalculateQueuePosition(column, pipeSourceRow + 1 + blockedRow);
                else
                    targetPos = CalculateQueuePosition(column, activeRow);
                // ROLLBACK_PIPE_FRONT_ACTIVE_20260624: '맨 앞(배포 가능)' = 첫 active 홀더(activeRow==0).
                // 막힌 뒤 홀더는 앞이 비어도 Z정렬상 맨앞이 되어도 '앞줄(아웃라인/연출/Hidden공개)' 취급 안 함.
                bool isFrontActive = !insideSpawner && activeRow == 0;
                if (insideSpawner) blockedRow++; else activeRow++;

                // #3: 팝업(Scale Up) 중인 holder 는 DOKill/즉시 scale 세팅 건너뜀(팝 트윈 보존, 이동은 허용).
                bool popping = _poppingScale.Contains(colHolders[row].holderId);
                if (!popping) colHolders[row].gameObject.transform.DOKill(false);
                // 뒤 대기 홀더도 일반 홀더처럼 정상 크기(축소 X). 팝업 중인 방출 payload 만 0→1 트윈 보존.
                if (!popping)
                    colHolders[row].gameObject.transform.localScale = Vector3.one;

                // Spawner 안 대기: TEXT 숨김 / 앞줄: TEXT 보이기
                // [TMP 부하 2026-06-10] row 2+ 도 텍스트 비활성 (MAGAZINE_TEXT_VISIBLE_ROWS 게이트).
                if (colHolders[row].magazineText != null)
                {
                    // ROLLBACK_PIPE_FRONT_ACTIVE_20260624: 뒤 대기 홀더도 텍스트 보이게(insideSpawner 게이트 제거).
                    // 흰색(앞줄 강조)은 '맨 앞 active' 만 — 막힌 뒤 홀더는 50% 반투명.
                    colHolders[row].magazineText.gameObject.SetActive(row < MAGAZINE_TEXT_VISIBLE_ROWS);
                    {
                        bool hiddenGuard = false;
                        if (HolderManager.HasInstance)
                        {
                            var holderData = HolderManager.Instance.FindHolderPublic(colHolders[row].holderId);
                            hiddenGuard = holderData != null && holderData.isHidden;
                        }
                        bool appliedSharedStyle = true;
                        if (appliedSharedStyle)
                            ApplyHolderMagazineTextStyle(colHolders[row], isFrontActive, hiddenGuard);
                        else if (colHolders[row].isSpawnerVisual)
                            RestoreSpawnerTextMaterialPreset(colHolders[row]);
                        else if (hiddenGuard)
                        {
                            // Hidden: row 무관, alpha 1.0 + fontSize 10 강제 (명세)
                            colHolders[row].magazineText.color = HIDDEN_MAGAZINE_COLOR;
                            colHolders[row].magazineText.fontSize = HIDDEN_MAGAZINE_FONT_SIZE;
                        }
                        else
                        {
                            colHolders[row].magazineText.color = isFrontActive
                                ? Color.white
                                : new Color(1f, 1f, 1f, 0.5f);
                        }
                    }
                }

                // 보관함 상태별 아웃라인 — 맨 앞 active 만 클릭가능 아웃라인. 막힌 뒤 홀더는 아웃라인 없음.
                if (colHolders[row].identifier != null)
                {
                    if (isFrontActive)
                        colHolders[row].identifier.SetActiveFrontRow(); // 검은 아웃라인(맨 앞 active 만)
                    else
                        colHolders[row].identifier.SetInactiveRow(); // 아웃라인 없음(뒤 대기 홀더 포함)
                }

                if (Vector3.Distance(colHolders[row].gameObject.transform.position, targetPos) > 0.05f)
                {
                    float dist = Vector3.Distance(colHolders[row].gameObject.transform.position, targetPos);
                    // ROLLBACK_SPAWNER_EMERGE_SYNC_20260630: 막 방출된 payload(popping) 의 emerge 이동은
                    //   큐 한 칸 이동시간으로 — 앞 홀더 전진과 동시·동일 시간. 일반 이동은 거리비례 유지.
                    float moveDur = popping ? queueStepDur : (dist / 4f);
                    colHolders[row].gameObject.transform.DOMove(targetPos, moveDur).SetEase(Ease.OutQuad);
                }

                colHolders[row].queuePosition = targetPos;

                if (isFrontActive && HolderManager.HasInstance)
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

            // [Optimization 2026-05-12] Holder Shadow 의 SpriteRenderer → MeshRenderer 전환 (Balloon 과 동일 패턴).
            SpriteSRPBatcherUtil.ConvertShadowToMeshSprite(obj);

            obj.SetActive(true);
            obj.transform.localScale = Vector3.one; // 풀 재사용 시 스케일 초기화

            // ROLLBACK_HOLDER_MAGAZINE_DART_TRAIL_OFF_20260616: 홀더 매거진 다트(정지)의 FXDartTrail 은 게임상 미사용
            //   (DartManager 가 비행 다트 트레일도 SetDartTrailActive(false) 로 항상 비활성). playOnAwake 라 홀더당 ~8개가
            //   상시 파티클 시뮬+드로우콜(GPU-bound 프레임의 과다 ParticleSystem 주범, 73홀더 레벨 ≈ 584개) → 비활성화.
            //   시각 변화 0(트레일은 어차피 안 보임), 게임플레이 무관. 롤백: 이 호출 제거.
            DisableMagazineDartTrails(obj);

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
                ident.ResetAnimator(); // 뚜껑 닫힌 상태로 초기화
                ident.SetHolderId(data.holderId);
                ident.ShowDarts(data.magazineCount);
                // ROLLBACK_PIPE_PAYLOAD_FROZEN_PARTICLE_OFF_20260625:
                // Pipe/Glass Pipe payload 생성은 해동 이벤트가 아니므로 풀에 남은 Frozen break FX가 재생되지 않게 초기화한다.
                ident.SetFrozen(data.isFrozen, playBreakEffect: false);
                ident.StopFrozenBreakEffect();
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

            // ROLLBACK_SPAWNER_MAGAZINE_TEXT_SERIALIZE_20260624: 인스펙터 지정 MagazineText 우선, 미할당 시 자동 탐색.
            TMP_Text textMesh = (ident != null && ident.MagazineText != null)
                ? ident.MagazineText
                : obj.GetComponentInChildren<TMP_Text>(true);
            Material spawnerTextMaterialPreset = textMesh != null && isSpawner ? textMesh.fontSharedMaterial : null;
            if (textMesh != null)
            {
                // [TMP 부하 2026-06-10] 풀 재사용 안전망 — row 2+ 비활성 상태로 반환된 홀더가 재사용될 때
                // 텍스트가 꺼진 채 나오지 않게 기본 활성. 행 게이트는 생성 직후 호출부/재배치에서 다시 적용됨.
                if (!textMesh.gameObject.activeSelf)
                    textMesh.gameObject.SetActive(true);
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
                if (isSpawner)
                {
                    if (spawnerTextMaterialPreset != null && textMesh.fontSharedMaterial != spawnerTextMaterialPreset)
                        textMesh.fontSharedMaterial = spawnerTextMaterialPreset;
                }
                else if (data.isHidden)
                {
                    // Hidden: alpha 1.0(=255) + fontSize 10 강제 고정 (명세)
                    textMesh.color = HIDDEN_MAGAZINE_COLOR;
                    textMesh.fontSize = HIDDEN_MAGAZINE_FONT_SIZE;
                }
                else if (data.isFrozen)
                {
                    // ROLLBACK_FROZEN_BOX_HIT_FX_20260626: Frozen Dart Box count text alpha = 128.
                    textMesh.color = FROZEN_MAGAZINE_COLOR;
                    textMesh.fontSize = MAGAZINE_FONT_SIZE;
                }
                else
                {
                    textMesh.color = Color.white;
                    textMesh.fontSize = MAGAZINE_FONT_SIZE;
                }
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
                isSpawnerVisual = isSpawner,
                spawnerTextMaterialPreset = spawnerTextMaterialPreset,
                queuePosition = position,
                isDeploying = false,
                isWaiting = false,
                isMovingToRail = false,
                isClusterFrozen = false,
                identifier = ident
            };
        }

        // ROLLBACK_HOLDER_MAGAZINE_DART_TRAIL_OFF_20260616: 홀더 하위 모든 FXDartTrail(매거진 다트 트레일) 비활성.
        //   (홀더엔 비행 다트가 없어 매거진 트레일만 존재 — 전부 게임상 미사용.) 풀 재사용마다 idempotent 호출.
        private static void DisableMagazineDartTrails(GameObject holder)
        {
            if (holder == null) return;
            var trs = holder.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < trs.Length; i++)
            {
                var t = trs[i];
                if (t != null && t.name == "FXDartTrail" && t.gameObject.activeSelf)
                    t.gameObject.SetActive(false);
            }
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
                    visual.identifier.SetDartsOnRail(false); // openHold 리셋
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
            // ROLLBACK_HOLDER_WAIT_FOLLOW:
            // A pre-tapped second holder used to move to a fixed waiting point. If the first holder
            // was still travelling, queue re-layout or speed changes could leave a visible gap.
            // Treat the waiting holder as "in transit" visually and keep it one row behind the
            // currently active holder in the same column until it is promoted.
            visual.isMovingToRail = true;

            if (visual.gameObject != null)
                visual.gameObject.transform.DOKill(false);

            StartCoroutine(FollowWaitingHolderCoroutine(visual));
        }

        /// <summary>
        /// 클릭된 보관함을 즉시 deploy point로 이동 시작 + 배치 큐에 등록.
        /// 이동은 동시에 가능, 배치만 순차.
        /// </summary>
        private void StartDeploy(int holderId)
        {
            if (!_holderVisuals.TryGetValue(holderId, out HolderVisual visual))
                return;

            if (visual.isDeploying || (visual.isMovingToRail && !visual.isWaiting)) return;
            visual.isWaiting = false;

            // NEW 코루틴은 이전 cancel 플래그 영향 받지 않도록 사전 정리.
            // (Continue 직후 같은 holder를 다시 클릭한 경우, 이전 사이클의 cancel 플래그가
            // 잔존해 NEW 코루틴이 즉시 yield break 되는 race 방지.)
            _cancelledHolders.Remove(holderId);

            // 큐에 이미 같은 holderId가 있으면 OLD 코루틴의 잔재 — 중복 enqueue 안 함.
            // (NEW가 OLD의 큐 자리를 그대로 이어받음. OLD는 generation mismatch로 곧 종료.)
            if (!ColumnQueueContains(visual.column, holderId))
                _colQueues[visual.column].Enqueue(holderId);

            // 선택됨 → 블러 해제 + 원래 색상 표시 + 뚜껑 열기
            if (visual.identifier != null)
            {
                visual.identifier.SetSelected();
                visual.identifier.StartDeploy(); // 터치 시 바로 뚜껑 열림
            }

            // 같은 컬럼의 대기 박스들에 Click 애니메이션 트리거
            foreach (var kvp in _holderVisuals)
            {
                var other = kvp.Value;
                if (other.column == visual.column && other.holderId != holderId
                    && !other.isDeploying && !other.isMovingToRail
                    && other.identifier != null)
                {
                    other.identifier.TriggerClick();
                }
            }

            // 즉시 이동 시작 (대기 없이)
            visual.isMovingToRail = true;

            // generation 증가 → 이전 사이클의 OLD 코루틴은 mismatch로 자동 stale 처리
            visual.deployGeneration++;
            int gen = visual.deployGeneration;

            // 기존 DOTween 킬 — RepositionColumnHolders의 DOMove와 충돌 방지
            if (visual.gameObject != null)
                visual.gameObject.transform.DOKill();

            RepositionColumnHolders(visual.column);
            StartCoroutine(DeployCoroutine(visual, gen));
        }

        private IEnumerator FollowWaitingHolderCoroutine(HolderVisual visual)
        {
            if (visual == null || visual.gameObject == null)
                yield break;

            bool snapped = false;
            while (!_boardFinished && visual.isWaiting && visual.gameObject != null)
            {
                Vector3 target = GetColumnWaitingTarget(visual.column, visual.holderId);
                Vector3 current = visual.gameObject.transform.position;
                float step = EffectiveDeployMoveSpeed * Time.deltaTime;
                visual.gameObject.transform.position = Vector3.MoveTowards(current, target, step);

                if (!snapped && Vector3.Distance(visual.gameObject.transform.position, target) <= 0.05f)
                {
                    snapped = true;
                    // ROLLBACK_HOLDER_PUNCH_TWEEN:
                    // Restore DOPunchScale here if the small holder arrival bounce is required.
                    visual.gameObject.transform.localScale = Vector3.one;
                }

                yield return null;
            }
        }

        private Vector3 GetColumnWaitingTarget(int column, int waitingHolderId)
        {
            Vector3 deployPoint = GetDeployPoint(column);
            Vector3 target = new Vector3(deployPoint.x, deployPoint.y, WAITING_HOLDER_Z);

            HolderVisual blocker = FindColumnBlockerVisual(column, waitingHolderId, true);
            if (blocker != null && blocker.gameObject != null)
            {
                target = new Vector3(deployPoint.x, deployPoint.y, WAITING_HOLDER_Z);
            }

            return target;
        }

        private HolderVisual FindColumnBlockerVisual(int column, int excludeHolderId, bool preferWaiting)
        {
            HolderVisual active = null;
            HolderVisual waiting = null;
            foreach (var kvp in _holderVisuals)
            {
                HolderVisual candidate = kvp.Value;
                if (candidate == null || candidate.holderId == excludeHolderId) continue;
                if (candidate.column != column || candidate.gameObject == null) continue;

                if (candidate.isWaiting)
                    waiting = candidate;
                else if (candidate.isDeploying || candidate.isMovingToRail)
                    active = candidate;
            }

            if (preferWaiting && waiting != null)
                return waiting;

            return active != null ? active : waiting;
        }

        private IEnumerator DeployCoroutine(HolderVisual visual, int gen)
        {
            if (!RailManager.HasInstance || visual.gameObject == null)
            {
                yield break;
            }

            // 헬퍼: stale 코루틴이면 큐에서 자기 ID 제거 후 yield break.
            // (이 코루틴은 lambda 캡처 안전성 위해 inline 으로 사용)

            // ── Phase 1: Move holder to deploy point (또는 대기 위치) ──
            Vector3 deployPoint = GetDeployPoint(visual.column);

            // 같은 열에 이미 배치 중인 보관함이 있으면 바로 뒤에 대기
            bool hasDeploying = _colBusy[visual.column];
            Vector3 targetPoint = hasDeploying
                ? new Vector3(deployPoint.x, deployPoint.y, WAITING_HOLDER_Z)
                : deployPoint;

            // 기존 DOTween 전부 킬 (RepositionColumnHolders의 DOMove 등)
            if (visual.gameObject != null)
                visual.gameObject.transform.DOKill();

            while (visual.gameObject != null)
            {
                // stale (NEW가 generation 증가시켜 take-over) → 큐는 NEW가 재사용 중, 건드리지 않음
                if (visual.deployGeneration != gen)
                {
                    AbortDeploy(visual, false);
                    yield break;
                }
                if (_cancelledHolders.Contains(visual.holderId))
                {
                    _cancelledHolders.Remove(visual.holderId);
                    AbortDeploy(visual, true);
                    yield break;
                }

                Vector3 current = visual.gameObject.transform.position;
                float dist = Vector3.Distance(current, targetPoint);
                if (dist < 0.15f) break;

                // MoveTowards로 step을 dist에 클램프 — 2x 속도/저프레임에서 오버슈트 방지
                float step = EffectiveDeployMoveSpeed * Time.deltaTime;
                visual.gameObject.transform.position = Vector3.MoveTowards(current, targetPoint, step);
                yield return null;
            }

            if (visual.gameObject != null)
            {
                visual.gameObject.transform.position = targetPoint;
                // deploy point 도착 펀치 (1회)
                // ROLLBACK_HOLDER_PUNCH_TWEEN:
                // Restore DOPunchScale here if the deploy arrival bounce is required.
                visual.gameObject.transform.localScale = Vector3.one;
            }

            visual.isMovingToRail = false;

            // ── Phase 1.5: 전역 순차 배치 — 다른 보관함 배치 완료까지 대기 ──
            int waitFrames = 0;
            float waitStart = Time.unscaledTime;
            const float MAX_WAIT_SECONDS = 60f;
            while (Time.unscaledTime - waitStart < MAX_WAIT_SECONDS)
            {
                // stale (NEW take-over) → 큐는 NEW 소유, 건드리지 않음
                if (visual.deployGeneration != gen)
                {
                    AbortDeploy(visual, false);
                    yield break;
                }
                if (_boardFinished)
                {
                    AbortDeploy(visual, false);
                    yield break;
                }
                if (_cancelledHolders.Contains(visual.holderId))
                {
                    _cancelledHolders.Remove(visual.holderId);
                    AbortDeploy(visual, true);
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

            if (Time.unscaledTime - waitStart >= MAX_WAIT_SECONDS)
            {
                Debug.LogWarning($"[HolderVisualManager] Holder {visual.holderId} timed out waiting for deploy turn.");
                AbortDeploy(visual, true);
                yield break;
            }

            // 대기 위치에서 실제 deploy point로 이동 (대기했던 경우)
            if (visual.gameObject != null && Vector3.Distance(visual.gameObject.transform.position, deployPoint) > 0.1f)
            {
                // ROLLBACK_HOLDER_DEPLOY_MANUAL_MOVE:
                // Previous behavior used DOMove + WaitForSeconds here. Manual MoveTowards avoids
                // creating a tween while preserving the same deploy sequencing.
                visual.gameObject.transform.DOKill();
                while (visual.gameObject != null
                       && visual.deployGeneration == gen
                       && !_cancelledHolders.Contains(visual.holderId)
                       && Vector3.Distance(visual.gameObject.transform.position, deployPoint) > 0.1f)
                {
                    Vector3 current = visual.gameObject.transform.position;
                    float step = EffectiveDeployMoveSpeed * Time.deltaTime;
                    visual.gameObject.transform.position = Vector3.MoveTowards(current, deployPoint, step);
                    yield return null;
                }

                if (visual.gameObject != null)
                    visual.gameObject.transform.position = deployPoint;
            }

            // ── Phase 2: 배치 시작 (열 순차 — 내 차례) ──
            visual.isDeploying = true;
            // 뚜껑은 이미 터치 시 열림 (StartDeploy에서 호출됨)

            if (HolderManager.HasInstance)
                HolderManager.Instance.ConfirmOnRail(visual.holderId);

            if (!RailManager.HasInstance)
            {
                AbortDeploy(visual, false);

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
            if (DEPLOY_DEBUG_ENABLED)
            {
                int fixedDeploySlot = rail.GetSlotAtPathDistance(fixedDeployProgress);
                string fixedDeployClearState = rail.GetProgressClearFailReason(fixedDeployProgress, visual.holderId);
                LogDeployDebug($"[DeployStart] holder={visual.holderId}(col{visual.column}) gen={gen} mag={visual.magazineRemaining} " +
                               $"fixedProg={fixedDeployProgress:F2} fixedSlot={fixedDeploySlot} clearNow={rail.IsProgressClear(fixedDeployProgress, visual.holderId)} " +
                               $"state={fixedDeployClearState} waitFrames={waitFrames} waitSec={(Time.unscaledTime - waitStart):F2} " +
                               $"Rail={rail.OccupiedCount}/{rail.SlotCount} active={rail.GetActiveDeployPointCount()} dlh={rail.DeadlockHolderId}");
            }

            // 배치 페이싱: belt 누적 이동 거리(distSinceLastPlacement)가 physicalGap에 도달할 때마다 1회 배치.
            // overshoot은 carry-over + placementProgress 보정으로 흡수 → 다트 간격이 항상 정확히 physicalGap.
            // (이전: IsProgressClear 의 minGap=0.9*physGap 폴링이라 frame 타이밍에 따라 spacing 변동.)
            // ROLLBACK_DART_CELL_SPACING_CLUSTER_GAP:
            // Deploy same-holder darts on the same spacing used for attack head promotion.
            float distSinceLastPlacement = rail.DartClusterAttackGap; // 첫 다트 즉시 배치

            // 한 frame 안 catch-up 한도 — belt 가 1.5~3 slot/frame 회전 시 누적된 distSinceLastPlacement
            // 를 한 frame 안에 N placements 로 처리. clamp 도 이 한도 이하로 자르지 않음.
            // 1*physGap 으로 clamp 하면 정상 catch-up (belt frame 당 1+ slot 회전) 도 손실 → cluster 안 빈 slot 누적.
            const int MAX_PLACEMENTS_PER_FRAME = 3;
            const int MAX_RECOVERY_PLACEMENTS_PER_FRAME = 8;

            float totalPathLen = rail.TotalPathLength;
            bool fixedGapBurstUnlocked = false;
            int fixedGapBurstPlaced = 0;

            // [2026-06-15] 명세: Holder 매거진 숫자 감소 시작 시점을 BoxOpen 진행률 60% 지점으로 지연.
            // 변경 범위는 '첫 발 타이밍만' — 이후 placement 페이싱은 distSinceLastPlacement 그대로.
            // BoxOpen.anim m_StopTime(=0.333s) 기준: HolderIdentifier.BOX_OPEN_ANIM_DURATION 동기화 필요.
            while (visual.identifier != null && !visual.identifier.IsReadyForMagazineDecrement())
            {
                if (visual.deployGeneration != gen) yield break;
                if (_boardFinished) yield break;
                yield return null;
            }

            while (visual.magazineRemaining > 0 && visual.gameObject != null && !_boardFinished)
            {
                // stale (NEW take-over). 이 시점엔 OLD가 이미 Phase 1.5에서 _colBusy=true 를 set 했고
                // visual.isDeploying=true 도 set 했으므로, 둘 다 release해야 함 — 안 그러면 NEW의
                // Phase 1.5가 (!_colBusy) 가드에 걸려 60초 timeout, 또는 isDeploying=true 가
                // 다음 user 클릭의 StartDeploy 진입을 차단. NEW는 자기 차례에 다시 set 함.
                if (visual.deployGeneration != gen)
                {
                    AbortDeploy(visual, false);
                    rail.UnregisterDeployPoint(visual.holderId);
                    rail.ReleaseHolderReservation(visual.holderId); // 사용자 요구: Slot Reservation 해제
                    rail.ExitDeployPlacement(visual.holderId);
                    _colBusy[visual.column] = false;
                    visual.isDeploying = false;
                    // 데드락 트리거 holder 가 abort 로 종료 → ExitDeadlockMode 누락 방지(다른 holder 영구 pause 차단).
                    if (rail.DeadlockHolderId == visual.holderId) rail.ExitDeadlockMode();
                    yield break;
                }
                // 취소 체크
                if (_cancelledHolders.Contains(visual.holderId))
                {
                    _cancelledHolders.Remove(visual.holderId);
                    AbortDeploy(visual, true);
                    rail.UnregisterDeployPoint(visual.holderId);
                    rail.ReleaseHolderReservation(visual.holderId); // 사용자 요구: Slot Reservation 해제
                    rail.ExitDeployPlacement(visual.holderId);
                    _colBusy[visual.column] = false;
                    // 데드락 트리거 holder 가 취소로 종료 → ExitDeadlockMode 누락 방지(다른 holder 영구 pause 차단).
                    if (rail.DeadlockHolderId == visual.holderId) rail.ExitDeadlockMode();
                    yield break;
                }

                // 매 iteration 마다 deadlock 감지 — cluster freeze / placement 실패 분기 모두 catch.
                // (이전: IsProgressClear false 시점에서만 호출 → blockedByDeployPoint 분기 빠지면 stuck)
                TryEnterDeadlockIfNeeded(rail);

                // 데드락 mode 시 leftmost holder 만 placement 진행. 다른 holder pause.
                if (rail.DeadlockHolderId >= 0 && rail.DeadlockHolderId != visual.holderId)
                {
                    if (!visual.deadlockPauseLogged)
                    {
                        LogDeployDebug($"[Deadlock] Holder {visual.holderId} (col {visual.column}) PAUSED — leftmost = {rail.DeadlockHolderId}");
                        visual.deadlockPauseLogged = true;
                    }
                    yield return null;
                    continue;
                }
                else if (visual.deadlockPauseLogged && rail.DeadlockHolderId < 0)
                {
                    LogDeployDebug($"[Deadlock] Holder {visual.holderId} RESUMED — deadlock cleared");
                    visual.deadlockPauseLogged = false;
                }

                // ROLLBACK_DART_CELL_SPACING_CLUSTER_GAP:
                // Same holder/cluster deployment cadence follows balloon cell spacing, not the
                // denser rail slot spacing, so promoted heads advance one target line at a time.
                float physGap = rail.DartClusterAttackGap;
                // ── 2026-05-08: client-side cluster freeze 분기 폐기 ── 롤백 가능.
                // V2UpdateFreezeOnDeployBlock 폐기 + packing physics deploy block obstacle 추가에 따라
                // cluster head 가 다른 deploy block 직전 packing 자연 정지. self.placement 는 cluster head
                // 와 위치 다르므로 IsProgressClear 통과 → 정상 진행. yield/wait 분기 불필요.
                /*
                RailManager.DartOnRail clusterHead = rail.GetClusterHeadDart(visual.holderId);
                bool blockedByDeployPoint = clusterHead != null
                    && rail.GetOtherActiveDeployPointHolderNear(clusterHead.progress, visual.holderId) >= 0;
                if (blockedByDeployPoint)
                {
                    rail.FreezeClusterByHolder(visual.holderId);
                    visual.isClusterFrozen = true;
                    // clamp 한도를 MAX_PLACEMENTS_PER_FRAME * physGap 로 완화 — 정상 catch-up (1.5~3 slot/frame) 보존.
                    float __clampMax_a = MAX_PLACEMENTS_PER_FRAME * physGap;
                    if (distSinceLastPlacement > __clampMax_a) distSinceLastPlacement = __clampMax_a;
                    yield return null;
                    continue;
                }

                if (visual.isClusterFrozen)
                {
                    rail.UnfreezeClusterByHolder(visual.holderId);
                    visual.isClusterFrozen = false;
                }
                */

                float beltSpeed = rail.GetBeltDistancePerSecond();
                distSinceLastPlacement += beltSpeed * Time.deltaTime;

                if (!rail.TryEnterDeployPlacement(visual.holderId))
                {
                    float __clampMax_b = MAX_PLACEMENTS_PER_FRAME * physGap;
                    if (distSinceLastPlacement > __clampMax_b) distSinceLastPlacement = __clampMax_b;
                    yield return null;
                    continue;
                }

                // 사용자 요구 (2026-05-07): slot index 기반 wait — fire 가 비운 slot 의 world 위치가
                // deploy point world 위치에 도달했을 때만 placement.
                // = 매 frame, deploy point 의 현재 slot index 조회 (belt 회전 따라 변함) → IsSlotEmpty 검사.
                // packing physics 로 인한 다른 gap 은 무시 — fire 가 비운 그 slot 이 도착해야 함.
                // 부하/race 방지를 위해 프레임당 최대 3개로 제한.

                // ── LEGACY (2026-05-08 이전 버전, 롤백용) ──────────────────────────────────
                // 이슈: belt 가 frame 당 2+ slot 회전 시 (UserSpeedMultiplier 가속 + low fps)
                //   inner loop 가 같은 deploySlotIndex 만 반복 시도 → 첫 PlaceDart 만 성공,
                //   두 번째 PlaceDart 는 같은 slot 점유로 거부 → break.
                //   그 frame 안에 deploy 가 통과한 다른 slot 들은 영원히 빈 채로 → AAAAA.AAAAAA
                //   → rail full 인데 빈 slot 존재 → deadlock.
                // 신규 버전은 currentDeploySlot + placementsThisFrame 으로 catch-up.
                /*
                int maxPlacementsThisFrame = 3;
                while (visual.magazineRemaining > 0 && maxPlacementsThisFrame-- > 0
                       && distSinceLastPlacement >= physGap)
                {
                    // 현재 belt 회전 기준 deploy point 의 slot index
                    int deploySlotIndex = rail.GetSlotAtPathDistance(fixedDeployProgress);

                    // slot 비어있나? (= 그 slot 의 world 위치가 deploy point 와 일치 + dart 없음)
                    if (!rail.IsSlotEmpty(deploySlotIndex))
                    {
                        // fire-gap 이 아직 deploy point 도달 안 함 — wait. deadlock 가능성 trigger 검사.
                        TryEnterDeadlockIfNeeded(rail);
                        if (distSinceLastPlacement > physGap) distSinceLastPlacement = physGap;
                        break;
                    }

                    // slot-based placement — slot 위치에 dart 배치 (PlaceDart 가 slot 의 world 위치를 progress 로 변환).
                    int dartId = rail.PlaceDart(deploySlotIndex, visual.color, visual.holderId);
                    if (dartId < 0)
                    {
                        // capacity 한도 도달 또는 race → deadlock 가능
                        TryEnterDeadlockIfNeeded(rail);
                        break;
                    }

                    // 진단 로그
                    // ROLLBACK_DEPLOY_DEBUG_LOGS:
                    // Use LogDeployDebug so release/play builds do not allocate this formatted string.
                    LogDeployDebug($"[DEPLOY] holder={visual.holderId} (col {visual.column}) placed dart at slot={deploySlotIndex} (fixedDeployProgress={fixedDeployProgress:F2}). Rail={rail.OccupiedCount}/{rail.SlotCount}. DeadlockHolder={rail.DeadlockHolderId}");

                    if (deployStarted)
                        rail.ActivateDeployPoint(visual.holderId);

                    // overshoot 보존 — physGap 만큼만 차감해서 다음 placement 의 timing drift 방지.
                    // (이전: = 0 reset 했는데, frame jitter 로 ε 손실 → consecutive placements 가 1+ slot 떨어짐 → gap)
                    distSinceLastPlacement -= physGap;
                    visual.magazineRemaining--;

                    if (!deployStarted)
                    {
                        deployStarted = true;
                        rail.ActivateDeployPoint(visual.holderId);
                        if (visual.identifier != null)
                            visual.identifier.SetDartsOnRail(true);
                        if (visual.gameObject != null)
                        {
                            // ROLLBACK_HOLDER_PUNCH_TWEEN:
                            // Restore DOPunchScale here if the first-placement bounce is required.
                            visual.gameObject.transform.localScale = Vector3.one;
                        }
                    }

                    // slot index 기반 placement 후 dart 의 progress = slot 의 path distance.
                    float dartProgress = rail.GetPathDistanceForSlot(deploySlotIndex);

                    Vector3 placedWorldPos = rail.GetDartWorldPosition(dartId);
                    if (placedWorldPos == Vector3.zero)
                        placedWorldPos = rail.GetPositionAtDistance(dartProgress);
                    LaunchDartChild(visual, placedWorldPos);

                    if (visual.magazineText != null)
                        visual.magazineText.SetText("{0}", visual.magazineRemaining);
                        RestoreSpawnerTextMaterialPreset(visual);

                    if (HolderManager.HasInstance)
                        HolderManager.Instance.ConsumeMagazine(visual.holderId);

                    EventBus.Publish(new OnDartPlaced
                    {
                        dartId = dartId,
                        color = visual.color,
                        holderId = visual.holderId,
                        progress = dartProgress
                    });
                }
                */

                // ── 신규-A (2026-05-08, slot-index catch-up — 회귀로 비활성) ───────────────
                // belt 1.5 slot/frame 회전 시 dart 가 deploy point 에서 0.5 slot 어긋남 → drift → 1 slot 갭.
                // progress-based (이전 버전, G:\BalanceProcessor) 로 복귀.
                /*
                int currentDeploySlot = rail.GetSlotAtPathDistance(fixedDeployProgress);
                int slotCount = rail.SlotCount;
                int placementsThisFrame_A = 0;
                while (visual.magazineRemaining > 0
                       && placementsThisFrame_A < MAX_PLACEMENTS_PER_FRAME
                       && distSinceLastPlacement >= physGap)
                {
                    int targetSlot = (currentDeploySlot + placementsThisFrame_A + slotCount) % slotCount;
                    if (!rail.IsSlotEmpty(targetSlot))
                    {
                        TryEnterDeadlockIfNeeded(rail);
                        float __clampMax_c = MAX_PLACEMENTS_PER_FRAME * physGap;
                        if (distSinceLastPlacement > __clampMax_c) distSinceLastPlacement = __clampMax_c;
                        break;
                    }
                    int dartId = rail.PlaceDart(targetSlot, visual.color, visual.holderId);
                    if (dartId < 0) { TryEnterDeadlockIfNeeded(rail); break; }
                    LogDeployDebug($"[DEPLOY] holder={visual.holderId} placed at slot={targetSlot}");
                    if (deployStarted) rail.ActivateDeployPoint(visual.holderId);
                    placementsThisFrame_A++;
                    distSinceLastPlacement -= physGap;
                    visual.magazineRemaining--;
                    if (!deployStarted) {
                        deployStarted = true;
                        rail.ActivateDeployPoint(visual.holderId);
                        if (visual.identifier != null) visual.identifier.SetDartsOnRail(true);
                        if (visual.gameObject != null) {
                            visual.gameObject.transform.localScale = Vector3.one;
                            PlayHolderPunch(visual.gameObject.transform, Vector3.one * 0.08f, 0.15f, 4, 0.3f);
                        }
                    }
                    float dartProgress = rail.GetPathDistanceForSlot(targetSlot);
                    Vector3 placedWorldPos = rail.GetDartWorldPosition(dartId);
                    if (placedWorldPos == Vector3.zero) placedWorldPos = rail.GetPositionAtDistance(dartProgress);
                    LaunchDartChild(visual, placedWorldPos);
                    if (visual.magazineText != null)
                    {
                        visual.magazineText.SetText("{0}", visual.magazineRemaining);
                        RestoreSpawnerTextMaterialPreset(visual);
                    }
                    if (HolderManager.HasInstance) HolderManager.Instance.ConsumeMagazine(visual.holderId);
                    EventBus.Publish(new OnDartPlaced { dartId = dartId, color = visual.color, holderId = visual.holderId, progress = dartProgress });
                }
                */

                // ── 신규-B (2026-05-08, progress-based + overshoot carry, G:\BalanceProcessor 참조) ──
                // overshoot = 누적된 잉여 (한 physGap 초과분).
                // placementProgress = fixedDeployProgress + overshoot — belt 가 overshoot 만큼 더 회전한 시점의 deploy 위치.
                // distSinceLastPlacement = overshoot 으로 잉여만 carry → spacing drift 누적 손실 0.
                // catch-up: 한 frame 안 N=floor(distSinceLastPlacement / physGap) placements 자연 처리.
                var __placeBatchSw = InGamePerfLogger.StartSection();
                int placementsThisFrame = 0;
                bool fixedGateModeThisFrame = rail.ShouldUseFixedDeployPlacement(visual.holderId) || fixedGapBurstUnlocked;
                int maxPlacementsThisFrame = fixedGateModeThisFrame ? MAX_RECOVERY_PLACEMENTS_PER_FRAME : MAX_PLACEMENTS_PER_FRAME;
                while (visual.magazineRemaining > 0
                       && placementsThisFrame < maxPlacementsThisFrame
                       && distSinceLastPlacement >= physGap)
                {
                    float overshoot = distSinceLastPlacement - physGap;
                    bool fixedGateMode = rail.ShouldUseFixedDeployPlacement(visual.holderId);
                    if (!fixedGateMode && !fixedGapBurstUnlocked)
                        fixedGapBurstPlaced = 0;

                    bool lockPlacementToDeployPoint = fixedGateMode && !fixedGapBurstUnlocked;
                    // 이전: deployStarted ? overshoot : 0f.
                    // 문제: deadlock/full 이후 멀리서 fire가 나 capacity만 비면, 실제 fire gap이 deploy point에
                    // 도달하기 전에도 fixedDeployProgress+overshoot 위치에 즉시 배포되어 AABBAABB가 생김.
                    // deadlock holder는 fixed deploy point에 gap이 직접 지나올 때만 배포한다.
                    float appliedOvershoot;
                    if (fixedGapBurstUnlocked)
                    {
                        appliedOvershoot = fixedGapBurstPlaced * physGap;
                    }
                    else
                    {
                        // 기존 방식: deployStarted ? overshoot : 0f.
                        // float appliedOvershoot = (deployStarted && !lockPlacementToDeployPoint) ? overshoot : 0f;
                        // recovery/deadlock에서는 첫 배치만 fixedDeployProgress에 고정해 빈 구간 도착을 확인한다.
                        appliedOvershoot = (deployStarted && !lockPlacementToDeployPoint) ? overshoot : 0f;
                    }
                    float placementProgress = fixedDeployProgress + appliedOvershoot;
                    if (totalPathLen > 0f && placementProgress >= totalPathLen) placementProgress -= totalPathLen;

                    bool useDeadlockFallback = false;
                    if (!rail.IsProgressClear(placementProgress, visual.holderId))
                    {
                        float originalBlockedProgress = placementProgress;
                        int originalBlockedSlot = rail.GetSlotAtPathDistance(originalBlockedProgress);
                        // [Stuck] 진단 로그 — IsProgressClear=false 원인 식별 (자기 cluster.tail vs 외부 dart vs capacity)
                        _lastStuckLogTime.TryGetValue(visual.holderId, out float __lastStuckT);
                        if (DEPLOY_DEBUG_ENABLED && Time.unscaledTime - __lastStuckT > STUCK_LOG_INTERVAL)
                        {
                            _lastStuckLogTime[visual.holderId] = Time.unscaledTime;
                            int __slotIdx = rail.GetSlotAtPathDistance(placementProgress);
                            var __slot = rail.GetSlot(__slotIdx);
                            var __clusterHead = rail.GetClusterHeadDart(visual.holderId);
                            string __headInfo = __clusterHead != null
                                ? $"head.dartId={__clusterHead.dartId} head.progress={__clusterHead.progress:F2} head.frozen={__clusterHead.isFrozen}"
                                : "head=null";
                            string __clearFailReason = rail.GetProgressClearFailReason(placementProgress, visual.holderId);
                            LogDeployDebug($"[Stuck] holder={visual.holderId}(col{visual.column}) magRem={visual.magazineRemaining} " +
                                      $"placementProg={placementProgress:F2} overshoot={overshoot:F3} dist={distSinceLastPlacement:F3} " +
                                      $"appliedOvershoot={appliedOvershoot:F3} fixedProg={fixedDeployProgress:F2} deployStarted={deployStarted} lockFixed={lockPlacementToDeployPoint} " +
                                      $"→ slot{__slotIdx}: occupiedBy=holder{__slot.holderId} dartId={__slot.dartId} color={__slot.dartColor}. " +
                                      $"{__headInfo}. reason={__clearFailReason}. " +
                                      $"Rail={rail.OccupiedCount}/{rail.SlotCount} active={rail.GetActiveDeployPointCount()} dlh={rail.DeadlockHolderId}");
                        }

                        TryEnterDeadlockIfNeeded(rail);

                        // DeadlockMode 의 leftmost 만 fallback — 빈 progress 직접 탐색 후 placement.
                        // 의도 7 ("deploy point 닿아 정렬될 때만 채움") 일시 양보. 이유: placementProgress 와
                        // 빈 progress 위치가 belt 진행 따라 같은 속도로 이동 → 첫 시점에 align 안 되면 영원히
                        // align 안 됨 (timing mismatch deadlock). DeadlockMode 풀기 위해 fallback 필요.
                        // 비-deadlock 상태에선 fallback 안 함 (의도 7 유지).
                        bool canUseLastSlotFallback =
                            ENABLE_DEADLOCK_FALLBACK
                            && rail.DeadlockHolderId == visual.holderId
                            && rail.OccupiedCount >= rail.PhysicalCapacity - DEADLOCK_FALLBACK_REMAINING_SLOTS
                            && rail.OccupiedCount < rail.PhysicalCapacity;

                        if (canUseLastSlotFallback)
                        {
                            float fallbackProgress = rail.FindClearProgressNear(placementProgress, visual.holderId);
                            if (fallbackProgress >= 0f)
                            {
                                int fallbackSlot = rail.GetSlotAtPathDistance(fallbackProgress);
                                string fallbackState = rail.GetProgressClearFailReason(fallbackProgress, visual.holderId);
                                LogDeployDebug($"[DeadlockFallback] holder={visual.holderId}(col{visual.column}) " +
                                          $"fromProg={originalBlockedProgress:F2} fromSlot={originalBlockedSlot} " +
                                          $"toProg={fallbackProgress:F2} toSlot={fallbackSlot} " +
                                          $"overshoot={overshoot:F3} dist={distSinceLastPlacement:F3} " +
                                          $"toState={fallbackState} Rail={rail.OccupiedCount}/{rail.SlotCount} active={rail.GetActiveDeployPointCount()} dlh={rail.DeadlockHolderId}");
                                placementProgress = fallbackProgress;
                                useDeadlockFallback = true;
                            }
                        }

                        if (!useDeadlockFallback)
                        {
                            fixedGapBurstUnlocked = false;
                            fixedGapBurstPlaced = 0;
                            float __clampMax_c = (fixedGateMode ? MAX_RECOVERY_PLACEMENTS_PER_FRAME : MAX_PLACEMENTS_PER_FRAME) * physGap;
                            if (distSinceLastPlacement > __clampMax_c) distSinceLastPlacement = __clampMax_c;
                            break;
                        }
                    }

                    if (!rail.IsDeployProgressPhysicallyClear(placementProgress, visual.color, visual.holderId, out string placementGapInfo))
                    {
                        _lastGapBlockLogTime.TryGetValue(visual.holderId, out float __lastGapBlockT);
                        if (DEPLOY_DEBUG_ENABLED && Time.unscaledTime - __lastGapBlockT > STUCK_LOG_INTERVAL)
                        {
                            _lastGapBlockLogTime[visual.holderId] = Time.unscaledTime;
                            LogDeployDebug($"[DeployGapBlocked] holder={visual.holderId}(col{visual.column}) magRem={visual.magazineRemaining} " +
                                      $"placementProg={placementProgress:F2} overshoot={overshoot:F3} dist={distSinceLastPlacement:F3} " +
                                      $"appliedOvershoot={appliedOvershoot:F3} fixedProg={fixedDeployProgress:F2} deployStarted={deployStarted} lockFixed={lockPlacementToDeployPoint} " +
                                      $"reason={placementGapInfo}. " +
                                      $"advance={rail.GetAdvanceModeDebugInfo()} " +
                                      $"Rail={rail.OccupiedCount}/{rail.SlotCount} active={rail.GetActiveDeployPointCount()} dlh={rail.DeadlockHolderId}");
                        }

                        // ROLLBACK_DEPLOY_CROSSED_DART_REVERT_20260618: ②(추월 강제 DeadlockMode)+워치독 되돌림 —
                        //   회귀(deadlock freeze) 유발로 제거. 추월 데드락은 별도 안전 접근으로 재설계 예정.
                        TryEnterDeadlockIfNeeded(rail);
                        fixedGapBurstUnlocked = false;
                        fixedGapBurstPlaced = 0;
                        float __clampMax_gap = (fixedGateMode ? MAX_RECOVERY_PLACEMENTS_PER_FRAME : MAX_PLACEMENTS_PER_FRAME) * physGap;
                        if (distSinceLastPlacement > __clampMax_gap) distSinceLastPlacement = __clampMax_gap;
                        break;
                    }

                    int dartId = rail.PlaceDartAtProgress(placementProgress, visual.color, visual.holderId);
                    if (dartId < 0)
                    {
                        TryEnterDeadlockIfNeeded(rail);
                        break;
                    }

                    bool wasFirstPlacement = !deployStarted;
                    if (DEPLOY_DEBUG_ENABLED && (LOG_DEPLOY_GAP_DIAG || wasFirstPlacement || useDeadlockFallback))
                    {
                        int placedSlot = rail.GetSlotAtPathDistance(placementProgress);
                        LogDeployDebug($"[DeployPlace] holder={visual.holderId}(col{visual.column}) dartId={dartId} " +
                                  $"progress={placementProgress:F2} slot={placedSlot} fallback={useDeadlockFallback} first={wasFirstPlacement} " +
                                  $"overshoot={overshoot:F3} appliedOvershoot={appliedOvershoot:F3} lockFixed={lockPlacementToDeployPoint} magBefore={visual.magazineRemaining} " +
                                  $"{placementGapInfo} " +
                                  $"Rail={rail.OccupiedCount}/{rail.SlotCount} active={rail.GetActiveDeployPointCount()} dlh={rail.DeadlockHolderId}");

                        if (placementGapInfo.Contains("splitRisk=between"))
                        {
                            LogDeployDebug($"[DeploySplitRisk] holder={visual.holderId}(col{visual.column}) dartId={dartId} " +
                                      $"progress={placementProgress:F2} slot={placedSlot} magBefore={visual.magazineRemaining} " +
                                      $"lockFixed={lockPlacementToDeployPoint} {placementGapInfo} advance={rail.GetAdvanceModeDebugInfo()}");
                        }
                    }

                    if (deployStarted)
                        rail.ActivateDeployPoint(visual.holderId);

                    if (fixedGateMode || fixedGapBurstUnlocked)
                    {
                        fixedGapBurstUnlocked = true;
                        fixedGapBurstPlaced++;
                        distSinceLastPlacement = Mathf.Max(0f, distSinceLastPlacement - physGap);
                    }
                    else
                    {
                        distSinceLastPlacement = wasFirstPlacement ? 0f : overshoot;  // 첫 배치 전 누적 overshoot는 Deploy Point 정렬을 위해 버림
                    }
                    placementsThisFrame++;
                    visual.magazineRemaining--;

                    if (!deployStarted)
                    {
                        deployStarted = true;
                        rail.ActivateDeployPoint(visual.holderId);
                        // 첫 placement 의 BoxOpenIdle 진입은 LaunchDartChild 의 NotifyMagazineDecreasing 으로 일원화됨.
                        if (visual.gameObject != null)
                        {
                            visual.gameObject.transform.localScale = Vector3.one;
                            PlayHolderPunch(visual.gameObject.transform, Vector3.one * 0.08f, 0.15f, 4, 0.3f);
                        }
                    }

                    LaunchDartChild(visual, rail.GetPositionAtDistance(placementProgress));

                    // 매거진 0 도달 순간 1회 — BoxClose → BoxDefault 시퀀스 시작. identifier 내부 idempotent.
                    if (visual.magazineRemaining <= 0 && visual.identifier != null)
                        visual.identifier.PlayBoxCloseToDefault();

                    if (visual.magazineText != null)
                        visual.magazineText.SetText("{0}", visual.magazineRemaining);
                        RestoreSpawnerTextMaterialPreset(visual);

                    if (HolderManager.HasInstance)
                        HolderManager.Instance.ConsumeMagazine(visual.holderId);

                    EventBus.Publish(new OnDartPlaced
                    {
                        dartId = dartId,
                        color = visual.color,
                        holderId = visual.holderId,
                        progress = placementProgress
                    });
                }

                // magazine이 0이 됐으면 즉시 outer loop 종료 (Continue 경합으로 visual이
                // CancelDeployAndReturnToQueue로 빠져 CompleteDeployment가 안 불리는 레이스 방지).
                InGamePerfLogger.EndSection(__placeBatchSw, "HolderDeploy.PlaceBatch");
                if (distSinceLastPlacement < physGap)
                {
                    fixedGapBurstUnlocked = false;
                    fixedGapBurstPlaced = 0;
                }

                if (visual.magazineRemaining <= 0) break;

                yield return null;
            }

            // ── Phase 3: deploy point 해제 → frozen 다트 unfreeze ──
            rail.UnregisterDeployPoint(visual.holderId);
            // 사용자 요구: Slot Reservation 해제 — magazine 다 spawn 후 다른 holder 가 이 영역 사용 가능.
            rail.ReleaseHolderReservation(visual.holderId);
            rail.ExitDeployPlacement(visual.holderId);

            // ── Phase 4: Cleanup ──
            CompleteDeployment(visual);
        }

        // 데드락 진단 로그 throttle — 동일 메시지 2초당 1회
        private float _lastDeadlockDiagLogTime;
        private const float DEADLOCK_DIAG_LOG_INTERVAL = 2.0f;

        // [Stuck] 진단 로그 throttle — holder 별 1초당 1회. IsProgressClear=false 시 점유 정보 log.
        private readonly Dictionary<int, float> _lastStuckLogTime = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _lastGapBlockLogTime = new Dictionary<int, float>();
        private const float STUCK_LOG_INTERVAL = 1.0f;
        private const bool ENABLE_DEADLOCK_FALLBACK = false; // 기존 FindClearProgressNear 직접 배포 fallback은 비활성화.
        private const int DEADLOCK_FALLBACK_REMAINING_SLOTS = 1; // 199/200 같은 마지막 1칸 deadlock 보정 전용.
        // ROLLBACK_DART_RUNTIME_LOG_THROTTLE:
        // Successful deploy logs allocate/format large strings during dense deployment and cause
        // visible frame drops. Re-enable only while capturing a short placement sample.
        private const bool LOG_DEPLOY_GAP_DIAG = false; // 성공 배포가 실제 어떤 앞/뒤 dart 사이에 들어가는지 임시 진단.

        /// <summary>
        /// 데드락 감지 — rail full + 다중 active deploy point 인 경우 leftmost holder 선택해 EnterDeadlockMode.
        /// 매 placement 실패 시 호출. 이미 deadlock 진입 상태면 no-op.
        /// </summary>
        private void TryEnterDeadlockIfNeeded(RailManager rail)
        {
            // [데드락 트리거 stale 방어] 트리거 holder 가 더 이상 isDeploying 이 아니면(abort/취소/소멸 등으로
            //   ExitDeadlockMode 가 누락된 경우) 데드락 모드를 해제한다 — 다른 holder 가 1355 라인에서 영구
            //   pause 되어 "배포 멈춤" 이 되는 버그 복구. 트리거가 정상 배포 중이면 기존 동작 그대로 유지.
            if (rail.DeadlockHolderId >= 0)
            {
                if (_holderVisuals.TryGetValue(rail.DeadlockHolderId, out HolderVisual dlVisual)
                    && dlVisual != null && dlVisual.isDeploying)
                    return; // 트리거 유효 — 정상 직렬화 유지.

                rail.ExitDeadlockMode(); // 트리거 stale → 해제. 다음 프레임 재평가에서 유효 트리거로 재진입 가능.
                return;
            }

            int activeCount = rail.GetActiveDeployPointCount();
            int isDeployingCount = 0;
            foreach (var kvp in _holderVisuals)
            {
                if (kvp.Value.isDeploying) isDeployingCount++;
            }

            // 이전: rail.IsRailNearFull() == capacity - 1 고정 기준.
            // 198/200에서도 active/deploying holder들이 deploy point를 막으면 gap이 순환하지 못해 stuck.
            int deployPressure = Mathf.Max(1, Mathf.Max(activeCount, isDeployingCount));
            // 이전: min 2칸. active=1/isDeploying=1에서도 197/200에서 자기 cluster tail에 막히는 케이스 발생.
            // deadlock advance 허용치와 맞춰 최소 3칸부터 nearFull로 취급한다.
            // ㅡ자(단면) 다열 레일은 deploy-point 밀도 최고 → 윈도우를 deploys 로 더 키움 (RailManager.DeadlockBeltAdvanceEmptySlots 와 정합).
            //   기존 clamp(+1, 3, 5) 는 5 에서 캡 → 5열 ㅡ 에서 빈칸 여유 0 → 잠김. +2 / 상한 8 로 완화.
            int emptyAllowance = Mathf.Clamp(deployPressure + 2, 3, 8);
            int nearFullThreshold = Mathf.Max(0, rail.PhysicalCapacity - emptyAllowance);
            bool nearFull = rail.OccupiedCount >= nearFullThreshold;

            if (DEPLOY_DEBUG_ENABLED && Time.unscaledTime - _lastDeadlockDiagLogTime > DEADLOCK_DIAG_LOG_INTERVAL)
            {
                _lastDeadlockDiagLogTime = Time.unscaledTime;
                LogDeployDebug($"[Deadlock][Diag] check: rail={rail.OccupiedCount}/{rail.SlotCount} (nearFull={nearFull}), " +
                          $"threshold={nearFullThreshold} emptyAllowance={emptyAllowance}, " +
                          $"activeDeploys={activeCount}, isDeployingHolders={isDeployingCount}, " +
                          $"deadlockHolder={rail.DeadlockHolderId}");
            }

            if (!nearFull) return;
            // 단일 holder 도 rail full 상태에서 ABABAB / cluster 정렬 깨짐 발생 → 자기 자신이 deadlock holder 되어 buffer 사용.
            if (activeCount < 1) return;

            int leftmostHolderId = -1;
            int leftmostCol = int.MaxValue;
            foreach (var kvp in _holderVisuals)
            {
                var v = kvp.Value;
                if (!v.isDeploying) continue;
                if (v.column < leftmostCol)
                {
                    leftmostCol = v.column;
                    leftmostHolderId = v.holderId;
                }
            }
            if (leftmostHolderId < 0)
            {
                Debug.LogWarning($"[Deadlock] Detect FAIL — no isDeploying holder found despite {activeCount} active deploy points.");
                return;
            }

            LogDeployDebug($"[Deadlock] Detected. Leftmost = holder {leftmostHolderId} (col {leftmostCol}). " +
                      $"Rail {rail.OccupiedCount}/{rail.SlotCount}, active deploys = {activeCount}.");
            rail.EnterDeadlockMode(leftmostHolderId);
        }

        /// <summary>
        /// HolderIdentifier의 Dart 슬롯에서 하나를 꺼내 슬롯 위치로 날림.
        /// 매거진 감소 시점을 박스 FSM에 알려 BoxOpenIdle 유지 + idle-decay 재시작.
        /// </summary>
        private void LaunchDartChild(HolderVisual visual, Vector3 slotWorldPos)
        {
            if (visual.identifier == null) return;

            float dist = Vector3.Distance(
                visual.gameObject != null ? visual.gameObject.transform.position : slotWorldPos,
                slotWorldPos);
            float duration = Mathf.Clamp(dist * 0.15f, 0.25f, 0.5f);

            // 유저 가속 반영 — 2x 토글 시 다트 발사 비주얼도 2x 빠르게
            float speedMult = RailManager.HasInstance ? RailManager.Instance.UserSpeedMultiplier : 1f;
            if (speedMult > 0.001f) duration /= speedMult;

            visual.identifier.NotifyMagazineDecreasing();
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
            visual.isClusterFrozen = false;

            _colBusy[col] = false;

            // 데드락 trigger holder 가 deploy 종료 → ExitDeadlockMode (다른 holder 들 재개).
            if (RailManager.HasInstance
                && RailManager.Instance.DeadlockHolderId == visual.holderId)
            {
                RailManager.Instance.ExitDeadlockMode();
            }

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
            RebuildChainLines();
        }

        #endregion

        #region Private Methods — Chain Lines

        /// <summary>Chain 그룹 연결선 전체 재생성.</summary>
        private void RebuildChainLines()
        {
            ClearChainLines();
            if (!HolderManager.HasInstance) return;

            var processedGroups = new HashSet<int>();
            foreach (var kvp in _holderVisuals)
            {
                var hData = HolderManager.Instance.FindHolderPublic(kvp.Value.holderId);
                if (hData == null || hData.chainGroupId < 0 || hData.isConsumed) continue;
                if (!processedGroups.Add(hData.chainGroupId)) continue;

                var members = HolderManager.Instance.GetChainGroup(hData.chainGroupId);
                CreateChainLinesForGroup(hData.chainGroupId, members);
            }
        }

        private struct ChainEdge
        {
            public int idA;
            public int idB;
            public float sqrDistance;
        }

        private readonly List<HolderVisual> _chainGroupVisuals = new List<HolderVisual>();
        private readonly List<ChainEdge> _chainCandidateEdges = new List<ChainEdge>();

        // ROLLBACK_CHAIN_LINK_TOPOLOGY_STABLE_20260630:
        //   groupId 의 '경로 순서'(초기 1회 캐시)대로 연속한 '현재 살아있는' 멤버만 잇는다.
        //   소비/미스폰된 멤버는 건너뛰어 그 이웃을 직접 연결(체인이 끊김 없이 이어짐).
        // ROLLBACK_CHAIN_LINK_ADJACENT_ONLY_20260630:
        //   캐시된 '경로 순서'(초기 토폴로지)에서 '인접한' 쌍만 잇는다. 둘 다 살아있을 때만 연결하고,
        //   소비/미스폰 멤버는 브리지하지 않는다 → 중간 멤버(B,C)가 빠져도 끝(A,D)이 엉뚱하게 이어지지 않음.
        //   (색은 무관 — gameplay chainGroupId 기준 토폴로지 그대로. 혼색 링크는 각 반쪽이 제 색을 가짐.)
        private void CreateChainLinesForGroup(int groupId, List<int> members)
        {
            List<int> order = GetOrComputeChainOrder(groupId, members);

            for (int i = 0; i + 1 < order.Count; i++)
            {
                int idA = order[i];
                int idB = order[i + 1];
                if (!IsChainMemberLinkable(idA, out HolderVisual va)) continue;
                if (!IsChainMemberLinkable(idB, out HolderVisual vb)) continue;

                // 색·위치 정합: key 는 작은id_큰id, CreateChainLine 의 a(=colorA) 도 작은id 로 맞춰 교차 방지.
                int lo = Mathf.Min(idA, idB);
                int hi = Mathf.Max(idA, idB);
                HolderVisual loV = (idA < idB) ? va : vb;
                HolderVisual hiV = (idA < idB) ? vb : va;
                CreateChainLine($"{lo}_{hi}", loV, hiV);
            }
        }

        private bool IsChainMemberLinkable(int id, out HolderVisual v)
        {
            v = null;
            if (!_holderVisuals.TryGetValue(id, out v) || v.gameObject == null) return false;
            var hd = HolderManager.HasInstance ? HolderManager.Instance.FindHolderPublic(id) : null;
            return hd != null && !hd.isConsumed;
        }

        // 초기 1회: 현재(=초기 레이아웃) 위치로 MST → 경로 끝점부터 순회해 '경로 순서' 를 구하고 캐시.
        private List<int> GetOrComputeChainOrder(int groupId, List<int> members)
        {
            if (_chainOrderByGroup.TryGetValue(groupId, out List<int> cached)) return cached;
            List<int> order = ComputeChainPathOrder(members);
            _chainOrderByGroup[groupId] = order;
            return order;
        }

        private List<int> ComputeChainPathOrder(List<int> members)
        {
            _chainGroupVisuals.Clear();
            for (int i = 0; i < members.Count; i++)
            {
                int id = members[i];
                if (_holderVisuals.TryGetValue(id, out HolderVisual visual) && visual.gameObject != null)
                    _chainGroupVisuals.Add(visual);
            }

            var order = new List<int>(_chainGroupVisuals.Count);
            if (_chainGroupVisuals.Count <= 2)
            {
                for (int i = 0; i < _chainGroupVisuals.Count; i++) order.Add(_chainGroupVisuals[i].holderId);
                return order;
            }

            // 거리 기반 MST (Kruskal) — 초기 레이아웃에선 체인 = 경로라서 MST 가 곧 경로다.
            _chainCandidateEdges.Clear();
            for (int i = 0; i < _chainGroupVisuals.Count; i++)
            {
                for (int j = i + 1; j < _chainGroupVisuals.Count; j++)
                {
                    Vector3 a = _chainGroupVisuals[i].gameObject.transform.position;
                    Vector3 b = _chainGroupVisuals[j].gameObject.transform.position;
                    Vector2 flat = new Vector2(a.x - b.x, a.z - b.z);
                    _chainCandidateEdges.Add(new ChainEdge
                    {
                        idA = _chainGroupVisuals[i].holderId,
                        idB = _chainGroupVisuals[j].holderId,
                        sqrDistance = flat.sqrMagnitude
                    });
                }
            }
            _chainCandidateEdges.Sort((a, b) =>
            {
                int dist = a.sqrDistance.CompareTo(b.sqrDistance);
                if (dist != 0) return dist;
                int minA = Mathf.Min(a.idA, a.idB);
                int minB = Mathf.Min(b.idA, b.idB);
                if (minA != minB) return minA.CompareTo(minB);
                return Mathf.Max(a.idA, a.idB).CompareTo(Mathf.Max(b.idA, b.idB));
            });

            var parent = new Dictionary<int, int>(_chainGroupVisuals.Count);
            var adj = new Dictionary<int, List<int>>(_chainGroupVisuals.Count);
            for (int i = 0; i < _chainGroupVisuals.Count; i++)
            {
                int id = _chainGroupVisuals[i].holderId;
                parent[id] = id;
                adj[id] = new List<int>(2);
            }

            int created = 0;
            for (int i = 0; i < _chainCandidateEdges.Count && created < _chainGroupVisuals.Count - 1; i++)
            {
                var edge = _chainCandidateEdges[i];
                if (!UnionChain(parent, edge.idA, edge.idB)) continue;
                adj[edge.idA].Add(edge.idB);
                adj[edge.idB].Add(edge.idA);
                created++;
            }

            // 경로 끝점(차수 1)부터 순회 → 순서. (끝점 없으면 첫 멤버부터)
            int start = _chainGroupVisuals[0].holderId;
            for (int i = 0; i < _chainGroupVisuals.Count; i++)
            {
                int id = _chainGroupVisuals[i].holderId;
                if (adj[id].Count == 1) { start = id; break; }
            }
            var visited = new HashSet<int>();
            int cur = start, prevNode = -1;
            while (cur != -1 && !visited.Contains(cur))
            {
                order.Add(cur);
                visited.Add(cur);
                int next = -1;
                List<int> ns = adj[cur];
                for (int k = 0; k < ns.Count; k++)
                {
                    if (ns[k] != prevNode && !visited.Contains(ns[k])) { next = ns[k]; break; }
                }
                prevNode = cur;
                cur = next;
            }
            // 분기/미연결로 누락된 멤버는 뒤에 덧붙임.
            for (int i = 0; i < _chainGroupVisuals.Count; i++)
            {
                int id = _chainGroupVisuals[i].holderId;
                if (!visited.Contains(id)) order.Add(id);
            }
            return order;
        }

        private static int FindChainRoot(Dictionary<int, int> parent, int id)
        {
            int root = id;
            while (parent[root] != root)
                root = parent[root];

            while (parent[id] != id)
            {
                int next = parent[id];
                parent[id] = root;
                id = next;
            }
            return root;
        }

        private static bool UnionChain(Dictionary<int, int> parent, int a, int b)
        {
            int rootA = FindChainRoot(parent, a);
            int rootB = FindChainRoot(parent, b);
            if (rootA == rootB) return false;
            parent[rootB] = rootA;
            return true;
        }

        // 모든 ChainLine 이 공유하는 단일 Material. LineRenderer.startColor/endColor 가
        // per-line 색상을 처리하므로 material 자체는 1개만 있으면 충분 — instance 누수 방지.
        private static Material _sharedChainLineMat;

        private void CreateChainLine(string key, HolderVisual a, HolderVisual b)
        {
            Color colorA = GetColor(a.color);
            Color colorB = GetColor(b.color);
            // [Defense 2026-05-11] Shader.Find 결과 null 가능 (mobile 빌드에서 strip 시) → null 방어.
            // 원본: if (_sharedChainLineMat == null) _sharedChainLineMat = new Material(Shader.Find("Sprites/Default"));
            if (_sharedChainLineMat == null)
            {
                // ROLLBACK_CHAIN_LINE_URP_MATERIAL:
                // Previous runtime material used Sprites/Default. Chain lines are LineRenderers,
                // so prefer URP unlit shaders and avoid pulling Default/Sprite into play builds.
                Shader chainShader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                    ?? Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Unlit/Color");
                if (chainShader != null)
                {
                    _sharedChainLineMat = new Material(chainShader);
                    _sharedChainLineMat.hideFlags = HideFlags.HideAndDontSave;
                    if (_sharedChainLineMat.HasProperty("_BaseColor"))
                        _sharedChainLineMat.SetColor("_BaseColor", Color.white);
                    if (_sharedChainLineMat.HasProperty("_Color"))
                        _sharedChainLineMat.SetColor("_Color", Color.white);
                }
                else
                {
                    Debug.LogError("[HolderVisualManager] No shader for chain line — chain line will not render.");
                    return;
                }
            }

            var go = new GameObject($"ChainLine_{key}");

            // A색 절반
            var lrA = go.AddComponent<LineRenderer>();
            lrA.positionCount = 2;
            lrA.startWidth = CHAIN_LINE_WIDTH;
            lrA.endWidth = CHAIN_LINE_WIDTH;
            lrA.useWorldSpace = true;
            lrA.alignment = LineAlignment.View;
            // ROLLBACK_CHAIN_LINE_CAP_VERTICES_20260623:
            // Linked Dart Box lines should have flat ends. Rounded caps made the line look
            // swollen at the holder connection points.
            lrA.numCapVertices = 0;
            lrA.sortingOrder = 5;
            lrA.startColor = colorA;
            lrA.endColor = colorA;
            lrA.sharedMaterial = _sharedChainLineMat;

            // B색 절반 — 별도 자식 오브젝트
            var goB = new GameObject($"ChainLineB_{key}");
            goB.transform.SetParent(go.transform, false);
            var lrB = goB.AddComponent<LineRenderer>();
            lrB.positionCount = 2;
            lrB.startWidth = CHAIN_LINE_WIDTH;
            lrB.endWidth = CHAIN_LINE_WIDTH;
            lrB.useWorldSpace = true;
            lrB.alignment = LineAlignment.View;
            // ROLLBACK_CHAIN_LINE_CAP_VERTICES_20260623:
            // Keep the child overlay line flat-ended as well.
            lrB.numCapVertices = 0;
            lrB.sortingOrder = 5;
            lrB.startColor = colorB;
            lrB.endColor = colorB;
            lrB.sharedMaterial = _sharedChainLineMat;

            _chainLines[key] = go;
        }

        /// <summary>매 프레임 Chain 연결선 위치 갱신.</summary>
        // 캐시: String.Split/GetComponent 매 프레임 호출 방지
        private struct ChainLineCache
        {
            public int idA, idB;
            public LineRenderer lrA, lrChild;
        }
        private readonly Dictionary<string, ChainLineCache> _chainCache = new Dictionary<string, ChainLineCache>();
        private readonly List<string> _chainRemoveKeys = new List<string>();

        private void UpdateChainLines()
        {
            _chainRemoveKeys.Clear();
            foreach (var kvp in _chainLines)
            {
                if (!_chainCache.TryGetValue(kvp.Key, out ChainLineCache cache))
                {
                    // 첫 호출 시 1번만 파싱 + GetComponent
                    var ids = kvp.Key.Split('_');
                    if (ids.Length != 2) continue;
                    cache.idA = int.Parse(ids[0]);
                    cache.idB = int.Parse(ids[1]);
                    cache.lrA = kvp.Value != null ? kvp.Value.GetComponent<LineRenderer>() : null;
                    cache.lrChild = (kvp.Value != null && kvp.Value.transform.childCount > 0)
                        ? kvp.Value.transform.GetChild(0).GetComponent<LineRenderer>() : null;
                    _chainCache[kvp.Key] = cache;
                }

                bool validA = _holderVisuals.TryGetValue(cache.idA, out HolderVisual vA) && vA.gameObject != null;
                bool validB = _holderVisuals.TryGetValue(cache.idB, out HolderVisual vB) && vB.gameObject != null;

                if (!validA || !validB)
                {
                    if (kvp.Value != null) Destroy(kvp.Value);
                    _chainRemoveKeys.Add(kvp.Key);
                    continue;
                }

                Vector3 baseA = vA.gameObject.transform.position;
                Vector3 baseB = vB.gameObject.transform.position;
                Vector3 flatDelta = new Vector3(baseB.x - baseA.x, 0f, baseB.z - baseA.z);
                float planarDistance = flatDelta.magnitude;
                Vector3 dirAtoB = planarDistance > 0.001f ? flatDelta / planarDistance : Vector3.right;
                // [2026-05-19] Chain 연결점 = 보관함 띠 가장자리 (중앙→중앙 X, 우측 중앙→좌측 중앙 O).
                // Same-row adjacent holders collapse to a zero-length line if the offset is half spacing,
                // so keep a visible bridge segment between the two edges.
                float spacingBase = Mathf.Min(_columnSpacing, _rowSpacing);
                float edgeOffset = Mathf.Min(spacingBase * CHAIN_LINE_EDGE_RATIO, planarDistance * CHAIN_LINE_EDGE_RATIO);
                float maxEdgeOffset = Mathf.Max(0f, (planarDistance - CHAIN_LINE_MIN_LENGTH) * 0.5f);
                edgeOffset = Mathf.Min(edgeOffset, maxEdgeOffset);
                Vector3 sideOffset = dirAtoB * edgeOffset;

                Vector3 posA = baseA + Vector3.up * CHAIN_LINE_Y_OFFSET + sideOffset;
                Vector3 posB = baseB + Vector3.up * CHAIN_LINE_Y_OFFSET - sideOffset;
                Vector3 mid = (posA + posB) * 0.5f;

                if (cache.lrA != null) { cache.lrA.SetPosition(0, posA); cache.lrA.SetPosition(1, mid); }
                if (cache.lrChild != null) { cache.lrChild.SetPosition(0, mid); cache.lrChild.SetPosition(1, posB); }
            }
            for (int i = 0; i < _chainRemoveKeys.Count; i++)
            {
                _chainCache.Remove(_chainRemoveKeys[i]);
                _chainLines.Remove(_chainRemoveKeys[i]);
            }
        }

        private void ClearChainLines()
        {
            foreach (var kvp in _chainLines)
            {
                if (kvp.Value != null) Destroy(kvp.Value);
            }
            _chainLines.Clear();
            _chainCache.Clear();
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
            ResetDeployQueues();
            StopAllCoroutines();
            ClearAllVisuals();
        }

        private void HandleBoardFailed(OnBoardFailed evt)
        {
            _boardFinished = true;
            ResetDeployQueues();
            StopAllCoroutines();
        }

        private void HandleHolderThawed(OnHolderThawed evt)
        {
            if (!_holderVisuals.TryGetValue(evt.holderId, out HolderVisual visual)) return;

            if (visual.identifier != null)
                visual.identifier.SetFrozen(false);

            if (visual.gameObject != null)
                visual.gameObject.transform.DOPunchScale(Vector3.one * 0.16f, 0.28f, 8, 0.75f);

            // 해동 시 텍스트를 탄창 수로 복원
            if (visual.magazineText != null)
            {
                visual.magazineText.SetText("{0}", visual.magazineRemaining);
                visual.magazineText.color = Color.white;
                visual.magazineText.fontSize = MAGAZINE_FONT_SIZE;
                RestoreSpawnerTextMaterialPreset(visual);
            }

            Color originalColor = GetColor(visual.color);
            if (visual.identifier != null && visual.identifier.HasColorRenderers)
                visual.identifier.ApplyColor(originalColor);
            else if (visual.gameObject != null)
                ApplyColorToRenderers(visual.gameObject, originalColor);
        }

        private void HandleFrozenHPChanged(OnFrozenHPChanged evt)
        {
            if (!_holderVisuals.TryGetValue(evt.holderId, out HolderVisual visual)) return;
            bool isFrozenHolder = IsFrozenHolderVisual(visual);
            if (visual.magazineText != null)
            {
                visual.magazineText.SetText("{0}", evt.remainingHP);
                if (isFrozenHolder)
                {
                    visual.magazineText.color = FROZEN_MAGAZINE_COLOR;
                    visual.magazineText.fontSize = MAGAZINE_FONT_SIZE;
                }
                RestoreSpawnerTextMaterialPreset(visual);
            }

            // ROLLBACK_FROZEN_BOX_HIT_FX_20260626:
            // Only remaining HP ticks play BoxFrozenHit. Final thaw/break uses SetFrozen(false).
            if (isFrozenHolder && evt.remainingHP > 0 && visual.identifier != null)
            {
                visual.identifier.TriggerFrozenHit();
                // ROLLBACK_GIMMICK_SFX_TABLE_20260703:
                // Frozen Dart Box hit uses BoxFrozen_Hit.mp3 on remaining-HP ticks only.
                if (AudioManager.HasInstance) AudioManager.Instance.PlayFrozenBoxHit();
            }

            // ROLLBACK_SPAWNER_CONSUME_DESPAWN_20260624 (#4 + #5):
            // The Spawner reuses this OnFrozenHPChanged channel for its remaining-count text. When a
            // Spawner (Pipe/Glass Pipe) reaches 0 (all holders sent), play a disappear animation and
            // return its visual to the pool — previously it just flipped isConsumed and lingered on screen.
            // Removing the visual drops the column's spawnerCount to 0, so RepositionColumnHolders no longer
            // pins the holders that were BEHIND the pipe → they advance to the front and become deployable
            // like normal holders (#5). Gated to Spawner types so a Frozen holder hitting 0 is NOT affected.
            if (evt.remainingHP <= 0 && HolderManager.HasInstance && visual.gameObject != null)
            {
                var sData = HolderManager.Instance.FindHolderPublic(evt.holderId);
                if (sData != null && (sData.queueGimmick == GimmickManager.GIMMICK_SPAWNER_T
                                   || sData.queueGimmick == GimmickManager.GIMMICK_SPAWNER_O))
                {
                    int col = visual.column;
                    // ROLLBACK_SPAWNER_END_PARTICLE_20260624 (#4): 사이즈 줄이는 연출 대신 EndParticle 활성화.
                    if (visual.identifier != null) visual.identifier.PlaySpawnerEndParticle();
                    visual.gameObject.transform.DOKill(false);
                    DG.Tweening.DOVirtual.DelayedCall(0.6f, () =>
                    {
                        if (visual != null && visual.gameObject != null)
                            ReturnHolderToPool(visual);
                        _holderVisuals.Remove(evt.holderId);
                        RepositionColumnHolders(col); // 뒤 홀더들이 정상 큐로 전진 → 사용 가능
                        RebuildChainLines();
                    });
                }
            }
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
                    RebuildChainLines();
                });
        }

        private void HandleHolderRevealed(OnHolderRevealed evt)
        {
            if (!_holderVisuals.TryGetValue(evt.holderId, out HolderVisual visual)) return;

            // Hidden 해금 애니메이션
            if (visual.identifier != null)
                visual.identifier.TriggerHiddenEnd();

            if (visual.magazineText != null)
                visual.magazineText.SetText(string.Empty);
                RestoreSpawnerTextMaterialPreset(visual);

            Transform tr = visual.gameObject != null ? visual.gameObject.transform : null;
            if (tr != null)
            {
                tr.DOPunchScale(Vector3.one * 0.18f, 0.32f, 8, 0.78f);
                tr.DOPunchRotation(new Vector3(0f, 10f, 0f), 0.28f, 6, 0.65f);
            }

            // Hidden Material → 원래 색상 복원
            Color originalColor = GetColor(visual.color);
            if (visual.identifier != null && visual.identifier.HasColorRenderers)
                visual.identifier.ApplyColor(originalColor);
            else if (visual.gameObject != null)
                ApplyColorToRenderers(visual.gameObject, originalColor);

            // 텍스트도 "?" → 실제 탄창 수로 변경
            DOVirtual.DelayedCall(0.1f, () =>
            {
                if (visual.magazineText != null)
                {
                    // 해제 시 일반 표시 규칙 복귀: fontSize/color 원복 후 SetText
                    visual.magazineText.fontSize = MAGAZINE_FONT_SIZE;
                    visual.magazineText.color = IsFrozenHolderVisual(visual) ? FROZEN_MAGAZINE_COLOR : Color.white;
                    visual.magazineText.SetText("{0}", visual.magazineRemaining);
                }
            });
        }

        private void HandleHolderClickAnim(OnHolderClickAnim evt)
        {
            if (_holderVisuals.TryGetValue(evt.holderId, out HolderVisual visual))
            {
                if (visual.identifier != null)
                    visual.identifier.TriggerClick();
            }
        }

        private void HandleHolderColumnBlocked(OnHolderColumnBlocked evt)
        {
            if (!_holderVisuals.TryGetValue(evt.holderId, out HolderVisual visual))
                return;
            if (visual.gameObject == null)
                return;

            HolderVisual blocker = FindColumnBlockerVisual(evt.column, evt.holderId, true);
            Vector3 start = visual.gameObject.transform.position;
            Vector3 push = start + Vector3.forward * Mathf.Min(_rowSpacing * 0.45f, 0.9f);

            if (blocker != null && blocker.gameObject != null)
            {
                Vector3 blockerPos = blocker.gameObject.transform.position;
                float stopZ = blockerPos.z - _rowSpacing * 0.82f;
                if (stopZ > start.z + 0.1f)
                    push = new Vector3(start.x, start.y, Mathf.Min(stopZ, start.z + _rowSpacing * 0.7f));
            }

            visual.gameObject.transform.DOKill(false);
            visual.gameObject.transform.localScale = Vector3.one;
            Sequence seq = DOTween.Sequence();
            seq.Append(visual.gameObject.transform.DOMove(push, 0.12f).SetEase(Ease.OutQuad));
            AppendHolderPunch(seq, visual.gameObject.transform, Vector3.one * 0.08f, 0.12f, 3, 0.35f);
            seq.Append(visual.gameObject.transform.DOMove(start, 0.16f).SetEase(Ease.OutQuad));
        }

        private readonly List<int> _continueReturnIds = new List<int>(8);
        private readonly List<int> _continueRedriveIds = new List<int>(8);

        private void HandleContinueApplied(OnContinueApplied evt)
        {
            _boardFinished = false;

            // fail 시 HandleBoardFailed 의 StopAllCoroutines 로 deploy 코루틴이 이미 죽은 상태.
            // 죽은 코루틴의 stale 큐 항목 제거 — 아래 재구동(StartDeploy)이 깨끗하게 재enqueue.
            ResetDeployQueues();

            // [색상 필터] 제거된 다트 색(holderResetColor) holder 만 큐 복귀, 다른 색은 재구동(이어 배포).
            //   이전: 색 구분 없이 전부 큐 복귀 → 다른 색 보관함이 레일에서 빠져 "사라짐". 그게 본 버그.
            //   -1(제거 없음/fallback)이면 전부 큐 복귀(이전 동작).
            int resetColor = evt.holderResetColor;

            // 반복 중 StartDeploy 가 큐/코루틴을 건드리므로 ID 를 먼저 수집한 뒤 처리.
            _continueReturnIds.Clear();
            _continueRedriveIds.Clear();
            foreach (var kvp in _holderVisuals)
            {
                HolderVisual visual = kvp.Value;
                if (!visual.isDeploying && !visual.isMovingToRail) continue; // 대기(미탭) holder 영향 X
                bool isTarget = resetColor < 0 || visual.color == resetColor;
                (isTarget ? _continueReturnIds : _continueRedriveIds).Add(visual.holderId);
            }

            // 제거된 색 holder: 큐 복귀(재탭 대상) — 다트가 사라졌으니 즉시 재배포하지 않음.
            for (int i = 0; i < _continueReturnIds.Count; i++)
                ReturnActiveHolderToQueue(_continueReturnIds[i]);

            // 다른 색 holder: 검증된 탭 경로(StartDeploy)로 재구동 → 남은 magazine 이어 배포(놓침 방지).
            for (int i = 0; i < _continueRedriveIds.Count; i++)
                RedriveActiveHolder(_continueRedriveIds[i]);
        }

        /// <summary>활성 holder 를 큐로 복귀(재탭 대상): 배포점 해제 + 데이터 리셋 + 컬럼 재배치.</summary>
        private void ReturnActiveHolderToQueue(int holderId)
        {
            if (!_holderVisuals.TryGetValue(holderId, out HolderVisual visual)) return;

            visual.deployGeneration++; // 죽은 코루틴 잔재 stale 처리
            if (RailManager.HasInstance)
            {
                RailManager.Instance.UnregisterDeployPoint(holderId);
                RailManager.Instance.ReleaseHolderReservation(holderId);
                RailManager.Instance.ExitDeployPlacement(holderId);
            }
            visual.isDeploying = false;
            visual.isMovingToRail = false;
            visual.isWaiting = false;
            visual.isClusterFrozen = false;
            if (visual.gameObject != null)
            {
                visual.gameObject.transform.DOKill();
                visual.gameObject.transform.localScale = Vector3.one;
            }
            if (visual.identifier != null)
                visual.identifier.SetDartsOnRail(false);
            if (HolderManager.HasInstance) HolderManager.Instance.UndoDeploy(holderId);
            RepositionColumnHolders(visual.column);
        }

        /// <summary>죽은 deploy 코루틴 복구 — 배포점/플래그 정리 후 StartDeploy 로 남은 magazine 이어 배포.
        /// 검증된 탭 경로를 재사용해 신규 데드락/연속공격 위험 최소화.</summary>
        private void RedriveActiveHolder(int holderId)
        {
            if (!_holderVisuals.TryGetValue(holderId, out HolderVisual visual)) return;

            // 기존 배포점/예약 정리 → StartDeploy 가 깨끗하게 재등록.
            if (RailManager.HasInstance)
            {
                RailManager.Instance.UnregisterDeployPoint(holderId);
                RailManager.Instance.ReleaseHolderReservation(holderId);
                RailManager.Instance.ExitDeployPlacement(holderId);
            }
            visual.deployGeneration++;     // 죽은 코루틴 잔재 stale 처리
            visual.isDeploying = false;
            visual.isMovingToRail = false; // StartDeploy 진입 가드 통과
            visual.isWaiting = false;
            visual.isClusterFrozen = false;
            if (visual.gameObject != null) visual.gameObject.transform.DOKill();
            _cancelledHolders.Remove(holderId); // 직전 cancel 플래그 잔재 제거(즉시 yield break 방지)

            StartDeploy(holderId);          // 검증된 경로로 재배포 (move → 남은 magazine fire)
        }

        #endregion
    }
}
