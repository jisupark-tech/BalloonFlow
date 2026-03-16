using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// Level Editor for BalloonFlow.
    /// EditorWindow: 설정 패널 (색상, 그리드 크기, 레일, 홀더, 익스포트)
    /// Scene View: 풍선 맵을 클릭/드래그로 직접 페인팅
    /// </summary>
    public class LevelEditorWindow : EditorWindow
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

        private static readonly string[] COLOR_NAMES =
            { "Red", "Blue", "Green", "Yellow", "Purple", "Orange", "Cyan", "Pink",
              "Crimson", "Navy", "Lime", "Gold", "Violet", "Amber", "Teal", "Rose",
              "Coral", "Indigo", "Mint", "Peach", "Magenta", "Olive", "Sky", "Salmon",
              "Maroon", "Forest", "Lavender", "Tan" };

        private static readonly string[] GIMMICK_TYPES =
            { "(none)", "Hidden", "Chain", "Pinata", "Spawner_T", "Pin", "Lock_Key",
              "Surprise", "Wall", "Spawner_O", "Pinata_Box", "Ice", "Frozen_Dart", "Color_Curtain" };

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
        private int _railDirection = 0;
        private float _railPadding = 1.5f;
        private float _railHeight = 0.5f;
        private Vector2 _boardCenter = new Vector2(0f, 2f);
        private int _railVisualType = 0;
        private int _maxOnRail = 9;

        // --- Balloon grid ---
        private int _balloonGridCols = 5;
        private int _balloonGridRows = 5;
        private float _boardWorldSize = 8f; // 보드 고정 크기 (월드 유닛)
        private int[,] _balloonColors;
        private int[,] _balloonGimmicks;

        // 자동 계산 — 보드 크기 고정, 그리드 수에 따라 풍선 크기 조절
        private float _cellSpacing => _boardWorldSize / Mathf.Max(_balloonGridCols, _balloonGridRows);
        private float _balloonScale => _cellSpacing * 0.9f;

        // --- Holder grid ---
        private int _holderGridCols = 5;
        private int _holderGridRows = 5;
        private int[,] _holderColors;
        private int[,] _holderMagazines;

        // --- UI state ---
        private int _paintColor = 0;
        private int _paintGimmick = 0;
        private int _defaultMagazine = 3;
        private Vector2 _scrollPos;
        private bool _foldRail = true;
        private bool _foldBalloon = true;
        private bool _foldHolder = true;
        private bool _foldExport = true;

        // --- Scene paint state ---
        private bool _paintMode = true;
        private int _hoverCol = -1;
        private int _hoverRow = -1;

        // --- Export ---
        private LevelDatabase _targetDatabase;

        #endregion

        #region Menu

        private const string MAPMAKER_SCENE_PATH = "Assets/0.Scenes/MapMaker.unity";

        [MenuItem("BalloonFlow/Level Editor")]
        public static void Open()
        {
            // MapMaker 씬이 아니면 자동 전환
            OpenMapMakerScene();

            var wnd = GetWindow<LevelEditorWindow>("Level Editor");
            wnd.minSize = new Vector2(420, 600);
            wnd.Show();

            // Scene View를 탑뷰로 맞춤
            EditorApplication.delayCall += AlignSceneViewTopDown;
        }

        private static void OpenMapMakerScene()
        {
            var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (currentScene.path == MAPMAKER_SCENE_PATH) return;

            if (!System.IO.File.Exists(MAPMAKER_SCENE_PATH))
            {
                Debug.LogWarning($"[LevelEditor] MapMaker 씬이 없습니다: {MAPMAKER_SCENE_PATH}\n" +
                                 "SceneBuilder를 먼저 실행하세요. (EditorPrefs에서 BalloonFlow_SceneBuilt 키 삭제 후 Unity 재시작)");
                return;
            }

            if (currentScene.isDirty)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
            }

            EditorSceneManager.OpenScene(MAPMAKER_SCENE_PATH, OpenSceneMode.Single);
        }

        private static void AlignSceneViewTopDown()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;

            sv.in2DMode = false;
            sv.rotation = Quaternion.Euler(90f, 0f, 0f);
            sv.pivot = new Vector3(0f, 0f, 2f);
            sv.size = 8f;
            sv.orthographic = true;
            sv.Repaint();
        }

        #endregion

        #region Lifecycle

        private void OnEnable()
        {
            InitGrids();
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
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

        #region GUI — EditorWindow (설정 패널)

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("BalloonFlow Level Editor", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // --- Level metadata ---
            _levelId = EditorGUILayout.IntField("Level ID", _levelId);
            _numColors = EditorGUILayout.IntSlider("Num Colors", _numColors, 2, 11);
            _difficultyPurpose = EditorGUILayout.TextField("Difficulty Purpose", _difficultyPurpose);

            EditorGUILayout.Space(8);

            // --- Color palette / brush ---
            DrawColorPalette();

            EditorGUILayout.Space(8);

            // --- Rail ---
            _foldRail = EditorGUILayout.Foldout(_foldRail, "Rail / Conveyor Settings", true);
            if (_foldRail) DrawRailSettings();

            EditorGUILayout.Space(8);

            // --- Balloon grid (설정만, 페인팅은 Scene View) ---
            _foldBalloon = EditorGUILayout.Foldout(_foldBalloon, "Balloon Grid (Scene View Paint)", true);
            if (_foldBalloon) DrawBalloonGridSettings();

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

            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = _paintColor == -1 ? Color.white : new Color(0.2f, 0.2f, 0.2f);
            if (GUILayout.Button(_paintColor == -1 ? "[Erase]" : "Erase", GUILayout.Width(60), GUILayout.Height(28)))
            {
                _paintColor = -1;
            }
            GUI.backgroundColor = oldBg;

            EditorGUILayout.EndHorizontal();

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

        #region Draw — Balloon Grid Settings (GUI는 설정만, 페인팅은 Scene View)

        private void DrawBalloonGridSettings()
        {
            EditorGUI.indentLevel++;

            // 그리드 크기 (최대 100)
            int newCols = EditorGUILayout.IntSlider("Columns", _balloonGridCols, 2, 100);
            int newRows = EditorGUILayout.IntSlider("Rows", _balloonGridRows, 2, 100);

            if (newCols != _balloonGridCols || newRows != _balloonGridRows)
            {
                _balloonGridCols = newCols;
                _balloonGridRows = newRows;
                InitGrids();
                SceneView.RepaintAll();
            }

            // 보드 고정 크기 — 그리드 수 변해도 판 크기는 동일
            _boardWorldSize = EditorGUILayout.Slider("Board Size (World)", _boardWorldSize, 4f, 20f);

            // 자동 계산값 표시 (읽기 전용)
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.FloatField("Cell Spacing (auto)", _cellSpacing);
            EditorGUILayout.FloatField("Balloon Scale (auto)", _balloonScale);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(4);

            // Paint Mode 토글
            _paintMode = EditorGUILayout.Toggle("Paint Mode (Scene)", _paintMode);

            // 상태 표시
            int balloonCount = CountBalloons();
            EditorGUILayout.HelpBox(
                $"Scene View에서 클릭/드래그로 풍선 배치\n" +
                $"그리드: {_balloonGridCols} x {_balloonGridRows} | 배치된 풍선: {balloonCount}\n" +
                $"키보드: 1~8 = 색상, 0 = 지우개, Space = Paint 토글",
                MessageType.Info);

            // Fill / Clear 버튼
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Fill All", GUILayout.Width(80)))
            {
                FillBalloonGrid(_paintColor, _paintGimmick);
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("Clear All", GUILayout.Width(80)))
            {
                FillBalloonGrid(-1, 0);
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("Random Fill", GUILayout.Width(100)))
            {
                RandomFillBalloonGrid();
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
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
            EditorGUILayout.LabelField("Click to set holder color. Eraser = empty slot.");

            for (int r = 0; r < _holderGridRows; r++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(20);
                string rowLabel = r == 0 ? "F" : $"R{r + 1}";
                EditorGUILayout.LabelField(rowLabel, GUILayout.Width(28));
                for (int c = 0; c < _holderGridCols; c++)
                {
                    DrawHolderCell(c, r);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Fill All", GUILayout.Width(80)))
                FillHolderGrid(_paintColor);
            if (GUILayout.Button("Clear All", GUILayout.Width(80)))
                FillHolderGrid(-1);
            if (GUILayout.Button("Random Fill", GUILayout.Width(100)))
                RandomFillHolderGrid();
            if (GUILayout.Button("Set All Mag", GUILayout.Width(100)))
                SetAllHolderMagazines(_defaultMagazine);
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

            string label = colorIdx < 0 ? "." : mag.ToString();

            Rect rect = GUILayoutUtility.GetRect(CELL_SIZE, CELL_SIZE, GUILayout.Width(CELL_SIZE));
            if (GUI.Button(rect, label))
            {
                if (Event.current.button == 1)
                {
                    _holderMagazines[col, row] = (_holderMagazines[col, row] % 20) + 1;
                }
                else
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
                for (int r = 0; r < _holderGridRows; r++)
                {
                    _holderColors[c, r] = color;
                    _holderMagazines[c, r] = color >= 0 ? _defaultMagazine : 0;
                }
        }

        private void RandomFillHolderGrid()
        {
            for (int c = 0; c < _holderGridCols; c++)
                for (int r = 0; r < _holderGridRows; r++)
                {
                    _holderColors[c, r] = Random.Range(0, _numColors);
                    _holderMagazines[c, r] = _defaultMagazine;
                }
        }

        private void SetAllHolderMagazines(int mag)
        {
            for (int c = 0; c < _holderGridCols; c++)
                for (int r = 0; r < _holderGridRows; r++)
                    if (_holderColors[c, r] >= 0)
                        _holderMagazines[c, r] = mag;
        }

        #endregion

        #region Scene View — 메인 콜백

        private void OnSceneGUI(SceneView sceneView)
        {
            if (_balloonColors == null) return;

            Event e = Event.current;

            // 키보드 단축키 (Scene 포커스 상태에서)
            HandleSceneKeyboard(e);

            // 보드 그리드 그리기
            DrawSceneBoardGrid();

            // 레일 경계 표시
            DrawSceneRailBounds();

            // Paint Mode: 마우스 입력 처리
            if (_paintMode)
            {
                HandleScenePaint(e);
            }

            // Scene 오버레이 UI (상단 툴바)
            DrawSceneOverlay();
        }

        #endregion

        #region Scene View — 보드 그리드 그리기

        private void DrawSceneBoardGrid()
        {
            float halfCols = (_balloonGridCols - 1) * _cellSpacing * 0.5f;
            float halfRows = (_balloonGridRows - 1) * _cellSpacing * 0.5f;
            float radius = _cellSpacing * 0.45f;
            float y = 0.5f;

            // 보드 경계 사각형
            Vector3 bl = new Vector3(_boardCenter.x - halfCols - _cellSpacing * 0.5f, y, _boardCenter.y - halfRows - _cellSpacing * 0.5f);
            Vector3 br = new Vector3(_boardCenter.x + halfCols + _cellSpacing * 0.5f, y, _boardCenter.y - halfRows - _cellSpacing * 0.5f);
            Vector3 tr = new Vector3(_boardCenter.x + halfCols + _cellSpacing * 0.5f, y, _boardCenter.y + halfRows + _cellSpacing * 0.5f);
            Vector3 tl = new Vector3(_boardCenter.x - halfCols - _cellSpacing * 0.5f, y, _boardCenter.y + halfRows + _cellSpacing * 0.5f);

            Handles.color = new Color(1f, 1f, 1f, 0.4f);
            Handles.DrawLine(bl, br);
            Handles.DrawLine(br, tr);
            Handles.DrawLine(tr, tl);
            Handles.DrawLine(tl, bl);

            // 그리드 라인 (30칸 이하일 때만)
            if (_balloonGridCols <= 30 && _balloonGridRows <= 30)
            {
                Handles.color = new Color(1f, 1f, 1f, 0.08f);
                for (int c = 0; c <= _balloonGridCols; c++)
                {
                    float x = _boardCenter.x + (c - _balloonGridCols * 0.5f) * _cellSpacing;
                    Handles.DrawLine(new Vector3(x, y, bl.z), new Vector3(x, y, tl.z));
                }
                for (int r = 0; r <= _balloonGridRows; r++)
                {
                    float z = _boardCenter.y + (r - _balloonGridRows * 0.5f) * _cellSpacing;
                    Handles.DrawLine(new Vector3(bl.x, y, z), new Vector3(br.x, y, z));
                }
            }

            // 채워진 셀만 그리기 (빈 셀은 생략 — 대형 그리드 성능)
            for (int c = 0; c < _balloonGridCols; c++)
            {
                for (int r = 0; r < _balloonGridRows; r++)
                {
                    int colorIdx = _balloonColors[c, r];
                    if (colorIdx < 0) continue;

                    float wx = _boardCenter.x + (c - (_balloonGridCols - 1) * 0.5f) * _cellSpacing;
                    float wz = _boardCenter.y + (r - (_balloonGridRows - 1) * 0.5f) * _cellSpacing;
                    Vector3 pos = new Vector3(wx, y, wz);

                    Handles.color = PALETTE[Mathf.Clamp(colorIdx, 0, PALETTE.Length - 1)];
                    Handles.DrawSolidDisc(pos, Vector3.up, radius);

                    // 기믹 표시 (글자)
                    int gIdx = _balloonGimmicks[c, r];
                    if (gIdx > 0 && gIdx < GIMMICK_TYPES.Length)
                    {
                        Handles.color = Color.white;
                        Handles.Label(pos + Vector3.up * 0.01f, GIMMICK_TYPES[gIdx][0].ToString().ToUpper(),
                            new GUIStyle { fontSize = 10, normal = { textColor = Color.white }, alignment = TextAnchor.MiddleCenter });
                    }
                }
            }

            // 호버 커서 표시
            if (_paintMode && _hoverCol >= 0 && _hoverCol < _balloonGridCols && _hoverRow >= 0 && _hoverRow < _balloonGridRows)
            {
                float cx = _boardCenter.x + (_hoverCol - (_balloonGridCols - 1) * 0.5f) * _cellSpacing;
                float cz = _boardCenter.y + (_hoverRow - (_balloonGridRows - 1) * 0.5f) * _cellSpacing;
                Vector3 cursorPos = new Vector3(cx, y + 0.01f, cz);

                // 브러시 색상 미리보기
                if (_paintColor >= 0 && _paintColor < PALETTE.Length)
                {
                    Color preview = PALETTE[_paintColor];
                    preview.a = 0.4f;
                    Handles.color = preview;
                    Handles.DrawSolidDisc(cursorPos, Vector3.up, radius);
                }

                Handles.color = Color.white;
                Handles.DrawWireDisc(cursorPos, Vector3.up, radius);
            }
        }

        #endregion

        #region Scene View — 레일 경계 표시

        private void DrawSceneRailBounds()
        {
            float halfCols = (_balloonGridCols - 1) * _cellSpacing * 0.5f;
            float halfRows = (_balloonGridRows - 1) * _cellSpacing * 0.5f;

            float left   = _boardCenter.x - halfCols - _railPadding;
            float right  = _boardCenter.x + halfCols + _railPadding;
            float bottom = _boardCenter.y - halfRows - _railPadding;
            float top    = _boardCenter.y + halfRows + _railPadding;

            Handles.color = new Color(0.3f, 0.8f, 1f, 0.35f);
            Handles.DrawLine(new Vector3(left,  _railHeight, bottom), new Vector3(right, _railHeight, bottom));
            Handles.DrawLine(new Vector3(right, _railHeight, bottom), new Vector3(right, _railHeight, top));
            Handles.DrawLine(new Vector3(right, _railHeight, top),    new Vector3(left,  _railHeight, top));
            Handles.DrawLine(new Vector3(left,  _railHeight, top),    new Vector3(left,  _railHeight, bottom));
        }

        #endregion

        #region Scene View — 마우스 페인팅

        private void HandleScenePaint(Event e)
        {
            // Paint Mode일 때 씬 회전/선택 방지
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            // 마우스 → 월드 좌표 → 그리드 좌표 변환
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (Mathf.Abs(ray.direction.y) < 0.001f)
            {
                _hoverCol = -1;
                _hoverRow = -1;
                return;
            }

            float t = (0.5f - ray.origin.y) / ray.direction.y;
            if (t <= 0f)
            {
                _hoverCol = -1;
                _hoverRow = -1;
                return;
            }

            Vector3 hit = ray.origin + ray.direction * t;
            float relC = (hit.x - _boardCenter.x) / _cellSpacing + (_balloonGridCols - 1) * 0.5f;
            float relR = (hit.z - _boardCenter.y) / _cellSpacing + (_balloonGridRows - 1) * 0.5f;
            int col = Mathf.RoundToInt(relC);
            int row = Mathf.RoundToInt(relR);

            _hoverCol = col;
            _hoverRow = row;

            // 클릭 또는 드래그로 페인팅
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0)
            {
                if (col >= 0 && col < _balloonGridCols && row >= 0 && row < _balloonGridRows)
                {
                    _balloonColors[col, row] = _paintColor;
                    _balloonGimmicks[col, row] = _paintColor >= 0 ? _paintGimmick : 0;
                    e.Use();
                    Repaint(); // EditorWindow 갱신 (카운터 업데이트)
                }
            }

            // 마우스 이동 시 Scene 다시 그리기 (호버 커서)
            if (e.type == EventType.MouseMove)
            {
                SceneView.RepaintAll();
            }
        }

        #endregion

        #region Scene View — 키보드 단축키

        private void HandleSceneKeyboard(Event e)
        {
            if (e.type != EventType.KeyDown) return;

            bool used = false;

            // 숫자키 1~9: 색상 선택 (1=color0 ... 9=color8)
            if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha9)
            {
                int colorIdx = e.keyCode - KeyCode.Alpha1;
                if (colorIdx < _numColors)
                {
                    _paintColor = colorIdx;
                    used = true;
                }
            }

            // 0: 지우개
            if (e.keyCode == KeyCode.Alpha0)
            {
                _paintColor = -1;
                used = true;
            }

            // Space: Paint Mode 토글
            if (e.keyCode == KeyCode.Space)
            {
                _paintMode = !_paintMode;
                used = true;
            }

            if (used)
            {
                e.Use();
                Repaint();
                SceneView.RepaintAll();
            }
        }

        #endregion

        #region Scene View — 오버레이 UI

        private void DrawSceneOverlay()
        {
            Handles.BeginGUI();

            // 상단 툴바
            GUILayout.BeginArea(new Rect(10, 10, 420, 70));
            var boxStyle = new GUIStyle("box") { padding = new RectOffset(6, 6, 4, 4) };
            GUILayout.BeginVertical(boxStyle);

            // 1행: 타이틀 + Paint Mode
            GUILayout.BeginHorizontal();
            GUILayout.Label("Map Editor", EditorStyles.boldLabel, GUILayout.Width(80));

            Color oldBtnBg = GUI.backgroundColor;
            GUI.backgroundColor = _paintMode ? new Color(0.3f, 1f, 0.3f) : Color.gray;
            if (GUILayout.Button(_paintMode ? "Paint ON" : "Paint OFF", GUILayout.Width(80)))
            {
                _paintMode = !_paintMode;
            }
            GUI.backgroundColor = oldBtnBg;

            GUILayout.Label($"  {_balloonGridCols}x{_balloonGridRows}  Scale:{_balloonScale:F2}  N:{CountBalloons()}",
                GUILayout.Width(260));
            GUILayout.EndHorizontal();

            // 2행: 색상 브러시
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Mathf.Min(_numColors, 8); i++)
            {
                Color old = GUI.backgroundColor;
                GUI.backgroundColor = i == _paintColor ? Color.white : PALETTE[i];
                if (GUILayout.Button(i == _paintColor ? $"[{i + 1}]" : (i + 1).ToString(), GUILayout.Width(32), GUILayout.Height(22)))
                {
                    _paintColor = i;
                    Repaint();
                }
                GUI.backgroundColor = old;
            }

            // 지우개
            Color eraseBg = GUI.backgroundColor;
            GUI.backgroundColor = _paintColor == -1 ? Color.white : Color.gray;
            if (GUILayout.Button(_paintColor == -1 ? "[X]" : "X", GUILayout.Width(32), GUILayout.Height(22)))
            {
                _paintColor = -1;
                Repaint();
            }
            GUI.backgroundColor = eraseBg;

            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.EndArea();

            Handles.EndGUI();
        }

        #endregion

        #region Draw — Export

        private void DrawExportSection()
        {
            EditorGUI.indentLevel++;

            _targetDatabase = (LevelDatabase)EditorGUILayout.ObjectField(
                "Target LevelDatabase", _targetDatabase, typeof(LevelDatabase), false);

            EditorGUILayout.Space(4);

            int balloonCount = CountBalloons();
            int holderCount = CountHolders();
            int totalMagazine = CountTotalMagazine();
            EditorGUILayout.HelpBox(
                $"Balloons: {balloonCount} | Holders: {holderCount} | Total Darts: {totalMagazine}\n" +
                $"Grid: {_balloonGridCols}x{_balloonGridRows} | Holder Grid: {_holderGridCols}x{_holderGridRows}\n" +
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
                SaveToDatabase();
            if (GUILayout.Button("Export JSON", GUILayout.Height(30)))
                ExportJson();
            if (GUILayout.Button("Play Test", GUILayout.Height(30)))
                PlayTest();
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
                        position = new Vector2(c, r)
                    });
                }
            }
            config.holders = holders.ToArray();

            config.rail = BuildRailLayout();

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

            if (_railDirection == 0)
            {
                waypoints.Add(new Vector3(left, _railHeight, bottom));
                waypoints.Add(new Vector3(Mathf.Lerp(left, right, 0.33f), _railHeight, bottom));
                waypoints.Add(new Vector3(Mathf.Lerp(left, right, 0.67f), _railHeight, bottom));
                waypoints.Add(new Vector3(right, _railHeight, bottom));
                waypoints.Add(new Vector3(right, _railHeight, Mathf.Lerp(bottom, top, 0.33f)));
                waypoints.Add(new Vector3(right, _railHeight, Mathf.Lerp(bottom, top, 0.67f)));
                waypoints.Add(new Vector3(right, _railHeight, top));
                waypoints.Add(new Vector3(Mathf.Lerp(right, left, 0.33f), _railHeight, top));
                waypoints.Add(new Vector3(Mathf.Lerp(right, left, 0.67f), _railHeight, top));
                waypoints.Add(new Vector3(left, _railHeight, top));
                waypoints.Add(new Vector3(left, _railHeight, Mathf.Lerp(top, bottom, 0.33f)));
                waypoints.Add(new Vector3(left, _railHeight, Mathf.Lerp(top, bottom, 0.67f)));
            }
            else
            {
                waypoints.Add(new Vector3(right, _railHeight, bottom));
                waypoints.Add(new Vector3(Mathf.Lerp(right, left, 0.33f), _railHeight, bottom));
                waypoints.Add(new Vector3(Mathf.Lerp(right, left, 0.67f), _railHeight, bottom));
                waypoints.Add(new Vector3(left, _railHeight, bottom));
                waypoints.Add(new Vector3(left, _railHeight, Mathf.Lerp(bottom, top, 0.33f)));
                waypoints.Add(new Vector3(left, _railHeight, Mathf.Lerp(bottom, top, 0.67f)));
                waypoints.Add(new Vector3(left, _railHeight, top));
                waypoints.Add(new Vector3(Mathf.Lerp(left, right, 0.33f), _railHeight, top));
                waypoints.Add(new Vector3(Mathf.Lerp(left, right, 0.67f), _railHeight, top));
                waypoints.Add(new Vector3(right, _railHeight, top));
                waypoints.Add(new Vector3(right, _railHeight, Mathf.Lerp(top, bottom, 0.33f)));
                waypoints.Add(new Vector3(right, _railHeight, Mathf.Lerp(top, bottom, 0.67f)));
            }

            int slotCount = _holderGridCols;
            var holderPositions = new Vector3[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                float tVal = (i + 1f) / (slotCount + 1f);
                holderPositions[i] = new Vector3(
                    Mathf.Lerp(left, right, tVal),
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
                for (int r = 0; r < _balloonGridRows; r++)
                {
                    int g = _balloonGimmicks[c, r];
                    if (g > 0 && g < GIMMICK_TYPES.Length)
                        set.Add(GIMMICK_TYPES[g]);
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

            var levels = _targetDatabase.levels != null
                ? new List<LevelConfig>(_targetDatabase.levels)
                : new List<LevelConfig>();

            int idx = levels.FindIndex(l => l.levelId == config.levelId);
            if (idx >= 0)
                levels[idx] = config;
            else
                levels.Add(config);

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
            if (_balloonColors == null) return 0;
            int count = 0;
            for (int c = 0; c < _balloonGridCols; c++)
                for (int r = 0; r < _balloonGridRows; r++)
                    if (_balloonColors[c, r] >= 0) count++;
            return count;
        }

        private int CountHolders()
        {
            if (_holderColors == null) return 0;
            int count = 0;
            for (int c = 0; c < _holderGridCols; c++)
                for (int r = 0; r < _holderGridRows; r++)
                    if (_holderColors[c, r] >= 0) count++;
            return count;
        }

        private int CountTotalMagazine()
        {
            if (_holderColors == null) return 0;
            int total = 0;
            for (int c = 0; c < _holderGridCols; c++)
                for (int r = 0; r < _holderGridRows; r++)
                    if (_holderColors[c, r] >= 0) total += _holderMagazines[c, r];
            return total;
        }

        #endregion
    }
}
