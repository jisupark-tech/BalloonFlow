using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// FxGold를 EffectCanvas에 직접 넣고 시작점→끝점 랜덤 포물선 비행.
    /// EffectCanvas (sortingOrder=300) 가 PopupCanvas (200) / UICanvas (100) 위에 렌더되어 popup 으로 가려지지 않음.
    /// (UIManager.ConfigureSceneCanvas 가 강제 부여 — 2026-05-20 변경)
    /// 연출 끝나면 ObjectPoolManager로 반환.
    /// </summary>
    public static class CoinFlyEffect
    {
        private const string PREFAB_PATH = "UI/FXGold";
        private const string POOL_KEY    = "FXGold";

        private static bool _poolRegistered;
        private static GameObject _prefab;

        /// <summary>진행 중인 연출이 사용 중인 코인 인스턴스 집합. StopAll에서 한번에 반환.</summary>
        private static readonly HashSet<GameObject> _activeCoins = new HashSet<GameObject>();

        /// <summary>[Optimization 2026-05-11] 풀 GameObject → ParticleSystem[] 캐시 (매 spawn GetComponentsInChildren alloc 제거).</summary>
        private static readonly Dictionary<GameObject, ParticleSystem[]> _particleCache
            = new Dictionary<GameObject, ParticleSystem[]>();

        /// <summary>[Optimization 2026-05-11] 풀 GameObject → RectTransform 캐시 (매 spawn GetComponent 제거).</summary>
        private static readonly Dictionary<GameObject, RectTransform> _rectCache
            = new Dictionary<GameObject, RectTransform>();
        private static readonly Dictionary<GameObject, Vector3> _rootScaleCache
            = new Dictionary<GameObject, Vector3>();
        private static readonly Dictionary<GameObject, Vector2> _rootSizeDeltaCache
            = new Dictionary<GameObject, Vector2>();

        /// <summary>[2026-05-11] 코인 사이 spawn 인터벌 (사용자 요청 0.2f).</summary>
        private const float SPAWN_INTERVAL = 0.12f;

        /// <summary>[2026-05-11] count 많을 때 총 spawn 시간이 길어지지 않게 cap. (count - 1) × interval ≤ MAX_TOTAL_SPAWN_TIME.</summary>
        private const float MAX_TOTAL_SPAWN_TIME = 0.4f;

        public static void Play(Vector2 screenFrom, Vector2 screenTo, int count,
            Action onEachLand = null, Action onAllComplete = null)
        {
            if (count <= 0) { onAllComplete?.Invoke(); return; }
            if (!UIManager.HasInstance)
            {
                onAllComplete?.Invoke();
                return;
            }

            Transform parent = GetParentTransform();
            if (parent == null)
            {
                onAllComplete?.Invoke();
                return;
            }

            EnsurePool();
            // [2026-06-23 사용자 피드백] FXGold 등장 시 Gold_Appear 1회 재생. Common_Coin_Gain.mp3 대체.
            if (AudioManager.HasInstance) AudioManager.Instance.PlayGoldAppear();
            CoroutineRunner.Get().StartCoroutine(
                RunFly(parent, screenFrom, screenTo, count, onEachLand, onAllComplete));

            // [2026-05-12] 코인 흡수 동안 진동 4회 균등 분배 (intensity 0.3, duration 0.18s default).
            // count 1~3 코인 시도 4회 균등 분배 — count 의존 없는 일정한 햅틱 패턴.
            CoroutineRunner.Get().StartCoroutine(VibrateSequence(count, 4));
        }

        // [2026-05-12] 코인 흡수 진동 시퀀스 — 전체 spawn+tween 기간 동안 균등 분배.
        private static IEnumerator VibrateSequence(int count, int times)
        {
            if (times <= 0) yield break;

            // 총 진행 시간 추정 — (count-1) × spawn interval + 평균 tween duration
            float spawnInterval = count > 1 ? Mathf.Min(SPAWN_INTERVAL, MAX_TOTAL_SPAWN_TIME / (count - 1)) : 0f;
            float avgDur = 0.7f;
            if (GameManager.HasInstance)
            {
                var b = GameManager.Instance.Board;
                avgDur = (b.coinFlyDurationMin + b.coinFlyDurationMax) * 0.5f;
            }
            float totalDuration = Mathf.Max(spawnInterval * (count - 1) + avgDur, 0.4f);
            float interval = totalDuration / (times + 1);

            for (int i = 0; i < times; i++)
            {
                yield return new WaitForSeconds(interval);
                VibrationManager.VibrateDefault();
            }
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
            if (ObjectPoolManager.HasInstance && ObjectPoolManager.Instance.HasPool(POOL_KEY))
            {
                foreach (var coin in _activeCoins)
                {
                    if (coin == null) continue;
                    ResetPooledTransform(coin);
                    ObjectPoolManager.Instance.Return(POOL_KEY, coin);
                }
            }
            else
            {
                foreach (var coin in _activeCoins)
                    if (coin != null) UnityEngine.Object.Destroy(coin);
            }
            _activeCoins.Clear();
        }

        private static void EnsurePool()
        {
            if (_prefab == null)
                _prefab = Resources.Load<GameObject>(PREFAB_PATH);

            if (_prefab == null)
            {
                Debug.LogError($"[CoinFlyEffect] {PREFAB_PATH}.prefab not found in Resources.");
                return;
            }

            if (!ObjectPoolManager.HasInstance) return;

            // ROLLBACK_POPUP_RESULT_REWARD_FX_POOL_REPAIR
            // _poolRegistered is static, while ObjectPoolManager can be recreated between scenes.
            if (_poolRegistered && ObjectPoolManager.Instance.HasPool(POOL_KEY)) return;

            GameObject prefab = _prefab;

            if (!ObjectPoolManager.Instance.HasPool(POOL_KEY))
                ObjectPoolManager.Instance.CreatePool(POOL_KEY, prefab, 28);
            _poolRegistered = true;
        }

        private static IEnumerator RunFly(Transform parent, Vector2 fromScreen, Vector2 toScreen, int count,
            Action onEachLand, Action onAllComplete)
        {
            if (parent == null) yield break;
            Canvas canvas = parent.GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera)
                ? canvas.worldCamera : null;
            RectTransform canvasRT = canvas != null ? canvas.GetComponent<RectTransform>() : null;

            Vector2 from = ScreenToLocal(canvasRT, cam, fromScreen);
            Vector2 to = ScreenToLocal(canvasRT, cam, toScreen);
            float cH = canvasRT != null ? canvasRT.rect.height : Screen.height;
            float cW = canvasRT != null ? canvasRT.rect.width : Screen.width;

            float minDur = 0.3f, maxDur = 0.5f, minDelay = 0.01f, maxDelay = 0.02f;
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

            // [2026-05-11] spawn 인터벌 계산: 기본 0.2s. count 많으면 (count-1) × interval 이 MAX_TOTAL_SPAWN_TIME 초과 시 단축.
            // count 5 이하 → 0.2s 그대로 (총 0.8s 이하). count 11 → 0.2s (총 2.0s). count 50 → 약 0.04s (총 2.0s).
            float spawnInterval = count > 1
                ? Mathf.Min(SPAWN_INTERVAL, MAX_TOTAL_SPAWN_TIME / (count - 1))
                : 0f;

            // [2026-05-11] 순차 발사 변경 — 사용자 보고: "한꺼번에 뭉쳐서 날아감 → 순차적 + 빠른 텐션감".
            // 기존 단일 프레임 발사 → minDelay/maxDelay 사용 시간차 발사. 코인이 stagger 으로 흩어지며 날아감.
            // GameManager.Board 의 coinSpawnDelayMin/Max 로 인터벌 조정 가능.
            // 롤백: 아래 yield 라인 제거 + 주석 처리된 `_ = minDelay; _ = maxDelay;` 라인 복원.
            // 원본:
            // _ = minDelay; _ = maxDelay;

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

                GameObject coin = GetCoinInstance(parent);
                if (coin == null)
                {
                    landed++;
                    onEachLand?.Invoke();
                    if (landed >= count) onAllComplete?.Invoke();
                    continue;
                }
                // [Optimization 2026-05-11] RectTransform 캐시 — 매 spawn GetComponent 제거.
                // 원본: var rt = coin.GetComponent<RectTransform>(); if (rt == null) rt = coin.AddComponent<RectTransform>();
                if (!_rectCache.TryGetValue(coin, out RectTransform rt) || rt == null)
                {
                    rt = coin.GetComponent<RectTransform>();
                    if (rt == null) rt = coin.AddComponent<RectTransform>();
                    _rectCache[coin] = rt;
                }
                rt.anchoredPosition = from;
                // 프리팹의 localScale 그대로 사용 — 자식 Light 컴포넌트가 root 스케일 변동에 영향 안 받도록.
                // (코인 사이즈는 FXGold.prefab 에서 직접 조절)

                // [Optimization 2026-05-11] ParticleSystem[] 캐시 — 매 spawn GetComponentsInChildren alloc 제거.
                // 원본: var particles = coin.GetComponentsInChildren<ParticleSystem>(true);
                if (!_particleCache.TryGetValue(coin, out var particles)
                    || particles == null
                    || (particles.Length > 0 && particles[0] == null))
                {
                    particles = coin.GetComponentsInChildren<ParticleSystem>(true);
                    _particleCache[coin] = particles;
                }
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
                        // [2026-06-23 사용자 피드백] 첫 도착 시점 Gold_Get 연속 3회(코루틴). Common_Coin_Gain.mp3 대체.
                        if (landed == 1 && AudioManager.HasInstance) AudioManager.Instance.PlayGoldGet();
                        onEachLand?.Invoke();
                        if (landed >= count) onAllComplete?.Invoke();
                    }));

                // [2026-05-11] 코인 사이 spawn 인터벌 — count 비례 자동 조정 (RunFly 진입부에서 계산).
                if (i < count - 1)
                    yield return new WaitForSecondsRealtime(spawnInterval);
            }
            yield break;
        }

        private static GameObject GetCoinInstance(Transform parent)
        {
            GameObject coin = null;
            if (ObjectPoolManager.HasInstance && ObjectPoolManager.Instance.HasPool(POOL_KEY))
                coin = ObjectPoolManager.Instance.Get(POOL_KEY);
            else if (_prefab != null)
                coin = UnityEngine.Object.Instantiate(_prefab);

            if (coin == null) return null;

            CacheInitialTransformState(coin);
            coin.transform.SetParent(parent, false);
            ResetPooledTransform(coin);
            coin.transform.SetAsLastSibling();
            _activeCoins.Add(coin);
            return coin;
        }

        /// <summary>
        /// 2단계 비행: 폭발(scatter) → 포물선 수렴(converge).
        /// Phase 1 (0~0.25): from → scatterPos (빠르게 퍼짐)
        /// Phase 2 (0.25~1.0): scatterPos → mid → to (베지어 포물선으로 목표 수렴)
        /// 스케일 변경은 Light 등 자식 컴포넌트에 영향 가니 안 함 — 위치만 애니메이트.
        /// </summary>
        // [스케일 업 후 비행] 원점에서 작게→원래 크기로 팝 후 비행 (동시 X). 팝은 짧고, 이후 비행은 원래 스케일 유지.
        private const float SCALEUP_FROM = 0.3f;
        private const float SCALEUP_DURATION = 0.15f;

        private static IEnumerator Fly(GameObject coin, RectTransform rt,
            Vector2 origin, Vector2 scatter, Vector2 mid, Vector2 target,
            float duration, Action onDone)
        {
            // ── Phase 0: 스케일 업 (원점 고정, 비행 전) ──
            if (rt != null)
            {
                rt.anchoredPosition = origin;
                Vector3 baseScale = rt.localScale;
                Vector3 fromScale = baseScale * SCALEUP_FROM;
                rt.localScale = fromScale;
                float su = 0f;
                while (su < SCALEUP_DURATION)
                {
                    su += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(su / SCALEUP_DURATION);
                    k = 1f - (1f - k) * (1f - k); // OutQuad
                    rt.localScale = Vector3.LerpUnclamped(fromScale, baseScale, k);
                    yield return null;
                }
                rt.localScale = baseScale; // 비행 중엔 원래 스케일 (Light 등 자식 영향 최소화)
            }

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
            if (ObjectPoolManager.HasInstance && ObjectPoolManager.Instance.HasPool(POOL_KEY))
            {
                ResetPooledTransform(coin);
                ObjectPoolManager.Instance.Return(POOL_KEY, coin);
            }
            else
                UnityEngine.Object.Destroy(coin);
            onDone?.Invoke();
        }

        private static void CacheInitialTransformState(GameObject root)
        {
            if (root == null) return;

            if (!_rootScaleCache.ContainsKey(root))
                _rootScaleCache[root] = root.transform.localScale;

            if (root.transform is RectTransform rt && !_rootSizeDeltaCache.ContainsKey(root))
                _rootSizeDeltaCache[root] = rt.sizeDelta;
        }

        private static void ResetPooledTransform(GameObject root)
        {
            if (root == null) return;

            // ROLLBACK_FXGOLD_POOL_SCALE_RESET
            // Pool.Return preserves local transform values. Reset on reuse/return so Canvas scale
            // or tween side effects cannot accumulate across repeated pooled flights.
            Vector3 scale = _prefab != null
                ? _prefab.transform.localScale
                : _rootScaleCache.TryGetValue(root, out Vector3 cachedScale)
                    ? cachedScale
                    : Vector3.one;
            root.transform.localScale = scale;
            root.transform.localRotation = Quaternion.identity;

            if (root.transform is RectTransform rt)
            {
                rt.localScale = root.transform.localScale;
                rt.anchoredPosition = Vector2.zero;
                rt.localRotation = Quaternion.identity;
                if (_prefab != null && _prefab.transform is RectTransform prefabRt)
                    rt.sizeDelta = prefabRt.sizeDelta;
                else if (_rootSizeDeltaCache.TryGetValue(root, out Vector2 sizeDelta))
                    rt.sizeDelta = sizeDelta;
            }
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
