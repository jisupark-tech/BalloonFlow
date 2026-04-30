using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 이펙트 프리팹의 카메라/렌더 setup 을 아트팀이 직접 관리하기로 결정 (2026-04-30) —
    /// 런타임 UIParticleRenderer 자동 부착은 비활성. 호출은 호환성 위해 유지하지만 no-op.
    /// 필요한 prefab 만 인스펙터에서 UIParticleRenderer 부착할 것.
    /// </summary>
    public static class UIParticleBinder
    {
        public static void Bind(GameObject root)
        {
            // intentional no-op
        }
    }
}
