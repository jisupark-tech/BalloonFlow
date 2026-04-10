using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 아이템 사용 확인 팝업.
    /// UseItem 프리팹에 부착. BottomExit 고정, 아이템별 이미지/설명 교체.
    /// </summary>
    public class PopupUseItem : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Item Display]")]
        [SerializeField] private Image _imgItem;
        [SerializeField] private TMP_Text _txtItemDescription;

        [Header("[Bottom Exit]")]
        [SerializeField] private Button _btnBottomExit;

        [Header("[Item Sprites — Inspector에서 할당]")]
        [SerializeField] private Sprite _sprHand;
        [SerializeField] private Sprite _sprShuffle;
        [SerializeField] private Sprite _sprZap;

        private System.Action _onConfirm;
        private System.Action _onCancel;

        protected override void Awake()
        {
            base.Awake();
            if (_frame != null)
            {
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.AddListener(OnConfirmClicked);
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.AddListener(OnCancelClicked);
            }
            if (_btnBottomExit != null) _btnBottomExit.onClick.AddListener(OnCancelClicked);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_frame != null)
            {
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.RemoveAllListeners();
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.RemoveAllListeners();
            }
            if (_btnBottomExit != null) _btnBottomExit.onClick.RemoveAllListeners();
        }

        /// <summary>아이템 사용 확인 팝업 표시.</summary>
        public void Show(string boosterType, string description,
                         System.Action onConfirm = null, System.Action onCancel = null)
        {
            _onConfirm = onConfirm;
            _onCancel = onCancel;

            if (_frame != null)
            {
                _frame.SetTitle("Use Item");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText("Use");
                _frame.ShowExitButton(true);
            }

            // 아이템별 이미지 교체
            if (_imgItem != null)
            {
                Sprite spr = GetBoosterSprite(boosterType);
                if (spr != null) _imgItem.sprite = spr;
            }

            if (_txtItemDescription != null)
                _txtItemDescription.text = description;

            OpenUI();
        }

        /// <summary>boosterType에 맞는 아이콘 스프라이트 반환.</summary>
        public Sprite GetBoosterSprite(string boosterType)
        {
            return boosterType switch
            {
                BoosterManager.SELECT_TOOL => _sprHand,
                BoosterManager.SHUFFLE     => _sprShuffle,
                BoosterManager.COLOR_REMOVE => _sprZap,
                _                          => null
            };
        }

        private void OnConfirmClicked()
        {
            _onConfirm?.Invoke();
            CloseUI();
        }

        private void OnCancelClicked()
        {
            _onCancel?.Invoke();
            CloseUI();
        }
    }
}
