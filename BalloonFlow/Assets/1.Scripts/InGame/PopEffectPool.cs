using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 풍선 pop 이펙트 (CircleParticle.prefab) 풀 관리.
    /// 풍선이 터질 때 풀에서 가져와 색상 적용 + play, 연출 끝나면 풀로 반환.
    /// 이전: BalloonIdentifier에 _popEffect 자식으로 부착 → detach/reattach + color 적용 매번 → 부하.
    /// </summary>
    public static class PopEffectPool
    {
        public const string POOL_KEY = "CircleParticle";

        /// <summary>이펙트 크기 배율. prefab 기본 대비 50% (사용자 조정).</summary>
        private const float EFFECT_SCALE = 0.5f;

        /// <summary>이펙트 Y 좌표 고정 (카메라 시야 보이도록).</summary>
        private const float EFFECT_Y = 1f;

        /// <summary>CircleParticle active cap. 도달 시 LRU 정책으로 가장 오래된 active effect 를 강제 종료하여 신규 spawn 슬롯 확보 (silently drop 하지 않음).</summary>
        private const int MAX_ACTIVE_EFFECTS = 60;

        /// <summary>[Optimization 2026-05-10] 풀 GameObject → ParticleSystem[] 캐시.
        /// 매 pop 마다 GetComponentsInChildren 으로 배열 alloc 하던 부분 제거 (콤보 시 GC 압력 큰 폭 감소).
        /// 풀 재사용 시 동일 GameObject → 캐시 hit. stale (destroyed) 검출 시 자동 재 fetch.
        /// 롤백: 이 필드 제거 + Play() 의 캐시 분기 제거 + 주석 처리된 원본 라인 복원.</summary>
        private static readonly Dictionary<GameObject, ParticleSystem[]> _systemsCache = new Dictionary<GameObject, ParticleSystem[]>();

        /// <summary>Active effect 추적 (LRU). 헤드 = 가장 오래된 effect.</summary>
        private sealed class ActiveEntry
        {
            public GameObject go;
            public MonoBehaviour runner;
            public Coroutine returnCoroutine;

            public void Reset()
            {
                go = null;
                runner = null;
                returnCoroutine = null;
            }
        }
        private static readonly LinkedList<ActiveEntry> _activeEffects = new LinkedList<ActiveEntry>();

        // ROLLBACK_POP_EFFECT_GC_POOL:
        // Remove _entryPool/RentEntry/ReleaseEntry and instantiate ActiveEntry directly if this
        // bookkeeping ever conflicts with effect lifetime tracking. This only affects pooled visual
        // effects; pop/attack/miss decisions already happened before Play() is called.
        private static readonly Stack<ActiveEntry> _entryPool = new Stack<ActiveEntry>(MAX_ACTIVE_EFFECTS);

        // ROLLBACK_POP_EFFECT_WAIT_CACHE:
        // Replace GetWait(delay) with "new WaitForSeconds(delay)" if particle lifetime changes must
        // be observed per prefab edit at runtime. Current CircleParticle lifetime is stable, so this
        // removes repeated wait-object GC during pop bursts.
        private static readonly Dictionary<float, WaitForSeconds> _waitCache = new Dictionary<float, WaitForSeconds>(4);

        /// <summary>풍선 위치에 pop effect 재생. runner는 코루틴 호스트 (예: BalloonController).</summary>
        public static void Play(Vector3 worldPos, Color color, MonoBehaviour runner, float scaleMultiplier = 1f)
        {
            float __totalStamp = InGamePerfLogger.StartStampMs();
            if (!ObjectPoolManager.HasInstance || runner == null) return;
            if (!ObjectPoolManager.Instance.HasPool(POOL_KEY)) return;

            // LRU eviction: cap 도달 시 가장 오래된 active effect 를 즉시 풀 반환 → 신규 spawn 슬롯 확보.
            // 이전: 60 초과 시 silently return → 사용자가 큰 콤보에서 파티클이 안 보이는 결함.
            // ROLLBACK_POP_EFFECT_ACTIVE_CAP:
            // 아래 분기 + EvictOldestActive / _activeEffects 추적 제거하면 풀이 무제한 확장됨.
            if (_activeEffects.Count >= MAX_ACTIVE_EFFECTS)
                EvictOldestActive();

            // Y축 고정 (xz 는 풍선 위치 그대로).
            Vector3 spawnPos = new Vector3(worldPos.x, EFFECT_Y, worldPos.z);

            float __getStamp = InGamePerfLogger.StartStampMs();
            GameObject go = ObjectPoolManager.Instance.Get(POOL_KEY, spawnPos, Quaternion.identity);
            if (go == null) return;
            InGamePerfLogger.EndSection(__getStamp, "PopEffectPool.Get");

            // prefab 대비 50% 축소.
            // ROLLBACK_BALLOON_EFFECT_SCALE:
            // Reset pooled effect size every play and scale it with the popped balloon.
            go.transform.localScale = Vector3.one * EFFECT_SCALE * Mathf.Max(0.01f, scaleMultiplier);

            // 모든 ParticleSystem에 색상 적용 + play. 가장 긴 life 시간 = 풀 반환 delay.
            // [Optimization 2026-05-10] 풀 재사용 시 ParticleSystem 배열 캐시 → 매 pop 의 GetComponentsInChildren alloc 제거.
            // 롤백: 아래 캐시 분기 제거 + 주석 처리된 원본 라인 복원.
            // 원본: var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            float __systemsStamp = InGamePerfLogger.StartStampMs();
            if (!_systemsCache.TryGetValue(go, out var systems)
                || systems == null
                || (systems.Length > 0 && systems[0] == null))
            {
                systems = go.GetComponentsInChildren<ParticleSystem>(true);
                _systemsCache[go] = systems;
            }
            InGamePerfLogger.EndSection(__systemsStamp, "PopEffectPool.GetSystems");

            float __playStamp = InGamePerfLogger.StartStampMs();
            float maxLife = 0f;
            for (int i = 0; i < systems.Length; i++)
            {
                var ps = systems[i];
                var main = ps.main;
                main.startColor = color;
                main.loop = false;
                ps.Clear();
                ps.Play();
                float life = main.duration + main.startLifetime.constantMax;
                if (life > maxLife) maxLife = life;
            }
            InGamePerfLogger.EndSection(__playStamp, "PopEffectPool.PlaySystems");
            if (maxLife <= 0f) maxLife = 1.5f;

            float __trackStamp = InGamePerfLogger.StartStampMs();
            var entry = RentEntry();
            entry.go = go;
            entry.runner = runner;
            var node = _activeEffects.AddLast(entry);
            entry.returnCoroutine = runner.StartCoroutine(ReturnAfterDelay(node, maxLife));
            InGamePerfLogger.EndSection(__trackStamp, "PopEffectPool.TrackReturn");
            InGamePerfLogger.EndSection(__totalStamp, "PopEffectPool.Total");
        }

        private static ActiveEntry RentEntry()
        {
            return _entryPool.Count > 0 ? _entryPool.Pop() : new ActiveEntry();
        }

        private static void ReleaseEntry(ActiveEntry entry)
        {
            if (entry == null) return;
            entry.Reset();
            if (_entryPool.Count < MAX_ACTIVE_EFFECTS)
                _entryPool.Push(entry);
        }

        private static WaitForSeconds GetWait(float delay)
        {
            // Round to milliseconds so tiny float differences from particle modules do not grow the cache.
            float key = Mathf.Round(delay * 1000f) * 0.001f;
            if (!_waitCache.TryGetValue(key, out WaitForSeconds wait))
            {
                wait = new WaitForSeconds(key);
                _waitCache[key] = wait;
            }
            return wait;
        }

        /// <summary>가장 오래된 active effect 강제 종료. cap 도달 시 신규 spawn 슬롯 확보용.</summary>
        private static void EvictOldestActive()
        {
            var node = _activeEffects.First;
            if (node == null) return;
            var entry = node.Value;
            _activeEffects.RemoveFirst();

            // runner 가 살아있으면 코루틴 중단 (Unity 가 disable/destroy 시 자동 중단하므로 누락 호출 안전).
            if (entry.runner != null && entry.returnCoroutine != null)
                entry.runner.StopCoroutine(entry.returnCoroutine);

            if (entry.go != null)
            {
                if (ObjectPoolManager.HasInstance && ObjectPoolManager.Instance.HasPool(POOL_KEY))
                    ObjectPoolManager.Instance.Return(POOL_KEY, entry.go);
                else
                    entry.go.SetActive(false);
            }

            ReleaseEntry(entry);
        }

        private static IEnumerator ReturnAfterDelay(LinkedListNode<ActiveEntry> node, float delay)
        {
            yield return GetWait(delay);
            // LRU 로 이미 evict 됐으면 list 에서 빠져있음 → 중복 Return 방지.
            if (node.List != _activeEffects) yield break;
            _activeEffects.Remove(node);

            var entry = node.Value;
            if (entry.go == null)
            {
                ReleaseEntry(entry);
                yield break;
            }

            if (ObjectPoolManager.HasInstance && ObjectPoolManager.Instance.HasPool(POOL_KEY))
                ObjectPoolManager.Instance.Return(POOL_KEY, entry.go);
            else
                entry.go.SetActive(false);

            ReleaseEntry(entry);
        }
    }
}
