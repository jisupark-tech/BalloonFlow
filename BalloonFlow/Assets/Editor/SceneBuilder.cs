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
    /// 3씬 빌더: Title, Lobby, InGame.
    /// 기존 Scenes 폴더의 씬을 열어서 필요한 오브젝트만 추가/수정.
    /// 새로 만들지 않고, 이미 있는 것은 건드리지 않음.
    ///
    /// Title 씬:
    ///   - GameManager, CameraManager(+MainCamera+UICamera), UIManager
    ///   - ResourceManager, ObjectPoolManager, Canvas, EventSystem, TitleController
    ///
    /// Lobby 씬:
    ///   - Canvas, EventSystem, LobbyController
    ///
    /// InGame 씬:
    ///   - SceneCanvas, EventSystem, GameBootstrap
    ///   - Directional Light, BoardPlatform
    /// </summary>
    [InitializeOnLoad]
    public static class SceneBuilder
    {
        private const string PREFS_KEY = "BalloonFlow_SceneBuilt_v15";
        private const int REF_WIDTH  = 1080;
        private const int REF_HEIGHT = 1920;
        private const string SCENES_FOLDER = "Assets/0.Scenes";

        static SceneBuilder()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(PREFS_KEY, false)) return;
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                BuildAllScenes();
                EditorPrefs.SetBool(PREFS_KEY, true);
                Debug.Log("[SceneBuilder] v15 완료");
            };
        }

        // MenuItem 삭제됨 — Level Editor만 BalloonFlow 탭에 표시

        // ═══════════════════════════════════════════
        // BUILD ALL
        // ═══════════════════════════════════════════

        static void BuildAllScenes()
        {
            EnsureFolder(SCENES_FOLDER);

            string _titlePath  = SCENES_FOLDER + "/Title.unity";
            string _lobbyPath  = SCENES_FOLDER + "/Lobby.unity";
            string _inGamePath = SCENES_FOLDER + "/InGame.unity";

            SetupTitleScene(_titlePath);
            SetupLobbyScene(_lobbyPath);
            SetupInGameScene(_inGamePath);

            // Build Settings 등록
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(_titlePath, true),
                new EditorBuildSettingsScene(_lobbyPath, true),
                new EditorBuildSettingsScene(_inGamePath, true)
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(_titlePath, OpenSceneMode.Single);
        }

        // ═══════════════════════════════════════════
        // TITLE SCENE — 기존 씬 열어서 Core 매니저 확인/추가
        // ═══════════════════════════════════════════

        static void SetupTitleScene(string _path)
        {
            var _scene = OpenOrCreateScene(_path);

            // ── GameManager ──
            EnsureComponent<GameManager>("GameManager");

            // ── CameraManager + 카메라 ──
            var _camMgrGO = EnsureComponent<CameraManager>("CameraManager");
            var _camMgr = _camMgrGO.GetComponent<CameraManager>();

            // MainCamera (CameraManager 자식)
            var _mainCamGO = EnsureChild(_camMgrGO, "Main Camera");
            _mainCamGO.tag = "MainCamera";
            var _mainCam = EnsureComponentOn<Camera>(_mainCamGO);
            _mainCam.orthographic = true;
            _mainCam.clearFlags = CameraClearFlags.SolidColor;
            _mainCam.backgroundColor = new Color(0.08f, 0.08f, 0.16f);
            _mainCam.depth = 0;
            EnsureComponentOn<AudioListener>(_mainCamGO);

            // UICamera (CameraManager 자식, 비활성)
            var _uiCamGO = EnsureChild(_camMgrGO, "UICamera");
            var _uiCam = EnsureComponentOn<Camera>(_uiCamGO);
            _uiCam.clearFlags = CameraClearFlags.Depth;
            _uiCam.orthographic = true;
            _uiCam.orthographicSize = 10f;
            _uiCam.depth = 10;
            _uiCam.cullingMask = 1 << LayerMask.NameToLayer("UI");
            _uiCamGO.SetActive(false);

            // CameraManager에 카메라 참조 와이어링
            WireField(_camMgr, "MainCamera", _mainCam);
            WireField(_camMgr, "UICamera", _uiCam);

            // ── UIManager ──
            EnsureComponent<UIManager>("UIManager");

            // ── ResourceManager ──
            EnsureComponent<ResourceManager>("ResourceManager");

            // ── ObjectPoolManager ──
            EnsureComponent<ObjectPoolManager>("ObjectPoolManager");

            // ── Canvas ──
            EnsureCanvas("Canvas");

            // ── EventSystem ──
            EnsureEventSystem();

            // ── TitleController ──
            EnsureComponent<TitleController>("TitleController");

            EditorSceneManager.SaveScene(_scene, _path);
        }

        // ═══════════════════════════════════════════
        // LOBBY SCENE
        // ═══════════════════════════════════════════

        static void SetupLobbyScene(string _path)
        {
            var _scene = OpenOrCreateScene(_path);

            EnsureCanvas("Canvas");
            EnsureEventSystem();
            EnsureComponent<LobbyController>("LobbyController");

            EditorSceneManager.SaveScene(_scene, _path);
        }

        // ═══════════════════════════════════════════
        // INGAME SCENE
        // ═══════════════════════════════════════════

        static void SetupInGameScene(string _path)
        {
            var _scene = OpenOrCreateScene(_path);

            // 3D 오브젝트
            EnsureLighting();
            EnsureBoardPlatform();

            EnsureCanvas("SceneCanvas");
            EnsureEventSystem();
            EnsureComponent<GameBootstrap>("GameBootstrap");

            EditorSceneManager.SaveScene(_scene, _path);
        }

        // ═══════════════════════════════════════════
        // HELPERS — 씬 열기
        // ═══════════════════════════════════════════

        /// <summary>기존 씬 파일이 있으면 열고, 없으면 새로 생성</summary>
        static UnityEngine.SceneManagement.Scene OpenOrCreateScene(string _path)
        {
            if (System.IO.File.Exists(_path))
                return EditorSceneManager.OpenScene(_path, OpenSceneMode.Single);
            else
                return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        // ═══════════════════════════════════════════
        // HELPERS — 오브젝트 확인/추가
        // ═══════════════════════════════════════════

        /// <summary>이름으로 루트 오브젝트 찾고, 없으면 새로 생성 + 컴포넌트 부착</summary>
        static GameObject EnsureComponent<T>(string _name) where T : Component
        {
            var _go = GameObject.Find(_name);
            if (_go == null)
            {
                _go = new GameObject(_name);
            }
            if (_go.GetComponent<T>() == null)
            {
                _go.AddComponent<T>();
            }
            return _go;
        }

        /// <summary>GameObject에 컴포넌트가 없으면 추가</summary>
        static T EnsureComponentOn<T>(GameObject _go) where T : Component
        {
            var _comp = _go.GetComponent<T>();
            if (_comp == null) _comp = _go.AddComponent<T>();
            return _comp;
        }

        /// <summary>부모 아래에 이름으로 자식 찾고, 없으면 생성</summary>
        static GameObject EnsureChild(GameObject _parent, string _childName)
        {
            var _tr = _parent.transform.Find(_childName);
            if (_tr != null) return _tr.gameObject;

            var _child = new GameObject(_childName);
            _child.transform.SetParent(_parent.transform, false);
            return _child;
        }

        /// <summary>Canvas 확인/추가 + CanvasScaler 설정</summary>
        static GameObject EnsureCanvas(string _name)
        {
            var _go = GameObject.Find(_name);
            if (_go == null)
            {
                _go = new GameObject(_name);
            }

            // Canvas
            var _canvas = _go.GetComponent<Canvas>();
            if (_canvas == null) _canvas = _go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // CanvasScaler
            var _scaler = _go.GetComponent<CanvasScaler>();
            if (_scaler == null) _scaler = _go.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(REF_WIDTH, REF_HEIGHT);
            _scaler.matchWidthOrHeight = 0.5f;

            // GraphicRaycaster
            if (_go.GetComponent<GraphicRaycaster>() == null)
                _go.AddComponent<GraphicRaycaster>();

            _go.layer = LayerMask.NameToLayer("UI");
            return _go;
        }

        /// <summary>EventSystem 확인/추가</summary>
        static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;

            var _go = new GameObject("EventSystem");
            _go.AddComponent<EventSystem>();
            _go.AddComponent<InputSystemUIInputModule>();
        }

        /// <summary>Directional Light 확인/추가</summary>
        static void EnsureLighting()
        {
            var _go = GameObject.Find("Directional Light");
            if (_go == null)
            {
                _go = new GameObject("Directional Light");
            }

            var _light = _go.GetComponent<Light>();
            if (_light == null) _light = _go.AddComponent<Light>();
            _light.type = LightType.Directional;
            _light.color = new Color(1.0f, 0.96f, 0.88f);
            _light.intensity = 1.0f;
            _light.shadows = LightShadows.Soft;
            _go.transform.eulerAngles = new Vector3(50f, -30f, 0f);
        }

        /// <summary>BoardPlatform 확인/추가</summary>
        static void EnsureBoardPlatform()
        {
            var _go = GameObject.Find("BoardPlatform");
            if (_go != null) return; // 이미 있으면 건드리지 않음

            _go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _go.name = "BoardPlatform";
            _go.transform.localScale = new Vector3(12f, 0.2f, 12f);
            _go.transform.position = new Vector3(0f, -0.1f, 2f);
            var _mr = _go.GetComponent<MeshRenderer>();
            var _mat = new Material(Shader.Find("Standard"));
            _mat.color = new Color(0.22f, 0.22f, 0.25f);
            _mr.material = _mat;
        }

        // ═══════════════════════════════════════════
        // HELPERS — 유틸
        // ═══════════════════════════════════════════

        static void WireField(Object _target, string _fieldName, Object _value)
        {
            if (_target == null || _value == null) return;
            var _so = new SerializedObject(_target);
            var _prop = _so.FindProperty(_fieldName);
            if (_prop != null)
            {
                _prop.objectReferenceValue = _value;
                _so.ApplyModifiedProperties();
            }
        }

        static void EnsureFolder(string _path)
        {
            if (!AssetDatabase.IsValidFolder(_path))
            {
                string[] _parts = _path.Split('/');
                string _current = _parts[0];
                for (int i = 1; i < _parts.Length; i++)
                {
                    string _next = _current + "/" + _parts[i];
                    if (!AssetDatabase.IsValidFolder(_next))
                        AssetDatabase.CreateFolder(_current, _parts[i]);
                    _current = _next;
                }
            }
        }
    }
}
#endif
