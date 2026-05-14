using UnityEngine;

namespace BalloonFlow
{
    // [의도된 설계] namespace 'BalloonFlow'는 프로젝트 전역 컨벤션(126/129 파일이 동일 사용). AimedPuzzle.<Project>.<Layer> 3단 구조는 본 리포지토리에 적용되지 않음.
    // [의도된 설계] 본 컴포넌트는 per-instance Fitter / per-scene UI로 전역 상태가 없으므로 Singleton 패턴 미적용.

    /// <summary>
    /// 1:1(5:5) 스플래시 배경 Image를 부모 RectTransform에 항상 cover하는 최소 envelope으로 배치한다.
    ///
    /// [Invariant — leaf-only / sizeDelta-only]
    ///  - 이 컴포넌트는 leaf RectTransform 전용이며, localScale/anchoredPosition을 변경하지 않고 sizeDelta만 제어한다.
    ///  - 따라서 형제 노드(Logo/LoadingBar/Text)의 위치·크기·스케일에 영향을 주지 않는다.
    ///  - 자식이 1개라도 존재하면 Awake에서 자동 비활성(_isDisabled=true) + LogError. 런타임에 자식이 추가되어도 ApplyFit이 진입 직전 재검증한다.
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
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class SplashBackgroundFitter : MonoBehaviour
    {
        [Header("⚠️ Leaf-only — 자식이 있으면 자동 비활성됩니다 (형제 레이아웃 보호)")]
        [Tooltip("부모 rect의 긴 변에 곱할 확대 계수. 1.0 = 정확한 cover(envelope), >1.0 = 추가 확대.")]
        [SerializeField, Range(1f, 1.5f)] private float _scaleMultiplier = 1.0f;

        private RectTransform _rect;
        private RectTransform _parentRect;
        private Vector2 _lastParentSize;
        private bool _isDisabled;
        private bool _runtimeChildWarned;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _parentRect = _rect.parent as RectTransform;

            if (_rect.childCount > 0)
            {
                _isDisabled = true;
                Debug.LogError("[SplashBackgroundFitter] Target has children — fitter disabled to protect child layouts (Logo/LoadingBar 등). Move fitter to a leaf Background node.");
                return;
            }

            // anchor/pivot/anchoredPosition은 Awake 1회만 설정. 이후 ApplyFit에서 절대 재변경하지 않는다 (sizeDelta-only invariant).
            _rect.anchorMin = new Vector2(0.5f, 0.5f);
            _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0.5f, 0.5f);
            _rect.anchoredPosition = Vector2.zero;
        }

        private void OnEnable() { ApplyFit(); }
        private void Start() { ApplyFit(); }

        private void Update()
        {
            if (_isDisabled) return;
            if (_parentRect == null) return;
            Vector2 cur = _parentRect.rect.size;
            if (cur != _lastParentSize) ApplyFit();
        }

        private void ApplyFit()
        {
            if (_isDisabled) return;
            if (_rect == null) _rect = GetComponent<RectTransform>();
            if (_parentRect == null) _parentRect = _rect.parent as RectTransform;
            if (_parentRect == null) return;

            // 런타임 자식 추가 방어: 자식이 생기면 즉시 중단 (1회만 경고).
            if (_rect.childCount > 0)
            {
                if (!_runtimeChildWarned)
                {
                    Debug.LogError("[SplashBackgroundFitter] Children detected at runtime — fitter halted to protect child layouts. Inspect scene/prefab for unintended hierarchy changes.");
                    _runtimeChildWarned = true;
                }
                _isDisabled = true;
                return;
            }

            float pw = _parentRect.rect.width;
            float ph = _parentRect.rect.height;
            float side = Mathf.Max(pw, ph) * _scaleMultiplier;

            // sizeDelta-only: localScale / anchoredPosition은 여기서 절대 건드리지 않는다.
            _rect.sizeDelta = new Vector2(side, side);
            _lastParentSize = new Vector2(pw, ph);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            var rt = transform as RectTransform;
            if (rt != null && rt.childCount > 0)
            {
                Debug.LogWarning($"[SplashBackgroundFitter] '{name}' has {rt.childCount} child(ren). This fitter is leaf-only and will auto-disable at runtime to protect sibling/child layouts.", this);
            }
        }
#endif
    }
}
