using System.Collections;
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

        // GETREWARD_POOL (2026-06-11): 이전엔 Instantiate 후 파괴 코드가 없어 사용할 때마다 인스턴스 누적.
        // 상승+페이드(UILobby WS_REWARD_RISE_DURATION=0.55 + 대기 0.25) 종료 후 풀로 반납해 재사용.
        // 반납 SetActive(false) → 재사용 SetActive(true) 시 prefab Animator 가 entry state 부터 재생.
        private const float ReleaseAfterSeconds = 1.6f;
        private const int PoolMaxCount = 9;           // 동시 표시 최대 3개 × 여유

        private static bool _subscribed;
        private static GameObject _prefab;
        private static readonly Stack<GameObject> _pool = new Stack<GameObject>(PoolMaxCount);
        private static PoolRunner _runner;            // 코루틴 호스트 + 풀 보관 루트 (씬과 함께 파괴)

        private sealed class PoolRunner : MonoBehaviour { }

        private static PoolRunner EnsureRunner()
        {
            if (_runner != null) return _runner;
            var go = new GameObject("[WinningStreakGetRewardPool]");
            _runner = go.AddComponent<PoolRunner>();
            return _runner;
        }

        private static GameObject TakeFromPool()
        {
            while (_pool.Count > 0)
            {
                GameObject go = _pool.Pop();
                if (go != null) return go; // 씬 전환으로 파괴된 항목은 스킵
            }
            return null;
        }

        private static IEnumerator ReleaseRoutine(GameObject go)
        {
            // 로비 FX 가 unscaled 시간 기반이라 동일하게 realtime 사용.
            yield return new WaitForSecondsRealtime(ReleaseAfterSeconds);
            if (go == null) yield break;
            go.SetActive(false);
            if (_pool.Count >= PoolMaxCount || _runner == null)
            {
                Object.Destroy(go);
                yield break;
            }
            go.transform.SetParent(_runner.transform, false);
            _pool.Push(go);
        }

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
            // GETREWARD_POOL: 풀 재사용 우선, 없을 때만 Instantiate. 수명 후 ReleaseRoutine 이 반납.
            GameObject go = TakeFromPool();
            if (go == null) go = Object.Instantiate(_prefab, parent);
            else go.transform.SetParent(parent, false);
            go.name = $"WinningStreakGetReward_{index}";
            go.SetActive(true);
            EnsureRunner().StartCoroutine(ReleaseRoutine(go));
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
                    if (sprite != null)
                    {
                        img.sprite = sprite;
                        img.preserveAspect = true;
                        if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
                    }
                    else
                    {
                        // sprite 미해석 → 프리팹 기본(잘못된) 아이콘이 그대로 보이는 문제의 원인.
                        Debug.LogWarning($"[WinningStreakGetRewardSpawner] 보상 아이콘 sprite 미발견 — type={entry.type}. atlas_ui 키 확인 필요.");
                    }
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
                    // ROLLBACK_WS_REWARD_TIME_LOCALIZE_20260715: 시간 단위 언어 인지(EN h/m/s, KO 시간/분/초).
                    int s = entry.count;
                    bool ko = LocalizationService.CurrentLanguageCode == "KO";
                    int h = s / 3600;
                    if (h >= 1) return ko ? $"{h}시간" : $"{h}h";
                    int m = s / 60;
                    if (m > 0) return ko ? $"{m}분" : $"{m}m";
                    return ko ? $"{s}초" : $"{s}s";
                default: return $"x{entry.count}";                                // booster
            }
        }

        private static Sprite ResolveRewardSprite(RewardKind type)
        {
            if (!ResourceManager.HasInstance) return null;
            // reward* 전용 아트 우선. atlas 에 미포함이면 RewardItem/팝업과 동일한 icon* 키로 폴백 —
            // 폴백까지 실패하면 null 반환 → 호출부가 경고 로그 (프리팹 기본 아이콘 노출 원인 추적용).
            string primary = type switch
            {
                RewardKind.Coin           => Const.SPR_REWARDGOLD,
                RewardKind.InfiniteHearts => Const.SPR_REWARDLIFE,
                RewardKind.Hand           => Const.SPR_REWARDHAND,
                RewardKind.Shuffle        => Const.SPR_REWARDSUFFLE,
                RewardKind.Zap            => Const.SPR_REWARDZAP,
                _ => null
            };
            string fallback = type switch
            {
                RewardKind.Coin           => Const.SPR_ICONGOLD,
                RewardKind.InfiniteHearts => Const.SPR_ICONHEARINFINITE,
                RewardKind.Hand           => Const.SPR_ICONHAND,
                RewardKind.Shuffle        => Const.SPR_ICONSUFFLE,
                RewardKind.Zap            => Const.SPR_ICONZAP,
                _ => null
            };
            var rm = ResourceManager.Instance;
            var sprite = rm.GetUISprite(primary);
            return sprite != null ? sprite : rm.GetUISprite(fallback);
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
