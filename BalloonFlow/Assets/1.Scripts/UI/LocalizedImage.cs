using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// ROLLBACK_LOCALIZED_IMAGE_20260715: 언어별 이미지('굽힌 텍스트' 스프라이트) 동적 스왑.
    ///   폰트 fallback 으로 안 되는 '이미지에 박힌 텍스트'용 — 프리팹의 기존(EN) 스프라이트를 base 로 캡처한 뒤,
    ///   현재 언어가 한국어면 UI 아틀라스에서 '{base}KR' 를 '이름'으로 로드해 교체한다(예: textFail → textFailKR).
    ///   · 컴포넌트만 붙이면 됨(스프라이트 수동 할당 불필요) — 이름 규약 '~KR'.
    ///   · KR 이 아틀라스에 없거나 언어가 EN 이면 base(원본) 유지 → 회귀 안전.
    ///   · 언어 전환(LocalizationService.OnLanguageChanged) 시 실시간 재적용.
    ///   대상 예: PopupFail01/ImageTxt(textFail), PopupWinningStreakinfo/ImageTxt(winningStreakTitle),
    ///           NewFeature/Title(newFeatureTitle). ※ KR png 는 Assets/2.Sprite 하위(=UI 아틀라스 packable)라 빌드 포함됨.
    /// 롤백: 이 컴포넌트 제거(프리팹은 base EN 스프라이트 그대로 표시).
    /// </summary>
    [RequireComponent(typeof(Image))]
    [DisallowMultipleComponent]
    public class LocalizedImage : MonoBehaviour
    {
        [Tooltip("(선택) 이름 규약 대신 강제로 쓸 KR 스프라이트. 미할당 시 '{base}KR' 이름으로 UI 아틀라스에서 로드.")]
        [SerializeField] private Sprite _koreanOverride;
        [Tooltip("(선택) base 이름 강제. 미할당 시 프리팹의 현재 sprite.name 사용.")]
        [SerializeField] private string _baseNameOverride;
        [Tooltip("스왑 후 SetNativeSize 호출(EN/KR 크기 다를 때 원본 크기 유지용)")]
        [SerializeField] private bool _setNativeSize;

        private Image _img;
        private Sprite _baseSprite;   // 프리팹의 원본(EN) 스프라이트 캐시
        private string _baseName;
        private bool _init;
        private bool _subscribed;

        private void EnsureInit()
        {
            if (_init) return;
            _init = true;
            _img = GetComponent<Image>();
            _baseSprite = _img != null ? _img.sprite : null;
            _baseName = !string.IsNullOrEmpty(_baseNameOverride)
                ? _baseNameOverride
                : (_baseSprite != null ? _baseSprite.name : null);
        }

        private void OnEnable()
        {
            EnsureInit();
            Apply();
            if (!_subscribed) { LocalizationService.OnLanguageChanged += Apply; _subscribed = true; }
        }

        private void OnDisable()
        {
            if (_subscribed) { LocalizationService.OnLanguageChanged -= Apply; _subscribed = false; }
        }

        /// <summary>현재 언어에 맞춰 스프라이트 적용. KO → '{base}KR'(override 우선), 그 외/미존재 → base 유지.</summary>
        private void Apply()
        {
            if (_img == null) return;
            Sprite target = _baseSprite;

            if (LocalizationService.CurrentLanguageCode == "KO")
            {
                Sprite kr = _koreanOverride;
                if (kr == null && !string.IsNullOrEmpty(_baseName) && ResourceManager.HasInstance)
                    kr = ResourceManager.Instance.GetUISprite(_baseName + "KR");
                if (kr != null) target = kr;
            }

            if (target != null && _img.sprite != target)
            {
                _img.sprite = target;
                if (_setNativeSize) _img.SetNativeSize();
            }
        }
    }
}
