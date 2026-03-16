using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// Level Editor window for BalloonFlow.
    /// Provides visual editing of: rail layout, holder grid, balloon grid, colors.
    /// Saves to LevelDatabase ScriptableObject for runtime use.
    /// </summary>
    public class LevelEditorWindow : EditorWindow
    {
        #region Constants

        private static readonly Color[] PALETTE =
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

        private static readonly string[] COLOR_NAMES =
            { "Red", "Blue", "Green", "Yellow", "Purple", "Orange", "Cyan", "Pink" };

        private static readonly string[] GIMMICK_TYPES =
            { "(none)", "hidden", "chain", "frozen", "bomb", "spawner" };

        private static readonly string[] RAIL_DIRECTIONS =
            { "Clockwise", "Counter-Clockwise" };

        private const float CELL_SIZE = 32f;
        private const float CELL_GAP = 2f;

        #endregion

        #region State

        // --- Level metadata ---
        private int _levelId = 1;
        private int _numColors = 4;
        private string _difficultyPurpose = "normal";

        // --- Rail config ---
        private int _railDirection = 0; // 0=CW, 1=CCW
        private float _railPadding = 1.5f;
        private float _railHeight = 0.5f;
        private Vector2 _boardCenter = new Vector2(0f, 2f); // X, Z
        private int _railVisualType = 0; // 0=Cylinder, 1=Flat2D, 2=Custom3D
        private int _maxOnRail = 9;

        // --- Balloon grid ---
        private int _balloonGridCols = 5;
        private int _balloonGridRows = 5;
        private float _cellSpacing = 0.65f;
        private float _balloonScale = 0.5f;
        private int[,] _balloonColors;    // -1 = empty, 0-7 = color
        private int[,] _balloonGimmicks;  // 0 = none, 1+ = gimmick index

        // --- Holder grid ---
        private int _holderGridCols = 5;
        private int _holderGridRows = 5;
        private int[,] _holderColors;     // -1 = empty slot, 0-7 = color
        private int[,] _holderMagazines;  // dart count per holder

        // --- UI state ---
        private int _paintColor = 0;      // Current brush color
        private int _paintGimmick = 0;
        private int _defaultMagazine = 3;
        private Vector2 _scrollPos;
        private bool _foldRail = true;
        private bool _foldBalloon = true;
        private bool _foldHolder = true;
        private bool _foldExport = true;

        // --- Export ---
        private LevelDatabase _targetDatabase;

        #endregion

        #region Menu

        [MenuItem("BalloonFlow/Level Editor")]
        public static void Open()
        {
            var wnd = GetWindow<LevelEditorWindow>("Level Editor");
            wnd.minSize = new Vector2(520, 600);
            wnd.Show();
        }

        #endregion

        #region Lifecycle

        private void OnEnable()
        {
            InitGrids();
        }

        private void InitGrids()
        {
            if (_balloonColors == null || _balloonColors.GetLength(0) != _balloonGridCols || _balloonColors.GetLength(1) != _balloonGridRows)
            {
                var oldColors = _balloonColors;
                var oldGimmicks = _balloonGimmicks;
                _balloonColors = new int[_balloonGridCols, _balloonGridRows];
                _balloonGimmicks = new int[_balloonGridCols, _balloonGridRows];
                CopyGrid(oldColors, _balloonColors, -1);
                CopyGrid(oldGimmicks, _balloonGimmicks, 0);
            }

            if (_holderColors == null || _holderColors.GetLength(0) != _holderGridCols || _holderColors.GetLength(1) != _holderGridRows)
            {
                var oldColors = _holderColors;
                var oldMags = _holderMagazines;
                _holderColors = new int[_holderGridCols, _holderGridRows];
                _holderMagazines = new int[_holderGridCols, _holderGridRows];
                CopyGrid(oldColors, _holderColors, -1);
                CopyGrid(oldMags, _holderMagazines, _defaultMagazine);
            }
        }

        private void CopyGrid(int[,] src, int[,] dst, int defaultValue)
        {
            int cols = dst.GetLength(0);
            int rows = dst.GetLength(1);
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    if (src != null && c < src.GetLength(0) && r < src.GetLength(1))
                        dst[c, r] = src[c, r];
                    else
                        dst[c, r] = defaultValue;
                }
            }
        }

        #endregion

        #region GUI

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("BalloonFlow Level Editor", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // --- Level metadata ---
            _levelId = EditorGUILayout.IntField("Level ID", _levelId);
            _numColors = EditorGUILayout.IntSlider("Num Colors", _numColors, 2, 8);
            _difficultyPurpose = EditorGUILayout.TextField("Difficulty Purpose", _difficultyPurpose);

            EditorGUILayout.Space(8);

            // --- Color palette / brush ---
            DrawColorPalette();

            EditorGUILayout.Space(8);

            // --- Rail ---
            _foldRail = EditorGUILayout.Foldout(_foldRail, "Rail / Conveyor Settings", true);
            if (_foldRail) DrawRailSettings();

            EditorGUILayout.Space(8);

            // --- Balloon grid ---
            _foldBalloon = EditorGUILayout.Foldout(_foldBalloon, "Balloon Grid (Center)", true);
            if (_foldBalloon) DrawBalloonGrid();

            EditorGUILayout.Space(8);

            // --- Holder grid ---
            _foldHolder = EditorGUILayout.Foldout(_foldHolder, "Holder Grid (Waiting Area)", true);
            if (_foldHolder) DrawHolderGrid();

            EditorGUILayout.Space(8);

            // --- Export ---
            _foldExport = EditorGUILayout.Foldout(_foldExport, "Export / Save", true);
            if (_foldExport) DrawExportSection();

            EditorGUILayout.EndScrollView();
        }

        #endregion

        #region Draw — Color Palette

        private void DrawColorPalette()
        {
            EditorGUILayout.LabelField("Paint Brush", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < _numColors; i++)
            {
                Color old = GUI.backgroundColor;
                GUI.backgroundColor = i == _paintColor ? Color.white : PALETTE[i];
                string label = i == _paintColor ? $"[{COLOR_NAMES[i]}]" : COLOR_NAMES[i];
                if (GUILayout.Button(label, GUILayout.Width(60), GUILayout.Height(28)))
                {
                    _paintColor = i;
                }
                GUI.backgroundColor = old;
            }

            // Empty Cell tool (removes balloon, creates gap in grid)
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = _paintColor == -1 ? Color.white : new Color(0.2f, 0.2f, 0.2f);
            if (GUILayout.Button(_paintColor == -1 ? "[Empty]" : "Empty", GUILayout.Width(60), GUILayout.Height(28)))
            {
                _paintColor = -1;
            }
            GUI.backgroundColor = oldBg;

            EditorGUILayout.EndHorizontal();

            // Gimmick selector
            _paintGimmick = EditorGUILayout.Popup("Balloon Gimmick", _paintGimmick, GIMMICK_TYPES);
        }

        #endregion

        #region Draw — Rail Settings

        private static readonly string[] RAIL_VISUAL_TYPES =
            { "Cylinder (3D)", "Flat 2D (Quad)", "Custom 3D (Prefab)" };

        private void DrawRailSettings()
        {
            EditorGUI.indentLevel++;
            _railDirection = EditorGUILayout.Popup("Direction", _railDirection, RAIL_DIRECTIONS);
            _railVisualType = EditorGUILayout.Popup("Visual Type", _railVisualType, RAIL_VISUAL_TYPES);
            _maxOnRail = EditorGUILayout.IntSlider("Max Holders on Rail", _maxOnRail, 1, 15);
            _boardCenter = EditorGUILayout.Vector2Field("Board Center (X, Z)", _boardCenter);
            _railPadding = EditorGUILayout.FloatField("Rail Padding", _railPadding);
            _railHeight = EditorGUILayout.FloatField("Rail Height (Y)", _railHeight);
            EditorGUI.indentLevel--;
        }

        #endregion

        #region Draw — Balloon Grid

        private void DrawBalloonGrid()
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.BeginHorizontal();
            int newCols = EditorGUILayout.IntSlider("Columns", _balloonGridCols, 2, 10);
            int newRows = EditorGUILayout.IntSlider("Rows", _balloonGridRows, 2, 10);
            EditorGUILayout.EndHorizontal();

            if (newCols != _balloonGridCols || newRows != _balloonGridRows)
            {
                _balloonGridCols = newCols;
                _balloonGridRows = newRows;
                InitGrids();
            }

            _cellSpacing = EditorGUILayout.FloatField("Cell Spacing", _cellSpacing);
            _balloonScale = EditorGUILayout.Slider("Balloon Scale", _balloonScale, 0.2f, 1.0f);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Click cells to paint color. Empty (✕) = no balloon (gap in grid).");

            // Draw grid (row 0 = south/bottom, displayed at bottom)
            for (int r = _balloonGridRows - 1; r >= 0; r--)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(20);
                EditorGUILayout.LabelField($"R{r + 1}", GUILayout.Width(28));
                for (int c = 0; c < _balloonGridCols; c++)
                {
                    DrawBalloonCell(c, r);
                }
                EditorGUILayout.EndHorizontal();
            }

            // Column labels
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(50);
            for (int c = 0; c < _balloonGridCols; c++)
            {
                EditorGUILayout.LabelField($"C{c + 1}", GUILayout.Width(CELL_SIZE + CELL_GAP));
            }
            EditorGUILayout.EndHorizontal();

            // Fill buttons
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Fill All", GUILayout.Width(80)))
            {
                FillBalloonGrid(_paintColor, _paintGimmick);
            }
            if (GUILayout.Button("Clear All", GUILayout.Width(80)))
            {
                FillBalloonGrid(-1, 0);
            }
            if (GUILayout.Button("Random Fill", GUILayout.Width(100)))
            {
                RandomFillBalloonGrid();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }

        private void DrawBalloonCell(int col, int row)
        {
            int colorIdx = _balloonColors[col, row];
            int gimmickIdx = _balloonGimmicks[col, row];

            Color bgColor = colorIdx >= 0 && colorIdx < PALETTE.Length ? PALETTE[colorIdx] : new Color(0.15f, 0.15f, 0.15f);
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = bgColor;

            string label = colorIdx < 0 ? "✕" : (gimmickIdx > 0 ? GIMMICK_TYPES[gimmickIdx][0].ToString().ToUpper() : "●");

            if (GUILayout.Button(label, GUILayout.Width(CELL_SIZE), GUILayout.Height(CELL_SIZE)))
            {
                _balloonColors[col, row] = _paintColor;
                _balloonGimmicks[col, row] = _paintColor >= 0 ? _paintGimmick : 0;
            }

            GUI.backgroundColor = old;
        }

        private void FillBalloonGrid(int color, int gimmick)
        {
            for (int c = 0; c < _balloonGridCols; c++)
            {
                for (int r = 0; r < _balloonGridRows; r++)
                {
                    _balloonColors[c, r] = color;
                    _balloonGimmicks[c, r] = gimmick;
                }
            }
        }

        private void RandomFillBalloonGrid()
        {
            for (int c = 0; c < _balloonGridCols; c++)
            {
                for (int r = 0; r < _balloonGridRows; r++)
                {
                    _balloonColors[c, r] = Random.Range(0, _numColors);
                    _balloonGimmicks[c, r] = 0;
                }
            }
        }

        #endregion

        #region Draw — Holder Grid

        private void DrawHolderGrid()
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.BeginHorizontal();
            int newCols = EditorGUILayout.IntSlider("Columns", _holderGridCols, 1, 8);
            int newRows = EditorGUILayout.IntSlider("Rows", _holderGridRows, 1, 8);
            EditorGUILayout.EndHorizontal();

            if (newCols != _holderGridCols || newRows != _holderGridRows)
            {
                _holderGridCols = newCols;
                _holderGridRows = newRows;
                InitGrids();
            }

            _defaultMagazine = EditorGUILayout.IntSlider("Default Magazine", _defaultMagazine, 1, 20);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Click to set holder color. Eraser = empty slot. Row 1 (top) = front row (clickable).");

            // Draw grid (row 0 = front, displayed at top)
            for (int r = 0; r < _holderGridRows; r++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(20);
                string rowLabel = r == 0 ? "★" : $"R{r + 1}";
                EditorGUILayout.LabelField(rowLabel, GUILayout.Width(28));
                for (int c = 0; c < _holderGridCols; c++)
                {
                    DrawHolderCell(c, r);
                }
                EditorGUILayout.EndHorizontal();
            }

            // Magazine editor per holder (inline)
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Magazine Counts (right-click a holder to edit, or set default above)");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Fill All", GUILayout.Width(80)))
            {
                FillHolderGrid(_paintColor);
            }
            if (GUILayout.Button("Clear All", GUILayout.Width(80)))
            {
                FillHolderGrid(-1);
            }
            if (GUILayout.Button("Random Fill", GUILayout.Width(100)))
            {
                RandomFillHolderGrid();
            }
            if (GUILayout.Button("Set All Magazine", GUILayout.Width(120)))
            {
                SetAllHolderMagazines(_defaultMagazine);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }

        private void DrawHolderCell(int col, int row)
        {
            int colorIdx = _holderColors[col, row];
            int mag = _holderMagazines[col, row];

            Color bgColor = colorIdx >= 0 && colorIdx < PALETTE.Length ? PALETTE[colorIdx] : new Color(0.25f, 0.25f, 0.25f);
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = bgColor;

            string label = colorIdx < 0 ? "·" : mag.ToString();

            // Left-click = paint color, Right-click = cycle magazine
            Rect rect = GUILayoutUtility.GetRect(CELL_SIZE, CELL_SIZE, GUILayout.Width(CELL_SIZE));
            if (GUI.Button(rect, label))
            {
                if (Event.current.button == 1) // Right-click
                {
                    _holderMagazines[col, row] = (_holderMagazines[col, row] % 20) + 1;
                }
                else // Left-click
                {
                    _holderColors[col, row] = _paintColor;
                    if (_paintColor >= 0)
                        _holderMagazines[col, row] = _defaultMagazine;
                }
            }

            GUI.backgroundColor = old;
        }

        private void FillHolderGrid(int color)
        {
            for (int c = 0; c < _holderGridCols; c++)
            {
                for (int r = 0; r < _holderGridRows; r++)
                {
                    _holderColors[c, r] = color;
                    _holderMagazines[c, r] = color >= 0 ? _defaultMagazine : 0;
                }
            }
        }

        private void RandomFillHolderGrid()
        {
            for (int c = 0; c < _holderGridCols; c++)
            {
                for (int r = 0; r < _holderGridRows; r++)
                {
                    _holderColors[c, r] = Random.Range(0, _numColors);
                    _holderMagazines[c, r] = _defaultMagazine;
                }
            }
        }

        private void SetAllHolderMagazines(int mag)
        {
            for (int c = 0; c < _holderGridCols; c++)
            {
                for (int r = 0; r < _holderGridRows; r++)
                {
                    if (_holderColors[c, r] >= 0)
                        _holderMagazines[c, r] = mag;
                }
            }
        }

        #endregion

        #region Draw — Export

        private void DrawExportSection()
        {
            EditorGUI.indentLevel++;

            _targetDatabase = (LevelDatabase)EditorGUILayout.ObjectField(
                "Target LevelDatabase", _targetDatabase, typeof(LevelDatabase), false);

            EditorGUILayout.Space(4);

            // Summary
            int balloonCount = CountBalloons();
            int holderCount = CountHolders();
            int totalMagazine = CountTotalMagazine();
            EditorGUILayout.HelpBox(
                $"Balloons: {balloonCount} | Holders: {holderCount} | Total Darts: {totalMagazine}\n" +
                $"Grid: {_balloonGridCols}×{_balloonGridRows} | Holder Grid: {_holderGridCols}×{_holderGridRows}\n" +
                $"Rail: {RAIL_DIRECTIONS[_railDirection]} | Colors: {_numColors}",
                totalMagazine >= balloonCount ? MessageType.Info : MessageType.Warning);

            if (totalMagazine < balloonCount)
            {
                EditorGUILayout.HelpBox(
                    $"Not enough darts ({totalMagazine}) to pop all balloons ({balloonCount}). Level may be unsolvable!",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Save to Database", GUILayout.Height(30)))
            {
                SaveToDatabase();
            }

            if (GUILayout.Button("Export JSON", GUILayout.Height(30)))
            {
                ExportJson();
            }

            if (GUILayout.Button("Play Test", GUILayout.Height(30)))
            {
                PlayTest();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }

        #endregion

        #region Build LevelConfig

        private LevelConfig BuildLevelConfig()
        {
            var config = new LevelConfig();
            config.levelId = _levelId;
            config.packageId = 1;
            config.positionInPackage = _levelId;
            config.numColors = _numColors;
            config.difficultyPurpose = _difficultyPurpose;
            config.gimmickTypes = CollectGimmickTypes();
            config.balloonScale = _balloonScale;

            // Build balloons
            var balloons = new List<BalloonLayout>();
            int balloonIdCounter = 0;
            for (int c = 0; c < _balloonGridCols; c++)
            {
                for (int r = 0; r < _balloonGridRows; r++)
                {
                    if (_balloonColors[c, r] < 0) continue;

                    balloons.Add(new BalloonLayout
                    {
                        balloonId = balloonIdCounter++,
                        color = _balloonColors[c, r],
                        gridPosition = new Vector2(
                            _boardCenter.x + (c - (_balloonGridCols - 1) * 0.5f) * _cellSpacing,
                            _boardCenter.y + (r - (_balloonGridRows - 1) * 0.5f) * _cellSpacing),
                        gimmickType = _balloonGimmicks[c, r] > 0 ? GIMMICK_TYPES[_balloonGimmicks[c, r]] : ""
                    });
                }
            }
            config.balloons = balloons.ToArray();
            config.balloonCount = balloons.Count;

            // Build holders
            var holders = new List<HolderSetup>();
            int holderIdCounter = 0;
            for (int r = 0; r < _holderGridRows; r++)
            {
                for (int c = 0; c < _holderGridCols; c++)
                {
                    if (_holderColors[c, r] < 0) continue;

                    holders.Add(new HolderSetup
                    {
                        holderId = holderIdCounter++,
                        color = _holderColors[c, r],
                        magazineCount = _holderMagazines[c, r],
                        position = new Vector2(c, r) // grid coords, runtime converts to world
                    });
                }
            }
            config.holders = holders.ToArray();

            // Build rail
            config.rail = BuildRailLayout();

            // Star thresholds
            config.star1Threshold = config.balloonCount * 100;
            config.star2Threshold = Mathf.CeilToInt(config.star1Threshold * 1.5f);
            config.star3Threshold = Mathf.CeilToInt(config.star1Threshold * 2.2f);

            return config;
        }

        private RailLayout BuildRailLayout()
        {
            float halfGridX = (_balloonGridCols - 1) * _cellSpacing * 0.5f;
            float halfGridZ = (_balloonGridRows - 1) * _cellSpacing * 0.5f;

            float left = _boardCenter.x - halfGridX - _railPadding;
            float right = _boardCenter.x + halfGridX + _railPadding;
            float bottom = _boardCenter.y - halfGridZ - _railPadding;
            float top = _boardCenter.y + halfGridZ + _railPadding;

            var waypoints = new List<Vector3>();

            if (_railDirection == 0) // Clockwise
            {
                // South (left→right)
                waypoints.Add(new Vector3(left, _railHeight, bottom));
                waypoints.Add(new Vector3(Mathf.Lerp(left, right, 0.33f), _railHeight, bottom));
                waypoints.Add(new Vector3(Mathf.Lerp(left, right, 0.67f), _railHeight, bottom));
                // East (bottom→top)
                waypoints.Add(new Vector3(right, _railHeight, bottom));
                waypoints.Add(new Vector3(right, _railHeight, Mathf.Lerp(bottom, top, 0.33f)));
                waypoints.Add(new Vector3(right, _railHeight, Mathf.Lerp(bottom, top, 0.67f)));
                // North (right→left)
                waypoints.Add(new Vector3(right, _railHeight, top));
                waypoints.Add(new Vector3(Mathf.Lerp(right, left, 0.33f), _railHeight, top));
                waypoints.Add(new Vector3(Mathf.Lerp(right, left, 0.67f), _railHeight, top));
                // West (top→bottom)
                waypoints.Add(new Vector3(left, _railHeight, top));
                waypoints.Add(new Vector3(left, _railHeight, Mathf.Lerp(top, bottom, 0.33f)));
                waypoints.Add(new Vector3(left, _railHeight, Mathf.Lerp(top, bottom, 0.67f)));
            }
            else // Counter-clockwise
            {
                // South (right→left)
                waypoints.Add(new Vector3(right, _railHeight, bottom));
                waypoints.Add(new Vector3(Mathf.Lerp(right, left, 0.33f), _railHeight, bottom));
                waypoints.Add(new Vector3(Mathf.Lerp(right, left, 0.67f), _railHeight, bottom));
                // West (bottom→top)
                waypoints.Add(new Vector3(left, _railHeight, bottom));
                waypoints.Add(new Vector3(left, _railHeight, Mathf.Lerp(bottom, top, 0.33f)));
                waypoints.Add(new Vector3(left, _railHeight, Mathf.Lerp(bottom, top, 0.67f)));
                // North (left→right)
                waypoints.Add(new Vector3(left, _railHeight, top));
                waypoints.Add(new Vector3(Mathf.Lerp(left, right, 0.33f), _railHeight, top));
                waypoints.Add(new Vector3(Mathf.Lerp(left, right, 0.67f), _railHeight, top));
                // East (top→bottom)
                waypoints.Add(new Vector3(right, _railHeight, top));
                waypoints.Add(new Vector3(right, _railHeight, Mathf.Lerp(top, bottom, 0.33f)));
                waypoints.Add(new Vector3(right, _railHeight, Mathf.Lerp(top, bottom, 0.67f)));
            }

            // Holder queue positions along near edge
            int slotCount = _holderGridCols;
            var holderPositions = new Vector3[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                float t = (i + 1f) / (slotCount + 1f);
                holderPositions[i] = new Vector3(
                    Mathf.Lerp(left, right, t),
                    _railHeight,
                    bottom);
            }

            return new RailLayout
            {
                waypoints = waypoints.ToArray(),
                holderPositions = holderPositions,
                visualType = _railVisualType,
                maxOnRail = _maxOnRail
            };
        }

        private string[] CollectGimmickTypes()
        {
            var set = new HashSet<string>();
            for (int c = 0; c < _balloonGridCols; c++)
            {
                for (int r = 0; r < _balloonGridRows; r++)
                {
                    int g = _balloonGimmicks[c, r];
                    if (g > 0 && g < GIMMICK_TYPES.Length)
                        set.Add(GIMMICK_TYPES[g]);
                }
            }
            var arr = new string[set.Count];
            set.CopyTo(arr);
            return arr;
        }

        #endregion

        #region Actions

        private void SaveToDatabase()
        {
            if (_targetDatabase == null)
            {
                // Try to find or create one
                string path = "Assets/Resources/LevelDatabase.asset";
                _targetDatabase = AssetDatabase.LoadAssetAtPath<LevelDatabase>(path);
                if (_targetDatabase == null)
                {
                    _targetDatabase = ScriptableObject.CreateInstance<LevelDatabase>();
                    AssetDatabase.CreateAsset(_targetDatabase, path);
                    Debug.Log($"[LevelEditor] Created new LevelDatabase at {path}");
                }
            }

            LevelConfig config = BuildLevelConfig();

            // Expand or replace level in database
            var levels = _targetDatabase.levels != null
                ? new List<LevelConfig>(_targetDatabase.levels)
                : new List<LevelConfig>();

            int idx = levels.FindIndex(l => l.levelId == config.levelId);
            if (idx >= 0)
                levels[idx] = config;
            else
                levels.Add(config);

            // Sort by levelId
            levels.Sort((a, b) => a.levelId.CompareTo(b.levelId));
            _targetDatabase.levels = levels.ToArray();

            EditorUtility.SetDirty(_targetDatabase);
            AssetDatabase.SaveAssets();

            Debug.Log($"[LevelEditor] Saved level {config.levelId} to database. " +
                      $"Balloons={config.balloonCount}, Holders={config.holders.Length}");
        }

        private void ExportJson()
        {
            LevelConfig config = BuildLevelConfig();
            string json = JsonUtility.ToJson(config, true);
            string path = EditorUtility.SaveFilePanel("Export Level JSON", "Assets", $"level_{config.levelId}", "json");
            if (!string.IsNullOrEmpty(path))
            {
                System.IO.File.WriteAllText(path, json);
                Debug.Log($"[LevelEditor] Exported level {config.levelId} to {path}");
            }
        }

        private void PlayTest()
        {
            LevelConfig config = BuildLevelConfig();

            // Store config temporarily for runtime pickup
            string json = JsonUtility.ToJson(config);
            EditorPrefs.SetString("BalloonFlow_TestLevel", json);
            EditorPrefs.SetBool("BalloonFlow_UseTestLevel", true);

            Debug.Log($"[LevelEditor] Test level stored. Press Play in Unity to test. " +
                      $"Balloons={config.balloonCount}, Holders={config.holders.Length}");

            if (!EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = true;
            }
        }

        #endregion

        #region Helpers

        private int CountBalloons()
        {
            int count = 0;
            for (int c = 0; c < _balloonGridCols; c++)
                for (int r = 0; r < _balloonGridRows; r++)
                    if (_balloonColors[c, r] >= 0) count++;
            return count;
        }

        private int CountHolders()
        {
            int count = 0;
            for (int c = 0; c < _holderGridCols; c++)
                for (int r = 0; r < _holderGridRows; r++)
                    if (_holderColors[c, r] >= 0) count++;
            return count;
        }

        private int CountTotalMagazine()
        {
            int total = 0;
            for (int c = 0; c < _holderGridCols; c++)
                for (int r = 0; r < _holderGridRows; r++)
                    if (_holderColors[c, r] >= 0) total += _holderMagazines[c, r];
            return total;
        }

        private static void SetCenter(GameObject go, Vector2 pos, Vector2 size)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        #endregion
    }
}
