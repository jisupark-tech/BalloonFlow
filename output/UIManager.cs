using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// UI page state management. Controls visibility of page CanvasGroups
    /// using ShowPage/HidePage pattern with transition support.
    /// </summary>
    /// <remarks>
    /// Layer: Core | Genre: Puzzle | Role: Manager | Phase: 0
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow
    /// </remarks>
    public class UIManager : Singleton<UIManager>
    {
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
        [SerializeField] private float _fadeDuration = 0.25f;

        #endregion

        #region Fields

        private readonly Dictionary<string, CanvasGroup> _pageMap = new Dictionary<string, CanvasGroup>();
        private string _currentPageId;
        private Coroutine _fadeCoroutine;

        #endregion

        #region Properties

        /// <summary>
        /// The currently active page ID.
        /// </summary>
        public string CurrentPageId => _currentPageId;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            InitializePages();

            if (!string.IsNullOrEmpty(_defaultPageId))
            {
                ShowPage(_defaultPageId);
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Shows the specified page and hides the current page.
        /// </summary>
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

            // Hide current page
            if (!string.IsNullOrEmpty(_currentPageId) && _pageMap.TryGetValue(_currentPageId, out CanvasGroup currentGroup))
            {
                SetPageVisible(currentGroup, false);
            }

            // Show new page
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

        /// <summary>
        /// Hides the specified page without showing another.
        /// </summary>
        public void HidePage(string pageId)
        {
            if (string.IsNullOrEmpty(pageId))
            {
                return;
            }

            if (_pageMap.TryGetValue(pageId, out CanvasGroup group))
            {
                SetPageVisible(group, false);

                if (_currentPageId == pageId)
                {
                    _currentPageId = null;
                }
            }
        }

        /// <summary>
        /// Hides all pages.
        /// </summary>
        public void HideAllPages()
        {
            foreach (var kvp in _pageMap)
            {
                if (kvp.Value != null)
                {
                    SetPageVisible(kvp.Value, false);
                }
            }
            _currentPageId = null;
        }

        /// <summary>
        /// Whether a page with the given ID is registered.
        /// </summary>
        public bool HasPage(string pageId)
        {
            return !string.IsNullOrEmpty(pageId) && _pageMap.ContainsKey(pageId);
        }

        /// <summary>
        /// Whether the specified page is currently visible.
        /// </summary>
        public bool IsPageVisible(string pageId)
        {
            if (_pageMap.TryGetValue(pageId, out CanvasGroup group))
            {
                return group != null && group.alpha > 0f;
            }
            return false;
        }

        /// <summary>
        /// Registers a page at runtime (for dynamically loaded pages).
        /// </summary>
        public void RegisterPage(string pageId, CanvasGroup canvasGroup)
        {
            if (string.IsNullOrEmpty(pageId) || canvasGroup == null)
            {
                return;
            }

            _pageMap[pageId] = canvasGroup;
            SetPageVisible(canvasGroup, false);
        }

        /// <summary>
        /// Unregisters a page.
        /// </summary>
        public void UnregisterPage(string pageId)
        {
            if (!string.IsNullOrEmpty(pageId))
            {
                _pageMap.Remove(pageId);
            }
        }

        #endregion

        #region Private Methods

        private void InitializePages()
        {
            _pageMap.Clear();

            if (_pages == null)
            {
                return;
            }

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
            if (group == null)
            {
                return;
            }

            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        #endregion
    }
}
