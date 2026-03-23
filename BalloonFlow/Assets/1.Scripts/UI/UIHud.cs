using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 인게임 HUD UI. Resources/UI/UIHud 프리팹에서 로드.
    /// HUDController가 BindView로 참조 연결.
    /// </summary>
    public class UIHud : UIBase
    {
        [Header("[Top — 레벨/골드]")]
        [SerializeField] private TMP_Text _txtLevelOutline;
        [SerializeField] private TMP_Text _txtLevel;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private TMP_Text _goldText;
        [SerializeField] private Button _goldPlusButton;

        [Header("[Bottom Panel — 부스터 아이템]")]
        [SerializeField] private Button _itemBtnShuffle;
        [SerializeField] private Button _itemBtnRemove;
        [SerializeField] private Button _itemBtnHand;
        [SerializeField] private TMP_Text _itemCountShuffle;
        [SerializeField] private TMP_Text _itemCountRemove;
        [SerializeField] private TMP_Text _itemCountHand;

        [Header("[색상 선택 패널 — Color Remove용]")]
        [SerializeField] private GameObject _colorPanel;
        [SerializeField] private Button _color0Button;
        [SerializeField] private Button _color1Button;
        [SerializeField] private Button _color2Button;
        [SerializeField] private Button _color3Button;

        [Header("[배경/오버레이]")]
        [SerializeField] private Image _backgroundImage;

        private bool _isMapMakerMode;

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

        #endregion

        private void Start()
        {
            WireButtons();
            if (_colorPanel != null) _colorPanel.SetActive(false);
            RefreshBoosterCounts();
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
        }

        public void SetLevel(int _levelId)
        {
            if (_txtLevel != null) _txtLevel.text = $"{_levelId}";
            if (_txtLevelOutline != null) _txtLevelOutline.text = $"{_levelId}";
        }

        public void SetGold(int _amount)
        {
            if (_goldText != null) _goldText.text = _amount.ToString("N0");
        }

        public void RefreshBoosterCounts()
        {
            if (_isMapMakerMode || GameManager.IsTestItemMode)
            {
                SetCountText(_itemCountShuffle, "\u221E"); // ∞
                SetCountText(_itemCountRemove, "\u221E");
                SetCountText(_itemCountHand, "\u221E");
                return;
            }

            if (!BoosterManager.HasInstance) return;
            SetCountText(_itemCountShuffle, BoosterManager.Instance.GetBoosterCount(BoosterManager.SHUFFLE).ToString());
            SetCountText(_itemCountRemove, BoosterManager.Instance.GetBoosterCount(BoosterManager.COLOR_REMOVE).ToString());
            SetCountText(_itemCountHand, BoosterManager.Instance.GetBoosterCount(BoosterManager.HAND).ToString());
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
        }

        #endregion

        #region Booster Handlers

        private void OnShuffleClicked()
        {
            if (!CanUseBooster(BoosterManager.SHUFFLE)) return;
            BoosterManager.Instance.UseBooster(BoosterManager.SHUFFLE);
            RefreshBoosterCounts();
        }

        private void OnColorRemoveClicked()
        {
            if (!CanUseBooster(BoosterManager.COLOR_REMOVE)) return;
            BoosterManager.Instance.UseBooster(BoosterManager.COLOR_REMOVE);
            RefreshBoosterCounts();
            // 색상 패널 제거 — 카메라가 필드로 이동 → 풍선 직접 클릭으로 색상 선택
            // BoosterExecutor.HandleBoosterUsed에서 카메라 이동 + 아웃라인 처리
        }

        private void OnHandClicked()
        {
            if (!CanUseBooster(BoosterManager.HAND)) return;
            BoosterManager.Instance.UseBooster(BoosterManager.HAND);
            RefreshBoosterCounts();
            Debug.Log("[UIHud] Hand 사용 — 보관함을 탭하세요.");
        }

        private void OnColorPicked(int color)
        {
            if (BoosterExecutor.HasInstance)
                BoosterExecutor.Instance.OnColorSelected(color);
            if (_colorPanel != null) _colorPanel.SetActive(false);
        }

        private bool CanUseBooster(string boosterType)
        {
            if (!BoosterManager.HasInstance) return false;

            // MapMaker 또는 TEST ITEM 모드: 무한 사용
            if (_isMapMakerMode || GameManager.IsTestItemMode)
            {
                if (BoosterManager.Instance.GetBoosterCount(boosterType) <= 0)
                    BoosterManager.Instance.AddBooster(boosterType, 1);
                return true;
            }

            return BoosterManager.Instance.GetBoosterCount(boosterType) > 0;
        }

        #endregion

        #region Legacy Compat (HUDController 호환)

        public void SetHolderInfo(int _onRail, int _max) { }
        public void SetMoveCount(int _used, int _total) { }

        #endregion

        #region Utility

        private static void SetCountText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }

        #endregion
    }
}
