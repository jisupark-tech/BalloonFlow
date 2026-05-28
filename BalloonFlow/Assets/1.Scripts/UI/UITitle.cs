using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    // [의도된 설계] namespace 'BalloonFlow'는 프로젝트 전역 컨벤션(126/129 파일이 동일 사용). AimedPuzzle.<Project>.<Layer> 3단 구조는 본 리포지토리에 적용되지 않음.
    // [의도된 설계] 본 컴포넌트는 per-instance Fitter / per-scene UI로 전역 상태가 없으므로 Singleton 패턴 미적용.

    /// <summary>
    /// 타이틀 UI. Resources/UI/UITitle 프리팹에서 로드.
    /// CDM 다운로드 + 서버 세팅 진행도를 슬라이더 + "%" 텍스트로 표시.
    /// 100% 도달 시 TitleController 가 게임 자동 입장.
    ///
    /// ※ UITitle은 per-scene MonoBehaviour로 Singleton이 아님. 인스턴스 관리는 TitleController가 담당.
    /// </summary>
    public class UITitle : UIBase
    {
        [Header("[Title 텍스트]")]
        [SerializeField] private Text _logoText;
        [SerializeField] private Text _subtitleText;
        [SerializeField] private Text _tapToStartText;

        [Header("[Loading Progress — CDM/서버 세팅]")]
        [SerializeField] private Slider _progressSlider;
        [SerializeField] private TMP_Text _txtPercentage;
        [SerializeField] private TMP_Text _txtPercentageOutline;
        [Tooltip("진행 상태 라벨 (예: \"Connecting...\")")]
        [SerializeField] private TMP_Text _txtStatus;
        [SerializeField] private TMP_Text _txtStatusOutline;

        [Header("[Splash Background — 해상도 대응 대상]")]
        [Tooltip("잘림 없이 fit + 축소될 배경 Image의 RectTransform (leaf 노드여야 함; Logo/LoadingBar의 부모면 안 됨)")]
        [SerializeField] private RectTransform _splashBackground;

        public Text LogoText => _logoText;
        public Text SubtitleText => _subtitleText;
        public Text TapToStartText => _tapToStartText;

        public Slider ProgressSlider => _progressSlider;

        private Image _splashImage;

        /// <summary>스플래시 배경 sprite — 첫 실행 시 레벨 진입 전환에 그대로 이어 보여주기 위해 사용 (별도 전환 이미지 없음).</summary>
        public Sprite SplashSprite => _splashImage != null ? _splashImage.sprite : null;

        private void Awake()
        {
            if (_splashBackground != null)
            {
                // leaf 검증: 자식이 있으면 SplashBackgroundFitter를 추가하지 않는다 (형제/자식 레이아웃 보호).
                if (_splashBackground.childCount > 0)
                {
                    Debug.LogError("[UITitle] _splashBackground has children — SplashBackgroundFitter NOT attached. Wire a leaf RectTransform (자식 없는 Background Image)을 인스펙터에 지정하세요.");
                    return;
                }

                var go = _splashBackground.gameObject;
                // sprite native aspect 기반 cover를 사용하므로 Image + sprite 할당 여부를 사전 점검(미할당이면 fitter가 _isDisabled로 자기 보호).
                var img = go.GetComponent<Image>();
                _splashImage = img;
                if (img == null || img.sprite == null)
                {
                    Debug.LogWarning("[UITitle] _splashBackground에 Image 컴포넌트 또는 sprite가 없습니다 — SplashBackgroundFitter는 부착되나 sprite 할당 전까지 sizeDelta를 조작하지 않습니다.");
                }

                if (go.GetComponent<SplashBackgroundFitter>() == null)
                    go.AddComponent<SplashBackgroundFitter>();
                return;
            }

            var bg = transform.Find("Background");
            if (bg == null)
            {
                Debug.LogWarning("[UITitle] Background child not found — splash fitter skipped");
                return;
            }

            var bgGo = bg.gameObject;
            var bgImage = bgGo.GetComponent<Image>();
            _splashImage = bgImage;
            bool hasImage = bgImage != null;
            bool isLeaf = bg.childCount == 0;
            if (!hasImage || !isLeaf)
            {
                Debug.LogError("[UITitle] _splashBackground를 인스펙터에서 명시적으로 wire하세요 — Background 노드가 leaf가 아니거나 Image가 없습니다.");
                return;
            }

            if (bgImage.sprite == null)
            {
                Debug.LogWarning("[UITitle] Background Image.sprite가 비어있습니다 — SplashBackgroundFitter는 부착되나 sprite 할당 전까지 sizeDelta를 조작하지 않습니다.");
            }

            if (bgGo.GetComponent<SplashBackgroundFitter>() == null)
                bgGo.AddComponent<SplashBackgroundFitter>();
        }

        /// <summary>
        /// 진행도 갱신: 0~1 비율을 슬라이더 + "XX%" 텍스트 (본문 + outline) 둘 다 갱신.
        /// </summary>
        public void SetProgress(float ratio01)
        {
            ratio01 = Mathf.Clamp01(ratio01);
            if (_progressSlider != null) _progressSlider.value = ratio01;

            int percent = Mathf.RoundToInt(ratio01 * 100f);
            string txt = $"{percent}%";
            if (_txtPercentage != null) _txtPercentage.text = txt;
            if (_txtPercentageOutline != null) _txtPercentageOutline.text = txt;
        }

        /// <summary>현재 진행 상태 라벨 (옵션, 없으면 무시).</summary>
        public void SetStatus(string status)
        {
            if (_txtStatus != null) _txtStatus.text = status;
            if (_txtStatusOutline != null) _txtStatusOutline.text = status;
        }

        /// <summary>"Tap to Start" 표시/숨김 (로딩 중 숨기기 등).</summary>
        public void SetTapHintVisible(bool visible)
        {
            if (_tapToStartText != null) _tapToStartText.gameObject.SetActive(visible);
        }
    }
}
