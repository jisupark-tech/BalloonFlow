using UnityEngine;
using UnityEngine.Tilemaps;

namespace BalloonFlow
{
    /// <summary>
    /// Manages 2D floor tiles and conveyor belt tilemap.
    /// Floor tiles are visual only (dark background beneath balloons).
    /// Conveyor tiles define the belt path using 6 tile types:
    ///   4 corners (bl, br, tl, tr) + 2 straight (h, v).
    /// Tiles are center-aligned, placed seamlessly with no overlap.
    /// Uses XY tilemap rotated 90 degrees on X to map onto the XZ world plane.
    ///
    /// The conveyor grid uses FIXED proportions relative to the balloon field width,
    /// independent of balloon grid dimensions. An arrow LineRenderer overlays the
    /// conveyor path with a scrolling chevron animation indicating belt direction.
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Manager | Phase: 1
    /// </remarks>
    public class BoardTileManager : SceneSingleton<BoardTileManager>
    {
        #region Fields

        [SerializeField] private Grid _grid;
        [SerializeField] private Tilemap _floorTilemap;
        [SerializeField] private Tilemap _conveyorTilemap;
        [SerializeField] private TilemapRenderer _floorRenderer;
        [SerializeField] private TilemapRenderer _conveyorRenderer;

        private TileBase _floorTile;
        private TileBase _conveyorTile; // fallback single tile

        // 6 directional rail tiles (center-aligned)
        private TileBase _tileH;   // horizontal straight
        private TileBase _tileV;   // vertical straight
        private TileBase _tileBL;  // bottom-left corner
        private TileBase _tileBR;  // bottom-right corner
        private TileBase _tileTL;  // top-left corner
        private TileBase _tileTR;  // top-right corner
        private bool _hasDirectionalTiles;

        private int _cols;
        private int _rows;

        // Fixed conveyor proportions (all relative to fieldWidth)
        private const float PROP_RAIL_WIDTH       = 0.39f; // 0.30 × 1.3 (30% 두껍게)
        private const float PROP_RAIL_GAP_H       = 0.07f;
        private const float PROP_TOTAL_WIDTH       = 1.48f;
        private const float PROP_TOTAL_HEIGHT      = 1.72f;
        private const float PROP_RAIL_GAP_V_TOP    = 0.09f;
        private const float PROP_RAIL_GAP_V_BOTTOM = 0.12f;

        // Cached rail layout values (computed in InitializeBoard)
        private float _fieldWidth;
        private float _railWidth;
        private float _railGapH;
        private float _totalAreaWidth;
        private float _totalAreaHeight;
        private float _railGapVTop;
        private float _railGapVBottom;
        private float _cellSpacing; // balloon cellSpacing, cached for reference

        // SpriteRenderer 기반 컨베이어 타일 (MapMaker와 동일 방식)
        private Transform _conveyorSpriteRoot;

        // RailTileSet for auto-tiling sprites
        private RailTileSet _spriteRailTileSet;
        private Sprite _cachedArrowSprite;

        // Arrow: 슬롯 기반 회전 (다트처럼 벨트와 함께 이동)
        private const int ARROW_COUNT = 20;
        private GameObject[] _arrowObjects;
        private int[] _arrowSlotIndices; // 각 Arrow가 점유한 슬롯 인덱스

        #endregion

        #region Properties

        public Tilemap FloorTilemap => _floorTilemap;
        public Tilemap ConveyorTilemap => _conveyorTilemap;
        public Grid TileGrid => _grid;
        public int Cols => _cols;
        public int Rows => _rows;

        /// <summary>Balloon field width (cols * cellSpacing).</summary>
        public float FieldWidth => _fieldWidth;

        /// <summary>Rail thickness in world units.</summary>
        public float RailWidth => _railWidth;

        /// <summary>Horizontal gap between rail inner edge and balloon field edge.</summary>
        public float RailGapH => _railGapH;

        /// <summary>Total conveyor area width in world units.</summary>
        public float TotalAreaWidth => _totalAreaWidth;

        /// <summary>Total conveyor area height in world units.</summary>
        public float TotalAreaHeight => _totalAreaHeight;

        /// <summary>Vertical gap between rail inner edge and balloon field top edge.</summary>
        public float RailGapVTop => _railGapVTop;

        /// <summary>Vertical gap between rail inner edge and balloon field bottom edge.</summary>
        public float RailGapVBottom => _railGapVBottom;

