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
        public override Material materialForRendering
        {
            get
            {
                var m = new Material(base.materialForRendering);
                m.SetInt("_StencilComp", (int)CompareFunction.NotEqual);
                return m;
            }
        }
    }
}
