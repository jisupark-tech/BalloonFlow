using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// WinningStreak stage 보상 수령 시 WinningStreakGetReward.prefab 1~3개 spawn.
    /// prefab 구조:
    ///   WinningStreakGetReward > GameObject > [ImageReward, TextRewardOutline > TextReward]
    /// prefab 내부 Y 이동 + 알파 애니메이션은 이미 제작됨 (Animator). 시작 위치만 잡음.
    ///
    /// 호출:
    ///   WinningStreakGetRewardSpawner.Play(stageRewards, screenAnchor);
    /// 또는 자동 — WinningStreakManager.OnStageClaimed 구독.
    /// </summary>
    public static class WinningStreakGetRewardSpawner
    {
        private const string PrefabResource = "UI/UIAssets/WinningStreakGetReward";
        private const float StackSpacingY   = 130f;   // 보상 1개당 Y 간격 (px)
        private const float StackBaseOffsetY = 0f;    // 첫 보상의 Y 오프셋

        private static bool _subscribed;
        private static GameObject _prefab;

        /// <summary>WinningStreakManager.OnStageClaimed 자동 hook 등록. SdkBootstrap 같은 부트 단계에서 1회 호출.</summary>
        public static void EnsureSubscribed()
        {
            if (_subscribed) return;
            if (!WinningStreakManager.HasInstance) return;
            WinningStreakManager.Instance.OnStageClaimed += HandleStageClaimed;
            _subscribed = true;
        }

        private static void HandleStageClaimed(int stage1Based)
        {
            if (!WinningStreakConfigService.HasInstance) return;
            var stage = WinningStreakConfigService.Instance.GetStage(stage1Based);
            if (stage == null || stage.rewards == null) return;
            Play(stage.rewards, Vector2.zero);
        }

        /// <summary>보상 1~3개를 anchor 위치에서 시작해 spawn. anchor 는 screen 좌표 또는 local UI 좌표.</summary>
        public static void Play(ShopRewards rewards, Vector2 anchor)
        {
            if (rewards == null) return;
            EnsurePrefab();
            if (_prefab == null) return;

            Transform parent = GetParentTransform();
            if (parent == null) return;

            var entries = BuildEntries(rewards);
            for (int i = 0; i < entries.Count; i++)
                SpawnOne(parent, entries[i], anchor, i, entries.Count);
        }

        private static void EnsurePrefab()
        {
            if (_prefab != null) return;
            _prefab = Resources.Load<GameObject>(PrefabResource);
            if (_prefab == null)
                Debug.LogError($"[WinningStreakGetRewardSpawner] Resources/{PrefabResource}.prefab not found.");
        }

        private static Transform GetParentTransform()
        {
            if (!UIManager.HasInstance) return null;
            var ui = UIManager.Instance;
            return ui.EffectTr != null ? ui.EffectTr
                 : ui.PopupTr  != null ? ui.PopupTr
                 : ui.UiTr;
        }

        private static List<Entry> BuildEntries(ShopRewards r)
        {
            var list = new List<Entry>(3);
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

        private static void SpawnOne(Transform parent, Entry entry, Vector2 anchor, int index, int total)
        {
            var go = Object.Instantiate(_prefab, parent);
            go.name = $"WinningStreakGetReward_{index}";
            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                // 총 N 개 보상의 중앙이 anchor 가 되도록 분산.
                float centerOffset = (total - 1) * StackSpacingY * 0.5f;
                float y = anchor.y + StackBaseOffsetY + (centerOffset - index * StackSpacingY);
                rt.anchoredPosition = new Vector2(anchor.x, y);
            }

            // ImageReward sprite swap
            var imgGo = FindChildGo(go.transform, "ImageReward");
            if (imgGo != null)
            {
                var img = imgGo.GetComponent<Image>();
                if (img != null)
                {
                    var sprite = ResolveRewardSprite(entry.type);
                    if (sprite != null) img.sprite = sprite;
                }
            }

            // TextReward 카운트
            string text = FormatEntryText(entry);
            var txt = FindChildTmp(go.transform, "TextReward");
            var outline = FindChildTmp(go.transform, "TextRewardOutline");
            if (txt != null) txt.text = text;
            if (outline != null) outline.text = text;
        }

        private static string FormatEntryText(Entry entry)
        {
            if (entry.count <= 0) return "";
            switch (entry.type)
            {
                case RewardKind.Coin: return entry.count.ToString();             // "5000", no x
                case RewardKind.InfiniteHearts:
                    int s = entry.count;
                    int h = s / 3600;
                    if (h >= 1) return $"{h}h";
                    int m = s / 60;
                    return m > 0 ? $"{m}m" : $"{s}s";
                default: return $"x{entry.count}";                                // booster
            }
        }

        private static Sprite ResolveRewardSprite(RewardKind type)
        {
            if (!ResourceManager.HasInstance) return null;
            string spriteName = type switch
            {
                RewardKind.Coin           => Const.SPR_REWARDGOLD,
                RewardKind.InfiniteHearts => Const.SPR_REWARDLIFE,
                RewardKind.Hand           => Const.SPR_REWARDHAND,
                RewardKind.Shuffle        => Const.SPR_REWARDSUFFLE,
                RewardKind.Zap            => Const.SPR_REWARDZAP,
                _ => null
            };
            return ResourceManager.Instance.GetUISprite(spriteName);
        }

        private static GameObject FindChildGo(Transform root, string name)
        {
            if (root == null) return null;
            var arr = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < arr.Length; i++)
                if (arr[i].name == name) return arr[i].gameObject;
            return null;
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