        /// <summary>
        /// Rail center offset from field edge = gap + half rail width.
        /// Useful for computing waypoint rectangle positions.
        /// </summary>
        public float RailCenterOffsetH => _railGapH + _railWidth * 0.5f;

        /// <summary>Rail center offset from field top edge.</summary>
        public float RailCenterOffsetVTop => _railGapVTop + _railWidth * 0.5f;

        /// <summary>Rail center offset from field bottom edge.</summary>
        public float RailCenterOffsetVBottom => _railGapVBottom + _railWidth * 0.5f;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            if (_grid == null)
            {
                var gridGO = GameObject.Find("BoardGrid");
                if (gridGO != null)
                    _grid = gridGO.GetComponent<Grid>();
            }

            if (_grid != null)
            {
                if (_floorTilemap == null)
                {
                    var floorTr = _grid.transform.Find("FloorTiles");
                    if (floorTr != null)
                    {
                        _floorTilemap = floorTr.GetComponent<Tilemap>();
                        _floorRenderer = floorTr.GetComponent<TilemapRenderer>();
                    }
                }

                if (_conveyorTilemap == null)
                {
                    var convTr = _grid.transform.Find("ConveyorTiles");
                    if (convTr != null)
                    {
                        _conveyorTilemap = convTr.GetComponent<Tilemap>();
                        _conveyorRenderer = convTr.GetComponent<TilemapRenderer>();
                    }
                }
            }

            if (_grid == null)
                Debug.LogWarning("[BoardTileManager] BoardGrid not found in scene. Tilemap features disabled.");
        }

        private void Update()
        {
            UpdateArrowPositions();
        }

        private void OnDestroy()
        {
            ClearArrows();
            ClearConveyorSprites();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initializes the board tilemap: loads tile assets, sets grid cell size, fills floor.
        /// Called by LevelManager.SetupLevel() after level config is loaded.
        /// Computes fixed conveyor proportions based on fieldWidth = cols * cellSpacing.
        /// The conveyor tilemap uses a separate grid with cell size matching the rail width.
        /// </summary>
        public void InitializeBoard(int cols, int rows, Vector2 boardCenter, float cellSpacing)
        {
            _cols = cols;
            _rows = rows;
            _cellSpacing = cellSpacing;

            // 고정 비율
            _fieldWidth       = cols * cellSpacing;
            _railWidth        = _fieldWidth * PROP_RAIL_WIDTH; // 30%
            _railGapH         = _fieldWidth * PROP_RAIL_GAP_H;
            _totalAreaWidth   = _fieldWidth * PROP_TOTAL_WIDTH;
            _totalAreaHeight  = _fieldWidth * PROP_TOTAL_HEIGHT;
            _railGapVTop      = _fieldWidth * PROP_RAIL_GAP_V_TOP;
            _railGapVBottom   = _fieldWidth * PROP_RAIL_GAP_V_BOTTOM;

            _floorTile = Resources.Load<TileBase>("Tiles/FloorTile");
            _conveyorTile = Resources.Load<TileBase>("Tiles/ConveyorTile");

            if (_floorTile == null)
                _floorTile = CreateSimpleTile(new Color(0.255f, 0.235f, 0.392f)); // #413C64
            if (_conveyorTile == null)
                _conveyorTile = CreateSimpleTile(new Color(0.35f, 0.35f, 0.45f));

            LoadDirectionalRailTiles();

            if (_grid != null)
            {
                _grid.cellSize = new Vector3(cellSpacing, cellSpacing, 0f);

                float halfCols = (cols - 1) * 0.5f * cellSpacing;
                float halfRows = (rows - 1) * 0.5f * cellSpacing;
                _grid.transform.position = new Vector3(
                    boardCenter.x - halfCols - cellSpacing * 0.5f,
                    -0.05f,
                    boardCenter.y - halfRows - cellSpacing * 0.5f
                );

                // RailTileSet 로드 (SpriteRenderer 배치용)
                _spriteRailTileSet = Resources.Load<RailTileSet>("RailTileSet");
            }

            FillFloor(cols, rows);
        }

        public void FillFloor(int cols, int rows)
        {
            if (_floorTilemap == null || _floorTile == null) return;
            _floorTilemap.ClearAllTiles();

            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                    _floorTilemap.SetTile(new Vector3Int(x, y, 0), _floorTile);
        }

        /// <summary>
        /// Sets conveyor tiles from a conveyor path grid using directional auto-tiling.
        /// Each cell analyzes its neighbors to pick the correct tile (corner vs straight).
        /// Tiles are center-aligned and sized to cell spacing -- no overlap.
        /// </summary>
        public void SetConveyorFromGrid(bool[,] grid, int cols, int rows)
        {
            if (_conveyorTilemap == null) return;
            _conveyorTilemap.ClearAllTiles();

            if (grid == null || !_hasDirectionalTiles) return;

            var tileSet = _spriteRailTileSet ?? Resources.Load<RailTileSet>("RailTileSet");
            if (tileSet == null) return;

            for (int x = 0; x < cols; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    if (!grid[x, y]) continue;
                    Sprite sprite = tileSet.GetTileForCell(grid, x, y, cols, rows);
                    if (sprite != null)
                    {
                        var tile = ScriptableObject.CreateInstance<Tile>();
                        tile.sprite = sprite;
                        tile.color = Color.white;
                        _conveyorTilemap.SetTile(new Vector3Int(x, y, 0), tile);
                    }
                }
            }

            SpawnArrows();
        }

