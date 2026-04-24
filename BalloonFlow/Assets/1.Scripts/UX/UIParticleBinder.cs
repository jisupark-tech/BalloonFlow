using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// UI 계층 내부의 모든 ParticleSystem에 UIParticleRenderer를 런타임에 자동 부착.
    /// ScreenSpaceOverlay Canvas 위에서 파티클이 UI 레이어로 올바르게 렌더링되도록 보장.
    /// 이미 부착된 GameObject는 건너뜀(idempotent). 기존 계층/위치/부모는 변경하지 않는다.
    /// </summary>
    public static class UIParticleBinder
    {
        /// <summary>
        /// root 하위(자신 포함, 비활성 포함)의 모든 ParticleSystem GameObject에
        /// UIParticleRenderer가 없으면 AddComponent한다.
        /// ParticleSystemRenderer.enabled 제어는 UIParticleRenderer.Awake가 담당하므로 여기선 건드리지 않는다.
        /// </summary>
        public static void Bind(GameObject root)
        {
            if (root == null) return;

            var particles = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                var go = particles[i].gameObject;
                if (go.GetComponent<UIParticleRenderer>() == null)
                    go.AddComponent<UIParticleRenderer>();
            }
        }
    }
}
