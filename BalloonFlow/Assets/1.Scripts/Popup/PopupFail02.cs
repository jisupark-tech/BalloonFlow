using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 실패 시 마지막 팝업 (PopupContinue에서 거절 후).
    /// PopupCommonFrame: Horizontal 레이아웃 (Green=Retry, Red=Home).
    /// RetryButton: 하트 차감 + 해당 레벨 재시작.
    /// HomeButton: 로비씬 이동.
    /// </summary>
    public class PopupFail02 : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        private const int RETRY_BONUS_GOLD = 20;

        private void Start()
        {
            if (_frame != null)
            {
                if (_frame.BtnHorizGreen != null) _frame.BtnHorizGreen.onClick.AddListener(OnRetryClicked);
                if (_frame.BtnHorizRed != null) _frame.BtnHorizRed.onClick.AddListener(OnHomeClicked);
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.AddListener(OnHomeClicked);
            }
        }

        private void OnDestroy()
        {
            if (_frame != null)
            {
                if (_frame.BtnHorizGreen != null) _frame.BtnHorizGreen.onClick.RemoveAllListeners();
                if (_frame.BtnHorizRed != null) _frame.BtnHorizRed.onClick.RemoveAllListeners();
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.RemoveAllListeners();
            }
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
            OpenUI();
        }

        private void OnRetryClicked()
        {
            if (LifeManager.HasInstance)
            {
                if (LifeManager.Instance.CurrentLives <= 0)
                {
                    Debug.Log("[PopupFail02] 하트 부족 — 로비로 이동");
                    OnHomeClicked();
                    return;
                }
                LifeManager.Instance.UseLive();
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
