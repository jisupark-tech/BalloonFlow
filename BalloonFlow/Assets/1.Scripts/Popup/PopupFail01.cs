using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 실패 시 첫 번째 팝업.
    /// 난이도별 프레임 변경 (Normal/Hard/SuperHard).
    /// ContinueButton: 골드 차감 + 색상 제거(Remove) + 게임 재개.
    /// DeclineButton: PopupContinue로 이동.
    /// Flow: PopupFail01 → PopupContinue → PopupFail02
    /// </summary>
    public class PopupFail01 : UIBase
    {
        // [Header("[난이도별 프레임]")]
        // [SerializeField] private Image _resultPanel;
        // [SerializeField] private Image _leftTopSidePanel;
        // [SerializeField] private Image _rightTopSidePanel;

        // [Header("[난이도 스프라이트 — ResultPanel]")]
        // [SerializeField] private Sprite _framePopupNormal;
        // [SerializeField] private Sprite _framePopupHard;
        // [SerializeField] private Sprite _framePopupSuperHard;

        // [Header("[난이도 스프라이트 — SidePanel]")]
        // [SerializeField] private Sprite _frameResultNormal;
        // [SerializeField] private Sprite _frameResultHard;
        // [SerializeField] private Sprite _frameResultSuperHard;

        [Header("[버튼]")]
        [SerializeField] private Button _continueButton;
        [SerializeField] private Button _declineButton;
        [SerializeField] private Button _goldPlusButton;

        [Header("[코스트 텍스트]")]
        [SerializeField] private TMP_Text _costText;

        private void Start()
        {
            if (_continueButton != null) _continueButton.onClick.AddListener(OnContinueClicked);
            if (_declineButton != null) _declineButton.onClick.AddListener(OnDeclineClicked);
            if (_goldPlusButton != null) _goldPlusButton.onClick.AddListener(OnGoldPlusClicked);
        }

        private void OnDestroy()
        {
            if (_continueButton != null) _continueButton.onClick.RemoveAllListeners();
            if (_declineButton != null) _declineButton.onClick.RemoveAllListeners();
            if (_goldPlusButton != null) _goldPlusButton.onClick.RemoveAllListeners();
        }

        public void Show(DifficultyPurpose difficulty)
        {
            //ApplyDifficultyFrames(difficulty);
            UpdateCostDisplay();
            OpenUI();
        }

        private void OnContinueClicked()
        {
            if (!ContinueHandler.HasInstance) return;

            int cost = ContinueHandler.Instance.GetContinueCost();

            // 골드 확인
            if (CurrencyManager.HasInstance && CurrencyManager.Instance.Coins < cost && cost > 0)
            {
                Debug.Log("[PopupFail01] 골드 부족");
                return;
            }

            // 이어하기 실행 (골드 차감 + 다트 제거 + 색상 제거)
            bool success = ContinueHandler.Instance.Continue();
            if (success)
            {
                if (PopupManager.HasInstance)
                    PopupManager.Instance.ClosePopup("popup_fail01");
                Debug.Log("[PopupFail01] Continue 성공 — 게임 재개");
            }
        }

        private void OnDeclineClicked()
        {
            // PopupManager에서 현재 팝업 닫기 → 다음 팝업 표시 가능
            if (PopupManager.HasInstance)
            {
                PopupManager.Instance.ClosePopup("popup_fail01");
                PopupManager.Instance.ShowPopup("popup_continue", 50);
            }
        }

        private void OnGoldPlusClicked()
        {
            if (PopupManager.HasInstance)
            {
                PopupManager.Instance.ShowPopup("popup_goldshop", 10);
            }
        }

        private void UpdateCostDisplay()
        {
            if (_costText == null || !ContinueHandler.HasInstance) return;
            int cost = ContinueHandler.Instance.GetContinueCost();
            _costText.text = cost <= 0 ? "FREE" : cost.ToString("N0");
        }

        // private void ApplyDifficultyFrames(DifficultyPurpose difficulty)
        // {
        //     Sprite popupFrame = _framePopupNormal;
        //     Sprite sideFrame = _frameResultNormal;

        //     switch (difficulty)
        //     {
        //         case DifficultyPurpose.Hard:
        //             popupFrame = _framePopupHard;
        //             sideFrame = _frameResultHard;
        //             break;
        //         case DifficultyPurpose.SuperHard:
        //             popupFrame = _framePopupSuperHard;
        //             sideFrame = _frameResultSuperHard;
        //             break;
        //     }

        //     if (_resultPanel != null && popupFrame != null) _resultPanel.sprite = popupFrame;
        //     if (_leftTopSidePanel != null && sideFrame != null) _leftTopSidePanel.sprite = sideFrame;
        //     if (_rightTopSidePanel != null && sideFrame != null) _rightTopSidePanel.sprite = sideFrame;
        // }
    }
}
