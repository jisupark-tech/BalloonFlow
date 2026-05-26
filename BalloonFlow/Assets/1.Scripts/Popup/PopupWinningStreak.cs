using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// WinningStreak 이벤트 메인 팝업. 25 stage 보상 리스트를 virtual scroll 로 표시.
    /// 데이터: WinningStreakConfigService.Config + UserData.winningStreak (WinningStreakManager 경유).
    /// 슬롯 BtnReward 클릭 → 달성/미수령 stage 면 ClaimStage 호출. KeyBlazeClickInfo 는 사용 안 함.
    /// </summary>
    public class PopupWinningStreak : UIBase
    {
        private const int FallbackDataCount = 25;
        private const int VisibleSlotCount = 5;
        private const int ExtraPoolSlots = 2;
        private const int FallbackPoolSlots = 8;
        private const float ScrollElasticity = 0.18f;
        private const float ScrollDecelerationRate = 0.12f;
        private const string SlotPrefabResource = "UI/UIAssets/SlotWinningStreak";
        private const float SlotFixedWidth = 900f;
        private const float SlotFixedHeight = 300f;

        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;
        [SerializeField] private Button _btnInfo;

        [Header("[Key Blaze Slots]")]
        [SerializeField] private RectTransform _keyBlazeContents;
        [SerializeField] private GameObject _slotKeyBlazePrefab;
        [SerializeField] private ScrollRect _scrollRect;

        [Header("[Header — 현재 streak/진행 표시]")]
        [Tooltip("ImageMultiplier 루트 GameObject — 자식으로 SlotMultiplier(0..4) 5개를 가진 컨테이너. 각 SlotMultiplier 안의 TextMultiplier 에 streak1..streak5+ 배수 텍스트를 Firestore config 에서 채움.")]
        [SerializeField] private Transform _multiplierSlotsRoot;
        [Tooltip("진행 상태 텍스트 (inner). TxtDescriptionOutline 의 자식 TxtDescription.")]
        [SerializeField] private TMP_Text _txtDescription;
        [Tooltip("진행 상태 텍스트 (outline). 부모 TxtDescriptionOutline. _txtDescription 과 같은 내용으로 동기 갱신.")]
        [SerializeField] private TMP_Text _txtDescriptionOutline;

        private readonly List<TMP_Text> _multiplierTexts = new List<TMP_Text>(5);
        private bool _multiplierTextsResolved;

        // SlotWinningStreak 상태 스프라이트 캐시 — atlas_ui 가 늦게 로드될 수 있어 lazy fetch.
        private Sprite _sprFrameNumberDefault;
        private Sprite _sprArrow;
        private Sprite _sprArrowComplete;
        private Sprite _sprSlot;

        private readonly List<PooledSlot> _pooledSlots = new List<PooledSlot>(FallbackPoolSlots);
        private bool _slotsBuilt;
        private bool _scrollListenerBound;
        private bool _suppressScrollCallback;
        private bool _eventsSubscribed;
        private float _slotHeightY;
        private float _slotSpacingY;
        private float _slotStrideY;
        private float _contentTopPadding;
        private float _contentBottomPadding;

        private int DataCount
        {
            get
            {
                if (WinningStreakManager.HasInstance)
                {
                    int n = WinningStreakManager.Instance.TotalStageCount;
                    if (n > 0) return n;
                }
                return FallbackDataCount;
            }
        }

        protected override void Awake()
        {
            base.Awake();

            ResolveReferences();

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

            BindScrollListener();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.RemoveAllListeners();
            if (_btnInfo != null)
                _btnInfo.onClick.RemoveAllListeners();
            if (_scrollRect != null && _scrollListenerBound)
                _scrollRect.onValueChanged.RemoveListener(HandleScrollValueChanged);

            UnsubscribeStateEvents();

            for (int i = 0; i < _pooledSlots.Count; i++)
            {
                var p = _pooledSlots[i];
                if (p?.button != null)
                    p.button.onClick.RemoveAllListeners();
            }

            _pooledSlots.Clear();
        }

        public override void OpenUI()
        {
            ResolveReferences();
            BindScrollListener();
            SubscribeStateEvents();

            if (_frame != null)
            {
                _frame.SetTitle("Winning Streak");
                _frame.ShowExitButton(true);
            }

            base.OpenUI();
            BuildVirtualSlots();
            ResetScrollPosition();
            RefreshHeader();
        }

        // ── State 이벤트 ─────────────────────────────────────────

        private void SubscribeStateEvents()
        {
            if (_eventsSubscribed) return;
            if (WinningStreakManager.HasInstance)
                WinningStreakManager.Instance.OnStateChanged += HandleStateChanged;
            if (WinningStreakConfigService.HasInstance)
                WinningStreakConfigService.Instance.OnConfigLoaded += HandleStateChanged;
            _eventsSubscribed = true;
        }

        private void UnsubscribeStateEvents()
        {
            if (!_eventsSubscribed) return;
            if (WinningStreakManager.HasInstance)
                WinningStreakManager.Instance.OnStateChanged -= HandleStateChanged;
            if (WinningStreakConfigService.HasInstance)
                WinningStreakConfigService.Instance.OnConfigLoaded -= HandleStateChanged;
            _eventsSubscribed = false;
        }

        private void HandleStateChanged()
        {
            if (!gameObject.activeInHierarchy) return;
            // config 가 늦게 도착하면 content height 재계산 필요할 수 있음.
            SetVirtualContentHeight();
            RefreshVisibleSlots();
            RefreshHeader();
        }

        // ── Header (multiplier 슬롯 + 진행 텍스트) ───────────────

        private void RefreshHeader()
        {
            var mgr = WinningStreakManager.HasInstance ? WinningStreakManager.Instance : null;
            var state = mgr?.State;
            var cfg = mgr?.Config;

            RefreshMultiplierSlots(cfg);
            RefreshDescriptionText(state, cfg);
        }

        /// <summary>ImageMultiplier 아래의 SlotMultiplier(0..4) 5개에 streak1..streak5+ 배수 텍스트 채움.
        /// Firestore config 도착 전엔 디자이너가 prefab 에 박아둔 placeholder 텍스트가 유지됨.</summary>
        private void RefreshMultiplierSlots(WinningStreakConfigDoc cfg)
        {
            if (_multiplierSlotsRoot == null) return;
            if (cfg == null || cfg.streakMultipliers == null) return;

            ResolveMultiplierTexts();
            if (_multiplierTexts.Count == 0) return;

            var m = cfg.streakMultipliers;
            int[] values = { m.streak1, m.streak2, m.streak3, m.streak4, m.streak5Plus };

            int n = Mathf.Min(_multiplierTexts.Count, values.Length);
            for (int i = 0; i < n; i++)
            {
                var tmp = _multiplierTexts[i];
                if (tmp != null) tmp.text = $"x{values[i]}";
            }
        }

        /// <summary>_multiplierSlotsRoot 아래의 자식 SlotMultiplier 들에서 TextMultiplier(TMP_Text) 를 한 번만 수집.
        /// 자식 순서를 그대로 사용 (SlotMultiplier, SlotMultiplier (1), ... 형태).</summary>
        private void ResolveMultiplierTexts()
        {
            if (_multiplierTextsResolved) return;
            _multiplierTextsResolved = true;
            _multiplierTexts.Clear();
            if (_multiplierSlotsRoot == null) return;

            for (int i = 0; i < _multiplierSlotsRoot.childCount; i++)
            {
                var slot = _multiplierSlotsRoot.GetChild(i);
                if (slot == null) continue;
                var tmp = FindChildByName<TMP_Text>(slot.gameObject, "TextMultiplier");
                if (tmp == null)
                {
                    // 자식 어디든 TMP_Text 가 하나 있으면 그것 사용 (이름 변경 내성).
                    tmp = slot.GetComponentInChildren<TMP_Text>(true);
                }
                _multiplierTexts.Add(tmp);
            }
        }

        /// <summary>TxtDescription (inner) + TxtDescriptionOutline (outer) 둘 다 같은 내용으로 동기 갱신.</summary>
        private void RefreshDescriptionText(WinningStreakState state, WinningStreakConfigDoc cfg)
        {
            if (_txtDescription == null && _txtDescriptionOutline == null) return;

            string text;
            if (state == null || cfg == null)
            {
                text = "";
            }
            else if (state.eventFinished)
            {
                text = "All rewards completed!";
            }
            else
            {
                int multiplier = WinningStreakConfigService.HasInstance
                    ? WinningStreakConfigService.Instance.ResolveStreakMultiplier(Mathf.Max(1, state.currentStreak))
                    : 1;
                var stage = WinningStreakConfigService.HasInstance
                    ? WinningStreakConfigService.Instance.GetStage(state.currentStage)
                    : null;
                int need = stage != null ? Mathf.Max(0, stage.requiredPoints - state.currentStagePoints) : 0;
                text = $"{Mathf.Max(1, state.currentStreak)}연승 x{multiplier} / 다음까지 {need}";
            }

            if (_txtDescription != null) _txtDescription.text = text;
            if (_txtDescriptionOutline != null) _txtDescriptionOutline.text = text;
        }

        // ── Slot pool / virtual scroll ───────────────────────────

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
            _pooledSlots.Clear();

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
            if (_scrollRect == null) return;

            _scrollRect.vertical = true;
            _scrollRect.horizontal = false;
            _scrollRect.movementType = ScrollRect.MovementType.Elastic;
            _scrollRect.elasticity = ScrollElasticity;
            _scrollRect.inertia = true;
            _scrollRect.decelerationRate = ScrollDecelerationRate;

            if (_scrollRect.viewport == null && _keyBlazeContents != null)
                _scrollRect.viewport = _keyBlazeContents.parent as RectTransform;

            RectTransform viewport = _scrollRect.viewport;
            if (viewport == null) return;

            if (viewport.GetComponent<RectMask2D>() == null)
                viewport.gameObject.AddComponent<RectMask2D>();
        }

        private void ConfigureContentTransform()
        {
            if (_keyBlazeContents == null) return;

            _keyBlazeContents.anchorMin = new Vector2(0f, 1f);
            _keyBlazeContents.anchorMax = new Vector2(1f, 1f);
            _keyBlazeContents.pivot = new Vector2(0.5f, 1f);

            if (_scrollRect != null) _scrollRect.content = _keyBlazeContents;
        }

        private GameObject ResolveSlotPrefab()
        {
            if (_slotKeyBlazePrefab != null) return _slotKeyBlazePrefab;
            _slotKeyBlazePrefab = Resources.Load<GameObject>(SlotPrefabResource);
            if (_slotKeyBlazePrefab != null) return _slotKeyBlazePrefab;

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
            return SlotFixedHeight;
        }

        private void DisableContentLayoutControllers()
        {
            if (_keyBlazeContents == null) return;

            // VerticalLayoutGroup 은 런타임에 활성 상태로 유지 (slot 가시 정렬을 위해 필요).
            // 나머지 LayoutGroup 종류는 가상 스크롤 좌표 계산과 충돌하므로 비활성화.
            LayoutGroup[] layoutGroups = _keyBlazeContents.GetComponents<LayoutGroup>();
            for (int i = 0; i < layoutGroups.Length; i++)
            {
                if (layoutGroups[i] is VerticalLayoutGroup)
                    layoutGroups[i].enabled = true;
                else
                    layoutGroups[i].enabled = false;
            }

            ContentSizeFitter fitter = _keyBlazeContents.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
        }

        private void SetVirtualContentHeight()
        {
            if (_keyBlazeContents == null || _slotStrideY <= 1f) return;

            int n = DataCount;
            float contentHeight = _contentTopPadding + _contentBottomPadding;
            contentHeight += _slotHeightY * n;
            contentHeight += _slotSpacingY * Mathf.Max(0, n - 1);

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
            for (int i = _pooledSlots.Count; i < targetPoolCount; i++)
                CreatePooledSlot(slotPrefab, i);
        }

        private void CreatePooledSlot(GameObject slotPrefab, int poolIndex)
        {
            GameObject slot = Instantiate(slotPrefab, _keyBlazeContents);
            slot.name = $"SlotWinningStreak_Pool_{poolIndex:D2}";
            slot.SetActive(true);

            RectTransform slotRt = slot.GetComponent<RectTransform>();
            if (slotRt == null) return;
            ApplySlotLayout(slotRt);

            var pooled = new PooledSlot { root = slotRt };
            BindSlotChildren(pooled, slot);
            _pooledSlots.Add(pooled);

            if (pooled.button != null)
            {
                int captureIndex = _pooledSlots.Count - 1;
                pooled.button.onClick.RemoveAllListeners();
                pooled.button.onClick.AddListener(() => HandleSlotClick(captureIndex));
            }
        }

        private static T FindChildByName<T>(GameObject root, string name, bool includeInactive = true) where T : Component
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            var arr = root.GetComponentsInChildren<T>(includeInactive);
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] != null && arr[i].name == name) return arr[i];
            return null;
        }

        private static GameObject FindChildGOByName(GameObject root, string name, bool includeInactive = true)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            var arr = root.GetComponentsInChildren<Transform>(includeInactive);
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] != null && arr[i].name == name) return arr[i].gameObject;
            return null;
        }

        private void BindSlotChildren(PooledSlot pooled, GameObject slot)
        {
            // Number frame
            pooled.imageDefault = FindChildGOByName(slot, "ImageDefault");
            pooled.imageGet = FindChildGOByName(slot, "ImageGet");
            pooled.textNumber = FindChildByName<TMP_Text>(slot, "TextNumber");
            pooled.textNumberOutline = FindChildByName<TMP_Text>(slot, "TextNumberOutline");

            // BtnReward + inner state icons
            var btnRewardGo = FindChildGOByName(slot, "BtnReward");
            pooled.button = btnRewardGo != null ? btnRewardGo.GetComponent<Button>() : null;
            if (pooled.button == null)
            {
                // 안전망 — 첫 Button 사용.
                var anyBtn = slot.GetComponentInChildren<Button>(true);
                pooled.button = anyBtn;
            }
            pooled.btnRewardImage = btnRewardGo != null ? btnRewardGo.GetComponent<Image>() : null;
            pooled.frameInner = FindChildGOByName(slot, "FrameInner")?.transform as RectTransform;
            pooled.iconCheck = FindChildGOByName(slot, "IconCheck");
            pooled.iconLock = FindChildGOByName(slot, "IconLock");

            // 상태 스프라이트 타겟 — 사용자 스펙대로 RotateLight / ImageInnerFrame / ImageArrow.
            pooled.rotateLight = FindChildGOByName(slot, "RotateLight");
            pooled.imageInnerFrame = FindChildByName<Image>(slot, "ImageInnerFrame");
            pooled.imageArrow = FindChildByName<Image>(slot, "ImageArrow");
            // RewardItem 부모 컨테이너 — FrameInner 와 동일 (별도 root 없음).
            pooled.rewardItemRoot = pooled.frameInner;

            // RewardItem 템플릿 — FrameInner 의 첫 RewardItem
            if (pooled.frameInner != null)
            {
                for (int i = 0; i < pooled.frameInner.childCount; i++)
                {
                    var child = pooled.frameInner.GetChild(i);
                    if (child.name.StartsWith("RewardItem"))
                    {
                        pooled.rewardItemTemplate = child.gameObject;
                        break;
                    }
                }
            }
            if (pooled.rewardItemTemplate != null)
            {
                pooled.rewardItems = new List<RewardItemRefs>();
                pooled.rewardItems.Add(CaptureRewardItemRefs(pooled.rewardItemTemplate));
            }
        }

        private static RewardItemRefs CaptureRewardItemRefs(GameObject item)
        {
            return new RewardItemRefs
            {
                root = item,
                icon = FindChildByName<Image>(item, "ImageRewardItem"),
                text = FindChildByName<TMP_Text>(item, "TextReward"),
                textOutline = FindChildByName<TMP_Text>(item, "TextRewardOutline")
            };
        }

        private void ApplyPoolSlotLayout()
        {
            for (int i = 0; i < _pooledSlots.Count; i++)
                ApplySlotLayout(_pooledSlots[i].root);
        }

        // 사이즈/VLG 영향 완전 차단: 900x300 고정 + ignoreLayout
        private void ApplySlotLayout(RectTransform slotRt)
        {
            if (slotRt == null) return;

            slotRt.localScale = Vector3.one;
            slotRt.anchorMin = new Vector2(0f, 1f);
            slotRt.anchorMax = new Vector2(1f, 1f);
            slotRt.pivot = new Vector2(0.5f, 1f);
            slotRt.sizeDelta = new Vector2(SlotFixedWidth, SlotFixedHeight);

            LayoutElement layoutElement = slotRt.GetComponent<LayoutElement>();
            if (layoutElement == null)
                layoutElement = slotRt.gameObject.AddComponent<LayoutElement>();

            layoutElement.minWidth = SlotFixedWidth;
            layoutElement.preferredWidth = SlotFixedWidth;
            layoutElement.minHeight = SlotFixedHeight;
            layoutElement.preferredHeight = SlotFixedHeight;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;
            layoutElement.ignoreLayout = true;
        }

        private void ClearSlotContent(GameObject template)
        {
            if (_keyBlazeContents == null) return;

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

        private void BindScrollListener()
        {
            if (_scrollRect == null || _scrollListenerBound) return;
            _scrollRect.onValueChanged.AddListener(HandleScrollValueChanged);
            _scrollListenerBound = true;
        }

        private void HandleScrollValueChanged(Vector2 _)
        {
            if (_suppressScrollCallback) return;
            RefreshVisibleSlots();
        }

        private void ResetScrollPosition()
        {
            if (_scrollRect == null || _keyBlazeContents == null || !_slotsBuilt) return;

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

        // ── 슬롯 데이터 바인딩 ────────────────────────────────────

        private void RefreshVisibleSlots()
        {
            if (_keyBlazeContents == null || _slotStrideY <= 1f || _pooledSlots.Count == 0) return;

            int dataCount = DataCount;
            int maxFirstDataIndex = Mathf.Max(0, dataCount - _pooledSlots.Count);
            float verticalNormalizedPosition = _scrollRect != null ? Mathf.Clamp01(_scrollRect.verticalNormalizedPosition) : 1f;
            float normalizedFromTop = 1f - verticalNormalizedPosition;
            int firstDataIndex = Mathf.RoundToInt(normalizedFromTop * maxFirstDataIndex);
            firstDataIndex = Mathf.Clamp(firstDataIndex, 0, maxFirstDataIndex);

            for (int poolIndex = 0; poolIndex < _pooledSlots.Count; poolIndex++)
            {
                var pooled = _pooledSlots[poolIndex];
                if (pooled?.root == null) continue;

                int dataIndex = firstDataIndex + poolIndex;
                if (dataIndex >= dataCount)
                {
                    pooled.root.gameObject.SetActive(false);
                    pooled.boundStage = -1;
                    continue;
                }

                int stage = dataCount - dataIndex;       // 위로 갈수록 높은 stage
                pooled.root.gameObject.SetActive(true);
                pooled.root.name = $"SlotWinningStreak_{stage:D2}";
                pooled.root.anchoredPosition = new Vector2(
                    pooled.root.anchoredPosition.x,
                    -(_contentTopPadding + dataIndex * _slotStrideY));

                BindSlotData(pooled, stage);
            }
        }

        private void BindSlotData(PooledSlot pooled, int stage1Based)
        {
            pooled.boundStage = stage1Based;
            EnsureStreakSprites();

            SetSlotNumber(pooled, stage1Based);

            var mgr = WinningStreakManager.HasInstance ? WinningStreakManager.Instance : null;
            var stageDoc = WinningStreakConfigService.HasInstance
                ? WinningStreakConfigService.Instance.GetStage(stage1Based)
                : null;

            SlotState slotState = ResolveSlotState(mgr, stage1Based);
            pooled.lastState = slotState;
            ApplySlotState(pooled, slotState);
            BindRewardItems(pooled, stageDoc);
        }

        private SlotState ResolveSlotState(WinningStreakManager mgr, int stage1Based)
        {
            if (mgr == null || mgr.State == null) return SlotState.Locked;
            if (mgr.IsStageClaimed(stage1Based)) return SlotState.Claimed;
            if (mgr.IsStageAchieved(stage1Based)) return SlotState.AchievedUnclaimed;
            if (stage1Based == mgr.State.currentStage) return SlotState.InProgress;
            return SlotState.Locked;
        }

        private static void SetActiveSafe(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }

        /// <summary>3가지 상태(Lock / 현재 레벨 / 완료)별 SlotWinningStreak 시각 세팅.</summary>
        private void ApplySlotState(PooledSlot pooled, SlotState state)
        {
            EnsureStreakSprites();

            switch (state)
            {
                case SlotState.Locked:
                    // Lock 상태 — RotateLight off, Arrow=일반.
                    SetActiveSafe(pooled.rotateLight, false);
                    SetSpriteSafe(pooled.imageInnerFrame, _sprFrameNumberDefault);
                    SetSpriteSafe(pooled.imageArrow, _sprArrow);
                    SetSpriteSafe(pooled.btnRewardImage, _sprSlot);
                    SetActiveSafe(pooled.iconLock, true);
                    SetActiveSafe(pooled.iconCheck, false);
                    if (pooled.button != null) pooled.button.interactable = false;
                    break;

                case SlotState.InProgress:
                case SlotState.AchievedUnclaimed:
                    // 현재 레벨 상태 — RotateLight on, Arrow=Complete. (스펙: IconLock 활성화 유지)
                    SetActiveSafe(pooled.rotateLight, true);
                    SetSpriteSafe(pooled.imageInnerFrame, _sprFrameNumberDefault);
                    SetSpriteSafe(pooled.imageArrow, _sprArrowComplete);
                    SetSpriteSafe(pooled.btnRewardImage, _sprSlot);
                    SetActiveSafe(pooled.iconCheck, false);
                    SetActiveSafe(pooled.iconLock, true);
                    if (pooled.button != null)
                        pooled.button.interactable = (state == SlotState.AchievedUnclaimed);
                    break;

                case SlotState.Claimed:
                    // 완료 상태 — Arrow/BtnReward 스프라이트 swap.
                    SetActiveSafe(pooled.rotateLight, false);
                    SetActiveSafe(pooled.iconLock, false);
                    SetActiveSafe(pooled.iconCheck, true);
                    SetSpriteSafe(pooled.imageInnerFrame, _sprFrameNumberDefault);
                    SetSpriteSafe(pooled.imageArrow, _sprSlot);
                    SetSpriteSafe(pooled.btnRewardImage, _sprArrowComplete);
                    if (pooled.button != null) pooled.button.interactable = false;
                    break;
            }
            // imageDefault/imageGet 는 ImageInnerFrame sprite 와 충돌할 수 있어 여기서 토글하지 않음.
        }

        private static void SetSpriteSafe(Image image, Sprite sprite)
        {
            if (image == null || sprite == null) return;
            image.sprite = sprite;
        }

        private void EnsureStreakSprites()
        {
            if (!ResourceManager.HasInstance) return;
            var rm = ResourceManager.Instance;
            if (_sprFrameNumberDefault == null) _sprFrameNumberDefault = rm.GetUISprite(Const.SPR_FRAMEWINNERSTREAKNUMBERDEFAULT);
            if (_sprArrow == null) _sprArrow = rm.GetUISprite(Const.SPR_FRAMEWINNINGSTREAKSLOTARROW);
            if (_sprArrowComplete == null) _sprArrowComplete = rm.GetUISprite(Const.SPR_FRAMEWINNINGSTREAKSLOTARROWCOMPLETE);
            if (_sprSlot == null) _sprSlot = rm.GetUISprite(Const.SPR_FRAMEWINNINGSTREAKSLOT);
        }

        private void SetSlotNumber(PooledSlot pooled, int number)
        {
            string s = number.ToString();
            if (pooled.textNumber != null) pooled.textNumber.text = s;
            if (pooled.textNumberOutline != null) pooled.textNumberOutline.text = s;
        }

        // ── 보상 아이템 ──────────────────────────────────────────

        private void BindRewardItems(PooledSlot pooled, WinningStreakStage stageDoc)
        {
            if (pooled.frameInner == null || pooled.rewardItemTemplate == null) return;

            // 완료 상태에서는 RewardItem 모두 비활성화 (이미 수령했으므로 표시 X).
            if (pooled.lastState == SlotState.Claimed)
            {
                if (pooled.rewardItems != null)
                {
                    for (int i = 0; i < pooled.rewardItems.Count; i++)
                    {
                        var item = pooled.rewardItems[i];
                        if (item?.root != null) item.root.SetActive(false);
                    }
                }
                return;
            }

            List<RewardEntry> rewards = BuildRewardEntries(stageDoc);
            int requiredCount = Mathf.Max(1, rewards.Count); // 빈 stage 도 템플릿 1개는 켜둠 (이상치 방지)

            EnsureRewardItemCount(pooled, requiredCount);

            for (int i = 0; i < pooled.rewardItems.Count; i++)
            {
                var item = pooled.rewardItems[i];
                if (item?.root == null) continue;

                if (i < rewards.Count)
                {
                    item.root.SetActive(true);
                    if (item.icon != null)
                    {
                        var sprite = ResolveRewardSprite(rewards[i].type);
                        if (sprite != null) item.icon.sprite = sprite;
                        item.icon.enabled = true;
                    }
                    string countText = rewards[i].count > 0 ? $"x{rewards[i].count}" : "";
                    if (item.text != null) item.text.text = countText;
                    if (item.textOutline != null) item.textOutline.text = countText;
                }
                else
                {
                    item.root.SetActive(false);
                }
            }
        }

        private void EnsureRewardItemCount(PooledSlot pooled, int requiredCount)
        {
            while (pooled.rewardItems.Count < requiredCount)
            {
                var clone = Instantiate(pooled.rewardItemTemplate, pooled.frameInner);
                clone.name = $"{pooled.rewardItemTemplate.name}_{pooled.rewardItems.Count}";
                pooled.rewardItems.Add(CaptureRewardItemRefs(clone));
            }
        }

        private static List<RewardEntry> BuildRewardEntries(WinningStreakStage stageDoc)
        {
            var list = new List<RewardEntry>(3);
            if (stageDoc == null || stageDoc.rewards == null) return list;

            var r = stageDoc.rewards;
            if (r.coins > 0) list.Add(new RewardEntry { type = RewardType.Coin, count = r.coins });
            if (r.boosters != null)
            {
                if (r.boosters.hand > 0) list.Add(new RewardEntry { type = RewardType.Hand, count = r.boosters.hand });
                if (r.boosters.shuffle > 0) list.Add(new RewardEntry { type = RewardType.Shuffle, count = r.boosters.shuffle });
                if (r.boosters.zap > 0) list.Add(new RewardEntry { type = RewardType.Zap, count = r.boosters.zap });
            }
            if (r.infiniteHeartsSeconds > 0)
                list.Add(new RewardEntry { type = RewardType.InfiniteHearts, count = r.infiniteHeartsSeconds });
            return list;
        }

        // 보상 아이콘은 항상 atlas_ui (Addressable) 에서 동적 로드 — Shop / PurchaseRewardEffect 와 동일 패턴.
        // Inspector 직접 sprite 링크는 사용하지 않음.
        private static Sprite ResolveRewardSprite(RewardType type)
        {
            if (!ResourceManager.HasInstance) return null;
            string spriteName = type switch
            {
                RewardType.Coin => Const.SPR_ICONGOLD,
                RewardType.Hand => Const.SPR_ICONHAND,
                RewardType.Shuffle => Const.SPR_ICONSUFFLE,
                RewardType.Zap => Const.SPR_ICONZAP,
                RewardType.InfiniteHearts => Const.SPR_ICONHEARINFINITE,
                _ => null
            };
            return ResourceManager.Instance.GetUISprite(spriteName);
        }

        // ── Claim 처리 ───────────────────────────────────────────

        private void HandleSlotClick(int poolIndex)
        {
            if (poolIndex < 0 || poolIndex >= _pooledSlots.Count) return;
            var pooled = _pooledSlots[poolIndex];
            if (pooled == null || pooled.boundStage <= 0) return;
            if (!WinningStreakManager.HasInstance) return;

            bool ok = WinningStreakManager.Instance.ClaimStage(pooled.boundStage);
            if (ok)
            {
                // Manager 가 OnStateChanged 발화 → HandleStateChanged 가 슬롯 재그리기.
                // 즉시 시각 갱신.
                BindSlotData(pooled, pooled.boundStage);
                RefreshHeader();
            }
        }

        // ── 내부 데이터 구조 ──────────────────────────────────────

        private enum SlotState { Locked, InProgress, AchievedUnclaimed, Claimed }

        private enum RewardType { None, Coin, Hand, Shuffle, Zap, InfiniteHearts }

        private struct RewardEntry { public RewardType type; public int count; }

        private class RewardItemRefs
        {
            public GameObject root;
            public Image icon;
            public TMP_Text text;
            public TMP_Text textOutline;
        }

        private class PooledSlot
        {
            public RectTransform root;
            public TMP_Text textNumber;
            public TMP_Text textNumberOutline;
            public GameObject imageDefault;
            public GameObject imageGet;
            public GameObject iconCheck;
            public GameObject iconLock;
            public Button button;
            public RectTransform frameInner;
            public GameObject rewardItemTemplate;
            public List<RewardItemRefs> rewardItems;
            public int boundStage = -1;
            public GameObject rotateLight;
            public Image imageInnerFrame;
            public Image imageArrow;
            public Image btnRewardImage;
            public Transform rewardItemRoot;
            public SlotState lastState = SlotState.Locked;
        }
    }
}
