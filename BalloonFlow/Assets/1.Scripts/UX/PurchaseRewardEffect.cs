using System.Collections;
using System.Text;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// IAP 결제 성공 후 보상 시각 연출 컨트롤러.
    /// OnPurchaseRewardGranted 이벤트를 받아:
    ///   1) PopupError.ShowPurchaseSuccess (iconCheck) 띄움 — 받은 보상 요약 표시
    ///   2) 사용자 확인 클릭 시 → Lobby 페이지(Home) 전환
    ///   3) 화면 중앙에서 FxGold 다발 spawn → GoldPanel 비행
    ///   4) 한 알 도착 시마다 GoldPanel 펄스 + 코인 카운트업
    ///   5) 모두 도착 시 CurrencyManager.PublishCoinSync (다른 listener 와 sync)
    /// 코인 보상 없는 상품 (noads 등) 은 popup 만 띄우고 fly 연출 skip.
    /// </summary>
    public class PurchaseRewardEffect : Singleton<PurchaseRewardEffect>
    {
        private const int FLY_COUNT = 10;
        private const int ITEM_FLY_COUNT = 1;
        private const float ITEM_FLY_START_DELAY = 1f; // FXGold 시작 후 FXItem 시작까지 stagger

        // [2026-05-15] Booster / Life / InfiniteHearts fly icon.
        // Inspector wire 필요 — Resources 안 아이콘 (예: booster_hand.png / heart.png) 를 직접 연결.
        // null 이면 해당 종류 fly 스킵 + 코인만 fly (안전 폴백).
        [Header("[Item Fly Icons — 인스펙터 wire]")]
        [SerializeField] private Sprite _iconBooster;
        [SerializeField] private Sprite _iconHand;
        [SerializeField] private Sprite _iconShuffle;
        [SerializeField] private Sprite _iconZap;
        [SerializeField] private Sprite _iconLife;

        protected override void OnSingletonAwake()
        {
            // ResourceManager 에서 atlas fallback — Inspector wire 가 비어 있어도 가능한 한 자동 채움.
            // PopupBuyItem 의 atlas key 와 동일. iconHeart 키가 atlas 에 없으면 _iconLife 는 null 유지.
            EnsureIconsLoaded();
            EventBus.Subscribe<OnPurchaseRewardGranted>(HandleReward);
            EventBus.Subscribe<OnPurchaseCompleted>(HandlePurchaseCompleted);
        }

        protected override void OnDestroy()
        {
            EventBus.Unsubscribe<OnPurchaseRewardGranted>(HandleReward);
            EventBus.Unsubscribe<OnPurchaseCompleted>(HandlePurchaseCompleted);
            base.OnDestroy();
        }

        /// <summary>
        /// 구매 실패 처리. success=true 는 OnPurchaseRewardGranted (HandleReward) 가 먼저 발화돼서 spinner/UI 전이를 이미 처리.
        /// success=false 면 OnPurchaseRewardGranted 가 발행되지 않으므로 여기서 spinner 닫고 에러 popup.
        /// 트리거: IAPManager 의 미초기화 / product unavailable / 1회 한정 재구매 / OnPurchaseFailed callback.
        /// </summary>
        private void HandlePurchaseCompleted(OnPurchaseCompleted evt)
        {
            if (evt.success) return;

            Debug.LogWarning($"[PurchaseRewardEffect] HandlePurchaseCompleted fail productId={evt.productId}");

            if (!UIManager.HasInstance) return;

            var spinner = UIManager.Instance.GetOpenUI<PopupLoadingSpinner>();
            if (spinner != null)
            {
                spinner.SetCloseCallback(() => OpenPaymentFailedPopup());
                spinner.CloseUI();
                return;
            }

            OpenPaymentFailedPopup();
        }

        private static void OpenPaymentFailedPopup()
        {
            if (!UIManager.HasInstance) return;
            var popup = UIManager.Instance.OpenUI<PopupError>("Popup/PopupError");
            if (popup != null) popup.ShowPaymentFailed();
            else Debug.LogWarning("[PurchaseRewardEffect] OpenUI<PopupError> returned null on payment fail");
        }

        /// <summary>
        /// 순서 게이트: 활성 PopupLoadingSpinner 가 있으면 spinner.SetCloseCallback 로 PopupError.ShowPurchaseSuccess 호출을 예약 → spinner.CloseUI() 발화 시 콜백 실행. 결과 순서: PopupLoadingSpinner → PopupError. spinner 부재 시 fallback 직접 표시.
        /// </summary>
        private void HandleReward(OnPurchaseRewardGranted evt)
        {
            string desc = GetPurchaseCompletedDescription();
            int coinsAdded = evt.coinsAdded;
            ShopRewards rewards = evt.rewards;
            Debug.Log($"[PurchaseRewardEffect] HandleReward productId={evt.productId} coinsAdded={coinsAdded}");

            if (UIManager.HasInstance)
            {
                // 구매 성공 팝업은 PopupLoadingSpinner 닫힘 이후에만 표시되도록 spinner.SetCloseCallback에 등록한다 — 사용자 피드백 반영
                var spinner = UIManager.Instance.GetOpenUI<PopupLoadingSpinner>();
                if (spinner != null)
                {
                    Debug.Log("[PurchaseRewardEffect] Spinner active — defer success popup until spinner closes");
                    spinner.SetCloseCallback(() =>
                    {
                        var popup = UIManager.Instance.OpenUI<PopupError>("Popup/PopupError");
                        if (popup != null)
                        {
                            popup.ShowPurchaseSuccess(desc, () =>
                            {
                                Debug.Log("[PurchaseRewardEffect] Success popup OK clicked → PlayEffectFlow");
                                StartCoroutine(PlayEffectFlow(coinsAdded, rewards));
                            });
                        }
                        else
                        {
                            Debug.LogWarning("[PurchaseRewardEffect] OpenUI<PopupError> returned null after spinner close — fallback effect 즉시");
                            StartCoroutine(PlayEffectFlow(coinsAdded, rewards));
                        }
                    });
                    spinner.CloseUI();
                    return;
                }

                Debug.LogWarning("[PurchaseRewardEffect] PopupLoadingSpinner not active — fallback direct show");
                var directPopup = UIManager.Instance.OpenUI<PopupError>("Popup/PopupError");
                if (directPopup != null)
                {
                    Debug.Log("[PurchaseRewardEffect] Success popup opened (fallback), waiting for OK");
                    directPopup.ShowPurchaseSuccess(desc, () =>
                    {
                        Debug.Log("[PurchaseRewardEffect] Success popup OK clicked → PlayEffectFlow");
                        StartCoroutine(PlayEffectFlow(coinsAdded, rewards));
                    });
                    return;
                }
                Debug.LogWarning("[PurchaseRewardEffect] OpenUI<PopupError> returned null — fallback effect 즉시");
            }

            // popup 못 띄우면 즉시 effect (UI sync 만이라도 진행)
            StartCoroutine(PlayEffectFlow(coinsAdded, rewards));
        }

        private IEnumerator PlayEffectFlow(int coinsAdded, ShopRewards rewards)
        {
            // 1) Lobby Home 페이지로 전환
            UILobby lobby = FindUILobby();
            Debug.Log($"[PurchaseRewardEffect] PlayEffectFlow start. coinsAdded={coinsAdded} lobby={(lobby!=null?"OK":"null")}");
            if (lobby != null) lobby.GoToPage(1);

            // 페이지 전환 애니메이션 잠시 대기 (UILobby PAGE_SWIPE_DURATION = 0.3s)
            yield return new WaitForSecondsRealtime(0.35f);
            EnsureIconsLoaded();

            Vector2 from = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            // 2) Coin fly to GoldPanel — 기존 로직 그대로
            if (coinsAdded > 0 && lobby != null)
            if (coinsAdded > 0 && lobby != null)
            {
                Vector2 to = lobby.GetGoldPanelScreenPos();
                Debug.Log($"[PurchaseRewardEffect] Coin fly from={from} to={to} count={FLY_COUNT}");

                if (CurrencyManager.HasInstance)
                {
                    int displayBase = CurrencyManager.Instance.Coins - coinsAdded;
                    lobby.SetGoldText(displayBase);
                }

                int perCoinDelta = Mathf.Max(1, coinsAdded / FLY_COUNT);
                int remainder    = coinsAdded - perCoinDelta * FLY_COUNT;
                int landed       = 0;

                CoinFlyEffect.Play(from, to, FLY_COUNT,
                    onEachLand: () =>
                    {
                        int delta = perCoinDelta + (landed == FLY_COUNT - 1 ? remainder : 0);
                        landed++;
                        lobby.AddDisplayedGold(delta);
                        lobby.PulseGoldPanel();
                    },
                    onAllComplete: () =>
                    {
                        if (CurrencyManager.HasInstance)
                        {
                            lobby.SetGoldText(CurrencyManager.Instance.Coins);
                            CurrencyManager.Instance.PublishCoinSync();
                        }
                    });
            }
            else if (CurrencyManager.HasInstance)
            {
                CurrencyManager.Instance.PublishCoinSync();
            }

            if (coinsAdded > 0 && rewards != null) yield return new WaitForSecondsRealtime(ITEM_FLY_START_DELAY);

            // 3) Booster / Life / InfiniteHearts fly to LifePanel, then grant item rewards.
            if (rewards != null)
            {
                int pendingItemFly = 0;
                Vector2 boosterTo = lobby != null ? lobby.GetGameStartButtonScreenPos() : from;
                Vector2 lifeTo = lobby != null ? lobby.GetLifePanelScreenPos() : from;

                void PlayItem(Sprite icon, int rewardCount, string label, Vector2 target, System.Action onLand)
                {
                    if (rewardCount <= 0) return;
                    if (lobby == null)
                    {
                        Debug.LogWarning($"[PurchaseRewardEffect] {label} FXItem skipped - lobby missing. Reward still applies.");
                        return;
                    }

                    if (icon == null)
                        Debug.LogWarning($"[PurchaseRewardEffect] {label} icon missing - using FXItem prefab default sprite.");

                    pendingItemFly++;
                    ItemFlyEffect.Play(icon, from, target, ITEM_FLY_COUNT,
                        onEachLand: onLand,
                        onAllComplete: () => pendingItemFly--);
                }

                if (rewards.boosters != null)
                {
                    PlayItem(_iconHand != null ? _iconHand : _iconBooster, rewards.boosters.hand, "Hand", boosterTo, () => lobby.PulseGameStartButton());
                    PlayItem(_iconShuffle != null ? _iconShuffle : _iconBooster, rewards.boosters.shuffle, "Shuffle", boosterTo, () => lobby.PulseGameStartButton());
                    PlayItem(_iconZap != null ? _iconZap : _iconBooster, rewards.boosters.zap, "Zap", boosterTo, () => lobby.PulseGameStartButton());
                }

                // LifeManager.CurrentLives 는 ApplyItemRewardsAfterFx 까지 미증가 — 시각 펄스용 가상 +1.
                int lifeAfter = (LifeManager.HasInstance ? LifeManager.Instance.CurrentLives : 0) + 1;
                PlayItem(_iconLife, rewards.infiniteHeartsSeconds > 0 ? 1 : 0, "InfiniteHearts", lifeTo,
                    () => lobby.PulseLifePanel(lifeAfter));

                while (pendingItemFly > 0)
                    yield return null;

                ApplyItemRewardsAfterFx(rewards);
            }
        }

        private void EnsureIconsLoaded()
        {
            if (!ResourceManager.HasInstance) return;

            var rm = ResourceManager.Instance;
            _iconHand    = rm.UISpriteOr(Const.SPR_ICONHAND,   _iconHand);
            _iconShuffle = rm.UISpriteOr(Const.SPR_ICONSUFFLE, _iconShuffle);
            _iconZap     = rm.UISpriteOr(Const.SPR_ICONZAP,    _iconZap);
            _iconBooster = rm.UISpriteOr(Const.SPR_ICONHAND,   _iconBooster);
            _iconLife    = rm.UISpriteOr(Const.SPR_ICONLIFE,   _iconLife);
        }

        private static void ApplyItemRewardsAfterFx(ShopRewards rewards)
        {
            if (rewards == null) return;

            if (rewards.boosters != null && BoosterManager.HasInstance)
            {
                // ROLLBACK_ANALYTICS_NULLFILL_20260625: IAP/번들 보상 — 실금액 비용은 int 로 즉시 못 얻어 0(통화 표기만).
                if (rewards.boosters.hand    > 0) BoosterManager.Instance.AddBooster(BoosterManager.HAND,    rewards.boosters.hand,    "iap_purchase", 0, "");
                if (rewards.boosters.shuffle > 0) BoosterManager.Instance.AddBooster(BoosterManager.SHUFFLE, rewards.boosters.shuffle, "iap_purchase", 0, "");
                if (rewards.boosters.zap     > 0) BoosterManager.Instance.AddBooster(BoosterManager.ZAP,     rewards.boosters.zap,     "iap_purchase", 0, "");
            }

            if (rewards.infiniteHeartsSeconds > 0 && LifeManager.HasInstance)
                LifeManager.Instance.ActivateInfiniteHearts(rewards.infiniteHeartsSeconds);
        }

        private static UILobby FindUILobby()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<UILobby>(FindObjectsInactive.Exclude);
#else
            return Object.FindObjectOfType<UILobby>();
#endif
        }

        /// <summary>구매 보상 구성을 사람이 읽을 수 있는 multi-line description 으로.</summary>
        private static string GetPurchaseCompletedDescription()
        {
            // ROLLBACK_PURCHASE_SUCCESS_GENERIC_COPY_20260624:
            // Purchase success popup must not list reward item details. Reward grants/fly effects
            // still use the original ShopRewards data after the popup is confirmed.
            return LocalizationService.Get("popup.txtdescription.purchasecompleted");
        }

        private static string BuildRewardDescription(OnPurchaseRewardGranted evt)
        {
            var r = evt.rewards;
            if (r == null) return "Purchase successful!";

            var sb = new StringBuilder();
            if (r.coins > 0) sb.AppendLine($"+ {r.coins:N0} coins");
            if (r.boosters != null)
            {
                if (r.boosters.hand    > 0) sb.AppendLine($"+ {r.boosters.hand} Hand");
                if (r.boosters.shuffle > 0) sb.AppendLine($"+ {r.boosters.shuffle} Shuffle");
                if (r.boosters.zap     > 0) sb.AppendLine($"+ {r.boosters.zap} Zap");
            }
            if (r.infiniteHeartsSeconds > 0)
            {
                int hours = Mathf.RoundToInt(r.infiniteHeartsSeconds / 3600f);
                sb.AppendLine($"+ Infinite hearts {hours}h");
            }
            if (r.removeAds) sb.AppendLine("+ Ads removed");

            string s = sb.ToString().TrimEnd();
            return string.IsNullOrEmpty(s) ? "Purchase successful!" : s;
        }
    }
}
