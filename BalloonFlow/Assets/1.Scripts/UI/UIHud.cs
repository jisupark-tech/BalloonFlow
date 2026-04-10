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
        #region Constants — Lock Colors

        private static readonly Color LOCK_NORMAL    = new Color(0x9E / 255f, 0xD1 / 255f, 0xFF / 255f); // #9ED1FF
        private static readonly Color LOCK_HARD      = new Color(0xCF / 255f, 0x9E / 255f, 0xFF / 255f); // #CF9EFF
        private static readonly Color LOCK_SUPERHARD  = new Color(0xFA / 255f, 0x9F / 255f, 0x7D / 255f); // #FA9F7D

        #endregion

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

        [Header("[Lock Icons — 미해금 시 표시]")]
        [SerializeField] private Image _iconLockShuffle;
        [SerializeField] private Image _iconLockRemove;
        [SerializeField] private Image _iconLockHand;

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

        [Header("[배경/오버레이]")]
        [SerializeField] private Image _backgroundImage;

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

        #endregion

        private void Start()
        {
            WireButtons();
            if (_colorPanel != null) _colorPanel.SetActive(false);
            RefreshBoosterCounts();
            RefreshLockState();
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

        /// <summary>현재 난이도 설정 (Lock 색상에 사용).</summary>
        public void SetDifficulty(DifficultyPurpose difficulty)
        {
            _currentDifficulty = difficulty;
            RefreshLockState();
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

        /// <summary>Lock 아이콘 갱신. 미해금 → Lock 표시 + 난이도 색상.</summary>
        public void RefreshLockState()
        {
            if (_isMapMakerMode || GameManager.IsTestItemMode)
            {
                SetLockIcon(_iconLockHand, false, _currentDifficulty);
                SetLockIcon(_iconLockShuffle, false, _currentDifficulty);
                SetLockIcon(_iconLockRemove, false, _currentDifficulty);
                SetIconItemVisible(_iconItemHand, true);
                SetIconItemVisible(_iconItemShuffle, true);
                SetIconItemVisible(_iconItemRemove, true);
                return;
            }

            if (!BoosterManager.HasInstance) return;

            bool handLocked = !BoosterManager.Instance.IsBoosterUnlocked(BoosterManager.HAND);
            bool shuffleLocked = !BoosterManager.Instance.IsBoosterUnlocked(BoosterManager.SHUFFLE);
            bool removeLocked = !BoosterManager.Instance.IsBoosterUnlocked(BoosterManager.COLOR_REMOVE);

            SetLockIcon(_iconLockHand, handLocked, _currentDifficulty);
            SetLockIcon(_iconLockShuffle, shuffleLocked, _currentDifficulty);
            SetLockIcon(_iconLockRemove, removeLocked, _currentDifficulty);

            // 미해금 시 IconItem 비활성화
            SetIconItemVisible(_iconItemHand, !handLocked);
            SetIconItemVisible(_iconItemShuffle, !shuffleLocked);
            SetIconItemVisible(_iconItemRemove, !removeLocked);
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
            if (_isMapMakerMode || GameManager.IsTestItemMode)
            {
                if (BoosterManager.Instance.GetBoosterCount(boosterType) <= 0)
                    BoosterManager.Instance.AddBooster(boosterType, 1);
                BoosterManager.Instance.UseBooster(boosterType);
                RefreshBoosterCounts();
                return;
            }

            // 미해금 → 해금 팝업
            if (!BoosterManager.Instance.IsBoosterUnlocked(boosterType))
            {
                ShowUnlockPopup(boosterType);
                return;
            }

            // 재고 없음 → 구매 팝업
            if (BoosterManager.Instance.GetBoosterCount(boosterType) <= 0)
            {
                ShowBuyPopup(boosterType);
                return;
            }

            // 재고 있음 → 사용 확인 팝업
            ShowUseItemPopup(boosterType);
        }

        private void ShowUseItemPopup(string boosterType)
        {
            if (!UIManager.HasInstance) return;
            var popup = UIManager.Instance.OpenUI<PopupUseItem>("Popup/UseItem");
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
            popup.ShowUnlock("Locked", spr, unlockLevel);
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
