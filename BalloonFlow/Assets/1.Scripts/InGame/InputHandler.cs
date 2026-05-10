using System.Collections.Generic;
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

        // [Optimization 2026-05-10] RaycastAll 매 입력마다 RaycastHit[] alloc 하던 부분 제거.
        // 16 = 한 ray 가 통과하는 holder collider 의 현실적 상한 (보드 holder ~5–8개). 부족 시 size 확장.
        // 정렬 comparer 도 람다 alloc 대신 정적 캐시 사용.
        // 롤백: 두 필드 제거 + Tap 메서드 의 RaycastAll/Array.Sort 원본 라인 복원.
        private static readonly RaycastHit[] _raycastHitsCache = new RaycastHit[16];
        private static readonly IComparer<RaycastHit> _hitDistanceComparer =
            Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

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
            // [Optimization 2026-05-10] RaycastNonAlloc + 정적 buffer + 정적 comparer → 매 입력 GC alloc 0.
            // 롤백: 캐시 분기 제거 + 아래 주석 처리된 원본 라인 복원.
            // 원본:
            // RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, _holderLayerMask);
            // if (hits.Length == 0) return;
            // System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            int hitCount = Physics.RaycastNonAlloc(ray, _raycastHitsCache, Mathf.Infinity, _holderLayerMask);
            if (hitCount == 0) return;

            // 카메라에서 가장 가까운 hit부터 정렬 (buffer 의 앞 hitCount 만)
            System.Array.Sort(_raycastHitsCache, 0, hitCount, _hitDistanceComparer);

            for (int i = 0; i < hitCount; i++)
            {
                HolderIdentifier holder = _raycastHitsCache[i].collider.GetComponent<HolderIdentifier>();
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
