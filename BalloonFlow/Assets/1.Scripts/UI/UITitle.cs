using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
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

        private void Awake()
        {
            if (_splashBackground != null)
            {
                var go = _splashBackground.gameObject;
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
            bool hasImage = bgGo.GetComponent<Image>() != null;
            bool isLeaf = bg.childCount == 0;
            if (!hasImage || !isLeaf)
            {
                Debug.LogWarning("[UITitle] _splashBackground를 인스펙터에서 명시적으로 wire하세요 — Background 노드가 leaf가 아니거나 Image가 없습니다.");
                return;
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
