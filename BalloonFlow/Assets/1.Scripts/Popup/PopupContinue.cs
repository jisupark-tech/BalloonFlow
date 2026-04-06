using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// Continue popup — 실패 흐름의 두 번째 단계.
    /// PopupCommonFrame: Horizontal 레이아웃 (Green=Continue, Red=Give Up).
    /// Flow: PopupFail01 → PopupContinue → PopupFail02
    /// </summary>
    public class PopupContinue : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[코스트 텍스트]")]
        [SerializeField] private Text _costText;

        public Button ContinueButton => _frame != null ? _frame.BtnHorizGreen : null;
        public Button DeclineButton => _frame != null ? _frame.BtnHorizRed : null;

        private void Start()
        {
            if (_frame != null)
            {
                if (_frame.BtnHorizGreen != null) _frame.BtnHorizGreen.onClick.AddListener(OnContinueClicked);
                if (_frame.BtnHorizRed != null) _frame.BtnHorizRed.onClick.AddListener(OnDeclineClicked);
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.AddListener(OnDeclineClicked);
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

        public void Show()
        {
            if (_frame != null)
            {
                _frame.SetTitle("Continue?");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.SetHorizGreenText("Continue");
                _frame.SetHorizRedText("Give Up");
                _frame.ShowExitButton(true);
            }
            UpdateCostDisplay();
            OpenUI();
        }

        public void OnContinueClicked()
        {
            if (!ContinueHandler.HasInstance) return;

            int cost = ContinueHandler.Instance.GetContinueCost();
            if (CurrencyManager.HasInstance && CurrencyManager.Instance.Coins < cost && cost > 0)
            {
                Debug.Log("[PopupContinue] 골드 부족");
                return;
            }

            bool success = ContinueHandler.Instance.Continue();
            if (success)
            {
                if (PopupManager.HasInstance) PopupManager.Instance.ClosePopup("popup_continue");
            }
            else
            {
                OnDeclineClicked();
            }
        }

        public void OnDeclineClicked()
        {
            if (PopupManager.HasInstance)
            {
                PopupManager.Instance.ClosePopup("popup_continue");
                PopupManager.Instance.ShowPopup("popup_fail02", 50);
            }
        }

        private void UpdateCostDisplay()
        {
            if (_costText == null || !ContinueHandler.HasInstance) return;
            int cost = ContinueHandler.Instance.GetContinueCost();
            _costText.text = cost <= 0 ? "FREE" : cost.ToString("N0");
        }
    }
}
