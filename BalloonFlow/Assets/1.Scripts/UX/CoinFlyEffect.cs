using System;
using System.Collections;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// FxGold를 PopupCanvas에 직접 넣고 시작점→끝점 랜덤 포물선 비행.
    /// UIParticleRenderer가 ParticleSystem을 Canvas UI로 렌더링.
    /// 연출 끝나면 ObjectPoolManager로 반환.
    /// </summary>
    public static class CoinFlyEffect
    {
        private const string POOL_KEY = "FXGold";
        private static bool _poolRegistered;

        public static void Play(Vector2 screenFrom, Vector2 screenTo, int count,
            Action onEachLand = null, Action onAllComplete = null)
        {
            if (count <= 0) { onAllComplete?.Invoke(); return; }
            if (!UIManager.HasInstance || UIManager.Instance.PopupTr == null) return;

            EnsurePool();
            CoroutineRunner.Get().StartCoroutine(
                RunFly(screenFrom, screenTo, count, onEachLand, onAllComplete));
        }

        private static void EnsurePool()
        {
            if (_poolRegistered || !ObjectPoolManager.HasInstance) return;
            var prefab = Resources.Load<GameObject>("UI/UIAssets/FXGold");
            if (prefab == null) return;

            // 프리팹에 UIParticleRenderer 추가 (없으면)
            if (prefab.GetComponent<UIParticleRenderer>() == null)
                prefab.AddComponent<UIParticleRenderer>();
            // 자식에도
            foreach (Transform child in prefab.transform)
            {
                if (child.GetComponent<ParticleSystem>() != null && child.GetComponent<UIParticleRenderer>() == null)
                    child.gameObject.AddComponent<UIParticleRenderer>();
            }

            ObjectPoolManager.Instance.CreatePool(POOL_KEY, prefab, 28);
            _poolRegistered = true;
        }

        private static IEnumerator RunFly(Vector2 fromScreen, Vector2 toScreen, int count,
            Action onEachLand, Action onAllComplete)
        {
            Transform parent = UIManager.Instance.PopupTr;
            Canvas canvas = parent.GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera)
                ? canvas.worldCamera : null;
            RectTransform canvasRT = canvas != null ? canvas.GetComponent<RectTransform>() : null;

            Vector2 from = ScreenToLocal(canvasRT, cam, fromScreen);
            Vector2 to = ScreenToLocal(canvasRT, cam, toScreen);
            float cH = canvasRT != null ? canvasRT.rect.height : Screen.height;
            float cW = canvasRT != null ? canvasRT.rect.width : Screen.width;

            float minDur = 0.5f, maxDur = 0.9f, minDelay = 0.01f, maxDelay = 0.04f;
            if (GameManager.HasInstance)
            {
                var b = GameManager.Instance.Board;
                minDur = b.coinFlyDurationMin;
                maxDur = b.coinFlyDurationMax;
                minDelay = b.coinSpawnDelayMin;
                maxDelay = b.coinSpawnDelayMax;
            }

            int landed = 0;
            float scatterRadius = Mathf.Min(cW, cH) * 0.25f;

            for (int i = 0; i < count; i++)
            {
                // 와르르 폭발: 360° 랜덤 방향으로 흩뿌림
                float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float radius = UnityEngine.Random.Range(scatterRadius * 0.4f, scatterRadius);
                Vector2 scatterDir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 scatterPos = from + scatterDir * radius;

                // 포물선 꼭짓점: scatter 방향 바깥쪽 + 위로 솟구침
                Vector2 mid = (scatterPos + to) * 0.5f;
                mid += scatterDir * UnityEngine.Random.Range(cW * 0.05f, cW * 0.15f);
                mid.y += UnityEngine.Random.Range(cH * 0.08f, cH * 0.25f);

                GameObject coin = ObjectPoolManager.Instance.Get(POOL_KEY);
                coin.transform.SetParent(parent, false);
                coin.transform.SetAsLastSibling();
                var rt = coin.GetComponent<RectTransform>();
                if (rt == null) rt = coin.AddComponent<RectTransform>();
                rt.anchoredPosition = from;
                float coinScale = GameManager.HasInstance ? GameManager.Instance.Board.coinFlyScale : 3f;
                rt.localScale = Vector3.one * coinScale;

                var particles = coin.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in particles)
                {
                    var main = ps.main;
                    main.startSpeed = 0f;
                    ps.Clear();
                    ps.Play();
                }

                float dur = UnityEngine.Random.Range(minDur, maxDur);

                CoroutineRunner.Get().StartCoroutine(
                    Fly(coin, rt, from, scatterPos, mid, to, dur, coinScale, () =>
                    {
                        landed++;
                        onEachLand?.Invoke();
                        if (landed >= count) onAllComplete?.Invoke();
                    }));

                yield return new WaitForSecondsRealtime(
                    UnityEngine.Random.Range(minDelay, maxDelay));
            }
        }

        /// <summary>
        /// 2단계 비행: 폭발(scatter) → 포물선 수렴(converge).
        /// Phase 1 (0~0.25): from → scatterPos (빠르게 퍼짐)
        /// Phase 2 (0.25~1.0): scatterPos → mid → to (베지어 포물선으로 목표 수렴)
        /// </summary>
        private static IEnumerator Fly(GameObject coin, RectTransform rt,
            Vector2 origin, Vector2 scatter, Vector2 mid, Vector2 target,
            float duration, float baseScale, Action onDone)
        {
            float elapsed = 0f;
            float scatterPhase = 0.2f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                if (t < scatterPhase)
                {
                    // Phase 1: 중앙에서 폭발 (EaseOut으로 빠르게 퍼짐)
                    float st = t / scatterPhase;
                    float ease = 1f - (1f - st) * (1f - st);
                    rt.anchoredPosition = Vector2.Lerp(origin, scatter, ease);
                }
                else
                {
                    // Phase 2: 포물선으로 목표 수렴 (Quadratic Bezier)
                    float ct = (t - scatterPhase) / (1f - scatterPhase);
                    float ease = ct * ct; // EaseIn — 처음 느리다 끝에 빨라짐 (도착감)
                    float u = 1f - ease;
                    rt.anchoredPosition = u * u * scatter + 2f * u * ease * mid + ease * ease * target;
                }

                // 도착 직전 스케일 축소 (골드 HUD에 흡수되는 느낌)
                if (t > 0.75f)
                    rt.localScale = Vector3.one * baseScale * Mathf.Lerp(1f, 0.3f, (t - 0.75f) / 0.25f);

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
