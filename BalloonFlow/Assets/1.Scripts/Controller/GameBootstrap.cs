using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace BalloonFlow
{
    /// <summary>
    /// InGame 씬 컨트롤러.
    /// - GameManager.InitInGame() → InGame 매니저 생성 (GameManager 자식)
    /// - UIManager.OpenUI로 UIHud, PopupResult, PopupSettings, PopupGoldShop 로드
    /// - 레벨 로드, 결과 팝업, Retry/Next/Home 처리
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        private UIHud _hud;
        private PopupResult _result;
        private PopupSettings _settings;
        private PopupGoldShop _goldShop;
        private bool _pendingResultIsWin;
        private bool _isTestMode;

        void Start()
        {
            // Detect test mode from MapMaker
            #if UNITY_EDITOR
            _isTestMode = UnityEditor.EditorPrefs.GetBool("BalloonFlow_UseTestLevel", false)
                          || GameManager.IsTestPlayMode;
            #else
            _isTestMode = GameManager.IsTestPlayMode;
            #endif

            // Safety: 직접 씬 로드 테스트용
            if (!GameManager.HasInstance)
            {
                var _go = new GameObject("GameManager");
                _go.AddComponent<GameManager>();
            }

            // Lobby 매니저 확보 (Lobby 안 거쳤을 때를 위해)
            // Test mode에서도 LevelManager가 필요하므로 InitLobby 호출
            GameManager.Instance.InitLobby();

            // InGame 매니저 생성 (GameManager 자식)
            GameManager.Instance.InitInGame();

            // 카메라 설정
            if (CameraManager.HasInstance)
                CameraManager.Instance.ConfigureInGame();

            // EventSystem 확인
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var _go = new GameObject("EventSystem");
                _go.AddComponent<EventSystem>();
                _go.AddComponent<InputSystemUIInputModule>();
            }

            // 씬 캔버스 등록
            var _canvasGO = GameObject.Find("SceneCanvas");
            if (_canvasGO != null && UIManager.HasInstance)
                UIManager.Instance.SetSceneCanvas(_canvasGO.transform);

            // UI 로드
            LoadUI();

            // 레벨 로드
            LoadPendingLevel();

            Debug.Log("[GameBootstrap] InGame 초기화 완료");
        }

        void OnEnable()
        {
            EventBus.Subscribe<OnLevelCompleted>(HandleLevelCompleted);
            EventBus.Subscribe<OnLevelFailed>(HandleLevelFailed);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelCompleted>(HandleLevelCompleted);
            EventBus.Unsubscribe<OnLevelFailed>(HandleLevelFailed);

            if (_result != null)
            {
                if (_result.NextButton != null) _result.NextButton.onClick.RemoveListener(OnNextClicked);
                if (_result.RetryButton != null) _result.RetryButton.onClick.RemoveListener(OnRetryClicked);
                if (_result.HomeButton != null) _result.HomeButton.onClick.RemoveListener(OnHomeClicked);
            }
        }

        #region UI 로드

        void LoadUI()
        {
            if (!UIManager.HasInstance) return;

            // UIHud
            _hud = UIManager.Instance.OpenUI<UIHud>("UI/UIHud");

            // HUDController 바인딩
            if (HUDController.HasInstance && _hud != null)
            {
                HUDController.Instance.BindView(_hud);
            }

            // PopupResult (로드 후 숨김)
            _result = UIManager.Instance.OpenUI<PopupResult>("Popup/PopupResult");
            if (_result != null)
            {
                _result.CloseUI();
                if (_result.NextButton != null) _result.NextButton.onClick.AddListener(OnNextClicked);
                if (_result.RetryButton != null) _result.RetryButton.onClick.AddListener(OnRetryClicked);
                if (_result.HomeButton != null) _result.HomeButton.onClick.AddListener(OnHomeClicked);

                // Wire gold target for coin fly effect
                if (_result.GoldTarget == null && _hud != null && _hud.GoldText != null)
                {
                    _result.SetGoldTarget(_hud.GoldText.rectTransform);
                }
            }

            // PopupSettings (로드 후 숨김)
            _settings = UIManager.Instance.OpenUI<PopupSettings>("Popup/PopupSettings");
            if (_settings != null) _settings.CloseUI();

            // PopupGoldShop (로드 후 숨김)
            _goldShop = UIManager.Instance.OpenUI<PopupGoldShop>("Popup/PopupGoldShop");
            if (_goldShop != null) _goldShop.CloseUI();

            // HUDController에 팝업 연결
            if (HUDController.HasInstance)
            {
                HUDController.Instance.SetSettingsPopup(_settings);
                HUDController.Instance.SetGoldShopPopup(_goldShop);
            }
        }

        void LoadPendingLevel()
        {
            int _levelId = PlayerPrefs.GetInt("BF_PendingLevelId", 0);
            if (_levelId <= 0)
            {
                if (LevelManager.HasInstance)
                {
                    int _highest = LevelManager.Instance.GetHighestCompletedLevel();
                    _levelId = _highest > 0 ? _highest + 1 : 1;
                }
                else
                {
                    _levelId = 1;
                }
            }
            PlayerPrefs.DeleteKey("BF_PendingLevelId");

            if (LevelManager.HasInstance)
                LevelManager.Instance.LoadLevel(_levelId);
        }

        #endregion

        #region 게임 결과 처리

        void HandleLevelCompleted(OnLevelCompleted _evt)
        {
            _pendingResultIsWin = true;
            StartCoroutine(ShowResultDelayed(true, _evt.score, _evt.starCount));
        }

        void HandleLevelFailed(OnLevelFailed _evt)
        {
            if (ContinueHandler.HasInstance && ContinueHandler.Instance.CanContinue()) return;
            if (_pendingResultIsWin) return;
            StartCoroutine(ShowResultDelayed(false, 0, 0));
        }

        IEnumerator ShowResultDelayed(bool _isWin, int _score, int _stars)
        {
            yield return new WaitForSeconds(0.8f);
            if (_result != null)
            {
                if (_isWin) _result.ShowWin(_score, _stars);
                else _result.ShowFail();
            }
        }

        #endregion

        #region 버튼 이벤트

        void OnNextClicked()
        {
            _pendingResultIsWin = false;
            if (_result != null) _result.CloseUI();
            if (_hud != null) _hud.OpenUI();

            if (_isTestMode)
            {
                // In test mode, "Next" replays the same level (no progression)
                if (LevelManager.HasInstance)
                    LevelManager.Instance.RetryLevel();
                return;
            }

            if (LevelManager.HasInstance)
            {
                int _next = LevelManager.Instance.GetHighestCompletedLevel() + 1;
                LevelManager.Instance.LoadLevel(_next);
            }
        }

        void OnRetryClicked()
        {
            _pendingResultIsWin = false;
            if (_result != null) _result.CloseUI();
            if (_hud != null) _hud.OpenUI();

            if (LevelManager.HasInstance)
                LevelManager.Instance.RetryLevel();
        }

        void OnHomeClicked()
        {
            if (_result != null) _result.CloseUI();

            if (_isTestMode && GameManager.HasInstance)
            {
                // Return to MapMaker scene in test mode
                GameManager.Instance.GoToMapMaker();
                return;
            }

            if (GameManager.HasInstance) GameManager.Instance.GoToLobby();
        }

        #endregion
    }
}
