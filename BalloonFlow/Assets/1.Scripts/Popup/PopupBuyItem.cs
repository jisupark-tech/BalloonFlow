using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 아이템 구매 확인 팝업.
    /// PopupCommonFrame: Horizontal (Green=Buy, Red=Cancel).
    /// 아이템 이미지, 수량, 가격(골드) ���시.
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

        private System.Action _onConfirm;
        private System.Action _onCancel;

        private void Start()
        {
            if (_frame != null)
            {
                if (_frame.BtnHorizGreen != null) _frame.BtnHorizGreen.onClick.AddListener(OnBuyClicked);
                if (_frame.BtnHorizRed != null) _frame.BtnHorizRed.onClick.AddListener(OnCancelClicked);
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.AddListener(OnCancelClicked);
            }
        }

        private void OnDestroy()
        {
            if (_frame != null)
            {
                if (_frame.BtnHorizGreen != null) _frame.BtnHorizGreen.onClick.RemoveAllListeners();
                if (_frame.BtnHorizRed != null) _frame.BtnHorizRed.onClick.RemoveAllListeners();
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.RemoveAllListeners();
            }
        }

        /// <summary>아이템 구매 팝업 표시.</summary>
        public void Show(string title, Sprite itemSprite, string amount, int goldCost,
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

            if (_imgItem != null && itemSprite != null) _imgItem.sprite = itemSprite;
            if (_txtItemAmount != null) _txtItemAmount.text = amount;
            if (_txtItemAmountOutline != null) _txtItemAmountOutline.text = amount;

            string costStr = goldCost.ToString("N0");
            if (_txtGold != null) _txtGold.text = costStr;
            if (_txtGoldOutline != null) _txtGoldOutline.text = costStr;

            OpenUI();
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
