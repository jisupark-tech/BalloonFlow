using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// 공통 팝업 프레임. 모든 Popup 프리팹에 PopupCommonFrame 프리팹이 할당됨.
    /// 난이도별 프레임 스프라이트 교체, 타이틀 설정, 버튼 레이아웃 선택 등
    /// 공통 기능을 제공. 각 Popup은 이 프레임을 참조하여 추가 기능만 구현.
    ///
    /// 버튼 레이아웃:
    ///   - Single: 1개 버튼 (확인/닫기 등)
    ///   - Horizontal: 2개 버튼 좌우 배치 (Green/Red)
    ///   - Vertical: 3개 버튼 세로 배치 (Green/Red/Blue)
    /// </summary>
    public class PopupCommonFrame : MonoBehaviour
    {
        #region Pop Animation

        [Header("[Pop Animation — 팝업 등장 연출]")]
        [Tooltip("등장 애니메이션 사용 여부")]
        [SerializeField] private bool _usePopAnimation = true;
        [Tooltip("애니메이션 지속 시간 (초)")]
        [Range(0.05f, 2f)]
        [SerializeField] private float _popDuration = 0.35f;
        [Tooltip("Ease 종류. OutBack 권장 (오버슈트 후 원래 크기 복귀)")]
        [SerializeField] private Ease _popEase = Ease.OutBack;
        [Tooltip("시작 scale 배율 (원본 대비). 0.01 = 거의 점에서 시작, 1 = 변화 없음")]
        [Range(0.01f, 1f)]
        [SerializeField] private float _popStartScale = 0.01f;
        [Tooltip("Ease=OutBack/OutElastic 등에서 오버슈트 강도. (0~3 권장, OutBack 기본 ≈ 1.7)")]
        [Range(0f, 5f)]
        [SerializeField] private float _popOvershoot = 1.7f;
        [Tooltip("Time.timeScale 영향을 받지 않게 함. 일시정지 상태에서도 동작하려면 true 권장")]
        [SerializeField] private bool _popIgnoreTimeScale = true;

        private Vector3 _originalScale = Vector3.one;
        private bool _originalScaleCaptured;
        private Tween _popTween;

        private void Awake()
        {
            if (!_originalScaleCaptured)
            {
                _originalScale = transform.localScale;
                _originalScaleCaptured = true;
            }

            // Frame sprite override (난이도별). Side panel 은 atlas sprite 명 미확정.
            if (ResourceManager.HasInstance)
            {
                var rm = ResourceManager.Instance;
                _sprFrameNormal    = rm.UISpriteOr("framePopupNormal",    _sprFrameNormal);
                _sprFrameHard      = rm.UISpriteOr("framePopupHard",      _sprFrameHard);
                _sprFrameSuperHard = rm.UISpriteOr("framePopupSuperHard", _sprFrameSuperHard);
            }
        }

        private void OnEnable()
        {
            PlayPopAnimation();
        }

        private void OnDisable()
        {
            _popTween?.Kill();
            _popTween = null;
            if (_originalScaleCaptured)
                transform.localScale = _originalScale;
        }

        /// <summary>
        /// 등장 연출(scale 0.01 → 원본)을 시작. PopupManager.ActivatePopup에서
        /// 명시적으로 호출되어 close → reopen 시에도 신뢰성 있게 트윈이 작동.
        /// OnEnable에서도 호출되므로 일반 SetActive 토글로도 동작.
        /// </summary>
        public void PlayPopAnimation()
        {
            if (!_usePopAnimation) return;
            if (!_originalScaleCaptured)
            {
                _originalScale = transform.localScale;
                _originalScaleCaptured = true;
            }

            _popTween?.Kill();
            transform.localScale = _originalScale * _popStartScale;
            _popTween = transform
                .DOScale(_originalScale, _popDuration)
                .SetEase(_popEase, _popOvershoot)
                .SetUpdate(_popIgnoreTimeScale);
        }

        #endregion


        #region Serialized Fields — Frame

        [Header("[Frame Background]")]
        [SerializeField] private Image _frameImage;

        [Header("[Side Panels]")]
        [SerializeField] private Image _leftTopSidePanel;
        [SerializeField] private Image _rightTopSidePanel;

        [Header("[Title]")]
        [SerializeField] private TMP_Text _txtTitle;
        [SerializeField] private TMP_Text _txtTitleOutline;

        [Header("[Exit Button]")]
        [SerializeField] private Button _btnExit;

        #endregion

        #region Serialized Fields — Button Single

        [Header("[ButtonSingle — 1버튼 레이아웃]")]
        [SerializeField] private GameObject _btnSingleRoot;
        [SerializeField] private Button _btnSingle;
        [SerializeField] private Image _btnSingleFrame;
        [SerializeField] private TMP_Text _txtBtnSingle;
        [SerializeField] private TMP_Text _txtBtnSingleOutline;

        #endregion

        #region Serialized Fields — Button Horizontal (2버튼)

        [Header("[BtnHorizontal — 2버튼 좌우 레이아웃]")]
        [SerializeField] private GameObject _btnHorizontalRoot;

        [Header("[Horizontal — Green (Left)]")]
        [SerializeField] private Button _btnHorizGreen;
        [SerializeField] private Image _btnHorizGreenFrame;
        [SerializeField] private TMP_Text _txtHorizGreen;
        [SerializeField] private TMP_Text _txtHorizGreenOutline;

        [Header("[Horizontal — Red (Right)]")]
        [SerializeField] private Button _btnHorizRed;
        [SerializeField] private Image _btnHorizRedFrame;
        [SerializeField] private TMP_Text _txtHorizRed;
        [SerializeField] private TMP_Text _txtHorizRedOutline;

        #endregion

        #region Serialized Fields — Button Vertical (3버튼)

        [Header("[ButtonVertical — 3버튼 세로 레이아웃]")]
        [SerializeField] private GameObject _btnVerticalRoot;

        [Header("[Vertical — Green]")]
        [SerializeField] private Button _btnVertGreen;
        [SerializeField] private Image _btnVertGreenFrame;
        [SerializeField] private TMP_Text _txtVertGreen;
        [SerializeField] private TMP_Text _txtVertGreenOutline;

        [Header("[Vertical — Red]")]
        [SerializeField] private Button _btnVertRed;
        [SerializeField] private Image _btnVertRedFrame;
        [SerializeField] private TMP_Text _txtVertRed;
        [SerializeField] private TMP_Text _txtVertRedOutline;

        [Header("[Vertical — Blue]")]
        [SerializeField] private Button _btnVertBlue;
        [SerializeField] private Image _btnVertBlueFrame;
        [SerializeField] private TMP_Text _txtVertBlue;
        [SerializeField] private TMP_Text _txtVertBlueOutline;

        #endregion

        #region Serialized Fields — Difficulty Sprites

        [Header("[난이도별 프레임 스프라이트]")]
        [SerializeField] private Sprite _sprFrameNormal;
        [SerializeField] private Sprite _sprFrameHard;
        [SerializeField] private Sprite _sprFrameSuperHard;

        [Header("[난이도별 사이드패널 스프라이트]")]
        [SerializeField] private Sprite _sprSideNormal;
        [SerializeField] private Sprite _sprSideHard;
        [SerializeField] private Sprite _sprSideSuperHard;

        #endregion

        #region Properties — Buttons

        public Button BtnExit => _btnExit;
        public Button BtnSingle => _btnSingle;
        public Button BtnHorizGreen => _btnHorizGreen;
        public Button BtnHorizRed => _btnHorizRed;
        public Button BtnVertGreen => _btnVertGreen;
        public Button BtnVertRed => _btnVertRed;
        public Button BtnVertBlue => _btnVertBlue;

        #endregion

        #region Properties — Side Panels

        public Image LeftTopSidePanel => _leftTopSidePanel;
        public Image RightTopSidePanel => _rightTopSidePanel;

        #endregion

        #region Public Methods — Title

        public void SetTitle(string text)
        {
            if (_txtTitle != null) _txtTitle.text = text;
            if (_txtTitleOutline != null) _txtTitleOutline.text = text;
        }

        #endregion

        #region Public Methods — Button Layout

        public enum ButtonLayout { Single, Horizontal, Vertical, None }

        /// <summary>
        /// 버튼 레이아웃 선택. 선택된 레이아웃만 활성화, 나머지 비활성.
        /// </summary>
        public void SetButtonLayout(ButtonLayout layout)
        {
            if (_btnSingleRoot != null) _btnSingleRoot.SetActive(layout == ButtonLayout.Single);
            if (_btnHorizontalRoot != null) _btnHorizontalRoot.SetActive(layout == ButtonLayout.Horizontal);
            if (_btnVerticalRoot != null) _btnVerticalRoot.SetActive(layout == ButtonLayout.Vertical);
        }

        /// <summary>Single 버튼 텍스트 설정.</summary>
        public void SetSingleButtonText(string text)
        {
            if (_txtBtnSingle != null) _txtBtnSingle.text = text;
            if (_txtBtnSingleOutline != null) _txtBtnSingleOutline.text = text;
        }

        /// <summary>Horizontal Green 버튼 텍스트 설정.</summary>
        public void SetHorizGreenText(string text)
        {
            if (_txtHorizGreen != null) _txtHorizGreen.text = text;
            if (_txtHorizGreenOutline != null) _txtHorizGreenOutline.text = text;
        }

        /// <summary>Horizontal Red 버튼 텍스트 설정.</summary>
        public void SetHorizRedText(string text)
        {
            if (_txtHorizRed != null) _txtHorizRed.text = text;
            if (_txtHorizRedOutline != null) _txtHorizRedOutline.text = text;
        }

        /// <summary>Vertical Green/Red/Blue 버튼 텍스트 일괄 설정.</summary>
        public void SetVertButtonTexts(string green, string red, string blue)
        {
            if (_txtVertGreen != null) _txtVertGreen.text = green;
            if (_txtVertGreenOutline != null) _txtVertGreenOutline.text = green;
            if (_txtVertRed != null) _txtVertRed.text = red;
            if (_txtVertRedOutline != null) _txtVertRedOutline.text = red;
            if (_txtVertBlue != null) _txtVertBlue.text = blue;
            if (_txtVertBlueOutline != null) _txtVertBlueOutline.text = blue;
        }

        #endregion

        #region Public Methods — Exit Button

        public void ShowExitButton(bool show)
        {
            if (_btnExit != null) _btnExit.gameObject.SetActive(show);
        }

        #endregion

        #region Public Methods — Difficulty

        /// <summary>
        /// 난이도에 따라 프레임 + 사이드패널 스프라이트를 교체.
        /// </summary>
        public void ApplyDifficulty(DifficultyPurpose difficulty)
        {
            Sprite frameSpr = difficulty switch
            {
                DifficultyPurpose.Hard      => _sprFrameHard,
                DifficultyPurpose.SuperHard  => _sprFrameSuperHard,
                _                            => _sprFrameNormal
            };

            Sprite sideSpr = difficulty switch
            {
                DifficultyPurpose.Hard      => _sprSideHard,
                DifficultyPurpose.SuperHard  => _sprSideSuperHard,
                _                            => _sprSideNormal
            };

            if (_frameImage != null && frameSpr != null)
                _frameImage.sprite = frameSpr;
            if (_leftTopSidePanel != null && sideSpr != null)
                _leftTopSidePanel.sprite = sideSpr;
            if (_rightTopSidePanel != null && sideSpr != null)
                _rightTopSidePanel.sprite = sideSpr;
        }

        #endregion
    }
}
