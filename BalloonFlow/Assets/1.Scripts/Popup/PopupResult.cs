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

        private const int MIN_COIN_COUNT = 20;
        private const int MAX_COIN_COUNT = 25;
        private const int SCORE_PER_COIN_STEP = 500;

        #endregion

        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Buttons — 직접 할당]")]
        [SerializeField] private Button _btnNext;
        [SerializeField] private Button _btnHome;
        [SerializeField] private Button _btnExit;

        [Header("[난이도별 비주얼]")]
        [SerializeField] private Image _imageLight;
        [SerializeField] private Image _imageStage;
        [SerializeField] private Sprite _sprStageNormal;
        [SerializeField] private Sprite _sprStageHard;
        [SerializeField] private Sprite _sprStageSuperHard;

        [Header("[Hard Level Option — Hard/SuperHard 전용]")]
        [SerializeField] private GameObject _hardLevelOption;
        [SerializeField] private Image _iconSkull;
        [SerializeField] private Sprite _sprSkullHard;
        [SerializeField] private Sprite _sprSkullSuperHard;
        [SerializeField] private TMP_Text _txtHardLevel;
        [SerializeField] private TMP_Text _txtHardLevelOutline;
        [SerializeField] private Material _matHardLevelOutlineHard;
        [SerializeField] private Material _matHardLevelOutlineSuperHard;

        [Header("[난이도별 곱하기 라벨 — 표시용(내부 수치와 무관)]")]
        [SerializeField] private GameObject _multiplierLabel;
        [SerializeField] private TMP_Text _txtMultiplier;
        [SerializeField] private TMP_Text _txtMultiplierOutline;

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
            // 직접 할당 버튼 우선, 없으면 frame 버튼 fallback (CloseUI 후에도 listener 유지)
            if (_btnNext != null) _btnNext.onClick.AddListener(OnNextClicked);
            else if (_frame != null && _frame.BtnHorizGreen != null)
                _frame.BtnHorizGreen.onClick.AddListener(OnNextClicked);

            if (_btnHome != null) _btnHome.onClick.AddListener(OnHomeClicked);
            else if (_frame != null && _frame.BtnHorizRed != null)
                _frame.BtnHorizRed.onClick.AddListener(OnHomeClicked);

            // ExitButton: 직접 할당 + frame 둘 다 와이어 (둘 중 보이는 쪽이 동작)
            if (_btnExit != null) _btnExit.onClick.AddListener(OnHomeClicked);
            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.AddListener(OnHomeClicked);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_btnNext != null) _btnNext.onClick.RemoveAllListeners();
            if (_btnHome != null) _btnHome.onClick.RemoveAllListeners();
            if (_btnExit != null) _btnExit.onClick.RemoveAllListeners();
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
                // ExitButton은 직접 할당된 게 있으면 그걸 보이도록, 없으면 frame 것 표시
                _frame.ShowExitButton(_btnExit == null);
            }

            // 직접 할당된 ExitButton 강제 활성화 (다른 ShowXxx 호출에서 꺼졌을 가능성 대비)
            if (_btnExit != null)
            {
                _btnExit.gameObject.SetActive(true);
                _btnExit.interactable = true;
            }

            // Next/Home 버튼 강제 활성화 (interactable 미설정/prefab 기본값 false 방어)
            if (_btnNext != null)
            {
                _btnNext.gameObject.SetActive(true);
                _btnNext.interactable = true;
            }
            else if (_frame != null && _frame.BtnHorizGreen != null)
            {
                _frame.BtnHorizGreen.gameObject.SetActive(true);
                _frame.BtnHorizGreen.interactable = true;
            }
            if (_btnHome != null)
            {
                _btnHome.gameObject.SetActive(true);
                _btnHome.interactable = true;
            }
            else if (_frame != null && _frame.BtnHorizRed != null)
            {
                _frame.BtnHorizRed.gameObject.SetActive(true);
                _frame.BtnHorizRed.interactable = true;
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

        // 난이도별 ImageLight 색상
        private static readonly Color LIGHT_NORMAL    = new Color(0x00 / 255f, 0x9B / 255f, 0xFF / 255f); // #009BFF
        private static readonly Color LIGHT_HARD      = new Color(0xAF / 255f, 0x20 / 255f, 0xE5 / 255f); // #AF20E5
        private static readonly Color LIGHT_SUPERHARD  = new Color(0xFF / 255f, 0x59 / 255f, 0x00 / 255f); // #FF5900

        private void UpdateHardLevelOption(DifficultyPurpose difficulty)
        {
            // ImageLight 색상
            if (_imageLight != null)
            {
                _imageLight.color = difficulty switch
                {
                    DifficultyPurpose.Hard      => LIGHT_HARD,
                    DifficultyPurpose.SuperHard  => LIGHT_SUPERHARD,
                    _                            => LIGHT_NORMAL
                };
            }

            // ImageStage 스프라이트
            if (_imageStage != null)
            {
                Sprite stageSpr = difficulty switch
                {
                    DifficultyPurpose.Hard      => _sprStageHard ?? _sprStageNormal,
                    DifficultyPurpose.SuperHard  => _sprStageSuperHard ?? _sprStageNormal,
                    _                            => _sprStageNormal
                };
                if (stageSpr != null) _imageStage.sprite = stageSpr;
            }

            // HardLevelOption 표시: Normal=숨김, Hard/SuperHard=노출
            bool show = difficulty == DifficultyPurpose.Hard || difficulty == DifficultyPurpose.SuperHard;
            if (_hardLevelOption != null) _hardLevelOption.SetActive(show);

            // 방어적 처리: _hardLevelOption 루트가 프리팹에 미할당이거나 부분 영역만 가리키는 경우를 대비,
            // 하위 구성 요소(IconSkull, TxtHardLevel, Outline)도 개별적으로 가시 상태를 제어.
            if (_iconSkull != null) _iconSkull.gameObject.SetActive(show);
            if (_txtHardLevel != null) _txtHardLevel.gameObject.SetActive(show);
            if (_txtHardLevelOutline != null) _txtHardLevelOutline.gameObject.SetActive(show);

            // 곱하기 라벨: Normal 없음 / Hard x3 / SuperHard x5
            string multiplier = difficulty switch
            {
                DifficultyPurpose.SuperHard => "x5",
                DifficultyPurpose.Hard      => "x3",
                _                            => ""
            };
            bool showMultiplier = !string.IsNullOrEmpty(multiplier);
            if (_multiplierLabel != null) _multiplierLabel.SetActive(showMultiplier);
            if (_txtMultiplier != null)
            {
                _txtMultiplier.gameObject.SetActive(showMultiplier);
                _txtMultiplier.text = multiplier;
            }
            if (_txtMultiplierOutline != null)
            {
                _txtMultiplierOutline.gameObject.SetActive(showMultiplier);
                _txtMultiplierOutline.text = multiplier;
            }

            if (show)
            {
                string label = difficulty == DifficultyPurpose.SuperHard ? "SuperHard" : "Hard";
                if (_txtHardLevel != null) _txtHardLevel.text = label;
                if (_txtHardLevelOutline != null) _txtHardLevelOutline.text = label;

                // IconSkull 스프라이트
                if (_iconSkull != null)
                {
                    Sprite skullSpr = difficulty == DifficultyPurpose.SuperHard ? _sprSkullSuperHard : _sprSkullHard;
                    if (skullSpr != null) _iconSkull.sprite = skullSpr;
                }

                // TxtHardLevelOutline 머티리얼
                if (_txtHardLevelOutline != null)
                {
                    Material mat = difficulty == DifficultyPurpose.SuperHard
                        ? _matHardLevelOutlineSuperHard
                        : _matHardLevelOutlineHard;
                    if (mat != null) _txtHardLevelOutline.fontMaterial = mat;
                }
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

        /// <summary>이미 생성된 인스턴스가 있으면 반환, 없으면 null. StopAll 등 생성 없이 참조만 할 때 사용.</summary>
        public static CoroutineRunner GetIfExists()
        {
            return _instance != null && _instance.gameObject != null ? _instance : null;
        }

        public void Run(System.Collections.IEnumerator routine)
        {
            StartCoroutine(routine);
        }
    }
}
