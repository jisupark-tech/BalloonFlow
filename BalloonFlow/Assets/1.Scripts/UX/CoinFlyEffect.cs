using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// Reusable coin fly effect. Spawns coin images that fly from a source position
    /// to a target UI element with parabolic arcs and staggered timing for a "pouring" feel.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: UX | Phase: 1
    /// </remarks>
    public class CoinFlyEffect : MonoBehaviour
    {
        #region Constants

        private const float MIN_SPAWN_DELAY = 0.03f;
        private const float MAX_SPAWN_DELAY = 0.08f;
        private const float MIN_FLIGHT_DURATION = 0.5f;
        private const float MAX_FLIGHT_DURATION = 0.8f;
        private const float MIN_ARC_HEIGHT = 100f;
        private const float MAX_ARC_HEIGHT = 250f;
        private const float START_OFFSET_RANGE = 40f;
        private const float CONTROL_POINT_X_RANGE = 80f;
        private const float COIN_SIZE = 32f;
        private const float SCALE_DOWN_THRESHOLD = 0.7f;
        private const float SCALE_DOWN_MIN = 0.3f;

        #endregion

        #region Public — Create & Play

        /// <summary>
        /// Creates a CoinFlyEffect instance under the given canvas.
        /// The instance self-destructs after all coins land.
        /// </summary>
        public static CoinFlyEffect Create(Canvas canvas)
        {
            if (canvas == null)
            {
                Debug.LogWarning("[CoinFlyEffect] Canvas is null, cannot create effect.");
                return null;
            }

            var go = new GameObject("CoinFlyEffect");
            go.transform.SetParent(canvas.transform, false);
            return go.AddComponent<CoinFlyEffect>();
        }

        /// <summary>
        /// Plays the coin fly effect.
        /// </summary>
        /// <param name="screenFrom">Start position in screen space.</param>
        /// <param name="target">Target RectTransform (e.g., Gold counter).</param>
        /// <param name="coinCount">Number of coins to spawn.</param>
        /// <param name="onEachLand">Callback fired each time a coin lands (for incrementing display).</param>
        /// <param name="onAllComplete">Callback fired when all coins have landed.</param>
        public void Play(Vector2 screenFrom, RectTransform target, int coinCount,
            Action onEachLand = null, Action onAllComplete = null)
        {
            if (target == null || coinCount <= 0)
            {
                onAllComplete?.Invoke();
                Destroy(gameObject, 0.1f);
                return;
            }

            StartCoroutine(SpawnCoinsCoroutine(screenFrom, target, coinCount, onEachLand, onAllComplete));
        }

        #endregion

        #region Private — Spawn & Fly

        private IEnumerator SpawnCoinsCoroutine(Vector2 from, RectTransform target, int count,
            Action onEachLand, Action onAllComplete)
        {
            int landed = 0;

            for (int i = 0; i < count; i++)
            {
                Vector2 startOffset = new Vector2(
                    UnityEngine.Random.Range(-START_OFFSET_RANGE, START_OFFSET_RANGE),
                    UnityEngine.Random.Range(-START_OFFSET_RANGE, START_OFFSET_RANGE));
                Vector2 startPos = from + startOffset;

                // Create coin image
                var coinGO = CreateCoinObject();
                var rt = coinGO.GetComponent<RectTransform>();
                rt.position = startPos;

                // Random flight parameters
                float duration = UnityEngine.Random.Range(MIN_FLIGHT_DURATION, MAX_FLIGHT_DURATION);
                float arcHeight = UnityEngine.Random.Range(MIN_ARC_HEIGHT, MAX_ARC_HEIGHT);

                StartCoroutine(FlyCoinCoroutine(rt, startPos, target, duration, arcHeight, () =>
                {
                    landed++;
                    onEachLand?.Invoke();
                    Destroy(coinGO);

                    if (landed >= count)
                    {
                        onAllComplete?.Invoke();
                        Destroy(gameObject, 0.1f);
                    }
                }));

                // Staggered delay for "pouring" feel
                yield return new WaitForSeconds(
                    UnityEngine.Random.Range(MIN_SPAWN_DELAY, MAX_SPAWN_DELAY));
            }
        }

        private GameObject CreateCoinObject()
        {
            var coinGO = new GameObject("Coin", typeof(RectTransform), typeof(Image));
            coinGO.transform.SetParent(transform, false);

            var rt = coinGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(COIN_SIZE, COIN_SIZE);

            var img = coinGO.GetComponent<Image>();
            // Gold coin color with a slight warm tint
            img.color = new Color(1f, 0.85f, 0.1f);
            img.raycastTarget = false;

            // Make it circular by creating a simple round sprite at runtime
            img.sprite = CreateCircleSprite();

            return coinGO;
        }

        private IEnumerator FlyCoinCoroutine(RectTransform coin, Vector2 from,
            RectTransform target, float duration, float arcHeight, Action onLand)
        {
            float elapsed = 0f;
            Vector2 to = target.position;

            // Control point for quadratic bezier (above midpoint with horizontal randomness)
            Vector2 mid = (from + to) * 0.5f + Vector2.up * arcHeight;
            mid.x += UnityEngine.Random.Range(-CONTROL_POINT_X_RANGE, CONTROL_POINT_X_RANGE);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // Ease-out quadratic
                float eased = 1f - (1f - t) * (1f - t);

                // Quadratic bezier position
                coin.position = QuadBezier(from, mid, to, eased);

                // Scale down + fade near the end
                if (t > SCALE_DOWN_THRESHOLD)
                {
                    float scaleFactor = Mathf.Lerp(1f, SCALE_DOWN_MIN,
                        (t - SCALE_DOWN_THRESHOLD) / (1f - SCALE_DOWN_THRESHOLD));
                    coin.localScale = Vector3.one * scaleFactor;
                }

                yield return null;
            }

            onLand?.Invoke();
        }

        private static Vector2 QuadBezier(Vector2 a, Vector2 b, Vector2 c, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * b + t * t * c;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Creates a simple filled circle sprite at runtime (32x32 white circle).
        /// Cached statically so it is only generated once.
        /// </summary>
        private static Sprite _cachedCircleSprite;

        private static Sprite CreateCircleSprite()
        {
            if (_cachedCircleSprite != null) return _cachedCircleSprite;

            const int size = 32;
            const float radius = size * 0.5f;
            const float radiusSq = radius * radius;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - radius + 0.5f;
                    float dy = y - radius + 0.5f;
                    float distSq = dx * dx + dy * dy;

                    if (distSq <= radiusSq)
                    {
                        // Slight edge anti-aliasing
                        float edgeDist = radius - Mathf.Sqrt(distSq);
                        float alpha = Mathf.Clamp01(edgeDist * 2f);
                        pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                    }
                    else
                    {
                        pixels[y * size + x] = Color.clear;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            _cachedCircleSprite = Sprite.Create(tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                100f);

            return _cachedCircleSprite;
        }

        #endregion
    }
}
