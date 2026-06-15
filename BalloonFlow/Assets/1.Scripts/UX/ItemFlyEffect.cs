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

        public static void Play(Sprite icon, Vector2 screenFrom, Vector2 screenTo, int count,
            Action onEachLand = null, Action onAllComplete = null)
        {
            if (count <= 0) { onAllComplete?.Invoke(); return; }
            if (!UIManager.HasInstance) { onAllComplete?.Invoke(); return; }

            Transform parent = GetParentTransform();
            if (parent == null) { onAllComplete?.Invoke(); return; }

            EnsurePool();
            CoroutineRunner.Get().StartCoroutine(
                RunFly(parent, icon, screenFrom, screenTo, count, onEachLand, onAllComplete));
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

        // [스케일 업 후 비행] 원점에서 0→피크→1로 오버슈트 팝(in place) 한 뒤 비행 시작 (스케일업·비행 동시 X).
        private const float SCALEUP_DURATION = 0.22f;
        private const float SCALEUP_PEAK_SCALE = 1.2f;
        private const float SCALEUP_PEAK_RATIO = 0.55f;
        private const float SCALEUP_SETTLE_HOLD = 0.08f; // 스케일 1 도달 후 정착 hold (0.05~0.1초 범위 — 짧고 경쾌)
        private const float FADE_OUT_START = 0.85f;

        private static IEnumerator Fly(GameObject item, RectTransform rt, Image img,
            Vector2 origin, Vector2 scatter, Vector2 mid, Vector2 target,
            float duration, Action onDone)
        {
            // ── Phase 0: 스케일 등장 (원점 고정, 0 → 1.2 → 1 오버슈트) ──
            if (rt != null)
            {
                rt.anchoredPosition = origin;
                Vector3 baseScale = rt.localScale;
                Vector3 peakScale = baseScale * SCALEUP_PEAK_SCALE;
                rt.localScale = Vector3.zero;
                float su = 0f;
                float peakTime = SCALEUP_DURATION * SCALEUP_PEAK_RATIO;
                float settleTime = SCALEUP_DURATION - peakTime;
                while (su < SCALEUP_DURATION)
                {
                    su += Time.unscaledDeltaTime;
                    if (su <= peakTime)
                    {
                        // Phase A: 0 → 1.2 (빠른 팝, OutQuad)
                        float k = Mathf.Clamp01(su / peakTime);
                        k = 1f - (1f - k) * (1f - k);
                        rt.localScale = Vector3.LerpUnclamped(Vector3.zero, peakScale, k);
                    }
                    else
                    {
                        // Phase B: 1.2 → 1 (자연스러운 세틀, SmoothStep)
                        float k = Mathf.Clamp01((su - peakTime) / settleTime);
                        k = Mathf.SmoothStep(0f, 1f, k);
                        rt.localScale = Vector3.LerpUnclamped(peakScale, baseScale, k);
                    }
                    yield return null;
                }
                rt.localScale = baseScale;
                if (SCALEUP_SETTLE_HOLD > 0f)
                    yield return new WaitForSecondsRealtime(SCALEUP_SETTLE_HOLD);
            }

            float elapsed = 0f;
            const float scatterPhase = 0.18f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                if (t < scatterPhase)
                {
                    float st = t / scatterPhase;
                    float ease = 1f - (1f - st) * (1f - st);
                    rt.anchoredPosition = Vector2.Lerp(origin, scatter, ease);
                }
                else
                {
                    float ct = (t - scatterPhase) / (1f - scatterPhase);
                    float ease = ct * ct;
                    float u = 1f - ease;
                    rt.anchoredPosition = u * u * scatter + 2f * u * ease * mid + ease * ease * target;
                }

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
