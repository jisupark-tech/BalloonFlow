using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// WinningStreakClickInfo (slot 클릭 시 표시되는 tooltip) 의 보상 바인딩 컨트롤러.
    /// prefab 구조:
    ///   ImageFrame > [RewardItem, ImagePlus, RewardItem (1), ImagePlus, RewardItem (2), ArrowTop/Bottom]
    /// 각 RewardItem 자식:
    ///   RewardGold (variant — coin)
    ///   RewardItem (variant — booster/heart) > ImageItem | ImageHeart + Text/RewardFlag
    ///   ImageGift (variant — 모름/generic)
    ///
    /// 보상 N개 (1~3): 첫 N 개 RewardItem 활성 + ImagePlus N-1 개 활성. 나머지 비활성.
    /// </summary>
    public static class WinningStreakClickInfoBinder
    {
        // 외부에서 reward type 결정 후 호출. WinningStreakStage.rewards (ShopRewards) 활용.
        public static void Bind(GameObject tooltip, WinningStreakStage stage)
        {
            if (tooltip == null) return;
            var entries = BuildEntries(stage);
            BindEntries(tooltip, entries);
        }

        public static void Bind(GameObject tooltip, ShopRewards rewards)
        {
            if (tooltip == null) return;
            var entries = BuildEntries(rewards);
            BindEntries(tooltip, entries);
        }

        // ── Entries 구성 ─────────────────────────────────────────

        public static int CountRewards(WinningStreakStage stage)
            => stage != null ? CountRewards(stage.rewards) : 0;

        public static int CountRewards(ShopRewards rewards)
            => BuildEntries(rewards).Count;

        private static List<Entry> BuildEntries(WinningStreakStage stage)
            => stage != null ? BuildEntries(stage.rewards) : new List<Entry>();

        private static List<Entry> BuildEntries(ShopRewards r)
        {
            var list = new List<Entry>(3);
            if (r == null) return list;
            if (r.coins > 0) list.Add(new Entry { type = RewardKind.Coin, count = r.coins });
            if (r.boosters != null)
            {
                if (r.boosters.hand    > 0) list.Add(new Entry { type = RewardKind.Hand,    count = r.boosters.hand });
                if (r.boosters.shuffle > 0) list.Add(new Entry { type = RewardKind.Shuffle, count = r.boosters.shuffle });
                if (r.boosters.zap     > 0) list.Add(new Entry { type = RewardKind.Zap,     count = r.boosters.zap });
            }
            if (r.infiniteHeartsSeconds > 0)
                list.Add(new Entry { type = RewardKind.InfiniteHearts, count = r.infiniteHeartsSeconds });
            if (list.Count > 3) list.RemoveRange(3, list.Count - 3);
            return list;
        }

        // ── 바인딩 ──────────────────────────────────────────────

        private static void BindEntries(GameObject tooltip, List<Entry> entries)
        {
            Transform frame = FindChild(tooltip.transform, "ImageFrame");
            if (frame == null) frame = tooltip.transform;

            var rewardItemTransforms = new List<Transform>(3);
            var plusTransforms = new List<Transform>(2);

            for (int i = 0; i < frame.childCount; i++)
            {
                var child = frame.GetChild(i);
                string n = child.name;
                if (n.StartsWith("RewardItem")) rewardItemTransforms.Add(child);
                else if (n.StartsWith("ImagePlus") || n == "Plus") plusTransforms.Add(child);
            }

            int activeCount = Mathf.Clamp(entries.Count, 0, rewardItemTransforms.Count);
            for (int i = 0; i < rewardItemTransforms.Count; i++)
            {
                bool active = i < activeCount;
                rewardItemTransforms[i].gameObject.SetActive(active);
                if (active) BindRewardItem(rewardItemTransforms[i].gameObject, entries[i]);
            }
            for (int i = 0; i < plusTransforms.Count; i++)
                plusTransforms[i].gameObject.SetActive(i < Mathf.Max(0, activeCount - 1));
        }

        private static void BindRewardItem(GameObject rewardItemRoot, Entry entry)
        {
            // 자식 variant: RewardGold / RewardItem (내부) / ImageGift
            GameObject vRewardGold = FindChildGo(rewardItemRoot.transform, "RewardGold");
            GameObject vRewardItem = FindChildGo(rewardItemRoot.transform, "RewardItem");
            GameObject vImageGift  = FindChildGo(rewardItemRoot.transform, "ImageGift");

            bool useGold = entry.type == RewardKind.Coin;
            bool useGift = false; // 현재 명세상 generic gift 없음. 추후 special reward 시 활성.
            bool useItem = !useGold && !useGift;

            if (vRewardGold != null) vRewardGold.SetActive(useGold);
            if (vRewardItem != null) vRewardItem.SetActive(useItem);
            if (vImageGift  != null) vImageGift.SetActive(useGift);

            if (useGold && vRewardGold != null) ApplyText(vRewardGold, entry);
            else if (useItem && vRewardItem != null) ApplyItem(vRewardItem, entry);
        }

        private static void ApplyItem(GameObject itemRoot, Entry entry)
        {
            GameObject imgItem  = FindChildGo(itemRoot.transform, "ImageItem");
            GameObject imgHeart = FindChildGo(itemRoot.transform, "ImageHeart");

            bool isHeart = entry.type == RewardKind.InfiniteHearts;
            if (imgItem != null)  imgItem.SetActive(!isHeart);
            if (imgHeart != null) imgHeart.SetActive(isHeart);

            if (!isHeart && imgItem != null)
            {
                var img = imgItem.GetComponent<Image>();
                if (img != null)
                {
                    var sprite = ResolveItemSprite(entry.type);
                    if (sprite != null) img.sprite = sprite;
                }
            }
            ApplyText(itemRoot, entry);
        }

        private static void ApplyText(GameObject root, Entry entry)
        {
            string text = FormatEntryText(entry);
            var txt = FindChildTmp(root.transform, "TextReward");
            var outline = FindChildTmp(root.transform, "TextRewardOutline");
            if (txt != null) txt.text = text;
            if (outline != null) outline.text = text;
        }

        private static string FormatEntryText(Entry entry)
        {
            if (entry.type == RewardKind.InfiniteHearts)
            {
                // 시간 표시 — 단순화: "x1" 같은 무한하트 표기 또는 분/초 변환.
                int seconds = Mathf.Max(0, entry.count);
                int hours = seconds / 3600;
                if (hours >= 1) return $"x{hours}h";
                int mins = seconds / 60;
                return mins > 0 ? $"x{mins}m" : $"x{seconds}s";
            }
            return entry.count > 0 ? $"x{entry.count}" : "";
        }

        private static Sprite ResolveItemSprite(RewardKind type)
        {
            if (!ResourceManager.HasInstance) return null;
            string spriteName = type switch
            {
                RewardKind.Zap     => Const.SPR_ICONZAP,
                RewardKind.Hand    => Const.SPR_ICONHAND,
                RewardKind.Shuffle => Const.SPR_ICONSUFFLE,
                _ => null
            };
            return ResourceManager.Instance.GetUISprite(spriteName);
        }

        // ── helpers ─────────────────────────────────────────────

        private static Transform FindChild(Transform root, string name)
        {
            if (root == null) return null;
            var arr = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < arr.Length; i++)
                if (arr[i].name == name) return arr[i];
            return null;
        }

        private static GameObject FindChildGo(Transform root, string name)
        {
            var t = FindChild(root, name);
            return t != null ? t.gameObject : null;
        }

        private static TMP_Text FindChildTmp(Transform root, string name)
        {
            if (root == null) return null;
            var arr = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < arr.Length; i++)
                if (arr[i].name == name) return arr[i];
            return null;
        }

        private enum RewardKind { Coin, Hand, Shuffle, Zap, InfiniteHearts }

        private struct Entry
        {
            public RewardKind type;
            public int count;
        }
    }
}
