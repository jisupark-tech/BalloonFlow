using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// InGame scene controller. Loads UIHud, PopupResult, PopupSettings, PopupGoldShop prefabs.
    /// Manages gameplay lifecycle: level load, play, result display, retry/next/home.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        private const string HUD_PREFAB      = "UI/UIHud";
        private const string RESULT_PREFAB   = "Popup/PopupResult";
        private const string SETTINGS_PREFAB = "Popup/PopupSettings";
        private const string GOLDSHOP_PREFAB = "Popup/PopupGoldShop";

        private UIHud _hud;
        private PopupResult _resultPopup;
        private PopupSettings _settingsPopup;
        private PopupGoldShop _goldShopPopup;
        private CanvasGroup _hudCanvasGroup;
        private bool _pendingResultIsWin;

        private void Awake()
        {
            EnsureEventSystem();
            EnsureSceneManagers();
            LoadUI();
            Debug.Log("[GameBootstrap] InGame scene initialized.");
        }

        private void Start()
        {
            int pendingLevel = PlayerPrefs.GetInt("BF_PendingLevelId", 0);
            if (pendingLevel <= 0)
            {
                if (LevelManager.HasInstance)
                {
                    int highest = LevelManager.Instance.GetHighestCompletedLevel();
                    pendingLevel = highest > 0 ? highest + 1 : 1;
                }
                else
                {
                    pendingLevel = 1;
                }
            }
            PlayerPrefs.DeleteKey("BF_PendingLevelId");

            if (LevelManager.HasInstance)
                LevelManager.Instance.LoadLevel(pendingLevel);
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelCompleted>(HandleLevelCompleted);
            EventBus.Subscribe<OnLevelFailed>(HandleLevelFailed);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelCompleted>(HandleLevelCompleted);
            EventBus.Unsubscribe<OnLevelFailed>(HandleLevelFailed);

            if (_resultPopup != null)
            {
                if (_resultPopup.NextButton != null) _resultPopup.NextButton.onClick.RemoveListener(OnNextClicked);
                if (_resultPopup.RetryButton != null) _resultPopup.RetryButton.onClick.RemoveListener(OnRetryClicked);
                if (_resultPopup.HomeButton != null) _resultPopup.HomeButton.onClick.RemoveListener(OnHomeClicked);
            }
        }

        // ═══════════════════════════════════════════
        // SCENE MANAGERS
        // ═══════════════════════════════════════════

        private void EnsureSceneManagers()
        {
            if (!GameManager.HasInstance)
            {
                var go = new GameObject("GameManager");
                go.AddComponent<GameManager>();
            }

            if (!ResourceManager.HasInstance)
            {
                var go = new GameObject("Mgr_Resource");
                go.AddComponent<ResourceManager>();
            }

            EnsureSingleton<InputHandler>("Mgr_Input");

            var railGO = EnsureSingleton<RailManager>("Mgr_Rail");
            if (railGO.GetComponent<RailRenderer>() == null)
                railGO.AddComponent<RailRenderer>();

            EnsureSingleton<ScoreManager>("Mgr_Score");
            EnsureSingleton<BoardStateManager>("Mgr_BoardState");
            EnsureSingleton<HolderManager>("Mgr_Holder");
            EnsureSingleton<DartManager>("Mgr_Dart");
            EnsureSingleton<BalloonController>("Mgr_Balloon");
            EnsureSingleton<PopProcessor>("Mgr_Pop");
            EnsureSingleton<HUDController>("Mgr_HUD");
            EnsureSingleton<FeedbackController>("Mgr_Feedback");
            EnsureSingleton<GimmickManager>("Mgr_Gimmick");
            EnsureSingleton<BalanceProcessor>("Mgr_Balance");
            EnsureSingleton<TutorialController>("Mgr_TutorialCtrl");
            EnsureSingleton<TutorialManager>("Mgr_TutorialMgr");
            EnsureSingleton<HolderVisualManager>("Mgr_HolderVisual");
            EnsureSingleton<LevelGenerator>("Mgr_LevelGen");

            // Wire InputHandler._gameCamera
            var inputHandler = FindAnyObjectByType<InputHandler>();
            if (inputHandler != null)
            {
                Camera mainCam = Camera.main;
                if (mainCam != null)
                {
                    var field = typeof(InputHandler).GetField("_gameCamera",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null) field.SetValue(inputHandler, mainCam);
                }
            }
        }

        private GameObject EnsureSingleton<T>(string name) where T : Component
        {
            T existing = FindAnyObjectByType<T>();
            if (existing != null) return existing.gameObject;
            var go = new GameObject(name);
            go.AddComponent<T>();
            return go;
        }

        // ═══════════════════════════════════════════
        // UI LOADING
        // ═══════════════════════════════════════════

        private void LoadUI()
        {
            Canvas canvas = FindAnyObjectByType<Canvas>();
            Transform root;

            if (canvas != null)
            {
                root = canvas.transform;
            }
            else
            {
                root = CreateCanvasGO().transform;
            }

            // UIHud
            var hudPrefab = Resources.Load<GameObject>(HUD_PREFAB);
            if (hudPrefab != null)
            {
                var go = Instantiate(hudPrefab, root);
                _hud = go.GetComponent<UIHud>();
                _hudCanvasGroup = go.GetComponent<CanvasGroup>();
                ShowCG(_hudCanvasGroup);
            }
            else
            {
                Debug.LogError($"[GameBootstrap] Prefab not found: {HUD_PREFAB}");
            }

            // PopupResult
            var resultPrefab = Resources.Load<GameObject>(RESULT_PREFAB);
            if (resultPrefab != null)
            {
                var go = Instantiate(resultPrefab, root);
                _resultPopup = go.GetComponent<PopupResult>();
                _resultPopup.Hide();
                WireResultButtons();
            }
            else
            {
                Debug.LogError($"[GameBootstrap] Prefab not found: {RESULT_PREFAB}");
            }

            // PopupSettings
            var settingsPrefab = Resources.Load<GameObject>(SETTINGS_PREFAB);
            if (settingsPrefab != null)
            {
                var go = Instantiate(settingsPrefab, root);
                _settingsPopup = go.GetComponent<PopupSettings>();
                _settingsPopup.Hide();
            }

            // PopupGoldShop
            var goldShopPrefab = Resources.Load<GameObject>(GOLDSHOP_PREFAB);
            if (goldShopPrefab != null)
            {
                var go = Instantiate(goldShopPrefab, root);
                _goldShopPopup = go.GetComponent<PopupGoldShop>();
                _goldShopPopup.Hide();
            }

            // Bind HUDController to UIHud view + popups
            if (HUDController.HasInstance && _hud != null)
            {
                var hud = HUDController.Instance;
                hud.BindView(_hud);
                hud.SetSettingsPopup(_settingsPopup);
                hud.SetGoldShopPopup(_goldShopPopup);
            }
        }

        private void WireResultButtons()
        {
            if (_resultPopup == null) return;
            if (_resultPopup.NextButton != null) _resultPopup.NextButton.onClick.AddListener(OnNextClicked);
            if (_resultPopup.RetryButton != null) _resultPopup.RetryButton.onClick.AddListener(OnRetryClicked);
            if (_resultPopup.HomeButton != null) _resultPopup.HomeButton.onClick.AddListener(OnHomeClicked);
        }

        // ═══════════════════════════════════════════
        // GAME FLOW
        // ═══════════════════════════════════════════

        private void OnNextClicked()
        {
            _pendingResultIsWin = false;
            if (_resultPopup != null) _resultPopup.Hide();
            ShowCG(_hudCanvasGroup);

            if (LevelManager.HasInstance)
            {
                int nextLevel = LevelManager.Instance.GetHighestCompletedLevel() + 1;
                LevelManager.Instance.LoadLevel(nextLevel);
            }
        }

        private void OnRetryClicked()
        {
            _pendingResultIsWin = false;
            if (_resultPopup != null) _resultPopup.Hide();
            ShowCG(_hudCanvasGroup);

            if (LevelManager.HasInstance)
                LevelManager.Instance.RetryLevel();
        }

        private void OnHomeClicked()
        {
            if (_resultPopup != null) _resultPopup.Hide();
            if (GameManager.HasInstance)
                GameManager.Instance.GoToLobby();
        }

        // ═══════════════════════════════════════════
        // EVENT HANDLERS
        // ═══════════════════════════════════════════

        private void HandleLevelCompleted(OnLevelCompleted evt)
        {
            Debug.Log($"[GameBootstrap] Level completed: level={evt.levelId}, score={evt.score}, stars={evt.starCount}");
            _pendingResultIsWin = true;
            StopCoroutine(nameof(DelayedShowResultCoroutine));
            StartCoroutine(DelayedShowResultCoroutine(true, evt.score, evt.starCount));
        }

        private void HandleLevelFailed(OnLevelFailed evt)
        {
            if (ContinueHandler.HasInstance && ContinueHandler.Instance.CanContinue())
                return;
            if (_pendingResultIsWin) return;
            StartCoroutine(DelayedShowResultCoroutine(false, 0, 0));
        }

        private IEnumerator DelayedShowResultCoroutine(bool isWin, int score, int stars)
        {
            yield return new WaitForSeconds(0.8f);
            if (_resultPopup != null)
            {
                if (isWin) _resultPopup.ShowWin(score, stars);
                else _resultPopup.ShowFail();
            }
        }

        // ═══════════════════════════════════════════
        // INFRASTRUCTURE
        // ═══════════════════════════════════════════

        private void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        private GameObject CreateCanvasGO()
        {
            var go = new GameObject("Canvas");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            go.layer = LayerMask.NameToLayer("UI");

            Camera uiCam = null;
            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var cam in cameras)
            {
                if (cam.gameObject.name == "UICamera") { uiCam = cam; break; }
            }

            if (uiCam != null)
            {
                canvas.worldCamera = uiCam;
                canvas.planeDistance = 1f;
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            return go;
        }

        private void ShowCG(CanvasGroup cg)
        {
            if (cg == null) return;
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
            cg.gameObject.SetActive(true);
        }

        private void HideCG(CanvasGroup cg)
        {
            if (cg == null) return;
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;
        }
    }
}
