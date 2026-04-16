using UnityEngine;
using UnityEngine.EventSystems;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// 버튼 누를 때 스케일 축소 → 놓을 때 복원. 탠션감 연출.
    /// Button이 있는 GameObject에 추가.
    /// </summary>
    public class ButtonScaleEffect : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private float _pressScale = 0.9f;
        [SerializeField] private float _pressDuration = 0.08f;
        [SerializeField] private float _releaseDuration = 0.12f;

        private Vector3 _originalScale;
        private Tweener _tween;

        private void Awake()
        {
            _originalScale = transform.localScale;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _tween?.Kill();
            _tween = transform.DOScale(_originalScale * _pressScale, _pressDuration)
                .SetEase(Ease.InQuad)
                .SetUpdate(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _tween?.Kill();
            _tween = transform.DOScale(_originalScale, _releaseDuration)
                .SetEase(Ease.OutBack)
                .SetUpdate(true);
        }

        private void OnDisable()
        {
            _tween?.Kill();
            transform.localScale = _originalScale;
        }
    }
}
