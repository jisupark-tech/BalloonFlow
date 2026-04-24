using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Priority-based popup queue system. Shows one popup at a time,
    /// queuing others by priority (lower number = higher priority).
    /// </summary>
    /// <remarks>
    /// Layer: Core | Genre: Puzzle | Role: Manager | Phase: 0
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow
    /// </remarks>
    public class PopupManager : Singleton<PopupManager>
    {
        #region Nested Types

        [System.Serializable]
        public struct PopupEntry
        {
            public string popupId;
            public CanvasGroup canvasGroup;
        }

        private struct QueuedPopup : System.IComparable<QueuedPopup>
        {
            public string popupId;
            public int priority;
            public object data;

            public int CompareTo(QueuedPopup other)
            {
                return priority.CompareTo(other.priority);
            }
        }

        #endregion

        #region Serialized Fields

        [SerializeField] private PopupEntry[] _popupEntries;
        [SerializeField] private CanvasGroup _overlayBackground;

        #endregion

        #region Fields

        private readonly Dictionary<string, CanvasGroup> _popupMap = new Dictionary<string, CanvasGroup>();
        private readonly List<QueuedPopup> _queue = new List<QueuedPopup>();
        private string _activePopupId;

        #endregion

        #region Properties

        /// <summary>
        /// Whether any popup is currently showing.
        /// </summary>
        public bool IsPopupActive => !string.IsNullOrEmpty(_activePopupId);

        /// <summary>
        /// The ID of the currently active popup.
        /// </summary>
        public string ActivePopupId => _activePopupId;

        /// <summary>
        /// Number of popups waiting in the queue.
        /// </summary>
        public int QueueCount => _queue.Count;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            InitializePopups();
            EventBus.Subscribe<OnPopupRequested>(HandlePopupRequested);
        }

        protected override void OnDestroy()
        {
            EventBus.Unsubscribe<OnPopupRequested>(HandlePopupRequested);
            base.OnDestroy();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Shows a popup immediately if none is active, otherwise queues it.
        /// </summary>
        /// <param name="popupId">The popup identifier.</param>
        /// <param name="priority">Lower number = higher priority (0 = highest).</param>
        /// <param name="data">Optional data to pass to the popup.</param>
        public void ShowPopup(string popupId, int priority = 100, object data = null)
        {
            if (string.IsNullOrEmpty(popupId))
            {
                Debug.LogWarning("[PopupManager] Popup ID is null or empty.");
                return;
            }

            if (!_popupMap.ContainsKey(popupId))
            {
                Debug.LogWarning($"[PopupManager] Popup '{popupId}' not found in registry.");
                return;
            }

            if (!IsPopupActive)
            {
                ActivatePopup(popupId);
            }
            else
            {
                EnqueuePopup(popupId, priority, data);
            }
        }

        /// <summary>
        /// Closes the currently active popup and shows the next in queue.
        /// </summary>
        public void ClosePopup()
        {
            if (string.IsNullOrEmpty(_activePopupId))
            {
                return;
            }

            DeactivatePopup(_activePopupId);
            string closedId = _activePopupId;
            _activePopupId = null;

            EventBus.Publish(new OnPopupClosed { popupId = closedId });

            TryShowNext();
        }

        /// <summary>
        /// Closes a specific popup by ID. If it's active, shows next in queue.
        /// </summary>
        public void ClosePopup(string popupId)
        {
            if (string.IsNullOrEmpty(popupId))
            {
                return;
            }

            if (_activePopupId == popupId)
            {
                ClosePopup();
            }
            else
            {
                _queue.RemoveAll(q => q.popupId == popupId);
            }
        }

        /// <summary>
        /// Closes all popups and clears the queue.
        /// </summary>
        public void CloseAllPopups()
        {
            if (!string.IsNullOrEmpty(_activePopupId))
            {
                DeactivatePopup(_activePopupId);
                _activePopupId = null;
            }

            _queue.Clear();
            SetOverlayVisible(false);
        }

        /// <summary>
        /// Registers a popup at runtime.
        /// </summary>
        public void RegisterPopup(string popupId, CanvasGroup canvasGroup)
        {
            if (string.IsNullOrEmpty(popupId) || canvasGroup == null)
            {
                return;
            }

            _popupMap[popupId] = canvasGroup;
            SetCanvasGroupVisible(canvasGroup, false);
        }

        /// <summary>
        /// Whether a popup with the given ID is registered.
        /// </summary>
        public bool HasPopup(string popupId)
        {
            return !string.IsNullOrEmpty(popupId) && _popupMap.ContainsKey(popupId);
        }

        #endregion

        #region Private Methods

        private void InitializePopups()
        {
            _popupMap.Clear();

            if (_popupEntries == null)
            {
                return;
            }

            foreach (var entry in _popupEntries)
            {
                if (!string.IsNullOrEmpty(entry.popupId) && entry.canvasGroup != null)
                {
                    _popupMap[entry.popupId] = entry.canvasGroup;
                    SetCanvasGroupVisible(entry.canvasGroup, false);
                }
            }

            if (_overlayBackground != null)
            {
                SetCanvasGroupVisible(_overlayBackground, false);
            }
        }

        private void HandlePopupRequested(OnPopupRequested evt)
        {
            ShowPopup(evt.popupId, evt.priority);
        }

        private void ActivatePopup(string popupId)
        {
            if (_popupMap.TryGetValue(popupId, out CanvasGroup group))
            {
                // UIBase.CloseUI()가 SetActive(false)하므로 여기서 복원
                group.gameObject.SetActive(true);
                // UIBase.Awake가 이미 바인딩을 처리하지만, UIBase를 상속하지 않는
                // 팝업 프리팹이나 RegisterPopup으로 등록된 런타임 오브젝트까지 안전망으로 커버.
                UIParticleBinder.Bind(group.gameObject);
                SetCanvasGroupVisible(group, true);
                SetOverlayVisible(true);
                _activePopupId = popupId;
            }
        }

        private void DeactivatePopup(string popupId)
        {
            if (_popupMap.TryGetValue(popupId, out CanvasGroup group))
            {
                SetCanvasGroupVisible(group, false);
                group.gameObject.SetActive(false);
            }
        }

        private void EnqueuePopup(string popupId, int priority, object data)
        {
            _queue.Add(new QueuedPopup
            {
                popupId = popupId,
                priority = priority,
                data = data
            });

            _queue.Sort();
        }

        private void TryShowNext()
        {
            if (_queue.Count == 0)
            {
                SetOverlayVisible(false);
                return;
            }

            QueuedPopup next = _queue[0];
            _queue.RemoveAt(0);
            ActivatePopup(next.popupId);
        }

        private void SetCanvasGroupVisible(CanvasGroup group, bool visible)
        {
            if (group == null)
            {
                return;
            }

            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        private void SetOverlayVisible(bool visible)
        {
            if (_overlayBackground != null)
            {
                SetCanvasGroupVisible(_overlayBackground, visible);
            }
        }

        #endregion
    }
}
