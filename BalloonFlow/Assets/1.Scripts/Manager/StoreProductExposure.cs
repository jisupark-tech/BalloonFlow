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
    /// Shared shop exposure rules for lobby shop tab and in-game shop popup.
    /// </summary>
    public static class StoreProductExposure
    {
        private const int MainBannerSlots = 3;
        private const int MainCoinSlotsAfterStage1 = 2;

        public static StoreExposureStage DetermineStage(UserData user)
        {
            int maxReachedLevel = GetMaxReachedLevel(user);
            if (maxReachedLevel >= 20 && HasFirstInterstitialShown(user))
                return StoreExposureStage.Stage3;
            if (maxReachedLevel >= 15 && HasZapTutorialCompleted(user))
                return StoreExposureStage.Stage2;
            return StoreExposureStage.Stage1;
        }

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

        private static int GetMaxReachedLevel(UserData user)
        {
            return user != null ? Mathf.Max(1, user.highestClearedLevel + 1) : 1;
        }

        private static bool HasZapTutorialCompleted(UserData user)
        {
            if (user != null && user.zapTutorialCompleted) return true;
            return PlayerPrefs.GetInt(Const.PREFS_ZAP_TUTORIAL_COMPLETED, 0) == 1;
        }

        private static bool HasFirstInterstitialShown(UserData user)
        {
            if (user != null && user.firstInterstitialShown) return true;
            return PlayerPrefs.GetInt(Const.PREFS_FIRST_INTERSTITIAL_SHOWN, 0) == 1;
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
