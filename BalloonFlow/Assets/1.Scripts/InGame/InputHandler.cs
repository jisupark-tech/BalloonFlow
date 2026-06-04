using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Touchscreen = UnityEngine.InputSystem.Touchscreen;

namespace BalloonFlow
{
    /// <summary>
    /// Handles touch/mouse input for holder tapping.
    /// Raycasts to Holder colliders and publishes OnHolderTapped events.
    /// Uses New Input System (Unity 6000+).
    /// </summary>
    public class InputHandler : SceneSingleton<InputHandler>
    {
        private const string HOLDER_TAG = "Holder";

        [SerializeField] private Camera _gameCamera;
        [SerializeField] private LayerMask _holderLayerMask = ~0;

        // ROLLBACK_INPUT_NO_SORT_RAYCAST:
        // Restore RaycastAll/Array.Sort if exact hit ordering is needed again.
        // Current gameplay only needs the closest HolderIdentifier hit, so this avoids per-click
        // sorting and repeated GetComponent calls without changing holder selection rules.
        private static readonly RaycastHit[] _raycastHitsCache = new RaycastHit[16];
        private static readonly Dictionary<Collider, HolderIdentifier> _holderByCollider =
            new Dictionary<Collider, HolderIdentifier>(64);
        private static readonly List<RaycastResult> _uiRaycastResults = new List<RaycastResult>(16);
        private static PointerEventData _uiPointerEventData;
        private static EventSystem _uiPointerEventSystem;

        private bool _inputEnabled = true;
        private float _suppressInputUntilUnscaled;

        public bool IsInputEnabled() => _inputEnabled;

        protected override void OnSingletonAwake()
        {
            if (_gameCamera == null)
                _gameCamera = Camera.main;
        }

        private void Update()
        {
            if (!_inputEnabled) return;
            if (Time.unscaledTime < _suppressInputUntilUnscaled) return;
            var __sw = InGamePerfLogger.StartSection();
            try
            {
                ProcessInput();
            }
            finally
            {
                InGamePerfLogger.EndSection(__sw, "InputHandler.Update");
            }
        }

        public void EnableInput()
        {
            _inputEnabled = true;
            EventBus.Publish(new OnInputStateChanged { enabled = true });
        }

        public void DisableInput()
        {
            _inputEnabled = false;
            EventBus.Publish(new OnInputStateChanged { enabled = false });
        }

        public void SuppressInput(float duration)
        {
            _suppressInputUntilUnscaled = Mathf.Max(
                _suppressInputUntilUnscaled,
                Time.unscaledTime + Mathf.Max(0f, duration));
        }

        private void ProcessInput()
        {
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                TryRaycastInput(Touchscreen.current.primaryTouch.position.ReadValue());
                return;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                TryRaycastInput(Mouse.current.position.ReadValue());
            }
        }

        private void TryRaycastInput(Vector2 screenPosition)
        {
            if (_gameCamera == null) return;

            // ROLLBACK_USEITEM_CLOSE_ONLY_INPUT_BLOCK:
            // Only the UseItem close buttons should swallow gameplay input. The dim/cutout UI
            // can cover the board/holder selection area, especially for Zap and Hand.
            if (PopupUseItem.IsScreenPointOverActiveCloseButton(screenPosition))
                return;

            // UI buttons must consume the touch before it can hit world holder colliders behind them.
            // Keep this button-only so UseItem/Tutorial cutout dim images can still pass intentional
            // holder/balloon selection through the cutout area.
            if (IsScreenPointOverBlockingUIControl(screenPosition))
                return;

            Ray ray = _gameCamera.ScreenPointToRay(screenPosition);

            // ROLLBACK_ZAP_UI_HOLE_INPUT:
            // Zap(Color Remove) uses a UI dim with a cutout over the board. UI raycast state can
            // still be true there, so balloon picking must run before holder-only UI blocking.
            if (BoosterExecutor.HasInstance && BoosterExecutor.Instance.IsAwaitingBalloonClick)
            {
                if (BalloonController.HasInstance)
                {
                    var __boosterSw = InGamePerfLogger.StartSection();
                    // ROLLBACK_ZAP_SCREEN_SPACE_PICK:
                    // Zap selection must follow the rendered balloon positions. Projecting the
                    // click ray onto Y=0 shifts the pick on the tilted in-game camera and can
                    // select the balloon visually above the touch point.
                    int balloonId = BalloonController.Instance.FindNearestBalloonAtScreenPoint(
                        _gameCamera,
                        screenPosition);
                    InGamePerfLogger.EndSection(__boosterSw, "Input.BoosterBalloonPick");
                    if (balloonId >= 0)
                    {
                        BoosterExecutor.Instance.OnBalloonClicked(balloonId);
                        return;
                    }
                }
            }

            var __raySw = InGamePerfLogger.StartSection();
            int hitCount = Physics.RaycastNonAlloc(ray, _raycastHitsCache, Mathf.Infinity, _holderLayerMask);
            InGamePerfLogger.EndSection(__raySw, "Input.RaycastNonAlloc");
            if (hitCount == 0) return;

            var __selectSw = InGamePerfLogger.StartSection();
            HolderIdentifier holder = null;
            float closestDistance = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = _raycastHitsCache[i].collider;
                if (hitCollider == null) continue;

                HolderIdentifier candidate = GetHolderFromCollider(hitCollider);
                if (candidate == null) continue;

                float distance = _raycastHitsCache[i].distance;
                if (distance >= closestDistance) continue;

                holder = candidate;
                closestDistance = distance;
            }
            InGamePerfLogger.EndSection(__selectSw, "Input.SelectClosestHolder");

