using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// [2026-05-15] Generic UI Image fly. PurchaseRewardEffect 에서 booster/life/infiniteHearts 도착 연출에 사용.
    /// CoinFlyEffect 와 동일 시각 패턴 (scatter → parabolic converge → target) 이지만 풀 미사용,
    /// Sprite 외부 주입. 보상 합산이 1~5개 수준이라 매 spawn alloc 부담 작음.
    /// </summary>
    public static class ItemFlyEffect
    {
        private const float SPAWN_INTERVAL = 0.15f;
        private const float MAX_TOTAL_SPAWN_TIME = 0.6f;
        private const float DURATION_MIN = 0.45f;
        private const float DURATION_MAX = 0.7f;
        private const float ICON_SIZE = 120f;

        public static void Play(Sprite icon, Vector2 screenFrom, Vector2 screenTo, int count,
            Action onEachLand = null, Action onAllComplete = null)
        {
            if (icon == null || count <= 0) { onAllComplete?.Invoke(); return; }
            if (!UIManager.HasInstance) { onAllComplete?.Invoke(); return; }

            Transform parent = UIManager.Instance.EffectTr != null
                ? UIManager.Instance.EffectTr
                : UIManager.Instance.PopupTr;
            if (parent == null) { onAllComplete?.Invoke(); return; }

            CoroutineRunner.Get().StartCoroutine(
                RunFly(parent, icon, screenFrom, screenTo, count, onEachLand, onAllComplete));
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
                float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float radius = UnityEngine.Random.Range(scatterRadius * 0.5f, scatterRadius);
                Vector2 scatterDir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 scatterPos = from + scatterDir * radius;

                Vector2 mid = (scatterPos + to) * 0.5f;
                mid += scatterDir * UnityEngine.Random.Range(cW * 0.04f, cW * 0.1f);
                mid.y += UnityEngine.Random.Range(cH * 0.06f, cH * 0.15f);

                var go = new GameObject("ItemFly", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(parent, false);
                go.transform.SetAsLastSibling();
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = from;
                rt.sizeDelta = new Vector2(ICON_SIZE, ICON_SIZE);
                var img = go.GetComponent<Image>();
                img.sprite = icon;
                img.raycastTarget = false;
                img.preserveAspect = true;

                float dur = UnityEngine.Random.Range(DURATION_MIN, DURATION_MAX);
                CoroutineRunner.Get().StartCoroutine(
                    Fly(go, rt, img, from, scatterPos, mid, to, dur, () =>
                    {
                        landed++;
                        onEachLand?.Invoke();
                        if (landed >= count) onAllComplete?.Invoke();
                    }));

                if (i < count - 1)
                    yield return new WaitForSecondsRealtime(spawnInterval);
            }
        }

        private static IEnumerator Fly(GameObject go, RectTransform rt, Image img,
            Vector2 origin, Vector2 scatter, Vector2 mid, Vector2 target,
            float duration, Action onDone)
        {
            float elapsed = 0f;
            float scatterPhase = 0.18f;
            Color baseColor = img.color;

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

                if (t > 0.85f)
                {
                    float a = 1f - (t - 0.85f) / 0.15f;
                    img.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
                }
                yield return null;
            }

            onDone?.Invoke();
            UnityEngine.Object.Destroy(go);
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
