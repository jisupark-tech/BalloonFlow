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

        private const float DEFAULT_TRACK_WIDTH = 0.3f;
        private static readonly Color DEFAULT_RAIL_COLOR = new Color(0.4f, 0.4f, 0.45f, 1f);

        #endregion

        #region Serialized Fields

        [SerializeField] private float _trackWidth = DEFAULT_TRACK_WIDTH;
        [SerializeField] private Color _railColor = DEFAULT_RAIL_COLOR;
        [SerializeField] private int _visualType = VISUAL_CYLINDER;
        [SerializeField] private GameObject _customSegmentPrefab; // For VISUAL_CUSTOM3D

        #endregion

        #region Fields

        private readonly List<GameObject> _trackSegments = new List<GameObject>();
        private Material _trackMaterial;
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
            // Visual rendering disabled — conveyor belt visuals are handled by
            // manually painted tiles in the BoardGrid tilemap.
            // RailManager (waypoint data) remains active for holder movement.
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
            // Visual rendering disabled — BoardGrid's ConveyorTiles tilemap handles rail visuals.
            // RailManager (waypoint data) remains active for holder movement.
        }

        #endregion
    }
}
