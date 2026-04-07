using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 라이프 충전 팝업.
    /// PopupCommonFrame 사용.
    /// - 현재 라이프 표시 + 타이머
    /// - 코인으로 충전 (Green 버튼)
    /// - 광고로 무료 충전 (Red/Blue 버튼)
    /// - 닫기 (Exit)
    /// </summary>
    public class PopupMoreLive : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Life Display]")]
        [SerializeField] private TMP_Text _txtLife;
        [SerializeField] private TMP_Text _txtLifeOutline;
        [SerializeField] private Image _imgLife;

        [Header("[Timer — 다음 하트까지]")]
        [SerializeField] private TMP_Text _txtTimer;
        [SerializeField] private Image _imgClock;
        [SerializeField] private Image _imgClockHand;

        [Header("[Description]")]
        [SerializeField] private TMP_Text _txtDescription;

        [Header("[Coin Refill]")]
        [SerializeField] private TMP_Text _txtGold;
        [SerializeField] private TMP_Text _txtGoldOutline;
        [SerializeField] private Image _imgCoin;

        [Header("[Ad Reward]")]
        [SerializeField] private TMP_Text _txtFree;
        [SerializeField] private TMP_Text _txtFreeOutline;
        [SerializeField] private Image _imgAd;

        [Header("[Inner Frame]")]
        [SerializeField] private Image _imgInnerFrame;

        protected override void Awake()
        {
            base.Awake();
            // 버튼 연결은 Awake에서 (CloseUI 후에도 listener 유지)
            if (_frame != null)
            {
                if (_frame.BtnHorizGreen != null) _frame.BtnHorizGreen.onClick.AddListener(OnCoinRefillClicked);
                if (_frame.BtnHorizRed != null) _frame.BtnHorizRed.onClick.AddListener(OnAdRewardClicked);
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.AddListener(() => CloseUI());
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_frame != null)
            {
                if (_frame.BtnHorizGreen != null) _frame.BtnHorizGreen.onClick.RemoveAllListeners();
                if (_frame.BtnHorizRed != null) _frame.BtnHorizRed.onClick.RemoveAllListeners();
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.RemoveAllListeners();
            }
        }

        public override void OpenUI()
        {
            if (_frame != null)
            {
                _frame.SetTitle("Need Lives!");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.SetHorizGreenText("Refill");
                _frame.SetHorizRedText("Free");
                _frame.ShowExitButton(true);
            }

            RefreshDisplay();
            base.OpenUI();
        }

        private void Update()
        {
            if (!gameObject.activeSelf) return;
            UpdateTimer();
        }

        #region Display

        private void RefreshDisplay()
        {
            if (!LifeManager.HasInstance) return;

            int current = LifeManager.Instance.CurrentLives;
            int max = LifeManager.Instance.MaxLives;
            string lifeStr = $"{current}/{max}";

            if (_txtLife != null) _txtLife.text = lifeStr;
            if (_txtLifeOutline != null) _txtLifeOutline.text = lifeStr;

            // Coin cost
            int cost = 900; // LifeManager.COIN_REFILL_COST
            string costStr = cost.ToString("N0");
            if (_txtGold != null) _txtGold.text = costStr;
            if (_txtGoldOutline != null) _txtGoldOutline.text = costStr;

            // Ad text
            if (_txtFree != null) _txtFree.text = "FREE";
            if (_txtFreeOutline != null) _txtFreeOutline.text = "FREE";

            UpdateTimer();
        }

        private void UpdateTimer()
        {
            if (!LifeManager.HasInstance) return;

            if (LifeManager.Instance.IsInfiniteHeartsActive)
            {
                if (_txtTimer != null) _txtTimer.text = "UNLIMITED";
                return;
            }

            if (LifeManager.Instance.IsFullLives())
            {
                if (_txtTimer != null) _txtTimer.text = "FULL";
                return;
            }

            var remaining = LifeManager.Instance.GetTimeToNextLife();
            if (_txtTimer != null)
                _txtTimer.text = $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        }

        #endregion

        #region Button Handlers

        private void OnCoinRefillClicked()
        {
            if (!LifeManager.HasInstance) return;

            bool success = LifeManager.Instance.PurchaseRefillWithCoins();
            if (success)
            {
                RefreshDisplay();
                CloseUI();
            }
            else
            {
                Debug.Log("[PopupMoreLive] 골드 부족");
            }
        }

        private void OnAdRewardClicked()
        {
            if (!LifeManager.HasInstance) return;

            // TODO: 광고 시청 후 콜백에서 호출
            LifeManager.Instance.GrantAdRewardLife();
            RefreshDisplay();

            if (LifeManager.Instance.IsFullLives())
                CloseUI();

            Debug.Log("[PopupMoreLive] Ad reward — +1 life");
        }

        #endregion
    }
}
