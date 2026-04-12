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

        private const int MIN_COIN_COUNT = 5;
        private const int MAX_COIN_COUNT = 8;
        private const int SCORE_PER_COIN_STEP = 500;

        #endregion

        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Buttons — 직접 할당]")]
        [SerializeField] private Button _btnNext;
        [SerializeField] private Button _btnHome;
        [SerializeField] private Button _btnExit;

        [Header("[Hard Level Option — Hard/SuperHard 전용]")]
        [SerializeField] private GameObject _hardLevelOption;
        [SerializeField] private TMP_Text _txtHardLevel;
        [SerializeField] private TMP_Text _txtHardLevelOutline;

        [Header("[코인 연출 — Gold HUD 위치]")]
        [SerializeField] private RectTransform _goldTarget;

        public Button NextButton => _btnNext != null ? _btnNext : (_frame != null ? _frame.BtnHorizGreen : null);
        public Button RetryButton => null;
        public Button HomeButton => _btnHome != null ? _btnHome : (_frame != null ? _frame.BtnHorizRed : null);
        public RectTransform GoldTarget => _goldTarget;

        public void ShowFail()
        {
            if (PopupManager.HasInstance)
                PopupManager.Instance.ShowPopup("popup_fail01", 50);
        }

        protected override void Awake()
        {
            base.Awake();
            // 버튼 연결은 Awake에서 (CloseUI 후에도 listener 유지)
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

        public void ShowWin(int score, DifficultyPurpose difficulty = DifficultyPurpose.Normal)
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

            UpdateHardLevelOption(difficulty);
            OpenUI();

            // 애니메이션 상태와 무관하게 즉시 클릭 가능
            if (_canvasGroup != null)
            {
                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
            }

            TriggerCoinFly(score);
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
                int currentId = LevelManager.Instance.CurrentLevelId;

                // 마지막 레벨 클리어 → 다음 레벨이 없으면 축하 팝업
                if (nextId <= currentId && UIManager.HasInstance)
                {
                    var popup = UIManager.Instance.OpenUI<PopupDescription>("Popup/PopupDescription");
                    if (popup != null)
                        popup.Show("Congratulations!", "You've cleared all levels!", "OK",
                            () => { if (GameManager.HasInstance) GameManager.Instance.LoadScene(GameManager.SCENE_LOBBY); });
                    return;
                }

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

        #region Hard Level Option

        private void UpdateHardLevelOption(DifficultyPurpose difficulty)
        {
            bool show = difficulty == DifficultyPurpose.Hard || difficulty == DifficultyPurpose.SuperHard;
            if (_hardLevelOption != null) _hardLevelOption.SetActive(show);

            if (show)
            {
                string label = difficulty == DifficultyPurpose.SuperHard ? "SuperHard" : "Hard";
                if (_txtHardLevel != null) _txtHardLevel.text = label;
                if (_txtHardLevelOutline != null) _txtHardLevelOutline.text = label;
            }
        }

        #endregion

        #region Coin Fly

        private void TriggerCoinFly(int score)
        {
            RectTransform target = _goldTarget;
            if (target == null)
            {
                var hud = FindAnyObjectByType<UIHud>();
                if (hud != null && hud.GoldText != null) target = hud.GoldText.rectTransform;
            }
            if (target == null) { Debug.LogWarning("[CoinFly] target null"); return; }

            int coinCount = Mathf.Clamp(MIN_COIN_COUNT + (score / SCORE_PER_COIN_STEP), MIN_COIN_COUNT, MAX_COIN_COUNT);
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            // target의 screen 좌표 (어떤 Canvas에 있든 동작)
            Canvas targetCanvas = target.GetComponentInParent<Canvas>();
            Camera targetCam = (targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceCamera)
                ? targetCanvas.worldCamera : null;
            Vector2 screenTarget = RectTransformUtility.WorldToScreenPoint(targetCam, target.position);

            CoinFlyEffect.Play(screenCenter, screenTarget, coinCount,
                onEachLand: () => EventBus.Publish(new OnCoinFlyLanded()));
        }

        #endregion
    }

    /// <summary>독립 코루틴 실행용 헬퍼. 완료 후 풀로 반환.</summary>
    internal class CoroutineRunner : MonoBehaviour
    {
        private static CoroutineRunner _instance;

        public static CoroutineRunner Get()
        {
            if (_instance != null && _instance.gameObject != null)
            {
                _instance.gameObject.SetActive(true);
                return _instance;
            }

            var go = new GameObject("CoroutineRunner");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<CoroutineRunner>();
            return _instance;
        }

        public void Run(System.Collections.IEnumerator routine)
        {
            StartCoroutine(routine);
        }
    }
}
