using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

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

        [Header("[코스트 텍스트]")]
        [SerializeField] private TMP_Text _costText;
        [SerializeField] private TMP_Text _costTextOutline;

        [Header("[골드 표시]")]
        [SerializeField] private TMP_Text _txtGold;
        [SerializeField] private TMP_Text _txtGoldOutline;

        [Header("[ImgFail Animation]")]
        [SerializeField] private UnityEngine.Animation _imgFailAnimation;

        private Button ContinueBtn => _btnContinue != null ? _btnContinue : (_frame != null ? _frame.BtnHorizGreen : null);
        private Button DeclineBtn => _btnDecline != null ? _btnDecline : (_frame != null ? _frame.BtnHorizRed : null);
        private Button ExitBtn => _btnExit != null ? _btnExit : (_frame != null ? _frame.BtnExit : null);

        private int _displayedCoins;
        private Tweener _goldTween;

        private void OnEnable()
        {
            UpdateCostDisplay();
            UpdateGoldDisplay();
            PlayImgFailAnimation();
            EventBus.Subscribe<OnCoinChanged>(HandleCoinChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnCoinChanged>(HandleCoinChanged);
            _goldTween?.Kill();
        }

        private void HandleCoinChanged(OnCoinChanged evt)
        {
            if (evt.delta == 0)
            {
                _goldTween?.Kill();
                _displayedCoins = evt.currentCoins;
                ApplyTopBarGold(evt.currentCoins);
                return;
            }
            AnimateTopBarGold(evt.currentCoins);
        }

        private void AnimateTopBarGold(int target)
        {
            _goldTween?.Kill();
            if (_displayedCoins == target)
            {
                ApplyTopBarGold(target);
                return;
            }
            _goldTween = DOTween.To(
                    () => _displayedCoins,
                    v => { _displayedCoins = v; ApplyTopBarGold(v); },
                    target,
                    0.45f)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    _displayedCoins = target;
                    ApplyTopBarGold(target);
                });
        }

        private void ApplyTopBarGold(int value)
        {
            string str = value.ToString("N0");
            if (_txtGold != null) _txtGold.text = str;
            if (_txtGoldOutline != null) _txtGoldOutline.text = str;
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
            if (ContinueBtn != null) ContinueBtn.onClick.AddListener(OnContinueClicked);
            if (DeclineBtn != null) DeclineBtn.onClick.AddListener(OnDeclineClicked);
            if (ExitBtn != null) ExitBtn.onClick.AddListener(OnDeclineClicked);

            if (_btnGoldPlus == null)
            {
                Transform found = FindChildRecursive(transform, "GoldPlusBtn");
                if (found != null) _btnGoldPlus = found.GetComponent<Button>();
            }
            if (_btnGoldPlus != null) _btnGoldPlus.onClick.AddListener(OnGoldPlusClicked);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (ContinueBtn != null) ContinueBtn.onClick.RemoveAllListeners();
            if (DeclineBtn != null) DeclineBtn.onClick.RemoveAllListeners();
            if (ExitBtn != null) ExitBtn.onClick.RemoveAllListeners();
            if (_btnGoldPlus != null) _btnGoldPlus.onClick.RemoveAllListeners();
            _goldTween?.Kill();
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
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.SetHorizGreenText("Continue");
                _frame.SetHorizRedText("Give Up");
                _frame.ShowExitButton(true);
            }
            UpdateCostDisplay();
            UpdateGoldDisplay();
            OpenUI();
        }

        private void UpdateGoldDisplay()
        {
            if (!CurrencyManager.HasInstance) return;
            _goldTween?.Kill();
            _displayedCoins = CurrencyManager.Instance.Coins;
            ApplyTopBarGold(_displayedCoins);
        }

        private void OnContinueClicked()
        {
            if (!ContinueHandler.HasInstance) return;

            int cost = ContinueHandler.Instance.GetContinueCost();
            if (CurrencyManager.HasInstance && CurrencyManager.Instance.Coins < cost && cost > 0)
            {
                Debug.Log("[PopupFail01] 골드 부족");
                return;
            }

            bool success = ContinueHandler.Instance.Continue();
            if (success)
            {
                if (PopupManager.HasInstance) PopupManager.Instance.ClosePopup("popup_fail01");
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
