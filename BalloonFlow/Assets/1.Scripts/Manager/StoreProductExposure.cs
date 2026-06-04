using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    public enum StoreExposureStage
    {
        Stage1 = 1,
        Stage2 = 2,
        Stage3 = 3
    }

    /// <summary>
    /// Shared shop exposure rules for lobby shop tab (UIShop) and in-game shop popup (PopupGoldShop).
    /// 단일 진실 소스 — ShopCatalogService.GetVisibleForUser 및 두 UI 진입점이 모두 BuildProducts 로 위임한다.
    ///
    /// 단계 정책 (1.0 소프트런칭 — 2026-06-04 레벨 클리어만으로 단순화):
    /// - Stage1 (기본): 코인 카테고리만 노출.
    /// - Stage2: highestClearedLevel ≥ <see cref="Stage2MinLevel"/> (15 클리어)
    ///           → offer + bundle + coin 노출 (인게임 아이템 포함 상품군).
    /// - Stage3: highestClearedLevel ≥ <see cref="Stage3MinLevel"/> (20 클리어)
    ///           → offer + noads + bundle + coin 노출 (광고 제거 상품군 추가).
    /// (유저가 학습한 이후 노출한다는 맥락 유지. 이전엔 zapTutorial/firstInterstitial 플래그 AND 조건이 있었으나 제거.)
    ///
    /// expanded=false (메인 배너 모드): 배너 슬롯 <see cref="MainBannerSlots"/> 개,
    /// 그 후 코인 <see cref="MainCoinSlotsAfterStage1"/> 개까지 노출.
    /// expanded=true: 카테고리 전체 노출 (단계별 허용 범위 내).
    /// </summary>
    public static class StoreProductExposure
    {
        private const int MainBannerSlots = 3;
        private const int MainCoinSlotsAfterStage1 = 2;
        private const int Stage2MinLevel = 15;
        private const int Stage3MinLevel = 20;

        /// <summary>유저 진척도(클리어 레벨)로 현재 노출 단계(Stage1~3)를 판정.
        /// [단순화 2026-06-04] 플래그(zapTutorial/firstInterstitial) AND 조건 제거 — 레벨 클리어만으로 게이트.
        ///   (유저가 학습한 이후 노출한다는 맥락은 동일: Stage2=15클리어, Stage3=20클리어.)</summary>
        public static StoreExposureStage DetermineStage(UserData user)
        {
            // [단일 진실 소스] 클리어 진행도는 FtueGate.HighestClearedLevel(PlayerPrefs BF_HighestLevel) 기준.
            // 온보딩/광고해금(20)/WS해금(35) 게이트가 모두 이걸 읽는다. 상점도 동일 소스를 써야 어긋나지 않음.
            // (user.highestClearedLevel = Firestore 필드는 _isReady·동기화 타이밍에 따라 stale 가능 → 사용 금지.)
            int highestCleared = FtueGate.HighestClearedLevel;

            StoreExposureStage stage =
                (highestCleared >= Stage3MinLevel) ? StoreExposureStage.Stage3 :  // 광고 제거: 20 클리어
                (highestCleared >= Stage2MinLevel) ? StoreExposureStage.Stage2 :  // offer/bundle(인게임 아이템 포함): 15 클리어
                                                     StoreExposureStage.Stage1;   // 기본: 코인만

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // [검증 로그] stage 판정 — 입력이 바뀔 때만 1회 출력(스팸 방지).
            string diag = $"[Shop] stage={stage} | highestCleared={highestCleared}(FtueGate) (Stage2≥{Stage2MinLevel} 클리어, Stage3≥{Stage3MinLevel} 클리어) | user={(user == null ? "NULL" : "ok")}";
            if (diag != _lastStageDiag) { _lastStageDiag = diag; Debug.Log(diag); }
#endif
            return stage;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static string _lastStageDiag;
#endif

        /// <summary>단계 판정 후 catalog 를 필터·정렬해 실제 노출 리스트를 만든다.
        /// expanded=false 면 배너 슬롯 + 코인 일부만, true 면 단계별 허용 전체.</summary>
        public static List<ShopProductDoc> BuildProducts(
            IReadOnlyList<ShopProductDoc> catalog,
            UserData user,
            bool expanded)
        {
            var result = new List<ShopProductDoc>();
            if (catalog == null) return result;

            StoreExposureStage stage = DetermineStage(user);
            var offers = new List<ShopProductDoc>();
            var noAds = new List<ShopProductDoc>();
            var bundles = new List<ShopProductDoc>();
            var coins = new List<ShopProductDoc>();

            for (int i = 0; i < catalog.Count; i++)
            {
                var p = catalog[i];
                if (!IsBaseVisible(p)) continue;
                if (IsPurchasedHidden(p, user)) continue;

                string cat = NormalizeCategory(p.category);
                if (cat == "offer") offers.Add(p);
                else if (cat == "noads") noAds.Add(p);
                else if (cat == "bundle") bundles.Add(p);
                else if (cat == "coin") coins.Add(p);
            }

            SortByPriceThenOrder(offers);
            SortByPriceThenOrder(noAds);
            SortByPriceThenOrder(bundles);
            SortByPriceThenOrder(coins);

            if (stage == StoreExposureStage.Stage1)
            {
                result.AddRange(coins);
                return result;
            }

            if (!expanded)
            {
                AddBanners(result, stage, offers, noAds, bundles, MainBannerSlots);
                for (int i = 0; i < coins.Count && i < MainCoinSlotsAfterStage1; i++)
                    result.Add(coins[i]);
                return result;
            }

            result.AddRange(offers);
            if (stage == StoreExposureStage.Stage3)
                result.AddRange(noAds);
            result.AddRange(bundles);
            result.AddRange(coins);
            return result;
        }

        public static bool CanExpand(IReadOnlyList<ShopProductDoc> catalog, UserData user)
        {
            var collapsed = BuildProducts(catalog, user, false);
            var expanded = BuildProducts(catalog, user, true);
            return expanded.Count > collapsed.Count;
        }

        private static void AddBanners(
            List<ShopProductDoc> result,
            StoreExposureStage stage,
            List<ShopProductDoc> offers,
            List<ShopProductDoc> noAds,
            List<ShopProductDoc> bundles,
            int slots)
        {
            if (stage >= StoreExposureStage.Stage2)
                AddUntilFull(result, offers, slots);
            if (stage >= StoreExposureStage.Stage3)
                AddUntilFull(result, noAds, slots);
            AddUntilFull(result, bundles, slots);
        }

        private static void AddUntilFull(List<ShopProductDoc> result, List<ShopProductDoc> source, int slots)
        {
            for (int i = 0; i < source.Count && result.Count < slots; i++)
                result.Add(source[i]);
        }

        private static bool IsBaseVisible(ShopProductDoc p)
        {
            if (p == null || !p.visibleInShop) return false;
            string cat = NormalizeCategory(p.category);
            return cat == "offer" || cat == "noads" || cat == "bundle" || cat == "coin";
        }

        private static bool IsPurchasedHidden(ShopProductDoc p, UserData user)
        {
            string cat = NormalizeCategory(p.category);
            if (cat == "noads")
            {
                if (user != null && user.removedAds) return true;
                if (PlayerPrefs.GetInt(Const.PREFS_AD_REMOVED, 0) == 1) return true;
                if (PlayerPrefs.GetInt(Const.PREFS_NO_ADS_OWNED, 0) == 1) return true;
            }

            if (cat == "offer")
            {
                if (PlayerPrefs.GetInt(Const.PREFS_STARTER_PURCHASED, 0) == 1) return true;
            }

            if (p.maxPurchases == 1 && user != null && user.purchasedOnce != null
                && user.purchasedOnce.TryGetValue(p.productId, out bool purchased) && purchased)
                return true;

            return false;
        }

        private static string NormalizeCategory(string category)
        {
            return string.IsNullOrEmpty(category) ? "" : category.ToLowerInvariant();
        }

        private static void SortByPriceThenOrder(List<ShopProductDoc> list)
        {
            list.Sort((a, b) =>
            {
                int price = a.priceUsd.CompareTo(b.priceUsd);
                return price != 0 ? price : a.sortOrder.CompareTo(b.sortOrder);
            });
        }
    }
}
