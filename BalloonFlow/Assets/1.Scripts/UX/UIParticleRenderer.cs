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
        [Tooltip("ParticleSystem 월드 단위 → Canvas 픽셀 단위 보정 배율. " +
                 "ScreenSpaceOverlay에서 Start Size가 0.1처럼 작으면 먼지처럼 보임. " +
                 "기본 100배로 확대 (1 world = 100 pixel 가정).")]
        [SerializeField] private float _meshScale = 100f;

        private ParticleSystem _ps;
        private ParticleSystemRenderer _psr;
        private Mesh _bakedMesh;

        protected override void Awake()
        {
            base.Awake();
            _ps = GetComponent<ParticleSystem>();
            _psr = GetComponent<ParticleSystemRenderer>();
            _bakedMesh = new Mesh();

            // MaskableGraphic은 기본 raycastTarget=true → 밑에 깔린 버튼 클릭을 차단.
            // 파티클 비주얼은 입력을 받을 이유가 없으므로 항상 false.
            raycastTarget = false;

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
            if (_ps == null) return;
            // Loop가 켜진 파티클이면 isPlaying이 계속 true. 일회성 파티클은 끝나면 false.
            // 둘 다 지원 — 파티클이 있을 수도 있으니 isPlaying과 particleCount 둘 다 체크.
            if (!_ps.isPlaying && _ps.particleCount == 0) return;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            if (_ps == null || _psr == null) return;
            if (_ps.particleCount == 0) return;

            // 월드 공간 베이크 — 카메라 빌보드 + Simulation Space(World/Local) 모두 대응.
            _psr.BakeMesh(_bakedMesh, useTransform: true);

            if (_bakedMesh.vertexCount == 0) return;

            var verts = _bakedMesh.vertices;
            var colors = _bakedMesh.colors32;
            var uvs = _bakedMesh.uv;
            var indices = _bakedMesh.GetIndices(0);

            bool hasColors = colors != null && colors.Length == verts.Length;
            bool hasUVs = uvs != null && uvs.Length == verts.Length;

            // 월드 좌표 → Graphic의 RectTransform 로컬 좌표로 변환.
            // _meshScale: 파티클 Start Size가 작을 때 UI 픽셀 크기로 맞추기 위한 배율.
            RectTransform rt = rectTransform;
            float scale = _meshScale;

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 worldV = verts[i];
                Vector3 localV = rt.InverseTransformPoint(worldV);
                Vector3 finalPos = new Vector3(localV.x * scale, localV.y * scale, 0f);

                Color32 c = hasColors ? colors[i] : new Color32(255, 255, 255, 255);
                Vector2 uv = hasUVs ? uvs[i] : Vector2.zero;

                vh.AddVert(finalPos, c, uv);
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
