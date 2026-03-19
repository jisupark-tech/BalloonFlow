using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 실패 시 마지막 팝업 (PopupContinue에서 거절 후).
    /// RetryButton: 하트 차감 + 해당 레벨 재시작. 클리어 시 보너스 20골드.
    /// HomeButton: 로비씬 이동.
    /// Flow: PopupFail01 → PopupContinue → PopupFail02
    /// </summary>
    public class PopupFail02 : UIBase
    {
        [Header("[버튼]")]
        [SerializeField] private Button _retryButton;
        [SerializeField] private Button _homeButton;

        private const int RETRY_BONUS_GOLD = 20;

        private void Start()
        {
            if (_retryButton != null) _retryButton.onClick.AddListener(OnRetryClicked);
            if (_homeButton != null) _homeButton.onClick.AddListener(OnHomeClicked);
        }

        private void OnDestroy()
        {
            if (_retryButton != null) _retryButton.onClick.RemoveAllListeners();
            if (_homeButton != null) _homeButton.onClick.RemoveAllListeners();
        }

        private void OnRetryClicked()
        {
            // 하트 확인 + 차감
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

            // 해당 레벨 재시작 + 클리어 시 보너스 골드 표시용 플래그
            if (LevelManager.HasInstance)
            {
                LevelManager.Instance.RetryLevel();
            }

            Debug.Log($"[PopupFail02] Retry — 클리어 시 보너스 {RETRY_BONUS_GOLD} 골드");
        }

        private void OnHomeClicked()
        {
            CloseUI();

            if (PopupManager.HasInstance)
                PopupManager.Instance.CloseAllPopups();

            // 로비씬 이동
            UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby");
        }
    }
}
