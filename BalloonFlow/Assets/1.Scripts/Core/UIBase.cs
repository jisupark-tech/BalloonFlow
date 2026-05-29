using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// 모든 UI 패널의 베이스 클래스.
    /// OpenUI/CloseUI/ShowUI/HideUI 라이프사이클 제공.
    /// CanvasGroup이 있으면 alpha/interactable/blocksRaycasts 자동 제어.
    /// DOTween 애니메이션 연출 옵션 내장 (PopupAni에서 이식).
    /// </summary>
    public class UIBase : MonoBehaviour
    {
        protected CanvasGroup _canvasGroup;

        [Header("[애니메이션 연출]")]
        [SerializeField] private bool _useAnimation = false;
        [SerializeField] private AnimationType _animationType = AnimationType.ScalePopup;
        [SerializeField] private float _animDuration = 0.35f;
        [SerializeField] private Ease _animEase = Ease.OutBack;

        [Header("[딤 (어두운 배경) — 선택]")]
        [SerializeField] private CanvasGroup _dim;

        [Header("[팝업 윈도우 — Scale 애니 대상]")]
        [SerializeField] private RectTransform _popupWindow;

        private Sequence _currentSequence;

        // [2026-05-13] 모든 ingame 팝업 OpenUI/CloseUI 시 UIHud 의 HUD_Top + BottomPanel 연출을 자동 트리거.
        // UIHud 본인은 popup 이 아니므로 override 로 false. 특별한 비-팝업 UIBase 가 있다면 false 로 끄면 됨.
        protected virtual bool TriggersHudPopupAnimation => true;

        // [2026-05-22] LoadUI 사전 로드(OpenUI→CloseUI 즉시 닫기) 패턴에서 HUD popup 연출 트리거 우회용 1회성 flag.
        // 한 번 소비되면 자동 reset 되어 다음 OpenUI/CloseUI 는 일반 동작.
        private bool _suppressNextHudNotify;
        public void SuppressNextHudNotify() => _suppressNextHudNotify = true;
        private bool ConsumeSuppressHudNotify()
        {
            bool s = _suppressNextHudNotify;
            _suppressNextHudNotify = false;
            return s;
        }

        /// <summary>SuppressNextHudNotify + CloseUI 의 syntactic shortcut — 사전 로드 패턴 전용.</summary>
        public void CloseUISilent()
        {
            SuppressNextHudNotify();
            CloseUI();
        }

        public enum AnimationType
        {
            None,
            ScalePopup,     // 스케일 0→1 (OutBack)
            FadeIn,         // 알파 0→1
            SlideFromBottom,// 아래에서 올라옴
            SlideFromTop,   // 위에서 내려옴
        }

        /// <summary>안드로이드 백버튼 처리 결과 (BackButtonRouter 라우팅용).</summary>
        public enum BackResult
        {
            NotHandled, // 이 UI 가 백버튼을 처리하지 않음 → 라우터가 다음(씬) 단계로 위임
            Handled,    // 처리됨 (닫힘 등) → 입력 소비
            Blocked,    // 의도적 차단 (결제/광고/학습 의사결정 보호) → 입력 소비, 동작 없음
        }

        /// <summary>
        /// 백버튼 라우팅에서 "팝업"으로 취급될지. 팝업이면 씬 컨트롤러보다 우선해서 백버튼을 받는다.
        /// 씬 UI(UIHud/UILobby/UIShop/UISetting/UITitle)는 override 로 false → 팝업 없을 때만 씬 단위 백버튼 처리.
        /// </summary>
        public virtual bool ConsumesBackButton => true;

        /// <summary>
        /// 백버튼이 이 팝업에 도달했을 때의 동작 (UX플로우 §5-3-0 매트릭스).
        /// 기본: [X] 버튼과 동일 동작 (PopupCommonFrame.BtnExit 있으면 그 onClick, 없으면 CloseUI).
        /// 차단이 필요한 팝업(이어하기·클리어·튜토리얼)은 override 로 <see cref="BackResult.Blocked"/> 반환.
        /// </summary>
        public virtual BackResult OnBackPressed()
        {
            var frame = GetComponentInChildren<PopupCommonFrame>(true);
            if (frame != null && frame.BtnExit != null && frame.BtnExit.gameObject.activeInHierarchy
                && frame.BtnExit.interactable)
            {
                frame.BtnExit.onClick.Invoke();
                return BackResult.Handled;
            }
            CloseUI();
            return BackResult.Handled;
        }

        protected virtual void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            // [2026-05-13] popup/panel 하위 Button 더블 클릭 방어 자동 부착 (멱등) — 동적 spawn popup 도 처리.
            UIButtonClickGuard.AttachToHierarchy(gameObject);
        }

        /// <summary>초기화. 필요한 데이터 전달 시 사용.</summary>
        public virtual void Init(object[] _data) { }

        /// <summary>UI 열기 (활성화 + CanvasGroup ON + 애니메이션). 이미 열려있으면 중복 실행 안 함.</summary>
        public virtual void OpenUI()
        {
            // 이미 활성 + 보이는 상태면 중복 실행 방지
            if (gameObject.activeSelf && _canvasGroup != null && _canvasGroup.alpha > 0.99f && _canvasGroup.interactable)
                return;

            gameObject.SetActive(true);

            if (_useAnimation && _animationType != AnimationType.None)
            {
                PlayOpenAnimation();
            }
            else
            {
                if (_canvasGroup != null)
                {
                    _canvasGroup.alpha = 1f;
                    _canvasGroup.interactable = true;
                    _canvasGroup.blocksRaycasts = true;
                }
            }

            // PopupCommonFrame 등장 연출 — OnEnable 만으로 안 트리거되는 prefab 도 있어 명시 호출 (idempotent).
            var popFrame = GetComponentInChildren<PopupCommonFrame>(true);
            if (popFrame != null) popFrame.PlayPopAnimation();

            // [2026-05-13] HUD popup-open 연출 중앙화 — Settings/Quit/GoldShop/UseItem/Result 등 모든 인게임 popup
            // 이 열리면 UIHud 가 HUD_Top + BottomPanel slide tween 을 자동 실행. UIHud 자신은 override 로 self-skip.
            // [2026-05-22] 사전 로드(LoadUI) 경로는 SuppressNextHudNotify 로 우회 — popup 즉시 close 가 HUD tween 발사 안 함.
            bool suppress = ConsumeSuppressHudNotify();
            if (TriggersHudPopupAnimation && !suppress)
            {
                var hud = UnityEngine.Object.FindAnyObjectByType<UIHud>();
                if (hud != null && hud != this) hud.NotifyPopupOpened();
            }
        }

        /// <summary>
        /// UI 닫기. CanvasGroup 끄고 GameObject 도 SetActive(false). 비활성 상태에서 코루틴/Update/파티클 정지로 부하 절감.
        /// 재오픈은 OpenUI() 가 SetActive(true) → OnEnable → 애니메이션 수동 재생.
        /// </summary>
        public virtual void CloseUI()
        {
            // [2026-05-13] 활성 상태에서만 HUD popup-close 트리거 — 중복 close 호출이 _popupOpenCount 를 음수로 내려가게 막음.
            // [2026-05-22] 사전 로드 경로는 CloseUISilent / SuppressNextHudNotify 로 우회.
            bool suppress = ConsumeSuppressHudNotify();
            if (TriggersHudPopupAnimation && isActiveAndEnabled && !suppress)
            {
                var hud = UnityEngine.Object.FindAnyObjectByType<UIHud>();
                if (hud != null && hud != this) hud.NotifyPopupClosed();
            }

            KillAnimation();

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }

            gameObject.SetActive(false);
        }

        /// <summary>UI 보이기 (OpenUI와 동일)</summary>
        public virtual void ShowUI() { OpenUI(); }

        /// <summary>UI 숨기기 (CloseUI와 동일)</summary>
        public virtual void HideUI() { CloseUI(); }

        #region Animation

        /// <summary>열기 애니메이션 재생. _useAnimation이 true일 때 OpenUI에서 자동 호출.</summary>
        protected void PlayOpenAnimation()
        {
            KillAnimation();

            // 초기 상태
            if (_canvasGroup != null)
            {
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = true;
            }

            _currentSequence = DOTween.Sequence();

            // 딤 페이드
            if (_dim != null)
            {
                _dim.alpha = 0f;
                _currentSequence.Append(_dim.DOFade(1f, _animDuration * 0.6f));
            }

            // 애니메이션 타입별 연출
            switch (_animationType)
            {
                case AnimationType.ScalePopup:
                    if (_popupWindow != null)
                    {
                        _popupWindow.localScale = Vector3.zero;
                        _currentSequence.Join(
                            _popupWindow.DOScale(1f, _animDuration).SetEase(_animEase));
                    }
                    if (_canvasGroup != null)
                    {
                        _canvasGroup.alpha = 1f;
                    }
                    break;

                case AnimationType.FadeIn:
                    if (_canvasGroup != null)
                    {
                        _canvasGroup.alpha = 0f;
                        _currentSequence.Join(
                            _canvasGroup.DOFade(1f, _animDuration).SetEase(Ease.OutQuad));
                    }
                    break;

                case AnimationType.SlideFromBottom:
                    if (_popupWindow != null)
                    {
                        Vector2 startPos = _popupWindow.anchoredPosition;
                        _popupWindow.anchoredPosition = new Vector2(startPos.x, -Screen.height);
                        _currentSequence.Join(
                            _popupWindow.DOAnchorPosY(startPos.y, _animDuration).SetEase(Ease.OutQuad));
                    }
                    if (_canvasGroup != null) _canvasGroup.alpha = 1f;
                    break;

                case AnimationType.SlideFromTop:
                    if (_popupWindow != null)
                    {
                        Vector2 startPos = _popupWindow.anchoredPosition;
                        _popupWindow.anchoredPosition = new Vector2(startPos.x, Screen.height);
                        _currentSequence.Join(
                            _popupWindow.DOAnchorPosY(startPos.y, _animDuration).SetEase(Ease.OutQuad));
                    }
                    if (_canvasGroup != null) _canvasGroup.alpha = 1f;
                    break;
            }

            _currentSequence.SetUpdate(true); // timeScale=0에서도 동작
            _currentSequence.OnComplete(() =>
            {
                if (_canvasGroup != null)
                {
                    _canvasGroup.alpha = 1f;
                    _canvasGroup.interactable = true;
                    _canvasGroup.blocksRaycasts = true;
                }
            });
        }

        private void KillAnimation()
        {
            if (_currentSequence != null && _currentSequence.IsActive())
            {
                _currentSequence.Kill();
                _currentSequence = null;
            }
        }

        protected RectTransform GetBaseDimRectTransform()
        {
            return _dim != null ? _dim.transform as RectTransform : null;
        }

        protected bool TryApplyBaseDimMaterial(Material dimMaterial)
        {
            if (_dim == null || dimMaterial == null) return false;

            var graphics = _dim.GetComponentsInChildren<Graphic>(true);
            if (graphics == null || graphics.Length == 0) return false;

            for (int i = 0; i < graphics.Length; i++)
            {
                graphics[i].material = dimMaterial;
                graphics[i].color = Color.white;
            }

            return true;
        }

        protected virtual void OnDestroy()
        {
            KillAnimation();
        }

        #endregion
    }
}
