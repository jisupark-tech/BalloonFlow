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

        private static readonly Color[] PALETTE =
        {
            new Color(0.95f, 0.25f, 0.25f), //  0: Red
            new Color(0.25f, 0.55f, 0.95f), //  1: Blue
            new Color(0.25f, 0.85f, 0.35f), //  2: Green
            new Color(0.95f, 0.85f, 0.15f), //  3: Yellow
            new Color(0.80f, 0.30f, 0.90f), //  4: Purple
            new Color(0.95f, 0.55f, 0.15f), //  5: Orange
            new Color(0.40f, 0.90f, 0.90f), //  6: Cyan
            new Color(0.95f, 0.50f, 0.70f), //  7: Pink
            new Color(0.75f, 0.15f, 0.15f), //  8: Crimson
            new Color(0.15f, 0.20f, 0.65f), //  9: Navy
            new Color(0.55f, 0.95f, 0.25f), // 10: Lime
            new Color(0.95f, 0.75f, 0.05f), // 11: Gold
            new Color(0.55f, 0.20f, 0.80f), // 12: Violet
            new Color(0.95f, 0.65f, 0.00f), // 13: Amber
            new Color(0.15f, 0.70f, 0.65f), // 14: Teal
            new Color(0.95f, 0.35f, 0.50f), // 15: Rose
            new Color(0.95f, 0.45f, 0.35f), // 16: Coral
            new Color(0.30f, 0.15f, 0.70f), // 17: Indigo
            new Color(0.40f, 0.95f, 0.65f), // 18: Mint
            new Color(0.95f, 0.75f, 0.60f), // 19: Peach
            new Color(0.90f, 0.15f, 0.65f), // 20: Magenta
            new Color(0.50f, 0.55f, 0.15f), // 21: Olive
            new Color(0.45f, 0.75f, 0.95f), // 22: Sky
            new Color(0.95f, 0.55f, 0.45f), // 23: Salmon
            new Color(0.50f, 0.10f, 0.15f), // 24: Maroon
            new Color(0.10f, 0.45f, 0.20f), // 25: Forest
            new Color(0.70f, 0.55f, 0.90f), // 26: Lavender
            new Color(0.82f, 0.70f, 0.50f), // 27: Tan
        };

        private static readonly string[] COLOR_LABELS =
            { "R", "B", "G", "Y", "P", "O", "C", "K",
              "Cr", "Nv", "Lm", "Gd", "Vi", "Am", "Tl", "Rs",
              "Co", "In", "Mt", "Pc", "Mg", "Ol", "Sk", "Sl",
              "Mr", "Fr", "Lv", "Tn" };

        private static readonly string[] GIMMICK_NAMES =
            { "(none)", "Hidden", "Chain", "Pinata", "Spawner_T", "Pin", "Lock_Key",
              "Surprise", "Wall", "Spawner_O", "Pinata_Box", "Ice", "Frozen_Dart", "Color_Curtain" };

        // Short gimmick symbols for preview overlay
        private static readonly string[] GIMMICK_MARKS =
            { "", "H", "Ch", "Pi", "ST", "Pn", "LK", "?!", "W", "SO", "PB", "Ic", "FD", "CC" };

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
        private DifficultyPurpose _difficulty = DifficultyPurpose.Normal;
        private int _paintColor = 0;
        private int _paintGimmick = 0;

        private int _gridCols = 5;
        private int _gridRows = 5;
        private float _boardWorldSize = 8f;
        private Vector2 _boardCenter = new Vector2(0f, 2f);

        private int[,] _balloonColors;
        private int[,] _balloonGimmicks;

        private int _holderCols = 5;
        private int _holderRows = 1;
        private int _defaultMag = 3;
        private int[,] _holderColors;
        private int[,] _holderMags;

        private int _railDir;
        private float _railPadding = 1.5f;
        private float _railHeight = 0.5f;
        private int _railSlotCount = 200;
        private bool _smoothCorners;
        private float _cornerRadius = 1f;

        // Grid-based conveyor path (extended grid: +1 padding on each side)
        private bool[,] _pathGrid; // [gridCols+2, gridRows+2]
        private const int PATH_PAD = 1;
        private bool _conveyorPaintMode;

        // Auto-generated waypoints from path grid
        private List<Vector3> _customWaypoints = new List<Vector3>();

        private float CellSpacing => _boardWorldSize / Mathf.Max(_gridCols, _gridRows);
        private float BalloonScale => CellSpacing * 0.9f;

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

        private Text _txtStatus, _txtSpacing, _txtScale;
        private Text _txtColsVal, _txtRowsVal, _txtBoardVal;
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
            SetupCamera();
            BuildUI();
            RebuildPreview();
            RebuildGridLines();
            RebuildConveyorPreview();
            RebuildWaypointPreview();
            RefreshInfo();
            RefreshLevelList();
        }

        private void Update()
        {
            HandlePaintInput();
            HandleKeyboard();
            HandleCameraControl();
        }

        private void OnDestroy()
        {
            if (_colorMats != null)
                foreach (var m in _colorMats)
                    if (m) Destroy(m);
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
            _cam.orthographicSize = _boardWorldSize * 0.65f;
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

            // Keyboard shortcuts for zoom
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb[Key.NumpadPlus].isPressed || kb[Key.Equals].isPressed)
                    _cam.orthographicSize = Mathf.Max(0.5f, _cam.orthographicSize - Time.deltaTime * 3f);
                if (kb[Key.NumpadMinus].isPressed || kb[Key.Minus].isPressed)
                    _cam.orthographicSize = Mathf.Min(40f, _cam.orthographicSize + Time.deltaTime * 3f);
                // Reset view
                if (kb[Key.Home].wasPressedThisFrame)
                {
                    _cam.orthographicSize = _boardWorldSize * 0.65f;
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
            mat.color = color; // sets _Color
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color); // URP uses _BaseColor
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
            float spacing = CellSpacing;
            float halfX = (_gridCols - 1) * spacing * 0.5f;
            float halfZ = (_gridRows - 1) * spacing * 0.5f;
            float l = _boardCenter.x - halfX - _railPadding;
            float r = _boardCenter.x + halfX + _railPadding;
            float b = _boardCenter.y - halfZ - _railPadding;
            float t = _boardCenter.y + halfZ + _railPadding;
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
            BuildPaletteSection(content);
            BuildGridSection(content);
            BuildActionSection(content);
            BuildHolderSection(content);
            BuildRailSection(content);
            BuildConveyorSection(content);
            BuildExportSection(content);
        }

        #endregion

        #region UI Building — Sections

        private void BuildLevelSection(Transform p)
        {
            Lbl(p, "Level Info", 14, FontStyle.Bold);
            var r1 = Row(p); Lbl(r1, "Level ID", w: 90);
            MakeInputField(r1, _levelId.ToString(), s => { if (int.TryParse(s, out int v)) _levelId = v; });
            var r2 = Row(p); Lbl(r2, "Colors", w: 90);
            MakeSlider(r2, 2, 11, _numColors, true, v => { _numColors = (int)v; RebuildPalette(); });
            var r3 = Row(p); Lbl(r3, "Difficulty", w: 90);
            MakeDifficultyDropdown(r3);
            Sep(p);
        }

        private Transform _paletteContainer;

        private void BuildPaletteSection(Transform p)
        {
            Lbl(p, "Brush", 14, FontStyle.Bold);
            _paletteContainer = MakeRT("PaletteRow", p);
            _paletteContainer.gameObject.AddComponent<HorizontalLayoutGroup>().spacing = 2;
            _paletteContainer.gameObject.AddComponent<LayoutElement>().preferredHeight = 36;
            RebuildPalette();

            var gr = Row(p); Lbl(gr, "Gimmick", w: 90);
            MakeSlider(gr, 0, GIMMICK_NAMES.Length - 1, 0, true, v => {
                _paintGimmick = (int)v;
                SetStatus($"Gimmick: {GIMMICK_NAMES[_paintGimmick]}");
            });
            Sep(p);
        }

        private void RebuildPalette()
        {
            if (_paletteContainer == null) return;
            foreach (Transform c in _paletteContainer) Destroy(c.gameObject);

            _palTexts = new Text[_numColors + 1];
            for (int i = 0; i < _numColors; i++)
            {
                int idx = i;
                var btn = MakeColorBtn(_paletteContainer, PALETTE[i], (i + 1).ToString(),
                    () => SetPaintColor(idx));
                _palTexts[i] = btn.GetComponentInChildren<Text>();
            }
            var eraseBtn = MakeColorBtn(_paletteContainer, new Color(0.25f, 0.25f, 0.3f), "X",
                () => SetPaintColor(-1));
            _palTexts[_numColors] = eraseBtn.GetComponentInChildren<Text>();
            UpdatePaletteHighlight();
        }

        private void BuildGridSection(Transform p)
        {
            Lbl(p, "Balloon Grid", 14, FontStyle.Bold);
            var r1 = Row(p); Lbl(r1, "Columns", w: 90);
            MakeSlider(r1, 2, 100, _gridCols, true, v => { _gridCols = (int)v; OnBalloonGridChanged(); });
            _txtColsVal = Lbl(r1, _gridCols.ToString(), w: 36);
            var r2 = Row(p); Lbl(r2, "Rows", w: 90);
            MakeSlider(r2, 2, 100, _gridRows, true, v => { _gridRows = (int)v; OnBalloonGridChanged(); });
            _txtRowsVal = Lbl(r2, _gridRows.ToString(), w: 36);
            var r3 = Row(p); Lbl(r3, "Board Size", w: 90);
            MakeSlider(r3, 4, 20, _boardWorldSize, false, v => {
                _boardWorldSize = v;
                if (_cam) _cam.orthographicSize = v * 0.65f;
                OnBalloonGridChanged();
            });
            _txtBoardVal = Lbl(r3, _boardWorldSize.ToString("F1"), w: 36);

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
            Sep(p);
        }

        private void BuildHolderSection(Transform p)
        {
            Lbl(p, "Holder Grid", 14, FontStyle.Bold);
            var r1 = Row(p); Lbl(r1, "Columns", w: 90);
            MakeSlider(r1, 1, 8, _holderCols, true, v =>
            { _holderCols = (int)v; InitGrid(); RebuildHolderUI(); RefreshInfo(); });
            var r2 = Row(p); Lbl(r2, "Rows", w: 90);
            MakeSlider(r2, 1, 8, _holderRows, true, v =>
            { _holderRows = (int)v; InitGrid(); RebuildHolderUI(); RefreshInfo(); });
            var r3 = Row(p); Lbl(r3, "Default Mag", w: 90);
            MakeSlider(r3, 1, 20, _defaultMag, true, v => _defaultMag = (int)v);

            var gridGO = new GameObject("HolderButtons", typeof(RectTransform),
                typeof(GridLayoutGroup), typeof(LayoutElement));
            gridGO.transform.SetParent(p, false);
            var glg = gridGO.GetComponent<GridLayoutGroup>();
            glg.spacing = new Vector2(2, 2);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _holderGridContainer = gridGO.transform;
            RebuildHolderUI();

            var row = Row(p);
            Btn(row, "Fill", () => { FillHolders(_paintColor); RebuildHolderUI(); RefreshInfo(); });
            Btn(row, "Clear", () => { FillHolders(-1); RebuildHolderUI(); RefreshInfo(); });
            Btn(row, "Random", () => { RandomHolders(); RebuildHolderUI(); RefreshInfo(); });
            var row2 = Row(p);
            Btn(row2, "Set Mag", () => { SetAllMags(); RebuildHolderUI(); RefreshInfo(); });
            Btn(row2, "Auto Balance", () => { AutoBalanceHolders(); RebuildHolderUI(); RefreshInfo(); });
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
            var r3 = Row(p); Lbl(r3, "Slot Count", w: 90);
            MakeSlider(r3, 50, 400, _railSlotCount, true, v => _railSlotCount = (int)v);
            // Auto-calc button: calculate capacity from total darts
            var r3b = Row(p);
            Btn(r3b, "Auto (from darts)", () =>
            {
                int totalDarts = CalcTotalDarts();
                _railSlotCount = RailManager.CalculateCapacity(totalDarts);
                SetStatus($"Rail capacity auto: {_railSlotCount} (darts={totalDarts})");
                RefreshInfo();
            });

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
            var radiusLabel = Lbl(r5, _cornerRadius.ToString("F1"), w: 40);
            MakeSlider(r5, 0.2f, 3f, _cornerRadius, false, v =>
            {
                _cornerRadius = v;
                radiusLabel.text = _cornerRadius.ToString("F1");
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

        private void BuildExportSection(Transform p)
        {
            Lbl(p, "Export / Import", 14, FontStyle.Bold);
            var row = Row(p);
            Btn(row, "Save to DB", SaveToDatabase);
            Btn(row, "Export JSON", ExportJson);
            Btn(row, "Load Level", () => LoadLevelById(_levelId));
            Sep(p);

            // Test Play
            var testRow = Row(p, 44);
            var testBtn = Btn(testRow, "TEST PLAY", TestPlay);
            if (testBtn.GetComponent<Image>())
                testBtn.GetComponent<Image>().color = new Color(0.15f, 0.55f, 0.25f);
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
                    var btn = DefaultControls.CreateButton(_uiRes);
                    btn.transform.SetParent(_holderGridContainer, false);
                    btn.GetComponent<Image>().color = ci >= 0 ? PALETTE[ci] : new Color(0.22f, 0.22f, 0.26f);
                    var t = btn.GetComponentInChildren<Text>();
                    t.text = ci >= 0 ? _holderMags[c, r].ToString() : ".";
                    t.font = _font; t.fontSize = 11; t.color = Color.white;
                    btn.GetComponent<Button>().onClick.AddListener(() => {
                        _holderColors[cc, rr] = _paintColor;
                        _holderMags[cc, rr] = _paintColor >= 0 ? _defaultMag : 0;
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

        private void RebuildPreview()
        {
            if (_previewRoot) Destroy(_previewRoot.gameObject);
            _previewRoot = new GameObject("BalloonPreview").transform;
            _previewObjs = new GameObject[_gridCols, _gridRows];
            _previewLabels = new TextMesh[_gridCols, _gridRows];

            float spacing = CellSpacing;
            float scale = BalloonScale;

            for (int c = 0; c < _gridCols; c++)
            {
                for (int r = 0; r < _gridRows; r++)
                {
                    float wx = _boardCenter.x + (c - (_gridCols - 1) * 0.5f) * spacing;
                    float wz = _boardCenter.y + (r - (_gridRows - 1) * 0.5f) * spacing;

                    var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Destroy(sphere.GetComponent<Collider>());
                    sphere.transform.SetParent(_previewRoot, false);
                    sphere.transform.localScale = Vector3.one * scale;
                    sphere.transform.position = new Vector3(wx, 0.5f, wz);

                    int ci = _balloonColors[c, r];
                    int gi = _balloonGimmicks[c, r];

                    if (ci >= 0)
                    {
                        Color balloonColor = GetPreviewColor(ci, gi);
                        sphere.GetComponent<MeshRenderer>().material = MakeLitMaterial(FindLitShader(), balloonColor);
                        sphere.SetActive(true);
                    }
                    else
                    {
                        sphere.SetActive(false);
                    }
                    _previewObjs[c, r] = sphere;

                    // Gimmick label (floating text above balloon)
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

        private Color GetPreviewColor(int colorIndex, int gimmickIndex)
        {
            if (gimmickIndex > 0 && gimmickIndex < GIMMICK_NAMES.Length)
            {
                string gn = GIMMICK_NAMES[gimmickIndex];
                if (gn == "Wall") return GIMMICK_WALL_COLOR;
                if (gn == "Pin") return GIMMICK_PIN_COLOR;
                if (gn == "Ice") return GIMMICK_ICE_COLOR;
                if (gn == "Hidden") return GIMMICK_HIDDEN_COLOR;
                if (gn == "Pinata" || gn == "Pinata_Box") return GIMMICK_PINATA_COLOR;
            }
            // Normal balloon: use actual palette color
            if (colorIndex >= 0 && colorIndex < PALETTE.Length)
                return PALETTE[colorIndex];
            return Color.grey;
        }

        private void UpdatePreviewCell(int c, int r)
        {
            if (_previewObjs == null || c >= _previewObjs.GetLength(0) || r >= _previewObjs.GetLength(1)) return;
            // Full rebuild is simpler for gimmick label sync
            RebuildPreview();
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

        private void RebuildConveyorPreview()
        {
            if (_conveyorPreviewRoot) Destroy(_conveyorPreviewRoot.gameObject);
            _conveyorPreviewRoot = new GameObject("ConveyorPreview").transform;

            if (_pathGrid == null) return;

            int pw = _pathGrid.GetLength(0);
            int ph = _pathGrid.GetLength(1);
            float spacing = CellSpacing;

            for (int gx = 0; gx < pw; gx++)
            {
                for (int gy = 0; gy < ph; gy++)
                {
                    // World position: convert extended grid coord to world space
                    Vector3 wpos = PathGridToWorld(gx, gy);

                    if (_conveyorPaintMode)
                    {
                        // Show faint grid outline for all extended cells (paint guide)
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

                    if (!_pathGrid[gx, gy]) continue;

                    // Place tile sprite based on neighbor auto-tiling
                    Sprite tile = GetPathTileSprite(gx, gy);
                    if (tile != null)
                    {
                        PlaceConveyorSpriteTile(tile, wpos, spacing);
                    }
                    else
                    {
                        // Fallback: colored quad if no tile sprite available
                        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                        Destroy(quad.GetComponent<Collider>());
                        quad.transform.SetParent(_conveyorPreviewRoot, false);
                        quad.transform.localScale = new Vector3(spacing * 0.95f, spacing * 0.95f, 1f);
                        quad.transform.position = new Vector3(wpos.x, -0.05f, wpos.z);
                        quad.transform.eulerAngles = new Vector3(90f, 0f, 0f);
                        if (_conveyorMat) quad.GetComponent<MeshRenderer>().sharedMaterial = _conveyorMat;
                    }
                }
            }
        }

        /// <summary>
        /// Converts extended path grid coordinates to world position.
        /// Grid (PATH_PAD, PATH_PAD) = balloon grid (0,0).
        /// </summary>
        private Vector3 PathGridToWorld(int gx, int gy)
        {
            float spacing = CellSpacing;
            int bx = gx - PATH_PAD; // balloon grid x (-1 = outer left)
            int by = gy - PATH_PAD; // balloon grid y (-1 = outer bottom)
            float wx = _boardCenter.x + (bx - (_gridCols - 1) * 0.5f) * spacing;
            float wz = _boardCenter.y + (by - (_gridRows - 1) * 0.5f) * spacing;
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
                        RebuildPreview();
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
                    if (_balloonColors[col, row] != _paintColor || _balloonGimmicks[col, row] != (_paintColor >= 0 ? _paintGimmick : 0))
                    {
                        _balloonColors[col, row] = _paintColor;
                        _balloonGimmicks[col, row] = _paintColor >= 0 ? _paintGimmick : 0;
                        RebuildPreview();
                        RefreshInfo();
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
            for (int i = 0; i < Mathf.Min(9, _numColors); i++)
                if (kb[numKeys[i]].wasPressedThisFrame) SetPaintColor(i);
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
                ? $"Brush: {COLOR_LABELS[_paintColor]} + {GIMMICK_NAMES[_paintGimmick]}"
                : "Brush: Eraser");
        }

        private void UpdatePaletteHighlight()
        {
            if (_palTexts == null) return;
            for (int i = 0; i < _palTexts.Length; i++)
            {
                if (_palTexts[i] == null) continue;
                bool sel = (i < _numColors && i == _paintColor) || (i == _numColors && _paintColor == -1);
                string num = i < _numColors ? (i + 1).ToString() : "X";
                _palTexts[i].text = sel ? $"[{num}]" : num;
            }
        }

        private void OnBalloonGridChanged()
        {
            InitGrid();
            RebuildPreview();
            RebuildGridLines();
            RebuildConveyorPreview();
            RefreshInfo();
        }

        private void FillBalloons(int color)
        {
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                { _balloonColors[c, r] = color; _balloonGimmicks[c, r] = color >= 0 ? _paintGimmick : 0; }
        }

        private void RandomBalloons()
        {
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                { _balloonColors[c, r] = Random.Range(0, _numColors); _balloonGimmicks[c, r] = 0; }
        }

        private void FillHolders(int color)
        {
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                { _holderColors[c, r] = color; _holderMags[c, r] = color >= 0 ? _defaultMag : 0; }
        }

        private void RandomHolders()
        {
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                { _holderColors[c, r] = Random.Range(0, _numColors); _holderMags[c, r] = _defaultMag; }
        }

        private void SetAllMags()
        {
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                    if (_holderColors[c, r] >= 0) _holderMags[c, r] = _defaultMag;
        }

        private int GetGimmickLife(int gimmickIndex)
        {
            if (gimmickIndex <= 0 || gimmickIndex >= GIMMICK_NAMES.Length) return 1;
            string g = GIMMICK_NAMES[gimmickIndex];
            if (g == "Pinata" || g == "Pinata_Box") return 2;
            if (g == "Wall" || g == "Pin" || g == "Ice") return 0;
            return 1;
        }

        private void RefreshInfo()
        {
            if (_txtColsVal) _txtColsVal.text = _gridCols.ToString();
            if (_txtRowsVal) _txtRowsVal.text = _gridRows.ToString();
            if (_txtBoardVal) _txtBoardVal.text = _boardWorldSize.ToString("F1");
            if (_txtSpacing) _txtSpacing.text = $"  Spacing: {CellSpacing:F3}";
            if (_txtScale) _txtScale.text = $"  Scale: {BalloonScale:F3}";

            // === Build per-color stats ===
            var balloonCountPerColor = new Dictionary<int, int>();
            var dartsNeededPerColor = new Dictionary<int, int>();
            var gimmickCounts = new Dictionary<string, int>();

            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                    if (_balloonColors[c, r] >= 0)
                    {
                        int ci = _balloonColors[c, r];
                        int life = GetGimmickLife(_balloonGimmicks[c, r]);
                        balloonCountPerColor[ci] = (balloonCountPerColor.ContainsKey(ci) ? balloonCountPerColor[ci] : 0) + 1;
                        dartsNeededPerColor[ci] = (dartsNeededPerColor.ContainsKey(ci) ? dartsNeededPerColor[ci] : 0) + life;

                        int gi = _balloonGimmicks[c, r];
                        if (gi > 0 && gi < GIMMICK_NAMES.Length)
                        {
                            string gn = GIMMICK_NAMES[gi];
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
                        int ci = _balloonColors[c, r];
                        int life = GetGimmickLife(_balloonGimmicks[c, r]);
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
            SetStatus("Auto Balance (gimmick life accounted)");
        }

        private void TestPlay()
        {
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

            string json = JsonUtility.ToJson(config, false);
            EditorPrefs.SetString("BalloonFlow_TestLevel", json);
            EditorPrefs.SetBool("BalloonFlow_UseTestLevel", true);
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

        private void ApplyLevelConfig(LevelConfig config)
        {
            _levelId = config.levelId;
            _numColors = config.numColors;
            _difficulty = config.difficultyPurpose;
            _gridCols = Mathf.Max(config.gridCols, 2);
            _gridRows = Mathf.Max(config.gridRows, 2);

            float spacing = _boardWorldSize / Mathf.Max(_gridCols, _gridRows);
            _balloonColors = new int[_gridCols, _gridRows];
            _balloonGimmicks = new int[_gridCols, _gridRows];
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++) { _balloonColors[c, r] = -1; _balloonGimmicks[c, r] = 0; }

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
                            for (int g = 0; g < GIMMICK_NAMES.Length; g++)
                                if (GIMMICK_NAMES[g] == b.gimmickType) { gi = g; break; }
                        _balloonGimmicks[col, row] = gi;
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
                for (int c = 0; c < _holderCols; c++)
                    for (int r = 0; r < _holderRows; r++) { _holderColors[c, r] = -1; _holderMags[c, r] = 0; }
                foreach (var h in config.holders)
                {
                    int hc = Mathf.RoundToInt(h.position.x), hr = Mathf.RoundToInt(h.position.y);
                    if (hc >= 0 && hc < _holderCols && hr >= 0 && hr < _holderRows)
                    { _holderColors[hc, hr] = h.color; _holderMags[hc, hr] = h.magazineCount; }
                }
            }

            // Load rail capacity: prefer LevelConfig.railCapacity, fallback to rail.slotCount
            if (config.railCapacity > 0) _railSlotCount = config.railCapacity;
            else if (config.rail.slotCount > 0) _railSlotCount = config.rail.slotCount;
            _smoothCorners = config.rail.smoothCorners;
            _cornerRadius = config.rail.cornerRadius > 0f ? config.rail.cornerRadius : 1f;
            if (config.rail.waypoints != null && config.rail.waypoints.Length > 0)
            {
                _customWaypoints = new List<Vector3>(config.rail.waypoints);
            }

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

            RebuildPalette();
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
            if (_targetDB == null)
            {
                string path = "Assets/Resources/LevelDatabase.asset";
                _targetDB = AssetDatabase.LoadAssetAtPath<LevelDatabase>(path);
                if (_targetDB == null)
                { _targetDB = ScriptableObject.CreateInstance<LevelDatabase>(); AssetDatabase.CreateAsset(_targetDB, path); }
            }
            var config = BuildLevelConfig();
            var levels = _targetDB.levels != null ? new List<LevelConfig>(_targetDB.levels) : new List<LevelConfig>();
            int idx = levels.FindIndex(l => l.levelId == config.levelId);
            if (idx >= 0) levels[idx] = config; else levels.Add(config);
            levels.Sort((a, b) => a.levelId.CompareTo(b.levelId));
            _targetDB.levels = levels.ToArray();
            EditorUtility.SetDirty(_targetDB);
            AssetDatabase.SaveAssets();
            SetStatus($"Saved Level {config.levelId}");
            RefreshLevelList();
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
                    balloons.Add(new BalloonLayout
                    {
                        balloonId = bid++, color = _balloonColors[c, r],
                        gridPosition = new Vector2(
                            _boardCenter.x + (c - (_gridCols - 1) * 0.5f) * spacing,
                            _boardCenter.y + (r - (_gridRows - 1) * 0.5f) * spacing),
                        gimmickType = _balloonGimmicks[c, r] > 0 ? GIMMICK_NAMES[_balloonGimmicks[c, r]] : ""
                    });
                }
            config.balloons = balloons.ToArray();
            config.balloonCount = balloons.Count;

            var holders = new List<HolderSetup>();
            int hid = 0;
            for (int r = 0; r < _holderRows; r++)
                for (int c = 0; c < _holderCols; c++)
                {
                    if (_holderColors[c, r] < 0) continue;
                    holders.Add(new HolderSetup
                    { holderId = hid++, color = _holderColors[c, r], magazineCount = _holderMags[c, r], position = new Vector2(c, r) });
                }
            config.holders = holders.ToArray();
            config.queueColumns = Mathf.Clamp(_holderCols, 2, 5);

            // Use custom waypoints
            var wp = _customWaypoints.Count >= 3 ? _customWaypoints : BuildRectangularWaypoints();
            int cols = _holderCols;
            float halfX = (_gridCols - 1) * spacing * 0.5f;
            float l = _boardCenter.x - halfX - _railPadding;
            float rr = _boardCenter.x + halfX + _railPadding;
            float bz = _boardCenter.y - (_gridRows - 1) * spacing * 0.5f - _railPadding;
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
            return config;
        }

        private string[] CollectGimmicks()
        {
            var set = new HashSet<string>();
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                { int g = _balloonGimmicks[c, r]; if (g > 0 && g < GIMMICK_NAMES.Length) set.Add(GIMMICK_NAMES[g]); }
            var arr = new string[set.Count]; set.CopyTo(arr); return arr;
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
