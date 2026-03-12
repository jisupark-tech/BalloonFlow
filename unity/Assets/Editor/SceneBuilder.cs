#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// Comprehensive scene builder — creates GameScene with ALL managers,
    /// visible UI pages (Title/Main/Game/Result), proper wiring.
    /// 3D rendering: perspective MainCamera + orthographic UICamera + directional light + board platform.
    /// Runs once via [InitializeOnLoad]. Force re-run: BalloonFlow > Rebuild Scenes.
    /// </summary>
    [InitializeOnLoad]
    public static class SceneBuilder
    {
        private const string PREFS_KEY = "BalloonFlow_SceneBuilt_v10";
        private const int REF_WIDTH = 1080;
        private const int REF_HEIGHT = 1920;
        private const string SCENES_FOLDER = "Assets/Scenes";

        // Colors
        private static readonly Color BG_DARK = new Color(0.06f, 0.06f, 0.12f, 1f);
        private static readonly Color BG_TITLE = new Color(0.08f, 0.08f, 0.16f, 1f);
        private static readonly Color BG_MAIN = new Color(0.06f, 0.10f, 0.18f, 1f);
        private static readonly Color BG_GAME = new Color(0.04f, 0.04f, 0.08f, 1f);
        private static readonly Color BG_OVERLAY = new Color(0f, 0f, 0f, 0.85f);
        private static readonly Color COL_PLAY = new Color(0.15f, 0.75f, 0.3f, 1f);
        private static readonly Color COL_RETRY = new Color(0.9f, 0.55f, 0.1f, 1f);
        private static readonly Color COL_NEXT = new Color(0.2f, 0.6f, 0.95f, 1f);
        private static readonly Color COL_HOME = new Color(0.5f, 0.5f, 0.55f, 1f);
        private static readonly Color COL_HUD = new Color(0f, 0f, 0f, 0.5f);

        static SceneBuilder()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(PREFS_KEY, false)) return;
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                BuildAllScenes();
                EditorPrefs.SetBool(PREFS_KEY, true);
                Debug.Log("[SceneBuilder] BalloonFlow scene setup complete (v10 — UICamera Overlay, UI layer).");
            };
        }

        [MenuItem("BalloonFlow/Rebuild Scenes")]
        private static void RebuildScenes()
        {
            BuildAllScenes();
            EditorPrefs.SetBool(PREFS_KEY, true);
            Debug.Log("[SceneBuilder] Scenes rebuilt.");
        }

        [MenuItem("BalloonFlow/Reset Scene Builder")]
        private static void ResetPrefs()
        {
            EditorPrefs.DeleteKey(PREFS_KEY);
            Debug.Log("[SceneBuilder] Prefs reset. Scenes will rebuild on next domain reload.");
        }

        // ═══════════════════════════════════════════
        // MAIN BUILD
        // ═══════════════════════════════════════════

        private static void BuildAllScenes()
        {
            EnsureFolder(SCENES_FOLDER);
            string gamePath = SCENES_FOLDER + "/GameScene.unity";
            BuildGameScene(gamePath);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(gamePath, true)
            };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Force-open the scene so user is always in GameScene after rebuild
            EditorSceneManager.OpenScene(gamePath, OpenSceneMode.Single);
        }

        private static void BuildGameScene(string path)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── Cameras (3D main + 2D UI) ──
            GameObject mainCamGO = CreateMainCamera();
            GameObject uiCamGO   = CreateUICamera();

            // ── Lighting ──
            CreateLighting();

            // ── Board Platform ──
            CreateBoardPlatform();

            // ── Canvas (ScreenSpaceCamera using UICamera) ──
            var uiCamera = uiCamGO.GetComponent<Camera>();
            GameObject canvas = CreateCanvas(uiCamera);

            // ── EventSystem ──
            CreateEventSystem();

            // ── Pages ──
            var titlePage       = CreatePagePanel("TitlePage",       canvas.transform, BG_TITLE);
            var mainPage        = CreatePagePanel("MainPage",        canvas.transform, BG_MAIN);
            var gamePage        = CreatePagePanel("GamePage",        canvas.transform, BG_GAME);
            var resultPage      = CreatePagePanel("ResultPage",      canvas.transform, BG_OVERLAY);
            var levelSelectPage = CreatePagePanel("LevelSelectPage", canvas.transform, BG_MAIN);
            var settingsPage    = CreatePagePanel("SettingsPage",    canvas.transform, BG_MAIN);
            var popupOverlay    = CreatePagePanel("PopupOverlay",    canvas.transform, BG_OVERLAY);

            // Show only title at start
            SetCanvasGroupVisible(titlePage, true);
            SetCanvasGroupVisible(mainPage, false);
            SetCanvasGroupVisible(gamePage, false);
            SetCanvasGroupVisible(resultPage, false);
            SetCanvasGroupVisible(levelSelectPage, false);
            SetCanvasGroupVisible(settingsPage, false);
            SetCanvasGroupVisible(popupOverlay, false);

            // ── Title Page content ──
            CreateText("LogoText", titlePage.transform, "BalloonFlow",
                       72, TextAnchor.MiddleCenter, Color.white, new Vector2(0, 120), new Vector2(900, 140));
            CreateText("SubtitleText", titlePage.transform, "Pop & Flow Puzzle",
                       28, TextAnchor.MiddleCenter, new Color(0.7f, 0.7f, 0.8f), new Vector2(0, 20), new Vector2(600, 50));
            CreateText("TapToStart", titlePage.transform, "Tap to Start",
                       24, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.6f), new Vector2(0, -250), new Vector2(400, 50));

            // ── Main Page content ──
            CreateText("MainTitle", mainPage.transform, "BalloonFlow",
                       56, TextAnchor.MiddleCenter, Color.white, new Vector2(0, 400), new Vector2(800, 100));

            // Currency bar at top
            var currBar = CreatePanel("CurrencyBar", mainPage.transform, new Color(0, 0, 0, 0.4f));
            SetRectTopStretch(currBar.GetComponent<RectTransform>(), 80);
            var coinText = CreateText("CoinText", currBar.transform, "Coins: 2000",
                           24, TextAnchor.MiddleLeft, Color.yellow, new Vector2(30, 0), new Vector2(200, 50));
            var gemText = CreateText("GemText", currBar.transform, "Gems: 50",
                          24, TextAnchor.MiddleRight, new Color(0.5f, 0.8f, 1f), new Vector2(-30, 0), new Vector2(200, 50));
            var gemRT = gemText.GetComponent<RectTransform>();
            gemRT.anchorMin = new Vector2(1, 0.5f);
            gemRT.anchorMax = new Vector2(1, 0.5f);
            gemRT.pivot = new Vector2(1, 0.5f);

            var playBtn = CreateButton("PlayButton", mainPage.transform, "PLAY", COL_PLAY, 36,
                                       new Vector2(0, -50), new Vector2(360, 110));

            // ── Game Page content ──
            // HUD Top bar
            var hudTop = CreatePanel("HUD_Top", gamePage.transform, COL_HUD);
            SetRectTopStretch(hudTop.GetComponent<RectTransform>(), 120);

            var scoreText = CreateText("ScoreText", hudTop.transform, "0",
                            36, TextAnchor.MiddleCenter, Color.white, new Vector2(0, -10), new Vector2(300, 50));
            var levelText = CreateText("LevelText", hudTop.transform, "Level 1",
                            22, TextAnchor.MiddleCenter, new Color(0.8f, 0.8f, 0.9f), new Vector2(0, -55), new Vector2(200, 35));
            var remainText = CreateText("RemainingText", hudTop.transform, "Balloons: --",
                             20, TextAnchor.MiddleLeft, new Color(0.9f, 0.7f, 0.3f), new Vector2(30, -10), new Vector2(200, 35));
            var remRT = remainText.GetComponent<RectTransform>();
            remRT.anchorMin = new Vector2(0, 0.5f);
            remRT.anchorMax = new Vector2(0, 0.5f);
            remRT.pivot = new Vector2(0, 0.5f);

            var holderText = CreateText("HolderText", hudTop.transform, "Holders: 0/5",
                             20, TextAnchor.MiddleRight, new Color(0.3f, 0.9f, 0.7f), new Vector2(-30, -10), new Vector2(200, 35));
            var htRT = holderText.GetComponent<RectTransform>();
            htRT.anchorMin = new Vector2(1, 0.5f);
            htRT.anchorMax = new Vector2(1, 0.5f);
            htRT.pivot = new Vector2(1, 0.5f);

            // BoardArea and HolderArea removed — 3D scene doesn't need 2D overlay panels.
            // GamePage only contains HUD (top bar) for score/level/balloon info.

            // ── Result Page content ──
            var resultPanel = CreatePanel("ResultPanel", resultPage.transform, new Color(0.12f, 0.12f, 0.2f));
            SetRectCenter(resultPanel.GetComponent<RectTransform>(), Vector2.zero, new Vector2(700, 700));

            var resultTitle = CreateText("ResultTitle", resultPanel.transform, "Level Clear!",
                              48, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.1f), new Vector2(0, 240), new Vector2(600, 80));
            var resultScore = CreateText("ResultScore", resultPanel.transform, "Score: 0\n★ 0 / 3",
                              32, TextAnchor.MiddleCenter, Color.white, new Vector2(0, 100), new Vector2(500, 120));

            var nextBtn  = CreateButton("NextButton",  resultPanel.transform, "NEXT",  COL_NEXT,  28,
                                        new Vector2(0, -60),  new Vector2(250, 70));
            var retryBtn = CreateButton("RetryButton", resultPanel.transform, "RETRY", COL_RETRY, 28,
                                        new Vector2(0, -150), new Vector2(250, 70));
            var homeBtn  = CreateButton("HomeButton",  resultPanel.transform, "HOME",  COL_HOME,  24,
                                        new Vector2(0, -240), new Vector2(200, 55));

            // ── Popup Container ──
            CreatePanel("PopupContainer", popupOverlay.transform, new Color(0.15f, 0.15f, 0.25f));

            // ═══════════════════════════════════════════
            // CREATE ALL MANAGER SINGLETONS
            // ═══════════════════════════════════════════

            // Core
            CreateManagerGO<ObjectPoolManager>("Mgr_ObjectPool");
            var goInput  = CreateManagerGO<InputHandler>("Mgr_Input");
            var goUI     = CreateManagerGO<UIManager>("Mgr_UI");
            var goPopup  = CreateManagerGO<PopupManager>("Mgr_Popup");

            // Game
            var goRail = CreateManagerGO<RailManager>("Mgr_Rail");
            // Add RailRenderer — no LineRenderer setup (RailRenderer uses 3D mesh track)
            goRail.AddComponent<RailRenderer>();

            CreateManagerGO<ScoreManager>("Mgr_Score");
            var goLevel = CreateManagerGO<LevelManager>("Mgr_Level");
            CreateManagerGO<BoardStateManager>("Mgr_BoardState");

            // InGame
            CreateManagerGO<HolderManager>("Mgr_Holder");
            CreateManagerGO<DartManager>("Mgr_Dart");
            CreateManagerGO<BalloonController>("Mgr_Balloon");
            CreateManagerGO<PopProcessor>("Mgr_Pop");

            // UI Controllers
            var goHUD  = CreateManagerGO<HUDController>("Mgr_HUD");
            var goPage = CreateManagerGO<PageController>("Mgr_Page");
            CreateManagerGO<FeedbackController>("Mgr_Feedback");

            // Economy
            CreateManagerGO<CurrencyManager>("Mgr_Currency");
            CreateManagerGO<GemManager>("Mgr_Gem");
            CreateManagerGO<LifeManager>("Mgr_Life");
            CreateManagerGO<DailyRewardManager>("Mgr_DailyReward");
            CreateManagerGO<BoosterManager>("Mgr_Booster");
            CreateManagerGO<ContinueHandler>("Mgr_Continue");

            // Shop/Monetization
            CreateManagerGO<ShopManager>("Mgr_Shop");
            CreateManagerGO<AdManager>("Mgr_Ad");
            CreateManagerGO<OfferManager>("Mgr_Offer");
            CreateManagerGO<IAPManager>("Mgr_IAP");

            // Visual / Level Generation
            CreateManagerGO<HolderVisualManager>("Mgr_HolderVisual");
            CreateManagerGO<LevelGenerator>("Mgr_LevelGen");

            // Content
            CreateManagerGO<PackageManager>("Mgr_Package");
            CreateManagerGO<GimmickManager>("Mgr_Gimmick");
            CreateManagerGO<BalanceProcessor>("Mgr_Balance");
            CreateManagerGO<TutorialController>("Mgr_TutorialCtrl");
            CreateManagerGO<TutorialManager>("Mgr_TutorialMgr");

            // LevelDataProvider on LevelManager GO
            goLevel.AddComponent<LevelDataProvider>();

            // ── GameBootstrap ──
            var bootstrapGO = new GameObject("GameBootstrap");
            var bootstrap = bootstrapGO.AddComponent<GameBootstrap>();

            // ═══════════════════════════════════════════
            // WIRE SERIALIZED FIELDS
            // ═══════════════════════════════════════════

            // GameBootstrap
            WireField(bootstrap, "_titlePage", titlePage.GetComponent<CanvasGroup>());
            WireField(bootstrap, "_mainPage", mainPage.GetComponent<CanvasGroup>());
            WireField(bootstrap, "_gamePage", gamePage.GetComponent<CanvasGroup>());
            WireField(bootstrap, "_resultPage", resultPage.GetComponent<CanvasGroup>());
            WireField(bootstrap, "_playButton", playBtn.GetComponent<Button>());
            WireField(bootstrap, "_resultTitleText", resultTitle.GetComponent<Text>());
            WireField(bootstrap, "_resultScoreText", resultScore.GetComponent<Text>());
            WireField(bootstrap, "_nextButton", nextBtn.GetComponent<Button>());
            WireField(bootstrap, "_retryButton", retryBtn.GetComponent<Button>());
            WireField(bootstrap, "_homeButton", homeBtn.GetComponent<Button>());
            WireField(bootstrap, "_coinDisplayText", coinText.GetComponent<Text>());
            WireField(bootstrap, "_gemDisplayText", gemText.GetComponent<Text>());

            // InputHandler — wire to 3D perspective main camera (not UI camera)
            WireField(goInput.GetComponent<InputHandler>(), "_gameCamera", mainCamGO.GetComponent<Camera>());

            // PopupManager
            WireField(goPopup.GetComponent<PopupManager>(), "_overlayBackground",
                      popupOverlay.GetComponent<CanvasGroup>());

            // HUDController
            var hud = goHUD.GetComponent<HUDController>();
            WireField(hud, "_scoreText", scoreText.GetComponent<Text>());
            WireField(hud, "_remainingText", remainText.GetComponent<Text>());
            WireField(hud, "_holderCountText", holderText.GetComponent<Text>());
            WireField(hud, "_levelText", levelText.GetComponent<Text>());

            // PageController
            var pageCtrl = goPage.GetComponent<PageController>();
            WireField(pageCtrl, "_titlePage", titlePage);
            WireField(pageCtrl, "_mainPage", mainPage);
            WireField(pageCtrl, "_levelSelectPage", levelSelectPage);
            WireField(pageCtrl, "_gamePage", gamePage);
            WireField(pageCtrl, "_resultPage", resultPage);
            WireField(pageCtrl, "_settingsPage", settingsPage);

            // LevelManager
            WireField(goLevel.GetComponent<LevelManager>(), "_levelDataProvider",
                      goLevel.GetComponent<LevelDataProvider>());

            EditorSceneManager.SaveScene(scene, path);
        }

        // ═══════════════════════════════════════════
        // CAMERA & LIGHTING
        // ═══════════════════════════════════════════

        /// <summary>
        /// 3D perspective camera looking down at the balloon board at ~55 degrees.
        /// No AudioListener here — AudioListener lives on UICamera.
        /// </summary>
        private static GameObject CreateMainCamera()
        {
            var go = new GameObject("Main Camera");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.orthographic = false;
            cam.fieldOfView = 45f;
            cam.depth = 0;
            go.tag = "MainCamera";

            // Position above and behind the board, rotated down ~55 degrees
            go.transform.position = new Vector3(0f, 12f, -8f);
            go.transform.eulerAngles = new Vector3(55f, 0f, 0f);

            return go;
        }

        /// <summary>
        /// Orthographic UI camera — Overlay type (URP) or Depth-clear fallback (Built-in).
        /// Culling mask = UI layer only. Hosts the AudioListener.
        /// </summary>
        private static GameObject CreateUICamera()
        {
            var go = new GameObject("UICamera");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Depth;
            cam.orthographic = true;
            cam.orthographicSize = 10f;
            cam.depth = 10;
            // Culling mask: UI layer only (layer 5 = "UI" in Unity's default layers)
            cam.cullingMask = 1 << LayerMask.NameToLayer("UI");

            // Try to set URP Overlay render type via reflection (non-breaking if URP not installed)
            TrySetURPOverlay(go);

            go.AddComponent<AudioListener>();
            return go;
        }

        /// <summary>
        /// Attempts to set camera to URP Overlay type and add it to Main Camera stack.
        /// Uses reflection so it compiles even without URP package.
        /// </summary>
        private static void TrySetURPOverlay(GameObject uiCamGO)
        {
            var urpCamType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (urpCamType == null) return;

            // Set UICamera render type to Overlay (enum value 1)
            var uiCamData = uiCamGO.GetComponent(urpCamType);
            if (uiCamData == null) uiCamData = uiCamGO.AddComponent(urpCamType);
            var renderTypeProp = urpCamType.GetProperty("renderType");
            if (renderTypeProp != null)
            {
                // CameraRenderType.Overlay = 1
                renderTypeProp.SetValue(uiCamData, 1);
            }

            // Add UICamera to Main Camera's stack
            var mainCamGO = GameObject.FindWithTag("MainCamera");
            if (mainCamGO != null)
            {
                var mainCamData = mainCamGO.GetComponent(urpCamType);
                if (mainCamData == null) mainCamData = mainCamGO.AddComponent(urpCamType);
                var stackProp = urpCamType.GetProperty("cameraStack");
                if (stackProp != null)
                {
                    var stack = stackProp.GetValue(mainCamData) as System.Collections.IList;
                    var uiCam = uiCamGO.GetComponent<Camera>();
                    if (stack != null && uiCam != null) stack.Add(uiCam);
                }
            }

            Debug.Log("[SceneBuilder] UICamera set to URP Overlay mode.");
        }

        /// <summary>
        /// Directional light with a warm tone and soft shadows for the 3D board.
        /// </summary>
        private static void CreateLighting()
        {
            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1.0f, 0.96f, 0.88f); // Warm white
            light.intensity = 1.0f;
            light.shadows = LightShadows.Soft;
            go.transform.eulerAngles = new Vector3(50f, -30f, 0f);
        }

        /// <summary>
        /// Flat cube platform that the balloon grid rests on.
        /// </summary>
        private static void CreateBoardPlatform()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "BoardPlatform";
            go.transform.localScale = new Vector3(12f, 0.2f, 12f);
            go.transform.position = new Vector3(0f, -0.1f, 2f);

            var mr = go.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.22f, 0.22f, 0.25f); // Dark gray
            mr.material = mat;
        }

        // ═══════════════════════════════════════════
        // CANVAS & UI
        // ═══════════════════════════════════════════

        /// <summary>
        /// ScreenSpaceCamera canvas rendered by the UICamera overlay.
        /// Canvas and all children are set to UI layer so UICamera (cullingMask=UI) renders them.
        /// </summary>
        private static GameObject CreateCanvas(Camera uiCamera)
        {
            var go = new GameObject("Canvas");
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = uiCamera;
            c.planeDistance = 1f;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(REF_WIDTH, REF_HEIGHT);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            // Set Canvas to UI layer (matches UICamera culling mask)
            go.layer = LayerMask.NameToLayer("UI");
            return go;
        }

        private static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        // ═══════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════

        private static GameObject CreateManagerGO<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.AddComponent<T>();
            return go;
        }

        private static void WireField(Object target, string fieldName, Object value)
        {
            if (target == null || value == null) return;
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop != null)
            {
                prop.objectReferenceValue = value;
                so.ApplyModifiedProperties();
            }
        }

        private static GameObject CreatePagePanel(string name, Transform parent, Color bgColor)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            SetRectStretch(rt);
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            img.raycastTarget = true;
            go.AddComponent<CanvasGroup>();
            return go;
        }

        private static GameObject CreatePanel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            SetRectStretch(go.GetComponent<RectTransform>());
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return go;
        }

        private static GameObject CreateText(string name, Transform parent, string content,
            int fontSize, TextAnchor alignment, Color color, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            SetRectCenter(rt, position, size);
            var text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null) text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return go;
        }

        private static GameObject CreateButton(string name, Transform parent, string label,
            Color bgColor, int fontSize, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            SetRectCenter(rt, position, size);
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            go.AddComponent<Button>();

            var textGO = new GameObject("Label");
            textGO.layer = LayerMask.NameToLayer("UI");
            textGO.transform.SetParent(go.transform, false);
            SetRectStretch(textGO.AddComponent<RectTransform>());
            var text = textGO.AddComponent<Text>();
            text.text = label;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null) text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontStyle = FontStyle.Bold;
            text.raycastTarget = false;
            return go;
        }

        private static void SetRectStretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void SetRectCenter(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void SetRectTopStretch(RectTransform rt, float height)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(0, -height);
            rt.offsetMax = Vector2.zero;
        }

        private static void SetRectBottomStretch(RectTransform rt, float height)
        {
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(0, height);
        }

        private static void SetCanvasGroupVisible(GameObject go, bool visible)
        {
            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null) return;
            cg.alpha = visible ? 1f : 0f;
            cg.interactable = visible;
            cg.blocksRaycasts = visible;
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string[] parts = path.Split('/');
                string current = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                    {
                        AssetDatabase.CreateFolder(current, parts[i]);
                    }
                    current = next;
                }
            }
        }
    }
}
#endif
