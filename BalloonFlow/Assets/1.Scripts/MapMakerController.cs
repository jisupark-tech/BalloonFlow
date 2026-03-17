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
    /// MapMaker — 3-Panel Level Editor
    /// Left: Level list (scroll), click to load/edit
    /// Center: In-game-style board preview (3D)
    /// Right: Settings panel + Test Play button (bottom-right)
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

        private const float LEFT_PANEL_WIDTH = 240f;
        private const float RIGHT_PANEL_WIDTH = 380f;

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

        private bool[,] _conveyorTiles;
        private bool _conveyorPaintMode;

        private float CellSpacing => _boardWorldSize / Mathf.Max(_gridCols, _gridRows);
        private float BalloonScale => CellSpacing * 0.9f;

        #endregion

        #region Runtime Refs

        private Camera _cam;
        private Font _font;
        private Material[] _colorMats;
        private Transform _previewRoot;
        private GameObject[,] _previewObjs;
        private DefaultControls.Resources _uiRes;

        private Text _txtStatus, _txtSpacing, _txtScale;
        private Text _txtColsVal, _txtRowsVal, _txtBoardVal;
        private Text[] _palTexts;
        private Transform _holderGridContainer;
        private LevelDatabase _targetDB;

        // Conveyor preview
        private Transform _conveyorPreviewRoot;
        private GameObject[,] _conveyorPreviewObjs;
        private Material _conveyorMat;
        private Text _txtConveyorMode;

        // Left panel — level list
        private Transform _levelListContent;
        private int _selectedListIndex = -1;
        private List<Button> _levelListButtons = new List<Button>();

        // Center panel — level info text
        private Text _txtCenterInfo;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            IsTestMode = false;
            GameManager.IsTestPlayMode = false;

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 14);

            _uiRes = new DefaultControls.Resources();
            CreateMaterials();
            InitGrid();

            _cam = Camera.main;
            SetupCamera();
            BuildUI();
            RebuildPreview();
            RebuildConveyorPreview();
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

        private void HandleCameraControl()
        {
            if (_cam == null) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            float scroll = mouse.scroll.ReadValue().y / 120f;
            if (Mathf.Abs(scroll) > 0.01f)
                _cam.orthographicSize = Mathf.Clamp(_cam.orthographicSize - scroll * 0.5f, 1f, 30f);

            if (mouse.middleButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                float s = _cam.orthographicSize * 0.003f;
                _cam.transform.position += new Vector3(-delta.x * s, 0, -delta.y * s);
            }
        }

        #endregion

        #region Materials & Grid

        private void CreateMaterials()
        {
            var shader = Shader.Find("Standard");
            _colorMats = new Material[PALETTE.Length];
            for (int i = 0; i < PALETTE.Length; i++)
                _colorMats[i] = new Material(shader) { color = PALETTE[i] };
            _conveyorMat = new Material(shader) { color = new Color(0.35f, 0.35f, 0.45f, 0.8f) };
        }

        private void InitGrid()
        {
            _balloonColors = ResizeGrid(_balloonColors, _gridCols, _gridRows, -1);
            _balloonGimmicks = ResizeGrid(_balloonGimmicks, _gridCols, _gridRows, 0);
            _holderColors = ResizeGrid(_holderColors, _holderCols, _holderRows, -1);
            _holderMags = ResizeGrid(_holderMags, _holderCols, _holderRows, _defaultMag);
            _conveyorTiles = ResizeBoolGrid(_conveyorTiles, _gridCols, _gridRows);
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
            // Canvas (1920×1080 landscape)
            var canvasGO = new GameObject("MapMakerUI");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            // ── LEFT PANEL: Level List ──
            BuildLeftPanel(canvasGO.transform);

            // ── CENTER OVERLAY: Info text on 3D viewport ──
            BuildCenterOverlay(canvasGO.transform);

            // ── RIGHT PANEL: Settings ──
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
            var panelImg = panel.gameObject.AddComponent<Image>();
            panelImg.color = new Color(0.08f, 0.08f, 0.12f, 0.97f);

            // Header
            var header = MakeRT("Header", panel);
            header.anchorMin = new Vector2(0, 1);
            header.anchorMax = Vector2.one;
            header.pivot = new Vector2(0.5f, 1);
            header.sizeDelta = new Vector2(0, 36);
            header.anchoredPosition = Vector2.zero;
            var headerImg = header.gameObject.AddComponent<Image>();
            headerImg.color = new Color(0.12f, 0.12f, 0.18f);
            var headerTxt = MakeText(header, "LEVELS", 15, FontStyle.Bold, TextAnchor.MiddleCenter);
            SetFillRect(headerTxt.GetComponent<RectTransform>());

            // Scroll view for level list
            var scrollArea = MakeRT("ScrollArea", panel);
            scrollArea.anchorMin = Vector2.zero;
            scrollArea.anchorMax = Vector2.one;
            scrollArea.sizeDelta = new Vector2(0, -36);
            scrollArea.anchoredPosition = new Vector2(0, -18);

            var svGO = DefaultControls.CreateScrollView(_uiRes);
            svGO.transform.SetParent(scrollArea, false);
            var svRT = svGO.GetComponent<RectTransform>();
            SetFillRect(svRT);
            svGO.GetComponent<Image>().color = Color.clear;
            var sr = svGO.GetComponent<ScrollRect>();
            sr.horizontal = false;
            sr.scrollSensitivity = 30;
            var hBar = svGO.transform.Find("Scrollbar Horizontal");
            if (hBar) hBar.gameObject.SetActive(false);

            var content = sr.content;
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.spacing = 2;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            var csf = content.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _levelListContent = content;
        }

        private void BuildCenterOverlay(Transform canvasRoot)
        {
            // Semi-transparent info bar at top of center viewport
            float leftRatio = LEFT_PANEL_WIDTH / 1920f;
            float rightRatio = RIGHT_PANEL_WIDTH / 1920f;

            var overlay = MakeRT("CenterOverlay", canvasRoot);
            overlay.anchorMin = new Vector2(leftRatio, 0.92f);
            overlay.anchorMax = new Vector2(1f - rightRatio, 1f);
            overlay.sizeDelta = Vector2.zero;
            overlay.anchoredPosition = Vector2.zero;
            var overlayImg = overlay.gameObject.AddComponent<Image>();
            overlayImg.color = new Color(0f, 0f, 0f, 0.55f);

            _txtCenterInfo = MakeText(overlay, "", 13, FontStyle.Normal, TextAnchor.MiddleCenter);
            var txtRT = _txtCenterInfo.GetComponent<RectTransform>();
            SetFillRect(txtRT);
            _txtCenterInfo.color = new Color(0.85f, 0.9f, 1f);

            // Status bar at bottom of center viewport
            var statusBar = MakeRT("StatusBar", canvasRoot);
            statusBar.anchorMin = new Vector2(leftRatio, 0f);
            statusBar.anchorMax = new Vector2(1f - rightRatio, 0.04f);
            statusBar.sizeDelta = Vector2.zero;
            statusBar.anchoredPosition = Vector2.zero;
            var statusImg = statusBar.gameObject.AddComponent<Image>();
            statusImg.color = new Color(0f, 0f, 0f, 0.55f);

            _txtStatus = MakeText(statusBar, "Ready", 12, FontStyle.Normal, TextAnchor.MiddleLeft);
            var sRT = _txtStatus.GetComponent<RectTransform>();
            SetFillRect(sRT);
            sRT.offsetMin = new Vector2(8, 0);
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
            var panelImg = panel.gameObject.AddComponent<Image>();
            panelImg.color = new Color(0.10f, 0.10f, 0.14f, 0.97f);

            // ScrollView inside panel
            var content = BuildScrollView(panel);

            // === Sections ===
            Lbl(content, "MAP MAKER", 18, FontStyle.Bold);
            Sep(content);

            BuildLevelSection(content);
            BuildPaletteSection(content);
            BuildGridSection(content);
            BuildActionSection(content);
            BuildRailSection(content);
            BuildConveyorSection(content);
            BuildHolderSection(content);
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
            MakeSlider(gr, 0, GIMMICK_NAMES.Length - 1, 0, true, v => _paintGimmick = (int)v);
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
            // Eraser
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
            MakeSlider(r3, 4, 20, _boardWorldSize, false, v =>
            {
                _boardWorldSize = v;
                _cam.orthographicSize = v * 0.65f;
                RebuildPreview();
                RefreshInfo();
            });
            _txtBoardVal = Lbl(r3, _boardWorldSize.ToString("F1"), w: 36);

            _txtSpacing = Lbl(p, $"  Spacing: {CellSpacing:F3} (auto)", 11);
            _txtSpacing.color = new Color(0.6f, 0.6f, 0.7f);
            _txtScale = Lbl(p, $"  Scale: {BalloonScale:F3} (auto)", 11);
            _txtScale.color = new Color(0.6f, 0.6f, 0.7f);
            Sep(p);
        }

        private void BuildActionSection(Transform p)
        {
            var row = Row(p);
            Btn(row, "Fill All", () => { FillBalloons(_paintColor); RebuildPreview(); RefreshInfo(); });
            Btn(row, "Clear All", () => { FillBalloons(-1); RebuildPreview(); RefreshInfo(); });
            Btn(row, "Random", () => { RandomBalloons(); RebuildPreview(); RefreshInfo(); });
            Sep(p);
        }

        private void BuildRailSection(Transform p)
        {
            Lbl(p, "Rail / Conveyor", 14, FontStyle.Bold);
            var r1 = Row(p); Lbl(r1, "Direction", w: 90);
            Btn(r1, _railDir == 0 ? "CW" : "CCW", () =>
            {
                _railDir = 1 - _railDir;
            });
            var r2 = Row(p); Lbl(r2, "Padding", w: 90);
            MakeInputField(r2, _railPadding.ToString("F1"), s =>
            { if (float.TryParse(s, out float v)) _railPadding = v; });
            var r3 = Row(p); Lbl(r3, "Height", w: 90);
            MakeInputField(r3, _railHeight.ToString("F1"), s =>
            { if (float.TryParse(s, out float v)) _railHeight = v; });
            var r4 = Row(p); Lbl(r4, "Slot Count", w: 90);
            MakeSlider(r4, 50, 400, _railSlotCount, true, v => _railSlotCount = (int)v);
            Sep(p);
        }

        private void BuildConveyorSection(Transform p)
        {
            Lbl(p, "Conveyor Belt Tiles", 14, FontStyle.Bold);

            var r1 = Row(p);
            Lbl(r1, "Paint Mode", w: 90);
            _txtConveyorMode = Lbl(r1, "Balloon", w: 80);
            _txtConveyorMode.color = new Color(0.5f, 0.9f, 0.5f);
            Btn(r1, "Toggle", () =>
            {
                _conveyorPaintMode = !_conveyorPaintMode;
                if (_txtConveyorMode != null)
                {
                    _txtConveyorMode.text = _conveyorPaintMode ? "Conveyor" : "Balloon";
                    _txtConveyorMode.color = _conveyorPaintMode
                        ? new Color(0.5f, 0.5f, 0.9f)
                        : new Color(0.5f, 0.9f, 0.5f);
                }
                SetStatus(_conveyorPaintMode
                    ? "Conveyor Paint: click to toggle tiles"
                    : "Balloon Paint: click to place/erase");
            });

            var r2 = Row(p);
            Btn(r2, "Fill Conv.", () =>
            {
                for (int c = 0; c < _gridCols; c++)
                    for (int r = 0; r < _gridRows; r++)
                        _conveyorTiles[c, r] = true;
                RebuildConveyorPreview();
                RefreshInfo();
            });
            Btn(r2, "Clear Conv.", () =>
            {
                for (int c = 0; c < _gridCols; c++)
                    for (int r = 0; r < _gridRows; r++)
                        _conveyorTiles[c, r] = false;
                RebuildConveyorPreview();
                RefreshInfo();
            });
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
            Btn(row, "Set Mag", () => { SetAllMags(); RebuildHolderUI(); RefreshInfo(); });
            var row2 = Row(p);
            Btn(row2, "Auto Balance", () => { AutoBalanceHolders(); RebuildHolderUI(); RefreshInfo(); });
            Sep(p);
        }

        private void BuildExportSection(Transform p)
        {
            Lbl(p, "Export", 14, FontStyle.Bold);
            var row = Row(p);
            Btn(row, "Save to DB", SaveToDatabase);
            Btn(row, "Export JSON", ExportJson);
            Btn(row, "Load Level", () => LoadLevelById(_levelId));

            Sep(p);

            // Test Play — prominent button at bottom
            var testRow = Row(p, 40);
            var testBtn = Btn(testRow, "TEST PLAY", TestPlay);
            var testImg = testBtn.GetComponent<Image>();
            if (testImg) testImg.color = new Color(0.15f, 0.55f, 0.25f);
        }

        #endregion

        #region UI Building — Holder Grid Buttons

        private void RebuildHolderUI()
        {
            if (_holderGridContainer == null) return;
            foreach (Transform c in _holderGridContainer) Destroy(c.gameObject);

            var glg = _holderGridContainer.GetComponent<GridLayoutGroup>();
            glg.constraintCount = _holderCols;
            float cellW = Mathf.Min(280f / Mathf.Max(_holderCols, 1), 36f);
            glg.cellSize = new Vector2(cellW, cellW);
            _holderGridContainer.GetComponent<LayoutElement>().preferredHeight =
                cellW * _holderRows + (_holderRows - 1) * 2 + 4;

            for (int r = 0; r < _holderRows; r++)
            {
                for (int c = 0; c < _holderCols; c++)
                {
                    int cc = c, rr = r;
                    int ci = _holderColors[c, r];
                    var go = DefaultControls.CreateButton(_uiRes);
                    go.transform.SetParent(_holderGridContainer, false);
                    go.GetComponent<Image>().color = ci >= 0 ? PALETTE[ci] : new Color(0.22f, 0.22f, 0.26f);
                    var t = go.GetComponentInChildren<Text>();
                    t.text = ci >= 0 ? _holderMags[c, r].ToString() : ".";
                    t.font = _font; t.fontSize = 11; t.color = Color.white;
                    go.GetComponent<Button>().onClick.AddListener(() =>
                    {
                        _holderColors[cc, rr] = _paintColor;
                        _holderMags[cc, rr] = _paintColor >= 0 ? _defaultMag : 0;
                        RebuildHolderUI();
                        RefreshInfo();
                    });
                }
            }
        }

        #endregion

        #region UI Building — ScrollView

        private Transform BuildScrollView(RectTransform parent)
        {
            var svGO = DefaultControls.CreateScrollView(_uiRes);
            svGO.transform.SetParent(parent, false);
            var svRT = svGO.GetComponent<RectTransform>();
            svRT.anchorMin = Vector2.zero;
            svRT.anchorMax = Vector2.one;
            svRT.sizeDelta = Vector2.zero;
            svRT.anchoredPosition = Vector2.zero;

            svGO.GetComponent<Image>().color = Color.clear;
            var sr = svGO.GetComponent<ScrollRect>();
            sr.horizontal = false;
            sr.scrollSensitivity = 30;

            var hBar = svGO.transform.Find("Scrollbar Horizontal");
            if (hBar) hBar.gameObject.SetActive(false);

            var vBar = svGO.transform.Find("Scrollbar Vertical");
            if (vBar)
            {
                var vBarImg = vBar.GetComponent<Image>();
                if (vBarImg) vBarImg.color = new Color(0.15f, 0.15f, 0.2f);
            }

            var content = sr.content;
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 6, 6);
            vlg.spacing = 3;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            var csf = content.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return content;
        }

        #endregion

        #region UI Helpers

        private RectTransform MakeRT(string n, Transform parent)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private void SetFillRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
        }

        private Text MakeText(Transform parent, string text, int size,
            FontStyle style, TextAnchor align)
        {
            var go = new GameObject("Txt", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = text; t.font = _font; t.fontSize = size;
            t.fontStyle = style; t.color = Color.white; t.alignment = align;
            return t;
        }

        private Text Lbl(Transform parent, string text, int size = 13,
            FontStyle style = FontStyle.Normal, float w = 0)
        {
            var go = new GameObject("L", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = size + 8;
            if (w > 0) le.preferredWidth = w; else le.flexibleWidth = 1;
            var t = go.GetComponent<Text>();
            t.text = text; t.font = _font; t.fontSize = size;
            t.fontStyle = style; t.color = Color.white; t.alignment = TextAnchor.MiddleLeft;
            return t;
        }

        private RectTransform Row(Transform parent, float h = 28)
        {
            var go = new GameObject("R", typeof(RectTransform),
                typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = h;
            var hlg = go.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            return go.GetComponent<RectTransform>();
        }

        private void Sep(Transform parent)
        {
            var go = new GameObject("Sep", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.35f);
            go.GetComponent<LayoutElement>().preferredHeight = 1;
        }

        private Slider MakeSlider(Transform parent, float min, float max, float val,
            bool whole, System.Action<float> cb)
        {
            var go = DefaultControls.CreateSlider(_uiRes);
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1; le.preferredHeight = 20;
            var bg = go.transform.Find("Background")?.GetComponent<Image>();
            if (bg) bg.color = new Color(0.18f, 0.18f, 0.22f);
            var fill = go.transform.Find("Fill Area/Fill")?.GetComponent<Image>();
            if (fill) fill.color = new Color(0.3f, 0.55f, 0.9f);
            var s = go.GetComponent<Slider>();
            s.minValue = min; s.maxValue = max; s.wholeNumbers = whole; s.value = val;
            if (cb != null) s.onValueChanged.AddListener(v => cb(v));
            return s;
        }

        private InputField MakeInputField(Transform parent, string text, System.Action<string> cb)
        {
            var go = DefaultControls.CreateInputField(_uiRes);
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1; le.preferredHeight = 24;
            go.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.20f);
            var inp = go.GetComponent<InputField>();
            inp.text = text;
            inp.textComponent.font = _font; inp.textComponent.fontSize = 13;
            inp.textComponent.color = Color.white;
            var ph = inp.placeholder as Text;
            if (ph) { ph.font = _font; ph.fontSize = 13; }
            if (cb != null) inp.onEndEdit.AddListener(v => cb(v));
            return inp;
        }

        private void MakeDifficultyDropdown(Transform parent)
        {
            var go = DefaultControls.CreateDropdown(_uiRes);
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1; le.preferredHeight = 24;
            go.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.20f);
            var dd = go.GetComponent<Dropdown>();
            dd.ClearOptions();
            var names = System.Enum.GetNames(typeof(DifficultyPurpose));
            dd.AddOptions(new List<string>(names));
            dd.value = (int)_difficulty;
            dd.captionText.font = _font; dd.captionText.fontSize = 13; dd.captionText.color = Color.white;
            dd.onValueChanged.AddListener(v => _difficulty = (DifficultyPurpose)v);
        }

        private Button Btn(Transform parent, string text, System.Action cb)
        {
            var go = DefaultControls.CreateButton(_uiRes);
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1; le.preferredHeight = 28;
            go.GetComponent<Image>().color = new Color(0.22f, 0.22f, 0.28f);
            var t = go.GetComponentInChildren<Text>();
            t.text = text; t.font = _font; t.fontSize = 12; t.color = Color.white;
            go.GetComponent<Button>().onClick.AddListener(() => cb?.Invoke());
            return go.GetComponent<Button>();
        }

        private Button MakeColorBtn(Transform parent, Color color, string label, System.Action cb)
        {
            var go = DefaultControls.CreateButton(_uiRes);
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 34; le.preferredHeight = 34;
            go.GetComponent<Image>().color = color;
            var t = go.GetComponentInChildren<Text>();
            t.text = label; t.font = _font; t.fontSize = 14;
            t.fontStyle = FontStyle.Bold; t.color = Color.white;
            go.GetComponent<Button>().onClick.AddListener(() => cb?.Invoke());
            return go.GetComponent<Button>();
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        //  BOARD PREVIEW
        // ═══════════════════════════════════════════════════════════════

        #region Board Preview

        private void RebuildPreview()
        {
            if (_previewRoot) Destroy(_previewRoot.gameObject);
            _previewRoot = new GameObject("BalloonPreview").transform;
            _previewObjs = new GameObject[_gridCols, _gridRows];

            float spacing = CellSpacing;
            float scale = BalloonScale;

            for (int c = 0; c < _gridCols; c++)
            {
                for (int r = 0; r < _gridRows; r++)
                {
                    var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Destroy(sphere.GetComponent<Collider>());
                    sphere.transform.SetParent(_previewRoot, false);
                    sphere.transform.localScale = Vector3.one * scale;

                    float wx = _boardCenter.x + (c - (_gridCols - 1) * 0.5f) * spacing;
                    float wz = _boardCenter.y + (r - (_gridRows - 1) * 0.5f) * spacing;
                    sphere.transform.position = new Vector3(wx, 0.5f, wz);

                    int ci = _balloonColors[c, r];
                    if (ci >= 0 && ci < _colorMats.Length)
                    {
                        sphere.GetComponent<MeshRenderer>().sharedMaterial = _colorMats[ci];
                        sphere.SetActive(true);
                    }
                    else
                    {
                        sphere.SetActive(false);
                    }
                    _previewObjs[c, r] = sphere;
                }
            }
        }

        private void UpdatePreviewCell(int c, int r)
        {
            if (_previewObjs == null || c >= _previewObjs.GetLength(0) || r >= _previewObjs.GetLength(1))
                return;
            var obj = _previewObjs[c, r];
            if (obj == null) return;
            int ci = _balloonColors[c, r];
            if (ci >= 0 && ci < _colorMats.Length)
            {
                obj.GetComponent<MeshRenderer>().sharedMaterial = _colorMats[ci];
                obj.SetActive(true);
            }
            else
            {
                obj.SetActive(false);
            }
        }

        #endregion

        #region Conveyor Preview

        private void RebuildConveyorPreview()
        {
            if (_conveyorPreviewRoot) Destroy(_conveyorPreviewRoot.gameObject);
            _conveyorPreviewRoot = new GameObject("ConveyorPreview").transform;
            _conveyorPreviewObjs = new GameObject[_gridCols, _gridRows];

            float spacing = CellSpacing;
            float tileSize = spacing * 0.95f;

            for (int c = 0; c < _gridCols; c++)
            {
                for (int r = 0; r < _gridRows; r++)
                {
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Destroy(quad.GetComponent<Collider>());
                    quad.transform.SetParent(_conveyorPreviewRoot, false);
                    quad.transform.localScale = new Vector3(tileSize, tileSize, 1f);

                    float wx = _boardCenter.x + (c - (_gridCols - 1) * 0.5f) * spacing;
                    float wz = _boardCenter.y + (r - (_gridRows - 1) * 0.5f) * spacing;
                    quad.transform.position = new Vector3(wx, -0.04f, wz);
                    quad.transform.eulerAngles = new Vector3(90f, 0f, 0f);

                    if (_conveyorMat != null)
                        quad.GetComponent<MeshRenderer>().sharedMaterial = _conveyorMat;

                    quad.SetActive(_conveyorTiles != null && c < _conveyorTiles.GetLength(0)
                        && r < _conveyorTiles.GetLength(1) && _conveyorTiles[c, r]);
                    _conveyorPreviewObjs[c, r] = quad;
                }
            }
        }

        private void UpdateConveyorPreviewCell(int c, int r)
        {
            if (_conveyorPreviewObjs == null
                || c >= _conveyorPreviewObjs.GetLength(0)
                || r >= _conveyorPreviewObjs.GetLength(1))
                return;
            var obj = _conveyorPreviewObjs[c, r];
            if (obj == null) return;
            obj.SetActive(_conveyorTiles[c, r]);
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

            Ray ray = _cam.ScreenPointToRay(mouse.position.ReadValue());
            if (Mathf.Abs(ray.direction.y) < 0.001f) return;

            float t = (0.5f - ray.origin.y) / ray.direction.y;
            if (t <= 0) return;

            Vector3 hit = ray.origin + ray.direction * t;
            float spacing = CellSpacing;
            float relC = (hit.x - _boardCenter.x) / spacing + (_gridCols - 1) * 0.5f;
            float relR = (hit.z - _boardCenter.y) / spacing + (_gridRows - 1) * 0.5f;
            int col = Mathf.RoundToInt(relC);
            int row = Mathf.RoundToInt(relR);

            if (col >= 0 && col < _gridCols && row >= 0 && row < _gridRows)
            {
                if (_conveyorPaintMode)
                {
                    _conveyorTiles[col, row] = !_conveyorTiles[col, row];
                    UpdateConveyorPreviewCell(col, row);
                    _conveyorClickConsumed = true;
                    RefreshInfo();
                }
                else
                {
                    if (_balloonColors[col, row] != _paintColor)
                    {
                        _balloonColors[col, row] = _paintColor;
                        _balloonGimmicks[col, row] = _paintColor >= 0 ? _paintGimmick : 0;
                        UpdatePreviewCell(col, row);
                        RefreshInfo();
                    }
                }
            }
        }

        #endregion

        #region Keyboard

        private void HandleKeyboard()
        {
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            {
                var sel = EventSystem.current.currentSelectedGameObject;
                if (sel.GetComponent<InputField>() != null) return;
            }

            var kb = Keyboard.current;
            if (kb == null) return;

            Key[] numKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
                              Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
            for (int i = 0; i < Mathf.Min(9, _numColors); i++)
            {
                if (kb[numKeys[i]].wasPressedThisFrame)
                    SetPaintColor(i);
            }
            if (kb[Key.Digit0].wasPressedThisFrame) SetPaintColor(-1);
            if (kb[Key.Backquote].wasPressedThisFrame) SetPaintColor(-1);
            if (kb[Key.Tab].wasPressedThisFrame)
            {
                _conveyorPaintMode = !_conveyorPaintMode;
                if (_txtConveyorMode != null)
                {
                    _txtConveyorMode.text = _conveyorPaintMode ? "Conveyor" : "Balloon";
                    _txtConveyorMode.color = _conveyorPaintMode
                        ? new Color(0.5f, 0.5f, 0.9f)
                        : new Color(0.5f, 0.9f, 0.5f);
                }
                SetStatus(_conveyorPaintMode ? "Conveyor Paint (Tab)" : "Balloon Paint (Tab)");
            }
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
                ? $"Brush: {COLOR_LABELS[_paintColor]} ({_paintColor + 1})"
                : "Brush: Eraser");
        }

        private void UpdatePaletteHighlight()
        {
            if (_palTexts == null) return;
            for (int i = 0; i < _palTexts.Length; i++)
            {
                if (_palTexts[i] == null) continue;
                bool sel = (i < _numColors && i == _paintColor) ||
                           (i == _numColors && _paintColor == -1);
                string num = i < _numColors ? (i + 1).ToString() : "X";
                _palTexts[i].text = sel ? $"[{num}]" : num;
            }
        }

        private void OnBalloonGridChanged()
        {
            InitGrid();
            RebuildPreview();
            RebuildConveyorPreview();
            RefreshInfo();
        }

        private void FillBalloons(int color)
        {
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                {
                    _balloonColors[c, r] = color;
                    _balloonGimmicks[c, r] = color >= 0 ? _paintGimmick : 0;
                }
        }

        private void RandomBalloons()
        {
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                {
                    _balloonColors[c, r] = Random.Range(0, _numColors);
                    _balloonGimmicks[c, r] = 0;
                }
        }

        private void FillHolders(int color)
        {
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                {
                    _holderColors[c, r] = color;
                    _holderMags[c, r] = color >= 0 ? _defaultMag : 0;
                }
        }

        private void RandomHolders()
        {
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                {
                    _holderColors[c, r] = Random.Range(0, _numColors);
                    _holderMags[c, r] = _defaultMag;
                }
        }

        private void SetAllMags()
        {
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                    if (_holderColors[c, r] >= 0)
                        _holderMags[c, r] = _defaultMag;
        }

        /// <summary>
        /// Returns gimmick life for balloon dart calculations.
        /// Matches LevelDatabaseGenerator50.GetGimmickLife().
        /// </summary>
        private int GetGimmickLifeForBalance(int gimmickIndex)
        {
            if (gimmickIndex <= 0 || gimmickIndex >= GIMMICK_NAMES.Length) return 1;
            string gimmick = GIMMICK_NAMES[gimmickIndex];
            switch (gimmick)
            {
                case "Pinata":
                case "Pinata_Box": return 2;
                case "Wall":
                case "Pin":
                case "Ice": return 0;
                default: return 1;
            }
        }

        private void RefreshInfo()
        {
            if (_txtColsVal) _txtColsVal.text = _gridCols.ToString();
            if (_txtRowsVal) _txtRowsVal.text = _gridRows.ToString();
            if (_txtBoardVal) _txtBoardVal.text = _boardWorldSize.ToString("F1");
            if (_txtSpacing) _txtSpacing.text = $"  Spacing: {CellSpacing:F3} (auto)";
            if (_txtScale) _txtScale.text = $"  Scale: {BalloonScale:F3} (auto)";

            int bc = CountBalloons();
            int hc = CountHolders();
            int tm = CountTotalMags();

            // Per-color validation accounting for gimmick life
            var dartsNeededPerColor = new Dictionary<int, int>();
            var dartsProvidedPerColor = new Dictionary<int, int>();

            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                    if (_balloonColors[c, r] >= 0)
                    {
                        int ci = _balloonColors[c, r];
                        int life = GetGimmickLifeForBalance(_balloonGimmicks[c, r]);
                        dartsNeededPerColor[ci] = dartsNeededPerColor.ContainsKey(ci) ? dartsNeededPerColor[ci] + life : life;
                    }

            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                    if (_holderColors[c, r] >= 0)
                    {
                        int ci = _holderColors[c, r];
                        dartsProvidedPerColor[ci] = dartsProvidedPerColor.ContainsKey(ci) ? dartsProvidedPerColor[ci] + _holderMags[c, r] : _holderMags[c, r];
                    }

            int totalDartsNeeded = 0;
            foreach (var v in dartsNeededPerColor.Values) totalDartsNeeded += v;

            string warnings = "";
            foreach (var kvp in dartsNeededPerColor)
            {
                int darts = dartsProvidedPerColor.ContainsKey(kvp.Key) ? dartsProvidedPerColor[kvp.Key] : 0;
                if (darts != kvp.Value)
                {
                    string label = kvp.Key < COLOR_LABELS.Length ? COLOR_LABELS[kvp.Key] : kvp.Key.ToString();
                    warnings += $" [{label}:{kvp.Value}need/{darts}have]";
                }
            }
            foreach (var kvp in dartsProvidedPerColor)
            {
                if (!dartsNeededPerColor.ContainsKey(kvp.Key) || dartsNeededPerColor[kvp.Key] == 0)
                {
                    string label = kvp.Key < COLOR_LABELS.Length ? COLOR_LABELS[kvp.Key] : kvp.Key.ToString();
                    warnings += $" [{label}:0need/{kvp.Value}have]";
                }
            }

            string status = $"Balloons: {bc}  Holders: {hc}  Darts: {tm} (need: {totalDartsNeeded})";
            if (tm < totalDartsNeeded) status += "  [NOT ENOUGH]";
            if (tm > totalDartsNeeded) status += "  [SURPLUS]";
            if (!string.IsNullOrEmpty(warnings)) status += "\n" + warnings;

            if (_txtStatus) _txtStatus.text = status;

            // Center info panel
            if (_txtCenterInfo)
            {
                string gimmickInfo = "";
                var gimmickCounts = new Dictionary<string, int>();
                for (int c = 0; c < _gridCols; c++)
                    for (int r = 0; r < _gridRows; r++)
                        if (_balloonGimmicks[c, r] > 0 && _balloonGimmicks[c, r] < GIMMICK_NAMES.Length)
                        {
                            string gn = GIMMICK_NAMES[_balloonGimmicks[c, r]];
                            gimmickCounts[gn] = gimmickCounts.ContainsKey(gn) ? gimmickCounts[gn] + 1 : 1;
                        }
                foreach (var kvp in gimmickCounts)
                    gimmickInfo += $"  {kvp.Key}:{kvp.Value}";

                _txtCenterInfo.text = $"Level {_levelId}  |  {_gridCols}x{_gridRows}  |  {_numColors} colors  |  {bc} balloons  |  {_difficulty}" +
                    (string.IsNullOrEmpty(gimmickInfo) ? "" : $"\nGimmicks:{gimmickInfo}");
            }
        }

        private void SetStatus(string msg) { if (_txtStatus) _txtStatus.text = msg; }

        /// <summary>
        /// Auto-adjusts holder magazines so total darts per color = total balloon life per color.
        /// Accounts for gimmick life (Wall/Pin/Ice = 0, Pinata = 2, others = 1).
        /// </summary>
        private void AutoBalanceHolders()
        {
            // Count darts needed per color (accounting for gimmick life)
            var balloonsPerColor = new Dictionary<int, int>();
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                    if (_balloonColors[c, r] >= 0)
                    {
                        int ci = _balloonColors[c, r];
                        int life = GetGimmickLifeForBalance(_balloonGimmicks[c, r]);
                        balloonsPerColor[ci] = balloonsPerColor.ContainsKey(ci) ? balloonsPerColor[ci] + life : life;
                    }

            // Group holder positions by color
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

            // Distribute darts per color evenly across holders
            foreach (var kvp in balloonsPerColor)
            {
                int color = kvp.Key;
                int needed = kvp.Value;
                if (!holdersByColor.ContainsKey(color) || holdersByColor[color].Count == 0)
                    continue;

                var holders = holdersByColor[color];
                int perHolder = needed / holders.Count;
                int remainder = needed % holders.Count;
                int sumSoFar = 0;

                for (int i = 0; i < holders.Count; i++)
                {
                    int cc = holders[i].Item1, rr = holders[i].Item2;
                    if (i < holders.Count - 1)
                    {
                        int mag = perHolder + (i < remainder ? 1 : 0);
                        _holderMags[cc, rr] = Mathf.Max(1, mag);
                        sumSoFar += _holderMags[cc, rr];
                    }
                    else
                    {
                        _holderMags[cc, rr] = Mathf.Max(1, needed - sumSoFar);
                    }
                }
            }

            SetStatus("Auto Balance applied (gimmick life accounted)");
        }

        private void TestPlay()
        {
            var config = BuildLevelConfig();
            string json = JsonUtility.ToJson(config, false);

            EditorPrefs.SetString("BalloonFlow_TestLevel", json);
            EditorPrefs.SetBool("BalloonFlow_UseTestLevel", true);
            PlayerPrefs.SetInt("BF_PendingLevelId", _levelId);

            IsTestMode = true;
            GameManager.IsTestPlayMode = true;

            SetStatus($"Test Play: Loading level {_levelId}...");
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
                var emptyTxt = MakeText(_levelListContent, "No levels in DB.\nUse 'Save to DB' to add.", 12,
                    FontStyle.Italic, TextAnchor.MiddleCenter);
                emptyTxt.color = new Color(0.5f, 0.5f, 0.6f);
                var le = emptyTxt.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 60;
                return;
            }

            for (int i = 0; i < db.levels.Length; i++)
            {
                var lvl = db.levels[i];
                int idx = i;
                int lid = lvl.levelId;

                var go = DefaultControls.CreateButton(_uiRes);
                go.transform.SetParent(_levelListContent, false);
                var le = go.AddComponent<LayoutElement>();
                le.preferredHeight = 36;

                bool isSelected = (lid == _levelId);
                var img = go.GetComponent<Image>();
                img.color = isSelected ? new Color(0.2f, 0.35f, 0.55f) : new Color(0.15f, 0.15f, 0.20f);

                var txt = go.GetComponentInChildren<Text>();
                txt.font = _font; txt.fontSize = 12; txt.color = Color.white;
                txt.alignment = TextAnchor.MiddleLeft;

                // Gimmick summary
                string gimmickStr = "";
                if (lvl.gimmickTypes != null && lvl.gimmickTypes.Length > 0)
                    gimmickStr = $" [{string.Join(",", lvl.gimmickTypes)}]";

                txt.text = $"  Lv.{lid}  {lvl.balloonCount}B  {lvl.numColors}C{gimmickStr}";

                var btn = go.GetComponent<Button>();
                btn.onClick.AddListener(() =>
                {
                    LoadLevelFromDB(idx);
                });
                _levelListButtons.Add(btn);
            }
        }

        private void LoadLevelFromDB(int index)
        {
            var db = LoadLevelDatabase();
            if (db == null || db.levels == null || index < 0 || index >= db.levels.Length) return;

            var config = db.levels[index];
            ApplyLevelConfig(config);
            _selectedListIndex = index;

            RefreshLevelList();
            RebuildPreview();
            RebuildConveyorPreview();
            RefreshInfo();
            SetStatus($"Loaded Level {config.levelId} from DB");
        }

        private void LoadLevelById(int levelId)
        {
            var db = LoadLevelDatabase();
            if (db == null || db.levels == null)
            {
                SetStatus("No LevelDatabase found");
                return;
            }

            for (int i = 0; i < db.levels.Length; i++)
            {
                if (db.levels[i].levelId == levelId)
                {
                    LoadLevelFromDB(i);
                    return;
                }
            }
            SetStatus($"Level {levelId} not found in DB");
        }

        private void ApplyLevelConfig(LevelConfig config)
        {
            _levelId = config.levelId;
            _numColors = config.numColors;
            _difficulty = config.difficultyPurpose;

            // Determine grid size from balloon positions
            if (config.balloons != null && config.balloons.Length > 0)
            {
                _gridCols = Mathf.Max(config.gridCols, 2);
                _gridRows = Mathf.Max(config.gridRows, 2);
            }

            // Reconstruct balloon grid
            float spacing = _boardWorldSize / Mathf.Max(_gridCols, _gridRows);
            _balloonColors = new int[_gridCols, _gridRows];
            _balloonGimmicks = new int[_gridCols, _gridRows];
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                {
                    _balloonColors[c, r] = -1;
                    _balloonGimmicks[c, r] = 0;
                }

            if (config.balloons != null)
            {
                foreach (var b in config.balloons)
                {
                    // Convert world position back to grid coordinates
                    float relC = (b.gridPosition.x - _boardCenter.x) / spacing + (_gridCols - 1) * 0.5f;
                    float relR = (b.gridPosition.y - _boardCenter.y) / spacing + (_gridRows - 1) * 0.5f;
                    int col = Mathf.RoundToInt(relC);
                    int row = Mathf.RoundToInt(relR);

                    if (col >= 0 && col < _gridCols && row >= 0 && row < _gridRows)
                    {
                        _balloonColors[col, row] = b.color;
                        // Find gimmick index
                        int gi = 0;
                        if (!string.IsNullOrEmpty(b.gimmickType))
                        {
                            for (int g = 0; g < GIMMICK_NAMES.Length; g++)
                            {
                                if (GIMMICK_NAMES[g] == b.gimmickType) { gi = g; break; }
                            }
                        }
                        _balloonGimmicks[col, row] = gi;
                    }
                }
            }

            // Reconstruct holder grid
            if (config.holders != null && config.holders.Length > 0)
            {
                // Determine holder grid dimensions
                int maxC = 0, maxR = 0;
                foreach (var h in config.holders)
                {
                    maxC = Mathf.Max(maxC, Mathf.RoundToInt(h.position.x) + 1);
                    maxR = Mathf.Max(maxR, Mathf.RoundToInt(h.position.y) + 1);
                }
                _holderCols = Mathf.Max(maxC, 1);
                _holderRows = Mathf.Max(maxR, 1);
                _holderColors = new int[_holderCols, _holderRows];
                _holderMags = new int[_holderCols, _holderRows];
                for (int c = 0; c < _holderCols; c++)
                    for (int r = 0; r < _holderRows; r++)
                    {
                        _holderColors[c, r] = -1;
                        _holderMags[c, r] = 0;
                    }

                foreach (var h in config.holders)
                {
                    int hc = Mathf.RoundToInt(h.position.x);
                    int hr = Mathf.RoundToInt(h.position.y);
                    if (hc >= 0 && hc < _holderCols && hr >= 0 && hr < _holderRows)
                    {
                        _holderColors[hc, hr] = h.color;
                        _holderMags[hc, hr] = h.magazineCount;
                    }
                }
            }

            // Rail
            if (config.rail.slotCount > 0)
                _railSlotCount = config.rail.slotCount;

            // Conveyor positions
            _conveyorTiles = new bool[_gridCols, _gridRows];
            if (config.conveyorPositions != null)
            {
                foreach (var pos in config.conveyorPositions)
                {
                    if (pos.x >= 0 && pos.x < _gridCols && pos.y >= 0 && pos.y < _gridRows)
                        _conveyorTiles[pos.x, pos.y] = true;
                }
            }

            RebuildPalette();
            RebuildHolderUI();
        }

        private LevelDatabase LoadLevelDatabase()
        {
            if (_targetDB != null) return _targetDB;
            string path = "Assets/Resources/LevelDatabase.asset";
            _targetDB = AssetDatabase.LoadAssetAtPath<LevelDatabase>(path);
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
                {
                    _targetDB = ScriptableObject.CreateInstance<LevelDatabase>();
                    AssetDatabase.CreateAsset(_targetDB, path);
                }
            }

            var config = BuildLevelConfig();
            var levels = _targetDB.levels != null
                ? new List<LevelConfig>(_targetDB.levels) : new List<LevelConfig>();
            int idx = levels.FindIndex(l => l.levelId == config.levelId);
            if (idx >= 0) levels[idx] = config; else levels.Add(config);
            levels.Sort((a, b) => a.levelId.CompareTo(b.levelId));
            _targetDB.levels = levels.ToArray();

            EditorUtility.SetDirty(_targetDB);
            AssetDatabase.SaveAssets();
            SetStatus($"Saved Level {config.levelId} — {config.balloonCount} balloons");

            RefreshLevelList();
        }

        private void ExportJson()
        {
            var config = BuildLevelConfig();
            string json = JsonUtility.ToJson(config, true);
            string path = EditorUtility.SaveFilePanel("Export JSON", "Assets",
                $"level_{config.levelId}", "json");
            if (!string.IsNullOrEmpty(path))
            {
                System.IO.File.WriteAllText(path, json);
                SetStatus($"Exported: {System.IO.Path.GetFileName(path)}");
            }
        }

        private LevelConfig BuildLevelConfig()
        {
            float spacing = CellSpacing;
            var config = new LevelConfig
            {
                levelId = _levelId,
                packageId = 1,
                positionInPackage = _levelId,
                numColors = _numColors,
                difficultyPurpose = _difficulty,
                gimmickTypes = CollectGimmicks(),
                balloonScale = BalloonScale,
            };

            var balloons = new List<BalloonLayout>();
            int bid = 0;
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                {
                    if (_balloonColors[c, r] < 0) continue;
                    balloons.Add(new BalloonLayout
                    {
                        balloonId = bid++,
                        color = _balloonColors[c, r],
                        gridPosition = new Vector2(
                            _boardCenter.x + (c - (_gridCols - 1) * 0.5f) * spacing,
                            _boardCenter.y + (r - (_gridRows - 1) * 0.5f) * spacing),
                        gimmickType = _balloonGimmicks[c, r] > 0
                            ? GIMMICK_NAMES[_balloonGimmicks[c, r]] : ""
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
                    {
                        holderId = hid++,
                        color = _holderColors[c, r],
                        magazineCount = _holderMags[c, r],
                        position = new Vector2(c, r)
                    });
                }
            config.holders = holders.ToArray();
            config.rail = BuildRailLayout(spacing);

            config.gridCols = _gridCols;
            config.gridRows = _gridRows;

            var convPositions = new List<Vector2Int>();
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                    if (_conveyorTiles != null && c < _conveyorTiles.GetLength(0)
                        && r < _conveyorTiles.GetLength(1) && _conveyorTiles[c, r])
                        convPositions.Add(new Vector2Int(c, r));
            config.conveyorPositions = convPositions.ToArray();

            config.star1Threshold = config.balloonCount * 100;
            config.star2Threshold = Mathf.CeilToInt(config.star1Threshold * 1.5f);
            config.star3Threshold = Mathf.CeilToInt(config.star1Threshold * 2.2f);

            return config;
        }

        private RailLayout BuildRailLayout(float spacing)
        {
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

            int cols = _holderCols;
            var dp = new Vector3[cols];
            for (int i = 0; i < cols; i++)
            {
                float tv = (i + 1f) / (cols + 1f);
                dp[i] = new Vector3(Mathf.Lerp(l, r, tv), h, b);
            }

            return new RailLayout
            {
                waypoints = wp.ToArray(),
                slotCount = _railSlotCount,
                visualType = 0,
                deployPoints = dp
            };
        }

        private string[] CollectGimmicks()
        {
            var set = new HashSet<string>();
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                {
                    int g = _balloonGimmicks[c, r];
                    if (g > 0 && g < GIMMICK_NAMES.Length) set.Add(GIMMICK_NAMES[g]);
                }
            var arr = new string[set.Count];
            set.CopyTo(arr);
            return arr;
        }

        #endregion

        #region Counting

        private int CountBalloons()
        {
            int n = 0;
            for (int c = 0; c < _gridCols; c++)
                for (int r = 0; r < _gridRows; r++)
                    if (_balloonColors[c, r] >= 0) n++;
            return n;
        }

        private int CountHolders()
        {
            int n = 0;
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                    if (_holderColors[c, r] >= 0) n++;
            return n;
        }

        private int CountTotalMags()
        {
            int n = 0;
            for (int c = 0; c < _holderCols; c++)
                for (int r = 0; r < _holderRows; r++)
                    if (_holderColors[c, r] >= 0) n += _holderMags[c, r];
            return n;
        }

        #endregion
    }
}
#endif
