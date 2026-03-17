using UnityEngine;
using UnityEngine.Tilemaps;

namespace BalloonFlow
{
    /// <summary>
    /// Manages 2D floor tiles and conveyor belt tilemap.
    /// Floor tiles are visual only (dark background beneath balloons).
    /// Conveyor tiles define the belt path (brighter/different color).
    /// Uses XY tilemap rotated 90 degrees on X to map onto the XZ world plane
    /// (camera is orthographic top-down, Y=15, looking along -Y).
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Manager | Phase: 1
    /// DB Reference: No DB match -- generated from task spec (2D floor + tilemap conveyor)
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

        // 8 directional rail tiles
        private TileBase _tileBH; // bottom horizontal
        private TileBase _tileBL; // bottom-left corner
        private TileBase _tileBR; // bottom-right corner
        private TileBase _tileTH; // top horizontal
        private TileBase _tileTL; // top-left corner
        private TileBase _tileTR; // top-right corner
        private TileBase _tileVL; // vertical left
        private TileBase _tileVR; // vertical right
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
            // Auto-discover scene Grid/Tilemap if not wired via SerializeField
            // (BoardTileManager is created dynamically by GameManager.CreateChild)
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
        /// <param name="cols">Number of grid columns.</param>
        /// <param name="rows">Number of grid rows.</param>
        /// <param name="boardCenter">World-space center of the board (X, Z).</param>
        /// <param name="cellSpacing">Distance between cells in world units.</param>
        public void InitializeBoard(int cols, int rows, Vector2 boardCenter, float cellSpacing)
        {
            _cols = cols;
            _rows = rows;

            // Load tile assets from Resources (user can place real tiles later)
            _floorTile = Resources.Load<TileBase>("Tiles/FloorTile");
            _conveyorTile = Resources.Load<TileBase>("Tiles/ConveyorTile");

            // Fallback: create simple colored tiles if no art assets exist
            if (_floorTile == null)
                _floorTile = CreateSimpleTile(new Color(0.18f, 0.18f, 0.22f));
            if (_conveyorTile == null)
                _conveyorTile = CreateSimpleTile(new Color(0.35f, 0.35f, 0.45f));

            // Load 8 directional rail tiles
            LoadDirectionalRailTiles();

            // Configure grid cell size to match balloon spacing
            if (_grid != null)
            {
                _grid.cellSize = new Vector3(cellSpacing, cellSpacing, 0f);

                // Position the grid so tiles align with the board center on XZ plane.
                // The Grid is rotated 90 degrees on X (tilemap XY -> world XZ).
                // Tilemap origin (0,0) maps to grid world position.
                // We offset so the center of the tile grid aligns with boardCenter.
                float halfCols = (cols - 1) * 0.5f * cellSpacing;
                float halfRows = (rows - 1) * 0.5f * cellSpacing;
                _grid.transform.position = new Vector3(
                    boardCenter.x - halfCols - cellSpacing * 0.5f,
                    -0.05f, // Slightly below Y=0 so it sits under balloons
                    boardCenter.y - halfRows - cellSpacing * 0.5f
                );
            }

            FillFloor(cols, rows);
        }

