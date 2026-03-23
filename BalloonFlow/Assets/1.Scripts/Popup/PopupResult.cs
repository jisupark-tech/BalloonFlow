using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 클리어 결과 팝업.
    /// 난이도별 프레임 변경 (Normal/Hard/SuperHard).
    /// NextButton: 다음 레벨 (하트 있으면 진입, 없으면 로비).
    /// HomeButton: 로비씬 이동.
    /// </summary>
    public class PopupResult : UIBase
    {
        #region Constants

        private const int MAX_STARS = 3;
        private const float COIN_DELAY_AFTER_POPUP = 0.6f;
        private const int MIN_COIN_COUNT = 8;
        private const int MAX_COIN_COUNT = 20;
        private const int SCORE_PER_COIN_STEP = 500;

        private static readonly Color STAR_EARNED_COLOR = new Color(1f, 0.85f, 0.1f);
        private static readonly Color STAR_UNEARNED_COLOR = new Color(0.4f, 0.4f, 0.4f, 0.5f);

        #endregion

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

        [Header("[Result 텍스트]")]
        [SerializeField] private Text _titleText;

        [Header("[버튼]")]
        [SerializeField] private Button _nextButton;
        [SerializeField] private Button _homeButton;

        [Header("[별 표시 — 3개 Image, 왼쪽부터 순서대로]")]
        [SerializeField] private Image[] _starImages;

        [Header("[코인 연출 — Gold HUD 위치]")]
        [SerializeField] private RectTransform _goldTarget;

        public Button NextButton => _nextButton;
        public Button RetryButton => null; // PopupFail02로 이동됨. GameBootstrap 호환용.
        public Button HomeButton => _homeButton;
        public RectTransform GoldTarget => _goldTarget;

        /// <summary>GameBootstrap 호환: 실패 시 PopupFail01을 대신 표시.</summary>
        public void ShowFail()
        {
            // 실패 흐름은 PopupFail01 → PopupContinue → PopupFail02로 변경됨.
            // GameBootstrap에서 이 메서드를 호출하면 PopupFail01을 띄움.
            if (PopupManager.HasInstance)
            {
                PopupManager.Instance.ShowPopup("popup_fail01", 50);
            }
        }

        private void Start()
        {
            if (_nextButton != null) _nextButton.onClick.AddListener(OnNextClicked);
            if (_homeButton != null) _homeButton.onClick.AddListener(OnHomeClicked);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_nextButton != null) _nextButton.onClick.RemoveAllListeners();
            if (_homeButton != null) _homeButton.onClick.RemoveAllListeners();
        }

        public void SetGoldTarget(RectTransform target)
        {
            _goldTarget = target;
        }

        public void ShowWin(int _score, int _stars, DifficultyPurpose difficulty = DifficultyPurpose.Normal)
        {
            ApplyDifficultyFrames(difficulty);

            if (_titleText != null)
            {
                _titleText.text = "Level Clear!";
                _titleText.color = new Color(1f, 0.85f, 0.1f);
            }

            UpdateStarDisplay(_stars);
            OpenUI();

            StartCoroutine(TriggerCoinFlyDelayed(_score));
        }

        #region Button Handlers

        private void OnNextClicked()
        {
            CloseUI();

            // MapMaker에서 테스트 플레이 → MapMaker 씬으로 복귀
            if (GameManager.IsTestPlayMode)
            {
                if (PopupManager.HasInstance)
                    PopupManager.Instance.CloseAllPopups();

                if (GameManager.HasInstance)
                    GameManager.Instance.LoadScene(GameManager.SCENE_MAPMAKER);
                return;
            }

            // 하트 확인
            if (LifeManager.HasInstance && LifeManager.Instance.CurrentLives <= 0)
            {
                if (GameManager.HasInstance)
                    GameManager.Instance.LoadScene(GameManager.SCENE_LOBBY);
                else
                    UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby");
                return;
            }

            // 다음 레벨 진입
            if (LevelManager.HasInstance)
            {
                int nextId = LevelManager.Instance.GetNextLevelId();
                LevelManager.Instance.LoadLevel(nextId);
            }
        }

        private void OnHomeClicked()
        {
            CloseUI();

            if (PopupManager.HasInstance)
                PopupManager.Instance.CloseAllPopups();

            if (GameManager.HasInstance)
                GameManager.Instance.LoadScene(GameManager.SCENE_LOBBY);
            else
                UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby");
        }

        #endregion

        #region Difficulty Frames

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

        #endregion

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
                    _starImages[i].color = STAR_EARNED_COLOR;
                    _starImages[i].transform.localScale = Vector3.one;
                }
                else
                {
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

            if (target == null)
            {
                var hud = FindAnyObjectByType<UIHud>();
                if (hud != null && hud.GoldText != null)
                    target = hud.GoldText.rectTransform;
            }

            if (target == null) yield break;

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null) yield break;

            int coinCount = Mathf.Clamp(
                MIN_COIN_COUNT + (score / SCORE_PER_COIN_STEP),
                MIN_COIN_COUNT, MAX_COIN_COUNT);

            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            var effect = CoinFlyEffect.Create(canvas);
            if (effect != null)
            {
                effect.Play(screenCenter, target, coinCount,
                    onEachLand: () => EventBus.Publish(new OnCoinFlyLanded()));
            }
        }

        #endregion
    }
}
