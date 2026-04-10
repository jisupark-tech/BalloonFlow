using UnityEngine;
using UnityEngine.InputSystem;
using Touchscreen = UnityEngine.InputSystem.Touchscreen;

namespace BalloonFlow
{
    /// <summary>
    /// Handles touch/mouse input for holder tapping.
    /// Raycasts to Holder colliders and publishes OnHolderTapped events.
    /// Uses New Input System (Unity 6000+).
    /// </summary>
    /// <remarks>
    /// Layer: Core | Genre: Puzzle | Role: Handler | Phase: 0
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow
    /// </remarks>
    public class InputHandler : SceneSingleton<InputHandler>
    {
        #region Constants

        private const string HOLDER_TAG = "Holder";

        #endregion

        #region Serialized Fields

        [SerializeField] private Camera _gameCamera;
        [SerializeField] private LayerMask _holderLayerMask = ~0;

        #endregion

        #region Fields

        private bool _inputEnabled = true;

        #endregion

        #region Properties

        /// <summary>
        /// Whether input processing is currently enabled.
        /// </summary>
        public bool IsInputEnabled() => _inputEnabled;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            if (_gameCamera == null)
            {
                _gameCamera = Camera.main;
            }
        }

        private void Update()
        {
            if (!_inputEnabled)
            {
                return;
            }

            ProcessInput();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Enables input processing.
        /// </summary>
        public void EnableInput()
        {
            _inputEnabled = true;
            EventBus.Publish(new OnInputStateChanged { enabled = true });
        }

        /// <summary>
        /// Disables input processing.
        /// </summary>
        public void DisableInput()
        {
            _inputEnabled = false;
            EventBus.Publish(new OnInputStateChanged { enabled = false });
        }

        #endregion

        #region Private Methods

        private void ProcessInput()
        {
            // Touch input (mobile)
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                Vector2 pos = Touchscreen.current.primaryTouch.position.ReadValue();
                TryRaycastHolder(pos);
                return;
            }

            // Mouse input (editor / desktop)
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                TryRaycastHolder(Mouse.current.position.ReadValue());
            }
        }

        private void TryRaycastHolder(Vector2 screenPosition)
        {
            if (_gameCamera == null)
            {
                return;
            }

            Ray ray = _gameCamera.ScreenPointToRay(screenPosition);

            // Color Remove 모드: 풍선 클릭 감지 (Collider-free)
            if (BoosterExecutor.HasInstance && BoosterExecutor.Instance.IsAwaitingBalloonClick)
            {
                if (BalloonController.HasInstance)
                {
                    // Orthographic 카메라: screenPos → worldPos (Y=0 평면)
                    Vector3 worldPos = _gameCamera.ScreenToWorldPoint(
                        new Vector3(screenPosition.x, screenPosition.y, _gameCamera.nearClipPlane));
                    worldPos.y = 0.1f; // 풍선 Y 높이

                    int balloonId = BalloonController.Instance.FindNearestBalloonAtWorldPos(worldPos);
                    if (balloonId >= 0)
                    {
                        BoosterExecutor.Instance.OnBalloonClicked(balloonId);
                        return; // Don't process holder tap
                    }
                }
            }

            // RaycastAll: 앞줄이 뒷줄을 가려도 모든 hit 처리
            RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, _holderLayerMask);
            if (hits.Length == 0) return;

            // 카메라에서 가장 가까운 hit부터 정렬
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                HolderIdentifier holder = hits[i].collider.GetComponent<HolderIdentifier>();
                if (holder == null) continue;

                bool boosterAwaiting = BoosterExecutor.HasInstance
                    && BoosterExecutor.Instance.IsAwaitingHolderSelection;

                if (!boosterAwaiting)
                {
                    if (HolderVisualManager.HasInstance
                        && !HolderVisualManager.Instance.IsInFrontRow(holder.HolderId))
                    {
                        // 앞줄 아닌 보관함: Click 애니메이션만
                        EventBus.Publish(new OnHolderClickAnim { holderId = holder.HolderId });
                        return;
                    }
                }

                // 앞줄 보관함 또는 부스터 모드: 정상 탭 처리
                EventBus.Publish(new OnHolderTapped { holderId = holder.HolderId });
                return;
            }
        }

        #endregion
    }

    // HolderIdentifier moved to HolderIdentifier.cs (Unity requires class name == file name for prefab serialization)
}
