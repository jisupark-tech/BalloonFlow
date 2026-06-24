using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// Item reward fly effect. Uses Resources/UI/UIAssets/FXItem.prefab and applies reward callbacks after landing.
    /// </summary>
    public static class ItemFlyEffect
    {
        private const float SPAWN_INTERVAL = 0.15f;
        private const float MAX_TOTAL_SPAWN_TIME = 0.6f;
        private const float DURATION_MIN = 0.9f;
        private const float DURATION_MAX = 1.2f;
        private const float FALLBACK_ICON_SIZE = 180f;

        private static bool _poolRegistered;
        private static GameObject _prefab;
        private static Sprite _prefabDefaultSprite;

        private static readonly HashSet<GameObject> _activeItems = new HashSet<GameObject>();
        private static readonly Dictionary<GameObject, RectTransform> _rectCache = new Dictionary<GameObject, RectTransform>();
        private static readonly Dictionary<GameObject, Image> _imageCache = new Dictionary<GameObject, Image>();

        // 동시 활성 Play() 세션 수. 0↔N 전이 시점에만 OnActiveStateChanged 발화.
        // LobbyController 가 이 신호로 BtnPlay.interactable 을 게이팅한다 (SSOT).
        private static int _activeSessionCount;

        public static bool IsAnyActive => _activeSessionCount > 0;
        public static event Action<bool> OnActiveStateChanged;

        public static void Play(Sprite icon, Vector2 screenFrom, Vector2 screenTo, int count,
            Action onEachLand = null, Action onAllComplete = null)
        {
            if (count <= 0) { onAllComplete?.Invoke(); return; }
            if (!UIManager.HasInstance) { onAllComplete?.Invoke(); return; }

            Transform parent = GetParentTransform();
            if (parent == null) { onAllComplete?.Invoke(); return; }

            // [2026-06-24 사용자 피드백] Item_Fly SFX — session 1회당 1회 재생. early-return 통과 직후 = 실제 비행이 시작되는 시점. 절대 RunFly/Fly 코루틴 내부에서 호출 금지(잔향 중첩 회귀). 단일 진입점이므로 3개 caller(PurchaseRewardEffect/UIHud/PopupMoreLive) 모두 자동 커버. owner: ProjectHub 태스크 2026-06-24 익명 사용자 피드백.
            if (AudioManager.HasInstance) AudioManager.Instance.PlayItemFly();

            BeginSession();
            Action wrappedComplete = () =>
            {
                EndSession();
                onAllComplete?.Invoke();
            };

            EnsurePool();
            CoroutineRunner.Get().StartCoroutine(
                RunFly(parent, icon, screenFrom, screenTo, count, onEachLand, wrappedComplete));
        }

        private static void BeginSession()
        {
            _activeSessionCount++;
            if (_activeSessionCount == 1)
                OnActiveStateChanged?.Invoke(true);
        }

        private static void EndSession()
        {
            if (_activeSessionCount <= 0) return;
            _activeSessionCount--;
            if (_activeSessionCount == 0)
                OnActiveStateChanged?.Invoke(false);
        }

        private static Transform GetParentTransform()
        {
            if (!UIManager.HasInstance) return null;
            var ui = UIManager.Instance;
            return ui.EffectTr != null ? ui.EffectTr
                 : ui.PopupTr  != null ? ui.PopupTr
                 : ui.UiTr;
        }

        private static void EnsurePool()
        {
            if (_prefab == null)
            {
                _prefab = Resources.Load<GameObject>(Const.PREFAB_FXITEM);
                var prefabImage = _prefab != null ? _prefab.GetComponentInChildren<Image>(true) : null;
                _prefabDefaultSprite = prefabImage != null ? prefabImage.sprite : null;
            }

            if (_prefab == null)
            {
                Debug.LogError($"[ItemFlyEffect] {Const.PREFAB_FXITEM}.prefab not found in Resources.");
                return;
            }

            if (_poolRegistered || !ObjectPoolManager.HasInstance) return;
            if (!ObjectPoolManager.Instance.HasPool(Const.POOL_FXITEM))
                ObjectPoolManager.Instance.CreatePool(Const.POOL_FXITEM, _prefab, 24);
            _poolRegistered = true;
        }

        private static IEnumerator RunFly(Transform parent, Sprite icon,
            Vector2 fromScreen, Vector2 toScreen, int count,
            Action onEachLand, Action onAllComplete)
        {
            Canvas canvas = parent.GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera)
                ? canvas.worldCamera : null;
            RectTransform canvasRT = canvas != null ? canvas.GetComponent<RectTransform>() : null;

            Vector2 from = ScreenToLocal(canvasRT, cam, fromScreen);
            Vector2 to = ScreenToLocal(canvasRT, cam, toScreen);
            float cH = canvasRT != null ? canvasRT.rect.height : Screen.height;
            float cW = canvasRT != null ? canvasRT.rect.width : Screen.width;

            float spawnInterval = count > 1
                ? Mathf.Min(SPAWN_INTERVAL, MAX_TOTAL_SPAWN_TIME / (count - 1))
                : 0f;
            float scatterRadius = Mathf.Min(cW, cH) * 0.18f;
            int landed = 0;

            for (int i = 0; i < count; i++)
            {
                GameObject item = GetItemInstance(parent, icon, from);
                if (item == null)
                {
                    landed++;
                    onEachLand?.Invoke();
                    if (landed >= count) onAllComplete?.Invoke();
                    continue;
                }

                float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float radius = UnityEngine.Random.Range(scatterRadius * 0.5f, scatterRadius);
                Vector2 scatterDir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 scatterPos = from + scatterDir * radius;

                Vector2 mid = (scatterPos + to) * 0.5f;
                mid += scatterDir * UnityEngine.Random.Range(cW * 0.04f, cW * 0.1f);
                mid.y += UnityEngine.Random.Range(cH * 0.06f, cH * 0.15f);

                RectTransform rt = _rectCache[item];
                Image img = _imageCache[item];
                float dur = UnityEngine.Random.Range(DURATION_MIN, DURATION_MAX);
                CoroutineRunner.Get().StartCoroutine(
                    Fly(item, rt, img, from, scatterPos, mid, to, dur, () =>
                    {
                        landed++;
                        // [2026-06-24 사용자 피드백] Item_Get 단발 — FXItem 개별 도착마다 1회. per-arrival Fly() 완료 콜백 = 자연 1회(OnUpdate 람다 아님). HasInstance 가드로 Scene 전환/도메인 리로드 NRE 방지. onEachLand 이전 호출 = 사용자 콜백이 SFX 차단/지연시키지 않도록 격리.
                        if (AudioManager.HasInstance) AudioManager.Instance.PlayItemLand();
                        onEachLand?.Invoke();
                        if (landed >= count) onAllComplete?.Invoke();
                    }));

                if (i < count - 1)
                    yield return new WaitForSecondsRealtime(spawnInterval);
            }
        }

        private static GameObject GetItemInstance(Transform parent, Sprite icon, Vector2 from)
        {
            GameObject item = null;
            if (ObjectPoolManager.HasInstance && _poolRegistered && ObjectPoolManager.Instance.HasPool(Const.POOL_FXITEM))
                item = ObjectPoolManager.Instance.Get(Const.POOL_FXITEM);
            else if (_prefab != null)
                item = UnityEngine.Object.Instantiate(_prefab);

            if (item == null) return null;

            item.transform.SetParent(parent, false);
            item.transform.SetAsLastSibling();
            _activeItems.Add(item);

            if (!_rectCache.TryGetValue(item, out RectTransform rt) || rt == null)
            {
                rt = item.GetComponent<RectTransform>();
                if (rt == null) rt = item.AddComponent<RectTransform>();
                _rectCache[item] = rt;
            }

            if (!_imageCache.TryGetValue(item, out Image img) || img == null)
            {
                img = item.GetComponentInChildren<Image>(true);
                if (img == null) img = item.AddComponent<Image>();
                _imageCache[item] = img;
            }

            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = from;
            if (rt.sizeDelta.sqrMagnitude <= 1f)
                rt.sizeDelta = new Vector2(FALLBACK_ICON_SIZE, FALLBACK_ICON_SIZE);

            img.sprite = icon != null ? icon : (_prefabDefaultSprite != null ? _prefabDefaultSprite : img.sprite);
            img.color = Color.white;
            img.raycastTarget = false;
            img.preserveAspect = true;
            return item;
        }

        // 분산 이동 + 1→1.15→1 스케일 동시 진행 구간 (피크 허용 ±0.05)
        private const float SCATTER_SCALE_DURATION = 0.22f;
        private const float SCATTER_PEAK_SCALE = 1.15f;
        private const float SCATTER_PEAK_RATIO = 0.55f;
        // 도착 직전 마지막 10% 구간에서만 fade out 시작 (자연스러운 흡수 연출)
        private const float FADE_OUT_START = 0.90f;
        // 아이템별 launch 지연 상한 — 동일 spawnInterval 이후에도 미세하게 어긋나 겹침 방지
        private const float SCATTER_START_DELAY_MAX = 0.06f;

        private static IEnumerator Fly(GameObject item, RectTransform rt, Image img,
            Vector2 origin, Vector2 scatter, Vector2 mid, Vector2 target,
            float duration, Action onDone)
        {
            yield return new WaitForSecondsRealtime(UnityEngine.Random.Range(0f, SCATTER_START_DELAY_MAX));

            // ── Phase 0: 분산 + 스케일 오버슈트 동시 연출 (origin→scatter, 1→1.15→1) ──
            if (rt != null)
            {
                Vector3 baseScale = rt.localScale;
                Vector3 peakScale = baseScale * SCATTER_PEAK_SCALE;
                rt.anchoredPosition = origin;
                rt.localScale = baseScale;
                float sp = 0f;
                float peakTime = SCATTER_SCALE_DURATION * SCATTER_PEAK_RATIO;
                float settleTime = SCATTER_SCALE_DURATION - peakTime;
                while (sp < SCATTER_SCALE_DURATION)
                {
                    sp += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(sp / SCATTER_SCALE_DURATION);
                    float posEase = 1f - (1f - t) * (1f - t) * (1f - t);
                    rt.anchoredPosition = Vector2.Lerp(origin, scatter, posEase);
                    if (sp <= peakTime)
                    {
                        float k = Mathf.Clamp01(sp / peakTime);
                        k = 1f - (1f - k) * (1f - k);
                        rt.localScale = Vector3.LerpUnclamped(baseScale, peakScale, k);
                    }
                    else
                    {
                        float k = Mathf.Clamp01((sp - peakTime) / settleTime);
                        k = Mathf.SmoothStep(0f, 1f, k);
                        rt.localScale = Vector3.LerpUnclamped(peakScale, baseScale, k);
                    }
                    yield return null;
                }
                rt.anchoredPosition = scatter;
                rt.localScale = baseScale;
            }

            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float ease = t * t;
                float u = 1f - ease;
                rt.anchoredPosition = u * u * scatter + 2f * u * ease * mid + ease * ease * target;

                if (t > FADE_OUT_START && img != null)
                {
                    float ft = (t - FADE_OUT_START) / (1f - FADE_OUT_START);
                    float a = 1f - Mathf.SmoothStep(0f, 1f, ft);
                    img.color = new Color(1f, 1f, 1f, a);
                }
                yield return null;
            }

            _activeItems.Remove(item);
            onDone?.Invoke();

            if (ObjectPoolManager.HasInstance && ObjectPoolManager.Instance.HasPool(Const.POOL_FXITEM))
                ObjectPoolManager.Instance.Return(Const.POOL_FXITEM, item);
            else
                UnityEngine.Object.Destroy(item);
        }

        private static Vector2 ScreenToLocal(RectTransform canvasRT, Camera cam, Vector2 screen)
        {
            if (canvasRT != null &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screen, cam, out Vector2 local))
                return local;
            return screen;
        }
    }
}
