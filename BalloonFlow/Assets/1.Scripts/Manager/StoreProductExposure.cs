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
    /// 단계 정책 (1.0 소프트런칭):
    /// - Stage1 (기본): 코인 카테고리만 노출.
    /// - Stage2: highestClearedLevel ≥ <see cref="Stage2MinLevel"/>
    ///           → offer + bundle + coin 노출.
    /// - Stage3: highestClearedLevel ≥ <see cref="Stage3MinLevel"/>
    ///           → offer + noads + bundle + coin 노출.
    /// 맥락: 유저가 충분히 게임을 학습한 시점(레벨 15·20 클리어)에 노출.
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

        /// <summary>유저의 진척도를 보고 현재 노출 단계(Stage1~3)를 판정.</summary>
        public static StoreExposureStage DetermineStage(UserData user)
        {
            int cleared = user != null ? user.highestClearedLevel : 0;
            if (cleared >= Stage3MinLevel) return StoreExposureStage.Stage3;
            if (cleared >= Stage2MinLevel) return StoreExposureStage.Stage2;
            return StoreExposureStage.Stage1;
        }

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
