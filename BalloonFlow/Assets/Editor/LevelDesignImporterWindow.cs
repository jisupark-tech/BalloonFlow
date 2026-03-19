#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEngine;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// BalloonFlow > Import Level Design
    /// Reads CSV/XLSX level design spreadsheet and generates LevelDatabase.asset.
    /// Parses [Composition], [Shape], [Pattern], [Empty] tags from designer_note
    /// to drive procedural balloon placement, holder packing, and rail generation.
    /// </summary>
    public class LevelDesignImporterWindow : EditorWindow
    {
        #region Constants

        private const float BOARD_WORLD_SIZE = 8f;
        private const float BOARD_CENTER_X   = 0f;
        private const float BOARD_CENTER_Z   = 2f;
        private const float RAIL_PADDING     = 1.5f;

        // Magazine sizes allowed by design rules
        private static readonly int[] MAGAZINE_SIZES = { 5, 10, 20, 30, 40, 50 };

        #endregion

        #region Data Structures

        /// <summary>One row parsed from the spreadsheet.</summary>
        private class LevelRow
        {
            public int    levelNumber;
            public string levelId;
            public int    pkg;
            public int    pos;
            public int    chapter;
            public string purposeType;
            public float  targetCR;
            public float  targetAttempts;
            public int    numColors;
            public string colorDistribution;
            public int    fieldRows;
            public int    fieldColumns;
            public int    totalCells;
            public int    railCapacity;
            public string railCapacityTier;
            public int    queueColumns;
            public int    queueRows;
            // Gimmicks
            public int gimmickHidden, gimmickChain, gimmickPinata, gimmickSpawnerT;
            public int gimmickPin, gimmickLockKey, gimmickSurprise, gimmickWall;
            public int gimmickSpawnerO, gimmickPinataBox, gimmickIce, gimmickFrozenDart, gimmickCurtain;
            public int    totalDarts;
            public string dartCapacityRange;
            public string emotionCurve;
            public string designerNote;
            public string pixelArtSource;
            // Parsed tags
            public string tagComposition;
            public string tagShape;
            public string tagPattern;
            public string tagGimmick;
            public string tagEmpty;
        }

        #endregion

        #region Window State

        private string      _filePath = "";
        private List<LevelRow> _rows = new List<LevelRow>();
        private LevelConfig[]  _generatedConfigs;
        private int         _selectedLevel = -1;
        private Vector2     _listScroll;
        private Vector2     _previewScroll;
        private string      _statusMessage = "";
        private bool        _hasData;
        private float       _previewZoom = 1f;
        // Generation stats
        private int         _successCount;
        private int         _errorCount;
        private List<string> _errors = new List<string>();

        #endregion

        #region Menu

        [MenuItem("BalloonFlow/Import Level Design", false, 50)]
        public static void ShowWindow()
        {
            var win = GetWindow<LevelDesignImporterWindow>("Level Importer");
            win.minSize = new Vector2(900, 600);
        }

        #endregion

        #region GUI

        private void OnGUI()
        {
            try
            {
                // Top toolbar
                DrawToolbar();

                EditorGUILayout.BeginHorizontal();
                {
                    // Left panel: level list
                    EditorGUILayout.BeginVertical(GUILayout.Width(260));
                    DrawLevelList();
                    EditorGUILayout.EndVertical();

                    // Right panel: preview
                    EditorGUILayout.BeginVertical();
                    DrawPreview();
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndHorizontal();

                // Bottom status
                EditorGUILayout.LabelField(_statusMessage, EditorStyles.helpBox);
            }
            catch (Exception ex)
            {
                _selectedLevel = -1;
                Debug.LogError($"[LevelImporter] GUI error: {ex.Message}");
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // File picker
            if (GUILayout.Button("Open File...", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                string path = EditorUtility.OpenFilePanel(
                    "Select Level Design File", "", "xlsx,csv");
                if (!string.IsNullOrEmpty(path))
                {
                    _filePath = path;
                    LoadFile();
                }
            }

            // Show current file
            GUILayout.Label(string.IsNullOrEmpty(_filePath) ? "(no file)" : Path.GetFileName(_filePath),
                EditorStyles.toolbarTextField, GUILayout.MinWidth(200));

            GUILayout.FlexibleSpace();

            // Generate button
            GUI.enabled = _hasData && _rows.Count > 0;
            if (GUILayout.Button("Generate All", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                GenerateAll();
            }

            // Save button
            GUI.enabled = _generatedConfigs != null && _generatedConfigs.Length > 0;
            if (GUILayout.Button("Save to Asset", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                SaveToAsset();
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawLevelList()
        {
            EditorGUILayout.LabelField($"Levels ({_rows.Count})", EditorStyles.boldLabel);

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                bool selected = _selectedLevel == i;
                bool generated = _generatedConfigs != null && i < _generatedConfigs.Length
                    && _generatedConfigs[i] != null;

                string label = $"Lv{row.levelNumber:D3}  {row.purposeType ?? ""}  " +
                    $"{row.fieldRows}x{row.fieldColumns}  C{row.numColors}";

                var style = new GUIStyle(selected ? EditorStyles.selectionRect : EditorStyles.label);
                if (generated)
                    style.normal.textColor = new Color(0.3f, 0.8f, 0.3f);

                if (GUILayout.Button(label, style))
                {
                    _selectedLevel = i;
                    Repaint();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawPreview()
        {
            if (_selectedLevel < 0 || _generatedConfigs == null
                || _selectedLevel >= _generatedConfigs.Length
                || _generatedConfigs[_selectedLevel] == null
                || _rows == null || _selectedLevel >= _rows.Count)
            {
                EditorGUILayout.HelpBox(
                    _selectedLevel >= 0 ? "Generate levels first (click 'Generate All')" :
                    "Select a level from the list", MessageType.Info);
                return;
            }

            var config = _generatedConfigs[_selectedLevel];
            var row = _rows[_selectedLevel];

            // Info header
            EditorGUILayout.LabelField($"Level {config.levelId} — " +
                $"{config.gridCols}x{config.gridRows}  Balloons={config.balloonCount}  " +
                $"Holders={config.holders?.Length ?? 0}  Darts={row.totalDarts}",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"[Composition] {row.tagComposition}  " +
                $"[Shape] {row.tagShape}  [Empty] {row.tagEmpty}");

            // Zoom slider
            _previewZoom = EditorGUILayout.Slider("Zoom", _previewZoom, 0.5f, 3f);

            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll);

            float cellSize = 16f * _previewZoom;
            float offsetX = 20f;
            float offsetY = 10f;
            int cols = config.gridCols;
            int rows = config.gridRows;

            // Reserve space
            GUILayoutUtility.GetRect(
                cols * cellSize + offsetX * 2,
                rows * cellSize + offsetY * 2 + 80);

            // Color palette
            Color[] palette = {
                new Color(0.9f, 0.3f, 0.3f), // 0 red
                new Color(0.3f, 0.5f, 0.9f), // 1 blue
                new Color(0.3f, 0.8f, 0.3f), // 2 green
                new Color(0.9f, 0.8f, 0.2f), // 3 yellow
                new Color(0.8f, 0.4f, 0.8f), // 4 purple
                new Color(0.9f, 0.6f, 0.2f), // 5 orange
                new Color(0.2f, 0.8f, 0.8f), // 6 cyan
                new Color(0.6f, 0.4f, 0.2f), // 7 brown
                new Color(0.9f, 0.5f, 0.6f), // 8 pink
                new Color(0.5f, 0.5f, 0.5f), // 9 gray
                new Color(0.1f, 0.1f, 0.1f), // 10 black
            };

            // Draw grid background
            EditorGUI.DrawRect(new Rect(offsetX - 2, offsetY - 2,
                cols * cellSize + 4, rows * cellSize + 4),
                new Color(0.15f, 0.15f, 0.18f));

            // Build position-to-balloon map (using grid indices)
            var balloonMap = new Dictionary<Vector2Int, BalloonLayout>();
            if (config.balloons != null)
            {
                float cs = BOARD_WORLD_SIZE / Mathf.Max(cols, rows);
                float halfGrid = BOARD_WORLD_SIZE * 0.5f - cs * 0.5f;

                foreach (var b in config.balloons)
                {
                    // Reverse world→grid: gx = (wx - BOARD_CENTER_X + halfGrid) / cs
                    int gx = Mathf.RoundToInt((b.gridPosition.x - BOARD_CENTER_X + halfGrid) / cs);
                    int gy = Mathf.RoundToInt((b.gridPosition.y - BOARD_CENTER_Z + halfGrid) / cs);
                    var key = new Vector2Int(gx, gy);
                    if (!balloonMap.ContainsKey(key))
                        balloonMap[key] = b;
                }
            }

            // Draw cells
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    var rect = new Rect(
                        offsetX + x * cellSize,
                        offsetY + (rows - 1 - y) * cellSize, // flip Y for display
                        cellSize - 1, cellSize - 1);

                    var key = new Vector2Int(x, y);
                    if (balloonMap.TryGetValue(key, out var balloon))
                    {
                        int ci = Mathf.Clamp(balloon.color, 0, palette.Length - 1);
                        EditorGUI.DrawRect(rect, palette[ci]);

                        // Gimmick marker
                        if (!string.IsNullOrEmpty(balloon.gimmickType))
                        {
                            var markerRect = new Rect(rect.x + 1, rect.y + 1, 6, 6);
                            EditorGUI.DrawRect(markerRect, Color.white);
                        }
                    }
                    else
                    {
                        EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.25f));
                    }
                }
            }

            // Holder summary below grid
            float holderY = offsetY + rows * cellSize + 10;
            if (config.holders != null)
            {
                GUI.Label(new Rect(offsetX, holderY, 300, 16), "Holders:");
                holderY += 18;
                for (int i = 0; i < Mathf.Min(config.holders.Length, 40); i++)
                {
                    var h = config.holders[i];
                    int ci = Mathf.Clamp(h.color, 0, palette.Length - 1);
                    float hx = offsetX + (i % 20) * 30;
                    float hy = holderY + (i / 20) * 20;
                    var hRect = new Rect(hx, hy, 28, 16);
                    EditorGUI.DrawRect(hRect, palette[ci]);
                    GUI.Label(hRect, h.magazineCount.ToString(),
                        new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter,
                            normal = { textColor = Color.white } });
                }
            }

            EditorGUILayout.EndScrollView();
        }

        #endregion

        #region File Loading

        private void LoadFile()
        {
            _rows.Clear();
            _generatedConfigs = null;
            _selectedLevel = -1;
            _hasData = false;

            try
            {
                string ext = Path.GetExtension(_filePath).ToLowerInvariant();
                List<string[]> rawRows;

                if (ext == ".xlsx")
                    rawRows = ParseXlsx(_filePath);
                else
                    rawRows = ParseCsv(_filePath);

                if (rawRows == null || rawRows.Count < 4)
                {
                    _statusMessage = "Error: File has too few rows.";
                    return;
                }

                // Row 0 = group headers, Row 1 = column names, Row 2 = descriptions, Row 3+ = data
                // Find column indices from row 1
                var header = rawRows[1];
                var colMap = new Dictionary<string, int>();
                for (int i = 0; i < header.Length; i++)
                {
                    string key = header[i]?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(key) && !colMap.ContainsKey(key))
                        colMap[key] = i;
                }

                // Parse data rows (row 3+)
                for (int r = 3; r < rawRows.Count; r++)
                {
                    var cells = rawRows[r];
                    int lvNum = GetInt(cells, colMap, "level_number");
                    if (lvNum <= 0) continue; // skip empty rows

                    var row = new LevelRow
                    {
                        levelNumber       = lvNum,
                        levelId           = GetStr(cells, colMap, "level_id"),
                        pkg               = GetInt(cells, colMap, "pkg"),
                        pos               = GetInt(cells, colMap, "pos"),
                        chapter           = GetInt(cells, colMap, "chapter"),
                        purposeType       = GetStr(cells, colMap, "purpose_type"),
                        targetCR          = GetFloat(cells, colMap, "target_cr"),
                        targetAttempts    = GetFloat(cells, colMap, "target_attempts"),
                        numColors         = GetInt(cells, colMap, "num_colors"),
                        colorDistribution = GetStr(cells, colMap, "color_distribution"),
                        fieldRows         = GetInt(cells, colMap, "field_rows"),
                        fieldColumns      = GetInt(cells, colMap, "field_columns"),
                        totalCells        = GetInt(cells, colMap, "total_cells"),
                        railCapacity      = GetInt(cells, colMap, "rail_capacity"),
                        railCapacityTier  = GetStr(cells, colMap, "rail_capacity_tier"),
                        queueColumns      = GetInt(cells, colMap, "queue_columns"),
                        queueRows         = GetInt(cells, colMap, "queue_rows"),
                        gimmickHidden     = GetInt(cells, colMap, "gimmick_hidden"),
                        gimmickChain      = GetInt(cells, colMap, "gimmick_chain"),
                        gimmickPinata     = GetInt(cells, colMap, "gimmick_pinata"),
                        gimmickSpawnerT   = GetInt(cells, colMap, "gimmick_spawner_t"),
                        gimmickPin        = GetInt(cells, colMap, "gimmick_pin"),
                        gimmickLockKey    = GetInt(cells, colMap, "gimmick_lock_key"),
                        gimmickSurprise   = GetInt(cells, colMap, "gimmick_surprise"),
                        gimmickWall       = GetInt(cells, colMap, "gimmick_wall"),
                        gimmickSpawnerO   = GetInt(cells, colMap, "gimmick_spawner_o"),
                        gimmickPinataBox  = GetInt(cells, colMap, "gimmick_pinata_box"),
                        gimmickIce        = GetInt(cells, colMap, "gimmick_ice"),
                        gimmickFrozenDart = GetInt(cells, colMap, "gimmick_frozen_dart"),
                        gimmickCurtain    = GetInt(cells, colMap, "gimmick_curtain"),
                        totalDarts        = GetInt(cells, colMap, "total_darts"),
                        dartCapacityRange = GetStr(cells, colMap, "dart_capacity_range"),
                        emotionCurve      = GetStr(cells, colMap, "emotion_curve"),
                        designerNote      = GetStr(cells, colMap, "designer_note"),
                        pixelArtSource    = GetStr(cells, colMap, "pixel_art_source"),
                    };

                    // Parse designer_note tags
                    ParseDesignerTags(row);

                    _rows.Add(row);
                }

                _hasData = _rows.Count > 0;
                _statusMessage = $"Loaded {_rows.Count} levels from {Path.GetFileName(_filePath)}";
            }
            catch (Exception ex)
            {
                _statusMessage = $"Error loading file: {ex.Message}";
                Debug.LogException(ex);
            }
        }

        #endregion

        #region CSV Parser

        private List<string[]> ParseCsv(string path)
        {
            var result = new List<string[]>();
            var lines = File.ReadAllLines(path, Encoding.UTF8);

            foreach (var line in lines)
            {
                result.Add(ParseCsvLine(line));
            }
            return result;
        }

        private string[] ParseCsvLine(string line)
        {
            var fields = new List<string>();
            bool inQuotes = false;
            var sb = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            fields.Add(sb.ToString());
            return fields.ToArray();
        }

        #endregion

        #region XLSX Parser

        private List<string[]> ParseXlsx(string path)
        {
            var result = new List<string[]>();

            using (var zip = ZipFile.OpenRead(path))
            {
                // Read shared strings
                var sharedStrings = new List<string>();
                var ssEntry = zip.GetEntry("xl/sharedStrings.xml");
                if (ssEntry != null)
                {
                    using (var stream = ssEntry.Open())
                    {
                        var doc = new XmlDocument();
                        doc.Load(stream);
                        var ns = new XmlNamespaceManager(doc.NameTable);
                        ns.AddNamespace("s", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
                        var siNodes = doc.SelectNodes("//s:si", ns);
                        if (siNodes != null)
                        {
                            foreach (XmlNode si in siNodes)
                            {
                                sharedStrings.Add(si.InnerText);
                            }
                        }
                    }
                }

                // Read first sheet (Level Design)
                var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml");
                if (sheetEntry == null)
                {
                    _statusMessage = "Error: Cannot find sheet1.xml in XLSX";
                    return result;
                }

                using (var stream = sheetEntry.Open())
                {
                    var doc = new XmlDocument();
                    doc.Load(stream);
                    var ns = new XmlNamespaceManager(doc.NameTable);
                    ns.AddNamespace("s", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");

                    var rowNodes = doc.SelectNodes("//s:sheetData/s:row", ns);
                    if (rowNodes == null) return result;

                    foreach (XmlNode rowNode in rowNodes)
                    {
                        var cells = rowNode.SelectNodes("s:c", ns);
                        if (cells == null) continue;

                        // Determine max column from cell references
                        var rowData = new Dictionary<int, string>();
                        int maxCol = 0;

                        foreach (XmlNode cell in cells)
                        {
                            string cellRef = cell.Attributes?["r"]?.Value ?? "";
                            int colIdx = CellRefToColIndex(cellRef);
                            if (colIdx > maxCol) maxCol = colIdx;

                            string type = cell.Attributes?["t"]?.Value ?? "";
                            var vNode = cell.SelectSingleNode("s:v", ns);
                            string val = vNode?.InnerText ?? "";

                            if (type == "s" && int.TryParse(val, out int ssIdx)
                                && ssIdx >= 0 && ssIdx < sharedStrings.Count)
                            {
                                val = sharedStrings[ssIdx];
                            }

                            rowData[colIdx] = val;
                        }

                        var arr = new string[maxCol + 1];
                        for (int i = 0; i <= maxCol; i++)
                            arr[i] = rowData.ContainsKey(i) ? rowData[i] : "";

                        result.Add(arr);
                    }
                }
            }

            return result;
        }

        private int CellRefToColIndex(string cellRef)
        {
            int col = 0;
            foreach (char c in cellRef)
            {
                if (c >= 'A' && c <= 'Z')
                    col = col * 26 + (c - 'A' + 1);
                else
                    break;
            }
            return col - 1;
        }

        #endregion

        #region Tag Parsing

        private void ParseDesignerTags(LevelRow row)
        {
            string note = row.designerNote ?? "";
            row.tagComposition = ExtractTag(note, "Composition") ?? "random";
            row.tagShape       = ExtractTag(note, "Shape") ?? "꽉찬 사각형";
            row.tagPattern     = ExtractTag(note, "Pattern") ?? "";
            row.tagGimmick     = ExtractTag(note, "Gimmick") ?? "없음";
            row.tagEmpty       = ExtractTag(note, "Empty") ?? "없음";
        }

        private string ExtractTag(string text, string tagName)
        {
            var match = Regex.Match(text, @"\[" + tagName + @"\]\s*([^\[\]]+)");
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        #endregion

        #region Cell Helpers

        private static string GetStr(string[] cells, Dictionary<string, int> colMap, string key)
        {
            if (!colMap.TryGetValue(key, out int idx) || idx >= cells.Length) return "";
            return cells[idx]?.Trim() ?? "";
        }

        private static int GetInt(string[] cells, Dictionary<string, int> colMap, string key)
        {
            string s = GetStr(cells, colMap, key);
            if (string.IsNullOrEmpty(s)) return 0;
            // Handle "1.0" style from Excel
            if (float.TryParse(s, out float f)) return Mathf.RoundToInt(f);
            return 0;
        }

        private static float GetFloat(string[] cells, Dictionary<string, int> colMap, string key)
        {
            string s = GetStr(cells, colMap, key);
            if (string.IsNullOrEmpty(s)) return 0f;
            float.TryParse(s, out float f);
            return f;
        }

        #endregion

        #region Generation — Main

        private void GenerateAll()
        {
            _generatedConfigs = new LevelConfig[_rows.Count];
            _successCount = 0;
            _errorCount = 0;
            _errors.Clear();

            for (int i = 0; i < _rows.Count; i++)
            {
                if (i % 10 == 0)
                    EditorUtility.DisplayProgressBar("Generating Levels",
                        $"Level {_rows[i].levelNumber}...", (float)i / _rows.Count);

                try
                {
                    _generatedConfigs[i] = BuildLevelFromRow(_rows[i]);
                    _successCount++;
                }
                catch (Exception ex)
                {
                    _errors.Add($"Lv{_rows[i].levelNumber}: {ex.Message}");
                    _errorCount++;
                }
            }

            EditorUtility.ClearProgressBar();
            _statusMessage = $"Generated {_successCount} levels, {_errorCount} errors.";

            if (_errors.Count > 0)
            {
                Debug.LogWarning("[LevelImporter] Errors:\n" + string.Join("\n", _errors));
            }

            if (_selectedLevel < 0 && _rows.Count > 0) _selectedLevel = 0;
            Repaint();
        }

        private LevelConfig BuildLevelFromRow(LevelRow row)
        {
            var rng = new System.Random(row.levelNumber * 37 + 13);

            int gridRows = Mathf.Max(row.fieldRows, 4);
            int gridCols = Mathf.Max(row.fieldColumns, 4);
            int maxDim = Mathf.Max(gridCols, gridRows);
            float cellSpacing = BOARD_WORLD_SIZE / maxDim;
            float balloonScale = cellSpacing * 0.85f;

            // 1) Parse color distribution → {colorIndex: count}
            var colorDist = ParseColorDistribution(row.colorDistribution, row.numColors);
            int totalBalloons = 0;
            foreach (var kv in colorDist) totalBalloons += kv.Value;

            // 2) Priority-based cell selection (replaces shape mask + adjust)
            //    Every cell gets a priority score; top totalBalloons cells are selected.
            float fillRatio = (float)totalBalloons / (gridCols * gridRows);
            float[,] priority = BuildPriorityMap(gridCols, gridRows, fillRatio,
                row.tagShape, row.tagEmpty, rng);

            bool[,] finalMask = SelectTopCells(priority, gridCols, gridRows, totalBalloons);
            int activeCells = CountActive(finalMask, gridCols, gridRows);

            // 3) Place balloons with composition rule
            var balloons = PlaceBalloons(finalMask, gridCols, gridRows, cellSpacing,
                colorDist, row.numColors, row.tagComposition, row.tagPattern, rng);

            // 4) Assign gimmicks
            string[] gimmickTypes = AssignGimmicks(balloons, row, rng);

            // 5) Build holders from color_distribution + dart_capacity_range
            //    Use queue_rows to cap total holder count
            int[] dartsPerColor = CountDartsPerColor(balloons, row.numColors);
            int[] allowedMags = ParseDartCapacityRange(row.dartCapacityRange, row.railCapacity);
            int maxHolders = row.queueColumns * Mathf.Max(row.queueRows, 1);
            var holders = BuildHolders(dartsPerColor, allowedMags, row.queueColumns, maxHolders, rng);

            // 6) Generate rail
            var rail = GenerateRail(gridCols, gridRows, cellSpacing, row.queueColumns, row.railCapacity);

            // 7) Generate conveyor positions
            var conveyorPositions = GenerateConveyorPositions(gridCols, gridRows);

            // 8) Star thresholds
            int star1 = activeCells * 100;

            // 9) Map purpose
            DifficultyPurpose purpose = MapPurpose(row.purposeType);

            return new LevelConfig
            {
                levelId           = row.levelNumber,
                packageId         = row.pkg,
                positionInPackage = row.pos,
                railCapacity      = row.railCapacity,
                numColors         = row.numColors,
                balloonCount      = activeCells,
                balloonScale      = balloonScale,
                queueColumns      = Mathf.Max(row.queueColumns, 2),
                targetClearRate   = row.targetCR / 100f,
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

        #region Color Distribution Parsing

        /// <summary>
        /// Parses "c1:75,c2:75" → {0: 75, 1: 75}
        /// </summary>
        private Dictionary<int, int> ParseColorDistribution(string dist, int numColors)
        {
            var result = new Dictionary<int, int>();
            if (string.IsNullOrEmpty(dist))
            {
                // Fallback: even distribution, 20 per color
                for (int i = 0; i < numColors; i++)
                    result[i] = 20;
                return result;
            }

            var matches = Regex.Matches(dist, @"c(\d+)\s*:\s*(\d+)");
            foreach (Match m in matches)
            {
                int colorIdx = int.Parse(m.Groups[1].Value) - 1; // c1 → index 0
                int count = int.Parse(m.Groups[2].Value);
                result[colorIdx] = count;
            }

            return result;
        }

        private int[] ParseDartCapacityRange(string range, int railCapacity)
        {
            if (string.IsNullOrEmpty(range))
            {
                // Default based on rail capacity (Design Rules)
                if (railCapacity <= 50) return new[] { 5, 10, 20 };
                if (railCapacity <= 100) return new[] { 5, 10, 20, 30 };
                if (railCapacity <= 150) return new[] { 5, 10, 20, 30, 40 };
                return new[] { 5, 10, 20, 30, 40, 50 };
            }

            return range.Split(',')
                .Select(s => { int.TryParse(s.Trim(), out int v); return v; })
                .Where(v => v > 0)
                .OrderByDescending(v => v)
                .ToArray();
        }

        #endregion

        #region Priority Map — Cell Selection System

        /// <summary>
        /// Builds a priority value for every cell. Higher = more likely to have a balloon.
        /// Shape defines the base priority (1.0 inside shape, 0.0 outside).
        /// Empty pattern subtracts priority in designated areas.
        /// Random jitter prevents row/column-aligned artifacts when trimming.
        /// </summary>
        private float[,] BuildPriorityMap(int cols, int rows, float fillRatio,
            string shapeTag, string emptyTag, System.Random rng)
        {
            float[,] p = new float[cols, rows];
            float cx = (cols - 1) * 0.5f;
            float cy = (rows - 1) * 0.5f;
            float rx = Mathf.Max(cx, 1f);
            float ry = Mathf.Max(cy, 1f);

            // Step 1: Shape priority (0.0 ~ 1.0)
            string shape = (shapeTag ?? "").Trim();
            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                    p[x, y] = GetShapePriority(x, y, cols, rows, cx, cy, rx, ry, shape, fillRatio);

            // Step 2: Empty pattern penalty
            ApplyEmptyPenalty(p, cols, rows, emptyTag);

            // Step 3: Random jitter (±0.05) to break ties — prevents row/col artifacts
            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                    p[x, y] += (float)(rng.NextDouble() * 0.1 - 0.05);

            return p;
        }

        /// <summary>
        /// Returns 0.0~1.0 shape priority for a cell. Shapes are scaled to match fillRatio.
        /// Instead of binary in/out, uses smooth falloff so trimming looks natural.
        /// </summary>
        private float GetShapePriority(int x, int y, int cols, int rows,
            float cx, float cy, float rx, float ry, string shape, float fillRatio)
        {
            float nx = (x - cx) / rx; // normalized -1..1
            float ny = (y - cy) / ry;
            float dist = Mathf.Sqrt(nx * nx + ny * ny);
            float manhattan = Mathf.Abs(nx) + Mathf.Abs(ny);

            // Scale factor: shapes expand to capture more cells when fillRatio is high
            // base shape covers ~78% (circle) to ~100% (rect), scale to reach fillRatio
            float scale = Mathf.Lerp(1.0f, 1.4f, fillRatio);

            if (shape.Contains("꽉찬") || shape.Contains("사각형") || string.IsNullOrEmpty(shape))
            {
                // Full rect: all cells equal, slight center bias
                return 1.0f - dist * 0.1f;
            }
            if (shape.Contains("원형"))
            {
                float r = dist / scale;
                return r <= 1f ? (1f - r * 0.3f) : Mathf.Max(0f, 1f - (r - 1f) * 3f);
            }
            if (shape.Contains("다이아몬드"))
            {
                float r = manhattan / scale;
                return r <= 1f ? (1f - r * 0.3f) : Mathf.Max(0f, 1f - (r - 1f) * 3f);
            }
            if (shape.Contains("하트"))
            {
                float ny2 = -ny * 0.8f + 0.3f;
                float v = Mathf.Pow(nx * nx + ny2 * ny2 - 1f, 3) - nx * nx * ny2 * ny2 * ny2;
                float threshold = Mathf.Lerp(0.05f, 0.5f, fillRatio);
                return v <= threshold ? (1f - Mathf.Clamp01(v + 0.5f) * 0.3f) : 0f;
            }
            if (shape.Contains("별"))
            {
                float angle = Mathf.Atan2(ny, nx);
                float starR = (0.5f + 0.45f * Mathf.Cos(5f * angle)) * scale;
                return dist <= starR ? (1f - dist / starR * 0.3f) : Mathf.Max(0f, 0.5f - (dist - starR) * 2f);
            }
            if (shape.Contains("십자"))
            {
                float armW = Mathf.Lerp(0.25f, 0.5f, fillRatio);
                float inH = Mathf.Abs(ny) <= armW ? 1f : 0f;
                float inV = Mathf.Abs(nx) <= armW ? 1f : 0f;
                return Mathf.Max(inH, inV) * (1f - dist * 0.1f);
            }
            if (shape.Contains("도넛"))
            {
                float inner = 0.35f / scale;
                float outer = 1f * scale;
                if (dist >= inner && dist <= outer)
                    return 1f - Mathf.Abs(dist - (inner + outer) * 0.5f) / ((outer - inner) * 0.5f) * 0.3f;
                return 0f;
            }
            if (shape.Contains("삼각형"))
            {
                bool up = shape.Contains("위");
                float py = up ? (float)y / Mathf.Max(rows - 1, 1) : 1f - (float)y / Mathf.Max(rows - 1, 1);
                float halfW = py * scale;
                float ax = Mathf.Abs(nx);
                return ax <= halfW ? (1f - ax / Mathf.Max(halfW, 0.01f) * 0.2f) : 0f;
            }
            if (shape.Contains("L자"))
            {
                bool right = shape.Contains("우");
                bool bottom = shape.Contains("하");
                // L-shape: adjust thickness to fill ratio
                float thickness = Mathf.Lerp(0.35f, 0.7f, fillRatio);
                float vEdge = right ? nx : -nx; // positive = towards stem
                float hEdge = bottom ? -ny : ny;
                bool inVert = vEdge >= (1f - thickness * 2f);
                bool inHorz = hEdge >= (1f - thickness * 2f);
                float v = (inVert || inHorz) ? (1f - dist * 0.1f) : 0f;
                return v;
            }
            if (shape.Contains("T자"))
            {
                bool top = shape.Contains("ㅗ");
                float thickness = Mathf.Lerp(0.3f, 0.6f, fillRatio);
                float barEdge = top ? ny : -ny;
                bool inBar = barEdge >= (1f - thickness * 2f);
                bool inStem = Mathf.Abs(nx) <= thickness;
                return (inBar || inStem) ? (1f - dist * 0.1f) : 0f;
            }
            if (shape.Contains("U자"))
            {
                bool bottom = shape.Contains("아래");
                float thickness = Mathf.Lerp(0.25f, 0.5f, fillRatio);
                bool leftWall = nx <= (-1f + thickness * 2f);
                bool rightWall = nx >= (1f - thickness * 2f);
                float baseEdge = bottom ? -ny : ny;
                bool baseBar = baseEdge >= (1f - thickness * 2f);
                return (leftWall || rightWall || baseBar) ? (1f - dist * 0.1f) : 0f;
            }
            if (shape.Contains("화살표"))
            {
                bool horizontal = shape.Contains("→");
                float thickness = Mathf.Lerp(0.3f, 0.55f, fillRatio);
                if (horizontal)
                {
                    float headStart = 1f - thickness * 1.5f;
                    bool inStem = Mathf.Abs(ny) <= thickness && nx < headStart;
                    bool inHead = nx >= headStart && Mathf.Abs(ny) <= (1f - nx) / (1f - headStart + 0.01f);
                    return (inStem || inHead) ? (1f - dist * 0.1f) : 0f;
                }
                else
                {
                    float headEnd = -1f + thickness * 1.5f;
                    bool inStem = Mathf.Abs(nx) <= thickness && ny > headEnd;
                    bool inHead = ny <= headEnd && Mathf.Abs(nx) <= (ny - (-1f)) / (-headEnd + 1f + 0.01f);
                    return (inStem || inHead) ? (1f - dist * 0.1f) : 0f;
                }
            }
            if (shape.Contains("계단"))
            {
                bool leftTop = shape.Contains("좌상");
                int steps = Mathf.Max(3, Mathf.Min(cols, rows) / 3);
                float stepW = (float)cols / steps;
                float stepH = (float)rows / steps;
                int sx = Mathf.Clamp((int)(x / stepW), 0, steps - 1);
                int sy = Mathf.Clamp((int)(y / stepH), 0, steps - 1);
                // Scale step count threshold by fill ratio
                int threshold = Mathf.RoundToInt(steps * Mathf.Lerp(0.8f, 1.3f, fillRatio));
                bool fill = leftTop ? (sx + sy < threshold) : (sx + (steps - 1 - sy) < threshold);
                return fill ? (1f - dist * 0.1f) : 0f;
            }

            // Fallback: full rectangle
            return 1.0f - dist * 0.05f;
        }

        /// <summary>
        /// Applies empty pattern as priority penalty (subtract from affected cells).
        /// Penalty is strong (-0.8) so cells are excluded, but not absolute,
        /// allowing shape silhouette to remain if fill ratio demands it.
        /// </summary>
        private void ApplyEmptyPenalty(float[,] p, int cols, int rows, string emptyTag)
        {
            string tag = (emptyTag ?? "").Trim();
            float cx = (cols - 1) * 0.5f;
            float cy = (rows - 1) * 0.5f;

            if (tag.Contains("없음") || string.IsNullOrEmpty(tag)) return;

            float penalty = -0.8f;

            if (tag.Contains("중앙 3x3"))
                PenalizeCenter(p, cols, rows, 3, penalty);
            else if (tag.Contains("중앙 2x2"))
                PenalizeCenter(p, cols, rows, 2, penalty);
            else if (tag.Contains("중앙 1x1"))
                PenalizeCenter(p, cols, rows, 1, penalty);
            else if (tag.Contains("상하 채널"))
            {
                // Horizontal channel through center
                int channelH = Mathf.Max(1, rows / 10);
                int midY = Mathf.RoundToInt(cy);
                for (int x = 0; x < cols; x++)
                    for (int y = midY - channelH; y <= midY + channelH; y++)
                        if (y >= 0 && y < rows) p[x, y] += penalty;
            }
            else if (tag.Contains("좌우 채널"))
            {
                int channelW = Mathf.Max(1, cols / 10);
                int midX = Mathf.RoundToInt(cx);
                for (int x = midX - channelW; x <= midX + channelW; x++)
                    for (int y = 0; y < rows; y++)
                        if (x >= 0 && x < cols) p[x, y] += penalty;
            }
            else if (tag.Contains("모서리 컷"))
            {
                int cut = Mathf.Max(2, Mathf.Min(cols, rows) / 5);
                for (int x = 0; x < cut; x++)
                    for (int y = 0; y < cut - x; y++)
                    {
                        p[x, y] += penalty;
                        if (cols - 1 - x >= 0) p[cols - 1 - x, y] += penalty;
                        if (rows - 1 - y >= 0) p[x, rows - 1 - y] += penalty;
                        if (cols - 1 - x >= 0 && rows - 1 - y >= 0)
                            p[cols - 1 - x, rows - 1 - y] += penalty;
                    }
            }
            else if (tag.Contains("모서리 1곳"))
            {
                int cut = Mathf.Max(2, Mathf.Min(cols, rows) / 4);
                for (int x = 0; x < cut; x++)
                    for (int y = 0; y < cut - x; y++)
                        p[x, y] += penalty;
            }
            else if (tag.Contains("도넛 중앙"))
            {
                PenalizeCenter(p, cols, rows, Mathf.Max(2, Mathf.Min(cols, rows) / 4), penalty);
            }
        }

        private void PenalizeCenter(float[,] p, int cols, int rows, int size, float penalty)
        {
            int cx = cols / 2;
            int cy = rows / 2;
            int half = size / 2;
            for (int x = cx - half; x < cx - half + size; x++)
                for (int y = cy - half; y < cy - half + size; y++)
                    if (x >= 0 && x < cols && y >= 0 && y < rows)
                        p[x, y] += penalty;
        }

        /// <summary>
        /// Selects exactly 'count' cells with the highest priority values.
        /// This guarantees exact balloon count while preserving shape silhouette.
        /// </summary>
        private bool[,] SelectTopCells(float[,] priority, int cols, int rows, int count)
        {
            // Collect all cells with their priority
            var cells = new List<(int x, int y, float p)>(cols * rows);
            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                    cells.Add((x, y, priority[x, y]));

            // Sort descending by priority
            cells.Sort((a, b) => b.p.CompareTo(a.p));

            // Select top N
            bool[,] mask = new bool[cols, rows];
            int selected = Mathf.Min(count, cells.Count);
            for (int i = 0; i < selected; i++)
                mask[cells[i].x, cells[i].y] = true;

            return mask;
        }

        private int CountActive(bool[,] mask, int cols, int rows)
        {
            int count = 0;
            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                    if (mask[x, y]) count++;
            return count;
        }

        #endregion

        #region Balloon Placement — Composition Rules

        private BalloonLayout[] PlaceBalloons(bool[,] mask, int cols, int rows,
            float cellSpacing, Dictionary<int, int> colorDist, int numColors,
            string compositionTag, string patternTag, System.Random rng)
        {
            // Build ordered list of active cells
            var activeCells = new List<Vector2Int>();
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                    if (mask[x, y])
                        activeCells.Add(new Vector2Int(x, y));

            int totalBalloons = activeCells.Count;
            if (totalBalloons == 0) return Array.Empty<BalloonLayout>();

            // Build color pool matching distribution
            int[] colorPool = BuildColorPool(colorDist, totalBalloons, numColors);

            // Assign colors based on composition rule
            int[,] colorGrid = new int[cols, rows];
            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                    colorGrid[x, y] = -1;

            string comp = (compositionTag ?? "").Trim().ToLowerInvariant();

            // Normalize Korean composition names to algorithm types
            if (comp.Contains("좌우대칭"))
                ApplySymmetryLR(colorGrid, mask, cols, rows, colorPool, numColors, rng);
            else if (comp.Contains("상하대칭"))
                ApplySymmetryTB(colorGrid, mask, cols, rows, colorPool, numColors, rng);
            else if (comp.Contains("4방향대칭"))
                ApplySymmetry4Way(colorGrid, mask, cols, rows, colorPool, numColors, rng);
            else if (comp.Contains("회전대칭"))
                ApplyRotation180(colorGrid, mask, cols, rows, colorPool, numColors, rng);
            else if (comp.Contains("유닛") || comp.Contains("타일링") || comp.Contains("체커"))
                ApplyTiling(colorGrid, mask, cols, rows, colorPool, numColors, comp, rng);
            else if (comp.Contains("동심원") || comp.Contains("겹") || comp.Contains("외곽") || comp.Contains("중심부"))
                ApplyConcentricRings(colorGrid, mask, cols, rows, colorPool, numColors, comp, rng);
            else if (comp.Contains("뭉침") || comp.Contains("클러스터"))
                ApplyCluster(colorGrid, mask, cols, rows, colorPool, numColors, rng);
            else if (comp.Contains("분산") || comp.Contains("인접 금지"))
                ApplyScatter(colorGrid, mask, cols, rows, colorPool, numColors, rng);
            else if (comp.Contains("그라데이션"))
                ApplyGradient(colorGrid, mask, cols, rows, colorPool, numColors, comp, rng);
            else if (comp.Contains("분할") || comp.Contains("등분"))
                ApplySplit(colorGrid, mask, cols, rows, colorPool, numColors, comp, rng);
            else if (comp.Contains("4분할"))
                ApplyQuadrant(colorGrid, mask, cols, rows, colorPool, numColors, rng);
            else
                ApplyRandom(colorGrid, mask, cols, rows, colorPool, rng);

            // Fill any remaining unassigned cells
            FillUnassigned(colorGrid, mask, cols, rows, colorPool, rng);

            // Convert to BalloonLayout[]
            float maxDim = Mathf.Max(cols, rows);
            float cs = BOARD_WORLD_SIZE / maxDim;
            float halfGridX = (cols - 1) * 0.5f * cs;
            float halfGridY = (rows - 1) * 0.5f * cs;

            var balloons = new BalloonLayout[totalBalloons];
            int bid = 0;
            foreach (var cell in activeCells)
            {
                float wx = BOARD_CENTER_X + cell.x * cs - halfGridX;
                float wz = BOARD_CENTER_Z + cell.y * cs - halfGridY;

                balloons[bid] = new BalloonLayout
                {
                    balloonId = bid,
                    color = Mathf.Max(0, colorGrid[cell.x, cell.y]),
                    gridPosition = new Vector2(wx, wz),
                    gimmickType = ""
                };
                bid++;
            }

            return balloons;
        }

        private int[] BuildColorPool(Dictionary<int, int> colorDist, int totalBalloons, int numColors)
        {
            var pool = new List<int>();
            int assigned = 0;
            foreach (var kv in colorDist.OrderBy(k => k.Key))
            {
                for (int i = 0; i < kv.Value && assigned < totalBalloons; i++)
                {
                    pool.Add(kv.Key);
                    assigned++;
                }
            }
            // Fill any remaining with last color
            while (pool.Count < totalBalloons)
                pool.Add(pool.Count > 0 ? pool[pool.Count - 1] : 0);
            return pool.ToArray();
        }

        // ── Symmetry LR ──
        private void ApplySymmetryLR(int[,] grid, bool[,] mask, int cols, int rows,
            int[] pool, int numColors, System.Random rng)
        {
            Shuffle(pool, rng);
            int idx = 0;
            int halfX = cols / 2;

            // Place left half
            for (int y = 0; y < rows; y++)
                for (int x = 0; x <= halfX; x++)
                {
                    if (!mask[x, y]) continue;
                    int color = pool[idx % pool.Length];
                    grid[x, y] = color;

                    // Mirror
                    int mx = cols - 1 - x;
                    if (mx >= 0 && mx < cols && mask[mx, y])
                        grid[mx, y] = color;

                    idx++;
                }
        }

        // ── Symmetry TB ──
        private void ApplySymmetryTB(int[,] grid, bool[,] mask, int cols, int rows,
            int[] pool, int numColors, System.Random rng)
        {
            Shuffle(pool, rng);
            int idx = 0;
            int halfY = rows / 2;

            for (int y = 0; y <= halfY; y++)
                for (int x = 0; x < cols; x++)
                {
                    if (!mask[x, y]) continue;
                    int color = pool[idx % pool.Length];
                    grid[x, y] = color;

                    int my = rows - 1 - y;
                    if (my >= 0 && my < rows && mask[x, my])
                        grid[x, my] = color;

                    idx++;
                }
        }

        // ── 4-Way Symmetry ──
        private void ApplySymmetry4Way(int[,] grid, bool[,] mask, int cols, int rows,
            int[] pool, int numColors, System.Random rng)
        {
            Shuffle(pool, rng);
            int idx = 0;
            int halfX = cols / 2;
            int halfY = rows / 2;

            for (int y = 0; y <= halfY; y++)
                for (int x = 0; x <= halfX; x++)
                {
                    if (!mask[x, y]) continue;
                    int color = pool[idx % pool.Length];
                    grid[x, y] = color;

                    int mx = cols - 1 - x, my = rows - 1 - y;
                    if (mx >= 0 && mx < cols && mask[mx, y]) grid[mx, y] = color;
                    if (my >= 0 && my < rows && mask[x, my]) grid[x, my] = color;
                    if (mx >= 0 && mx < cols && my >= 0 && my < rows && mask[mx, my])
                        grid[mx, my] = color;

                    idx++;
                }
        }

        // ── Rotation 180 ──
        private void ApplyRotation180(int[,] grid, bool[,] mask, int cols, int rows,
            int[] pool, int numColors, System.Random rng)
        {
            Shuffle(pool, rng);
            int idx = 0;
            int total = cols * rows;

            for (int i = 0; i <= total / 2; i++)
            {
                int x = i % cols, y = i / cols;
                if (!mask[x, y]) continue;
                int color = pool[idx % pool.Length];
                grid[x, y] = color;

                int rx = cols - 1 - x, ry = rows - 1 - y;
                if (rx >= 0 && rx < cols && ry >= 0 && ry < rows && mask[rx, ry])
                    grid[rx, ry] = color;

                idx++;
            }
        }

        // ── Tiling ──
        private void ApplyTiling(int[,] grid, bool[,] mask, int cols, int rows,
            int[] pool, int numColors, string comp, System.Random rng)
        {
            int unitSize = 2;
            if (comp.Contains("3x3")) unitSize = 3;
            else if (comp.Contains("4x4")) unitSize = 4;

            bool checker = comp.Contains("체커");

            // Build unit pattern
            int[,] unit = new int[unitSize, unitSize];
            int ci = 0;
            for (int uy = 0; uy < unitSize; uy++)
                for (int ux = 0; ux < unitSize; ux++)
                {
                    if (checker)
                        unit[ux, uy] = (ux + uy) % numColors;
                    else
                        unit[ux, uy] = ci++ % numColors;
                }

            // Tile across grid
            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                    if (mask[x, y])
                        grid[x, y] = unit[x % unitSize, y % unitSize];
        }

        // ── Concentric Rings ──
        private void ApplyConcentricRings(int[,] grid, bool[,] mask, int cols, int rows,
            int[] pool, int numColors, string comp, System.Random rng)
        {
            float cx = (cols - 1) * 0.5f;
            float cy = (rows - 1) * 0.5f;
            float maxDist = Mathf.Sqrt(cx * cx + cy * cy);
            if (maxDist < 1f) maxDist = 1f;

            int rings = comp.Contains("3겹") ? 3 : comp.Contains("2겹") ? 2 : 3;
            bool outerEmphasis = comp.Contains("외곽") || comp.Contains("테두리");
            bool centerEmphasis = comp.Contains("중심부");

            // Assign colors per ring
            int[] ringColors = new int[rings];
            for (int i = 0; i < rings; i++)
                ringColors[i] = i % numColors;

            if (outerEmphasis && numColors >= 2)
            {
                ringColors[0] = 0; // outer = primary color
                for (int i = 1; i < rings; i++)
                    ringColors[i] = 1 + (i - 1) % (numColors - 1);
            }

            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                {
                    if (!mask[x, y]) continue;
                    // Manhattan distance for ring calculation
                    float dist = Mathf.Max(Mathf.Abs(x - cx) / (cx > 0 ? cx : 1),
                        Mathf.Abs(y - cy) / (cy > 0 ? cy : 1));
                    int ring = Mathf.Clamp(Mathf.FloorToInt(dist * rings), 0, rings - 1);
                    if (centerEmphasis) ring = rings - 1 - ring;
                    grid[x, y] = ringColors[ring];
                }
        }

        // ── Cluster (BFS) ──
        private void ApplyCluster(int[,] grid, bool[,] mask, int cols, int rows,
            int[] pool, int numColors, System.Random rng)
        {
            // Place seed points for each color
            var activeCells = new List<Vector2Int>();
            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                    if (mask[x, y]) activeCells.Add(new Vector2Int(x, y));

            if (activeCells.Count == 0) return;
            Shuffle(activeCells, rng);

            // Parse color pool counts
            var colorCounts = new int[numColors];
            foreach (int c in pool)
                if (c >= 0 && c < numColors) colorCounts[c]++;

            var remaining = (int[])colorCounts.Clone();

            // Seed: pick one cell per color
            var queue = new Queue<(Vector2Int pos, int color)>();
            for (int c = 0; c < numColors && c < activeCells.Count; c++)
            {
                var seed = activeCells[c];
                grid[seed.x, seed.y] = c;
                remaining[c]--;
                queue.Enqueue((seed, c));
            }

            // BFS expand
            int[] dx = { 0, 0, -1, 1 };
            int[] dy = { -1, 1, 0, 0 };

            while (queue.Count > 0)
            {
                var (pos, color) = queue.Dequeue();
                if (remaining[color] <= 0) continue;

                // Shuffle neighbor order for randomness
                int startDir = rng.Next(4);
                for (int d = 0; d < 4; d++)
                {
                    int di = (startDir + d) % 4;
                    int nx = pos.x + dx[di];
                    int ny = pos.y + dy[di];
                    if (nx < 0 || nx >= cols || ny < 0 || ny >= rows) continue;
                    if (!mask[nx, ny] || grid[nx, ny] >= 0) continue;
                    if (remaining[color] <= 0) break;

                    grid[nx, ny] = color;
                    remaining[color]--;
                    queue.Enqueue((new Vector2Int(nx, ny), color));
                }
            }
        }

        // ── Scatter (no adjacent same color) ──
        private void ApplyScatter(int[,] grid, bool[,] mask, int cols, int rows,
            int[] pool, int numColors, System.Random rng)
        {
            // Checkerboard-like: ensure no adjacent cells share a color
            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                    if (mask[x, y])
                        grid[x, y] = (x + y) % numColors;
        }

        // ── Gradient ──
        private void ApplyGradient(int[,] grid, bool[,] mask, int cols, int rows,
            int[] pool, int numColors, string comp, System.Random rng)
        {
            bool diagonal = comp.Contains("대각선");
            bool vertical = comp.Contains("세로") || comp.Contains("상→하");

            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                {
                    if (!mask[x, y]) continue;
                    float t;
                    if (diagonal)
                        t = (float)(x + y) / (cols + rows - 2);
                    else if (vertical)
                        t = (float)y / Mathf.Max(rows - 1, 1);
                    else
                        t = (float)x / Mathf.Max(cols - 1, 1);

                    grid[x, y] = Mathf.Clamp(Mathf.FloorToInt(t * numColors), 0, numColors - 1);
                }
        }

        // ── Split ──
        private void ApplySplit(int[,] grid, bool[,] mask, int cols, int rows,
            int[] pool, int numColors, string comp, System.Random rng)
        {
            float cx = (cols - 1) * 0.5f;
            float cy = (rows - 1) * 0.5f;

            int zones;
            if (comp.Contains("3등분")) zones = 3;
            else zones = 2;

            bool vertical = comp.Contains("세로") || comp.Contains("좌/우");
            bool diagonal = comp.Contains("대각선");

            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                {
                    if (!mask[x, y]) continue;
                    int zone;
                    if (diagonal)
                    {
                        float dv = (x - cx) + (y - cy);
                        zone = dv < -1 ? 0 : dv > 1 ? zones - 1 : zones / 2;
                    }
                    else if (vertical)
                    {
                        zone = Mathf.Clamp(Mathf.FloorToInt((float)x / cols * zones), 0, zones - 1);
                    }
                    else
                    {
                        zone = Mathf.Clamp(Mathf.FloorToInt((float)y / rows * zones), 0, zones - 1);
                    }
                    grid[x, y] = zone % numColors;
                }
        }

        // ── Quadrant ──
        private void ApplyQuadrant(int[,] grid, bool[,] mask, int cols, int rows,
            int[] pool, int numColors, System.Random rng)
        {
            int halfX = cols / 2;
            int halfY = rows / 2;
            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                {
                    if (!mask[x, y]) continue;
                    int qx = x < halfX ? 0 : 1;
                    int qy = y < halfY ? 0 : 1;
                    grid[x, y] = (qx + qy * 2) % numColors;
                }
        }

        // ── Random (fallback) ──
        private void ApplyRandom(int[,] grid, bool[,] mask, int cols, int rows,
            int[] pool, System.Random rng)
        {
            Shuffle(pool, rng);
            int idx = 0;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                    if (mask[x, y])
                        grid[x, y] = pool[idx++ % pool.Length];
        }

        private void FillUnassigned(int[,] grid, bool[,] mask, int cols, int rows,
            int[] pool, System.Random rng)
        {
            int idx = 0;
            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                    if (mask[x, y] && grid[x, y] < 0)
                        grid[x, y] = pool[idx++ % pool.Length];
        }

        #endregion

        #region Gimmick Assignment

        private string[] AssignGimmicks(BalloonLayout[] balloons, LevelRow row, System.Random rng)
        {
            var gimmickList = new List<string>();
            var assignments = new List<(int index, string type)>();

            // Collect gimmick assignments from CSV counts
            if (row.gimmickHidden > 0)
            {
                gimmickList.Add("hidden");
                for (int i = 0; i < row.gimmickHidden; i++)
                    assignments.Add((0, "hidden"));
            }
            if (row.gimmickChain > 0)
            {
                gimmickList.Add("chain");
                for (int i = 0; i < row.gimmickChain; i++)
                    assignments.Add((0, "chain"));
            }
            if (row.gimmickPinata > 0)
            {
                gimmickList.Add("pinata");
                for (int i = 0; i < row.gimmickPinata; i++)
                    assignments.Add((0, "pinata"));
            }
            if (row.gimmickSpawnerT > 0)
            {
                gimmickList.Add("spawner_t");
                for (int i = 0; i < row.gimmickSpawnerT; i++)
                    assignments.Add((0, "spawner_t"));
            }
            if (row.gimmickPin > 0)
            {
                gimmickList.Add("pin");
                for (int i = 0; i < row.gimmickPin; i++)
                    assignments.Add((0, "pin"));
            }
            if (row.gimmickLockKey > 0)
            {
                gimmickList.Add("lock_key");
                for (int i = 0; i < row.gimmickLockKey; i++)
                    assignments.Add((0, "lock_key"));
            }
            if (row.gimmickSurprise > 0)
            {
                gimmickList.Add("surprise");
                for (int i = 0; i < row.gimmickSurprise; i++)
                    assignments.Add((0, "surprise"));
            }
            if (row.gimmickWall > 0)
            {
                gimmickList.Add("wall");
                for (int i = 0; i < row.gimmickWall; i++)
                    assignments.Add((0, "wall"));
            }
            if (row.gimmickSpawnerO > 0)
            {
                gimmickList.Add("spawner_o");
                for (int i = 0; i < row.gimmickSpawnerO; i++)
                    assignments.Add((0, "spawner_o"));
            }
            if (row.gimmickPinataBox > 0)
            {
                gimmickList.Add("pinata_box");
                for (int i = 0; i < row.gimmickPinataBox; i++)
                    assignments.Add((0, "pinata_box"));
            }
            if (row.gimmickIce > 0)
            {
                gimmickList.Add("ice");
                for (int i = 0; i < row.gimmickIce; i++)
                    assignments.Add((0, "ice"));
            }
            if (row.gimmickFrozenDart > 0)
            {
                gimmickList.Add("frozen_dart");
                for (int i = 0; i < row.gimmickFrozenDart; i++)
                    assignments.Add((0, "frozen_dart"));
            }
            if (row.gimmickCurtain > 0)
            {
                gimmickList.Add("curtain");
                for (int i = 0; i < row.gimmickCurtain; i++)
                    assignments.Add((0, "curtain"));
            }

            if (assignments.Count == 0 || balloons.Length == 0)
                return gimmickList.ToArray();

            // Randomly assign gimmicks to balloons
            var indices = Enumerable.Range(0, balloons.Length).ToList();
            Shuffle(indices, rng);

            int count = Mathf.Min(assignments.Count, balloons.Length);
            for (int i = 0; i < count; i++)
            {
                balloons[indices[i]].gimmickType = assignments[i].type;
            }

            return gimmickList.ToArray();
        }

        #endregion

        #region Holder Packing

        private int[] CountDartsPerColor(BalloonLayout[] balloons, int numColors)
        {
            int[] counts = new int[numColors];
            foreach (var b in balloons)
            {
                int life = GetGimmickLife(b.gimmickType);
                if (b.color >= 0 && b.color < numColors)
                    counts[b.color] += life;
            }
            return counts;
        }

        private int GetGimmickLife(string gimmickType)
        {
            if (string.IsNullOrEmpty(gimmickType)) return 1;
            switch (gimmickType)
            {
                case "pinata":     return 2;
                case "pinata_box": return 2;
                case "wall":       return 0;
                case "pin":        return 0;
                case "ice":        return 0;
                default:           return 1;
            }
        }

        /// <summary>
        /// Packs darts per color into holders using allowed magazine sizes.
        /// Design Rule: all magazine counts must be from allowedMags, sum must be exact.
        /// </summary>
        private HolderSetup[] BuildHolders(int[] dartsPerColor, int[] allowedMags,
            int queueColumns, int maxHolders, System.Random rng)
        {
            var holders = new List<HolderSetup>();
            int hid = 0;

            // Sort allowed mags descending for greedy packing
            var mags = allowedMags.OrderByDescending(m => m).ToArray();
            if (mags.Length == 0) mags = new[] { 5 };

            // If maxHolders is too small, ensure at least 1 per color with darts
            int colorsWithDarts = dartsPerColor.Count(d => d > 0);
            if (maxHolders < colorsWithDarts) maxHolders = colorsWithDarts;

            // Calculate ideal holders per color (proportional to dart count)
            int totalDarts = dartsPerColor.Sum();
            int[] slotBudget = new int[dartsPerColor.Length];
            int assignedSlots = 0;
            for (int c = 0; c < dartsPerColor.Length; c++)
            {
                if (dartsPerColor[c] <= 0) continue;
                slotBudget[c] = Mathf.Max(1, Mathf.RoundToInt((float)dartsPerColor[c] / totalDarts * maxHolders));
                assignedSlots += slotBudget[c];
            }
            // Adjust for rounding — trim excess from largest budget
            while (assignedSlots > maxHolders)
            {
                int maxIdx = 0;
                for (int c = 1; c < slotBudget.Length; c++)
                    if (slotBudget[c] > slotBudget[maxIdx]) maxIdx = c;
                slotBudget[maxIdx]--;
                assignedSlots--;
            }

            for (int color = 0; color < dartsPerColor.Length; color++)
            {
                int remaining = dartsPerColor[color];
                if (remaining <= 0) continue;

                int budget = slotBudget[color];

                // If budget is 1, put all darts in one holder
                if (budget <= 1)
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

                // Greedy packing within budget: pick mags that distribute darts across budget holders
                int holdersUsed = 0;
                while (remaining > 0 && holdersUsed < budget)
                {
                    int holdersLeft = budget - holdersUsed;

                    // Last holder absorbs remainder
                    if (holdersLeft == 1)
                    {
                        holders.Add(new HolderSetup
                        {
                            holderId = hid++,
                            color = color,
                            magazineCount = remaining,
                            position = Vector2.zero
                        });
                        remaining = 0;
                        holdersUsed++;
                        break;
                    }

                    // Pick best mag: largest that leaves enough for remaining holders
                    int bestMag = mags[mags.Length - 1];
                    int minSmallest = mags[mags.Length - 1];
                    foreach (int mag in mags)
                    {
                        if (mag <= remaining)
                        {
                            int after = remaining - mag;
                            // After this holder, remaining holders need at least minSmallest each
                            if (after >= (holdersLeft - 1) * minSmallest)
                            {
                                bestMag = mag;
                                break;
                            }
                        }
                    }

                    if (bestMag > remaining) bestMag = remaining;

                    holders.Add(new HolderSetup
                    {
                        holderId = hid++,
                        color = color,
                        magazineCount = bestMag,
                        position = Vector2.zero
                    });
                    remaining -= bestMag;
                    holdersUsed++;
                }

                // If remaining > 0 after budget exhausted, merge into last holder
                if (remaining > 0 && holders.Count > 0)
                {
                    var last = holders[holders.Count - 1];
                    last.magazineCount += remaining;
                    holders[holders.Count - 1] = last;
                }
            }

            // Shuffle holders (mix colors for queue variety)
            for (int i = holders.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var tmp = holders[i]; holders[i] = holders[j]; holders[j] = tmp;
            }

            // Assign queue positions
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

        #region Rail Generation

        private RailLayout GenerateRail(int gridCols, int gridRows, float cellSpacing,
            int queueColumns, int railCapacity)
        {
            float halfBoard = BOARD_WORLD_SIZE * 0.5f;
            float left   = BOARD_CENTER_X - halfBoard - RAIL_PADDING;
            float right  = BOARD_CENTER_X + halfBoard + RAIL_PADDING;
            float bottom = BOARD_CENTER_Z - halfBoard - RAIL_PADDING;
            float top    = BOARD_CENTER_Z + halfBoard + RAIL_PADDING;

            var wp = new List<Vector3>(12);
            wp.Add(new Vector3(left,  0.5f, bottom));
            wp.Add(new Vector3(Mathf.Lerp(left, right, 0.33f), 0.5f, bottom));
            wp.Add(new Vector3(Mathf.Lerp(left, right, 0.67f), 0.5f, bottom));
            wp.Add(new Vector3(right, 0.5f, bottom));
            wp.Add(new Vector3(right, 0.5f, Mathf.Lerp(bottom, top, 0.33f)));
            wp.Add(new Vector3(right, 0.5f, Mathf.Lerp(bottom, top, 0.67f)));
            wp.Add(new Vector3(right, 0.5f, top));
            wp.Add(new Vector3(Mathf.Lerp(right, left, 0.33f), 0.5f, top));
            wp.Add(new Vector3(Mathf.Lerp(right, left, 0.67f), 0.5f, top));
            wp.Add(new Vector3(left,  0.5f, top));
            wp.Add(new Vector3(left,  0.5f, Mathf.Lerp(top, bottom, 0.33f)));
            wp.Add(new Vector3(left,  0.5f, Mathf.Lerp(top, bottom, 0.67f)));

            int qCols = Mathf.Max(queueColumns, 2);
            var dp = new Vector3[qCols];
            for (int i = 0; i < qCols; i++)
            {
                float t = (i + 1f) / (qCols + 1f);
                dp[i] = new Vector3(Mathf.Lerp(left, right, t), 0.5f, bottom - 1f);
            }

            int slotCount = railCapacity > 0 ? railCapacity : 200;

            return new RailLayout
            {
                waypoints = wp.ToArray(),
                slotCount = slotCount,
                visualType = 3, // SPRITE_TILE
                deployPoints = dp,
                smoothCorners = true,
                cornerRadius = 1f
            };
        }

        private Vector2Int[] GenerateConveyorPositions(int gridCols, int gridRows)
        {
            var positions = new List<Vector2Int>();

            // Rectangular ring around the grid: -1..gridCols, -1..gridRows
            for (int x = -1; x <= gridCols; x++)
            {
                positions.Add(new Vector2Int(x, -1));           // bottom
                positions.Add(new Vector2Int(x, gridRows));     // top
            }
            for (int y = 0; y < gridRows; y++)
            {
                positions.Add(new Vector2Int(-1, y));           // left
                positions.Add(new Vector2Int(gridCols, y));     // right
            }

            return positions.ToArray();
        }

        #endregion

        #region Purpose Mapping

        private DifficultyPurpose MapPurpose(string purposeType)
        {
            if (string.IsNullOrEmpty(purposeType)) return DifficultyPurpose.Normal;
            string p = purposeType.Trim();
            if (p.Contains("튜토리얼") || p.Contains("Tutorial")) return DifficultyPurpose.Tutorial;
            if (p.Contains("슈퍼하드") || p.Contains("SuperHard")) return DifficultyPurpose.SuperHard;
            if (p.Contains("하드") || p.Contains("Hard")) return DifficultyPurpose.Hard;
            if (p.Contains("휴식") || p.Contains("Rest")) return DifficultyPurpose.Rest;
            if (p.Contains("노말") || p.Contains("Normal")) return DifficultyPurpose.Normal;
            return DifficultyPurpose.Normal;
        }

        #endregion

        #region Save

        private void SaveToAsset()
        {
            if (_generatedConfigs == null || _generatedConfigs.Length == 0) return;

            // Filter out null configs
            var validConfigs = _generatedConfigs.Where(c => c != null).ToArray();

            string path = "Assets/Resources/LevelDatabase.asset";
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            var db = AssetDatabase.LoadAssetAtPath<LevelDatabase>(path);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<LevelDatabase>();
                db.levels = validConfigs;
                AssetDatabase.CreateAsset(db, path);
            }
            else
            {
                db.levels = validConfigs;
                EditorUtility.SetDirty(db);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _statusMessage = $"Saved {validConfigs.Length} levels to {path}";
            Debug.Log($"[LevelImporter] {_statusMessage}");
            EditorUtility.DisplayDialog("Saved",
                $"{validConfigs.Length} levels saved to\n{path}", "OK");
        }

        #endregion

        #region Utility

        private void Shuffle<T>(T[] array, System.Random rng)
        {
            for (int i = array.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T tmp = array[i]; array[i] = array[j]; array[j] = tmp;
            }
        }

        private void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }

        #endregion
    }
}
#endif
