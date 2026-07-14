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

                _sprSideNormal     = rm.UISpriteOr(Const.SPR_FRAMERESULTNORMAL,    _sprSideNormal);
                _sprSideHard       = rm.UISpriteOr(Const.SPR_FRAMERESULTHARD,      _sprSideHard);
                _sprSideSuperHard  = rm.UISpriteOr(Const.SPR_FRAMERESULTSUPERHARD, _sprSideSuperHard);
            }

            // 'ExitButton (1)' 은 prefab 의 복제 GameObject — 사용자 요구로 항상 활성 유지 (PopupError 구매 성공 등 전 케이스 적용)
            var exitDup = transform.Find("ExitButton (1)");
            if (exitDup == null)
            {
                var allChildren = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < allChildren.Length; i++)
                {
                    if (allChildren[i].name == "ExitButton (1)")
                    {
                        exitDup = allChildren[i];
                        break;
                    }
                }
            }
            if (exitDup != null) exitDup.gameObject.SetActive(true);
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

        #region Timer (Key Blaze 회차 카운트다운 등 — Inspector 링크 우선, 미할당 시 이름으로 fallback)

        [Header("[Timer — Inspector 링크. 비우면 이름(Timer/TextTimer/TextTimerOutline)으로 자동 탐색(fallback)]")]
        [Tooltip("Timer 그룹(ImageClock + TextTimer) 루트. 비우면 이름 'Timer' 로 탐색.")]
        [SerializeField] private Transform _timerGroup;
        [Tooltip("회차 카운트다운 본문 텍스트. 비우면 이름 'TextTimer' 로 탐색.")]
        [SerializeField] private TMP_Text _txtTimer;
        [Tooltip("회차 카운트다운 아웃라인 텍스트. 비우면 이름 'TextTimerOutline' 로 탐색.")]
        [SerializeField] private TMP_Text _txtTimerOutline;
        private bool _timerResolved;

        /// <summary>직렬화로 와이어된 참조는 그대로 쓰고, null 인 것만 이름으로 fallback 해석.</summary>
        private void ResolveTimerRefs()
        {
            if (_timerResolved) return;
            _timerResolved = true;

            // 셋 다 Inspector 링크돼 있으면 탐색 불필요.
            if (_timerGroup != null && _txtTimer != null && _txtTimerOutline != null) return;

            var all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                string n = all[i].name;
                if (_timerGroup == null && n == "Timer") _timerGroup = all[i];
                else if (_txtTimer == null && n == "TextTimer") _txtTimer = all[i].GetComponent<TMP_Text>();
                else if (_txtTimerOutline == null && n == "TextTimerOutline") _txtTimerOutline = all[i].GetComponent<TMP_Text>();
            }
        }

        /// <summary>Timer 그룹(ImageClock + TextTimer) 표시/숨김.</summary>
        public void ShowTimer(bool show)
        {
            ResolveTimerRefs();
            if (_timerGroup != null) _timerGroup.gameObject.SetActive(show);
        }

        /// <summary>Timer 텍스트 설정 (TextTimer + Outline 동시).</summary>
        public void SetTimerText(string text)
        {
            ResolveTimerRefs();
            if (_txtTimer != null) _txtTimer.text = text;
            if (_txtTimerOutline != null) _txtTimerOutline.text = text;
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

        [Header("[Description]")]
        [SerializeField] private TMP_Text _txtDescription;
        [SerializeField] private TMP_Text _txtDescriptionOutline;

        [Header("[Title Outline Difficulty Material Preset]")]
        [SerializeField] private Material _matTitleOutlineNormal;
        [SerializeField] private Material _matTitleOutlineHard;
        [SerializeField] private Material _matTitleOutlineSuperHard;

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

        private bool _difficultyRefsResolved;

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

        private DifficultyPurpose _currentDifficulty = DifficultyPurpose.Normal;
        private bool _hasExplicitDifficulty;

        public void SetTitle(string text)
        {
            if (_txtTitle != null) _txtTitle.text = text;
            if (_txtTitleOutline != null) _txtTitleOutline.text = text;
            // ROLLBACK_LOCALIZATION_HARDCODE_FIX_20260714: 채움 타이틀 KO 폰트 스왑(아웃라인은 ApplyTitleOutline 이 언어 인지 처리).
            LocalizationFont.Apply(_txtTitle);
            ApplyTitleOutline(_hasExplicitDifficulty ? _currentDifficulty : ResolveActiveDifficulty());
        }

        /// <summary>Description 텍스트 설정 (TxtDescription + Outline 동시). 미배선 시 null-safe로 skip.</summary>
        public void SetDescription(string text)
        {
            if (_txtDescription != null) _txtDescription.text = text;
            if (_txtDescriptionOutline != null) _txtDescriptionOutline.text = text;
            LocalizationFont.Apply(_txtDescription);
            LocalizationFont.Apply(_txtDescriptionOutline);
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
            LocalizationFont.Apply(_txtBtnSingle);
            LocalizationFont.Apply(_txtBtnSingleOutline);
        }

        /// <summary>Horizontal Green 버튼 텍스트 설정.</summary>
        public void SetHorizGreenText(string text)
        {
            if (_txtHorizGreen != null) _txtHorizGreen.text = text;
            if (_txtHorizGreenOutline != null) _txtHorizGreenOutline.text = text;
            LocalizationFont.Apply(_txtHorizGreen);
            LocalizationFont.Apply(_txtHorizGreenOutline);
        }

        /// <summary>Horizontal Red 버튼 텍스트 설정.</summary>
        public void SetHorizRedText(string text)
        {
            if (_txtHorizRed != null) _txtHorizRed.text = text;
            if (_txtHorizRedOutline != null) _txtHorizRedOutline.text = text;
            LocalizationFont.Apply(_txtHorizRed);
            LocalizationFont.Apply(_txtHorizRedOutline);
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
            LocalizationFont.Apply(_txtVertGreen);   LocalizationFont.Apply(_txtVertGreenOutline);
            LocalizationFont.Apply(_txtVertRed);     LocalizationFont.Apply(_txtVertRedOutline);
            LocalizationFont.Apply(_txtVertBlue);    LocalizationFont.Apply(_txtVertBlueOutline);
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
            _currentDifficulty = difficulty;
            _hasExplicitDifficulty = true;
            EnsureDifficultyRefs();
            EnsureDifficultySprites();

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

            ApplyTitleOutline(difficulty);
        }

        private void EnsureDifficultyRefs()
        {
            if (_difficultyRefsResolved) return;
            _difficultyRefsResolved = true;

            // ROLLBACK_COMMONFRAME_DIFFICULTY_REF_FALLBACK_20260624:
            // PopupQuit/PopupSettings prefab revisions can miss serialized Image references,
            // making ApplyDifficulty a no-op. Resolve common node names at runtime so they match
            // PopupResult behavior without prefab-only dependency.
            if (_frameImage == null)
                _frameImage = FindImageByNames("Frame", "FrameBG", "ImageFrame", "PopupFrame", "CommonFrame", "CommpanFrame");
            if (_leftTopSidePanel == null)
                _leftTopSidePanel = FindImageByNames("LeftTopSidePanel", "LeftSidePanel", "ImageLeftTopSidePanel", "ImageLeftSidePanel");
            if (_rightTopSidePanel == null)
                _rightTopSidePanel = FindImageByNames("RightTopSidePanel", "RightSidePanel", "ImageRightTopSidePanel", "ImageRightSidePanel");
        }

        private void EnsureDifficultySprites()
        {
            if (!ResourceManager.HasInstance) return;

            var rm = ResourceManager.Instance;
            _sprFrameNormal    = rm.UISpriteOr(Const.SPR_FRAMEPOPUPNORMAL,    _sprFrameNormal);
            _sprFrameHard      = rm.UISpriteOr(Const.SPR_FRAMEPOPUPHARD,      _sprFrameHard);
            _sprFrameSuperHard = rm.UISpriteOr(Const.SPR_FRAMEPOPUPSUPERHARD, _sprFrameSuperHard);

            _sprSideNormal     = rm.UISpriteOr(Const.SPR_FRAMERESULTNORMAL,    _sprSideNormal);
            _sprSideHard       = rm.UISpriteOr(Const.SPR_FRAMERESULTHARD,      _sprSideHard);
            _sprSideSuperHard  = rm.UISpriteOr(Const.SPR_FRAMERESULTSUPERHARD, _sprSideSuperHard);
        }

        private Image FindImageByNames(params string[] names)
        {
            var images = GetComponentsInChildren<Image>(true);
            for (int n = 0; n < names.Length; n++)
            {
                string target = names[n];
                for (int i = 0; i < images.Length; i++)
                {
                    Image image = images[i];
                    if (image != null && image.name == target)
                        return image;
                }
            }
            return null;
        }

        /// <summary>
        /// [WS_TITLE_PURPLE_OUTLINE_20260615] Normal 난이도용 TitleOutline 머티리얼을 런타임에 교체.
        /// 이전: prefab 직렬화 값 = Poppins-Bold-BlueOutline. 변경 사유: PopupWinningStreak 타이틀 보라 외곽선 적용. 프리팹이 바이너리 직렬화라 코드 오버라이드로 처리.
        /// 호출 시점은 SetTitle/ApplyDifficulty 전이어도/후여도 무방 — 내부에서 즉시 재적용함.
        /// </summary>
        public void OverrideTitleOutlineNormalMaterial(Material mat)
        {
            if (mat == null) return;
            _matTitleOutlineNormal = mat;
            ApplyTitleOutline(_hasExplicitDifficulty ? _currentDifficulty : ResolveActiveDifficulty());
        }

        public void OverrideTitleOutlineAllDifficultyMaterials(Material mat)
        {
            // ROLLBACK_WS_TITLE_PURPLE_ALL_DIFFICULTIES_20260624:
            // Winning Streak title must stay purple even when the active level difficulty is
            // Hard/SuperHard. Normal-only override lets difficulty materials repaint it.
            if (mat == null) return;
            _matTitleOutlineNormal = mat;
            _matTitleOutlineHard = mat;
            _matTitleOutlineSuperHard = mat;
            ApplyTitleOutline(_hasExplicitDifficulty ? _currentDifficulty : ResolveActiveDifficulty());
        }

        public void OverrideSingleButtonOutlineMaterial(Material mat, Color fallbackColor)
        {
            // ROLLBACK_QUITGAME_SINGLE_RED_OUTLINE_20260624:
            // Lobby Quit Game uses PopupDescription's single red button. Its outline should match
            // the red button style instead of the black title/shop override colors.
            UIOutlineStyle.ApplyMaterialOrColor(_txtBtnSingleOutline, mat, fallbackColor);
        }

        private void ApplyTitleOutline(DifficultyPurpose difficulty)
        {
            Material mat = UIOutlineStyle.SelectDifficultyMaterial(
                difficulty,
                _matTitleOutlineNormal,
                _matTitleOutlineHard,
                _matTitleOutlineSuperHard);
            // ROLLBACK_LOCALIZATION_HARDCODE_FIX_20260714: KO 면 아웃라인도 Chiron 폰트 + 동일 색/난이도 프리셋으로 매핑.
            if (_txtTitleOutline != null &&
                string.Equals(LocalizationService.CurrentLanguageCode, "KO", System.StringComparison.OrdinalIgnoreCase))
            {
                TMP_FontAsset ko = LocalizationFont.LoadKoFont();
                if (ko != null)
                {
                    if (_txtTitleOutline.font != ko) _txtTitleOutline.font = ko;
                    if (mat != null) mat = UIOutlineStyle.MaterialForFont(mat, ko, "ChironGoRoundTC-Black");
                }
            }
            UIOutlineStyle.ApplyMaterialOrColor(_txtTitleOutline, mat, UIOutlineStyle.ForDifficulty(difficulty));
        }

        private static DifficultyPurpose ResolveActiveDifficulty()
        {
            if (!LevelManager.HasInstance) return DifficultyPurpose.Normal;

            int levelId = LevelManager.Instance.CurrentLevelId;
            return levelId > 0
                ? LevelManager.Instance.GetLevelDifficulty(levelId)
                : DifficultyPurpose.Normal;
        }

        #endregion
    }
}
