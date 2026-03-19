using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// Continue popup — 실패 흐름의 두 번째 단계.
    /// ContinueButton: 골드 차감 + 색상 제거 + 게임 재개.
    /// DeclineButton: PopupFail02로 이동.
    /// Flow: PopupFail01 → PopupContinue → PopupFail02
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

        private void Start()
        {
            if (_continueButton != null) _continueButton.onClick.AddListener(OnContinueClicked);
            if (_declineButton != null) _declineButton.onClick.AddListener(OnDeclineClicked);
        }

        private void OnDestroy()
        {
            if (_continueButton != null) _continueButton.onClick.RemoveAllListeners();
            if (_declineButton != null) _declineButton.onClick.RemoveAllListeners();
        }

        public void Show()
        {
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
                CloseUI();
                if (PopupManager.HasInstance)
                    PopupManager.Instance.ClosePopup("popup_continue");
                Debug.Log("[PopupContinue] Continue 성공 — 게임 재개");
            }
            else
            {
                OnDeclineClicked();
            }
        }

        public void OnDeclineClicked()
        {
            CloseUI();

            if (PopupManager.HasInstance)
                PopupManager.Instance.ClosePopup("popup_continue");

            // PopupFail02 표시 (최종 실패 팝업)
            if (PopupManager.HasInstance)
            {
                PopupManager.Instance.ShowPopup("popup_fail02", 50);
            }

            Debug.Log("[PopupContinue] Decline → PopupFail02 표시");
        }

        private void UpdateCostDisplay()
        {
            if (_costText == null || !ContinueHandler.HasInstance) return;
            int cost = ContinueHandler.Instance.GetContinueCost();
            _costText.text = cost <= 0 ? "FREE" : cost.ToString("N0");
        }
    }
}
