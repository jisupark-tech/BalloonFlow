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
        [Header("[난이도별 프레임]")]
        [SerializeField] private Image _resultPanel;
        [SerializeField] private Image _leftTopSidePanel;
        [SerializeField] private Image _rightTopSidePanel;

        [Header("[난이도 스프라이트 — ResultPanel]")]
        [SerializeField] private Sprite _framePopupNormal;
        [SerializeField] private Sprite _framePopupHard;
        [SerializeField] private Sprite _framePopupSuperHard;

        [Header("[난이도 스프라이트 — SidePanel]")]
        [SerializeField] private Sprite _frameResultNormal;
        [SerializeField] private Sprite _frameResultHard;
        [SerializeField] private Sprite _frameResultSuperHard;

        [Header("[버튼]")]
        [SerializeField] private Button _retryButton;
        [SerializeField] private Button _homeButton;

        private const int RETRY_BONUS_GOLD = 20;

        private void Start()
        {
            if (_retryButton != null) _retryButton.onClick.AddListener(OnRetryClicked);
            if (_homeButton != null) _homeButton.onClick.AddListener(OnHomeClicked);
        }
        public void Show(DifficultyPurpose difficulty)
        {
            ApplyDifficultyFrames(difficulty);
            OpenUI();
        }
        private void ApplyDifficultyFrames(DifficultyPurpose difficulty)
        {
            Sprite popupFrame = _framePopupNormal;
            Sprite sideFrame = _frameResultNormal;

            switch (difficulty)
            {
                case DifficultyPurpose.Hard:
                    popupFrame = _framePopupHard;
                    sideFrame = _frameResultHard;
                    break;
                case DifficultyPurpose.SuperHard:
                    popupFrame = _framePopupSuperHard;
                    sideFrame = _frameResultSuperHard;
                    break;
            }

            if (_resultPanel != null && popupFrame != null) _resultPanel.sprite = popupFrame;
            if (_leftTopSidePanel != null && sideFrame != null) _leftTopSidePanel.sprite = sideFrame;
            if (_rightTopSidePanel != null && sideFrame != null) _rightTopSidePanel.sprite = sideFrame;
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

            // 로비씬 이동 (GameManager 경유 → CleanupInGame 실행)
            if (GameManager.HasInstance)
                GameManager.Instance.LoadScene(GameManager.SCENE_LOBBY);
            else
                UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby");
        }
    }
}
