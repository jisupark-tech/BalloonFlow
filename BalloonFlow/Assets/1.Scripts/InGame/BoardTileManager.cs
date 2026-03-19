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

        #endregion

        #region Properties

        public Tilemap FloorTilemap => _floorTilemap;
        public Tilemap ConveyorTilemap => _conveyorTilemap;
        public Grid TileGrid => _grid;
        public int Cols => _cols;
        public int Rows => _rows;

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

        #endregion

        #region Public Methods

        /// <summary>
        /// Initializes the board tilemap: loads tile assets, sets grid cell size, fills floor.
        /// Called by LevelManager.SetupLevel() after level config is loaded.
        /// </summary>
        public void InitializeBoard(int cols, int rows, Vector2 boardCenter, float cellSpacing)
        {
            _cols = cols;
            _rows = rows;

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
        /// Tiles are center-aligned and sized to cell spacing — no overlap.
        /// </summary>
        public void SetConveyorFromGrid(bool[,] grid, int cols, int rows)
        {
            if (_conveyorTilemap == null) return;
            _conveyorTilemap.ClearAllTiles();

            if (grid == null || !_hasDirectionalTiles) return;

            var tileSet = Resources.Load<RailTileSet>("RailTileSet");
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
                var tileSet = Resources.Load<RailTileSet>("RailTileSet");
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
                    return;
                }
            }

            // Fallback: single tile
            for (int i = 0; i < positions.Length; i++)
                _conveyorTilemap.SetTile(new Vector3Int(positions[i].x, positions[i].y, 0), _conveyorTile);
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
        }

        /// <summary>
        /// Builds a rectangular conveyor belt around the balloon grid using 6 directional tiles.
        /// </summary>
        public void BuildConveyorBelt()
        {
            if (_conveyorTilemap == null) return;
            _conveyorTilemap.ClearAllTiles();

            if (!_hasDirectionalTiles)
            {
                BuildConveyorBeltFallback();
                return;
            }

            int minX = -1;
            int maxX = _cols;
            int minY = -1;
            int maxY = _rows;

            // Bottom-left corner
            _conveyorTilemap.SetTile(new Vector3Int(minX, minY, 0), _tileBL);
            // Bottom edge (horizontal)
            for (int x = minX + 1; x < maxX; x++)
                _conveyorTilemap.SetTile(new Vector3Int(x, minY, 0), _tileH);
            // Bottom-right corner
            _conveyorTilemap.SetTile(new Vector3Int(maxX, minY, 0), _tileBR);
            // Right edge (vertical)
            for (int y = minY + 1; y < maxY; y++)
                _conveyorTilemap.SetTile(new Vector3Int(maxX, y, 0), _tileV);
            // Top-right corner
            _conveyorTilemap.SetTile(new Vector3Int(maxX, maxY, 0), _tileTR);
            // Top edge (horizontal)
            for (int x = maxX - 1; x > minX; x--)
                _conveyorTilemap.SetTile(new Vector3Int(x, maxY, 0), _tileH);
            // Top-left corner
            _conveyorTilemap.SetTile(new Vector3Int(minX, maxY, 0), _tileTL);
            // Left edge (vertical)
            for (int y = maxY - 1; y > minY; y--)
                _conveyorTilemap.SetTile(new Vector3Int(minX, y, 0), _tileV);
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
    }
}
