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

        // ROLLBACK_MORELIVE_CLOSE_ON_REFILL_COMPLETE_20260619: 팝업이 열린 동안 '비풀(<max)' 을 한 번이라도 관측했는지.
        //   true 인 상태에서 풀(5/5)로 전환되면 = 충전 완료 → 00:00/full 표시 대신 팝업을 닫는다.
        private bool _sawNonFullWhileOpen;
        [SerializeField] private Image _imgClock;
        [SerializeField] private Image _imgClockHand;
        private Transform _topBarGoldPanel;
        private const int OVERLAY_SORT_ORDER = 260;
        private Canvas _overrideCanvas;

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
            EnsureOverlaySorting();
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
            _topBarGoldPanel = gold;
            if (gold != null) GoldPanelFxFireUtil.DisableUnderGoldPanel(gold);

            // ROLLBACK_MORELIVE_HIDE_LEFT_COIN_BAR_20260622:
            // More Lives should not show the left/top balance coin bar. Keep the refill cost
            // label inside the popup buttons, but hide TopBarArea/GoldPanel.
            SetTopBarGoldPanelVisible(false);
        }

        private void OnEnable()
        {
            EnsureOverlaySorting();
            GoldPanelFxFireUtil.DisableUnderTopBarRoot(transform);
            SetTopBarGoldPanelVisible(false);
        }

        private void EnsureOverlaySorting()
        {
            // ROLLBACK_MORELIVE_MODAL_SORTING_20260622:
            // PopupFail02 keeps reward child canvases at 244~248 while it remains behind
            // More Lives. Render More Lives above those children without changing retry flow.
            Canvas parentCanvas = transform.parent != null ? transform.parent.GetComponentInParent<Canvas>() : null;
            if (_overrideCanvas == null)
                _overrideCanvas = GetComponent<Canvas>();
            if (_overrideCanvas == null)
                _overrideCanvas = gameObject.AddComponent<Canvas>();

            _overrideCanvas.overrideSorting = true;
            if (parentCanvas != null)
                _overrideCanvas.sortingLayerName = parentCanvas.sortingLayerName;
            _overrideCanvas.sortingOrder = OVERLAY_SORT_ORDER;

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();
        }

        private void SetTopBarGoldPanelVisible(bool visible)
        {
            if (_topBarGoldPanel == null)
            {
                Transform topBar = FindChildRecursive(transform, "TopBarArea");
                _topBarGoldPanel = topBar != null ? FindChildRecursive(topBar, "GoldPanel") : null;
            }

            if (_topBarGoldPanel != null && _topBarGoldPanel.gameObject.activeSelf != visible)
                _topBarGoldPanel.gameObject.SetActive(visible);
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
            EnsureOverlaySorting();
            if (_frame != null)
            {
                // ROLLBACK_MORELIVE_NORMAL_FRAME_20260626:
                // More Lives is a lobby/life popup, so it must not inherit the last in-game
                // Hard/SuperHard level from PopupCommonFrame.ResolveActiveDifficulty().
                _frame.ApplyDifficulty(DifficultyPurpose.Normal);
                _frame.SetTitle("More Lives");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Vertical);
                // Vertical(Green+Blue) — Red 미사용, 빈 문자열 전달
                _frame.SetVertButtonTexts("Refill", string.Empty, "Free");
                _frame.ShowExitButton(true);
            }

            _sawNonFullWhileOpen = false; // ROLLBACK_MORELIVE_CLOSE_ON_REFILL_COMPLETE_20260619: 오픈마다 전환 감지 리셋
            SetTopBarGoldPanelVisible(false);
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
            if (!LifeManager.HasInstance)
            {
                // [2026-06-11] 가드로 스킵되면 프리팹 저작 placeholder("{n}")가 그대로 노출 — 빈 값으로 클리어.
                if (_txtLife != null) _txtLife.text = string.Empty;
                if (_txtLifeOutline != null) _txtLifeOutline.text = string.Empty;
                return;
            }

            int current = LifeManager.Instance.CurrentLives;
            string lifeStr = $"{current}";

            if (_txtLife != null) _txtLife.text = lifeStr;
            if (_txtLifeOutline != null) _txtLifeOutline.text = lifeStr;

            // Coin cost — popup 내부 cost 라벨 전용 (TopBar 잔액과 분리)
            int cost = LifeManager.COIN_REFILL_COST;
            string costStr = cost.ToString("N0");
            if (_txtGold != null) _txtGold.text = costStr;
            if (_txtGoldOutline != null) _txtGoldOutline.text = costStr;

            // Ad text
            if (_txtFree != null) _txtFree.text = LocalizationService.Get("ui.morelive.free");
            if (_txtFreeOutline != null) _txtFreeOutline.text = LocalizationService.Get("ui.morelive.free");

            UpdateTimer();
        }

        private void UpdateTimer()
        {
            if (!LifeManager.HasInstance) return;

            if (LifeManager.Instance.IsInfiniteHeartsActive)
            {
                if (_txtTimer != null) _txtTimer.text = LocalizationService.Get("ui.morelive.unlimited");
                return;
            }

            if (LifeManager.Instance.IsFullLives())
            {
                // ROLLBACK_MORELIVE_CLOSE_ON_REFILL_COMPLETE_20260619: 팝업이 열린 동안 비풀→풀(=4→5 충전 완료)로
                //   전환되면 00:00/full 을 띄우지 말고 팝업을 닫는다. (풀 상태로 처음 열린 경우는 full 텍스트 유지.)
                if (_sawNonFullWhileOpen)
                {
                    CloseUI();
                    return;
                }
                if (_txtTimer != null) _txtTimer.text = LocalizationService.Get("ui.morelive.full");
                return;
            }

            _sawNonFullWhileOpen = true; // 비풀 상태 관측 — 이후 풀 전환 = 충전 완료
            var remaining = LifeManager.Instance.GetTimeToNextLife();

            // ROLLBACK_MORELIVE_CLOSE_ON_REFILL_COMPLETE_20260619: 마지막 하트(maxLives-1 → max)가 곧 차서
            //   잔여 < 1초(=다음 프레임에 "00:00" 으로 표시될 구간)면 00:00 을 띄우지 말고 팝업을 닫는다.
            //   (3→4 같은 중간 하트 충전은 CurrentLives < max-1 이라 해당 안 됨 → 카운트다운 유지.)
            if (LifeManager.Instance.CurrentLives >= LifeManager.Instance.MaxLives - 1
                && remaining.TotalSeconds < 1.0)
            {
                CloseUI();
                return;
            }

            if (_txtTimer != null)
                _txtTimer.text = $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        }

        #endregion

        #region Button Handlers

        private void OnCoinRefillClicked()
        {
            if (!LifeManager.HasInstance) return;

            if (CurrencyManager.HasInstance) CurrencyManager.Instance.PublishCoinSync();

            // 사양: 골드 부족 시 GreenBtn 무동작 — 사전 차단으로 명시적 보장
            int cost = LifeManager.COIN_REFILL_COST;
            int coins = CurrencyManager.HasInstance ? CurrencyManager.Instance.Coins : -1;
            if (!CurrencyManager.HasInstance || !CurrencyManager.Instance.HasEnoughCoins(cost))
            {
                Debug.LogWarning($"[PopupMoreLive] Coin refill blocked by coins. have={coins}, need={cost}");
                // ROLLBACK_NOCOIN_POPUP_TO_GOLDSHOP_20260622:
                // Not enough coins must open the shop instead of PopupError("Not enough coins").
                OpenGoldShopForInsufficientCoins();
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
                Debug.LogWarning($"[PopupMoreLive] Coin refill failed after precheck. have={(CurrencyManager.HasInstance ? CurrencyManager.Instance.Coins : -1)}, need={cost}");
                bool stillNotEnough = !CurrencyManager.HasInstance || !CurrencyManager.Instance.HasEnoughCoins(cost);
                if (stillNotEnough)
                {
                    OpenGoldShopForInsufficientCoins();
                }
                else if (UIManager.HasInstance)
                {
                    var err = UIManager.Instance.OpenUI<PopupError>("Popup/PopupError");
                    if (err != null) err.Show("Purchase Failed", "Purchase could not be completed. Please try again.");
                }
                Debug.Log("[PopupMoreLive] 골드 부족");
            }
        }

        private void OpenGoldShopForInsufficientCoins()
        {
            CloseUI();

            // 인게임: HUD 의 PopupGoldShop (현 구조 유지 — 인게임 골드부족은 팝업).
            if (HUDController.HasInstance && HUDController.Instance.GoldShopPopup != null)
            {
                HUDController.Instance.GoldShopPopup.OpenUI();
                return;
            }

            // ROLLBACK_LOBBY_NOCOIN_TO_SHOPPAGE_20260622: 로비 골드부족은 PopupGoldShop 이 아니라
            //   UILobby 상점 페이지(page 0)로 이동해야 한다(구조 규칙: 로비=상점 페이지, 인게임=PopupGoldShop).
            //   롤백: 아래 UILobby 분기 제거하고 PopupGoldShop 오픈만 남김.
            UILobby lobby = FindUILobby();
            if (lobby != null)
            {
                lobby.GoToPage(0);
                return;
            }

            // 폴백: UILobby 도 못 찾는 예외 케이스에만 PopupGoldShop.
            if (UIManager.HasInstance)
                UIManager.Instance.OpenUI<PopupGoldShop>("Popup/PopupGoldShop");
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
            // ROLLBACK_AD_EVENT_FIELDS_20260707: ad_placement 규약 문자열 전달 (스키마 v3.2).
            AdManager.Instance.ShowRewardedAd(GrantAdReward, ignoreAdProtection: true, placement: "morelives_popup");
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

            // LifeManager.CurrentLives 는 GrantAdRewardLife 까지 미증가 — 시각 펄스용 가상 +1 값 전달.
            int lifeAfter = (LifeManager.HasInstance ? LifeManager.Instance.CurrentLives : 0) + 1;
            ItemFlyEffect.Play(icon, from, to, 1,
                onEachLand: () => lobby.PulseLifePanel(lifeAfter),
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
