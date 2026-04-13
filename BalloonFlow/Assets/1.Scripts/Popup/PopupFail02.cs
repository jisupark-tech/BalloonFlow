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

        [Header("[골드 표시]")]
        [SerializeField] private TMP_Text _txtGold;
        [SerializeField] private TMP_Text _txtGoldOutline;

        private const int RETRY_BONUS_GOLD = 20;

        private Button RetryBtn => _btnRetry != null ? _btnRetry : (_frame != null ? _frame.BtnHorizGreen : null);
        private Button HomeBtn => _btnHome != null ? _btnHome : (_frame != null ? _frame.BtnHorizRed : null);
        private Button ExitBtn => _btnExit != null ? _btnExit : (_frame != null ? _frame.BtnExit : null);

        protected override void Awake()
        {
            base.Awake();
            if (RetryBtn != null) RetryBtn.onClick.AddListener(OnRetryClicked);
            if (HomeBtn != null) HomeBtn.onClick.AddListener(OnHomeClicked);
            if (ExitBtn != null) ExitBtn.onClick.AddListener(OnHomeClicked);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (RetryBtn != null) RetryBtn.onClick.RemoveAllListeners();
            if (HomeBtn != null) HomeBtn.onClick.RemoveAllListeners();
            if (ExitBtn != null) ExitBtn.onClick.RemoveAllListeners();
        }

        private bool _lifeConsumed;

        private void OnEnable()
        {
            // PopupManager가 SetActive(true) 할 때 호출됨
            // 실패 확정 시 하트 1개 소모
            if (!_lifeConsumed && LifeManager.HasInstance)
            {
                LifeManager.Instance.UseLive();
                _lifeConsumed = true;
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
                _frame.SetTitle("Stage Failed");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.SetHorizGreenText("Retry");
                _frame.SetHorizRedText("Home");
                _frame.ShowExitButton(true);
            }
            UpdateGoldDisplay();
            OpenUI();
        }

        private void UpdateGoldDisplay()
        {
            if (!CurrencyManager.HasInstance) return;
            string gold = CurrencyManager.Instance.Coins.ToString("N0");
            if (_txtGold != null) _txtGold.text = gold;
            if (_txtGoldOutline != null) _txtGoldOutline.text = gold;
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

            CloseUI();

            if (LevelManager.HasInstance)
                LevelManager.Instance.RetryLevel();

            Debug.Log($"[PopupFail02] Retry — 클리어 시 보너스 {RETRY_BONUS_GOLD} 골드");
        }

        private void OnHomeClicked()
        {
            CloseUI();
            if (PopupManager.HasInstance) PopupManager.Instance.CloseAllPopups();
            if (GameManager.HasInstance) GameManager.Instance.LoadScene(GameManager.SCENE_LOBBY);
        }
    }
}
