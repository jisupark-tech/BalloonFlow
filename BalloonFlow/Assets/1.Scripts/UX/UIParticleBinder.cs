using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 이펙트 프리팹의 카메라/렌더 setup 은 아트팀이 prefab inspector 에서 직접 관리하기로 결정.
    /// 런타임 UIParticleRenderer 자동 부착은 비활성 — 필요한 ParticleSystem 마다 인스펙터에서
    /// UIParticleRenderer 추가 + _meshScale 적정값 설정 + Material._MainTex 가 standalone texture 인지 확인.
    /// 호출 호환성 위해 메서드 유지하지만 no-op.
    /// </summary>
    public static class UIParticleBinder
    {
        public static void Bind(GameObject root)
        {
            // intentional no-op
        }
    }
}
