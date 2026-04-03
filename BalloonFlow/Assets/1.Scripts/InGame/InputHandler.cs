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

            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, _holderLayerMask))
            {
                HolderIdentifier holder = hit.collider.GetComponent<HolderIdentifier>();
                if (holder != null)
                {
                    // 부스터(Hand/SelectTool) 대기 중이면 모든 보관함 클릭 가능
                    bool boosterAwaiting = BoosterExecutor.HasInstance
                        && BoosterExecutor.Instance.IsAwaitingHolderSelection;

                    if (!boosterAwaiting)
                    {
                        // 일반 상태: 앞줄만 클릭 가능
                        if (HolderVisualManager.HasInstance
                            && !HolderVisualManager.Instance.IsInFrontRow(holder.HolderId))
                        {
                            return;
                        }
                    }

                    EventBus.Publish(new OnHolderTapped { holderId = holder.HolderId });
                }
            }
        }

        #endregion
    }

    // HolderIdentifier moved to HolderIdentifier.cs (Unity requires class name == file name for prefab serialization)
}
