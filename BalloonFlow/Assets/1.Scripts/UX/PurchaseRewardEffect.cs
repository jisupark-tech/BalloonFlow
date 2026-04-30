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

        protected override void OnSingletonAwake()
        {
            EventBus.Subscribe<OnPurchaseRewardGranted>(HandleReward);
        }

        protected override void OnDestroy()
        {
            EventBus.Unsubscribe<OnPurchaseRewardGranted>(HandleReward);
            base.OnDestroy();
        }

        private void HandleReward(OnPurchaseRewardGranted evt)
        {
            string desc = BuildRewardDescription(evt);
            int coinsAdded = evt.coinsAdded;
            Debug.Log($"[PurchaseRewardEffect] HandleReward productId={evt.productId} coinsAdded={coinsAdded}");

            if (UIManager.HasInstance)
            {
                var popup = UIManager.Instance.OpenUI<PopupError>("Popup/PopupError");
                if (popup != null)
                {
                    Debug.Log("[PurchaseRewardEffect] Success popup opened, waiting for OK");
                    popup.ShowPurchaseSuccess(desc, () =>
                    {
                        Debug.Log("[PurchaseRewardEffect] Success popup OK clicked → PlayEffectFlow");
                        StartCoroutine(PlayEffectFlow(coinsAdded));
                    });
                    return;
                }
                Debug.LogWarning("[PurchaseRewardEffect] OpenUI<PopupError> returned null — fallback effect 즉시");
            }

            // popup 못 띄우면 즉시 effect (UI sync 만이라도 진행)
            StartCoroutine(PlayEffectFlow(coinsAdded));
        }

        private IEnumerator PlayEffectFlow(int coinsAdded)
        {
            // 1) Lobby Home 페이지로 전환
            UILobby lobby = FindUILobby();
            Debug.Log($"[PurchaseRewardEffect] PlayEffectFlow start. coinsAdded={coinsAdded} lobby={(lobby!=null?"OK":"null")}");
            if (lobby != null) lobby.GoToPage(1);

            // 페이지 전환 애니메이션 잠시 대기 (UILobby PAGE_SWIPE_DURATION = 0.3s)
            yield return new WaitForSecondsRealtime(0.35f);

            if (coinsAdded <= 0 || lobby == null)
            {
                // 코인 보상 없음 (noads / 부스터-only 등) — sync 만
                Debug.Log($"[PurchaseRewardEffect] Skip fly (coinsAdded={coinsAdded}, lobby={(lobby!=null?"OK":"null")}). Sync only.");
                if (CurrencyManager.HasInstance)
                    CurrencyManager.Instance.PublishCoinSync();
                yield break;
            }

            // 2) FxGold 비행 시작 — 도착 전 표시값을 결제 전 잔액으로 스냅
            Vector2 from = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 to   = lobby.GetGoldPanelScreenPos();
            Debug.Log($"[PurchaseRewardEffect] Fly from={from} to={to} count={FLY_COUNT}");

            if (CurrencyManager.HasInstance)
            {
                int displayBase = CurrencyManager.Instance.Coins - coinsAdded;
                lobby.SetGoldText(displayBase);
                Debug.Log($"[PurchaseRewardEffect] Snap displayBase={displayBase} (currentCoins={CurrencyManager.Instance.Coins})");
            }

            int perCoinDelta = Mathf.Max(1, coinsAdded / FLY_COUNT);
            int remainder    = coinsAdded - perCoinDelta * FLY_COUNT;
            int landed       = 0;

            CoinFlyEffect.Play(from, to, FLY_COUNT,
                onEachLand: () =>
                {
                    // 마지막 코인에 잔여분 누적 → 표시값이 정확히 새 잔액으로 떨어짐
                    int delta = perCoinDelta + (landed == FLY_COUNT - 1 ? remainder : 0);
                    landed++;
                    lobby.AddDisplayedGold(delta);
                    lobby.PulseGoldPanel();
                },
                onAllComplete: () =>
                {
                    Debug.Log($"[PurchaseRewardEffect] Fly complete. final coins={(CurrencyManager.HasInstance?CurrencyManager.Instance.Coins:0)}");
                    // 절대값 보정 + listener sync
                    if (CurrencyManager.HasInstance)
                    {
                        lobby.SetGoldText(CurrencyManager.Instance.Coins);
                        CurrencyManager.Instance.PublishCoinSync();
                    }
                });
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
