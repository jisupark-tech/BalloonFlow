using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// ParticleSystem을 ScreenSpaceOverlay Canvas에서 렌더링.
    /// ParticleSystemRenderer.BakeMesh()로 메시를 추출하여 CanvasRenderer로 전달.
    /// FxGold 등 파티클 기반 UI 이펙트용.
    /// 참고: https://github.com/mob-sakai/ParticleEffectForUGUI
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    [RequireComponent(typeof(CanvasRenderer))]
    public class UIParticleRenderer : MaskableGraphic
    {
        private ParticleSystem _ps;
        private ParticleSystemRenderer _psr;
        private Mesh _bakedMesh;

        protected override void Awake()
        {
            base.Awake();
            _ps = GetComponent<ParticleSystem>();
            _psr = GetComponent<ParticleSystemRenderer>();
            _bakedMesh = new Mesh();

            // 기본 ParticleSystemRenderer 비활성화 (UI로 대체)
            if (_psr != null) _psr.enabled = false;

            // 자식도 처리
            var childRenderers = GetComponentsInChildren<ParticleSystemRenderer>(true);
            foreach (var r in childRenderers) r.enabled = false;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_bakedMesh != null) DestroyImmediate(_bakedMesh);
        }

        private void LateUpdate()
        {
            if (_ps == null || !_ps.isPlaying) return;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            if (_ps == null || _psr == null) return;
            if (_ps.particleCount == 0) return;

            // 파티클 메시 베이크
            _psr.enabled = true;
            _psr.BakeMesh(_bakedMesh, useTransform: true);
            _psr.enabled = false;

            if (_bakedMesh.vertexCount == 0) return;

            var verts = _bakedMesh.vertices;
            var colors = _bakedMesh.colors32;
            var uvs = _bakedMesh.uv;
            var indices = _bakedMesh.GetIndices(0);

            // 색상 배열이 비어있으면 흰색으로
            bool hasColors = colors != null && colors.Length == verts.Length;
            bool hasUVs = uvs != null && uvs.Length == verts.Length;

            for (int i = 0; i < verts.Length; i++)
            {
                // BakeMesh는 카메라 기준 빌보드 → XY만 사용, Z=0 (UI 평면)
                Vector3 v = verts[i];
                Vector3 localPos = new Vector3(v.x, v.y, 0f);

                Color32 c = hasColors ? colors[i] : new Color32(255, 255, 255, 255);
                Vector2 uv = hasUVs ? uvs[i] : Vector2.zero;

                vh.AddVert(localPos, c, uv);
            }

            for (int i = 0; i < indices.Length; i += 3)
            {
                if (i + 2 < indices.Length)
                    vh.AddTriangle(indices[i], indices[i + 1], indices[i + 2]);
            }
        }

        public override Texture mainTexture
        {
            get
            {
                if (_psr != null && _psr.sharedMaterial != null)
                    return _psr.sharedMaterial.mainTexture;
                return Texture2D.whiteTexture;
            }
        }

        public override Material materialForRendering
        {
            get
            {
                if (_psr != null && _psr.sharedMaterial != null)
                    return _psr.sharedMaterial;
                return base.materialForRendering;
            }
        }
    }
}