        /// <summary>
        /// Sets conveyor positions from Vector2Int array (from LevelConfig).
        /// Uses directional auto-tiling when available.
        /// </summary>
        public void SetConveyorFromConfig(Vector2Int[] positions)
        {
            if (_conveyorTilemap == null) return;
            _conveyorTilemap.ClearAllTiles();

            if (positions == null || positions.Length == 0) return;

            // Build a grid from position array for neighbor analysis
            // Positions may have negative coords (extended path grid), so offset to 0-based
            int minX = 0, minY = 0, maxX = 0, maxY = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                if (positions[i].x < minX) minX = positions[i].x;
                if (positions[i].y < minY) minY = positions[i].y;
                if (positions[i].x > maxX) maxX = positions[i].x;
                if (positions[i].y > maxY) maxY = positions[i].y;
            }
            int gw = maxX - minX + 2;
            int gh = maxY - minY + 2;
            bool[,] grid = new bool[gw, gh];
            for (int i = 0; i < positions.Length; i++)
                grid[positions[i].x - minX, positions[i].y - minY] = true;

            if (_hasDirectionalTiles)
            {
                var tileSet = _spriteRailTileSet ?? Resources.Load<RailTileSet>("RailTileSet");
                if (tileSet != null)
                {
                    for (int i = 0; i < positions.Length; i++)
                    {
                        int gx = positions[i].x - minX, gy = positions[i].y - minY;
                        Sprite sprite = tileSet.GetTileForCell(grid, gx, gy, gw, gh);
                        if (sprite != null)
                        {
                            var tile = ScriptableObject.CreateInstance<Tile>();
                            tile.sprite = sprite;
                            tile.color = Color.white;
                            // Tilemap uses original coords (supports negative)
                            _conveyorTilemap.SetTile(new Vector3Int(positions[i].x, positions[i].y, 0), tile);
                        }
                    }

                    SpawnArrows();
                    return;
                }
            }

            // Fallback: single tile
            for (int i = 0; i < positions.Length; i++)
                _conveyorTilemap.SetTile(new Vector3Int(positions[i].x, positions[i].y, 0), _conveyorTile);

