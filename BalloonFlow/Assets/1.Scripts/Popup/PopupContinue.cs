using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// Continue popup — wires Continue/Decline buttons to ContinueHandler.
    /// Design ref: 아웃게임디렉션 §이어하기
    ///   1st continue: FREE → 2nd: 900 → 3rd: 1900 → 4th: 2900 (max)
    /// </summary>
    public class PopupContinue : UIBase
    {
        [Header("[버튼]")]
        [SerializeField] private Button _continueButton;
        [SerializeField] private Button _declineButton;

        [Header("[코스트 텍스트]")]
        [SerializeField] private Text _costText;

        public Button ContinueButton => _continueButton;
        public Button DeclineButton => _declineButton;

        /// <summary>
        /// Shows the continue popup with current cost info.
        /// </summary>
        public void Show()
        {
            UpdateCostDisplay();
            OpenUI();
        }

        /// <summary>
        /// Called when player presses the Continue button.
        /// </summary>
        public void OnContinueClicked()
        {
            if (!ContinueHandler.HasInstance) return;

            bool success = ContinueHandler.Instance.Continue();
            if (success)
            {
                Debug.Log("[PopupContinue] Continue accepted.");
                CloseUI();

                // Close the PopupManager's popup_continue entry too
                if (PopupManager.HasInstance)
                    PopupManager.Instance.ClosePopup("popup_continue");
            }
            else
            {
                Debug.LogWarning("[PopupContinue] Continue failed (not enough coins or max reached).");
                // If failed due to coins, could show gold shop — for now just close
                OnDeclineClicked();
            }
        }

        /// <summary>
        /// Called when player presses the Decline button.
        /// Triggers the actual level fail flow.
        /// </summary>
        public void OnDeclineClicked()
        {
            CloseUI();

            if (PopupManager.HasInstance)
                PopupManager.Instance.ClosePopup("popup_continue");

            // Now actually fail the level since player declined continue
            if (LevelManager.HasInstance)
            {
                LevelManager.Instance.FailLevel();
            }

            Debug.Log("[PopupContinue] Continue declined — triggering level fail.");
        }

        private void UpdateCostDisplay()
        {
            if (_costText == null || !ContinueHandler.HasInstance) return;

            int cost = ContinueHandler.Instance.GetContinueCost();
            if (cost <= 0)
            {
                _costText.text = "FREE";
            }
            else
            {
                _costText.text = $"{cost}";
            }
        }
    }
}
