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
            // ROLLBACK_INPUT_BLOCK_DURING_LOADING_20260623: 씬전환/스테이지클리어 로딩·페이드 중 홀더 입력 차단.
            //   로딩 화면은 UI dim 인데 홀더는 3D 콜라이더 → 물리 Raycast 가 UI 를 무시해 로딩 중 탭이 먹던 버그.
            //   LevelManager.IsLoading(레벨 로드 중) 또는 UIManager.IsFading(페이드 overlay 가시) 동안 입력 무시.
            //   롤백: 이 if 블록 삭제.
            if ((LevelManager.HasInstance && LevelManager.Instance.IsLoading)
                || (UIManager.HasInstance && UIManager.Instance.IsFading)
                // ROLLBACK_NEWFEATURE_WORLD_INPUT_BLOCK_20260629:
                // NewFeature is a UI popup, but holder taps are physics raycasts. Block the world
                // path explicitly while the feature popup queue is open so touches cannot deploy
                // holders through the popup/loading transition.
                || (NewFeatureManager.HasInstance && NewFeatureManager.Instance.IsShowingPopup)) return;
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

            // 팝업(Settings/GoldShop/BuyItem 등)이 열려 있는 동안 다트 보관함 등 인게임 오브젝트 터치 차단.
            bool awaitingHolderSelection = BoosterExecutor.HasInstance
                && BoosterExecutor.Instance.IsAwaitingHolderSelection;
            bool awaitingBalloonClick = BoosterExecutor.HasInstance
                && BoosterExecutor.Instance.IsAwaitingBalloonClick;
            bool awaitingUseItemWorldSelection = awaitingHolderSelection || awaitingBalloonClick;

            // ROLLBACK_USEITEM_PAUSE_WORLD_INPUT_20260609:
            // UseItem pauses the game while Hand/Zap wait for a world click. Keep normal popup
            // pause blocking, but allow only these interactive item selections to pass through.
            if (PauseManager.IsPaused && !awaitingUseItemWorldSelection) return;

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

            // ITEM_TUTORIAL_HOLDER_BLOCK_20260628:
            // While a "tap the item button" tutorial step is waiting, only the highlighted item UI may be
            // tapped. The holder is a world collider on this separate physics-raycast path, so it would
            // otherwise stay clickable. Block it until the booster itself begins awaiting a world target
            // (Hand/Zap), which is signalled by awaitingUseItemWorldSelection above.
            if (!awaitingUseItemWorldSelection
                && TutorialController.HasInstance
                && TutorialController.Instance.IsAwaitingItemTap())
                return;

            // ROLLBACK_TUTORIAL_ITEM_FADE_HOLDER_BLOCK_20260706:
            // 아이템 튜토리얼 진입 시, dim 페이드가 '적용되기 전(페이드 인 중)' 에는 홀더(월드 콜라이더)가 터치되면 안 된다.
            // (사용자 보고: 페이드 연출 전에 홀더가 눌려버리는 이슈.) 페이드가 다 적용된 뒤엔 각 스텝 규칙(tap_item 은 위 블록,
            // tap_holder 는 정상 허용)이 이어받는다. 부스터가 월드 타겟 대기(Hand/Zap)면 예외적으로 통과.
            if (!awaitingUseItemWorldSelection
                && TutorialController.HasInstance && TutorialController.Instance.IsTutorialActive()
                && TutorialManager.HasInstance && TutorialManager.Instance.IsDimFadeInProgress)
                return;

            Ray ray = _gameCamera.ScreenPointToRay(screenPosition);

            // ROLLBACK_ZAP_UI_HOLE_INPUT:
            // Zap(Color Remove) uses a UI dim with a cutout over the board. UI raycast state can
            // still be true there, so balloon picking must run before holder-only UI blocking.
            if (awaitingBalloonClick)
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

                // ROLLBACK_USEITEM_ZAP_CONSUME_MISS_20260609:
                // During Zap selection, a miss should not fall through into holder tapping while
                // the UseItem popup is paused/open.
                return;
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

            bool boosterAwaiting = awaitingHolderSelection;

            // ROLLBACK_DEPLOYING_BOX_UNTOUCHABLE_20260619: 레일에 배치 중(isDeploying)/이동 중(isMovingToRail)인
            //   다트 박스는 터치 무시. 배치 중 박스는 앞줄을 벗어나 있어 아래 IsInFrontRow=false 분기로 빠져
            //   OnHolderClickAnim(TriggerClick)이 발행 → 박스 뚜껑이 다시 닫히는 버그. 배포 중엔 입력 자체를 막는다.
            //   (부스터 선택 대기 중에는 ForceSelectHolder 가 자체적으로 isDeploying 을 거르므로 여기선 일반 터치만 차단.)
            if (!boosterAwaiting && HolderManager.HasInstance)
            {
                HolderData __hData = HolderManager.Instance.FindHolderPublic(holder.HolderId);
                if (__hData != null && (__hData.isDeploying || __hData.isMovingToRail))
                    return;
            }

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

            // ROLLBACK_VERTICAL_CHAIN_DEPLOY_20260625: 기존엔 '그룹 전원 앞줄'(AND)이라 세로 체인(같은 열에
            //   스택돼 뒤 멤버 A-2(row1)가 앞줄이 아님)이 영영 1차 차단됐다. → '그룹이 차지한 각 열마다 앞줄
            //   선두 멤버가 하나 존재' 로 완화(HolderManager.TrySelectChainGroup 배포 규칙과 일치).
            //   가로 체인은 열당 1명이라 동작 동일. 세로 체인은 맨 위(앞줄) 멤버가 그 열의 선두.
            var members = HolderManager.Instance.GetChainGroup(holder.chainGroupId);
            var memberColumns = new System.Collections.Generic.HashSet<int>();
            var frontColumns = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < members.Count; i++)
            {
                HolderData m = HolderManager.Instance.FindHolderPublic(members[i]);
                if (m == null) continue;
                memberColumns.Add(m.column);
                if (HolderVisualManager.Instance.IsInFrontRow(members[i]))
                    frontColumns.Add(m.column);
            }
            foreach (int col in memberColumns)
                if (!frontColumns.Contains(col)) return false;
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
