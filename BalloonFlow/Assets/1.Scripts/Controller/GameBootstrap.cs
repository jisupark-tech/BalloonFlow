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
        private PopupContinue _continuePopup;
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

            // Ensure core singletons exist (may be missing if MapMaker → InGame directly)
            EnsureCoreSingletons();

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
            EnsureSceneCanvas();

            // UI 로드
            LoadUI();

            // 레벨 로드
            LoadPendingLevel();

            Debug.Log($"[GameBootstrap] InGame 초기화 완료 (testMode={_isTestMode})");
        }

        /// <summary>
        /// Ensures GameManager, UIManager, CameraManager exist.
        /// Required when entering InGame directly from MapMaker without passing through Title/Lobby.
        /// </summary>
        void EnsureCoreSingletons()
        {
            // GameManager
            if (!GameManager.HasInstance)
            {
                var _go = new GameObject("GameManager");
                _go.AddComponent<GameManager>();
            }

            // ObjectPoolManager (required for balloon & dart spawning)
            if (!ObjectPoolManager.HasInstance)
            {
                var _go = new GameObject("ObjectPoolManager");
                _go.AddComponent<ObjectPoolManager>();
                Debug.Log("[GameBootstrap] Created ObjectPoolManager (was missing — test mode or direct scene load)");
            }

            // ResourceManager
            if (!ResourceManager.HasInstance)
            {
                var _go = new GameObject("ResourceManager");
                _go.AddComponent<ResourceManager>();
                Debug.Log("[GameBootstrap] Created ResourceManager (was missing — test mode or direct scene load)");
            }

            // UIManager (required for all UI: HUD, popups, fade transitions)
            if (!UIManager.HasInstance)
            {
                var _go = new GameObject("Mgr_UI");
                _go.AddComponent<UIManager>();
                Debug.Log("[GameBootstrap] Created UIManager (was missing — test mode or direct scene load)");
            }

            // CameraManager (wraps Main Camera for scene-specific config + shake)
            if (!CameraManager.HasInstance)
            {
                var _go = new GameObject("Mgr_Camera");
                var _cmgr = _go.AddComponent<CameraManager>();
                Camera _mainCam = Camera.main;
                if (_mainCam == null)
                {
                    // No camera in scene (e.g. MapMaker → InGame direct) — create one
                    var _camGO = new GameObject("Main Camera");
                    _camGO.tag = "MainCamera";
                    _mainCam = _camGO.AddComponent<Camera>();
                    _camGO.AddComponent<AudioListener>();
                    Debug.Log("[GameBootstrap] Created Main Camera (no camera in InGame scene)");
                }
                _cmgr.MainCamera = _mainCam;
                Debug.Log("[GameBootstrap] Created CameraManager (was missing — test mode or direct scene load)");
            }
            else
            {
                // CameraManager exists but camera reference may be lost after scene transition
                CameraManager.Instance.RefreshMainCamera();
                if (CameraManager.Instance.MainCamera == null)
                {
                    var _camGO = new GameObject("Main Camera");
                    _camGO.tag = "MainCamera";
                    var _cam = _camGO.AddComponent<Camera>();
                    _camGO.AddComponent<AudioListener>();
                    CameraManager.Instance.MainCamera = _cam;
                    Debug.Log("[GameBootstrap] Re-created Main Camera (reference lost after scene transition)");
                }
            }
        }

        /// <summary>
        /// Finds or creates a SceneCanvas and registers it with UIManager.
        /// </summary>
        void EnsureSceneCanvas()
        {
            if (!UIManager.HasInstance) return;

            var _canvasGO = GameObject.Find("SceneCanvas");

            // Create canvas if not found in scene
            if (_canvasGO == null)
            {
                _canvasGO = new GameObject("SceneCanvas");
                var _canvas = _canvasGO.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 0;
                var _scaler = _canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
                _scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                _scaler.referenceResolution = new Vector2(1080f, 1920f);
                _scaler.matchWidthOrHeight = 0.5f;
                _canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                Debug.Log("[GameBootstrap] Created SceneCanvas (was missing)");
            }

            UIManager.Instance.SetSceneCanvas(_canvasGO.transform);
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

            if (_continuePopup != null)
            {
                if (_continuePopup.ContinueButton != null)
                    _continuePopup.ContinueButton.onClick.RemoveListener(_continuePopup.OnContinueClicked);
                if (_continuePopup.DeclineButton != null)
                    _continuePopup.DeclineButton.onClick.RemoveListener(_continuePopup.OnDeclineClicked);
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

            // PopupContinue (로드 후 숨김)
            _continuePopup = UIManager.Instance.OpenUI<PopupContinue>("Popup/PopupContinue");
            if (_continuePopup != null)
            {
                _continuePopup.CloseUI();
                if (_continuePopup.ContinueButton != null)
                    _continuePopup.ContinueButton.onClick.AddListener(_continuePopup.OnContinueClicked);
                if (_continuePopup.DeclineButton != null)
                    _continuePopup.DeclineButton.onClick.AddListener(_continuePopup.OnDeclineClicked);

                // Register with PopupManager so ShowPopup("popup_continue") works
                if (PopupManager.HasInstance)
                {
                    var cg = _continuePopup.GetComponent<CanvasGroup>();
                    if (cg != null)
                        PopupManager.Instance.RegisterPopup("popup_continue", cg);
                }
            }

            // PopupSettings (로드 후 숨김)
            _settings = UIManager.Instance.OpenUI<PopupSettings>("Popup/PopupSettings");
            if (_settings != null) _settings.CloseUI();

            // PopupGoldShop (로드 후 숨김)
            _goldShop = UIManager.Instance.OpenUI<PopupGoldShop>("Popup/PopupGoldShop");
            if (_goldShop != null) _goldShop.CloseUI();

            // PopupQuit (로드 후 숨김)
            var _quitPopup = UIManager.Instance.OpenUI<PopupQuit>("Popup/PopupQuit");
            if (_quitPopup != null) _quitPopup.CloseUI();

            // BoosterTestPanel (테스트용 — 하단 좌측에 부스터 버튼 표시)
            UIManager.Instance.OpenUI<BoosterTestPanel>("UI/BoosterTestPanel");

            // HUDController에 팝업 연결
            if (HUDController.HasInstance)
            {
                HUDController.Instance.SetSettingsPopup(_settings);
                HUDController.Instance.SetGoldShopPopup(_goldShop);
                HUDController.Instance.SetQuitPopup(_quitPopup);
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
            // OnLevelFailed now only fires after continues are exhausted or declined.
            // LevelManager defers FailLevel when continues are available.
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
