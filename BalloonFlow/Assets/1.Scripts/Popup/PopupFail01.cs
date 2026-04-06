using UnityEngine;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 실패 시 첫 번째 팝업.
    /// PopupCommonFrame: Horizontal 레이아웃 (Green=Continue, Red=Decline).
    /// ContinueButton: 골드 차감 + 이어하기.
    /// DeclineButton: PopupContinue로 이동.
    /// Flow: PopupFail01 → PopupContinue → PopupFail02
    /// </summary>
    public class PopupFail01 : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[코스트 텍스트]")]
        [SerializeField] private TMP_Text _costText;

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

        public void Show(DifficultyPurpose difficulty)
        {
            if (_frame != null)
            {
                _frame.ApplyDifficulty(difficulty);
                _frame.SetTitle("Continue?");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.SetHorizGreenText("Continue");
                _frame.SetHorizRedText("Give Up");
                _frame.ShowExitButton(true);
            }
            UpdateCostDisplay();
            OpenUI();
        }

        private void OnContinueClicked()
        {
            if (!ContinueHandler.HasInstance) return;

            int cost = ContinueHandler.Instance.GetContinueCost();
            if (CurrencyManager.HasInstance && CurrencyManager.Instance.Coins < cost && cost > 0)
            {
                Debug.Log("[PopupFail01] 골드 부족");
                return;
            }

            bool success = ContinueHandler.Instance.Continue();
            if (success)
            {
                if (PopupManager.HasInstance) PopupManager.Instance.ClosePopup("popup_fail01");
            }
        }

        private void OnDeclineClicked()
        {
            if (PopupManager.HasInstance)
            {
                PopupManager.Instance.ClosePopup("popup_fail01");
                PopupManager.Instance.ShowPopup("popup_continue", 50);
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
