using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    public class PopupContinue : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Buttons — 직접 할당]")]
        [SerializeField] private Button _btnContinue;
        [SerializeField] private Button _btnDecline;
        [SerializeField] private Button _btnExit;

        [Header("[코스트 텍스트]")]
        [SerializeField] private Text _costText;

        [Header("[골드 표시 — 보수적 보존(미사용). TopBar 잔액은 AnimatedCoinLabel 가 갱신.]")]
        [SerializeField] private TMP_Text _txtGold;
        [SerializeField] private TMP_Text _txtGoldOutline;

        [Header("[ContinuePanel — 난이도별 inner frame]")]
        [SerializeField] private Image _imageContinuePanel;
        [SerializeField] private Sprite _sprContinuePanelNormal;
        [SerializeField] private Sprite _sprContinuePanelHard;
        [SerializeField] private Sprite _sprContinuePanelSuperHard;

        private Button ContinueBtn => _btnContinue != null ? _btnContinue : (_frame != null ? _frame.BtnHorizGreen : null);
        private Button DeclineBtn => _btnDecline != null ? _btnDecline : (_frame != null ? _frame.BtnHorizRed : null);
        private Button ExitBtn => _btnExit != null ? _btnExit : (_frame != null ? _frame.BtnExit : null);

        public Button ContinueButton => ContinueBtn;
        public Button DeclineButton => DeclineBtn;

        private void OnEnable()
        {
            UpdateCostDisplay();

            DifficultyPurpose diff = DifficultyPurpose.Normal;
            if (LevelManager.HasInstance)
                diff = LevelManager.Instance.GetLevelDifficulty(LevelManager.Instance.CurrentLevelId);
            if (_frame != null) _frame.ApplyDifficulty(diff);
            ApplyContinuePanelDifficulty(diff);
        }

        protected override void Awake()
        {
            base.Awake();

            if (ResourceManager.HasInstance)
            {
                var rm = ResourceManager.Instance;
                _sprContinuePanelNormal    = rm.UISpriteOr(Const.SPR_FRAMEPOPUPCONTINUENORMAL,    _sprContinuePanelNormal);
                _sprContinuePanelHard      = rm.UISpriteOr(Const.SPR_FRAMEPOPUPCONTINUEHARD,      _sprContinuePanelHard);
                _sprContinuePanelSuperHard = rm.UISpriteOr(Const.SPR_FRAMEPOPUPCONTINUESUPERHARD, _sprContinuePanelSuperHard);
            }

            // 프리팹에 _imageContinuePanel 가 미와이어링이면 ContinuePanel 자식에서 자동 탐색
            if (_imageContinuePanel == null)
            {
                Transform panel = FindChildRecursive(transform, "ContinuePanel");
                if (panel != null) _imageContinuePanel = panel.GetComponent<Image>();
            }

            if (ContinueBtn != null) ContinueBtn.onClick.AddListener(OnContinueClicked);
            if (DeclineBtn != null) DeclineBtn.onClick.AddListener(OnDeclineClicked);
            if (ExitBtn != null) ExitBtn.onClick.AddListener(OnDeclineClicked);

            EnsureTopBarBinding();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (ContinueBtn != null) ContinueBtn.onClick.RemoveAllListeners();
            if (DeclineBtn != null) DeclineBtn.onClick.RemoveAllListeners();
            if (ExitBtn != null) ExitBtn.onClick.RemoveAllListeners();
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

        public void Show()
        {
            DifficultyPurpose diff = DifficultyPurpose.Normal;
            if (LevelManager.HasInstance)
                diff = LevelManager.Instance.GetLevelDifficulty(LevelManager.Instance.CurrentLevelId);

            if (_frame != null)
            {
                _frame.ApplyDifficulty(diff);
                _frame.SetTitle("Continue?");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.SetHorizGreenText("Continue");
                _frame.SetHorizRedText("Give Up");
                _frame.ShowExitButton(true);
            }
            ApplyContinuePanelDifficulty(diff);
            UpdateCostDisplay();
            OpenUI();
        }

        private void ApplyContinuePanelDifficulty(DifficultyPurpose difficulty)
        {
            if (_imageContinuePanel == null) return;
            Sprite chosen = difficulty switch
            {
                DifficultyPurpose.Hard      => _sprContinuePanelHard,
                DifficultyPurpose.SuperHard => _sprContinuePanelSuperHard,
                _                           => _sprContinuePanelNormal
            };
            if (chosen != null) _imageContinuePanel.sprite = chosen;
        }

        public void OnContinueClicked()
        {
            if (!ContinueHandler.HasInstance) return;

            int cost = ContinueHandler.Instance.GetContinueCost();
            if (CurrencyManager.HasInstance && CurrencyManager.Instance.Coins < cost && cost > 0)
            {
                Debug.Log("[PopupContinue] 골드 부족");
                return;
            }

            bool success = ContinueHandler.Instance.Continue();
            if (success)
            {
                if (PopupManager.HasInstance) PopupManager.Instance.ClosePopup("popup_continue");
            }
            else
            {
                OnDeclineClicked();
            }
        }

        public void OnDeclineClicked()
        {
            if (PopupManager.HasInstance)
            {
                PopupManager.Instance.ClosePopup("popup_continue");
                PopupManager.Instance.ShowPopup("popup_fail02", 50);
            }
        }

        private void UpdateCostDisplay()
        {
            if (_costText == null || !ContinueHandler.HasInstance) return;
            int cost = ContinueHandler.Instance.GetContinueCost();
            _costText.text = cost <= 0 ? "FREE" : cost.ToString("N0");
        }
    }
}