            SpawnArrows();
        }

        /// <summary>
        /// Sets conveyor tiles at the specified tilemap positions (legacy single-tile mode).
        /// </summary>
        public void SetConveyorPath(Vector3Int[] positions)
        {
            if (_conveyorTilemap == null) return;
            _conveyorTilemap.ClearAllTiles();

            if (positions == null) return;
            foreach (var pos in positions)
                _conveyorTilemap.SetTile(pos, _conveyorTile);

            SpawnArrows();
        }

        /// <summary>
        /// 컨베이어벨트: 코너 4개 + 직선 4개 = 총 8개 타일.
        /// 코너: 고정 크기 (railWidth × railWidth).
        /// 가로 직선: X 스케일을 양쪽 코너에 연결될 만큼 늘림.
        /// 세로 직선: Y 스케일을 양쪽 코너에 연결될 만큼 늘림.
        /// </summary>
        public void BuildConveyorBelt()
        {
            ClearConveyorSprites();
            _conveyorSpriteRoot = new GameObject("ConveyorSprites").transform;

            float boardCX = GameManager.HasInstance ? GameManager.Instance.Board.boardCenterX : 0f;
            float boardCZ = GameManager.HasInstance ? GameManager.Instance.Board.boardCenterZ : 2f;

            float halfFieldX = _fieldWidth * 0.5f;
            float halfFieldZ = _rows * _cellSpacing * 0.5f;
            float offsetH = _railGapH + _railWidth * 0.5f;
            float offsetVTop = _railGapVTop + _railWidth * 0.5f;
            float offsetVBottom = _railGapVBottom + _railWidth * 0.5f;

            float left   = boardCX - halfFieldX - offsetH;
            float right  = boardCX + halfFieldX + offsetH;
            float bottom = boardCZ - halfFieldZ - offsetVBottom;
            float top    = boardCZ + halfFieldZ + offsetVTop;

            float cornerSize = _railWidth;
            float hLength = right - left - cornerSize;
            float vLength = top - bottom - cornerSize;
            float hCenter = (left + right) * 0.5f;
            float vCenter = (bottom + top) * 0.5f;

            Sprite spBL = _spriteRailTileSet != null ? _spriteRailTileSet.tileBL : null;
            Sprite spBR = _spriteRailTileSet != null ? _spriteRailTileSet.tileBR : null;
            Sprite spTL = _spriteRailTileSet != null ? _spriteRailTileSet.tileTL : null;
            Sprite spTR = _spriteRailTileSet != null ? _spriteRailTileSet.tileTR : null;

            PlaceConveyorSprite(spBL, left, bottom, cornerSize);
            PlaceConveyorSprite(spBR, right, bottom, cornerSize);
            PlaceConveyorSprite(spTL, left, top, cornerSize);
            PlaceConveyorSprite(spTR, right, top, cornerSize);

            Sprite hSprite = GetTileSprite(true, false, true, false);
            Sprite vSprite = GetTileSprite(false, true, false, true);

            PlaceConveyorSpriteStretched(hSprite, hCenter, bottom, hLength, cornerSize);
            PlaceConveyorSpriteStretched(hSprite, hCenter, top, hLength, cornerSize);
            PlaceConveyorSpriteStretched(vSprite, left, vCenter, cornerSize, vLength);
            PlaceConveyorSpriteStretched(vSprite, right, vCenter, cornerSize, vLength);

            // Arrow: 슬롯 기반 (다트처럼 벨트와 함께 회전)
            SpawnArrows();
        }

        private void PlaceConveyorSpriteStretched(Sprite sprite, float wx, float wz, float worldW, float worldH)
        {
            if (sprite == null || _conveyorSpriteRoot == null) return;

            var go = new GameObject("CSTile");
            go.transform.SetParent(_conveyorSpriteRoot, false);
            go.transform.position = new Vector3(wx, -0.02f, wz);
            go.transform.eulerAngles = new Vector3(90f, 0f, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = -1;

            // Simple 모드 + localScale로 늘리기
            float sw = sprite.bounds.size.x;
            float sh = sprite.bounds.size.y;
            float scaleX = sw > 0.001f ? worldW / sw : 1f;
            float scaleY = sh > 0.001f ? worldH / sh : 1f;
            go.transform.localScale = new Vector3(scaleX, scaleY, 1f);
        }

        private void PlaceConveyorSprite(Sprite sprite, float wx, float wz, float tileSize)
        {
            if (sprite == null || _conveyorSpriteRoot == null) return;

            var go = new GameObject("CSTile");
            go.transform.SetParent(_conveyorSpriteRoot, false);
            go.transform.position = new Vector3(wx, -0.02f, wz);
            go.transform.eulerAngles = new Vector3(90f, 0f, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = -1;

            float sw = sprite.bounds.size.x;
            float sh = sprite.bounds.size.y;
            if (sw > 0.001f && sh > 0.001f)
                go.transform.localScale = new Vector3(tileSize / sw, tileSize / sh, 1f);
        }

        private Sprite GetTileSprite(bool hasRight, bool hasUp, bool hasLeft, bool hasDown)
        {
            if (_spriteRailTileSet == null) return null;

            // 코너
            if (hasRight && hasUp && !hasLeft && !hasDown) return _spriteRailTileSet.tileBL;
            if (hasLeft && hasUp && !hasRight && !hasDown) return _spriteRailTileSet.tileBR;
            if (hasRight && hasDown && !hasLeft && !hasUp) return _spriteRailTileSet.tileTL;
            if (hasLeft && hasDown && !hasRight && !hasUp) return _spriteRailTileSet.tileTR;

            // 직선
            if (hasLeft && hasRight) return _spriteRailTileSet.GetH();
            if (hasUp && hasDown) return _spriteRailTileSet.GetV();

            return _spriteRailTileSet.GetH();
        }

        private void ClearConveyorSprites()
        {
            if (_conveyorSpriteRoot != null)
            {
                Destroy(_conveyorSpriteRoot.gameObject);
                _conveyorSpriteRoot = null;
            }
        }

        private void BuildConveyorBeltFallback()
        {
            if (_conveyorTilemap == null || _conveyorTile == null) return;

            int minX = -1, maxX = _cols, minY = -1, maxY = _rows;

            for (int x = minX; x <= maxX; x++)
                _conveyorTilemap.SetTile(new Vector3Int(x, minY, 0), _conveyorTile);
            for (int x = minX; x <= maxX; x++)
                _conveyorTilemap.SetTile(new Vector3Int(x, maxY, 0), _conveyorTile);
            for (int y = minY + 1; y < maxY; y++)
                _conveyorTilemap.SetTile(new Vector3Int(minX, y, 0), _conveyorTile);
            for (int y = minY + 1; y < maxY; y++)
                _conveyorTilemap.SetTile(new Vector3Int(maxX, y, 0), _conveyorTile);
        }

        public void SetConveyorTileAt(Vector3Int pos, bool active)
        {
            if (_conveyorTilemap == null) return;
            _conveyorTilemap.SetTile(pos, active ? _conveyorTile : null);
        }

        public bool HasConveyorAt(Vector3Int pos)
        {
            if (_conveyorTilemap == null) return false;
            return _conveyorTilemap.GetTile(pos) != null;
        }

        public void ClearAll()
        {
            if (_floorTilemap != null) _floorTilemap.ClearAllTiles();
            if (_conveyorTilemap != null) _conveyorTilemap.ClearAllTiles();
            ClearArrows();
        }

        #endregion

        #region Private Methods


        /// <summary>
        /// Loads the 6 directional rail tiles from Resources/Tiles/.
        /// New naming: h, v, bl, br, tl, tr (center-aligned).
        /// Falls back to legacy 8-tile naming if new tiles not found.
        /// </summary>
        private void LoadDirectionalRailTiles()
        {
            // Try new 6-tile naming first
            var tileH_res  = Resources.Load<TileBase>("Tiles/rail_corner_h");
            var tileV_res  = Resources.Load<TileBase>("Tiles/rail_corner_v");
            var tileBL_res = Resources.Load<TileBase>("Tiles/rail_corner_bl");
            var tileBR_res = Resources.Load<TileBase>("Tiles/rail_corner_br");
            var tileTL_res = Resources.Load<TileBase>("Tiles/rail_corner_tl");
            var tileTR_res = Resources.Load<TileBase>("Tiles/rail_corner_tr");

            if (tileH_res != null && tileV_res != null
                && tileBL_res != null && tileBR_res != null
                && tileTL_res != null && tileTR_res != null)
            {
                _tileH  = tileH_res;
                _tileV  = tileV_res;
                _tileBL = tileBL_res;
                _tileBR = tileBR_res;
                _tileTL = tileTL_res;
                _tileTR = tileTR_res;
                _hasDirectionalTiles = true;
                return;
            }

            // Fallback to legacy 8-tile naming
            var bh = Resources.Load<TileBase>("Tiles/rail_corner_bh");
            var vl = Resources.Load<TileBase>("Tiles/rail_corner_vl");
            var bl = Resources.Load<TileBase>("Tiles/rail_corner_bl");
            var br = Resources.Load<TileBase>("Tiles/rail_corner_br");
            var tl = Resources.Load<TileBase>("Tiles/rail_corner_tl");
            var tr = Resources.Load<TileBase>("Tiles/rail_corner_tr");

            if (bh != null && vl != null && bl != null && br != null && tl != null && tr != null)
            {
                _tileH  = bh;  // use bh as horizontal
                _tileV  = vl;  // use vl as vertical
                _tileBL = bl;
                _tileBR = br;
                _tileTL = tl;
                _tileTR = tr;
                _hasDirectionalTiles = true;
                return;
            }

            _hasDirectionalTiles = false;
            Debug.Log("[BoardTileManager] Directional rail tiles not found in Resources/Tiles/. Using fallback.");
        }

        private TileBase CreateSimpleTile(Color color)
        {
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.color = color;

            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();

            tile.sprite = Sprite.Create(
                tex,
                new Rect(0, 0, 4, 4),
                new Vector2(0.5f, 0.5f),
                4f
            );

            return tile;
        }

        #endregion

        #region Arrow Slot System

        /// <summary>
        /// Arrow를 벨트 슬롯에 균등 배치 (ARROW_COUNT개).
        /// 다트처럼 벨트와 함께 회전. 슬롯이 다트로 차면 숨기고, 비면 보이기.
        /// </summary>
        private void SpawnArrows()
        {
            ClearArrows();

            if (!RailManager.HasInstance) return;
            RailManager rail = RailManager.Instance;
            if (rail.SlotCount == 0) return;

            _arrowObjects = new GameObject[ARROW_COUNT];
            _arrowSlotIndices = new int[ARROW_COUNT];

            // 균등 간격으로 슬롯 배정
            int spacing = Mathf.Max(1, rail.SlotCount / ARROW_COUNT);

            // Arrow 스프라이트 캐시
            if (_cachedArrowSprite == null)
                _cachedArrowSprite = Resources.Load<Sprite>("Sprites/arrow");
            Sprite arrowSprite = _cachedArrowSprite;

            for (int i = 0; i < ARROW_COUNT; i++)
            {
                int slotIdx = (i * spacing) % rail.SlotCount;
                _arrowSlotIndices[i] = slotIdx;

                var go = new GameObject($"Arrow_{i}");
                if (_conveyorSpriteRoot != null)
                    go.transform.SetParent(_conveyorSpriteRoot);

                // SpriteRenderer 사용
                if (arrowSprite != null)
                {
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = arrowSprite;
                    sr.sortingOrder = 0;
                    sr.color = new Color(1f, 1f, 1f, 0.6f);
                    float arrowSize = _railWidth * 0.5f;
                    float sw = arrowSprite.bounds.size.x;
                    float sh = arrowSprite.bounds.size.y;
                    if (sw > 0.001f && sh > 0.001f)
                        go.transform.localScale = new Vector3(arrowSize / sw, arrowSize / sh, 1f);
                }
                else
                {
                    // 폴백: MeshRenderer + Quad
                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = BalloonController.GetOrCreateSharedMaterial(new Color(1f, 1f, 1f, 0.4f));
                    go.transform.localScale = Vector3.one * _railWidth * 0.3f;
                }

                _arrowObjects[i] = go;
            }

            // 즉시 위치 갱신
            UpdateArrowPositions();
        }

        /// <summary>매 프레임: Arrow를 슬롯 위치로 이동 + 방향 설정 + 다트 있으면 숨기기.</summary>
        private void UpdateArrowPositions()
        {
            if (_arrowObjects == null || !RailManager.HasInstance) return;

            RailManager rail = RailManager.Instance;

            for (int i = 0; i < _arrowObjects.Length; i++)
            {
                if (_arrowObjects[i] == null) continue;

                int slotIdx = _arrowSlotIndices[i];

                // 다트가 있는 슬롯이면 숨기기
                bool occupied = !rail.IsSlotEmpty(slotIdx);
                _arrowObjects[i].SetActive(!occupied);

                if (occupied) continue;

                // 슬롯 위치 + 방향
                Vector3 pos = rail.GetSlotWorldPosition(slotIdx);
                pos.y = 0.05f; // 컨베이어 타일 위
                _arrowObjects[i].transform.position = pos;

                // 벨트 이동 방향으로 회전 (90도 눕힘 + 진행 방향)
                Vector3 dir = rail.GetSlotDirection(slotIdx);
                if (dir.sqrMagnitude > 0.001f)
                {
                    // XZ 평면에서 진행 방향 → Sprite를 90도 눕히고 진행 방향으로 회전
                    // 스프라이트 기본이 아래(↓)를 향하므로 +90도 보정하여 오른쪽(→) 기준
                    float angle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                    _arrowObjects[i].transform.rotation = Quaternion.Euler(90f, angle - 90f, 0f);
                }
            }
        }

        private void ClearArrows()
        {
            if (_arrowObjects != null)
            {
                for (int i = 0; i < _arrowObjects.Length; i++)
                {
                    if (_arrowObjects[i] != null)
                        Destroy(_arrowObjects[i]);
                }
                _arrowObjects = null;
                _arrowSlotIndices = null;
            }
        }

        #endregion
    }
}
