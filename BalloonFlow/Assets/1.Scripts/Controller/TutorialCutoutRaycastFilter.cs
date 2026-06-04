using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// CutoutDim Image 에 부착되어 hole 영역 안쪽 raycast 를 통과시키는 필터.
    /// 외부=차단(=raycast 적중 → 입력 흡수), 내부=하위 UI/게임 오브젝트로 클릭 전달.
    /// SetGraceActive(true) 동안엔 hole 까지 포함 전체 차단 — 튜토리얼 등장 직후 입력 차단 grace 용.
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
            // grace 중이면 hole 포함 전체 차단.
            if (_graceActive) return true;
            if (!_hasHole || _maskRect == null) return true;

            Vector2 localPoint;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_maskRect, screenPoint, eventCamera, out localPoint))
                return true;

            // hole 내부면 raycast pass-through (= false → 입력이 하위로 전달).
            float halfW = _holeSize.x * 0.5f;
            float halfH = _holeSize.y * 0.5f;
            if (localPoint.x >= _holeLocalCenter.x - halfW && localPoint.x <= _holeLocalCenter.x + halfW &&
                localPoint.y >= _holeLocalCenter.y - halfH && localPoint.y <= _holeLocalCenter.y + halfH)
                return false;

            return true;
        }
    }
}
