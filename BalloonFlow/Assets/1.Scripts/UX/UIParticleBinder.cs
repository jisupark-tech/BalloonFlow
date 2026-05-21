using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// ScreenSpaceOverlay Canvas 환경에서 raw ParticleSystemRenderer 는 카메라 렌더링 이후 그려지는
    /// UI 위로 절대 못 올라옴 (Unity 렌더 파이프라인 구조 한계). UIParticleRenderer 가
    /// ParticleSystem 의 BakedMesh 를 CanvasRenderer 의 vertex stream 으로 baking 하므로
    /// SSO Canvas batch 안에서 다른 UI graphic 과 같이 정렬돼 보임.
    ///
    /// 호출 측 (CoinFlyEffect / PopupResult / UILobby) 에서 spawn 직후 1회 호출.
    /// Idempotent — 이미 UIParticleRenderer 가 있는 ParticleSystem 은 skip.
    /// </summary>
    public static class UIParticleBinder
    {
        public static void Bind(GameObject root)
        {
            if (root == null) return;
            var particles = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                var ps = particles[i];
                if (ps == null) continue;
                if (ps.GetComponent<UIParticleRenderer>() != null) continue;
                ps.gameObject.AddComponent<UIParticleRenderer>();
            }
        }
    }
}
