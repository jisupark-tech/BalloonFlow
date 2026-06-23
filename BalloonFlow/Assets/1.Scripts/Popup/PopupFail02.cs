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

        private const int OVERLAY_SORT_ORDER = 200; // PopupCanvas 기본값과 동일 — 사용자 요청 2026-06-12 (이전 260 → 200, Tutorial 위 강제 표시 정책 철회)
        private Canvas _overrideCanvas;

        // PopupFail02 Sorting Order 사양 (FX=240/ImageStage=243/Gold=244/TxtGoldOutline=247/TxtGold=248)
        private const int REWARD_ORDER_FX               = 240;
        private const int REWARD_ORDER_IMAGE_STAGE      = 243;
        private const int REWARD_ORDER_GOLD             = 244;
        private const int REWARD_ORDER_TXT_GOLD_OUTLINE = 247;
        private const int REWARD_ORDER_TXT_GOLD         = 248;

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
            EnsureOverlaySorting();
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
            // [#4] 단일버튼(BtnSingleFrame) = Try Again. RetryBtn(BtnHorizGreen)만 연결돼 BtnSingle 이 죽어있던 버그 수정.
            if (_frame != null && _frame.BtnSingle != null) _frame.BtnSingle.onClick.AddListener(OnRetryClicked);
            if (HomeBtn != null) HomeBtn.onClick.AddListener(OnHomeClicked);
            if (ExitBtn != null) ExitBtn.onClick.AddListener(OnHomeClicked);

            EnsureTopBarBinding();
        }

        /// <summary>
        /// PopupFail02 Canvas Sorting Order=200 부여 — 사용자 요청 2026-06-12.
        /// PopupCanvas 기본 sortingOrder(=200)와 동일 레벨로 표시. 이전(2026-06-04) Tutorial(=250) 위 강제 노출 정책은 철회.
        /// Canvas+GraphicRaycaster 런타임 부착 메커니즘은 prefab 바이너리 직렬화로 인스펙터 수정이 어려운 본 프로젝트 정책상 유지.
        /// </summary>
        private void EnsureOverlaySorting()
        {
            _overrideCanvas = GetComponent<Canvas>();
            if (_overrideCanvas == null) _overrideCanvas = gameObject.AddComponent<Canvas>();
            _overrideCanvas.overrideSorting = true;
            _overrideCanvas.sortingOrder = OVERLAY_SORT_ORDER;
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
        }

        private void EnsureTopBarBinding()
        {
            Transform topBar = FindChildRecursive(transform, "TopBarArea");
            Transform gold = topBar != null ? FindChildRecursive(topBar, "GoldPanel") : null;
            if (gold != null) GoldPanelFxFireUtil.DisableUnderGoldPanel(gold);
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

        // PopupResult.ApplyRewardLayerOrder 와 동일 매핑 — 단, PopupFail02 는 절대값 사양(FX=240/ImageStage=243/
        // Gold=244/TxtGoldOutline=247/TxtGold=248)을 그대로 부여 (parentCanvas+offset 패턴 미사용).
        private void ApplyRewardSortingOrder(Transform rewardRoot, string layer)
        {
            AssignChildCanvasOrder(rewardRoot, "ImageStage",     REWARD_ORDER_IMAGE_STAGE,      layer);
            AssignChildCanvasOrder(rewardRoot, "Gold",           REWARD_ORDER_GOLD,             layer);
            ApplyFxSubtreeOrder(rewardRoot,                      REWARD_ORDER_FX,               layer);
            AssignChildCanvasOrder(rewardRoot, "TxtGoldOutline", REWARD_ORDER_TXT_GOLD_OUTLINE, layer);
            AssignChildCanvasOrder(rewardRoot, "TxtGold",        REWARD_ORDER_TXT_GOLD,         layer);
        }

        private static void AssignChildCanvasOrder(Transform root, string nodeName, int order, string layer)
        {
            Transform node = FindChildRecursive(root, nodeName);
            if (node == null) return;
            var canvas = node.GetComponent<Canvas>();
            if (canvas == null) canvas = node.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingLayerName = layer;
            canvas.sortingOrder = order;
        }

        private static void ApplyFxSubtreeOrder(Transform rewardRoot, int order, string layer)
        {
            Transform fxNode = FindChildRecursive(rewardRoot, "FX");
            if (fxNode == null) return;

            var fxCanvas = fxNode.GetComponent<Canvas>();
            if (fxCanvas == null) fxCanvas = fxNode.gameObject.AddComponent<Canvas>();
            fxCanvas.overrideSorting = true;
            fxCanvas.sortingLayerName = layer;
            fxCanvas.sortingOrder = order;
            if (fxNode.GetComponent<GraphicRaycaster>() == null)
                fxNode.gameObject.AddComponent<GraphicRaycaster>();

            var particles = fxNode.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] == null) continue;
                if (!particles[i].gameObject.activeSelf)
                    particles[i].gameObject.SetActive(true);
                if (!particles[i].isPlaying)
                    particles[i].Play(true);
            }
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
            if (_frame != null && _frame.BtnSingle != null) _frame.BtnSingle.onClick.RemoveAllListeners();
            if (HomeBtn != null) HomeBtn.onClick.RemoveAllListeners();
            if (ExitBtn != null) ExitBtn.onClick.RemoveAllListeners();
        }

        private bool _lifeConsumed;

        private void OnEnable()
        {
            GoldPanelFxFireUtil.DisableUnderTopBarRoot(transform);

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

            // [v1.2.40] Title/Button 텍스트도 OnEnable에서 항상 주입 — Show() 미호출 진입 경로(PopupContinue→popup_fail02)에서
            // 프리팹 placeholder("Title"/"Resume")가 노출되던 P0 버그 수정.
            if (_frame != null)
            {
                _frame.ApplyDifficulty(diff);
                string failTitle = LevelManager.HasInstance
                    ? string.Format(LocalizationService.Get("popup.title.level"), LevelManager.Instance.CurrentLevelId)
                    : LocalizationService.Get("popup.title.level_failed");
                _frame.SetTitle(failTitle);
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText("Retry");
                _frame.ShowExitButton(true);
            }
            UpdateHardLevelOption(diff);

            // Reward subtree sorting order 부여 — Canvas.overrideSorting 은 GameObject.activeInHierarchy=false 일 때
            // silently 무시되므로 호출 전 활성화 보장 (PopupResult.cs:197-199 동일 메커니즘).
            Transform rewardRoot = FindChildRecursive(transform, "Reward");
            if (rewardRoot != null)
            {
                // ROLLBACK_FAIL02_HIDE_RESULT_REWARD_20260623:
                // PopupFail02 can be opened from PopupSettings -> PopupQuit -> Quit. This
                // prefab carries PopupResult-style Reward/Gold children, but failed/quit flow
                // must not display the clear reward coin image.
                rewardRoot.gameObject.SetActive(false);
            }
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
                string failTitle = LevelManager.HasInstance
                    ? string.Format(LocalizationService.Get("popup.title.level"), LevelManager.Instance.CurrentLevelId)
                    : LocalizationService.Get("popup.title.level_failed");
                _frame.SetTitle(failTitle);
                // [#4] 명세 ③ Level Failed = [Retry] 단일버튼 + [X](나가기→로비). 단일버튼(BtnSingle) 레이아웃 사용.
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText("Retry");
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
                string label = LocalizationService.Get(
                    difficulty == DifficultyPurpose.SuperHard ? "ui.superhard" : "ui.hardlevel");
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

            CloseForRetry();

            if (LevelManager.HasInstance)
                LevelManager.Instance.RetryLevel();

            Debug.Log($"[PopupFail02] Retry — 클리어 시 보너스 {RETRY_BONUS_GOLD} 골드");
        }

        private void CloseForRetry()
        {
            // ROLLBACK_FAIL02_RETRY_POPUPMANAGER_CLOSE_20260622:
            // popup_fail02 is usually opened by PopupManager. Calling UIBase.CloseUI()
            // hides the GameObject but leaves PopupManager.ActivePopupId as popup_fail02,
            // so a second fail can be queued behind an inactive popup and visible controls
            // look dead. Close through PopupManager when it owns this popup.
            if (PopupManager.HasInstance && PopupManager.Instance.ActivePopupId == "popup_fail02")
            {
                PopupManager.Instance.ClosePopup("popup_fail02");
                return;
            }

            CloseUI();
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
