using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 결과 팝업. Resources/Popup/PopupResult 프리팹에서 로드.
    /// </summary>
    public class PopupResult : UIBase
    {
        [Header("[Result 텍스트]")]
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _scoreText;

        [Header("[버튼]")]
        [SerializeField] private Button _nextButton;
        [SerializeField] private Button _retryButton;
        [SerializeField] private Button _homeButton;

        public Button NextButton => _nextButton;
        public Button RetryButton => _retryButton;
        public Button HomeButton => _homeButton;

        public void ShowWin(int _score, int _stars)
        {
            if (_titleText != null)
            {
                _titleText.text = "Level Clear!";
                _titleText.color = new Color(1f, 0.85f, 0.1f);
            }
            if (_scoreText != null)
                _scoreText.text = $"Score: {_score:N0}\n★ {_stars} / 3";
            if (_nextButton != null)
                _nextButton.gameObject.SetActive(true);

            OpenUI();
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

            OpenUI();
        }
    }
}
