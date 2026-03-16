using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Data container for an active dart on the rail.
    /// </summary>
    [System.Serializable]
    public class DartData
    {
        public int dartId;
        public int holderId;
        public int color;
        public GameObject gameObject;
        public float distanceTraveled;
        public bool isActive;
    }

    /// <summary>
    /// Manages darts that travel the circular rail and fire at matching-color balloons.
    /// When a holder is selected, darts deploy sequentially onto the rail, move along
    /// the path, and hit matching balloons 1:1. Magazine=0 mid-loop removes from rail.
    /// Magazine>0 after full loop returns to holder.
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Manager | Phase: 1
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow (ingame_holder_dart_pop)
    /// </remarks>
    public class DartManager : SceneSingleton<DartManager>
    {
        #region Constants

        private const string DART_POOL_KEY = "Dart";
        private const float DEFAULT_DART_SPEED = 5f;
        private const float DEFAULT_DEPLOY_INTERVAL = 0.15f;
        private const float HIT_DETECTION_RADIUS = 0.5f;

        #endregion

        #region Serialized Fields

        [SerializeField] private float _dartSpeed = DEFAULT_DART_SPEED;
        [SerializeField] private float _deployInterval = DEFAULT_DEPLOY_INTERVAL;
        [SerializeField] private float _hitRadius = HIT_DETECTION_RADIUS;

        #endregion

        #region Fields

        private readonly List<DartData> _activeDarts = new List<DartData>();
        private int _nextDartId;
        private int _deployedCount;
        private bool _isDeploying;

        #endregion

        #region Properties

        /// <summary>
        /// Current dart movement speed along the rail.
        /// </summary>
        public float DartSpeed => _dartSpeed;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            _nextDartId = 0;
            _deployedCount = 0;

            // GameManager.Board에서 설정값 읽기
            if (GameManager.HasInstance)
            {
                _dartSpeed = GameManager.Instance.Board.dartRailSpeed;
            }
        }

        private void OnEnable()
        {
            // OnHolderSelected is handled by HolderVisualManager (straight-line darts).
            // DartManager.StartRailLoop() is available for direct calls if needed.
        }

        private void OnDisable()
        {
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts a rail loop deployment for a holder. Darts are deployed
        /// sequentially onto the rail and begin moving along the path.
        /// </summary>
        public void StartRailLoop(int holderId, int color, int magazineCount)
        {
            if (magazineCount <= 0)
            {
                Debug.LogWarning($"[DartManager] StartRailLoop: magazineCount is 0 for holder {holderId}.");
                return;
            }

            if (!RailManager.HasInstance)
            {
                Debug.LogError("[DartManager] RailManager not available.");
                return;
            }

            StartCoroutine(DeployAndTraverseCoroutine(holderId, color, magazineCount));
        }

        /// <summary>
        /// Returns all currently active darts on the rail.
        /// </summary>
        public DartData[] GetActiveDarts()
        {
            return _activeDarts.ToArray();
        }

        /// <summary>
        /// Returns the total number of darts deployed in the current session.
        /// </summary>
        public int GetDeployedCount()
        {
            return _deployedCount;
        }

        /// <summary>
        /// Whether a deployment sequence is currently in progress.
        /// </summary>
        public bool IsDeploymentInProgress()
        {
            return _isDeploying;
        }

        /// <summary>
        /// Clears all active darts and returns them to pool.
        /// </summary>
        public void ClearAllDarts()
        {
            for (int i = _activeDarts.Count - 1; i >= 0; i--)
            {
                ReturnDartToPool(_activeDarts[i]);
            }
            _activeDarts.Clear();
            _deployedCount = 0;
            _isDeploying = false;
        }

        /// <summary>
        /// Resets state for a new level.
        /// </summary>
        public void ResetAll()
        {
            StopAllCoroutines();
            ClearAllDarts();
            _nextDartId = 0;
        }

        #endregion

        #region Private Methods — Deployment Coroutine

        private IEnumerator DeployAndTraverseCoroutine(int holderId, int color, int magazineCount)
        {
            _isDeploying = true;

            RailManager rail = RailManager.Instance;
            if (rail == null)
            {
                _isDeploying = false;
                yield break;
            }

            Vector3[] railPath = rail.GetRailPath();
            if (railPath == null || railPath.Length < 2)
            {
                Debug.LogError("[DartManager] Rail path is invalid or has fewer than 2 waypoints.");
                _isDeploying = false;
                yield break;
            }

            float totalLength = rail.TotalPathLength;
            if (totalLength <= 0f)
            {
                Debug.LogError("[DartManager] Rail total path length is zero.");
                _isDeploying = false;
                yield break;
            }

            // Deploy all darts sequentially onto the rail entry point
            List<DartData> deployedDarts = new List<DartData>(magazineCount);
            Vector3 entryPosition = rail.GetPositionAtNormalized(0f);

            for (int i = 0; i < magazineCount; i++)
            {
                DartData dart = SpawnDart(holderId, color, entryPosition);
                if (dart == null)
                {
                    continue;
                }

                deployedDarts.Add(dart);
                _deployedCount++;

                EventBus.Publish(new OnDartDeployed
                {
                    dartId = dart.dartId,
                    color = color,
                    position = entryPosition
                });

                if (i < magazineCount - 1)
                {
                    yield return new WaitForSeconds(_deployInterval);
                }
            }

            _isDeploying = false;

            if (deployedDarts.Count == 0)
            {
                yield break;
            }

            // Move all deployed darts along the rail
            yield return MoveAlongRail(deployedDarts, holderId, color, rail);

            // After rail loop completes, determine outcome
            int remaining = CountActiveDartsForHolder(deployedDarts);

            // Clean up any remaining dart objects (those that didn't hit)
            for (int i = deployedDarts.Count - 1; i >= 0; i--)
            {
                if (deployedDarts[i].isActive)
                {
                    ReturnDartToPool(deployedDarts[i]);
                    _activeDarts.Remove(deployedDarts[i]);
                }
            }

            // Publish rail loop complete
            EventBus.Publish(new OnRailLoopComplete
            {
                holderId = holderId,
                remainingMagazine = remaining
            });

            EventBus.Publish(new OnDeploymentComplete
            {
                color = color,
                count = deployedDarts.Count - remaining
            });
        }

        /// <summary>
        /// Moves darts along the rail path. Each dart checks for matching-color
        /// balloon hits at each position. Darts that hit a balloon are consumed.
        /// </summary>
        private IEnumerator MoveAlongRail(List<DartData> darts, int holderId, int color, RailManager rail)
        {
            float totalLength = rail.TotalPathLength;
            bool allComplete = false;

            // Spacing between darts on the rail
            float spacing = Mathf.Min(totalLength * 0.05f, 0.5f);

            // Initialize positions with spacing offsets (negative = behind leader)
            for (int i = 0; i < darts.Count; i++)
            {
                darts[i].distanceTraveled = -(i * spacing);
            }

            while (!allComplete)
            {
                allComplete = true;
                float deltaDistance = _dartSpeed * Time.deltaTime;

                for (int i = 0; i < darts.Count; i++)
                {
                    DartData dart = darts[i];
                    if (!dart.isActive)
                    {
                        continue;
                    }

                    dart.distanceTraveled += deltaDistance;

                    // Check if this dart has completed the full loop
                    if (dart.distanceTraveled >= totalLength)
                    {
                        // Dart completed loop — still active means it didn't hit a balloon
                        continue;
                    }

                    allComplete = false;

                    // Only update position if dart has entered the rail (positive distance)
                    if (dart.distanceTraveled >= 0f)
                    {
                        Vector3 pos = rail.GetPositionAtDistance(dart.distanceTraveled);
                        if (dart.gameObject != null)
                        {
                            dart.gameObject.transform.position = pos;

                            // Orient dart along rail direction
                            float normalizedT = dart.distanceTraveled / totalLength;
                            Vector3 dir = rail.GetDirectionAtNormalized(normalizedT);
                            if (dir.sqrMagnitude > 0.001f)
                            {
                                dart.gameObject.transform.forward = dir;
                            }
                        }

                        // Try to hit a matching balloon at current position
                        TryHitBalloon(dart, color, pos);
                    }
                }

                // Check if all remaining darts have completed the loop
                bool anyStillMoving = false;
                for (int i = 0; i < darts.Count; i++)
                {
                    if (darts[i].isActive && darts[i].distanceTraveled < totalLength)
                    {
                        anyStillMoving = true;
                        break;
                    }
                }

                if (!anyStillMoving)
                {
                    allComplete = true;
                }

                yield return null;
            }
        }

        /// <summary>
        /// Attempts to hit a matching-color balloon near the dart's position.
        /// On hit: publishes OnDartHitBalloon, deactivates dart, consumes magazine.
        /// </summary>
        private void TryHitBalloon(DartData dart, int color, Vector3 dartPosition)
        {
            if (!dart.isActive)
            {
                return;
            }

            // Use 3D Physics overlap to detect balloons within hit radius
            Collider[] hits = Physics.OverlapSphere(dartPosition, _hitRadius);
            if (hits == null || hits.Length == 0) return;

            for (int i = 0; i < hits.Length; i++)
            {
                BalloonIdentifier balloon = hits[i].GetComponent<BalloonIdentifier>();
                if (balloon != null && balloon.Color == color && !balloon.IsPopped)
                {
                    ExecuteHit(dart, balloon);
                    return;
                }
            }
        }

        private void ExecuteHit(DartData dart, BalloonIdentifier balloon)
        {
            dart.isActive = false;

            EventBus.Publish(new OnDartFired
            {
                dartId = dart.dartId,
                holderId = dart.holderId,
                color = dart.color
            });

            EventBus.Publish(new OnDartHitBalloon
            {
                dartId = dart.dartId,
                balloonId = balloon.BalloonId
            });

            // Consume magazine from the holder
            if (HolderManager.HasInstance)
            {
                HolderManager.Instance.ConsumeMagazine(dart.holderId);
            }

            // Return dart to pool
            ReturnDartToPool(dart);
            _activeDarts.Remove(dart);
        }

        #endregion

        #region Private Methods — Pool Management

        private DartData SpawnDart(int holderId, int color, Vector3 position)
        {
            GameObject dartObj = null;

            if (ObjectPoolManager.HasInstance)
            {
                dartObj = ObjectPoolManager.Instance.Get(DART_POOL_KEY, position, Quaternion.identity);
            }

            if (dartObj == null)
            {
                Debug.LogError($"[DartManager] Failed to get dart from pool '{DART_POOL_KEY}'.");
                return null;
            }

            DartData dart = new DartData
            {
                dartId = _nextDartId++,
                holderId = holderId,
                color = color,
                gameObject = dartObj,
                distanceTraveled = 0f,
                isActive = true
            };

            _activeDarts.Add(dart);
            return dart;
        }

        private void ReturnDartToPool(DartData dart)
        {
            if (dart.gameObject != null && ObjectPoolManager.HasInstance)
            {
                ObjectPoolManager.Instance.Return(DART_POOL_KEY, dart.gameObject);
            }
            dart.gameObject = null;
            dart.isActive = false;
        }

        private int CountActiveDartsForHolder(List<DartData> darts)
        {
            int count = 0;
            for (int i = 0; i < darts.Count; i++)
            {
                if (darts[i].isActive)
                {
                    count++;
                }
            }
            return count;
        }

        #endregion

        #region Private Methods — Event Handlers

        // HandleHolderSelected removed — HolderVisualManager handles holder selection
        // and fires darts straight to balloons (not along rail path).
        // DartManager.StartRailLoop() remains available for programmatic use.

        #endregion
    }

    // BalloonIdentifier moved to BalloonIdentifier.cs (Unity requires class name == file name for prefab serialization)
}
