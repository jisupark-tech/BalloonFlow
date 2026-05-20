using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

namespace BalloonFlow
{
    /// <summary>
    /// "Hole in UI" 표준 패턴.
    /// Mask 컴포넌트와 함께 사용 — 자식이 이 Image 의 사각형 영역 "바깥" 에만 그려짐 (구멍 펀칭).
    /// 사용법:
    ///   - 이 컴포넌트를 가진 GameObject 에 Mask 컴포넌트 추가 (showMaskGraphic 은 무관)
    ///   - 펀칭할 사각형 크기로 RectTransform 설정
    ///   - 자식으로 DimOverlay(Image, 전체화면) 배치 → 그 자식이 이 사각형 영역 밖에만 그려져 dim 효과
    /// 참고: youtube.com/watch?v=2BKKTFIueZw
    /// </summary>
    public class CutoutMaskUI : Image
    {
        // Material cache — 매 frame new Material() 호출되던 문제 fix.
        // base.materialForRendering 이 동일하면 cache 재사용. shader 변경 시 새로 생성.
        private Material _cachedMat;
        private Material _cachedBaseMat;

        public override Material materialForRendering
        {
            get
            {
                Material baseMat = base.materialForRendering;
                // [Defense 2026-05-11] baseMat null 방어 — Image 가 destroy 직전 또는 mat 없는 상태 가능.
                if (baseMat == null) return _cachedMat; // 이전 cache 있으면 그것, 없으면 null

                if (_cachedMat == null || _cachedBaseMat != baseMat)
                {
                    if (_cachedMat != null) DestroyImmediate(_cachedMat);
                    _cachedMat = new Material(baseMat);
                    // ROLLBACK_USEITEM_CUTOUT_WRITER_STENCIL:
                    // CutoutMaskUI is the stencil writer. It must always stamp the cutout
                    // area before the dim image renders with NotEqual.
                    _cachedMat.SetInt("_Stencil", 1);
                    _cachedMat.SetInt("_StencilComp", (int)CompareFunction.Always);
                    _cachedMat.SetInt("_StencilOp", (int)StencilOp.Replace);
                    _cachedMat.SetInt("_StencilReadMask", 255);
                    _cachedMat.SetInt("_StencilWriteMask", 255);
                    _cachedMat.SetInt("_ColorMask", 0);
                    _cachedBaseMat = baseMat;
                }
                return _cachedMat;
            }
        }

        protected override void OnDestroy()
        {
            if (_cachedMat != null)
            {
                DestroyImmediate(_cachedMat);
                _cachedMat = null;
            }
            base.OnDestroy();
        }
    }
}
