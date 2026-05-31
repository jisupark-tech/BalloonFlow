using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 아이템 구매/해금 확인 팝업.
    /// 구매 모드: Single 버튼 (Buy) — TxtBtnBuyOutline.
    /// 해금 모드: Single 버튼 — TxtSingleOutline.
    /// </summary>
    public class PopupBuyItem : UIBase
    {
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

        private System.Action _onConfirm;
        private System.Action _onCancel;

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

        private void EnsureTopBarBinding()
        {
            Transform topBar = FindChildRecursive(transform, "TopBarArea");
            Transform gold = topBar != null ? FindChildRecursive(topBar, "GoldPanel") : null;
            _topBarGoldPanel = gold;
            Transform txt = gold != null ? FindChildRecursive(gold, "TxtGold") : null;
            if (txt != null && txt.GetComponent<AnimatedCoinLabel>() == null)
                txt.gameObject.AddComponent<AnimatedCoinLabel>();
        }

        /// <summary>[#2] TopBar 잔액 GoldPanel 노출 토글.</summary>
        private void SetTopBarGoldPanelVisible(bool visible)
        {
            if (_topBarGoldPanel == null) EnsureTopBarBinding();
            if (_topBarGoldPanel != null && _topBarGoldPanel.gameObject.activeSelf != visible)
                _topBarGoldPanel.gameObject.SetActive(visible);
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
                            System.Action onConfirm = null, System.Action onCancel = null)
        {
            _onConfirm = onConfirm;
            _onCancel = onCancel;

            if (_frame != null)
            {
                _frame.SetTitle(title);
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText("Buy");
                _frame.ShowExitButton(true);
            }

            if (_txtBtnBuyOutline != null) _txtBtnBuyOutline.SetActive(true);
            if (_txtSingleOutline != null) _txtSingleOutline.SetActive(false);

            if (_txtItemAmount != null) _txtItemAmount.gameObject.SetActive(true);
            if (_txtItemAmountOutline != null) _txtItemAmountOutline.gameObject.SetActive(true);
            if (_txtGold != null) _txtGold.gameObject.SetActive(true);
            if (_txtGoldOutline != null) _txtGoldOutline.gameObject.SetActive(true);
            if (_imgCoin != null) _imgCoin.gameObject.SetActive(true);
            SetTopBarGoldPanelVisible(true);   // [#2] 유료 구매 — 잔액 노출

            SetItemDisplay(itemSprite, amount, goldCost);
            OpenUI();
        }

        /// <summary>아이템 해금 팝업 표시 (Single 버튼).</summary>
        public void ShowUnlock(string title, Sprite itemSprite, int unlockLevel,
                               string amount = "x3",
                               System.Action onConfirm = null, System.Action onCancel = null)
        {
            _onConfirm = onConfirm;
            _onCancel = onCancel;

            if (_frame != null)
            {
                _frame.SetTitle(title);
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText("Claim");
                _frame.ShowExitButton(true);
            }

            if (_txtBtnBuyOutline != null) _txtBtnBuyOutline.SetActive(false);
            if (_txtSingleOutline != null)
            {
                _txtSingleOutline.SetActive(true);
                SetTextInChildren(_txtSingleOutline, "Claim");
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
            if (_txtGold != null) _txtGold.gameObject.SetActive(false);
            if (_txtGoldOutline != null) _txtGoldOutline.gameObject.SetActive(false);
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
            if (_txtGold != null) _txtGold.text = costStr;
            if (_txtGoldOutline != null) _txtGoldOutline.text = costStr;
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
            _onConfirm?.Invoke();
            CloseUI();
        }

        private void OnCancelClicked()
        {
            _onCancel?.Invoke();
            CloseUI();
        }
    }
}
