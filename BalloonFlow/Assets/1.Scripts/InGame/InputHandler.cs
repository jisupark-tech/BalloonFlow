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

        private bool _inputEnabled = true;

        public bool IsInputEnabled() => _inputEnabled;

        protected override void OnSingletonAwake()
        {
            if (_gameCamera == null)
                _gameCamera = Camera.main;
        }

        private void Update()
        {
            if (!_inputEnabled) return;
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

        private void ProcessInput()
        {
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                TryRaycastHolder(Touchscreen.current.primaryTouch.position.ReadValue());
                return;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                TryRaycastHolder(Mouse.current.position.ReadValue());
            }
        }

        private void TryRaycastHolder(Vector2 screenPosition)
        {
            if (_gameCamera == null) return;

            Ray ray = _gameCamera.ScreenPointToRay(screenPosition);

            if (BoosterExecutor.HasInstance && BoosterExecutor.Instance.IsAwaitingBalloonClick)
            {
                if (BalloonController.HasInstance)
                {
                    var __boosterSw = InGamePerfLogger.StartSection();
                    int balloonId = TryGetGroundPoint(ray, out Vector3 worldPos)
                        ? BalloonController.Instance.FindNearestBalloonAtWorldPos(worldPos)
                        : -1;
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
    }

    // HolderIdentifier moved to HolderIdentifier.cs (Unity requires class name == file name for prefab serialization)
}