        /// <summary>
        /// Fills the floor tilemap with floor tiles covering the full grid area.
        /// </summary>
        public void FillFloor(int cols, int rows)
        {
            if (_floorTilemap == null || _floorTile == null) return;
            _floorTilemap.ClearAllTiles();

            for (int x = 0; x < cols; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    _floorTilemap.SetTile(new Vector3Int(x, y, 0), _floorTile);
                }
            }
        }

        /// <summary>
        /// Sets conveyor tiles at the specified tilemap positions.
        /// Uses directional rail tiles when available, falls back to single tile.
        /// </summary>
        public void SetConveyorPath(Vector3Int[] positions)
        {
            if (_conveyorTilemap == null) return;
            _conveyorTilemap.ClearAllTiles();

            if (positions == null) return;
            foreach (var pos in positions)
            {
                _conveyorTilemap.SetTile(pos, _conveyorTile);
            }
        }

        /// <summary>
        /// Sets conveyor positions from Vector2Int array (from LevelConfig).
        /// Converts to Vector3Int for the tilemap.
        /// </summary>
        public void SetConveyorFromConfig(Vector2Int[] positions)
        {
            if (positions == null || positions.Length == 0)
            {
                if (_conveyorTilemap != null)
                    _conveyorTilemap.ClearAllTiles();
                return;
            }

            var tilePositions = new Vector3Int[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                tilePositions[i] = new Vector3Int(positions[i].x, positions[i].y, 0);
            }
            SetConveyorPath(tilePositions);
        }

        /// <summary>
        /// Builds the rectangular conveyor belt around the balloon grid using directional tiles.
        /// Call after InitializeBoard. Uses the 8 rail corner/edge tiles for proper visuals.
        /// </summary>
        public void BuildConveyorBelt()
        {
            if (_conveyorTilemap == null) return;
            _conveyorTilemap.ClearAllTiles();

            if (!_hasDirectionalTiles)
            {
                // Fallback: use single conveyor tile for a simple rectangular loop
                BuildConveyorBeltFallback();
                return;
            }

            // Rectangular belt around the balloon grid: 1 tile outside each edge
            // Grid occupies cells (0,0) to (cols-1, rows-1)
            // Conveyor ring: x from -1 to cols, y from -1 to rows
            int minX = -1;
            int maxX = _cols;
            int minY = -1;
            int maxY = _rows;

            // Bottom-left corner
            _conveyorTilemap.SetTile(new Vector3Int(minX, minY, 0), _tileBL);

            // Bottom edge (horizontal)
            for (int x = minX + 1; x < maxX; x++)
                _conveyorTilemap.SetTile(new Vector3Int(x, minY, 0), _tileBH);

            // Bottom-right corner
            _conveyorTilemap.SetTile(new Vector3Int(maxX, minY, 0), _tileBR);

            // Right edge (vertical)
            for (int y = minY + 1; y < maxY; y++)
                _conveyorTilemap.SetTile(new Vector3Int(maxX, y, 0), _tileVR);

            // Top-right corner
            _conveyorTilemap.SetTile(new Vector3Int(maxX, maxY, 0), _tileTR);

            // Top edge (horizontal)
            for (int x = maxX - 1; x > minX; x--)
                _conveyorTilemap.SetTile(new Vector3Int(x, maxY, 0), _tileTH);

            // Top-left corner
            _conveyorTilemap.SetTile(new Vector3Int(minX, maxY, 0), _tileTL);

            // Left edge (vertical)
            for (int y = maxY - 1; y > minY; y--)
                _conveyorTilemap.SetTile(new Vector3Int(minX, y, 0), _tileVL);
        }

        /// <summary>
        /// Fallback conveyor belt using single tile.
        /// </summary>
        private void BuildConveyorBeltFallback()
        {
            if (_conveyorTilemap == null || _conveyorTile == null) return;

            int minX = -1;
            int maxX = _cols;
            int minY = -1;
            int maxY = _rows;

            // Bottom row
            for (int x = minX; x <= maxX; x++)
                _conveyorTilemap.SetTile(new Vector3Int(x, minY, 0), _conveyorTile);

            // Top row
            for (int x = minX; x <= maxX; x++)
                _conveyorTilemap.SetTile(new Vector3Int(x, maxY, 0), _conveyorTile);

            // Left column (excluding corners)
            for (int y = minY + 1; y < maxY; y++)
                _conveyorTilemap.SetTile(new Vector3Int(minX, y, 0), _conveyorTile);

            // Right column (excluding corners)
            for (int y = minY + 1; y < maxY; y++)
                _conveyorTilemap.SetTile(new Vector3Int(maxX, y, 0), _conveyorTile);
        }

        /// <summary>
        /// Toggles a single conveyor tile at the given grid position.
        /// </summary>
        public void SetConveyorTileAt(Vector3Int pos, bool active)
        {
            if (_conveyorTilemap == null) return;
            _conveyorTilemap.SetTile(pos, active ? _conveyorTile : null);
        }

        /// <summary>
        /// Returns true if a conveyor tile exists at the given position.
        /// </summary>
        public bool HasConveyorAt(Vector3Int pos)
        {
            if (_conveyorTilemap == null) return false;
            return _conveyorTilemap.GetTile(pos) != null;
        }

        /// <summary>
        /// Clears all tiles (floor + conveyor). Call before re-initializing.
        /// </summary>
        public void ClearAll()
        {
            if (_floorTilemap != null) _floorTilemap.ClearAllTiles();
            if (_conveyorTilemap != null) _conveyorTilemap.ClearAllTiles();
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Loads the 8 directional rail tiles from Resources/Tiles/.
        /// Falls back to single conveyor tile if any are missing.
        /// </summary>
        private void LoadDirectionalRailTiles()
        {
            _tileBH = Resources.Load<TileBase>("Tiles/rail_corner_bh");
            _tileBL = Resources.Load<TileBase>("Tiles/rail_corner_bl");
            _tileBR = Resources.Load<TileBase>("Tiles/rail_corner_br");
            _tileTH = Resources.Load<TileBase>("Tiles/rail_corner_th");
            _tileTL = Resources.Load<TileBase>("Tiles/rail_corner_tl");
            _tileTR = Resources.Load<TileBase>("Tiles/rail_corner_tr");
            _tileVL = Resources.Load<TileBase>("Tiles/rail_corner_vl");
            _tileVR = Resources.Load<TileBase>("Tiles/rail_corner_vr");

            _hasDirectionalTiles = _tileBH != null && _tileBL != null && _tileBR != null
                                && _tileTH != null && _tileTL != null && _tileTR != null
                                && _tileVL != null && _tileVR != null;

            if (!_hasDirectionalTiles)
                Debug.Log("[BoardTileManager] Directional rail tiles not found in Resources/Tiles/. Using fallback conveyor tile.");
        }

        /// <summary>
        /// Creates a simple Tile with a white 4x4 texture tinted by the given color.
        /// Placeholder until user provides real tile art.
        /// </summary>
        private TileBase CreateSimpleTile(Color color)
        {
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.color = color;

            // Create a 4x4 white texture as sprite base
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
