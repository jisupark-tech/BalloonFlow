using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    public class PopupWinningStreak : UIBase
    {
        private const int DataCount = 25;
        private const int VisibleSlotCount = 5;
        private const int ExtraPoolSlots = 2;
        private const int FallbackPoolSlots = 8;
        private const float ScrollElasticity = 0.18f;
        private const float ScrollDecelerationRate = 0.12f;
        private const string SlotPrefabResource = "UI/UIAssets/SlotWinningStreak";
        private const string ClickInfoPrefabResource = "UI/UIAssets/WinningStreakClickInfo";

        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;
        [SerializeField] private Button _btnInfo;

        [Header("[Key Blaze Slots]")]
        [SerializeField] private RectTransform _keyBlazeContents;
        [SerializeField] private GameObject _slotKeyBlazePrefab;
        [SerializeField] private ScrollRect _scrollRect;

        [Header("[Slot Click Info]")]
        [SerializeField] private RectTransform _clickInfoOverlayParent;
        [SerializeField] private GameObject _clickInfo;
        [SerializeField] private float _clickInfoYOffset = 200f;
        [SerializeField] private Button _btnDismissArea;

        private readonly List<Button> _slotButtons = new List<Button>(FallbackPoolSlots);
        private readonly List<RectTransform> _slotItems = new List<RectTransform>(FallbackPoolSlots);
        private bool _slotsBuilt;
        private bool _scrollListenerBound;
        private bool _suppressScrollCallback;
        private float _slotHeightY;
        private float _slotSpacingY;
        private float _slotStrideY;
        private float _contentTopPadding;
        private float _contentBottomPadding;

        protected override void Awake()
        {
            base.Awake();

            ResolveReferences();
            EnsureClickInfoInstance();

            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.AddListener(CloseUI);

            if (_btnInfo != null)
            {
                _btnInfo.onClick.RemoveAllListeners();
                _btnInfo.onClick.AddListener(() =>
                {
                    if (UIManager.HasInstance)
                        UIManager.Instance.OpenUI<PopupWinningStreakInfo>(Const.POPUP_WINNING_STREAK_INFO);
                });
            }

            if (_btnDismissArea != null)
            {
                _btnDismissArea.onClick.RemoveAllListeners();
                _btnDismissArea.onClick.AddListener(HideClickInfo);
            }

            if (_clickInfo != null)
                _clickInfo.SetActive(false);
            if (_btnDismissArea != null)
                _btnDismissArea.gameObject.SetActive(false);

            BindScrollListener();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.RemoveAllListeners();
            if (_btnInfo != null)
                _btnInfo.onClick.RemoveAllListeners();
            if (_btnDismissArea != null)
                _btnDismissArea.onClick.RemoveAllListeners();
            if (_scrollRect != null && _scrollListenerBound)
                _scrollRect.onValueChanged.RemoveListener(HandleScrollValueChanged);

            for (int i = 0; i < _slotButtons.Count; i++)
            {
                if (_slotButtons[i] != null)
                    _slotButtons[i].onClick.RemoveAllListeners();
            }

            _slotButtons.Clear();
            _slotItems.Clear();
        }

        public override void OpenUI()
        {
            ResolveReferences();
            EnsureClickInfoInstance();
            BindScrollListener();

            if (_frame != null)
            {
                _frame.SetTitle("Winning Streak");
                _frame.ShowExitButton(true);
            }

            base.OpenUI();
            BuildVirtualSlots();
            HideClickInfo();
            ResetScrollPosition();
        }

        private void BuildVirtualSlots()
        {
            ResolveReferences();
            Canvas.ForceUpdateCanvases();

            if (_keyBlazeContents == null)
            {
                Debug.LogWarning("[PopupWinningStreak] Content is not assigned.");
                return;
            }

            GameObject slotPrefab = ResolveSlotPrefab();
            if (slotPrefab == null)
            {
                Debug.LogWarning("[PopupWinningStreak] SlotWinningStreak prefab was not found.");
                return;
            }

            CacheSlotMetrics(slotPrefab);
            DisableContentLayoutControllers();
            SetVirtualContentHeight();

            if (_slotsBuilt)
            {
                EnsurePoolSize(slotPrefab);
                ApplyPoolSlotLayout();
                RefreshVisibleSlots();
                return;
            }

            ClearSlotContent(slotPrefab);
            _slotButtons.Clear();
            _slotItems.Clear();

            int poolCount = CalculatePoolCount();
            for (int i = 0; i < poolCount; i++)
                CreatePooledSlot(slotPrefab, i);

            _slotsBuilt = true;
            ApplyPoolSlotLayout();
            RefreshVisibleSlots();
        }

        private void ResolveReferences()
        {
            if (_scrollRect == null)
                _scrollRect = GetComponentInChildren<ScrollRect>(true);

            if (_keyBlazeContents == null && _scrollRect != null)
                _keyBlazeContents = _scrollRect.content;

            if (_scrollRect == null && _keyBlazeContents != null)
                _scrollRect = _keyBlazeContents.GetComponentInParent<ScrollRect>(true);

            ConfigureScrollRect();
            ConfigureContentTransform();
        }

        private void ConfigureScrollRect()
        {
            if (_scrollRect == null)
                return;

            _scrollRect.vertical = true;
            _scrollRect.horizontal = false;
            _scrollRect.movementType = ScrollRect.MovementType.Elastic;
            _scrollRect.elasticity = ScrollElasticity;
            _scrollRect.inertia = true;
            _scrollRect.decelerationRate = ScrollDecelerationRate;

            if (_scrollRect.viewport == null && _keyBlazeContents != null)
                _scrollRect.viewport = _keyBlazeContents.parent as RectTransform;

            RectTransform viewport = _scrollRect.viewport;
            if (viewport == null)
                return;

            if (viewport.GetComponent<RectMask2D>() == null)
                viewport.gameObject.AddComponent<RectMask2D>();
        }

        private void ConfigureContentTransform()
        {
            if (_keyBlazeContents == null)
                return;

            _keyBlazeContents.anchorMin = new Vector2(0f, 1f);
            _keyBlazeContents.anchorMax = new Vector2(1f, 1f);
            _keyBlazeContents.pivot = new Vector2(0.5f, 1f);

            if (_scrollRect != null)
                _scrollRect.content = _keyBlazeContents;
        }

        private GameObject ResolveSlotPrefab()
        {
            if (_slotKeyBlazePrefab != null)
                return _slotKeyBlazePrefab;

            _slotKeyBlazePrefab = Resources.Load<GameObject>(SlotPrefabResource);
            if (_slotKeyBlazePrefab != null)
                return _slotKeyBlazePrefab;

            if (_keyBlazeContents != null && _keyBlazeContents.childCount > 0)
            {
                _slotKeyBlazePrefab = _keyBlazeContents.GetChild(0).gameObject;
                _slotKeyBlazePrefab.SetActive(false);
                return _slotKeyBlazePrefab;
            }

            return null;
        }

        private void CacheSlotMetrics(GameObject slotPrefab)
        {
            VerticalLayoutGroup layoutGroup = _keyBlazeContents != null ? _keyBlazeContents.GetComponent<VerticalLayoutGroup>() : null;
            _slotSpacingY = layoutGroup != null ? layoutGroup.spacing : 0f;
            _contentTopPadding = layoutGroup != null ? layoutGroup.padding.top : 0f;
            _contentBottomPadding = layoutGroup != null ? layoutGroup.padding.bottom : 0f;

            RectTransform slotRt = slotPrefab != null ? slotPrefab.GetComponent<RectTransform>() : null;
            _slotHeightY = CalculateSlotHeightForFiveVisible(slotRt);

            _slotStrideY = Mathf.Max(1f, _slotHeightY + _slotSpacingY);
        }

        private float CalculateSlotHeightForFiveVisible(RectTransform slotRt)
        {
            float prefabHeight = slotRt != null ? slotRt.rect.height : 0f;
            if (prefabHeight <= 1f && slotRt != null)
                prefabHeight = LayoutUtility.GetPreferredHeight(slotRt);
            if (prefabHeight <= 1f)
                prefabHeight = 100f;

            if (_scrollRect == null || _scrollRect.viewport == null)
                return prefabHeight;

            float viewportHeight = _scrollRect.viewport.rect.height;
            if (viewportHeight <= 1f)
                return prefabHeight;

            float availableHeight = viewportHeight - _contentTopPadding - _contentBottomPadding;
            availableHeight -= _slotSpacingY * Mathf.Max(0, VisibleSlotCount - 1);
            if (availableHeight <= 1f)
                return prefabHeight;

            return availableHeight / VisibleSlotCount;
        }

        private void DisableContentLayoutControllers()
        {
            if (_keyBlazeContents == null)
                return;

            LayoutGroup[] layoutGroups = _keyBlazeContents.GetComponents<LayoutGroup>();
            for (int i = 0; i < layoutGroups.Length; i++)
                layoutGroups[i].enabled = false;

            ContentSizeFitter fitter = _keyBlazeContents.GetComponent<ContentSizeFitter>();
            if (fitter != null)
                fitter.enabled = false;
        }

        private void SetVirtualContentHeight()
        {
            if (_keyBlazeContents == null || _slotStrideY <= 1f)
                return;

            float contentHeight = _contentTopPadding + _contentBottomPadding;
            contentHeight += _slotHeightY * DataCount;
            contentHeight += _slotSpacingY * Mathf.Max(0, DataCount - 1);

            if (_scrollRect != null && _scrollRect.viewport != null)
                contentHeight = Mathf.Max(contentHeight, _scrollRect.viewport.rect.height + 1f);

            _keyBlazeContents.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);
        }

        private int CalculatePoolCount()
        {
            if (_scrollRect == null || _scrollRect.viewport == null || _slotStrideY <= 1f)
                return Mathf.Min(DataCount, FallbackPoolSlots);

            int poolCount = Mathf.Max(1, VisibleSlotCount + ExtraPoolSlots);
            return Mathf.Min(DataCount, poolCount);
        }

        private void EnsurePoolSize(GameObject slotPrefab)
        {
            int targetPoolCount = CalculatePoolCount();
            for (int i = _slotItems.Count; i < targetPoolCount; i++)
                CreatePooledSlot(slotPrefab, i);
        }

        private void CreatePooledSlot(GameObject slotPrefab, int poolIndex)
        {
            GameObject slot = Instantiate(slotPrefab, _keyBlazeContents);
            slot.name = $"SlotWinningStreak_Pool_{poolIndex:D2}";
            slot.SetActive(true);

            RectTransform slotRt = slot.GetComponent<RectTransform>();
            if (slotRt != null)
            {
                ApplySlotLayout(slotRt);
                _slotItems.Add(slotRt);
            }

            Button slotButton = FindSlotButton(slot);
            if (slotButton == null)
            {
                Debug.LogWarning($"[PopupWinningStreak] Pool slot {poolIndex}: Button not found.");
                return;
            }

            slotButton.onClick.RemoveAllListeners();
            slotButton.onClick.AddListener(() => ShowClickInfoForSlot(slotRt));
            _slotButtons.Add(slotButton);
        }

        private void ApplyPoolSlotLayout()
        {
            for (int i = 0; i < _slotItems.Count; i++)
                ApplySlotLayout(_slotItems[i]);
        }

        private void ApplySlotLayout(RectTransform slotRt)
        {
            if (slotRt == null)
                return;

            slotRt.localScale = Vector3.one;
            slotRt.anchorMin = new Vector2(0f, 1f);
            slotRt.anchorMax = new Vector2(1f, 1f);
            slotRt.pivot = new Vector2(0.5f, 1f);
            slotRt.sizeDelta = new Vector2(slotRt.sizeDelta.x, _slotHeightY);

            LayoutElement layoutElement = slotRt.GetComponent<LayoutElement>();
            if (layoutElement != null)
            {
                layoutElement.minHeight = _slotHeightY;
                layoutElement.preferredHeight = _slotHeightY;
                layoutElement.flexibleHeight = 0f;
            }
        }

        private void ClearSlotContent(GameObject template)
        {
            if (_keyBlazeContents == null)
                return;

            Transform templateTransform = template != null ? template.transform : null;
            for (int i = _keyBlazeContents.childCount - 1; i >= 0; i--)
            {
                Transform child = _keyBlazeContents.GetChild(i);
                if (child == templateTransform)
                {
                    child.gameObject.SetActive(false);
                    continue;
                }

                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }

        private Button FindSlotButton(GameObject slot)
        {
            if (slot == null)
                return null;

            Button[] buttons = slot.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null && buttons[i].name == "BtnReward")
                    return buttons[i];
            }

            return buttons.Length > 0 ? buttons[0] : null;
        }

        private void EnsureClickInfoInstance()
        {
            if (_clickInfo != null || _clickInfoOverlayParent == null)
                return;

            GameObject prefab = Resources.Load<GameObject>(ClickInfoPrefabResource);
            if (prefab == null)
                return;

            _clickInfo = Instantiate(prefab, _clickInfoOverlayParent);
            _clickInfo.name = prefab.name;
            _clickInfo.SetActive(false);
        }

        private void BindScrollListener()
        {
            if (_scrollRect == null || _scrollListenerBound)
                return;

            _scrollRect.onValueChanged.AddListener(HandleScrollValueChanged);
            _scrollListenerBound = true;
        }

        private void HandleScrollValueChanged(Vector2 _)
        {
            if (_suppressScrollCallback)
                return;

            RefreshVisibleSlots();
            HideClickInfo();
        }

        private void ResetScrollPosition()
        {
            if (_scrollRect == null || _keyBlazeContents == null || !_slotsBuilt)
                return;

            SetVirtualContentHeight();
            Canvas.ForceUpdateCanvases();

            _suppressScrollCallback = true;
            _scrollRect.StopMovement();
            _scrollRect.velocity = Vector2.zero;
            _scrollRect.verticalNormalizedPosition = 0f;
            Canvas.ForceUpdateCanvases();
            _scrollRect.velocity = Vector2.zero;
            _suppressScrollCallback = false;

            RefreshVisibleSlots();
        }

        private void RefreshVisibleSlots()
        {
            if (_keyBlazeContents == null || _slotStrideY <= 1f || _slotItems.Count == 0)
                return;

            int maxFirstDataIndex = Mathf.Max(0, DataCount - _slotItems.Count);
            float verticalNormalizedPosition = _scrollRect != null ? Mathf.Clamp01(_scrollRect.verticalNormalizedPosition) : 1f;
            float normalizedFromTop = 1f - verticalNormalizedPosition;
            int firstDataIndex = Mathf.RoundToInt(normalizedFromTop * maxFirstDataIndex);
            firstDataIndex = Mathf.Clamp(firstDataIndex, 0, maxFirstDataIndex);

            for (int poolIndex = 0; poolIndex < _slotItems.Count; poolIndex++)
            {
                RectTransform slotRt = _slotItems[poolIndex];
                if (slotRt == null)
                    continue;

                int dataIndex = firstDataIndex + poolIndex;
                if (dataIndex >= DataCount)
                {
                    slotRt.gameObject.SetActive(false);
                    continue;
                }

                int rewardNumber = DataCount - dataIndex;
                slotRt.gameObject.SetActive(true);
                slotRt.name = $"SlotWinningStreak_{rewardNumber:D2}";
                slotRt.anchoredPosition = new Vector2(slotRt.anchoredPosition.x, -(_contentTopPadding + dataIndex * _slotStrideY));
                SetSlotNumber(slotRt.gameObject, rewardNumber, false);
            }
        }

        private void SetSlotNumber(GameObject slot, int number, bool warnIfMissing = true)
        {
            string s = number.ToString();
            TMP_Text[] tmps = slot.GetComponentsInChildren<TMP_Text>(true);
            bool setMain = false;
            bool setOutline = false;

            foreach (TMP_Text t in tmps)
            {
                if (t.name == "TextNumber")
                {
                    t.text = s;
                    setMain = true;
                }
                else if (t.name == "TextNumberOutline")
                {
                    t.text = s;
                    setOutline = true;
                }
            }

            if (warnIfMissing && (!setMain || !setOutline))
                Debug.LogWarning($"[PopupWinningStreak] Slot {number}: TextNumber/Outline missing. main={setMain}, outline={setOutline}");
        }

        private void ShowClickInfoForSlot(RectTransform slotRt)
        {
            if (slotRt == null)
                return;

            if (_clickInfoOverlayParent == null)
            {
                Debug.LogWarning("[PopupWinningStreak] Click info overlay parent is not assigned.");
                return;
            }

            if (_clickInfo == null)
            {
                Debug.LogWarning("[PopupWinningStreak] Click info object is not assigned.");
                return;
            }

            if (_btnDismissArea == null)
            {
                Debug.LogWarning("[PopupWinningStreak] Dismiss area is not assigned.");
                return;
            }

            _clickInfo.transform.SetParent(_clickInfoOverlayParent, false);

            Vector3 worldTop = slotRt.TransformPoint(new Vector3(0f, slotRt.rect.height * (1f - slotRt.pivot.y), 0f));
            Canvas canvas = _clickInfoOverlayParent.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

            Vector2 screenPt = RectTransformUtility.WorldToScreenPoint(cam, worldTop);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_clickInfoOverlayParent, screenPt, cam, out Vector2 localPt))
            {
                RectTransform clickInfoRt = _clickInfo.GetComponent<RectTransform>();
                if (clickInfoRt != null)
                    clickInfoRt.anchoredPosition = localPt + new Vector2(0f, _clickInfoYOffset);
            }

            _clickInfo.SetActive(true);
            _btnDismissArea.gameObject.SetActive(true);
        }

        private void HideClickInfo()
        {
            if (_clickInfo != null)
                _clickInfo.SetActive(false);
            if (_btnDismissArea != null)
                _btnDismissArea.gameObject.SetActive(false);
        }
    }
}
