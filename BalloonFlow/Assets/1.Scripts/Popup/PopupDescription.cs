using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 설명/정보 팝업. 범용 텍스트 표시용.
    /// PopupCommonFrame 사용. Single 버튼 (확인).
    /// </summary>
    public class PopupDescription : UIBase
    {
        private const string GOLD_ROTATE_NAME = "GoldRotate";

        public static bool IsShowing { get; private set; }

        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Content]")]
        [SerializeField] private TMP_Text _txtDescription;
        [SerializeField] private Image _imgInnerFrame;

        private System.Action _onConfirm;
        private bool _exitClosesOnly;

        protected override void Awake()
        {
            base.Awake();
            if (_frame != null)
            {
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.AddListener(OnConfirm);
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.AddListener(OnExitClicked);
            }
        }

        private void OnEnable() => IsShowing = true;
        private void OnDisable() => IsShowing = false;

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_frame != null)
            {
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.RemoveAllListeners();
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.RemoveAllListeners();
            }
            IsShowing = false;
        }

        /// <summary>타이틀 + 설명 텍스트 설정 후 열기.</summary>
        public void Show(string title, string description)
        {
            Show(title, description, LocalizationService.Get("ui.common.ok"), null);
        }

        /// <summary>타이틀 + 설명 + 버튼 텍스트 + 콜백.</summary>
        public void Show(string title, string description, string buttonText,
                         System.Action onConfirm = null)
        {
            Show(title, description, buttonText, onConfirm, false);
        }

        /// <summary>타이틀 + 설명 + 버튼 텍스트 + 콜백 + X버튼 동작 분리.</summary>
        public void Show(string title, string description, string buttonText,
                         System.Action onConfirm, bool exitClosesOnly, bool clearOverlayCoins = false)
        {
            _onConfirm = onConfirm;
            _exitClosesOnly = exitClosesOnly;

            if (clearOverlayCoins)
            {
                // ROLLBACK_POPUP_DESCRIPTION_CLEAR_OVERLAY_COINS_20260624:
                // Lobby Quit Game uses PopupDescription, not PopupQuit. FXGold lives on EffectCanvas above popup canvas,
                // so clear active coin visuals before opening this popup to prevent oversized idle coins showing over it.
                // Rollback: remove this branch and the clearOverlayCoins argument from BackButtonRouter.ShowQuitGame().
                CoinFlyEffect.ClearActiveCoinsForPopup();
                GoldPanelFxFireUtil.DisableUnderTopBarRoot(transform);
                DisableNamedObjectRecursive(transform, GOLD_ROTATE_NAME);
            }

            if (_frame != null)
            {
                _frame.SetTitle(title);
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText(buttonText);
                if (clearOverlayCoins)
                {
                    // ROLLBACK_QUITGAME_BUTTON_RED_OUTLINE_20260624:
                    // BackButtonRouter's Quit Game popup uses PopupDescription, so apply the red
                    // button outline here instead of relying on PopupQuit-specific styling.
                    Material redOutline = Resources.Load<Material>(Const.FONT_MAT_POPPINS_BOLD_RED_OUTLINE);
                    _frame.OverrideSingleButtonOutlineMaterial(redOutline, UIOutlineStyle.ForShopBundle(true));
                }
                else
                {
                    // ROLLBACK_ALLCLEAR_GREEN_BUTTON_20260715: 비-Quit(all-clear/아이템설명 등)은 프리팹 기본(초록)으로 복원.
                    //   풀링 재사용으로 이전 Quit 의 red 아웃라인이 남던 문제 해소.
                    _frame.ResetSingleButtonOutline();
                }
                _frame.ShowExitButton(true);
            }

            if (_txtDescription != null) _txtDescription.text = description;

            IsShowing = true;
            OpenUI();
        }

        private void OnConfirm()
        {
            IsShowing = false;
            _onConfirm?.Invoke();
            CloseUI();
        }

        // X 버튼 = 취소(콜백 미발화), Single 버튼 = 확정
        private void OnExitClicked()
        {
            if (_exitClosesOnly)
            {
                IsShowing = false;
                CloseUI();
                return;
            }
            OnConfirm();
        }

        private static void DisableNamedObjectRecursive(Transform root, string objectName)
        {
            if (root == null || string.IsNullOrEmpty(objectName)) return;

            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null || t.name != objectName) continue;

                var animators = t.GetComponentsInChildren<Animator>(true);
                for (int j = 0; j < animators.Length; j++)
                    if (animators[j] != null) animators[j].enabled = false;

                t.gameObject.SetActive(false);
            }
        }
    }
}
