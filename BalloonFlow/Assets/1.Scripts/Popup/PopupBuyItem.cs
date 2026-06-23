using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// 아이템 구매/해금 확인 팝업.
    /// 구매 모드: Single 버튼 (Buy) — TxtBtnBuyOutline.
    /// 해금 모드: Single 버튼 — TxtSingleOutline.
    /// </summary>
    public class PopupBuyItem : UIBase
    {
        public static bool IsUnlockShowing { get; private set; }

        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Item Display]")]
        [SerializeField] private Image _imgItem;
        [SerializeField] private TMP_Text _txtItemAmount;
        [SerializeField] private TMP_Text _txtItemAmountOutline;
        [SerializeField] private TMP_Text _txtDescription;
        [SerializeField] private Image _imgInnerFrame;

        [Header("[Gold Display]")]
        [SerializeField] private TMP_Text _txtGold;
        [SerializeField] private TMP_Text _txtGoldOutline;
        [SerializeField] private Image _imgCoin;

        [Header("[Buy Outline — 구매 모드]")]
        [SerializeField] private GameObject _txtBtnBuyOutline;

        [Header("[Single Outline — 해금 모드]")]
        [SerializeField] private GameObject _txtSingleOutline;

        [Header("[Item Sprites — Inspector fallback. Awake 시 Addressable atlas 에서 override]")]
        [SerializeField] private Sprite _sprHand;
        [SerializeField] private Sprite _sprShuffle;
        [SerializeField] private Sprite _sprZap;

        [Header("[Idle Motion — ImageItem 아이들 연출]")]
        [SerializeField] private float _idleDelay = 1.0f;       // 매 루프 시작 전 대기 시간(초)
        [SerializeField] private float _idleDuration = 0.3f;    // 1.0 → peak / peak → 1.0 각 구간 시간(초)
        [SerializeField] private float _idlePeakScale = 1.2f;   // 원본 스케일 대비 피크 배율

        private Sequence _idleSeq;
        private Vector3 _imgItemOrigScale;
        private bool _imgItemOrigScaleCaptured;

        private System.Action _onConfirm;
        private System.Func<bool> _onConfirmResult;
        private System.Action _onCancel;
        private bool _isUnlockMode;

        protected override void Awake()
        {
            base.Awake();
            if (_frame != null)
            {
                if (_frame.BtnHorizGreen != null) _frame.BtnHorizGreen.onClick.AddListener(OnBuyClicked);
                if (_frame.BtnHorizRed != null) _frame.BtnHorizRed.onClick.AddListener(OnCancelClicked);
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.AddListener(OnBuyClicked);
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.AddListener(OnCancelClicked);
            }

            // 'iconSuffle' 은 atlas 측 의도된 typo. ResourceManager 에 atlas 사전 로드되어 있으면 sprite 교체.
            if (ResourceManager.HasInstance)
            {
                var rm = ResourceManager.Instance;
                _sprHand    = rm.UISpriteOr("iconHand",    _sprHand);
                _sprShuffle = rm.UISpriteOr("iconSuffle",  _sprShuffle);
                _sprZap     = rm.UISpriteOr("iconZap",     _sprZap);
            }

            // TopBar 잔액 라이브 갱신 — 누락 시 prefab 의 정적 "1900" 이 그대로 노출되어 사용자가
            // 실제 보유 골드를 잘못 인지함 (구매 실패 원인 오해). 다른 popup 들 (Continue/Fail01/Fail02/GoldShop)
            // 과 동일 패턴.
            EnsureTopBarBinding();
        }

        /// <summary>[#2] TopBar 잔액 GoldPanel — 무료 제공(ShowUnlock) 시 숨기고, 유료 구매(ShowBuy) 시 노출.</summary>
        private Transform _topBarGoldPanel;
        private TMP_Text _topBarTxtGold;
        private TMP_Text _topBarTxtGoldOutline;

        private void EnsureTopBarBinding()
        {
            Transform topBar = FindChildRecursive(transform, "TopBarArea");
            Transform gold = topBar != null ? FindChildRecursive(topBar, "GoldPanel") : null;
            _topBarGoldPanel = gold;
            if (gold != null) GoldPanelFxFireUtil.DisableUnderGoldPanel(gold);
            Transform txt = gold != null ? FindChildRecursive(gold, "TxtGold") : null;
            _topBarTxtGold = txt != null ? txt.GetComponent<TMP_Text>() : null;
            Transform outline = gold != null ? FindChildRecursive(gold, "TxtGoldOutline") : null;
            _topBarTxtGoldOutline = outline != null ? outline.GetComponent<TMP_Text>() : null;
            if (txt != null && txt.GetComponent<AnimatedCoinLabel>() == null)
                txt.gameObject.AddComponent<AnimatedCoinLabel>();
            SyncTopBarGoldPanelText();
        }

        // InGame 중 BuyItem 열림 시 게임 일시 정지 (PopupSettings 패턴 동일).
        private bool _paused;
        private void OnEnable()
        {
            GoldPanelFxFireUtil.DisableUnderTopBarRoot(transform);
            if (!_paused) { PauseManager.Pause(); _paused = true; }
            StartIdleMotion();
        }

        private void OnDisable()
        {
            if (_paused) { PauseManager.Resume(); _paused = false; }
            if (_isUnlockMode) IsUnlockShowing = false;
            StopIdleMotion();
        }

        private void StartIdleMotion()
        {
            if (_imgItem == null) return;

            if (!_imgItemOrigScaleCaptured)
            {
                _imgItemOrigScale = _imgItem.transform.localScale;
                _imgItemOrigScaleCaptured = true;
            }

            _idleSeq?.Kill();
            _imgItem.transform.localScale = _imgItemOrigScale;

            _idleSeq = DOTween.Sequence()
                .AppendInterval(_idleDelay)
                .Append(_imgItem.transform.DOScale(_imgItemOrigScale * _idlePeakScale, _idleDuration).SetEase(Ease.OutQuad))
                .Append(_imgItem.transform.DOScale(_imgItemOrigScale, _idleDuration).SetEase(Ease.InQuad))
                .SetLoops(-1, LoopType.Restart)
                .SetUpdate(true);
        }

        private void StopIdleMotion()
        {
            _idleSeq?.Kill();
            _idleSeq = null;
            if (_imgItem != null && _imgItemOrigScaleCaptured)
                _imgItem.transform.localScale = _imgItemOrigScale;
        }

        /// <summary>[#2] TopBar 잔액 GoldPanel 노출 토글.</summary>
        private void SetTopBarGoldPanelVisible(bool visible)
        {
            if (_topBarGoldPanel == null) EnsureTopBarBinding();
            if (_topBarGoldPanel != null && _topBarGoldPanel.gameObject.activeSelf != visible)
                _topBarGoldPanel.gameObject.SetActive(visible);
            if (visible)
                SyncTopBarGoldPanelText();
        }

        private void SyncTopBarGoldPanelText()
        {
            if (!CurrencyManager.HasInstance) return;

            string coins = CurrencyManager.Instance.Coins.ToString("N0");
            if (_topBarTxtGold != null) _topBarTxtGold.text = coins;
            if (_topBarTxtGoldOutline != null) _topBarTxtGoldOutline.text = coins;
        }

        private bool IsTopBarGoldText(TMP_Text text)
        {
            if (text == null || _topBarGoldPanel == null) return false;

            Transform current = text.transform;
            while (current != null)
            {
                if (current == _topBarGoldPanel)
                    return true;
                current = current.parent;
            }
            return false;
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

        protected override void OnDestroy()
        {
            base.OnDestroy();
            StopIdleMotion();
            if (_isUnlockMode) IsUnlockShowing = false;
            if (_frame != null)
            {
                if (_frame.BtnHorizGreen != null) _frame.BtnHorizGreen.onClick.RemoveAllListeners();
                if (_frame.BtnHorizRed != null) _frame.BtnHorizRed.onClick.RemoveAllListeners();
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.RemoveAllListeners();
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.RemoveAllListeners();
            }
        }

        /// <summary>아이템 구매 팝업 표시 (Single 버튼 — Buy).</summary>
        public void ShowBuy(string title, Sprite itemSprite, string amount, int goldCost,
                            System.Action onConfirm = null, System.Action onCancel = null,
                            string description = null)
        {
            _isUnlockMode = false;
            IsUnlockShowing = false;
            _onConfirm = onConfirm;
            _onConfirmResult = null;
            _onCancel = onCancel;

            if (_frame != null)
            {
                _frame.SetTitle(title);
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText(LocalizationService.Get("popup.txtbtnbuy"));
                _frame.ShowExitButton(true);
            }

            if (_txtDescription != null && !string.IsNullOrEmpty(description))
                _txtDescription.text = description;

            if (_txtBtnBuyOutline != null) _txtBtnBuyOutline.SetActive(true);
            if (_txtSingleOutline != null) _txtSingleOutline.SetActive(false);

            if (_txtItemAmount != null) _txtItemAmount.gameObject.SetActive(true);
            if (_txtItemAmountOutline != null) _txtItemAmountOutline.gameObject.SetActive(true);
            if (_txtGold != null && !IsTopBarGoldText(_txtGold)) _txtGold.gameObject.SetActive(true);
            if (_txtGoldOutline != null && !IsTopBarGoldText(_txtGoldOutline)) _txtGoldOutline.gameObject.SetActive(true);
            if (_imgCoin != null) _imgCoin.gameObject.SetActive(true);
            SetTopBarGoldPanelVisible(true);   // [#2] 유료 구매 — 잔액 노출

            SetItemDisplay(itemSprite, amount, goldCost);
            OpenUI();
        }

        /// <summary>Paid buy popup. Keeps the popup open when the confirm callback returns false.</summary>
        public void ShowBuyResult(string title, Sprite itemSprite, string amount, int goldCost,
                                  System.Func<bool> onConfirm = null, System.Action onCancel = null,
                                  string description = null)
        {
            ShowBuy(title, itemSprite, amount, goldCost, onConfirm: null, onCancel: onCancel, description: description);
            _onConfirmResult = onConfirm;
        }

        /// <summary>아이템 해금 팝업 표시 (Single 버튼).</summary>
        public void ShowUnlock(string title, Sprite itemSprite, int unlockLevel,
                               string amount = "x3",
                               System.Action onConfirm = null, System.Action onCancel = null,
                               string description = null)
        {
            // ROLLBACK_TUTORIAL_WAIT_UNLOCK_POPUP_20260623:
            // TutorialController must treat the booster-unlock Claim popup as the item
            // description popup, otherwise the tutorial can appear above it on level entry.
            _isUnlockMode = true;
            IsUnlockShowing = true;
            _onConfirm = onConfirm;
            _onConfirmResult = null;
            _onCancel = onCancel;

            if (_frame != null)
            {
                _frame.SetTitle(title);
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText(LocalizationService.Get("ui.common.cliam"));
                _frame.ShowExitButton(true);
            }

            if (_txtDescription != null && !string.IsNullOrEmpty(description))
                _txtDescription.text = description;

            if (_txtBtnBuyOutline != null) _txtBtnBuyOutline.SetActive(false);
            if (_txtSingleOutline != null)
            {
                _txtSingleOutline.SetActive(true);
                SetTextInChildren(_txtSingleOutline, LocalizationService.Get("ui.common.cliam"));
            }

            if (_txtItemAmount != null)
            {
                _txtItemAmount.gameObject.SetActive(true);
                _txtItemAmount.text = amount;
            }
            if (_txtItemAmountOutline != null)
            {
                _txtItemAmountOutline.gameObject.SetActive(true);
                _txtItemAmountOutline.text = amount;
            }
            if (_txtGold != null && !IsTopBarGoldText(_txtGold)) _txtGold.gameObject.SetActive(false);
            if (_txtGoldOutline != null && !IsTopBarGoldText(_txtGoldOutline)) _txtGoldOutline.gameObject.SetActive(false);
            if (_imgCoin != null) _imgCoin.gameObject.SetActive(false);
            SetTopBarGoldPanelVisible(false);   // [#2] 무료 제공(해금/Claim) — TopBar 잔액 GoldPanel 숨김

            if (_imgItem != null && itemSprite != null) _imgItem.sprite = itemSprite;
            OpenUI();
        }

        /// <summary>boosterType에 맞는 아이콘 스프라이트 반환.</summary>
        public Sprite GetBoosterSprite(string boosterType)
        {
            return boosterType switch
            {
                BoosterManager.SELECT_TOOL  => _sprHand,
                BoosterManager.SHUFFLE      => _sprShuffle,
                BoosterManager.COLOR_REMOVE => _sprZap,
                _                           => null
            };
        }

        /// <summary>기존 Show 호환 (구매 모드로 동작).</summary>
        public void Show(string title, Sprite itemSprite, string amount, int goldCost,
                         System.Action onConfirm = null, System.Action onCancel = null)
        {
            ShowBuy(title, itemSprite, amount, goldCost, onConfirm, onCancel);
        }

        private void SetItemDisplay(Sprite itemSprite, string amount, int goldCost)
        {
            if (_imgItem != null && itemSprite != null) _imgItem.sprite = itemSprite;
            if (_txtItemAmount != null) _txtItemAmount.text = amount;
            if (_txtItemAmountOutline != null) _txtItemAmountOutline.text = amount;

            string costStr = goldCost.ToString("N0");
            // ROLLBACK_BUYITEM_TOPBAR_BALANCE_FIX_20260622:
            // TopBar GoldPanel must always show the player's current coins. Some prefabs have
            // _txtGold wired to TopBar/GoldPanel/TxtGold, so do not write item prices there.
            if (_txtGold != null && !IsTopBarGoldText(_txtGold)) _txtGold.text = costStr;
            if (_txtGoldOutline != null && !IsTopBarGoldText(_txtGoldOutline)) _txtGoldOutline.text = costStr;
            SyncTopBarGoldPanelText();

            // [2026-06-12] Buy 버튼 내 가격 라벨 동적 적용 — 프리팹의 정적 숫자가 아이템과 무관하게
            // 노출되던 문제 (Hand 1900 / Shuffle 1500 / Zap 2900 은 호출부가 GetBoosterPrice 로 전달).
            // 버튼 하위 TMP 중 '숫자만'인 텍스트만 교체 — "Buy" 같은 라벨은 보존, 노드명 무관.
            SetNumericTextsInChildren(_txtBtnBuyOutline, costStr, replaceEmpty: true);
            if (_frame != null && _frame.BtnSingle != null)
                SetNumericTextsInChildren(_frame.BtnSingle.gameObject, costStr, replaceEmpty: false);
        }

        /// <summary>root 하위 TMP 중 콤마 제거 시 정수로 파싱되는(=가격 표기) 텍스트만 value 로 교체.</summary>
        private static void SetNumericTextsInChildren(GameObject root, string value, bool replaceEmpty)
        {
            if (root == null) return;
            TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] == null) continue;
                string t = labels[i].text != null ? labels[i].text.Replace(",", "").Trim() : null;
                // ROLLBACK_BUYITEM_PRICE_PLACEHOLDER_FIX_20260618:
                // Dynamic price labels can be raw "{n}" or be cleared by UIText on enable.
                // Recover empty labels only for the dedicated price-outline root so full-button
                // labels such as "Buy" are not overwritten.
                bool isDynamicPlaceholder = !string.IsNullOrEmpty(t) && t.IndexOf('{') >= 0;
                bool isNumeric = !string.IsNullOrEmpty(t) && int.TryParse(t, out _);
                bool isRecoverableEmpty = replaceEmpty && string.IsNullOrEmpty(t);
                if (isNumeric || isDynamicPlaceholder || isRecoverableEmpty)
                    labels[i].text = value;
            }
        }

        private static void SetTextInChildren(GameObject root, string text)
        {
            if (root == null) return;

            TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
                labels[i].text = text;
        }

        private void OnBuyClicked()
        {
            if (_onConfirmResult != null && !_onConfirmResult.Invoke())
                return;

            _onConfirm?.Invoke();
            if (_isUnlockMode) IsUnlockShowing = false;
            CloseUI();
        }

        private void OnCancelClicked()
        {
            _onCancel?.Invoke();
            if (_isUnlockMode) IsUnlockShowing = false;
            CloseUI();
        }
    }
}
