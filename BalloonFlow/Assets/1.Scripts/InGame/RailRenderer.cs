using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Visualizes the conveyor belt rail path.
    /// Supports multiple visual styles: Cylinder (3D tubes), Flat2D (quad strips),
    /// Custom3D (user-provided prefab segments).
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: UX | Phase: 1
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow
    /// </remarks>
    public class RailRenderer : MonoBehaviour
    {
        #region Constants

        public const int VISUAL_CYLINDER = 0;
        public const int VISUAL_FLAT2D = 1;
        public const int VISUAL_CUSTOM3D = 2;
        public const int VISUAL_SPRITE_TILE = 3;

        private const float DEFAULT_TRACK_WIDTH = 0.3f;
        private const float DEFAULT_TILE_SIZE = 1.5f;
        private static readonly Color DEFAULT_RAIL_COLOR = new Color(0.4f, 0.4f, 0.45f, 1f);

        #endregion

        #region Serialized Fields

        [SerializeField] private float _trackWidth = DEFAULT_TRACK_WIDTH;
        [SerializeField] private Color _railColor = DEFAULT_RAIL_COLOR;
        [SerializeField] private int _visualType = VISUAL_SPRITE_TILE;
        [SerializeField] private GameObject _customSegmentPrefab; // For VISUAL_CUSTOM3D
        [SerializeField] private float _tileWorldSize = DEFAULT_TILE_SIZE;

        #endregion

        #region Fields

        private readonly List<GameObject> _trackSegments = new List<GameObject>();
        private Material _trackMaterial;
        private RailTileSet _tileSet;
        private bool _isInitialized;

        #endregion

        #region Properties

        /// <summary>Current visual type (0=Cylinder, 1=Flat2D, 2=Custom3D).</summary>
        public int VisualType
        {
            get => _visualType;
            set { _visualType = value; }
        }

        #endregion

        #region Lifecycle

        private void Awake()
        {
            _tileSet = Resources.Load<RailTileSet>("RailTileSet");

            // Create default material for non-sprite visual modes (Cylinder, Flat2D)
            _trackMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Sprites/Default"));
            _trackMaterial.color = _railColor;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
        }

        private void OnDestroy()
        {
            if (_trackMaterial != null)
            {
                Destroy(_trackMaterial);
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Reads the current rail path from RailManager and builds cylinder track segments.
        /// Call after RailManager.SetRailLayout() has been invoked.
        /// </summary>
        public void RefreshPath()
        {
            if (!RailManager.HasInstance)
            {
                Debug.LogWarning("[RailRenderer] RailManager not available. Cannot render rail path.");
                ClearPath();
                return;
            }

            RailManager rail = RailManager.Instance;
            Vector3[] waypoints = rail.GetRailPath();

            if (waypoints == null || waypoints.Length < 2)
            {
                Debug.LogWarning("[RailRenderer] Rail path has fewer than 2 waypoints. Clearing track.");
                ClearPath();
                return;
            }

            ClearPath();

            bool isLoop = rail.IsClosedLoop;

            // Sprite tile mode — use grid-based placement when conveyorPositions exist
            if (_visualType == VISUAL_SPRITE_TILE)
            {
                // Try grid-based tile placement from LevelConfig.conveyorPositions
                if (LevelManager.HasInstance && LevelManager.Instance.CurrentLevel != null)
                {
                    var config = LevelManager.Instance.CurrentLevel;
                    if (config.conveyorPositions != null && config.conveyorPositions.Length > 0)
                    {
                        BuildGridBasedTilePath(config);
                        _isInitialized = true;
                        return;
                    }
                }

                // Fallback: waypoint-interpolated tile placement
                BuildSpriteTilePath(waypoints, isLoop);
                _isInitialized = true;
                return;
            }

            int segmentCount = isLoop ? waypoints.Length : waypoints.Length - 1;

            for (int i = 0; i < segmentCount; i++)
            {
                Vector3 start = waypoints[i];
                Vector3 end = (i == waypoints.Length - 1) ? waypoints[0] : waypoints[i + 1];

                Vector3 midpoint = (start + end) * 0.5f;
                float length = Vector3.Distance(start, end);

                if (length < 0.001f)
                {
                    continue;
                }

                GameObject segment;

                switch (_visualType)
                {
                    case VISUAL_FLAT2D:
                        segment = GameObject.CreatePrimitive(PrimitiveType.Quad);
                        segment.name = $"RailSegment2D_{i}";
                        segment.transform.SetParent(transform);
                        segment.transform.position = midpoint;
                        // Quad lies in XY by default; rotate to XZ plane, then align with direction
                        Vector3 flatDir = (end - start).normalized;
                        segment.transform.rotation = Quaternion.LookRotation(flatDir, Vector3.up);
                        segment.transform.localScale = new Vector3(_trackWidth * 3f, length, 1f);
                        break;

                    case VISUAL_CUSTOM3D:
                        if (_customSegmentPrefab != null)
                        {
                            segment = Instantiate(_customSegmentPrefab, midpoint, Quaternion.identity, transform);
                        }
                        else
                        {
                            segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            segment.transform.SetParent(transform);
                            segment.transform.position = midpoint;
                        }
                        segment.name = $"RailSegmentCustom_{i}";
                        // Align prefab forward (Z) along segment direction, keep upright
                        Vector3 segDir = (end - start).normalized;
                        if (segDir.sqrMagnitude > 0.001f)
                            segment.transform.rotation = Quaternion.LookRotation(segDir, Vector3.up);
                        // Scale: X=width, Y=width, Z=length (stretch along forward axis)
                        segment.transform.localScale = new Vector3(_trackWidth * 2f, _trackWidth * 2f, length);
                        break;

                    default: // VISUAL_CYLINDER
                        segment = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        segment.name = $"RailSegment_{i}";
                        segment.transform.SetParent(transform);
                        segment.transform.position = midpoint;
                        segment.transform.localScale = new Vector3(_trackWidth, length * 0.5f, _trackWidth);
                        segment.transform.up = (end - start).normalized;
                        break;
                }

                // Only override material for primitive segments (not custom prefab which has its own)
                if (_visualType != VISUAL_CUSTOM3D)
                {
                    var meshRenderer = segment.GetComponent<MeshRenderer>();
                    if (meshRenderer != null)
                    {
                        meshRenderer.material = _trackMaterial;
                    }
                }

                // Disable colliders on rail segments (visual only)
                var colliders = segment.GetComponentsInChildren<Collider>();
                for (int c = 0; c < colliders.Length; c++)
                {
                    colliders[c].enabled = false;
                }

                _trackSegments.Add(segment);
            }

            _isInitialized = true;
        }

        /// <summary>
        /// Destroys all track segment GameObjects and clears the list.
        /// </summary>
        public void ClearPath()
        {
            for (int i = _trackSegments.Count - 1; i >= 0; i--)
            {
                if (_trackSegments[i] != null)
                {
                    Destroy(_trackSegments[i]);
                }
            }
            _trackSegments.Clear();
            _isInitialized = false;
        }

        #endregion

        #region Sprite Tile Path

        /// <summary>
        /// Builds tile visuals from LevelConfig.conveyorPositions using grid-aligned placement.
        /// Each position maps to a world coordinate via the same formula as MapMaker.
        /// Neighbor-based auto-tiling picks the correct sprite (h, v, bl, br, tl, tr).
        /// </summary>
        private void BuildGridBasedTilePath(LevelConfig config)
        {
            if (_tileSet == null) return;

            var positions = config.conveyorPositions;
            int gridCols = config.gridCols > 0 ? config.gridCols : 5;
            int gridRows = config.gridRows > 0 ? config.gridRows : 5;

            float boardCX = 0f, boardCY = 2f;
            float cellSpacing = 1.6f;
            if (GameManager.HasInstance)
            {
                boardCX = GameManager.Instance.Board.boardCenterX;
                boardCY = GameManager.Instance.Board.boardCenterZ;
                cellSpacing = GameManager.Instance.Board.cellSpacing;
            }

            // Build lookup grid (offset to handle negative coords)
            int minX = 0, minY = 0, maxX = 0, maxY = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                if (positions[i].x < minX) minX = positions[i].x;
                if (positions[i].y < minY) minY = positions[i].y;
                if (positions[i].x > maxX) maxX = positions[i].x;
                if (positions[i].y > maxY) maxY = positions[i].y;
            }
            int gw = maxX - minX + 1;
            int gh = maxY - minY + 1;
            bool[,] grid = new bool[gw, gh];
            for (int i = 0; i < positions.Length; i++)
                grid[positions[i].x - minX, positions[i].y - minY] = true;

            // Place tiles at grid-aligned world positions
            for (int i = 0; i < positions.Length; i++)
            {
                int bx = positions[i].x; // balloon-grid-relative coord
                int by = positions[i].y;

                // World position (same formula as MapMaker.PathGridToWorld)
                float wx = boardCX + (bx - (gridCols - 1) * 0.5f) * cellSpacing;
                float wz = boardCY + (by - (gridRows - 1) * 0.5f) * cellSpacing;
                Vector3 wpos = new Vector3(wx, 0f, wz);

                // Auto-tile: check 4 neighbors in the offset grid
                int gx = bx - minX, gy = by - minY;
                bool hasUp    = (gy + 1 < gh) && grid[gx, gy + 1];
                bool hasDown  = (gy - 1 >= 0) && grid[gx, gy - 1];
                bool hasLeft  = (gx - 1 >= 0) && grid[gx - 1, gy];
                bool hasRight = (gx + 1 < gw) && grid[gx + 1, gy];

                Sprite tile;
                // Corners
                if      (hasRight && hasUp   && !hasLeft && !hasDown) tile = _tileSet.tileBL;
                else if (hasLeft  && hasUp   && !hasRight && !hasDown) tile = _tileSet.tileBR;
                else if (hasRight && hasDown && !hasLeft && !hasUp)   tile = _tileSet.tileTL;
                else if (hasLeft  && hasDown && !hasRight && !hasUp)  tile = _tileSet.tileTR;
                // Straights
                else if (hasLeft && hasRight) tile = _tileSet.GetH();
                else if (hasUp   && hasDown)  tile = _tileSet.GetV();
                // Single-neighbor fallback
                else if (hasLeft || hasRight) tile = _tileSet.GetH();
                else if (hasUp   || hasDown)  tile = _tileSet.GetV();
                else tile = _tileSet.GetH();

                PlaceSpriteTileAtSize(tile, wpos, cellSpacing);
            }
        }

        /// <summary>
        /// Places a sprite tile at exact world position with specified tile size.
        /// </summary>
        private void PlaceSpriteTileAtSize(Sprite sprite, Vector3 position, float tileSize)
        {
            if (sprite == null) return;

            var tileGO = new GameObject($"RailTile_{_trackSegments.Count}");
            tileGO.transform.SetParent(transform);
            tileGO.transform.position = new Vector3(position.x, -0.02f, position.z);
            tileGO.transform.eulerAngles = new Vector3(90f, 0f, 0f);

            var sr = tileGO.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = -1;

            float spriteW = sprite.bounds.size.x;
            float spriteH = sprite.bounds.size.y;
            if (spriteW > 0.001f && spriteH > 0.001f)
            {
                float scaleX = tileSize / spriteW;
                float scaleY = tileSize / spriteH;
                tileGO.transform.localScale = new Vector3(scaleX, scaleY, 1f);
            }

            _trackSegments.Add(tileGO);
        }

        /// <summary>
        /// Builds the rail visual using 2D sprite tiles from RailTileSet.
        /// Places straight and corner tiles along the waypoint loop.
        /// </summary>
        private void BuildSpriteTilePath(Vector3[] waypoints, bool isLoop)
        {
            if (_tileSet == null)
            {
                Debug.LogWarning("[RailRenderer] RailTileSet not loaded. Run BalloonFlow > Setup Rail Tiles.");
                return;
            }

            int count = isLoop ? waypoints.Length : waypoints.Length - 1;

            for (int i = 0; i < count; i++)
            {
                int prev = (i - 1 + waypoints.Length) % waypoints.Length;
                int next = (i + 1) % waypoints.Length;
                int nextNext = (i + 2) % waypoints.Length;

                Vector3 start = waypoints[i];
                Vector3 end = waypoints[(i + 1) % waypoints.Length];
                Vector3 delta = end - start;
                float segLen = delta.magnitude;
                if (segLen < 0.01f) continue;

                bool isHorizontal = Mathf.Abs(delta.x) > Mathf.Abs(delta.z);
                Sprite tile = isHorizontal ? _tileSet.GetH() : _tileSet.GetV();

                // Check if start/end are corners to avoid tile overlap
                bool startIsCorner = false;
                bool endIsCorner = false;
                if (isLoop && waypoints.Length >= 3)
                {
                    Vector3 inAtStart = (waypoints[i] - waypoints[prev]).normalized;
                    Vector3 outAtStart = delta.normalized;
                    startIsCorner = Mathf.Abs(inAtStart.x) > Mathf.Abs(inAtStart.z)
                                 != Mathf.Abs(outAtStart.x) > Mathf.Abs(outAtStart.z);

                    Vector3 inAtEnd = delta.normalized;
                    Vector3 outAtEnd = (waypoints[nextNext] - waypoints[(i + 1) % waypoints.Length]).normalized;
                    endIsCorner = Mathf.Abs(inAtEnd.x) > Mathf.Abs(inAtEnd.z)
                               != Mathf.Abs(outAtEnd.x) > Mathf.Abs(outAtEnd.z);
                }

                int tileCount = Mathf.Max(1, Mathf.RoundToInt(segLen / _tileWorldSize));
                int straightCount = tileCount;
                if (startIsCorner) straightCount--;
                if (endIsCorner) straightCount--;
                if (straightCount <= 0) continue;

                for (int t = 0; t < straightCount; t++)
                {
                    float offset = startIsCorner ? 1f : 0.5f;
                    float frac = (t + offset) / tileCount;
                    Vector3 pos = Vector3.Lerp(start, end, frac);
                    PlaceSpriteTile(tile, pos);
                }
            }

            // Place corner tiles at waypoints where direction changes
            if (isLoop && waypoints.Length >= 3)
            {
                for (int i = 0; i < waypoints.Length; i++)
                {
                    int prev = (i - 1 + waypoints.Length) % waypoints.Length;
                    int next = (i + 1) % waypoints.Length;

                    Vector3 inDir = (waypoints[i] - waypoints[prev]).normalized;
                    Vector3 outDir = (waypoints[next] - waypoints[i]).normalized;

                    bool inH = Mathf.Abs(inDir.x) > Mathf.Abs(inDir.z);
                    bool outH = Mathf.Abs(outDir.x) > Mathf.Abs(outDir.z);

                    if (inH == outH) continue;

                    // Direction-based corner selection (works for any path shape)
                    Sprite cornerTile = _tileSet.GetTileForDirections(inDir, outDir);
                    PlaceSpriteTile(cornerTile, waypoints[i]);
                }
            }
        }

        private void PlaceSpriteTile(Sprite sprite, Vector3 position)
        {
            if (sprite == null) return;

            var tileGO = new GameObject($"RailTile_{_trackSegments.Count}");
            tileGO.transform.SetParent(transform);
            tileGO.transform.position = position;
            tileGO.transform.eulerAngles = new Vector3(90f, 0f, 0f); // Lie flat on XZ plane

            var sr = tileGO.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = -1;

            // Scale sprite to exactly fill tile size (center-aligned, no overlap)
            float spriteW = sprite.bounds.size.x;
            float spriteH = sprite.bounds.size.y;
            if (spriteW > 0.001f && spriteH > 0.001f)
            {
                float scaleX = _tileWorldSize / spriteW;
                float scaleY = _tileWorldSize / spriteH;
                tileGO.transform.localScale = new Vector3(scaleX, scaleY, 1f);
            }

            _trackSegments.Add(tileGO);
        }

        #endregion

        #region Visual Controls

        /// <summary>
        /// Updates the rail color at runtime.
        /// </summary>
        public void SetRailColor(Color color)
        {
            _railColor = color;
            if (_trackMaterial != null)
            {
                _trackMaterial.color = _railColor;
            }
        }

        /// <summary>
        /// Updates the track width (cylinder X/Z scale) at runtime.
        /// </summary>
        public void SetTrackWidth(float width)
        {
            _trackWidth = width;
            foreach (GameObject segment in _trackSegments)
            {
                if (segment == null) continue;
                Vector3 scale = segment.transform.localScale;
                scale.x = _trackWidth;
                scale.z = _trackWidth;
                segment.transform.localScale = scale;
            }
        }

        #endregion

        #region Private Methods

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            RefreshPath();
        }

        #endregion
    }
}
