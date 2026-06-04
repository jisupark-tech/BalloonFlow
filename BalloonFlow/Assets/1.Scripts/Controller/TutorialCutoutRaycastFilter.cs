using UnityEngine;
using UnityEngine.UI;

namespace AimedPuzzle.BalloonFlow.UI
{
    /// <summary>
    /// CutoutDim Image 에 부착되어 hole 영역 안쪽 raycast 를 통과시키는 필터.
    /// 정책: 하이라이트된 CutoutFrame(hole) 영역은 항상 클릭 가능, dim 영역은 항상 차단.
    /// SetGraceActive(true) 는 hole 이 없는 step(useCutoutFrame=false)에서만 의미가 있으며,
    /// 그 경우 튜토리얼 등장 직후 입력 차단 grace 로 dim 전체를 흡수한다.
    /// hole 이 있는 step 에서는 grace 여부와 무관하게 hole 내부는 pass-through.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public class TutorialCutoutRaycastFilter : MonoBehaviour, ICanvasRaycastFilter
    {
        private RectTransform _maskRect;
        private bool _hasHole;
        private Vector2 _holeLocalCenter;
        private Vector2 _holeSize;
        private bool _graceActive;

        public void Initialize(RectTransform maskRect)
        {
            _maskRect = maskRect;
        }

        public void SetHole(Vector2 localCenter, Vector2 size)
        {
            _holeLocalCenter = localCenter;
            _holeSize = size;
            _hasHole = size.x > 0f && size.y > 0f;
        }

        public void ClearHole()
        {
            _hasHole = false;
            _holeSize = Vector2.zero;
        }

        public void SetGraceActive(bool active)
        {
            _graceActive = active;
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            // hole 이 있는 step: grace 여부와 무관하게 hole 내부 좌표는 pass-through, 외부는 차단.
            if (_hasHole && _maskRect != null)
            {
                Vector2 localPoint;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_maskRect, screenPoint, eventCamera, out localPoint))
                    return true;

                float halfW = _holeSize.x * 0.5f;
                float halfH = _holeSize.y * 0.5f;
                if (localPoint.x >= _holeLocalCenter.x - halfW && localPoint.x <= _holeLocalCenter.x + halfW &&
                    localPoint.y >= _holeLocalCenter.y - halfH && localPoint.y <= _holeLocalCenter.y + halfH)
                    return false;

                return true;
            }

            // hole 없는 step: grace 활성 시 dim 전체 차단.
            return true;
        }
    }
}
