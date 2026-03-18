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

            // Sprite tile mode — place 2D tile sprites along the path
            if (_visualType == VISUAL_SPRITE_TILE)
            {
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
