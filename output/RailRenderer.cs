using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Visualizes the conveyor belt rail path using 3D cylinder segment primitives.
    /// Reads waypoints from RailManager and renders a segmented cylindrical track.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: UX | Phase: 1
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow
    /// </remarks>
    public class RailRenderer : MonoBehaviour
    {
        #region Constants

        private const float DEFAULT_TRACK_WIDTH = 0.3f;
        private static readonly Color DEFAULT_RAIL_COLOR = new Color(0.4f, 0.4f, 0.45f, 1f);

        #endregion

        #region Serialized Fields

        [SerializeField] private float _trackWidth = DEFAULT_TRACK_WIDTH;
        [SerializeField] private Color _railColor = DEFAULT_RAIL_COLOR;

        #endregion

        #region Fields

        private readonly List<GameObject> _trackSegments = new List<GameObject>();
        private Material _trackMaterial;
        private bool _isInitialized;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            _trackMaterial = new Material(Shader.Find("Standard"));
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
            int segmentCount = isLoop ? waypoints.Length : waypoints.Length - 1;

            for (int i = 0; i < segmentCount; i++)
            {
                Vector3 start = waypoints[i];
                // Wrap to first waypoint for the closing segment of a loop
                Vector3 end = (i == waypoints.Length - 1) ? waypoints[0] : waypoints[i + 1];

                Vector3 midpoint = (start + end) * 0.5f;
                float length = Vector3.Distance(start, end);

                if (length < 0.001f)
                {
                    continue;
                }

                var segment = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                segment.name = $"RailSegment_{i}";
                segment.transform.SetParent(transform);

                segment.transform.position = midpoint;
                // Cylinder default height is 2 units, so height scale = length / 2
                segment.transform.localScale = new Vector3(_trackWidth, length * 0.5f, _trackWidth);
                segment.transform.up = (end - start).normalized;

                var meshRenderer = segment.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    meshRenderer.material = _trackMaterial;
                }

                // Track is visual only — disable collider
                var col = segment.GetComponent<Collider>();
                if (col != null)
                {
                    col.enabled = false;
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
            // Delay one frame to ensure RailManager has processed its layout data
            StartCoroutine(RefreshNextFrame());
        }

        private System.Collections.IEnumerator RefreshNextFrame()
        {
            yield return null;
            RefreshPath();
        }

        #endregion
    }
}
