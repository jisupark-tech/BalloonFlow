using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 결과 팝업. Resources/Popup/PopupResult 프리팹에서 로드.
    /// Clear: 별 표시 + Next/Home (Retry 숨김) + 코인 비행 연출
    /// Fail: Retry/Home (Next 숨김) + 별 회색 처리
    /// </summary>
    public class PopupResult : UIBase
    {
        #region Constants

        private const int MAX_STARS = 3;
        private const float COIN_DELAY_AFTER_POPUP = 0.6f;
        private const int MIN_COIN_COUNT = 8;
        private const int MAX_COIN_COUNT = 20;
        private const int SCORE_PER_COIN_STEP = 500;

        // Star colors
        private static readonly Color STAR_EARNED_COLOR = new Color(1f, 0.85f, 0.1f);  // Gold
        private static readonly Color STAR_UNEARNED_COLOR = new Color(0.4f, 0.4f, 0.4f, 0.5f);  // Grey

        #endregion

        [Header("[Result 텍스트]")]
        [SerializeField] private Text _titleText;

        [Header("[버튼]")]
        [SerializeField] private Button _nextButton;
        [SerializeField] private Button _retryButton;
        [SerializeField] private Button _homeButton;

        [Header("[별 표시 — 3개 Image, 왼쪽부터 순서대로]")]
        [SerializeField] private Image[] _starImages;

        [Header("[코인 연출 — Gold HUD 위치]")]
        [SerializeField] private RectTransform _goldTarget;

        public Button NextButton => _nextButton;
        public Button RetryButton => _retryButton;
        public Button HomeButton => _homeButton;
        public RectTransform GoldTarget => _goldTarget;

        /// <summary>
        /// Sets the gold target RectTransform for coin fly effect.
        /// Called by GameBootstrap to wire the HUD gold display.
        /// </summary>
        public void SetGoldTarget(RectTransform target)
        {
            _goldTarget = target;
        }

        public void ShowWin(int _score, int _stars)
        {
            if (_titleText != null)
            {
                _titleText.text = "Level Clear!";
                _titleText.color = new Color(1f, 0.85f, 0.1f);
            }
            // Hide Retry on clear, show Next
            if (_retryButton != null)
                _retryButton.gameObject.SetActive(false);
            if (_nextButton != null)
                _nextButton.gameObject.SetActive(true);

            // Show stars
            UpdateStarDisplay(_stars);

            OpenUI();

            // Trigger coin fly effect after a brief delay
            StartCoroutine(TriggerCoinFlyDelayed(_score));
        }

        public void ShowFail()
        {
            if (_titleText != null)
            {
                _titleText.text = "Game Over";
                _titleText.color = new Color(1f, 0.3f, 0.3f);
            }
            // Show Retry on fail, hide Next
            if (_retryButton != null)
                _retryButton.gameObject.SetActive(true);
            if (_nextButton != null)
                _nextButton.gameObject.SetActive(false);

            // Grey out all stars
            UpdateStarDisplay(0);

            OpenUI();
        }

        #region Star Display

        private void UpdateStarDisplay(int earnedCount)
        {
            if (_starImages == null || _starImages.Length == 0) return;

            int clampedStars = Mathf.Clamp(earnedCount, 0, MAX_STARS);

            for (int i = 0; i < _starImages.Length; i++)
            {
                if (_starImages[i] == null) continue;

                _starImages[i].gameObject.SetActive(true);

                if (i < clampedStars)
                {
                    // Earned star — gold
                    _starImages[i].color = STAR_EARNED_COLOR;
                    _starImages[i].transform.localScale = Vector3.one;
                }
                else
                {
                    // Unearned star — grey
                    _starImages[i].color = STAR_UNEARNED_COLOR;
                    _starImages[i].transform.localScale = Vector3.one * 0.85f;
                }
            }
        }

        #endregion

        #region Coin Fly Effect

        private IEnumerator TriggerCoinFlyDelayed(int score)
        {
            yield return new WaitForSeconds(COIN_DELAY_AFTER_POPUP);

            RectTransform target = _goldTarget;

            // Fallback: try to find gold text from HUD
            if (target == null && HUDController.HasInstance)
            {
                var hud = HUDController.Instance.GetComponent<UIHud>();
                if (hud == null)
                {
                    // Try to find UIHud in scene
                    hud = FindAnyObjectByType<UIHud>();
                }
                if (hud != null && hud.GoldText != null)
                {
                    target = hud.GoldText.rectTransform;
                }
            }

            if (target == null)
            {
                Debug.LogWarning("[PopupResult] Gold target not found, skipping coin fly effect.");
                yield break;
            }

            // Find a parent canvas for the effect
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                canvas = FindAnyObjectByType<Canvas>();
            }
            if (canvas == null) yield break;

            // Determine coin count based on score
            int coinCount = Mathf.Clamp(
                MIN_COIN_COUNT + (score / SCORE_PER_COIN_STEP),
                MIN_COIN_COUNT,
                MAX_COIN_COUNT);

            // Source: screen center
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            var effect = CoinFlyEffect.Create(canvas);
            if (effect != null)
            {
                effect.Play(screenCenter, target, coinCount,
                    onEachLand: () =>
                    {
                        // Increment gold display per coin landing
                        EventBus.Publish(new OnCoinFlyLanded());
                    });
            }
        }

        #endregion
    }
}
