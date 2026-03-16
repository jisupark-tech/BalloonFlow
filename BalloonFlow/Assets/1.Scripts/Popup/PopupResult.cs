using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// Result popup view. Loaded from Resources/Popup/PopupResult prefab.
    /// All child references wired via UIPrefabBuilder at editor-time.
    /// </summary>
    public class PopupResult : MonoBehaviour
    {
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _scoreText;
        [SerializeField] private Button _nextButton;
        [SerializeField] private Button _retryButton;
        [SerializeField] private Button _homeButton;

        private CanvasGroup _canvasGroup;

        public Button NextButton => _nextButton;
        public Button RetryButton => _retryButton;
        public Button HomeButton => _homeButton;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
        }

        public void ShowWin(int score, int stars)
        {
            if (_titleText != null)
            {
                _titleText.text = "Level Clear!";
                _titleText.color = new Color(1f, 0.85f, 0.1f);
            }
            if (_scoreText != null)
                _scoreText.text = $"Score: {score:N0}\n★ {stars} / 3";
            if (_nextButton != null)
                _nextButton.gameObject.SetActive(true);

            Show();
        }

        public void ShowFail()
        {
            if (_titleText != null)
            {
                _titleText.text = "Game Over";
                _titleText.color = new Color(1f, 0.3f, 0.3f);
            }
            if (_scoreText != null)
                _scoreText.text = "Better luck next time!";
            if (_nextButton != null)
                _nextButton.gameObject.SetActive(false);

            Show();
        }

        public void Show()
        {
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = 1f;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }
    }
}
