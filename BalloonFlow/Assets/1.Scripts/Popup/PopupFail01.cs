using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    public class PopupFail01 : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Buttons — 직접 할당]")]
        [SerializeField] private Button _btnContinue;
        [SerializeField] private Button _btnDecline;
        [SerializeField] private Button _btnExit;

        [Header("[코스트 텍스트]")]
        [SerializeField] private TMP_Text _costText;

        [Header("[골드 표시]")]
        [SerializeField] private TMP_Text _txtGold;
        [SerializeField] private TMP_Text _txtGoldOutline;

        private Button ContinueBtn => _btnContinue != null ? _btnContinue : (_frame != null ? _frame.BtnHorizGreen : null);
        private Button DeclineBtn => _btnDecline != null ? _btnDecline : (_frame != null ? _frame.BtnHorizRed : null);
        private Button ExitBtn => _btnExit != null ? _btnExit : (_frame != null ? _frame.BtnExit : null);

        private void OnEnable()
        {
            UpdateCostDisplay();
            UpdateGoldDisplay();
        }

        protected override void Awake()
        {
            base.Awake();
            if (ContinueBtn != null) ContinueBtn.onClick.AddListener(OnContinueClicked);
            if (DeclineBtn != null) DeclineBtn.onClick.AddListener(OnDeclineClicked);
            if (ExitBtn != null) ExitBtn.onClick.AddListener(OnDeclineClicked);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (ContinueBtn != null) ContinueBtn.onClick.RemoveAllListeners();
            if (DeclineBtn != null) DeclineBtn.onClick.RemoveAllListeners();
            if (ExitBtn != null) ExitBtn.onClick.RemoveAllListeners();
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
            UpdateGoldDisplay();
            OpenUI();
        }

        private void UpdateGoldDisplay()
        {
            if (!CurrencyManager.HasInstance) return;
            string gold = CurrencyManager.Instance.Coins.ToString("N0");
            if (_txtGold != null) _txtGold.text = gold;
            if (_txtGoldOutline != null) _txtGoldOutline.text = gold;
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
