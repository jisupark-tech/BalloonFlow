using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 클리어 결과 팝업.
    /// PopupCommonFrame으로 프레임/난이도/버튼 관리.
    /// NextButton(Green), HomeButton(Red) = Horizontal 레이아웃.
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

        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[별 표시 — 3개 Image]")]
        [SerializeField] private Image[] _starImages;

        [Header("[코인 연출 — Gold HUD 위치]")]
        [SerializeField] private RectTransform _goldTarget;

        public Button NextButton => _frame != null ? _frame.BtnHorizGreen : null;
        public Button RetryButton => null;
        public Button HomeButton => _frame != null ? _frame.BtnHorizRed : null;
        public RectTransform GoldTarget => _goldTarget;

        public void ShowFail()
        {
            if (PopupManager.HasInstance)
                PopupManager.Instance.ShowPopup("popup_fail01", 50);
        }

        private void Start()
        {
            if (_frame != null)
            {
                if (_frame.BtnHorizGreen != null) _frame.BtnHorizGreen.onClick.AddListener(OnNextClicked);
                if (_frame.BtnHorizRed != null) _frame.BtnHorizRed.onClick.AddListener(OnHomeClicked);
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.AddListener(OnHomeClicked);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_frame != null)
            {
                if (_frame.BtnHorizGreen != null) _frame.BtnHorizGreen.onClick.RemoveAllListeners();
                if (_frame.BtnHorizRed != null) _frame.BtnHorizRed.onClick.RemoveAllListeners();
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.RemoveAllListeners();
            }
        }

        public void SetGoldTarget(RectTransform target) { _goldTarget = target; }

        public void ShowWin(int score, int stars, DifficultyPurpose difficulty = DifficultyPurpose.Normal)
        {
            if (_frame != null)
            {
                _frame.ApplyDifficulty(difficulty);
                _frame.SetTitle("Level Clear!");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                _frame.SetHorizGreenText("Next");
                _frame.SetHorizRedText("Home");
                _frame.ShowExitButton(false);
            }

            UpdateStarDisplay(stars);
            OpenUI();
            StartCoroutine(TriggerCoinFlyDelayed(score));
        }

        #region Button Handlers

        private void OnNextClicked()
        {
            CloseUI();

            if (GameManager.IsTestPlayMode)
            {
                if (PopupManager.HasInstance) PopupManager.Instance.CloseAllPopups();
                if (GameManager.HasInstance) GameManager.Instance.LoadScene(GameManager.SCENE_MAPMAKER);
                return;
            }

            if (LifeManager.HasInstance && LifeManager.Instance.CurrentLives <= 0)
            {
                if (GameManager.HasInstance) GameManager.Instance.LoadScene(GameManager.SCENE_LOBBY);
                return;
            }

            if (LevelManager.HasInstance)
            {
                int nextId = LevelManager.Instance.GetNextLevelId();
                LevelManager.Instance.LoadLevel(nextId);
            }
        }

        private void OnHomeClicked()
        {
            CloseUI();
            if (PopupManager.HasInstance) PopupManager.Instance.CloseAllPopups();
            if (GameManager.HasInstance) GameManager.Instance.LoadScene(GameManager.SCENE_LOBBY);
        }

        #endregion

        #region Star Display

        private void UpdateStarDisplay(int earnedCount)
        {
            if (_starImages == null) return;
            int clamped = Mathf.Clamp(earnedCount, 0, MAX_STARS);
            for (int i = 0; i < _starImages.Length; i++)
            {
                if (_starImages[i] == null) continue;
                _starImages[i].gameObject.SetActive(true);
                _starImages[i].color = i < clamped ? STAR_EARNED_COLOR : STAR_UNEARNED_COLOR;
                _starImages[i].transform.localScale = i < clamped ? Vector3.one : Vector3.one * 0.85f;
            }
        }

        #endregion

        #region Coin Fly

        private IEnumerator TriggerCoinFlyDelayed(int score)
        {
            yield return new WaitForSeconds(COIN_DELAY_AFTER_POPUP);

            RectTransform target = _goldTarget;
            if (target == null)
            {
                var hud = FindAnyObjectByType<UIHud>();
                if (hud != null && hud.GoldText != null) target = hud.GoldText.rectTransform;
            }
            if (target == null) yield break;

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null) yield break;

            int coinCount = Mathf.Clamp(MIN_COIN_COUNT + (score / SCORE_PER_COIN_STEP), MIN_COIN_COUNT, MAX_COIN_COUNT);
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            var effect = CoinFlyEffect.Create(canvas);
            if (effect != null)
                effect.Play(screenCenter, target, coinCount, onEachLand: () => EventBus.Publish(new OnCoinFlyLanded()));
        }

        #endregion
    }
}
