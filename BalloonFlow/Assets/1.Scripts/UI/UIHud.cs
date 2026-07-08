using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections.Generic;

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

        // [#15] 씬 UI — 백버튼은 열린 팝업이 우선. HUD 자체는 백버튼을 소비하지 않음 (씬 단위 라우팅으로 위임).
        public override bool ConsumesBackButton => false;

        #region Constants — Lock Colors

        private static readonly Color LOCK_NORMAL    = new Color(1f, 1f, 1f); // #FFFFFF
        private static readonly Color LOCK_HARD      = new Color(1f, 1f, 1f); // #FFFFFF
        private static readonly Color LOCK_SUPERHARD = new Color(1f, 1f, 1f); // #FFFFFF

        private const string ANIM_SPEED_X1 = "SpeedBtnX1";
        private const string ANIM_SPEED_X2 = "SpeedBtnX2";

        #endregion

        [Header("[CHEAT — 스테이지 강제 클리어]")]
        [Tooltip("치트 버튼. 클릭 시 현재 스테이지를 즉시 클리어 처리. Inspector 에서 BtnClear 와이어.")]
        [SerializeField] private Button _btnClear;

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
        [Tooltip("아이템 사용 popup 열릴 때 화면 밖 -260 으로 tween — Inspector 에서 BottomPanel root RectTransform 할당")]
        [SerializeField] private RectTransform _bottomPanelRoot;
        [Tooltip("UseItem popup 때 실제로 이동할 버튼 묶음. 미할당 시 'ButtonContainer' 이름으로 자동 탐색.")]
        [SerializeField] private RectTransform _buttonContainerRoot;
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

        [Header("[TxtLockOutline Difficulty Material Preset]")]
        [SerializeField] private Material _matLockOutlineNormal;
        [SerializeField] private Material _matLockOutlineHard;
        [SerializeField] private Material _matLockOutlineSuperHard;

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

        // BottomPanel 아이템 수량 outline material 캐시 — Resources.Load 결과 보관용 (SerializeField 아님).
        // HUDController.BindView/UIManager 재바인딩·prefab override 로 fontSharedMaterial 이 되돌려지는
        // 케이스에도 ApplyItemCountOutlineMaterial 이 반복 호출되어 상태를 재적용한다.
        private Material _matItemCountOutlineGreen;

        private bool _isMapMakerMode;
        private DifficultyPurpose _currentDifficulty = DifficultyPurpose.Normal;
        private readonly HashSet<string> _pendingItemRewardFx = new HashSet<string>();

        /// <summary>ROLLBACK_TUTORIAL_WAIT_UNLOCK_FX_20260622: 부스터 언락 보상 비행 연출(아이콘이 HUD 하단으로
        ///   날아가 펄스) 진행 중 여부. true 동안 TutorialController 가 튜토리얼 시작을 보류해
        ///   "아이템 HUD 추가 + 연출 종료 → 튜토리얼" 순서를 보장한다. 롤백: 이 속성 삭제 + 대기 루프 제거.</summary>
        public bool IsBoosterRewardFxPlaying => _pendingItemRewardFx.Count > 0;

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
        private const float BOTTOM_PANEL_HIDE_Y = -260f;
        private const float HUD_TOP_USEITEM_Y = 160f;
        // [2026-05-12] 0.25s → 0.5s — 너무 빨라서 tween 인지 어려움.
        private const float BOTTOM_PANEL_TWEEN_DUR = 0.5f;
        private Vector2 _bottomPanelOrigPos;
        private bool _bottomPanelOrigCached;
        private Vector2 _buttonContainerOrigPos;
        private bool _buttonContainerOrigCached;
        private DG.Tweening.Tweener _bottomPanelTween;
        private DG.Tweening.Tweener _hudTopUseItemTween;

        // [2026-05-13] 인게임 팝업 오픈 연출 — 사용자 스펙 절대 anchoredPosition.y값 (-60→-100→160 sequence)
        private const float HUD_TOP_OPEN_START        = -60f;
        private const float HUD_TOP_OPEN_MID          = -100f;
        private const float HUD_TOP_OPEN_END          = 160f;
        private const float BOTTOM_PANEL_POPUP_OPEN_Y = -300f;
        private const float POPUP_OPEN_TWEEN_DUR      = 0.5f; // HUD_Top 전체 duration = BottomPanel duration

        // [2026-05-13] 인게임 enter 슬라이드-인 연출 — prefab 캐시(-300/-60)에 의존하지 않는 하드코딩 시작/끝값.
        // 의미 분리: OPEN_END / POPUP_OPEN_Y 와 수치는 같아도 용도가 다르다 (popup vs ingame enter).
        private const float HUD_TOP_INGAME_HIDDEN_Y      = 240f;  // 화면 밖 위쪽 시작 위치
        private const float HUD_TOP_INGAME_REST_Y        = -60f;  // 인게임 정착 위치
        private const float BOTTOM_PANEL_INGAME_HIDDEN_Y = -260f; // 화면 밖 아래쪽 시작 위치
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
            RectTransform moveTarget = GetButtonContainerRoot();
            if (moveTarget == null)
            {
                Debug.LogWarning("[UIHud] HideBottomPanel: _bottomPanelRoot 미할당. Inspector 에서 BottomPanel RectTransform 와이어 필요.");
                return;
            }
            if (!_buttonContainerOrigCached)
            {
                _buttonContainerOrigPos = moveTarget.anchoredPosition;
                _buttonContainerOrigCached = true;
            }
            _bottomPanelTween?.Kill();
            // ROLLBACK_HUD_BOTTOM_DOKILL:
            // Zap can start immediately after PopupUseItem closes. Kill any bottom-panel tween
            // already created by popup close/open so the Zap hide tween is not overwritten.
            moveTarget.DOKill(false);
            // ROLLBACK_USEITEM_BOTTOM_PANEL_ABSOLUTE_HIDE:
            // UseItem spec is "BottomPanel to y=-260". Do not offset from a stale cached
            // origin, because scene-enter/popup tweens can leave the cache out of sync.
            float targetY = BOTTOM_PANEL_HIDE_Y;
            Debug.Log($"[UIHud] HideBottomPanel: origin.y={_buttonContainerOrigPos.y:F1} -> target.y={targetY:F1}");
            MoveHudTopForUseItem(true);
            _bottomPanelTween = moveTarget.DOAnchorPosY(targetY, BOTTOM_PANEL_TWEEN_DUR)
                .SetEase(DG.Tweening.Ease.OutCubic)
                .SetUpdate(true); // timeScale=0 (PauseManager) 환경에서도 동작
        }

        public void ShowBottomPanel()
        {
            RectTransform moveTarget = GetButtonContainerRoot();
            if (moveTarget == null) return;
            if (!_buttonContainerOrigCached)
            {
                MoveHudTopForUseItem(false);
                return;
            }
            _bottomPanelTween?.Kill();
            // ROLLBACK_HUD_BOTTOM_DOKILL: see HideBottomPanel.
            moveTarget.DOKill(false);
            Debug.Log($"[UIHud] ShowBottomPanel: -> origin.y={_buttonContainerOrigPos.y:F1}");
            MoveHudTopForUseItem(false);
            _bottomPanelTween = moveTarget.DOAnchorPosY(_buttonContainerOrigPos.y, BOTTOM_PANEL_TWEEN_DUR)
                .SetEase(DG.Tweening.Ease.OutCubic)
                .SetUpdate(true);
        }

        private RectTransform GetButtonContainerRoot()
        {
            if (_buttonContainerRoot != null) return _buttonContainerRoot;

            Transform found = FindDeep(transform, "ButtonContainer");
            if (found != null)
                _buttonContainerRoot = found as RectTransform;

            return _buttonContainerRoot != null ? _buttonContainerRoot : _bottomPanelRoot;
        }

        private void MoveHudTopForUseItem(bool hidden)
        {
            if (_hudTopRoot == null) return;
            if (!_hudTopOrigCached)
            {
                _hudTopOrigPos = _hudTopRoot.anchoredPosition;
                _hudTopOrigCached = true;
            }

            _hudTopUseItemTween?.Kill();
            _hudTopRoot.DOKill(false);
            float targetY = hidden ? HUD_TOP_USEITEM_Y : HUD_TOP_INGAME_REST_Y;
            _hudTopUseItemTween = _hudTopRoot.DOAnchorPosY(targetY, BOTTOM_PANEL_TWEEN_DUR)
                .SetEase(DG.Tweening.Ease.OutCubic)
                .SetUpdate(true);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }

            return null;
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
                // [2026-06-23 사용자 피드백] HUD_Top 슬라이드-인 시작 시 1회. 진입 메서드가 1회 호출이라 tween 매 프레임 위치 갱신과 무관하게 1회 보장. _hudTopRoot null 가드 안쪽에 두어 실제 이동이 있을 때만 재생.
                if (AudioManager.HasInstance) AudioManager.Instance.PlayIngameStart();
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

        // [2026-05-22] 인스턴스화 즉시 HIDDEN_Y 강제 — UIManager.OpenUI 의 SetActive(true) 이후 prefab REST 위치가
        // 1프레임 노출되는 플리커 차단. PrimeIngameEnterStartPos 가 LoadUI 단계에서 한 번 더 호출되어도 idempotent.
        protected override void Awake()
        {
            base.Awake();
            PrimeIngameEnterStartPos();

            // [PopupTextInventory P0-18 / P0-19a] prefab 정적 텍스트('level 00', '1000.0' 등) 잔재 1프레임 노출 차단.
            // SetLevel/SetGold 가 HUDController 에서 호출되기 전 단계에서 안전한 기본값으로 덮어쓴다.
            if (_txtLevel != null) _txtLevel.SetText("Level {0}", 1);
            if (_txtLevelOutline != null) _txtLevelOutline.SetText("Level {0}", 1);
            if (_goldText != null) _goldText.text = "0";

            // BottomPanel 아이템 수량 outline 텍스트는 prefab 바이너리라 YAML 편집 불가 →
            // 누적 도메인 원칙(색 직접 지정 금지)에 따라 TMP material preset 을 런타임에 교체.
            ApplyItemCountOutlineMaterial();
        }

        // PR #299 의 Awake 1회 적용만으로는 HUDController.BindView 재바인딩/prefab override 가
        // fontSharedMaterial 을 prefab 기본값으로 되돌리는 경우 outline 이 화면에서 사라진다.
        // 캐시 필드 비교로 idempotent — 반복 호출 시 no-op. ForceMeshUpdate 로 outline sub-mesh 강제 리빌드.
        private void ApplyItemCountOutlineMaterial()
        {
            if (_matItemCountOutlineGreen == null)
                _matItemCountOutlineGreen = Resources.Load<Material>(Const.FONT_MAT_POPPINS_BOLD_GREEN_OUTLINE);
            if (_matItemCountOutlineGreen == null) return;

            if (_itemCountOutlineShuffle != null && _itemCountOutlineShuffle.fontSharedMaterial != _matItemCountOutlineGreen)
            {
                _itemCountOutlineShuffle.fontSharedMaterial = _matItemCountOutlineGreen;
                _itemCountOutlineShuffle.havePropertiesChanged = true;
                _itemCountOutlineShuffle.ForceMeshUpdate();
            }
            if (_itemCountOutlineRemove != null && _itemCountOutlineRemove.fontSharedMaterial != _matItemCountOutlineGreen)
            {
                _itemCountOutlineRemove.fontSharedMaterial = _matItemCountOutlineGreen;
                _itemCountOutlineRemove.havePropertiesChanged = true;
                _itemCountOutlineRemove.ForceMeshUpdate();
            }
            if (_itemCountOutlineHand != null && _itemCountOutlineHand.fontSharedMaterial != _matItemCountOutlineGreen)
            {
                _itemCountOutlineHand.fontSharedMaterial = _matItemCountOutlineGreen;
                _itemCountOutlineHand.havePropertiesChanged = true;
                _itemCountOutlineHand.ForceMeshUpdate();
            }
        }

        private void OnEnable()
        {
            // 스테이지 in-place 전환 시 GameSpeedController._toggleOn latch 와 HUD 비주얼/텍스트가 어긋날 수 있어,
            // OnLevelLoaded 발화에 맞춰 X1/X2 비주얼을 재동기화한다.
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
            // ROLLBACK_HUD_BOOSTER_INVENTORY_SYNC_20260609:
            // Booster counts can change through use, purchase rewards, unlock rewards, daily rewards,
            // or Firestore reconcile. Refresh from the source of truth whenever inventory changes.
            EventBus.Subscribe<OnBoosterInventoryChanged>(HandleBoosterInventoryChanged);
            // [2026-05-22] 씬 전환 시작 즉시 UIHud 강제 숨김 — Win/Fail Home, mid-game Settings→Quit 등 모든 로비 이탈 경로 커버.
            // latch off 경로에서 NotifyPopupClosed 가 PlayPopupCloseAnimation 으로 REST 복귀 연출을 트리거하던 재노출 차단.
            EventBus.Subscribe<OnSceneTransitionStarted>(HandleSceneTransitionStarted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnBoosterInventoryChanged>(HandleBoosterInventoryChanged);
            EventBus.Unsubscribe<OnSceneTransitionStarted>(HandleSceneTransitionStarted);
        }

        private void HandleLevelLoaded(OnLevelLoaded _)
        {
            // [구매 fix 2026-06-10] 이전 레벨에서 끊긴 보상 FX 의 pending 플래그 정리 — 연출 dedupe 가 새 레벨까지 잠그지 않게.
            _pendingItemRewardFx.Clear();
            RefreshSpeedToggleVisual();
            RefreshBoosterCounts();
            RefreshLockState();
        }

        private void HandleBoosterInventoryChanged(OnBoosterInventoryChanged _)
        {
            RefreshBoosterCounts();
        }

        private void HandleSceneTransitionStarted(OnSceneTransitionStarted _)
        {
            _popupOpenSeq?.Kill();
            _bottomPanelTween?.Kill();
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
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

        public void PlayBoosterRewardFly(string boosterType, int count, Sprite icon, System.Action afterAction)
        {
            if (count <= 0)
            {
                afterAction?.Invoke();
                return;
            }

            if (!_pendingItemRewardFx.Add(boosterType))
                return;

            void Complete()
            {
                _pendingItemRewardFx.Remove(boosterType);
                afterAction?.Invoke();
            }

            Sprite flyIcon = icon != null ? icon : GetBoosterFlyIcon(boosterType);
            if (flyIcon == null)
            {
                Complete();
                return;
            }

            Vector2 from = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 to = GetBoosterButtonScreenPos(boosterType);
            // ROLLBACK_ITEMFLY_BUTTON_CENTER_20260708 rev5: 도착 좌표 실측 로그 — 잔여 어긋남 재보고 시
            //   이 값과 화면상 버튼 위치를 대조해 좌표/연출 어느 쪽 문제인지 즉시 확정. 검증 후 제거 예정.
            Debug.Log($"[ItemFly-AUDIT] booster={boosterType} target={to} screen={Screen.width}x{Screen.height}");

            // ROLLBACK_ITEMFLY_LIVE_TARGET_20260708 (AUDIT 확정 원인): 클레임 시점 shuffle/zap 버튼은 아직
            //   화면 밖 아래(y≈-152)에 있고 등장 연출로 나중에 제자리에 온다 — 고정 좌표는 어떤 시점에 읽어도
            //   레이스가 남는다. 비행 중 매 프레임 버튼의 '현재' 원형 중심을 추적해 도착 순간 실제 위치에 꽂는다.
            //   리졸브(1회, 지연)는 비행 시작 후 첫 프레임 — 그 시점엔 버튼이 활성/정착 중이라 참조가 안정.
            RectTransform liveRt = null;
            Camera liveCam = null;
            bool liveResolved = false;
            System.Func<Vector2> liveTarget = () =>
            {
                if (!liveResolved)
                {
                    liveResolved = true;
                    TryResolveBoosterButtonVisual(boosterType, out liveRt, out liveCam);
                }
                return liveRt != null ? ScreenPosOfRectCenter(liveRt, liveCam) : to;
            };

            ItemFlyEffect.Play(flyIcon, from, to, 1,
                onEachLand: () => PulseBoosterButton(boosterType),
                onAllComplete: Complete,
                screenToProvider: liveTarget);
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
            UIOutlineStyle.ApplyMaterialOrColor(_txtSpeedOutline, speedOutlineMat, UIOutlineStyle.ForDifficulty(difficulty));

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
            UIOutlineStyle.ApplyMaterialOrColor(_txtLVOutline, lvOutlineMat, UIOutlineStyle.ForDifficulty(difficulty));

            // Number 텍스트 아웃라인 머티리얼
            Material numberOutlineMat = difficulty switch
            {
                DifficultyPurpose.Hard      => _matNumberOutlineHard,
                DifficultyPurpose.SuperHard => _matNumberOutlineSuperHard,
                _                           => _matNumberOutlineNormal
            };
            UIOutlineStyle.ApplyMaterialOrColor(_txtNumberOutline, numberOutlineMat, UIOutlineStyle.ForDifficulty(difficulty));

            Material lockOutlineMat = difficulty switch
            {
                DifficultyPurpose.Hard      => _matLockOutlineHard,
                DifficultyPurpose.SuperHard => _matLockOutlineSuperHard,
                _                           => _matLockOutlineNormal
            };
            if (lockOutlineMat == null) lockOutlineMat = numberOutlineMat;
            UIOutlineStyle.ApplyMaterialOrColor(_txtLockShuffleOutline, lockOutlineMat, UIOutlineStyle.ForDifficulty(difficulty));
            UIOutlineStyle.ApplyMaterialOrColor(_txtLockRemoveOutline, lockOutlineMat, UIOutlineStyle.ForDifficulty(difficulty));
            UIOutlineStyle.ApplyMaterialOrColor(_txtLockHandOutline, lockOutlineMat, UIOutlineStyle.ForDifficulty(difficulty));

            // Percentage 텍스트 아웃라인 머티리얼
            Material percentageOutlineMat = difficulty switch
            {
                DifficultyPurpose.Hard      => _matPercentageOutlineHard,
                DifficultyPurpose.SuperHard => _matPercentageOutlineSuperHard,
                _                           => _matPercentageOutlineNormal
            };
            UIOutlineStyle.ApplyMaterialOrColor(_txtPercentageOutline, percentageOutlineMat, UIOutlineStyle.ForDifficulty(difficulty));
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
            // HUDController.BindView/UIManager 재바인딩 또는 prefab override 로 fontSharedMaterial 이
            // prefab 기본값으로 되돌려질 수 있어, 수량 갱신 진입마다 outline material 을 재확인한다.
            // 캐시 필드 비교 덕분에 동일 material 이면 no-op (ForceMeshUpdate 도 호출 안 됨).
            ApplyItemCountOutlineMaterial();

            if (_isMapMakerMode || GameManager.IsTestItemMode || GameManager.IsTestPlayMode)
            {
                SetCountText(_itemCountShuffle, "\u221E"); // ∞
                SetCountText(_itemCountRemove, "\u221E");
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
            // ROLLBACK_HUD_COUNT_REFRESH_MODAL_20260630:
            // RefreshBoosterCounts is display sync, not input execution. Modal/input guards belong
            // in button handlers only; otherwise reward/use/purchase events that arrive during
            // tutorial/loading/fade can be skipped and the HUD count remains stale.
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
            if (_btnClear != null) _btnClear.onClick.AddListener(OnClearCheatClicked);
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
            if (_btnClear != null) _btnClear.onClick.RemoveAllListeners();
        }

        /// <summary>[CHEAT] BtnClear 클릭 — 현재 스테이지를 즉시 클리어 처리.</summary>
        private void OnClearCheatClicked()
        {
            if (BoardStateManager.HasInstance)
                BoardStateManager.Instance.ForceClearStage();
            else
                Debug.LogWarning("[UIHud] OnClearCheatClicked: BoardStateManager 인스턴스 없음 — 클리어 불가.");
        }

        private void OnSpeedToggleClicked()
        {
            if (ShouldBlockHudUtilityInputForModalState()) return;

            if (GameSpeedController.HasInstance)
                GameSpeedController.Instance.ToggleSpeedBoost();
            RefreshSpeedToggleVisual();
        }

        private void RefreshSpeedToggleVisual()
        {
            bool on = GameSpeedController.HasInstance && GameSpeedController.Instance.ToggleOn;
            if (_speedToggleOnVisual != null) _speedToggleOnVisual.SetActive(on);
            if (_speedToggleOffVisual != null) _speedToggleOffVisual.SetActive(!on);

            string speedTxt = on ? "x2" : "x1";
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
                // ROLLBACK_LOCKED_ITEM_TOAST_TEXT_20260619 / LEVEL_20260623:
                //   "Unlocks at Level {n}" 의 {n} 을 실제 해금 레벨로 치환.
                // ROLLBACK_LOCKED_ITEM_TOAST_ROBUST_20260625: {n} 치환이 데이터/케이스 변형·placeholder 차이 등
                //   어떤 이유로든 누락돼도 raw placeholder 가 사용자에게 절대 노출되지 않게 방어:
                //   1) 흔한 토큰 변형({n}/{N}/{0}/{level}) 직접 치환 → 2) 그래도 남은 {..} 는 레벨로 강제 치환.
                string lockMsg = FormatLockedItemToast(GetBoosterUnlockLevel(boosterType));
                ShowToast(lockMsg);
                return;
            }

            // 재고 없음 → 구매 팝업
            if (ShouldIgnoreBoosterTapForClearImminent())
            {
                // ROLLBACK_CLEAR_IMMINENT_BOOSTER_NOOP_20260622:
                // Once Almost There is active, booster use is wasteful because the board is already
                // guaranteed to clear. Keep the button tap feedback, but do not open STEP 1,
                // spend inventory, or emit item_use_event.
                VibrationManager.Vibrate(10L, 150);
                Debug.Log($"[UIHud] Booster tap ignored during clear-imminent state: {boosterType}");
                return;
            }

            if (BoosterManager.Instance.GetBoosterCount(boosterType) <= 0)
            {
                if (!BoosterManager.Instance.IsUnlockRewardClaimed(boosterType))
                {
                    ShowUnlockPopup(boosterType);
                    return;
                }

                ShowBuyPopup(boosterType);
                return;
            }

            // Shuffle은 즉시 실행 (팝업 없이)
            if (boosterType == BoosterManager.SHUFFLE)
            {
                BoosterManager.Instance.UseBooster(boosterType);
                RefreshBoosterCounts();
                NotifyTutorialItemTapped(boosterType);
                NotifyTutorialItemUseCompleted();
                return;
            }

            // Hand/Remove → UseItem 팝업 (Dim + Cutout)
            ShowUseItemPopup(boosterType);
            NotifyTutorialItemTapped(boosterType);
        }

        private static bool ShouldIgnoreBoosterTapForClearImminent()
        {
            return RailManager.HasInstance && RailManager.Instance.IsClearImminentForBoosterLock();
        }

        private static bool ShouldBlockHudItemInputForModalState()
        {
            // ROLLBACK_HUD_ITEM_MODAL_INPUT_BLOCK_20260629:
            // UI Button callbacks bypass InputHandler, so guard booster execution here too.
            // This prevents item use through NewFeature/loading/fade overlays even if a prefab
            // misses a raycast-blocking background.
            return (LevelManager.HasInstance && LevelManager.Instance.IsLoading)
                || (UIManager.HasInstance && UIManager.Instance.IsFading)
                || (NewFeatureManager.HasInstance && NewFeatureManager.Instance.IsShowingPopup);
        }

        public static bool ShouldBlockHudUtilityInputForModalState()
        {
            // ROLLBACK_TUTORIAL_HUD_UTILITY_BLOCK_20260630:
            // Tutorial visuals block world/booster paths, but top HUD utility buttons are normal
            // Button callbacks. Block speed/settings/gold shortcuts while tutorial is visible.
            return ShouldBlockHudItemInputForModalState()
                || (TutorialController.HasInstance && TutorialController.Instance.IsTutorialActive());
        }

        private static void NotifyTutorialItemTapped(string boosterType)
        {
            // ROLLBACK_TUTORIAL_TAP_ITEM_ACTION_20260623:
            // Advance requireAction=tap_item only after a real usable booster path.
            if (TutorialController.HasInstance)
                TutorialController.Instance.NotifyItemTapped(boosterType);
        }

        private static void NotifyTutorialItemUseCompleted()
        {
            if (TutorialController.HasInstance)
                TutorialController.Instance.NotifyItemUseCompleted();
        }

        private void ShowUseItemPopup(string boosterType)
        {
            if (!UIManager.HasInstance) return;
            // ROLLBACK_USEITEM_DIRECT_HUD_HIDE:
            // This call is on the live UIHud instance. Keep it here so UseItem does not depend
            // on PopupUseItem finding the correct HUD object while the popup canvas is active.
            HideBottomPanel();
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

            // [구매 desync 방지] buy 팝업 노출 직전 HUD 골드를 권위값(_currentCoins)과 강제 동기화.
            // OnCoinChanged 이벤트를 놓쳤거나 startup reconcile 이후 표시가 어긋난 "유령 골드" 차단 —
            // 사용자가 실제 보유액 기준으로 구매를 결정하게 한다.
            // [구매 fix 2026-06-10] 표시 전에 UserData 캐시 잔액까지 끌어와 팝업의 잔액/구매 가능 판정이 권위값 기준이 되게 함
            //   (빌드에서 CurrencyManager 가 PlayerPrefs 초기값으로 남아 실제 보유 골드보다 적게 보이던 케이스).
            if (CurrencyManager.HasInstance)
            {
                CurrencyManager.Instance.RefreshFromUserDataCache();
                CurrencyManager.Instance.PublishCoinSync();
            }

            var popup = UIManager.Instance.OpenUI<PopupBuyItem>("Popup/PopupBuyItem");
            if (popup == null) return;

            int price = BoosterManager.Instance.GetBoosterPrice(boosterType);
            Sprite spr = popup.GetBoosterSprite(boosterType);
            popup.ShowBuyResult(GetBoosterDisplayName(boosterType), spr, "x3", price,
                onConfirm: () =>
                {
                    if (CurrencyManager.HasInstance)
                    {
                        // ROLLBACK_BOOSTER_BUY_COIN_CACHE_REFRESH_20260609:
                        // If UserData loaded after CurrencyManager's local PlayerPrefs cache, pull
                        // the latest cached server balance before deciding whether the user can buy.
                        CurrencyManager.Instance.RefreshFromUserDataCache();
                    }

                    int coins = CurrencyManager.HasInstance ? CurrencyManager.Instance.Coins : -1;
                    int serverCoins = UserDataService.HasInstance && UserDataService.Instance.IsReady && UserDataService.Instance.CurrentUser != null
                        ? UserDataService.Instance.CurrentUser.coins
                        : -1;
                    bool unlocked = BoosterManager.Instance.IsBoosterUnlocked(boosterType);
                    bool pending = _pendingItemRewardFx.Contains(boosterType);
                    Debug.Log($"[UIHud] Buy booster confirm: type={boosterType}, price={price}, coins={coins}, serverCoins={serverCoins}, unlocked={unlocked}, pendingFx={pending}");

                    // [구매 fix 2026-06-10] pending FX 에 의한 구매 차단("Please wait.") 제거 —
                    //   빌드에서 FX 코루틴이 끊기면(씬 전환/러너 소실) pending 이 영구 잔존해 골드가 있어도
                    //   구매가 막히던 원인. 지급이 FX 와 분리됐으므로 차단 자체가 불필요.

                    // 진짜 코인 부족만 "코인 부족"으로 표시. TrySpend 는 미해금/매니저 부재 등 다른 사유로도
                    // false 를 반환하므로, 사유를 선판정하지 않으면 코인이 충분한데도 "코인 부족"으로 오표시된다.
                    if (!CurrencyManager.HasInstance || !CurrencyManager.Instance.HasEnoughCoins(price))
                    {
                        Debug.LogWarning($"[UIHud] Booster buy blocked by coins: type={boosterType}, price={price}, coins={coins}, serverCoins={serverCoins}");
                        if (CurrencyManager.HasInstance) CurrencyManager.Instance.PublishCoinSync();
                        // ROLLBACK_NOCOIN_POPUP_TO_GOLDSHOP_20260622:
                        // Not enough coins must open the shop instead of PopupError("Not enough coins").
                        popup.CloseUI();
                        OpenGoldShopForInsufficientCoins();
                        return false;
                    }

                    if (BoosterManager.Instance.TrySpendBoosterPurchaseCost(boosterType))
                    {
                        // [구매 fix 2026-06-10] 지급을 FX 완료 콜백에서 분리 — 차감 즉시 지급 (데이터 무결성).
                        //   기존: 차감 → FX 비행 완료 후 AddBooster. FX 가 죽으면 코인만 차감되고 지급 누락 + pending 영구 잠금.
                        //   변경: 차감 → 즉시 지급/표시 갱신/토스트, FX 는 연출 전용 (완료 시 카운트 펄스 겸 재갱신).
                        // ROLLBACK_ANALYTICS_NULLFILL_20260625: HUD 코인 구매 — 획득경로/비용 기록.
                        BoosterManager.Instance.AddBooster(boosterType, 3, "purchase", BoosterManager.Instance.GetBoosterPrice(boosterType), "coin");
                        RefreshBoosterCounts();
                        ShowToast("Purchase successful!");
                        PlayBoosterRewardFly(boosterType, 3, spr, RefreshBoosterCounts);
                        return true;
                    }
                    else
                    {
                        // 코인은 충분한데 차감 실패 → 코인 문제가 아님(미해금/매니저 부재 등). 실제 사유 로그 + 정직한 메시지.
                        Debug.LogWarning($"[UIHud] Booster 구매 실패(코인 충분): type={boosterType}, price={price}, " +
                            $"unlocked={BoosterManager.Instance.IsBoosterUnlocked(boosterType)}, " +
                            $"coins={(CurrencyManager.HasInstance ? CurrencyManager.Instance.Coins : -1)}, serverCoins={serverCoins}");
                        if (CurrencyManager.HasInstance) CurrencyManager.Instance.PublishCoinSync();
                        var err = UIManager.Instance.OpenUI<PopupError>("Popup/PopupError");
                        if (err != null) err.Show("Purchase Failed", "Purchase could not be completed. Please try again.");
                        return false;
                    }
                },
                description: GetBoosterBuyDescription(boosterType));
        }

        private void OpenGoldShopForInsufficientCoins()
        {
            if (HUDController.HasInstance && HUDController.Instance.GoldShopPopup != null)
            {
                HUDController.Instance.GoldShopPopup.OpenUI();
                return;
            }

            if (UIManager.HasInstance)
                UIManager.Instance.OpenUI<PopupGoldShop>("Popup/PopupGoldShop");
        }

        // ROLLBACK_LOCKED_ITEM_TOAST_LEVEL_20260623: 부스터 해금 레벨 (잠김 토스트 {n} 치환 + 해금 팝업 공용).
        private static int GetBoosterUnlockLevel(string boosterType) => boosterType switch
        {
            _ when BoosterManager.HasInstance => BoosterManager.Instance.GetBoosterUnlockLevel(boosterType),
            BoosterManager.SELECT_TOOL        => 9,
            BoosterManager.SHUFFLE            => 12,
            BoosterManager.COLOR_REMOVE       => 15,
            _                                 => 1
        };

        private void ShowUnlockPopup(string boosterType)
        {
            if (!UIManager.HasInstance) return;
            var popup = UIManager.Instance.OpenUI<PopupBuyItem>("Popup/PopupBuyItem");
            if (popup == null) return;

            int unlockLevel = GetBoosterUnlockLevel(boosterType);

            // ROLLBACK_UNLOCK_POPUP_TITLE_TEXTDATA_20260623: 해금 팝업 타이틀 = TextData "Item Unlocked!"(부스터명 대신).
            Sprite spr = popup.GetBoosterSprite(boosterType);
            popup.ShowUnlock(LocalizationService.Get("popup.txttitle.itemunlocked"), spr, unlockLevel, $"x{BoosterManager.UNLOCK_REWARD_COUNT}",
                onConfirm: () =>
                {
                    // [구매 fix 2026-06-10] 해금 보상도 즉시 지급 — FX 는 연출 전용 (구매 흐름과 동일 원칙).
                    if (BoosterManager.Instance.TryClaimUnlockReward(boosterType))
                    {
                        // ROLLBACK_ITEM_ACQUIRE_INPUT_LOCK_20260708: Claim 확정 → 튜토("Tap your item!") 시작까지
                        //   전역 입력 잠금(UI 레이캐스트/홀더 터치/백버튼). 해제는 StartTutorial 및 실패 경로들이 담당.
                        TutorialController.BeginItemAcquisitionInputLock();
                        RefreshBoosterCounts();
                        RefreshLockState();
                        ShowToast("Item claimed!");
                        // ROLLBACK_TUTORIAL_START_AFTER_UNLOCK_20260622: 아이콘이 HUD 하단으로 날아가는 보상연출이 끝난 "후"
                        //   해당 레벨의 튜토리얼(Tutorial Editor 에서 Manual Trigger Only 로 작성)을 시작 — "Claim → 연출 → 튜토리얼" 흐름.
                        //   레벨에 튜토리얼 없거나 이미 완료면 StartTutorialForLevel 이 no-op. 롤백: afterAction 을 RefreshBoosterCounts 로 환원.
                        PlayBoosterRewardFly(boosterType, BoosterManager.UNLOCK_REWARD_COUNT, spr, () =>
                        {
                            RefreshBoosterCounts();
                            bool tutorialStarted = TutorialController.HasInstance && LevelManager.HasInstance
                                && TutorialController.Instance.StartTutorialForLevel(LevelManager.Instance.CurrentLevelId);
                            if (!tutorialStarted)
                                TutorialController.EndItemAcquisitionInputLock(); // 튜토 없음 — 즉시 해제
                        });
                    }
                },
                description: GetBoosterBuyDescription(boosterType));
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
            // [2026-06-11] 하드코딩 "Lv.{level}" → TextData 키(itembtn.txtlock="Lv.{n}") + 치환
            // (placeholder 가 있는 키는 반드시 Format/GetWith 로 소비 룰).
            string txt = locked ? LocalizationService.GetWith("itembtn.txtlock", "n", level) : string.Empty;
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
                _ when BoosterManager.HasInstance => BoosterManager.Instance.GetBoosterUnlockLevel(boosterType),
                BoosterManager.SELECT_TOOL        => 9,
                BoosterManager.SHUFFLE            => 12,
                BoosterManager.COLOR_REMOVE       => 15,
                _                                 => 1
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

        private static string FormatLockedItemToast(int unlockLevel)
        {
            // ROLLBACK_LOCKED_ITEM_TOAST_STRING_FORMAT_20260625:
            // TextData는 "Unlocks at Level {n}"처럼 named placeholder를 쓰지만,
            // string.Format은 "{0}" 같은 숫자 placeholder만 지원하므로 먼저 정규화한다.
            string lvStr = unlockLevel > 0 ? unlockLevel.ToString() : "?";
            string template = LocalizationService.Get("toast.item.locked");
            if (string.IsNullOrEmpty(template)) template = "Unlocks at Level {0}";

            string format = template
                .Replace("{n}", "{0}")
                .Replace("{N}", "{0}")
                .Replace("{level}", "{0}");

            try
            {
                string result = string.Format(System.Globalization.CultureInfo.InvariantCulture, format, lvStr);
                return StripRemainingPlaceholders(result, lvStr);
            }
            catch (System.FormatException)
            {
                return StripRemainingPlaceholders(template, lvStr);
            }
        }

        private static string StripRemainingPlaceholders(string text, string replacement)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
            return System.Text.RegularExpressions.Regex.Replace(text, @"\{[^}]*\}", replacement ?? string.Empty);
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
                BoosterManager.SELECT_TOOL  => LocalizationService.Get("useitem.txtdescription.hand"),
                BoosterManager.SHUFFLE      => LocalizationService.Get("popup.txtdescription.suffle"),
                BoosterManager.COLOR_REMOVE => LocalizationService.Get("useitem.txtdescription.zap"),
                _                           => ""
            };
        }

        private static string GetBoosterDisplayName(string boosterType)
        {
            return boosterType switch
            {
                BoosterManager.SELECT_TOOL  => LocalizationService.Get("popup.txttitle.hand"),
                BoosterManager.SHUFFLE      => LocalizationService.Get("popup.txttitle.suffle"),
                BoosterManager.COLOR_REMOVE => LocalizationService.Get("popup.txttitle.zap"),
                _                           => ""
            };
        }

        private static string GetBoosterBuyDescription(string boosterType)
        {
            return boosterType switch
            {
                BoosterManager.SELECT_TOOL  => LocalizationService.Get("popup.txtdescription.hand"),
                BoosterManager.SHUFFLE      => LocalizationService.Get("popup.txtdescription.suffle"),
                BoosterManager.COLOR_REMOVE => LocalizationService.Get("popup.txtdescription.zap"),
                _                           => ""
            };
        }

        // ROLLBACK_ITEMFLY_BUTTON_CENTER_20260708 rev5/rev6: 도착 버튼의 '보이는 원형' rect + 카메라 해석.
        //   rev5: 버튼 루트 rect 는 프리팹별 구성이 달라("+" 배지/여백) 하위 최대면적 Image(원형 본체)를 실측.
        //   rev6(AUDIT 확정): 클레임 시점 shuffle/zap 버튼 트랜스폼이 화면 밖 아래(y=-152)였다가 등장 연출로
        //   제자리에 옴 — 고정 좌표로는 시점 문제를 못 이김. 리졸버를 분리해 비행 중 실시간 추적에 사용.
        private bool TryResolveBoosterButtonVisual(string boosterType, out RectTransform visualRt, out Camera cam)
        {
            visualRt = null;
            cam = null;

            Button button = boosterType switch
            {
                BoosterManager.SHUFFLE      => _itemBtnShuffle,
                BoosterManager.COLOR_REMOVE => _itemBtnRemove,
                BoosterManager.SELECT_TOOL  => _itemBtnHand,
                _                           => null
            };
            RectTransform rt = button != null ? button.transform as RectTransform : null;
            if (rt == null) return false;

            // 레이아웃 재계산이 '예약'만 된 상태의 stale rect 방지 — 해석 시 1회 강제 리빌드.
            Canvas.ForceUpdateCanvases();

            visualRt = rt;
            float bestArea = 0f;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image img = images[i];
                if (img == null || !img.gameObject.activeInHierarchy) continue;
                RectTransform irt = img.rectTransform;
                Vector3 ls = irt.lossyScale;
                float area = Mathf.Abs(irt.rect.width * ls.x) * Mathf.Abs(irt.rect.height * ls.y);
                if (area > bestArea) { bestArea = area; visualRt = irt; }
            }

            Canvas canvas = visualRt.GetComponentInParent<Canvas>();
            cam = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera
                ? canvas.worldCamera
                : null;
            return true;
        }

        private static Vector2 ScreenPosOfRectCenter(RectTransform rt, Camera cam)
            => RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));

        private Vector2 GetBoosterButtonScreenPos(string boosterType)
        {
            if (!TryResolveBoosterButtonVisual(boosterType, out RectTransform rt, out Camera cam))
                return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            return ScreenPosOfRectCenter(rt, cam);
        }

        private void PulseBoosterButton(string boosterType)
        {
            Button button = boosterType switch
            {
                BoosterManager.SHUFFLE      => _itemBtnShuffle,
                BoosterManager.COLOR_REMOVE => _itemBtnRemove,
                BoosterManager.SELECT_TOOL  => _itemBtnHand,
                _                           => null
            };
            if (button == null) return;
            button.transform.DOKill();
            button.transform.DOPunchScale(Vector3.one * 0.08f, 0.18f, 6, 0.6f).SetUpdate(true);
        }

        private static Sprite GetBoosterFlyIcon(string boosterType)
        {
            if (!ResourceManager.HasInstance) return null;
            string key = boosterType switch
            {
                BoosterManager.SHUFFLE      => Const.SPR_ICONSUFFLE,
                BoosterManager.COLOR_REMOVE => Const.SPR_ICONZAP,
                BoosterManager.SELECT_TOOL  => Const.SPR_ICONHAND,
                _                           => null
            };
            return string.IsNullOrEmpty(key) ? null : ResourceManager.Instance.UISpriteOr(key, null);
        }

        #endregion
    }
}
