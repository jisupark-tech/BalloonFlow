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

        private static readonly string[] COLOR_LABELS =
            { "HP", "Cy", "Pu", "Yl", "Gr", "Or", "Wh", "DG",
              "SB", "Fo", "Rd", "Bl", "Tl", "Lv", "Pw", "Br",
              "Cr", "Pk", "Wi", "Mt", "In", "Rs", "Si", "Gy",
              "Mg", "Am", "Cm", "Sa" };

        // 전체 기믹 (기존 호환 유지)
        private static readonly string[] GIMMICK_NAMES =
            { "(none)", "Hidden", "Chain", "Pinata", "Spawner_T", "Pin", "Lock_Key",
              "Surprise", "Wall", "Spawner_O", "Pinata_Box", "Ice", "Frozen_Dart", "Color_Curtain", "Barricade" };

        // 풍선(필드) 기믹만
        private static readonly string[] FIELD_GIMMICK_NAMES =
            { "(none)", "Pinata", "Pin", "Surprise", "Wall", "Pinata_Box", "Ice", "Color_Curtain", "Lock_Key", "Barricade" };

        // 보관함(큐) 기믹만
        private static readonly string[] HOLDER_GIMMICK_NAMES =
            { "(none)", "Hidden", "Chain", "Spawner_T", "Spawner_O", "Frozen_Dart", "Lock_Key" };

        // Short gimmick symbols for preview overlay
        private static readonly string[] GIMMICK_MARKS =
            { "", "H", "Ch", "Pi", "ST", "Pn", "LK", "?!", "W", "SO", "PB", "Ic", "FD", "CC", "Bc" };

        private static readonly Color GIMMICK_WALL_COLOR  = new Color(0.35f, 0.35f, 0.38f);
        private static readonly Color GIMMICK_PIN_COLOR   = new Color(0.70f, 0.50f, 0.20f);
        private static readonly Color GIMMICK_ICE_COLOR   = new Color(0.65f, 0.85f, 0.95f);
        private static readonly Color GIMMICK_HIDDEN_COLOR = new Color(0.45f, 0.45f, 0.50f);
        private static readonly Color GIMMICK_PINATA_COLOR = new Color(0.95f, 0.70f, 0.20f);

        private static readonly string[] TILE_NAMES =
            { "bl", "br", "h", "tl", "tr", "v" };

        private const float LEFT_PANEL_WIDTH = 240f;
        private const float RIGHT_PANEL_WIDTH = 400f;

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

        private int _gridCols = 5;
        private int _gridRows = 5;
        private float _boardWorldSize = 8f;
        private Vector2 _boardCenter = new Vector2(0f, 2f);

        private int[,] _balloonColors;
        private int[,] _balloonGimmicks;

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
        private int _paintPinataHP = 2;    // 브러시용 Piñata HP
        private int _paintPinataW = 1;     // 브러시용 Piñata 가로
        private int _paintPinataH = 1;     // 브러시용 Piñata 세로
        private int _paintChainGroup = 0;  // 브러시용 Chain 그룹 ID
        private int _nextChainGroupId = 1; // 자동 증가 Chain 그룹 ID
        private int _paintFrozenHP = 3;    // 브러시용 Frozen Dart 해동 체력
        private int _paintSpawnerHP = 3;   // 브러시용 Spawner 소환 횟수
        private int _paintSpawnerMag = 20; // 브러시용 Spawner 소환 탄창
        private int _paintLockPairId = 0;  // 브러시용 Lock_Key pair ID
        private int _nextLockPairId = 1;   // 자동 증가 Lock pair ID
        private int[,] _balloonLockPairIds; // 풍선별 Lock pair ID (-1 = 없음)
        private int[,] _holderLockPairIds;  // 보관함별 Lock pair ID (-1 = 없음)

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
        private RectTransform _fieldGimmickSizeRow;
        private RectTransform _fieldGimmickLockRow;
        private RectTransform _fieldGimmickChainRow;
        private RectTransform _fieldGimmickFrozenRow;

        // Gimmick-specific UI rows (Holder brush)
        private RectTransform _holderGimmickChainRow;
        private RectTransform _holderGimmickFrozenRow;
        private RectTransform _holderGimmickSpawnerRow;
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

        // Level Info UI 참조 (로드 시 갱신용)
        private InputField _levelIdInput;
        // _numColorsInput removed — replaced by color toggle grid
        private Dropdown _difficultyDropdown;
        private Text[] _palTexts;
        private Transform _holderGridContainer;
        private LevelDatabase _targetDB;

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
            RebuildPreview();
            RebuildGridLines();
            RebuildConveyorPreview();
            RebuildWaypointPreview();
            RefreshInfo();
            RefreshLevelList();

            // 테스트 플레이 복귀 시 마지막 편집 레벨, 처음이면 레벨 1 로드
            int lastEditedLevel = EditorPrefs.GetInt("BalloonFlow_LastEditedLevel", 1);
            if (lastEditedLevel > 0)
            {
                LoadLevelById(lastEditedLevel);
            }
        }

        private void Update()
        {
            HandlePaintInput();
            HandleKeyboard();
            HandleCameraControl();
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
            if (mouse.middleButton.isPressed || mouse.rightButton.isPressed)
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
        private static Shader FindLitShader()
        {
            // URP Lit (most BalloonFlow setups)
            var s = Shader.Find("Universal Render Pipeline/Lit");
            if (s != null) return s;
            // URP Simple Lit
            s = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (s != null) return s;
            // Built-in Standard
            s = Shader.Find("Standard");
            if (s != null) return s;
            // Last resort
            return Shader.Find("Sprites/Default");
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
            _balloonLockPairIds = ResizeGrid(_balloonLockPairIds, _gridCols, _gridRows, -1);
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

            // Scroll area
            var scrollArea = MakeRT("ScrollArea", panel);
            scrollArea.anchorMin = Vector2.zero;
            scrollArea.anchorMax = Vector2.one;
            scrollArea.sizeDelta = new Vector2(0, -36);
            scrollArea.anchoredPosition = new Vector2(0, -18);

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

            _selectedColors.Clear();
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
            RefreshInfo();
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
            AutoRoundBalloonCounts();
            OnBalloonGridChanged();
            RebuildHolderUI();
            RefreshInfo();
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
            fgdd.AddOptions(new List<string>(FIELD_GIMMICK_NAMES));
            fgdd.value = 0;
            fgdd.captionText.font = _font; fgdd.captionText.fontSize = 12; fgdd.captionText.color = Color.white;
            fgdd.onValueChanged.AddListener(v => {
                _paintGimmick = v; // FIELD_GIMMICK_NAMES 인덱스 그대로 사용
                string name = FIELD_GIMMICK_NAMES[v];
                UpdateFieldGimmickUI(name);
                SetStatus($"Field Gimmick: {name}");
            });

            // Piñata HP (shown for Pinata/Pinata_Box)
            var hpRow = Row(p); Lbl(hpRow, "Piñata HP", w: 110);
            MakeIntField(hpRow, _paintPinataHP, 1, 50, v => {
                _paintPinataHP = v;
                SetStatus($"Piñata HP: {v}");
            });
            _fieldGimmickHPRow = hpRow.GetComponent<RectTransform>();

            // Piñata Size (shown for Pinata/Pinata_Box)
            var sizeRow = Row(p); Lbl(sizeRow, "Piñata Size", w: 110);
            MakeIntField(sizeRow, _paintPinataW, 1, 6, v => {
                _paintPinataW = v;
                SetStatus($"Piñata Size: {_paintPinataW}x{_paintPinataH}");
            });
            Lbl(sizeRow, "x", w: 15);
            MakeIntField(sizeRow, _paintPinataH, 1, 6, v => {
                _paintPinataH = v;
                SetStatus($"Piñata Size: {_paintPinataW}x{_paintPinataH}");
            });
            _fieldGimmickSizeRow = sizeRow.GetComponent<RectTransform>();

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

            // Initially hide all gimmick-specific rows (none selected)
            _fieldGimmickHPRow.gameObject.SetActive(false);
            _fieldGimmickSizeRow.gameObject.SetActive(false);
            _fieldGimmickLockRow.gameObject.SetActive(false);

            Sep(p);
        }

        private void UpdateFieldGimmickUI(string gimmickName)
        {
            bool isPinata = gimmickName == "Pinata" || gimmickName == "Pinata_Box";
            bool isLockKey = gimmickName == "Lock_Key";
            if (_fieldGimmickHPRow != null) _fieldGimmickHPRow.gameObject.SetActive(isPinata);
            if (_fieldGimmickSizeRow != null) _fieldGimmickSizeRow.gameObject.SetActive(isPinata);
            if (_fieldGimmickLockRow != null) _fieldGimmickLockRow.gameObject.SetActive(isLockKey);
        }

        private void UpdateHolderGimmickUI(string gimmickName)
        {
            bool isChain = gimmickName == "Chain";
            bool isFrozen = gimmickName == "Frozen_Dart";
            bool isSpawner = gimmickName == "Spawner_T" || gimmickName == "Spawner_O";
            bool isLockKey = gimmickName == "Lock_Key";
            if (_holderGimmickChainRow != null) _holderGimmickChainRow.gameObject.SetActive(isChain);
            if (_holderGimmickFrozenRow != null) _holderGimmickFrozenRow.gameObject.SetActive(isFrozen);
            if (_holderGimmickSpawnerRow != null) _holderGimmickSpawnerRow.gameObject.SetActive(isSpawner);
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
            Btn(row2, "Fill Neighbor", () => { _fillNeighborMode = true; SetStatus("Click an empty cell to fill same-empty neighbors"); });
            Sep(p);
        }

        private bool _eraseNeighborMode;
        private bool _fillNeighborMode;

        private void BuildHolderSection(Transform p)
        {
            Lbl(p, "Holder Grid", 14, FontStyle.Bold);
            var r1 = Row(p); Lbl(r1, "Columns", w: 90);
            _holderColsInput = MakeIntField(r1, _holderCols, 1, 20, v =>
            { _holderCols = v; InitGrid(); RebuildHolderUI(); RefreshInfo(); });
            var r2 = Row(p); Lbl(r2, "Rows", w: 90);
            _holderRowsInput = MakeIntField(r2, _holderRows, 1, 20, v =>
            { _holderRows = v; InitGrid(); RebuildHolderUI(); RefreshInfo(); });
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
            hgdd.AddOptions(new List<string>(HOLDER_GIMMICK_NAMES));
            hgdd.value = 0;
            hgdd.captionText.font = _font; hgdd.captionText.fontSize = 12; hgdd.captionText.color = Color.white;
            hgdd.onValueChanged.AddListener(v => {
                _paintHolderGimmick = v;
                UpdateHolderGimmickUI(HOLDER_GIMMICK_NAMES[v]);
                SetStatus($"Holder Gimmick: {HOLDER_GIMMICK_NAMES[v]}");
            });

            // Chain 그룹 ID 설정
            var chainRow = Row(p); Lbl(chainRow, "Chain Group", w: 110);
            MakeIntField(chainRow, _paintChainGroup, 0, 99, v => {
                _paintChainGroup = v;
                SetStatus($"Chain Group: {v} (0=없음)");
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
            Lbl(spawnerRow, "Mag", w: 30);
            MakeIntField(spawnerRow, _paintSpawnerMag, 10, 50, v => { _paintSpawnerMag = v; });
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
            Btn(row, "Fill", () => { FillHolders(_paintColor); RebuildHolderUI(); RefreshInfo(); });
            Btn(row, "Clear", () => { FillHolders(-1); RebuildHolderUI(); RefreshInfo(); });
            Btn(row, "Random", () => { RandomHolders(); RebuildHolderUI(); RefreshInfo(); });
            var row2 = Row(p);
            Btn(row2, "Set Mag", () => { SetAllMags(); RebuildHolderUI(); RefreshInfo(); });
            Sep(p);

            // ── 큐 생성기 섹션 ──
            Lbl(p, "Queue Generator", 14, FontStyle.Bold);
            _queueGenScoreLabel = Lbl(p, "Score: -", 12);
            var rowGen = Row(p);
            Btn(rowGen, "Generate Queue", () => { GenerateQueue(); RebuildHolderUI(); RefreshInfo(); });
            Btn(rowGen, "Auto Balance", () => { AutoBalanceHolders(); RebuildHolderUI(); RefreshInfo(); });
            Sep(p);
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
                RefreshInfo();
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
            Btn(r2, "Auto Ring", () => { AutoConveyorRing(); RebuildConveyorPreview(); RefreshInfo(); });
            Btn(r2, "Clear Path", () => {
                int pw = _pathGrid.GetLength(0), ph = _pathGrid.GetLength(1);
                for (int c = 0; c < pw; c++)
                    for (int r = 0; r < ph; r++)
                        _pathGrid[c, r] = false;
                _customWaypoints.Clear();
                RebuildConveyorPreview(); RebuildWaypointPreview(); RefreshInfo();
            });

            Lbl(p, "  Tab=Toggle Mode. Click grid cells to draw path.", 10);
            Lbl(p, "  Path inside board removes balloons on that cell.", 10);
            Sep(p);
        }

        // ── Image Import ──
        private Texture2D _importedImage;
        private int _importGridCols = 20;
        private int _importGridRows = 20;
        private int _importRoundTo = 10;
        private float _importBgThreshold = 0.15f; // 이 밝기 이하는 배경으로 인식 (0~1)
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

                // 해상도가 낮으면 600x600 이상으로 업스케일 (NearestNeighbor)
                const int MIN_RES = 600;
                if (raw.width < MIN_RES || raw.height < MIN_RES)
                {
                    float scale = Mathf.Max((float)MIN_RES / raw.width, (float)MIN_RES / raw.height);
                    int newW = Mathf.CeilToInt(raw.width * scale);
                    int newH = Mathf.CeilToInt(raw.height * scale);

                    var rt = RenderTexture.GetTemporary(newW, newH, 0, RenderTextureFormat.ARGB32);
                    rt.filterMode = FilterMode.Point; // NearestNeighbor
                    Graphics.Blit(raw, rt);

                    var upscaled = new Texture2D(newW, newH, TextureFormat.RGBA32, false);
                    upscaled.filterMode = FilterMode.Point;
                    RenderTexture.active = rt;
                    upscaled.ReadPixels(new Rect(0, 0, newW, newH), 0, 0);
                    upscaled.Apply();
                    RenderTexture.active = null;
                    RenderTexture.ReleaseTemporary(rt);
                    Object.Destroy(raw);

                    _importedImage = upscaled;
                    SetStatus($"Image loaded: {raw.width}x{raw.height} → upscaled to {newW}x{newH}");
                }
                else
                {
                    _importedImage = raw;
                    SetStatus($"Image loaded: {raw.width}x{raw.height}");
                }
                UpdateImagePreview();
            });
            Btn(r1, "Apply", () =>
            {
                if (_importPreview == null) { SetStatus("Load an image first"); return; }
                ApplyImageToGrid();
            });

            var r2 = Row(p); Lbl(r2, "Grid W", w: 50);
            MakeIntField(r2, _importGridCols, 4, 100, v => { _importGridCols = v; UpdateImagePreview(); });
            Lbl(r2, "H", w: 20);
            MakeIntField(r2, _importGridRows, 4, 100, v => { _importGridRows = v; UpdateImagePreview(); });

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

        private void UpdateImagePreview()
        {
            if (_importedImage == null) return;

            int gw = _importGridCols, gh = _importGridRows;
            var selColorList = new List<int>(_selectedColors);
            if (selColorList.Count == 0) return;
            _importPreview = new int[gw, gh];

            // PixelArtConverter 방식: 원본 해상도에서 팔레트 매칭 → 그리드 셀별 최빈값(MODE)
            int srcW = _importedImage.width;
            int srcH = _importedImage.height;

            // 1단계: 원본 해상도의 모든 픽셀을 팔레트 인덱스로 변환 (full-res quantize)
            // 팔레트 색상을 미리 배열로 변환 (성능)
            float[] palR = new float[selColorList.Count];
            float[] palG = new float[selColorList.Count];
            float[] palB = new float[selColorList.Count];
            for (int si = 0; si < selColorList.Count; si++)
            {
                int pi = selColorList[si];
                palR[si] = PALETTE[pi].r; palG[si] = PALETTE[pi].g; palB[si] = PALETTE[pi].b;
            }

            // 2단계: 그리드 셀별로 원본 픽셀 블록의 최빈 팔레트 색상 선택
            var colorVotes = new Dictionary<int, int>(); // paletteIndex → count

            for (int c = 0; c < gw; c++)
            {
                for (int r = 0; r < gh; r++)
                {
                    int x0 = (int)((float)c / gw * srcW);
                    int x1 = Mathf.Max(x0 + 1, (int)((float)(c + 1) / gw * srcW));
                    int y0 = (int)((float)r / gh * srcH);
                    int y1 = Mathf.Max(y0 + 1, (int)((float)(r + 1) / gh * srcH));

                    colorVotes.Clear();
                    int bgCount = 0;
                    int totalPixels = 0;

                    for (int py = y0; py < y1 && py < srcH; py++)
                    {
                        for (int px = x0; px < x1 && px < srcW; px++)
                        {
                            Color p = _importedImage.GetPixel(px, py);
                            totalPixels++;

                            // 투명 또는 어두운 배경
                            float brightness = p.r * 0.299f + p.g * 0.587f + p.b * 0.114f;
                            if (p.a < 0.5f || brightness < _importBgThreshold)
                            {
                                bgCount++;
                                continue;
                            }

                            // 이 픽셀의 가장 가까운 팔레트 색상 (원본 해상도에서 매칭)
                            int bestSi = 0;
                            float bestDist = float.MaxValue;
                            for (int si = 0; si < selColorList.Count; si++)
                            {
                                float dr = p.r - palR[si];
                                float dg = p.g - palG[si];
                                float db = p.b - palB[si];
                                // Redmean 가중 거리 (인간 시각 최적화)
                                float rmean = (p.r + palR[si]) * 0.5f;
                                float dist = (2f + rmean) * dr * dr + 4f * dg * dg + (3f - rmean) * db * db;
                                if (dist < bestDist) { bestDist = dist; bestSi = si; }
                            }

                            int palIdx = selColorList[bestSi];
                            if (colorVotes.ContainsKey(palIdx)) colorVotes[palIdx]++;
                            else colorVotes[palIdx] = 1;
                        }
                    }

                    // 배경이 과반수면 빈 셀
                    if (bgCount > totalPixels / 2 || colorVotes.Count == 0)
                    {
                        _importPreview[c, r] = -1;
                        continue;
                    }

                    // 최빈 색상 (MODE) 선택
                    int modeColor = selColorList[0];
                    int modeCount = 0;
                    foreach (var kvp in colorVotes)
                    {
                        if (kvp.Value > modeCount) { modeCount = kvp.Value; modeColor = kvp.Key; }
                    }
                    _importPreview[c, r] = modeColor;
                }
            }

            // 색상별 카운트를 10의 배수로 조정
            if (_importRoundTo > 0)
                RoundColorCounts(_importPreview, gw, gh, _importRoundTo);

            // 프리뷰를 현재 그리드에 반영
            _gridCols = gw;
            _gridRows = gh;
            InitGrid();
            for (int c = 0; c < gw; c++)
                for (int r = 0; r < gh; r++)
                    _balloonColors[c, r] = _importPreview[c, r];

            _levelLoaded = true;
            OnBalloonGridChanged();

            // 색상별 카운트 로그
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
            var sb = new System.Text.StringBuilder($"Preview: {gw}x{gh} {_numColors}C {totalBalloons}B — ");
            foreach (var kvp in counts) sb.Append($"c{kvp.Key}={kvp.Value} ");
            SetStatus(sb.ToString());
        }

        private void RoundColorCounts(int[,] grid, int cols, int rows, int multiple)
        {
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
            _selectedColors.Clear();
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
            RefreshInfo();

            int totalB = 0;
            foreach (var v in colorCounts.Values) totalB += v;
            SetStatus($"Applied: {gw}x{gh}, {_numColors}C, {totalB}B, {holders.Count} holders, rail={railCap}");
        }

        private void BuildExportSection(Transform p)
        {
            Lbl(p, "Export / Import", 14, FontStyle.Bold);
            var row = Row(p);
            Btn(row, "Save to DB", SaveToDatabase);
            Btn(row, "Export JSON", ExportJson);
            Btn(row, "Load Level", () => LoadLevelById(_levelId));
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
                var stepRow = Row(_tutorialStepContainer);
                Lbl(stepRow, $"#{i}", w: 25);
                MakeInputField(stepRow, step.instruction, s => _tutorialSteps[idx].instruction = s);
                Btn(stepRow, "X", () => { _tutorialSteps.RemoveAt(idx); RebuildTutorialStepUI(parent); });
            }

            if (_tutorialSteps.Count > 0)
                SetStatus($"Tutorial: {_tutorialSteps.Count} steps");
        }

        #endregion

        #region UI Building — Holder Grid

        private void RebuildHolderUI()
        {
            if (_holderGridContainer == null) return;

            // Unparent first so GridLayoutGroup stops counting old children immediately
            var toDestroy = new List<GameObject>();
            foreach (Transform c in _holderGridContainer) toDestroy.Add(c.gameObject);
            foreach (var go in toDestroy) { go.transform.SetParent(null); Destroy(go); }

            var glg = _holderGridContainer.GetComponent<GridLayoutGroup>();
            glg.constraintCount = _holderCols;
            float cellW = Mathf.Min(300f / Mathf.Max(_holderCols, 1), 36f);
            glg.cellSize = new Vector2(cellW, cellW);
            var le = _holderGridContainer.GetComponent<LayoutElement>();
            le.preferredHeight = cellW * _holderRows + (_holderRows - 1) * glg.spacing.y + 4;

            for (int r = 0; r < _holderRows; r++)
                for (int c = 0; c < _holderCols; c++)
                {
                    int cc = c, rr = r;
                    int ci = _holderColors[c, r];
                    int gi = _holderGimmicks[c, r];
                    var btn = DefaultControls.CreateButton(_uiRes);
                    btn.transform.SetParent(_holderGridContainer, false);
                    btn.GetComponent<Image>().color = (ci >= 0 && ci < PALETTE.Length) ? PALETTE[ci] : new Color(0.22f, 0.22f, 0.26f);
                    var t = btn.GetComponentInChildren<Text>();
                    // 기믹 + Chain 그룹 표시
                    string gimmickMark = (gi > 0 && gi < HOLDER_GIMMICK_NAMES.Length) ? HOLDER_GIMMICK_NAMES[gi].Substring(0, System.Math.Min(2, HOLDER_GIMMICK_NAMES[gi].Length)) : "";
                    int chainGrp = _holderChainGroups[c, r];
                    string chainMark = chainGrp > 0 ? $"C{chainGrp}" : "";
                    string mark = gimmickMark + (chainMark.Length > 0 ? " " + chainMark : "");
                    t.text = ci >= 0 ? $"{_holderMags[c, r]}{(mark.Length > 0 ? "\n" + mark : "")}" : ".";
                    t.font = _font; t.fontSize = 8; t.color = Color.white;
                    btn.GetComponent<Button>().onClick.AddListener(() => {
                        _holderColors[cc, rr] = _paintColor;
                        _holderMags[cc, rr] = _paintColor >= 0 ? _defaultMag : 0;
                        _holderGimmicks[cc, rr] = _paintColor >= 0 ? _paintHolderGimmick : 0;
                        _holderChainGroups[cc, rr] = _paintColor >= 0 ? _paintChainGroup : -1;
                        _holderFrozenHP[cc, rr] = _paintFrozenHP;
                        // Spawner 기믹일 때만 spawnerHP/Mag 저장, 아니면 0
                        bool isSpawnerGimmick = _paintHolderGimmick > 0 && _paintHolderGimmick < HOLDER_GIMMICK_NAMES.Length
                            && (HOLDER_GIMMICK_NAMES[_paintHolderGimmick] == "Spawner_T"
                             || HOLDER_GIMMICK_NAMES[_paintHolderGimmick] == "Spawner_O");
                        _holderSpawnerHP[cc, rr] = isSpawnerGimmick ? _paintSpawnerHP : 0;
                        _holderSpawnerMag[cc, rr] = isSpawnerGimmick ? _paintSpawnerMag : 20;
                        // Lock_Key pairId
                        bool isLockKeyHolder = _paintHolderGimmick > 0 && _paintHolderGimmick < HOLDER_GIMMICK_NAMES.Length
                            && HOLDER_GIMMICK_NAMES[_paintHolderGimmick] == "Lock_Key";
                        _holderLockPairIds[cc, rr] = isLockKeyHolder ? _paintLockPairId : -1;
                        RebuildHolderUI(); RefreshInfo();
                    });
                }

            // Force layout recalculation so container expands immediately
            LayoutRebuilder.ForceRebuildLayoutImmediate(_holderGridContainer.GetComponent<RectTransform>());
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
            t.text = label; t.font = _font; t.fontSize = 14; t.fontStyle = FontStyle.Bold; t.color = Color.white;
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
                    if (ci >= 0 && gi > 0 && gi < GIMMICK_MARKS.Length && !string.IsNullOrEmpty(GIMMICK_MARKS[gi]))
                    {
                        var labelGO = new GameObject("GLabel");
                        labelGO.transform.SetParent(_previewRoot, false);
                        labelGO.transform.position = new Vector3(wx, 1.2f, wz);
                        labelGO.transform.eulerAngles = new Vector3(90f, 0f, 0f);
                        var tm = labelGO.AddComponent<TextMesh>();
                        tm.text = GIMMICK_MARKS[gi];
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
                if (gn == "Ice") return GIMMICK_ICE_COLOR;
                if (gn == "Hidden") return GIMMICK_HIDDEN_COLOR;
                if (gn == "Pinata" || gn == "Pinata_Box") return GIMMICK_PINATA_COLOR;
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

            // Remove old label
            if (_previewLabels[c, r] != null)
            {
                Destroy(_previewLabels[c, r].gameObject);
                _previewLabels[c, r] = null;
            }

            // Add new label if needed
            if (ci >= 0 && gi > 0 && gi < GIMMICK_MARKS.Length && !string.IsNullOrEmpty(GIMMICK_MARKS[gi]))
            {
                var labelGO = new GameObject("GLabel");
                labelGO.transform.SetParent(_previewRoot, false);
                labelGO.transform.position = new Vector3(wx, 1.2f, wz);
                labelGO.transform.eulerAngles = new Vector3(90f, 0f, 0f);
                var tm = labelGO.AddComponent<TextMesh>();
                tm.text = GIMMICK_MARKS[gi];
                tm.fontSize = 32;
                tm.characterSize = BalloonScale * 0.35f;
                tm.alignment = TextAlignment.Center;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.color = Color.white;
                _previewLabels[c, r] = tm;
            }
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
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.useWorldSpace = true;
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
            Sprite hSprite = ts != null ? ts.GetH() : null;
            Sprite vSprite = ts != null ? ts.GetV() : null;

            // 허용량별 면 수 계산 (Piñata 비앵커 셀 제외)
            int totalDarts = 0;
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                {
                    if (_balloonColors[c, r] < 0) continue;
                    int gi = _balloonGimmicks[c, r];
                    bool isPinata = gi > 0 && gi < GIMMICK_NAMES.Length
                        && (FIELD_GIMMICK_NAMES[gi] == "Pinata" || FIELD_GIMMICK_NAMES[gi] == "Pinata_Box");
                    if (isPinata && _balloonPinataW[c, r] == 0) continue;
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
                PlaceConveyorSpriteTileStretched(hSprite, new Vector3(hCenter, rh, bottom), hLength, cornerSize);
                PlaceConveyorSpriteTileStretched(hSprite, new Vector3(hCenter, rh, top), hLength, cornerSize);
                PlaceConveyorSpriteTileStretched(vSprite, new Vector3(left, rh, vCenter), cornerSize, vLength);
                PlaceConveyorSpriteTileStretched(vSprite, new Vector3(right, rh, vCenter), cornerSize, vLength);
            }
            else if (sides == 3)
            {
                PlaceConveyorSpriteTile(capL, new Vector3(left, rh, bottom), cornerSize);
                PlaceConveyorSpriteTileStretched(hSprite, new Vector3(hCenter, rh, bottom), hLength, cornerSize);
                PlaceConveyorSpriteTile(spBR, new Vector3(right, rh, bottom), cornerSize);
                PlaceConveyorSpriteTileStretched(vSprite, new Vector3(right, rh, vCenter), cornerSize, vLength);
                PlaceConveyorSpriteTile(spTR, new Vector3(right, rh, top), cornerSize);
                PlaceConveyorSpriteTileStretched(hSprite, new Vector3(hCenter, rh, top), hLength, cornerSize);
                PlaceConveyorSpriteTile(capL, new Vector3(left, rh, top), cornerSize);
            }
            else if (sides == 2)
            {
                PlaceConveyorSpriteTile(capL, new Vector3(left, rh, bottom), cornerSize);
                PlaceConveyorSpriteTileStretched(hSprite, new Vector3(hCenter, rh, bottom), hLength, cornerSize);
                PlaceConveyorSpriteTile(spBR, new Vector3(right, rh, bottom), cornerSize);
                PlaceConveyorSpriteTileStretched(vSprite, new Vector3(right, rh, vCenter), cornerSize, vLength);
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
                    PlaceCaveOverlayTile(caveL, new Vector3(left, rh, bottom), cornerSize);
                    PlaceCaveOverlayTile(caveL, new Vector3(left, rh, top), cornerSize);
                }
                else if (sides == 2)
                {
                    PlaceCaveOverlayTile(caveL, new Vector3(left, rh, bottom), cornerSize);
                    PlaceCaveOverlayTile(caveT, new Vector3(right, rh, top), cornerSize);
                }
                else
                {
                    PlaceCaveOverlayTile(caveL, new Vector3(left, rh, bottom), cornerSize);
                    PlaceCaveOverlayTile(caveR, new Vector3(right, rh, bottom), cornerSize);
                }
            }

            // Paint 모드일 때 가이드 그리드 표시
            if (_conveyorPaintMode && _pathGrid != null)
            {
                int pw = _pathGrid.GetLength(0);
                int ph = _pathGrid.GetLength(1);
                for (int gx = 0; gx < pw; gx++)
                    for (int gy = 0; gy < ph; gy++)
                    {
                        Vector3 wpos = PathGridToWorld(gx, gy);
                        var outline = GameObject.CreatePrimitive(PrimitiveType.Quad);
                        Destroy(outline.GetComponent<Collider>());
                        outline.transform.SetParent(_conveyorPreviewRoot, false);
                        outline.transform.localScale = new Vector3(spacing * 0.98f, spacing * 0.98f, 1f);
                        outline.transform.position = new Vector3(wpos.x, -0.08f, wpos.z);
                        outline.transform.eulerAngles = new Vector3(90f, 0f, 0f);
                        var mat = MakeLitMaterial(FindLitShader(),
                            _pathGrid[gx, gy] ? new Color(0.3f, 0.3f, 0.6f, 0.5f) : new Color(0.15f, 0.15f, 0.2f, 0.3f));
                        outline.GetComponent<MeshRenderer>().material = mat;
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

            // Corners: exactly 2 neighbors at right angle
            if (hasRight && hasUp    && !hasLeft && !hasDown) return _railTileSet.tileBL;
            if (hasLeft  && hasUp    && !hasRight && !hasDown) return _railTileSet.tileBR;
            if (hasRight && hasDown  && !hasLeft && !hasUp)   return _railTileSet.tileTL;
            if (hasLeft  && hasDown  && !hasRight && !hasUp)  return _railTileSet.tileTR;

            // Straight segments
            if (hasLeft && hasRight) return _railTileSet.GetH();
            if (hasUp   && hasDown)  return _railTileSet.GetV();

            // Single-neighbor fallback
            if (hasLeft || hasRight) return _railTileSet.GetH();
            if (hasUp   || hasDown)  return _railTileSet.GetV();

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

        private void PlaceCaveOverlayTile(Sprite sprite, Vector3 position, float tileSize)
        {
            if (sprite == null) return;

            var go = new GameObject($"CaveTile_{_conveyorPreviewRoot.childCount}");
            go.transform.SetParent(_conveyorPreviewRoot, false);
            go.transform.position = new Vector3(position.x, -0.01f, position.z);
            go.transform.eulerAngles = new Vector3(90f, 0f, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 1; // Arrow(0)보다 위

            float sw = sprite.bounds.size.x;
            float sh = sprite.bounds.size.y;
            if (sw > 0.001f && sh > 0.001f)
                go.transform.localScale = new Vector3(tileSize / sw, tileSize / sh, 1f);
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

        private void RebuildWaypointPreview()
        {
            if (_waypointPreviewRoot) Destroy(_waypointPreviewRoot.gameObject);
            _waypointPreviewRoot = new GameObject("WaypointPreview").transform;

            if (_customWaypoints.Count == 0) return;

            // Draw small spheres at each waypoint
            for (int i = 0; i < _customWaypoints.Count; i++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(s.GetComponent<Collider>());
                s.transform.SetParent(_waypointPreviewRoot, false);
                s.transform.position = _customWaypoints[i] + Vector3.up * 0.3f;
                s.transform.localScale = Vector3.one * 0.2f;
                s.GetComponent<MeshRenderer>().material = _waypointMat;

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

                    var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(arrow.GetComponent<Collider>());
                    arrow.transform.SetParent(_waypointPreviewRoot, false);
                    arrow.transform.position = mid;
                    arrow.transform.localScale = new Vector3(0.06f, 0.06f, 0.25f);
                    if (dir.sqrMagnitude > 0.001f)
                        arrow.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                    arrow.GetComponent<MeshRenderer>().material = arrowMat;

                    var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(head.GetComponent<Collider>());
                    head.transform.SetParent(_waypointPreviewRoot, false);
                    head.transform.position = mid + dir * 0.15f;
                    head.transform.localScale = new Vector3(0.18f, 0.06f, 0.12f);
                    if (dir.sqrMagnitude > 0.001f)
                        head.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                    head.GetComponent<MeshRenderer>().material = arrowMat;
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

        private void HandlePaintInput()
        {
            if (_cam == null) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            if (_conveyorPaintMode)
            {
                if (!mouse.leftButton.wasPressedThisFrame) { _conveyorClickConsumed = false; return; }
                if (_conveyorClickConsumed) return;
            }
            else
            {
                if (!mouse.leftButton.isPressed) return;
            }


            Vector3 hit;
            if (!RaycastToGround(mouse, out hit)) return;

            float spacing = CellSpacing;

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
                    RefreshInfo();
                }
            }
            else
            {
                // Balloon paint mode
                int col = Mathf.RoundToInt((hit.x - _boardCenter.x) / spacing + (_gridCols - 1) * 0.5f);
                int row = Mathf.RoundToInt((hit.z - _boardCenter.y) / spacing + (_gridRows - 1) * 0.5f);

                if (col >= 0 && col < _gridCols && row >= 0 && row < _gridRows)
                {
                    if (_eraseNeighborMode && mouse.leftButton.wasPressedThisFrame)
                    {
                        EraseNeighborSameColor(col, row);
                        _eraseNeighborMode = false;
                        OnBalloonGridChanged();
                    }
                    else if (_fillNeighborMode && mouse.leftButton.wasPressedThisFrame)
                    {
                        FillNeighborEmpty(col, row, _paintColor);
                        _fillNeighborMode = false;
                        OnBalloonGridChanged();
                    }
                    else if (_floodFillMode && mouse.leftButton.wasPressedThisFrame)
                    {
                        FloodFill(col, row, _paintColor);
                        OnBalloonGridChanged();
                    }
                    else if (!_floodFillMode && !_eraseNeighborMode && !_fillNeighborMode)
                    {
                        bool isPinataGimmick = _paintGimmick > 0 && _paintGimmick < GIMMICK_NAMES.Length
                            && (FIELD_GIMMICK_NAMES[_paintGimmick] == "Pinata" || FIELD_GIMMICK_NAMES[_paintGimmick] == "Pinata_Box");

                        if (isPinataGimmick && _paintColor >= 0)
                        {
                            int pw = _paintPinataW, ph = _paintPinataH;
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
                                    UpdatePreviewCell(cx, cy);
                                }
                            // 앵커 셀에만 사이즈 저장
                            _balloonPinataW[col, row] = pw;
                            _balloonPinataH[col, row] = ph;
                            UpdatePreviewCell(col, row);
                            RefreshInfo();
                        }
                        else
                        {
                            _balloonColors[col, row] = _paintColor;
                            int gimmickToSet = _paintColor >= 0 ? _paintGimmick : 0;
                            _balloonGimmicks[col, row] = gimmickToSet;
                            _balloonGimmickHP[col, row] = _paintPinataHP;
                            _balloonPinataW[col, row] = 1;
                            _balloonPinataH[col, row] = 1;
                            // Lock_Key pairId
                            bool isLockKeyGimmick = gimmickToSet > 0 && gimmickToSet < FIELD_GIMMICK_NAMES.Length
                                && FIELD_GIMMICK_NAMES[gimmickToSet] == "Lock_Key";
                            _balloonLockPairIds[col, row] = isLockKeyGimmick ? _paintLockPairId : -1;
                            UpdatePreviewCell(col, row);
                            RefreshInfo();
                        }
                    }
                }
            }
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
            var _sortedSelColors = new List<int>(_selectedColors);
            _sortedSelColors.Sort();
            for (int i = 0; i < Mathf.Min(9, _sortedSelColors.Count); i++)
                if (kb[numKeys[i]].wasPressedThisFrame) SetPaintColor(_sortedSelColors[i]);
            if (kb[Key.Digit0].wasPressedThisFrame) SetPaintColor(-1);
            if (kb[Key.Backquote].wasPressedThisFrame) SetPaintColor(-1);
            if (kb[Key.Tab].wasPressedThisFrame) ToggleConveyorMode();
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
            RefreshInfo();
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
                            if (ci >= 0 && gi > 0 && gi < GIMMICK_NAMES.Length)
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
            if (g == "Pinata" || g == "Pinata_Box")
            {
                // 실제 HP 사용 (기본값 2)
                int hp = (col >= 0 && row >= 0) ? _balloonGimmickHP[col, row] : 2;
                return hp > 0 ? hp : 2;
            }
            if (g == "Wall" || g == "Pin" || g == "Ice") return 0;
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

            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                    if (_balloonColors[c, r] >= 0)
                    {
                        // Piñata 비앵커 셀 스킵 (실제 풍선 아님)
                        int gi = _balloonGimmicks[c, r];
                        bool isPinataCell = gi > 0 && gi < GIMMICK_NAMES.Length
                            && (FIELD_GIMMICK_NAMES[gi] == "Pinata" || FIELD_GIMMICK_NAMES[gi] == "Pinata_Box");
                        if (isPinataCell && _balloonPinataW[c, r] == 0) continue;

                        int ci = _balloonColors[c, r];
                        int life = GetGimmickLife(gi, c, r);
                        balloonCountPerColor[ci] = (balloonCountPerColor.ContainsKey(ci) ? balloonCountPerColor[ci] : 0) + 1;
                        dartsNeededPerColor[ci] = (dartsNeededPerColor.ContainsKey(ci) ? dartsNeededPerColor[ci] : 0) + life;

                        if (gi > 0 && gi < FIELD_GIMMICK_NAMES.Length)
                        {
                            string gn = FIELD_GIMMICK_NAMES[gi];
                            gimmickCounts[gn] = (gimmickCounts.ContainsKey(gn) ? gimmickCounts[gn] : 0) + 1;
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
                        dartsProvidedPerColor[ci] = (dartsProvidedPerColor.ContainsKey(ci) ? dartsProvidedPerColor[ci] : 0) + _holderMags[c, r];
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
                string line1 = $"Level {_levelId}  |  {_gridCols}x{_gridRows}  |  {_numColors}C  |  {_difficulty}";
                string line2 = $"Balloons: {totalBalloons}  |  Holders: {totalHolders}  |  Darts: {totalDartsProvided} (need {totalDartsNeeded})";

                string gimmickStr = "";
                foreach (var kvp in gimmickCounts)
                    gimmickStr += $"  {kvp.Key}:{kvp.Value}";
                if (!string.IsNullOrEmpty(gimmickStr))
                    line2 += "\nGimmicks:" + gimmickStr;

                // Holder summary
                string holderStr = "\nHolders: ";
                foreach (var kvp in dartsProvidedPerColor)
                {
                    string label = kvp.Key < COLOR_LABELS.Length ? COLOR_LABELS[kvp.Key] : kvp.Key.ToString();
                    holderStr += $"[{label}:{kvp.Value}]  ";
                }

                _txtCenterInfo.text = line1 + "\n" + line2 + holderStr;
            }

            // === Center Bottom: Per-color balance validation ===
            if (_txtBalanceInfo)
            {
                string bal = "BALANCE CHECK (per color):\n";
                bool hasIssue = false;

                // Collect all colors
                var allColors = new HashSet<int>();
                foreach (var k in dartsNeededPerColor.Keys) allColors.Add(k);
                foreach (var k in dartsProvidedPerColor.Keys) allColors.Add(k);

                foreach (int ci in allColors)
                {
                    string label = ci < COLOR_LABELS.Length ? COLOR_LABELS[ci] : ci.ToString();
                    int bCount = balloonCountPerColor.ContainsKey(ci) ? balloonCountPerColor[ci] : 0;
                    int need = dartsNeededPerColor.ContainsKey(ci) ? dartsNeededPerColor[ci] : 0;
                    int have = dartsProvidedPerColor.ContainsKey(ci) ? dartsProvidedPerColor[ci] : 0;
                    string status = (have == need) ? "OK" : (have > need ? $"+{have - need}" : $"-{need - have} !!!");
                    if (have != need) hasIssue = true;
                    bal += $"  {label}: {bCount}B (need {need}D) / have {have}D  [{status}]\n";
                }

                if (!hasIssue && allColors.Count > 0)
                    bal += "  All colors balanced!";
                else if (allColors.Count == 0)
                    bal = "No balloons placed.";

                _txtBalanceInfo.text = bal;
                _txtBalanceInfo.color = hasIssue ? new Color(1f, 0.7f, 0.4f) : new Color(0.6f, 0.9f, 0.6f);
            }

            if (_txtStatus) _txtStatus.text = $"Balloons: {totalBalloons}  Darts: {totalDartsProvided}/{totalDartsNeeded}  WP: {_customWaypoints.Count}";
        }

        private void SetStatus(string msg) { if (_txtStatus) _txtStatus.text = msg; }

        private void AutoBalanceHolders()
        {
            var needed = new Dictionary<int, int>();
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                    if (_balloonColors[c, r] >= 0)
                    {
                        int gi = _balloonGimmicks[c, r];
                        bool isPinata = gi > 0 && gi < GIMMICK_NAMES.Length
                            && (FIELD_GIMMICK_NAMES[gi] == "Pinata" || FIELD_GIMMICK_NAMES[gi] == "Pinata_Box");
                        if (isPinata && _balloonPinataW[c, r] == 0) continue;
                        int ci = _balloonColors[c, r];
                        int life = GetGimmickLife(gi, c, r);
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
        //  큐 생성기 (Queue Generator) — BalloonFlow_큐생성기_명세 기반
        // ════════════════════════════════════════════════════════════════

        #region Queue Generator

        // 난이도별 탄창 풀 (70% 주력 / 30% 나머지)
        private static readonly int[][] PRIMARY_POOL = {
            new[] { 10, 20 },         // Easy (Tutorial, Rest)
            new[] { 20, 30 },         // Normal
            new[] { 20, 30, 40 },     // Hard
            new[] { 20, 30, 40, 50 }  // SuperHard
        };
        private static readonly int[][] SECONDARY_POOL = {
            new[] { 10, 30, 40, 50 }, // Easy
            new[] { 10, 40, 50 },     // Normal
            new[] { 10, 50 },         // Hard
            new[] { 10 }              // SuperHard
        };
        // 순서 배치 파라미터: 앞 50%에 depth 0 비율 (min~max)
        private static readonly float[][] DEPTH0_FRONT_RATIO = {
            new[] { 0.70f, 0.90f }, // Easy
            new[] { 0.40f, 0.65f }, // Normal
            new[] { 0.25f, 0.45f }, // Hard
            new[] { 0.10f, 0.30f }  // SuperHard
        };
        private static readonly int[] SAME_COLOR_MAX = { 1, 2, 3, 4 };

        private void GenerateQueue()
        {
            // ── 1. 필드 분석 (Piñata 비앵커 셀 제외) ──
            var colorDarts = new Dictionary<int, int>();
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                    if (_balloonColors[c, r] >= 0)
                    {
                        int gi = _balloonGimmicks[c, r];
                        bool isPinata = gi > 0 && gi < GIMMICK_NAMES.Length
                            && (FIELD_GIMMICK_NAMES[gi] == "Pinata" || FIELD_GIMMICK_NAMES[gi] == "Pinata_Box");
                        if (isPinata && _balloonPinataW[c, r] == 0) continue;
                        int ci = _balloonColors[c, r];
                        int life = GetGimmickLife(gi, c, r);
                        colorDarts[ci] = (colorDarts.ContainsKey(ci) ? colorDarts[ci] : 0) + life;
                    }

            if (colorDarts.Count == 0)
            {
                SetStatus("Generate Queue: No balloons on field.");
                return;
            }

            // 10배수 올림
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

            // color_depth 계산 (4면 스캔)
            var colorDepth = CalcColorDepth(colorDartsRounded);

            // ── 2. 난이도 인덱스 ──
            int diffIdx = GetDifficultyIndex(_difficulty);

            // ── 3. STEP A: 보관함 분해 ──
            var allMagazines = new List<(int color, int mag)>();
            foreach (var kvp in colorDartsRounded)
            {
                var mags = DecomposeMagazines(kvp.Value, diffIdx, dartCapMax);
                foreach (int m in mags)
                    allMagazines.Add((kvp.Key, m));
            }

            // ── 4. STEP B: 그리드 배치 (depth 기반 순서) ──
            allMagazines = LayoutByDepth(allMagazines, colorDepth, diffIdx);

            // ── 5. 홀더 그리드에 반영 ──
            int queueCols = _holderCols;
            int neededRows = Mathf.CeilToInt((float)allMagazines.Count / queueCols);
            if (neededRows != _holderRows)
            {
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

            // ── 6. 난이도 점수 계산 ──
            float score = CalcDifficultyScore(allMagazines, colorDepth, railCapacity);
            string grade = score < 20f ? "Easy" : score < 50f ? "Normal" : score < 80f ? "Hard" : "SuperHard";

            if (_queueGenScoreLabel != null)
                _queueGenScoreLabel.text = $"Score: {score:F0}%  [{grade}]  |  {allMagazines.Count} holders  |  {totalDarts} darts";

            // ── 7. 무결성 검증 ──
            int sumCheck = 0;
            foreach (var m in allMagazines) sumCheck += m.mag;
            bool valid = sumCheck == totalDarts;
            SetStatus(valid
                ? $"Generate Queue OK — {grade} ({score:F0}%)"
                : $"Generate Queue WARN: dart sum {sumCheck} != {totalDarts}");
        }

        private List<int> DecomposeMagazines(int colorDarts, int diffIdx, int dartCapMax)
        {
            var primary = FilterPool(PRIMARY_POOL[diffIdx], dartCapMax);
            var secondary = FilterPool(SECONDARY_POOL[diffIdx], dartCapMax);
            if (primary.Length == 0) primary = secondary;
            if (secondary.Length == 0) secondary = primary;
            if (primary.Length == 0) return new List<int> { colorDarts }; // fallback

            var result = new List<int>();
            int remaining = colorDarts;

            int safety = 0;
            while (remaining > 0 && safety++ < 200)
            {
                int[] pool = Random.value < 0.7f ? primary : secondary;
                var candidates = FilterPool(pool, Mathf.Min(remaining, dartCapMax));
                if (candidates.Length == 0)
                    candidates = FilterPool(Concat(primary, secondary), Mathf.Min(remaining, dartCapMax));
                if (candidates.Length == 0)
                {
                    result.Add(remaining); // 남은 전부
                    remaining = 0;
                    break;
                }

                int mag = candidates[Random.Range(0, candidates.Length)];
                if (remaining - mag >= 0 && (remaining - mag == 0 || remaining - mag >= 5))
                {
                    result.Add(mag);
                    remaining -= mag;
                }
            }

            return result;
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

            // 같은 색 연속 max 제한
            int maxConsec = SAME_COLOR_MAX[diffIdx];
            EnforceColorConsecutiveLimit(sorted, maxConsec);

            return sorted;
        }

        private void EnforceColorConsecutiveLimit(List<(int color, int mag)> list, int maxConsec)
        {
            for (int i = maxConsec; i < list.Count; i++)
            {
                bool allSame = true;
                for (int j = 1; j <= maxConsec; j++)
                {
                    if (list[i].color != list[i - j].color) { allSame = false; break; }
                }
                if (!allSame) continue;

                // 뒤에서 다른 색 찾아서 swap
                for (int k = i + 1; k < list.Count; k++)
                {
                    if (list[k].color != list[i].color)
                    {
                        var temp = list[i];
                        list[i] = list[k];
                        list[k] = temp;
                        break;
                    }
                }
            }
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

        private float CalcDifficultyScore(
            List<(int color, int mag)> magazines,
            Dictionary<int, int> colorDepth,
            int railCapacity)
        {
            if (railCapacity <= 0) return 0f;

            // max_possible = (depth1 + depth2 다트 합) / rail_capacity
            int innerDarts = 0;
            foreach (var m in magazines)
            {
                int d = colorDepth.ContainsKey(m.color) ? colorDepth[m.color] : 0;
                if (d > 0) innerDarts += m.mag;
            }
            float maxPossible = (float)innerDarts / railCapacity;
            if (maxPossible <= 0f) return 0f; // 전부 외곽 → Easy

            // absolute = depth>0인 보관함의 mag 합 (순서 기반, 간략화)
            var consumed = new Dictionary<int, float>();
            float absolute = 0f;
            foreach (var m in magazines)
            {
                int d = colorDepth.ContainsKey(m.color) ? colorDepth[m.color] : 0;
                if (d == 0)
                {
                    // depth 0: 즉시 발사 가능 → 0
                }
                else
                {
                    absolute += (float)m.mag / railCapacity;
                }
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
            if (railCapacity <= 50) return 30;
            if (railCapacity <= 100) return 40;
            return 50;
        }

        private static int[] FilterPool(int[] pool, int max)
        {
            var result = new List<int>();
            for (int i = 0; i < pool.Length; i++)
                if (pool[i] <= max) result.Add(pool[i]);
            return result.ToArray();
        }

        private static int[] Concat(int[] a, int[] b)
        {
            var result = new int[a.Length + b.Length];
            a.CopyTo(result, 0);
            b.CopyTo(result, a.Length);
            return result;
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
            SaveToDatabase();

            string json = JsonUtility.ToJson(config, false);
            EditorPrefs.SetString("BalloonFlow_TestLevel", json);
            EditorPrefs.SetBool("BalloonFlow_UseTestLevel", true);
            EditorPrefs.SetInt("BalloonFlow_LastEditedLevel", _levelId);
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

        private void LoadLevelById(int levelId)
        {
            var db = LoadLevelDatabase();
            if (db == null || db.levels == null) { SetStatus("No LevelDatabase"); return; }
            for (int i = 0; i < db.levels.Length; i++)
                if (db.levels[i].levelId == levelId) { LoadLevelFromDB(i); return; }
            SetStatus($"Level {levelId} not found");
        }

        /// <summary>풍선 좌표에서 실제 spacing 감지. 같은 행/열의 인접 풍선 간 최소 거리.</summary>
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

            // spacing 자동 감지: 실제 풍선 좌표에서 최소 간격 계산
            float spacing = DetectSpacingFromBalloons(config.balloons, _gridCols, _gridRows);
            if (spacing <= 0f) spacing = CellSpacing; // 풍선 없으면 현재 공식 사용
            _balloonColors = new int[_gridCols, _gridRows];
            _balloonGimmicks = new int[_gridCols, _gridRows];
            _balloonGimmickHP = new int[_gridCols, _gridRows];
            _balloonPinataW = new int[_gridCols, _gridRows];
            _balloonPinataH = new int[_gridCols, _gridRows];
            _balloonLockPairIds = new int[_gridCols, _gridRows];
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                {
                    _balloonColors[c, r] = -1;
                    _balloonGimmicks[c, r] = 0;
                    _balloonGimmickHP[c, r] = 2;
                    _balloonPinataW[c, r] = 1;
                    _balloonPinataH[c, r] = 1;
                    _balloonLockPairIds[c, r] = -1;
                }

            if (config.balloons != null)
                foreach (var b in config.balloons)
                {
                    int col = Mathf.RoundToInt((b.gridPosition.x - _boardCenter.x) / spacing + (_gridCols - 1) * 0.5f);
                    int row = Mathf.RoundToInt((b.gridPosition.y - _boardCenter.y) / spacing + (_gridRows - 1) * 0.5f);
                    if (col >= 0 && col < _gridCols && row >= 0 && row < _gridRows)
                    {
                        _balloonColors[col, row] = b.color;
                        int gi = 0;
                        if (!string.IsNullOrEmpty(b.gimmickType))
                            for (int g = 0; g < FIELD_GIMMICK_NAMES.Length; g++)
                                if (FIELD_GIMMICK_NAMES[g] == b.gimmickType) { gi = g; break; }
                        _balloonGimmicks[col, row] = gi;
                        _balloonGimmickHP[col, row] = b.hp > 0 ? b.hp : 2;
                        _balloonLockPairIds[col, row] = b.lockPairId;

                        // Piñata 사이즈 복원 + 비앵커 셀 채우기
                        int bpw = b.sizeW > 0 ? b.sizeW : 1;
                        int bph = b.sizeH > 0 ? b.sizeH : 1;
                        _balloonPinataW[col, row] = bpw;
                        _balloonPinataH[col, row] = bph;
                        if (bpw > 1 || bph > 1)
                        {
                            // 비앵커 셀에 같은 색+기믹, sizeW=0 (비앵커 표시)
                            for (int dx = 0; dx < bpw; dx++)
                                for (int dy = 0; dy < bph; dy++)
                                {
                                    if (dx == 0 && dy == 0) continue; // 앵커 스킵
                                    int cx = col + dx, cy = row + dy;
                                    if (cx >= 0 && cx < _gridCols && cy >= 0 && cy < _gridRows)
                                    {
                                        _balloonColors[cx, cy] = b.color;
                                        _balloonGimmicks[cx, cy] = gi;
                                        _balloonGimmickHP[cx, cy] = b.hp > 0 ? b.hp : 2;
                                        _balloonPinataW[cx, cy] = 0; // 비앵커 표시
                                        _balloonPinataH[cx, cy] = 0;
                                    }
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
                _holderFrozenHP = new int[_holderCols, _holderRows];
                _holderSpawnerHP = new int[_holderCols, _holderRows];
                _holderSpawnerMag = new int[_holderCols, _holderRows];
                _holderLockPairIds = new int[_holderCols, _holderRows];
                for (int c = 0; c < _holderCols; c++)
                    for (int r = 0; r < _holderRows; r++) { _holderColors[c, r] = -1; _holderMags[c, r] = 0; _holderGimmicks[c, r] = 0; _holderFrozenHP[c, r] = 3; _holderSpawnerHP[c, r] = 0; _holderSpawnerMag[c, r] = 20; _holderLockPairIds[c, r] = -1; }
                foreach (var h in config.holders)
                {
                    int hc = Mathf.RoundToInt(h.position.x), hr = Mathf.RoundToInt(h.position.y);
                    if (hc >= 0 && hc < _holderCols && hr >= 0 && hr < _holderRows)
                    {
                        _holderColors[hc, hr] = h.color;
                        _holderMags[hc, hr] = h.magazineCount;
                        // 보관함 기믹 복원
                        int hgi = System.Array.IndexOf(HOLDER_GIMMICK_NAMES, h.queueGimmick ?? "");
                        _holderGimmicks[hc, hr] = hgi > 0 ? hgi : 0;
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
            _selectedColors.Clear();
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
            _selectedColors.Clear();
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
            if (_targetDB != null) return _targetDB;
            _targetDB = AssetDatabase.LoadAssetAtPath<LevelDatabase>("Assets/Resources/LevelDatabase.asset");
            return _targetDB;
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        //  EXPORT
        // ═══════════════════════════════════════════════════════════════

        #region Export

        private void SaveToDatabase()
        {
            if (!_levelLoaded)
            {
                SetStatus("ERROR: No level loaded. Load or create a level first.");
                return;
            }
            if (_targetDB == null)
            {
                string path = "Assets/Resources/LevelDatabase.asset";
                _targetDB = AssetDatabase.LoadAssetAtPath<LevelDatabase>(path);
                if (_targetDB == null)
                { _targetDB = ScriptableObject.CreateInstance<LevelDatabase>(); AssetDatabase.CreateAsset(_targetDB, path); }
            }

            // 자동 백업: Save 전에 기존 DB를 JSON 파일로 백업
            BackupDatabase();

            var config = BuildLevelConfig();
            var levels = _targetDB.levels != null ? new List<LevelConfig>(_targetDB.levels) : new List<LevelConfig>();
            int idx = levels.FindIndex(l => l.levelId == config.levelId);
            if (idx >= 0) levels[idx] = config; else levels.Add(config);
            levels.Sort((a, b) => a.levelId.CompareTo(b.levelId));
            _targetDB.levels = levels.ToArray();
            EditorUtility.SetDirty(_targetDB);
            AssetDatabase.SaveAssets();
            SetStatus($"Saved Level {config.levelId} (backup created)");
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

        private void ExportJson()
        {
            var config = BuildLevelConfig();
            string json = JsonUtility.ToJson(config, true);
            string path = EditorUtility.SaveFilePanel("Export JSON", "Assets", $"level_{config.levelId}", "json");
            if (!string.IsNullOrEmpty(path))
            { System.IO.File.WriteAllText(path, json); SetStatus($"Exported: {System.IO.Path.GetFileName(path)}"); }
        }

        private LevelConfig BuildLevelConfig()
        {
            float spacing = CellSpacing;
            var config = new LevelConfig
            {
                levelId = _levelId, packageId = 1, positionInPackage = _levelId,
                numColors = _numColors, difficultyPurpose = _difficulty,
                gimmickTypes = CollectGimmicks(), balloonScale = BalloonScale,
            };

            var balloons = new List<BalloonLayout>();
            int bid = 0;
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                {
                    if (_balloonColors[c, r] < 0) continue;
                    // Piñata 비앵커 셀(sizeW==0)은 스킵 — 앵커 1개만 생성
                    int gi = _balloonGimmicks[c, r];
                    bool isPinataCell = gi > 0 && gi < FIELD_GIMMICK_NAMES.Length
                        && (FIELD_GIMMICK_NAMES[gi] == "Pinata" || FIELD_GIMMICK_NAMES[gi] == "Pinata_Box");
                    if (isPinataCell && _balloonPinataW[c, r] == 0) continue;

                    balloons.Add(new BalloonLayout
                    {
                        balloonId = bid++, color = _balloonColors[c, r],
                        gridPosition = new Vector2(
                            _boardCenter.x + (c - (_gridCols - 1) * 0.5f) * spacing,
                            _boardCenter.y + (r - (_gridRows - 1) * 0.5f) * spacing),
                        gimmickType = gi > 0 && gi < FIELD_GIMMICK_NAMES.Length ? FIELD_GIMMICK_NAMES[gi] : "",
                        sizeW = _balloonPinataW[c, r],
                        sizeH = _balloonPinataH[c, r],
                        hp = _balloonGimmickHP[c, r],
                        lockPairId = _balloonLockPairIds != null && c < _balloonLockPairIds.GetLength(0) && r < _balloonLockPairIds.GetLength(1)
                            ? _balloonLockPairIds[c, r] : -1
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
                    if (_holderColors[c, r] < 0) continue;
                    int hgi = _holderGimmicks[c, r];
                    string hgName = (hgi > 0 && hgi < HOLDER_GIMMICK_NAMES.Length) ? HOLDER_GIMMICK_NAMES[hgi] : "";
                    int chainGrp = _holderChainGroups[c, r];
                    bool isSpawner = hgName == "Spawner_T" || hgName == "Spawner_O";
                    holders.Add(new HolderSetup
                    { holderId = hid++, color = _holderColors[c, r], magazineCount = _holderMags[c, r],
                      position = new Vector2(c, r), queueGimmick = hgName,
                      chainGroupId = chainGrp > 0 ? chainGrp : -1,
                      frozenHP = _holderFrozenHP[c, r],
                      spawnerHP = isSpawner ? _holderSpawnerHP[c, r] : 0,
                      spawnerMag = isSpawner ? (_holderSpawnerMag[c, r] > 0 ? _holderSpawnerMag[c, r] : 20) : 0,
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
            if (_targetDB == null)
            {
                string path = "Assets/Resources/LevelDatabase.asset";
                _targetDB = AssetDatabase.LoadAssetAtPath<LevelDatabase>(path);
                if (_targetDB == null)
                { _targetDB = ScriptableObject.CreateInstance<LevelDatabase>(); AssetDatabase.CreateAsset(_targetDB, path); }
            }
            var config = BuildLevelConfig();
            config.levelId = levelId;
            var levels = _targetDB.levels != null ? new List<LevelConfig>(_targetDB.levels) : new List<LevelConfig>();
            int idx = levels.FindIndex(l => l.levelId == levelId);
            if (idx >= 0) levels[idx] = config; else levels.Add(config);
            levels.Sort((a, b) => a.levelId.CompareTo(b.levelId));
            _targetDB.levels = levels.ToArray();
            EditorUtility.SetDirty(_targetDB);
            AssetDatabase.SaveAssets();
            SetStatus($"Saved Level {levelId} to DB");
            RefreshLevelList();
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

            var newColors = new int[_gridCols, newRows];
            var newGimmicks = new int[_gridCols, newRows];
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < newRows; r++)
                { newColors[c, r] = -1; newGimmicks[c, r] = 0; }

            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                {
                    int dr = r < insertAt ? r : r + 1;
                    newColors[c, dr] = _balloonColors[c, r];
                    newGimmicks[c, dr] = _balloonGimmicks[c, r];
                }

            _gridRows = newRows;
            _balloonColors = newColors;
            _balloonGimmicks = newGimmicks;
            OnBalloonGridChanged();
            SetStatus($"Inserted row {(above ? "above" : "below")} {at}");
        }

        private void InsertCol(int at, bool left)
        {
            int insertAt = left ? at : at + 1;
            insertAt = Mathf.Clamp(insertAt, 0, _gridCols);
            int newCols = _gridCols + 1;

            var newColors = new int[newCols, _gridRows];
            var newGimmicks = new int[newCols, _gridRows];
            for (int c = 0; c < newCols; c++)
                for (int r = 0; r < _gridRows; r++)
                { newColors[c, r] = -1; newGimmicks[c, r] = 0; }

            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                {
                    int dc = c < insertAt ? c : c + 1;
                    newColors[dc, r] = _balloonColors[c, r];
                    newGimmicks[dc, r] = _balloonGimmicks[c, r];
                }

            _gridCols = newCols;
            _balloonColors = newColors;
            _balloonGimmicks = newGimmicks;
            OnBalloonGridChanged();
            SetStatus($"Inserted col {(left ? "left of" : "right of")} {at}");
        }

        // ── Feature 6: Delete Row/Column ──

        private void DeleteRow(int at)
        {
            if (_gridRows <= 1) { SetStatus("Cannot delete: only 1 row left"); return; }
            if (at < 0 || at >= _gridRows) { SetStatus($"Row {at} out of range (0-{_gridRows - 1})"); return; }
            int newRows = _gridRows - 1;

            var newColors = new int[_gridCols, newRows];
            var newGimmicks = new int[_gridCols, newRows];
            var newHP = new int[_gridCols, newRows];
            var newPW = new int[_gridCols, newRows];
            var newPH = new int[_gridCols, newRows];

            for (int c = 0; c < _gridCols; c++)
            {
                int dr = 0;
                for (int r = 0; r < _gridRows; r++)
                {
                    if (r == at) continue;
                    newColors[c, dr] = _balloonColors[c, r];
                    newGimmicks[c, dr] = _balloonGimmicks[c, r];
                    if (_balloonGimmickHP != null && c < _balloonGimmickHP.GetLength(0) && r < _balloonGimmickHP.GetLength(1))
                        newHP[c, dr] = _balloonGimmickHP[c, r];
                    if (_balloonPinataW != null && c < _balloonPinataW.GetLength(0) && r < _balloonPinataW.GetLength(1))
                        newPW[c, dr] = _balloonPinataW[c, r];
                    if (_balloonPinataH != null && c < _balloonPinataH.GetLength(0) && r < _balloonPinataH.GetLength(1))
                        newPH[c, dr] = _balloonPinataH[c, r];
                    dr++;
                }
            }

            _gridRows = newRows;
            _balloonColors = newColors;
            _balloonGimmicks = newGimmicks;
            _balloonGimmickHP = newHP;
            _balloonPinataW = newPW;
            _balloonPinataH = newPH;
            OnBalloonGridChanged();
            SetStatus($"Deleted row {at}");
        }

        private void DeleteCol(int at)
        {
            if (_gridCols <= 1) { SetStatus("Cannot delete: only 1 col left"); return; }
            if (at < 0 || at >= _gridCols) { SetStatus($"Col {at} out of range (0-{_gridCols - 1})"); return; }
            int newCols = _gridCols - 1;

            var newColors = new int[newCols, _gridRows];
            var newGimmicks = new int[newCols, _gridRows];
            var newHP = new int[newCols, _gridRows];
            var newPW = new int[newCols, _gridRows];
            var newPH = new int[newCols, _gridRows];

            for (int r = 0; r < _gridRows; r++)
            {
                int dc = 0;
                for (int c = 0; c < _gridCols; c++)
                {
                    if (c == at) continue;
                    newColors[dc, r] = _balloonColors[c, r];
                    newGimmicks[dc, r] = _balloonGimmicks[c, r];
                    if (_balloonGimmickHP != null && c < _balloonGimmickHP.GetLength(0) && r < _balloonGimmickHP.GetLength(1))
                        newHP[dc, r] = _balloonGimmickHP[c, r];
                    if (_balloonPinataW != null && c < _balloonPinataW.GetLength(0) && r < _balloonPinataW.GetLength(1))
                        newPW[dc, r] = _balloonPinataW[c, r];
                    if (_balloonPinataH != null && c < _balloonPinataH.GetLength(0) && r < _balloonPinataH.GetLength(1))
                        newPH[dc, r] = _balloonPinataH[c, r];
                    dc++;
                }
            }

            _gridCols = newCols;
            _balloonColors = newColors;
            _balloonGimmicks = newGimmicks;
            _balloonGimmickHP = newHP;
            _balloonPinataW = newPW;
            _balloonPinataH = newPH;
            OnBalloonGridChanged();
            SetStatus($"Deleted col {at}");
        }

        // ── Feature 7: Flood Fill ──

        /// <summary>특정 색상의 풍선을 전부 제거.</summary>
        private void EraseColor(int color)
        {
            if (color < 0) { SetStatus("Select a color first"); return; }
            int count = 0;
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                    if (_balloonColors[c, r] == color)
                    {
                        _balloonColors[c, r] = -1;
                        _balloonGimmicks[c, r] = 0;
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
                _balloonColors[c, r] = -1;
                _balloonGimmicks[c, r] = 0;

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
        private void FillNeighborEmpty(int startCol, int startRow, int fillColor)
        {
            if (_balloonColors[startCol, startRow] >= 0) { SetStatus("Click an empty cell"); return; }
            if (fillColor < 0) { SetStatus("Select a color first"); return; }

            var queue = new Queue<Vector2Int>();
            var visited = new HashSet<Vector2Int>();
            queue.Enqueue(new Vector2Int(startCol, startRow));
            visited.Add(new Vector2Int(startCol, startRow));

            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                int c = cell.x, r = cell.y;
                _balloonColors[c, r] = fillColor;
                _balloonGimmicks[c, r] = _paintGimmick;

                Vector2Int[] dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0),
                                      new Vector2Int(0, 1), new Vector2Int(0, -1) };
                foreach (var d in dirs)
                {
                    int nc = c + d.x, nr = r + d.y;
                    var np = new Vector2Int(nc, nr);
                    if (nc >= 0 && nc < _gridCols && nr >= 0 && nr < _gridRows
                        && !visited.Contains(np) && _balloonColors[nc, nr] < 0)
                    {
                        visited.Add(np);
                        queue.Enqueue(np);
                    }
                }
            }
            SetStatus($"Filled {visited.Count} empty neighbor cells (color {fillColor})");
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
                _balloonColors[c, r] = newColor;
                _balloonGimmicks[c, r] = newColor >= 0 ? _paintGimmick : 0;

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
            RefreshInfo();
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
