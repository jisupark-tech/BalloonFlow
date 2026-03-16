using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// Persistent UI manager with dual canvas system.
    /// Owns UICanvas (regular UI) and PopupCanvas (higher sort order for popups).
    /// Provides page management, fade transitions, and popup delegation.
    /// </summary>
    /// <remarks>
    /// Layer: Core | Genre: Puzzle | Role: Manager | Phase: 0
    /// </remarks>
    public class UIManager : Singleton<UIManager>
    {
        #region Constants

        private const int REF_WIDTH  = 1080;
        private const int REF_HEIGHT = 1920;
        private const int UI_SORT_ORDER    = 10;
        private const int POPUP_SORT_ORDER = 20;

        #endregion

        #region Nested Types

        [System.Serializable]
        public struct PageEntry
        {
            public string pageId;
            public CanvasGroup canvasGroup;
        }

        #endregion

        #region Serialized Fields

        [SerializeField] private PageEntry[] _pages;
        [SerializeField] private string _defaultPageId;

        #endregion

        #region Fields

        private readonly Dictionary<string, CanvasGroup> _pageMap = new Dictionary<string, CanvasGroup>();
        private string _currentPageId;

        // Dual canvas
        private Canvas _uiCanvas;
        private Canvas _popupCanvas;
        private CanvasGroup _fadeOverlay;
        private Coroutine _fadeCoroutine;

        #endregion

        #region Properties

        public string CurrentPageId => _currentPageId;
        public Canvas UICanvas => _uiCanvas;
        public Canvas PopupCanvas => _popupCanvas;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            EnsureDualCanvas();
            InitializePages();

            if (!string.IsNullOrEmpty(_defaultPageId))
            {
                ShowPage(_defaultPageId);
            }
        }

        #endregion

        #region Public Methods — Pages

        public void ShowPage(string pageId)
        {
            if (string.IsNullOrEmpty(pageId))
            {
                Debug.LogWarning("[UIManager] Page ID is null or empty.");
                return;
            }

            if (!_pageMap.ContainsKey(pageId))
            {
                Debug.LogWarning($"[UIManager] Page '{pageId}' not found.");
                return;
            }

            string fromPage = _currentPageId;

            if (!string.IsNullOrEmpty(_currentPageId) && _pageMap.TryGetValue(_currentPageId, out CanvasGroup currentGroup))
            {
                SetPageVisible(currentGroup, false);
            }

            if (_pageMap.TryGetValue(pageId, out CanvasGroup newGroup))
            {
                SetPageVisible(newGroup, true);
            }

            _currentPageId = pageId;

            EventBus.Publish(new OnPageChanged
            {
                fromPage = fromPage ?? string.Empty,
                toPage = pageId
            });
        }

        public void HidePage(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return;

            if (_pageMap.TryGetValue(pageId, out CanvasGroup group))
            {
                SetPageVisible(group, false);
                if (_currentPageId == pageId) _currentPageId = null;
            }
        }

        public void HideAllPages()
        {
            foreach (var kvp in _pageMap)
            {
                if (kvp.Value != null) SetPageVisible(kvp.Value, false);
            }
            _currentPageId = null;
        }

        public bool HasPage(string pageId)
        {
            return !string.IsNullOrEmpty(pageId) && _pageMap.ContainsKey(pageId);
        }

        public bool IsPageVisible(string pageId)
        {
            if (_pageMap.TryGetValue(pageId, out CanvasGroup group))
            {
                return group != null && group.alpha > 0f;
            }
            return false;
        }

        public void RegisterPage(string pageId, CanvasGroup canvasGroup)
        {
            if (string.IsNullOrEmpty(pageId) || canvasGroup == null) return;
            _pageMap[pageId] = canvasGroup;
            SetPageVisible(canvasGroup, false);
        }

        public void UnregisterPage(string pageId)
        {
            if (!string.IsNullOrEmpty(pageId)) _pageMap.Remove(pageId);
        }

        #endregion

        #region Public Methods — Fade

        /// <summary>
        /// Fades the screen to black over the given duration.
        /// </summary>
        public void FadeOut(float duration)
        {
            EnsureFadeOverlay();
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeCoroutine(0f, 1f, duration));
        }

        /// <summary>
        /// Fades the screen from black to clear over the given duration.
        /// </summary>
        public void FadeIn(float duration)
        {
            EnsureFadeOverlay();
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeCoroutine(1f, 0f, duration));
        }

        #endregion

        #region Public Methods — Canvas Access

        /// <summary>
        /// Returns the UICanvas transform for attaching regular UI elements.
        /// </summary>
        public Transform GetUICanvasTransform()
        {
            EnsureDualCanvas();
            return _uiCanvas != null ? _uiCanvas.transform : null;
        }

        /// <summary>
        /// Returns the PopupCanvas transform for attaching popup elements.
        /// </summary>
        public Transform GetPopupCanvasTransform()
        {
            EnsureDualCanvas();
            return _popupCanvas != null ? _popupCanvas.transform : null;
        }

        #endregion

        #region Private Methods

        private void InitializePages()
        {
            _pageMap.Clear();

            if (_pages == null) return;

            foreach (var entry in _pages)
            {
                if (!string.IsNullOrEmpty(entry.pageId) && entry.canvasGroup != null)
                {
                    _pageMap[entry.pageId] = entry.canvasGroup;
                    SetPageVisible(entry.canvasGroup, false);
                }
            }
        }

        private void SetPageVisible(CanvasGroup group, bool visible)
        {
            if (group == null) return;
            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        /// <summary>
        /// Creates the dual canvas system if not already set up.
        /// UICanvas: sort order 10 (regular UI)
        /// PopupCanvas: sort order 20 (popups above UI)
        /// </summary>
        private void EnsureDualCanvas()
        {
            if (_uiCanvas != null && _popupCanvas != null) return;

            // Find or create UICamera
            Camera uiCam = FindUICamera();

            // UICanvas
            if (_uiCanvas == null)
            {
                _uiCanvas = FindCanvasByName("UICanvas");
                if (_uiCanvas == null)
                {
                    _uiCanvas = CreateCanvas("UICanvas", UI_SORT_ORDER, uiCam);
                }
            }

            // PopupCanvas
            if (_popupCanvas == null)
            {
                _popupCanvas = FindCanvasByName("PopupCanvas");
                if (_popupCanvas == null)
                {
                    _popupCanvas = CreateCanvas("PopupCanvas", POPUP_SORT_ORDER, uiCam);
                }
            }

            // Parent canvases under UIManager for organization
            if (_uiCanvas.transform.parent != transform)
                _uiCanvas.transform.SetParent(transform, false);
            if (_popupCanvas.transform.parent != transform)
                _popupCanvas.transform.SetParent(transform, false);
        }

        private Canvas CreateCanvas(string name, int sortOrder, Camera cam)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            var canvas = go.AddComponent<Canvas>();

            if (cam != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 1f;
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            canvas.sortingOrder = sortOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(REF_WIDTH, REF_HEIGHT);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();

            return canvas;
        }

        private Camera FindUICamera()
        {
            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var cam in cameras)
            {
                if (cam.gameObject.name == "UICamera") return cam;
            }
            return null;
        }

        private Canvas FindCanvasByName(string name)
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (var c in canvases)
            {
                if (c.gameObject.name == name) return c;
            }
            return null;
        }

        private void EnsureFadeOverlay()
        {
            if (_fadeOverlay != null) return;

            Transform parent = _popupCanvas != null ? _popupCanvas.transform : transform;

            var go = new GameObject("FadeOverlay");
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = Color.black;
            img.raycastTarget = true;

            _fadeOverlay = go.AddComponent<CanvasGroup>();
            _fadeOverlay.alpha = 0f;
            _fadeOverlay.interactable = false;
            _fadeOverlay.blocksRaycasts = false;
        }

        private IEnumerator FadeCoroutine(float from, float to, float duration)
        {
            if (_fadeOverlay == null) yield break;

            _fadeOverlay.alpha = from;
            _fadeOverlay.blocksRaycasts = true;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                _fadeOverlay.alpha = Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }

            _fadeOverlay.alpha = to;
            _fadeOverlay.blocksRaycasts = to > 0.01f;
            _fadeOverlay.interactable = false;
        }

        #endregion
    }
}
