using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// 인게임 HUD UI. Resources/UI/UIHud 프리팹에서 로드.
    /// HUDController가 BindView로 참조 연결.
    /// </summary>
    public class UIHud : UIBase
    {
        // [2026-05-13] UIBase.OpenUI/CloseUI 중앙화 시 self-trigger 방지 — UIHud 자체는 popup 이 아니다.
        protected override bool TriggersHudPopupAnimation => false;

        #region Constants — Lock Colors

        private static readonly Color LOCK_NORMAL    = new Color(1f, 1f, 1f); // #FFFFFF
        private static readonly Color LOCK_HARD      = new Color(1f, 1f, 1f); // #FFFFFF
        private static readonly Color LOCK_SUPERHARD = new Color(1f, 1f, 1f); // #FFFFFF

        private const string ANIM_SPEED_X1 = "SpeedBtnX1";
        private const string ANIM_SPEED_X2 = "SpeedBtnX2";

        #endregion

        [Header("[Top — 레벨/골드]")]
        [SerializeField] private TMP_Text _txtLevelOutline;
        [SerializeField] private TMP_Text _txtLevel;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private TMP_Text _goldText;
        [SerializeField] private Button _goldPlusButton;

        [Header("[LvPanel — 진행률 게이지 Image (popped/total, fillAmount)]")]
        [SerializeField] private Image _fillGaugeImage;
        [SerializeField] private TMP_Text _txtPercentage;
        [SerializeField] private TMP_Text _txtPercentageOutline;

        [Header("[Top — x2 속도 토글 (우상단)]")]
        [SerializeField] private Button _speedToggleButton;
        [SerializeField] private GameObject _speedToggleOnVisual;
        [SerializeField] private GameObject _speedToggleOffVisual;
        [SerializeField] private Image _imgSpeedColor;
        [SerializeField] private Sprite _sprSpeedNormal;
        [SerializeField] private Sprite _sprSpeedHard;
        [SerializeField] private Sprite _sprSpeedSuperHard;
        [SerializeField] private TMP_Text _txtSpeed;
        [SerializeField] private TMP_Text _txtSpeedOutline;
        [SerializeField] private Animator _animatorSpeedBtn;

        [Header("[TxtSpeedOutline — 난이도별 Material Preset]")]
        [SerializeField] private Material _matSpeedOutlineNormal;
        [SerializeField] private Material _matSpeedOutlineHard;
        [SerializeField] private Material _matSpeedOutlineSuperHard;

        [Header("[HUD Top — 팝업 오픈 연출용]")]
        [Tooltip("인게임 팝업 오픈 시 -60→-100→0 sequence tween 적용 — Inspector에서 HUD_Top RectTransform 와이어")]
        [SerializeField] private RectTransform _hudTopRoot;

        [Header("[Bottom Panel — 부스터 아이템]")]
        [Tooltip("아이템 사용 popup 열릴 때 화면 밖 -270 으로 tween — Inspector 에서 BottomPanel root RectTransform 할당")]
        [SerializeField] private RectTransform _bottomPanelRoot;
        [SerializeField] private Button _itemBtnShuffle;
        [SerializeField] private Button _itemBtnRemove;
        [SerializeField] private Button _itemBtnHand;
        [SerializeField] private TMP_Text _itemCountShuffle;
        [SerializeField] private TMP_Text _itemCountRemove;
        [SerializeField] private TMP_Text _itemCountHand;
        [SerializeField] private TMP_Text _itemCountOutlineShuffle;
        [SerializeField] private TMP_Text _itemCountOutlineRemove;
        [SerializeField] private TMP_Text _itemCountOutlineHand;
        [SerializeField] private GameObject _countBadgeShuffle;
        [SerializeField] private GameObject _countBadgeRemove;
        [SerializeField] private GameObject _countBadgeHand;
        [SerializeField] private GameObject _imgPlusShuffle;
        [SerializeField] private GameObject _imgPlusRemove;
        [SerializeField] private GameObject _imgPlusHand;

        [Header("[Lock Icons — 미해금 시 표시]")]
        [SerializeField] private Image _iconLockShuffle;
        [SerializeField] private Image _iconLockRemove;
        [SerializeField] private Image _iconLockHand;

        [Header("[Lock 레벨 텍스트 — Lv.X 표시 (각 본문 + outline 짝)]")]
        [SerializeField] private TMP_Text _txtLockShuffle;
        [SerializeField] private TMP_Text _txtLockShuffleOutline;
        [SerializeField] private TMP_Text _txtLockRemove;
        [SerializeField] private TMP_Text _txtLockRemoveOutline;
        [SerializeField] private TMP_Text _txtLockHand;
        [SerializeField] private TMP_Text _txtLockHandOutline;

        [Header("[Icon Items — 미해금 시 비활성화]")]
        [SerializeField] private GameObject _iconItemShuffle;
        [SerializeField] private GameObject _iconItemRemove;
        [SerializeField] private GameObject _iconItemHand;

        [Header("[색상 선택 패널 — Color Remove용]")]
        [SerializeField] private GameObject _colorPanel;
        [SerializeField] private Button _color0Button;
        [SerializeField] private Button _color1Button;
        [SerializeField] private Button _color2Button;
        [SerializeField] private Button _color3Button;

        [Header("[아이템 패널 — 난이도별 리소스]")]
        [SerializeField] private Image _imgItemPanelBg;
        [SerializeField] private Sprite _sprItemPanelNormal;
        [SerializeField] private Sprite _sprItemPanelHard;
        [SerializeField] private Sprite _sprItemPanelSuperHard;

        [Header("[아이템 버튼 — 난이도별 리소스]")]
        [SerializeField] private Image _imgBtnShuffle;
        [SerializeField] private Image _imgBtnRemove;
        [SerializeField] private Image _imgBtnHand;
        [SerializeField] private Sprite _sprItemBtnNormal;
        [SerializeField] private Sprite _sprItemBtnHard;
        [SerializeField] private Sprite _sprItemBtnSuperHard;

        [Header("[Settings 버튼 — 난이도별 리소스]")]
        [SerializeField] private Image _imgSettingColor;
        [SerializeField] private Sprite _sprSettingNormal;
        [SerializeField] private Sprite _sprSettingHard;
        [SerializeField] private Sprite _sprSettingSuperHard;

        [Header("[배경/오버레이]")]
        [SerializeField] private Image _backgroundImage;
        [SerializeField] private Image _imgBgColor;
        [SerializeField] private Sprite _sprBgColorNormal;
        [SerializeField] private Sprite _sprBgColorHard;
        [SerializeField] private Sprite _sprBgColorSuperHard;

        [Header("[LvFrame — 난이도별 리소스]")]
        [SerializeField] private Image _imgLvFrame;
        [SerializeField] private Sprite _sprLvFrameNormal;
        [SerializeField] private Sprite _sprLvFrameHard;
        [SerializeField] private Sprite _sprLvFrameSuperHard;

        [Header("[Lv 아이콘 — 난이도별 리소스]")]
        [SerializeField] private Image _imgLvIcon;
        [SerializeField] private Sprite _sprLvIconNormal;
        [SerializeField] private Sprite _sprLvIconHard;
        [SerializeField] private Sprite _sprLvIconSuperHard;

        [Header("[TxtLVOutline — 난이도별 Material Preset]")]
        [SerializeField] private TMP_Text _txtLVOutline;
        [SerializeField] private Material _matLvOutlineNormal;
        [SerializeField] private Material _matLvOutlineHard;
        [SerializeField] private Material _matLvOutlineSuperHard;

        [Header("[TxtNumberOutline — 난이도별 Material Preset]")]
        [SerializeField] private TMP_Text _txtNumberOutline;
        [SerializeField] private Material _matNumberOutlineNormal;
        [SerializeField] private Material _matNumberOutlineHard;
        [SerializeField] private Material _matNumberOutlineSuperHard;

        [Header("[TxtPercentageOutline — 난이도별 Material Preset]")]
        [SerializeField] private Material _matPercentageOutlineNormal;
        [SerializeField] private Material _matPercentageOutlineHard;
        [SerializeField] private Material _matPercentageOutlineSuperHard;

        private bool _isMapMakerMode;
        private DifficultyPurpose _currentDifficulty = DifficultyPurpose.Normal;

        #region Accessors

        public Button SettingsButton => _settingsButton;
        public TMP_Text LevelText => _txtLevel;
        public TMP_Text LevelOutlineText => _txtLevelOutline;
        public TMP_Text GoldText => _goldText;
        public Button GoldPlusButton => _goldPlusButton;
        public Image BackgroundImage => _backgroundImage;
        public Button ItemBtnShuffle => _itemBtnShuffle;
        public Button ItemBtnRemove => _itemBtnRemove;
        public Button ItemBtnHand => _itemBtnHand;

        // [2026-05-12] BottomPanel hide / show — UseItem popup 열릴 때 -270 으로 tween. 닫힐 때 원위치 복귀.
        // 기존 cutout/dim 시스템 (Hole in UI) 대신 패널 자체 비키게 — 보관함/풍선 dim 없이 시각 노출.
        private const float BOTTOM_PANEL_HIDE_Y = -270f;
        // [2026-05-12] 0.25s → 0.5s — 너무 빨라서 tween 인지 어려움.
        private const float BOTTOM_PANEL_TWEEN_DUR = 0.5f;
        private Vector2 _bottomPanelOrigPos;
        private bool _bottomPanelOrigCached;
        private DG.Tweening.Tweener _bottomPanelTween;

        // [2026-05-13] 인게임 팝업 오픈 연출 — 사용자 스펙 절대 anchoredPosition.y값 (-60→-100→160 sequence)
        private const float HUD_TOP_OPEN_START        = -60f;
        private const float HUD_TOP_OPEN_MID          = -100f;
        private const float HUD_TOP_OPEN_END          = 160f;
        private const float BOTTOM_PANEL_POPUP_OPEN_Y = -300f;
        private const float POPUP_OPEN_TWEEN_DUR      = 0.5f; // HUD_Top 전체 duration = BottomPanel duration

        // [2026-05-13] 인게임 enter 슬라이드-인 연출 — prefab 캐시(-300/-60)에 의존하지 않는 하드코딩 시작/끝값.
        // 의미 분리: OPEN_END / POPUP_OPEN_Y 와 수치는 같아도 용도가 다르다 (popup vs ingame enter).
        private const float HUD_TOP_INGAME_HIDDEN_Y      = 160f;  // 화면 밖 위쪽 시작 위치
        private const float HUD_TOP_INGAME_REST_Y        = -60f;  // 인게임 정착 위치
        private const float BOTTOM_PANEL_INGAME_HIDDEN_Y = -300f; // 화면 밖 아래쪽 시작 위치
        private const float BOTTOM_PANEL_INGAME_REST_Y   = 0f;    // 인게임 정착 위치
        private Vector2 _hudTopOrigPos;
        private bool _hudTopOrigCached;
        private DG.Tweening.Sequence _popupOpenSeq;

        // [2026-05-13] 중첩 팝업 reference-count — 첫 open 에서만 Open 연출, 마지막 close 에서만 Close 연출.
        private int _popupOpenCount;

        // [2026-05-13 rework] PlayStageEndPanelShift 전용 sticky latch — 일반 popup open/close 경로에는 적용 안 됨. PlayIngameEnterAnimation 에서만 reset.
        private bool _stageEndShiftLatch;

        public void HideBottomPanel()
        {
            if (_bottomPanelRoot == null)
            {
                Debug.LogWarning("[UIHud] HideBottomPanel: _bottomPanelRoot 미할당. Inspector 에서 BottomPanel RectTransform 와이어 필요.");
                return;
            }
            if (!_bottomPanelOrigCached)
            {
                _bottomPanelOrigPos = _bottomPanelRoot.anchoredPosition;
                _bottomPanelOrigCached = true;
            }
            _bottomPanelTween?.Kill();
            float targetY = _bottomPanelOrigPos.y + BOTTOM_PANEL_HIDE_Y;
            Debug.Log($"[UIHud] HideBottomPanel: origin.y={_bottomPanelOrigPos.y:F1} → target.y={targetY:F1}");
            _bottomPanelTween = _bottomPanelRoot.DOAnchorPosY(targetY, BOTTOM_PANEL_TWEEN_DUR)
                .SetEase(DG.Tweening.Ease.OutCubic)
                .SetUpdate(true); // timeScale=0 (PauseManager) 환경에서도 동작
        }

        public void ShowBottomPanel()
        {
            if (_bottomPanelRoot == null) return;
            if (!_bottomPanelOrigCached) return;
            _bottomPanelTween?.Kill();
            Debug.Log($"[UIHud] ShowBottomPanel: → origin.y={_bottomPanelOrigPos.y:F1}");
            _bottomPanelTween = _bottomPanelRoot.DOAnchorPosY(_bottomPanelOrigPos.y, BOTTOM_PANEL_TWEEN_DUR)
                .SetEase(DG.Tweening.Ease.OutCubic)
                .SetUpdate(true);
        }

        /// <summary>
        /// UIBase 가 popup open 시 호출 — count++. 첫 popup 에서만 Open 연출 트리거.
        /// [2026-05-13 rework] 정책 반전: 일반 popup open/close 는 즉시 시프트/복귀 연출. _stageEndShiftLatch ON 일 때만 재트리거 차단.
        /// </summary>
        public void NotifyPopupOpened()
        {
            _popupOpenCount++;
            if (_popupOpenCount == 1 && !_stageEndShiftLatch)
            {
                PlayPopupOpenAnimation();
            }
        }

        /// <summary>
        /// UIBase 가 popup close 시 호출 — count-- 및 마지막 popup 이 닫히면 HUD 즉시 복귀 연출.
        /// [2026-05-13 rework] 정책 반전: popup close 시 즉시 HUD 복귀 연출(HUD_Top 160→-60, BottomPanel -300→0).
        /// 단, _stageEndShiftLatch ON(스테이지 종료 결과 팝업 흐름)일 때는 복귀 안 함 — 곧 PlayIngameEnterAnimation 이 처리.
        /// </summary>
        public void NotifyPopupClosed()
        {
            _popupOpenCount = Mathf.Max(0, _popupOpenCount - 1);
            if (_popupOpenCount == 0 && !_stageEndShiftLatch)
            {
                PlayPopupCloseAnimation();
            }
        }

        /// <summary>스테이지 종료(승/패) 시 popup 노출 직전 panel shift — open 연출 후 latch ON → 이후 popup close 가 복귀 연출 트리거하지 않도록 latch ON, 다음 스테이지 enter 애니에서 -300/160 → 원위치 복귀.</summary>
        public void PlayStageEndPanelShift()
        {
            PlayPopupOpenAnimation();
            _stageEndShiftLatch = true;
        }

        /// <summary>인게임 팝업 오픈 연출 — HUD_Top: -60→-100→0 sequence, BottomPanel: 0→-300. 동일 duration.</summary>
        public void PlayPopupOpenAnimation()
        {
            _popupOpenSeq?.Kill();
            _popupOpenSeq = DG.Tweening.DOTween.Sequence().SetUpdate(true);

            if (_hudTopRoot != null)
            {
                if (!_hudTopOrigCached) { _hudTopOrigPos = _hudTopRoot.anchoredPosition; _hudTopOrigCached = true; }
                // -60 → -100 → 0 (절반 duration씩 2단계)
                _hudTopRoot.anchoredPosition = new Vector2(_hudTopOrigPos.x, HUD_TOP_OPEN_START);
                float half = POPUP_OPEN_TWEEN_DUR * 0.5f;
                _popupOpenSeq.Join(_hudTopRoot.DOAnchorPosY(HUD_TOP_OPEN_MID, half).SetEase(Ease.OutCubic));
                _popupOpenSeq.Insert(half, _hudTopRoot.DOAnchorPosY(HUD_TOP_OPEN_END, half).SetEase(Ease.OutCubic));
            }
            else Debug.LogWarning("[UIHud] PlayPopupOpenAnimation: _hudTopRoot 미할당.");

            if (_bottomPanelRoot != null)
            {
                if (!_bottomPanelOrigCached) { _bottomPanelOrigPos = _bottomPanelRoot.anchoredPosition; _bottomPanelOrigCached = true; }
                _popupOpenSeq.Join(_bottomPanelRoot.DOAnchorPosY(BOTTOM_PANEL_POPUP_OPEN_Y, POPUP_OPEN_TWEEN_DUR).SetEase(Ease.OutCubic));
            }
        }

        /// <summary>인게임 팝업 close 시 HUD 복귀 연출 — NotifyPopupClosed 가 마지막 popup 닫힐 때 호출. HUD_Top: 현재→-60(REST), BottomPanel: 현재→0(REST). 사용자 스펙 절대값.</summary>
        public void PlayPopupCloseAnimation()
        {
            _popupOpenSeq?.Kill();
            _popupOpenSeq = DG.Tweening.DOTween.Sequence().SetUpdate(true);
            if (_hudTopRoot != null)
                _popupOpenSeq.Join(_hudTopRoot.DOAnchorPosY(HUD_TOP_INGAME_REST_Y, POPUP_OPEN_TWEEN_DUR).SetEase(Ease.OutCubic));
            if (_bottomPanelRoot != null)
                _popupOpenSeq.Join(_bottomPanelRoot.DOAnchorPosY(BOTTOM_PANEL_INGAME_REST_Y, POPUP_OPEN_TWEEN_DUR).SetEase(Ease.OutCubic));
        }

        /// <summary>로비→인게임 진입 시 등장 연출 — HUD_Top: 160→-60, BottomPanel: -300→0. 동일 duration. 화면 밖에서 슬라이드 인.</summary>
        public void PlayIngameEnterAnimation()
        {
            // [2026-05-13] 직전 스테이지 종료 latch / popup count 클린업 — 다음 스테이지 진입 직전에 초기화.
            _stageEndShiftLatch = false;
            _popupOpenCount = 0;

            _popupOpenSeq?.Kill();
            _popupOpenSeq = DG.Tweening.DOTween.Sequence().SetUpdate(true);

            // [2026-05-13] prefab 캐시(_hudTopOrigPos / _bottomPanelOrigPos)에 의존하지 않고 하드코딩 상수로 시작/끝값 결정.
            // 이전 버그: BottomPanel prefab이 anchoredPosition.y=-300 으로 저장되어 있어서 origPos.y(=-300)을 끝값으로 쓰면 -300→-300 no-op.
            if (_hudTopRoot != null)
            {
                float startX = _hudTopRoot.anchoredPosition.x;
                _hudTopOrigPos = new Vector2(startX, HUD_TOP_INGAME_REST_Y);
                _hudTopOrigCached = true;
                _hudTopRoot.anchoredPosition = new Vector2(startX, HUD_TOP_INGAME_HIDDEN_Y);
                _popupOpenSeq.Join(_hudTopRoot.DOAnchorPosY(HUD_TOP_INGAME_REST_Y, POPUP_OPEN_TWEEN_DUR).SetEase(Ease.OutCubic));
            }
            else Debug.LogWarning("[UIHud] PlayIngameEnterAnimation: _hudTopRoot 미할당.");

            if (_bottomPanelRoot != null)
            {
                float startX = _bottomPanelRoot.anchoredPosition.x;
                _bottomPanelOrigPos = new Vector2(startX, BOTTOM_PANEL_INGAME_REST_Y);
                _bottomPanelOrigCached = true;
                _bottomPanelRoot.anchoredPosition = new Vector2(startX, BOTTOM_PANEL_INGAME_HIDDEN_Y);
                _popupOpenSeq.Join(_bottomPanelRoot.DOAnchorPosY(BOTTOM_PANEL_INGAME_REST_Y, POPUP_OPEN_TWEEN_DUR).SetEase(Ease.OutCubic));
            }
        }

        /// <summary>로비→인게임 진입 시 시작 위치만 강제 세팅 (tween 없이 즉시) — origPos 캐시도 같이. UI 노출 1프레임 플릭커 차단용. tween 시작은 LevelManager.IsLoading/UIManager.IsFading 대기 후 PlayIngameEnterAnimation()이 처리.</summary>
        public void PrimeIngameEnterStartPos()
        {
            // [2026-05-13] origPos 캐시는 prefab anchoredPosition 이 아니라 REST_Y 로 명시 세팅.
            // 이전 버그: prefab이 -300/-60 으로 저장되어 있어 origPos.y 가 의도된 정착 위치와 달라짐 → popup close 복귀 위치 오류.
            if (_hudTopRoot != null)
            {
                float startX = _hudTopRoot.anchoredPosition.x;
                _hudTopOrigPos = new Vector2(startX, HUD_TOP_INGAME_REST_Y);
                _hudTopOrigCached = true;
                _hudTopRoot.anchoredPosition = new Vector2(startX, HUD_TOP_INGAME_HIDDEN_Y);
            }
            if (_bottomPanelRoot != null)
            {
                float startX = _bottomPanelRoot.anchoredPosition.x;
                _bottomPanelOrigPos = new Vector2(startX, BOTTOM_PANEL_INGAME_REST_Y);
                _bottomPanelOrigCached = true;
                _bottomPanelRoot.anchoredPosition = new Vector2(startX, BOTTOM_PANEL_INGAME_HIDDEN_Y);
            }
        }

        #endregion

        private void OnEnable()
        {
            // 스테이지 in-place 전환 시 GameSpeedController._toggleOn latch 와 HUD 비주얼/텍스트가 어긋날 수 있어,
            // OnLevelLoaded 발화에 맞춰 X1/X2 비주얼을 재동기화한다.
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
        }

        private void HandleLevelLoaded(OnLevelLoaded _)
        {
            RefreshSpeedToggleVisual();
        }

        private void Start()
        {
            WireButtons();
            if (_colorPanel != null) _colorPanel.SetActive(false);
            RefreshBoosterCounts();
            RefreshLockState();

            // ItemBtn 변형은 atlas 명 미확정 → Inspector 값 그대로
            if (ResourceManager.HasInstance)
            {
                var rm = ResourceManager.Instance;
                _sprSpeedNormal    = rm.UISpriteOr("ingameBtnFastNormal",    _sprSpeedNormal);
                _sprSpeedHard      = rm.UISpriteOr("ingameBtnFastHard",      _sprSpeedHard);
                _sprSpeedSuperHard = rm.UISpriteOr("ingameBtnFastSuperHard", _sprSpeedSuperHard);

                _sprItemPanelNormal    = rm.UISpriteOr("frameItemNormal",    _sprItemPanelNormal);
                _sprItemPanelHard      = rm.UISpriteOr("frameItemHard",      _sprItemPanelHard);
                _sprItemPanelSuperHard = rm.UISpriteOr("frameItemSuperHard", _sprItemPanelSuperHard);

                _sprSettingNormal    = rm.UISpriteOr("btnSettingNormal",    _sprSettingNormal);
                _sprSettingHard      = rm.UISpriteOr("btnSettingHard",      _sprSettingHard);
                _sprSettingSuperHard = rm.UISpriteOr("btnSettingSuperHard", _sprSettingSuperHard);

                _sprBgColorNormal    = rm.UISpriteOr(Const.SPR_FRAMEBOTTOMNORMAL,    _sprBgColorNormal);
                _sprBgColorHard      = rm.UISpriteOr(Const.SPR_FRAMEBOTTOMHARD,      _sprBgColorHard);
                _sprBgColorSuperHard = rm.UISpriteOr(Const.SPR_FRAMEBOTTOMSUPERHARD, _sprBgColorSuperHard);

                _sprLvFrameNormal    = rm.UISpriteOr(Const.SPR_INGAMEGAUGEBARNORMAL,    _sprLvFrameNormal);
                _sprLvFrameHard      = rm.UISpriteOr(Const.SPR_INGAMEGAUGEBARHARD,      _sprLvFrameHard);
                _sprLvFrameSuperHard = rm.UISpriteOr(Const.SPR_INGAMEGAUGEBARSUPERHARD, _sprLvFrameSuperHard);

                _sprLvIconNormal     = rm.UISpriteOr(Const.SPR_INGAMELVNORMAL,    _sprLvIconNormal);
                _sprLvIconHard       = rm.UISpriteOr(Const.SPR_INGAMELVHARD,      _sprLvIconHard);
                _sprLvIconSuperHard  = rm.UISpriteOr(Const.SPR_INGAMELVSUPERHARD, _sprLvIconSuperHard);
            }
        }

        private void OnDestroy()
        {
            UnwireButtons();
        }

        #region Public Methods

        /// <summary>MapMaker에서 진입 시 아이템 무한대 표기.</summary>
        public void SetMapMakerMode(bool isMapMaker)
        {
            _isMapMakerMode = isMapMaker;
            RefreshBoosterCounts();
            RefreshLockState();
        }

        /// <summary>현재 난이도 설정 (Lock 색상 + 아이템 패널 리소스).</summary>
        public void SetDifficulty(DifficultyPurpose difficulty)
        {
            _currentDifficulty = difficulty;
            RefreshLockState();
            ApplyItemPanelDifficulty(difficulty);
        }

        private void ApplyItemPanelDifficulty(DifficultyPurpose difficulty)
        {
            // 패널 배경
            Sprite panelSpr = difficulty switch
            {
                DifficultyPurpose.Hard      => _sprItemPanelHard,
                DifficultyPurpose.SuperHard  => _sprItemPanelSuperHard,
                _                            => _sprItemPanelNormal
            };
            if (_imgItemPanelBg != null && panelSpr != null)
                _imgItemPanelBg.sprite = panelSpr;

            // 버튼 리소스
            Sprite btnSpr = difficulty switch
            {
                DifficultyPurpose.Hard      => _sprItemBtnHard,
                DifficultyPurpose.SuperHard  => _sprItemBtnSuperHard,
                _                            => _sprItemBtnNormal
            };
            if (btnSpr != null)
            {
                if (_imgBtnShuffle != null) _imgBtnShuffle.sprite = btnSpr;
                if (_imgBtnRemove != null) _imgBtnRemove.sprite = btnSpr;
                if (_imgBtnHand != null) _imgBtnHand.sprite = btnSpr;
            }

            // Settings 버튼 리소스
            Sprite settingSpr = difficulty switch
            {
                DifficultyPurpose.Hard      => _sprSettingHard,
                DifficultyPurpose.SuperHard  => _sprSettingSuperHard,
                _                            => _sprSettingNormal
            };
            if (_imgSettingColor != null && settingSpr != null)
                _imgSettingColor.sprite = settingSpr;

            // Speed 토글 버튼 리소스
            Sprite speedSpr = difficulty switch
            {
                DifficultyPurpose.Hard      => _sprSpeedHard,
                DifficultyPurpose.SuperHard => _sprSpeedSuperHard,
                _                           => _sprSpeedNormal
            };
            if (_imgSpeedColor != null && speedSpr != null)
                _imgSpeedColor.sprite = speedSpr;

            // Speed 텍스트 아웃라인 머티리얼
            Material speedOutlineMat = difficulty switch
            {
                DifficultyPurpose.Hard      => _matSpeedOutlineHard,
                DifficultyPurpose.SuperHard => _matSpeedOutlineSuperHard,
                _                           => _matSpeedOutlineNormal
            };
            if (speedOutlineMat != null && _txtSpeedOutline != null) _txtSpeedOutline.fontSharedMaterial = speedOutlineMat;

            // 배경 색상 (frameBottom)
            Sprite bgSpr = difficulty switch
            {
                DifficultyPurpose.Hard      => _sprBgColorHard,
                DifficultyPurpose.SuperHard => _sprBgColorSuperHard,
                _                           => _sprBgColorNormal
            };
            if (_imgBgColor != null && bgSpr != null)
                _imgBgColor.sprite = bgSpr;

            // LvFrame 리소스
            Sprite lvFrameSpr = difficulty switch
            {
                DifficultyPurpose.Hard      => _sprLvFrameHard,
                DifficultyPurpose.SuperHard => _sprLvFrameSuperHard,
                _                           => _sprLvFrameNormal
            };
            if (_imgLvFrame != null && lvFrameSpr != null)
                _imgLvFrame.sprite = lvFrameSpr;

            // Lv 아이콘 리소스
            Sprite lvIconSpr = difficulty switch
            {
                DifficultyPurpose.Hard      => _sprLvIconHard,
                DifficultyPurpose.SuperHard => _sprLvIconSuperHard,
                _                           => _sprLvIconNormal
            };
            if (_imgLvIcon != null && lvIconSpr != null)
                _imgLvIcon.sprite = lvIconSpr;

            // Lv 텍스트 아웃라인 머티리얼
            Material lvOutlineMat = difficulty switch
            {
                DifficultyPurpose.Hard      => _matLvOutlineHard,
                DifficultyPurpose.SuperHard => _matLvOutlineSuperHard,
                _                           => _matLvOutlineNormal
            };
            if (lvOutlineMat != null && _txtLVOutline != null) _txtLVOutline.fontSharedMaterial = lvOutlineMat;

            // Number 텍스트 아웃라인 머티리얼
            Material numberOutlineMat = difficulty switch
            {
                DifficultyPurpose.Hard      => _matNumberOutlineHard,
                DifficultyPurpose.SuperHard => _matNumberOutlineSuperHard,
                _                           => _matNumberOutlineNormal
            };
            if (numberOutlineMat != null && _txtNumberOutline != null) _txtNumberOutline.fontSharedMaterial = numberOutlineMat;

            // Percentage 텍스트 아웃라인 머티리얼
            Material percentageOutlineMat = difficulty switch
            {
                DifficultyPurpose.Hard      => _matPercentageOutlineHard,
                DifficultyPurpose.SuperHard => _matPercentageOutlineSuperHard,
                _                           => _matPercentageOutlineNormal
            };
            if (percentageOutlineMat != null && _txtPercentageOutline != null) _txtPercentageOutline.fontSharedMaterial = percentageOutlineMat;
        }

        public void SetLevel(int _levelId)
        {
            if (_txtLevel != null) _txtLevel.SetText("Level {0}", _levelId);
            if (_txtLevelOutline != null) _txtLevelOutline.SetText("Level {0}", _levelId);
        }

        public void SetGold(int _amount)
        {
            if (_goldText != null) _goldText.text = _amount.ToString("N0");
        }

        /// <summary>
        /// 진행률 표시: 공격한 풍선 / 전체 풍선 비율을 슬라이더와 텍스트("XX%")로 갱신.
        /// </summary>
        public void SetProgress(int popped, int total)
        {
            float ratio = total > 0 ? Mathf.Clamp01((float)popped / total) : 0f;
            if (_fillGaugeImage != null) { _fillGaugeImage.type = Image.Type.Filled; _fillGaugeImage.fillAmount = ratio; }

            int percent = Mathf.RoundToInt(ratio * 100f);
            if (_txtPercentage != null) _txtPercentage.SetText("{0}%", percent);
            if (_txtPercentageOutline != null) _txtPercentageOutline.SetText("{0}%", percent);
        }

        public void RefreshBoosterCounts()
        {
            if (_isMapMakerMode || GameManager.IsTestItemMode || GameManager.IsTestPlayMode)
            {
                SetCountText(_itemCountShuffle, "\u221E"); // ∞
                SetCountText(_itemCountRemove, "\u221E");
                SetCountText(_itemCountHand, "\u221E");
                SetCountText(_itemCountOutlineShuffle, "\u221E");
                SetCountText(_itemCountOutlineRemove, "\u221E");
                SetCountText(_itemCountOutlineHand, "\u221E");
                ApplyCountBadgeContent(_imgPlusShuffle, _itemCountShuffle, _itemCountOutlineShuffle, true);
                ApplyCountBadgeContent(_imgPlusRemove,  _itemCountRemove,  _itemCountOutlineRemove,  true);
                ApplyCountBadgeContent(_imgPlusHand,    _itemCountHand,    _itemCountOutlineHand,    true);
                return;
            }

            if (!BoosterManager.HasInstance) return;
            int shuffleCount = BoosterManager.Instance.GetBoosterCount(BoosterManager.SHUFFLE);
            int removeCount  = BoosterManager.Instance.GetBoosterCount(BoosterManager.COLOR_REMOVE);
            int handCount    = BoosterManager.Instance.GetBoosterCount(BoosterManager.HAND);

            SetCountText(_itemCountShuffle, shuffleCount.ToString());
            SetCountText(_itemCountRemove,  removeCount.ToString());
            SetCountText(_itemCountHand,    handCount.ToString());
            SetCountText(_itemCountOutlineShuffle, shuffleCount.ToString());
            SetCountText(_itemCountOutlineRemove,  removeCount.ToString());
            SetCountText(_itemCountOutlineHand,    handCount.ToString());

            ApplyCountBadgeContent(_imgPlusShuffle, _itemCountShuffle, _itemCountOutlineShuffle, shuffleCount >= 1);
            ApplyCountBadgeContent(_imgPlusRemove,  _itemCountRemove,  _itemCountOutlineRemove,  removeCount  >= 1);
            ApplyCountBadgeContent(_imgPlusHand,    _itemCountHand,    _itemCountOutlineHand,    handCount    >= 1);
        }

        /// <summary>수량/+ 토글: hasItem=true(수량 1+ 또는 무한) → text 노출 + plus 숨김. false(0) → plus 노출 + text 숨김.</summary>
        private static void ApplyCountBadgeContent(GameObject imgPlus, TMP_Text txt, TMP_Text txtOutline, bool hasItem)
        {
            if (imgPlus != null) imgPlus.SetActive(!hasItem);
            if (txt != null) txt.gameObject.SetActive(hasItem);
            if (txtOutline != null) txtOutline.gameObject.SetActive(hasItem);
        }

        /// <summary>Lock 아이콘 + Lv.X 텍스트 갱신. 미해금 → Lock 표시 + 난이도 색상 + 해금 레벨.</summary>
        public void RefreshLockState()
        {
            if (_isMapMakerMode || GameManager.IsTestItemMode || GameManager.IsTestPlayMode)
            {
                SetLockIcon(_iconLockHand, false, _currentDifficulty);
                SetLockIcon(_iconLockShuffle, false, _currentDifficulty);
                SetLockIcon(_iconLockRemove, false, _currentDifficulty);
                SetIconItemVisible(_iconItemHand, true);
                SetIconItemVisible(_iconItemShuffle, true);
                SetIconItemVisible(_iconItemRemove, true);
                // 해금 상태에선 Lv.X 텍스트 모두 숨김
                SetLockText(_txtLockHand, _txtLockHandOutline, false, 0);
                SetLockText(_txtLockShuffle, _txtLockShuffleOutline, false, 0);
                SetLockText(_txtLockRemove, _txtLockRemoveOutline, false, 0);
                // Unlocked → CountBadge 노출 (∞ 표기를 위해)
                SetCountBadgeVisible(_countBadgeShuffle, true);
                SetCountBadgeVisible(_countBadgeRemove,  true);
                SetCountBadgeVisible(_countBadgeHand,    true);
                return;
            }

            if (!BoosterManager.HasInstance) return;

            bool handLocked = !BoosterManager.Instance.IsBoosterUnlocked(BoosterManager.HAND);
            bool shuffleLocked = !BoosterManager.Instance.IsBoosterUnlocked(BoosterManager.SHUFFLE);
            bool removeLocked = !BoosterManager.Instance.IsBoosterUnlocked(BoosterManager.COLOR_REMOVE);

            SetLockIcon(_iconLockHand, handLocked, _currentDifficulty);
            SetLockIcon(_iconLockShuffle, shuffleLocked, _currentDifficulty);
            SetLockIcon(_iconLockRemove, removeLocked, _currentDifficulty);

            // Lv.X 표시 — 잠긴 booster 만 텍스트 활성, 해금되면 숨김
            int handUnlock    = GetUnlockLevel(BoosterManager.HAND);
            int shuffleUnlock = GetUnlockLevel(BoosterManager.SHUFFLE);
            int removeUnlock  = GetUnlockLevel(BoosterManager.COLOR_REMOVE);

            SetLockText(_txtLockHand,    _txtLockHandOutline,    handLocked,    handUnlock);
            SetLockText(_txtLockShuffle, _txtLockShuffleOutline, shuffleLocked, shuffleUnlock);
            SetLockText(_txtLockRemove,  _txtLockRemoveOutline,  removeLocked,  removeUnlock);

            // 미해금 시 IconItem 비활성화
            SetIconItemVisible(_iconItemHand, !handLocked);
            SetIconItemVisible(_iconItemShuffle, !shuffleLocked);
            SetIconItemVisible(_iconItemRemove, !removeLocked);

            // Lock 시 CountBadge 비활성 → IconLock / Lv.X 표시 영역과 충돌 방지
            SetCountBadgeVisible(_countBadgeShuffle, !shuffleLocked);
            SetCountBadgeVisible(_countBadgeRemove,  !removeLocked);
            SetCountBadgeVisible(_countBadgeHand,    !handLocked);
        }

        private static void SetCountBadgeVisible(GameObject badge, bool visible)
        {
            if (badge != null) badge.SetActive(visible);
        }

        #endregion

        #region Button Wiring

        private void WireButtons()
        {
            if (_itemBtnShuffle != null) _itemBtnShuffle.onClick.AddListener(OnShuffleClicked);
            if (_itemBtnRemove != null) _itemBtnRemove.onClick.AddListener(OnColorRemoveClicked);
            if (_itemBtnHand != null) _itemBtnHand.onClick.AddListener(OnHandClicked);
            if (_color0Button != null) _color0Button.onClick.AddListener(() => OnColorPicked(0));
            if (_color1Button != null) _color1Button.onClick.AddListener(() => OnColorPicked(1));
            if (_color2Button != null) _color2Button.onClick.AddListener(() => OnColorPicked(2));
            if (_color3Button != null) _color3Button.onClick.AddListener(() => OnColorPicked(3));
            if (_speedToggleButton != null) _speedToggleButton.onClick.AddListener(OnSpeedToggleClicked);
            RefreshSpeedToggleVisual();
        }

        private void UnwireButtons()
        {
            if (_itemBtnShuffle != null) _itemBtnShuffle.onClick.RemoveAllListeners();
            if (_itemBtnRemove != null) _itemBtnRemove.onClick.RemoveAllListeners();
            if (_itemBtnHand != null) _itemBtnHand.onClick.RemoveAllListeners();
            if (_color0Button != null) _color0Button.onClick.RemoveAllListeners();
            if (_color1Button != null) _color1Button.onClick.RemoveAllListeners();
            if (_color2Button != null) _color2Button.onClick.RemoveAllListeners();
            if (_color3Button != null) _color3Button.onClick.RemoveAllListeners();
            if (_speedToggleButton != null) _speedToggleButton.onClick.RemoveAllListeners();
        }

        private void OnSpeedToggleClicked()
        {
            if (GameSpeedController.HasInstance)
                GameSpeedController.Instance.ToggleSpeedBoost();
            RefreshSpeedToggleVisual();
        }

        private void RefreshSpeedToggleVisual()
        {
            bool on = GameSpeedController.HasInstance && GameSpeedController.Instance.ToggleOn;
            if (_speedToggleOnVisual != null) _speedToggleOnVisual.SetActive(on);
            if (_speedToggleOffVisual != null) _speedToggleOffVisual.SetActive(!on);

            string speedTxt = on ? "X2" : "X1";
            if (_txtSpeed != null) _txtSpeed.text = speedTxt;
            if (_txtSpeedOutline != null) _txtSpeedOutline.text = speedTxt;

            if (_animatorSpeedBtn != null)
                _animatorSpeedBtn.Play(on ? ANIM_SPEED_X2 : ANIM_SPEED_X1, 0, 0f);
        }

        #endregion

        #region Booster Handlers

        private void OnShuffleClicked()
        {
            HandleBoosterButton(BoosterManager.SHUFFLE);
        }

        private void OnColorRemoveClicked()
        {
            HandleBoosterButton(BoosterManager.COLOR_REMOVE);
        }

        private void OnHandClicked()
        {
            HandleBoosterButton(BoosterManager.HAND);
        }

        /// <summary>
        /// 부스터 버튼 공통 처리.
        /// 미해금 → 해금 팝업, 재고 없음 → 구매 팝업, 재고 있음 → 사용 확인 팝업.
        /// </summary>
        private void HandleBoosterButton(string boosterType)
        {
            if (!BoosterManager.HasInstance) return;

            // MapMaker / TestItem: 직접 사용 (팝업 없이)
            if (_isMapMakerMode || GameManager.IsTestItemMode || GameManager.IsTestPlayMode)
            {
                if (BoosterManager.Instance.GetBoosterCount(boosterType) <= 0)
                    BoosterManager.Instance.AddBooster(boosterType, 1);
                BoosterManager.Instance.UseBooster(boosterType);
                RefreshBoosterCounts();
                return;
            }

            // 미해금 → 토스트
            if (!BoosterManager.Instance.IsBoosterUnlocked(boosterType))
            {
                int unlockLevel = boosterType switch
                {
                    BoosterManager.SELECT_TOOL  => 9,
                    BoosterManager.SHUFFLE      => 12,
                    BoosterManager.COLOR_REMOVE => 15,
                    _                           => 1
                };
                ShowToast($"Unlocks at Level {unlockLevel}");
                return;
            }

            // 재고 없음 → 구매 팝업
            if (BoosterManager.Instance.GetBoosterCount(boosterType) <= 0)
            {
                ShowBuyPopup(boosterType);
                return;
            }

            // Shuffle은 즉시 실행 (팝업 없이)
            if (boosterType == BoosterManager.SHUFFLE)
            {
                BoosterManager.Instance.UseBooster(boosterType);
                RefreshBoosterCounts();
                return;
            }

            // Hand/Remove → UseItem 팝업 (Dim + Cutout)
            ShowUseItemPopup(boosterType);
        }

        private void ShowUseItemPopup(string boosterType)
        {
            if (!UIManager.HasInstance) return;
            var popup = UIManager.Instance.OpenUI<PopupUseItem>("Popup/UseItem");
            Debug.Log($"[UIHud] ShowUseItemPopup: popup={popup != null}, booster={boosterType}");
            if (popup == null) return;

            string desc = GetBoosterDescription(boosterType);
            popup.Show(boosterType, desc,
                onConfirm: () =>
                {
                    BoosterManager.Instance.UseBooster(boosterType);
                    RefreshBoosterCounts();
                });
        }

        private void ShowBuyPopup(string boosterType)
        {
            if (!UIManager.HasInstance) return;
            var popup = UIManager.Instance.OpenUI<PopupBuyItem>("Popup/PopupBuyItem");
            if (popup == null) return;

            int price = BoosterManager.Instance.GetBoosterPrice(boosterType);
            Sprite spr = popup.GetBoosterSprite(boosterType);
            popup.ShowBuy("Buy Item", spr, "x3", price,
                onConfirm: () =>
                {
                    if (BoosterManager.Instance.PurchaseBooster(boosterType))
                    {
                        RefreshBoosterCounts();

                        // 인게임 결제 성공 → 가벼운 토스트로 알림
                        if (UIManager.HasInstance)
                        {
                            Transform parent = UIManager.Instance.PopupTr ?? UIManager.Instance.UiTr;
                            if (parent != null)
                                TxtToast.Spawn(parent, "Purchase successful!", new Vector2(0f, -1022f));
                        }
                    }
                    else
                    {
                        // 결제 실패
                        var err = UIManager.Instance.OpenUI<PopupError>("Popup/PopupError");
                        if (err != null) err.ShowPaymentFailed("Not enough coins.");
                    }
                });
        }

        private void ShowUnlockPopup(string boosterType)
        {
            if (!UIManager.HasInstance) return;
            var popup = UIManager.Instance.OpenUI<PopupBuyItem>("Popup/PopupBuyItem");
            if (popup == null) return;

            int unlockLevel = boosterType switch
            {
                BoosterManager.SELECT_TOOL  => 9,
                BoosterManager.SHUFFLE      => 12,
                BoosterManager.COLOR_REMOVE => 15,
                _                           => 1
            };

            Sprite spr = popup.GetBoosterSprite(boosterType);
            popup.ShowUnlock("Unlock", spr, unlockLevel);
        }

        private void OnColorPicked(int color)
        {
            if (BoosterExecutor.HasInstance)
                BoosterExecutor.Instance.OnColorSelected(color);
            if (_colorPanel != null) _colorPanel.SetActive(false);
        }

        #endregion

        #region Lock Icon

        private static void SetLockIcon(Image lockIcon, bool locked, DifficultyPurpose difficulty)
        {
            if (lockIcon == null) return;
            lockIcon.gameObject.SetActive(locked);
            if (!locked) return;

            lockIcon.color = difficulty switch
            {
                DifficultyPurpose.Hard      => LOCK_HARD,
                DifficultyPurpose.SuperHard  => LOCK_SUPERHARD,
                _                            => LOCK_NORMAL
            };
        }

        private static void SetIconItemVisible(GameObject iconItem, bool visible)
        {
            if (iconItem != null) iconItem.SetActive(visible);
        }

        private static void SetLockText(TMP_Text main, TMP_Text outline, bool locked, int level)
        {
            string txt = locked ? $"Lv.{level}" : string.Empty;
            if (main != null)
            {
                main.gameObject.SetActive(locked);
                main.text = txt;
            }
            if (outline != null)
            {
                outline.gameObject.SetActive(locked);
                outline.text = txt;
            }
        }

        private static int GetUnlockLevel(string boosterType)
        {
            return boosterType switch
            {
                BoosterManager.SELECT_TOOL  => 9,
                BoosterManager.SHUFFLE      => 12,
                BoosterManager.COLOR_REMOVE => 15,
                _                           => 1
            };
        }

        #endregion

        #region Legacy Compat (HUDController 호환)

        public void SetHolderInfo(int _onRail, int _max) { }
        public void SetMoveCount(int _used, int _total) { }

        #endregion

        #region Toast

        private void ShowToast(string message)
        {
            if (!UIManager.HasInstance) return;
            Transform parent = UIManager.Instance.PopupTr ?? UIManager.Instance.UiTr;
            if (parent == null) return;

            TxtToast.Spawn(parent, message, new Vector2(0f, -1022f));
        }

        #endregion

        #region Utility

        private static void SetCountText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }

        private static string GetBoosterDescription(string boosterType)
        {
            return boosterType switch
            {
                BoosterManager.SELECT_TOOL  => "Select a holder from the queue to deploy.",
                BoosterManager.SHUFFLE      => "Shuffle the holder queue order.",
                BoosterManager.COLOR_REMOVE => "Remove all balloons of a selected color.",
                _                           => ""
            };
        }

        #endregion
    }
}
