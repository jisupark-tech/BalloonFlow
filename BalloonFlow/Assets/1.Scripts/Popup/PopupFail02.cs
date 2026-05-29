using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    public class PopupFail02 : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Buttons — 직접 할당]")]
        [SerializeField] private Button _btnRetry;
        [SerializeField] private Button _btnHome;
        [SerializeField] private Button _btnExit;

        [Header("[골드 표시 — 보수적 보존(미사용). TopBar 잔액은 AnimatedCoinLabel 가 갱신.]")]
        [SerializeField] private TMP_Text _txtGold;
        [SerializeField] private TMP_Text _txtGoldOutline;

        [Header("[난이도별 비주얼]")]
        [SerializeField] private Image _imageLight;
        [SerializeField] private Image _imageStage;
        [SerializeField] private Sprite _sprStageNormal;
        [SerializeField] private Sprite _sprStageHard;
        [SerializeField] private Sprite _sprStageSuperHard;

        [Header("[Hard Level Option — Hard/SuperHard 전용]")]
        [SerializeField] private GameObject _hardLevelOption;
        [SerializeField] private Image _iconSkull;
        [SerializeField] private Sprite _sprSkullHard;
        [SerializeField] private Sprite _sprSkullSuperHard;
        [SerializeField] private TMP_Text _txtHardLevel;
        [SerializeField] private TMP_Text _txtHardLevelOutline;
        [SerializeField] private Material _matHardLevelOutlineHard;
        [SerializeField] private Material _matHardLevelOutlineSuperHard;

        [Header("[난이도별 곱하기 라벨 — 표시용(내부 수치와 무관)]")]
        [SerializeField] private GameObject _multiplierLabel;
        [SerializeField] private TMP_Text _txtMultiplier;
        [SerializeField] private TMP_Text _txtMultiplierOutline;

        [Header("[Fail 이미지 — 난이도별]")]
        [SerializeField] private Image _imageFail;
        [SerializeField] private TMP_Text _txtFailOutline;
        [SerializeField] private Material _matFailOutlineNormal;
        [SerializeField] private Material _matFailOutlineHard;
        [SerializeField] private Material _matFailOutlineSuperHard;
        [SerializeField] private Sprite _sprFailNormal;
        [SerializeField] private Sprite _sprFailHard;
        [SerializeField] private Sprite _sprFailSuperHard;

        [Header("[HardOptionColor — Hard/SuperHard 전용]")]
        [SerializeField] private Image _imageHardOptionColor;
        [SerializeField] private Sprite _sprHardOptionHard;
        [SerializeField] private Sprite _sprHardOptionSuperHard;

        private const int RETRY_BONUS_GOLD = 20;

        // 난이도별 ImageLight 색상 (PopupResult와 동일)
        private static readonly Color LIGHT_NORMAL    = new Color(0x00 / 255f, 0x9B / 255f, 0xFF / 255f); // #009BFF
        private static readonly Color LIGHT_HARD      = new Color(0xAF / 255f, 0x20 / 255f, 0xE5 / 255f); // #AF20E5
        private static readonly Color LIGHT_SUPERHARD  = new Color(0xFF / 255f, 0x59 / 255f, 0x00 / 255f); // #FF5900

        private Button RetryBtn => _btnRetry != null ? _btnRetry : (_frame != null ? _frame.BtnHorizGreen : null);
        private Button HomeBtn => _btnHome != null ? _btnHome : (_frame != null ? _frame.BtnHorizRed : null);
        private Button ExitBtn => _btnExit != null ? _btnExit : (_frame != null ? _frame.BtnExit : null);

        protected override void Awake()
        {
            base.Awake();
            LoadStageSpritesFromResources();
            EnsureFailOutlineBinding();

            // Skull (Hard/SuperHard) 는 atlas 에 있음. Stage sprites 는 Resources/ 에만 있어 별도 로드 유지.
            if (ResourceManager.HasInstance)
            {
                var rm = ResourceManager.Instance;
                _sprSkullHard      = rm.UISpriteOr("iconSkullHard",      _sprSkullHard);
                _sprSkullSuperHard = rm.UISpriteOr("iconSkullSuperHard", _sprSkullSuperHard);

                _sprFailNormal          = rm.UISpriteOr(Const.SPR_POPUPINNERNORMAL,    _sprFailNormal);
                _sprFailHard            = rm.UISpriteOr(Const.SPR_POPUPINNERHARD,      _sprFailHard);
                _sprFailSuperHard       = rm.UISpriteOr(Const.SPR_POPUPINNERSUPURHARD, _sprFailSuperHard);
                _sprHardOptionHard      = rm.UISpriteOr(Const.SPR_FRAMEHARD,           _sprHardOptionHard);
                _sprHardOptionSuperHard = rm.UISpriteOr(Const.SPR_FRAMESUPERHARD,      _sprHardOptionSuperHard);
            }

            if (RetryBtn != null) RetryBtn.onClick.AddListener(OnRetryClicked);
            if (HomeBtn != null) HomeBtn.onClick.AddListener(OnHomeClicked);
            if (ExitBtn != null) ExitBtn.onClick.AddListener(OnHomeClicked);

            EnsureTopBarBinding();
        }

        private void EnsureTopBarBinding()
        {
            Transform topBar = FindChildRecursive(transform, "TopBarArea");
            Transform gold = topBar != null ? FindChildRecursive(topBar, "GoldPanel") : null;
            Transform txt = gold != null ? FindChildRecursive(gold, "TxtGold") : null;
            if (txt != null && txt.GetComponent<AnimatedCoinLabel>() == null)
                txt.gameObject.AddComponent<AnimatedCoinLabel>();
        }

        private static Transform FindChildRecursive(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName) return child;
                Transform deep = FindChildRecursive(child, childName);
                if (deep != null) return deep;
            }
            return null;
        }

        private void EnsureFailOutlineBinding()
        {
            if (_txtFailOutline != null) return;
            Transform failOutline = FindChildRecursive(transform, "TxtFailOutline");
            if (failOutline != null)
                _txtFailOutline = failOutline.GetComponent<TMP_Text>();
        }

        private void LoadStageSpritesFromResources()
        {
            // Stage sprite 들은 Resources/Sprites/UI/ 에 있고 atlas 와 별개.
            var n = Resources.Load<Sprite>("Sprites/UI/resultStageNormal");
            var h = Resources.Load<Sprite>("Sprites/UI/resultStageHard");
            var s = Resources.Load<Sprite>("Sprites/UI/resultStageSuperHard");
            if (n != null) _sprStageNormal   = n;
            if (h != null) _sprStageHard     = h;
            if (s != null) _sprStageSuperHard = s;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (RetryBtn != null) RetryBtn.onClick.RemoveAllListeners();
            if (HomeBtn != null) HomeBtn.onClick.RemoveAllListeners();
            if (ExitBtn != null) ExitBtn.onClick.RemoveAllListeners();
        }

        private bool _lifeConsumed;

        private void OnEnable()
        {
            // PopupManager가 SetActive(true) 할 때 호출됨
            // 실패 확정 시 하트 1개 소모
            if (!_lifeConsumed && LifeManager.HasInstance)
            {
                LifeManager.Instance.UseLive();
                _lifeConsumed = true;
            }

            // PopupManager.ShowPopup("popup_fail02")로 진입하는 경로에선
            // Show(difficulty)가 호출되지 않으므로 여기서 자동 적용.
            DifficultyPurpose diff = DifficultyPurpose.Normal;
            if (LevelManager.HasInstance)
                diff = LevelManager.Instance.GetLevelDifficulty(LevelManager.Instance.CurrentLevelId);

            if (_frame != null) _frame.ApplyDifficulty(diff);
            UpdateHardLevelOption(diff);
        }

        private void OnDisable()
        {
            _lifeConsumed = false; // 다음 실패 시 다시 소모 가능
        }

        public void Show(DifficultyPurpose difficulty)
        {
            if (_frame != null)
            {
                _frame.ApplyDifficulty(difficulty);
                _frame.SetTitle("Stage Failed");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.SetHorizGreenText("Retry");
                _frame.SetHorizRedText("Home");
                _frame.ShowExitButton(true);
            }
            UpdateHardLevelOption(difficulty);
            OpenUI();
        }

        private void UpdateHardLevelOption(DifficultyPurpose difficulty)
        {
            EnsureFailOutlineBinding();

            // ImageLight 색상
            if (_imageLight != null)
            {
                _imageLight.color = difficulty switch
                {
                    DifficultyPurpose.Hard      => LIGHT_HARD,
                    DifficultyPurpose.SuperHard  => LIGHT_SUPERHARD,
                    _                            => LIGHT_NORMAL
                };
            }

            // ImageStage 스프라이트
            if (_imageStage != null)
            {
                Sprite stageSpr = difficulty switch
                {
                    DifficultyPurpose.Hard      => _sprStageHard ?? _sprStageNormal,
                    DifficultyPurpose.SuperHard  => _sprStageSuperHard ?? _sprStageNormal,
                    _                            => _sprStageNormal
                };
                if (stageSpr != null) _imageStage.sprite = stageSpr;
            }

            // ImageFail (Fail Inner) 스프라이트
            if (_imageFail != null)
            {
                Sprite failSpr = difficulty switch
                {
                    DifficultyPurpose.Hard      => _sprFailHard ?? _sprFailNormal,
                    DifficultyPurpose.SuperHard  => _sprFailSuperHard ?? _sprFailNormal,
                    _                            => _sprFailNormal
                };
                if (failSpr != null) _imageFail.sprite = failSpr;
            }

            // HardOptionColor: Normal 숨김 / Hard·SuperHard 노출 + 스프라이트 교체
            Material failOutlineMat = UIOutlineStyle.SelectDifficultyMaterial(
                difficulty,
                _matFailOutlineNormal,
                _matFailOutlineHard,
                _matFailOutlineSuperHard);
            UIOutlineStyle.ApplyMaterialOrColor(_txtFailOutline, failOutlineMat, UIOutlineStyle.ForDifficulty(difficulty));

            if (_imageHardOptionColor != null)
            {
                if (difficulty == DifficultyPurpose.Normal)
                {
                    _imageHardOptionColor.gameObject.SetActive(false);
                }
                else
                {
                    _imageHardOptionColor.gameObject.SetActive(true);
                    Sprite hardOptSpr = difficulty == DifficultyPurpose.SuperHard
                        ? _sprHardOptionSuperHard
                        : _sprHardOptionHard;
                    if (hardOptSpr != null) _imageHardOptionColor.sprite = hardOptSpr;
                }
            }

            // HardLevelOption 표시: Normal=숨김, Hard/SuperHard=노출
            bool show = difficulty == DifficultyPurpose.Hard || difficulty == DifficultyPurpose.SuperHard;
            if (_hardLevelOption != null) _hardLevelOption.SetActive(show);

            // 방어적 처리: _hardLevelOption 루트가 프리팹에 미할당이거나 부분 영역만 가리키는 경우를 대비,
            // 하위 구성 요소(IconSkull, TxtHardLevel, Outline)도 개별적으로 가시 상태 제어.
            if (_iconSkull != null) _iconSkull.gameObject.SetActive(show);
            if (_txtHardLevel != null) _txtHardLevel.gameObject.SetActive(show);
            if (_txtHardLevelOutline != null) _txtHardLevelOutline.gameObject.SetActive(show);

            // 곱하기 라벨: Normal 없음 / Hard x3 / SuperHard x5
            string multiplier = difficulty switch
            {
                DifficultyPurpose.SuperHard => "x5",
                DifficultyPurpose.Hard      => "x3",
                _                            => ""
            };
            bool showMultiplier = !string.IsNullOrEmpty(multiplier);
            if (_multiplierLabel != null) _multiplierLabel.SetActive(showMultiplier);
            if (_txtMultiplier != null)
            {
                _txtMultiplier.gameObject.SetActive(showMultiplier);
                _txtMultiplier.text = multiplier;
            }
            if (_txtMultiplierOutline != null)
            {
                _txtMultiplierOutline.gameObject.SetActive(showMultiplier);
                _txtMultiplierOutline.text = multiplier;
            }

            if (show)
            {
                string label = difficulty == DifficultyPurpose.SuperHard ? "SuperHard" : "Hard";
                if (_txtHardLevel != null) _txtHardLevel.text = label;
                if (_txtHardLevelOutline != null) _txtHardLevelOutline.text = label;

                // IconSkull 스프라이트
                if (_iconSkull != null)
                {
                    Sprite skullSpr = difficulty == DifficultyPurpose.SuperHard ? _sprSkullSuperHard : _sprSkullHard;
                    if (skullSpr != null) _iconSkull.sprite = skullSpr;
                }

                // TxtHardLevelOutline 머티리얼
                if (_txtHardLevelOutline != null)
                {
                    Material mat = difficulty == DifficultyPurpose.SuperHard
                        ? _matHardLevelOutlineSuperHard
                        : _matHardLevelOutlineHard;
                    UIOutlineStyle.ApplyMaterialOrColor(_txtHardLevelOutline, mat, UIOutlineStyle.ForDifficulty(difficulty));
                    UIOutlineStyle.ApplyMaterialOrColor(_txtMultiplierOutline, mat, UIOutlineStyle.ForDifficulty(difficulty));
                }
            }
        }

        private void OnRetryClicked()
        {
            if (LifeManager.HasInstance && LifeManager.Instance.CurrentLives <= 0)
            {
                Debug.Log("[PopupFail02] 하트 부족 — More Lives 팝업");
                if (UIManager.HasInstance)
                    UIManager.Instance.OpenUI<PopupMoreLive>("Popup/PopupMoreLive");
                return;
            }

            CloseUI();

            if (LevelManager.HasInstance)
                LevelManager.Instance.RetryLevel();

            Debug.Log($"[PopupFail02] Retry — 클리어 시 보너스 {RETRY_BONUS_GOLD} 골드");
        }

        private void OnHomeClicked()
        {
            CloseUI();
            if (PopupManager.HasInstance) PopupManager.Instance.CloseAllPopups();

            // [#4] 전면 광고 — ③ Level Failed 나가기 지면 (interstitial_fail_quit). 조건 충족 시 오버레이 노출.
            // Try Again(OnRetryClicked)은 재도전 의지 보호를 위해 광고 X (명세 v1.2.30).
            if (AdManager.HasInstance)
                AdManager.Instance.TryShowInterstitial(AdManager.InterstitialPlacement.FailQuit);

            if (GameManager.HasInstance) GameManager.Instance.LoadScene(GameManager.SCENE_LOBBY);
        }
    }
}
