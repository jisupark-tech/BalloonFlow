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
    /// TopBar 코인 표시는 AnimatedCoinLabel 가 TopBarArea/GoldPanel/TxtGold 에 자동 부착되어 처리.
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

            EnsureTopBarBinding();
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

        private void EnsureTopBarBinding()
        {
            Transform topBar = FindChildRecursive(transform, "TopBarArea");
            Transform gold = topBar != null ? FindChildRecursive(topBar, "GoldPanel") : null;
            if (gold != null) GoldPanelFxFireUtil.DisableUnderGoldPanel(gold);
            Transform txt = gold != null ? FindChildRecursive(gold, "TxtGold") : null;
            if (txt != null && txt.GetComponent<AnimatedCoinLabel>() == null)
                txt.gameObject.AddComponent<AnimatedCoinLabel>();
        }

        private void OnEnable()
        {
            GoldPanelFxFireUtil.DisableUnderTopBarRoot(transform);
        }

        private static Transform FindChildRecursive(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName) return child;
                Transform deep = FindChildRecursive(child, childName);
                if (deep != null) return deep;
            }
            return null;
        }

        public override void OpenUI()
        {
            if (_frame != null)
            {
                _frame.SetTitle("More Lives");
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

            // Coin cost — popup 내부 cost 라벨 전용 (TopBar 잔액과 분리)
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

            Sprite icon = ResolveLifeIcon();
            CloseUI();

            UILobby lobby = FindUILobby();
            if (lobby == null || icon == null)
            {
                LifeManager.Instance.GrantAdRewardLife();
                Debug.Log("[PopupMoreLive] Ad reward — +1 life (FXItem fallback)");
                return;
            }

            Vector2 from = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 to = lobby.GetLifePanelScreenPos();

            ItemFlyEffect.Play(icon, from, to, 1,
                onEachLand: () => lobby.PulseLifePanel(),
                onAllComplete: () =>
                {
                    if (LifeManager.HasInstance)
                        LifeManager.Instance.GrantAdRewardLife();
                    Debug.Log("[PopupMoreLive] Ad reward — FXItem landed, +1 life");
                });
        }

        private Sprite ResolveLifeIcon()
        {
            Sprite icon = _imgLife != null ? _imgLife.sprite : null;
            if (ResourceManager.HasInstance)
            {
                icon = ResourceManager.Instance.UISpriteOr(Const.SPR_ICONLIFE, icon);
                icon = ResourceManager.Instance.UISpriteOr(Const.SPR_ICONHEARINFINITE, icon);
            }
            return icon;
        }

        private static UILobby FindUILobby()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<UILobby>(FindObjectsInactive.Exclude);
#else
            return Object.FindObjectOfType<UILobby>();
#endif
        }

        #endregion
    }
}
