using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Manages screen/page transitions for all major pages in the game.
    /// Maintains a navigation stack for GoBack() support and delegates
    /// canvas visibility to UIManager.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Controller | Phase: 3
    /// DB Reference: No DB match — generated from L3 YAML logicFlow
    /// Pages: "Title", "Main", "LevelSelect", "Game", "Result", "Settings"
    /// </remarks>
    public class PageController : Singleton<PageController>
    {
        #region Constants

        public const string PAGE_TITLE       = "Title";
        public const string PAGE_MAIN        = "Main";
        public const string PAGE_LEVEL_SELECT = "LevelSelect";
        public const string PAGE_GAME        = "Game";
        public const string PAGE_RESULT      = "Result";
        public const string PAGE_SETTINGS    = "Settings";

        #endregion

        #region Serialized Fields

        [SerializeField] private GameObject _titlePage;
        [SerializeField] private GameObject _mainPage;
        [SerializeField] private GameObject _levelSelectPage;
        [SerializeField] private GameObject _gamePage;
        [SerializeField] private GameObject _resultPage;
        [SerializeField] private GameObject _settingsPage;

        #endregion

        #region Fields

        private readonly Dictionary<string, GameObject> _pageObjects = new Dictionary<string, GameObject>();
        private readonly Stack<string> _pageStack = new Stack<string>();
        private string _currentPage;

        #endregion

        #region Properties

        /// <summary>
        /// Returns the ID of the currently shown page, or empty string if none.
        /// </summary>
        public string CurrentPage => _currentPage ?? string.Empty;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            RegisterPageObjects();
            HideAllPageObjects();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Shows the specified page and hides the current one.
        /// Pushes the previous page onto the navigation stack.
        /// </summary>
        public void ShowPage(string pageId)
        {
            if (string.IsNullOrEmpty(pageId))
            {
                Debug.LogWarning("[PageController] ShowPage called with null/empty pageId.");
                return;
            }

            if (!_pageObjects.ContainsKey(pageId))
            {
                Debug.LogWarning($"[PageController] Page '{pageId}' is not registered.");
                return;
            }

            string fromPage = _currentPage;

            // Hide current page object
            if (!string.IsNullOrEmpty(_currentPage))
            {
                SetPageActive(_currentPage, false);
            }

            // Show new page object
            SetPageActive(pageId, true);
            _currentPage = pageId;

            EventBus.Publish(new OnPageChanged
            {
                fromPage = fromPage ?? string.Empty,
                toPage   = pageId
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

            SetPageActive(pageId, false);

            if (_currentPage == pageId)
            {
                _currentPage = null;
            }

        }

        /// <summary>
        /// Returns the ID of the currently shown page.
        /// </summary>
        public string GetCurrentPage()
        {
            return _currentPage ?? string.Empty;
        }

        /// <summary>
        /// Navigates to a page and pushes the previous page onto the back stack.
        /// </summary>
        public void GoToPage(string pageId)
        {
            if (string.IsNullOrEmpty(pageId))
            {
                Debug.LogWarning("[PageController] GoToPage called with null/empty pageId.");
                return;
            }

            // Push current page onto stack before transitioning
            if (!string.IsNullOrEmpty(_currentPage))
            {
                _pageStack.Push(_currentPage);
            }

            ShowPage(pageId);
        }

        /// <summary>
        /// Returns to the previous page if one exists on the navigation stack.
        /// Does nothing if the stack is empty.
        /// </summary>
        public void GoBack()
        {
            if (_pageStack.Count == 0)
            {
                Debug.Log("[PageController] GoBack called but navigation stack is empty.");
                return;
            }

            string previousPage = _pageStack.Pop();
            ShowPage(previousPage);
        }

        /// <summary>
        /// Clears the navigation history stack without changing the current page.
        /// </summary>
        public void ClearHistory()
        {
            _pageStack.Clear();
        }

        /// <summary>
        /// Returns true if there is at least one page on the back stack.
        /// </summary>
        public bool CanGoBack()
        {
            return _pageStack.Count > 0;
        }

        #endregion

        #region Private Methods

        private void RegisterPageObjects()
        {
            _pageObjects.Clear();

            if (_titlePage        != null) _pageObjects[PAGE_TITLE]        = _titlePage;
            if (_mainPage         != null) _pageObjects[PAGE_MAIN]         = _mainPage;
            if (_levelSelectPage  != null) _pageObjects[PAGE_LEVEL_SELECT] = _levelSelectPage;
            if (_gamePage         != null) _pageObjects[PAGE_GAME]         = _gamePage;
            if (_resultPage       != null) _pageObjects[PAGE_RESULT]       = _resultPage;
            if (_settingsPage     != null) _pageObjects[PAGE_SETTINGS]     = _settingsPage;
        }

        private void HideAllPageObjects()
        {
            foreach (var kvp in _pageObjects)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.SetActive(false);
                }
            }
        }

        private void SetPageActive(string pageId, bool active)
        {
            if (_pageObjects.TryGetValue(pageId, out GameObject pageObj))
            {
                if (pageObj != null)
                {
                    pageObj.SetActive(active);
                }
            }
        }

        #endregion
    }
}
