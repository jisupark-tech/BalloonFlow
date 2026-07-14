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
    /// Pixel Art Converter / Episode / LevelConfig JSON 을 읽어 episode JSON 에 직접 병합·저장.
    ///   - firebase/seed/episodes/episode_XX.json (각 패키지=20레벨, node upload-episodes.js 로 Firestore 업로드)
    ///   - pkg1 은 StreamingAssets/episode_01.json 로도 동기화 (앱 번들/오프라인)
    /// LevelDatabase.asset(18MB SO)은 거치지 않는다 — freeze 제거. MapMaker(SO 기반)와는 별도 경로.
    /// designer_note의 [FieldMap]을 파싱하여 정확한 픽셀 위치/색상으로 풍선을 배치.
    /// </summary>
    public class LevelJsonImporterWindow : EditorWindow
    {
        #region Constants

        private const float BOARD_WORLD_SIZE = 8f;
        private const float BOARD_CENTER_X   = 0f;
        private const float BOARD_CENTER_Z   = 2f;
        private const float RAIL_PADDING     = 1.5f;
        private const int   LEVELS_PER_PACKAGE = 20;

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
            public string       sourceKind;
            public JsonLevelData json;
            public LevelConfig  config;
            public bool         selected = true;
            public bool         conflict;       // 기존 DB에 동일 levelId 존재
            public string       error;
            // ROLLBACK_IMPORTER_OLD_SO_TARGET_20260618: 이 레벨을 old(레거시 SO)로 import 할지. false=ori(Episodes JSON).
            //   전역 토글(_globalTargetOld) 기본값 + 행별 개별 토글로 override. (특정 레벨만 old 로)
            public bool         importToOld;
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
        // ROLLBACK_IMPORTER_OLD_SO_TARGET_20260618: import 대상 전역 기본값(true=old SO, false=ori Episodes).
        //   새 entry 추가 시 이 값으로 초기화 + 행별 토글로 개별 override 가능.
        private bool    _globalTargetOld;
        private const string LEGACY_SO_PATH = "Assets/EditorData/LevelDatabase.asset";

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

            if (GUILayout.Button("레벨 백업 추가", EditorStyles.toolbarButton, GUILayout.Width(100)))
                AddLevelBackups();

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

            // ROLLBACK_IMPORTER_OLD_SO_TARGET_20260618: 전역 import 대상 토글. 변경 시 모든 entry 에 일괄 적용(전역 선택).
            //   행별 토글로 개별 override 가능. old=레거시 SO, ori=Episodes JSON.
            bool prevTargetOld = _globalTargetOld;
            _globalTargetOld = GUILayout.Toggle(_globalTargetOld,
                _globalTargetOld ? "대상: Old(SO)" : "대상: Ori(Episode)",
                EditorStyles.toolbarButton, GUILayout.Width(140));
            if (_globalTargetOld != prevTargetOld)
                foreach (var en in _entries) en.importToOld = _globalTargetOld;

            GUILayout.Space(10);

            GUI.enabled = _entries.Any(e => e.selected && e.config != null && e.error == null);
            if (GUILayout.Button("적용 (Ori/Old)", EditorStyles.toolbarButton, GUILayout.Width(120)))
                ApplyToEpisodes();
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

                // ROLLBACK_IMPORTER_OLD_SO_TARGET_20260618: 행별 import 대상 토글 (Old=레거시 SO / Ori=Episodes). 전역 override.
                e.importToOld = GUILayout.Toggle(e.importToOld, e.importToOld ? "Old" : "Ori",
                    EditorStyles.miniButton, GUILayout.Width(34));

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

                int levelId = e.config?.levelId ?? e.json?.level_number ?? 0;
                int rows = e.config?.gridRows ?? e.json?.field_rows ?? 0;
                int cols = e.config?.gridCols ?? e.json?.field_columns ?? 0;
                int colors = e.config?.numColors ?? e.json?.num_colors ?? 0;
                string kind = string.IsNullOrEmpty(e.sourceKind) ? "JSON" : e.sourceKind;

                string label = $"{icon}Lv{levelId:D3}  " +
                    $"{rows}x{cols}  " +
                    $"C{colors}  [{kind}] {e.fileName}";

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
                    $"episode_{PackageIdForLevel(config.levelId):D2}.json 에 levelId={config.levelId} 이미 존재. " +
                    (_overwriteConflicts ? "덮어씁니다." : "건너뜁니다."),
                    MessageType.Warning);

            string source = json != null && !string.IsNullOrEmpty(json.pixel_art_source)
                ? json.pixel_art_source
                : entry.fileName;
            EditorGUILayout.LabelField($"Source: {source}", EditorStyles.miniLabel);

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
                c(89,46,126), c(220,120,129), c(174,178,194), c(111,114,127),
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
                    // Y축 반전 역매핑: max Z(상단) → y=0
                    int gy = (rows - 1) - Mathf.RoundToInt((b.gridPosition.y - BOARD_CENTER_Z + halfGrid) / cs);
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
                        offsetY + y * cellSize,
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

        // [2026-06-12] MapMaker 'Export Level JSON' 백업(Assets/LevelBackups/level_*.json) 일괄 로드 —
        // 따로 백업해 둔 단일 레벨을 바로 'Episode 파일에 적용'으로 병합(SO 미경유)하는 원클릭 경로.
        private void AddLevelBackups()
        {
            const string backupDir = "Assets/LevelBackups";
            if (!Directory.Exists(backupDir))
            {
                _statusMessage = $"{backupDir} 폴더 없음 — MapMaker 의 'Export Level JSON' 으로 먼저 백업하세요";
                return;
            }
            var files = Directory.GetFiles(backupDir, "level_*.json", SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
            {
                _statusMessage = $"{backupDir} 에 level_*.json 없음";
                return;
            }
            foreach (var f in files.OrderBy(f => f))
                LoadJsonFile(f);
            _statusMessage = $"레벨 백업 {files.Length}개 추가됨 (총 {_entries.Count}개)";
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
                if (TryLoadLevelEpisode(path, jsonText)) return;
                if (TryLoadLevelConfig(path, jsonText)) return;

                entry.json = JsonUtility.FromJson<JsonLevelData>(jsonText);

                if (entry.json == null || entry.json.field_rows <= 0)
                {
                    entry.error = "유효하지 않은 JSON 형식";
                }
                else
                {
                    entry.sourceKind = "Legacy";
                    entry.config = BuildLevelConfig(entry.json);
                    NormalizeImportedLevel(entry.config);
                    CheckConflict(entry);
                }
            }
            catch (Exception ex)
            {
                entry.error = ex.Message;
            }

            entry.importToOld = _globalTargetOld; // ROLLBACK_IMPORTER_OLD_SO_TARGET_20260618: 전역 기본 대상
            _entries.Add(entry);
        }

        private bool TryLoadLevelEpisode(string path, string jsonText)
        {
            if (!jsonText.Contains("\"levels\"", StringComparison.Ordinal))
                return false;

            LevelEpisode episode = JsonUtility.FromJson<LevelEpisode>(jsonText);
            if (episode?.levels == null || episode.levels.Length == 0)
                return false;

            string baseName = Path.GetFileName(path);
            for (int i = 0; i < episode.levels.Length; i++)
            {
                LevelConfig level = episode.levels[i];
                if (level == null) continue;

                NormalizeImportedLevel(level);

                var entry = new ImportEntry
                {
                    filePath = path,
                    fileName = $"{baseName} : Lv{level.levelId:D3}",
                    sourceKind = "Episode",
                    config = level
                };
                entry.importToOld = _globalTargetOld; // ROLLBACK_IMPORTER_OLD_SO_TARGET_20260618: 전역 기본 대상
                CheckConflict(entry);
                _entries.Add(entry);
            }

            return true;
        }

        private bool TryLoadLevelConfig(string path, string jsonText)
        {
            if (!jsonText.Contains("\"levelId\"", StringComparison.Ordinal))
                return false;

            LevelConfig level = JsonUtility.FromJson<LevelConfig>(jsonText);
            if (level == null || level.levelId <= 0)
                return false;

            // ROLLBACK_IMPORT_RUNTIME_LEVEL_JSON:
            // MapMaker/runtime JSON can be a LevelConfig or a LevelEpisode. The importer
            // previously accepted only the old snake_case converter format.
            if (level.rail == null && level.gridRows <= 0 && level.gridCols <= 0 && level.balloons == null)
                return false;

            NormalizeImportedLevel(level);

            var entry = new ImportEntry
            {
                filePath = path,
                fileName = Path.GetFileName(path),
                sourceKind = "LevelConfig",
                config = level
            };
            entry.importToOld = _globalTargetOld; // ROLLBACK_IMPORTER_OLD_SO_TARGET_20260618: 전역 기본 대상
            CheckConflict(entry);
            _entries.Add(entry);
            return true;
        }

        private static void NormalizeImportedLevel(LevelConfig level)
        {
            if (level == null) return;

            if (level.packageId <= 0)
                level.packageId = PackageIdForLevel(level.levelId);

            if (level.positionInPackage <= 0 || level.positionInPackage > LEVELS_PER_PACKAGE)
                level.positionInPackage = PositionInPackage(level.levelId);

            if (level.balloonCount <= 0 && level.balloons != null)
                level.balloonCount = level.balloons.Length;

            if (level.numColors <= 0 && level.balloons != null)
                level.numColors = level.balloons.Select(b => b.color).Distinct().Count();

            if (level.railCapacity <= 0 && level.rail != null && level.rail.slotCount > 0)
                level.railCapacity = level.rail.slotCount;

            if (level.rail != null && level.rail.slotCount <= 0 && level.railCapacity > 0)
                level.rail.slotCount = level.railCapacity;

            if (level.star1Threshold <= 0 && level.balloonCount > 0)
            {
                level.star1Threshold = level.balloonCount * 100;
                level.star2Threshold = Mathf.CeilToInt(level.star1Threshold * 1.5f);
                level.star3Threshold = Mathf.CeilToInt(level.star1Threshold * 2.2f);
            }
        }

        private static int PackageIdForLevel(int levelId)
        {
            if (levelId < 1) return 1;
            return ((levelId - 1) / LEVELS_PER_PACKAGE) + 1;
        }

        private static int PositionInPackage(int levelId)
        {
            if (levelId < 1) return 1;
            return ((levelId - 1) % LEVELS_PER_PACKAGE) + 1;
        }

        private void CheckConflict(ImportEntry entry)
        {
            if (entry.config == null) return;
            int pkg = PackageIdForLevel(entry.config.levelId);
            entry.conflict = GetEpisodeLevelIds(pkg).Contains(entry.config.levelId);
        }

        // 패키지별 기존 levelId 집합 캐시 — 파일마다 episode 를 재로드하던 O(N×M) 제거. ApplyToEpisodes 후 무효화.
        private readonly Dictionary<int, HashSet<int>> _episodeLevelIds = new();

        private HashSet<int> GetEpisodeLevelIds(int pkg)
        {
            if (_episodeLevelIds.TryGetValue(pkg, out var set)) return set;
            set = new HashSet<int>();
            LevelEpisode ep = LoadEpisodeFile(pkg);
            if (ep?.levels != null)
                foreach (var l in ep.levels) if (l != null) set.Add(l.levelId);
            _episodeLevelIds[pkg] = set;
            return set;
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
                Debug.LogWarning($"[PixelForge] 이미지 없음: {imageFileName} — 색상 분포 기반 랜덤 배치");
                return BuildRandomFieldMap(cols, rows, numColors, colorDistribution);
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
                {23, new Color(174/255f, 178/255f, 194/255f)}, {24, new Color(111/255f, 114/255f, 127/255f)},
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

            UnityEngine.Object.DestroyImmediate(tex);
            return field;
        }

        private float ColorDistance(Color a, Color b)
        {
            return (a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b);
        }

        /// <summary>이미지 없을 때 색상 분포 기반 랜덤 FieldMap 생성</summary>
        private int[,] BuildRandomFieldMap(int cols, int rows, int numColors, string colorDistribution)
        {
            var field = new int[cols, rows];
            var colorIds = new List<int>();
            var dist = ParseColorDistribution(colorDistribution);

            if (dist.Count > 0)
                colorIds.AddRange(dist.Keys);
            else
                for (int i = 1; i <= Mathf.Max(numColors, 2); i++) colorIds.Add(i);

            var rng = new System.Random(42);
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                    field[x, y] = colorIds[rng.Next(colorIds.Count)];

            Debug.Log($"[PixelForge] 랜덤 FieldMap 생성: {cols}x{rows}, {colorIds.Count}색");
            return field;
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

                    // [ROLLBACK_IMPORTER_COLOR_RANGE_VALIDATION]
                    // colorId - 1 변환 후 BalloonColors 배열(28 색) 범위 검증.
                    // JSON 의 colorId 가 잘못된 값이면 invalid color 인덱스가 만들어져 다트 hit 매칭 실패 발생.
                    // 롤백 시 이 if 블록 제거.
                    int colorIdx = colorId - 1;
                    if (colorIdx < 0 || colorIdx >= BalloonController.BalloonColors.Length)
                    {
                        Debug.LogWarning($"[Importer] Invalid colorId={colorId} (mapped to {colorIdx}) at field cell ({x},{y}) — out of BalloonColors[0..{BalloonController.BalloonColors.Length - 1}]. Skipping balloon.");
                        continue;
                    }

                    float wx = BOARD_CENTER_X + x * cs - halfGridX;
                    // Y축 반전: FieldMap 텍스트 첫 줄(y=0) → 게임 화면 상단(max Z)
                    float wz = BOARD_CENTER_Z + (rows - 1 - y) * cs - halfGridY;

                    balloons.Add(new BalloonLayout
                    {
                        balloonId = bid++,
                        color = colorIdx, // validated 0-based
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

        // [ROLLBACK_JSON_IMPORTER_GIMMICK_LIFE_FIX]
        // 기존 GetGimmickLife 가 (1) Pin=0 → 다트 0개 → 매칭 hit 불가, (2) Pinata HP hardcoded 2 → JSON hp 무시,
        // (3) Barricade/FrozenDart/ColorCurtain/Surprise/Hidden/Chain/Spawner/FlexTube 등 누락 → default 1
        // → 일부 색 풍선의 다트가 부족하여 import 된 레벨에서 "공격 못 함" 발생.
        // 수정: BalloonLayout.hp 값 우선 사용 + 모든 활성 기믹 반영.
        // 롤백 시 위 두 메서드 원형으로 복원.
        private int[] CountDartsPerColor(BalloonLayout[] balloons, int maxColors)
        {
            int[] counts = new int[maxColors];
            foreach (var b in balloons)
            {
                int life = GetGimmickLife(b.gimmickType, b.hp);
                if (b.color >= 0 && b.color < maxColors)
                    counts[b.color] += life;
            }
            return counts;
        }

        // 풍선 1개당 필요한 다트 수 — 색별 holder magazine 산정에 사용. 0 = 다트 안 듦 (hit 불가).
        // 대소문자 모두 케이스. BalloonController.Gimmick* 상수와 동기화.
        private int GetGimmickLife(string gimmickType, int hp)
        {
            if (string.IsNullOrEmpty(gimmickType) || gimmickType == "none") return 1;
            string g = gimmickType.ToLowerInvariant();
            int hpOrDefault(int dflt) => hp > 0 ? hp : dflt;
            switch (g)
            {
                // 직접 hit 불가 — 다트 0개 필요 (간접 제거).
                case "wall":          return 0;  // indestructible
                case "ice":           return 0;  // indirect only
                case "color_curtain": return 0;  // indirect only

                // FlexTube 는 자체 색별 hit. HP=segment 수.
                case "flextube":      return hpOrDefault(1);

                // HP 기반 기믹 — JSON hp 우선.
                case "pinata":        return hpOrDefault(2);
                case "pinata_box":    return hpOrDefault(2);
                case "barricade":     return hpOrDefault(3);  // Pin 통합 후 색 매칭 + HP
                case "pin":           return hpOrDefault(3);  // legacy — Barricade 로 정규화되지만 import 시 매핑

                // 2-hit (thaw + pop) — 항상 2 발 필요.
                case "frozen_dart":   return 2;

                // 일반 풍선 처럼 1 발이지만 별도 mechanic (concealed visual).
                case "surprise":      return 1;
                case "hidden":        return 1;

                // Holder 전용 기믹 — 풍선엔 적용 안 됨. fallback 1.
                case "chain":         return 1;
                case "spawner_t":     return 1;
                case "spawner_o":     return 1;

                // Lock_Key — dead 처리. 일반 풍선처럼 1 발.
                case "lock_key":      return 1;

                default:              return 1;
            }
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

        #region Episode JSON 에 적용 (LevelDatabase SO 미경유)

        // 단일 episode 스토어 = Assets/EditorData/Episodes/episode_01~15.json (git 교환 + MapMaker 라운드트립).
        //   - 각 패키지=20레벨. pkg1 은 StreamingAssets/episode_01.json 로도 동기화 (앱 번들/오프라인).
        //   - Firestore 업로드는 'BalloonFlow/Level Episodes' 메뉴가 이 폴더에서 seed 로 복사 후 node 업로더 실행.
        // Importer 는 18MB LevelDatabase.asset 을 거치지 않고 영향받는 패키지 파일만 병합·쓰기 → freeze 제거.

        private const int    LEVELS_PER_EPISODE = LEVELS_PER_PACKAGE; // 20
        private const int    TOTAL_EPISODES     = 15;
        private const int    BUNDLED_PACKAGE_ID = 1;
        private const int    EPISODE_VERSION    = 1;
        private const string EPISODES_DIR       = "Assets/EditorData/Master";
        private const string STREAMING_EP1      = "Assets/StreamingAssets/episode_01.json";

        private void ApplyToEpisodes()
        {
            var selected = _entries.Where(e => e.selected && e.config != null && e.error == null).ToList();
            if (selected.Count == 0) { _statusMessage = "적용할 항목 없음"; Repaint(); return; }

            // ROLLBACK_IMPORTER_OLD_SO_TARGET_20260618: old(레거시 SO) 대상은 SO 로, 나머지(ori)는 Episodes JSON 으로.
            int soWritten = 0;
            var oldEntries = selected.Where(e => e.importToOld).ToList();
            if (oldEntries.Count > 0) soWritten = WriteLevelsToLegacySO(oldEntries);

            var toApply = selected.Where(e => !e.importToOld).ToList();
            if (toApply.Count == 0)
            {
                _episodeLevelIds.Clear();
                foreach (var e in _entries) CheckConflict(e);
                _statusMessage = $"완료 — old(SO) {soWritten}개 적용 (ori 대상 없음)";
                Debug.Log($"[LevelJsonImporter] {_statusMessage}");
                EditorUtility.DisplayDialog("적용 완료", $"old(LevelDatabase.asset SO)에 {soWritten}개 레벨 적용 완료.", "OK");
                Repaint();
                return;
            }

            // 패키지(=에피소드)별 그룹. levelId 로 패키지/포지션 결정 (JSON 의 packageId 는 신뢰하지 않음).
            var byPkg = new Dictionary<int, List<LevelConfig>>();
            int outOfRange = 0;
            foreach (var e in toApply)
            {
                int levelId = e.config.levelId;
                int pkg = PackageIdForLevel(levelId);
                if (levelId < 1 || pkg < 1 || pkg > TOTAL_EPISODES)
                {
                    Debug.LogWarning($"[Importer] levelId={levelId} → pkg {pkg} 범위 밖(1~{TOTAL_EPISODES}). skip.");
                    outOfRange++;
                    continue;
                }
                if (!byPkg.TryGetValue(pkg, out var list)) { list = new List<LevelConfig>(); byPkg[pkg] = list; }
                list.Add(e.config);
            }

            int added = 0, overwritten = 0, skipped = 0;
            var touchedPkgs = new List<int>();
            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            foreach (var kv in byPkg.OrderBy(k => k.Key))
            {
                int pkg = kv.Key;

                // 기존 episode 로드 (병합 기준). 없으면 새 패키지로 시작.
                LevelEpisode ep = LoadEpisodeFile(pkg);
                var levels = (ep?.levels != null)
                    ? new List<LevelConfig>(ep.levels.Where(l => l != null))
                    : new List<LevelConfig>();

                // 덮어쓰기 전 백업 (기존 파일 존재 시).
                BackupEpisodeFile(pkg, ts);

                var idxById = new Dictionary<int, int>();
                for (int i = 0; i < levels.Count; i++) idxById[levels[i].levelId] = i;

                foreach (var lv in kv.Value)
                {
                    if (idxById.TryGetValue(lv.levelId, out int idx))
                    {
                        if (_overwriteConflicts) { levels[idx] = lv; overwritten++; }
                        else { skipped++; }
                    }
                    else
                    {
                        levels.Add(lv);
                        idxById[lv.levelId] = levels.Count - 1;
                        added++;
                    }
                }

                // levelId 정렬 + packageId/positionInPackage 정규화.
                levels.Sort((a, b) => a.levelId.CompareTo(b.levelId));
                for (int i = 0; i < levels.Count; i++)
                {
                    levels[i].packageId = pkg;
                    levels[i].positionInPackage = PositionInPackage(levels[i].levelId);
                }

                ValidateEpisodeContiguity(pkg, levels);
                WriteEpisodeFile(pkg, levels);
                touchedPkgs.Add(pkg);
            }

            // 충돌 캐시 무효화 + 재계산.
            _episodeLevelIds.Clear();
            foreach (var e in _entries) CheckConflict(e);

            string pkgList = touchedPkgs.Count > 0 ? string.Join(", ", touchedPkgs) : "(없음)";
            bool needUpload = touchedPkgs.Any(p => p != BUNDLED_PACKAGE_ID);

            _statusMessage = $"완료 — 추가:{added} 덮어쓰기:{overwritten} 건너뜀:{skipped}" +
                             (outOfRange > 0 ? $" 범위밖:{outOfRange}" : "") + $"  (episode {pkgList})" +
                             (soWritten > 0 ? $" · old(SO):{soWritten}" : ""); // ROLLBACK_IMPORTER_OLD_SO_TARGET_20260618
            Debug.Log($"[LevelJsonImporter] {_statusMessage}");

            EditorUtility.DisplayDialog("Episode 적용 완료",
                $"추가: {added}\n덮어쓰기: {overwritten}\n건너뜀: {skipped}" +
                (outOfRange > 0 ? $"\n범위 밖(skip): {outOfRange}" : "") +
                $"\n\n갱신된 episode: {pkgList}\n" +
                (needUpload
                    ? "\npkg 2~15 는 Firestore 반영을 위해\n'BalloonFlow/Level Episodes/Export & Upload to Firestore'\n또는 node upload-episodes.js 를 실행하세요."
                    : "\npkg1 은 StreamingAssets/episode_01.json (번들) 갱신 완료."),
                "OK");

            Repaint();
        }

        // ROLLBACK_IMPORTER_OLD_SO_TARGET_20260618: old 대상 entry 들을 레거시 SO(LevelDatabase.asset)에 병합·저장.
        //   기존 importer 는 18MB SO 를 피해 Episodes JSON 만 썼지만, 사용자 요청(old=SO 쓰기가능 + 특정 레벨 old import)에
        //   따라 old 지정 레벨만 SO 로 직접 기록한다(SetDirty+SaveAssets). _overwriteConflicts 동일 적용. 반환=적용 레벨 수.
        private int WriteLevelsToLegacySO(List<ImportEntry> entries)
        {
            var db = AssetDatabase.LoadAssetAtPath<LevelDatabase>(LEGACY_SO_PATH);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<LevelDatabase>();
                AssetDatabase.CreateAsset(db, LEGACY_SO_PATH);
                Debug.Log($"[Importer] old SO 신규 생성: {LEGACY_SO_PATH}");
            }
            BackupLegacySO();

            var levels = db.levels != null ? new List<LevelConfig>(db.levels) : new List<LevelConfig>();
            var idxById = new Dictionary<int, int>();
            for (int i = 0; i < levels.Count; i++) if (levels[i] != null) idxById[levels[i].levelId] = i;

            int applied = 0, skipped = 0;
            foreach (var e in entries)
            {
                var lv = e.config;
                if (lv == null) continue;
                if (idxById.TryGetValue(lv.levelId, out int idx))
                {
                    if (_overwriteConflicts) { levels[idx] = lv; applied++; }
                    else skipped++;
                }
                else { levels.Add(lv); idxById[lv.levelId] = levels.Count - 1; applied++; }
            }
            levels.Sort((a, b) => a.levelId.CompareTo(b.levelId));
            db.levels = levels.ToArray();
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Importer] old(SO) {LEGACY_SO_PATH} ← 적용:{applied} 건너뜀:{skipped} (총 {levels.Count}레벨)");
            return applied;
        }

        private void BackupLegacySO()
        {
            if (!File.Exists(LEGACY_SO_PATH)) return;
            const string backupDir = "Assets/LevelBackups";
            Directory.CreateDirectory(backupDir);
            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            try { File.Copy(LEGACY_SO_PATH, $"{backupDir}/LevelDatabase_{ts}.asset", true); }
            catch (Exception ex) { Debug.LogWarning($"[Importer] old SO 백업 실패: {ex.Message}"); }
        }

        private static string EpisodePath(int pkg) => $"{EPISODES_DIR}/episode_{pkg:D2}.json";

        private LevelEpisode LoadEpisodeFile(int pkg)
        {
            string path = EpisodePath(pkg);
            if (!File.Exists(path)) return null;
            try
            {
                return JsonUtility.FromJson<LevelEpisode>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogError($"[Importer] episode_{pkg:D2}.json 읽기 실패: {e.Message}");
                return null;
            }
        }

        private void WriteEpisodeFile(int pkg, List<LevelConfig> levels)
        {
            var ep = new LevelEpisode
            {
                packageId  = pkg,
                levelCount = levels.Count,
                version    = EPISODE_VERSION,
                levels     = levels.ToArray()
            };
            string json = JsonUtility.ToJson(ep, false); // 업로드/런타임과 동일 포맷

            Directory.CreateDirectory(EPISODES_DIR);
            File.WriteAllText(EpisodePath(pkg), json);
            AssetDatabase.ImportAsset(EpisodePath(pkg)); // EditorData 하위 → 전체 Refresh 대신 해당 파일만
            Debug.Log($"[Importer] {EpisodePath(pkg)} 갱신 ({levels.Count}레벨, {json.Length} bytes)");

            if (pkg == BUNDLED_PACKAGE_ID)
            {
                string streamDir = Path.GetDirectoryName(STREAMING_EP1);
                if (!string.IsNullOrEmpty(streamDir)) Directory.CreateDirectory(streamDir);
                File.WriteAllText(STREAMING_EP1, json);
                AssetDatabase.ImportAsset(STREAMING_EP1);
                Debug.Log("[Importer] pkg1 → StreamingAssets/episode_01.json (번들) 동기화");
            }
        }

        private void BackupEpisodeFile(int pkg, string ts)
        {
            string src = EpisodePath(pkg);
            if (!File.Exists(src)) return;
            const string backupDir = "Assets/LevelBackups";
            Directory.CreateDirectory(backupDir);
            File.Copy(src, $"{backupDir}/episode_{pkg:D2}_{ts}.json", true);
        }

        /// <summary>
        /// 런타임 LevelEpisodeService.GetLevel 은 levels[positionInPackage-1] 로 직접 인덱싱한다.
        /// 따라서 position 이 1..N 연속·정렬돼 있어야 정상 매핑. 어긋나면 경고 (저장은 진행).
        /// </summary>
        private void ValidateEpisodeContiguity(int pkg, List<LevelConfig> levels)
        {
            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i].positionInPackage != i + 1)
                {
                    Debug.LogWarning($"[Importer] episode_{pkg:D2}: position 불연속 — index {i} = level {levels[i].levelId} " +
                        $"(position={levels[i].positionInPackage}, 기대={i + 1}). 런타임 GetLevel 매핑이 어긋날 수 있음 — 빠진 levelId 를 채우세요.");
                    break;
                }
            }
            if (levels.Count != LEVELS_PER_EPISODE)
                Debug.LogWarning($"[Importer] episode_{pkg:D2}: 레벨 {levels.Count}개 (정상 {LEVELS_PER_EPISODE}). 미완성 패키지일 수 있음.");
        }

        #endregion
    }
}
#endif
