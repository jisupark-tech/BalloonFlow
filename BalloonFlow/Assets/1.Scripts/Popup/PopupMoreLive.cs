using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 라이프 충전 팝업.
    /// PopupCommonFrame 사용 — Vertical(Green+Blue) 레이아웃.
    /// - 현재 라이프 표시 + 타이머
    /// - GreenBtn: 900 골드 차감 후 Life 풀 충전 (골드 부족 시 무동작)
    /// - BlueBtn: 광고 시청 보상 — Ad 미연동 상태이므로 fallback +1 Life 즉시 지급
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
                if (_frame.BtnVertGreen != null) _frame.BtnVertGreen.onClick.AddListener(OnCoinRefillClicked);
                if (_frame.BtnVertBlue != null) _frame.BtnVertBlue.onClick.AddListener(OnAdRewardClicked);
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.AddListener(() => CloseUI());
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_frame != null)
            {
                if (_frame.BtnVertGreen != null) _frame.BtnVertGreen.onClick.RemoveAllListeners();
                if (_frame.BtnVertBlue != null) _frame.BtnVertBlue.onClick.RemoveAllListeners();
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.RemoveAllListeners();
            }
        }

        public override void OpenUI()
        {
            if (_frame != null)
            {
                _frame.SetTitle("Need Lives!");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Vertical);
                // Vertical(Green+Blue) — Red 미사용, 빈 문자열 전달
                _frame.SetVertButtonTexts("Refill", string.Empty, "Free");
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
            string lifeStr = $"{current}";

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

            // 사양: 골드 부족 시 GreenBtn 무동작 — 사전 차단으로 명시적 보장
            if (!CurrencyManager.HasInstance || !CurrencyManager.Instance.HasEnoughCoins(900))
            {
                Debug.Log("[PopupMoreLive] 골드 부족 — GreenBtn 무동작");
                return;
            }

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

            // Ad 미연동 상태 — fallback 경로로 +1 Life 즉시 지급 (사양: 광고 시청 완료 후 Life 1개 충전)
            // AdManager 부재 또는 RewardedAd 미준비 시 즉시 보상
            if (!AdManager.HasInstance || !AdManager.Instance.IsRewardedAdReady())
            {
                Debug.LogWarning("[PopupMoreLive] Rewarded ad not ready — granting reward as fallback.");
                GrantAdReward();
                return;
            }

            // 광고 시청 → 보상 콜백에서 하트 +1. Lives 충전은 outgame이라 ad protection 우회.
            AdManager.Instance.ShowRewardedAd(GrantAdReward, ignoreAdProtection: true);
        }

        private void GrantAdReward()
        {
            if (!LifeManager.HasInstance) return;

            LifeManager.Instance.GrantAdRewardLife();
            RefreshDisplay();

            if (LifeManager.Instance.IsFullLives())
                CloseUI();

            Debug.Log("[PopupMoreLive] Ad reward — +1 life");
        }

        #endregion
    }
}
