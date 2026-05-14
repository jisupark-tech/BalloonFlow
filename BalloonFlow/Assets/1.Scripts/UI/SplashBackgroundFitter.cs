using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 1:1(5:5) 스플래시 배경 Image를 부모 RectTransform에 항상 cover하는 최소 envelope으로 배치한다.
    ///
    /// 동작:
    ///  - 부모 rect의 긴 변(Max(width, height))을 기준으로 정사각형 sizeDelta를 계산 → 부모를 가득 채우는 최소 크기.
    ///  - 1:1 비율 보존(sizeDelta.x == sizeDelta.y), 중앙 정렬(anchor/pivot=0.5)로 대칭 크롭 → 잘림 최소화.
    ///  - <see cref="_scaleMultiplier"/>(1.0~1.5)는 디자이너가 추가 확대를 원할 때만 사용. 1.0 = 정확한 cover.
    ///
    /// 사용자 요구:
    ///  - 화면 전체를 항상 가득 채워야 한다(여백 금지) + 잘림은 대칭 중앙 크롭으로 최소화.
    ///
    /// 왜 Singleton이 아닌가:
    ///  - 본 컴포넌트는 단일 RectTransform에 부착되는 per-instance Fitter다. 전역 상태가 없으므로 Singleton 패턴 불필요.
    ///
    /// 주의:
    ///  - localScale은 절대 변경하지 않는다(자식이 있으면 전파됨). sizeDelta만 제어.
    ///  - 부착 대상은 반드시 leaf RectTransform이어야 한다(자식 Logo/LoadingBar 등에 영향 없도록).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class SplashBackgroundFitter : MonoBehaviour
    {
        [Tooltip("부모 rect의 긴 변에 곱할 확대 계수. 1.0 = 정확한 cover(envelope), >1.0 = 추가 확대.")]
        [SerializeField, Range(1f, 1.5f)] private float _scaleMultiplier = 1.0f;

        private RectTransform _rect;
        private RectTransform _parentRect;
        private Vector2 _lastParentSize;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _parentRect = _rect.parent as RectTransform;

            _rect.anchorMin = new Vector2(0.5f, 0.5f);
            _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0.5f, 0.5f);
            _rect.anchoredPosition = Vector2.zero;
        }

        private void OnEnable() { ApplyFit(); }
        private void Start() { ApplyFit(); }

        private void Update()
        {
            if (_parentRect == null) return;
            Vector2 cur = _parentRect.rect.size;
            if (cur != _lastParentSize) ApplyFit();
        }

        private void ApplyFit()
        {
            if (_rect == null) _rect = GetComponent<RectTransform>();
            if (_parentRect == null) _parentRect = _rect.parent as RectTransform;
            if (_parentRect == null) return;

            float pw = _parentRect.rect.width;
            float ph = _parentRect.rect.height;
            float side = Mathf.Max(pw, ph) * _scaleMultiplier;

            _rect.sizeDelta = new Vector2(side, side);
            _lastParentSize = new Vector2(pw, ph);
        }
    }
}