            if (holder == null) return;

            bool boosterAwaiting = BoosterExecutor.HasInstance
                && BoosterExecutor.Instance.IsAwaitingHolderSelection;

            if (!boosterAwaiting
                && HolderVisualManager.HasInstance
                && !HolderVisualManager.Instance.IsInFrontRow(holder.HolderId))
            {
                EventBus.Publish(new OnHolderClickAnim { holderId = holder.HolderId });
                return;
            }

            // Chain: 그룹 전원 앞줄 검증 (AND 조건, 2026-05-18).
            // 이전 OR 동작 (탭한 보관함만 앞줄이면 chain 전체 배치) → 그룹 전원 앞줄에서만 배치 가능.
            if (!boosterAwaiting && !AllChainMembersInFrontRow(holder.HolderId))
            {
                EventBus.Publish(new OnHolderClickAnim { holderId = holder.HolderId });
                return;
            }

            EventBus.Publish(new OnHolderTapped { holderId = holder.HolderId });
        }

        /// <summary>
        /// Chain group 의 모든 보관함이 앞줄에 있는지 검증.
        /// 일반 보관함 (chainGroupId &lt; 0) 또는 매니저 미준비 시 true 반환 (early-out).
        /// </summary>
        private static bool AllChainMembersInFrontRow(int holderId)
        {
            if (!HolderManager.HasInstance || !HolderVisualManager.HasInstance) return true;

            HolderData holder = HolderManager.Instance.FindHolderPublic(holderId);
            if (holder == null || holder.chainGroupId < 0) return true;

            var members = HolderManager.Instance.GetChainGroup(holder.chainGroupId);
            for (int i = 0; i < members.Count; i++)
            {
                int mid = members[i];
                if (mid == holderId) continue;
                if (!HolderVisualManager.Instance.IsInFrontRow(mid)) return false;
            }
            return true;
        }

        private static bool TryGetGroundPoint(Ray ray, out Vector3 worldPos)
        {
            Plane boardPlane = new Plane(Vector3.up, Vector3.zero);
            if (boardPlane.Raycast(ray, out float enter))
            {
                worldPos = ray.GetPoint(enter);
                return true;
            }

            worldPos = default;
            return false;
        }

        private static HolderIdentifier GetHolderFromCollider(Collider col)
        {
            if (col == null) return null;
            if (_holderByCollider.TryGetValue(col, out HolderIdentifier cached))
                return cached;

            HolderIdentifier holder = col.GetComponent<HolderIdentifier>();
            _holderByCollider[col] = holder;
            return holder;
        }

        private static bool IsScreenPointOverBlockingUIControl(Vector2 screenPosition)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null) return false;

            if (_uiPointerEventData == null || _uiPointerEventSystem != eventSystem)
            {
                _uiPointerEventData = new PointerEventData(eventSystem);
                _uiPointerEventSystem = eventSystem;
            }

            _uiPointerEventData.Reset();
            _uiPointerEventData.position = screenPosition;
            _uiRaycastResults.Clear();
            eventSystem.RaycastAll(_uiPointerEventData, _uiRaycastResults);

            for (int i = 0; i < _uiRaycastResults.Count; i++)
            {
                GameObject hit = _uiRaycastResults[i].gameObject;
                if (hit == null || !hit.activeInHierarchy) continue;

                Selectable selectable = hit.GetComponentInParent<Selectable>();
                if (selectable != null && selectable.IsActive())
                    return true;
            }

            return false;
        }
    }

    // HolderIdentifier moved to HolderIdentifier.cs (Unity requires class name == file name for prefab serialization)
}
