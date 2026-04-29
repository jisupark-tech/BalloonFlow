using System.Collections;
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
        private const float EFFECT_Y = 2.2f;

        /// <summary>풍선 위치에 pop effect 재생. runner는 코루틴 호스트 (예: BalloonController).</summary>
        public static void Play(Vector3 worldPos, Color color, MonoBehaviour runner)
        {
            if (!ObjectPoolManager.HasInstance || runner == null) return;
            if (!ObjectPoolManager.Instance.HasPool(POOL_KEY)) return;

            // Y축 고정 (xz 는 풍선 위치 그대로).
            Vector3 spawnPos = new Vector3(worldPos.x, EFFECT_Y, worldPos.z);

            GameObject go = ObjectPoolManager.Instance.Get(POOL_KEY, spawnPos, Quaternion.identity);
            if (go == null) return;

            // prefab 대비 50% 축소.
            go.transform.localScale = Vector3.one * EFFECT_SCALE;

            // 모든 ParticleSystem에 색상 적용 + play. 가장 긴 life 시간 = 풀 반환 delay.
            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
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
            if (maxLife <= 0f) maxLife = 1.5f;

            runner.StartCoroutine(ReturnAfterDelay(go, maxLife));
        }

        private static IEnumerator ReturnAfterDelay(GameObject go, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (go == null) yield break;

            if (ObjectPoolManager.HasInstance && ObjectPoolManager.Instance.HasPool(POOL_KEY))
                ObjectPoolManager.Instance.Return(POOL_KEY, go);
            else
                go.SetActive(false);
        }
    }
}
