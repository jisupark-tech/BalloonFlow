using System;
using System.Collections;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// FXGold를 PopupCanvas 자식의 ScreenSpaceCamera Canvas에서 비행.
    /// 연출 끝나면 ObjectPoolManager로 반환.
    /// </summary>
    public static class CoinFlyEffect
    {
        private const string POOL_KEY = "FXGold";
        private static bool _poolRegistered;
        private static Transform _fxLayer;

        public static void Play(Vector2 screenFrom, Vector2 screenTo, int count,
            Action onEachLand = null, Action onAllComplete = null)
        {
            if (count <= 0) { onAllComplete?.Invoke(); return; }
            if (!UIManager.HasInstance || UIManager.Instance.PopupTr == null) return;

            EnsurePool();
            EnsureFXLayer();
            if (_fxLayer == null) return;

            CoroutineRunner.Get().StartCoroutine(
                RunFly(screenFrom, screenTo, count, onEachLand, onAllComplete));
        }

        private static void EnsurePool()
        {
            if (_poolRegistered || !ObjectPoolManager.HasInstance) return;
            var prefab = Resources.Load<GameObject>("Prefabs/FxGold");
            if (prefab == null) return;
            ObjectPoolManager.Instance.CreatePool(POOL_KEY, prefab, 8);
            _poolRegistered = true;
        }

        private static void EnsureFXLayer()
        {
            if (_fxLayer != null) return;

            var go = new GameObject("FXGoldLayer");
            go.transform.SetParent(UIManager.Instance.transform, false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = CameraManager.HasInstance ? CameraManager.Instance.UICamera : Camera.main;
            canvas.sortingOrder = 100;
            canvas.planeDistance = 100f;

            // CanvasScaler 추가 (스케일 정상화)
            var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1242f, 2688f);
            scaler.matchWidthOrHeight = 0.5f;

            var cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            _fxLayer = go.transform;
        }

        private static IEnumerator RunFly(Vector2 fromScreen, Vector2 toScreen, int count,
            Action onEachLand, Action onAllComplete)
        {
            Canvas canvas = _fxLayer.GetComponent<Canvas>();
            RectTransform canvasRT = canvas != null ? canvas.GetComponent<RectTransform>() : null;
            Camera cam = canvas != null ? canvas.worldCamera : null;

            Vector2 from = ScreenToLocal(canvasRT, cam, fromScreen);
            Vector2 to = ScreenToLocal(canvasRT, cam, toScreen);
            float cH = canvasRT != null ? canvasRT.rect.height : Screen.height;
            float cW = canvasRT != null ? canvasRT.rect.width : Screen.width;

            float minDur = 0.4f, maxDur = 0.7f, minDelay = 0.03f, maxDelay = 0.08f;
            if (GameManager.HasInstance)
            {
                var b = GameManager.Instance.Board;
                minDur = b.coinFlyDurationMin;
                maxDur = b.coinFlyDurationMax;
                minDelay = b.coinSpawnDelayMin;
                maxDelay = b.coinSpawnDelayMax;
            }

            int landed = 0;

            for (int i = 0; i < count; i++)
            {
                Vector2 start = from + new Vector2(
                    UnityEngine.Random.Range(-40f, 40f),
                    UnityEngine.Random.Range(-40f, 40f));

                Vector2 mid = (start + to) * 0.5f;
                mid.y += UnityEngine.Random.Range(cH * 0.1f, cH * 0.25f);
                mid.x += UnityEngine.Random.Range(-cW * 0.1f, cW * 0.1f);

                GameObject coin = ObjectPoolManager.Instance.Get(POOL_KEY);
                coin.transform.SetParent(_fxLayer, false);
                var rt = coin.GetComponent<RectTransform>();
                if (rt == null) rt = coin.AddComponent<RectTransform>();
                rt.anchoredPosition = start;
                rt.localScale = Vector3.one;

                float dur = UnityEngine.Random.Range(minDur, maxDur);

                CoroutineRunner.Get().StartCoroutine(Fly(coin, rt, start, mid, to, dur, () =>
                {
                    landed++;
                    onEachLand?.Invoke();
                    if (landed >= count) onAllComplete?.Invoke();
                }));

                yield return new WaitForSecondsRealtime(
                    UnityEngine.Random.Range(minDelay, maxDelay));
            }
        }

        private static IEnumerator Fly(GameObject coin, RectTransform rt,
            Vector2 a, Vector2 b, Vector2 c, float duration, Action onDone)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float e = 1f - (1f - t) * (1f - t);
                float u = 1f - e;
                rt.anchoredPosition = u * u * a + 2f * u * e * b + e * e * c;
                if (t > 0.7f)
                    rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.3f, (t - 0.7f) / 0.3f);
                yield return null;
            }

            rt.localScale = Vector3.one;
            coin.transform.SetParent(null);
            if (ObjectPoolManager.HasInstance)
                ObjectPoolManager.Instance.Return(POOL_KEY, coin);
            else
                coin.SetActive(false);
            onDone?.Invoke();
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
