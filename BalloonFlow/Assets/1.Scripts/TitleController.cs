using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Touchscreen = UnityEngine.InputSystem.Touchscreen;

namespace BalloonFlow
{
    /// <summary>
    /// Title scene controller. Shows logo, subtitle, "tap to start" prompt.
    /// Transitions to Lobby scene on tap or after timeout.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Controller | Phase: 0
    /// </remarks>
    public class TitleController : MonoBehaviour
    {
        #region Constants

        private const int REF_WIDTH  = 1080;
        private const int REF_HEIGHT = 1920;
        private const float AUTO_TRANSITION_DELAY = 3.0f;

        private static readonly Color BG_TITLE = new Color(0.08f, 0.08f, 0.16f, 1f);

        #endregion

        #region Fields

        private bool _tapped;
        private float _timer;
        private Font _font;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 14);

            // Ensure GameManager exists (persistent — creates all persistent managers)
            if (!GameManager.HasInstance)
            {
                var go = new GameObject("GameManager");
                go.AddComponent<GameManager>();
            }

            // Ensure ResourceManager
            if (!ResourceManager.HasInstance)
            {
                var go = new GameObject("Mgr_Resource");
                go.AddComponent<ResourceManager>();
            }

            BuildUI();
        }

        private void Update()
        {
            if (_tapped) return;

            _timer += Time.deltaTime;

            bool mousePressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            bool touchPressed = Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame;

            if (mousePressed || touchPressed || _timer >= AUTO_TRANSITION_DELAY)
            {
                _tapped = true;
                GoToLobby();
            }
        }

        #endregion

        #region Private Methods

        private void GoToLobby()
        {
            if (GameManager.HasInstance)
            {
                GameManager.Instance.LoadScene(GameManager.SCENE_LOBBY);
            }
        }

        private void BuildUI()
        {
            // Find or create canvas
            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasGO = new GameObject("Canvas");
                canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvasGO.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(REF_WIDTH, REF_HEIGHT);
                scaler.matchWidthOrHeight = 0.5f;
                canvasGO.AddComponent<GraphicRaycaster>();
                canvasGO.layer = LayerMask.NameToLayer("UI");
            }

            Transform root = canvas.transform;

            // Background
            var bg = CreatePanel("Background", root, BG_TITLE);
            Stretch(bg.GetComponent<RectTransform>());

            // Logo
            CreateText("LogoText", root, "BalloonFlow", 72,
                TextAnchor.MiddleCenter, Color.white, new Vector2(0, 120), new Vector2(900, 140));

            // Subtitle
            CreateText("SubtitleText", root, "Pop & Flow Puzzle", 28,
                TextAnchor.MiddleCenter, new Color(0.7f, 0.7f, 0.8f), new Vector2(0, 20), new Vector2(600, 50));

            // Tap to start
            CreateText("TapToStart", root, "Tap to Start", 24,
                TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.5f), new Vector2(0, -250), new Vector2(400, 50));
        }

        private GameObject CreatePanel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = true;
            return go;
        }

        private GameObject CreateText(string name, Transform parent, string content,
            int fontSize, TextAnchor alignment, Color color, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.AddComponent<Text>();
            t.text = content;
            t.fontSize = fontSize;
            t.alignment = alignment;
            t.color = color;
            t.font = _font;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return go;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        #endregion
    }
}
