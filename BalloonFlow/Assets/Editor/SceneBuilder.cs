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
    /// Three-scene builder: Title, Lobby, InGame.
    /// Title: splash/logo → auto-transition to Lobby.
    /// Lobby: main menu, settings, shop access.
    /// InGame: 3D board + HUD + all gameplay managers.
    /// Persistent managers (GameManager, UIManager, etc.) are created at runtime via GameManager.
    /// Scene-specific managers are placed only in InGame scene.
    /// Runs once via [InitializeOnLoad]. Force re-run: BalloonFlow > Rebuild Scenes.
    /// </summary>
    [InitializeOnLoad]
    public static class SceneBuilder
    {
        private const string PREFS_KEY = "BalloonFlow_SceneBuilt_v11_3Scene";
        private const int REF_WIDTH  = 1080;
        private const int REF_HEIGHT = 1920;
        private const string SCENES_FOLDER = "Assets/Scenes";

        // Colors
        private static readonly Color BG_TITLE   = new Color(0.08f, 0.08f, 0.16f, 1f);
        private static readonly Color BG_LOBBY   = new Color(0.06f, 0.10f, 0.18f, 1f);
        private static readonly Color BG_GAME    = new Color(0.04f, 0.04f, 0.08f, 1f);
        private static readonly Color BG_OVERLAY = new Color(0f, 0f, 0f, 0.85f);
        private static readonly Color COL_PLAY   = new Color(0.15f, 0.75f, 0.3f, 1f);
        private static readonly Color COL_HUD    = new Color(0f, 0f, 0f, 0.5f);
        private static readonly Color COL_COIN   = new Color(0.85f, 0.75f, 0.1f, 1f);
        private static readonly Color COL_SETTINGS = new Color(0.4f, 0.4f, 0.5f, 1f);

        static SceneBuilder()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(PREFS_KEY, false)) return;
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                BuildAllScenes();
                EditorPrefs.SetBool(PREFS_KEY, true);
                Debug.Log("[SceneBuilder] 3-scene setup complete (v11 — Title/Lobby/InGame).");
            };
        }

        [MenuItem("BalloonFlow/Rebuild Scenes")]
        private static void RebuildScenes()
        {
            BuildAllScenes();
            EditorPrefs.SetBool(PREFS_KEY, true);
            Debug.Log("[SceneBuilder] Scenes rebuilt (3-scene).");
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

            string titlePath  = SCENES_FOLDER + "/Title.unity";
            string lobbyPath  = SCENES_FOLDER + "/Lobby.unity";
            string inGamePath = SCENES_FOLDER + "/InGame.unity";

            BuildTitleScene(titlePath);
            BuildLobbyScene(lobbyPath);
            BuildInGameScene(inGamePath);

            // Register all 3 scenes in Build Settings
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(titlePath, true),
                new EditorBuildSettingsScene(lobbyPath, true),
                new EditorBuildSettingsScene(inGamePath, true)
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Open Title scene
            EditorSceneManager.OpenScene(titlePath, OpenSceneMode.Single);
        }

        // ═══════════════════════════════════════════
        // TITLE SCENE
        // ═══════════════════════════════════════════

        private static void BuildTitleScene(string path)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Camera
            var camGO = new GameObject("Main Camera");
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BG_TITLE;
            cam.orthographic = true;
            cam.depth = 0;
            camGO.tag = "MainCamera";
            camGO.AddComponent<AudioListener>();

            // Canvas
            var canvas = CreateCanvas(cam);

            // EventSystem
            CreateEventSystem();

            // TitleController
            var titleCtrlGO = new GameObject("TitleController");
            titleCtrlGO.AddComponent<TitleController>();

            EditorSceneManager.SaveScene(scene, path);
        }

        // ═══════════════════════════════════════════
        // LOBBY SCENE
        // ═══════════════════════════════════════════

        private static void BuildLobbyScene(string path)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Camera
            var camGO = new GameObject("Main Camera");
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BG_LOBBY;
            cam.orthographic = true;
            cam.depth = 0;
            camGO.tag = "MainCamera";
            camGO.AddComponent<AudioListener>();

            // Canvas
            var canvas = CreateCanvas(cam);

            // EventSystem
            CreateEventSystem();

            // LobbyController
            var lobbyCtrlGO = new GameObject("LobbyController");
            lobbyCtrlGO.AddComponent<LobbyController>();

            EditorSceneManager.SaveScene(scene, path);
        }

        // ═══════════════════════════════════════════
        // INGAME SCENE
        // ═══════════════════════════════════════════

        private static void BuildInGameScene(string path)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── 3D Camera ──
            var mainCamGO = CreateMainCamera();

            // ── UI Camera ──
            var uiCamGO = CreateUICamera();

            // ── Lighting ──
            CreateLighting();

            // ── Board Platform ──
            CreateBoardPlatform();

            // ── EventSystem ──
            // (Canvas is NOT created here — GameBootstrap creates it at runtime
            //  to avoid duplicate with UIManager's persistent canvases)
            CreateEventSystem();

            // ── Scene-Specific Managers ──
            // (These are SceneSingleton — destroyed on scene unload)
            var goInput = CreateManagerGO<InputHandler>("Mgr_Input");
            var goRail = CreateManagerGO<RailManager>("Mgr_Rail");
            goRail.AddComponent<RailRenderer>();
            CreateManagerGO<ScoreManager>("Mgr_Score");
            CreateManagerGO<BoardStateManager>("Mgr_BoardState");
            CreateManagerGO<HolderManager>("Mgr_Holder");
            CreateManagerGO<DartManager>("Mgr_Dart");
            CreateManagerGO<BalloonController>("Mgr_Balloon");
            CreateManagerGO<PopProcessor>("Mgr_Pop");
            var goHUD = CreateManagerGO<HUDController>("Mgr_HUD");
            CreateManagerGO<FeedbackController>("Mgr_Feedback");
            CreateManagerGO<GimmickManager>("Mgr_Gimmick");
            CreateManagerGO<BalanceProcessor>("Mgr_Balance");
            CreateManagerGO<TutorialController>("Mgr_TutorialCtrl");
            CreateManagerGO<TutorialManager>("Mgr_TutorialMgr");
            CreateManagerGO<HolderVisualManager>("Mgr_HolderVisual");
            CreateManagerGO<LevelGenerator>("Mgr_LevelGen");

            // ── GameBootstrap ──
            var bootstrapGO = new GameObject("GameBootstrap");
            bootstrapGO.AddComponent<GameBootstrap>();

            // ── Wire InputHandler._gameCamera ──
            WireField(goInput.GetComponent<InputHandler>(), "_gameCamera", mainCamGO.GetComponent<Camera>());

            EditorSceneManager.SaveScene(scene, path);
        }

        // ═══════════════════════════════════════════
        // CAMERA & LIGHTING
        // ═══════════════════════════════════════════

        private static GameObject CreateMainCamera()
        {
            var go = new GameObject("Main Camera");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.orthographic = false;
            cam.fieldOfView = 45f;
            cam.depth = 0;
            go.tag = "MainCamera";
            go.transform.position = new Vector3(0f, 12f, -8f);
            go.transform.eulerAngles = new Vector3(55f, 0f, 0f);
            return go;
        }

        private static GameObject CreateUICamera()
        {
            var go = new GameObject("UICamera");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Depth;
            cam.orthographic = true;
            cam.orthographicSize = 10f;
            cam.depth = 10;
            cam.cullingMask = 1 << LayerMask.NameToLayer("UI");

            TrySetURPOverlay(go);
            go.AddComponent<AudioListener>();
            return go;
        }

        private static void TrySetURPOverlay(GameObject uiCamGO)
        {
            var urpCamType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (urpCamType == null) return;

            var uiCamData = uiCamGO.GetComponent(urpCamType);
            if (uiCamData == null) uiCamData = uiCamGO.AddComponent(urpCamType);
            var renderTypeProp = urpCamType.GetProperty("renderType");
            if (renderTypeProp != null) renderTypeProp.SetValue(uiCamData, 1);

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
        }

        private static void CreateLighting()
        {
            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1.0f, 0.96f, 0.88f);
            light.intensity = 1.0f;
            light.shadows = LightShadows.Soft;
            go.transform.eulerAngles = new Vector3(50f, -30f, 0f);
        }

        private static void CreateBoardPlatform()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "BoardPlatform";
            go.transform.localScale = new Vector3(12f, 0.2f, 12f);
            go.transform.position = new Vector3(0f, -0.1f, 2f);
            var mr = go.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.22f, 0.22f, 0.25f);
            mr.material = mat;
        }

        // ═══════════════════════════════════════════
        // CANVAS & UI
        // ═══════════════════════════════════════════

        private static GameObject CreateCanvas(Camera cam)
        {
            var go = new GameObject("Canvas");
            var c = go.AddComponent<Canvas>();

            if (cam != null)
            {
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = 1f;
            }
            else
            {
                c.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(REF_WIDTH, REF_HEIGHT);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
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
