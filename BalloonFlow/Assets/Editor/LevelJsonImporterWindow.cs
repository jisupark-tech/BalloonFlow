#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// BalloonFlow > Import Level Data From JSON
    /// Pixel Art Converter에서 Export한 JSON 파일을 읽어 LevelDatabase.asset에 추가.
    /// designer_note의 [FieldMap]을 파싱하여 정확한 픽셀 위치/색상으로 풍선을 배치.
    /// </summary>
    public class LevelJsonImporterWindow : EditorWindow
    {
        #region Constants

        private const float BOARD_WORLD_SIZE = 8f;
        private const float BOARD_CENTER_X   = 0f;
        private const float BOARD_CENTER_Z   = 2f;
        private const float RAIL_PADDING     = 1.5f;

        #endregion

        #region JSON Data Structure (snake_case — Converter 출력과 1:1 대응)

        [Serializable]
        private class JsonLevelData
        {
            public int    level_number;
            public string level_id;
            public int    pkg;
            public int    pos;
            public int    chapter;
            public string purpose_type;
            public int    target_cr;
            public float  target_attempts;
            public int    num_colors;
            public string color_distribution;
            public int    field_rows;
            public int    field_columns;
            public int    total_cells;
            public int    rail_capacity;
            public string rail_capacity_tier;
            public int    queue_columns;
            public int    queue_rows;
            public int    gimmick_hidden;
            public int    gimmick_chain;
            public int    gimmick_pinata;
            public int    gimmick_spawner_t;
            public int    gimmick_pin;
            public int    gimmick_lock_key;
            public int    gimmick_surprise;
            public int    gimmick_wall;
            public int    gimmick_spawner_o;
            public int    gimmick_pinata_box;
            public int    gimmick_ice;
            public int    gimmick_frozen_dart;
            public int    gimmick_curtain;
            public int    total_darts;
            public string dart_capacity_range;
            public string emotion_curve;
            public string designer_note;
            public string pixel_art_source;
        }

        /// <summary>Import할 레벨 정보 + 변환된 Config</summary>
        private class ImportEntry
        {
            public string       filePath;
            public string       fileName;
            public JsonLevelData json;
            public LevelConfig  config;
            public bool         selected = true;
            public bool         conflict;       // 기존 DB에 동일 levelId 존재
            public string       error;
        }

        #endregion

        #region Window State

        private List<ImportEntry> _entries = new();
        private Vector2 _listScroll;
        private Vector2 _previewScroll;
        private int     _selectedIndex = -1;
        private string  _statusMessage = "JSON 파일을 추가하세요";
        private bool    _overwriteConflicts = true;
        private float   _previewZoom = 1f;

        private static readonly string[] DB_NAMES = { "Origin", "AI Extractor", "Transform Extractor" };
        private static readonly string[] DB_ASSET_PATHS = {
            "Assets/Resources/LevelDatabase.asset",
            "Assets/EditorData/LevelDatabase_AI.asset",
            "Assets/EditorData/LevelDatabase_Transform.asset"
        };
        private int _targetDBIndex = 0;

        #endregion

        #region Menu

        [MenuItem("BalloonFlow/Import Level Data From JSON", false, 60)]
        public static void ShowWindow()
        {
            var win = GetWindow<LevelJsonImporterWindow>("JSON Level Importer");
            win.minSize = new Vector2(950, 650);
        }

        #endregion

        #region GUI

        private void OnGUI()
        {
            DrawToolbar();

            EditorGUILayout.BeginHorizontal();
            {
                // Left: entry list
                EditorGUILayout.BeginVertical(GUILayout.Width(320));
                DrawEntryList();
                EditorGUILayout.EndVertical();

                // Right: preview
                EditorGUILayout.BeginVertical();
                DrawPreview();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();

            // Status bar
            EditorGUILayout.LabelField(_statusMessage, EditorStyles.helpBox);
        }

        private void DrawToolbar()
        {
            // ── Row 1: Import 도구 ──
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("파일 추가...", EditorStyles.toolbarButton, GUILayout.Width(90)))
                AddFiles();

            if (GUILayout.Button("폴더 추가...", EditorStyles.toolbarButton, GUILayout.Width(90)))
                AddFolder();

            if (GUILayout.Button("전체 제거", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                _entries.Clear();
                _selectedIndex = -1;
                _statusMessage = "JSON 파일을 추가하세요";
            }

            GUILayout.FlexibleSpace();

            _overwriteConflicts = GUILayout.Toggle(_overwriteConflicts,
                "중복 레벨 덮어쓰기", EditorStyles.toolbarButton, GUILayout.Width(130));

            GUILayout.Space(10);

            GUILayout.Label("저장 대상:", EditorStyles.miniLabel, GUILayout.Width(55));
            _targetDBIndex = EditorGUILayout.Popup(_targetDBIndex, DB_NAMES, EditorStyles.toolbarPopup, GUILayout.Width(120));

            GUI.enabled = _entries.Any(e => e.selected && e.config != null);
            if (GUILayout.Button($"{DB_NAMES[_targetDBIndex]}에 추가", EditorStyles.toolbarButton, GUILayout.Width(140)))
                ApplyToDatabase();
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            // ── Row 2: DB 관리 도구 ──
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("DB Export", EditorStyles.toolbarButton, GUILayout.Width(80)))
                LevelDatabaseTools.ExportAll();

            if (GUILayout.Button("DB Import", EditorStyles.toolbarButton, GUILayout.Width(80)))
                LevelDatabaseTools.ImportAll();

            GUILayout.Space(5);

            if (GUILayout.Button("백업", EditorStyles.toolbarButton, GUILayout.Width(50)))
                LevelDatabaseTools.ManualBackup();

            if (GUILayout.Button("롤백", EditorStyles.toolbarButton, GUILayout.Width(50)))
                LevelDatabaseTools.DoRollback();

            GUILayout.Space(5);

            if (GUILayout.Button("레벨 Swap", EditorStyles.toolbarButton, GUILayout.Width(80)))
                LevelDatabaseTools.SwapLevels();

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawEntryList()
        {
            EditorGUILayout.LabelField($"JSON 파일 ({_entries.Count})", EditorStyles.boldLabel);

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                bool isSel = _selectedIndex == i;

                EditorGUILayout.BeginHorizontal(isSel
                    ? new GUIStyle("selectionRect") : GUIStyle.none);

                e.selected = EditorGUILayout.Toggle(e.selected, GUILayout.Width(18));

                // Status icon
                string icon = e.error != null ? "X " :
                              e.conflict ? "! " : "  ";

                var labelStyle = new GUIStyle(EditorStyles.label);
                if (e.error != null)
                    labelStyle.normal.textColor = new Color(1f, 0.3f, 0.3f);
                else if (e.conflict)
                    labelStyle.normal.textColor = new Color(1f, 0.7f, 0.2f);
                else if (e.config != null)
                    labelStyle.normal.textColor = new Color(0.3f, 0.8f, 0.3f);

                string label = $"{icon}Lv{e.json?.level_number ?? 0:D3}  " +
                    $"{e.json?.field_rows ?? 0}x{e.json?.field_columns ?? 0}  " +
                    $"C{e.json?.num_colors ?? 0}  {e.fileName}";

                if (GUILayout.Button(label, labelStyle))
                {
                    _selectedIndex = i;
                    Repaint();
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            // Summary
            int total = _entries.Count;
            int ok = _entries.Count(e => e.config != null && e.error == null);
            int conflicts = _entries.Count(e => e.conflict);
            int errors = _entries.Count(e => e.error != null);

            EditorGUILayout.LabelField(
                $"성공: {ok}  충돌: {conflicts}  오류: {errors}",
                EditorStyles.miniLabel);
        }

        private void DrawPreview()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _entries.Count)
            {
                EditorGUILayout.HelpBox("왼쪽 목록에서 레벨을 선택하세요", MessageType.Info);
                return;
            }

            var entry = _entries[_selectedIndex];

            if (entry.error != null)
            {
                EditorGUILayout.HelpBox($"오류: {entry.error}", MessageType.Error);
                return;
            }

            if (entry.config == null)
            {
                EditorGUILayout.HelpBox("변환 실패", MessageType.Warning);
                return;
            }

            var config = entry.config;
            var json = entry.json;

            // Info header
            EditorGUILayout.LabelField(
                $"Level {config.levelId}  —  {config.gridCols}x{config.gridRows}  " +
                $"Balloons={config.balloonCount}  Colors={config.numColors}  " +
                $"Holders={config.holders?.Length ?? 0}",
                EditorStyles.boldLabel);

            if (entry.conflict)
                EditorGUILayout.HelpBox(
                    $"LevelDatabase에 levelId={config.levelId} 이미 존재. " +
                    (_overwriteConflicts ? "덮어씁니다." : "건너뜁니다."),
                    MessageType.Warning);

            EditorGUILayout.LabelField($"Source: {json.pixel_art_source}", EditorStyles.miniLabel);

            // Zoom
            _previewZoom = EditorGUILayout.Slider("Zoom", _previewZoom, 0.5f, 4f);

            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll);

            float cellSize = 14f * _previewZoom;
            float offsetX = 20f, offsetY = 10f;
            int cols = config.gridCols;
            int rows = config.gridRows;

            GUILayoutUtility.GetRect(
                cols * cellSize + offsetX * 2,
                rows * cellSize + offsetY * 2 + 100);

            // BalloonFlow 28-color palette (preview용)
            Color[] palette = {
                c(252,106,175), c(80,232,246), c(137,80,248), c(254,213,85),
                c(115,254,102), c(253,161,76), c(255,255,255), c(65,65,65),
                c(110,168,250), c(57,174,46), c(252,94,94), c(50,107,248),
                c(58,165,139), c(231,167,250), c(183,199,251), c(106,74,48),
                c(254,227,169), c(253,183,193), c(158,61,94), c(167,221,148),
                c(89,46,126), c(220,120,129), c(217,217,231), c(111,114,127),
                c(252,56,165), c(253,180,88), c(137,10,8), c(111,175,177),
            };

            // Grid background
            EditorGUI.DrawRect(new Rect(offsetX - 2, offsetY - 2,
                cols * cellSize + 4, rows * cellSize + 4),
                new Color(0.12f, 0.12f, 0.15f));

            // Build position map
            var balloonMap = new Dictionary<Vector2Int, BalloonLayout>();
            if (config.balloons != null)
            {
                float cs = BOARD_WORLD_SIZE / Mathf.Max(cols, rows);
                float halfGrid = BOARD_WORLD_SIZE * 0.5f - cs * 0.5f;
                foreach (var b in config.balloons)
                {
                    int gx = Mathf.RoundToInt((b.gridPosition.x - BOARD_CENTER_X + halfGrid) / cs);
                    int gy = Mathf.RoundToInt((b.gridPosition.y - BOARD_CENTER_Z + halfGrid) / cs);
                    balloonMap[new Vector2Int(gx, gy)] = b;
                }
            }

            // Draw cells
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    var rect = new Rect(
                        offsetX + x * cellSize,
                        offsetY + (rows - 1 - y) * cellSize,
                        cellSize - 1, cellSize - 1);

                    if (balloonMap.TryGetValue(new Vector2Int(x, y), out var balloon))
                    {
                        int ci = Mathf.Clamp(balloon.color, 0, palette.Length - 1);
                        EditorGUI.DrawRect(rect, palette[ci]);
                    }
                    else
                    {
                        EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.22f));
                    }
                }
            }

            // Holder summary
            float holderY = offsetY + rows * cellSize + 10;
            if (config.holders != null && config.holders.Length > 0)
            {
                GUI.Label(new Rect(offsetX, holderY, 400, 16),
                    $"Holders: {config.holders.Length}  (총 다트: {config.holders.Sum(h => h.magazineCount)})");
                holderY += 18;

                int shown = Mathf.Min(config.holders.Length, 60);
                for (int i = 0; i < shown; i++)
                {
                    var h = config.holders[i];
                    int ci = Mathf.Clamp(h.color, 0, palette.Length - 1);
                    float hx = offsetX + (i % 20) * 32;
                    float hy = holderY + (i / 20) * 20;
                    var hRect = new Rect(hx, hy, 30, 16);
                    EditorGUI.DrawRect(hRect, palette[ci]);
                    GUI.Label(hRect, h.magazineCount.ToString(),
                        new GUIStyle(EditorStyles.miniLabel) {
                            alignment = TextAnchor.MiddleCenter,
                            normal = { textColor = Color.white }
                        });
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private static Color c(int r, int g, int b) =>
            new Color(r / 255f, g / 255f, b / 255f);

        #endregion

        #region File Loading

        private void AddFiles()
        {
            string path = EditorUtility.OpenFilePanelWithFilters(
                "JSON 파일 선택", "",
                new[] { "JSON files", "json", "All files", "*" });
            if (string.IsNullOrEmpty(path)) return;
            LoadJsonFile(path);
            _statusMessage = $"{_entries.Count}개 파일 로드됨";
        }

        private void AddFolder()
        {
            string folder = EditorUtility.OpenFolderPanel("JSON 폴더 선택", "", "");
            if (string.IsNullOrEmpty(folder)) return;

            var files = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly);
            int added = 0;
            foreach (var f in files.OrderBy(f => f))
            {
                LoadJsonFile(f);
                added++;
            }
            _statusMessage = $"{added}개 JSON 파일 추가됨 (총 {_entries.Count}개)";
        }

        private void LoadJsonFile(string path)
        {
            // 중복 체크
            if (_entries.Any(e => e.filePath == path)) return;

            var entry = new ImportEntry
            {
                filePath = path,
                fileName = Path.GetFileName(path)
            };

            try
            {
                string jsonText = File.ReadAllText(path);
                entry.json = JsonUtility.FromJson<JsonLevelData>(jsonText);

                if (entry.json == null || entry.json.field_rows <= 0)
                {
                    entry.error = "유효하지 않은 JSON 형식";
                }
                else
                {
                    entry.config = BuildLevelConfig(entry.json);
                    CheckConflict(entry);
                }
            }
            catch (Exception ex)
            {
                entry.error = ex.Message;
            }

            _entries.Add(entry);
        }

        private void CheckConflict(ImportEntry entry)
        {
            if (entry.config == null) return;

            var db = AssetDatabase.LoadAssetAtPath<LevelDatabase>(
                DB_ASSET_PATHS[_targetDBIndex]);
            if (db?.levels == null) return;

            entry.conflict = db.levels.Any(lv => lv.levelId == entry.config.levelId);
        }

        #endregion

        #region JSON → LevelConfig 변환

        private LevelConfig BuildLevelConfig(JsonLevelData json)
        {
            int gridRows = Mathf.Max(json.field_rows, 1);
            int gridCols = Mathf.Max(json.field_columns, 1);
            int maxDim = Mathf.Max(gridCols, gridRows);
            float cellSpacing = BOARD_WORLD_SIZE / maxDim;
            float balloonScale = cellSpacing * 0.85f;

            // 1) FieldMap 파싱 → 정확한 풍선 배치
            int[,] fieldMap = ParseFieldMap(json.designer_note, gridCols, gridRows);

            // FieldMap이 비어있으면 pixel_art_source 이미지에서 자동 생성
            if (IsFieldMapEmpty(fieldMap, gridCols, gridRows) && !string.IsNullOrEmpty(json.pixel_art_source))
            {
                fieldMap = BuildFieldMapFromImage(json.pixel_art_source, gridCols, gridRows, json.num_colors, json.color_distribution);
            }

            var balloons = BuildBalloonsFromFieldMap(fieldMap, gridCols, gridRows, cellSpacing);

            // 2) 색상 분포 파싱
            var colorDist = ParseColorDistribution(json.color_distribution);
            int numColors = json.num_colors > 0 ? json.num_colors
                : colorDist.Count > 0 ? colorDist.Count
                : CountColorsInField(fieldMap, gridCols, gridRows);

            // 3) 기믹 할당
            string[] gimmickTypes = AssignGimmicks(balloons, json);

            // 4) 홀더 생성
            int[] dartsPerColor = CountDartsPerColor(balloons, 28);
            int[] allowedMags = ParseDartCapacityRange(json.dart_capacity_range, json.rail_capacity);
            int queueCols = Mathf.Max(json.queue_columns, 2);
            int maxHolders = queueCols * Mathf.Max(json.queue_rows, 3);
            var holders = BuildHolders(dartsPerColor, allowedMags, queueCols, maxHolders);

            // 5) 레일 생성
            var rail = GenerateRail(gridCols, gridRows, queueCols, json.rail_capacity);

            // 6) 컨베이어 포지션
            var conveyorPositions = GenerateConveyorPositions(gridCols, gridRows);

            // 7) 스타 계산
            int activeCells = balloons.Length;
            int star1 = activeCells * 100;

            // 8) 난이도
            DifficultyPurpose purpose = MapPurpose(json.purpose_type);

            return new LevelConfig
            {
                levelId           = json.level_number > 0 ? json.level_number : 1,
                packageId         = json.pkg,
                positionInPackage = json.pos,
                railCapacity      = json.rail_capacity,
                numColors         = numColors,
                balloonCount      = activeCells,
                balloonScale      = balloonScale,
                queueColumns      = queueCols,
                targetClearRate   = json.target_cr / 100f,
                difficultyPurpose = purpose,
                gimmickTypes      = gimmickTypes,
                holders           = holders,
                balloons          = balloons,
                rail              = rail,
                conveyorPositions = conveyorPositions,
                gridCols          = gridCols,
                gridRows          = gridRows,
                star1Threshold    = star1,
                star2Threshold    = Mathf.CeilToInt(star1 * 1.5f),
                star3Threshold    = Mathf.CeilToInt(star1 * 2.2f)
            };
        }

        #endregion

        #region FieldMap 파싱 — Converter의 [FieldMap]에서 정확한 색상 배치 추출

        /// <summary>
        /// designer_note의 [FieldMap] 섹션 파싱.
        /// 형식: "07 07 .. .. ..\n07 .. 07 .. .."
        /// 숫자 = color ID (1-based), ".." = 빈 셀.
        /// 반환: int[cols, rows] (0 = 빈 셀, 1~28 = 색상 ID)
        /// </summary>
        private int[,] ParseFieldMap(string designerNote, int cols, int rows)
        {
            var field = new int[cols, rows];

            if (string.IsNullOrEmpty(designerNote)) return field;

            // [FieldMap] 태그 이후의 텍스트 추출
            int mapStart = designerNote.IndexOf("[FieldMap]", StringComparison.Ordinal);
            if (mapStart < 0) return field;

            string mapText = designerNote.Substring(mapStart + "[FieldMap]".Length);

            // 다음 태그가 있으면 거기까지만
            int nextTag = mapText.IndexOf('[');
            if (nextTag >= 0) mapText = mapText.Substring(0, nextTag);

            var lines = mapText.Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToArray();

            for (int y = 0; y < Mathf.Min(lines.Length, rows); y++)
            {
                var tokens = lines[y].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int x = 0; x < Mathf.Min(tokens.Length, cols); x++)
                {
                    if (tokens[x] == "..")
                        field[x, y] = 0;
                    else if (int.TryParse(tokens[x], out int colorId))
                        field[x, y] = colorId; // 1-based color ID
                    else
                        field[x, y] = 0;
                }
            }

            return field;
        }

        /// <summary>FieldMap이 전부 0인지 확인</summary>
        private bool IsFieldMapEmpty(int[,] field, int cols, int rows)
        {
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                    if (field[x, y] != 0) return false;
            return true;
        }

        /// <summary>
        /// pixel_art_source 이미지에서 FieldMap 자동 생성.
        /// JSON과 같은 폴더 또는 Assets/ 하위에서 이미지를 찾아 픽셀→색상ID 매핑.
        /// </summary>
        private int[,] BuildFieldMapFromImage(string imageFileName, int cols, int rows,
            int numColors, string colorDistribution)
        {
            var field = new int[cols, rows];

            // 이미지 파일 찾기: JSON과 같은 폴더 또는 Assets/
            string imagePath = null;
            foreach (var entry in _entries)
            {
                if (entry.filePath == null) continue;
                string dir = Path.GetDirectoryName(entry.filePath);
                string candidate = Path.Combine(dir, imageFileName);
                if (File.Exists(candidate)) { imagePath = candidate; break; }
            }
            if (imagePath == null)
            {
                // Assets 폴더에서도 검색
                var guids = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(imageFileName));
                foreach (var guid in guids)
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (p.EndsWith(".png") || p.EndsWith(".jpg")) { imagePath = p; break; }
                }
            }
            if (imagePath == null)
            {
                Debug.LogWarning($"[PixelForge] 이미지 없음: {imageFileName} — 빈 FieldMap 사용");
                return field;
            }

            // 이미지 로드
            byte[] bytes = File.ReadAllBytes(imagePath);
            var tex = new Texture2D(2, 2);
            if (!tex.LoadImage(bytes))
            {
                Debug.LogWarning($"[PixelForge] 이미지 로드 실패: {imagePath}");
                return field;
            }

            Debug.Log($"[PixelForge] 이미지→FieldMap: {imagePath} ({tex.width}x{tex.height}) → {cols}x{rows}");

            // 허용 색상 결정
            var allowedColors = ParseColorDistribution(colorDistribution);
            if (allowedColors.Count == 0)
                for (int i = 1; i <= Mathf.Max(numColors, 1); i++) allowedColors[i] = 1;

            // 28색 게임 팔레트
            var palette = new Dictionary<int, Color>
            {
                {1, new Color(252/255f, 106/255f, 175/255f)}, {2, new Color(80/255f, 232/255f, 246/255f)},
                {3, new Color(137/255f, 80/255f, 248/255f)},  {4, new Color(254/255f, 213/255f, 85/255f)},
                {5, new Color(115/255f, 254/255f, 102/255f)}, {6, new Color(253/255f, 161/255f, 76/255f)},
                {7, new Color(1f, 1f, 1f)},                   {8, new Color(65/255f, 65/255f, 65/255f)},
                {9, new Color(110/255f, 168/255f, 250/255f)},  {10, new Color(57/255f, 174/255f, 46/255f)},
                {11, new Color(252/255f, 94/255f, 94/255f)},   {12, new Color(50/255f, 107/255f, 248/255f)},
                {13, new Color(58/255f, 165/255f, 139/255f)},  {14, new Color(231/255f, 167/255f, 250/255f)},
                {15, new Color(183/255f, 199/255f, 251/255f)}, {16, new Color(106/255f, 74/255f, 48/255f)},
                {17, new Color(254/255f, 227/255f, 169/255f)}, {18, new Color(253/255f, 183/255f, 193/255f)},
                {19, new Color(158/255f, 61/255f, 94/255f)},   {20, new Color(167/255f, 221/255f, 148/255f)},
                {21, new Color(89/255f, 46/255f, 126/255f)},   {22, new Color(220/255f, 120/255f, 129/255f)},
                {23, new Color(217/255f, 217/255f, 231/255f)}, {24, new Color(111/255f, 114/255f, 127/255f)},
                {25, new Color(252/255f, 56/255f, 165/255f)},  {26, new Color(253/255f, 180/255f, 88/255f)},
                {27, new Color(137/255f, 10/255f, 8/255f)},    {28, new Color(111/255f, 175/255f, 177/255f)},
            };

            // 허용 색상만 필터
            var allowed = palette.Where(kv => allowedColors.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (allowed.Count == 0) allowed = palette;

            // 배경색 추정 (코너 4곳 평균)
            Color bg = (tex.GetPixel(0, 0) + tex.GetPixel(tex.width - 1, 0) +
                        tex.GetPixel(0, tex.height - 1) + tex.GetPixel(tex.width - 1, tex.height - 1)) / 4f;

            // 각 셀의 색상 매핑
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    // 이미지에서 해당 셀 영역의 중심 픽셀
                    int px = Mathf.FloorToInt((x + 0.5f) * tex.width / cols);
                    int py = Mathf.FloorToInt((1f - (y + 0.5f) / rows) * tex.height); // Y 반전
                    px = Mathf.Clamp(px, 0, tex.width - 1);
                    py = Mathf.Clamp(py, 0, tex.height - 1);
                    Color pixel = tex.GetPixel(px, py);

                    // 배경과 유사하면 빈 셀
                    if (ColorDistance(pixel, bg) < 0.15f) { field[x, y] = 0; continue; }

                    // 가장 가까운 허용 색상 찾기
                    int bestId = 0;
                    float bestDist = float.MaxValue;
                    foreach (var kv in allowed)
                    {
                        float d = ColorDistance(pixel, kv.Value);
                        if (d < bestDist) { bestDist = d; bestId = kv.Key; }
                    }
                    field[x, y] = bestId;
                }
            }

            Object.DestroyImmediate(tex);
            return field;
        }

        private float ColorDistance(Color a, Color b)
        {
            return (a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b);
        }

        /// <summary>
        /// FieldMap의 정확한 색상 배치를 BalloonLayout[]로 변환.
        /// color는 0-based 인덱스로 변환 (colorId 1 → color 0).
        /// </summary>
        private BalloonLayout[] BuildBalloonsFromFieldMap(int[,] field, int cols, int rows,
            float cellSpacing)
        {
            var balloons = new List<BalloonLayout>();
            int maxDim = Mathf.Max(cols, rows);
            float cs = BOARD_WORLD_SIZE / maxDim;
            float halfGridX = (cols - 1) * 0.5f * cs;
            float halfGridY = (rows - 1) * 0.5f * cs;
            int bid = 0;

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    int colorId = field[x, y];
                    if (colorId <= 0) continue; // 빈 셀

                    float wx = BOARD_CENTER_X + x * cs - halfGridX;
                    float wz = BOARD_CENTER_Z + y * cs - halfGridY;

                    balloons.Add(new BalloonLayout
                    {
                        balloonId = bid++,
                        color = colorId - 1, // 1-based → 0-based
                        gridPosition = new Vector2(wx, wz),
                        gimmickType = ""
                    });
                }
            }

            return balloons.ToArray();
        }

        private int CountColorsInField(int[,] field, int cols, int rows)
        {
            var colors = new HashSet<int>();
            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                    if (field[x, y] > 0) colors.Add(field[x, y]);
            return colors.Count;
        }

        #endregion

        #region Color Distribution

        private Dictionary<int, int> ParseColorDistribution(string dist)
        {
            var result = new Dictionary<int, int>();
            if (string.IsNullOrEmpty(dist)) return result;

            var matches = Regex.Matches(dist, @"c(\d+)\s*:\s*(\d+)");
            foreach (Match m in matches)
            {
                int colorIdx = int.Parse(m.Groups[1].Value) - 1; // c1 → 0
                int count = int.Parse(m.Groups[2].Value);
                result[colorIdx] = count;
            }
            return result;
        }

        private int[] ParseDartCapacityRange(string range, int railCapacity)
        {
            if (!string.IsNullOrEmpty(range))
            {
                var parsed = range.Split(',')
                    .Select(s => { int.TryParse(s.Trim(), out int v); return v; })
                    .Where(v => v > 0)
                    .OrderByDescending(v => v)
                    .ToArray();
                if (parsed.Length > 0) return parsed;
            }

            if (railCapacity <= 50)  return new[] { 5, 10, 20 };
            if (railCapacity <= 100) return new[] { 5, 10, 20, 30 };
            if (railCapacity <= 150) return new[] { 5, 10, 20, 30, 40 };
            return new[] { 5, 10, 20, 30, 40, 50 };
        }

        #endregion

        #region Holder 생성

        private int[] CountDartsPerColor(BalloonLayout[] balloons, int maxColors)
        {
            int[] counts = new int[maxColors];
            foreach (var b in balloons)
            {
                int life = GetGimmickLife(b.gimmickType);
                if (b.color >= 0 && b.color < maxColors)
                    counts[b.color] += life;
            }
            return counts;
        }

        private int GetGimmickLife(string gimmickType)
        {
            if (string.IsNullOrEmpty(gimmickType)) return 1;
            return gimmickType switch
            {
                "pinata"     => 2,
                "pinata_box" => 2,
                "wall"       => 0,
                "pin"        => 0,
                "ice"        => 0,
                _            => 1
            };
        }

        private HolderSetup[] BuildHolders(int[] dartsPerColor, int[] allowedMags,
            int queueColumns, int maxHolders)
        {
            var holders = new List<HolderSetup>();
            var mags = allowedMags.OrderByDescending(m => m).ToArray();
            if (mags.Length == 0) mags = new[] { 5 };

            int hid = 0;
            int totalDarts = dartsPerColor.Sum();
            int colorsWithDarts = dartsPerColor.Count(d => d > 0);
            if (maxHolders < colorsWithDarts) maxHolders = Mathf.Max(colorsWithDarts, 6);

            // 색상별 홀더 예산 배분
            int[] budget = new int[dartsPerColor.Length];
            int assigned = 0;
            for (int c = 0; c < dartsPerColor.Length; c++)
            {
                if (dartsPerColor[c] <= 0) continue;
                budget[c] = Mathf.Max(1,
                    Mathf.RoundToInt((float)dartsPerColor[c] / Mathf.Max(totalDarts, 1) * maxHolders));
                assigned += budget[c];
            }
            while (assigned > maxHolders)
            {
                int maxIdx = Array.IndexOf(budget, budget.Max());
                budget[maxIdx]--;
                assigned--;
            }

            for (int color = 0; color < dartsPerColor.Length; color++)
            {
                int remaining = dartsPerColor[color];
                if (remaining <= 0) continue;

                int holderBudget = budget[color];

                if (holderBudget <= 1)
                {
                    holders.Add(new HolderSetup
                    {
                        holderId = hid++,
                        color = color,
                        magazineCount = remaining,
                        position = Vector2.zero
                    });
                    continue;
                }

                int used = 0;
                while (remaining > 0 && used < holderBudget)
                {
                    int left = holderBudget - used;
                    if (left == 1)
                    {
                        holders.Add(new HolderSetup
                        {
                            holderId = hid++, color = color,
                            magazineCount = remaining, position = Vector2.zero
                        });
                        remaining = 0;
                        used++;
                        break;
                    }

                    int smallest = mags[mags.Length - 1];
                    int bestMag = smallest;
                    foreach (int mag in mags)
                    {
                        if (mag <= remaining && (remaining - mag) >= (left - 1) * smallest)
                        {
                            bestMag = mag;
                            break;
                        }
                    }
                    if (bestMag > remaining) bestMag = remaining;

                    holders.Add(new HolderSetup
                    {
                        holderId = hid++, color = color,
                        magazineCount = bestMag, position = Vector2.zero
                    });
                    remaining -= bestMag;
                    used++;
                }

                if (remaining > 0 && holders.Count > 0)
                    holders[holders.Count - 1].magazineCount += remaining;
            }

            // 셔플 + 큐 포지션 할당
            var rng = new System.Random(holders.Count * 7 + 31);
            for (int i = holders.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (holders[i], holders[j]) = (holders[j], holders[i]);
            }

            for (int i = 0; i < holders.Count; i++)
            {
                int col = i % Mathf.Max(queueColumns, 1);
                int row = i / Mathf.Max(queueColumns, 1);
                holders[i].position = new Vector2(col, row);
                holders[i].holderId = i;
            }

            return holders.ToArray();
        }

        #endregion

        #region Gimmick 할당

        private string[] AssignGimmicks(BalloonLayout[] balloons, JsonLevelData json)
        {
            var gimmickList = new List<string>();
            var assignments = new List<string>();

            void AddGimmick(string type, int count)
            {
                if (count <= 0) return;
                gimmickList.Add(type);
                for (int i = 0; i < count; i++) assignments.Add(type);
            }

            AddGimmick("hidden",      json.gimmick_hidden);
            AddGimmick("chain",       json.gimmick_chain);
            AddGimmick("pinata",      json.gimmick_pinata);
            AddGimmick("spawner_t",   json.gimmick_spawner_t);
            AddGimmick("pin",         json.gimmick_pin);
            AddGimmick("lock_key",    json.gimmick_lock_key);
            AddGimmick("surprise",    json.gimmick_surprise);
            AddGimmick("wall",        json.gimmick_wall);
            AddGimmick("spawner_o",   json.gimmick_spawner_o);
            AddGimmick("pinata_box",  json.gimmick_pinata_box);
            AddGimmick("ice",         json.gimmick_ice);
            AddGimmick("frozen_dart", json.gimmick_frozen_dart);
            AddGimmick("curtain",     json.gimmick_curtain);

            if (assignments.Count > 0 && balloons.Length > 0)
            {
                var rng = new System.Random(balloons.Length * 13 + 7);
                var indices = Enumerable.Range(0, balloons.Length).ToList();
                for (int i = indices.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (indices[i], indices[j]) = (indices[j], indices[i]);
                }

                int count = Mathf.Min(assignments.Count, balloons.Length);
                for (int i = 0; i < count; i++)
                    balloons[indices[i]].gimmickType = assignments[i];
            }

            return gimmickList.ToArray();
        }

        #endregion

        #region Rail / Conveyor 생성

        private RailLayout GenerateRail(int gridCols, int gridRows,
            int queueColumns, int railCapacity)
        {
            float halfBoard = BOARD_WORLD_SIZE * 0.5f;
            float left   = BOARD_CENTER_X - halfBoard - RAIL_PADDING;
            float right  = BOARD_CENTER_X + halfBoard + RAIL_PADDING;
            float bottom = BOARD_CENTER_Z - halfBoard - RAIL_PADDING;
            float top    = BOARD_CENTER_Z + halfBoard + RAIL_PADDING;

            var wp = new List<Vector3>
            {
                new(left, 0.5f, bottom),
                new(Mathf.Lerp(left, right, 0.33f), 0.5f, bottom),
                new(Mathf.Lerp(left, right, 0.67f), 0.5f, bottom),
                new(right, 0.5f, bottom),
                new(right, 0.5f, Mathf.Lerp(bottom, top, 0.33f)),
                new(right, 0.5f, Mathf.Lerp(bottom, top, 0.67f)),
                new(right, 0.5f, top),
                new(Mathf.Lerp(right, left, 0.33f), 0.5f, top),
                new(Mathf.Lerp(right, left, 0.67f), 0.5f, top),
                new(left, 0.5f, top),
                new(left, 0.5f, Mathf.Lerp(top, bottom, 0.33f)),
                new(left, 0.5f, Mathf.Lerp(top, bottom, 0.67f))
            };

            int qCols = Mathf.Max(queueColumns, 2);
            var dp = new Vector3[qCols];
            for (int i = 0; i < qCols; i++)
            {
                float t = (i + 1f) / (qCols + 1f);
                dp[i] = new Vector3(Mathf.Lerp(left, right, t), 0.5f, bottom - 1f);
            }

            return new RailLayout
            {
                waypoints = wp.ToArray(),
                slotCount = railCapacity > 0 ? railCapacity : 200,
                visualType = 3,
                deployPoints = dp,
                smoothCorners = true,
                cornerRadius = 1f
            };
        }

        private Vector2Int[] GenerateConveyorPositions(int gridCols, int gridRows)
        {
            var pos = new List<Vector2Int>();
            for (int x = -1; x <= gridCols; x++)
            {
                pos.Add(new Vector2Int(x, -1));
                pos.Add(new Vector2Int(x, gridRows));
            }
            for (int y = 0; y < gridRows; y++)
            {
                pos.Add(new Vector2Int(-1, y));
                pos.Add(new Vector2Int(gridCols, y));
            }
            return pos.ToArray();
        }

        #endregion

        #region Purpose Mapping

        private DifficultyPurpose MapPurpose(string purposeType)
        {
            if (string.IsNullOrEmpty(purposeType)) return DifficultyPurpose.Normal;
            string p = purposeType.Trim();
            if (p.Contains("튜토리얼") || p.Contains("Tutorial")) return DifficultyPurpose.Tutorial;
            if (p.Contains("슈퍼하드") || p.Contains("SuperHard")) return DifficultyPurpose.SuperHard;
            if (p.Contains("하드") || p.Contains("Hard"))         return DifficultyPurpose.Hard;
            if (p.Contains("휴식") || p.Contains("Rest"))         return DifficultyPurpose.Rest;
            return DifficultyPurpose.Normal;
        }

        #endregion

        #region LevelDatabase에 적용

        private void ApplyToDatabase()
        {
            string dbPath = DB_ASSET_PATHS[_targetDBIndex];

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            var db = AssetDatabase.LoadAssetAtPath<LevelDatabase>(dbPath);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<LevelDatabase>();
                db.levels = Array.Empty<LevelConfig>();
                AssetDatabase.CreateAsset(db, dbPath);
            }

            // 덮어쓰기가 있으면 자동 백업
            bool hasOverwrite = _overwriteConflicts &&
                _entries.Any(e => e.selected && e.conflict && e.config != null && e.error == null);
            if (hasOverwrite)
                LevelDatabaseTools.CreateBackup(db, "before_overwrite");

            Undo.RecordObject(db, "Import Level Data From JSON");

            var existingLevels = db.levels != null
                ? new List<LevelConfig>(db.levels)
                : new List<LevelConfig>();

            int added = 0, overwritten = 0, skipped = 0;

            foreach (var entry in _entries.Where(e => e.selected && e.config != null && e.error == null))
            {
                int targetId = entry.config.levelId;
                int existingIdx = existingLevels.FindIndex(lv => lv.levelId == targetId);

                if (existingIdx >= 0)
                {
                    if (_overwriteConflicts)
                    {
                        existingLevels[existingIdx] = entry.config;
                        overwritten++;
                    }
                    else
                    {
                        skipped++;
                    }
                }
                else
                {
                    existingLevels.Add(entry.config);
                    added++;
                }
            }

            // levelId 순으로 정렬
            existingLevels.Sort((a, b) => a.levelId.CompareTo(b.levelId));
            db.levels = existingLevels.ToArray();

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _statusMessage = $"완료 — 추가: {added}, 덮어쓰기: {overwritten}, 건너뜀: {skipped}  " +
                $"(총 {db.levels.Length}개 레벨)";

            // 충돌 상태 갱신
            foreach (var entry in _entries)
                CheckConflict(entry);

            Debug.Log($"[LevelJsonImporter] {_statusMessage}");
            EditorUtility.DisplayDialog("Import 완료",
                $"추가: {added}\n덮어쓰기: {overwritten}\n건너뜀: {skipped}\n\n" +
                $"LevelDatabase 총 {db.levels.Length}개 레벨",
                "OK");

            Repaint();
        }

        #endregion
    }
}
#endif
