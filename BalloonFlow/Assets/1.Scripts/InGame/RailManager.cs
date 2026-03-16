using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Manages the circular rail path for dart/magazine movement.
    /// Provides waypoint-based path data and holder position mapping.
    /// </summary>
    /// <remarks>
    /// Layer: Core | Genre: Puzzle | Role: Manager | Phase: 0
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow
    ///
    /// The rail is a closed-loop path defined by waypoints.
    /// Magazines travel along this path, firing darts at matching balloons.
    /// After completing a loop: magazine=0 → removed, magazine>0 → returns to holder.
    /// </remarks>
    public class RailManager : SceneSingleton<RailManager>
    {
        #region Nested Types

        [System.Serializable]
        public struct RailWaypoint
        {
            public Vector3 position;
            public Vector3 tangent;
        }

        [System.Serializable]
        public struct HolderSlot
        {
            public int holderId;
            public Vector3 position;
            public Vector3 entryDirection;
        }

        #endregion

        #region Serialized Fields

        [SerializeField] private Transform[] _waypointTransforms;
        [SerializeField] private Transform[] _holderSlotTransforms;
        [SerializeField] private bool _isClosedLoop = true;
        [SerializeField] private int _railCapacity = 10;

        #endregion

        #region Fields

        private readonly List<RailWaypoint> _waypoints = new List<RailWaypoint>();
        private readonly Dictionary<int, HolderSlot> _holderSlots = new Dictionary<int, HolderSlot>();
        private float _totalPathLength;
        private readonly List<float> _segmentLengths = new List<float>();
        private readonly List<float> _cumulativeLengths = new List<float>();

        #endregion

        #region Properties

        /// <summary>
        /// Total length of the rail path.
        /// </summary>
        public float TotalPathLength => _totalPathLength;

        /// <summary>
        /// Number of waypoints defining the rail.
        /// </summary>
        public int WaypointCount => _waypoints.Count;

        /// <summary>
        /// Whether the rail forms a closed loop.
        /// </summary>
        public bool IsClosedLoop => _isClosedLoop;

        /// <summary>
        /// Maximum number of darts the rail can hold simultaneously.
        /// </summary>
        public int RailCapacity => _railCapacity;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            BuildPathFromTransforms();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns the rail path as an array of world positions.
        /// </summary>
        public Vector3[] GetRailPath()
        {
            if (_waypoints.Count == 0)
            {
                return System.Array.Empty<Vector3>();
            }

            Vector3[] path = new Vector3[_waypoints.Count];
            for (int i = 0; i < _waypoints.Count; i++)
            {
                path[i] = _waypoints[i].position;
            }
            return path;
        }

        /// <summary>
        /// Sets the rail layout from level data. Call when loading a new level.
        /// </summary>
        public void SetRailLayout(Vector3[] positions, bool closedLoop = true)
        {
            _waypoints.Clear();
            _isClosedLoop = closedLoop;

            if (positions == null || positions.Length == 0)
            {
                _totalPathLength = 0f;
                return;
            }

            foreach (var pos in positions)
            {
                _waypoints.Add(new RailWaypoint { position = pos, tangent = Vector3.forward });
            }

            RecalculatePathLengths();
        }

        /// <summary>
        /// Sets holder slot positions. Call when loading a new level.
        /// </summary>
        public void SetHolderSlots(HolderSlot[] slots)
        {
            _holderSlots.Clear();

            if (slots == null)
            {
                return;
            }

            foreach (var slot in slots)
            {
                _holderSlots[slot.holderId] = slot;
            }
        }

        /// <summary>
        /// Gets the world position for a holder by ID.
        /// </summary>
        public Vector3 GetHolderPosition(int holderId)
        {
            if (_holderSlots.TryGetValue(holderId, out HolderSlot slot))
            {
                return slot.position;
            }

            Debug.LogWarning($"[RailManager] Holder slot {holderId} not found.");
            return Vector3.zero;
        }

        /// <summary>
        /// Gets the position on the rail path at a normalized distance (0..1).
        /// </summary>
        public Vector3 GetPositionAtNormalized(float t)
        {
            if (_waypoints.Count == 0)
            {
                return Vector3.zero;
            }

            if (_waypoints.Count == 1)
            {
                return _waypoints[0].position;
            }

            t = Mathf.Clamp01(t);
            float targetDistance = t * _totalPathLength;

            return GetPositionAtDistance(targetDistance);
        }

        /// <summary>
        /// Gets the position on the rail at a specific distance from the start.
        /// </summary>
        public Vector3 GetPositionAtDistance(float distance)
        {
            if (_waypoints.Count == 0)
            {
                return Vector3.zero;
            }

            if (_waypoints.Count == 1)
            {
                return _waypoints[0].position;
            }

            if (_totalPathLength <= 0f)
            {
                return _waypoints[0].position;
            }

            // Wrap distance for closed loop
            if (_isClosedLoop && _totalPathLength > 0f)
            {
                distance = ((distance % _totalPathLength) + _totalPathLength) % _totalPathLength;
            }
            else
            {
                distance = Mathf.Clamp(distance, 0f, _totalPathLength);
            }

            // Find segment
            for (int i = 0; i < _segmentLengths.Count; i++)
            {
                if (distance <= _cumulativeLengths[i])
                {
                    float segStart = (i > 0) ? _cumulativeLengths[i - 1] : 0f;
                    float segLength = _segmentLengths[i];

                    if (segLength <= 0f)
                    {
                        return _waypoints[i].position;
                    }

                    float localT = (distance - segStart) / segLength;
                    int nextIndex = (i + 1) % _waypoints.Count;
                    return Vector3.Lerp(_waypoints[i].position, _waypoints[nextIndex].position, localT);
                }
            }

            return _waypoints[_waypoints.Count - 1].position;
        }

        /// <summary>
        /// Gets the forward direction on the rail at a normalized distance.
        /// </summary>
        public Vector3 GetDirectionAtNormalized(float t)
        {
            if (_waypoints.Count < 2)
            {
                return Vector3.forward;
            }

            const float epsilon = 0.001f;
            float tA = Mathf.Clamp01(t - epsilon);
            float tB = Mathf.Clamp01(t + epsilon);

            Vector3 posA = GetPositionAtNormalized(tA);
            Vector3 posB = GetPositionAtNormalized(tB);
            Vector3 dir = (posB - posA).normalized;

            return dir.sqrMagnitude > 0.001f ? dir : Vector3.forward;
        }

        /// <summary>
        /// Returns all holder slot data.
        /// </summary>
        public HolderSlot[] GetAllHolderSlots()
        {
            var result = new HolderSlot[_holderSlots.Count];
            int index = 0;
            foreach (var kvp in _holderSlots)
            {
                result[index++] = kvp.Value;
            }
            return result;
        }

        #endregion

        #region Private Methods

        private void BuildPathFromTransforms()
        {
            _waypoints.Clear();

            if (_waypointTransforms == null || _waypointTransforms.Length == 0)
            {
                return;
            }

            foreach (var t in _waypointTransforms)
            {
                if (t != null)
                {
                    _waypoints.Add(new RailWaypoint
                    {
                        position = t.position,
                        tangent = t.forward
                    });
                }
            }

            RecalculatePathLengths();

            // Build holder slots from transforms
            if (_holderSlotTransforms != null)
            {
                for (int i = 0; i < _holderSlotTransforms.Length; i++)
                {
                    if (_holderSlotTransforms[i] != null)
                    {
                        _holderSlots[i] = new HolderSlot
                        {
                            holderId = i,
                            position = _holderSlotTransforms[i].position,
                            entryDirection = _holderSlotTransforms[i].forward
                        };
                    }
                }
            }
        }

        private void RecalculatePathLengths()
        {
            _segmentLengths.Clear();
            _cumulativeLengths.Clear();
            _totalPathLength = 0f;

            if (_waypoints.Count < 2)
            {
                return;
            }

            int segmentCount = _isClosedLoop ? _waypoints.Count : _waypoints.Count - 1;

            for (int i = 0; i < segmentCount; i++)
            {
                int nextIndex = (i + 1) % _waypoints.Count;
                float segLen = Vector3.Distance(_waypoints[i].position, _waypoints[nextIndex].position);
                _segmentLengths.Add(segLen);
                _totalPathLength += segLen;
                _cumulativeLengths.Add(_totalPathLength);
            }
        }

        #endregion

        #region Gizmos

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (_waypointTransforms == null || _waypointTransforms.Length < 2)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            for (int i = 0; i < _waypointTransforms.Length; i++)
            {
                if (_waypointTransforms[i] == null)
                {
                    continue;
                }

                int nextIndex = (i + 1) % _waypointTransforms.Length;
                if (!_isClosedLoop && i == _waypointTransforms.Length - 1)
                {
                    break;
                }

                if (_waypointTransforms[nextIndex] != null)
                {
                    Gizmos.DrawLine(_waypointTransforms[i].position, _waypointTransforms[nextIndex].position);
                }

                Gizmos.DrawSphere(_waypointTransforms[i].position, 0.1f);
            }

            // Draw holder slots
            if (_holderSlotTransforms != null)
            {
                Gizmos.color = Color.yellow;
                foreach (var t in _holderSlotTransforms)
                {
                    if (t != null)
                    {
                        Gizmos.DrawWireSphere(t.position, 0.15f);
                    }
                }
            }
        }
#endif

        #endregion
    }
}
