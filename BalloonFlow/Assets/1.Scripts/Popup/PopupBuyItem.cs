using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 아이템 구매/해금 확인 팝업.
    /// 구매 모드: Horizontal (Green=Buy, Red=Cancel) — TxtBtnBuyOutline.
    /// 해금 모드: Single 버튼 — TxtSingleOutline.
    /// </summary>
    public class PopupBuyItem : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Item Display]")]
        [SerializeField] private Image _imgItem;
        [SerializeField] private TMP_Text _txtItemAmount;
        [SerializeField] private TMP_Text _txtItemAmountOutline;
        [SerializeField] private TMP_Text _txtDescription;
        [SerializeField] private Image _imgInnerFrame;

        [Header("[Gold Display]")]
        [SerializeField] private TMP_Text _txtGold;
        [SerializeField] private TMP_Text _txtGoldOutline;
        [SerializeField] private Image _imgCoin;

        [Header("[Buy Outline — 구매 모드]")]
        [SerializeField] private GameObject _txtBtnBuyOutline;

        [Header("[Single Outline — 해금 모드]")]
        [SerializeField] private GameObject _txtSingleOutline;

        [Header("[Item Sprites — Inspector fallback. Awake 시 Addressable atlas 에서 override]")]
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
                if (_frame.BtnHorizGreen != null) _frame.BtnHorizGreen.onClick.AddListener(OnBuyClicked);
                if (_frame.BtnHorizRed != null) _frame.BtnHorizRed.onClick.AddListener(OnCancelClicked);
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.AddListener(OnBuyClicked);
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.AddListener(OnCancelClicked);
            }

            // 'iconSuffle' 은 atlas 측 의도된 typo. ResourceManager 에 atlas 사전 로드되어 있으면 sprite 교체.
            if (ResourceManager.HasInstance)
            {
                var rm = ResourceManager.Instance;
                _sprHand    = rm.UISpriteOr("iconHand",    _sprHand);
                _sprShuffle = rm.UISpriteOr("iconSuffle",  _sprShuffle);
                _sprZap     = rm.UISpriteOr("iconZap",     _sprZap);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_frame != null)
            {
                if (_frame.BtnHorizGreen != null) _frame.BtnHorizGreen.onClick.RemoveAllListeners();
                if (_frame.BtnHorizRed != null) _frame.BtnHorizRed.onClick.RemoveAllListeners();
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.RemoveAllListeners();
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.RemoveAllListeners();
            }
        }

        /// <summary>아이템 구매 팝업 표시 (Horizontal — Buy/Cancel).</summary>
        public void ShowBuy(string title, Sprite itemSprite, string amount, int goldCost,
                            System.Action onConfirm = null, System.Action onCancel = null)
        {
            _onConfirm = onConfirm;
            _onCancel = onCancel;

            if (_frame != null)
            {
                _frame.SetTitle(title);
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.SetHorizGreenText("Buy");
                _frame.SetHorizRedText("Cancel");
                _frame.ShowExitButton(true);
            }

            if (_txtBtnBuyOutline != null) _txtBtnBuyOutline.SetActive(true);
            if (_txtSingleOutline != null) _txtSingleOutline.SetActive(false);

            SetItemDisplay(itemSprite, amount, goldCost);
            OpenUI();
        }

        /// <summary>아이템 해금 팝업 표시 (Single 버튼).</summary>
        public void ShowUnlock(string title, Sprite itemSprite, int unlockLevel,
                               System.Action onConfirm = null, System.Action onCancel = null)
        {
            _onConfirm = onConfirm;
            _onCancel = onCancel;

            if (_frame != null)
            {
                _frame.SetTitle(title);
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText($"Unlock at Lv.{unlockLevel}");
                _frame.ShowExitButton(true);
            }

            if (_txtBtnBuyOutline != null) _txtBtnBuyOutline.SetActive(false);
            if (_txtSingleOutline != null) _txtSingleOutline.SetActive(true);

            if (_imgItem != null && itemSprite != null) _imgItem.sprite = itemSprite;
            OpenUI();
        }

        /// <summary>boosterType에 맞는 아이콘 스프라이트 반환.</summary>
        public Sprite GetBoosterSprite(string boosterType)
        {
            return boosterType switch
            {
                BoosterManager.SELECT_TOOL  => _sprHand,
                BoosterManager.SHUFFLE      => _sprShuffle,
                BoosterManager.COLOR_REMOVE => _sprZap,
                _                           => null
            };
        }

        /// <summary>기존 Show 호환 (구매 모드로 동작).</summary>
        public void Show(string title, Sprite itemSprite, string amount, int goldCost,
                         System.Action onConfirm = null, System.Action onCancel = null)
        {
            ShowBuy(title, itemSprite, amount, goldCost, onConfirm, onCancel);
        }

        private void SetItemDisplay(Sprite itemSprite, string amount, int goldCost)
        {
            if (_imgItem != null && itemSprite != null) _imgItem.sprite = itemSprite;
            if (_txtItemAmount != null) _txtItemAmount.text = amount;
            if (_txtItemAmountOutline != null) _txtItemAmountOutline.text = amount;

            string costStr = goldCost.ToString("N0");
            if (_txtGold != null) _txtGold.text = costStr;
            if (_txtGoldOutline != null) _txtGoldOutline.text = costStr;
        }

        private void OnBuyClicked()
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
