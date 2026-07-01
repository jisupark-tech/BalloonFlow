#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEditor;

namespace BalloonFlow
{
    /// <summary>
    /// MapMaker — 3-Panel Level Editor (v2)
    /// Left: Level list (scroll) — click to load/edit
    /// Center: 3D board preview with colored balloons, gimmick marks, grid lines,
    ///         holder info, balance validation, conveyor path visualization
    /// Right: Settings (scroll) — grid, palette, holders, rail, conveyor, waypoint, export, test play
    /// </summary>
    public class MapMakerController : MonoBehaviour
    {
        #region Constants

        /// <summary>PixelArtConverter 28색 팔레트와 동기화.</summary>
        private static readonly Color[] PALETTE =
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
            new Color(158/255f,  61/255f,  86/255f),  // 18: Wine — #9E3D56 (.py 레퍼런스 정합, 2026-06-24)
            new Color(167/255f, 221/255f, 148/255f),  // 19: Mint
            new Color( 89/255f,  46/255f, 126/255f),  // 20: Indigo
            new Color(220/255f, 120/255f, 129/255f),  // 21: Rose
            new Color(174/255f, 178/255f, 194/255f),  // 22: Silver — [2026-06-12] #D9D9E7→#AEB2C2, 흰색(6)과 구분 강화
            new Color(111/255f, 114/255f, 127/255f),  // 23: Gray
            new Color(252/255f,  56/255f, 165/255f),  // 24: Magenta
            new Color(253/255f, 180/255f,  88/255f),  // 25: Amber
            new Color(137/255f,  10/255f,   8/255f),  // 26: Crimson
            new Color(111/255f, 175/255f, 184/255f),  // 27: Sage — #6FAFB8 (.py 레퍼런스 정합, 2026-06-24)
        };

        private static readonly string[] COLOR_LABELS =
            { "HP", "Cy", "Pu", "Yl", "Gr", "Or", "Wh", "DG",
              "SB", "Fo", "Rd", "Bl", "Tl", "Lv", "Pw", "Br",
              "Cr", "Pk", "Wi", "Mt", "In", "Rs", "Si", "Gy",
              "Mg", "Am", "Cm", "Sa" };

        // 전체 기믹 (기존 호환 유지)
        private static readonly string[] GIMMICK_NAMES =
            { "(none)", "Hidden", "Chain", "Pinata", "Spawner_T", "Pin", "Lock_Key",
              "Surprise", "Wall", "Spawner_O", "Pinata_Box", "Ice", "Frozen_Dart", "Color_Curtain", "Barricade" };

        // [ROLLBACK_LOCKKEY_DEPRECATE]
        // Lock_Key 기믹 dropdown 제거 — 새 레벨에서 사용 안 함. 기존 LevelData 호환은 BalloonController/HolderManager 에서 처리.
        // 풍선(필드) 기믹만
        // [PIN_DEPRECATE] Pin 은 Barricade 로 통합(런타임 BalloonController:1171 에서 normalize) → 드롭다운에서 제거.
        // 레거시 Pin 레벨은 로드 시 Barricade 로 매핑(아래 LoadLevel). 인덱스는 문자열로 해석되므로 시프트 안전.
        private static readonly string[] FIELD_GIMMICK_NAMES =
            { "(none)", "Pinata", "Surprise", "Wall", "Pinata_Box", "Ice", "Color_Curtain", "Barricade", "FlexTube" };

        // 보관함(큐) 기믹만
        private static readonly string[] HOLDER_GIMMICK_NAMES =
            { "(none)", "Hidden", "Chain", "Spawner_T", "Spawner_O", "Frozen_Dart" };

        // ROLLBACK_FIELD_GIMMICK_MARK_ORDER:
        // Field cells store FIELD_GIMMICK_NAMES indices, not legacy all-gimmick indices.
        // Keep preview labels in the exact same order as FIELD_GIMMICK_NAMES.
        private static readonly string[] FIELD_GIMMICK_MARKS =
            { "", "Pi", "?!", "W", "PB", "Ic", "CC", "Ba", "FT" };

        private static readonly Color GIMMICK_WALL_COLOR  = new Color(0.35f, 0.35f, 0.38f);
        private static readonly Color GIMMICK_PIN_COLOR   = new Color(0.70f, 0.50f, 0.20f);
        private static readonly Color GIMMICK_ICE_COLOR   = new Color(0.65f, 0.85f, 0.95f);
        private static readonly Color GIMMICK_HIDDEN_COLOR = new Color(0.45f, 0.45f, 0.50f);
        private static readonly Color GIMMICK_PINATA_COLOR = new Color(0.95f, 0.70f, 0.20f);

        private static readonly string[] TILE_NAMES =
            { "bl", "br", "h", "tl", "tr", "v" };

        // ROLLBACK_BARRICADE_SIZED_FIELD_GIMMICK:
        // Barricade is authored as a single multi-cell field object, like Pinata/Pinata_Box.
        // Wall도 multi-cell footprint(정사각 1/2/3) 지원 — 런타임은 이미 multi-cell wall 처리(BalloonController.IsSizedFieldGimmick에 Wall 포함).
        private static bool IsSizedFieldGimmick(string gimmickName)
        {
            // ROLLBACK_ICE_OVERLAY_UNDER_BALLOONS_20260626:
            // Ice is not a single saved sized gimmick. 2x2/3x3 keeps 4/9 underlying balloon cells
            // and renders one combined Ice overlay at runtime via iceBlockSize/region grouping.
            return gimmickName == "Pinata" || gimmickName == "Pinata_Box" || gimmickName == "Barricade" || gimmickName == "Wall";
        }

        // HP/Size(자유 W×H 1~6) 옵션이 의미 있는 sized 기믹 — Wall은 정사각 1/2/3 전용 row가 따로 있음.
        private static bool NeedsPinataHpAndSize(string gimmickName)
        {
            // ROLLBACK_BARRICADE_NO_PINATA_SIZE_20260608: Barricade 는 sizeW×sizeH(Pinata Size) 미사용 — dir/length 로만 저작.
            //   (옛 sized 방식 잔재. Pinata Size 로 5×1 넣으면 평면 1줄 sized rect 가 돼 2-thick dir/length 와 충돌)
            //   HP row 는 UpdateFieldGimmickUI 의 `|| isBarricade` 로 유지, 길이는 Barricade 전용 row(dir+length)로.
            return gimmickName == "Pinata" || gimmickName == "Pinata_Box";
        }

        private int GetCurrentPinataSizeMax()
        {
            string gimmickName = (_paintGimmick >= 0 && _paintGimmick < FIELD_GIMMICK_NAMES.Length)
                ? FIELD_GIMMICK_NAMES[_paintGimmick]
                : string.Empty;
            return gimmickName == "Pinata" ? WOODEN_BOARD_SIZE_MAX : TARGET_BOX_SIZE_MAX;
        }

        private int GetCurrentFieldGimmickHpMax()
        {
            string gimmickName = (_paintGimmick >= 0 && _paintGimmick < FIELD_GIMMICK_NAMES.Length)
                ? FIELD_GIMMICK_NAMES[_paintGimmick]
                : string.Empty;
            return (gimmickName == "FlexTube" || gimmickName == "Barricade")
                ? FIELD_GIMMICK_HP_MAX_EXTENDED
                : FIELD_GIMMICK_HP_MAX_DEFAULT;
        }

        // 배경색 밝기 기반 가독 텍스트 색. White(6) 등 밝은 셀 위 흰 글자가 안 보이는 문제 해결.
        // Rec.601 luma > 0.6 이면 검정, 아니면 흰색.
        private static Color ContrastTextColor(Color bg)
        {
            float luma = 0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b;
            return luma > 0.6f ? Color.black : Color.white;
        }

        private const float LEFT_PANEL_WIDTH = 240f;
        private const float RIGHT_PANEL_WIDTH = 400f;
        // ROLLBACK_EXTENDED_GIMMICK_LIFE_MAX_20260629:
        // FlexTube/Barricade life authoring max is raised from 50 to 400.
        // Other field gimmicks keep the previous 50 cap to avoid accidental balance changes.
        private const int FIELD_GIMMICK_HP_MAX_DEFAULT = 50;
        private const int FIELD_GIMMICK_HP_MAX_EXTENDED = 400;

        #endregion

        #region Test Play

        public static bool IsTestMode { get; private set; }

        #endregion

        #region State

        private int _levelId = 1;
        private int _numColors = 4;
        private HashSet<int> _selectedColors = new HashSet<int> { 0, 1, 2, 3 };
        private DifficultyPurpose _difficulty = DifficultyPurpose.Normal;
        private int _paintColor = 0;
        private int _activeTab = 0;
        private int _paintGimmick = 0;       // FIELD_GIMMICK_NAMES 인덱스
        private int _paintHolderGimmick = 0; // HOLDER_GIMMICK_NAMES 인덱스

        // 큐/필드 기믹 paint 모드 — 색상·개수는 유지하고 기믹만 조작.
        // Normal: 색상+기믹 동시 / GimmickOnly: 기존 셀에 기믹만 덮어쓰기 / GimmickErase: 기믹만 제거.
        private enum GimmickPaintMode { Normal, GimmickOnly, GimmickErase }
        private GimmickPaintMode _holderPaintMode = GimmickPaintMode.Normal;
        private bool _holderGridNeedsRebuildAfterPaint;
        private bool _fieldGimmickOnlyMode; // 필드: 색상 유지하고 기믹만 추가 (Surprise/Ice 등)
        private bool _fieldGimmickEraseMode; // 필드: 색상 유지하고 기믹만 제거

        private int _gridCols = 5;
        private int _gridRows = 5;
        private float _boardWorldSize = 8f;
        private Vector2 _boardCenter = new Vector2(0f, 2f);

        private int[,] _balloonColors;
        private int[,] _balloonGimmicks;

        // [Target Box Egg 패널] footprint(박스 영역)과 분리된 명시적 알 리스트.
        // 브러시용 현재 알 목록(색/HP) + 칠한 박스 anchor 별 저장.
        private readonly System.Collections.Generic.List<int> _boxEggColors = new System.Collections.Generic.List<int>();
        private readonly System.Collections.Generic.List<int> _boxEggHps = new System.Collections.Generic.List<int>();
        private readonly System.Collections.Generic.Dictionary<Vector2Int, int[]> _boxEggConfigColors = new System.Collections.Generic.Dictionary<Vector2Int, int[]>();
        private readonly System.Collections.Generic.Dictionary<Vector2Int, int[]> _boxEggConfigHps = new System.Collections.Generic.Dictionary<Vector2Int, int[]>();
        private Text _boxEggStatusLabel;          // UI 상태 라벨
        private RectTransform _fieldGimmickEggRow; // Egg 패널 row (Target Box 일 때만 표시)

        // ROLLBACK_MAPMAKER_HOLDER_ROWS_MAX_20260629: Holder authoring now allows up to 30 queue rows.
        private const int HOLDER_ROWS_MAX = 30;
        // ROLLBACK_WOODENBOARD_SIZE_CAP_20260629: Wooden Board(Pinata) can be authored up to 8x8.
        private const int WOODEN_BOARD_SIZE_MAX = 8;
        private const int TARGET_BOX_SIZE_MAX = 6;

        private int _holderCols = 5;
        private int _holderRows = 1;
        private int _defaultMag = 3;
        private InputField _holderColsInput;   // UI 갱신용 참조
        private InputField _holderRowsInput;   // UI 갱신용 참조
        private int[,] _holderColors;
        private int[,] _holderMags;
        private int[,] _holderGimmicks;  // 보관함 기믹 인덱스 (HOLDER_GIMMICK_NAMES 기준)
        private int[,] _holderChainGroups; // Chain 그룹 ID (-1 = 없음)
        private int[,] _holderFrozenHP;    // Frozen Dart 해동 체력 (기본 3)
        private int[,] _holderSpawnerHP;    // Spawner 소환 횟수
        private int[,] _holderSpawnerMag;   // Spawner 소환 보관함 탄창
        private int[,] _balloonGimmickHP;  // Piñata HP (기본 2)
        private int[,] _balloonPinataW;   // Piñata 가로 크기 (앵커 셀에만 저장)
        private int[,] _balloonPinataH;   // Piñata 세로 크기
        private int[,] _balloonIceBlockSize; // [Ice §11] 얼음 블록 변 길이(셀). 기본 1. Ice 셀에만 의미.
        // ROLLBACK_ICE_MANUAL_GROUP_20260608:
        // Optional explicit Ice grouping. groupId 0 keeps old adjacency grouping.
        private int[,] _balloonIceGroupId;
        private int[,] _balloonIceGroupHp;
        private int[,] _balloonIceGroupHpMode; // 1=sum member HP, 2=override
        // ROLLBACK_BARRICADE_MAPMAKER_20260608: 바리케이드 방향(0=N/1=E/2=S/3=W)+길이(셀) 셀별 저작. Barricade 셀에만 의미.
        private int[,] _balloonBarricadeDir;    // 바리케이드 방향 (기본 1=E)
        private int[,] _balloonBarricadeLength; // 바리케이드 길이(body 셀 수, 기본 1)
        private int _paintPinataHP = 2;    // 브러시용 Piñata HP
        private int _paintPinataW = 1;     // 브러시용 Piñata 가로
        private int _paintPinataH = 1;     // 브러시용 Piñata 세로
        private int _paintWallSize = 1;    // 브러시용 Wall 정사각 사이즈 (1/2/3) — Pinata 사이즈와 carryover 차단
        private int _paintIceGroupId = 0;
        private int _paintIceGroupHp = 0;
        private int _paintIceGroupHpMode = 1;
        private int _paintChainGroup = 1;  // 브러시용 Chain 그룹 ID
        private int _nextChainGroupId = 1; // 자동 증가 Chain 그룹 ID
        private int _paintFrozenHP = 3;    // 브러시용 Frozen Dart 해동 체력
        private int _paintSpawnerHP = 3;   // 브러시용 Spawner 소환 횟수
        private int _paintSpawnerMag = 20; // 브러시용 Spawner 소환 탄창
        private int _paintLockPairId = 0;  // 브러시용 Lock_Key pair ID
        private int _nextLockPairId = 1;   // 자동 증가 Lock pair ID
        // ROLLBACK_BARRICADE_MAPMAKER_20260608: 바리케이드 브러시 파라미터.
        private int _paintBarricadeDir = 1;    // 브러시용 바리케이드 방향 (0=N/1=E/2=S/3=W)
        private int _paintBarricadeLength = 3; // 브러시용 바리케이드 길이(진행축 전체 칸, 최소 3=head2+edge1)
        private int[,] _balloonLockPairIds; // 풍선별 Lock pair ID (-1 = 없음)
        private int[,] _holderLockPairIds;  // 보관함별 Lock pair ID (-1 = 없음)

        // FlexTube — 같은 groupId 셀들이 StartCap → Segments → EndCap 한 줄로 연결.
        // partType / rotation 은 BuildLevelConfig 시점에 sequenceIndex + 인접 셀 위치로 자동 계산.
        private int[,] _balloonFlexTubeGroupId;       // -1 = FlexTube 셀 아님
        private int[,] _balloonFlexTubeSequenceIndex; // -1 = 미설정, 0..N-1 = paint 순서
        private int _paintFlexTubeGroupId = 1;        // 현재 paint 중인 그룹 ID (수정 가능)
        private int _nextFlexTubeGroupId = 1;         // 자동 증가 다음 ID
        private readonly List<Vector2Int> _flexTubePaintOrder = new List<Vector2Int>(); // 현재 그룹의 paint 순서 (sequenceIndex 추적)
        private RectTransform _fieldGimmickFlexTubeRow;
        private Text _flexTubeStatusText;

        private int _railDir;
        private float _railPadding = 1.5f;
        private float _railHeight = 0.5f;
        private int _railSlotCount = 200;
        private bool _smoothCorners = true;
        private float _cornerRadius = 2.5f; // 벨트 코너 타일 곡선에 맞춤

        // Grid-based conveyor path (extended grid: +1 padding on each side)
        private bool[,] _pathGrid; // [gridCols+2, gridRows+2]
        private const int PATH_PAD = 1;
        private bool _conveyorPaintMode;

        // Edit Tools state
        private int _cropWidth = 5;
        private int _cropHeight = 5;
        private int _shiftAmount = 1;
        private int _insertRowAt;
        private int _insertColAt;
        private int _deleteRowAt;
        private int _deleteColAt;
        private int _swapFromColor;
        private int _swapToColor = 1;
        private Dropdown _swapFromDropdown;
        private Dropdown _swapToDropdown;
        private bool _floodFillMode;
        private Text _txtFillMode;

        // Gimmick-specific UI rows (Balloon/Field brush)
        private RectTransform _fieldGimmickHPRow;
        private InputField _fieldGimmickHPInput;
        private RectTransform _fieldGimmickSizeRow;
        private RectTransform _fieldGimmickWallSizeRow;
        private RectTransform _fieldGimmickIceGroupRow;
        private RectTransform _fieldGimmickBarricadeRow; // [Barricade] 방향+길이 row (Barricade 일 때만 표시)
        private RectTransform _fieldGimmickLockRow;
        private RectTransform _fieldGimmickChainRow;
        private RectTransform _fieldGimmickFrozenRow;

        // Gimmick-specific UI rows (Holder brush)
        private RectTransform _holderGimmickChainRow;
        private RectTransform _holderGimmickFrozenRow;
        private RectTransform _holderGimmickSpawnerRow;
        private RectTransform _holderSpawnerMagLabel;
        private RectTransform _holderSpawnerMagField;
        private RectTransform _holderGimmickLockRow;

        // Auto-generated waypoints from path grid
        private List<Vector3> _customWaypoints = new List<Vector3>();

        private float CellSpacing
        {
            get
            {
                float innerW = BoardTileManager.CONVEYOR_WIDTH - BoardTileManager.RAIL_THICKNESS - BoardTileManager.RAIL_GAP * 2f;
                float innerH = BoardTileManager.CONVEYOR_HEIGHT - BoardTileManager.RAIL_THICKNESS - BoardTileManager.RAIL_GAP * 2f;
                int maxDim = Mathf.Max(_gridCols, _gridRows);
                float bwsFromW = innerW / Mathf.Max(_gridCols, 1) * maxDim;
                float bwsFromH = innerH / Mathf.Max(_gridRows, 1) * maxDim;
                float boardWorldSize = Mathf.Min(bwsFromW, bwsFromH);
                return boardWorldSize / Mathf.Max(maxDim, 1);
            }
        }
        private float BalloonScale => CellSpacing * 0.9f;

        /// <summary>
        /// Conveyor tile render size based on fixed rail width proportion.
        /// InGame BoardTileManager.RAIL_THICKNESS와 동일한 절대값.
        /// </summary>
        private float ConveyorTileSize => 2.0f;

        private const float CAVE_OVERLAY_Y = 0.5f;
        private const float CAVE_BOTTOM_Z = -5.52f;
        private const float CAVE_TOP_Z_2_SIDES = 9.3f;
        private const float CAVE_TOP_Z_3_SIDES = 9.86f;

        #endregion

        #region Runtime Refs

        private Camera _cam;
        private Font _font;
        private Material[] _colorMats;
        private Material _gridLineMat;
        private Material _waypointMat;
        private Material _waypointLineMat;
        private Transform _previewRoot;
        private GameObject[,] _previewObjs;
        private TextMesh[,] _previewLabels;  // Gimmick marks on each balloon
        private DefaultControls.Resources _uiRes;

        private Text _txtStatus, _txtSpacing, _txtScale, _railCapacityLabel;
        private Text _queueGenScoreLabel;
        // 명세 §9 — 큐 생성기 UI v2 (게이지바 + warn + 추천 cols + Confirm)
        private Text _queueGenWarnLabel;          // Soft warn (red), Hard fail (red bold)
        private Text _queueGenRecommendLabel;     // "추천 cols: N | sample ammo/holder: M"
        private Image _queueGenGaugeFill;          // 게이지 바 fillAmount = score/100
        private Text _queueGenGaugeText;           // 게이지 위 점수 + 등급
        private Button _queueGenConfirmBtn;        // Generate 성공 후 활성 — SaveToDatabase 호출
        private bool _queueGenConfirmReady;        // 마지막 Generate 가 성공 (Hard rule 통과) 했는지
        // Holder Grid 요약 라벨 — §9 mock "보관함 N개 | 다트 X/Y" 실시간 표시
        private Text _holderSummaryLabel;
        // queue_columns 자동 추천 vs 수동 입력 — §2-3 디자이너 오버라이드
        private bool _queueColsAuto = true;

        // Level Info UI 참조 (로드 시 갱신용)
        private InputField _levelIdInput;
        // _numColorsInput removed — replaced by color toggle grid
        private Dropdown _difficultyDropdown;
        private Text[] _palTexts;
        private Transform _holderGridContainer;
        // [2026-06-12] AI/Transform 탭 폐기 — Episode JSON 단일 스토어 (SO 미경유).
        //   _targetDB 는 episode JSON 합본의 in-memory 캐시 (에셋 아님).
        // [2026-06-12 v2] "Old" 탭 추가 — 옛날 데이터(레거시 SO LevelDatabase.asset) '조회 전용' 소스.
        //   Old 탭에서 로드한 레벨도 Save 는 항상 Episode JSON 으로 감 → 레거시 → 현행 스토어
        //   마이그레이션 경로로 사용. (레거시 SO 에 쓰는 코드는 없음)
        private LevelDatabase _targetDB;           // Episode JSON 합본 캐시
        private LevelDatabase _targetDBLegacy;     // 레거시 SO 캐시 (읽기 전용)
        private const string LEGACY_SO_PATH = "Assets/EditorData/LevelDatabase.asset";
        private static readonly string[] DB_TAB_NAMES = { "Epi", "Old" };
        private int _activeDBTab; // 0=Episode(기본), 1=Legacy SO(조회 전용)
        private Text[] _dbTabLabels;

        private const int LEVELS_PER_EXPORT_EPISODE = 20;
        private const int LEVEL_EPISODE_VERSION = 1;
        // ROLLBACK_MAPMAKER_EPISODE_STORE_20260609: LevelDatabase SO 폐기 → Episode JSON 직접 로드/저장.
        //   importer(LevelJsonImporterWindow) 와 같은 저장소를 보게 되어, import 한 레벨이 MapMaker 에 즉시 보임.
        private const string MM_EPISODES_DIR = "Assets/EditorData/Episodes";
        private const string MM_STREAMING_EP1 = "Assets/StreamingAssets/episode_01.json";
        private const string EDITOR_PREF_LAST_LEVEL = "BalloonFlow_LastEditedLevel";
        // [2026-06-12] 다량 episode 일괄 export 입력 ("1-15" / "1,5,6,7" / 혼합 "1-3,7").
        private string _bulkExportEpisodesInput = "1-15";

        // Grid lines
        private Transform _gridLineRoot;

        // Conveyor path preview
        private Transform _conveyorPreviewRoot;
        private Material _conveyorMat;
        private Text _txtConveyorMode;
        private RailTileSet _railTileSet;

        // Waypoint line preview (auto-generated from path grid)
        private Transform _waypointPreviewRoot;

        // Left panel — level list
        private Transform _levelListContent;
        private int _selectedListIndex = -1;
        private List<Button> _levelListButtons = new List<Button>();

        // Center panel — info overlay
        private Text _txtCenterInfo;
        private Text _txtBalanceInfo;

        // Perf: deferred refresh
        private bool _infoDirty;
        private List<int> _sortedSelectedColors = new List<int>();
        private bool _sortedColorsDirty = true;

        // Perf: shared primitive meshes (avoid CreatePrimitive per call)
        private Mesh _sharedSphereMesh;
        private Mesh _sharedCubeMesh;

        // Perf: holder button pool
        private GameObject[,] _holderButtonPool;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            IsTestMode = false;
            GameManager.IsTestPlayMode = false;

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 14);

            _uiRes = new DefaultControls.Resources();
            _railTileSet = Resources.Load<RailTileSet>("RailTileSet");
            CreateMaterials();
            InitGrid();
            InitDefaultWaypoints();

            _cam = Camera.main;
            if (_cam == null) { Debug.LogError("[MapMaker] Camera.main not found!"); return; }
            SetupCamera();
            BuildUI();

            // 테스트 플레이 복귀 시 마지막 편집 레벨, 처음이면 레벨 1 로드.
            // 로드가 성공하면 LoadLevelFromDB 경로가 프리뷰/그리드/컨베이어/웨이포인트/레벨리스트를
            // 전부 다시 그리므로(OnBalloonGridChanged 는 _previewObjs==null 이라 풀 리빌드 진입),
            // 시작 시 초기 빌드는 로드할 레벨이 없을 때만 수행해 중복 작업을 제거한다.
            int lastEditedLevel = EditorPrefs.GetInt(EDITOR_PREF_LAST_LEVEL, 1);
            bool loaded = lastEditedLevel > 0 && LoadLevelById(lastEditedLevel);

            if (!loaded)
            {
                RebuildPreview();
                RebuildGridLines();
                RebuildConveyorPreview();
                RebuildWaypointPreview();
                RefreshInfo();
                RefreshLevelList();
            }
        }

        private void Update()
        {
            HandlePaintInput();
            HandleKeyboard();
            HandleCameraControl();
            if (_infoDirty) { _infoDirty = false; RefreshInfo(); RefreshFlexTubeOverlay(); }
        }

        private void OnDestroy()
        {
            // 프리뷰 오브젝트 일괄 파괴 (개별 파괴보다 빠름)
            if (_previewRoot) DestroyImmediate(_previewRoot.gameObject);
            if (_gridLineRoot) DestroyImmediate(_gridLineRoot.gameObject);
            if (_conveyorPreviewRoot) DestroyImmediate(_conveyorPreviewRoot.gameObject);
            if (_waypointPreviewRoot) DestroyImmediate(_waypointPreviewRoot.gameObject);

            // 머티리얼 정리
            if (_colorMats != null)
                foreach (var m in _colorMats)
                    if (m) Destroy(m);
            foreach (var kvp in _gimmickMatCache)
                if (kvp.Value) Destroy(kvp.Value);
            _gimmickMatCache.Clear();
            if (_conveyorMat) Destroy(_conveyorMat);
            if (_gridLineMat) Destroy(_gridLineMat);
            if (_waypointMat) Destroy(_waypointMat);
            if (_waypointLineMat) Destroy(_waypointLineMat);
        }

        #endregion

        #region Camera

        private void SetupCamera()
        {
            if (_cam == null) return;
            float leftRatio = LEFT_PANEL_WIDTH / 1920f;
            float rightRatio = RIGHT_PANEL_WIDTH / 1920f;
            _cam.rect = new Rect(leftRatio, 0, 1f - leftRatio - rightRatio, 1f);
            _cam.orthographic = true;
            _cam.orthographicSize = BoardTileManager.CONVEYOR_HEIGHT * 0.55f;
            _cam.transform.position = new Vector3(_boardCenter.x, 15f, _boardCenter.y);
            _cam.transform.eulerAngles = new Vector3(90f, 0f, 0f);
        }

        /// <summary>Check if mouse is over the center 3D viewport (not over left/right UI panels).</summary>
        private bool IsMouseOverViewport()
        {
            if (_cam == null) return false;
            var mouse = Mouse.current;
            if (mouse == null) return false;
            Vector2 pos = mouse.position.ReadValue();
            float screenW = Screen.width;
            float leftEdge = LEFT_PANEL_WIDTH * screenW / 1920f;
            float rightEdge = screenW - RIGHT_PANEL_WIDTH * screenW / 1920f;
            return pos.x >= leftEdge && pos.x <= rightEdge;
        }

        private void HandleCameraControl()
        {
            if (_cam == null) return;
            if (!IsMouseOverViewport()) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            // Scroll zoom (works even over 3D viewport regardless of UI raycast)
            float scroll = mouse.scroll.ReadValue().y / 120f;
            if (Mathf.Abs(scroll) > 0.01f)
                _cam.orthographicSize = Mathf.Clamp(_cam.orthographicSize - scroll * 0.8f, 0.5f, 40f);

            // Middle mouse OR right mouse pan
            if (mouse.middleButton.isPressed || (mouse.rightButton.isPressed && !_blockRightMousePanUntilRelease))
            {
                Vector2 delta = mouse.delta.ReadValue();
                float s = _cam.orthographicSize * 0.003f;
                _cam.transform.position += new Vector3(-delta.x * s, 0, -delta.y * s);
            }

            var kb = Keyboard.current;
            if (kb != null)
            {
                // Keyboard shortcuts for zoom
                if (kb[Key.NumpadPlus].isPressed || kb[Key.Equals].isPressed)
                    _cam.orthographicSize = Mathf.Max(0.5f, _cam.orthographicSize - Time.deltaTime * 3f);
                if (kb[Key.NumpadMinus].isPressed || kb[Key.Minus].isPressed)
                    _cam.orthographicSize = Mathf.Min(40f, _cam.orthographicSize + Time.deltaTime * 3f);
                // Reset view
                if (kb[Key.Home].wasPressedThisFrame)
                {
                    _cam.orthographicSize = BoardTileManager.CONVEYOR_HEIGHT * 0.55f;
                    _cam.transform.position = new Vector3(_boardCenter.x, 15f, _boardCenter.y);
                }
            }
        }

        #endregion

        #region Materials & Grid

        /// <summary>Find the best available lit shader (URP first, then Standard fallback).</summary>
        /// <remarks>Shader.Find 는 비용이 있어 1회 조회 후 캐시. 프리뷰/컨베이어/웨이포인트에서 매 리빌드마다 호출되던 것을 제거.</remarks>
        private static Shader _cachedLitShader;
        private static Shader FindLitShader()
        {
            if (_cachedLitShader != null) return _cachedLitShader;

            // URP Lit (most BalloonFlow setups)
            var s = Shader.Find("Universal Render Pipeline/Lit");
            // URP Simple Lit
            if (s == null) s = Shader.Find("Universal Render Pipeline/Simple Lit");
            // Built-in Standard
            if (s == null) s = Shader.Find("Standard");
            // Last resort
            if (s == null) s = Shader.Find("Sprites/Default");

            _cachedLitShader = s;
            return s;
        }

        /// <summary>Create a material with the correct color property for any pipeline.</summary>
        private static Material MakeLitMaterial(Shader shader, Color color)
        {
            var mat = new Material(shader);
            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            mat.enableInstancing = true; // GPU Instancing
            return mat;
        }

        private void CreateMaterials()
        {
            var shader = FindLitShader();
            _colorMats = new Material[PALETTE.Length];
            for (int i = 0; i < PALETTE.Length; i++)
                _colorMats[i] = MakeLitMaterial(shader, PALETTE[i]);
            _conveyorMat = MakeLitMaterial(shader, new Color(0.35f, 0.35f, 0.45f, 0.8f));

            // Grid line material (semi-transparent)
            _gridLineMat = new Material(Shader.Find("Sprites/Default"));
            _gridLineMat.color = new Color(0.4f, 0.4f, 0.5f, 0.4f);

            // Waypoint materials
            _waypointMat = MakeLitMaterial(shader, new Color(0.2f, 0.8f, 0.3f));
            _waypointLineMat = new Material(Shader.Find("Sprites/Default"));
            _waypointLineMat.color = new Color(0.2f, 0.9f, 0.4f, 0.7f);
        }

        private void InitGrid()
        {
            _balloonColors = ResizeGrid(_balloonColors, _gridCols, _gridRows, -1);
            _balloonGimmicks = ResizeGrid(_balloonGimmicks, _gridCols, _gridRows, 0);
            _holderColors = ResizeGrid(_holderColors, _holderCols, _holderRows, -1);
            _holderMags = ResizeGrid(_holderMags, _holderCols, _holderRows, _defaultMag);
            _holderGimmicks = ResizeGrid(_holderGimmicks, _holderCols, _holderRows, 0);
            _holderChainGroups = ResizeGrid(_holderChainGroups, _holderCols, _holderRows, -1);
            _holderFrozenHP = ResizeGrid(_holderFrozenHP, _holderCols, _holderRows, 3);
            _holderSpawnerHP = ResizeGrid(_holderSpawnerHP, _holderCols, _holderRows, 0);
            _holderSpawnerMag = ResizeGrid(_holderSpawnerMag, _holderCols, _holderRows, 20);
            _balloonGimmickHP = ResizeGrid(_balloonGimmickHP, _gridCols, _gridRows, 2);
            _balloonPinataW = ResizeGrid(_balloonPinataW, _gridCols, _gridRows, 1);
            _balloonPinataH = ResizeGrid(_balloonPinataH, _gridCols, _gridRows, 1);
            _balloonIceBlockSize = ResizeGrid(_balloonIceBlockSize, _gridCols, _gridRows, 1);
            _balloonIceGroupId = ResizeGrid(_balloonIceGroupId, _gridCols, _gridRows, 0);
            _balloonIceGroupHp = ResizeGrid(_balloonIceGroupHp, _gridCols, _gridRows, 0);
            _balloonIceGroupHpMode = ResizeGrid(_balloonIceGroupHpMode, _gridCols, _gridRows, 1);
            _balloonBarricadeDir = ResizeGrid(_balloonBarricadeDir, _gridCols, _gridRows, 1);
            _balloonBarricadeLength = ResizeGrid(_balloonBarricadeLength, _gridCols, _gridRows, 1);
            _balloonLockPairIds = ResizeGrid(_balloonLockPairIds, _gridCols, _gridRows, -1);
            _balloonFlexTubeGroupId = ResizeGrid(_balloonFlexTubeGroupId, _gridCols, _gridRows, -1);
            _balloonFlexTubeSequenceIndex = ResizeGrid(_balloonFlexTubeSequenceIndex, _gridCols, _gridRows, -1);
            _holderLockPairIds = ResizeGrid(_holderLockPairIds, _holderCols, _holderRows, -1);
            _pathGrid = ResizeBoolGrid(_pathGrid, _gridCols + PATH_PAD * 2, _gridRows + PATH_PAD * 2);
        }

        private void InitDefaultWaypoints()
        {
            if (_customWaypoints.Count > 0) return;
            // Default: auto-ring the outer border of the extended path grid
            AutoConveyorRing();
        }

        private List<Vector3> BuildRectangularWaypoints()
        {
            // 벨트 타일과 동일한 기준 — 코너 중심을 지나도록 halfCorner 안쪽
            float halfW = BoardTileManager.CONVEYOR_WIDTH * 0.5f;
            float halfH = BoardTileManager.CONVEYOR_HEIGHT * 0.5f;
            float halfCorner = BoardTileManager.RAIL_THICKNESS * 0.5f;
            float l = _boardCenter.x - halfW + halfCorner;
            float r = _boardCenter.x + halfW - halfCorner;
            float b = _boardCenter.y - halfH + halfCorner;
            float t = _boardCenter.y + halfH - halfCorner;
            float h = _railHeight;

            var wp = new List<Vector3>();
            if (_railDir == 0)
            {
                wp.Add(new Vector3(l, h, b));
                wp.Add(new Vector3(Mathf.Lerp(l, r, .33f), h, b));
                wp.Add(new Vector3(Mathf.Lerp(l, r, .67f), h, b));
                wp.Add(new Vector3(r, h, b));
                wp.Add(new Vector3(r, h, Mathf.Lerp(b, t, .33f)));
                wp.Add(new Vector3(r, h, Mathf.Lerp(b, t, .67f)));
                wp.Add(new Vector3(r, h, t));
                wp.Add(new Vector3(Mathf.Lerp(r, l, .33f), h, t));
                wp.Add(new Vector3(Mathf.Lerp(r, l, .67f), h, t));
                wp.Add(new Vector3(l, h, t));
                wp.Add(new Vector3(l, h, Mathf.Lerp(t, b, .33f)));
                wp.Add(new Vector3(l, h, Mathf.Lerp(t, b, .67f)));
            }
            else
            {
                wp.Add(new Vector3(r, h, b));
                wp.Add(new Vector3(Mathf.Lerp(r, l, .33f), h, b));
                wp.Add(new Vector3(Mathf.Lerp(r, l, .67f), h, b));
                wp.Add(new Vector3(l, h, b));
                wp.Add(new Vector3(l, h, Mathf.Lerp(b, t, .33f)));
                wp.Add(new Vector3(l, h, Mathf.Lerp(b, t, .67f)));
                wp.Add(new Vector3(l, h, t));
                wp.Add(new Vector3(Mathf.Lerp(l, r, .33f), h, t));
                wp.Add(new Vector3(Mathf.Lerp(l, r, .67f), h, t));
                wp.Add(new Vector3(r, h, t));
                wp.Add(new Vector3(r, h, Mathf.Lerp(t, b, .33f)));
                wp.Add(new Vector3(r, h, Mathf.Lerp(t, b, .67f)));
            }
            return wp;
        }

        private bool[,] ResizeBoolGrid(bool[,] old, int cols, int rows)
        {
            var g = new bool[cols, rows];
            for (int c = 0; c < cols; c++)
                for (int r = 0; r < rows; r++)
                    g[c, r] = (old != null && c < old.GetLength(0) && r < old.GetLength(1))
                        ? old[c, r] : false;
            return g;
        }

        private int[,] ResizeGrid(int[,] old, int cols, int rows, int def)
        {
            var g = new int[cols, rows];
            for (int c = 0; c < cols; c++)
                for (int r = 0; r < rows; r++)
                    g[c, r] = (old != null && c < old.GetLength(0) && r < old.GetLength(1))
                        ? old[c, r] : def;
            return g;
        }

        private int[,] InsertRowGrid(int[,] old, int cols, int rows, int insertAt, int def)
        {
            var g = new int[cols, rows + 1];
            for (int c = 0; c < cols; c++)
                for (int r = 0; r < rows + 1; r++)
                    g[c, r] = def;

            for (int c = 0; c < cols; c++)
                for (int r = 0; r < rows; r++)
                {
                    int dr = r < insertAt ? r : r + 1;
                    g[c, dr] = (old != null && c < old.GetLength(0) && r < old.GetLength(1)) ? old[c, r] : def;
                }
            return g;
        }

        private int[,] InsertColGrid(int[,] old, int cols, int rows, int insertAt, int def)
        {
            var g = new int[cols + 1, rows];
            for (int c = 0; c < cols + 1; c++)
                for (int r = 0; r < rows; r++)
                    g[c, r] = def;

            for (int c = 0; c < cols; c++)
                for (int r = 0; r < rows; r++)
                {
                    int dc = c < insertAt ? c : c + 1;
                    g[dc, r] = (old != null && c < old.GetLength(0) && r < old.GetLength(1)) ? old[c, r] : def;
                }
            return g;
        }

        private int[,] DeleteRowGrid(int[,] old, int cols, int rows, int deleteAt, int def)
        {
            var g = new int[cols, rows - 1];
            for (int c = 0; c < cols; c++)
            {
                int dr = 0;
                for (int r = 0; r < rows; r++)
                {
                    if (r == deleteAt) continue;
                    g[c, dr] = (old != null && c < old.GetLength(0) && r < old.GetLength(1)) ? old[c, r] : def;
                    dr++;
                }
            }
            return g;
        }

        private int[,] DeleteColGrid(int[,] old, int cols, int rows, int deleteAt, int def)
        {
            var g = new int[cols - 1, rows];
            for (int r = 0; r < rows; r++)
            {
                int dc = 0;
                for (int c = 0; c < cols; c++)
                {
                    if (c == deleteAt) continue;
                    g[dc, r] = (old != null && c < old.GetLength(0) && r < old.GetLength(1)) ? old[c, r] : def;
                    dc++;
                }
            }
            return g;
        }

        private void ShiftFlexTubePaintOrderAfterRowInsert(int insertAt)
        {
            for (int i = 0; i < _flexTubePaintOrder.Count; i++)
            {
                Vector2Int p = _flexTubePaintOrder[i];
                if (p.y >= insertAt) _flexTubePaintOrder[i] = new Vector2Int(p.x, p.y + 1);
            }
        }

        private void ShiftFlexTubePaintOrderAfterColInsert(int insertAt)
        {
            for (int i = 0; i < _flexTubePaintOrder.Count; i++)
            {
                Vector2Int p = _flexTubePaintOrder[i];
                if (p.x >= insertAt) _flexTubePaintOrder[i] = new Vector2Int(p.x + 1, p.y);
            }
        }

        private void ShiftFlexTubePaintOrderAfterRowDelete(int deleteAt)
        {
            for (int i = _flexTubePaintOrder.Count - 1; i >= 0; i--)
            {
                Vector2Int p = _flexTubePaintOrder[i];
                if (p.y == deleteAt) _flexTubePaintOrder.RemoveAt(i);
                else if (p.y > deleteAt) _flexTubePaintOrder[i] = new Vector2Int(p.x, p.y - 1);
            }
        }

        private void ShiftFlexTubePaintOrderAfterColDelete(int deleteAt)
        {
            for (int i = _flexTubePaintOrder.Count - 1; i >= 0; i--)
            {
                Vector2Int p = _flexTubePaintOrder[i];
                if (p.x == deleteAt) _flexTubePaintOrder.RemoveAt(i);
                else if (p.x > deleteAt) _flexTubePaintOrder[i] = new Vector2Int(p.x - 1, p.y);
            }
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        //  UI BUILDING — 3-PANEL LAYOUT
        // ═══════════════════════════════════════════════════════════════

        #region UI Building — Main

        private void BuildUI()
        {
            var canvasGO = new GameObject("MapMakerUI");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            BuildLeftPanel(canvasGO.transform);
            BuildCenterOverlay(canvasGO.transform);
            BuildRightPanel(canvasGO.transform);
        }

        private void BuildLeftPanel(Transform canvasRoot)
        {
            var panel = MakeRT("LeftPanel", canvasRoot);
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = new Vector2(0, 1);
            panel.pivot = new Vector2(0, 0.5f);
            panel.sizeDelta = new Vector2(LEFT_PANEL_WIDTH, 0);
            panel.anchoredPosition = Vector2.zero;
            panel.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.12f, 0.97f);

            // Header
            var header = MakeRT("Header", panel);
            header.anchorMin = new Vector2(0, 1);
            header.anchorMax = Vector2.one;
            header.pivot = new Vector2(0.5f, 1);
            header.sizeDelta = new Vector2(0, 36);
            header.anchoredPosition = Vector2.zero;
            header.gameObject.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f);
            var headerTxt = MakeText(header, "LEVELS", 15, FontStyle.Bold, TextAnchor.MiddleCenter);
            SetFillRect(headerTxt.GetComponent<RectTransform>());

            // [2026-06-12 v2] DB 탭바 — Epi(Episode JSON, 기본) / Old(레거시 SO 조회 전용).
            var dbTabBar = MakeRT("DBTabBar", panel);
            dbTabBar.anchorMin = new Vector2(0, 1);
            dbTabBar.anchorMax = Vector2.one;
            dbTabBar.pivot = new Vector2(0.5f, 1);
            dbTabBar.sizeDelta = new Vector2(0, 28);
            dbTabBar.anchoredPosition = new Vector2(0, -36);
            var dbTabHlg = dbTabBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            dbTabHlg.spacing = 2;
            dbTabHlg.childForceExpandWidth = true;
            dbTabHlg.childForceExpandHeight = true;

            _dbTabLabels = new Text[DB_TAB_NAMES.Length];
            for (int t = 0; t < DB_TAB_NAMES.Length; t++)
            {
                int tabIdx = t;
                var tabBtn = Btn(dbTabBar, DB_TAB_NAMES[t], () => SetActiveDBTab(tabIdx));
                _dbTabLabels[t] = tabBtn.GetComponentInChildren<Text>();
                if (_dbTabLabels[t] != null) _dbTabLabels[t].fontSize = 10;
            }
            UpdateDBTabColors();

            // Scroll area (헤더 + DB탭 아래)
            float topOffset = 36 + 28; // header + dbTab
            var scrollArea = MakeRT("ScrollArea", panel);
            scrollArea.anchorMin = Vector2.zero;
            scrollArea.anchorMax = Vector2.one;
            scrollArea.sizeDelta = new Vector2(0, -topOffset);
            scrollArea.anchoredPosition = new Vector2(0, -topOffset * 0.5f);

            var svGO = DefaultControls.CreateScrollView(_uiRes);
            svGO.transform.SetParent(scrollArea, false);
            SetFillRect(svGO.GetComponent<RectTransform>());
            svGO.GetComponent<Image>().color = Color.clear;
            var sr = svGO.GetComponent<ScrollRect>();
            sr.horizontal = false; sr.scrollSensitivity = 30;
            var hBar = svGO.transform.Find("Scrollbar Horizontal");
            if (hBar) hBar.gameObject.SetActive(false);

            var content = sr.content;
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.spacing = 2;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _levelListContent = content;
        }

        /// <summary>[2026-06-12] 활성 탭 캐시 리프레시 — Importer 적용 등 외부 변경 후 목록 갱신용.</summary>
        private void ReloadEpisodeStore()
        {
            _targetDB = null;       // episode JSON 합본 재빌드
            _targetDBLegacy = null; // 레거시 SO 재로드
            RefreshLevelList();
            SetStatus(_activeDBTab == 1 ? "Legacy store reloaded" : "Episode store reloaded");
        }

        /// <summary>[2026-06-12 v2] Epi(0) / Old(1, 레거시 SO 조회 전용) 탭 전환.</summary>
        private void SetActiveDBTab(int tabIdx)
        {
            _activeDBTab = Mathf.Clamp(tabIdx, 0, DB_TAB_NAMES.Length - 1);
            UpdateDBTabColors();
            RefreshLevelList();
            SetStatus(_activeDBTab == 1
                ? "DB: Old (레거시 SO 조회 전용 — Save 는 Episode 로 저장됨)"
                : "DB: Episode JSON");
        }

        private void UpdateDBTabColors()
        {
            if (_dbTabLabels == null) return;
            for (int i = 0; i < _dbTabLabels.Length; i++)
            {
                if (_dbTabLabels[i] == null) continue;
                var img = _dbTabLabels[i].transform.parent.GetComponent<Image>();
                if (img != null)
                    img.color = i == _activeDBTab ? new Color(0.2f, 0.45f, 0.7f) : new Color(0.15f, 0.15f, 0.2f);
            }
        }

        private void BuildCenterOverlay(Transform canvasRoot)
        {
            float leftRatio = LEFT_PANEL_WIDTH / 1920f;
            float rightRatio = RIGHT_PANEL_WIDTH / 1920f;

            // Top info bar
            var topBar = MakeRT("TopBar", canvasRoot);
            topBar.anchorMin = new Vector2(leftRatio, 0.88f);
            topBar.anchorMax = new Vector2(1f - rightRatio, 1f);
            topBar.sizeDelta = Vector2.zero;
            topBar.anchoredPosition = Vector2.zero;
            topBar.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            _txtCenterInfo = MakeText(topBar, "", 12, FontStyle.Normal, TextAnchor.UpperLeft);
            var infoRT = _txtCenterInfo.GetComponent<RectTransform>();
            SetFillRect(infoRT);
            infoRT.offsetMin = new Vector2(8, 4);
            infoRT.offsetMax = new Vector2(-8, -4);
            _txtCenterInfo.color = new Color(0.85f, 0.9f, 1f);

            // Bottom balance bar
            var botBar = MakeRT("BalanceBar", canvasRoot);
            botBar.anchorMin = new Vector2(leftRatio, 0f);
            botBar.anchorMax = new Vector2(1f - rightRatio, 0.12f);
            botBar.sizeDelta = Vector2.zero;
            botBar.anchoredPosition = Vector2.zero;
            botBar.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            _txtBalanceInfo = MakeText(botBar, "", 11, FontStyle.Normal, TextAnchor.UpperLeft);
            var balRT = _txtBalanceInfo.GetComponent<RectTransform>();
            SetFillRect(balRT);
            balRT.offsetMin = new Vector2(8, 4);
            balRT.offsetMax = new Vector2(-8, -4);
            _txtBalanceInfo.color = new Color(0.8f, 0.9f, 0.7f);

            // Status bar (very bottom)
            var statusBar = MakeRT("StatusBar", canvasRoot);
            statusBar.anchorMin = new Vector2(0, 0);
            statusBar.anchorMax = new Vector2(1, 0);
            statusBar.pivot = new Vector2(0.5f, 0);
            statusBar.sizeDelta = new Vector2(0, 24);
            statusBar.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.95f);

            _txtStatus = MakeText(statusBar, "Ready", 11, FontStyle.Normal, TextAnchor.MiddleLeft);
            var sRT = _txtStatus.GetComponent<RectTransform>();
            SetFillRect(sRT);
            sRT.offsetMin = new Vector2(LEFT_PANEL_WIDTH + 8, 0);
            _txtStatus.color = new Color(0.6f, 0.8f, 1f);
        }

        private Transform[] _tabContents;
        private Button[] _tabButtons;
        private static readonly string[] TAB_NAMES = { "Balloon", "Holder", "Image", "Export" };

        private void BuildRightPanel(Transform canvasRoot)
        {
            var panel = MakeRT("RightPanel", canvasRoot);
            panel.anchorMin = new Vector2(1, 0);
            panel.anchorMax = Vector2.one;
            panel.pivot = new Vector2(1, 0.5f);
            panel.sizeDelta = new Vector2(RIGHT_PANEL_WIDTH, 0);
            panel.anchoredPosition = Vector2.zero;
            panel.gameObject.AddComponent<Image>().color = new Color(0.10f, 0.10f, 0.14f, 0.97f);

            var content = BuildScrollView(panel);

            Lbl(content, "MAP MAKER", 18, FontStyle.Bold);
            Sep(content);
            BuildLevelSection(content);

            // Tab bar
            var tabRow = Row(content, 30);
            _tabButtons = new Button[TAB_NAMES.Length];
            for (int i = 0; i < TAB_NAMES.Length; i++)
            {
                int tabIdx = i;
                var b = Btn(tabRow, TAB_NAMES[i], () => SetActiveTab(tabIdx));
                _tabButtons[i] = b;
            }
            Sep(content);

            // Tab contents
            _tabContents = new Transform[TAB_NAMES.Length];

            // Tab 0: Balloon
            var tab0 = MakeRT("Tab_Balloon", content);
            tab0.gameObject.AddComponent<VerticalLayoutGroup>().spacing = 2;
            tab0.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _tabContents[0] = tab0;
            BuildPaletteSection(tab0);
            BuildGridSection(tab0);
            BuildActionSection(tab0);
            BuildEditToolsSection(tab0);

            // Tab 1: Holder
            var tab1 = MakeRT("Tab_Holder", content);
            tab1.gameObject.AddComponent<VerticalLayoutGroup>().spacing = 2;
            tab1.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _tabContents[1] = tab1;
            BuildHolderSection(tab1);

            // Tab 2: Image
            var tab2 = MakeRT("Tab_Image", content);
            tab2.gameObject.AddComponent<VerticalLayoutGroup>().spacing = 2;
            tab2.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _tabContents[2] = tab2;
            BuildImageImportSection(tab2);

            // Tab 3: Export
            var tab3 = MakeRT("Tab_Export", content);
            tab3.gameObject.AddComponent<VerticalLayoutGroup>().spacing = 2;
            tab3.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _tabContents[3] = tab3;
            BuildRailSection(tab3);
            BuildExportSection(tab3);

            SetActiveTab(0);
        }

        private void SetActiveTab(int tabIndex)
        {
            _activeTab = tabIndex;
            for (int i = 0; i < _tabContents.Length; i++)
            {
                if (_tabContents[i] != null)
                    _tabContents[i].gameObject.SetActive(i == _activeTab);
            }
            // Highlight active tab button
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                if (_tabButtons[i] != null)
                {
                    var img = _tabButtons[i].gameObject.GetComponent<Image>();
                    if (img != null)
                        img.color = (i == _activeTab)
                            ? new Color(0.25f, 0.45f, 0.70f)
                            : new Color(0.18f, 0.18f, 0.22f);
                }
            }
        }

        #endregion

        #region UI Building — Sections

        private Transform _colorToggleContainer;

        private void BuildLevelSection(Transform p)
        {
            Lbl(p, "Level Info", 14, FontStyle.Bold);
            var r1 = Row(p); Lbl(r1, "Level ID", w: 90);
            _levelIdInput = MakeInputField(r1, _levelId.ToString(), s => { if (int.TryParse(s, out int v)) _levelId = v; });
            var r3 = Row(p); Lbl(r3, "Difficulty", w: 90);
            MakeDifficultyDropdown(r3);

            // Color Palette Toggle Grid — replaces numColors IntField
            Lbl(p, "Color Palette (click to toggle)", 11);
            _colorToggleContainer = MakeRT("ColorToggleGrid", p);
            var glg = _colorToggleContainer.gameObject.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(34, 34);
            glg.spacing = new Vector2(3, 3);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 10;
            var le = _colorToggleContainer.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 120;
            RebuildColorToggleGrid();

            var deselectRow = Row(p);
            Btn(deselectRow, "Deselect Unused", () => { DeselectUnusedColors(); });

            Sep(p);
        }

        private void DeselectUnusedColors()
        {
            var usedColors = new HashSet<int>();
            // 풍선 색상
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                    if (_balloonColors[c, r] >= 0)
                        usedColors.Add(_balloonColors[c, r]);
            // 보관함 색상
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                    if (_holderColors[c, r] >= 0)
                        usedColors.Add(_holderColors[c, r]);

            _selectedColors.Clear(); _sortedColorsDirty = true;
            foreach (int ci in usedColors)
                _selectedColors.Add(ci);
            if (_selectedColors.Count < 2)
            {
                _selectedColors.Add(0);
                _selectedColors.Add(1);
            }
            _numColors = _selectedColors.Count;
            RebuildColorToggleGrid();
            RebuildPalette();
            RebuildHolderUI();
            _infoDirty = true;
            SetStatus($"Deselected unused colors — {_numColors} remain");
        }

        private void RebuildColorToggleGrid()
        {
            if (_colorToggleContainer == null) return;
            foreach (Transform c in _colorToggleContainer) Destroy(c.gameObject);

            for (int i = 0; i < PALETTE.Length; i++)
            {
                int idx = i;
                bool selected = _selectedColors.Contains(idx);

                // Outer container (acts as border frame)
                var outerGO = new GameObject($"ColorSlot_{idx}", typeof(RectTransform), typeof(Image));
                outerGO.transform.SetParent(_colorToggleContainer, false);
                var outerImg = outerGO.GetComponent<Image>();
                outerImg.color = selected ? Color.white : new Color(0.12f, 0.12f, 0.15f);

                // Inner color button
                var btn = DefaultControls.CreateButton(_uiRes);
                btn.transform.SetParent(outerGO.transform, false);
                var btnRT = btn.GetComponent<RectTransform>();
                btnRT.anchorMin = Vector2.zero; btnRT.anchorMax = Vector2.one;
                float border = selected ? 3f : 0f;
                btnRT.offsetMin = new Vector2(border, border);
                btnRT.offsetMax = new Vector2(-border, -border);

                Color btnColor = PALETTE[idx];
                btnColor.a = 1f;
                btn.GetComponent<Image>().color = btnColor;
                var t = btn.GetComponentInChildren<Text>();
                t.text = $"{idx}";
                t.font = _font; t.fontSize = 9; t.color = Color.white;
                t.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
                btn.GetComponent<Button>().onClick.AddListener(() => ToggleColorSelection(idx));
            }
        }

        private void ToggleColorSelection(int colorIndex)
        {
            if (_selectedColors.Contains(colorIndex))
            {
                if (_selectedColors.Count <= 2) { SetStatus("Minimum 2 colors required"); return; }
                _selectedColors.Remove(colorIndex);
            }
            else
            {
                _selectedColors.Add(colorIndex);
            }
            _sortedColorsDirty = true;
            _numColors = _selectedColors.Count;

            // Remove balloons only of the just-toggled color (not all non-selected colors)
            if (!_selectedColors.Contains(colorIndex))
            {
                for (int c = 0; c < _gridCols; c++)
                    for (int r = 0; r < _gridRows; r++)
                        if (_balloonColors[c, r] == colorIndex)
                        {
                            _balloonColors[c, r] = -1;
                            _balloonGimmicks[c, r] = 0;
                        }

                // Remove holders only of the just-toggled color
                for (int c = 0; c < _holderCols; c++)
                    for (int r = 0; r < _holderRows; r++)
                        if (_holderColors[c, r] == colorIndex)
                        {
                            _holderColors[c, r] = -1;
                            _holderMags[c, r] = 0;
                            _holderGimmicks[c, r] = 0;
                        }
            }

            RebuildColorToggleGrid();
            RebuildPalette();
            OnBalloonGridChanged();
            RebuildHolderUI();
            _infoDirty = true;
            SetStatus($"Colors: {_numColors} selected");
        }

        private Transform _paletteContainer;

        private void BuildPaletteSection(Transform p)
        {
            Lbl(p, "Brush", 14, FontStyle.Bold);
            _paletteContainer = MakeRT("PaletteRow", p);
            _paletteContainer.gameObject.AddComponent<HorizontalLayoutGroup>().spacing = 2;
            _paletteContainer.gameObject.AddComponent<LayoutElement>().preferredHeight = 36;
            RebuildPalette();

            // 풍선(필드) 기믹 드롭다운
            var gr = Row(p); Lbl(gr, "Field Gimmick", w: 110);
            var fieldGimmickDD = DefaultControls.CreateDropdown(_uiRes);
            fieldGimmickDD.transform.SetParent(gr, false);
            var fgLE = fieldGimmickDD.AddComponent<LayoutElement>(); fgLE.flexibleWidth = 1; fgLE.preferredHeight = 24;
            fieldGimmickDD.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.20f);
            var fgdd = fieldGimmickDD.GetComponent<Dropdown>();
            fgdd.ClearOptions();
            // [ROLLBACK_GIMMICK_DISPLAY_NAME] FIELD_GIMMICK_NAMES 의 코드 식별자 → display name 매핑.
            var fieldDisplayNames = new List<string>(FIELD_GIMMICK_NAMES.Length);
            for (int i = 0; i < FIELD_GIMMICK_NAMES.Length; i++) fieldDisplayNames.Add(GimmickDisplayName.Get(FIELD_GIMMICK_NAMES[i]));
            fgdd.AddOptions(fieldDisplayNames);
            fgdd.value = 0;
            fgdd.captionText.font = _font; fgdd.captionText.fontSize = 12; fgdd.captionText.color = Color.white;
            fgdd.onValueChanged.AddListener(v => {
                _paintGimmick = v; // FIELD_GIMMICK_NAMES 인덱스 그대로 사용
                string name = FIELD_GIMMICK_NAMES[v];
                UpdateFieldGimmickUI(name);
                SetStatus($"Field Gimmick: {name}");
            });

            // 필드 기믹만 모드 토글 — ON 이면 풍선 색상 유지하고 기믹만 덮어쓰기 (Surprise/Ice 등).
            var fieldModeRow = Row(p); Lbl(fieldModeRow, "Paint Mode", w: 110);
            Button fieldModeBtn = null;
            Button fieldEraseModeBtn = null;
            fieldModeBtn = Btn(fieldModeRow, _fieldGimmickOnlyMode ? "기믹만 추가" : "색상+기믹", () => {
                _fieldGimmickOnlyMode = !_fieldGimmickOnlyMode;
                if (_fieldGimmickOnlyMode) _fieldGimmickEraseMode = false;
                var tt = fieldModeBtn.GetComponentInChildren<Text>();
                if (tt != null) tt.text = _fieldGimmickOnlyMode ? "기믹만 추가" : "색상+기믹";
                var et = fieldEraseModeBtn != null ? fieldEraseModeBtn.GetComponentInChildren<Text>() : null;
                if (et != null) et.text = _fieldGimmickEraseMode ? "제거 ON" : "기믹만 제거";
                SetStatus(_fieldGimmickOnlyMode ? "필드: 기믹만 추가 (색상 유지)" : "필드: 색상+기믹");
            });
            fieldEraseModeBtn = Btn(fieldModeRow, _fieldGimmickEraseMode ? "제거 ON" : "기믹만 제거", () => {
                _fieldGimmickEraseMode = !_fieldGimmickEraseMode;
                if (_fieldGimmickEraseMode) _fieldGimmickOnlyMode = false;
                var tt = fieldModeBtn != null ? fieldModeBtn.GetComponentInChildren<Text>() : null;
                if (tt != null) tt.text = _fieldGimmickOnlyMode ? "기믹만 추가" : "색상+기믹";
                var et = fieldEraseModeBtn.GetComponentInChildren<Text>();
                if (et != null) et.text = _fieldGimmickEraseMode ? "제거 ON" : "기믹만 제거";
                SetStatus(_fieldGimmickEraseMode ? "필드: 기믹만 제거 (색상 유지)" : "필드: 색상+기믹");
            });

            // Piñata HP (shown for Pinata/Pinata_Box)
            var hpRow = Row(p); Lbl(hpRow, "Piñata HP", w: 110);
            _fieldGimmickHPInput = MakeIntField(hpRow, _paintPinataHP, 1, FIELD_GIMMICK_HP_MAX_EXTENDED, v => {
                _paintPinataHP = Mathf.Clamp(v, 1, GetCurrentFieldGimmickHpMax());
                if (_fieldGimmickHPInput != null) _fieldGimmickHPInput.text = _paintPinataHP.ToString();
                SetStatus($"Piñata HP: {v}");
            });
            _fieldGimmickHPRow = hpRow.GetComponent<RectTransform>();

            // Piñata Size (shown for Pinata/Pinata_Box)
            var sizeRow = Row(p); Lbl(sizeRow, "Piñata Size", w: 110);
            MakeIntField(sizeRow, _paintPinataW, 1, WOODEN_BOARD_SIZE_MAX, v => {
                _paintPinataW = Mathf.Clamp(v, 1, GetCurrentPinataSizeMax());
                SetStatus($"Piñata Size: {_paintPinataW}x{_paintPinataH}");
                UpdateBoxEggLabel(); // footprint 변경 → Egg 라벨의 W×H 기대치 갱신
            });
            Lbl(sizeRow, "x", w: 15);
            MakeIntField(sizeRow, _paintPinataH, 1, WOODEN_BOARD_SIZE_MAX, v => {
                _paintPinataH = Mathf.Clamp(v, 1, GetCurrentPinataSizeMax());
                SetStatus($"Piñata Size: {_paintPinataW}x{_paintPinataH}");
                UpdateBoxEggLabel();
            });
            _fieldGimmickSizeRow = sizeRow.GetComponent<RectTransform>();

            // Wall Size (Wall 전용 — 정사각 1×1 / 2×2 / 3×3). Pinata와 별도 row로 분리해 자유 W×H 옵션과 혼동을 피함.
            // 선택 시 _paintPinataW = _paintPinataH = N 으로 기록 → 기존 sized 페인트 경로(앵커 + footprint) 재사용.
            var wallSizeRow = Row(p); Lbl(wallSizeRow, "Wall Size", w: 110);
            var wallDD = DefaultControls.CreateDropdown(_uiRes);
            wallDD.transform.SetParent(wallSizeRow, false);
            var wsLE = wallDD.AddComponent<LayoutElement>(); wsLE.flexibleWidth = 1; wsLE.preferredHeight = 24;
            wallDD.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.20f);
            var wsdd = wallDD.GetComponent<Dropdown>();
            wsdd.ClearOptions();
            wsdd.AddOptions(new List<string> { "1×1", "2×2", "3×3" });
            int initWallIdx = (_paintWallSize >= 1 && _paintWallSize <= 3) ? _paintWallSize - 1 : 0;
            wsdd.value = initWallIdx;
            wsdd.captionText.font = _font; wsdd.captionText.fontSize = 12; wsdd.captionText.color = Color.white;
            wsdd.onValueChanged.AddListener(v => {
                _paintWallSize = v + 1; // 0→1, 1→2, 2→3
                SetStatus($"Wall Size: {_paintWallSize}×{_paintWallSize}");
            });
            _fieldGimmickWallSizeRow = wallSizeRow.GetComponent<RectTransform>();

            // ROLLBACK_ICE_MANUAL_GROUP_20260608:
            // Ice-only explicit grouping. Group 0 uses legacy adjacency grouping.
            var iceGroupRow = Row(p); Lbl(iceGroupRow, "Ice Group", w: 110);
            MakeIntField(iceGroupRow, _paintIceGroupId, 0, 999, v => {
                _paintIceGroupId = Mathf.Max(0, v);
                SetStatus($"Ice Group: {_paintIceGroupId} (0=Auto)");
            });
            Lbl(iceGroupRow, "HP", w: 24);
            MakeIntField(iceGroupRow, _paintIceGroupHp, 0, 999, v => {
                _paintIceGroupHp = Mathf.Max(0, v);
                SetStatus($"Ice Group HP Override: {_paintIceGroupHp}");
            });
            var iceModeDD = DefaultControls.CreateDropdown(_uiRes);
            iceModeDD.transform.SetParent(iceGroupRow, false);
            var iceModeLE = iceModeDD.AddComponent<LayoutElement>(); iceModeLE.preferredWidth = 95; iceModeLE.preferredHeight = 24;
            iceModeDD.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.20f);
            var imdd = iceModeDD.GetComponent<Dropdown>();
            imdd.ClearOptions();
            imdd.AddOptions(new List<string> { "Sum", "Override" });
            imdd.value = _paintIceGroupHpMode == 2 ? 1 : 0;
            imdd.captionText.font = _font; imdd.captionText.fontSize = 12; imdd.captionText.color = Color.white;
            imdd.onValueChanged.AddListener(v => {
                _paintIceGroupHpMode = v == 1 ? 2 : 1;
                SetStatus(_paintIceGroupHpMode == 2 ? "Ice HP Mode: Override" : "Ice HP Mode: Sum");
            });
            _fieldGimmickIceGroupRow = iceGroupRow.GetComponent<RectTransform>();

            // ROLLBACK_BARRICADE_MAPMAKER_20260608: Barricade 전용 — 방향(N/E/S/W) + 길이(body 셀). HP 는 위 Piñata HP row 재사용.
            var barRow = Row(p); Lbl(barRow, "Barricade 방향", w: 110);
            var barDD = DefaultControls.CreateDropdown(_uiRes);
            barDD.transform.SetParent(barRow, false);
            var barLE = barDD.AddComponent<LayoutElement>(); barLE.flexibleWidth = 1; barLE.preferredHeight = 24;
            barDD.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.20f);
            var barddd = barDD.GetComponent<Dropdown>();
            barddd.ClearOptions();
            barddd.AddOptions(new List<string> { "N(북/+Z)", "E(동/+X)", "S(남/-Z)", "W(서/-X)" }); // index = dir 0/1/2/3
            barddd.value = ((_paintBarricadeDir % 4) + 4) % 4;
            barddd.captionText.font = _font; barddd.captionText.fontSize = 12; barddd.captionText.color = Color.white;
            barddd.onValueChanged.AddListener(v => {
                _paintBarricadeDir = v;
                SetStatus($"Barricade 방향: {v} (0=N/1=E/2=S/3=W), 길이 {_paintBarricadeLength}");
            });
            Lbl(barRow, "길이", w: 35);
            // ROLLBACK_BARRICADE_MAXLEN50_20260625: 최대 길이 12 → 50(머리+꼬리 포함 진행축 전체 칸). 런타임/검증에 별도 캡 없음.
            MakeIntField(barRow, _paintBarricadeLength, 3, 50, v => {
                _paintBarricadeLength = Mathf.Clamp(v, 3, 50);
                SetStatus($"Barricade 길이: {_paintBarricadeLength} (방향 {_paintBarricadeDir}, footprint {_paintBarricadeLength}×2)");
            });
            _fieldGimmickBarricadeRow = barRow.GetComponent<RectTransform>();

            // Target Box Egg 패널 (Pinata_Box 전용) — footprint(박스 영역)과 분리된 명시적 알 리스트.
            // 현재 팔레트 색 + Piñata HP 로 "+추가" → 알 N개·색·HP 를 직접 구성. 박스를 빈 칸에 그리면 이 리스트가 적용됨.
            var eggRow = Row(p); Lbl(eggRow, "Box Eggs", w: 110);
            Btn(eggRow, "+추가", () => {
                _boxEggColors.Add(_paintColor);
                _boxEggHps.Add(Mathf.Max(1, _paintPinataHP));
                UpdateBoxEggLabel();
                SetStatus($"Egg 추가: 색 {_paintColor} HP {_paintPinataHP} (총 {_boxEggColors.Count})");
            });
            Btn(eggRow, "비우기", () => {
                _boxEggColors.Clear(); _boxEggHps.Clear();
                UpdateBoxEggLabel();
                SetStatus("Egg 리스트 비움");
            });
            _boxEggStatusLabel = Lbl(p, "Eggs: (없음)", 11);
            _boxEggStatusLabel.color = new Color(0.7f, 0.85f, 0.6f);
            _fieldGimmickEggRow = eggRow.GetComponent<RectTransform>();

            // Lock_Key pair ID (Field — Key 풍선용)
            var lockRow = Row(p); Lbl(lockRow, "Lock PairId", w: 110);
            MakeIntField(lockRow, _paintLockPairId, 0, 99, v => {
                _paintLockPairId = v;
                SetStatus($"Lock PairId: {v}");
            });
            Btn(lockRow, "New", () => {
                _paintLockPairId = _nextLockPairId++;
                SetStatus($"New Lock PairId: {_paintLockPairId}");
            });
            _fieldGimmickLockRow = lockRow.GetComponent<RectTransform>();

            // FlexTube — Group ID input + Finish Group 버튼 + 상태 텍스트
            var flexRow = Row(p); Lbl(flexRow, "FlexTube GID", w: 110);
            MakeIntField(flexRow, _paintFlexTubeGroupId, 1, 999, v => {
                _paintFlexTubeGroupId = v;
                _flexTubePaintOrder.Clear();
                UpdateFlexTubeStatusText();
                SetStatus($"FlexTube Group: {v}");
            });
            Btn(flexRow, "New", () => {
                _paintFlexTubeGroupId = _nextFlexTubeGroupId++;
                _flexTubePaintOrder.Clear();
                UpdateFlexTubeStatusText();
                SetStatus($"New FlexTube Group: {_paintFlexTubeGroupId}");
            });
            Btn(flexRow, "Finish", () => {
                _paintFlexTubeGroupId = _nextFlexTubeGroupId++;
                _flexTubePaintOrder.Clear();
                UpdateFlexTubeStatusText();
                SetStatus($"FlexTube group finished — next: {_paintFlexTubeGroupId}");
            });
            _flexTubeStatusText = Lbl(p, "FlexTube: 0 cells", 11);
            _flexTubeStatusText.color = new Color(0.6f, 0.7f, 0.6f);
            _fieldGimmickFlexTubeRow = flexRow.GetComponent<RectTransform>();

            // Initially hide all gimmick-specific rows (none selected)
            _fieldGimmickHPRow.gameObject.SetActive(false);
            _fieldGimmickSizeRow.gameObject.SetActive(false);
            _fieldGimmickWallSizeRow.gameObject.SetActive(false);
            if (_fieldGimmickIceGroupRow != null) _fieldGimmickIceGroupRow.gameObject.SetActive(false);
            if (_fieldGimmickBarricadeRow != null) _fieldGimmickBarricadeRow.gameObject.SetActive(false); // [Barricade] 초기 숨김 누락 보완
            _fieldGimmickLockRow.gameObject.SetActive(false);
            _fieldGimmickFlexTubeRow.gameObject.SetActive(false);
            if (_fieldGimmickEggRow != null) _fieldGimmickEggRow.gameObject.SetActive(false);
            if (_boxEggStatusLabel != null) _boxEggStatusLabel.gameObject.SetActive(false);
            if (_flexTubeStatusText != null) _flexTubeStatusText.gameObject.SetActive(false);

            Sep(p);
        }

        private void UpdateBoxEggLabel()
        {
            if (_boxEggStatusLabel == null) return;
            int footprint = Mathf.Max(1, _paintPinataW) * Mathf.Max(1, _paintPinataH);
            if (_boxEggColors.Count == 0)
            {
                _boxEggStatusLabel.text = $"Eggs: (없음 → footprint {_paintPinataW}×{_paintPinataH}={footprint}개 현재 색 자동채움)";
                return;
            }
            // 각 알 = 풍선 1칸 모델: 명시 리스트는 footprint(W*H)와 개수가 맞아야 정상.
            string match = _boxEggColors.Count == footprint ? "" : $"  ⚠ footprint {_paintPinataW}×{_paintPinataH}={footprint} 와 불일치";
            var sb = new System.Text.StringBuilder($"Eggs({_boxEggColors.Count}){match}: ");
            for (int i = 0; i < _boxEggColors.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append($"c{_boxEggColors[i]}×{_boxEggHps[i]}");
            }
            _boxEggStatusLabel.text = sb.ToString();
        }

        private void UpdateFieldGimmickUI(string gimmickName)
        {
            // Pinata 계열(자유 W×H + HP)과 Wall(정사각 1/2/3) 은 별도 row 로 분기.
            bool needsPinataHpSize = NeedsPinataHpAndSize(gimmickName);
            bool isWall = gimmickName == "Wall";
            bool isLockKey = gimmickName == "Lock_Key";
            bool isFlexTube = gimmickName == "FlexTube";
            bool isTargetBox = gimmickName == "Pinata_Box";
            // [#13/§11] Ice 는 영역 공유 HP(Health) 를 가지므로 HP row 노출. 단 W×H Size 는 없음(셀 단위, 인접 연결로 영역화).
            // 얼음 블록 변 길이(2×2 등)는 Wall-size row(정사각 1/2/3) 를 재사용해 작성 → _paintWallSize 가 blockSize.
            bool isIce = gimmickName == "Ice";
            // ROLLBACK_BARRICADE_MAPMAKER_20260608: Barricade 는 HP(파괴 히트수) + 방향/길이 row 노출. W×H Size 는 없음.
            bool isBarricade = gimmickName == "Barricade";
            _paintPinataHP = Mathf.Clamp(_paintPinataHP, 1, GetCurrentFieldGimmickHpMax());
            if (_fieldGimmickHPInput != null) _fieldGimmickHPInput.text = _paintPinataHP.ToString();
            if (needsPinataHpSize)
            {
                int sizeMax = GetCurrentPinataSizeMax();
                _paintPinataW = Mathf.Clamp(_paintPinataW, 1, sizeMax);
                _paintPinataH = Mathf.Clamp(_paintPinataH, 1, sizeMax);
            }
            if (_fieldGimmickHPRow != null) _fieldGimmickHPRow.gameObject.SetActive(needsPinataHpSize || isIce || isBarricade || isFlexTube);
            if (_fieldGimmickSizeRow != null) _fieldGimmickSizeRow.gameObject.SetActive(needsPinataHpSize);
            if (_fieldGimmickWallSizeRow != null) _fieldGimmickWallSizeRow.gameObject.SetActive(isWall || isIce);
            if (_fieldGimmickIceGroupRow != null) _fieldGimmickIceGroupRow.gameObject.SetActive(isIce);
            if (_fieldGimmickBarricadeRow != null) _fieldGimmickBarricadeRow.gameObject.SetActive(isBarricade);
            if (_fieldGimmickLockRow != null) _fieldGimmickLockRow.gameObject.SetActive(isLockKey);
            if (_fieldGimmickFlexTubeRow != null) _fieldGimmickFlexTubeRow.gameObject.SetActive(isFlexTube);
            if (_flexTubeStatusText != null) _flexTubeStatusText.gameObject.SetActive(isFlexTube);
            // Target Box Egg 패널 — Pinata_Box 일 때만.
            if (_fieldGimmickEggRow != null) _fieldGimmickEggRow.gameObject.SetActive(isTargetBox);
            if (_boxEggStatusLabel != null) { _boxEggStatusLabel.gameObject.SetActive(isTargetBox); if (isTargetBox) UpdateBoxEggLabel(); }
        }

        private void UpdateFlexTubeStatusText()
        {
            if (_flexTubeStatusText == null) return;
            _flexTubeStatusText.text = $"FlexTube Group {_paintFlexTubeGroupId}: {_flexTubePaintOrder.Count} cells";
        }

        // ROLLBACK_FLEXTUBE_2THICK_20260626: FlexTube 2줄 두께용 'row-B'(수직 짝) 셀 칠하기.
        //   row-A 와 같은 group/seq 공유 — _flexTubePaintOrder 에는 넣지 않음(순서는 row-A 만). 격자 밖이거나
        //   이미 이 튜브 그룹이면 스킵(코너 2×2 겹침 방지). 런타임 BuildFlexTubes 가 같은 seq 쌍을 중점으로 평균.
        private void PaintFlexTubeRowB(int c, int r, int seq)
        {
            if (c < 0 || c >= _gridCols || r < 0 || r >= _gridRows) return;
            if (_balloonFlexTubeGroupId[c, r] == _paintFlexTubeGroupId) return; // 이미 이 튜브 셀 → 스킵(중복 방지)
            _balloonColors[c, r] = _paintColor;
            _balloonGimmicks[c, r] = _paintGimmick;
            _balloonGimmickHP[c, r] = _paintPinataHP;
            _balloonPinataW[c, r] = 1;
            _balloonPinataH[c, r] = 1;
            _balloonLockPairIds[c, r] = -1;
            ApplyIceGroupBrushMeta(c, r, false);
            _balloonFlexTubeGroupId[c, r] = _paintFlexTubeGroupId;
            _balloonFlexTubeSequenceIndex[c, r] = seq;
            UpdatePreviewCell(c, r);
        }

        // ROLLBACK_FLEXTUBE_HEADCORNER2X2_20260626: FlexTube row-A(경로 중심) 셀 칠하기 + (옵션)순서 등록.
        //   row-B 와 달리 경로 셀이라 _flexTubePaintOrder 에 들어가며(addOrder), build 의 centerline/캡 매핑 기준이 된다.
        private void PaintFlexTubeRowA(int c, int r, int seq, bool addOrder)
        {
            if (c < 0 || c >= _gridCols || r < 0 || r >= _gridRows) return;
            _balloonColors[c, r] = _paintColor;
            _balloonGimmicks[c, r] = _paintGimmick;
            _balloonGimmickHP[c, r] = _paintPinataHP;
            _balloonPinataW[c, r] = 1;
            _balloonPinataH[c, r] = 1;
            _balloonLockPairIds[c, r] = -1;
            ApplyIceGroupBrushMeta(c, r, false);
            _balloonFlexTubeGroupId[c, r] = _paintFlexTubeGroupId;
            _balloonFlexTubeSequenceIndex[c, r] = seq;
            if (addOrder) _flexTubePaintOrder.Add(new Vector2Int(c, r));
            UpdatePreviewCell(c, r);
        }

        // ROLLBACK_FLEXTUBE_2X2_PARTS_20260626: FlexTube 파트 1개 = 축정렬 2×2 footprint 스탬프.
        //   클릭 셀을 좌하단 앵커로 +1 col/+1 row 확장(격자 끝이면 안쪽으로 클램프해 항상 2×2 가 격자 안에 들어옴).
        //   4셀 모두 같은 group/seq. 앵커 1셀만 paint 순서에 등록(RowA, addOrder=true), 나머지 3셀은 RowB(순서 X).
        //   정사각이라 진행방향과 무관하게 두께 2 + 길이 2 가 동시에 성립 → Head/Segment/Edge 전부 2×2.
        //   런타임은 같은 seq 4셀을 평균해 2×2 중심 1점으로 쓰므로 centerline/캡/rib 파이프라인 무변경.
        private void PaintFlexTube2x2(int col, int row, int seq)
        {
            // ROLLBACK_FLEXTUBE_CLICK_ANCHOR_20260701: 짝수 그리드 스냅 폐기 → '클릭한 셀'을 2×2 앵커(좌하단)로.
            //   요청: 클릭 위치가 어느 짝수 블록에 드느냐로 위/아래 점프하지 않고, 클릭 셀 기준으로 +col/+row 방향 2×2 를 찍는다.
            //   ⚠ 타일 주의: 파트끼리는 '2칸 간격'으로 클릭해야 겹침 없이 깨끗이 이어짐(1칸 간격이면 RowB 겹침 스킵으로
            //     리브 삐짐/캡 중심 어긋남 — 짝수 스냅이 자동으로 막아주던 부분을 이제 작성자가 지켜야 함).
            //   가드(_balloonFlexTubeGroupId != _paintFlexTubeGroupId)가 이미 칠한 셀 재클릭을 막으므로 앵커 셀 재클릭은 무시됨.
            int baseC = Mathf.Clamp(col, 0, Mathf.Max(0, _gridCols - 2));
            int baseR = Mathf.Clamp(row, 0, Mathf.Max(0, _gridRows - 2));

            PaintFlexTubeRowA(baseC, baseR, seq, true);          // 블록 앵커(좌하단) — paint 순서 등록
            PaintFlexTubeRowB(baseC + 1, baseR, seq);
            PaintFlexTubeRowB(baseC, baseR + 1, seq);
            PaintFlexTubeRowB(baseC + 1, baseR + 1, seq);
        }

        private void UpdateHolderGimmickUI(string gimmickName)
        {
            bool isChain = gimmickName == "Chain";
            bool isFrozen = gimmickName == "Frozen_Dart";
            bool isSpawner = gimmickName == "Spawner_T" || gimmickName == "Spawner_O";
            // GLASSPIPE_PARITY_20260625: Glass Pipe(Spawner_T)도 Pipe와 동일 패널 —
            // 둘 다 authored payload 라 Magazine 필드 불필요(숨김). 설정 창이 완전히 동일해짐.
            bool isPipe = gimmickName == "Spawner_O" || gimmickName == "Spawner_T";
            bool isLockKey = gimmickName == "Lock_Key";
            if (_holderGimmickChainRow != null) _holderGimmickChainRow.gameObject.SetActive(isChain);
            if (_holderGimmickFrozenRow != null) _holderGimmickFrozenRow.gameObject.SetActive(isFrozen);
            if (_holderGimmickSpawnerRow != null) _holderGimmickSpawnerRow.gameObject.SetActive(isSpawner);
            if (_holderSpawnerMagLabel != null) _holderSpawnerMagLabel.gameObject.SetActive(isSpawner && !isPipe);
            if (_holderSpawnerMagField != null) _holderSpawnerMagField.gameObject.SetActive(isSpawner && !isPipe);
            if (_holderGimmickLockRow != null) _holderGimmickLockRow.gameObject.SetActive(isLockKey);
        }

        private void RebuildPalette()
        {
            if (_paletteContainer == null) return;
            foreach (Transform c in _paletteContainer) Destroy(c.gameObject);

            // Build sorted list of selected color indices
            var sortedColors = new List<int>(_selectedColors);
            sortedColors.Sort();

            _palTexts = new Text[sortedColors.Count + 1];
            for (int i = 0; i < sortedColors.Count; i++)
            {
                int idx = sortedColors[i];
                var btn = MakeColorBtn(_paletteContainer, PALETTE[idx], COLOR_LABELS[idx],
                    () => SetPaintColor(idx));
                _palTexts[i] = btn.GetComponentInChildren<Text>();
            }
            var eraseBtn = MakeColorBtn(_paletteContainer, new Color(0.25f, 0.25f, 0.3f), "X",
                () => SetPaintColor(-1));
            _palTexts[sortedColors.Count] = eraseBtn.GetComponentInChildren<Text>();
            UpdatePaletteHighlight();
            RebuildSwapDropdowns();
        }

        private void BuildGridSection(Transform p)
        {
            // Columns/Rows removed from UI — grid size is set by image import or level load
            Lbl(p, "Balloon Grid", 14, FontStyle.Bold);

            _txtSpacing = Lbl(p, $"  Spacing: {CellSpacing:F3}", 11);
            _txtSpacing.color = new Color(0.6f, 0.6f, 0.7f);
            _txtScale = Lbl(p, $"  Scale: {BalloonScale:F3}", 11);
            _txtScale.color = new Color(0.6f, 0.6f, 0.7f);
            Sep(p);
        }

        private void BuildActionSection(Transform p)
        {
            var row = Row(p);
            Btn(row, "Fill All", () => { FillBalloons(_paintColor); OnBalloonGridChanged(); });
            Btn(row, "Clear All", () => { FillBalloons(-1); OnBalloonGridChanged(); });
            Btn(row, "Random", () => { RandomBalloons(); OnBalloonGridChanged(); });
            var row2 = Row(p);
            Btn(row2, "Erase Color", () => { EraseColor(_paintColor); OnBalloonGridChanged(); });
            Btn(row2, "Erase Neighbor", () => { _eraseNeighborMode = true; SetStatus("Click a cell to erase same-color neighbors"); });
            Btn(row2, "Fill Neighbor", () => { _fillNeighborMode = true; SetStatus("Click a cell to fill same-color neighbors"); });
            var row3 = Row(p);
            Btn(row3, "Round x10", RoundCurrentBalloonCountsToTen);
            Sep(p);
        }

        private bool _eraseNeighborMode;
        private bool _fillNeighborMode;

        private void BuildHolderSection(Transform p)
        {
            Lbl(p, "Holder Grid (= Queue)", 14, FontStyle.Bold);

            // §9 mock — Holder Grid 위 요약 라벨 (실시간 보관함 수 + 다트 합 + 색별 분포)
            _holderSummaryLabel = Lbl(p, "보관함 -개 | 다트 -", 11);
            _holderSummaryLabel.color = new Color(0.85f, 0.95f, 0.85f);

            // Cols 행 — Auto/Manual 토글 + (Manual일 때만 입력 활성)
            var r1 = Row(p); Lbl(r1, "Columns", w: 90);
            // ROLLBACK_HOLDER_COLS_PREVIEW_MATCH_20260625: 입력 범위를 런타임 큐 상한과 일치(2~5)시켜
            //   미리보기 열 수가 실제 배치(queueColumns=Clamp(_holderCols,2,5), 런타임 MAX_QUEUE_COLUMNS=5)를
            //   절대 초과하지 않게 한다. (기존 1~20 → 6열+ 저작 시 런타임이 5로 뭉개 미리보기와 달라졌음.)
            _holderColsInput = MakeIntField(r1, _holderCols, 2, 5, v =>
            { _holderCols = v; InitGrid(); RebuildHolderUI(); _infoDirty = true; });
            // §2-3 — Auto 추천 토글
            Button togBtn = null;
            togBtn = Btn(r1, _queueColsAuto ? "Auto" : "Manual", () =>
            {
                _queueColsAuto = !_queueColsAuto;
                var tt = togBtn.GetComponentInChildren<Text>();
                if (tt != null) tt.text = _queueColsAuto ? "Auto" : "Manual";
                if (_holderColsInput != null)
                    _holderColsInput.interactable = !_queueColsAuto;
                SetStatus(_queueColsAuto ? "queue_columns: Auto (§2-3 추천)" : "queue_columns: Manual (수동 입력)");
            });
            if (_holderColsInput != null) _holderColsInput.interactable = !_queueColsAuto;

            var r2 = Row(p); Lbl(r2, "Rows", w: 90);
            _holderRowsInput = MakeIntField(r2, _holderRows, 1, HOLDER_ROWS_MAX, v =>
            { _holderRows = v; InitGrid(); RebuildHolderUI(); _infoDirty = true; });
            var r3 = Row(p); Lbl(r3, "Default Mag", w: 90);
            MakeIntField(r3, _defaultMag, 1, 99, v => _defaultMag = v);

            var gridGO = new GameObject("HolderButtons", typeof(RectTransform),
                typeof(GridLayoutGroup), typeof(LayoutElement));
            gridGO.transform.SetParent(p, false);
            var glg = gridGO.GetComponent<GridLayoutGroup>();
            glg.spacing = new Vector2(2, 2);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _holderGridContainer = gridGO.transform;
            RebuildHolderUI();

            // 보관함(큐) 기믹 드롭다운
            var hgRow = Row(p); Lbl(hgRow, "Holder Gimmick", w: 110);
            var holderGimmickDD = DefaultControls.CreateDropdown(_uiRes);
            holderGimmickDD.transform.SetParent(hgRow, false);
            var hgLE = holderGimmickDD.AddComponent<LayoutElement>(); hgLE.flexibleWidth = 1; hgLE.preferredHeight = 24;
            holderGimmickDD.GetComponent<Image>().color = new Color(0.20f, 0.16f, 0.22f);
            var hgdd = holderGimmickDD.GetComponent<Dropdown>();
            hgdd.ClearOptions();
            // [ROLLBACK_GIMMICK_DISPLAY_NAME] HOLDER_GIMMICK_NAMES 의 코드 식별자 → display name 매핑.
            var holderDisplayNames = new List<string>(HOLDER_GIMMICK_NAMES.Length);
            for (int i = 0; i < HOLDER_GIMMICK_NAMES.Length; i++) holderDisplayNames.Add(GimmickDisplayName.Get(HOLDER_GIMMICK_NAMES[i]));
            hgdd.AddOptions(holderDisplayNames);
            hgdd.value = 0;
            hgdd.captionText.font = _font; hgdd.captionText.fontSize = 12; hgdd.captionText.color = Color.white;
            hgdd.onValueChanged.AddListener(v => {
                _paintHolderGimmick = v;
                UpdateHolderGimmickUI(HOLDER_GIMMICK_NAMES[v]);
                SetStatus($"Holder Gimmick: {HOLDER_GIMMICK_NAMES[v]}");
            });

            // 큐 Paint 모드 토글 — 색상+기믹 / 기믹만 추가 / 기믹 제거 순환.
            // GimmickOnly·GimmickErase 는 기존 다트박스의 색상·개수를 보존한 채 기믹만 조작.
            var holderModeRow = Row(p); Lbl(holderModeRow, "Paint Mode", w: 110);
            Button holderModeBtn = null;
            holderModeBtn = Btn(holderModeRow, HolderPaintModeLabel(), () => {
                _holderPaintMode = (GimmickPaintMode)(((int)_holderPaintMode + 1) % 3);
                var tt = holderModeBtn.GetComponentInChildren<Text>();
                if (tt != null) tt.text = HolderPaintModeLabel();
                SetStatus($"큐 Paint Mode: {HolderPaintModeLabel()}");
            });

            // Chain 그룹 ID 설정
            var chainRow = Row(p); Lbl(chainRow, "Chain Group", w: 110);
            MakeIntField(chainRow, _paintChainGroup, 1, 99, v => {
                _paintChainGroup = v;
                SetStatus($"Chain Group: {v}");
            });
            Btn(chainRow, "New", () => {
                _paintChainGroup = _nextChainGroupId++;
                SetStatus($"New Chain Group: {_paintChainGroup}");
            });
            _holderGimmickChainRow = chainRow.GetComponent<RectTransform>();

            // Frozen Dart 해동 체력 설정
            var frozenRow = Row(p); Lbl(frozenRow, "Frozen HP", w: 110);
            MakeIntField(frozenRow, _paintFrozenHP, 1, 99, v => {
                _paintFrozenHP = v;
                SetStatus($"Frozen Dart HP: {v}");
            });
            _holderGimmickFrozenRow = frozenRow.GetComponent<RectTransform>();

            // Spawner HP + Mag
            var spawnerRow = Row(p); Lbl(spawnerRow, "Spawn Count", w: 100);
            MakeIntField(spawnerRow, _paintSpawnerHP, 1, 50, v => { _paintSpawnerHP = v; });
            _holderSpawnerMagLabel = Lbl(spawnerRow, "Mag", w: 30).GetComponent<RectTransform>();
            _holderSpawnerMagField = MakeIntField(spawnerRow, _paintSpawnerMag, 10, 50, v => { _paintSpawnerMag = v; }).GetComponent<RectTransform>();
            _holderGimmickSpawnerRow = spawnerRow.GetComponent<RectTransform>();

            // Lock_Key pair ID (Holder — Lock 보관함용)
            var holderLockRow = Row(p); Lbl(holderLockRow, "Lock PairId", w: 110);
            MakeIntField(holderLockRow, _paintLockPairId, 0, 99, v => {
                _paintLockPairId = v;
                SetStatus($"Lock PairId: {v}");
            });
            Btn(holderLockRow, "New", () => {
                _paintLockPairId = _nextLockPairId++;
                SetStatus($"New Lock PairId: {_paintLockPairId}");
            });
            _holderGimmickLockRow = holderLockRow.GetComponent<RectTransform>();

            // Initially hide holder gimmick-specific rows
            _holderGimmickChainRow.gameObject.SetActive(false);
            _holderGimmickFrozenRow.gameObject.SetActive(false);
            _holderGimmickSpawnerRow.gameObject.SetActive(false);
            _holderGimmickLockRow.gameObject.SetActive(false);

            var row = Row(p);
            Btn(row, "Fill", () => { FillHolders(_paintColor); RebuildHolderUI(); _infoDirty = true; });
            Btn(row, "Clear", () => { FillHolders(-1); RebuildHolderUI(); _infoDirty = true; });
            Btn(row, "Random", () => { RandomHolders(); RebuildHolderUI(); _infoDirty = true; });
            var row2 = Row(p);
            Btn(row2, "Set Mag", () => { SetAllMags(); RebuildHolderUI(); _infoDirty = true; });
            Sep(p);

            // ── 큐 생성기 섹션 (v2 — 명세 §9 mock 기준) ──
            Lbl(p, "Queue Generator", 14, FontStyle.Bold);

            // 점수 게이지 바 + 등급 텍스트 overlay
            MakeGaugeBar(p, out _queueGenGaugeFill, out _queueGenGaugeText, 22f);

            // 보관함/다트 요약 + cap 종류 등
            _queueGenScoreLabel = Lbl(p, "Score: -", 12);
            // 추천 cols / ammo per holder 안내
            _queueGenRecommendLabel = Lbl(p, "", 11);
            _queueGenRecommendLabel.color = new Color(0.75f, 0.85f, 1f);
            // Soft warn / Hard fail 메시지
            _queueGenWarnLabel = Lbl(p, "", 11, FontStyle.Bold);
            _queueGenWarnLabel.color = new Color(1f, 0.55f, 0.35f);
            _queueGenWarnLabel.gameObject.SetActive(false);

            var rowGen = Row(p);
            Btn(rowGen, "Generate Queue", () => { GenerateQueue(); RebuildHolderUI(); _infoDirty = true; });
            _queueGenConfirmBtn = Btn(rowGen, "Confirm", () =>
            {
                if (!_queueGenConfirmReady) { SetStatus("Confirm: Generate Queue 성공 후 가능합니다."); return; }
                SaveToActiveDB();
                SetStatus("Queue confirmed — saved to database.");
            });
            // Confirm 은 Generate 성공 전까지 비활성
            SetQueueConfirmReady(false);

            var rowGen2 = Row(p);
            Btn(rowGen2, "Auto Balance", () => { AutoBalanceHolders(); RebuildHolderUI(); _infoDirty = true; });
            Sep(p);
        }

        /// <summary>
        /// Confirm 버튼 활성/비활성 + 시각 상태. Generate Queue 성공 시 true, 실패/수동 변경 시 false.
        /// </summary>
        private void SetQueueConfirmReady(bool ready)
        {
            _queueGenConfirmReady = ready;
            if (_queueGenConfirmBtn == null) return;
            var img = _queueGenConfirmBtn.GetComponent<Image>();
            if (img != null)
                img.color = ready ? new Color(0.30f, 0.55f, 0.30f) : new Color(0.22f, 0.22f, 0.28f);
            var txt = _queueGenConfirmBtn.GetComponentInChildren<Text>();
            if (txt != null)
                txt.color = ready ? Color.white : new Color(0.65f, 0.65f, 0.70f);
        }

        private void BuildRailSection(Transform p)
        {
            Lbl(p, "Rail Settings", 14, FontStyle.Bold);
            var r1 = Row(p); Lbl(r1, "Direction", w: 90);
            var dirBtn = Btn(r1, _railDir == 0 ? "CW (clockwise)" : "CCW (counter-CW)", () =>
            {
                _railDir = 1 - _railDir;
                GenerateWaypointsFromPathGrid();
                if (_customWaypoints.Count < 3)
                    _customWaypoints = BuildRectangularWaypoints();
                RebuildPreview(); RebuildConveyorPreview(); RebuildWaypointPreview();
                _infoDirty = true;
            });
            var r2 = Row(p); Lbl(r2, "Padding", w: 90);
            MakeInputField(r2, _railPadding.ToString("F1"), s =>
            { if (float.TryParse(s, out float v)) _railPadding = v; });
            // 허용량은 총 다트 수에서 자동 결정 (읽기 전용)
            var r3 = Row(p); Lbl(r3, "Capacity", w: 90);
            int autoCapacity = RailManager.CalculateCapacity(CalcTotalDarts());
            _railSlotCount = autoCapacity;
            _railCapacityLabel = Lbl(r3, $"{autoCapacity}  ({RailManager.GetRailSideCount(autoCapacity)}면, 제거:{RailManager.GetContinueRemoveCount(autoCapacity)})", w: 200);

            // Smooth corners toggle + radius
            var r4 = Row(p); Lbl(r4, "Smooth Corner", w: 100);
            var smoothLabel = Lbl(r4, _smoothCorners ? "ON" : "OFF", w: 40);
            smoothLabel.color = _smoothCorners ? new Color(0.5f, 0.95f, 0.5f) : new Color(0.7f, 0.7f, 0.7f);
            Btn(r4, "Toggle", () =>
            {
                _smoothCorners = !_smoothCorners;
                smoothLabel.text = _smoothCorners ? "ON" : "OFF";
                smoothLabel.color = _smoothCorners ? new Color(0.5f, 0.95f, 0.5f) : new Color(0.7f, 0.7f, 0.7f);
                RebuildWaypointPreview();
                SetStatus(_smoothCorners ? "Smooth corners ON" : "Smooth corners OFF");
            });

            var r5 = Row(p); Lbl(r5, "Corner Radius", w: 100);
            MakeFloatField(r5, _cornerRadius, 0.1f, 10f, v =>
            {
                _cornerRadius = v;
                if (_smoothCorners) RebuildWaypointPreview();
            });
            Sep(p);
        }

        private void BuildConveyorSection(Transform p)
        {
            Lbl(p, "Conveyor Path", 14, FontStyle.Bold);

            var r1 = Row(p);
            Lbl(r1, "Paint Mode", w: 90);
            _txtConveyorMode = Lbl(r1, "Balloon", w: 80);
            _txtConveyorMode.color = new Color(0.5f, 0.9f, 0.5f);
            Btn(r1, "Toggle (Tab)", () => ToggleConveyorMode());

            var r2 = Row(p);
            Btn(r2, "Auto Ring", () => { AutoConveyorRing(); RebuildConveyorPreview(); _infoDirty = true; });
            Btn(r2, "Clear Path", () => {
                int pw = _pathGrid.GetLength(0), ph = _pathGrid.GetLength(1);
                for (int c = 0; c < pw; c++)
                    for (int r = 0; r < ph; r++)
                        _pathGrid[c, r] = false;
                _customWaypoints.Clear();
                RebuildConveyorPreview(); RebuildWaypointPreview(); _infoDirty = true;
            });

            Lbl(p, "  Tab=Toggle Mode. Click grid cells to draw path.", 10);
            Lbl(p, "  Path inside board removes balloons on that cell.", 10);
            Sep(p);
        }

        // ── Image Import ──
        private Texture2D _importedImage;
        private int _importGridCols = 20;
        private int _importGridRows = 20;
        private InputField _importGridColsInput;
        private InputField _importGridRowsInput;
        private int _importRoundTo = 10;
        // ROLLBACK_MAPMAKER_NUM_COLORS_20260623: 임포트 시 사용할 색 수(0=자동/전부). >0 이면 대표 N색만 선별.
        private int _importNumColors = 0;
        // ROLLBACK_MAPMAKER_DOCSNAP_20260624: 문서(bl_palette_snap_base28) 기준 = alpha 투명만 빈칸.
        //   밝기 컷은 옵션(0=off, 문서 기본). 0보다 크게 주면 해당 밝기 이하를 추가로 배경 처리(어두운 색 보존을 위해 기본 off).
        private float _importBgThreshold = 0f; // 이 밝기 이하는 배경으로 인식 (0~1). 0=off(문서 기본)
        private int[,] _importPreview; // color index grid from image

        private void BuildImageImportSection(Transform p)
        {
            Lbl(p, "Image Import", 14, FontStyle.Bold);

            var r1 = Row(p);
            Btn(r1, "Load Image", () =>
            {
                string path = EditorUtility.OpenFilePanel("Select Image", "", "png,jpg,jpeg,bmp");
                if (string.IsNullOrEmpty(path)) return;
                byte[] data = System.IO.File.ReadAllBytes(path);
                var raw = new Texture2D(2, 2);
                raw.LoadImage(data);

                // ROLLBACK_MAPMAKER_RAWANALYZE_20260624: 색 분석은 원본 픽셀 그대로 사용(업스케일 제거).
                //   [버그] 기존 Graphics.Blit 업스케일은 raw 의 기본 Bilinear 필터로 경계에 "원본에 없는"
                //   보간색을 대량 생성(예: 두더지 40x48 1031색 → 60399색) → median-cut 이 빨강/와인 클러스터를
                //   만들어 어두운 배경이 "붉게" 스냅됨. 로컬 .py 는 원본 픽셀만 분석해 멀쩡(= 로컬↔유니티 차이의 원인).
                //   (+ Linear 컬러스페이스면 RT/ReadPixels 감마 시프트로 가중.) _importedImage 는 GetPixel 분석
                //   전용(표시 안 함)이고 BuildCellSourceColors 가 블록 최빈(MODE)으로 임의 크기를 처리하므로
                //   업스케일은 불필요·유해. 원본을 그대로 분석해 .py(원본 픽셀)와 결과 일치시킴.
                raw.filterMode = FilterMode.Point;
                _importedImage = raw;
                SetStatus($"Image loaded: {raw.width}x{raw.height}");
                // 색상 스냅(문서 파이프라인)은 UpdateImagePreview 에서 일괄 수행 → 팔레트 자동 선택 포함.

                // 파일명에서 그리드 크기 파싱 (예: level_101_31x36.png → 31x36)
                string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                var match = System.Text.RegularExpressions.Regex.Match(fileName, @"(\d+)x(\d+)");
                if (match.Success)
                {
                    _importGridCols = int.Parse(match.Groups[1].Value);
                    _importGridRows = int.Parse(match.Groups[2].Value);
                }
                else
                {
                    // ROLLBACK_MAPMAKER_RAWANALYZE_20260624: 파일명에 NxM 없으면 보드=이미지 크기로 1:1
                    //   (.py 기본 "보드 크기 변경 없음"). 셀=픽셀이라 다운샘플 없이 .py 와 동일 결과.
                    //   필요 시 designer 가 Grid W/H 로 조정 가능. (필드 범위 4~100 클램프.)
                    _importGridCols = Mathf.Clamp(_importedImage.width, 4, 100);
                    _importGridRows = Mathf.Clamp(_importedImage.height, 4, 100);
                }
                if (_importGridColsInput != null) _importGridColsInput.text = _importGridCols.ToString();
                if (_importGridRowsInput != null) _importGridRowsInput.text = _importGridRows.ToString();

                UpdateImagePreview();
            });
            Btn(r1, "Apply", () =>
            {
                if (_importPreview == null) { SetStatus("Load an image first"); return; }
                ApplyImageToGrid();
            });

            var r2 = Row(p); Lbl(r2, "Grid W", w: 50);
            _importGridColsInput = MakeIntField(r2, _importGridCols, 4, 100, v => { _importGridCols = v; UpdateImagePreview(); });
            Lbl(r2, "H", w: 20);
            _importGridRowsInput = MakeIntField(r2, _importGridRows, 4, 100, v => { _importGridRows = v; UpdateImagePreview(); });

            // ROLLBACK_MAPMAKER_NUM_COLORS_20260623: 임포트 전 사용할 색 수 입력(0=자동/전부). Load 전 미리 넣으면 적용.
            var rN = Row(p); Lbl(rN, "Num Colors", w: 70);
            MakeIntField(rN, _importNumColors, 0, 28, v =>
            {
                _importNumColors = v;
                if (_importedImage != null) UpdateImagePreview();
            });

            var r3 = Row(p); Lbl(r3, "Round To", w: 70);
            MakeIntField(r3, _importRoundTo, 0, 50, v => _importRoundTo = v);

            var r4 = Row(p); Lbl(r4, "BG Cut", w: 70);
            MakeInputField(r4, _importBgThreshold.ToString("F2"), s =>
            {
                if (float.TryParse(s, out float v))
                {
                    _importBgThreshold = Mathf.Clamp01(v);
                    UpdateImagePreview();
                }
            });

            Sep(p);
        }

        /// <summary>이미지 픽셀을 분석하여 사용된 팔레트 색상을 자동 감지 → _selectedColors 갱신.</summary>
        // ROLLBACK_MAPMAKER_CIEDE2000_20260623: 지각 거리 매칭 보조 (팔레트 Lab 캐시 / 고유색 키 / 표현불가 임계).
        private const float IMPORT_GAP_THRESHOLD = 20f; // ΔE2000 ≳20 = 눈에 띄게 다른 색 (레퍼런스 gap_threshold 기본값)
        private PerceptualColor.Lab[] _paletteLab;

        /// <summary>PALETTE 28색의 CIELab 1회 캐시.</summary>
        private void EnsurePaletteLab()
        {
            if (_paletteLab != null && _paletteLab.Length == PALETTE.Length) return;
            _paletteLab = new PerceptualColor.Lab[PALETTE.Length];
            for (int i = 0; i < PALETTE.Length; i++)
                _paletteLab[i] = PerceptualColor.RgbToLab(PALETTE[i]);
        }

        /// <summary>Color(0~1) → 8bit RGB 정수 키(고유색 LUT 용).</summary>
        private static int PackRgb(Color c)
        {
            int r = Mathf.Clamp((int)(c.r * 255f + 0.5f), 0, 255);
            int g = Mathf.Clamp((int)(c.g * 255f + 0.5f), 0, 255);
            int b = Mathf.Clamp((int)(c.b * 255f + 0.5f), 0, 255);
            return (r << 16) | (g << 8) | b;
        }

        // ────────────────────────────────────────────────────────────────────────
        // ROLLBACK_MAPMAKER_DOCSNAP_20260624: 색상 스냅을 레퍼런스 문서(bl_palette_snap_base28.py)
        //   파이프라인과 1:1 로 재구현. 핵심 차이(기존 대비):
        //     ① 픽셀을 곧장 팔레트에 argmin 스냅 → 폐기. 먼저 "소스 색 클러스터"를 만든다.
        //     ② 대표색 선별을 소스 클러스터 Lab 거리로 수행(기존: 이미 스냅된 팔레트 거리 → 오류).
        //     ③ 대표색 ↔ 팔레트 배정을 헝가리안 1:1(injective)로 → 서로 다른 색이 같은 팔레트로
        //        뭉개지는 현상 제거(문서의 핵심 목적).
        //     ④ 빈칸은 alpha 투명만(문서 기본). 밝기 컷은 옵션(_importBgThreshold>0).
        //   흐름: 다운샘플(셀별 소스색) → 고유색≤64 직접 / 초과 median-cut → 가중(빈도)
        //         → snap_clusters(select_representatives + hungarian) → 셀별 게임 색 인덱스.
        // ────────────────────────────────────────────────────────────────────────
        private const float IMPORT_DISPERSION = 1.5f; // 레퍼런스 dispersion(power) 기본값
        private const int IMPORT_CLUSTER_CAP = 64;     // 레퍼런스 CAP — 고유색 ≤ 이 값이면 무손실
        private const float IMPORT_EMPTY_ALPHA = 8f / 255f; // 레퍼런스: alpha < 8 → 빈칸

        /// <summary>레퍼런스 hungarian — 정사각 패딩 후 O(N^3) 최소비용 1:1 배정. 행→열(-1=미배정).</summary>
        private static int[] Hungarian(float[,] cost)
        {
            int n = cost.GetLength(0), m = cost.GetLength(1);
            int N = Mathf.Max(n, m);
            var C = new float[N, N];
            for (int i = 0; i < N; i++)
                for (int j = 0; j < N; j++)
                    C[i, j] = (i < n && j < m) ? cost[i, j] : 0f;

            const float INF = 1e18f;
            var u = new float[N + 1];
            var v = new float[N + 1];
            var p = new int[N + 1];
            var way = new int[N + 1];
            for (int i = 1; i <= N; i++)
            {
                p[0] = i;
                int j0 = 0;
                var minv = new float[N + 1];
                var used = new bool[N + 1];
                for (int j = 0; j <= N; j++) minv[j] = INF;
                do
                {
                    used[j0] = true;
                    int i0 = p[j0];
                    float delta = INF;
                    int j1 = -1;
                    for (int j = 1; j <= N; j++)
                    {
                        if (!used[j])
                        {
                            float cur = C[i0 - 1, j - 1] - u[i0] - v[j];
                            if (cur < minv[j]) { minv[j] = cur; way[j] = j0; }
                            if (minv[j] < delta) { delta = minv[j]; j1 = j; }
                        }
                    }
                    for (int j = 0; j <= N; j++)
                    {
                        if (used[j]) { u[p[j]] += delta; v[j] -= delta; }
                        else minv[j] -= delta;
                    }
                    j0 = j1;
                } while (p[j0] != 0);
                while (j0 != 0)
                {
                    int prev = way[j0];
                    p[j0] = p[prev];
                    j0 = prev;
                }
            }
            var res = new int[n];
            for (int i = 0; i < n; i++) res[i] = -1;
            for (int j = 1; j <= N; j++)
                if (p[j] >= 1 && p[j] <= n && j <= m) res[p[j] - 1] = j - 1;
            return res;
        }

        /// <summary>레퍼런스 select_representatives — 빈도×(고른 색들과의 최소 ΔE)^power 그리디.
        /// 거리는 *소스 클러스터끼리* 측정(문서 그대로). 시드=빈도 최고.</summary>
        private static int[] SelectRepresentatives(PerceptualColor.Lab[] labs, int[] weights, int N, float power)
        {
            int K = labs.Length;
            if (N >= K)
            {
                var all = new int[K];
                for (int i = 0; i < K; i++) all[i] = i;
                return all;
            }
            var chosen = new List<int>(N);
            int seed = 0;
            for (int i = 1; i < K; i++) if (weights[i] > weights[seed]) seed = i;
            chosen.Add(seed);
            while (chosen.Count < N)
            {
                int best = -1; float bestScore = -1f;
                for (int i = 0; i < K; i++)
                {
                    if (chosen.Contains(i)) continue;
                    float md = float.MaxValue;
                    for (int c = 0; c < chosen.Count; c++)
                    {
                        float d = PerceptualColor.DeltaE2000(labs[i], labs[chosen[c]]);
                        if (d < md) md = d;
                    }
                    float s = weights[i] * Mathf.Pow(md, power);
                    if (s > bestScore) { bestScore = s; best = i; }
                }
                if (best < 0) break;
                chosen.Add(best);
            }
            return chosen.ToArray();
        }

        /// <summary>공유 median-cut 양자화(고유색>CAP). 레퍼런스 bl_palette_snap_base28.median_cut_shared 와 1:1 동일.
        /// 박스 선택 = (최장축 길이)×(박스 총 빈도), 분할 = 채널 값 중앙(midrange, Heckbert),
        /// 정렬 = (채널 정수값, packed RGB) 전순서, 대표색 = 빈도 가중 평균을 정수(0~255)로 반올림.
        /// ROLLBACK_MAPMAKER_SHAREDQUANT_20260624: PIL quantize 대체 — .py 와 부동소수 차이 없애려 정수 RGB로만 계산.</summary>
        private static void MedianCutQuantize(List<Color> uniqueColors, int[] uniqueWeights, int K,
            out List<Color> clusterRgb, out int[] colorToCluster)
        {
            int U = uniqueColors.Count;
            colorToCluster = new int[U];
            // 정수 RGB(0~255) 사전 계산 — 레퍼런스(.py)와 동일 도메인. packed = 정렬 동률 분리용 전순서 키.
            var ir = new int[U]; var ig = new int[U]; var ib = new int[U]; var packed = new int[U];
            for (int i = 0; i < U; i++)
            {
                int r = Mathf.Clamp((int)(uniqueColors[i].r * 255f + 0.5f), 0, 255);
                int g = Mathf.Clamp((int)(uniqueColors[i].g * 255f + 0.5f), 0, 255);
                int b = Mathf.Clamp((int)(uniqueColors[i].b * 255f + 0.5f), 0, 255);
                ir[i] = r; ig[i] = g; ib[i] = b; packed[i] = (r << 16) | (g << 8) | b;
            }

            var boxes = new List<List<int>>();
            var all = new List<int>(U);
            for (int i = 0; i < U; i++) all.Add(i);
            boxes.Add(all);

            while (boxes.Count < K)
            {
                long bestP = -1; int bestBox = -1, bestChannel = 0;
                for (int b = 0; b < boxes.Count; b++)
                {
                    var bx = boxes[b];
                    if (bx.Count < 2) continue;
                    int mnr = 255, mng = 255, mnb = 255, mxr = 0, mxg = 0, mxb = 0;
                    long tw = 0;
                    foreach (int i in bx)
                    {
                        if (ir[i] < mnr) mnr = ir[i]; if (ir[i] > mxr) mxr = ir[i];
                        if (ig[i] < mng) mng = ig[i]; if (ig[i] > mxg) mxg = ig[i];
                        if (ib[i] < mnb) mnb = ib[i]; if (ib[i] > mxb) mxb = ib[i];
                        tw += uniqueWeights[i];
                    }
                    int rr = mxr - mnr, rg = mxg - mng, rb = mxb - mnb;
                    int ch = (rr >= rg && rr >= rb) ? 0 : (rg >= rb ? 1 : 2);
                    int rng = Mathf.Max(rr, Mathf.Max(rg, rb));
                    long p = (long)rng * tw;                     // 최장축 × 총빈도
                    if (p > bestP) { bestP = p; bestBox = b; bestChannel = ch; }
                }
                if (bestBox < 0) break; // 더 쪼갤 박스 없음

                var box = boxes[bestBox];
                int channel = bestChannel;
                int[] chArr = channel == 0 ? ir : channel == 1 ? ig : ib;
                box.Sort((x, y) =>
                {
                    int d = chArr[x].CompareTo(chArr[y]);
                    return d != 0 ? d : packed[x].CompareTo(packed[y]);   // 전순서 → .py 안정 정렬과 동일 결과
                });
                float thr = (chArr[box[0]] + chArr[box[box.Count - 1]]) * 0.5f;   // 채널 값 중앙
                int splitAt = box.Count - 1;
                for (int i = 0; i < box.Count; i++)
                    if (chArr[box[i]] > thr) { splitAt = Mathf.Clamp(i, 1, box.Count - 1); break; }
                var left = box.GetRange(0, splitAt);
                var right = box.GetRange(splitAt, box.Count - splitAt);
                boxes[bestBox] = left;
                boxes.Add(right);
            }

            clusterRgb = new List<Color>(boxes.Count);
            for (int b = 0; b < boxes.Count; b++)
            {
                long sr = 0, sg = 0, sb = 0, sw = 0;
                foreach (int i in boxes[b])
                {
                    int w = uniqueWeights[i];
                    sr += (long)ir[i] * w; sg += (long)ig[i] * w; sb += (long)ib[i] * w; sw += w;
                    colorToCluster[i] = b;
                }
                if (sw == 0) sw = 1;
                int rr = Mathf.Clamp((int)((double)sr / sw + 0.5), 0, 255);
                int gg = Mathf.Clamp((int)((double)sg / sw + 0.5), 0, 255);
                int bb = Mathf.Clamp((int)((double)sb / sw + 0.5), 0, 255);
                clusterRgb.Add(new Color(rr / 255f, gg / 255f, bb / 255f));   // 정수 반올림 → .py 와 동일
            }
        }

        /// <summary>소스 이미지를 그리드 셀로 다운샘플 — 셀별 대표 소스색(블록 최빈 MODE) + 빈칸 마스크.
        /// 빈칸 = alpha 투명 과반(문서 기본) + (옵션) 밝기 컷 과반.</summary>
        private void BuildCellSourceColors(int gw, int gh, out Color[,] cellColor, out bool[,] cellEmpty)
        {
            cellColor = new Color[gw, gh];
            cellEmpty = new bool[gw, gh];
            int srcW = _importedImage.width, srcH = _importedImage.height;
            var votes = new Dictionary<int, int>();
            var rep = new Dictionary<int, Color>();

            for (int c = 0; c < gw; c++)
                for (int r = 0; r < gh; r++)
                {
                    int x0 = (int)((float)c / gw * srcW);
                    int x1 = Mathf.Max(x0 + 1, (int)((float)(c + 1) / gw * srcW));
                    int y0 = (int)((float)r / gh * srcH);
                    int y1 = Mathf.Max(y0 + 1, (int)((float)(r + 1) / gh * srcH));

                    votes.Clear(); rep.Clear();
                    int bg = 0, tot = 0;
                    for (int py = y0; py < y1 && py < srcH; py++)
                        for (int px = x0; px < x1 && px < srcW; px++)
                        {
                            Color p = _importedImage.GetPixel(px, py);
                            tot++;
                            float brightness = p.r * 0.299f + p.g * 0.587f + p.b * 0.114f;
                            if (p.a < IMPORT_EMPTY_ALPHA || (_importBgThreshold > 0f && brightness < _importBgThreshold))
                            { bg++; continue; }
                            int key = PackRgb(p);
                            votes[key] = votes.TryGetValue(key, out int hc) ? hc + 1 : 1;
                            if (!rep.ContainsKey(key)) rep[key] = p;
                        }

                    if (bg > tot / 2 || votes.Count == 0) { cellEmpty[c, r] = true; continue; }
                    int modeKey = 0, modeCnt = 0;
                    foreach (var kv in votes) if (kv.Value > modeCnt) { modeCnt = kv.Value; modeKey = kv.Key; }
                    cellColor[c, r] = rep[modeKey];
                }
        }

        /// <summary>레퍼런스 snap_clusters — 대표 선별 + 헝가리안 1:1 배정 + 비대표 상속 + 표현불가 flags.
        /// 반환: 클러스터→팔레트 인덱스(0-base, MapMaker 팔레트 인덱스).</summary>
        private int[] SnapClusters(List<Color> clusterRgb, int[] clusterWeight, int targetN,
            float gapTh, float power, out List<string> flags)
        {
            EnsurePaletteLab();
            int K = clusterRgb.Count;
            int P = PALETTE.Length;
            var cLab = new PerceptualColor.Lab[K];
            for (int i = 0; i < K; i++) cLab[i] = PerceptualColor.RgbToLab(clusterRgb[i]);

            int N = targetN > 0 ? Mathf.Min(targetN, K) : K;
            int[] rep = SelectRepresentatives(cLab, clusterWeight, N, power);

            var cost = new float[rep.Length, P];
            for (int k = 0; k < rep.Length; k++)
                for (int pi = 0; pi < P; pi++)
                    cost[k, pi] = PerceptualColor.DeltaE2000(cLab[rep[k]], _paletteLab[pi]);

            int[] col = new int[rep.Length];
            if (N <= P)
            {
                col = Hungarian(cost);
                // 안전망: 헝가리안이 미배정으로 남기면 최근접으로 보정.
                for (int k = 0; k < rep.Length; k++)
                    if (col[k] < 0) col[k] = ArgMinRow(cost, k, P);
            }
            else
            {
                for (int k = 0; k < rep.Length; k++) col[k] = ArgMinRow(cost, k, P);
            }

            var repToId = new Dictionary<int, int>(rep.Length);
            for (int k = 0; k < rep.Length; k++) repToId[rep[k]] = col[k];

            // 비대표 클러스터 → 가장 가까운 대표의 ID 상속 (클러스터끼리 ΔE)
            var cl2id = new int[K];
            for (int ci = 0; ci < K; ci++)
            {
                float best = float.MaxValue; int br = rep[0];
                for (int k = 0; k < rep.Length; k++)
                {
                    float d = PerceptualColor.DeltaE2000(cLab[ci], cLab[rep[k]]);
                    if (d < best) { best = d; br = rep[k]; }
                }
                cl2id[ci] = repToId[br];
            }

            flags = new List<string>();
            for (int k = 0; k < rep.Length; k++)
            {
                float d = cost[k, col[k]];
                if (d > gapTh) flags.Add($"#{rep[k]}→{COLOR_LABELS[col[k]]}(ΔE{d:F1})");
            }
            return cl2id;
        }

        private static int ArgMinRow(float[,] cost, int row, int cols)
        {
            int best = 0; float bd = float.MaxValue;
            for (int p = 0; p < cols; p++) if (cost[row, p] < bd) { bd = cost[row, p]; best = p; }
            return best;
        }

        // ROLLBACK_MAPMAKER_DOCSNAP_20260624: 문서 파이프라인 오케스트레이션.
        //   [1] 다운샘플(셀별 소스색+빈칸) → [2] 소스 고유색 클러스터(≤64 직접 / 초과 median-cut)
        //   → [3] 가중(빈도) → [4] snap_clusters(대표선별+헝가리안 1:1) → [5] 셀별 팔레트 인덱스.
        private void UpdateImagePreview()
        {
            if (_importedImage == null) return;

            int gw = _importGridCols, gh = _importGridRows;
            _importPreview = new int[gw, gh];

            // [1] 다운샘플 → 셀별 소스색 + 빈칸 마스크
            BuildCellSourceColors(gw, gh, out Color[,] cellColor, out bool[,] cellEmpty);

            // [2] 소스 고유색 수집(빈도=셀 수). 각 고유색 → 인덱스.
            var uniqueList = new List<Color>();
            var uniqueWeights = new List<int>();
            var keyToUnique = new Dictionary<int, int>();
            for (int c = 0; c < gw; c++)
                for (int r = 0; r < gh; r++)
                {
                    if (cellEmpty[c, r]) continue;
                    int key = PackRgb(cellColor[c, r]);
                    if (keyToUnique.TryGetValue(key, out int ui)) uniqueWeights[ui]++;
                    else { keyToUnique[key] = uniqueList.Count; uniqueList.Add(cellColor[c, r]); uniqueWeights.Add(1); }
                }

            if (uniqueList.Count == 0)
            {
                for (int c = 0; c < gw; c++) for (int r = 0; r < gh; r++) _importPreview[c, r] = -1;
                ApplyPreviewToGrid(gw, gh);
                SetStatus("Preview: 빈 이미지(전부 빈칸). 알파/배경 임계 확인.");
                return;
            }

            // 고유색 ≤ CAP → 무손실 직접 클러스터 / 초과 → median-cut 축소
            List<Color> clusterRgb;
            int[] uniqueToCluster;
            if (uniqueList.Count <= IMPORT_CLUSTER_CAP)
            {
                clusterRgb = new List<Color>(uniqueList);
                uniqueToCluster = new int[uniqueList.Count];
                for (int i = 0; i < uniqueList.Count; i++) uniqueToCluster[i] = i;
            }
            else
            {
                int K = Mathf.Min(IMPORT_CLUSTER_CAP, Mathf.Max((_importNumColors > 0 ? _importNumColors : 8) * 4, 32));
                MedianCutQuantize(uniqueList, uniqueWeights.ToArray(), K, out clusterRgb, out uniqueToCluster);
            }

            // [3] 클러스터 가중 = 고유색 빈도 합
            var clusterWeight = new int[clusterRgb.Count];
            for (int i = 0; i < uniqueList.Count; i++) clusterWeight[uniqueToCluster[i]] += uniqueWeights[i];

            // [4] snap_clusters → 클러스터별 팔레트 인덱스
            int[] cl2id = SnapClusters(clusterRgb, clusterWeight, _importNumColors,
                IMPORT_GAP_THRESHOLD, IMPORT_DISPERSION, out List<string> flags);

            // [5] 셀 → 팔레트 인덱스
            for (int c = 0; c < gw; c++)
                for (int r = 0; r < gh; r++)
                {
                    if (cellEmpty[c, r]) { _importPreview[c, r] = -1; continue; }
                    int u = keyToUnique[PackRgb(cellColor[c, r])];
                    _importPreview[c, r] = cl2id[uniqueToCluster[u]];
                }

            // 색상별 카운트를 10의 배수로 조정 (색상 스냅과 별개 단계 — 문서 범위 밖, 기존 유지)
            if (_importRoundTo > 0)
                RoundColorCounts(_importPreview, gw, gh, _importRoundTo);

            // 실제 사용된 팔레트 색 → _selectedColors (스냅이 자동 선택; 문서엔 사용자 서브셋 제약 없음)
            var used = new HashSet<int>();
            for (int c = 0; c < gw; c++)
                for (int r = 0; r < gh; r++)
                    if (_importPreview[c, r] >= 0) used.Add(_importPreview[c, r]);
            _selectedColors.Clear(); _sortedColorsDirty = true;
            foreach (int id in used) _selectedColors.Add(id);
            if (_selectedColors.Count < 1) _selectedColors.Add(0);
            _numColors = _selectedColors.Count;
            RebuildColorToggleGrid();
            RebuildPalette();
            _infoDirty = true;

            ApplyPreviewToGrid(gw, gh);

            // 리포트(색별 카운트 + 표현불가 flags)
            var counts = new Dictionary<int, int>();
            int totalBalloons = 0;
            for (int c = 0; c < gw; c++)
                for (int r = 0; r < gh; r++)
                    if (_importPreview[c, r] >= 0)
                    {
                        int ci = _importPreview[c, r];
                        counts[ci] = counts.ContainsKey(ci) ? counts[ci] + 1 : 1;
                        totalBalloons++;
                    }
            var sb = new System.Text.StringBuilder($"Preview: {gw}x{gh} {counts.Count}C {totalBalloons}B — ");
            foreach (var kvp in counts) sb.Append($"{COLOR_LABELS[kvp.Key]}={kvp.Value} ");
            if (flags != null && flags.Count > 0)
                sb.Append($" | [표현불가 {flags.Count}] " + string.Join(", ", flags));
            SetStatus(sb.ToString());
        }

        /// <summary>_importPreview 를 현재 그리드(_balloonColors)에 반영.</summary>
        private void ApplyPreviewToGrid(int gw, int gh)
        {
            _gridCols = gw;
            _gridRows = gh;
            InitGrid();
            for (int c = 0; c < gw; c++)
                for (int r = 0; r < gh; r++)
                    _balloonColors[c, r] = _importPreview[c, r];
            _levelLoaded = true;
            OnBalloonGridChanged();
        }

        // ROLLBACK_MAPMAKER_MOD10_20260624: ÷10(10배수) 보정은 검증된 Mod10 모듈로 위임.
        //   Mod10 = 색매핑과 분리된 독립 단계(색은 안 건드림). STEP A(총합 ÷10, 구석 빈칸)
        //   + STEP B~D(버퍼 흡수 + relay 다중홉 + 모티프 안전 배치)로 "전 색 ÷10" 수학적 보장.
        //   기존 단순 반올림(MergeTinyColors+경계+ForceRound)은 multiple!=10 일 때만 fallback.
        private void RoundColorCounts(int[,] grid, int cols, int rows, int multiple)
        {
            if (multiple == 10)
            {
                EnforceMod10OnColRowGrid(grid, cols, rows, Mod10.NO_FRAME);
                return;
            }

            // ── 이하 fallback: multiple != 10 일 때만(일반 N배수 단순 반올림) ──
            // Pass 1: Merge colors with too few balloons into nearest palette color
            MergeTinyColors(grid, cols, rows, multiple);

            // Pass 2: Boundary-first rounding
            for (int pass = 0; pass < 3; pass++) // up to 3 passes to converge
            {
                // Collect positions per color
                var colorPositions = new Dictionary<int, List<Vector2Int>>();
                for (int c = 0; c < cols; c++)
                    for (int r = 0; r < rows; r++)
                    {
                        int ci = grid[c, r];
                        if (ci < 0) continue;
                        if (!colorPositions.ContainsKey(ci))
                            colorPositions[ci] = new List<Vector2Int>();
                        colorPositions[ci].Add(new Vector2Int(c, r));
                    }

                // Compute targets
                var targets = new Dictionary<int, int>();
                foreach (var kvp in colorPositions)
                {
                    int count = kvp.Value.Count;
                    int rounded = Mathf.RoundToInt((float)count / multiple) * multiple;
                    if (rounded < multiple) rounded = multiple;
                    targets[kvp.Key] = rounded;
                }

                bool anyChanged = false;

                // Reduce excess colors via boundary pixels
                foreach (var kvp in colorPositions)
                {
                    int ci = kvp.Key;
                    int excess = kvp.Value.Count - targets[ci];
                    if (excess <= 0) continue;

                    // Boundary cells (neighbor has different color)
                    var boundary = new List<Vector2Int>();
                    foreach (var pos in kvp.Value)
                    {
                        int x = pos.x, y = pos.y;
                        bool edge = false;
                        if (x > 0 && grid[x - 1, y] != ci) edge = true;
                        if (x < cols - 1 && grid[x + 1, y] != ci) edge = true;
                        if (y > 0 && grid[x, y - 1] != ci) edge = true;
                        if (y < rows - 1 && grid[x, y + 1] != ci) edge = true;
                        if (edge) boundary.Add(pos);
                    }

                    int removed = 0;
                    foreach (var pos in boundary)
                    {
                        if (removed >= excess) break;
                        int bestNeighbor = -1;
                        int bestNeed = int.MinValue;
                        Vector2Int[] dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
                        foreach (var d in dirs)
                        {
                            int nx = pos.x + d.x, ny = pos.y + d.y;
                            if (nx < 0 || nx >= cols || ny < 0 || ny >= rows) continue;
                            int nc = grid[nx, ny];
                            if (nc < 0 || nc == ci) continue;
                            int need = targets.ContainsKey(nc) ? targets[nc] - (colorPositions.ContainsKey(nc) ? colorPositions[nc].Count : 0) : 0;
                            if (need > bestNeed) { bestNeed = need; bestNeighbor = nc; }
                        }
                        if (bestNeighbor >= 0)
                        {
                            grid[pos.x, pos.y] = bestNeighbor;
                            if (!colorPositions.ContainsKey(bestNeighbor))
                                colorPositions[bestNeighbor] = new List<Vector2Int>();
                            colorPositions[bestNeighbor].Add(pos);
                            removed++;
                            anyChanged = true;
                        }
                    }

                    // Second pass: corner cells (least visually impactful)
                    if (removed < excess)
                    {
                        var corners = new List<Vector2Int>();
                        foreach (var pos in kvp.Value)
                        {
                            if (grid[pos.x, pos.y] != ci) continue; // already reassigned
                            int x = pos.x, y = pos.y;
                            int sameNeighbors = 0;
                            if (x > 0 && grid[x - 1, y] == ci) sameNeighbors++;
                            if (x < cols - 1 && grid[x + 1, y] == ci) sameNeighbors++;
                            if (y > 0 && grid[x, y - 1] == ci) sameNeighbors++;
                            if (y < rows - 1 && grid[x, y + 1] == ci) sameNeighbors++;
                            if (sameNeighbors <= 2) corners.Add(pos);
                        }
                        foreach (var pos in corners)
                        {
                            if (removed >= excess) break;
                            int bestNeighbor = -1;
                            int bestNeed = int.MinValue;
                            Vector2Int[] dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
                            foreach (var d in dirs)
                            {
                                int nx = pos.x + d.x, ny = pos.y + d.y;
                                if (nx < 0 || nx >= cols || ny < 0 || ny >= rows) continue;
                                int nc = grid[nx, ny];
                                if (nc < 0 || nc == ci) continue;
                                int need = targets.ContainsKey(nc) ? targets[nc] - (colorPositions.ContainsKey(nc) ? colorPositions[nc].Count : 0) : 0;
                                if (need > bestNeed) { bestNeed = need; bestNeighbor = nc; }
                            }
                            if (bestNeighbor >= 0)
                            {
                                grid[pos.x, pos.y] = bestNeighbor;
                                if (!colorPositions.ContainsKey(bestNeighbor))
                                    colorPositions[bestNeighbor] = new List<Vector2Int>();
                                colorPositions[bestNeighbor].Add(pos);
                                removed++;
                                anyChanged = true;
                            }
                        }
                    }
                }

                if (!anyChanged) break;
            }

            // Final verification: ensure all counts are exact multiples
            VerifyAndForceRound(grid, cols, rows, multiple);
        }

        /// <summary>Merge colors with fewer than 'multiple' balloons into nearest palette color by hue.</summary>
        private void MergeTinyColors(int[,] grid, int cols, int rows, int multiple)
        {
            var counts = new Dictionary<int, int>();
            for (int c = 0; c < cols; c++)
                for (int r = 0; r < rows; r++)
                {
                    int ci = grid[c, r];
                    if (ci < 0) continue;
                    counts[ci] = counts.ContainsKey(ci) ? counts[ci] + 1 : 1;
                }

            foreach (var kvp in new Dictionary<int, int>(counts))
            {
                if (kvp.Value >= multiple) continue;
                int tinyColor = kvp.Key;
                // Find nearest color by hue similarity
                float tinyH, tinyS, tinyV;
                Color.RGBToHSV(PALETTE[tinyColor], out tinyH, out tinyS, out tinyV);

                int bestColor = -1;
                float bestDist = float.MaxValue;
                foreach (var other in counts)
                {
                    if (other.Key == tinyColor) continue;
                    if (other.Value <= 0) continue;
                    float oH, oS, oV;
                    Color.RGBToHSV(PALETTE[other.Key], out oH, out oS, out oV);
                    float dh = Mathf.Min(Mathf.Abs(tinyH - oH), 1f - Mathf.Abs(tinyH - oH));
                    float ds = Mathf.Abs(tinyS - oS);
                    float dv = Mathf.Abs(tinyV - oV);
                    float dist = dh * 2f + ds + dv;
                    if (dist < bestDist) { bestDist = dist; bestColor = other.Key; }
                }

                if (bestColor >= 0)
                {
                    for (int c = 0; c < cols; c++)
                        for (int r = 0; r < rows; r++)
                            if (grid[c, r] == tinyColor)
                                grid[c, r] = bestColor;
                    counts[bestColor] = counts.ContainsKey(bestColor) ? counts[bestColor] + kvp.Value : kvp.Value;
                    counts[tinyColor] = 0;
                }
            }
        }

        /// <summary>Force remaining non-multiples by converting boundary pixels to empty (-1).</summary>
        private void VerifyAndForceRound(int[,] grid, int cols, int rows, int multiple)
        {
            var counts = new Dictionary<int, int>();
            for (int c = 0; c < cols; c++)
                for (int r = 0; r < rows; r++)
                {
                    int ci = grid[c, r];
                    if (ci < 0) continue;
                    counts[ci] = counts.ContainsKey(ci) ? counts[ci] + 1 : 1;
                }

            foreach (var kvp in counts)
            {
                int ci = kvp.Key;
                int remainder = kvp.Value % multiple;
                if (remainder == 0) continue;

                // Decide whether to round down or up
                int removeCount = remainder; // round down
                int addCount = multiple - remainder; // round up
                // Use whichever is smaller change
                if (addCount < removeCount)
                {
                    // Round up by converting nearest empty cells to this color
                    int added = 0;
                    for (int c = 0; c < cols && added < addCount; c++)
                        for (int r = 0; r < rows && added < addCount; r++)
                        {
                            if (grid[c, r] != -1) continue;
                            // Check if adjacent to this color
                            bool adj = false;
                            if (c > 0 && grid[c - 1, r] == ci) adj = true;
                            if (c < cols - 1 && grid[c + 1, r] == ci) adj = true;
                            if (r > 0 && grid[c, r - 1] == ci) adj = true;
                            if (r < rows - 1 && grid[c, r + 1] == ci) adj = true;
                            if (adj) { grid[c, r] = ci; added++; }
                        }
                    // If still not enough, fill any empty
                    for (int c = 0; c < cols && added < addCount; c++)
                        for (int r = 0; r < rows && added < addCount; r++)
                            if (grid[c, r] == -1) { grid[c, r] = ci; added++; }
                }
                else
                {
                    // Round down by removing boundary pixels
                    int removed = 0;
                    for (int c = 0; c < cols && removed < removeCount; c++)
                        for (int r = 0; r < rows && removed < removeCount; r++)
                        {
                            if (grid[c, r] != ci) continue;
                            bool edge = false;
                            if (c == 0 || grid[c - 1, r] != ci) edge = true;
                            if (c == cols - 1 || grid[c + 1, r] != ci) edge = true;
                            if (r == 0 || grid[c, r - 1] != ci) edge = true;
                            if (r == rows - 1 || grid[c, r + 1] != ci) edge = true;
                            if (edge) { grid[c, r] = -1; removed++; }
                        }
                    // Force remove if boundary wasn't enough
                    for (int c = 0; c < cols && removed < removeCount; c++)
                        for (int r = 0; r < rows && removed < removeCount; r++)
                            if (grid[c, r] == ci) { grid[c, r] = -1; removed++; }
                }
            }
        }

        private void ApplyImageToGrid()
        {
            if (_importPreview == null) return;
            int gw = _importPreview.GetLength(0);
            int gh = _importPreview.GetLength(1);

            // 1) 그리드 크기 갱신
            _gridCols = gw;
            _gridRows = gh;
            InitGrid();

            for (int c = 0; c < gw; c++)
                for (int r = 0; r < gh; r++)
                {
                    _balloonColors[c, r] = _importPreview[c, r];
                    _balloonGimmicks[c, r] = 0;
                }

            // 2) 색상별 풍선 수 집계
            var colorCounts = new Dictionary<int, int>();
            for (int c = 0; c < gw; c++)
                for (int r = 0; r < gh; r++)
                    if (_importPreview[c, r] >= 0)
                    {
                        int ci = _importPreview[c, r];
                        colorCounts[ci] = colorCounts.ContainsKey(ci) ? colorCounts[ci] + 1 : 1;
                    }

            _numColors = colorCounts.Count;
            _selectedColors.Clear(); _sortedColorsDirty = true;
            foreach (var key in colorCounts.Keys) _selectedColors.Add(key);
            RebuildColorToggleGrid();
            RebuildPalette();

            // 3) 보관함 자동 생성 — 색상별 풍선 수 = 다트 수
            int totalDarts = 0;
            foreach (var v in colorCounts.Values) totalDarts += v;
            int railCap = RailManager.CalculateCapacity(totalDarts);
            _railSlotCount = railCap;

            // 보관함 열 수 = 기존 _holderCols 유지 (2~5)
            int qCols = Mathf.Clamp(_holderCols, 2, 5);
            var holders = new List<(int color, int mag)>();
            foreach (var kvp in colorCounts)
            {
                int remaining = kvp.Value;
                while (remaining > 0)
                {
                    int mag = Mathf.Min(remaining, 50); // 최대 50발
                    // 10의 배수로 맞춤 (마지막 홀더 제외)
                    if (remaining > 50 && mag % 10 != 0)
                        mag = (mag / 10) * 10;
                    if (mag <= 0) mag = remaining;
                    holders.Add((kvp.Key, mag));
                    remaining -= mag;
                }
            }

            // 보관함 행 수 계산
            _holderCols = qCols;
            _holderRows = Mathf.Max(1, (holders.Count + qCols - 1) / qCols);
            _holderColors = new int[_holderCols, _holderRows];
            _holderMags = new int[_holderCols, _holderRows];
            _holderGimmicks = new int[_holderCols, _holderRows];
            _holderChainGroups = new int[_holderCols, _holderRows];
            _holderFrozenHP = new int[_holderCols, _holderRows];
            _holderSpawnerHP = new int[_holderCols, _holderRows];
            _holderSpawnerMag = new int[_holderCols, _holderRows];
            // Clear all holder gimmicks (hidden, spawner, chain, frozen from previous level)
            for (int c2 = 0; c2 < _holderCols; c2++)
                for (int r2 = 0; r2 < _holderRows; r2++)
                {
                    _holderColors[c2, r2] = -1;
                    _holderGimmicks[c2, r2] = 0;      // no gimmick
                    _holderChainGroups[c2, r2] = -1;   // no chain
                    _holderFrozenHP[c2, r2] = 3;       // default frozen HP
                    _holderSpawnerHP[c2, r2] = 0;
                    _holderSpawnerMag[c2, r2] = 20;
                }

            for (int i = 0; i < holders.Count; i++)
            {
                int hc = i % _holderCols;
                int hr = i / _holderCols;
                if (hr >= _holderRows) break;
                _holderColors[hc, hr] = holders[i].color;
                _holderMags[hc, hr] = holders[i].mag;
            }

            // 4) 전체 UI 갱신
            _levelLoaded = true;
            OnBalloonGridChanged();
            RebuildHolderUI();
            _infoDirty = true;

            int totalB = 0;
            foreach (var v in colorCounts.Values) totalB += v;
            SetStatus($"Applied: {gw}x{gh}, {_numColors}C, {totalB}B, {holders.Count} holders, rail={railCap}");
        }

        private void BuildExportSection(Transform p)
        {
            Lbl(p, "Export / Import", 14, FontStyle.Bold);
            var row = Row(p);
            Btn(row, "Save to DB", () => SaveToActiveDB());
            Btn(row, "Load Level", () => LoadLevelById(_levelId));
            // [2026-06-12] Importer 'Episode 파일에 적용' 후 MapMaker 캐시가 stale 하던 문제 —
            // 탭 전환(폐기됨) 대신 명시 Reload 버튼으로 episode 합본 캐시 재빌드.
            Btn(row, "Reload", ReloadEpisodeStore);
            var exportRow = Row(p);
            Btn(exportRow, "Export Episode JSON", ExportEpisodeJson);
            Btn(exportRow, "Export Level JSON", ExportLevelJson);
            // [2026-06-12] 다량 episode 일괄 export — "1-15" 또는 "1,5,6,7" (혼합 "1-3,7" 가능) 입력 후
            // 폴더 선택 → 해당 episode_XX.json 들을 한 번에 복사. (미저장 편집분은 Save This Level 먼저)
            var bulkRow = Row(p);
            MakeInputField(bulkRow, _bulkExportEpisodesInput, s => _bulkExportEpisodesInput = s);
            Btn(bulkRow, "Export Episodes...", ExportEpisodesBulk);
            Sep(p);

            // ── Tutorial Steps ──
            Lbl(p, "Tutorial Steps", 14, FontStyle.Bold);
            var tutRow = Row(p);
            Btn(tutRow, "+ Add Step", () =>
            {
                _tutorialSteps.Add(new TutorialStepData
                {
                    instruction = "설명을 입력하세요",
                    highlightTarget = "holder_0",
                    requireAction = "tap_holder",
                    cutoutWidth = 200, cutoutHeight = 200
                });
                RebuildTutorialStepUI(p);
            });
            Btn(tutRow, "Clear All", () =>
            {
                _tutorialSteps.Clear();
                RebuildTutorialStepUI(p);
                SetStatus("Tutorial steps cleared");
            });

            _tutorialStepContainer = new GameObject("TutorialStepContainer", typeof(RectTransform), typeof(VerticalLayoutGroup)).transform;
            _tutorialStepContainer.SetParent(p, false);
            _tutorialStepContainer.GetComponent<VerticalLayoutGroup>().spacing = 2;

            RebuildTutorialStepUI(p);
            Sep(p);

            // Test Play
            var testRow = Row(p, 44);
            var testBtn = Btn(testRow, "TEST PLAY", TestPlay);
            if (testBtn.GetComponent<Image>())
                testBtn.GetComponent<Image>().color = new Color(0.15f, 0.55f, 0.25f);
        }

        private List<TutorialStepData> _tutorialSteps = new List<TutorialStepData>();
        private Transform _tutorialStepContainer;

        private static readonly string[] TUTORIAL_TARGETS = {
            "holder_0", "holder_1", "holder_2", "holder_3", "holder_4",
            "board", "holder_queue",
            "gimmick_hidden", "gimmick_chain", "gimmick_pinata", "gimmick_spawner"
        };
        private static readonly string[] TUTORIAL_ACTIONS = { "none", "tap_holder", "wait_pop", "tap_anywhere" };

        private void RebuildTutorialStepUI(Transform parent)
        {
            if (_tutorialStepContainer == null) return;
            foreach (Transform c in _tutorialStepContainer) Destroy(c.gameObject);

            for (int i = 0; i < _tutorialSteps.Count; i++)
            {
                int idx = i;
                var step = _tutorialSteps[i];

                // Row 1: index + instruction + delete
                var stepRow = Row(_tutorialStepContainer);
                Lbl(stepRow, $"#{i}", w: 25);
                MakeInputField(stepRow, step.instruction, s => _tutorialSteps[idx].instruction = s);
                Btn(stepRow, "X", () => { _tutorialSteps.RemoveAt(idx); RebuildTutorialStepUI(parent); });

                // [2026-05-15] Row 2: highlightTarget + requireAction + cutout size
                // — MapMaker 에서 cutout 안 보이던 원인: instruction 외 필드 미노출 + default holder_0/200x200 로 고정되던 문제 해소.
                var optRow = Row(_tutorialStepContainer);
                Lbl(optRow, "Target", w: 40);
                MakeTutorialDropdown(optRow, TUTORIAL_TARGETS, step.highlightTarget,
                    v => _tutorialSteps[idx].highlightTarget = v);
                Lbl(optRow, "Act", w: 26);
                MakeTutorialDropdown(optRow, TUTORIAL_ACTIONS, step.requireAction,
                    v => _tutorialSteps[idx].requireAction = v);
                Lbl(optRow, "W", w: 14);
                MakeFloatField(optRow, step.cutoutWidth, 50f, 2000f,
                    v => _tutorialSteps[idx].cutoutWidth = v);
                Lbl(optRow, "H", w: 14);
                MakeFloatField(optRow, step.cutoutHeight, 50f, 2000f,
                    v => _tutorialSteps[idx].cutoutHeight = v);
            }

            if (_tutorialSteps.Count > 0)
                SetStatus($"Tutorial: {_tutorialSteps.Count} steps");
        }

        /// <summary>Tutorial step option dropdown. 현재 값이 옵션 배열에 없으면 첫 항목 사용.</summary>
        private Dropdown MakeTutorialDropdown(Transform p, string[] options, string current, System.Action<string> cb)
        {
            var go = DefaultControls.CreateDropdown(_uiRes);
            go.transform.SetParent(p, false);
            var le = go.AddComponent<LayoutElement>(); le.flexibleWidth = 1; le.preferredHeight = 24;
            go.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.20f);
            var dd = go.GetComponent<Dropdown>();
            dd.ClearOptions();
            dd.AddOptions(new List<string>(options));
            int sel = System.Array.IndexOf(options, current);
            dd.value = sel >= 0 ? sel : 0;
            dd.captionText.font = _font; dd.captionText.fontSize = 12; dd.captionText.color = Color.white;
            dd.onValueChanged.AddListener(v =>
            {
                if (v >= 0 && v < options.Length) cb?.Invoke(options[v]);
            });
            // 초기값 콜백 (배열에 없던 값으로 시작 시 보정)
            if (sel < 0 && options.Length > 0) cb?.Invoke(options[0]);
            return dd;
        }

        #endregion

        #region UI Building — Holder Grid

        private void RebuildHolderUI()
        {
            if (_holderGridContainer == null) return;

            bool needsRebuild = _holderButtonPool == null
                || _holderButtonPool.GetLength(0) != _holderCols
                || _holderButtonPool.GetLength(1) != _holderRows;

            if (needsRebuild)
            {
                // Grid 크기 변경 시에만 전체 재생성
                var toDestroy = new List<GameObject>();
                foreach (Transform c in _holderGridContainer) toDestroy.Add(c.gameObject);
                foreach (var go in toDestroy) { go.transform.SetParent(null); Destroy(go); }

                var glg = _holderGridContainer.GetComponent<GridLayoutGroup>();
                glg.constraintCount = _holderCols;
                float cellW = Mathf.Min(300f / Mathf.Max(_holderCols, 1), 36f);
                glg.cellSize = new Vector2(cellW, cellW);
                var le = _holderGridContainer.GetComponent<LayoutElement>();
                le.preferredHeight = cellW * _holderRows + (_holderRows - 1) * glg.spacing.y + 4;

                _holderButtonPool = new GameObject[_holderCols, _holderRows];

                for (int r = 0; r < _holderRows; r++)
                    for (int c = 0; c < _holderCols; c++)
                    {
                        int cc = c, rr = r;
                        var btn = DefaultControls.CreateButton(_uiRes);
                        btn.transform.SetParent(_holderGridContainer, false);
                        var t = btn.GetComponentInChildren<Text>();
                        t.font = _font; t.fontSize = 8; t.color = Color.white;
                        btn.GetComponent<Button>().onClick.AddListener(() => PaintHolderCell(cc, rr));
                        _holderButtonPool[c, r] = btn;
                    }

                LayoutRebuilder.ForceRebuildLayoutImmediate(_holderGridContainer.GetComponent<RectTransform>());
            }

            // 모든 버튼 외형만 갱신 (Destroy/Create 없음)
            for (int r = 0; r < _holderRows; r++)
                for (int c = 0; c < _holderCols; c++)
                    UpdateHolderButton(c, r);
        }

        private string HolderPaintModeLabel()
        {
            switch (_holderPaintMode)
            {
                case GimmickPaintMode.GimmickOnly:  return "기믹만 추가";
                case GimmickPaintMode.GimmickErase: return "기믹 제거";
                default:                            return "색상+기믹";
            }
        }

        /// <summary>
        /// 큐 셀 paint — _holderPaintMode 에 따라 색상+기믹 / 기믹만 / 기믹제거 분기.
        /// GimmickOnly·GimmickErase 는 색상·개수(magazineCount) 를 보존한다.
        /// </summary>
        private void PaintHolderCell(int cc, int rr)
        {
            // ROLLBACK_PIPE_INSERT_20260625: Pipe/Glass Pipe 브러시로 '기존 홀더'를 클릭하면 그 홀더를 소비하지 않고
            // Pipe 를 그 칸에 놓고 클릭 홀더를 한 칸 아래(payload #1)로 밀어넣어 그룹에 포함시킨다.
            bool isPipeBrush = _holderPaintMode != GimmickPaintMode.GimmickErase
                && _paintHolderGimmick > 0 && _paintHolderGimmick < HOLDER_GIMMICK_NAMES.Length
                && (HOLDER_GIMMICK_NAMES[_paintHolderGimmick] == "Spawner_O"
                 || HOLDER_GIMMICK_NAMES[_paintHolderGimmick] == "Spawner_T");
            if (isPipeBrush && _holderColors[cc, rr] >= 0)
            {
                if (TryInsertPipeAt(cc, rr))
                {
                    RebuildHolderUI();
                    SetQueueConfirmReady(false);
                    _infoDirty = true;
                    return;
                }
                // 아래 빈 칸이 없어 밀 수 없음 — 손실 방지를 위해 덮어쓰지 않고 안내.
                SetStatus("Pipe 아래에 빈 칸이 없어 홀더를 밀어넣을 수 없습니다. 컬럼 아래를 비우고 다시 시도하세요.");
                return;
            }

            if (isPipeBrush)
            {
                ApplyPipeAnchorCell(cc, rr);
                UpdateHolderButton(cc, rr);
                RefreshHolderPipeOutlines();
                SetQueueConfirmReady(false);
                _infoDirty = true;
                return;
            }

            switch (_holderPaintMode)
            {
                case GimmickPaintMode.GimmickErase:
                    // 색상/개수 유지, 배치된 기믹만 제거.
                    ClearHolderGimmick(cc, rr);
                    break;

                case GimmickPaintMode.GimmickOnly:
                    // 색상 있는 셀에만 기믹 덮어쓰기 (색상·개수 보존). 빈 셀은 무시.
                    if (_holderColors[cc, rr] >= 0)
                        ApplyHolderGimmick(cc, rr);
                    break;

                default: // Normal — 색상+개수+기믹 동시 설정 (기존 동작)
                    _holderColors[cc, rr] = _paintColor;
                    _holderMags[cc, rr] = _paintColor >= 0 ? _defaultMag : 0;
                    if (_paintColor >= 0) ApplyHolderGimmick(cc, rr);
                    else ClearHolderGimmick(cc, rr);
                    break;
            }
            if (_holderGridNeedsRebuildAfterPaint)
            {
                _holderGridNeedsRebuildAfterPaint = false;
                RebuildHolderUI();
            }
            else
            {
                UpdateHolderButton(cc, rr);
                RefreshHolderPipeOutlines();
            }
            // Generate 결과가 수동 변경됨 — Confirm 무효화 (§8 수동 조정 후 재검증)
            SetQueueConfirmReady(false);
            _infoDirty = true;
        }

        /// <summary>현재 브러시 기믹 + 부속 파라미터를 큐 셀에 적용 (색상/개수는 안 건드림).</summary>
        private void ApplyHolderGimmick(int cc, int rr)
        {
            _holderGimmicks[cc, rr] = _paintHolderGimmick;
            // [Chain fix] chainGroup 은 Chain(Linked) 기믹일 때만 기록. 무조건 기록하면 이후 다른 기믹/색을 칠할 때
            // _paintChainGroup 이 남아 선택 안 한 holder 까지 같은 그룹에 편입됨(연결 버그). Spawner/Lock 과 동일 게이팅.
            bool isChainHolder = _paintHolderGimmick > 0 && _paintHolderGimmick < HOLDER_GIMMICK_NAMES.Length
                && HOLDER_GIMMICK_NAMES[_paintHolderGimmick] == "Chain";
            _holderChainGroups[cc, rr] = isChainHolder ? _paintChainGroup : -1;
            _holderFrozenHP[cc, rr] = _paintFrozenHP;
            bool isSpawnerGimmick = _paintHolderGimmick > 0 && _paintHolderGimmick < HOLDER_GIMMICK_NAMES.Length
                && (HOLDER_GIMMICK_NAMES[_paintHolderGimmick] == "Spawner_T"
                 || HOLDER_GIMMICK_NAMES[_paintHolderGimmick] == "Spawner_O");
            _holderSpawnerHP[cc, rr] = isSpawnerGimmick ? _paintSpawnerHP : 0;
            _holderSpawnerMag[cc, rr] = isSpawnerGimmick ? _paintSpawnerMag : 20;
            // GLASSPIPE_PARITY_20260625: Pipe·Glass Pipe 둘 다 앵커(배포 홀더 아님)라 셀 mag 0.
            if (isSpawnerGimmick)
                _holderMags[cc, rr] = 0;
            bool isLockKeyHolder = _paintHolderGimmick > 0 && _paintHolderGimmick < HOLDER_GIMMICK_NAMES.Length
                && HOLDER_GIMMICK_NAMES[_paintHolderGimmick] == "Lock_Key";
            _holderLockPairIds[cc, rr] = isLockKeyHolder ? _paintLockPairId : -1;
        }

        // ROLLBACK_PIPE_INSERT_20260625: 컬럼 셀의 모든 per-cell 데이터(색·개수·기믹 부속)를 src→dst 로 복사.
        private void ApplyPipeAnchorCell(int cc, int rr)
        {
            // ROLLBACK_PIPE_COLORLESS_ANCHOR_20260625:
            // Pipe/Glass Pipe is a queue gimmick anchor, not a colored dart holder.
            _holderColors[cc, rr] = -1;
            _holderMags[cc, rr] = 0;
            ApplyHolderGimmick(cc, rr);
        }

        private void CopyHolderCell(int c, int srcR, int dstR)
        {
            _holderColors[c, dstR]      = _holderColors[c, srcR];
            _holderMags[c, dstR]        = _holderMags[c, srcR];
            _holderGimmicks[c, dstR]    = _holderGimmicks[c, srcR];
            _holderChainGroups[c, dstR] = _holderChainGroups[c, srcR];
            _holderFrozenHP[c, dstR]    = _holderFrozenHP[c, srcR];
            _holderSpawnerHP[c, dstR]   = _holderSpawnerHP[c, srcR];
            _holderSpawnerMag[c, dstR]  = _holderSpawnerMag[c, srcR];
            if (_holderLockPairIds != null)
                _holderLockPairIds[c, dstR] = _holderLockPairIds[c, srcR];
        }

        /// <summary>ROLLBACK_PIPE_INSERT_20260625: Pipe/Glass Pipe 를 (cc,rr) 에 배치하되 클릭한 홀더를
        /// 소비하지 않고 한 칸 아래(payload #1)로 밀어넣는다. 아래 첫 빈 칸까지만 밀어 데이터 손실 방지.
        /// 아래에 빈 칸이 전혀 없으면 false(호출부에서 안내).</summary>
        private void ClearHolderCell(int c, int r)
        {
            _holderColors[c, r] = -1;
            _holderMags[c, r] = 0;
            _holderGimmicks[c, r] = 0;
            _holderChainGroups[c, r] = -1;
            _holderFrozenHP[c, r] = 3;
            _holderSpawnerHP[c, r] = 0;
            _holderSpawnerMag[c, r] = 20;
            if (_holderLockPairIds != null)
                _holderLockPairIds[c, r] = -1;
        }

        private void CollapsePipeColumnAfterRemove(int c, int removedRow)
        {
            // ROLLBACK_PIPE_REMOVE_COLLAPSE_20260625:
            // Removing a Pipe/Glass Pipe should undo the insert shift in this column only.
            for (int r = removedRow; r < _holderRows - 1; r++)
                CopyHolderCell(c, r + 1, r);
            ClearHolderCell(c, _holderRows - 1);
            _holderGridNeedsRebuildAfterPaint = true;
        }

        private bool TryInsertPipeAt(int cc, int rr)
        {
            int emptyRow = -1;
            for (int r = rr + 1; r < _holderRows; r++)
                if (_holderColors[cc, r] < 0) { emptyRow = r; break; }
            if (emptyRow < 0)
            {
                // ROLLBACK_PIPE_AUTO_ROW_APPEND_20260625:
                // Grow holder rows by one when a pipe insert needs a payload slot at the bottom.
                _holderRows = Mathf.Max(1, _holderRows + 1);
                InitGrid();
                if (_holderRowsInput != null)
                    _holderRowsInput.text = _holderRows.ToString();
                emptyRow = _holderRows - 1;
            }

            // rr..emptyRow-1 을 한 칸씩 아래로 — 클릭 홀더가 rr → rr+1(payload #1) 로 이동.
            for (int r = emptyRow; r > rr; r--)
                CopyHolderCell(cc, r - 1, r);

            ApplyPipeAnchorCell(cc, rr); // gimmick=Pipe, colorless anchor, spawnerHP=Count, mag=0
            return true;
        }

        /// <summary>큐 셀의 기믹 부속 데이터를 기본값으로 리셋 (색상/개수는 유지).</summary>
        private void ClearHolderGimmick(int cc, int rr)
        {
            bool wasPipeAnchor = IsPipeAnchorCell(cc, rr);
            _holderGimmicks[cc, rr] = 0;
            _holderChainGroups[cc, rr] = -1;
            _holderFrozenHP[cc, rr] = _paintFrozenHP;
            _holderSpawnerHP[cc, rr] = 0;
            _holderSpawnerMag[cc, rr] = 20;
            _holderLockPairIds[cc, rr] = -1;
            if (wasPipeAnchor)
            {
                CollapsePipeColumnAfterRemove(cc, rr);
            }
        }

        private void UpdateHolderButton(int c, int r)
        {
            if (_holderButtonPool == null || c >= _holderButtonPool.GetLength(0) || r >= _holderButtonPool.GetLength(1)) return;
            var btn = _holderButtonPool[c, r];
            if (btn == null) return;
            int ci = _holderColors[c, r];
            int gi = _holderGimmicks[c, r];
            bool isPipeAnchor = IsPipeAnchorCell(c, r);
            Color cellBg = isPipeAnchor
                ? new Color(0.34f, 0.30f, 0.16f)
                : ((ci >= 0 && ci < PALETTE.Length) ? PALETTE[ci] : new Color(0.22f, 0.22f, 0.26f));
            btn.GetComponent<Image>().color = cellBg;
            ApplyPipePreviewOutline(btn, c, r);
            var t = btn.GetComponentInChildren<Text>();
            t.color = ContrastTextColor(cellBg);
            string gimmickMark = (gi > 0 && gi < HOLDER_GIMMICK_NAMES.Length) ? HOLDER_GIMMICK_NAMES[gi].Substring(0, System.Math.Min(2, HOLDER_GIMMICK_NAMES[gi].Length)) : "";
            int chainGrp = _holderChainGroups[c, r];
            string chainMark = chainGrp > 0 ? $"C{chainGrp}" : "";
            string mark = gimmickMark + (chainMark.Length > 0 ? " " + chainMark : "");
            if (isPipeAnchor)
                t.text = $"P{Mathf.Max(0, _holderSpawnerHP[c, r])}";
            else
                t.text = ci >= 0 ? $"{_holderMags[c, r]}{(mark.Length > 0 ? "\n" + mark : "")}" : ".";
        }

        private void ApplyPipePreviewOutline(GameObject btn, int c, int r)
        {
            if (btn == null) return;

            bool isPipeAnchor = IsPipeAnchorCell(c, r);
            bool isPipePayload = IsPipePayloadPreviewCell(c, r);
            var outline = btn.GetComponent<Outline>();
            if (!isPipeAnchor && !isPipePayload)
            {
                if (outline != null) outline.enabled = false;
                return;
            }

            if (outline == null) outline = btn.AddComponent<Outline>();
            outline.enabled = true;
            outline.useGraphicAlpha = false;
            outline.effectColor = isPipeAnchor
                ? new Color(1f, 0.86f, 0.18f, 1f)
                : new Color(0.12f, 0.95f, 1f, 1f);
            outline.effectDistance = isPipeAnchor ? new Vector2(3f, -3f) : new Vector2(2f, -2f);
        }

        // ROLLBACK_GLASSPIPE_PARITY_20260625: Pipe(Spawner_O)·Glass Pipe(Spawner_T) 둘 다 pipe 로 취급(기능 동일).
        private bool IsPipeKind(int gimmickIndex)
        {
            if (gimmickIndex <= 0) return false;
            int pipeIndex = System.Array.IndexOf(HOLDER_GIMMICK_NAMES, "Spawner_O");
            int glassPipeIndex = System.Array.IndexOf(HOLDER_GIMMICK_NAMES, "Spawner_T");
            return gimmickIndex == pipeIndex || gimmickIndex == glassPipeIndex;
        }

        private bool IsPipeAnchorCell(int c, int r)
        {
            return _holderGimmicks != null
                && c >= 0 && c < _holderCols
                && r >= 0 && r < _holderRows
                && IsPipeKind(_holderGimmicks[c, r]);
        }

        private bool IsPipePayloadPreviewCell(int c, int r)
        {
            if (_holderGimmicks == null || _holderSpawnerHP == null) return false;
            if (c < 0 || c >= _holderCols) return false;

            for (int anchorRow = 0; anchorRow < _holderRows; anchorRow++)
            {
                if (!IsPipeKind(_holderGimmicks[c, anchorRow])) continue;
                int count = Mathf.Max(0, _holderSpawnerHP[c, anchorRow]);
                if (r > anchorRow && r <= anchorRow + count)
                    return true;
            }
            return false;
        }

        private void RefreshHolderPipeOutlines()
        {
            if (_holderButtonPool == null) return;
            for (int r = 0; r < _holderRows; r++)
                for (int c = 0; c < _holderCols; c++)
                    ApplyPipePreviewOutline(_holderButtonPool[c, r], c, r);
        }

        #endregion

        #region UI Building — ScrollView & Helpers

        private Transform BuildScrollView(RectTransform parent)
        {
            var svGO = DefaultControls.CreateScrollView(_uiRes);
            svGO.transform.SetParent(parent, false);
            SetFillRect(svGO.GetComponent<RectTransform>());
            svGO.GetComponent<Image>().color = Color.clear;
            var sr = svGO.GetComponent<ScrollRect>();
            sr.horizontal = false; sr.scrollSensitivity = 30;
            var hBar = svGO.transform.Find("Scrollbar Horizontal");
            if (hBar) hBar.gameObject.SetActive(false);
            var vBar = svGO.transform.Find("Scrollbar Vertical");
            if (vBar) { var img = vBar.GetComponent<Image>(); if (img) img.color = new Color(0.15f, 0.15f, 0.2f); }

            var content = sr.content;
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 6, 6);
            vlg.spacing = 3;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return content;
        }

        private RectTransform MakeRT(string n, Transform parent)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private void SetFillRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
        }

        private Text MakeText(Transform parent, string text, int size, FontStyle style, TextAnchor align)
        {
            var go = new GameObject("Txt", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = text; t.font = _font; t.fontSize = size;
            t.fontStyle = style; t.color = Color.white; t.alignment = align;
            return t;
        }

        private Text Lbl(Transform p, string text, int size = 13, FontStyle style = FontStyle.Normal, float w = 0)
        {
            var go = new GameObject("L", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(p, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = size + 8;
            if (w > 0) le.preferredWidth = w; else le.flexibleWidth = 1;
            var t = go.GetComponent<Text>();
            t.text = text; t.font = _font; t.fontSize = size;
            t.fontStyle = style; t.color = Color.white; t.alignment = TextAnchor.MiddleLeft;
            return t;
        }

        private RectTransform Row(Transform p, float h = 28)
        {
            var go = new GameObject("R", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(p, false);
            go.GetComponent<LayoutElement>().preferredHeight = h;
            var hlg = go.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4; hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            return go.GetComponent<RectTransform>();
        }

        private void Sep(Transform p)
        {
            var go = new GameObject("Sep", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(p, false);
            go.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.35f);
            go.GetComponent<LayoutElement>().preferredHeight = 1;
        }

        /// <summary>
        /// 가로 게이지 바. fillImage 와 overlay text 를 out 으로 반환.
        /// 명세 §9 큐 생성기 — 난이도 점수 게이지 (0-100%).
        /// </summary>
        private void MakeGaugeBar(Transform p, out Image fill, out Text overlay, float height = 18f)
        {
            // bg
            var bgGO = new GameObject("Gauge", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            bgGO.transform.SetParent(p, false);
            bgGO.GetComponent<Image>().color = new Color(0.14f, 0.14f, 0.18f);
            bgGO.GetComponent<LayoutElement>().preferredHeight = height;
            bgGO.GetComponent<LayoutElement>().flexibleWidth = 1;

            // fill
            var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGO.transform.SetParent(bgGO.transform, false);
            var frt = fillGO.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(1, 1); frt.offsetMax = new Vector2(-1, -1);
            var fi = fillGO.GetComponent<Image>();
            fi.color = new Color(0.30f, 0.65f, 0.30f);
            fi.type = Image.Type.Filled;
            fi.fillMethod = Image.FillMethod.Horizontal;
            fi.fillAmount = 0f;
            fill = fi;

            // overlay text
            var txtGO = new GameObject("Txt", typeof(RectTransform), typeof(Text));
            txtGO.transform.SetParent(bgGO.transform, false);
            var trt = txtGO.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var t = txtGO.GetComponent<Text>();
            t.text = "-"; t.font = _font; t.fontSize = 12; t.fontStyle = FontStyle.Bold;
            t.color = Color.white; t.alignment = TextAnchor.MiddleCenter;
            overlay = t;
        }

        private Slider MakeSlider(Transform p, float min, float max, float val, bool whole, System.Action<float> cb)
        {
            var go = DefaultControls.CreateSlider(_uiRes);
            go.transform.SetParent(p, false);
            var le = go.AddComponent<LayoutElement>(); le.flexibleWidth = 1; le.preferredHeight = 20;
            var bg = go.transform.Find("Background")?.GetComponent<Image>();
            if (bg) bg.color = new Color(0.18f, 0.18f, 0.22f);
            var fill = go.transform.Find("Fill Area/Fill")?.GetComponent<Image>();
            if (fill) fill.color = new Color(0.3f, 0.55f, 0.9f);
            var s = go.GetComponent<Slider>();
            s.minValue = min; s.maxValue = max; s.wholeNumbers = whole; s.value = val;
            if (cb != null) s.onValueChanged.AddListener(v => cb(v));
            return s;
        }

        private InputField MakeInputField(Transform p, string text, System.Action<string> cb)
        {
            var go = DefaultControls.CreateInputField(_uiRes);
            go.transform.SetParent(p, false);
            var le = go.AddComponent<LayoutElement>(); le.flexibleWidth = 1; le.preferredHeight = 24;
            go.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.20f);
            var inp = go.GetComponent<InputField>();
            inp.text = text; inp.textComponent.font = _font; inp.textComponent.fontSize = 13; inp.textComponent.color = Color.white;
            var ph = inp.placeholder as Text; if (ph) { ph.font = _font; ph.fontSize = 13; }
            if (cb != null) inp.onEndEdit.AddListener(v => cb(v));
            return inp;
        }

        /// <summary>Integer input field with min/max clamping.</summary>
        private InputField MakeIntField(Transform p, int value, int min, int max, System.Action<int> cb)
        {
            var inp = MakeInputField(p, value.ToString(), s =>
            {
                if (int.TryParse(s, out int v))
                    cb?.Invoke(Mathf.Clamp(v, min, max));
            });
            inp.contentType = InputField.ContentType.IntegerNumber;
            return inp;
        }

        /// <summary>Float input field with min/max clamping.</summary>
        private InputField MakeFloatField(Transform p, float value, float min, float max, System.Action<float> cb)
        {
            var inp = MakeInputField(p, value.ToString("F1"), s =>
            {
                if (float.TryParse(s, out float v))
                    cb?.Invoke(Mathf.Clamp(v, min, max));
            });
            inp.contentType = InputField.ContentType.DecimalNumber;
            return inp;
        }

        private void MakeDifficultyDropdown(Transform p)
        {
            var go = DefaultControls.CreateDropdown(_uiRes);
            go.transform.SetParent(p, false);
            var le = go.AddComponent<LayoutElement>(); le.flexibleWidth = 1; le.preferredHeight = 24;
            go.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.20f);
            var dd = go.GetComponent<Dropdown>();
            dd.ClearOptions();
            dd.AddOptions(new List<string>(System.Enum.GetNames(typeof(DifficultyPurpose))));
            dd.value = (int)_difficulty;
            dd.captionText.font = _font; dd.captionText.fontSize = 13; dd.captionText.color = Color.white;
            dd.onValueChanged.AddListener(v => _difficulty = (DifficultyPurpose)v);
            _difficultyDropdown = dd;
        }

        private Button Btn(Transform p, string text, System.Action cb)
        {
            var go = DefaultControls.CreateButton(_uiRes);
            go.transform.SetParent(p, false);
            var le = go.AddComponent<LayoutElement>(); le.flexibleWidth = 1; le.preferredHeight = 28;
            go.GetComponent<Image>().color = new Color(0.22f, 0.22f, 0.28f);
            var t = go.GetComponentInChildren<Text>();
            t.text = text; t.font = _font; t.fontSize = 12; t.color = Color.white;
            go.GetComponent<Button>().onClick.AddListener(() => cb?.Invoke());
            return go.GetComponent<Button>();
        }

        private Button MakeColorBtn(Transform p, Color color, string label, System.Action cb)
        {
            var go = DefaultControls.CreateButton(_uiRes);
            go.transform.SetParent(p, false);
            var le = go.AddComponent<LayoutElement>(); le.preferredWidth = 34; le.preferredHeight = 34;
            go.GetComponent<Image>().color = color;
            var t = go.GetComponentInChildren<Text>();
            t.text = label; t.font = _font; t.fontSize = 14; t.fontStyle = FontStyle.Bold; t.color = ContrastTextColor(color);
            go.GetComponent<Button>().onClick.AddListener(() => cb?.Invoke());
            return go.GetComponent<Button>();
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        //  3D PREVIEW — Balloons + Grid Lines + Gimmick Marks
        // ═══════════════════════════════════════════════════════════════

        #region Board Preview

        /// <summary>Shared sphere mesh for all preview objects (created once).</summary>
        private Mesh _sharedQuadMesh;

        /// <summary>Cached gimmick-specific materials (created on demand, reused).</summary>
        private readonly Dictionary<Color, Material> _gimmickMatCache = new Dictionary<Color, Material>();

        private void RebuildPreview()
        {
            if (_previewRoot) Destroy(_previewRoot.gameObject);
            _previewRoot = new GameObject("BalloonPreview").transform;
            _previewObjs = new GameObject[_gridCols, _gridRows];
            _previewLabels = new TextMesh[_gridCols, _gridRows];

            // Quad 메시 (Sphere 720 tri → Quad 2 tri, ~99.7% GPU 절감)
            if (_sharedQuadMesh == null)
            {
                var tmpQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                _sharedQuadMesh = tmpQuad.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(tmpQuad);
            }

            float spacing = CellSpacing;
            float scale = BalloonScale;

            for (int c = 0; c < _gridCols; c++)
            {
                for (int r = 0; r < _gridRows; r++)
                {
                    float wx = _boardCenter.x + (c - (_gridCols - 1) * 0.5f) * spacing;
                    float wz = _boardCenter.y + (r - (_gridRows - 1) * 0.5f) * spacing;

                    var go = new GameObject("B");
                    go.transform.SetParent(_previewRoot, false);
                    go.transform.localScale = Vector3.one * scale;
                    go.transform.position = new Vector3(wx, 0.5f, wz);
                    go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // Quad를 위에서 보이게
                    go.AddComponent<MeshFilter>().sharedMesh = _sharedQuadMesh;
                    var mr = go.AddComponent<MeshRenderer>();

                    int ci = _balloonColors[c, r];
                    int gi = _balloonGimmicks[c, r];

                    if (ci >= 0)
                    {
                        mr.sharedMaterial = GetCachedMaterial(ci, gi);
                        go.SetActive(true);
                    }
                    else
                    {
                        go.SetActive(false);
                    }
                    _previewObjs[c, r] = go;

                    // Gimmick label
                    if (ci >= 0 && gi > 0 && gi < FIELD_GIMMICK_MARKS.Length && !string.IsNullOrEmpty(FIELD_GIMMICK_MARKS[gi]))
                    {
                        var labelGO = new GameObject("GLabel");
                        labelGO.transform.SetParent(_previewRoot, false);
                        labelGO.transform.position = new Vector3(wx, 1.2f, wz);
                        labelGO.transform.eulerAngles = new Vector3(90f, 0f, 0f);
                        var tm = labelGO.AddComponent<TextMesh>();
                        // ROLLBACK_ICE_MANUAL_GROUP_20260608:
                        // Show explicit Ice group id in MapMaker preview. Group 0 keeps the legacy auto group label.
                        tm.text = GetFieldGimmickPreviewMark(c, r, gi);
                        tm.fontSize = 32;
                        tm.characterSize = scale * 0.35f;
                        tm.alignment = TextAlignment.Center;
                        tm.anchor = TextAnchor.MiddleCenter;
                        tm.color = Color.white;
                        _previewLabels[c, r] = tm;
                    }
                }
            }
        }

        /// <summary>Returns a cached material for the given color/gimmick combination. No allocation per cell.</summary>
        private Material GetCachedMaterial(int colorIndex, int gimmickIndex)
        {
            Color c = GetPreviewColor(colorIndex, gimmickIndex);

            // Normal palette color — use pre-created _colorMats
            if (gimmickIndex <= 0 && colorIndex >= 0 && colorIndex < _colorMats.Length)
                return _colorMats[colorIndex];

            // Gimmick color — cache on demand
            if (!_gimmickMatCache.TryGetValue(c, out Material mat))
            {
                mat = MakeLitMaterial(FindLitShader(), c);
                _gimmickMatCache[c] = mat;
            }
            return mat;
        }

        private Color GetPreviewColor(int colorIndex, int gimmickIndex)
        {
            if (gimmickIndex > 0 && gimmickIndex < FIELD_GIMMICK_NAMES.Length)
            {
                string gn = FIELD_GIMMICK_NAMES[gimmickIndex];
                if (gn == "Wall") return GIMMICK_WALL_COLOR;
                if (gn == "Pin") return GIMMICK_PIN_COLOR;
                // Ice / Barricade 는 색상값 있는 기믹 — 풍선 색상 그대로 표시 (약자 라벨로 기믹 구분).
                if (gn == "Hidden") return GIMMICK_HIDDEN_COLOR;
                // Pinata 는 인게임처럼 '지정된 셀 색상'을 미리보기에도 표시(아래 PALETTE 폴백). Pinata_Box 만 고정색.
                if (gn == "Pinata_Box") return GIMMICK_PINATA_COLOR;
            }
            if (colorIndex >= 0 && colorIndex < PALETTE.Length)
                return PALETTE[colorIndex];
            return Color.grey;
        }

        private void UpdatePreviewCell(int c, int r)
        {
            if (_previewObjs == null || c >= _previewObjs.GetLength(0) || r >= _previewObjs.GetLength(1)) return;

            var go = _previewObjs[c, r];
            if (go == null) return;

            int ci = _balloonColors[c, r];
            int gi = _balloonGimmicks[c, r];

            if (ci >= 0)
            {
                go.GetComponent<MeshRenderer>().sharedMaterial = GetCachedMaterial(ci, gi);
                go.SetActive(true);
            }
            else
            {
                go.SetActive(false);
            }

            // Update gimmick label
            float spacing = CellSpacing;
            float wx = _boardCenter.x + (c - (_gridCols - 1) * 0.5f) * spacing;
            float wz = _boardCenter.y + (r - (_gridRows - 1) * 0.5f) * spacing;

            // Reuse existing label or create once; toggle visibility
            bool needLabel = ci >= 0 && gi > 0 && gi < FIELD_GIMMICK_MARKS.Length && !string.IsNullOrEmpty(FIELD_GIMMICK_MARKS[gi]);
            if (needLabel)
            {
                var tm = _previewLabels[c, r];
                if (tm == null)
                {
                    var labelGO = new GameObject("GLabel");
                    labelGO.transform.SetParent(_previewRoot, false);
                    labelGO.transform.eulerAngles = new Vector3(90f, 0f, 0f);
                    tm = labelGO.AddComponent<TextMesh>();
                    tm.fontSize = 32;
                    tm.alignment = TextAlignment.Center;
                    tm.anchor = TextAnchor.MiddleCenter;
                    _previewLabels[c, r] = tm;
                }
                tm.color = ContrastTextColor(GetPreviewColor(ci, gi));
                tm.transform.position = new Vector3(wx, 1.2f, wz);
                tm.characterSize = BalloonScale * 0.35f;
                // ROLLBACK_ICE_MANUAL_GROUP_20260608:
                // Show explicit Ice group id in MapMaker preview. Group 0 keeps the legacy auto group label.
                tm.text = GetFieldGimmickPreviewMark(c, r, gi);
                tm.gameObject.SetActive(true);
            }
            else if (_previewLabels[c, r] != null)
            {
                _previewLabels[c, r].gameObject.SetActive(false);
            }
        }

        private string GetFieldGimmickPreviewMark(int c, int r, int gimmickIndex)
        {
            string mark = FIELD_GIMMICK_MARKS[gimmickIndex];
            if (gimmickIndex > 0
                && gimmickIndex < FIELD_GIMMICK_NAMES.Length
                && FIELD_GIMMICK_NAMES[gimmickIndex] == "Ice"
                && _balloonIceGroupId != null
                && c < _balloonIceGroupId.GetLength(0)
                && r < _balloonIceGroupId.GetLength(1)
                && _balloonIceGroupId[c, r] > 0)
            {
                return $"{mark}\nG{_balloonIceGroupId[c, r]}";
            }

            return mark;
        }

        #endregion

        #region Grid Lines

        private void RebuildGridLines()
        {
            if (_gridLineRoot) Destroy(_gridLineRoot.gameObject);
            _gridLineRoot = new GameObject("GridLines").transform;

            float spacing = CellSpacing;
            float halfW = _gridCols * spacing * 0.5f;
            float halfH = _gridRows * spacing * 0.5f;
            float startX = _boardCenter.x - halfW + spacing * 0.5f;
            float startZ = _boardCenter.y - halfH + spacing * 0.5f;
            float y = 0.01f; // Just above ground

            // Vertical lines
            for (int c = 0; c <= _gridCols; c++)
            {
                float x = _boardCenter.x + (c - _gridCols * 0.5f) * spacing;
                float z0 = _boardCenter.y - _gridRows * 0.5f * spacing;
                float z1 = _boardCenter.y + _gridRows * 0.5f * spacing;
                CreateGridLine(new Vector3(x, y, z0), new Vector3(x, y, z1));
            }

            // Horizontal lines
            for (int r = 0; r <= _gridRows; r++)
            {
                float z = _boardCenter.y + (r - _gridRows * 0.5f) * spacing;
                float x0 = _boardCenter.x - _gridCols * 0.5f * spacing;
                float x1 = _boardCenter.x + _gridCols * 0.5f * spacing;
                CreateGridLine(new Vector3(x0, y, z), new Vector3(x1, y, z));
            }
        }

        private void CreateGridLine(Vector3 from, Vector3 to)
        {
            var go = new GameObject("GridLine");
            go.transform.SetParent(_gridLineRoot, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = _gridLineMat;
            lr.startWidth = 0.02f; lr.endWidth = 0.02f;
            lr.numCapVertices = 0;
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.useWorldSpace = true;
        }

        // ROLLBACK_FLEXTUBE_AUTHOR_OVERLAY_20260628: MapMaker 에서 FlexTube 의 Start/End 와 클릭 순서를 시각화.
        //   Start(seq0=첫 클릭) 칸 = 빨강 테두리, End(마지막 seq=Edge) 칸 = 흰색 테두리, 순서대로 seq 중심을 잇는 청록 선.
        private Transform _flexTubeOverlayRoot;
        private Material _overlayLineMat;

        private void RefreshFlexTubeOverlay()
        {
            if (_flexTubeOverlayRoot) Destroy(_flexTubeOverlayRoot.gameObject);
            if (_balloonFlexTubeGroupId == null || _balloonFlexTubeSequenceIndex == null) return;
            _flexTubeOverlayRoot = new GameObject("FlexTubeOverlay").transform;

            if (_overlayLineMat == null)
            {
                var sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("Unlit/Color");
                if (sh == null) sh = Shader.Find("Hidden/Internal-Colored");
                _overlayLineMat = new Material(sh);
            }

            float spacing = CellSpacing;
            const float y = 0.62f; // 셀 quad(y=0.5) 위에 보이도록
            Vector3 CellPos(int c, int r) => new Vector3(
                _boardCenter.x + (c - (_gridCols - 1) * 0.5f) * spacing, y,
                _boardCenter.y + (r - (_gridRows - 1) * 0.5f) * spacing);

            // group -> seq -> cells
            var groups = new Dictionary<int, Dictionary<int, List<Vector2Int>>>();
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                {
                    int g = _balloonFlexTubeGroupId[c, r];
                    if (g < 0) continue;
                    int s = _balloonFlexTubeSequenceIndex[c, r];
                    if (s < 0) continue;
                    if (!groups.TryGetValue(g, out var seqMap)) { seqMap = new Dictionary<int, List<Vector2Int>>(); groups[g] = seqMap; }
                    if (!seqMap.TryGetValue(s, out var cells)) { cells = new List<Vector2Int>(); seqMap[s] = cells; }
                    cells.Add(new Vector2Int(c, r));
                }

            foreach (var kv in groups)
            {
                var seqMap = kv.Value;
                var seqs = new List<int>(seqMap.Keys); seqs.Sort();
                if (seqs.Count == 0) continue;

                // 순서 선 (seq 중심들을 순서대로)
                var centers = new List<Vector3>(seqs.Count);
                foreach (int s in seqs)
                {
                    var cells = seqMap[s];
                    Vector3 sum = Vector3.zero;
                    foreach (var cell in cells) sum += CellPos(cell.x, cell.y);
                    centers.Add(sum / cells.Count);
                }
                if (centers.Count >= 2)
                    CreateOverlayLine(centers.ToArray(), new Color(0.12f, 0.95f, 1f, 1f), spacing * 0.06f, false);

                // Start(첫 seq)=빨강, End(마지막 seq)=흰색 테두리
                DrawSeqFrame(seqMap[seqs[0]], new Color(1f, 0.16f, 0.12f, 1f), spacing, CellPos);
                if (seqs.Count > 1)
                    DrawSeqFrame(seqMap[seqs[seqs.Count - 1]], Color.white, spacing, CellPos);
            }
        }

        private void DrawSeqFrame(List<Vector2Int> cells, Color col, float spacing, System.Func<int, int, Vector3> CellPos)
        {
            if (cells == null || cells.Count == 0) return;
            int minC = int.MaxValue, maxC = int.MinValue, minR = int.MaxValue, maxR = int.MinValue;
            foreach (var cell in cells)
            {
                minC = Mathf.Min(minC, cell.x); maxC = Mathf.Max(maxC, cell.x);
                minR = Mathf.Min(minR, cell.y); maxR = Mathf.Max(maxR, cell.y);
            }
            float h = spacing * 0.5f;
            Vector3 bl = CellPos(minC, minR) + new Vector3(-h, 0f, -h);
            Vector3 br = CellPos(maxC, minR) + new Vector3(h, 0f, -h);
            Vector3 tr = CellPos(maxC, maxR) + new Vector3(h, 0f, h);
            Vector3 tl = CellPos(minC, maxR) + new Vector3(-h, 0f, h);
            CreateOverlayLine(new[] { bl, br, tr, tl }, col, spacing * 0.07f, true);
        }

        private void CreateOverlayLine(Vector3[] pts, Color col, float width, bool loop)
        {
            var go = new GameObject("FTOverlayLine");
            go.transform.SetParent(_flexTubeOverlayRoot, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = _overlayLineMat;
            lr.startColor = lr.endColor = col;
            lr.startWidth = lr.endWidth = width;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.loop = loop;
            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
            lr.useWorldSpace = true;
            lr.textureMode = LineTextureMode.Stretch;
            lr.sortingOrder = 200;
        }

        #endregion

        #region Conveyor Preview

        /// <summary>
        /// 컨베이어벨트 프리뷰: 코너 4개 + 직선 4개 = 총 8개 타일.
        /// 코너: 고정 크기. 직선: 스케일 늘려서 코너에 연결.
        /// </summary>
        private void RebuildConveyorPreview()
        {
            if (_conveyorPreviewRoot) Destroy(_conveyorPreviewRoot.gameObject);
            _conveyorPreviewRoot = new GameObject("ConveyorPreview").transform;

            float spacing = CellSpacing;
            float fieldWidth = _gridCols * spacing;
            float halfFieldX = fieldWidth * 0.5f;
            float halfFieldZ = _gridRows * spacing * 0.5f;
            float railWidth = ConveyorTileSize;
            float offsetH = fieldWidth * 0.07f + railWidth * 0.5f;
            float offsetVTop = fieldWidth * 0.09f + railWidth * 0.5f;
            float offsetVBottom = fieldWidth * 0.12f + railWidth * 0.5f;

            float left   = _boardCenter.x - halfFieldX - offsetH;
            float right  = _boardCenter.x + halfFieldX + offsetH;
            float bottom = _boardCenter.y - halfFieldZ - offsetVBottom;
            float top    = _boardCenter.y + halfFieldZ + offsetVTop;

            float cornerSize = railWidth;
            float hLength = right - left - cornerSize;
            float vLength = top - bottom - cornerSize;
            float hCenter = (left + right) * 0.5f;
            float vCenter = (bottom + top) * 0.5f;

            var ts = _railTileSet;
            Sprite spBL = ts != null ? ts.tileBL : null;
            Sprite spBR = ts != null ? ts.tileBR : null;
            Sprite spTL = ts != null ? ts.tileTL : null;
            Sprite spTR = ts != null ? ts.tileTR : null;
            Sprite capB = ts != null ? ts.capB : null;
            Sprite capT = ts != null ? ts.capT : null;
            Sprite capL = ts != null ? ts.capL : null;
            Sprite capR = ts != null ? ts.capR : null;
            Sprite caveB = ts != null ? ts.caveB : null;
            Sprite caveT = ts != null ? ts.caveT : null;
            Sprite caveL = ts != null ? ts.caveL : null;
            Sprite caveR = ts != null ? ts.caveR : null;
            Sprite hSprite  = ts != null ? ts.GetH()  : null;
            Sprite vlSprite = ts != null ? ts.GetVL() : null;
            Sprite vrSprite = ts != null ? ts.GetVR() : null;

            // 허용량별 면 수 계산 (Piñata 비앵커 셀 제외)
            int totalDarts = 0;
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                {
                    if (_balloonColors[c, r] < 0) continue;
                    int gi = _balloonGimmicks[c, r];
                    bool isSizedFieldCell = gi > 0 && gi < FIELD_GIMMICK_NAMES.Length
                        && IsSizedFieldGimmick(FIELD_GIMMICK_NAMES[gi]);
                    // Pinata_Box 는 알마다 색/HP 가 달라 각 셀(알)을 카운트. 그 외 sized 는 anchor 1개만.
                    bool countEggCell = gi > 0 && gi < FIELD_GIMMICK_NAMES.Length && FIELD_GIMMICK_NAMES[gi] == "Pinata_Box";
                    if (isSizedFieldCell && _balloonPinataW[c, r] == 0 && !countEggCell) continue;
                    totalDarts++;
                }
            int capacity = _railSlotCount > 0 ? _railSlotCount : RailManager.CalculateCapacity(totalDarts);
            int sides = RailManager.GetRailSideCount(capacity);

            float rh = _railHeight;

            if (sides >= 4)
            {
                PlaceConveyorSpriteTile(spBL, new Vector3(left, rh, bottom), cornerSize);
                PlaceConveyorSpriteTile(spBR, new Vector3(right, rh, bottom), cornerSize);
                PlaceConveyorSpriteTile(spTR, new Vector3(right, rh, top), cornerSize);
                PlaceConveyorSpriteTile(spTL, new Vector3(left, rh, top), cornerSize);
                PlaceConveyorSpriteTileStretched(hSprite,  new Vector3(hCenter, rh, bottom), hLength, cornerSize);
                PlaceConveyorSpriteTileStretched(hSprite,  new Vector3(hCenter, rh, top),    hLength, cornerSize);
                PlaceConveyorSpriteTileStretched(vlSprite, new Vector3(left,  rh, vCenter), cornerSize, vLength);
                PlaceConveyorSpriteTileStretched(vrSprite, new Vector3(right, rh, vCenter), cornerSize, vLength);
            }
            else if (sides == 3)
            {
                PlaceConveyorSpriteTile(capL, new Vector3(left, rh, bottom), cornerSize);
                PlaceConveyorSpriteTileStretched(hSprite,  new Vector3(hCenter, rh, bottom), hLength, cornerSize);
                PlaceConveyorSpriteTile(spBR, new Vector3(right, rh, bottom), cornerSize);
                PlaceConveyorSpriteTileStretched(vrSprite, new Vector3(right, rh, vCenter), cornerSize, vLength);
                PlaceConveyorSpriteTile(spTR, new Vector3(right, rh, top), cornerSize);
                PlaceConveyorSpriteTileStretched(hSprite,  new Vector3(hCenter, rh, top), hLength, cornerSize);
                PlaceConveyorSpriteTile(capL, new Vector3(left, rh, top), cornerSize);
            }
            else if (sides == 2)
            {
                PlaceConveyorSpriteTile(capL, new Vector3(left, rh, bottom), cornerSize);
                PlaceConveyorSpriteTileStretched(hSprite,  new Vector3(hCenter, rh, bottom), hLength, cornerSize);
                PlaceConveyorSpriteTile(spBR, new Vector3(right, rh, bottom), cornerSize);
                PlaceConveyorSpriteTileStretched(vrSprite, new Vector3(right, rh, vCenter), cornerSize, vLength);
                PlaceConveyorSpriteTile(capT, new Vector3(right, rh, top), cornerSize);
            }
            else
            {
                PlaceConveyorSpriteTile(capL, new Vector3(left, rh, bottom), cornerSize);
                PlaceConveyorSpriteTileStretched(hSprite, new Vector3(hCenter, rh, bottom), hLength, cornerSize);
                PlaceConveyorSpriteTile(capR, new Vector3(right, rh, bottom), cornerSize);
            }

            // Cave: 개방 끝점 위에 터널 오버레이
            if (sides < 4)
            {
                if (sides == 3)
                {
                    PlaceCaveOverlayTile(caveL, new Vector3(left, rh, bottom), cornerSize, sides, 0);
                    PlaceCaveOverlayTile(caveL, new Vector3(left, rh, top), cornerSize, sides, 1);
                }
                else if (sides == 2)
                {
                    PlaceCaveOverlayTile(caveL, new Vector3(left, rh, bottom), cornerSize, sides, 0);
                    PlaceCaveOverlayTile(caveT, new Vector3(right, rh, top), cornerSize, sides, 1);
                }
                else
                {
                    PlaceCaveOverlayTile(caveL, new Vector3(left, rh, bottom), cornerSize, sides, 0);
                    PlaceCaveOverlayTile(caveR, new Vector3(right, rh, bottom), cornerSize, sides, 1);
                }
            }

            // Paint 모드일 때 가이드 그리드 표시
            if (_conveyorPaintMode && _pathGrid != null)
            {
                EnsureSharedMeshes();
                // 두 가지 상태(ON/OFF)에 대해 머티리얼 캐시
                var matOn = MakeLitMaterial(FindLitShader(), new Color(0.3f, 0.3f, 0.6f, 0.5f));
                var matOff = MakeLitMaterial(FindLitShader(), new Color(0.15f, 0.15f, 0.2f, 0.3f));
                int pw = _pathGrid.GetLength(0);
                int ph = _pathGrid.GetLength(1);
                for (int gx = 0; gx < pw; gx++)
                    for (int gy = 0; gy < ph; gy++)
                    {
                        Vector3 wpos = PathGridToWorld(gx, gy);
                        var outline = MakeMeshObj("PG", _sharedQuadMesh, _pathGrid[gx, gy] ? matOn : matOff, _conveyorPreviewRoot);
                        outline.transform.localScale = new Vector3(spacing * 0.98f, spacing * 0.98f, 1f);
                        outline.transform.position = new Vector3(wpos.x, -0.08f, wpos.z);
                        outline.transform.eulerAngles = new Vector3(90f, 0f, 0f);
                    }
            }
        }

        private Sprite GetPathTileSprite_Corner(bool isLeft, bool isBottom)
        {
            if (_railTileSet == null) return null;
            if (isLeft && isBottom) return _railTileSet.tileBL;
            if (!isLeft && isBottom) return _railTileSet.tileBR;
            if (isLeft && !isBottom) return _railTileSet.tileTL;
            return _railTileSet.tileTR;
        }

        private void PlaceConveyorSpriteTileStretched(Sprite sprite, Vector3 position, float worldW, float worldH)
        {
            if (sprite == null) return;

            var go = new GameObject("ConvStretched");
            go.transform.SetParent(_conveyorPreviewRoot, false);
            go.transform.position = new Vector3(position.x, -0.02f, position.z);
            go.transform.eulerAngles = new Vector3(90f, 0f, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = -1;

            float sw = sprite.bounds.size.x;
            float sh = sprite.bounds.size.y;
            float scaleX = sw > 0.001f ? worldW / sw : 1f;
            float scaleY = sh > 0.001f ? worldH / sh : 1f;
            go.transform.localScale = new Vector3(scaleX, scaleY, 1f);
        }

        /// <summary>
        /// Converts extended path grid coordinates to world position.
        /// Grid (PATH_PAD, PATH_PAD) = balloon grid (0,0).
        /// </summary>
        private Vector3 PathGridToWorld(int gx, int gy)
        {
            float spacing = CellSpacing;
            float fieldWidth = _gridCols * spacing;
            float halfFieldX = fieldWidth * 0.5f;
            float halfFieldZ = _gridRows * spacing * 0.5f;
            // Fixed proportion offsets for rail center position
            float railOffsetH = fieldWidth * 0.07f + fieldWidth * 0.30f * 0.5f;
            float railOffsetVTop = fieldWidth * 0.09f + fieldWidth * 0.30f * 0.5f;
            float railOffsetVBottom = fieldWidth * 0.12f + fieldWidth * 0.30f * 0.5f;

            int bx = gx - PATH_PAD; // balloon grid x (-1 = outer left)
            int by = gy - PATH_PAD; // balloon grid y (-1 = outer bottom)

            // For cells inside the balloon grid range, use cellSpacing
            // For cells outside (the rail ring), use fixed proportion offsets
            float wx, wz;

            if (bx < 0)
                wx = _boardCenter.x - halfFieldX - railOffsetH;
            else if (bx >= _gridCols)
                wx = _boardCenter.x + halfFieldX + railOffsetH;
            else
                wx = _boardCenter.x + (bx - (_gridCols - 1) * 0.5f) * spacing;

            if (by < 0)
                wz = _boardCenter.y - halfFieldZ - railOffsetVBottom;
            else if (by >= _gridRows)
                wz = _boardCenter.y + halfFieldZ + railOffsetVTop;
            else
                wz = _boardCenter.y + (by - (_gridRows - 1) * 0.5f) * spacing;

            return new Vector3(wx, _railHeight, wz);
        }

        /// <summary>
        /// Gets the auto-tile sprite for a path grid cell based on its neighbors.
        /// </summary>
        private Sprite GetPathTileSprite(int gx, int gy)
        {
            if (_railTileSet == null) return null;

            int pw = _pathGrid.GetLength(0), ph = _pathGrid.GetLength(1);
            bool hasUp    = (gy + 1 < ph) && _pathGrid[gx, gy + 1];
            bool hasDown  = (gy - 1 >= 0) && _pathGrid[gx, gy - 1];
            bool hasLeft  = (gx - 1 >= 0) && _pathGrid[gx - 1, gy];
            bool hasRight = (gx + 1 < pw) && _pathGrid[gx + 1, gy];

            int midCol = (pw - 1) / 2;

            // Corners: exactly 2 neighbors at right angle
            if (hasRight && hasUp    && !hasLeft && !hasDown) return _railTileSet.tileBL;
            if (hasLeft  && hasUp    && !hasRight && !hasDown) return _railTileSet.tileBR;
            if (hasRight && hasDown  && !hasLeft && !hasUp)   return _railTileSet.tileTL;
            if (hasLeft  && hasDown  && !hasRight && !hasUp)  return _railTileSet.tileTR;

            // Straight segments
            if (hasLeft && hasRight) return _railTileSet.GetH();
            if (hasUp   && hasDown)  return gx <= midCol ? _railTileSet.GetVL() : _railTileSet.GetVR();

            // Single-neighbor fallback
            if (hasLeft || hasRight) return _railTileSet.GetH();
            if (hasUp   || hasDown)  return gx <= midCol ? _railTileSet.GetVL() : _railTileSet.GetVR();

            return _railTileSet.GetH(); // isolated cell default
        }

        /// <summary>
        /// Places tile sprites along the waypoint path (conveyor belt line).
        /// Uses 6 center-aligned tiles: h, v, bl, br, tl, tr.
        /// Direction-based corner detection (not position-based) so it works with any path shape.
        /// Tiles are sized exactly to tileSize with no overlap.
        /// </summary>
        private void PlaceConveyorSpriteTile(Sprite sprite, Vector3 position, float tileSize)
        {
            if (sprite == null) return;

            var tileGO = new GameObject($"ConvTile_{_conveyorPreviewRoot.childCount}");
            tileGO.transform.SetParent(_conveyorPreviewRoot, false);
            tileGO.transform.position = new Vector3(position.x, -0.02f, position.z);
            tileGO.transform.eulerAngles = new Vector3(90f, 0f, 0f);

            var sr = tileGO.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = -1;

            float spriteWidth = sprite.bounds.size.x;
            float spriteHeight = sprite.bounds.size.y;
            if (spriteWidth > 0.001f && spriteHeight > 0.001f)
            {
                float scaleX = tileSize / spriteWidth;
                float scaleY = tileSize / spriteHeight;
                tileGO.transform.localScale = new Vector3(scaleX, scaleY, 1f);
            }
        }

        private void PlaceCaveOverlayTile(Sprite sprite, Vector3 position, float tileSize, int sides, int tunnelIndex)
        {
            if (sprite == null) return;

            var go = new GameObject($"CaveTile_{_conveyorPreviewRoot.childCount}");
            go.transform.SetParent(_conveyorPreviewRoot, false);
            go.transform.position = new Vector3(position.x, CAVE_OVERLAY_Y, GetCaveOverlayZ(sides, tunnelIndex, position.z));
            go.transform.eulerAngles = new Vector3(90f, 0f, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 1; // Arrow(0)보다 위

            float sw = sprite.bounds.size.x;
            float sh = sprite.bounds.size.y;
            if (sw > 0.001f && sh > 0.001f)
                go.transform.localScale = new Vector3(tileSize / sw, tileSize / sh, 1f);
        }

        private static float GetCaveOverlayZ(int sides, int tunnelIndex, float fallbackZ)
        {
            if (tunnelIndex == 0) return CAVE_BOTTOM_Z;
            if (sides == 1) return CAVE_BOTTOM_Z;
            if (sides == 2) return CAVE_TOP_Z_2_SIDES;
            if (sides == 3) return CAVE_TOP_Z_3_SIDES;
            return fallbackZ;
        }

        /// <summary>
        /// Auto-generate a rectangular ring path in the outer border of the extended grid.
        /// </summary>
        private void AutoConveyorRing()
        {
            int pw = _gridCols + PATH_PAD * 2;
            int ph = _gridRows + PATH_PAD * 2;
            _pathGrid = new bool[pw, ph];

            // Ring on the outermost row/col of the extended grid (index 0 and max)
            for (int c = 0; c < pw; c++)
            {
                _pathGrid[c, 0] = true;
                _pathGrid[c, ph - 1] = true;
            }
            for (int r = 1; r < ph - 1; r++)
            {
                _pathGrid[0, r] = true;
                _pathGrid[pw - 1, r] = true;
            }

            GenerateWaypointsFromPathGrid();
        }

        /// <summary>
        /// Traces the path grid to generate an ordered list of waypoints.
        /// Finds connected loop, extracts corner positions as waypoints.
        /// </summary>
        private void GenerateWaypointsFromPathGrid()
        {
            _customWaypoints.Clear();

            if (_pathGrid == null) return;
            int pw = _pathGrid.GetLength(0);
            int ph = _pathGrid.GetLength(1);

            // Find all path cells
            var pathCells = new List<Vector2Int>();
            for (int x = 0; x < pw; x++)
                for (int y = 0; y < ph; y++)
                    if (_pathGrid[x, y]) pathCells.Add(new Vector2Int(x, y));

            if (pathCells.Count < 3) return;

            // Trace ordered loop via neighbor-following
            var ordered = TracePathLoop(pathCells);
            if (ordered.Count < 3) return;

            // Extract corner waypoints (where direction changes)
            for (int i = 0; i < ordered.Count; i++)
            {
                int prev = (i - 1 + ordered.Count) % ordered.Count;
                int next = (i + 1) % ordered.Count;

                Vector2Int dp = ordered[i] - ordered[prev];
                Vector2Int dn = ordered[next] - ordered[i];

                // Corner = direction changes
                if (dp.x != dn.x || dp.y != dn.y)
                {
                    Vector3 wpos = PathGridToWorld(ordered[i].x, ordered[i].y);
                    _customWaypoints.Add(wpos);
                }
            }

            // If no corners detected (e.g., straight line), use all cells
            if (_customWaypoints.Count < 2)
            {
                _customWaypoints.Clear();
                for (int i = 0; i < ordered.Count; i++)
                {
                    Vector3 wpos = PathGridToWorld(ordered[i].x, ordered[i].y);
                    _customWaypoints.Add(wpos);
                }
            }
        }

        /// <summary>
        /// Traces path grid cells into an ordered loop by following neighbors.
        /// Each cell should have exactly 2 neighbors for a valid loop.
        /// </summary>
        private List<Vector2Int> TracePathLoop(List<Vector2Int> cells)
        {
            if (cells.Count == 0) return new List<Vector2Int>();

            int pw = _pathGrid.GetLength(0);
            int ph = _pathGrid.GetLength(1);

            // Build lookup set
            var cellSet = new HashSet<Vector2Int>(cells);
            var ordered = new List<Vector2Int>();
            var visited = new HashSet<Vector2Int>();

            // Start from first cell
            Vector2Int current = cells[0];
            Vector2Int previous = new Vector2Int(-999, -999);

            for (int safety = 0; safety < cells.Count + 1; safety++)
            {
                if (visited.Contains(current)) break;
                ordered.Add(current);
                visited.Add(current);

                // Find unvisited neighbor (4-directional)
                Vector2Int[] dirs = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
                Vector2Int next = new Vector2Int(-1, -1);
                bool found = false;

                foreach (var d in dirs)
                {
                    Vector2Int n = current + d;
                    if (n.x >= 0 && n.x < pw && n.y >= 0 && n.y < ph
                        && cellSet.Contains(n) && !visited.Contains(n))
                    {
                        next = n;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    // Check if we can close the loop back to start
                    foreach (var d in dirs)
                    {
                        Vector2Int n = current + d;
                        if (n == cells[0] && ordered.Count >= 3) break;
                    }
                    break;
                }

                previous = current;
                current = next;
            }

            return ordered;
        }

        #endregion

        #region Waypoint Preview

        private void EnsureSharedMeshes()
        {
            if (_sharedSphereMesh == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _sharedSphereMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(tmp);
            }
            if (_sharedCubeMesh == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _sharedCubeMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(tmp);
            }
        }

        private GameObject MakeMeshObj(string name, Mesh mesh, Material mat, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private void RebuildWaypointPreview()
        {
            if (_waypointPreviewRoot) Destroy(_waypointPreviewRoot.gameObject);
            _waypointPreviewRoot = new GameObject("WaypointPreview").transform;

            if (_customWaypoints.Count == 0) return;

            EnsureSharedMeshes();

            // Draw small spheres at each waypoint
            for (int i = 0; i < _customWaypoints.Count; i++)
            {
                var s = MakeMeshObj("WP", _sharedSphereMesh, _waypointMat, _waypointPreviewRoot);
                s.transform.position = _customWaypoints[i] + Vector3.up * 0.3f;
                s.transform.localScale = Vector3.one * 0.2f;

                // Number label
                var labelGO = new GameObject("WPLabel");
                labelGO.transform.SetParent(_waypointPreviewRoot, false);
                labelGO.transform.position = _customWaypoints[i] + Vector3.up * 0.8f;
                labelGO.transform.eulerAngles = new Vector3(90f, 0f, 0f);
                var tm = labelGO.AddComponent<TextMesh>();
                tm.text = i.ToString();
                tm.fontSize = 24; tm.characterSize = 0.2f;
                tm.alignment = TextAlignment.Center; tm.anchor = TextAnchor.MiddleCenter;
                tm.color = Color.white;
            }

            // Draw line connecting waypoints
            if (_customWaypoints.Count >= 2)
            {
                var lineGO = new GameObject("WPLine");
                lineGO.transform.SetParent(_waypointPreviewRoot, false);
                var lr = lineGO.AddComponent<LineRenderer>();
                lr.material = _waypointLineMat;
                float lineWidth = _smoothCorners ? 0.04f : 0.08f;
                lr.startWidth = lineWidth; lr.endWidth = lineWidth;
                lr.numCapVertices = 0;
                lr.loop = true;
                lr.positionCount = _customWaypoints.Count;
                for (int i = 0; i < _customWaypoints.Count; i++)
                    lr.SetPosition(i, _customWaypoints[i] + Vector3.up * 0.15f);

                // Smoothed path preview (thicker, different color)
                if (_smoothCorners && _customWaypoints.Count >= 3)
                {
                    var smoothPoints = BuildSmoothedPreviewPath();
                    if (smoothPoints.Count >= 2)
                    {
                        var smoothLineGO = new GameObject("SmoothLine");
                        smoothLineGO.transform.SetParent(_waypointPreviewRoot, false);
                        var smoothLR = smoothLineGO.AddComponent<LineRenderer>();
                        smoothLR.material = MakeLitMaterial(FindLitShader(), new Color(0.2f, 0.8f, 1f));
                        smoothLR.startWidth = 0.1f; smoothLR.endWidth = 0.1f;
                        smoothLR.numCapVertices = 0;
                        smoothLR.loop = true;
                        smoothLR.positionCount = smoothPoints.Count;
                        for (int i = 0; i < smoothPoints.Count; i++)
                            smoothLR.SetPosition(i, smoothPoints[i] + Vector3.up * 0.15f);
                    }
                }

                // Direction arrows
                var arrowMat = MakeLitMaterial(FindLitShader(), new Color(1f, 0.5f, 0f));
                for (int i = 0; i < _customWaypoints.Count; i++)
                {
                    int next = (i + 1) % _customWaypoints.Count;
                    Vector3 from = _customWaypoints[i];
                    Vector3 to = _customWaypoints[next];
                    Vector3 mid = (from + to) * 0.5f + Vector3.up * 0.15f;
                    Vector3 dir = (to - from).normalized;

                    var arrow = MakeMeshObj("Arrow", _sharedCubeMesh, arrowMat, _waypointPreviewRoot);
                    arrow.transform.position = mid;
                    arrow.transform.localScale = new Vector3(0.06f, 0.06f, 0.25f);
                    if (dir.sqrMagnitude > 0.001f)
                        arrow.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

                    var head = MakeMeshObj("Head", _sharedCubeMesh, arrowMat, _waypointPreviewRoot);
                    head.transform.position = mid + dir * 0.15f;
                    head.transform.localScale = new Vector3(0.18f, 0.06f, 0.12f);
                    if (dir.sqrMagnitude > 0.001f)
                        head.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                }
            }
        }

        /// <summary>
        /// Builds a smoothed path preview using the same quadratic Bezier algorithm as RailManager.
        /// </summary>
        private List<Vector3> BuildSmoothedPreviewPath()
        {
            var result = new List<Vector3>();
            int wpCount = _customWaypoints.Count;
            if (wpCount < 3) { result.AddRange(_customWaypoints); return result; }

            const int SUBS = 8;

            for (int i = 0; i < wpCount; i++)
            {
                int prev = (i - 1 + wpCount) % wpCount;
                int next = (i + 1) % wpCount;

                Vector3 dirIn = (_customWaypoints[i] - _customWaypoints[prev]).normalized;
                Vector3 dirOut = (_customWaypoints[next] - _customWaypoints[i]).normalized;
                float dot = Vector3.Dot(dirIn, dirOut);

                if (dot > 0.95f)
                {
                    result.Add(_customWaypoints[i]);
                    continue;
                }

                float distPrev = Vector3.Distance(_customWaypoints[i], _customWaypoints[prev]);
                float distNext = Vector3.Distance(_customWaypoints[i], _customWaypoints[next]);
                float maxR = Mathf.Min(distPrev * 0.45f, distNext * 0.45f);
                float r = Mathf.Min(_cornerRadius, maxR);
                if (r < 0.01f) { result.Add(_customWaypoints[i]); continue; }

                Vector3 tIn = _customWaypoints[i] - dirIn * r;
                Vector3 tOut = _customWaypoints[i] + dirOut * r;

                for (int s = 0; s <= SUBS; s++)
                {
                    float t = (float)s / SUBS;
                    float u = 1f - t;
                    result.Add(u * u * tIn + 2f * u * t * _customWaypoints[i] + t * t * tOut);
                }
            }
            return result;
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        //  INPUT
        // ═══════════════════════════════════════════════════════════════

        #region Paint Input

        private bool _conveyorClickConsumed;
        private bool _blockRightMousePanUntilRelease;
        // [perf] 좌클릭 hold 페인트 시 같은 셀 중복 처리 방지용 마지막 페인트 셀.
        // 가드 없으면 같은 셀에 들고만 있어도 매 프레임 RefreshInfo(딕셔너리 3개 alloc + 전체 그리드 스캔)
        // 가 돌아 MapMaker EditorLoop 스파이크 발생.
        private int _lastPaintCol = -1;
        private int _lastPaintRow = -1;

        private void HandlePaintInput()
        {
            if (_cam == null) return;

            var mouse = Mouse.current;
            if (mouse == null) return;
            if (!mouse.rightButton.isPressed)
                _blockRightMousePanUntilRelease = false;
            // 좌클릭 해제 시 마지막 페인트 셀 리셋 → 다음 press 는 같은 셀이라도 다시 페인트.
            if (!mouse.leftButton.isPressed)
            {
                _lastPaintCol = -1;
                _lastPaintRow = -1;
            }
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            if (_conveyorPaintMode)
            {
                if (!mouse.leftButton.wasPressedThisFrame) { _conveyorClickConsumed = false; return; }
                if (_conveyorClickConsumed) return;
            }
            else
            {
                if (!mouse.leftButton.isPressed && !mouse.rightButton.wasPressedThisFrame) return;
            }


            Vector3 hit;
            if (!RaycastToGround(mouse, out hit)) return;

            float spacing = CellSpacing;
            bool leftDown = mouse.leftButton.wasPressedThisFrame;
            bool rightDown = mouse.rightButton.wasPressedThisFrame;

            if (_conveyorPaintMode)
            {
                // Convert world hit to extended path grid coordinates
                // Path grid (PATH_PAD, PATH_PAD) = balloon grid (0,0)
                int bx = Mathf.RoundToInt((hit.x - _boardCenter.x) / spacing + (_gridCols - 1) * 0.5f);
                int by = Mathf.RoundToInt((hit.z - _boardCenter.y) / spacing + (_gridRows - 1) * 0.5f);
                int gx = bx + PATH_PAD;
                int gy = by + PATH_PAD;

                int pw = _pathGrid.GetLength(0);
                int ph = _pathGrid.GetLength(1);

                if (gx >= 0 && gx < pw && gy >= 0 && gy < ph)
                {
                    _pathGrid[gx, gy] = !_pathGrid[gx, gy];

                    // If toggling ON inside balloon grid, remove the balloon at that cell
                    if (_pathGrid[gx, gy] && bx >= 0 && bx < _gridCols && by >= 0 && by < _gridRows)
                    {
                        _balloonColors[bx, by] = -1;
                        _balloonGimmicks[bx, by] = 0;
                        UpdatePreviewCell(bx, by);
                    }

                    GenerateWaypointsFromPathGrid();
                    RebuildConveyorPreview();
                    RebuildWaypointPreview();
                    _conveyorClickConsumed = true;
                    _infoDirty = true;
                }
            }
            else
            {
                // Balloon paint mode
                float colFloat = (hit.x - _boardCenter.x) / spacing + (_gridCols - 1) * 0.5f;
                float rowFloat = (hit.z - _boardCenter.y) / spacing + (_gridRows - 1) * 0.5f;
                int col = Mathf.RoundToInt(colFloat);
                int row = Mathf.RoundToInt(rowFloat);

                if (col >= 0 && col < _gridCols && row >= 0 && row < _gridRows)
                {
                    if (HandleBalloonGridShortcut(col, row, colFloat, rowFloat, leftDown, rightDown))
                        return;

                    // [perf] 좌클릭 hold 중 같은 셀이면(드래그로 셀이 안 바뀜) 중복 paint + 매 프레임
                    //   RefreshInfo(전체 그리드 스캔 + GC)를 스킵 → MapMaker EditorLoop 스파이크 제거.
                    //   fresh click(leftDown/rightDown)은 항상 통과.
                    if (!leftDown && !rightDown && col == _lastPaintCol && row == _lastPaintRow)
                        return;
                    _lastPaintCol = col;
                    _lastPaintRow = row;

                    if (rightDown)
                    {
                        EraseBalloonCell(col, row);
                        UpdatePreviewCell(col, row);
                        _blockRightMousePanUntilRelease = true;
                        _infoDirty = true;
                        SetStatus($"Erased cell ({col}, {row})");
                        return;
                    }

                    // 필드 기믹만 모드 — 기존 풍선 색상 유지하고 기믹만 추가 (Surprise/Ice 등).
                    // 빈 셀(색상 없음)은 무시. sized/FlexTube 기믹은 단순 set (anchor 1×1).
                    if (_fieldGimmickEraseMode && mouse.leftButton.isPressed)
                    {
                        ClearFieldGimmickAt(col, row);
                        _infoDirty = true;
                        SetStatus($"Field gimmick removed at ({col}, {row})");
                        return;
                    }

                    if (_fieldGimmickOnlyMode && mouse.leftButton.isPressed)
                    {
                        bool isIceGimmickBrush = _paintGimmick > 0 && _paintGimmick < FIELD_GIMMICK_NAMES.Length
                            && FIELD_GIMMICK_NAMES[_paintGimmick] == "Ice";
                        if (isIceGimmickBrush && _paintWallSize > 1)
                        {
                            int b = Mathf.Max(2, _paintWallSize);
                            if (_balloonColors[col, row] < 0)
                            {
                                if (leftDown) SetStatus($"Ice {b}x{b}: anchor cell has no balloon ({col},{row})");
                                return;
                            }

                            // ROLLBACK_ICE_OVERLAY_UNDER_BALLOONS_20260626:
                            // Ice 2x2/3x3 keeps every footprint balloon as real data, but runtime
                            // groups them by iceBlockSize and renders one combined Ice overlay.
                            // ROLLBACK_ICE_GIMMICKONLY_KEEPCOLOR_20260628:
                            // 기믹만 모드 — 기존 풍선 색을 '그대로' 유지하고 빈 셀엔 새 풍선을 만들지 않는다.
                            // 기존엔 2×2 전체를 anchorColor 로 덮어써, 빈 셀/다른 색 셀까지 색이 칠해졌다(의도 위반).
                            for (int dx = 0; dx < b; dx++)
                                for (int dy = 0; dy < b; dy++)
                                {
                                    int cx = col + dx, cy = row + dy;
                                    if (cx < 0 || cx >= _gridCols || cy < 0 || cy >= _gridRows) continue;
                                    if (_balloonColors[cx, cy] < 0) continue; // 빈 셀: 새 풍선 만들지 않음(색 미입힘)
                                    // 색은 건드리지 않고 Ice 기믹/블록 메타만 입힌다.
                                    _balloonGimmicks[cx, cy] = _paintGimmick;
                                    _balloonGimmickHP[cx, cy] = _paintPinataHP;
                                    _balloonIceBlockSize[cx, cy] = b;
                                    ApplyIceGroupBrushMeta(cx, cy, true);
                                    _balloonPinataW[cx, cy] = 1;
                                    _balloonPinataH[cx, cy] = 1;
                                    _balloonLockPairIds[cx, cy] = -1;
                                    _balloonFlexTubeGroupId[cx, cy] = -1;
                                    _balloonFlexTubeSequenceIndex[cx, cy] = -1;
                                    UpdatePreviewCell(cx, cy);
                                }

                            _infoDirty = true;
                            SetStatus($"Ice {b}x{b}: authored as {b * b} underlying balloons with one overlay at ({col},{row})");
                            return;
                        }

                        if (_balloonColors[col, row] >= 0)
                        {
                            _balloonGimmicks[col, row] = _paintGimmick;
                            _balloonGimmickHP[col, row] = _paintPinataHP;
                            _balloonPinataW[col, row] = 1;
                            _balloonPinataH[col, row] = 1;
                            _balloonIceBlockSize[col, row] = isIceGimmickBrush ? Mathf.Max(1, _paintWallSize) : 1;
                            ApplyIceGroupBrushMeta(col, row, isIceGimmickBrush);
                            _balloonLockPairIds[col, row] = -1;
                            _balloonFlexTubeGroupId[col, row] = -1;
                            _balloonFlexTubeSequenceIndex[col, row] = -1;
                            UpdatePreviewCell(col, row);
                            _infoDirty = true;
                        }
                        else if (leftDown)
                        {
                            SetStatus($"빈 셀 — 기믹만 추가는 색상 있는 셀에만 ({col},{row})");
                        }
                        return;
                    }

                    if (_eraseNeighborMode && leftDown)
                    {
                        EraseNeighborSameColor(col, row);
                        _eraseNeighborMode = false;
                        OnBalloonGridChanged();
                    }
                    else if (_fillNeighborMode && leftDown)
                    {
                        FillNeighborSameColor(col, row, _paintColor);
                        _fillNeighborMode = false;
                        OnBalloonGridChanged();
                    }
                    else if (_floodFillMode && leftDown)
                    {
                        FloodFill(col, row, _paintColor);
                        OnBalloonGridChanged();
                    }
                    else if (!_floodFillMode && !_eraseNeighborMode && !_fillNeighborMode)
                    {
                        bool isSizedFieldGimmick = _paintGimmick > 0 && _paintGimmick < FIELD_GIMMICK_NAMES.Length
                            && IsSizedFieldGimmick(FIELD_GIMMICK_NAMES[_paintGimmick]);
                        bool isFlexTubeGimmick = _paintGimmick > 0 && _paintGimmick < FIELD_GIMMICK_NAMES.Length
                            && FIELD_GIMMICK_NAMES[_paintGimmick] == "FlexTube";
                        // [Ice §11] Ice + blockSize(Wall-size row 재사용)>1 → B×B footprint 를 모두 개별 ice 셀로 채움.
                        // 각 셀 아래 풍선 은닉(흡수 X), 런타임은 인접 영역을 blockSize 블록으로 묶어 1개 오버레이로 병합 렌더.
                        bool isIceBlockGimmick = _paintGimmick > 0 && _paintGimmick < FIELD_GIMMICK_NAMES.Length
                            && FIELD_GIMMICK_NAMES[_paintGimmick] == "Ice" && _paintWallSize > 1;
                        // ROLLBACK_BARRICADE_MAPMAKER_FOOTPRINT_20260608: 바리케이드는 dir+length 로 length×2 footprint 채움.
                        bool isBarricadeGimmick = _paintGimmick > 0 && _paintGimmick < FIELD_GIMMICK_NAMES.Length
                            && FIELD_GIMMICK_NAMES[_paintGimmick] == "Barricade";
                        // ROLLBACK_IRONWALL_NOCOLOR_20260626: Wall(IronWall)은 색·공격 없는 불파괴 기믹 → 전용 분기에서
                        //   _paintColor 게이트 없이(기믹만 클릭) 배치. (Wall 도 IsSizedFieldGimmick 이라 아래 sized 분기보다
                        //   먼저 와야 함 — 그 분기는 _paintColor 를 요구해 Wall 에 색을 입히던 원인.)
                        bool isWallGimmick = _paintGimmick > 0 && _paintGimmick < FIELD_GIMMICK_NAMES.Length
                            && FIELD_GIMMICK_NAMES[_paintGimmick] == "Wall";

                        if (isFlexTubeGimmick && _paintColor >= 0)
                        {
                            // FlexTube 모드 — wasPressedThisFrame 일 때만 paint, hold/drag 프레임은 no-op.
                            // 일반 paint 분기로 절대 fallback 하지 않음 (fallback 시 clear 코드가 데이터 삭제).
                            if (mouse.leftButton.wasPressedThisFrame
                                && _balloonFlexTubeGroupId[col, row] != _paintFlexTubeGroupId)
                            {
                                int fseq = _flexTubePaintOrder.Count;

                                // ROLLBACK_FLEXTUBE_INPUT_ORDER_20260626:
                                // Authoring order is fixed: first click = Head(seq0), middle clicks = Segment,
                                // last/highest seq = Edge. Do not auto-create seq1 on the first click.
                                // ROLLBACK_FLEXTUBE_2X2_PARTS_20260626:
                                // 클릭 1회 = FlexTube 파트 1개 = 2×2 footprint (Barricade 처럼 두께 2 + 경로 방향으로도 2칸).
                                // 클릭 순서가 역할: seq0=Head, 중간=Segment, 마지막=Edge (역할은 직렬화 시 max seq 로 결정).
                                // 같은 seq 의 4셀은 런타임 BuildFlexTubes 가 한 centerline 점(2×2 중심)으로 평균 → 튜브가
                                // 파트 중심들을 따라 2칸 두께로 렌더되고 코너는 Bezier 로 자연스럽게 휜다. 두께 방향 추측/코너
                                // 특례 불필요(정사각 2×2 라 방향 무관). 프리뷰도 셀 4개가 칠해져 2×2 로 보인다.
                                PaintFlexTube2x2(col, row, fseq);
                                UpdateFlexTubeStatusText();
                                _infoDirty = true;
                            }
                        }
                        else if (isIceBlockGimmick && _paintColor >= 0)
                        {
                            // ROLLBACK_ICE_OVERLAY_UNDER_BALLOONS_20260626:
                            // Ice 2x2/3x3 is saved as 4/9 real underlying balloons. Runtime hides them
                            // under one FrozenLayer overlay until the Ice region breaks.
                            int b = Mathf.Max(2, _paintWallSize);
                            for (int dx = 0; dx < b; dx++)
                                for (int dy = 0; dy < b; dy++)
                                {
                                    int cx = col + dx, cy = row + dy;
                                    if (cx < 0 || cx >= _gridCols || cy < 0 || cy >= _gridRows) continue;
                                    _balloonColors[cx, cy] = _paintColor;
                                    _balloonGimmicks[cx, cy] = _paintGimmick;
                                    _balloonGimmickHP[cx, cy] = _paintPinataHP;
                                    _balloonIceBlockSize[cx, cy] = b;
                                    ApplyIceGroupBrushMeta(cx, cy, true);
                                    _balloonPinataW[cx, cy] = 1;
                                    _balloonPinataH[cx, cy] = 1;
                                    _balloonLockPairIds[cx, cy] = -1;
                                    _balloonFlexTubeGroupId[cx, cy] = -1;
                                    _balloonFlexTubeSequenceIndex[cx, cy] = -1;
                                    UpdatePreviewCell(cx, cy);
                                }
                            _infoDirty = true;
                        }
                        else if (isBarricadeGimmick && _paintColor >= 0)
                        {
                            // ROLLBACK_BARRICADE_MAPMAKER_FOOTPRINT_20260608:
                            // footprint = 진행축 length칸 × 두께 2칸. anchor(col,row) 에서 dir 로 뻗음.
                            // 비앵커 셀은 색+기믹 채워 GUI 에 영역 표시, sizeW=0 → emit 스킵(앵커만 저장). 런타임은 dir+length 로 계산.
                            int blen = Mathf.Max(3, _paintBarricadeLength);
                            int bdir = ((_paintBarricadeDir % 4) + 4) % 4; // 0=N(+row) 1=E(+col) 2=S(-row) 3=W(-col)
                            int aCol = (bdir == 1) ? 1 : (bdir == 3) ? -1 : 0; // 진행축 col 델타(E/W)
                            int aRow = (bdir == 0) ? 1 : (bdir == 2) ? -1 : 0; // 진행축 row 델타(N/S)
                            int pCol = (bdir == 0 || bdir == 2) ? 1 : 0;       // N/S 는 두께가 col
                            int pRow = (bdir == 1 || bdir == 3) ? 1 : 0;       // E/W 는 두께가 row
                            for (int a = 0; a < blen; a++)
                                for (int p = 0; p < 2; p++)
                                {
                                    int cx = col + aCol * a + pCol * p;
                                    int cy = row + aRow * a + pRow * p;
                                    if (cx < 0 || cx >= _gridCols || cy < 0 || cy >= _gridRows) continue;
                                    bool isAnchor = (a == 0 && p == 0);
                                    _balloonColors[cx, cy] = _paintColor;
                                    _balloonGimmicks[cx, cy] = _paintGimmick;
                                    _balloonGimmickHP[cx, cy] = _paintPinataHP;
                                    _balloonPinataW[cx, cy] = isAnchor ? 1 : 0; // 비앵커=0 → emit 스킵
                                    _balloonPinataH[cx, cy] = isAnchor ? 1 : 0;
                                    _balloonBarricadeDir[cx, cy] = bdir;
                                    _balloonBarricadeLength[cx, cy] = blen;
                                    _balloonLockPairIds[cx, cy] = -1;
                                    ApplyIceGroupBrushMeta(cx, cy, false);
                                    _balloonFlexTubeGroupId[cx, cy] = -1;
                                    _balloonFlexTubeSequenceIndex[cx, cy] = -1;
                                    UpdatePreviewCell(cx, cy);
                                }
                            _infoDirty = true;
                        }
                        else if (isWallGimmick)
                        {
                            // ROLLBACK_IRONWALL_NOCOLOR_20260626 / ROLLBACK_IRONWALL_COLOR_SENTINEL_MINUS1_20260630:
                            //   Wall = 색·공격 없는 불파괴. 에디터 내부 _balloonColors 는 0(센티넬) 유지 — 에디터가
                            //   color<0 셀을 '빈 칸' 으로 보므로(save 스킵·렌더·밸런스 등 다수) 내부는 반드시 >=0 이어야 함.
                            //   '저장 포맷' 만 -1(무색) 로 기록하고, 로드 시 -1→0 으로 환원(BuildLevelConfig / config 로드부).
                            //   렌더는 GetPreviewColor 가 Wall 을 항상 GIMMICK_WALL_COLOR(iron-gray), 밸런스는 색 카운트 제외.
                            int wsize = Mathf.Clamp(_paintWallSize, 1, 3);
                            for (int dx = 0; dx < wsize; dx++)
                                for (int dy = 0; dy < wsize; dy++)
                                {
                                    int cx = col + dx, cy = row + dy;
                                    if (cx < 0 || cx >= _gridCols || cy < 0 || cy >= _gridRows) continue;
                                    _balloonColors[cx, cy] = 0;            // 센티넬(>=0 저장용). 렌더/카운트는 색 무시.
                                    _balloonGimmicks[cx, cy] = _paintGimmick;
                                    _balloonGimmickHP[cx, cy] = 0;          // 불파괴 — HP 무의미
                                    _balloonPinataW[cx, cy] = 0;            // 비앵커 표시(앵커는 아래에서 size)
                                    _balloonPinataH[cx, cy] = 0;
                                    _balloonLockPairIds[cx, cy] = -1;
                                    ApplyIceGroupBrushMeta(cx, cy, false);
                                    _balloonFlexTubeGroupId[cx, cy] = -1;
                                    _balloonFlexTubeSequenceIndex[cx, cy] = -1;
                                    UpdatePreviewCell(cx, cy);
                                }
                            _balloonPinataW[col, row] = wsize;  // 앵커에만 사이즈 저장(save 가 앵커만 emit)
                            _balloonPinataH[col, row] = wsize;
                            _infoDirty = true;
                        }
                        else if (isSizedFieldGimmick && _paintColor >= 0)
                        {
                            // ROLLBACK_IRONWALL_NOCOLOR_20260626: Wall 은 위 isWallGimmick 분기에서 선처리되므로 여기 도달 안 함.
                            //   (아래 isWallSized 잔재는 비-Wall sized 기믹에는 영향 없는 dead 분기.)
                            // Wall 은 정사각 _paintWallSize(1/2/3) 사용 — Pinata 자유 W×H 와 분리해 carryover 방지.
                            bool isWallSized = FIELD_GIMMICK_NAMES[_paintGimmick] == "Wall";
                            int pw = isWallSized ? _paintWallSize : _paintPinataW;
                            int ph = isWallSized ? _paintWallSize : _paintPinataH;
                            // 범위 내 셀에 같은 색 + Piñata 기믹으로 채움 (프리뷰에서 영역 표시)
                            for (int dx = 0; dx < pw; dx++)
                                for (int dy = 0; dy < ph; dy++)
                                {
                                    int cx = col + dx, cy = row + dy;
                                    if (cx < 0 || cx >= _gridCols || cy < 0 || cy >= _gridRows) continue;
                                    _balloonColors[cx, cy] = _paintColor;
                                    _balloonGimmicks[cx, cy] = _paintGimmick;
                                    _balloonGimmickHP[cx, cy] = _paintPinataHP;
                                    _balloonPinataW[cx, cy] = 0; // 비앵커 셀: sizeW=0 (앵커 아님 표시)
                                    _balloonPinataH[cx, cy] = 0;
                                    ApplyIceGroupBrushMeta(cx, cy, false);
                                    // 잔존 FlexTube 데이터 클리어 — 다른 gimmick 으로 덮어쓰는 cell.
                                    _balloonFlexTubeGroupId[cx, cy] = -1;
                                    _balloonFlexTubeSequenceIndex[cx, cy] = -1;
                                    UpdatePreviewCell(cx, cy);
                                }
                            // 앵커 셀에만 사이즈 저장
                            _balloonPinataW[col, row] = pw;
                            _balloonPinataH[col, row] = ph;

                            // Target Box: 패널에서 구성한 알 리스트를 이 박스 anchor 에 저장 (footprint 와 분리).
                            // 리스트가 비어있으면 현재 색 1개로 기본.
                            if (FIELD_GIMMICK_NAMES[_paintGimmick] == "Pinata_Box")
                            {
                                var key = new Vector2Int(col, row);
                                if (_boxEggColors.Count > 0)
                                {
                                    _boxEggConfigColors[key] = _boxEggColors.ToArray();
                                    _boxEggConfigHps[key] = _boxEggHps.ToArray();
                                }
                                else
                                {
                                    // 명시 리스트 없으면 footprint W×H 만큼 현재 색/HP 로 자동 채움 (각 알 = 풍선 1칸).
                                    int cellCount = Mathf.Max(1, pw * ph);
                                    var fillColors = new int[cellCount];
                                    var fillHps = new int[cellCount];
                                    for (int i = 0; i < cellCount; i++)
                                    {
                                        fillColors[i] = _paintColor;
                                        fillHps[i] = Mathf.Max(1, _paintPinataHP);
                                    }
                                    _boxEggConfigColors[key] = fillColors;
                                    _boxEggConfigHps[key] = fillHps;
                                }
                            }

                            UpdatePreviewCell(col, row);
                            _infoDirty = true;
                        }
                        else
                        {
                            _balloonColors[col, row] = _paintColor;
                            int gimmickToSet = _paintColor >= 0 ? _paintGimmick : 0;
                            _balloonGimmicks[col, row] = gimmickToSet;
                            _balloonGimmickHP[col, row] = _paintPinataHP;
                            _balloonPinataW[col, row] = 1;
                            _balloonPinataH[col, row] = 1;
                            // [Ice §11] Ice 셀 blockSize = wall-size 브러시(1/2/3) 재사용. 그 외 기믹은 1.
                            bool isIceGimmick = gimmickToSet > 0 && gimmickToSet < FIELD_GIMMICK_NAMES.Length
                                && FIELD_GIMMICK_NAMES[gimmickToSet] == "Ice";
                            _balloonIceBlockSize[col, row] = isIceGimmick ? Mathf.Max(1, _paintWallSize) : 1;
                            ApplyIceGroupBrushMeta(col, row, isIceGimmick);
                            // ROLLBACK_BARRICADE_MAPMAKER_20260608: 이 단일셀 경로는 바리케이드 미해당(footprint 분기에서 처리) — 기본값(1/1) 으로 정리.
                            _balloonBarricadeDir[col, row] = 1;
                            _balloonBarricadeLength[col, row] = 1;
                            // Lock_Key pairId
                            bool isLockKeyGimmick = gimmickToSet > 0 && gimmickToSet < FIELD_GIMMICK_NAMES.Length
                                && FIELD_GIMMICK_NAMES[gimmickToSet] == "Lock_Key";
                            _balloonLockPairIds[col, row] = isLockKeyGimmick ? _paintLockPairId : -1;
                            // 잔존 FlexTube 데이터 클리어 — erase / 일반 색 / 다른 gimmick paint 모두 이 경로.
                            _balloonFlexTubeGroupId[col, row] = -1;
                            _balloonFlexTubeSequenceIndex[col, row] = -1;
                            UpdatePreviewCell(col, row);
                            _infoDirty = true;
                        }
                    }
                }
            }
        }

        private bool HandleBalloonGridShortcut(int col, int row, float colFloat, float rowFloat, bool leftDown, bool rightDown)
        {
            if (!leftDown && !rightDown) return false;

            var kb = Keyboard.current;
            if (kb == null) return false;

            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;

            if (kb[Key.R].isPressed)
            {
                if (rightDown)
                {
                    _blockRightMousePanUntilRelease = true;
                    DeleteRow(row);
                }
                else
                {
                    int insertAt = GetClosestInsertIndex(rowFloat, _gridRows);
                    InsertRow(insertAt, true);
                }
                return true;
            }

            if (kb[Key.T].isPressed)
            {
                if (rightDown)
                {
                    _blockRightMousePanUntilRelease = true;
                    DeleteCol(col);
                }
                else
                {
                    int insertAt = GetClosestInsertIndex(colFloat, _gridCols);
                    InsertCol(insertAt, true);
                }
                return true;
            }

            if (kb[Key.F].isPressed)
            {
                if (shift)
                {
                    if (rightDown)
                        _blockRightMousePanUntilRelease = true;
                    ReplaceClickedColorWithBrush(col, row);
                    return true;
                }

                if (leftDown)
                {
                    FillNeighborSameColor(col, row, _paintColor);
                    OnBalloonGridChanged();
                    return true;
                }

                if (rightDown)
                {
                    _blockRightMousePanUntilRelease = true;
                    EraseNeighborSameColor(col, row);
                    OnBalloonGridChanged();
                    return true;
                }
            }

            return false;
        }

        private static int GetClosestInsertIndex(float cellFloat, int cellCount)
        {
            return Mathf.Clamp(Mathf.FloorToInt(cellFloat + 0.5f), 0, cellCount);
        }

        private bool RaycastToGround(Mouse mouse, out Vector3 hit)
        {
            hit = Vector3.zero;
            Ray ray = _cam.ScreenPointToRay(mouse.position.ReadValue());
            if (Mathf.Abs(ray.direction.y) < 0.001f) return false;
            float t = (0.5f - ray.origin.y) / ray.direction.y;
            if (t <= 0) return false;
            hit = ray.origin + ray.direction * t;
            return true;
        }

        #endregion

        #region Keyboard

        private void HandleKeyboard()
        {
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                if (EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            Key[] numKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
                              Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
            if (_sortedColorsDirty)
            {
                _sortedSelectedColors.Clear();
                _sortedSelectedColors.AddRange(_selectedColors);
                _sortedSelectedColors.Sort();
                _sortedColorsDirty = false;
            }
            for (int i = 0; i < Mathf.Min(9, _sortedSelectedColors.Count); i++)
                if (kb[numKeys[i]].wasPressedThisFrame) SetPaintColor(_sortedSelectedColors[i]);
            if (kb[Key.Digit0].wasPressedThisFrame) SetPaintColor(-1);
            if (kb[Key.Backquote].wasPressedThisFrame) SetPaintColor(-1);
            if (kb[Key.Tab].wasPressedThisFrame) ToggleConveyorMode();

            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            if (shift && kb[Key.S].wasPressedThisFrame)
                SaveToActiveDB();
            if (kb[Key.F5].wasPressedThisFrame)
                TestPlay();
            if (kb[Key.O].wasPressedThisFrame)
                RoundCurrentBalloonCountsToTen();
        }

        private void ToggleConveyorMode()
        {
            _conveyorPaintMode = !_conveyorPaintMode;
            if (_txtConveyorMode != null)
            {
                _txtConveyorMode.text = _conveyorPaintMode ? "Conveyor" : "Balloon";
                _txtConveyorMode.color = _conveyorPaintMode
                    ? new Color(0.5f, 0.5f, 0.9f) : new Color(0.5f, 0.9f, 0.5f);
            }
            SetStatus(_conveyorPaintMode ? "Conveyor Paint (Tab)" : "Balloon Paint (Tab)");
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        //  GRID OPERATIONS
        // ═══════════════════════════════════════════════════════════════

        #region Grid Ops

        private void SetPaintColor(int idx)
        {
            _paintColor = idx;
            UpdatePaletteHighlight();
            SetStatus(_paintColor >= 0
                ? $"Brush: {COLOR_LABELS[_paintColor]} + {FIELD_GIMMICK_NAMES[_paintGimmick]}"
                : "Brush: Eraser");
        }

        private void UpdatePaletteHighlight()
        {
            if (_palTexts == null) return;
            var sortedColors = new List<int>(_selectedColors);
            sortedColors.Sort();
            for (int i = 0; i < _palTexts.Length; i++)
            {
                if (_palTexts[i] == null) continue;
                if (i > sortedColors.Count) break; // 범위 초과 방지
                bool isEraser = (i == sortedColors.Count);
                bool sel = isEraser ? (_paintColor == -1) : (sortedColors[i] == _paintColor);
                string label = isEraser ? "X" : COLOR_LABELS[sortedColors[i]];
                _palTexts[i].text = sel ? $"[{label}]" : label;
            }
        }

        private void AutoRoundBalloonCounts()
        {
            if (_importRoundTo <= 0) return;
            // Count colors
            var counts = new Dictionary<int, int>();
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                    if (_balloonColors[c, r] >= 0)
                    {
                        int ci = _balloonColors[c, r];
                        counts[ci] = counts.ContainsKey(ci) ? counts[ci] + 1 : 1;
                    }

            // Check if any color is not a multiple
            bool needsRound = false;
            foreach (var v in counts.Values)
                if (v % _importRoundTo != 0) { needsRound = true; break; }

            if (needsRound)
                RoundColorCounts(_balloonColors, _gridCols, _gridRows, _importRoundTo);
        }

        private void RoundCurrentBalloonCountsToTen()
        {
            var rep = EnforceMod10OnColRowGrid(_balloonColors, _gridCols, _gridRows, Mod10.NO_FRAME);
            OnBalloonGridChanged();
            if (rep == null) { SetStatus("Round x10: grid empty"); return; }
            SetStatus(rep.AllMod10
                ? $"Mod10 ✓ 전 색 ÷10 (총 {rep.Total}, 색 {rep.ColorCounts.Count}, 바뀐셀 {rep.ChangedCells}, 구석빈칸 {rep.EmptyCornerCells})"
                : $"Mod10 ⚠ 일부 색 ÷10 미달 (총 {rep.Total}) — 연결 안 된 고립색 가능. 원본/색 검토 요망");
        }

        /// <summary>
        /// ROLLBACK_MAPMAKER_MOD10_20260624: MapMaker [col,row] 그리드 ↔ Mod10 모듈 [row(H),col(W)] 브리지.
        /// 전치 → Mod10.EnforceMod10(relay 다중홉으로 전 색 ÷10 보장) → 결과를 같은 그리드에 반영.
        /// 색은 유지하고 카운트만 ÷10 (색 선택은 색매핑 단계 책임).
        /// </summary>
        private Mod10.Report EnforceMod10OnColRowGrid(int[,] grid, int cols, int rows, int frameColor)
        {
            if (grid == null || cols <= 0 || rows <= 0) return null;
            var g = new int[rows, cols]; // [H=rows, W=cols] 행우선
            int nonEmpty = 0;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int v = grid[c, r];
                    g[r, c] = v;
                    if (v != Mod10.EMPTY) nonEmpty++;
                }
            // 비어있으면(색 없음) 이미 총합 0 = ÷10. Mod10 의 buffer 선택이 빈 후보로 터지는 것 방지.
            if (nonEmpty == 0) return null;

            var (outGrid, rep) = Mod10.EnforceMod10(g, frameColor);

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    grid[c, r] = outGrid[r, c];
            return rep;
        }

        private void OnBalloonGridChanged()
        {
            InitGrid();
            // 그리드 크기가 변경됐으면 전체 리빌드, 아니면 색상만 업데이트
            if (_previewObjs == null || _previewObjs.GetLength(0) != _gridCols || _previewObjs.GetLength(1) != _gridRows)
            {
                RebuildPreview();
                RebuildGridLines();
                RebuildConveyorPreview();
            }
            else
            {
                UpdatePreviewColors();
            }
            _infoDirty = true;
        }

        /// <summary>프리뷰 오브젝트의 색상/표시만 갱신 (Destroy/Create 없이).</summary>
        /// <summary>인간 시각 기반 색상 거리. 색상(Hue) 차이에 가중치를 줘서 밝기만 비슷한 다른 색을 구별.</summary>
        private static float PerceptualColorDist(float r1, float g1, float b1, float r2, float g2, float b2)
        {
            // 가중 RGB (인간 눈의 감도: G > R > B)
            float dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
            float rgbDist = 2f * dr * dr + 4f * dg * dg + 3f * db * db;

            // HSV 색상(Hue) 거리 — 밝기가 비슷해도 색이 다르면 큰 거리
            float h1, s1, v1, h2, s2, v2;
            Color.RGBToHSV(new Color(r1, g1, b1), out h1, out s1, out v1);
            Color.RGBToHSV(new Color(r2, g2, b2), out h2, out s2, out v2);

            float dh = Mathf.Abs(h1 - h2);
            if (dh > 0.5f) dh = 1f - dh; // Hue wrap-around
            float ds = Mathf.Abs(s1 - s2);

            // 채도가 낮으면 Hue 차이 무시 (회색 계열은 Hue가 불안정)
            float satWeight = Mathf.Min(s1, s2);
            float hueDist = dh * dh * satWeight * 8f; // Hue에 높은 가중치
            float satDist = ds * ds * 2f;

            return rgbDist + hueDist + satDist;
        }

        private void UpdatePreviewColors()
        {
            if (_previewObjs == null) return;
            for (int c = 0; c < _gridCols; c++)
            {
                for (int r = 0; r < _gridRows; r++)
                {
                    if (c >= _previewObjs.GetLength(0) || r >= _previewObjs.GetLength(1)) continue;
                    var go = _previewObjs[c, r];
                    if (go == null) continue;

                    int ci = _balloonColors[c, r];
                    int gi = _balloonGimmicks[c, r];
                    var mr = go.GetComponent<MeshRenderer>();
                    if (mr == null) continue;

                    if (ci >= 0)
                    {
                        go.SetActive(true);
                        mr.sharedMaterial = GetCachedMaterial(ci, gi);
                    }
                    else
                    {
                        go.SetActive(false);
                    }

                    // 라벨 갱신
                    if (_previewLabels != null && c < _previewLabels.GetLength(0) && r < _previewLabels.GetLength(1))
                    {
                        var label = _previewLabels[c, r];
                        if (label != null)
                        {
                            if (ci >= 0 && gi > 0 && gi < FIELD_GIMMICK_NAMES.Length)
                                label.text = FIELD_GIMMICK_NAMES[gi].Substring(0, System.Math.Min(2, FIELD_GIMMICK_NAMES[gi].Length));
                            else
                                label.text = "";
                        }
                    }
                }
            }
        }

        private void FillBalloons(int color)
        {
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                { _balloonColors[c, r] = color; _balloonGimmicks[c, r] = color >= 0 ? _paintGimmick : 0; }
        }

        private void RandomBalloons()
        {
            var colorList = new List<int>(_selectedColors);
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                { _balloonColors[c, r] = colorList[Random.Range(0, colorList.Count)]; _balloonGimmicks[c, r] = 0; }
        }

        private void FillHolders(int color)
        {
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                { _holderColors[c, r] = color; _holderMags[c, r] = color >= 0 ? _defaultMag : 0; }
        }

        private void RandomHolders()
        {
            var colorList = new List<int>(_selectedColors);
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                { _holderColors[c, r] = colorList[Random.Range(0, colorList.Count)]; _holderMags[c, r] = _defaultMag; }
        }

        private void SetAllMags()
        {
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                    if (_holderColors[c, r] >= 0) _holderMags[c, r] = _defaultMag;
        }

        private int GetGimmickLife(int gimmickIndex, int col = -1, int row = -1)
        {
            if (gimmickIndex <= 0 || gimmickIndex >= FIELD_GIMMICK_NAMES.Length) return 1;
            string g = FIELD_GIMMICK_NAMES[gimmickIndex];
            // Wall/Pin 는 직접 hit 불가 → 다트 0개. Wall 이 sized(multi-cell) 라도 HP 산정에선 제외하므로
            // IsSizedFieldGimmick HP 분기보다 먼저 처리한다 (Wall 편입 후 회귀 방지).
            if (g == "Wall" || g == "Pin") return 0;
            // ROLLBACK_ICE_BALANCE_UNDERLYING_BALLOON_20260626:
            // Ice is an overlay/status on top of an existing balloon, not a replacement object.
            // The covered balloon still needs one matching dart after Ice breaks, so Balance/AutoBalance
            // must keep the underlying balloon's 1 dart need. Ice HP is shown separately as gimmick HP.
            if (g == "Ice") return 1;
            // ROLLBACK_BALANCE_HP_MAP_20260625: Color_Curtain 은 counter(=hp, 기본 3=DEFAULT_CURTAIN_COUNTER)
            //   만큼 자기색 pop 필요 → 1 이 아닌 HP 반영. (직접타격 아닌 간접이나, 보수적으로 자기색 수요로 계상.)
            if (g == "Color_Curtain")
            {
                int chp = (col >= 0 && row >= 0) ? _balloonGimmickHP[col, row] : 3;
                return chp > 0 ? chp : 3;
            }
            if (IsSizedFieldGimmick(g))
            {
                // 실제 HP 사용 (기본값 2)
                int hp = (col >= 0 && row >= 0) ? _balloonGimmickHP[col, row] : 2;
                return hp > 0 ? hp : 2;
            }
            return 1;
        }

        private void RefreshInfo()
        {
            // Holder Cols/Rows 숫자 UI 동기화
            if (_holderColsInput != null) _holderColsInput.text = _holderCols.ToString();
            if (_holderRowsInput != null) _holderRowsInput.text = _holderRows.ToString();

            if (_txtSpacing) _txtSpacing.text = $"  Spacing: {CellSpacing:F3}";
            if (_txtScale) _txtScale.text = $"  Scale: {BalloonScale:F3}";

            // 허용량 자동 갱신
            int autoCap = RailManager.CalculateCapacity(CalcTotalDarts());
            _railSlotCount = autoCap;
            if (_railCapacityLabel != null)
                _railCapacityLabel.text = $"{autoCap}  ({RailManager.GetRailSideCount(autoCap)}면, 제거:{RailManager.GetContinueRemoveCount(autoCap)})";

            // === Build per-color stats ===
            var balloonCountPerColor = new Dictionary<int, int>();
            var dartsNeededPerColor = new Dictionary<int, int>();
            var gimmickCounts = new Dictionary<string, int>();

            // ROLLBACK_BALANCE_HP_MAP_20260625: FlexTube 그룹 중복 카운트 방지(그룹 id 1회만 집계).
            var countedFlexTubeGroups = new System.Collections.Generic.HashSet<int>();
            // ROLLBACK_IRONWALL_NOCOLOR_20260626: Wall(IronWall) 셀수 별도 집계(색상별 B/need 에는 미포함).
            int ironCellCount = 0;
            // ROLLBACK_BALANCE_GIMMICK_BREAKDOWN_20260626: 색상별 HP 기믹 분해 — (색,기믹표시명)→HP 합.
            //   하단 Balance Check 에서 "Cy: 140 / Cy_Wooden: 20" 형태로 색 아래 기믹별 기여를 보여준다.
            var hpByColorGimmick = new Dictionary<(int color, string gim), int>();
            var seenIceRegions = new HashSet<(int grp, int color)>(); // Ice 영역 HP 중복 집계 방지.
            void AddGimHp(int colr, string gimName, int amt)
            {
                if (colr < 0 || amt <= 0 || string.IsNullOrEmpty(gimName)) return;
                var key = (colr, gimName);
                hpByColorGimmick.TryGetValue(key, out int v);
                hpByColorGimmick[key] = v + amt;
            }
            // 기믹 내부명 → 표시명. 분해 대상 아닌 것(Surprise/일반 풍선)은 null → 미표시.
            string GimDisplay(string g)
            {
                switch (g)
                {
                    case "Pinata":        return "Wooden";
                    case "Pinata_Box":    return "TargetBox";
                    case "Barricade":     return "Barricade";
                    case "FlexTube":      return "FlexTube";
                    case "Color_Curtain": return "Curtain";
                    case "Ice":           return "Ice";
                    default:              return null;
                }
            }
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                    if (_balloonColors[c, r] >= 0)
                    {
                        int gi = _balloonGimmicks[c, r];
                        string gn = (gi > 0 && gi < FIELD_GIMMICK_NAMES.Length) ? FIELD_GIMMICK_NAMES[gi] : "";

                        // 기믹 표시 카운트 — 사이즈 기믹(Pinata/Barricade/Wall) 비앵커 footprint 셀은 제외(기존 동작 유지).
                        if (gn.Length > 0
                            && !(IsSizedFieldGimmick(gn) && gn != "Pinata_Box" && _balloonPinataW[c, r] == 0))
                        {
                            gimmickCounts.TryGetValue(gn, out int gcOld); gimmickCounts[gn] = gcOld + 1;
                        }

                        // ROLLBACK_IRONWALL_NOCOLOR_20260626: Wall(IronWall)은 색·공격 없는 불파괴 → 색상별 B/need 에서 제외,
                        //   Iron 셀수만 별도 집계(앵커+footprint 모든 Wall 셀). (기존엔 default 경로에서 색 풍선으로 잡혀 색 수가 부풀었음.)
                        if (gn == "Wall") { ironCellCount++; continue; }

                        // ROLLBACK_BALANCE_HP_MAP_20260625: Pinata_Box — 알별(색·HP)을 그 알 색에 1회 합산.
                        //   (기존: 앵커 _balloonGimmickHP × 그리드 셀색 으로 수·색·HP 전부 틀렸음.) 알 config 는 anchor 키 저장.
                        if (gn == "Pinata_Box")
                        {
                            var bkey = new Vector2Int(c, r);
                            if (_boxEggConfigColors.TryGetValue(bkey, out int[] eggC) && eggC != null && eggC.Length > 0)
                            {
                                int[] eggH = (_boxEggConfigHps.TryGetValue(bkey, out int[] hArr) && hArr != null && hArr.Length == eggC.Length) ? hArr : null;
                                for (int e = 0; e < eggC.Length; e++)
                                {
                                    int ec = eggC[e];
                                    if (ec < 0) continue;
                                    int eh = (eggH != null && eggH[e] > 0) ? eggH[e] : 1;
                                    // ROLLBACK_BALANCE_GIMMICK_NOTBALLOON_20260626: Target Box 는 풍선이 아님 → B(풍선) 카운트 미포함(need/분해만).
                                    dartsNeededPerColor.TryGetValue(ec, out int ed); dartsNeededPerColor[ec] = ed + eh;
                                    AddGimHp(ec, "TargetBox", eh); // ROLLBACK_BALANCE_GIMMICK_BREAKDOWN_20260626: 알별 HP 분해
                                }
                                continue;
                            }
                            if (_balloonPinataW[c, r] == 0) continue; // config 없는 비앵커 박스 셀 스킵
                            int ac = _balloonColors[c, r];
                            int ah = _balloonGimmickHP[c, r] > 0 ? _balloonGimmickHP[c, r] : 2; // 폴백: 앵커색 단일 알
                            // ROLLBACK_BALANCE_GIMMICK_NOTBALLOON_20260626: Target Box 폴백도 B 카운트 미포함.
                            dartsNeededPerColor.TryGetValue(ac, out int ad); dartsNeededPerColor[ac] = ad + ah;
                            AddGimHp(ac, "TargetBox", ah); // ROLLBACK_BALANCE_GIMMICK_BREAKDOWN_20260626
                            continue;
                        }

                        // ROLLBACK_BALANCE_HP_MAP_20260625: FlexTube — 그룹 공유 HP → 그룹당 1회(그룹 max HP, 폴백 셀수), 자기색.
                        //   (기존: 셀마다 1 더해 길이만큼 과다 계산.)
                        if (gn == "FlexTube" && _balloonFlexTubeGroupId[c, r] >= 0)
                        {
                            int grp = _balloonFlexTubeGroupId[c, r];
                            if (countedFlexTubeGroups.Contains(grp)) continue;
                            countedFlexTubeGroups.Add(grp);
                            // ROLLBACK_FLEXTUBE_2X2_PARTS_20260626: 각 파트가 2×2=4셀이라 raw cellCnt 는 파트수×4 로 부풀어
                            //   런타임 auto-HP(=중간 파트 수)와 어긋난다. 폴백 HP 는 distinct seq(파트) 기준으로 센다.
                            int maxHp = 0, tubeColor = _balloonColors[c, r];
                            var ftSeqs = new System.Collections.Generic.HashSet<int>();
                            for (int cc = 0; cc < _gridCols; cc++)
                                for (int rr = 0; rr < _gridRows; rr++)
                                    if (_balloonFlexTubeGroupId[cc, rr] == grp)
                                    {
                                        if (_balloonGimmickHP[cc, rr] > maxHp) maxHp = _balloonGimmickHP[cc, rr];
                                        if (_balloonColors[cc, rr] >= 0) tubeColor = _balloonColors[cc, rr];
                                        int sq = _balloonFlexTubeSequenceIndex[cc, rr];
                                        if (sq >= 0) ftSeqs.Add(sq);
                                    }
                            // 런타임: HP = 작가 지정(maxHp>0) 아니면 segment 파트 수(전체 파트 - Head - Edge).
                            int ftSegParts = Mathf.Max(1, ftSeqs.Count - 2);
                            int tubeHp = maxHp > 0 ? maxHp : ftSegParts;
                            if (tubeColor >= 0)
                            {
                                // ROLLBACK_BALANCE_GIMMICK_NOTBALLOON_20260626: FlexTube 는 풍선이 아님 → B 카운트 미포함(need/분해만).
                                dartsNeededPerColor.TryGetValue(tubeColor, out int td); dartsNeededPerColor[tubeColor] = td + tubeHp;
                                AddGimHp(tubeColor, "FlexTube", tubeHp); // ROLLBACK_BALANCE_GIMMICK_BREAKDOWN_20260626
                            }
                            continue;
                        }

                        // 사이즈 기믹 비앵커 셀(실제 풍선 아님) 스킵 — Pinata/Barricade/Wall
                        // ROLLBACK_ICE_OVERLAY_UNDER_BALLOONS_20260626:
                        // Ice is intentionally excluded from IsSizedFieldGimmick, so 2x2/3x3 Ice keeps
                        // 4/9 real balloon cells for balance while only the visual overlay is merged.
                        bool isSizedFieldCell = gn.Length > 0 && IsSizedFieldGimmick(gn);
                        if (isSizedFieldCell && _balloonPinataW[c, r] == 0) continue;

                        int ci = _balloonColors[c, r];
                        int life = GetGimmickLife(gi, c, r);
                        // ROLLBACK_BALANCE_WOODEN_NOT_BALLOON_COUNT_20260628:
                        // Wooden Box(Pinata) is a field gimmick layered on the board, not an extra
                        // color balloon for the MapMaker "Balloons" total. Keep its HP in dart need
                        // and breakdown, but do not make 200 yellow balloons display as 201B.
                        // ROLLBACK_BALANCE_BARRICADE_NOT_BALLOON_COUNT_20260628:
                        // Barricade 도 보드 위에 얹는 필드 기믹(단일 오브젝트, 그 아래 실제 풍선 없음)이라
                        // 색상별 풍선 수에 포함되면 안 됨. HP(need)·분해 표시는 유지하고 B 카운트만 제외.
                        if (gn != "Pinata" && gn != "Barricade")
                        {
                            balloonCountPerColor.TryGetValue(ci, out int bcOld);
                            balloonCountPerColor[ci] = bcOld + 1;
                        }
                        dartsNeededPerColor.TryGetValue(ci, out int dnOld); dartsNeededPerColor[ci] = dnOld + life;

                        // ROLLBACK_BALANCE_GIMMICK_BREAKDOWN_20260626: 색상별 기믹 분해 누적.
                        if (gn == "Ice")
                        {
                            // Ice 는 런타임상 색무관 영역 공유 HP라 per-color need 엔 미포함(life=0). 단 표시용으로는
                            //   cell 색 하단에 '영역 HP 1회'를 보여준다(그룹 중복 제거). mode2=override(_balloonIceGroupHp),
                            //   그 외=영역 멤버 _balloonGimmickHP 합. 비그룹(groupId<0)은 셀별 HP.
                            int igrp = _balloonIceGroupId[c, r];
                            if (igrp >= 0)
                            {
                                if (seenIceRegions.Add((igrp, ci)))
                                {
                                    int ihp;
                                    if (_balloonIceGroupHpMode[c, r] == 2 && _balloonIceGroupHp[c, r] > 0)
                                        ihp = _balloonIceGroupHp[c, r];
                                    else
                                    {
                                        ihp = 0;
                                        for (int cc = 0; cc < _gridCols; cc++)
                                            for (int rr = 0; rr < _gridRows; rr++)
                                                if (_balloonIceGroupId[cc, rr] == igrp && _balloonGimmicks[cc, rr] == gi)
                                                    ihp += Mathf.Max(1, _balloonGimmickHP[cc, rr]);
                                    }
                                    AddGimHp(ci, "Ice", ihp);
                                }
                            }
                            else
                            {
                                AddGimHp(ci, "Ice", _balloonGimmickHP[c, r] > 0 ? _balloonGimmickHP[c, r] : 2);
                            }
                        }
                        else
                        {
                            // Pinata→Wooden / Barricade / Color_Curtain→Curtain (Surprise·일반풍선은 GimDisplay=null → 미표시).
                            string gd = GimDisplay(gn);
                            if (gd != null) AddGimHp(ci, gd, life);
                        }
                    }

            var dartsProvidedPerColor = new Dictionary<int, int>();
            int totalHolders = 0;
            int totalDartsProvided = 0;
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                    if (_holderColors[c, r] >= 0)
                    {
                        int ci = _holderColors[c, r];
                        dartsProvidedPerColor.TryGetValue(ci, out int dpOld); dartsProvidedPerColor[ci] = dpOld + _holderMags[c, r];
                        totalDartsProvided += _holderMags[c, r];
                        totalHolders++;
                    }

            int totalBalloons = 0;
            foreach (var v in balloonCountPerColor.Values) totalBalloons += v;
            int totalDartsNeeded = 0;
            foreach (var v in dartsNeededPerColor.Values) totalDartsNeeded += v;

            // === Center Top: Map + Holder info ===
            if (_txtCenterInfo)
            {
                var sb = new System.Text.StringBuilder(256);
                sb.Append($"Level {_levelId}  |  {_gridCols}x{_gridRows}  |  {_numColors}C  |  {_difficulty}");
                sb.Append($"\nBalloons: {totalBalloons}  |  Holders: {totalHolders}  |  Darts: {totalDartsProvided} (need {totalDartsNeeded})");
                // ROLLBACK_IRONWALL_NOCOLOR_20260626: Wall 갯수는 하단 Balance check 에 "Wall: N" 으로 표시(아래로 이동).

                if (gimmickCounts.Count > 0)
                {
                    sb.Append("\nGimmicks:");
                    foreach (var kvp in gimmickCounts)
                        sb.Append($"  {kvp.Key}:{kvp.Value}");
                }

                sb.Append("\nHolders: ");
                foreach (var kvp in dartsProvidedPerColor)
                {
                    string label = kvp.Key < COLOR_LABELS.Length ? COLOR_LABELS[kvp.Key] : kvp.Key.ToString();
                    sb.Append($"[{label}:{kvp.Value}]  ");
                }

                _txtCenterInfo.text = sb.ToString();
            }

            // === Center Bottom: Per-color balance validation ===
            if (_txtBalanceInfo)
            {
                var sb = new System.Text.StringBuilder(256);
                bool hasIssue = false;

                var allColors = new HashSet<int>();
                foreach (var k in dartsNeededPerColor.Keys) allColors.Add(k);
                foreach (var k in dartsProvidedPerColor.Keys) allColors.Add(k);

                if (allColors.Count > 0)
                {
                    sb.Append("BALANCE CHECK (per color):\n");
                    // ROLLBACK_BALANCE_GIMMICK_BREAKDOWN_20260626: 색 아래 기믹 분해 표시 순서.
                    string[] breakdownOrder = { "Wooden", "TargetBox", "Barricade", "FlexTube", "Curtain", "Ice" };
                    foreach (int ci in allColors)
                    {
                        string label = ci < COLOR_LABELS.Length ? COLOR_LABELS[ci] : ci.ToString();
                        balloonCountPerColor.TryGetValue(ci, out int bCount);
                        dartsNeededPerColor.TryGetValue(ci, out int need);
                        dartsProvidedPerColor.TryGetValue(ci, out int have);
                        string status = (have == need) ? "OK" : (have > need ? $"+{have - need}" : $"-{need - have} !!!");
                        if (have != need) hasIssue = true;
                        sb.Append($"  {label}: {bCount}B (need {need}D) / have {have}D  [{status}]\n");
                        // ROLLBACK_BALANCE_GIMMICK_BREAKDOWN_20260626: 그 색에 기여한 HP 기믹 분해(예: "Cy_Wooden: 20").
                        foreach (var gimName in breakdownOrder)
                            if (hpByColorGimmick.TryGetValue((ci, gimName), out int gv) && gv > 0)
                                sb.Append($"    {label}_{gimName}: {gv}\n");
                    }
                    if (!hasIssue) sb.Append("  All colors balanced!");
                }
                else
                {
                    sb.Append("No balloons placed.");
                }

                // ROLLBACK_IRONWALL_NOCOLOR_20260626: Wall(IronWall)은 색 무관 불파괴 → 색상별 카운트엔 당연히 미포함.
                //   하단 Balance check 에 셀 갯수만 "Wall: N" 으로 별도 표시(색 없이도 표시).
                if (ironCellCount > 0)
                    sb.Append($"\nWall: {ironCellCount}");

                _txtBalanceInfo.text = sb.ToString();
                _txtBalanceInfo.color = hasIssue ? new Color(1f, 0.7f, 0.4f) : new Color(0.6f, 0.9f, 0.6f);
            }

            if (_txtStatus) _txtStatus.text = $"Balloons: {totalBalloons}  Darts: {totalDartsProvided}/{totalDartsNeeded}  WP: {_customWaypoints.Count}";

            // §9 mock — Holder Grid 위 요약 라벨 (실시간)
            if (_holderSummaryLabel != null)
            {
                if (totalHolders == 0)
                {
                    _holderSummaryLabel.text = "보관함 -개 | 다트 -";
                    _holderSummaryLabel.color = new Color(0.65f, 0.65f, 0.70f);
                }
                else
                {
                    float ammoPerH = (float)totalDartsProvided / totalHolders;
                    string mark = totalDartsProvided == totalDartsNeeded ? "✓" : "✗";
                    var sbHol = new System.Text.StringBuilder(96);
                    sbHol.Append($"보관함 {totalHolders}개 | 다트 {totalDartsProvided}/{totalDartsNeeded} {mark} | {ammoPerH:F1}/h");
                    _holderSummaryLabel.text = sbHol.ToString();
                    _holderSummaryLabel.color = totalDartsProvided == totalDartsNeeded
                        ? new Color(0.55f, 0.95f, 0.55f)
                        : new Color(1f, 0.65f, 0.35f);
                }
            }

            // §2-3 Auto 추천 — 디자이너가 _holderCols 를 변경하면 추천값과의 일치 여부 표시
            if (_queueGenRecommendLabel != null && !_queueGenConfirmReady)
            {
                int rec = totalHolders > 0
                    ? Mathf.Clamp(RecommendQueueColumns(totalHolders, _difficulty), 2, 5)
                    : 0;
                if (rec > 0)
                    _queueGenRecommendLabel.text = _queueColsAuto
                        ? $"cols Auto (추천 {rec})"
                        : $"cols Manual {_holderCols} (추천 {rec})";
                else
                    _queueGenRecommendLabel.text = "";
            }
        }

        private void SetStatus(string msg) { if (_txtStatus) _txtStatus.text = msg; }

        private void AutoBalanceHolders()
        {
            // ROLLBACK_GENQUEUE_GIMMICK_NEED_MATCH_20260628: AutoBalance 도 GenerateQueue/Balance check 와 동일 수요 산정.
            //   TargetBox=알(egg) 기준, FlexTube=그룹 기준 — footprint 셀 수(2×2)로 과다 카운트하던 것 교정.
            var needed = new Dictionary<int, int>();
            var autoCountedFlexTubeGroups = new System.Collections.Generic.HashSet<int>();
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                    if (_balloonColors[c, r] >= 0)
                    {
                        int gi = _balloonGimmicks[c, r];
                        string autoGimmickName = (gi > 0 && gi < FIELD_GIMMICK_NAMES.Length) ? FIELD_GIMMICK_NAMES[gi] : "";

                        // TargetBox(Pinata_Box): 알(egg) config 기준.
                        if (autoGimmickName == "Pinata_Box")
                        {
                            var bkey = new Vector2Int(c, r);
                            if (_boxEggConfigColors.TryGetValue(bkey, out int[] eggC) && eggC != null && eggC.Length > 0)
                            {
                                int[] eggH = (_boxEggConfigHps.TryGetValue(bkey, out int[] hArr) && hArr != null && hArr.Length == eggC.Length) ? hArr : null;
                                for (int e = 0; e < eggC.Length; e++)
                                {
                                    int ec = eggC[e];
                                    if (ec < 0) continue;
                                    int eh = (eggH != null && eggH[e] > 0) ? eggH[e] : 1;
                                    needed[ec] = (needed.ContainsKey(ec) ? needed[ec] : 0) + eh;
                                }
                                continue;
                            }
                            if (_balloonPinataW[c, r] == 0) continue;
                            int ac = _balloonColors[c, r];
                            int ah = _balloonGimmickHP[c, r] > 0 ? _balloonGimmickHP[c, r] : 2;
                            needed[ac] = (needed.ContainsKey(ac) ? needed[ac] : 0) + ah;
                            continue;
                        }

                        // FlexTube: 그룹당 1회(그룹 HP).
                        if (autoGimmickName == "FlexTube" && _balloonFlexTubeGroupId[c, r] >= 0)
                        {
                            int grp = _balloonFlexTubeGroupId[c, r];
                            if (autoCountedFlexTubeGroups.Contains(grp)) continue;
                            autoCountedFlexTubeGroups.Add(grp);
                            int maxHp = 0, tubeColor = _balloonColors[c, r];
                            var ftSeqs = new System.Collections.Generic.HashSet<int>();
                            for (int cc = 0; cc < _gridCols; cc++)
                                for (int rr = 0; rr < _gridRows; rr++)
                                    if (_balloonFlexTubeGroupId[cc, rr] == grp)
                                    {
                                        if (_balloonGimmickHP[cc, rr] > maxHp) maxHp = _balloonGimmickHP[cc, rr];
                                        if (_balloonColors[cc, rr] >= 0) tubeColor = _balloonColors[cc, rr];
                                        int sq = _balloonFlexTubeSequenceIndex[cc, rr];
                                        if (sq >= 0) ftSeqs.Add(sq);
                                    }
                            int tubeHp = maxHp > 0 ? maxHp : Mathf.Max(1, ftSeqs.Count - 2);
                            if (tubeColor >= 0)
                                needed[tubeColor] = (needed.ContainsKey(tubeColor) ? needed[tubeColor] : 0) + tubeHp;
                            continue;
                        }

                        // ROLLBACK_ICE_OVERLAY_UNDER_BALLOONS_20260626:
                        // Ice 2x2/3x3 keeps the underlying 4/9 balloons, so AutoBalance must count those
                        // cells. IsSizedFieldGimmick excludes Ice and this skip does not apply to it.
                        bool isSizedFieldCell = autoGimmickName.Length > 0
                            && IsSizedFieldGimmick(autoGimmickName);
                        if (isSizedFieldCell && _balloonPinataW[c, r] == 0) continue;
                        int ci = _balloonColors[c, r];
                        int life = GetGimmickLife(gi, c, r);
                        // ROLLBACK_GENQUEUE_WALL_NO_DART_20260701: Wall/Pin(life=0) 은 수요 색 목록에서 제외(0값 키 방지).
                        if (life <= 0) continue;
                        needed[ci] = (needed.ContainsKey(ci) ? needed[ci] : 0) + life;
                    }

            var holdersByColor = new Dictionary<int, List<System.Tuple<int, int>>>();
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                    if (_holderColors[c, r] >= 0)
                    {
                        int ci = _holderColors[c, r];
                        if (!holdersByColor.ContainsKey(ci))
                            holdersByColor[ci] = new List<System.Tuple<int, int>>();
                        holdersByColor[ci].Add(System.Tuple.Create(c, r));
                    }

            foreach (var kvp in needed)
            {
                if (!holdersByColor.ContainsKey(kvp.Key) || holdersByColor[kvp.Key].Count == 0) continue;
                var holders = holdersByColor[kvp.Key];
                int per = kvp.Value / holders.Count;
                int rem = kvp.Value % holders.Count;
                int sum = 0;
                for (int i = 0; i < holders.Count; i++)
                {
                    int cc = holders[i].Item1, rr = holders[i].Item2;
                    if (i < holders.Count - 1)
                    { _holderMags[cc, rr] = Mathf.Max(1, per + (i < rem ? 1 : 0)); sum += _holderMags[cc, rr]; }
                    else
                    { _holderMags[cc, rr] = Mathf.Max(1, kvp.Value - sum); }
                }
            }
            // After auto balance, round magazines to 10 multiples
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                    if (_holderColors[c, r] >= 0 && _holderMags[c, r] > 0)
                    {
                        int rounded = Mathf.Max(10, Mathf.RoundToInt(_holderMags[c, r] / 10f) * 10);
                        _holderMags[c, r] = rounded;
                    }

            SetStatus("Auto Balance (gimmick life accounted)");
        }

        // ════════════════════════════════════════════════════════════════
        //  큐 생성기 (Queue Generator) — BalloonFlow_큐생성기_명세 v2 (2026-04-30) 기반
        //  v1 → v2: cap20 백본 분포 + queue_columns 자동 추천 + cap30/50 가드 + 첫 3행 가드 + 검증 자동 재생성
        // ════════════════════════════════════════════════════════════════

        #region Queue Generator

        // 명세 §2-1 — 난이도별 cap 가중치 (총합 100). PF picked 1-300 실측 평균.
        // diffIdx: 0=Tutorial/Rest, 1=Normal/Intro, 2=Hard, 3=SuperHard
        //   ※ Rest 는 §2-7 cap30 금지로 별도 처리되지만, 가중치 자체는 Tutorial 과 공유.
        private static readonly int[][] CAP_KEYS = {
            new[] { 10, 20, 30, 40, 50 },
            new[] { 10, 20, 30, 40, 50 },
            new[] { 10, 20, 30, 40, 50 },
            new[] { 10, 20, 30, 40, 50 },
        };
        private static readonly float[][] CAP_WEIGHTS_BASE = {
            // 10    20    30    40    50      ← 명세 §2-1 표 (v5.1, 2026-05-13 — cap10/40 점진곡선 명확화)
            new[] { 0.28f, 0.64f, 0.00f, 0.08f, 0.00f }, // Tutorial (28/64/0/8/0)
            new[] { 0.19f, 0.67f, 0.02f, 0.12f, 0.00f }, // Normal   (19/67/2/12/0)
            new[] { 0.16f, 0.64f, 0.03f, 0.16f, 0.01f }, // Hard     (16/64/3/16/1)
            new[] { 0.14f, 0.62f, 0.03f, 0.18f, 0.03f }, // SuperHard(14/62/3/18/3)
        };
        // Rest 전용 (§2-7: cap30 금지) — §2-1 v5.1 휴식 행 22/68/0/10/0.
        private static readonly float[] CAP_WEIGHTS_REST =
            new[] { 0.22f, 0.68f, 0.00f, 0.10f, 0.00f };

        // 순서 배치 파라미터: 앞 50%에 depth 0 비율 (min~max) — 명세 §2-4 표.
        private static readonly float[][] DEPTH0_FRONT_RATIO = {
            new[] { 0.80f, 0.95f }, // Tutorial/Rest
            new[] { 0.40f, 0.65f }, // Normal
            new[] { 0.25f, 0.45f }, // Hard
            new[] { 0.10f, 0.30f }, // SuperHard
        };
        // 행(가로)/열(세로) 연속 max — 명세 §4-3 / §2-4 v2.2 (2026-05-13, PF picked 1-300 max 회귀).
        //   ※ 구버전 v2.1(2026-04-30) ROW{1,2,3,4}/COL{1,2,2,3} 은 PF max 보다 빡빡 → 50% 위반 처리.
        //   diffIdx(4-index) 표는 fallback. Rest 는 Tutorial 과 다르므로 GetConsecutiveLimits() 로 분기.
        private static readonly int[] SAME_COLOR_MAX_ROW = { 1, 3, 3, 4 };
        private static readonly int[] SAME_COLOR_MAX_COL = { 2, 4, 4, 5 };

        /// <summary>§4-3 / §2-4 v2.2 — purpose 별 같은 색 연속 max (행, 열). Rest 를 Tutorial 과 구분.</summary>
        private static void GetConsecutiveLimits(DifficultyPurpose purpose, out int rowMax, out int colMax)
        {
            switch (purpose)
            {
                case DifficultyPurpose.Tutorial:  rowMax = 1; colMax = 2; return; // 튜토 1/2
                case DifficultyPurpose.Rest:      rowMax = 2; colMax = 3; return; // 휴식 2/3
                case DifficultyPurpose.Hard:      rowMax = 3; colMax = 4; return; // 하드 3/4
                case DifficultyPurpose.SuperHard: rowMax = 4; colMax = 5; return; // 슈하 4/5
                case DifficultyPurpose.Normal:
                case DifficultyPurpose.Intro:
                default:                          rowMax = 3; colMax = 4; return; // 노말 3/4
            }
        }

        private const int AVG_CAP = 21;                 // 명세 §2-0 — PF 평균 탄창
        private const int GENERATE_RETRY_MAX = 20;       // 명세 §6 Hard rule fail 시 자동 재생성 최대 시도

        private void GenerateQueue()
        {
            // ── STEP C 입력 — 현재 칠해진 홀더 큐 기믹 개수 (그리드 재구성 전 캡처) ──
            //   레벨의 "기믹 데이터"(칠해진 Hidden/Chain/Frozen 수)를 읽어 STEP C 가 위치를 알고리즘 배치.
            int stepCHidden = 0, stepCChain = 0, stepCFrozen = 0;
            if (_holderGimmicks != null)
                for (int c = 0; c < _holderCols; c++)
                    for (int r = 0; r < _holderRows; r++)
                    {
                        int g = _holderGimmicks[c, r];
                        if (g <= 0 || g >= HOLDER_GIMMICK_NAMES.Length) continue;
                        string gn = HOLDER_GIMMICK_NAMES[g];
                        if (gn == "Hidden") stepCHidden++;
                        else if (gn == "Chain") stepCChain++;
                        else if (gn == "Frozen_Dart") stepCFrozen++;
                    }

            // ── 1. 필드 분석 (§1) ──
            // ROLLBACK_GENQUEUE_GIMMICK_NEED_MATCH_20260628:
            //   TargetBox/FlexTube 다트 수요를 Balance check(RefreshInfo)와 '동일'하게 센다.
            //   기존엔 TargetBox 를 footprint 셀마다(2×2=4셀)×HP, FlexTube 를 셀마다(2×2=4셀×파트수) 세어
            //   실제 알(egg)/그룹 수요보다 과다 생성됐다(예: pk 다트가 필요량보다 많음).
            var colorDarts = new Dictionary<int, int>();
            var genCountedFlexTubeGroups = new System.Collections.Generic.HashSet<int>();
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                    if (_balloonColors[c, r] >= 0)
                    {
                        int gi = _balloonGimmicks[c, r];
                        string gn = (gi > 0 && gi < FIELD_GIMMICK_NAMES.Length) ? FIELD_GIMMICK_NAMES[gi] : "";

                        // TargetBox(Pinata_Box): 알(egg) config 기준 — 알별 색·HP 만큼만(footprint 셀 수 무관).
                        if (gn == "Pinata_Box")
                        {
                            var bkey = new Vector2Int(c, r);
                            if (_boxEggConfigColors.TryGetValue(bkey, out int[] eggC) && eggC != null && eggC.Length > 0)
                            {
                                int[] eggH = (_boxEggConfigHps.TryGetValue(bkey, out int[] hArr) && hArr != null && hArr.Length == eggC.Length) ? hArr : null;
                                for (int e = 0; e < eggC.Length; e++)
                                {
                                    int ec = eggC[e];
                                    if (ec < 0) continue;
                                    int eh = (eggH != null && eggH[e] > 0) ? eggH[e] : 1;
                                    colorDarts[ec] = (colorDarts.ContainsKey(ec) ? colorDarts[ec] : 0) + eh;
                                }
                                continue;
                            }
                            if (_balloonPinataW[c, r] == 0) continue; // config 없는 비앵커 박스 셀 스킵
                            int ac = _balloonColors[c, r];
                            int ah = _balloonGimmickHP[c, r] > 0 ? _balloonGimmickHP[c, r] : 2;
                            colorDarts[ac] = (colorDarts.ContainsKey(ac) ? colorDarts[ac] : 0) + ah;
                            continue;
                        }

                        // FlexTube: 그룹당 1회 — 그룹 HP(작가지정 or segment 파트 수). 셀 수(2×2) 무관.
                        if (gn == "FlexTube" && _balloonFlexTubeGroupId[c, r] >= 0)
                        {
                            int grp = _balloonFlexTubeGroupId[c, r];
                            if (genCountedFlexTubeGroups.Contains(grp)) continue;
                            genCountedFlexTubeGroups.Add(grp);
                            int maxHp = 0, tubeColor = _balloonColors[c, r];
                            var ftSeqs = new System.Collections.Generic.HashSet<int>();
                            for (int cc = 0; cc < _gridCols; cc++)
                                for (int rr = 0; rr < _gridRows; rr++)
                                    if (_balloonFlexTubeGroupId[cc, rr] == grp)
                                    {
                                        if (_balloonGimmickHP[cc, rr] > maxHp) maxHp = _balloonGimmickHP[cc, rr];
                                        if (_balloonColors[cc, rr] >= 0) tubeColor = _balloonColors[cc, rr];
                                        int sq = _balloonFlexTubeSequenceIndex[cc, rr];
                                        if (sq >= 0) ftSeqs.Add(sq);
                                    }
                            int tubeHp = maxHp > 0 ? maxHp : Mathf.Max(1, ftSeqs.Count - 2);
                            if (tubeColor >= 0)
                                colorDarts[tubeColor] = (colorDarts.ContainsKey(tubeColor) ? colorDarts[tubeColor] : 0) + tubeHp;
                            continue;
                        }

                        bool isSizedFieldCell = gi > 0 && gi < FIELD_GIMMICK_NAMES.Length && IsSizedFieldGimmick(gn);
                        if (isSizedFieldCell && _balloonPinataW[c, r] == 0) continue;
                        int ci = _balloonColors[c, r];
                        int life = GetGimmickLife(gi, c, r);
                        // ROLLBACK_GENQUEUE_WALL_NO_DART_20260701: Wall/Pin 등 다트 불필요(life=0) 셀은 색 목록에 넣지 않는다.
                        //   넣으면 colorDarts[색]=0 키가 생겨 이후 10배수 올림에서 0→10 으로 올라가 유령 홀더(예: 핑크)가 생성된다.
                        if (life <= 0) continue;
                        colorDarts[ci] = (colorDarts.ContainsKey(ci) ? colorDarts[ci] : 0) + life;
                    }

            if (colorDarts.Count == 0)
            {
                SetStatus("Generate Queue: No balloons on field.");
                return;
            }

            // 10배수 올림 — 명세 §1 color_darts 정의
            var colorDartsRounded = new Dictionary<int, int>();
            int totalDarts = 0;
            foreach (var kvp in colorDarts)
            {
                int rounded = ((kvp.Value + 9) / 10) * 10;
                if (rounded < 10) rounded = 10;
                colorDartsRounded[kvp.Key] = rounded;
                totalDarts += rounded;
            }

            int railCapacity = RailManager.CalculateCapacity(totalDarts);
            int dartCapMax = GetDartCapacityMax(railCapacity);

            // color_depth + color_dependency — 4면 스캔
            var colorDepth = CalcColorDepth(colorDartsRounded);
            var colorDependency = CalcColorDependency(colorDartsRounded);

            int diffIdx = GetDifficultyIndex(_difficulty);
            int levelId = _levelId;

            // 명세 §2-8 — 허용 cap 결정 (rail cap + PKG + purpose 가드)
            var allowedCaps = BuildAllowedCaps(levelId, _difficulty, railCapacity, dartCapMax);

            // 명세 §2-1 — 가중치 → 허용 cap 으로 마스킹 + 재정규화
            var weights = BuildCapWeights(diffIdx, _difficulty, allowedCaps);

            // ── 명세 §6 — Hard rule fail 시 자동 재생성 (디자이너에게 안 보임) ──
            List<(int color, int mag)> allMagazines = null;
            int queueCols = _holderCols;
            int attempts;
            string hardFailReason = null;
            // 진단용 — 각 attempts 의 fail reason 카운트 (디자이너 콘솔 출력)
            var failReasonCounts = new Dictionary<string, int>();
            var lastFailDiagnostic = new System.Text.StringBuilder();

            for (attempts = 0; attempts < GENERATE_RETRY_MAX; attempts++)
            {
                // ── 3. STEP A: 보관함 분해 (§3 v2 — 색상별 holder 수 추정 + 가중치 추첨 + 합계 보정) ──
                var pending = new List<(int color, int mag)>();
                foreach (var kvp in colorDartsRounded)
                {
                    var mags = DecomposeMagazinesV2(kvp.Value, weights, allowedCaps, dartCapMax);
                    foreach (int m in mags)
                        pending.Add((kvp.Key, m));
                }

                // ── §2-3 — Auto: 추천 사용 / Manual: 디자이너 _holderCols 존중 ──
                queueCols = _queueColsAuto
                    ? Mathf.Clamp(RecommendQueueColumns(pending.Count, _difficulty), 2, 5)
                    : Mathf.Clamp(_holderCols, 2, 5);

                // ── 4. STEP B: 그리드 배치 (§4-2) ──
                var laid = LayoutByDepth(pending, colorDepth, diffIdx);

                // 2D 그리드 연속 제한 (§4-2 #4)
                GetConsecutiveLimits(_difficulty, out int maxRowConsec, out int maxColConsec);
                EnforceGridConsecutiveLimit(laid, queueCols, maxRowConsec, maxColConsec);

                // 명세 §4-2 step 5 — 첫 3행 깊이 가드 (Hard rule)
                bool depthGuardOk = EnforceFirst3RowsDepthGuard(
                    laid, queueCols, colorDepth, maxRowConsec, maxColConsec);

                // 무결성 Hard rule 검증
                string hardFail = ValidateHardRules(laid, totalDarts, dartCapMax, queueCols);
                if (hardFail == null && depthGuardOk)
                {
                    allMagazines = laid;
                    hardFailReason = null;
                    break;
                }
                hardFailReason = hardFail ?? "first-3-rows depth guard (§4-2 step 5)";
                failReasonCounts.TryGetValue(hardFailReason, out int rc);
                failReasonCounts[hardFailReason] = rc + 1;

                // 마지막 attempt 의 상세 진단 보존 (실패 시 콘솔 출력)
                if (attempts == GENERATE_RETRY_MAX - 1)
                {
                    lastFailDiagnostic.Clear();
                    lastFailDiagnostic.Append($"\n  laid count={laid.Count} cols={queueCols} rows={(laid.Count + queueCols - 1) / queueCols}");
                    int sumLaid = 0;
                    foreach (var m in laid) sumLaid += m.mag;
                    lastFailDiagnostic.Append($"\n  sum(laid)={sumLaid} target={totalDarts}");
                    lastFailDiagnostic.Append($"\n  capMax={dartCapMax} allowedCaps=[{string.Join(",", allowedCaps)}]");
                    var capDist = new Dictionary<int, int>();
                    foreach (var m in laid)
                    {
                        capDist.TryGetValue(m.mag, out int v); capDist[m.mag] = v + 1;
                    }
                    var capDistStr = new List<string>();
                    foreach (var kvp in capDist) capDistStr.Add($"cap{kvp.Key}x{kvp.Value}");
                    lastFailDiagnostic.Append($"\n  cap distribution: {string.Join(", ", capDistStr)}");
                }
            }

            if (allMagazines == null)
            {
                // ── 진단 출력 — 콘솔 + 상태바 ──
                var reasonSummary = new List<string>();
                foreach (var kvp in failReasonCounts) reasonSummary.Add($"{kvp.Value}× {kvp.Key}");
                string reasonsText = string.Join("  /  ", reasonSummary);

                var depthSummary = new List<string>();
                foreach (var kvp in colorDepth)
                {
                    string lbl = kvp.Key < COLOR_LABELS.Length ? COLOR_LABELS[kvp.Key] : kvp.Key.ToString();
                    int darts = colorDartsRounded.ContainsKey(kvp.Key) ? colorDartsRounded[kvp.Key] : 0;
                    depthSummary.Add($"{lbl}(d{kvp.Value},{darts}d)");
                }

                Debug.LogWarning(
                    $"[GenerateQueue] FAIL after {GENERATE_RETRY_MAX} retries\n" +
                    $"  reasons: {reasonsText}\n" +
                    $"  field colors: {string.Join(" ", depthSummary)}\n" +
                    $"  totalDarts={totalDarts} railCap={railCapacity} capMax={dartCapMax} purpose={_difficulty} lv={levelId}\n" +
                    $"  last attempt:{lastFailDiagnostic}");

                SetStatus($"Generate Queue FAIL — {reasonsText} (콘솔 진단 참조)");
                if (_queueGenGaugeFill != null) { _queueGenGaugeFill.fillAmount = 0f; _queueGenGaugeFill.color = new Color(0.65f, 0.20f, 0.20f); }
                if (_queueGenGaugeText != null) { _queueGenGaugeText.text = "FAIL"; _queueGenGaugeText.color = Color.white; }
                if (_queueGenScoreLabel != null) _queueGenScoreLabel.text = "Score: FAIL";
                if (_queueGenRecommendLabel != null) _queueGenRecommendLabel.text = $"reasons: {reasonsText}";
                if (_queueGenWarnLabel != null)
                {
                    _queueGenWarnLabel.text = $"⚠ Hard rule 위반 {GENERATE_RETRY_MAX}회 — 콘솔 진단 확인";
                    _queueGenWarnLabel.color = new Color(1f, 0.30f, 0.30f);
                    _queueGenWarnLabel.gameObject.SetActive(true);
                }
                SetQueueConfirmReady(false);
                return;
            }

            // ── 5. 홀더 그리드에 반영 ──
            int neededRows = Mathf.CeilToInt((float)allMagazines.Count / queueCols);
            if (_holderCols != queueCols || _holderRows != neededRows)
            {
                _holderCols = queueCols;
                _holderRows = Mathf.Max(1, neededRows);
                InitGrid();
            }

            // Clear holder grid
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                {
                    _holderColors[c, r] = -1;
                    _holderMags[c, r] = _defaultMag;
                }

            // Fill from allMagazines (row by row, left to right)
            for (int i = 0; i < allMagazines.Count; i++)
            {
                int col = i % queueCols;
                int row = i / queueCols;
                if (col < _holderCols && row < _holderRows)
                {
                    _holderColors[col, row] = allMagazines[i].color;
                    _holderMags[col, row] = allMagazines[i].mag;
                }
            }

            // ── 5-C. STEP C — 큐 기믹 자동 배치 (ProjectHub queue-generator.ts 1:1 포팅) ──
            //   홀더는 매 생성마다 재배치되므로 기존 기믹 위치는 무효 → 배열 초기화 후 STEP C 로 재배치.
            //   STEP C 관리 대상은 Hidden/Chain/Frozen 뿐. Spawner/Lock 이 칠해져 있었다면 재생성으로 사라지므로 경고.
            int clearedOther = 0;
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                {
                    int g = _holderGimmicks[c, r];
                    if (g > 0 && g < HOLDER_GIMMICK_NAMES.Length)
                    {
                        string gn = HOLDER_GIMMICK_NAMES[g];
                        if (gn != "Hidden" && gn != "Chain" && gn != "Frozen_Dart") clearedOther++;
                    }
                    _holderGimmicks[c, r] = 0;
                    _holderChainGroups[c, r] = -1;
                    _holderFrozenHP[c, r] = 3;
                }
            if (clearedOther > 0)
                Debug.LogWarning($"[GenerateQueue] STEP C 재배치로 Spawner/Lock 홀더 기믹 {clearedOther}개가 초기화됨 — 필요 시 수동 재배치 요망.");
            int stepCPlaced = 0;
            if (stepCHidden + stepCChain + stepCFrozen > 0)
                stepCPlaced = ApplyStepC(allMagazines, queueCols, colorDartsRounded, totalDarts,
                    stepCHidden, stepCChain, stepCFrozen, _difficulty, levelId);

            // ── 6. 난이도 점수 (§7) ──
            float score = CalcDifficultyScore(allMagazines, colorDepth, colorDependency, colorDartsRounded, railCapacity);
            string grade = score < 35f ? "Easy" : score < 70f ? "Normal" : score < 90f ? "Hard" : "SuperHard";

            // ── 7. Soft rule 경고 (§6) ──
            string softWarn = ValidateSoftRules(allMagazines, colorDartsRounded);

            // UI v2 — §9 mock 갱신
            int totalH = allMagazines.Count;
            float ammoPerHolder = totalH > 0 ? (float)totalDarts / totalH : 0f;
            int capKinds = CountCapKinds(allMagazines);

            // 게이지 바 (점수 + 등급 색상)
            if (_queueGenGaugeFill != null)
            {
                _queueGenGaugeFill.fillAmount = Mathf.Clamp01(score / 100f);
                // 등급별 색상
                _queueGenGaugeFill.color =
                    score < 35f  ? new Color(0.30f, 0.65f, 0.30f) :  // Easy: green
                    score < 70f  ? new Color(0.30f, 0.55f, 0.85f) :  // Normal: blue
                    score < 90f  ? new Color(0.85f, 0.55f, 0.25f) :  // Hard: orange
                                   new Color(0.85f, 0.30f, 0.30f);   // SuperHard: red
            }
            if (_queueGenGaugeText != null)
                _queueGenGaugeText.text = $"{score:F0}%  [{grade}]";

            // 요약 라벨 — §9 mock "보관함 18개 | 다트 600/600 ✅"
            int totalCheck = 0;
            foreach (var m in allMagazines) totalCheck += m.mag;
            string sumMark = totalCheck == totalDarts ? "✓" : "✗";
            if (_queueGenScoreLabel != null)
                _queueGenScoreLabel.text =
                    $"보관함 {totalH}개  |  다트 {totalCheck}/{totalDarts} {sumMark}  |  caps {capKinds}종";

            // 추천 라벨 — queue_columns + ammo/holder + retry
            if (_queueGenRecommendLabel != null)
                _queueGenRecommendLabel.text =
                    $"cols {queueCols} (추천)  |  ammo/holder {ammoPerHolder:F1}  |  retries {attempts}"
                    + (stepCPlaced > 0 ? $"  |  기믹 자동배치 {stepCPlaced}" : "");

            // Soft warn 라벨
            if (_queueGenWarnLabel != null)
            {
                if (softWarn != null)
                {
                    _queueGenWarnLabel.text = $"⚠ Soft: {softWarn}";
                    _queueGenWarnLabel.color = new Color(1f, 0.65f, 0.25f);
                    _queueGenWarnLabel.gameObject.SetActive(true);
                }
                else
                {
                    _queueGenWarnLabel.text = "";
                    _queueGenWarnLabel.gameObject.SetActive(false);
                }
            }

            // Confirm 활성화
            SetQueueConfirmReady(true);

            SetStatus(softWarn == null
                ? $"Generate Queue OK — {grade} ({score:F0}%)  [retries {attempts}]"
                : $"Generate Queue OK (soft warn): {softWarn}");
        }

        // ─── 명세 §2-1, §2-6, §2-7, §2-8 — 가중치/허용 cap 빌드 ───

        /// <summary>
        /// §2-8 — 레벨별 사용 가능 cap 집합 결정. rail_capacity + PKG + purpose 가드 적용.
        /// 항상 cap20 백본 포함 보장.
        /// </summary>
        // ════════════════════════════════════════════════════════════════
        //  STEP C — 큐 기믹 자동 배치 (BalloonFlow_큐생성기_명세 v3.15)
        //  ProjectHub queue-generator.ts generateStepC 1:1 포팅 (홀더 큐 기믹 한정).
        //  순서: Linked(Chain) → Frozen → Hidden. 사슬 인접(col diff≤1)/cycle 검사 + 다중기믹 가드.
        //  RNG 은 기존 GenerateQueue 와 동일하게 UnityEngine.Random 사용.
        //  ※ 에디터는 홀더당 기믹 1개만 표현 가능 → Hidden 은 Linked/Frozen 위치를 제외
        //    (원본 TS 는 Hidden+Linked 공존 허용하나, 단일 슬롯 제약상 분리).
        // ════════════════════════════════════════════════════════════════

        private static readonly Dictionary<int, string> INTRO_LVS_QUEUE = new Dictionary<int, string>
        {
            { 11, "Hidden" }, { 21, "Chain" }, { 41, "Spawner_T" },
            { 81, "Lock_Key" }, { 141, "Spawner_O" }, { 241, "Frozen_Dart" },
        };
        private const float HIDDEN_FRONT_HALF_BIAS = 0.58f;
        private const float COLOR_W_RARE_THRESHOLD = 0.05f;
        private const float COLOR_W_UNCOMMON_THRESHOLD = 0.10f;
        private const float COLOR_W_RARE_BOOST = 3.0f;
        private const float COLOR_W_UNCOMMON_BOOST = 1.5f;
        private const float COLOR_W_COMMON_PENALTY = 0.4f;
        private static readonly Dictionary<int, float> LINKED_SAME_COLOR_PROB = new Dictionary<int, float>
        {
            { 2, 0.08f }, { 3, 0.40f }, { 4, 0.62f }, { 5, 1.0f },
        };

        private class QueueGimmickOverlay
        {
            public List<int> hiddenIds = new List<int>();
            public List<(List<int> ids, bool sameColor)> linkedGroups = new List<(List<int>, bool)>();
            public List<(int id, int health)> frozen = new List<(int, int)>();
        }

        /// <summary>STEP C 실행 + 홀더 기믹 배열에 반영. 배치된 기믹 총 개수 반환.</summary>
        private int ApplyStepC(
            List<(int color, int mag)> holders, int queueCols,
            Dictionary<int, int> colorDarts, int totalDarts,
            int hiddenN, int chainN, int frozenN,
            DifficultyPurpose diff, int lv)
        {
            var overlay = GenerateStepC(holders, queueCols, colorDarts, totalDarts, hiddenN, chainN, frozenN, diff, lv);

            int chainIdx = System.Array.IndexOf(HOLDER_GIMMICK_NAMES, "Chain");
            int frozenIdx = System.Array.IndexOf(HOLDER_GIMMICK_NAMES, "Frozen_Dart");
            int hiddenIdx = System.Array.IndexOf(HOLDER_GIMMICK_NAMES, "Hidden");
            int placed = 0;

            // Chain (Linked) — 그룹 ID 부여
            for (int gi = 0; gi < overlay.linkedGroups.Count; gi++)
                foreach (int id in overlay.linkedGroups[gi].ids)
                {
                    int col = id % queueCols, row = id / queueCols;
                    if (col < _holderCols && row < _holderRows && _holderColors[col, row] >= 0)
                    {
                        _holderGimmicks[col, row] = chainIdx;
                        _holderChainGroups[col, row] = gi;
                        placed++;
                    }
                }
            // Frozen
            foreach (var f in overlay.frozen)
            {
                int col = f.id % queueCols, row = f.id / queueCols;
                if (col < _holderCols && row < _holderRows && _holderColors[col, row] >= 0)
                {
                    _holderGimmicks[col, row] = frozenIdx;
                    _holderFrozenHP[col, row] = f.health;
                    placed++;
                }
            }
            // Hidden
            foreach (int id in overlay.hiddenIds)
            {
                int col = id % queueCols, row = id / queueCols;
                if (col < _holderCols && row < _holderRows && _holderColors[col, row] >= 0)
                {
                    _holderGimmicks[col, row] = hiddenIdx;
                    placed++;
                }
            }
            return placed;
        }

        private QueueGimmickOverlay GenerateStepC(
            List<(int color, int mag)> holders, int queueCols,
            Dictionary<int, int> colorDarts, int totalDarts,
            int hiddenN, int chainN, int frozenN,
            DifficultyPurpose diff, int lv)
        {
            // INTRO_LVS 격리 — 도입 lv면 도입 기믹만 활성
            if (INTRO_LVS_QUEUE.TryGetValue(lv, out string introKey))
            {
                if (introKey != "Hidden") hiddenN = 0;
                if (introKey != "Chain") chainN = 0;
                if (introKey != "Frozen_Dart") frozenN = 0;
            }

            int totalSh = holders.Count;
            var overlay = new QueueGimmickOverlay();

            // 1. Linked (Chain) — 큰 묶음 먼저. 그룹마다 인접/cycle 검사, 실패 시 재추첨.
            if (chainN >= 2 && totalSh >= 2)
            {
                var partitions = SplitChain(chainN, diff);
                const int MAX_GROUP_RETRY = 5, MAX_OUTER = 5;
                for (int outer = 0; outer < MAX_OUTER; outer++)
                {
                    overlay.linkedGroups.Clear();
                    var used = new HashSet<int>();
                    bool allOk = true;
                    foreach (int linkN in partitions)
                    {
                        if (linkN < 2) continue;
                        bool groupOk = false;
                        for (int attempt = 0; attempt < MAX_GROUP_RETRY; attempt++)
                        {
                            var positions = PickLinkedPositions(totalSh, queueCols, linkN, used);
                            if (positions.Count != linkN) break;
                            var tentative = new List<List<int>>();
                            foreach (var g in overlay.linkedGroups) tentative.Add(g.ids);
                            tentative.Add(positions);
                            if (!ValidateChainAdjacency(tentative, queueCols)) continue;
                            if (HasCycle(BuildChainDependencyGraph(tentative, queueCols, totalSh))) continue;
                            float p = LINKED_SAME_COLOR_PROB.TryGetValue(linkN, out float pv) ? pv : 0.1f;
                            overlay.linkedGroups.Add((positions, Random.value < p));
                            foreach (int pp in positions) used.Add(pp);
                            groupOk = true;
                            break;
                        }
                        if (!groupOk) { allOk = false; break; }
                    }
                    if (allOk) break;
                }
            }

            var linkedIds = new HashSet<int>();
            foreach (var g in overlay.linkedGroups) foreach (int id in g.ids) linkedIds.Add(id);

            // 2. Frozen — Linked 제외, front-half 편향
            if (frozenN >= 1)
            {
                var used = new HashSet<int>(linkedIds);
                for (int i = 0; i < frozenN; i++)
                {
                    int id = PickFrozenPosition(totalSh, queueCols, diff, used);
                    if (id < 0) break;
                    used.Add(id);
                    overlay.frozen.Add((id, PickFrozenHealth(totalSh, diff)));
                }
            }

            // 3. Hidden — Frozen + Linked 제외(단일 슬롯), line 0 금지, 희소색 가중
            if (hiddenN >= 1)
            {
                var exclude = new HashSet<int>(linkedIds);
                foreach (var f in overlay.frozen) exclude.Add(f.id);
                overlay.hiddenIds = PickHiddenPositions(holders, queueCols, hiddenN, exclude, colorDarts, totalDarts);
            }

            return overlay;
        }

        // §5.3.1 — splitChain: 95% 2-link 반복(+홀수 끝 3), 5% 변형
        private static List<int> SplitChain(int total, DifficultyPurpose diff)
        {
            var outList = new List<int>();
            if (total < 2) return outList;
            if (Random.value < 0.95f)
            {
                if (total == 2) { outList.Add(2); return outList; }
                if (total == 3) { outList.Add(3); return outList; }
                if (total % 2 == 0) { for (int i = 0; i < total / 2; i++) outList.Add(2); return outList; }
                for (int i = 0; i < (total - 3) / 2; i++) outList.Add(2);
                outList.Add(3);
                return outList;
            }
            if (total == 4) { outList.Add(4); return outList; }
            if (total == 5 && diff == DifficultyPurpose.SuperHard) { outList.Add(5); return outList; }
            if (total >= 6)
            {
                int big = total >= 8 ? 4 : 3;
                outList.Add(big);
                outList.AddRange(SplitChain(total - big, diff));
                return outList;
            }
            for (int i = 0; i < total / 2; i++) outList.Add(2);
            return outList;
        }

        // §5.3.2 — pickLinkedPositions: seed + row tier fallback(1,3,nRows) + col diff≤1 + 방향 가중
        private static List<int> PickLinkedPositions(int totalSh, int queueCols, int linkN, HashSet<int> exclude)
        {
            var available = new List<int>();
            for (int i = 0; i < totalSh; i++) if (!exclude.Contains(i)) available.Add(i);
            if (available.Count < linkN) return new List<int>();

            int nRows = Mathf.CeilToInt((float)totalSh / queueCols);
            int seedId = available[Mathf.FloorToInt(Random.value * available.Count)];
            int seedRow = seedId / queueCols, seedCol = seedId % queueCols;

            int[][] tiers = { new[] { 1, 1 }, new[] { 3, 1 }, new[] { nRows, 1 } };
            foreach (var tier in tiers)
            {
                int rowMax = tier[0], colMax = tier[1];
                var cands = new List<int>();
                var ws = new List<float>();
                foreach (int sid in available)
                {
                    if (sid == seedId) continue;
                    int r = sid / queueCols, c = sid % queueCols;
                    int rowDist = Mathf.Abs(r - seedRow), colDist = Mathf.Abs(c - seedCol);
                    if (rowDist > rowMax || colDist > colMax) continue;
                    float w = rowDist == 1 ? 0.74f : rowDist == 2 ? 0.18f : 0.08f;
                    int dRow = r - seedRow, dCol = c - seedCol;
                    float dirW;
                    if (dRow > 0 && dCol == 0) dirW = 1.6f;       // down
                    else if (dRow == 0 && dCol > 0) dirW = 1.0f;  // right
                    else if (dRow > 0 && dCol > 0) dirW = 0.7f;   // down-right
                    else if (dRow > 0 && dCol < 0) dirW = 0.7f;   // down-left
                    else dirW = 0.2f;                              // up / left / etc.
                    cands.Add(sid);
                    ws.Add(w * dirW);
                }
                if (cands.Count >= linkN - 1)
                {
                    var picked = WeightedSampleNoReplace(cands, ws, linkN - 1);
                    var result = new List<int> { seedId };
                    result.AddRange(picked);
                    return result;
                }
            }
            return new List<int>();
        }

        // v3.12 — 사슬 인접: (col,row) 정렬 후 연속 col diff ≤ 1
        private static bool ValidateChainAdjacency(List<List<int>> groups, int queueCols)
        {
            foreach (var grp in groups)
            {
                if (grp.Count < 2) continue;
                var sorted = new List<int>(grp);
                sorted.Sort((a, b) =>
                {
                    int ca = a % queueCols, cb = b % queueCols;
                    if (ca != cb) return ca.CompareTo(cb);
                    return (a / queueCols).CompareTo(b / queueCols);
                });
                for (int i = 1; i < sorted.Count; i++)
                    if ((sorted[i] % queueCols) - (sorted[i - 1] % queueCols) > 1) return false;
            }
            return true;
        }

        // v3.15 — 그룹 의존성 그래프: row>0 보관함은 같은 col 위쪽 다른 그룹에 의존
        private static Dictionary<int, HashSet<int>> BuildChainDependencyGraph(List<List<int>> groups, int queueCols, int totalSh)
        {
            var sidToGid = new Dictionary<int, int>();
            for (int gid = 0; gid < groups.Count; gid++)
                foreach (int sid in groups[gid]) sidToGid[sid] = gid;
            var deps = new Dictionary<int, HashSet<int>>();
            for (int gid = 0; gid < groups.Count; gid++) deps[gid] = new HashSet<int>();
            for (int gid = 0; gid < groups.Count; gid++)
                foreach (int sid in groups[gid])
                {
                    int row = sid / queueCols, col = sid % queueCols;
                    if (row == 0) continue;
                    for (int r2 = 0; r2 < row; r2++)
                    {
                        int sid2 = r2 * queueCols + col;
                        if (sid2 >= totalSh) continue;
                        if (sidToGid.TryGetValue(sid2, out int gid2) && gid2 != gid) deps[gid].Add(gid2);
                    }
                }
            return deps;
        }

        private static bool HasCycle(Dictionary<int, HashSet<int>> deps)
        {
            var color = new Dictionary<int, int>();   // 0=white,1=gray,2=black
            foreach (var k in deps.Keys) color[k] = 0;
            foreach (var gid in deps.Keys)
                if (color[gid] == 0 && HasCycleDfs(gid, deps, color)) return true;
            return false;
        }
        private static bool HasCycleDfs(int node, Dictionary<int, HashSet<int>> deps, Dictionary<int, int> color)
        {
            color[node] = 1;
            if (deps.TryGetValue(node, out var nexts))
                foreach (int next in nexts)
                {
                    int c = color.TryGetValue(next, out int cv) ? cv : 0;
                    if (c == 1) return true;
                    if (c == 0 && HasCycleDfs(next, deps, color)) return true;
                }
            color[node] = 2;
            return false;
        }

        private static float FrozenFrontHalfBias(DifficultyPurpose d)
        {
            switch (d)
            {
                case DifficultyPurpose.SuperHard: return 0.80f;
                case DifficultyPurpose.Normal:
                case DifficultyPurpose.Intro: return 0.60f;
                default: return 0.55f; // Tutorial / Rest / Hard
            }
        }

        // §5.5 — Frozen 위치: front-half 편향
        private static int PickFrozenPosition(int totalSh, int queueCols, DifficultyPurpose diff, HashSet<int> exclude)
        {
            int halfLineCount = Mathf.CeilToInt(Mathf.CeilToInt((float)totalSh / queueCols) / 2f);
            int halfHolderIdx = halfLineCount * queueCols;
            float frontBias = FrozenFrontHalfBias(diff);
            var available = new List<int>();
            var weights = new List<float>();
            for (int i = 0; i < totalSh; i++)
            {
                if (exclude.Contains(i)) continue;
                available.Add(i);
                weights.Add(i < halfHolderIdx ? frontBias : (1f - frontBias));
            }
            if (available.Count == 0) return -1;
            var picked = WeightedSampleNoReplace(available, weights, 1);
            return picked.Count > 0 ? picked[0] : -1;
        }

        // §5.5 — Frozen health: PF picked 분포 v1.2.23 (난이도별)
        private static int PickFrozenHealth(int totalSh, DifficultyPurpose diff)
        {
            int[] vals; float[] ws;
            switch (diff)
            {
                case DifficultyPurpose.Hard:
                    vals = new[] { 6, 16, 18, 24, 26, 30, 32, 42 };
                    ws = new[] { 0.08f, 0.20f, 0.10f, 0.25f, 0.10f, 0.10f, 0.10f, 0.07f };
                    break;
                case DifficultyPurpose.SuperHard:
                    vals = new[] { 16, 24, 30, 42, 52, 61, 32 };
                    ws = new[] { 0.10f, 0.15f, 0.15f, 0.15f, 0.20f, 0.15f, 0.10f };
                    break;
                case DifficultyPurpose.Normal:
                    vals = new[] { 6, 8, 10, 16, 24, 18, 30 };
                    ws = new[] { 0.20f, 0.15f, 0.10f, 0.20f, 0.15f, 0.10f, 0.10f };
                    break;
                default: // Tutorial / Rest / Intro
                    vals = new[] { 6, 8, 10, 16, 4 };
                    ws = new[] { 0.40f, 0.25f, 0.20f, 0.10f, 0.05f };
                    break;
            }
            float totalW = 0f;
            foreach (float w in ws) totalW += w;
            float roll = Random.value * totalW;
            for (int i = 0; i < vals.Length; i++)
            {
                roll -= ws[i];
                if (roll <= 0f) return Mathf.Max(2, Mathf.Min(totalSh - 1, vals[i]));
            }
            return Mathf.Max(2, Mathf.Min(totalSh - 1, vals[vals.Length - 1]));
        }

        // §5.4 — Hidden 위치: front-half 약한 편향 + 희소색 가중 + line 0 금지(Hard Rule)
        private static List<int> PickHiddenPositions(
            List<(int color, int mag)> holders, int queueCols, int n,
            HashSet<int> exclude, Dictionary<int, int> colorDarts, int totalDarts)
        {
            int totalSh = holders.Count;
            int halfLineCount = Mathf.CeilToInt(Mathf.CeilToInt((float)totalSh / queueCols) / 2f);
            int halfHolderIdx = halfLineCount * queueCols;
            int lineFirstSize = queueCols;
            var available = new List<int>();
            var weights = new List<float>();
            for (int i = 0; i < totalSh; i++)
            {
                if (exclude.Contains(i)) continue;
                if (i < lineFirstSize) continue;   // Hidden line 0 금지
                available.Add(i);
                float positionW = i < halfHolderIdx ? HIDDEN_FRONT_HALF_BIAS : (1f - HIDDEN_FRONT_HALF_BIAS);
                weights.Add(positionW * ColorWeightForHidden(holders[i].color, colorDarts, totalDarts));
            }
            return WeightedSampleNoReplace(available, weights, n);
        }
        private static float ColorWeightForHidden(int color, Dictionary<int, int> colorDarts, int totalDarts)
        {
            float share = totalDarts > 0
                ? (float)(colorDarts.TryGetValue(color, out int cd) ? cd : 0) / totalDarts : 0f;
            if (share <= COLOR_W_RARE_THRESHOLD) return COLOR_W_RARE_BOOST;
            if (share <= COLOR_W_UNCOMMON_THRESHOLD) return COLOR_W_UNCOMMON_BOOST;
            return COLOR_W_COMMON_PENALTY;
        }

        // 가중 비복원 추출 (TS weightedSample 1:1)
        private static List<int> WeightedSampleNoReplace(List<int> items, List<float> weights, int k)
        {
            var remaining = new List<int>(items);
            var remW = new List<float>(weights);
            var outList = new List<int>();
            int pickK = Mathf.Min(k, remaining.Count);
            for (int i = 0; i < pickK; i++)
            {
                float total = 0f;
                for (int j = 0; j < remW.Count; j++) total += remW[j];
                if (total <= 0f) break;
                float roll = Random.value * total;
                int chosen = -1;
                for (int j = 0; j < remaining.Count; j++)
                {
                    roll -= remW[j];
                    if (roll <= 0f) { chosen = j; break; }
                }
                if (chosen < 0) chosen = remaining.Count - 1;
                outList.Add(remaining[chosen]);
                remaining.RemoveAt(chosen);
                remW.RemoveAt(chosen);
            }
            return outList;
        }

        private HashSet<int> BuildAllowedCaps(int levelId, DifficultyPurpose purpose, int railCapacity, int dartCapMax)
        {
            var caps = new HashSet<int> { 10, 20, 30, 40, 50 };

            // Step 1: rail_capacity / dart cap max 가드
            if (railCapacity < 80) { caps.Remove(40); caps.Remove(50); }
            if (railCapacity < 120) caps.Remove(50);
            caps.RemoveWhere(c => c > dartCapMax);

            // Step 2: cap30 가드 (§2-7)
            if (purpose == DifficultyPurpose.Tutorial || purpose == DifficultyPurpose.Rest)
                caps.Remove(30);

            // Step 3: cap50 가드 (§2-6)
            int pkg = (levelId - 1) / 20 + 1;
            bool cap50Allowed = true;
            if (pkg < 2) cap50Allowed = false;
            else if (purpose == DifficultyPurpose.Tutorial || purpose == DifficultyPurpose.Rest || purpose == DifficultyPurpose.Normal || purpose == DifficultyPurpose.Intro)
                cap50Allowed = false;
            else if (pkg >= 11)
                cap50Allowed = (levelId == 219 || levelId == 249 || levelId == 299);
            // PKG 2-10: Hard/SuperHard 만 허용 (위 if 분기에서 차단되지 않은 경우)
            if (!cap50Allowed) caps.Remove(50);

            // Step 4: cap20 백본 보장
            caps.Add(20);

            return caps;
        }

        /// <summary>
        /// §2-1 — 난이도별 cap 가중치 테이블에서 허용 cap 만 마스킹 + cap20 흡수 + 재정규화.
        /// Rest 는 §2-7 cap30 금지 흡수가 이미 base 에 적용된 별도 표 사용.
        /// </summary>
        private Dictionary<int, float> BuildCapWeights(int diffIdx, DifficultyPurpose purpose, HashSet<int> allowed)
        {
            float[] baseWeights = purpose == DifficultyPurpose.Rest
                ? CAP_WEIGHTS_REST
                : CAP_WEIGHTS_BASE[Mathf.Clamp(diffIdx, 0, CAP_WEIGHTS_BASE.Length - 1)];
            int[] keys = CAP_KEYS[Mathf.Clamp(diffIdx, 0, CAP_KEYS.Length - 1)];

            // §3 step 4 v5.1 (2026-05-13) — 정규화 방식: 차단 cap 은 단순 제외 후 남은 cap 비율 보존하며 합 1.0.
            //   이전 v4 는 차단 cap 가중치를 cap20 에 단순 흡수 → cap40 비율 손실. v5.1 은 비율 보존.
            //   cap20 백본은 BuildAllowedCaps(§2-8) 가 항상 20 을 allowed 에 포함시켜 보장 (여기서 흡수 X).
            var w = new Dictionary<int, float>();
            for (int i = 0; i < keys.Length; i++)
            {
                int cap = keys[i];
                float wi = baseWeights[i];
                if (wi <= 0f) continue;
                if (allowed.Contains(cap))
                    w[cap] = wi;
            }
            // cap20 백본 안전장치 — allowed 에 20 있으나 base 가중치가 0 인 극단 케이스 최소 비율 부여.
            if (allowed.Contains(20) && !w.ContainsKey(20)) w[20] = 0.01f;
            // v5.1 정규화 — 남은 cap 의 v5.1 비율 보존하며 합 1.0.
            float sum = 0f;
            foreach (var v in w.Values) sum += v;
            if (sum > 0f)
            {
                var keysList = new List<int>(w.Keys);
                foreach (var k in keysList) w[k] /= sum;
            }
            return w;
        }

        /// <summary>
        /// §2-3 — holder_count + purpose 기반 queue_columns 자동 추천.
        /// PF picked 1-300 분포 검증된 룰. clamp [2,5].
        /// </summary>
        private static int RecommendQueueColumns(int holderCount, DifficultyPurpose purpose)
        {
            int baseCols;
            if (holderCount <= 20) baseCols = 3;
            else if (holderCount <= 35) baseCols = 3;
            else if (holderCount <= 55) baseCols = 4;
            else baseCols = 4;

            switch (purpose)
            {
                case DifficultyPurpose.Tutorial:
                    return Mathf.Clamp(baseCols - 1, 2, 4);
                case DifficultyPurpose.SuperHard:
                    return Mathf.Clamp(baseCols + 1, 3, 5);
                default:
                    return Mathf.Clamp(baseCols, 2, 5);
            }
        }

        /// <summary>
        /// §3 STEP A v2 — 색상별 holder 수 추정(round(color_darts/AVG_CAP)) → 가중치 추첨 → balance_to_target 합계 보정.
        /// 결과 합계 == colorDarts 보장 (모두 10의 배수).
        /// </summary>
        private List<int> DecomposeMagazinesV2(
            int colorDarts, Dictionary<int, float> weights, HashSet<int> allowed, int dartCapMax)
        {
            // 1. 색상별 holder 수 추정
            int estimated = Mathf.Max(1, Mathf.RoundToInt((float)colorDarts / AVG_CAP));

            // 2. 추첨 가능한 cap 후보 — dartCapMax 이하 + allowed
            var candidates = new List<int>();
            var candWeights = new List<float>();
            foreach (var kvp in weights)
            {
                if (kvp.Key > dartCapMax) continue;
                if (!allowed.Contains(kvp.Key)) continue;
                if (kvp.Value <= 0f) continue;
                candidates.Add(kvp.Key);
                candWeights.Add(kvp.Value);
            }
            // 최소 보장 — cap20 백본
            if (candidates.Count == 0) { candidates.Add(20); candWeights.Add(1f); }

            // 3. holder 개수만큼 cap 추첨
            var result = new List<int>(estimated);
            for (int i = 0; i < estimated; i++)
                result.Add(WeightedRandomPick(candidates, candWeights));

            // 4. 합계 보정
            BalanceToTarget(result, colorDarts, candidates, dartCapMax);
            return result;
        }

        private static int WeightedRandomPick(List<int> values, List<float> weights)
        {
            float total = 0f;
            for (int i = 0; i < weights.Count; i++) total += weights[i];
            if (total <= 0f) return values[Random.Range(0, values.Count)];
            float roll = Random.value * total;
            float acc = 0f;
            for (int i = 0; i < values.Count; i++)
            {
                acc += weights[i];
                if (roll <= acc) return values[i];
            }
            return values[values.Count - 1];
        }

        /// <summary>
        /// §3 balance_to_target — 합계가 target 과 다르면 cap 교체/추가/제거로 조정.
        /// 모든 cap 이 10 배수이고 target 도 10 배수이므로 ±10 단위로 수렴.
        /// </summary>
        private static void BalanceToTarget(List<int> holders, int target, List<int> candidates, int capMax)
        {
            int safety = 0;
            while (safety++ < 200)
            {
                int sum = 0;
                for (int i = 0; i < holders.Count; i++) sum += holders[i];
                int diff = target - sum;
                if (diff == 0) return;

                if (diff > 0)
                {
                    // 부족 — 가장 작은 cap 을 한 단계 큰 cap 으로 교체
                    int smallIdx = FindMinIndex(holders);
                    if (smallIdx >= 0)
                    {
                        int cur = holders[smallIdx];
                        int next = NextCapUp(cur, candidates, capMax, diff);
                        if (next > cur)
                        {
                            holders[smallIdx] = next;
                            continue;
                        }
                    }
                    // 교체로 안 되면 새 holder 추가 (cap20 우선, 그 외 가능한 가장 큰 cap)
                    int addCap = Mathf.Min(capMax, 20);
                    if (!candidates.Contains(addCap))
                    {
                        addCap = 20;
                        if (!candidates.Contains(addCap))
                            addCap = candidates.Count > 0 ? candidates[0] : 10;
                    }
                    holders.Add(addCap);
                }
                else
                {
                    // 초과 — 가장 큰 cap 을 한 단계 작은 cap 으로 교체
                    int bigIdx = FindMaxIndex(holders);
                    if (bigIdx >= 0)
                    {
                        int cur = holders[bigIdx];
                        int next = NextCapDown(cur, candidates, -diff);
                        if (next >= 10 && next < cur)
                        {
                            holders[bigIdx] = next;
                            continue;
                        }
                        // 더 작은 cap 으로 못 가면 holder 제거 (cap 만큼 sum 감소)
                        if (holders.Count > 1)
                        {
                            holders.RemoveAt(bigIdx);
                            continue;
                        }
                    }
                    // 안전 종료
                    break;
                }
            }
        }

        private static int FindMinIndex(List<int> list)
        {
            if (list.Count == 0) return -1;
            int idx = 0;
            for (int i = 1; i < list.Count; i++) if (list[i] < list[idx]) idx = i;
            return idx;
        }
        private static int FindMaxIndex(List<int> list)
        {
            if (list.Count == 0) return -1;
            int idx = 0;
            for (int i = 1; i < list.Count; i++) if (list[i] > list[idx]) idx = i;
            return idx;
        }

        private static int NextCapUp(int current, List<int> candidates, int capMax, int maxDelta)
        {
            int best = current;
            for (int i = 0; i < candidates.Count; i++)
            {
                int c = candidates[i];
                if (c <= current) continue;
                if (c > capMax) continue;
                int delta = c - current;
                if (delta > maxDelta) continue; // diff 초과하는 교체는 금지
                if (c > best) best = c;
            }
            return best;
        }

        private static int NextCapDown(int current, List<int> candidates, int maxDelta)
        {
            int best = current;
            for (int i = 0; i < candidates.Count; i++)
            {
                int c = candidates[i];
                if (c >= current) continue;
                if (c < 10) continue;
                int delta = current - c;
                if (delta > maxDelta) continue;
                if (c < best || best == current) best = c;
            }
            return best;
        }

        /// <summary>
        /// §4-2 step 5 — 첫 3행 깊이 가드 (Hard Rule).
        /// 첫 (queueCols × 3) 보관함 내 depth 0 매칭 가능 보관함이 1개 이상 보장.
        /// 부족하면 뒤쪽(row 4+) depth 0 보관함과 swap 시도. 행/열 연속 위반 시 다음 후보.
        /// 모든 후보 실패 → false (호출자가 큐 재생성).
        /// </summary>
        private bool EnforceFirst3RowsDepthGuard(
            List<(int color, int mag)> list, int cols,
            Dictionary<int, int> colorDepth,
            int maxRow, int maxCol)
        {
            int firstZone = Mathf.Min(cols * 3, list.Count);
            int rows = (list.Count + cols - 1) / cols;

            // 사전 조건: 전체 list 에 depth 0 holder 가 0 개면 가드 skip (필드 자체에 외곽 색 없음 → 가드 의미 없음).
            // 명세 §4-2 step 5 는 swap 후보가 있다는 가정. 후보 0 개일 때 무한 fail 회피.
            int totalDepth0 = 0;
            for (int i = 0; i < list.Count; i++)
            {
                int d = colorDepth.ContainsKey(list[i].color) ? colorDepth[list[i].color] : 0;
                if (d == 0) totalDepth0++;
            }
            if (totalDepth0 == 0) return true;

            bool HasDepth0InFirst()
            {
                for (int i = 0; i < firstZone; i++)
                {
                    int d = colorDepth.ContainsKey(list[i].color) ? colorDepth[list[i].color] : 0;
                    if (d == 0) return true;
                }
                return false;
            }

            if (HasDepth0InFirst()) return true;

            // Phase 1 — strict swap (행/열 연속 제약 둘 다 만족하는 swap)
            for (int k = firstZone; k < list.Count; k++)
            {
                int d = colorDepth.ContainsKey(list[k].color) ? colorDepth[list[k].color] : 0;
                if (d != 0) continue;

                for (int i = 0; i < firstZone; i++)
                {
                    var a = list[i]; var b = list[k];
                    list[i] = b; list[k] = a;

                    int ic = i % cols, ir = i / cols;
                    int kc = k % cols, kr = k / cols;
                    bool okI = !ViolatesConsecutive(list, cols, rows, ir, ic, maxRow, maxCol);
                    bool okK = !ViolatesConsecutive(list, cols, rows, kr, kc, maxRow, maxCol);

                    if (okI && okK) return true; // swap 유지
                    list[i] = a; list[k] = b;
                }
            }

            // Phase 2 — best-effort fallback (consec 위반 허용, depth 가드만 만족).
            // 명세 §4-2 step 5 의 의도 (첫 3행 매칭 보장) 가 §4-3 행/열 연속 (Soft Rule) 보다 우선.
            // 작은 보드 + tight maxRow/maxCol 조합에서 strict 가 매번 실패해 무한 재생성되는 케이스 회피.
            for (int k = firstZone; k < list.Count; k++)
            {
                int d = colorDepth.ContainsKey(list[k].color) ? colorDepth[list[k].color] : 0;
                if (d != 0) continue;
                var a = list[0]; var b = list[k];
                list[0] = b; list[k] = a;
                return true;
            }

            return false; // 뒤쪽에도 depth 0 없음 — 호출자 재생성 (현실적으로 totalDepth0==0 early return 에서 잡힘)
        }

        // ─── 명세 §6 Hard/Soft Rule 검증 ───

        /// <summary>§6 Hard Rule — 모두 통과해야 함. 실패 시 사유 문자열 반환 (성공 시 null).</summary>
        private string ValidateHardRules(
            List<(int color, int mag)> list, int totalDarts, int dartCapMax, int queueCols)
        {
            if (list == null || list.Count == 0) return "보관함이 없음";

            int sum = 0;
            for (int i = 0; i < list.Count; i++)
            {
                int m = list[i].mag;
                if (m != 10 && m != 20 && m != 30 && m != 40 && m != 50)
                    return $"cap not in {{10,20,30,40,50}}: {m}";
                if (m > dartCapMax) return $"cap {m} > dart_capacity_max {dartCapMax}";
                sum += m;
            }
            if (sum != totalDarts) return $"sum {sum} != total {totalDarts}";

            if (queueCols < 2 || queueCols > 5) return $"queueColumns {queueCols} not in [2,5]";

            return null;
        }

        /// <summary>§6 Soft Rule — 경고만. 경고 메시지 반환 (없으면 null).</summary>
        private string ValidateSoftRules(
            List<(int color, int mag)> list, Dictionary<int, int> colorDartsRounded)
        {
            if (list == null || list.Count == 0) return null;

            int total = 0, cap20 = 0;
            var capSet = new HashSet<int>();
            foreach (var m in list)
            {
                total += m.mag;
                if (m.mag == 20) cap20 += m.mag;
                capSet.Add(m.mag);
            }

            var warns = new List<string>();
            float cap20Ratio = total > 0 ? (float)cap20 / total : 0f;
            if (cap20Ratio < 0.50f)
                warns.Add($"cap20 ratio {cap20Ratio:P0} < 50% (PF avg 67.5%)");

            if (capSet.Count > 4)
                warns.Add($"cap kinds {capSet.Count} > 4");

            float ammoPerHolder = list.Count > 0 ? (float)total / list.Count : 0f;
            if (ammoPerHolder < 18f || ammoPerHolder > 25f)
                warns.Add($"ammo/holder {ammoPerHolder:F1} out of [18,25] (PF avg 21)");

            return warns.Count == 0 ? null : string.Join("; ", warns);
        }

        private static int CountCapKinds(List<(int color, int mag)> list)
        {
            var set = new HashSet<int>();
            foreach (var m in list) set.Add(m.mag);
            return set.Count;
        }

        private List<(int color, int mag)> LayoutByDepth(
            List<(int color, int mag)> magazines,
            Dictionary<int, int> colorDepth,
            int diffIdx)
        {
            // depth별 그룹핑
            var depth0 = new List<(int, int)>();
            var depth12 = new List<(int, int)>();

            foreach (var m in magazines)
            {
                int depth = colorDepth.ContainsKey(m.color) ? colorDepth[m.color] : 0;
                if (depth == 0) depth0.Add(m);
                else depth12.Add(m);
            }

            // 각 그룹 내 셔플
            Shuffle(depth0);
            Shuffle(depth12);

            // 앞 50%에 depth 0 비율
            float[] ratioRange = DEPTH0_FRONT_RATIO[diffIdx];
            float targetRatio = Random.Range(ratioRange[0], ratioRange[1]);
            int halfCount = magazines.Count / 2;
            int frontDepth0Count = Mathf.RoundToInt(halfCount * targetRatio);
            frontDepth0Count = Mathf.Min(frontDepth0Count, depth0.Count);

            var sorted = new List<(int, int)>();
            // 앞쪽: depth 0 일부 + depth 1~2 일부
            sorted.AddRange(depth0.GetRange(0, frontDepth0Count));
            int frontDepth12 = Mathf.Min(halfCount - frontDepth0Count, depth12.Count);
            if (frontDepth12 > 0) sorted.AddRange(depth12.GetRange(0, frontDepth12));

            // 뒤쪽: 나머지
            if (frontDepth0Count < depth0.Count)
                sorted.AddRange(depth0.GetRange(frontDepth0Count, depth0.Count - frontDepth0Count));
            if (frontDepth12 < depth12.Count)
                sorted.AddRange(depth12.GetRange(frontDepth12, depth12.Count - frontDepth12));

            // 1D 연속 제한은 deprecated — GenerateQueue에서 2D 그리드 배치 후
            // EnforceGridConsecutiveLimit로 행/열 동시 체크

            return sorted;
        }

        /// <summary>
        /// (r,c)를 포함하는 최대 4개 2×2 정사각형 중 단색(같은 색 4칸)이 하나라도 있으면 true.
        /// 행/열 run 체크(ViolatesConsecutive)가 Normal(maxRow=2,maxCol=2)에서 2×2 정사각형을 못 잡는 보완.
        /// </summary>
        private bool InMonoSquare(List<(int color, int mag)> list, int cols, int rows, int r, int c)
        {
            int[][] tops = { new[] { r, c }, new[] { r, c - 1 }, new[] { r - 1, c }, new[] { r - 1, c - 1 } };
            foreach (var t in tops)
            {
                int tr = t[0], tc = t[1];
                if (tr < 0 || tc < 0 || tr + 1 >= rows || tc + 1 >= cols) continue;
                int i00 = tr * cols + tc, i01 = tr * cols + (tc + 1), i10 = (tr + 1) * cols + tc, i11 = (tr + 1) * cols + (tc + 1);
                if (i00 >= list.Count || i01 >= list.Count || i10 >= list.Count || i11 >= list.Count) continue;
                int col = list[i00].color;
                if (list[i01].color == col && list[i10].color == col && list[i11].color == col) return true;
            }
            return false;
        }

        /// <summary>
        /// 2D 그리드 연속 제한 (명세 v1 §4-2 #4) + 2×2 정사각형 뭉침 차단.
        /// row-major (i % cols, i / cols)로 배치된 리스트에서
        /// 행(가로)/열(세로) 연속 + 같은색 2×2 정사각형을 swap으로 해소.
        /// swap 대상이 없으면 현재 배치 유지 (Soft Rule).
        /// </summary>
        private void EnforceGridConsecutiveLimit(
            List<(int color, int mag)> list, int cols, int maxRow, int maxCol)
        {
            if (list.Count == 0 || cols <= 0) return;
            int rows = (list.Count + cols - 1) / cols;

            // 2×2 차단을 추가하면서 수렴 위해 3 pass.
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    int c = i % cols;
                    int r = i / cols;

                    if (ViolatesConsecutive(list, cols, rows, r, c, maxRow, maxCol)
                        || InMonoSquare(list, cols, rows, r, c))
                    {
                        // 뒤쪽에서 swap 후 양쪽 다 (연속 + 2×2) 만족하는 후보 찾기
                        for (int k = i + 1; k < list.Count; k++)
                        {
                            if (list[k].color == list[i].color) continue;

                            // swap 시뮬레이션: 두 위치 모두 제약을 만족하는지 체크
                            var a = list[i]; var b = list[k];
                            list[i] = b; list[k] = a;

                            int kc = k % cols, kr = k / cols;
                            bool okI = !ViolatesConsecutive(list, cols, rows, r, c, maxRow, maxCol)
                                       && !InMonoSquare(list, cols, rows, r, c);
                            bool okK = !ViolatesConsecutive(list, cols, rows, kr, kc, maxRow, maxCol)
                                       && !InMonoSquare(list, cols, rows, kr, kc);

                            if (okI && okK) break; // swap 유지
                            // 복원
                            list[i] = a; list[k] = b;
                        }
                    }
                }
            }
        }

        /// <summary>(r,c) 위치에 배치된 보관함이 행/열 연속 max를 위반하는지.</summary>
        private bool ViolatesConsecutive(
            List<(int color, int mag)> list, int cols, int rows,
            int r, int c, int maxRow, int maxCol)
        {
            int idx = r * cols + c;
            if (idx >= list.Count) return false;
            int color = list[idx].color;

            // 행(가로) 좌측 연속 카운트
            int rowRun = 1;
            for (int cc = c - 1; cc >= 0; cc--)
            {
                int id = r * cols + cc;
                if (id >= list.Count) break;
                if (list[id].color != color) break;
                rowRun++;
            }
            if (rowRun > maxRow) return true;

            // 열(세로) 위쪽 연속 카운트
            int colRun = 1;
            for (int rr = r - 1; rr >= 0; rr--)
            {
                int id = rr * cols + c;
                if (id >= list.Count) break;
                if (list[id].color != color) break;
                colRun++;
            }
            if (colRun > maxCol) return true;

            return false;
        }

        /// <summary>
        /// 4면 스캔으로 color_dependency 계산 (명세 v1 §1).
        /// 각 방향(상/하/좌/우) 스캔 시, 같은 라인에서 먼저 만난 색 A가 나중에 만난 색 B를 가림.
        /// → dependency[B].add(A)
        /// </summary>
        private Dictionary<int, HashSet<int>> CalcColorDependency(Dictionary<int, int> colorDarts)
        {
            var dep = new Dictionary<int, HashSet<int>>();
            foreach (var kvp in colorDarts)
                dep[kvp.Key] = new HashSet<int>();

            // 각 스캔 라인에서 순서대로 만난 unique 색상 시퀀스 수집 후
            // 앞선 색이 뒤에 있는 색을 가림
            void ProcessLine(List<int> seq)
            {
                for (int i = 0; i < seq.Count; i++)
                {
                    int b = seq[i];
                    if (!dep.ContainsKey(b)) continue;
                    for (int j = 0; j < i; j++)
                    {
                        int a = seq[j];
                        if (a != b) dep[b].Add(a);
                    }
                }
            }

            // 상단 (각 열, row 0→max)
            for (int c = 0; c < _gridCols; c++)
            {
                var seq = new List<int>();
                for (int r = 0; r < _gridRows; r++)
                {
                    int ci = _balloonColors[c, r];
                    if (ci >= 0 && !seq.Contains(ci)) seq.Add(ci);
                }
                ProcessLine(seq);
            }
            // 하단 (각 열, row max→0)
            for (int c = 0; c < _gridCols; c++)
            {
                var seq = new List<int>();
                for (int r = _gridRows - 1; r >= 0; r--)
                {
                    int ci = _balloonColors[c, r];
                    if (ci >= 0 && !seq.Contains(ci)) seq.Add(ci);
                }
                ProcessLine(seq);
            }
            // 좌측 (각 행, col 0→max)
            for (int r = 0; r < _gridRows; r++)
            {
                var seq = new List<int>();
                for (int c = 0; c < _gridCols; c++)
                {
                    int ci = _balloonColors[c, r];
                    if (ci >= 0 && !seq.Contains(ci)) seq.Add(ci);
                }
                ProcessLine(seq);
            }
            // 우측 (각 행, col max→0)
            for (int r = 0; r < _gridRows; r++)
            {
                var seq = new List<int>();
                for (int c = _gridCols - 1; c >= 0; c--)
                {
                    int ci = _balloonColors[c, r];
                    if (ci >= 0 && !seq.Contains(ci)) seq.Add(ci);
                }
                ProcessLine(seq);
            }

            return dep;
        }

        private Dictionary<int, int> CalcColorDepth(Dictionary<int, int> colorDarts)
        {
            var depth = new Dictionary<int, int>();
            var exposureCount = new Dictionary<int, int>();
            int totalEdges = 0;

            // 4면 스캔: 상/하/좌/우에서 첫 풍선 색상
            // 상단 (row=0, 각 col)
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                    if (_balloonColors[c, r] >= 0)
                    {
                        int ci = _balloonColors[c, r];
                        exposureCount[ci] = (exposureCount.ContainsKey(ci) ? exposureCount[ci] : 0) + 1;
                        totalEdges++;
                        break;
                    }
            // 하단 (row=max, 각 col)
            for (int c = 0; c < _gridCols; c++)
                for (int r = _gridRows - 1; r >= 0; r--)
                    if (_balloonColors[c, r] >= 0)
                    {
                        int ci = _balloonColors[c, r];
                        exposureCount[ci] = (exposureCount.ContainsKey(ci) ? exposureCount[ci] : 0) + 1;
                        totalEdges++;
                        break;
                    }
            // 좌측 (col=0, 각 row)
            for (int r = 0; r < _gridRows; r++)
                for (int c = 0; c < _gridCols; c++)
                    if (_balloonColors[c, r] >= 0)
                    {
                        int ci = _balloonColors[c, r];
                        exposureCount[ci] = (exposureCount.ContainsKey(ci) ? exposureCount[ci] : 0) + 1;
                        totalEdges++;
                        break;
                    }
            // 우측 (col=max, 각 row)
            for (int r = 0; r < _gridRows; r++)
                for (int c = _gridCols - 1; c >= 0; c--)
                    if (_balloonColors[c, r] >= 0)
                    {
                        int ci = _balloonColors[c, r];
                        exposureCount[ci] = (exposureCount.ContainsKey(ci) ? exposureCount[ci] : 0) + 1;
                        totalEdges++;
                        break;
                    }

            // depth 판정
            foreach (var kvp in colorDarts)
            {
                int ci = kvp.Key;
                float ratio = totalEdges > 0 && exposureCount.ContainsKey(ci)
                    ? (float)exposureCount[ci] / totalEdges
                    : 0f;

                if (ratio > 0.5f) depth[ci] = 0;
                else if (ratio > 0.2f) depth[ci] = 1;
                else depth[ci] = 2;
            }

            return depth;
        }

        /// <summary>
        /// 난이도 점수 (명세 v1 §7). 순서 기반 fireable 로직.
        /// absolute = Σ fireable(i) × mag(i) / rail_capacity
        /// fireable(i):
        ///   0 = depth 0 (즉시 발사 가능)
        ///   0 = 가림 색상 전부가 이전 보관함들에서 50%+ 이미 소모됨
        ///   1 = 가림 색상 중 하나라도 아직 안 벗겨짐 (다트 레일에 잔류)
        /// max_possible = (depth > 0 다트 합) / rail_capacity
        /// relative = absolute / max_possible × 100%
        /// </summary>
        private float CalcDifficultyScore(
            List<(int color, int mag)> magazines,
            Dictionary<int, int> colorDepth,
            Dictionary<int, HashSet<int>> colorDependency,
            Dictionary<int, int> colorDartsTotal,
            int railCapacity)
        {
            if (railCapacity <= 0) return 0f;

            // max_possible = 안쪽 색 다트 합 (최악 = 전부 쌓임)
            int innerDarts = 0;
            foreach (var m in magazines)
            {
                int d = colorDepth.ContainsKey(m.color) ? colorDepth[m.color] : 0;
                if (d > 0) innerDarts += m.mag;
            }
            float maxPossible = (float)innerDarts / railCapacity;
            if (maxPossible <= 0f) return 0f; // 전부 외곽 → Easy

            // 순서대로 보관함 처리: 각 색 누적 소모량 추적
            var consumed = new Dictionary<int, int>(); // color → 누적 소모 다트 수
            float absolute = 0f;

            foreach (var m in magazines)
            {
                int color = m.color;
                int depth = colorDepth.ContainsKey(color) ? colorDepth[color] : 0;

                bool fireable0 = false;
                if (depth == 0)
                {
                    fireable0 = true; // 최외곽 → 즉시 발사
                }
                else if (colorDependency.ContainsKey(color))
                {
                    // 가림 색상이 전부 50%+ 소모됐는지
                    var blockers = colorDependency[color];
                    if (blockers.Count == 0)
                    {
                        fireable0 = true; // 가림 없음
                    }
                    else
                    {
                        bool allUnblocked = true;
                        foreach (int blocker in blockers)
                        {
                            int total = colorDartsTotal.ContainsKey(blocker) ? colorDartsTotal[blocker] : 0;
                            int used = consumed.ContainsKey(blocker) ? consumed[blocker] : 0;
                            if (total <= 0 || (float)used / total < 0.5f)
                            {
                                allUnblocked = false;
                                break;
                            }
                        }
                        fireable0 = allUnblocked;
                    }
                }

                if (!fireable0)
                {
                    absolute += (float)m.mag / railCapacity;
                }

                // 소모 누적 (발사 가능 여부와 관계없이 보관함은 소비됨)
                if (consumed.ContainsKey(color)) consumed[color] += m.mag;
                else consumed[color] = m.mag;
            }

            float relative = Mathf.Clamp01(absolute / maxPossible) * 100f;
            return relative;
        }

        // ── 유틸리티 ──

        private static int GetDifficultyIndex(DifficultyPurpose d)
        {
            switch (d)
            {
                case DifficultyPurpose.Tutorial:
                case DifficultyPurpose.Rest:
                    return 0; // Easy
                case DifficultyPurpose.Normal:
                case DifficultyPurpose.Intro:
                    return 1;
                case DifficultyPurpose.Hard:
                    return 2;
                case DifficultyPurpose.SuperHard:
                    return 3;
                default: return 1;
            }
        }

        private static int GetDartCapacityMax(int railCapacity)
        {
            if (railCapacity <= 40) return 30;
            if (railCapacity <= 80) return 40;
            return 50;
        }

        private static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }

        #endregion

        private bool _levelLoaded; // 레벨이 로드/편집된 상태인지

        private void TestPlay()
        {
            // 레벨이 로드되지 않은 상태에서 TestPlay 방지
            if (!_levelLoaded)
            {
                SetStatus("ERROR: Load a level first before test play.");
                return;
            }

            if (!ValidateCurrentLevelBeforeCommit("Test Play"))
                return;

            var config = BuildLevelConfig();
            if (config.balloons == null || config.balloons.Length == 0)
            {
                SetStatus("ERROR: No balloons placed. Add balloons first.");
                return;
            }
            if (config.holders == null || config.holders.Length == 0)
            {
                SetStatus("ERROR: No holders placed. Add holders first.");
                return;
            }

            // 테스트 플레이 전 자동 저장 (돌아올 때 데이터 유실 방지)
            SaveToActiveDB(skipValidation: true);

            string json = JsonUtility.ToJson(config, false);
            EditorPrefs.SetString("BalloonFlow_TestLevel", json);
            EditorPrefs.SetBool("BalloonFlow_UseTestLevel", true);
            EditorPrefs.SetInt(EDITOR_PREF_LAST_LEVEL, _levelId);
            PlayerPrefs.SetInt("BF_PendingLevelId", _levelId);
            IsTestMode = true;
            GameManager.IsTestPlayMode = true;
            SetStatus($"Test Play: Loading level {_levelId}...");

            // Use GameManager scene transition if available, else direct load
            if (GameManager.HasInstance)
                GameManager.Instance.StartLevel(_levelId);
            else
                UnityEngine.SceneManagement.SceneManager.LoadScene("InGame");
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        //  LEVEL LIST (Left Panel)
        // ═══════════════════════════════════════════════════════════════

        #region Level List

        private void RefreshLevelList()
        {
            if (_levelListContent == null) return;
            foreach (Transform c in _levelListContent) Destroy(c.gameObject);
            _levelListButtons.Clear();

            var db = LoadLevelDatabase();
            if (db == null || db.levels == null || db.levels.Length == 0)
            {
                var emptyTxt = MakeText(_levelListContent, "No levels.\nSave to DB first.", 12, FontStyle.Italic, TextAnchor.MiddleCenter);
                emptyTxt.color = new Color(0.5f, 0.5f, 0.6f);
                emptyTxt.gameObject.AddComponent<LayoutElement>().preferredHeight = 60;
                return;
            }

            for (int i = 0; i < db.levels.Length; i++)
            {
                var lvl = db.levels[i];
                int idx = i;
                bool isSel = (lvl.levelId == _levelId);

                var go = DefaultControls.CreateButton(_uiRes);
                go.transform.SetParent(_levelListContent, false);
                go.AddComponent<LayoutElement>().preferredHeight = 36;
                go.GetComponent<Image>().color = isSel ? new Color(0.2f, 0.35f, 0.55f) : new Color(0.15f, 0.15f, 0.20f);
                var txt = go.GetComponentInChildren<Text>();
                txt.font = _font; txt.fontSize = 11; txt.color = Color.white; txt.alignment = TextAnchor.MiddleLeft;

                string gStr = (lvl.gimmickTypes != null && lvl.gimmickTypes.Length > 0)
                    ? $"\n  [{string.Join(",", lvl.gimmickTypes)}]" : "";
                txt.text = $" Lv.{lvl.levelId}  {lvl.balloonCount}B {lvl.numColors}C{gStr}";

                go.GetComponent<Button>().onClick.AddListener(() => LoadLevelFromDB(idx));
                _levelListButtons.Add(go.GetComponent<Button>());
            }
        }

        private void LoadLevelFromDB(int index)
        {
            var db = LoadLevelDatabase();
            if (db == null || db.levels == null || index < 0 || index >= db.levels.Length) return;
            ApplyLevelConfig(db.levels[index]);
            _selectedListIndex = index;
            RefreshLevelList();
            OnBalloonGridChanged();
            RebuildHolderUI();
            RebuildWaypointPreview();
            SetStatus($"Loaded Level {db.levels[index].levelId}");
        }

        /// <summary>levelId 로 레벨 로드. 성공 여부 반환 (Awake 초기 빌드 분기에 사용).</summary>
        private bool LoadLevelById(int levelId)
        {
            var db = LoadLevelDatabase();
            if (db == null || db.levels == null) { SetStatus("No LevelDatabase"); return false; }
            for (int i = 0; i < db.levels.Length; i++)
                if (db.levels[i].levelId == levelId) { LoadLevelFromDB(i); return true; }
            SetStatus($"Level {levelId} not found");
            return false;
        }

        /// <summary>[2026-06-12] 풍선 좌표들이 spacing 격자에 정수배로 안착하는지 검증 —
        /// 공식 spacing 신뢰 가능 여부 판정 (라운드트립 데이터는 항상 true).</summary>
        private static bool BalloonsFitSpacing(BalloonLayout[] balloons, float spacing)
        {
            if (balloons == null || balloons.Length == 0) return true; // 빈 레벨 — 공식 그대로
            if (spacing <= 0.0001f) return false;

            float minX = float.MaxValue, minY = float.MaxValue;
            for (int i = 0; i < balloons.Length; i++)
            {
                if (balloons[i].gridPosition.x < minX) minX = balloons[i].gridPosition.x;
                if (balloons[i].gridPosition.y < minY) minY = balloons[i].gridPosition.y;
            }
            for (int i = 0; i < balloons.Length; i++)
            {
                float fx = (balloons[i].gridPosition.x - minX) / spacing;
                float fy = (balloons[i].gridPosition.y - minY) / spacing;
                if (Mathf.Abs(fx - Mathf.Round(fx)) > 0.2f) return false;
                if (Mathf.Abs(fy - Mathf.Round(fy)) > 0.2f) return false;
            }
            return true;
        }

        /// <summary>풍선 좌표에서 실제 spacing 감지. 같은 행/열의 인접 풍선 간 최소 거리.
        /// [2026-06-12] 공식 spacing 이 안 맞는 외부/레거시 데이터 전용 폴백 — 듬성듬성한 레벨에선
        /// 2칸 간격을 1칸으로 오인할 수 있어 1순위로 쓰지 않는다 (ApplyLevelConfig 참고).</summary>
        private float DetectSpacingFromBalloons(BalloonLayout[] balloons, int cols, int rows)
        {
            if (balloons == null || balloons.Length < 2) return -1f;

            // X 좌표들 수집 후 정렬, 인접 차이의 최소값 = spacing
            var xs = new List<float>();
            var zs = new List<float>();
            for (int i = 0; i < balloons.Length; i++)
            {
                xs.Add(balloons[i].gridPosition.x);
                zs.Add(balloons[i].gridPosition.y);
            }
            xs.Sort();
            zs.Sort();

            float minGap = float.MaxValue;
            for (int i = 1; i < xs.Count; i++)
            {
                float gap = xs[i] - xs[i - 1];
                if (gap > 0.01f && gap < minGap) minGap = gap;
            }
            for (int i = 1; i < zs.Count; i++)
            {
                float gap = zs[i] - zs[i - 1];
                if (gap > 0.01f && gap < minGap) minGap = gap;
            }

            return minGap < float.MaxValue ? minGap : -1f;
        }

        private void ApplyLevelConfig(LevelConfig config)
        {
            _levelLoaded = true;
            _levelId = config.levelId;
            _numColors = config.numColors;
            _difficulty = config.difficultyPurpose;
            _gridCols = Mathf.Max(config.gridCols, 2);
            _gridRows = Mathf.Max(config.gridRows, 2);

            // [2026-06-12 fix] spacing 결정을 '공식 우선'으로 변경.
            //   기존 '최소 간격 자동 감지'는 듬성듬성한 레벨(인접 열/행에 풍선이 전혀 없는 디자인 —
            //   예: 2칸 간격 도트 패턴)에서 실제 간격의 2배 이상을 감지 → 모든 col/row 가 절반으로
            //   접혀 '압축'된 모습으로 로드되던 버그 (Export Level JSON → Import 라운드트립 보고).
            //   저장(BuildLevelConfig)이 쓰는 공식 spacing(CellSpacing — 위에서 세팅된 grid 치수 기반)을
            //   1순위로 쓰고, 좌표가 공식 그리드에 안 맞는 외부/레거시 데이터만 감지값으로 폴백.
            float spacing = CellSpacing;
            if (!BalloonsFitSpacing(config.balloons, spacing))
            {
                float detected = DetectSpacingFromBalloons(config.balloons, _gridCols, _gridRows);
                if (detected > 0f) spacing = detected;
            }
            _balloonColors = new int[_gridCols, _gridRows];
            _balloonGimmicks = new int[_gridCols, _gridRows];
            _balloonGimmickHP = new int[_gridCols, _gridRows];
            _balloonPinataW = new int[_gridCols, _gridRows];
            _balloonPinataH = new int[_gridCols, _gridRows];
            _balloonIceBlockSize = new int[_gridCols, _gridRows];
            _balloonIceGroupId = new int[_gridCols, _gridRows];
            _balloonIceGroupHp = new int[_gridCols, _gridRows];
            _balloonIceGroupHpMode = new int[_gridCols, _gridRows];
            _balloonBarricadeDir = new int[_gridCols, _gridRows];
            _balloonBarricadeLength = new int[_gridCols, _gridRows];
            _balloonLockPairIds = new int[_gridCols, _gridRows];
            _balloonFlexTubeGroupId = new int[_gridCols, _gridRows];
            _balloonFlexTubeSequenceIndex = new int[_gridCols, _gridRows];
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                {
                    _balloonColors[c, r] = -1;
                    _balloonGimmicks[c, r] = 0;
                    _balloonGimmickHP[c, r] = 2;
                    _balloonPinataW[c, r] = 1;
                    _balloonPinataH[c, r] = 1;
                    _balloonIceBlockSize[c, r] = 1;
                    _balloonIceGroupId[c, r] = 0;
                    _balloonIceGroupHp[c, r] = 0;
                    _balloonIceGroupHpMode[c, r] = 1;
                    _balloonBarricadeDir[c, r] = 1;
                    _balloonBarricadeLength[c, r] = 1;
                    _balloonLockPairIds[c, r] = -1;
                    _balloonFlexTubeGroupId[c, r] = -1;
                    _balloonFlexTubeSequenceIndex[c, r] = -1;
                }

            // [import fix] 소스 JSON 의 X 원점 컨벤션과 무관하게 풍선 클러스터를 그리드에 중앙 정렬.
            // 풍선 X 범위(minX..maxX)를 그리드 가운데에 배치 → 이미 중앙정렬된 레벨은 동일 col 로 보존(왕복 일치),
            // 좌정렬 등으로 우측 쏠린 레벨은 중앙으로 교정. (행/Z 는 기존 절대식 유지)
            float _impMinX = 0f; int _impColOffset = 0;
            float _impMinY = 0f; int _impRowAnchor = 0;
            if (config.balloons != null && config.balloons.Length > 0)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                float loY = float.MaxValue;
                foreach (var b in config.balloons)
                {
                    if (b.gridPosition.x < lo) lo = b.gridPosition.x;
                    if (b.gridPosition.x > hi) hi = b.gridPosition.x;
                    if (b.gridPosition.y < loY) loY = b.gridPosition.y;
                }
                _impMinX = lo;
                int contentCols = Mathf.RoundToInt((hi - lo) / spacing) + 1;
                _impColOffset = Mathf.Max(0, (_gridCols - contentCols) / 2);

                // [2026-06-12] 행 매핑을 '앵커(minY) 1회 절대 라운딩 + 풍선별 상대 라운딩'으로 변경.
                //   기존 풍선별 절대식(y/spacing + (rows-1)*0.5)은 gridRows 와 콘텐츠 행 패리티가
                //   어긋난 데이터(Importer position 정규화 등)에서 값이 정확히 .5 경계에 놓여
                //   Mathf.RoundToInt(banker's rounding)가 행을 건너뜀 → 미리보기에만 빈 줄 발생.
                //   앵커 기준 상대 오프셋은 항상 정수 근처라 경계 문제가 없고, 앵커 자체는 기존
                //   절대식과 동일한 위치로 라운딩되므로 라운드트립/실제 레벨 좌표에는 영향 없음.
                _impMinY = loY;
                _impRowAnchor = Mathf.RoundToInt((loY - _boardCenter.y) / spacing + (_gridRows - 1) * 0.5f);
            }

            if (config.balloons != null)
                foreach (var b in config.balloons)
                {
                    int col = _impColOffset + Mathf.RoundToInt((b.gridPosition.x - _impMinX) / spacing);
                    int row = _impRowAnchor + Mathf.RoundToInt((b.gridPosition.y - _impMinY) / spacing);
                    if (col >= 0 && col < _gridCols && row >= 0 && row < _gridRows)
                    {
                        int gi = 0;
                        string normalizedGimmick = GimmickDisplayName.Normalize(b.gimmickType);
                        // [PIN_DEPRECATE] 레거시 Pin → Barricade (런타임 normalize 와 동일). Pin 은 드롭다운에서 제거됨.
                        if (normalizedGimmick == "Pin") normalizedGimmick = "Barricade";
                        // ROLLBACK_IRONWALL_COLOR_SENTINEL_MINUS1_20260630: Wall 색 센티넬 = 저장 -1 → 내부 0.
                        //   Wall 은 무색이지만 에디터가 color<0 셀을 '빈 칸' 으로 보므로 내부는 0 으로 둔다(앵커·footprint 공통).
                        int loadColor = (normalizedGimmick == "Wall" && b.color < 0) ? 0 : b.color;
                        _balloonColors[col, row] = loadColor;
                        if (!string.IsNullOrEmpty(normalizedGimmick))
                            for (int g = 0; g < FIELD_GIMMICK_NAMES.Length; g++)
                                if (FIELD_GIMMICK_NAMES[g] == normalizedGimmick) { gi = g; break; }
                        _balloonGimmicks[col, row] = gi;
                        _balloonGimmickHP[col, row] = b.hp > 0 ? b.hp : 2;
                        _balloonIceBlockSize[col, row] = b.iceBlockSize > 0 ? b.iceBlockSize : 1;
                        // ROLLBACK_ICE_MANUAL_GROUP_20260608:
                        // Restore explicit Ice grouping metadata. Non-Ice cells ignore these at runtime.
                        _balloonIceGroupId[col, row] = b.iceGroupId;
                        _balloonIceGroupHp[col, row] = b.iceGroupHp;
                        _balloonIceGroupHpMode[col, row] = b.iceGroupHpMode == 2 ? 2 : 1;
                        // ROLLBACK_BARRICADE_MAPMAKER_20260608: 바리케이드 방향/길이 복원 (기본 dir=1=E, length=1).
                        _balloonBarricadeDir[col, row] = ((b.barricadeDir % 4) + 4) % 4;
                        _balloonBarricadeLength[col, row] = b.barricadeLength >= 3 ? b.barricadeLength : 3;
                        _balloonLockPairIds[col, row] = b.lockPairId;
                        // FlexTube 데이터는 gimmickType 이 실제 "FlexTube" 일 때만 복원 — 잔존 데이터 방지.
                        bool isFlexTubeLoad = normalizedGimmick == "FlexTube";
                        _balloonFlexTubeGroupId[col, row] = isFlexTubeLoad ? b.flexTubeGroupId : -1;
                        _balloonFlexTubeSequenceIndex[col, row] = isFlexTubeLoad ? b.flexTubeSequenceIndex : -1;
                        if (isFlexTubeLoad && b.flexTubeGroupId >= _nextFlexTubeGroupId)
                            _nextFlexTubeGroupId = b.flexTubeGroupId + 1;

                        // Piñata 사이즈 복원 + 비앵커 셀 채우기
                        int bpw = b.sizeW > 0 ? b.sizeW : 1;
                        int bph = b.sizeH > 0 ? b.sizeH : 1;
                        _balloonPinataW[col, row] = bpw;
                        _balloonPinataH[col, row] = bph;

                        // Pinata_Box(Target Box): 알 config(anchor 별)를 복원. footprint 셀은 영역 표시용 uniform.
                        bool isTargetBox = normalizedGimmick == "Pinata_Box";
                        // ROLLBACK_ICE_OVERLAY_UNDER_BALLOONS_20260626:
                        // Older saved Ice may still have sizeW/sizeH on one anchor. Migrate it back to
                        // real 1x1 underlying balloon cells; runtime renders one overlay by iceBlockSize.
                        bool isSizedIceLoad = normalizedGimmick == "Ice" && (bpw > 1 || bph > 1);
                        if (isSizedIceLoad)
                        {
                            int blockSize = Mathf.Max(bpw, bph, b.iceBlockSize > 0 ? b.iceBlockSize : 1);
                            for (int dx = 0; dx < bpw; dx++)
                                for (int dy = 0; dy < bph; dy++)
                                {
                                    int cx = col + dx, cy = row + dy;
                                    if (cx < 0 || cx >= _gridCols || cy < 0 || cy >= _gridRows) continue;
                                    _balloonColors[cx, cy] = loadColor;
                                    _balloonGimmicks[cx, cy] = gi;
                                    _balloonGimmickHP[cx, cy] = b.hp > 0 ? b.hp : 2;
                                    _balloonPinataW[cx, cy] = 1;
                                    _balloonPinataH[cx, cy] = 1;
                                    _balloonIceBlockSize[cx, cy] = blockSize;
                                    _balloonIceGroupId[cx, cy] = b.iceGroupId;
                                    _balloonIceGroupHp[cx, cy] = b.iceGroupHp;
                                    _balloonIceGroupHpMode[cx, cy] = b.iceGroupHpMode == 2 ? 2 : 1;
                                }
                        }
                        else if (isTargetBox)
                        {
                            if (b.eggColors != null && b.eggColors.Length > 0)
                            {
                                var key = new Vector2Int(col, row);
                                _boxEggConfigColors[key] = (int[])b.eggColors.Clone();
                                _boxEggConfigHps[key] = (b.eggHps != null && b.eggHps.Length == b.eggColors.Length)
                                    ? (int[])b.eggHps.Clone() : null;
                            }
                            // footprint 비앵커 셀 uniform 마킹 (영역 표시).
                            for (int dx = 0; dx < bpw; dx++)
                                for (int dy = 0; dy < bph; dy++)
                                {
                                    if (dx == 0 && dy == 0) continue;
                                    int cx = col + dx, cy = row + dy;
                                    if (cx < 0 || cx >= _gridCols || cy < 0 || cy >= _gridRows) continue;
                                    _balloonColors[cx, cy] = loadColor;
                                    _balloonGimmicks[cx, cy] = gi;
                                    _balloonGimmickHP[cx, cy] = b.hp > 0 ? b.hp : 2;
                                    _balloonPinataW[cx, cy] = 0;
                                    _balloonPinataH[cx, cy] = 0;
                                }
                        }
                        else if (bpw > 1 || bph > 1)
                        {
                            // 비앵커 셀에 같은 색+기믹, sizeW=0 (비앵커 표시)
                            for (int dx = 0; dx < bpw; dx++)
                                for (int dy = 0; dy < bph; dy++)
                                {
                                    if (dx == 0 && dy == 0) continue; // 앵커 스킵
                                    int cx = col + dx, cy = row + dy;
                                    if (cx >= 0 && cx < _gridCols && cy >= 0 && cy < _gridRows)
                                    {
                                        _balloonColors[cx, cy] = loadColor;
                                        _balloonGimmicks[cx, cy] = gi;
                                        _balloonGimmickHP[cx, cy] = b.hp > 0 ? b.hp : 2;
                                        _balloonPinataW[cx, cy] = 0; // 비앵커 표시
                                        _balloonPinataH[cx, cy] = 0;
                                    }
                                }
                        }
                        else if (normalizedGimmick == "Barricade")
                        {
                            // ROLLBACK_BARRICADE_LOAD_PINATAW_20260626: 앵커 마커(_balloonPinataW=1) 복원.
                            //   ApplyLevelConfig 가 _balloonPinataW 를 new int[](전부 0)로 재할당하는데, 이 블록이
                            //   앵커 pinataW 를 복원하지 않아(저장된 sizeW=1 무시), 로드된 Barricade 가 밸런스 루프
                            //   (isSizedFieldCell && pinataW==0 → skip)·재저장(8008 동일 스킵)에서 '비앵커'로 오인돼
                            //   HP 미카운트 + 재저장 시 누락됐다. paint 와 동일하게 앵커=1 로 복원 → 정상화.
                            _balloonPinataW[col, row] = 1;
                            // ROLLBACK_BARRICADE_MAPMAKER_FOOTPRINT_20260608: 앵커 dir+length 로 length×2 footprint 비앵커 셀 재구성(GUI 표시).
                            int blen = Mathf.Max(3, _balloonBarricadeLength[col, row]);
                            int bdir = _balloonBarricadeDir[col, row];
                            int aCol = (bdir == 1) ? 1 : (bdir == 3) ? -1 : 0;
                            int aRow = (bdir == 0) ? 1 : (bdir == 2) ? -1 : 0;
                            int pCol = (bdir == 0 || bdir == 2) ? 1 : 0;
                            int pRow = (bdir == 1 || bdir == 3) ? 1 : 0;
                            for (int a = 0; a < blen; a++)
                                for (int p = 0; p < 2; p++)
                                {
                                    if (a == 0 && p == 0) continue; // 앵커 스킵
                                    int cx = col + aCol * a + pCol * p;
                                    int cy = row + aRow * a + pRow * p;
                                    if (cx < 0 || cx >= _gridCols || cy < 0 || cy >= _gridRows) continue;
                                    _balloonColors[cx, cy] = loadColor;
                                    _balloonGimmicks[cx, cy] = gi;
                                    _balloonGimmickHP[cx, cy] = b.hp > 0 ? b.hp : 2;
                                    _balloonPinataW[cx, cy] = 0;
                                    _balloonPinataH[cx, cy] = 0;
                                    _balloonBarricadeDir[cx, cy] = bdir;
                                    _balloonBarricadeLength[cx, cy] = blen;
                                }
                        }
                    }
                }

            if (config.holders != null && config.holders.Length > 0)
            {
                int maxC = 0, maxR = 0;
                foreach (var h in config.holders)
                { maxC = Mathf.Max(maxC, Mathf.RoundToInt(h.position.x) + 1); maxR = Mathf.Max(maxR, Mathf.RoundToInt(h.position.y) + 1); }
                _holderCols = Mathf.Max(maxC, 1); _holderRows = Mathf.Max(maxR, 1);
                _holderColors = new int[_holderCols, _holderRows];
                _holderMags = new int[_holderCols, _holderRows];
                _holderGimmicks = new int[_holderCols, _holderRows];
                _holderChainGroups = new int[_holderCols, _holderRows];
                _holderFrozenHP = new int[_holderCols, _holderRows];
                _holderSpawnerHP = new int[_holderCols, _holderRows];
                _holderSpawnerMag = new int[_holderCols, _holderRows];
                _holderLockPairIds = new int[_holderCols, _holderRows];
                for (int c = 0; c < _holderCols; c++)
                    for (int r = 0; r < _holderRows; r++) { _holderColors[c, r] = -1; _holderMags[c, r] = 0; _holderGimmicks[c, r] = 0; _holderChainGroups[c, r] = -1; _holderFrozenHP[c, r] = 3; _holderSpawnerHP[c, r] = 0; _holderSpawnerMag[c, r] = 20; _holderLockPairIds[c, r] = -1; }
                foreach (var h in config.holders)
                {
                    int hc = Mathf.RoundToInt(h.position.x), hr = Mathf.RoundToInt(h.position.y);
                    if (hc >= 0 && hc < _holderCols && hr >= 0 && hr < _holderRows)
                    {
                        _holderColors[hc, hr] = h.color;
                        _holderMags[hc, hr] = h.magazineCount;
                        // 보관함 기믹 복원
                        string normalizedHolderGimmick = GimmickDisplayName.Normalize(h.queueGimmick);
                        if (normalizedHolderGimmick == "none")
                            normalizedHolderGimmick = "";
                        int hgi = System.Array.IndexOf(HOLDER_GIMMICK_NAMES, normalizedHolderGimmick);
                        _holderGimmicks[hc, hr] = hgi > 0 ? hgi : 0;
                        _holderChainGroups[hc, hr] = h.chainGroupId;
                        _holderFrozenHP[hc, hr] = h.frozenHP > 0 ? h.frozenHP : 3;
                        _holderSpawnerHP[hc, hr] = h.spawnerHP;
                        _holderSpawnerMag[hc, hr] = h.spawnerMag > 0 ? h.spawnerMag : 20;
                        if (_holderLockPairIds != null && hc < _holderLockPairIds.GetLength(0) && hr < _holderLockPairIds.GetLength(1))
                            _holderLockPairIds[hc, hr] = h.lockPairId;
                    }
                }
            }

            // Load rail capacity: prefer LevelConfig.railCapacity, fallback to rail.slotCount
            if (config.railCapacity > 0) _railSlotCount = config.railCapacity;
            else if (config.rail.slotCount > 0) _railSlotCount = config.rail.slotCount;
            _smoothCorners = config.rail.smoothCorners;
            _cornerRadius = config.rail.cornerRadius > 0f ? config.rail.cornerRadius : 2.5f;
            if (config.rail.waypoints != null && config.rail.waypoints.Length > 0)
            {
                _customWaypoints = new List<Vector3>(config.rail.waypoints);
            }

            // 팔레트 색상 동기화 — 레벨에 실제 사용된 색상만 선택
            _selectedColors.Clear(); _sortedColorsDirty = true;
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                    if (_balloonColors[c, r] >= 0)
                        _selectedColors.Add(_balloonColors[c, r]);
            if (config.holders != null)
                foreach (var h in config.holders)
                    if (h.color >= 0) _selectedColors.Add(h.color);
            if (_selectedColors.Count < 2) { _selectedColors.Add(0); _selectedColors.Add(1); }
            _numColors = _selectedColors.Count;

            // Load conveyor path into extended path grid
            int pw = _gridCols + PATH_PAD * 2;
            int ph = _gridRows + PATH_PAD * 2;
            _pathGrid = new bool[pw, ph];
            if (config.conveyorPositions != null)
                foreach (var pos in config.conveyorPositions)
                {
                    // conveyorPositions stored as balloon-grid coords → shift to extended grid
                    int gx = pos.x + PATH_PAD;
                    int gy = pos.y + PATH_PAD;
                    if (gx >= 0 && gx < pw && gy >= 0 && gy < ph)
                        _pathGrid[gx, gy] = true;
                }
            GenerateWaypointsFromPathGrid();

            // 튜토리얼 스텝 로드
            _tutorialSteps.Clear();
            if (config.tutorialSteps != null)
                _tutorialSteps.AddRange(config.tutorialSteps);

            RebuildPalette();

            // UI 위젯 텍스트 갱신 (변수는 바뀌었지만 InputField/Dropdown은 자동 갱신 안 됨)
            if (_levelIdInput != null) _levelIdInput.text = _levelId.ToString();
            // Rebuild _selectedColors from actual balloon/holder data
            _selectedColors.Clear(); _sortedColorsDirty = true;
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                    if (_balloonColors[c, r] >= 0) _selectedColors.Add(_balloonColors[c, r]);
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                    if (_holderColors[c, r] >= 0) _selectedColors.Add(_holderColors[c, r]);
            // Ensure at least _numColors colors are selected (fill from 0 if needed)
            for (int i = 0; _selectedColors.Count < _numColors && i < PALETTE.Length; i++)
                _selectedColors.Add(i);
            _numColors = _selectedColors.Count;
            RebuildColorToggleGrid();
            if (_difficultyDropdown != null) _difficultyDropdown.value = (int)_difficulty;
        }

        private LevelDatabase LoadLevelDatabase()
        {
            // [2026-06-12 v2] Epi 탭 = Episode JSON 합본 / Old 탭 = 레거시 SO (조회 전용).
            if (_activeDBTab == 1)
            {
                if (_targetDBLegacy != null) return _targetDBLegacy;
                _targetDBLegacy = AssetDatabase.LoadAssetAtPath<LevelDatabase>(LEGACY_SO_PATH);
                if (_targetDBLegacy == null)
                    SetStatus($"Legacy DB 없음: {LEGACY_SO_PATH}");
                return _targetDBLegacy;
            }

            if (_targetDB != null) return _targetDB;
            _targetDB = LoadEpisodesAsLevelDatabase();
            return _targetDB;
        }

        /// <summary>
        /// ROLLBACK_MAPMAKER_EPISODE_STORE_20260609: Assets/EditorData/Episodes/episode_*.json 전부를 읽어
        /// levels 를 합친 in-memory LevelDatabase 를 만든다(에셋 아님). importer 와 동일 저장소 → import 즉시 반영.
        /// </summary>
        private LevelDatabase LoadEpisodesAsLevelDatabase()
        {
            var all = new List<LevelConfig>();
            if (System.IO.Directory.Exists(MM_EPISODES_DIR))
            {
                string[] files = System.IO.Directory.GetFiles(MM_EPISODES_DIR, "episode_*.json");
                System.Array.Sort(files);
                foreach (string p in files)
                {
                    try
                    {
                        var ep = JsonUtility.FromJson<LevelEpisode>(System.IO.File.ReadAllText(p));
                        if (ep?.levels != null)
                            foreach (var lv in ep.levels) if (lv != null) all.Add(lv);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[MapMaker] {p} 읽기 실패: {e.Message}");
                    }
                }
            }
            all.Sort((a, b) => a.levelId.CompareTo(b.levelId));
            var db = ScriptableObject.CreateInstance<LevelDatabase>();
            db.levels = all.ToArray();
            return db;
        }

        /// <summary>현재 레벨 저장 — [2026-06-12] AI/Transform 탭 폐기, Episode JSON 단일 경로.</summary>
        private void SaveToActiveDB(bool skipValidation = false)
        {
            if (!skipValidation && !ValidateCurrentLevelBeforeCommit("Save"))
                return;

            // ROLLBACK_MAPMAKER_OLD_SO_SAVE_20260618: Old(레거시 SO) 탭에서 로드/편집한 레벨은 SO 로 저장한다.
            //   기존엔 탭 무관하게 항상 Episode JSON(=ori)으로 갔음(line 327). 이제 old 에서 save/test-play(복귀 auto-save 포함)
            //   하면 ori(Episodes)로 새지 않고 old(SO)에만 저장. Epi 탭은 기존대로 Episode JSON. (사용자: old=SO 쓰기가능)
            if (_activeDBTab == 1)
            {
                SaveToDB(LEGACY_SO_PATH, ref _targetDBLegacy);
                RefreshLevelList();
                return;
            }
            SaveCurrentLevelToEpisode();
        }

        /// <summary>
        /// ROLLBACK_MAPMAKER_EPISODE_STORE_20260609: 현재 편집 중인 레벨을 해당 패키지의 episode JSON 에 병합·저장.
        /// importer(WriteEpisodeFile) 와 동일 포맷/경로 → 라운드트립. pkg1 은 StreamingAssets 동기화.
        /// </summary>
        private void SaveCurrentLevelToEpisode()
        {
            if (!_levelLoaded)
            {
                SetStatus("ERROR: No level loaded. Load or create a level first.");
                return;
            }

            LevelConfig config = BuildLevelConfig();
            int pkg = GetPackageIdForLevel(config.levelId);
            string path = $"{MM_EPISODES_DIR}/episode_{pkg:D2}.json";

            // 기존 episode 로드(병합 기준).
            var levels = new List<LevelConfig>();
            if (System.IO.File.Exists(path))
            {
                try
                {
                    var existing = JsonUtility.FromJson<LevelEpisode>(System.IO.File.ReadAllText(path));
                    if (existing?.levels != null)
                        foreach (var lv in existing.levels) if (lv != null) levels.Add(lv);
                }
                catch (System.Exception e) { Debug.LogError($"[MapMaker] {path} 읽기 실패: {e.Message}"); }
            }

            // 덮어쓰기 전 백업.
            BackupEpisodeFileMM(pkg);

            int idx = levels.FindIndex(l => l.levelId == config.levelId);
            if (idx >= 0) levels[idx] = config; else levels.Add(config);
            levels.Sort((a, b) => a.levelId.CompareTo(b.levelId));
            foreach (var lv in levels) NormalizeLevelEpisodeFields(lv);

            var ep = new LevelEpisode
            {
                packageId  = pkg,
                levelCount = levels.Count,
                version    = LEVEL_EPISODE_VERSION,
                levels     = levels.ToArray()
            };
            string json = JsonUtility.ToJson(ep, false); // importer/런타임과 동일 포맷

            System.IO.Directory.CreateDirectory(MM_EPISODES_DIR);
            System.IO.File.WriteAllText(path, json);
            AssetDatabase.ImportAsset(path);

            if (pkg == 1)
            {
                string streamDir = System.IO.Path.GetDirectoryName(MM_STREAMING_EP1);
                if (!string.IsNullOrEmpty(streamDir)) System.IO.Directory.CreateDirectory(streamDir);
                System.IO.File.WriteAllText(MM_STREAMING_EP1, json);
                AssetDatabase.ImportAsset(MM_STREAMING_EP1);
            }

            _targetDB = null; // episode-backed 캐시 무효화 → 다음 로드 시 재빌드(저장 레벨 즉시 반영)
            SetStatus($"Saved Level {config.levelId} → episode_{pkg:D2}.json ({levels.Count} levels)" +
                      (pkg != 1 ? "  · Firestore 반영은 'Export & Upload' 필요" : "  · StreamingAssets 동기화"));
            RefreshLevelList();
        }

        private void BackupEpisodeFileMM(int pkg)
        {
            string src = $"{MM_EPISODES_DIR}/episode_{pkg:D2}.json";
            if (!System.IO.File.Exists(src)) return;
            const string backupDir = "Assets/LevelBackups";
            System.IO.Directory.CreateDirectory(backupDir);
            string ts = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            System.IO.File.Copy(src, $"{backupDir}/episode_{pkg:D2}_{ts}.json", true);
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        //  EXPORT
        // ═══════════════════════════════════════════════════════════════

        #region Export

        /// <summary>지정된 경로의 LevelDatabase에 저장.</summary>
        private void SaveToDB(string assetPath, ref LevelDatabase db)
        {
            if (!_levelLoaded)
            {
                SetStatus("ERROR: No level loaded. Load or create a level first.");
                return;
            }
            if (db == null)
            {
                db = AssetDatabase.LoadAssetAtPath<LevelDatabase>(assetPath);
                if (db == null)
                {
                    db = ScriptableObject.CreateInstance<LevelDatabase>();
                    AssetDatabase.CreateAsset(db, assetPath);
                }
            }

            // 자동 백업 (Origin DB만)
            if (assetPath.Contains("LevelDatabase.asset") && !assetPath.Contains("_"))
                BackupDatabase();

            var config = BuildLevelConfig();
            var levels = db.levels != null ? new List<LevelConfig>(db.levels) : new List<LevelConfig>();
            int idx = levels.FindIndex(l => l.levelId == config.levelId);
            if (idx >= 0) levels[idx] = config; else levels.Add(config);
            levels.Sort((a, b) => a.levelId.CompareTo(b.levelId));
            db.levels = levels.ToArray();
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();

            string dbName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            SetStatus($"Saved Level {config.levelId} to {dbName}");
            RefreshLevelList();
        }

        private void BackupDatabase()
        {
            if (_targetDB == null || _targetDB.levels == null) return;
            string dir = "Assets/LevelBackups";
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupPath = $"{dir}/LevelDB_backup_{timestamp}.json";

            // 전체 DB를 JSON으로 직렬화
            var wrapper = new LevelDatabaseWrapper { levels = _targetDB.levels };
            string json = JsonUtility.ToJson(wrapper, true);
            System.IO.File.WriteAllText(backupPath, json);
            Debug.Log($"[MapMaker] Backup saved: {backupPath}");

            // 오래된 백업 정리 (최대 10개 유지)
            var files = new List<string>(System.IO.Directory.GetFiles(dir, "LevelDB_backup_*.json"));
            files.Sort();
            while (files.Count > 10)
            {
                System.IO.File.Delete(files[0]);
                files.RemoveAt(0);
            }
        }

        [System.Serializable]
        private class LevelDatabaseWrapper
        {
            public LevelConfig[] levels;
        }

        private bool ValidateCurrentLevelBeforeCommit(string actionName)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            // ROLLBACK_MAPMAKER_GIMMICK_VALIDATION_20260623:
            // Block doc-defined Hard Rule violations before Save/Export/TestPlay.
            // Soft guidance remains confirmable so valid designer-authored edge cases are not lost.
            ValidateHolderGimmickRules(errors, warnings);
            ValidateFieldGimmickRules(errors, warnings);

            if (errors.Count == 0)
            {
                if (warnings.Count == 0)
                    return true;

                string warningMessage = $"Level {_levelId} has gimmick warning(s) before {actionName}:\n\n- {string.Join("\n- ", warnings)}\n\nContinue anyway?";
                bool proceed = EditorUtility.DisplayDialog("MapMaker Validation", warningMessage, "Continue", "Cancel");
                if (!proceed)
                    SetStatus($"{actionName} canceled: gimmick warning");
                else
                    Debug.LogWarning($"[MapMakerValidation] {actionName} continued with warnings\n{warningMessage}");
                return proceed;
            }

            string message = $"Level {_levelId} has invalid gimmick settings:\n\n- {string.Join("\n- ", errors)}";
            EditorUtility.DisplayDialog("MapMaker Validation", message, "OK");
            SetStatus($"{actionName} blocked: gimmick validation failed");
            Debug.LogWarning($"[MapMakerValidation] {actionName} blocked\n{message}");
            return false;
        }

        private void ValidateHolderGimmickRules(List<string> errors, List<string> warnings)
        {
            if (errors == null || _holderGimmicks == null || _holderChainGroups == null)
                return;

            int chainIndex = System.Array.IndexOf(HOLDER_GIMMICK_NAMES, "Chain");
            int hiddenIndex = System.Array.IndexOf(HOLDER_GIMMICK_NAMES, "Hidden");
            int glassPipeIndex = System.Array.IndexOf(HOLDER_GIMMICK_NAMES, "Spawner_T");
            int pipeIndex = System.Array.IndexOf(HOLDER_GIMMICK_NAMES, "Spawner_O");
            int frozenIndex = System.Array.IndexOf(HOLDER_GIMMICK_NAMES, "Frozen_Dart");
            int totalHolderSlots = Mathf.Max(1, _holderCols * _holderRows);
            int activeHolderCount = 0;
            bool hasGlassPipe = false;
            bool hasPipe = false;
            var groups = new Dictionary<int, List<Vector2Int>>();
            for (int c = 0; c < _holderCols; c++)
            {
                for (int r = 0; r < _holderRows; r++)
                {
                    if (_holderColors != null && _holderColors[c, r] >= 0)
                        activeHolderCount++;
                }
            }

            for (int c = 0; c < _holderCols; c++)
            {
                for (int r = 0; r < _holderRows; r++)
                {
                    int gimmick = _holderGimmicks[c, r];
                    if (gimmick <= 0)
                        continue;

                    string gimmickName = GetHolderGimmickName(gimmick);
                    bool isPipeGimmick = gimmick == glassPipeIndex || gimmick == pipeIndex;
                    if (_holderColors != null && _holderColors[c, r] < 0 && !isPipeGimmick)
                    {
                        errors.Add($"Holder gimmick {gimmickName} at ({c},{r}) is set on an empty holder cell.");
                        continue;
                    }

                    int debutLevel = GetHolderGimmickDebutLevel(gimmickName);
                    if (debutLevel > 0 && _levelId < debutLevel)
                        errors.Add($"{gimmickName} is unlocked at level {debutLevel}, but current level is {_levelId}. Holder: ({c},{r}).");

                    if (gimmick == hiddenIndex && r == 0)
                        errors.Add($"Hidden Dart Box cannot be placed on front row 0. Holder: ({c},{r}).");

                    // ROLLBACK_GLASSPIPE_PARITY_20260625: Glass Pipe(Spawner_T)·Pipe(Spawner_O) 기능 동일 →
                    // payload 규칙(row0 금지·아래 칸 채움·중첩 금지)도 동일하게 검증. 머티리얼만 다름.
                    if (gimmick == glassPipeIndex || gimmick == pipeIndex)
                    {
                        bool isGlass = (gimmick == glassPipeIndex);
                        if (isGlass) hasGlassPipe = true; else hasPipe = true;
                        ValidatePipePayloadRules(c, r, pipeIndex, glassPipeIndex, errors, isGlass ? "Glass Pipe" : "Pipe");
                    }

                    if (gimmick == frozenIndex)
                    {
                        int hp = _holderFrozenHP != null ? _holderFrozenHP[c, r] : 0;
                        int hpMax = (activeHolderCount > 0 ? activeHolderCount : totalHolderSlots) - 1;
                        if (hpMax < 2)
                            errors.Add($"Frozen Dart Box at ({c},{r}) needs at least 3 active holder slots because HP must be 2..total_sh-1.");
                        else if (hp < 2 || hp > hpMax)
                            errors.Add($"Frozen Dart Box HP at ({c},{r}) is {hp}. Use 2..{hpMax}.");
                    }

                    if (gimmick != chainIndex)
                        continue;

                    int groupId = _holderChainGroups[c, r];
                    if (groupId <= 0)
                    {
                        errors.Add($"Linked Dart Box at holder ({c},{r}) has Chain Group {groupId}. Use group 1 or higher.");
                        continue;
                    }

                    if (!groups.TryGetValue(groupId, out var cells))
                    {
                        cells = new List<Vector2Int>();
                        groups[groupId] = cells;
                    }
                    cells.Add(new Vector2Int(c, r));
                }
            }

            if (hasGlassPipe && hasPipe)
                errors.Add("Glass Pipe (Spawner_T) and Pipe (Spawner_O) cannot be mixed in the same level.");

            ValidateLinkedDartBoxRules(groups, errors, warnings);
        }

        // GLASSPIPE_PARITY_20260625: label 로 Pipe / Glass Pipe 메시지 구분(규칙은 동일).
        private void ValidatePipePayloadRules(int c, int anchorRow, int pipeIndex, int glassPipeIndex, List<string> errors, string label = "Pipe")
        {
            if (_holderSpawnerHP == null || _holderColors == null || _holderGimmicks == null)
                return;

            int count = _holderSpawnerHP[c, anchorRow];
            // ROLLBACK_PIPE_NOT_ON_RAIL_FRONT_20260624: Pipe/Glass Pipe must NOT sit on row 0 (directly in front
            // of the rail). Otherwise its auto-released holder lands on the rail-front with no player-controlled
            // buffer in between. Require row ≥ 1 so row 0 stays a normal deploy slot. (Payloads are still
            // authored in the cells BELOW the anchor.)
            if (anchorRow < 1)
                errors.Add($"{label} at holder ({c},{anchorRow}) cannot be on row 0 (directly in front of the rail) — its auto-released holder would go straight onto the rail. Place it at row 1 or higher.");

            if (count <= 0)
            {
                errors.Add($"{label} at holder ({c},{anchorRow}) has Count {count}. Use 1 or higher.");
                return;
            }

            int lastRow = anchorRow + count;
            if (lastRow >= _holderRows)
            {
                errors.Add($"{label} at holder ({c},{anchorRow}) needs {count} authored holder(s) below it, but holder rows end at {_holderRows - 1}.");
                return;
            }

            for (int r = anchorRow + 1; r <= lastRow; r++)
            {
                if (_holderColors[c, r] < 0)
                    errors.Add($"{label} at holder ({c},{anchorRow}) payload cell ({c},{r}) is empty. Fill all {count} cells below the {label}.");

                int gimmick = _holderGimmicks[c, r];
                if (gimmick == pipeIndex || gimmick == glassPipeIndex)
                    errors.Add($"{label} at holder ({c},{anchorRow}) payload cell ({c},{r}) cannot contain another Pipe/Glass Pipe.");
            }
        }

        private void ValidateLinkedDartBoxRules(Dictionary<int, List<Vector2Int>> groups, List<string> errors, List<string> warnings)
        {
            var dependencyGroups = new List<List<int>>();
            foreach (var kvp in groups)
            {
                int groupId = kvp.Key;
                List<Vector2Int> cells = kvp.Value;
                if (cells.Count < 2 || cells.Count > 5)
                {
                    errors.Add($"Linked Dart Box Chain Group {groupId} has {cells.Count} holder(s). Use 2..5.");
                    continue;
                }

                if (cells.Count == 5 && _difficulty != DifficultyPurpose.SuperHard)
                    warnings.Add($"Linked Dart Box Chain Group {groupId} uses 5 holders. Spec treats link_n=5 as SuperHard-only soft guidance.");

                var sorted = new List<Vector2Int>(cells);
                sorted.Sort((a, b) =>
                {
                    int col = a.x.CompareTo(b.x);
                    return col != 0 ? col : a.y.CompareTo(b.y);
                });

                bool hasVerticalStep = false;
                bool hasColumnStep = false;
                int maxAnyColDiff = 0;
                int maxAnyRowDiff = 0;
                for (int i = 0; i < sorted.Count; i++)
                {
                    for (int j = i + 1; j < sorted.Count; j++)
                    {
                        maxAnyColDiff = Mathf.Max(maxAnyColDiff, Mathf.Abs(sorted[i].x - sorted[j].x));
                        maxAnyRowDiff = Mathf.Max(maxAnyRowDiff, Mathf.Abs(sorted[i].y - sorted[j].y));
                    }
                }

                for (int i = 1; i < sorted.Count; i++)
                {
                    Vector2Int prev = sorted[i - 1];
                    Vector2Int cur = sorted[i];
                    int colDiff = Mathf.Abs(prev.x - cur.x);
                    int rowDiff = Mathf.Abs(prev.y - cur.y);

                    if (colDiff > 1)
                    {
                        errors.Add($"Linked Dart Box Chain Group {groupId} has non-adjacent chain step ({prev.x},{prev.y}) -> ({cur.x},{cur.y}). Consecutive col diff must be <= 1.");
                        continue;
                    }

                    if (colDiff == 0)
                    {
                        hasVerticalStep = true;
                        if (rowDiff != 1)
                            errors.Add($"Linked Dart Box Chain Group {groupId} same-column step ({prev.x},{prev.y}) -> ({cur.x},{cur.y}) must have row diff 1.");
                        // ROLLBACK_VERTICAL_CHAIN_ROWRANGE_20260625: 세로 체인의 'rows 0..2' 위치 제한 제거.
                        //   런타임이 세로 체인을 어느 행이든 배포하도록 수정됨(HolderManager/InputHandler).
                        //   '연속 인접(rowDiff==1)'·'개수 제한(아래 max link_n)'만 유지하면 충분.
                    }
                    else
                    {
                        hasColumnStep = true;
                        if (rowDiff > 5)
                            errors.Add($"Linked Dart Box Chain Group {groupId} adjacent-column step ({prev.x},{prev.y}) -> ({cur.x},{cur.y}) has row diff {rowDiff}. Max is 5.");
                    }
                }

                // ROLLBACK_VERTICAL_CHAIN_MAX2_20260625 (옵션 A): 한 열에 쌓인 체인 셀 ≤ 2.
                //   런타임 한 열 활성 슬롯이 deploying+waiting=2 라, 한 열에 3+ 스택(세로 3개 또는 mixed 의
                //   한 열 3 스택)은 인게임 배포 불가 → 저작 단계에서 차단(세로 연결 최대 2개).
                var chainColCount = new Dictionary<int, int>();
                foreach (var pc in cells)
                {
                    chainColCount.TryGetValue(pc.x, out int cc);
                    chainColCount[pc.x] = cc + 1;
                }
                foreach (var kv in chainColCount)
                    if (kv.Value > 2)
                        errors.Add($"Linked Dart Box Chain Group {groupId} stacks {kv.Value} holders in one column (col {kv.Key}). A column deploys at most 2 (slot + 1 waiting) — keep vertical link ≤ 2.");

                if (hasVerticalStep && hasColumnStep && sorted.Count > 4)
                    errors.Add($"Linked Dart Box Chain Group {groupId} is a mixed chain with {sorted.Count} holders. Mixed chains allow max link_n=4.");

                if (maxAnyRowDiff > 2)
                    warnings.Add($"Linked Dart Box Chain Group {groupId} has max row distance {maxAnyRowDiff}. Spec marks row distance > 2 as soft-risk.");

                if (maxAnyColDiff > 3)
                    warnings.Add($"Linked Dart Box Chain Group {groupId} has max col distance {maxAnyColDiff}. Spec marks col distance > 3 as soft-risk.");

                var sidGroup = new List<int>();
                foreach (var p in cells)
                    sidGroup.Add(p.y * _holderCols + p.x);
                dependencyGroups.Add(sidGroup);
            }

            if (dependencyGroups.Count > 1 && HasCycle(BuildChainDependencyGraph(dependencyGroups, _holderCols, _holderCols * _holderRows)))
                errors.Add("Linked Dart Box chain groups create a dependency cycle. Chain group dependency graph must be DAG.");
        }

        private void ValidateFieldGimmickRules(List<string> errors, List<string> warnings)
        {
            if (_balloonGimmicks == null)
                return;

            bool hasBarricade = false;
            bool hasHiddenBalloon = false;
            bool hasIronWall = false;
            bool hasIce = false;
            for (int c = 0; c < _gridCols; c++)
            {
                for (int r = 0; r < _gridRows; r++)
                {
                    int gimmick = _balloonGimmicks[c, r];
                    if (gimmick <= 0 || gimmick >= FIELD_GIMMICK_NAMES.Length)
                        continue;

                    string gimmickName = FIELD_GIMMICK_NAMES[gimmick];
                    int debutLevel = GetFieldGimmickDebutLevel(gimmickName);
                    if (debutLevel > 0 && _levelId < debutLevel)
                        errors.Add($"{gimmickName} is unlocked at level {debutLevel}, but current level is {_levelId}. Cell: ({c},{r}).");

                    if (gimmickName == "Surprise")
                        hasHiddenBalloon = true;
                    else if (gimmickName == "Wall")
                        hasIronWall = true;
                    else if (gimmickName == "Barricade")
                        hasBarricade = true;
                    else if (gimmickName == "Ice")
                        hasIce = true;
                }
            }

            // ROLLBACK_ALLOW_BARRICADE_HIDDEN_BALLOON_20260701: 설계 변경 — Barricade 와 Hidden Balloon(Surprise) 공존 허용.
            //   (둘 다 필드 기믹이지만 셀당 1개라 다른 셀에 공존 가능. 기존 차단 규칙 제거.)
            //   IronWall+Hidden / IronWall+Ice(데드락) 규칙은 유지.
            _ = hasBarricade; // 조합 검증에서만 쓰이던 플래그 — 규칙 제거로 참조만 유지(경고 방지).
            if (hasHiddenBalloon && hasIronWall)
                errors.Add("Hidden Balloon (Surprise) and Iron Wall (Wall) cannot coexist in the same level.");
            if (hasIronWall && hasIce)
                errors.Add("Iron Wall (Wall) and Ice cannot coexist in the same level because it can create deadlock.");
        }

        private static string GetHolderGimmickName(int index)
        {
            return index > 0 && index < HOLDER_GIMMICK_NAMES.Length ? HOLDER_GIMMICK_NAMES[index] : "(unknown)";
        }

        private static int GetHolderGimmickDebutLevel(string gimmickName)
        {
            switch (gimmickName)
            {
                case "Hidden": return 11;
                case "Chain": return 21;
                case "Spawner_T": return 41;
                case "Spawner_O": return 121;
                case "Frozen_Dart": return 241;
                default: return 0;
            }
        }

        private static int GetFieldGimmickDebutLevel(string gimmickName)
        {
            switch (gimmickName)
            {
                case "Pinata": return 31;
                case "Barricade": return 61;
                case "Surprise": return 81;
                case "Wall": return 101;
                case "Pinata_Box": return 161;
                case "Ice": return 201;
                case "Color_Curtain": return 301;
                default: return 0;
            }
        }

        private void ExportEpisodeJson()
        {
            if (!_levelLoaded)
            {
                SetStatus("Export failed: no level loaded");
                return;
            }

            if (!ValidateCurrentLevelBeforeCommit("Export Episode JSON"))
                return;

            LevelEpisode episode = BuildCurrentEpisodeForExport();
            if (episode == null || episode.levels == null || episode.levels.Length == 0)
            {
                SetStatus("Export failed: no levels for current episode");
                return;
            }

            SaveLevelEpisodeJson(
                episode,
                "Export Episode JSON",
                episode.packageId == 1 ? "Assets/StreamingAssets" : "Assets",
                $"episode_{episode.packageId:D2}",
                $"Exported episode {episode.packageId}: {episode.levels.Length} levels");
        }

        private void ExportLevelJson()
        {
            if (!_levelLoaded)
            {
                SetStatus("Export failed: no level loaded");
                return;
            }

            if (!ValidateCurrentLevelBeforeCommit("Export Level JSON"))
                return;

            LevelEpisode episode = BuildCurrentLevelEpisodeForExport();
            // [2026-06-12] 백업 컨벤션: 기본 저장 위치 = Assets/LevelBackups (Importer 의 episode 백업과
            // 동일 폴더). Importer 의 '레벨 백업 추가' 버튼이 이 폴더의 level_*.json 을 일괄 로드해
            // episode 로 병합(SO 미경유)할 수 있다.
            const string levelBackupDir = "Assets/LevelBackups";
            System.IO.Directory.CreateDirectory(levelBackupDir);
            SaveLevelEpisodeJson(
                episode,
                "Export Current Level JSON",
                levelBackupDir,
                $"level_{_levelId:D4}",
                $"Exported level {_levelId}");
        }

        /// <summary>[2026-06-12] 다량 episode 일괄 export — 입력("1-15"/"1,5,6,7"/혼합)에 해당하는
        /// episode_XX.json 들을 선택 폴더로 한 번에 복사. 저장소 파일 기준이므로 미저장 편집분은
        /// Save This Level 후 실행해야 반영됨.</summary>
        private void ExportEpisodesBulk()
        {
            List<int> ids = ParseEpisodeRangeInput(_bulkExportEpisodesInput);
            if (ids.Count == 0)
            {
                SetStatus("Export failed: invalid input (예: 1-15 또는 1,5,6,7)");
                return;
            }

            string dir = EditorUtility.OpenFolderPanel("Export Episodes To Folder", "", "");
            if (string.IsNullOrEmpty(dir)) return;

            int copied = 0;
            var missing = new List<int>();
            foreach (int id in ids)
            {
                string src = $"{MM_EPISODES_DIR}/episode_{id:D2}.json";
                if (!System.IO.File.Exists(src)) { missing.Add(id); continue; }
                System.IO.File.Copy(src, System.IO.Path.Combine(dir, $"episode_{id:D2}.json"), true);
                copied++;
            }

            string msg = $"Exported {copied} episode(s) -> {dir}";
            if (missing.Count > 0) msg += $" / missing: {string.Join(",", missing)}";
            SetStatus(msg);
            Debug.Log($"[MapMaker] {msg}");
        }

        /// <summary>"1-15", "1,5,6,7", "1-3,7,10-12" 형태를 episode 번호 목록(중복 제거·오름차순)으로 파싱.
        /// 토큰 단위로 관대하게 — 잘못된 토큰은 무시하고 유효한 것만 수집.</summary>
        private static List<int> ParseEpisodeRangeInput(string input)
        {
            var result = new SortedSet<int>();
            if (string.IsNullOrWhiteSpace(input)) return new List<int>();

            foreach (string raw in input.Split(','))
            {
                string token = raw.Trim();
                if (token.Length == 0) continue;

                int dash = token.IndexOf('-');
                if (dash > 0)
                {
                    string a = token.Substring(0, dash).Trim();
                    string b = token.Substring(dash + 1).Trim();
                    if (int.TryParse(a, out int lo) && int.TryParse(b, out int hi) && lo >= 1 && hi >= lo)
                    {
                        for (int i = lo; i <= hi; i++) result.Add(i);
                    }
                }
                else if (int.TryParse(token, out int v) && v >= 1)
                {
                    result.Add(v);
                }
            }
            return new List<int>(result);
        }

        private void SaveLevelEpisodeJson(LevelEpisode episode, string title, string defaultDir, string defaultName, string statusPrefix)
        {
            if (episode == null || episode.levels == null || episode.levels.Length == 0)
            {
                SetStatus("Export failed: no levels");
                return;
            }

            string json = JsonUtility.ToJson(episode, true);
            string path = EditorUtility.SaveFilePanel(title, defaultDir, defaultName, "json");
            if (string.IsNullOrEmpty(path)) return;

            System.IO.File.WriteAllText(path, json);
            AssetDatabase.Refresh();
            SetStatus($"{statusPrefix} -> {System.IO.Path.GetFileName(path)}");
        }

        private LevelEpisode BuildCurrentEpisodeForExport()
        {
            // ROLLBACK_MAPMAKER_EXPORT_SINGLE_LEVEL_JSON:
            // Runtime loads LevelEpisode JSON (StreamingAssets/episode_01.json and Firestore
            // /episodes/{packageId}.levelsJson). MapMaker export must match that shape, while
            // still merging the currently edited level even if it has not been saved to DB yet.
            LevelConfig current = BuildLevelConfig();
            int packageId = GetPackageIdForLevel(current.levelId);
            int firstLevel = ((packageId - 1) * LEVELS_PER_EXPORT_EPISODE) + 1;
            int lastLevel = firstLevel + LEVELS_PER_EXPORT_EPISODE - 1;

            var levels = new List<LevelConfig>(LEVELS_PER_EXPORT_EPISODE);
            LevelDatabase db = LoadLevelDatabase();
            if (db != null && db.levels != null)
            {
                for (int i = 0; i < db.levels.Length; i++)
                {
                    LevelConfig level = db.levels[i];
                    if (level == null) continue;
                    if (level.levelId < firstLevel || level.levelId > lastLevel) continue;
                    levels.Add(level);
                }
            }

            int currentIndex = levels.FindIndex(l => l.levelId == current.levelId);
            if (currentIndex >= 0) levels[currentIndex] = current;
            else levels.Add(current);

            levels.Sort((a, b) => a.levelId.CompareTo(b.levelId));
            for (int i = 0; i < levels.Count; i++)
                NormalizeLevelEpisodeFields(levels[i]);

            return new LevelEpisode
            {
                packageId = packageId,
                levelCount = levels.Count,
                version = LEVEL_EPISODE_VERSION,
                levels = levels.ToArray()
            };
        }

        private LevelEpisode BuildCurrentLevelEpisodeForExport()
        {
            // ROLLBACK_MAPMAKER_EXPORT_SINGLE_LEVEL_JSON:
            // Keep the same LevelEpisode container as episode export so the exported file can be
            // merged/imported by tools that already understand episode JSON. Only the levels array is narrowed to the current level.
            LevelConfig current = BuildLevelConfig();
            NormalizeLevelEpisodeFields(current);
            return new LevelEpisode
            {
                packageId = current.packageId,
                levelCount = 1,
                version = LEVEL_EPISODE_VERSION,
                levels = new[] { current }
            };
        }

        private static int GetPackageIdForLevel(int levelId)
        {
            if (levelId < 1) return 1;
            return ((levelId - 1) / LEVELS_PER_EXPORT_EPISODE) + 1;
        }

        private static int GetPositionInPackage(int levelId)
        {
            if (levelId < 1) return 1;
            return ((levelId - 1) % LEVELS_PER_EXPORT_EPISODE) + 1;
        }

        private static void NormalizeLevelEpisodeFields(LevelConfig level)
        {
            if (level == null) return;
            level.packageId = GetPackageIdForLevel(level.levelId);
            level.positionInPackage = GetPositionInPackage(level.levelId);
        }

        private LevelConfig BuildLevelConfig()
        {
            float spacing = CellSpacing;
            int packageId = GetPackageIdForLevel(_levelId);
            var config = new LevelConfig
            {
                levelId = _levelId, packageId = packageId, positionInPackage = GetPositionInPackage(_levelId),
                numColors = _numColors, difficultyPurpose = _difficulty,
                gimmickTypes = CollectGimmicks(), balloonScale = BalloonScale,
            };

            // FlexTube partType 사전 계산 — 현재 _balloonGimmicks 가 실제로 FlexTube 인 cell 만 인정.
            // (erase 후 다른 gimmick 으로 paint 한 경우 잔존 groupId 가 있을 수 있어 가드.)
            var flexTubeMaxSeq = new Dictionary<int, int>();
            var flexTubeCells = new Dictionary<int, List<(int c, int r, int seq, int color)>>(); // 검증용
            // 디버그: 모든 FlexTube cell 의 grid 상태 dump.
            int dbgScanCount = 0;
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                {
                    int gimmickIdx = _balloonGimmicks[c, r];
                    bool isFlexTubeCell = gimmickIdx > 0 && gimmickIdx < FIELD_GIMMICK_NAMES.Length
                                          && FIELD_GIMMICK_NAMES[gimmickIdx] == "FlexTube";
                    if (!isFlexTubeCell) continue;
                    dbgScanCount++;

                    int gid = _balloonFlexTubeGroupId[c, r];
                    int seq = _balloonFlexTubeSequenceIndex[c, r];
                    Debug.Log($"[MapMaker:FlexTube] scan cell ({c},{r}) color={_balloonColors[c, r]} groupId={gid} seq={seq}");
                    if (gid < 0) continue;
                    if (!flexTubeMaxSeq.TryGetValue(gid, out int curMax) || seq > curMax)
                        flexTubeMaxSeq[gid] = seq;
                    if (!flexTubeCells.TryGetValue(gid, out var list))
                    {
                        list = new List<(int, int, int, int)>();
                        flexTubeCells[gid] = list;
                    }
                    list.Add((c, r, seq, _balloonColors[c, r]));
                }
            Debug.Log($"[MapMaker:FlexTube] total scanned FlexTube cells = {dbgScanCount}, groups = {flexTubeCells.Count}");

            // 그룹별 무결성 검증 — 부족/끊김 발견 시 경고 로그.
            foreach (var kv in flexTubeCells)
            {
                int gid = kv.Key;
                var cells = kv.Value;
                if (cells.Count < 2)
                {
                    Debug.LogWarning($"[MapMaker:FlexTube] Group {gid}: cells={cells.Count} (need at least 2: StartCap+EndCap)");
                    SetStatus($"FlexTube G{gid}: incomplete ({cells.Count} cells)");
                    continue;
                }
                // ROLLBACK_FLEXTUBE_2THICK_VALIDATION_20260626:
                // FlexTube is now authored as a footprint: straight sections can have row-A/row-B
                // with the same seq, and corners can have a 2x2 set for one seq. Validate the
                // continuous unique sequence range instead of requiring exactly one cell per seq.
                cells.Sort((a, b) => a.seq.CompareTo(b.seq));
                int highestSeq = -1;
                for (int i = 0; i < cells.Count; i++)
                    if (cells[i].seq > highestSeq) highestSeq = cells[i].seq;

                bool seqOK = highestSeq >= 0;
                bool[] seenSeq = seqOK ? new bool[highestSeq + 1] : System.Array.Empty<bool>();
                for (int i = 0; i < cells.Count && seqOK; i++)
                {
                    int cellSeq = cells[i].seq;
                    if (cellSeq < 0 || cellSeq > highestSeq)
                    {
                        seqOK = false;
                        break;
                    }
                    seenSeq[cellSeq] = true;
                }
                for (int cellSeq = 0; cellSeq <= highestSeq && seqOK; cellSeq++)
                    if (!seenSeq[cellSeq]) seqOK = false;
                if (!seqOK)
                {
                    Debug.LogWarning($"[MapMaker:FlexTube] Group {gid}: sequenceIndex 불연속 — paint 순서 손상");
                    SetStatus($"FlexTube G{gid}: sequence broken");
                }
                // ROLLBACK_FLEXTUBE_2THICK_VALIDATION_20260626:
                // Consecutive logical seqs are connected if any cell in seq N touches any
                // cell in seq N+1. This supports two-thick tubes and 2x2 corner footprints.
                for (int seqIndex = 0; seqIndex < highestSeq; seqIndex++)
                {
                    bool connected = false;
                    for (int ai = 0; ai < cells.Count && !connected; ai++)
                    {
                        if (cells[ai].seq != seqIndex) continue;
                        for (int bi = 0; bi < cells.Count; bi++)
                        {
                            if (cells[bi].seq != seqIndex + 1) continue;
                            int cellDx = Mathf.Abs(cells[bi].c - cells[ai].c);
                            int cellDy = Mathf.Abs(cells[bi].r - cells[ai].r);
                            if (cellDx + cellDy == 1)
                            {
                                connected = true;
                                break;
                            }
                        }
                    }
                    if (!connected)
                    {
                        Debug.LogWarning($"[MapMaker:FlexTube] Group {gid}: seq {seqIndex} and {seqIndex + 1} are disconnected");
                        SetStatus($"FlexTube G{gid}: cells disconnected at seq {seqIndex}-{seqIndex + 1}");
                    }
                }
                // 색상 일관성 — 그룹 내 모든 셀이 같은 색이어야 함
                int firstColor = cells[0].color;
                foreach (var cell in cells)
                {
                    if (cell.color != firstColor)
                    {
                        Debug.LogWarning($"[MapMaker:FlexTube] Group {gid}: 색상 불일치 — seq {cell.seq} color={cell.color}, 그룹 색={firstColor}");
                        SetStatus($"FlexTube G{gid}: color mismatch");
                        break;
                    }
                }
            }

            var balloons = new List<BalloonLayout>();
            int bid = 0;
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                {
                    if (_balloonColors[c, r] < 0) continue;
                    // Piñata 비앵커 셀(sizeW==0)은 스킵 — 앵커 1개만 생성
                    int gi = _balloonGimmicks[c, r];
                    // ROLLBACK_ICE_MANUAL_GROUP_20260608:
                    // Persist explicit Ice grouping only on Ice cells. Group 0 preserves legacy adjacency grouping.
                    bool isIceCell = gi > 0 && gi < FIELD_GIMMICK_NAMES.Length
                        && FIELD_GIMMICK_NAMES[gi] == "Ice";
                    // ROLLBACK_BARRICADE_SIZED_FIELD_GIMMICK:
                    // Sized field gimmick non-anchor cells(sizeW==0) are skipped; only the anchor emits layout data.
                    bool isSizedFieldCell = gi > 0 && gi < FIELD_GIMMICK_NAMES.Length
                        && IsSizedFieldGimmick(FIELD_GIMMICK_NAMES[gi]);
                    // ROLLBACK_BARRICADE_MAPMAKER_FOOTPRINT_20260608: 바리케이드 비앵커 footprint 셀(sizeW==0)도 스킵 — 앵커만 emit.
                    bool isBarricadeCell = gi > 0 && gi < FIELD_GIMMICK_NAMES.Length
                        && FIELD_GIMMICK_NAMES[gi] == "Barricade";
                    if ((isSizedFieldCell || isBarricadeCell) && _balloonPinataW[c, r] == 0) continue;

                    // FlexTube — sequenceIndex 로 partType 자동 결정. rotation 은 런타임 spawn 에서 계산(0 저장).
                    // 가드: 현재 _balloonGimmicks 가 FlexTube 인 cell 만 ftGroupId/Seq 유효 처리.
                    bool cellIsFlexTube = gi > 0 && gi < FIELD_GIMMICK_NAMES.Length && FIELD_GIMMICK_NAMES[gi] == "FlexTube";
                    int ftGroupId = -1;
                    int ftSeq = -1;
                    string ftPart = "";
                    if (cellIsFlexTube)
                    {
                        ftGroupId = _balloonFlexTubeGroupId[c, r];
                        ftSeq = _balloonFlexTubeSequenceIndex[c, r];
                        if (ftGroupId >= 0 && flexTubeMaxSeq.TryGetValue(ftGroupId, out int maxSeq))
                        {
                            if (ftSeq == 0) ftPart = "StartCap";
                            else if (ftSeq == maxSeq) ftPart = "EndCap";
                            else ftPart = "Segment";
                        }
                    }

                    // Pinata_Box(Target Box): 패널에서 구성한 알 리스트(anchor 별 저장)를 직렬화. footprint 와 분리.
                    int[] eggColors = null, eggHps = null;
                    if (gi > 0 && gi < FIELD_GIMMICK_NAMES.Length && FIELD_GIMMICK_NAMES[gi] == "Pinata_Box")
                    {
                        var key = new Vector2Int(c, r);
                        if (_boxEggConfigColors.TryGetValue(key, out int[] cfgC) && cfgC != null && cfgC.Length > 0)
                        {
                            eggColors = (int[])cfgC.Clone();
                            eggHps = (_boxEggConfigHps.TryGetValue(key, out int[] cfgH) && cfgH != null && cfgH.Length == cfgC.Length)
                                ? (int[])cfgH.Clone()
                                : null;
                        }
                        else
                        {
                            // config 없으면 anchor 색 1개로 폴백.
                            eggColors = new[] { _balloonColors[c, r] };
                            eggHps = new[] { _balloonGimmickHP[c, r] > 0 ? _balloonGimmickHP[c, r] : 1 };
                        }
                    }

                    balloons.Add(new BalloonLayout
                    {
                        // ROLLBACK_IRONWALL_COLOR_SENTINEL_MINUS1_20260630: Wall(무색) 은 -1 로 저장(0=실색 오해 방지).
                        //   내부 _balloonColors 는 0(에디터 color<0=빈칸 로직 안전), 저장 포맷만 -1. 로드 시 -1→0 환원.
                        balloonId = bid++,
                        color = (gi > 0 && gi < FIELD_GIMMICK_NAMES.Length && FIELD_GIMMICK_NAMES[gi] == "Wall")
                            ? -1 : _balloonColors[c, r],
                        gridPosition = new Vector2(
                            _boardCenter.x + (c - (_gridCols - 1) * 0.5f) * spacing,
                            _boardCenter.y + (r - (_gridRows - 1) * 0.5f) * spacing),
                        gimmickType = gi > 0 && gi < FIELD_GIMMICK_NAMES.Length ? FIELD_GIMMICK_NAMES[gi] : "",
                        sizeW = _balloonPinataW[c, r],
                        sizeH = _balloonPinataH[c, r],
                        hp = _balloonGimmickHP[c, r],
                        iceBlockSize = _balloonIceBlockSize[c, r],
                        iceGroupId = isIceCell ? _balloonIceGroupId[c, r] : 0,
                        iceGroupHp = isIceCell ? _balloonIceGroupHp[c, r] : 0,
                        iceGroupHpMode = isIceCell ? (_balloonIceGroupHpMode[c, r] == 2 ? 2 : 1) : 0,
                        // ROLLBACK_BARRICADE_MAPMAKER_20260608: 바리케이드 방향/길이 emit (Barricade 외 셀은 런타임 무시).
                        barricadeDir = _balloonBarricadeDir[c, r],
                        barricadeLength = _balloonBarricadeLength[c, r],
                        eggColors = eggColors,
                        eggHps = eggHps,
                        lockPairId = _balloonLockPairIds != null && c < _balloonLockPairIds.GetLength(0) && r < _balloonLockPairIds.GetLength(1)
                            ? _balloonLockPairIds[c, r] : -1,
                        flexTubeGroupId = ftGroupId,
                        flexTubeSequenceIndex = ftSeq,
                        flexTubePartType = ftPart,
                        flexTubeRotation = 0,  // 런타임 spawn 에서 인접 셀 위치로 계산
                        flexTubeHp = (ftGroupId >= 0) ? _balloonGimmickHP[c, r] : 0   // FlexTube 셀이면 HP 브러시값
                    });
                }
            config.balloons = balloons.ToArray();
            config.balloonCount = balloons.Count;

            // DEBUG: Lock_Key 풍선 저장 확인
            foreach (var b in config.balloons)
            {
                if (!string.IsNullOrEmpty(b.gimmickType) && b.gimmickType != "none")
                    Debug.Log($"[MapMaker SAVE] Balloon {b.balloonId}: gimmick={b.gimmickType}, lockPairId={b.lockPairId}, color={b.color}");
            }

            var holders = new List<HolderSetup>();
            int hid = 0;
            for (int r = 0; r < _holderRows; r++)
                for (int c = 0; c < _holderCols; c++)
                {
                    int hgi = _holderGimmicks[c, r];
                    string hgName = (hgi > 0 && hgi < HOLDER_GIMMICK_NAMES.Length) ? HOLDER_GIMMICK_NAMES[hgi] : "";
                    int chainGrp = _holderChainGroups[c, r];
                    bool isSpawner = hgName == "Spawner_T" || hgName == "Spawner_O";
                    // GLASSPIPE_PARITY_20260625: Glass Pipe도 authored payload → spawnerMag 미사용(아래 :0).
                    bool isPipe = hgName == "Spawner_O" || hgName == "Spawner_T";
                    if (_holderColors[c, r] < 0 && !isPipe) continue;
                    holders.Add(new HolderSetup
                    { holderId = hid++, color = isPipe ? -1 : _holderColors[c, r], magazineCount = isPipe ? 0 : _holderMags[c, r],
                      position = new Vector2(c, r), queueGimmick = hgName,
                      // [Chain fix] Chain(Linked) 기믹 holder 에만 chainGroupId 저장 — 비-Chain 의 stale 그룹값(구버전 데이터) 차단.
                      chainGroupId = (hgName == "Chain" && chainGrp > 0) ? chainGrp : -1,
                      frozenHP = _holderFrozenHP[c, r],
                      spawnerHP = isSpawner ? _holderSpawnerHP[c, r] : 0,
                      spawnerMag = isSpawner && !isPipe ? (_holderSpawnerMag[c, r] > 0 ? _holderSpawnerMag[c, r] : 20) : 0,
                      lockPairId = _holderLockPairIds != null && c < _holderLockPairIds.GetLength(0) && r < _holderLockPairIds.GetLength(1)
                          ? _holderLockPairIds[c, r] : -1 });
                }
            config.holders = holders.ToArray();
            config.queueColumns = Mathf.Clamp(_holderCols, 2, 5);

            // Use custom waypoints
            var wp = _customWaypoints.Count >= 3 ? _customWaypoints : BuildRectangularWaypoints();
            int cols = _holderCols;
            float fieldWidth = _gridCols * spacing;
            float halfFieldX = fieldWidth * 0.5f;
            // deploy point — 벨트 하단 레일 중심선
            float dpHalfW = BoardTileManager.CONVEYOR_WIDTH * 0.5f;
            float dpHalfH = BoardTileManager.CONVEYOR_HEIGHT * 0.5f;
            float dpHalfCorner = BoardTileManager.RAIL_THICKNESS * 0.5f;
            float l = _boardCenter.x - dpHalfW + dpHalfCorner;
            float rr = _boardCenter.x + dpHalfW - dpHalfCorner;
            float bz = _boardCenter.y - dpHalfH + dpHalfCorner;
            var dp = new Vector3[cols];
            for (int i = 0; i < cols; i++)
            { float tv = (i + 1f) / (cols + 1f); dp[i] = new Vector3(Mathf.Lerp(l, rr, tv), _railHeight, bz); }

            config.rail = new RailLayout
            {
                waypoints = wp.ToArray(), slotCount = _railSlotCount,
                visualType = RailRenderer.VISUAL_SPRITE_TILE, deployPoints = dp,
                smoothCorners = _smoothCorners, cornerRadius = _cornerRadius
            };
            config.railCapacity = _railSlotCount; // explicit capacity override

            config.gridCols = _gridCols; config.gridRows = _gridRows;

            // Export path grid as conveyor positions (in balloon-grid coords)
            var convPos = new List<Vector2Int>();
            if (_pathGrid != null)
            {
                int pgw = _pathGrid.GetLength(0);
                int pgh = _pathGrid.GetLength(1);
                for (int gx = 0; gx < pgw; gx++)
                    for (int gy = 0; gy < pgh; gy++)
                        if (_pathGrid[gx, gy])
                            convPos.Add(new Vector2Int(gx - PATH_PAD, gy - PATH_PAD));
            }
            config.conveyorPositions = convPos.ToArray();

            config.star1Threshold = config.balloonCount * 100;
            config.star2Threshold = Mathf.CeilToInt(config.star1Threshold * 1.5f);
            config.star3Threshold = Mathf.CeilToInt(config.star1Threshold * 2.2f);

            // 튜토리얼 스텝 저장
            if (_tutorialSteps.Count > 0)
                config.tutorialSteps = _tutorialSteps.ToArray();

            return config;
        }

        private string[] CollectGimmicks()
        {
            var set = new HashSet<string>();
            // 풍선 기믹 (FIELD_GIMMICK_NAMES 인덱스)
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                { int g = _balloonGimmicks[c, r]; if (g > 0 && g < FIELD_GIMMICK_NAMES.Length) set.Add(FIELD_GIMMICK_NAMES[g]); }
            // 보관함 기믹 (HOLDER_GIMMICK_NAMES 인덱스)
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                { int g = _holderGimmicks[c, r]; if (g > 0 && g < HOLDER_GIMMICK_NAMES.Length) set.Add(HOLDER_GIMMICK_NAMES[g]); }
            var arr = new string[set.Count]; set.CopyTo(arr); return arr;
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        //  EDIT TOOLS (8 Features)
        // ═══════════════════════════════════════════════════════════════

        #region Edit Tools

        private void BuildEditToolsSection(Transform p)
        {
            Lbl(p, "Edit Tools", 14, FontStyle.Bold);
            Sep(p);

            // ── Grid Size Display (Feature 2) ──
            Lbl(p, $"Grid: {_gridCols}\u00D7{_gridRows}", 12);

            // Crop Tool UI removed — CropGrid method retained for internal use

            // ── Shift/Move Tool (Feature 4) ──
            Lbl(p, "Shift", 12, FontStyle.Bold);
            var shiftRow1 = Row(p);
            Lbl(shiftRow1, "Amount:", w: 55);
            MakeIntField(shiftRow1, _shiftAmount, 1, 100, v => _shiftAmount = v);
            var shiftRow2 = Row(p);
            Btn(shiftRow2, "\u25B2", () => ShiftGrid(0, _shiftAmount));
            Btn(shiftRow2, "\u25BC", () => ShiftGrid(0, -_shiftAmount));
            Btn(shiftRow2, "\u25C0", () => ShiftGrid(-_shiftAmount, 0));
            Btn(shiftRow2, "\u25B6", () => ShiftGrid(_shiftAmount, 0));

            // ── Insert Row/Column (Feature 5) ──
            Lbl(p, "Insert Row/Col", 12, FontStyle.Bold);
            var insRowR = Row(p);
            Lbl(insRowR, "Row At:", w: 55);
            MakeIntField(insRowR, _insertRowAt, 0, 100, v => _insertRowAt = v);
            Btn(insRowR, "Above", () => InsertRow(_insertRowAt, true));
            Btn(insRowR, "Below", () => InsertRow(_insertRowAt, false));

            var insColR = Row(p);
            Lbl(insColR, "Col At:", w: 55);
            MakeIntField(insColR, _insertColAt, 0, 100, v => _insertColAt = v);
            Btn(insColR, "Left", () => InsertCol(_insertColAt, true));
            Btn(insColR, "Right", () => InsertCol(_insertColAt, false));

            // ── Delete Row/Column (Feature 6) ──
            Lbl(p, "Delete Row/Col", 12, FontStyle.Bold);
            var delRowR = Row(p);
            Lbl(delRowR, "Row:", w: 40);
            MakeIntField(delRowR, _deleteRowAt, 0, 100, v => _deleteRowAt = v);
            Btn(delRowR, "Del Row", () => DeleteRow(_deleteRowAt));

            var delColR = Row(p);
            Lbl(delColR, "Col:", w: 40);
            MakeIntField(delColR, _deleteColAt, 0, 100, v => _deleteColAt = v);
            Btn(delColR, "Del Col", () => DeleteCol(_deleteColAt));

            // ── Color Swap (Feature 8) ──
            Lbl(p, "Swap Color", 12, FontStyle.Bold);
            var swapRow1 = Row(p);
            Lbl(swapRow1, "From:", w: 40);
            var swapFromDD = DefaultControls.CreateDropdown(_uiRes);
            swapFromDD.transform.SetParent(swapRow1, false);
            var sfLE = swapFromDD.AddComponent<LayoutElement>(); sfLE.flexibleWidth = 1; sfLE.preferredHeight = 24;
            swapFromDD.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.20f);
            _swapFromDropdown = swapFromDD.GetComponent<Dropdown>();
            _swapFromDropdown.captionText.font = _font; _swapFromDropdown.captionText.fontSize = 12; _swapFromDropdown.captionText.color = Color.white;
            _swapFromDropdown.onValueChanged.AddListener(v => {
                var sorted = new List<int>(_selectedColors); sorted.Sort();
                if (v >= 0 && v < sorted.Count) _swapFromColor = sorted[v];
            });

            var swapRow2 = Row(p);
            Lbl(swapRow2, "To:", w: 40);
            var swapToDD = DefaultControls.CreateDropdown(_uiRes);
            swapToDD.transform.SetParent(swapRow2, false);
            var stLE = swapToDD.AddComponent<LayoutElement>(); stLE.flexibleWidth = 1; stLE.preferredHeight = 24;
            swapToDD.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.20f);
            _swapToDropdown = swapToDD.GetComponent<Dropdown>();
            _swapToDropdown.captionText.font = _font; _swapToDropdown.captionText.fontSize = 12; _swapToDropdown.captionText.color = Color.white;
            _swapToDropdown.onValueChanged.AddListener(v => {
                var sorted = new List<int>(_selectedColors); sorted.Sort();
                if (v >= 0 && v < sorted.Count) _swapToColor = sorted[v];
            });

            RebuildSwapDropdowns();
            var swapRow3 = Row(p);
            Btn(swapRow3, "Swap", () => SwapColors(_swapFromColor, _swapToColor));

            // ── Flood Fill (Feature 7) ──
            var fillRow = Row(p);
            _txtFillMode = Lbl(fillRow, "Fill Mode: OFF", w: 120);
            _txtFillMode.color = new Color(0.7f, 0.7f, 0.7f);
            Btn(fillRow, "Toggle Fill", () =>
            {
                _floodFillMode = !_floodFillMode;
                if (_txtFillMode != null)
                {
                    _txtFillMode.text = _floodFillMode ? "Fill Mode: ON" : "Fill Mode: OFF";
                    _txtFillMode.color = _floodFillMode ? new Color(0.5f, 0.95f, 0.5f) : new Color(0.7f, 0.7f, 0.7f);
                }
                SetStatus(_floodFillMode ? "Flood Fill ON — click a cell to fill" : "Flood Fill OFF");
            });

            // ── Save This Level (Feature 1) ──
            var saveRow = Row(p);
            Btn(saveRow, "Save This Level", () => SaveLevelToDatabase(_levelId));

            Sep(p);
        }

        // ── Feature 1: Save individual level ──

        private void SaveLevelToDatabase(int levelId)
        {
            // ROLLBACK_MAPMAKER_OLD_SO_SAVE_20260618: "Save This Level" 도 활성 탭 기준으로 저장한다.
            //   기존엔 SaveCurrentLevelToEpisode() 직접 호출이라 Old(SO) 탭에서도 Ori(Episodes)로 저장되던 버그.
            //   SaveToActiveDB() 로 라우팅 → Old=SO / Epi=Episodes 분기 적용. (Save to DB 버튼과 동일 경로)
            _levelId = levelId;
            SaveToActiveDB();
        }

        // ── Feature 3: Crop Tool ──

        private void CropGrid(int newCols, int newRows)
        {
            newCols = Mathf.Clamp(newCols, 1, 100);
            newRows = Mathf.Clamp(newRows, 1, 100);

            var newColors = new int[newCols, newRows];
            var newGimmicks = new int[newCols, newRows];
            for (int c = 0; c < newCols; c++)
                for (int r = 0; r < newRows; r++)
                {
                    newColors[c, r] = (c < _gridCols && r < _gridRows) ? _balloonColors[c, r] : -1;
                    newGimmicks[c, r] = (c < _gridCols && r < _gridRows) ? _balloonGimmicks[c, r] : 0;
                }

            _gridCols = newCols;
            _gridRows = newRows;
            _balloonColors = newColors;
            _balloonGimmicks = newGimmicks;
            OnBalloonGridChanged();
            SetStatus($"Cropped to {newCols}\u00D7{newRows}");
        }

        // ── Feature 4: Shift/Move Tool ──

        private void ShiftGrid(int dx, int dy)
        {
            var newColors = new int[_gridCols, _gridRows];
            var newGimmicks = new int[_gridCols, _gridRows];
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                { newColors[c, r] = -1; newGimmicks[c, r] = 0; }

            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                {
                    int nc = c + dx;
                    int nr = r + dy;
                    if (nc >= 0 && nc < _gridCols && nr >= 0 && nr < _gridRows)
                    {
                        newColors[nc, nr] = _balloonColors[c, r];
                        newGimmicks[nc, nr] = _balloonGimmicks[c, r];
                    }
                }

            _balloonColors = newColors;
            _balloonGimmicks = newGimmicks;
            OnBalloonGridChanged();
            SetStatus($"Shifted ({dx}, {dy})");
        }

        // ── Feature 5: Insert Row/Column ──

        private void InsertRow(int at, bool above)
        {
            int insertAt = above ? at : at + 1;
            insertAt = Mathf.Clamp(insertAt, 0, _gridRows);
            int newRows = _gridRows + 1;

            _balloonColors = InsertRowGrid(_balloonColors, _gridCols, _gridRows, insertAt, -1);
            _balloonGimmicks = InsertRowGrid(_balloonGimmicks, _gridCols, _gridRows, insertAt, 0);
            _balloonGimmickHP = InsertRowGrid(_balloonGimmickHP, _gridCols, _gridRows, insertAt, 2);
            _balloonPinataW = InsertRowGrid(_balloonPinataW, _gridCols, _gridRows, insertAt, 1);
            _balloonPinataH = InsertRowGrid(_balloonPinataH, _gridCols, _gridRows, insertAt, 1);
            _balloonIceBlockSize = InsertRowGrid(_balloonIceBlockSize, _gridCols, _gridRows, insertAt, 1);
            // ROLLBACK_ICE_MANUAL_GROUP_20260608:
            // Keep explicit Ice group metadata aligned when rows/columns are edited.
            _balloonIceGroupId = InsertRowGrid(_balloonIceGroupId, _gridCols, _gridRows, insertAt, 0);
            _balloonIceGroupHp = InsertRowGrid(_balloonIceGroupHp, _gridCols, _gridRows, insertAt, 0);
            _balloonIceGroupHpMode = InsertRowGrid(_balloonIceGroupHpMode, _gridCols, _gridRows, insertAt, 1);
            _balloonBarricadeDir = InsertRowGrid(_balloonBarricadeDir, _gridCols, _gridRows, insertAt, 1);
            _balloonBarricadeLength = InsertRowGrid(_balloonBarricadeLength, _gridCols, _gridRows, insertAt, 1);
            _balloonLockPairIds = InsertRowGrid(_balloonLockPairIds, _gridCols, _gridRows, insertAt, -1);
            _balloonFlexTubeGroupId = InsertRowGrid(_balloonFlexTubeGroupId, _gridCols, _gridRows, insertAt, -1);
            _balloonFlexTubeSequenceIndex = InsertRowGrid(_balloonFlexTubeSequenceIndex, _gridCols, _gridRows, insertAt, -1);
            ShiftFlexTubePaintOrderAfterRowInsert(insertAt);
            _gridRows = newRows;
            OnBalloonGridChanged();
            SetStatus($"Inserted row {(above ? "above" : "below")} {at}");
        }

        private void InsertCol(int at, bool left)
        {
            int insertAt = left ? at : at + 1;
            insertAt = Mathf.Clamp(insertAt, 0, _gridCols);
            int newCols = _gridCols + 1;

            _balloonColors = InsertColGrid(_balloonColors, _gridCols, _gridRows, insertAt, -1);
            _balloonGimmicks = InsertColGrid(_balloonGimmicks, _gridCols, _gridRows, insertAt, 0);
            _balloonGimmickHP = InsertColGrid(_balloonGimmickHP, _gridCols, _gridRows, insertAt, 2);
            _balloonPinataW = InsertColGrid(_balloonPinataW, _gridCols, _gridRows, insertAt, 1);
            _balloonPinataH = InsertColGrid(_balloonPinataH, _gridCols, _gridRows, insertAt, 1);
            _balloonIceBlockSize = InsertColGrid(_balloonIceBlockSize, _gridCols, _gridRows, insertAt, 1);
            // ROLLBACK_ICE_MANUAL_GROUP_20260608:
            // Keep explicit Ice group metadata aligned when rows/columns are edited.
            _balloonIceGroupId = InsertColGrid(_balloonIceGroupId, _gridCols, _gridRows, insertAt, 0);
            _balloonIceGroupHp = InsertColGrid(_balloonIceGroupHp, _gridCols, _gridRows, insertAt, 0);
            _balloonIceGroupHpMode = InsertColGrid(_balloonIceGroupHpMode, _gridCols, _gridRows, insertAt, 1);
            _balloonBarricadeDir = InsertColGrid(_balloonBarricadeDir, _gridCols, _gridRows, insertAt, 1);
            _balloonBarricadeLength = InsertColGrid(_balloonBarricadeLength, _gridCols, _gridRows, insertAt, 1);
            _balloonLockPairIds = InsertColGrid(_balloonLockPairIds, _gridCols, _gridRows, insertAt, -1);
            _balloonFlexTubeGroupId = InsertColGrid(_balloonFlexTubeGroupId, _gridCols, _gridRows, insertAt, -1);
            _balloonFlexTubeSequenceIndex = InsertColGrid(_balloonFlexTubeSequenceIndex, _gridCols, _gridRows, insertAt, -1);
            ShiftFlexTubePaintOrderAfterColInsert(insertAt);
            _gridCols = newCols;
            OnBalloonGridChanged();
            SetStatus($"Inserted col {(left ? "left of" : "right of")} {at}");
        }

        // ── Feature 6: Delete Row/Column ──

        private void DeleteRow(int at)
        {
            if (_gridRows <= 1) { SetStatus("Cannot delete: only 1 row left"); return; }
            if (at < 0 || at >= _gridRows) { SetStatus($"Row {at} out of range (0-{_gridRows - 1})"); return; }
            int newRows = _gridRows - 1;

            _balloonColors = DeleteRowGrid(_balloonColors, _gridCols, _gridRows, at, -1);
            _balloonGimmicks = DeleteRowGrid(_balloonGimmicks, _gridCols, _gridRows, at, 0);
            _balloonGimmickHP = DeleteRowGrid(_balloonGimmickHP, _gridCols, _gridRows, at, 2);
            _balloonPinataW = DeleteRowGrid(_balloonPinataW, _gridCols, _gridRows, at, 1);
            _balloonPinataH = DeleteRowGrid(_balloonPinataH, _gridCols, _gridRows, at, 1);
            _balloonIceBlockSize = DeleteRowGrid(_balloonIceBlockSize, _gridCols, _gridRows, at, 1);
            // ROLLBACK_ICE_MANUAL_GROUP_20260608:
            // Keep explicit Ice group metadata aligned when rows/columns are edited.
            _balloonIceGroupId = DeleteRowGrid(_balloonIceGroupId, _gridCols, _gridRows, at, 0);
            _balloonIceGroupHp = DeleteRowGrid(_balloonIceGroupHp, _gridCols, _gridRows, at, 0);
            _balloonIceGroupHpMode = DeleteRowGrid(_balloonIceGroupHpMode, _gridCols, _gridRows, at, 1);
            _balloonBarricadeDir = DeleteRowGrid(_balloonBarricadeDir, _gridCols, _gridRows, at, 1);
            _balloonBarricadeLength = DeleteRowGrid(_balloonBarricadeLength, _gridCols, _gridRows, at, 1);
            _balloonLockPairIds = DeleteRowGrid(_balloonLockPairIds, _gridCols, _gridRows, at, -1);
            _balloonFlexTubeGroupId = DeleteRowGrid(_balloonFlexTubeGroupId, _gridCols, _gridRows, at, -1);
            _balloonFlexTubeSequenceIndex = DeleteRowGrid(_balloonFlexTubeSequenceIndex, _gridCols, _gridRows, at, -1);
            ShiftFlexTubePaintOrderAfterRowDelete(at);
            _gridRows = newRows;
            OnBalloonGridChanged();
            SetStatus($"Deleted row {at}");
        }

        private void DeleteCol(int at)
        {
            if (_gridCols <= 1) { SetStatus("Cannot delete: only 1 col left"); return; }
            if (at < 0 || at >= _gridCols) { SetStatus($"Col {at} out of range (0-{_gridCols - 1})"); return; }
            int newCols = _gridCols - 1;

            _balloonColors = DeleteColGrid(_balloonColors, _gridCols, _gridRows, at, -1);
            _balloonGimmicks = DeleteColGrid(_balloonGimmicks, _gridCols, _gridRows, at, 0);
            _balloonGimmickHP = DeleteColGrid(_balloonGimmickHP, _gridCols, _gridRows, at, 2);
            _balloonPinataW = DeleteColGrid(_balloonPinataW, _gridCols, _gridRows, at, 1);
            _balloonPinataH = DeleteColGrid(_balloonPinataH, _gridCols, _gridRows, at, 1);
            _balloonIceBlockSize = DeleteColGrid(_balloonIceBlockSize, _gridCols, _gridRows, at, 1);
            // ROLLBACK_ICE_MANUAL_GROUP_20260608:
            // Keep explicit Ice group metadata aligned when rows/columns are edited.
            _balloonIceGroupId = DeleteColGrid(_balloonIceGroupId, _gridCols, _gridRows, at, 0);
            _balloonIceGroupHp = DeleteColGrid(_balloonIceGroupHp, _gridCols, _gridRows, at, 0);
            _balloonIceGroupHpMode = DeleteColGrid(_balloonIceGroupHpMode, _gridCols, _gridRows, at, 1);
            _balloonBarricadeDir = DeleteColGrid(_balloonBarricadeDir, _gridCols, _gridRows, at, 1);
            _balloonBarricadeLength = DeleteColGrid(_balloonBarricadeLength, _gridCols, _gridRows, at, 1);
            _balloonLockPairIds = DeleteColGrid(_balloonLockPairIds, _gridCols, _gridRows, at, -1);
            _balloonFlexTubeGroupId = DeleteColGrid(_balloonFlexTubeGroupId, _gridCols, _gridRows, at, -1);
            _balloonFlexTubeSequenceIndex = DeleteColGrid(_balloonFlexTubeSequenceIndex, _gridCols, _gridRows, at, -1);
            ShiftFlexTubePaintOrderAfterColDelete(at);
            _gridCols = newCols;
            OnBalloonGridChanged();
            SetStatus($"Deleted col {at}");
        }

        // ── Feature 7: Flood Fill ──

        /// <summary>특정 색상의 풍선을 전부 제거.</summary>
        private void ApplyIceGroupBrushMeta(int col, int row, bool isIce)
        {
            // ROLLBACK_ICE_MANUAL_GROUP_20260608:
            // Ice group metadata is meaningful only for Ice cells. Other gimmicks clear it to avoid stale groups.
            if (isIce)
            {
                _balloonIceGroupId[col, row] = Mathf.Max(0, _paintIceGroupId);
                _balloonIceGroupHp[col, row] = Mathf.Max(0, _paintIceGroupHp);
                _balloonIceGroupHpMode[col, row] = _paintIceGroupHpMode == 2 ? 2 : 1;
            }
            else
            {
                _balloonIceGroupId[col, row] = 0;
                _balloonIceGroupHp[col, row] = 0;
                _balloonIceGroupHpMode[col, row] = 1;
            }
        }

        private void EraseBalloonCell(int col, int row)
        {
            _balloonColors[col, row] = -1;
            _balloonGimmicks[col, row] = 0;
            _balloonGimmickHP[col, row] = 2;
            _balloonPinataW[col, row] = 1;
            _balloonPinataH[col, row] = 1;
            _balloonIceBlockSize[col, row] = 1;
            ApplyIceGroupBrushMeta(col, row, false);
            _balloonLockPairIds[col, row] = -1;
            _balloonFlexTubeGroupId[col, row] = -1;
            _balloonFlexTubeSequenceIndex[col, row] = -1;
            _flexTubePaintOrder.RemoveAll(p => p.x == col && p.y == row);
        }

        private void ClearFieldGimmickAt(int col, int row)
        {
            if (col < 0 || col >= _gridCols || row < 0 || row >= _gridRows) return;

            int flexGroup = _balloonFlexTubeGroupId[col, row];
            if (flexGroup >= 0)
            {
                // ROLLBACK_FIELD_GIMMICK_ERASE_BUTTON_20260629:
                // FlexTube is authored as a group. Removing only one cell leaves broken group data,
                // so erase the whole matching group while preserving every cell color.
                for (int c = 0; c < _gridCols; c++)
                    for (int r = 0; r < _gridRows; r++)
                        if (_balloonFlexTubeGroupId[c, r] == flexGroup)
                            ClearFieldGimmickCell(c, r);
                _flexTubePaintOrder.Clear();
                UpdateFlexTubeStatusText();
                return;
            }

            ClearFieldGimmickCell(col, row);
        }

        private void ClearFieldGimmickCell(int col, int row)
        {
            _balloonGimmicks[col, row] = 0;
            _balloonGimmickHP[col, row] = 2;
            _balloonPinataW[col, row] = 1;
            _balloonPinataH[col, row] = 1;
            _balloonIceBlockSize[col, row] = 1;
            ApplyIceGroupBrushMeta(col, row, false);
            _balloonLockPairIds[col, row] = -1;
            _balloonBarricadeDir[col, row] = 1;
            _balloonBarricadeLength[col, row] = 1;
            _balloonFlexTubeGroupId[col, row] = -1;
            _balloonFlexTubeSequenceIndex[col, row] = -1;
            _boxEggConfigColors.Remove(new Vector2Int(col, row));
            _boxEggConfigHps.Remove(new Vector2Int(col, row));
            _flexTubePaintOrder.RemoveAll(p => p.x == col && p.y == row);
            UpdatePreviewCell(col, row);
        }

        private void FillBalloonCellWithBrush(int col, int row, int color)
        {
            bool applyHiddenBalloonGimmick = color >= 0
                && _paintGimmick > 0
                && _paintGimmick < FIELD_GIMMICK_NAMES.Length
                && FIELD_GIMMICK_NAMES[_paintGimmick] == "Surprise";

            // ROLLBACK_FILL_NEIGHBOR_HIDDEN_ONLY_20260629:
            // Fill Neighbor/Flood Fill should only copy the current field gimmick when it is
            // Hidden Balloon(Surprise). Other footprint/group gimmicks keep the legacy color-only
            // behavior to avoid breaking authored sizes, groups, or sequences.
            if (_fieldGimmickOnlyMode && !applyHiddenBalloonGimmick)
                return;

            if (!_fieldGimmickOnlyMode)
                _balloonColors[col, row] = color;
            else if (_balloonColors[col, row] < 0)
                return;

            _balloonGimmicks[col, row] = applyHiddenBalloonGimmick ? _paintGimmick : 0;
            _balloonGimmickHP[col, row] = _paintPinataHP;
            _balloonPinataW[col, row] = 1;
            _balloonPinataH[col, row] = 1;

            bool isLockKeyGimmick = _balloonGimmicks[col, row] > 0
                && _balloonGimmicks[col, row] < FIELD_GIMMICK_NAMES.Length
                && FIELD_GIMMICK_NAMES[_balloonGimmicks[col, row]] == "Lock_Key";
            bool isIceGimmick = _balloonGimmicks[col, row] > 0
                && _balloonGimmicks[col, row] < FIELD_GIMMICK_NAMES.Length
                && FIELD_GIMMICK_NAMES[_balloonGimmicks[col, row]] == "Ice";
            ApplyIceGroupBrushMeta(col, row, isIceGimmick);
            _balloonLockPairIds[col, row] = isLockKeyGimmick ? _paintLockPairId : -1;
            _balloonFlexTubeGroupId[col, row] = -1;
            _balloonFlexTubeSequenceIndex[col, row] = -1;
            _flexTubePaintOrder.RemoveAll(p => p.x == col && p.y == row);
        }

        private void ReplaceClickedColorWithBrush(int col, int row)
        {
            int fromColor = _balloonColors[col, row];
            if (fromColor < 0) { SetStatus("Click a colored cell"); return; }
            if (_paintColor < 0) { SetStatus("Select a target color first"); return; }
            SwapColors(fromColor, _paintColor);
        }

        private void EraseColor(int color)
        {
            if (color < 0) { SetStatus("Select a color first"); return; }
            int count = 0;
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                    if (_balloonColors[c, r] == color)
                    {
                        EraseBalloonCell(c, r);
                        count++;
                    }
            SetStatus($"Erased {count} cells of color {color}");
        }

        /// <summary>클릭한 셀과 이웃한 같은 색상 셀들을 전부 제거 (BFS).</summary>
        private void EraseNeighborSameColor(int startCol, int startRow)
        {
            int targetColor = _balloonColors[startCol, startRow];
            if (targetColor < 0) { SetStatus("Click a colored cell"); return; }

            var queue = new Queue<Vector2Int>();
            var visited = new HashSet<Vector2Int>();
            queue.Enqueue(new Vector2Int(startCol, startRow));
            visited.Add(new Vector2Int(startCol, startRow));

            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                int c = cell.x, r = cell.y;
                EraseBalloonCell(c, r);

                Vector2Int[] dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0),
                                      new Vector2Int(0, 1), new Vector2Int(0, -1) };
                foreach (var d in dirs)
                {
                    int nc = c + d.x, nr = r + d.y;
                    var np = new Vector2Int(nc, nr);
                    if (nc >= 0 && nc < _gridCols && nr >= 0 && nr < _gridRows
                        && !visited.Contains(np) && _balloonColors[nc, nr] == targetColor)
                    {
                        visited.Add(np);
                        queue.Enqueue(np);
                    }
                }
            }
            SetStatus($"Erased {visited.Count} neighbor cells (color {targetColor})");
        }

        /// <summary>클릭한 빈 셀과 이웃한 빈 셀들을 현재 브러시 색상으로 채움 (BFS).</summary>
        private void FillNeighborSameColor(int startCol, int startRow, int fillColor)
        {
            if (fillColor < 0) { SetStatus("Select a color first"); return; }

            int targetColor = _balloonColors[startCol, startRow];
            var queue = new Queue<Vector2Int>();
            var visited = new HashSet<Vector2Int>();
            queue.Enqueue(new Vector2Int(startCol, startRow));
            visited.Add(new Vector2Int(startCol, startRow));

            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                int c = cell.x, r = cell.y;
                FillBalloonCellWithBrush(c, r, fillColor);

                Vector2Int[] dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0),
                                      new Vector2Int(0, 1), new Vector2Int(0, -1) };
                foreach (var d in dirs)
                {
                    int nc = c + d.x, nr = r + d.y;
                    var np = new Vector2Int(nc, nr);
                    if (nc >= 0 && nc < _gridCols && nr >= 0 && nr < _gridRows
                        && !visited.Contains(np) && _balloonColors[nc, nr] == targetColor)
                    {
                        visited.Add(np);
                        queue.Enqueue(np);
                    }
                }
            }
            SetStatus($"Filled {visited.Count} neighbor cells ({targetColor} -> {fillColor})");
        }

        private void FloodFill(int startCol, int startRow, int newColor)
        {
            int targetColor = _balloonColors[startCol, startRow];
            if (targetColor == newColor) return;

            var queue = new Queue<Vector2Int>();
            var visited = new HashSet<Vector2Int>();
            queue.Enqueue(new Vector2Int(startCol, startRow));
            visited.Add(new Vector2Int(startCol, startRow));

            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                int c = cell.x, r = cell.y;
                if (newColor >= 0)
                    FillBalloonCellWithBrush(c, r, newColor);
                else
                    EraseBalloonCell(c, r);

                Vector2Int[] dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0),
                                      new Vector2Int(0, 1), new Vector2Int(0, -1) };
                foreach (var d in dirs)
                {
                    int nc = c + d.x, nr = r + d.y;
                    var np = new Vector2Int(nc, nr);
                    if (nc >= 0 && nc < _gridCols && nr >= 0 && nr < _gridRows
                        && !visited.Contains(np) && _balloonColors[nc, nr] == targetColor)
                    {
                        visited.Add(np);
                        queue.Enqueue(np);
                    }
                }
            }
            SetStatus($"Flood filled {visited.Count} cells");
        }

        // ── Feature 8: Color Swap ──

        private void RebuildSwapDropdowns()
        {
            var sorted = new List<int>(_selectedColors);
            sorted.Sort();
            var options = new List<string>();
            foreach (int ci in sorted)
                options.Add($"{ci}: {COLOR_LABELS[ci]}");

            if (_swapFromDropdown != null)
            {
                _swapFromDropdown.ClearOptions();
                _swapFromDropdown.AddOptions(options);
                int fromIdx = sorted.IndexOf(_swapFromColor);
                _swapFromDropdown.value = fromIdx >= 0 ? fromIdx : 0;
                if (sorted.Count > 0) _swapFromColor = sorted[_swapFromDropdown.value];
            }
            if (_swapToDropdown != null)
            {
                _swapToDropdown.ClearOptions();
                _swapToDropdown.AddOptions(options);
                int toIdx = sorted.IndexOf(_swapToColor);
                _swapToDropdown.value = toIdx >= 0 ? toIdx : (sorted.Count > 1 ? 1 : 0);
                if (sorted.Count > 0) _swapToColor = sorted[_swapToDropdown.value];
            }
        }

        private void SwapColors(int fromColor, int toColor)
        {
            if (fromColor == toColor) { SetStatus("From and To colors are the same"); return; }
            int balloonCount = 0;
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                    if (_balloonColors[c, r] == fromColor)
                    {
                        _balloonColors[c, r] = toColor;
                        balloonCount++;
                    }
            // 보관함(큐) 색상도 같이 swap
            int holderCount = 0;
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                    if (_holderColors[c, r] == fromColor)
                    {
                        _holderColors[c, r] = toColor;
                        holderCount++;
                    }
            OnBalloonGridChanged();
            RebuildHolderUI();
            _infoDirty = true;
            SetStatus($"Swapped color {fromColor} -> {toColor} (balloons:{balloonCount}, holders:{holderCount})");
        }

        #endregion

        #region Counting

        private int CountBalloons()
        {
            int n = 0;
            for (int c = 0; c < _gridCols; c++) for (int r = 0; r < _gridRows; r++) if (_balloonColors[c, r] >= 0) n++;
            return n;
        }

        private int CountHolders()
        {
            int n = 0;
            for (int c = 0; c < _holderCols; c++) for (int r = 0; r < _holderRows; r++) if (_holderColors[c, r] >= 0) n++;
            return n;
        }

        private int CountTotalMags()
        {
            int n = 0;
            for (int c = 0; c < _holderCols; c++) for (int r = 0; r < _holderRows; r++) if (_holderColors[c, r] >= 0) n += _holderMags[c, r];
            return n;
        }

        private int CalcTotalDarts()
        {
            return CountTotalMags();
        }

        #endregion
    }
}
#endif
