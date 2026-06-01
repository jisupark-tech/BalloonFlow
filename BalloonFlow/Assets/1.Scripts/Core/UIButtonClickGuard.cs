using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// [2026-05-13] 모든 UI Button 의 더블/연타 클릭 방어.
    ///
    /// 동작: IPointerClickHandler 로 클릭 이벤트를 별도로 수신 (Button 의 onClick UnityEvent 와 독립).
    /// 클릭 발생 시 Button.enabled = false 로 일정 시간 비활성 → EventSystem 이 다음 클릭부터
    /// Button 에 OnPointerClick 을 dispatch 못 함. COOLDOWN 후 enabled = true.
    ///
    /// 구성:
    ///  - 이 컴포넌트는 Button 컴포넌트가 추가된 GameObject 에 부착 (AddComponent 시점이 Button 보다 나중).
    ///  - EventSystem.Execute&lt;IPointerClickHandler&gt; 는 component 추가 순서대로 dispatch → Button 먼저 실행,
    ///    이 가드가 그 다음 실행. 따라서 정상 클릭 1회는 통과, 가드가 enabled=false 로 막아 후속 클릭 차단.
    ///  - Button.interactable 은 건드리지 않으므로 시각적 darken 없음.
    ///  - Button.onClick UnityEvent 와 무관 → 일부 popup 이 RemoveAllListeners() 호출해도 가드 영향 없음.
    ///
    /// Time.unscaledTime 사용 — timeScale=0 popup (예: PopupSettings, PopupUseItem) 에서도 정상 동작.
    /// GameObject 비활성 시 OnEnable 에서 즉시 복원 → popup close→reopen 시 stuck-disabled 회피.
    ///
    /// 자동 부착:
    ///  - RuntimeInitializeOnLoadMethod: 게임 시작 직후 + SceneManager.sceneLoaded 시 매 씬 스캔
    ///  - UIBase.Awake → AttachToHierarchy(gameObject) (popup 동적 spawn 대응)
    ///  - 그 외 동적 spawn 위치는 호출자가 AttachToHierarchy 호출 (UIShop/PopupGoldShop/UILobby 등)
    /// </summary>
    [DisallowMultipleComponent]
    public class UIButtonClickGuard : MonoBehaviour, IPointerClickHandler
    {
        // 더블탭 방어 기준 — 일반적인 더블탭 간격(<300ms) 차단. 너무 길면 정상 연타 의도(예: +/- 카운터)도 막힘.
        private const float COOLDOWN = 0.3f;

        private Button _button;
        private float _restoreAt = -1f;
        private bool _cooldownActive;

        private void Awake()
        {
            _button = GetComponent<Button>();
        }

        // popup 재오픈 시 cooldown 중이었던 button 즉시 복원 (popup close→open 사이 시간이 cooldown 보다 길어도 안전).
        private void OnEnable()
        {
            if (_cooldownActive)
            {
                if (_button != null) _button.enabled = true;
                _cooldownActive = false;
                _restoreAt = -1f;
            }
        }

        private void OnDisable()
        {
            // popup close 시 button 이 disabled 잠긴 채 끝나지 않도록 복원.
            if (_cooldownActive && _button != null) _button.enabled = true;
            _cooldownActive = false;
            _restoreAt = -1f;
        }

        private void Update()
        {
            // 비-cooldown 시 single-branch early return — 부하 무시 가능.
            if (_restoreAt < 0f || Time.unscaledTime < _restoreAt) return;
            if (_button != null) _button.enabled = true;
            _cooldownActive = false;
            _restoreAt = -1f;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_button == null) return;
            // Button.OnPointerClick 이 같은 dispatch loop 에서 onClick.Invoke() 를 이미 실행한 상태.
            // 다음 클릭 차단 — EventSystem 이 disabled Button 에 OnPointerClick 미dispatch.
            _button.enabled = false;
            // 전역 UI 버튼 SFX (Common_Button_Touch). 개별 팝업의 PlayNormalTouch/PlayPopupTouch 호출과 별개 — 누락 보강.
            if (AudioManager.HasInstance) AudioManager.Instance.PlayButtonClick();
            _cooldownActive = true;
            _restoreAt = Time.unscaledTime + COOLDOWN;
        }

        // ─────────────────────────────────────────────────────────────────────
        // 자동 부착 (전역)
        // ─────────────────────────────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void HookSceneLoad()
        {
            AttachAllInScene(SceneManager.GetActiveScene());
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            AttachAllInScene(scene);
        }

        private static void AttachAllInScene(Scene scene)
        {
            if (!scene.IsValid()) return;
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                AttachToHierarchy(roots[i]);
        }

        /// <summary>주어진 GameObject 와 그 자식 hierarchy 의 모든 Button 에 guard 부착 (멱등).
        /// 동적으로 spawn 한 prefab (popup, shop list item 등) 처리 후 호출.</summary>
        public static void AttachToHierarchy(GameObject root)
        {
            if (root == null) return;
            var buttons = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                var b = buttons[i];
                if (b == null) continue;
                if (b.GetComponent<UIButtonClickGuard>() == null)
                    b.gameObject.AddComponent<UIButtonClickGuard>();
            }
        }
    }
}
