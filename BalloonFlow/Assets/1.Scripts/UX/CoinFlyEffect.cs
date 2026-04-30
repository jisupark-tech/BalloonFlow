using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// FxGold를 EffectCanvas에 직접 넣고 시작점→끝점 랜덤 포물선 비행.
    /// EffectCanvas (sortingOrder=15) 가 PopupCanvas (10) 위에 렌더되어 popup 으로 가려지지 않음.
    /// 연출 끝나면 ObjectPoolManager로 반환.
    /// </summary>
    public static class CoinFlyEffect
    {
        private const string PREFAB_PATH = "UI/UIAssets/FXGold";
        private const string POOL_KEY    = "FXGold";

        private static bool _poolRegistered;

        /// <summary>진행 중인 연출이 사용 중인 코인 인스턴스 집합. StopAll에서 한번에 반환.</summary>
        private static readonly HashSet<GameObject> _activeCoins = new HashSet<GameObject>();

        public static void Play(Vector2 screenFrom, Vector2 screenTo, int count,
            Action onEachLand = null, Action onAllComplete = null)
        {
            if (count <= 0) { onAllComplete?.Invoke(); return; }
            if (!UIManager.HasInstance || GetParentTransform() == null) return;

            EnsurePool();
            CoroutineRunner.Get().StartCoroutine(
                RunFly(screenFrom, screenTo, count, onEachLand, onAllComplete));
        }

        /// <summary>EffectCanvas 우선, 없으면 PopupCanvas, 그것도 없으면 UICanvas 로 fallback.</summary>
        private static Transform GetParentTransform()
        {
            if (!UIManager.HasInstance) return null;
            var ui = UIManager.Instance;
            return ui.EffectTr != null ? ui.EffectTr
                 : ui.PopupTr  != null ? ui.PopupTr
                 : ui.UiTr;
        }

        /// <summary>
        /// 진행 중인 모든 코인 연출 중단 + 활성 코인을 풀로 반환.
        /// 씬 전환 시 호출하여 잔여 연출/사운드 이어짐을 방지.
        /// </summary>
        public static void StopAll()
        {
            // CoroutineRunner에 걸린 모든 RunFly/Fly 코루틴 중단
            var runner = CoroutineRunner.GetIfExists();
            if (runner != null) runner.StopAllCoroutines();

            // 활성 코인 오브젝트를 풀로 반환 — Pool.Return 이 SetParent(_poolParent, false) 처리하므로 직접 detach 안 함
            // (worldPositionStays=true 기본값으로 detach 시 캔버스 스케일이 localScale 로 흡수되어 누적 증가 버그)
            if (ObjectPoolManager.HasInstance)
            {
                foreach (var coin in _activeCoins)
                {
                    if (coin == null) continue;
                    ObjectPoolManager.Instance.Return(POOL_KEY, coin);
                }
            }
            else
            {
                foreach (var coin in _activeCoins)
                    if (coin != null) coin.SetActive(false);
            }
            _activeCoins.Clear();
        }

        private static void EnsurePool()
        {
            if (_poolRegistered || !ObjectPoolManager.HasInstance) return;

            GameObject prefab = Resources.Load<GameObject>(PREFAB_PATH);
            if (prefab == null)
            {
                Debug.LogError($"[CoinFlyEffect] {PREFAB_PATH}.prefab not found in Resources.");
                return;
            }

            // 프리팹에 붙어있는 ParticleSystem 만 사용. UIParticleRenderer 자동 부착 제거.
            ObjectPoolManager.Instance.CreatePool(POOL_KEY, prefab, 28);
            _poolRegistered = true;
        }

        private static IEnumerator RunFly(Vector2 fromScreen, Vector2 toScreen, int count,
            Action onEachLand, Action onAllComplete)
        {
            Transform parent = GetParentTransform();
            if (parent == null) yield break;
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

            // 스폰: "연속적으로 날아가는" 느낌을 위해 모든 코인을 단일 프레임에 발사.
            // 자연스러운 도착 간격은 코인별 랜덤 flight duration으로 처리됨.
            // (minDelay/maxDelay는 하위 호환 보존용이지만 여기선 사용하지 않음)
            _ = minDelay; _ = maxDelay;

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
                _activeCoins.Add(coin);
                var rt = coin.GetComponent<RectTransform>();
                if (rt == null) rt = coin.AddComponent<RectTransform>();
                rt.anchoredPosition = from;
                // 프리팹의 localScale 그대로 사용 — 자식 Light 컴포넌트가 root 스케일 변동에 영향 안 받도록.
                // (코인 사이즈는 FXGold.prefab 에서 직접 조절)

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
                    Fly(coin, rt, from, scatterPos, mid, to, dur, () =>
                    {
                        landed++;
                        onEachLand?.Invoke();
                        if (landed >= count) onAllComplete?.Invoke();
                    }));
            }
            yield break;
        }

        /// <summary>
        /// 2단계 비행: 폭발(scatter) → 포물선 수렴(converge).
        /// Phase 1 (0~0.25): from → scatterPos (빠르게 퍼짐)
        /// Phase 2 (0.25~1.0): scatterPos → mid → to (베지어 포물선으로 목표 수렴)
        /// 스케일 변경은 Light 등 자식 컴포넌트에 영향 가니 안 함 — 위치만 애니메이트.
        /// </summary>
        private static IEnumerator Fly(GameObject coin, RectTransform rt,
            Vector2 origin, Vector2 scatter, Vector2 mid, Vector2 target,
            float duration, Action onDone)
        {
            float elapsed = 0f;
            // 스캐터 단계를 짧게 유지해 코인이 빠르게 타겟으로 수렴 (연속 비행 느낌).
            float scatterPhase = 0.08f;

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

                yield return null;
            }

            // Pool.Return 이 SetParent(_poolParent, worldPositionStays=false) 로 localScale 보존하며 분리.
            // 직접 SetParent(null) 호출하면 worldPositionStays=true(기본값) 라 캔버스 스케일이 localScale 로 흡수됨.
            _activeCoins.Remove(coin);
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
