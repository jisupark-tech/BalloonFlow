using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 1:1(5:5) 스플래시 배경 Image를 부모 RectTransform에 'fit + 축소' 방식으로 배치한다.
    ///
    /// 동작:
    ///  - 부모 rect의 짧은 변(Min(width, height))을 기준으로 정사각형 sizeDelta를 계산.
    ///  - 추가로 <see cref="_scaleMultiplier"/>(0.5~1.0)를 곱해 더 작게 표시 → 풍선 영역이 잘리지 않고 여백 노출.
    ///
    /// 왜 envelope(가득 채우기)가 아닌 fit(축소 우선)인가:
    ///  - 풍선이 화면 가장자리에서 잘리면 안 된다(아트 디렉션). 항상 전부 보이도록 축소를 선택.
    ///
    /// 왜 scaleMultiplier가 있는가:
    ///  - 디자이너/사용자가 "기본 fit보다 더 작게" 보이기를 요구할 때 인스펙터 한 줄로 조절하기 위함.
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
        private const float SOURCE_ASPECT = 1f;

        [Tooltip("부모 rect의 짧은 변에 곱할 축소 계수. 1.0 = fit 그대로, <1.0 = 더 작게.")]
        [SerializeField, Range(0.5f, 1f)] private float _scaleMultiplier = 0.82f;

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
            float side = Mathf.Min(pw, ph) * _scaleMultiplier * SOURCE_ASPECT;

            _rect.sizeDelta = new Vector2(side, side);
            _lastParentSize = new Vector2(pw, ph);
        }
    }
}
