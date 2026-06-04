using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    public class PopupFail01 : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Buttons — 직접 할당]")]
        [SerializeField] private Button _btnContinue;
        [SerializeField] private Button _btnDecline;
        [SerializeField] private Button _btnExit;
        [SerializeField] private Button _btnGoldPlus;

        private const int OVERLAY_SORT_ORDER = 260; // Tutorial(=250) 위에 항상 표시 — 사용자 요청 2026-06-04
        private Canvas _overrideCanvas;

        [Header("[코스트 텍스트]")]
        [SerializeField] private TMP_Text _costText;
        [SerializeField] private TMP_Text _costTextOutline;

        [Header("[골드 표시 — 보수적 보존(미사용). TopBar 잔액은 AnimatedCoinLabel 가 갱신.]")]
        [SerializeField] private TMP_Text _txtGold;
        [SerializeField] private TMP_Text _txtGoldOutline;

        [Header("[ImgFail Animation]")]
        [SerializeField] private UnityEngine.Animation _imgFailAnimation;

        private Button ContinueBtn => _btnContinue != null ? _btnContinue : (_frame != null ? _frame.BtnHorizGreen : null);
        private Button DeclineBtn => _btnDecline != null ? _btnDecline : (_frame != null ? _frame.BtnHorizRed : null);
        private Button ExitBtn => _btnExit != null ? _btnExit : (_frame != null ? _frame.BtnExit : null);

        private void OnEnable()
        {
            UpdateCostDisplay();
            PlayImgFailAnimation();
            GoldPanelFxFireUtil.DisableUnderTopBarRoot(transform);
        }

        private void PlayImgFailAnimation()
        {
            if (_imgFailAnimation == null)
            {
                Transform imgFail = transform.Find("ImgFail");
                if (imgFail != null) _imgFailAnimation = imgFail.GetComponent<UnityEngine.Animation>();
            }
            if (_imgFailAnimation == null) return;

            _imgFailAnimation.Stop();
            _imgFailAnimation.Rewind();
            _imgFailAnimation.Play();
        }

        protected override void Awake()
        {
            base.Awake();
            EnsureOverlaySorting();
            if (ContinueBtn != null) ContinueBtn.onClick.AddListener(OnContinueClicked);
            if (DeclineBtn != null) DeclineBtn.onClick.AddListener(OnDeclineClicked);
            if (ExitBtn != null) ExitBtn.onClick.AddListener(OnDeclineClicked);

            if (_btnGoldPlus == null)
            {
                Transform found = FindChildRecursive(transform, "GoldPlusBtn");
                if (found != null) _btnGoldPlus = found.GetComponent<Button>();
            }
            if (_btnGoldPlus != null) _btnGoldPlus.onClick.AddListener(OnGoldPlusClicked);

            EnsureTopBarBinding();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (ContinueBtn != null) ContinueBtn.onClick.RemoveAllListeners();
            if (DeclineBtn != null) DeclineBtn.onClick.RemoveAllListeners();
            if (ExitBtn != null) ExitBtn.onClick.RemoveAllListeners();
            if (_btnGoldPlus != null) _btnGoldPlus.onClick.RemoveAllListeners();
        }

        /// <summary>
        /// PopupFail01은 Tutorial(sortingOrder=250) 위에 항상 표시되어야 함 — 사용자 요청 2026-06-04.
        /// Tutorial이 자체 Canvas.overrideSorting=true 로 PopupCanvas(=200)을 덮어쓰므로,
        /// 같은 메커니즘으로 PopupFail01 에도 Canvas+GraphicRaycaster 런타임 부착 + sortingOrder 260 부여.
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

        public void Show(DifficultyPurpose difficulty)
        {
            if (_frame != null)
            {
                _frame.ApplyDifficulty(difficulty);
                _frame.SetTitle("Continue?");
                _frame.SetDescription("Spend coins to keep playing.");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.SetHorizGreenText("Continue");
                _frame.SetHorizRedText("Give Up");
                _frame.ShowExitButton(true);
            }
            UpdateCostDisplay();
            OpenUI();
        }

        private void OnContinueClicked()
        {
            if (!ContinueHandler.HasInstance) return;

            int cost = ContinueHandler.Instance.GetContinueCost();
            if (CurrencyManager.HasInstance) CurrencyManager.Instance.PublishCoinSync();

            int coins = CurrencyManager.HasInstance ? CurrencyManager.Instance.Coins : -1;
            if (cost > 0 && (!CurrencyManager.HasInstance || !CurrencyManager.Instance.HasEnoughCoins(cost)))
            {
                Debug.LogWarning($"[PopupFail01] Continue blocked by coins. have={coins}, need={cost}");
                if (UIManager.HasInstance)
                {
                    var err = UIManager.Instance.OpenUI<PopupError>("Popup/PopupError");
                    if (err != null) err.ShowPaymentFailed("Not enough coins.");
                }
                Debug.Log("[PopupFail01] 골드 부족");
                return;
            }

            bool success = ContinueHandler.Instance.Continue();
            if (success)
            {
                if (PopupManager.HasInstance) PopupManager.Instance.ClosePopup("popup_fail01");
            }
            else if (UIManager.HasInstance)
            {
                var err = UIManager.Instance.OpenUI<PopupError>("Popup/PopupError");
                if (err != null) err.Show("Continue Failed", "Continue could not be completed. Please try again.");
            }
        }

        private void OnDeclineClicked()
        {
            if (PopupManager.HasInstance)
            {
                PopupManager.Instance.ClosePopup("popup_fail01");
                PopupManager.Instance.ShowPopup("popup_continue", 50);
            }
        }

        private void OnGoldPlusClicked()
        {
            if (PopupManager.HasInstance)
                PopupManager.Instance.ClosePopup("popup_fail01");

            if (HUDController.HasInstance && HUDController.Instance.GoldShopPopup != null)
            {
                HUDController.Instance.GoldShopPopup.OpenWithCloseCallback(() =>
                {
                    if (PopupManager.HasInstance)
                        PopupManager.Instance.ShowPopup("popup_fail01", 50);
                });
            }
        }

        private void UpdateCostDisplay()
        {
            if (!ContinueHandler.HasInstance) return;
            int cost = ContinueHandler.Instance.GetContinueCost();
            string costStr = cost <= 0 ? "FREE" : cost.ToString("N0");
            if (_costText != null) _costText.text = costStr;
            if (_costTextOutline != null) _costTextOutline.text = costStr;
        }
    }
}
