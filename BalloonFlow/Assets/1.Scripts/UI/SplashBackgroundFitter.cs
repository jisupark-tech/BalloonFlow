using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    // [의도된 설계] namespace 'BalloonFlow'는 프로젝트 전역 컨벤션(126/129 파일이 동일 사용). AimedPuzzle.<Project>.<Layer> 3단 구조는 본 리포지토리에 적용되지 않음.
    // [의도된 설계] 본 컴포넌트는 per-instance Fitter / per-scene UI로 전역 상태가 없으므로 Singleton 패턴 미적용.

    /// <summary>
    /// 스플래시 배경 Image를 부모 RectTransform에 항상 cover하는 최소 envelope으로 배치한다.
    ///
    /// [Invariant — leaf-only / sizeDelta-only]
    ///  - 이 컴포넌트는 leaf RectTransform 전용이며, localScale/anchoredPosition을 변경하지 않고 sizeDelta만 제어한다.
    ///  - 따라서 형제 노드(Logo/LoadingBar/Text)의 위치·크기·스케일에 영향을 주지 않는다.
    ///  - 자식이 1개라도 존재하면 Awake에서 자동 비활성(_isDisabled=true) + LogError. 런타임에 자식이 추가되어도 ApplyFit이 진입 직전 재검증한다.
    ///
    /// 동작:
    ///  - 1:1 정사각형 가정을 제거하고, sprite의 native aspect(srcW:srcH)를 기준으로 envelope cover를 계산한다.
    ///  - scale = Max(parentW / srcW, parentH / srcH) * _scaleMultiplier → sizeDelta = (srcW * scale, srcH * scale).
    ///  - sizeDelta 비율 = srcW / srcH → 원본 비율 보존(distortion 0), 부모 양변 ≥ 부모 → 여백 0.
    ///  - 중앙 정렬(anchor/pivot=0.5)로 대칭 크롭 → 잘림 최소화.
    ///  - <see cref="_scaleMultiplier"/>(1.0~1.5)는 디자이너가 추가 확대를 원할 때만 사용. 1.0 = 정확한 cover.
    ///
    /// 사용자 요구:
    ///  - 임의 source aspect / 임의 화면 비율에서도 화면 전체를 항상 가득 채워야 한다(여백 금지) + 원본 비율 보존 + 대칭 중앙 크롭.
    ///
    /// 의존성:
    ///  - UnityEngine.UI.Image 컴포넌트 필수(RequireComponent). 또한 Image.sprite가 할당되어 있어야 동작.
    ///  - preserveAspect는 Awake에서 false로 강제(sizeDelta 자체가 source aspect와 동일하므로 distortion 없음;
    ///    preserveAspect=true는 floating-point 오차로 미세 여백을 만들 수 있어 명시적으로 false).
    ///
    /// 왜 Singleton이 아닌가:
    ///  - 본 컴포넌트는 단일 RectTransform에 부착되는 per-instance Fitter다. 전역 상태가 없으므로 Singleton 패턴 불필요.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(Image))]
    public class SplashBackgroundFitter : MonoBehaviour
    {
        [Header("⚠️ Leaf-only — 자식이 있으면 자동 비활성됩니다 (형제 레이아웃 보호)")]
        [Tooltip("envelope cover scale에 곱할 추가 확대 계수. 1.0 = 정확한 cover, >1.0 = 추가 확대.")]
        [SerializeField, Range(1f, 1.5f)] private float _scaleMultiplier = 1.0f;

        private RectTransform _rect;
        private RectTransform _parentRect;
        private Image _image;
        private Vector2 _lastParentSize;
        private bool _isDisabled;
        private bool _runtimeChildWarned;
        private bool _spriteMissingWarned;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _parentRect = _rect.parent as RectTransform;
            _image = GetComponent<Image>();

            if (_image == null)
            {
                _isDisabled = true;
                Debug.LogError("[SplashBackgroundFitter] Image component missing — fitter disabled. This component requires UnityEngine.UI.Image to read sprite native aspect.");
                return;
            }

            if (_rect.childCount > 0)
            {
                _isDisabled = true;
                Debug.LogError("[SplashBackgroundFitter] Target has children — fitter disabled to protect child layouts (Logo/LoadingBar 등). Move fitter to a leaf Background node.");
                return;
            }

            // sizeDelta가 이미 sprite native aspect와 동일하므로 preserveAspect는 불필요.
            // preserveAspect=true는 floating-point 오차로 미세 여백을 만들 수 있어 명시적으로 false로 강제.
            _image.preserveAspect = false;

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

        // Update만으로는 sprite 교체를 감지하지 못함 (parent size만 비교). 외부에서 sprite를 바꾼 직후 호출.
        public void Refresh() { ApplyFit(); }

        private void ApplyFit()
        {
            if (_isDisabled) return;
            if (_rect == null) _rect = GetComponent<RectTransform>();
            if (_parentRect == null) _parentRect = _rect.parent as RectTransform;
            if (_parentRect == null) return;
            if (_image == null) _image = GetComponent<Image>();
            if (_image == null) return;

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

            var sprite = _image.sprite;
            if (sprite == null)
            {
                // Image가 비활성(렌더 안 함)이면 sprite-less placeholder 상태(예: 단색 페이드 오버레이) — fit/경고 불필요.
                // sprite는 표시 직전 ApplyFadeImage 등에서 할당 후 Refresh()로 재계산되므로 여기서 조용히 스킵.
                if (_image.enabled && !_spriteMissingWarned)
                {
                    Debug.LogError("[SplashBackgroundFitter] Image.sprite is null — cannot read native aspect; sizeDelta untouched. Assign a sprite to the Image component.");
                    _spriteMissingWarned = true;
                }
                return;
            }

            float srcW = sprite.rect.width;
            float srcH = sprite.rect.height;
            if (srcW <= 0f || srcH <= 0f) return;

            float pw = _parentRect.rect.width;
            float ph = _parentRect.rect.height;
            if (pw <= 0f || ph <= 0f) return;

            // envelope cover: 양변 모두 부모 변 이상이 되도록 max ratio 사용 → 여백 0.
            // sizeDelta 비율 = srcW/srcH이므로 원본 비율 보존 → distortion 0.
            float scale = Mathf.Max(pw / srcW, ph / srcH) * _scaleMultiplier;

            // sizeDelta-only: localScale / anchoredPosition은 여기서 절대 건드리지 않는다.
            _rect.sizeDelta = new Vector2(srcW * scale, srcH * scale);
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
