using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// First Time User Experience (FTUE) visual manager.
    /// Listens to OnTutorialStepChanged and drives all tutorial UI elements:
    /// 4-panel cutout dim overlay, highlight frame, directional arrow, and instruction panel.
    /// Auto-creates all UI in code — no prefab dependencies.
    /// Contains no game logic — purely cosmetic guidance layer.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Manager | Phase: 2
    /// DB Reference: No DB match — generated from logicFlow (ux_pages_tutorial)
    /// requires: UIManager (overlay management via auto-created canvas)
    /// </remarks>
    public class TutorialManager : SceneSingleton<TutorialManager>
    {
        #region Constants

        private const float DIM_ALPHA = 0.75f;
        private const float FADE_DURATION = 0.25f;
        private const float ARROW_BOB_AMPLITUDE = 10f;
        private const float ARROW_BOB_FREQUENCY = 2f;
        private const float CUTOUT_PADDING = 20f;
        private const float FRAME_THICKNESS = 4f;
        private const int CANVAS_SORT_ORDER = 200;
        private static readonly Color DIM_COLOR = new Color(0f, 0f, 0f, DIM_ALPHA);
        private static readonly Color FRAME_COLOR = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color INSTRUCTION_BG_COLOR = new Color(0.1f, 0.1f, 0.15f, 0.92f);

        #endregion

        #region Fields

        // Root
        private GameObject _tutorialCanvas;
        private Canvas _canvas;
        private RectTransform _canvasRect;

        // Spotlight dim (단일 패널 + 스포트라이트 셰이더)
        private Image _spotlightImage;
        private Material _spotlightMat;
        private static readonly int _propCenter = Shader.PropertyToID("_Center");
        private static readonly int _propRadius = Shader.PropertyToID("_Radius");
        private static readonly int _propSoftness = Shader.PropertyToID("_Softness");

        // Legacy 4-panel references (PopupTutorial 프리팹 호환)
        private RectTransform _dimTop;
        private RectTransform _dimBottom;
        private RectTransform _dimLeft;
        private RectTransform _dimRight;
        private Image _dimTopImage;
        private Image _dimBottomImage;
        private Image _dimLeftImage;
        private Image _dimRightImage;
        private RectTransform _cutoutFrame;
        private Image _cutoutFrameImage;
        /// <summary>CutoutFrame 자식의 dim overlay (frame 영역 "밖"에만 그려짐).</summary>
        private Image _cutoutDimImage;

        // Arrow indicator
        private RectTransform _arrowIndicator;
        private Image _arrowImage;

        // Instruction panel
        private GameObject _instructionPanel;
        private RectTransform _instructionPanelRect;
        private TextMeshProUGUI _instructionText;
        private Button _skipButton;

        // Tap-anywhere overlay (invisible button that covers the cutout hole area)
        private Button _tapAnywhereButton;
        private GameObject _tapAnywhereGO;

        // State
        private Coroutine _fadeDimCoroutine;
        private Coroutine _arrowBobCoroutine;
        private bool _isDimActive;
        private bool _isCutoutVisible;

        // Tutorial prefab root canvas/canvasgroup/raycaster — 튜토리얼 active 시에만 raycast 인터셉트.
        private Canvas _prefabRootCanvas;
        private CanvasGroup _prefabRootCanvasGroup;
        private UnityEngine.UI.GraphicRaycaster _prefabRootRaycaster;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            LoadOrCreateTutorialUI();
            HideAllVisuals();
        }

        /// <summary>UIManager.OpenUI 통해 PopupCanvas(sortingOrder=10)에 instantiate.
        /// 직접 Resources.Load + Instantiate 시 SceneCanvas(sortingOrder=0)에 parent돼 다른 popup에 가려지는 문제 해결.</summary>
        private void LoadOrCreateTutorialUI()
        {
            if (UIManager.HasInstance)
            {
                var popup = UIManager.Instance.OpenUI<PopupTutorial>("Popup/Tutorial");
                if (popup != null)
                {
                    Debug.Log("[TutorialDbg] LoadOrCreateTutorialUI via UIManager.OpenUI OK");
                    BindFromPopup(popup.gameObject);
                    // 직후 닫기 — TutorialController.StartTutorial 호출 시 다시 활성화됨.
                    // (HideAllVisuals가 dim/cutout/instruction 개별 비활성화 처리)
                    return;
                }
                Debug.LogWarning("[TutorialManager] UIManager.OpenUI returned null for Popup/Tutorial.");
            }

            // Fallback: 코드로 직접 생성
            Debug.Log("[TutorialDbg] LoadOrCreateTutorialUI fallback to CreateTutorialUI");
            CreateTutorialUI();
        }

        /// <summary>씬에서 기존 Canvas 찾기.</summary>
        private Canvas FindSceneCanvas()
        {
            // UIManager의 Canvas 우선
            if (UIManager.HasInstance)
            {
                var canvas = UIManager.Instance.GetComponentInChildren<Canvas>();
                if (canvas != null) return canvas;
            }
            // SceneCanvas 찾기
            var all = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (var c in all)
                if (c.renderMode == RenderMode.ScreenSpaceOverlay) return c;
            return null;
        }

        /// <summary>PopupTutorial 컴포넌트에서 SerializeField 바인딩.</summary>
        private void BindFromPopup(GameObject root)
        {
            _tutorialCanvas = root;
            _canvasRect = root.GetComponent<RectTransform>();

            // 부모 Canvas 참조
            _canvas = root.GetComponentInParent<Canvas>();

            // 프리팹의 Canvas/Raycaster 참조만 보관. raycast 인터셉트는 튜토리얼이 실제 active 일 때만.
            // (평소엔 비활성 → Tutorial canvas 가 HUD 아이템 클릭 가로채는 부작용 방지.)
            _prefabRootCanvas = root.GetComponent<Canvas>();
            _prefabRootCanvasGroup = root.GetComponent<CanvasGroup>();
            _prefabRootRaycaster = root.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            SetTutorialCanvasInteractive(false);
            Debug.Log($"[TutorialDbg] BindFromPopup done. parentCanvas={(_canvas != null ? _canvas.name : "NULL")} " +
                      $"sortingOrder={(_canvas != null ? _canvas.sortingOrder : -1)} " +
                      $"rectSize={(_canvasRect != null ? _canvasRect.sizeDelta.ToString() : "NULL")}");

            var popup = root.GetComponent<PopupTutorial>();
            if (popup != null)
            {
                _dimTop = popup.DimTop;
                _dimBottom = popup.DimBottom;
                _dimLeft = popup.DimLeft;
                _dimRight = popup.DimRight;
                _dimTopImage = _dimTop?.GetComponent<Image>();
                _dimBottomImage = _dimBottom?.GetComponent<Image>();
                _dimLeftImage = _dimLeft?.GetComponent<Image>();
                _dimRightImage = _dimRight?.GetComponent<Image>();

                _cutoutFrame = popup.CutoutFrame;
                _cutoutFrameImage = _cutoutFrame?.GetComponent<Image>();

                // CutoutFrame 에 CutoutMaskUI + Mask 부착 → 자식 DimOverlay 가 frame 영역 "밖"에만 그려져 펀칭 효과.
                // 기존 4-panel(DimTop/Bottom/Left/Right) 접근은 이 단일 mask 로 대체.
                SetupCutoutMaskOnFrame();

                _arrowIndicator = popup.ArrowIndicator;
                _arrowImage = _arrowIndicator?.GetComponent<Image>();

                _instructionText = popup.InstructionText;
                // Prefab에서 InstructionPanel이 명시 지정되어 있으면 우선 사용 (디자이너가 위치 이동 가능).
                // 없으면 기존처럼 InstructionText의 parent로 폴백.
                _instructionPanelRect = popup.InstructionPanel;
                if (_instructionPanelRect == null && _instructionText != null)
                    _instructionPanelRect = _instructionText.transform.parent as RectTransform;
                _instructionPanel = _instructionPanelRect != null ? _instructionPanelRect.gameObject : null;

                _skipButton = popup.SkipButton;
                if (_skipButton != null)
                    _skipButton.onClick.AddListener(() =>
                    {
                        if (TutorialController.HasInstance) TutorialController.Instance.SkipTutorial();
                    });

                _tapAnywhereButton = popup.TapAnywhereButton;
                _tapAnywhereGO = _tapAnywhereButton?.gameObject;
                if (_tapAnywhereButton != null)
                    _tapAnywhereButton.onClick.AddListener(() =>
                    {
                        if (TutorialController.HasInstance) TutorialController.Instance.AdvanceStep();
                    });
            }
            else
            {
                Debug.LogWarning("[TutorialManager] PopupTutorial component not found on prefab.");
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnTutorialStepChanged>(HandleTutorialStepChanged);
            EventBus.Subscribe<OnTutorialCompleted>(HandleTutorialCompleted);
            EventBus.Subscribe<OnTutorialStarted>(HandleTutorialStarted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnTutorialStepChanged>(HandleTutorialStepChanged);
            EventBus.Unsubscribe<OnTutorialCompleted>(HandleTutorialCompleted);
            EventBus.Unsubscribe<OnTutorialStarted>(HandleTutorialStarted);
        }

        protected override void OnDestroy()
        {
            // BindFromPopup / 재바인딩 시 lambda 누적 leak 방지.
            if (_skipButton != null) _skipButton.onClick.RemoveAllListeners();
            if (_tapAnywhereButton != null) _tapAnywhereButton.onClick.RemoveAllListeners();
            base.OnDestroy();
        }

        #endregion

        #region Public Methods — Cutout

        /// <summary>
        /// Shows the cutout (transparent hole) at the given world position with the given size.
        /// The 4 dim panels are positioned around the hole.
        /// </summary>
        public void ShowCutout(Vector3 worldPos, Vector2 size)
        {
            Camera cam = Camera.main;
            if (cam == null || _canvasRect == null) return;

            Vector2 canvasPos = WorldToCanvasPosition(worldPos);
            ApplyCutout(canvasPos, size);
        }

        /// <summary>
        /// Shows the cutout around the given RectTransform target.
        /// </summary>
        public void ShowCutout(RectTransform target)
        {
            if (target == null || _canvasRect == null) return;

            // Get target corners in world space, then convert to our canvas space
            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);

            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);

            for (int i = 0; i < 4; i++)
            {
                Vector2 canvasPoint = WorldUIToCanvasPosition(corners[i]);
                min = Vector2.Min(min, canvasPoint);
                max = Vector2.Max(max, canvasPoint);
            }

            Vector2 center = (min + max) * 0.5f;
            Vector2 size = (max - min) + new Vector2(CUTOUT_PADDING, CUTOUT_PADDING);
            ApplyCutout(center, size);
        }

        /// <summary>
        /// Shows the cutout around a specific holder by index.
        /// Finds the holder's world position from HolderVisualManager.
        /// </summary>
        public void ShowCutoutForHolder(int holderIndex)
        {
            if (!HolderManager.HasInstance) return;

            HolderData[] holders = HolderManager.Instance.GetHolders();
            if (holders == null || holderIndex < 0 || holderIndex >= holders.Length) return;

            // Use HolderVisualManager to find the visual position
            if (HolderVisualManager.HasInstance)
            {
                Vector3 queueCenter = HolderVisualManager.Instance.CalculateQueueCenterPosition();
                // Approximate holder position — offset from queue center based on column
                int column = holders[holderIndex].column;
                float columnSpacing = 2.0f; // approximate spacing
                int totalColumns = HolderManager.Instance.QueueColumns;
                float xOffset = (column - (totalColumns - 1) * 0.5f) * columnSpacing;
                Vector3 holderWorldPos = new Vector3(queueCenter.x + xOffset, queueCenter.y, queueCenter.z);

                ShowCutout(holderWorldPos, new Vector2(200f, 200f));
            }
            else
            {
                // Fallback: show cutout at screen center bottom area
                Vector2 canvasSize = _canvasRect.sizeDelta;
                ApplyCutout(new Vector2(canvasSize.x * 0.5f, canvasSize.y * 0.25f), new Vector2(250f, 250f));
            }
        }

        /// <summary>
        /// Shows the cutout around the board area (center of screen, larger region).
        /// </summary>
        public void ShowCutoutForBoard()
        {
            if (_canvasRect == null) return;

            Vector2 canvasSize = _canvasRect.sizeDelta;
            // Board is typically in the upper-center area of the screen
            Vector2 boardCenter = new Vector2(canvasSize.x * 0.5f, canvasSize.y * 0.6f);
            Vector2 boardSize = new Vector2(canvasSize.x * 0.8f, canvasSize.y * 0.4f);

            ApplyCutout(boardCenter, boardSize);
        }

        /// <summary>
        /// Hides the cutout and all dim panels.
        /// </summary>
        public void HideCutout()
        {
            _isCutoutVisible = false;
            if (_spotlightImage != null) _spotlightImage.gameObject.SetActive(false);
            SetDimPanelsActive(false);

            if (_cutoutFrame != null)
                _cutoutFrame.gameObject.SetActive(false);

            if (_arrowIndicator != null)
                _arrowIndicator.gameObject.SetActive(false);
        }

        #endregion

        #region Public Methods — Instruction

        /// <summary>
        /// Shows the instruction panel with the given text.
        /// </summary>
        public void ShowInstruction(string text)
        {
            if (_instructionPanel != null)
                _instructionPanel.SetActive(true);

            if (_instructionText != null)
                _instructionText.text = text ?? string.Empty;
        }

        /// <summary>
        /// Hides the instruction panel.
        /// </summary>
        public void HideInstruction()
        {
            if (_instructionPanel != null)
                _instructionPanel.SetActive(false);
        }

        #endregion

        #region Public Methods — Arrow

        /// <summary>
        /// Shows the arrow indicator at the given canvas position pointing in direction.
        /// </summary>
        public void ShowArrow(Vector2 canvasPosition, Vector2 direction)
        {
            if (_arrowIndicator == null) return;

            _arrowIndicator.gameObject.SetActive(true);
            _arrowIndicator.anchoredPosition = canvasPosition;

            if (direction != Vector2.zero)
            {
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                _arrowIndicator.localEulerAngles = new Vector3(0f, 0f, angle - 90f);
            }

            if (_arrowBobCoroutine != null)
                StopCoroutine(_arrowBobCoroutine);
            _arrowBobCoroutine = StartCoroutine(BobArrowCoroutine(canvasPosition, direction));
        }

        /// <summary>
        /// Hides the arrow indicator.
        /// </summary>
        public void HideArrow()
        {
            if (_arrowBobCoroutine != null)
            {
                StopCoroutine(_arrowBobCoroutine);
                _arrowBobCoroutine = null;
            }

            if (_arrowIndicator != null)
                _arrowIndicator.gameObject.SetActive(false);
        }

        #endregion

        #region Public Methods — Dim Overlay

        /// <summary>
        /// Fades the dim panels in or out.
        /// </summary>
        public void SetDimOverlay(bool active)
        {
            _isDimActive = active;

            if (_fadeDimCoroutine != null)
                StopCoroutine(_fadeDimCoroutine);

            float targetAlpha = active ? DIM_ALPHA : 0f;
            _fadeDimCoroutine = StartCoroutine(FadeDimCoroutine(targetAlpha));
        }

        #endregion

        #region Public Methods — Tap Anywhere

        /// <summary>
        /// Enables or disables the "tap anywhere to continue" overlay.
        /// </summary>
        public void SetTapAnywherEnabled(bool enabled)
        {
            if (_tapAnywhereGO != null)
                _tapAnywhereGO.SetActive(enabled);
        }

        #endregion

        #region Private Methods — UI Creation

        private void CreateTutorialUI()
        {
            // Create Canvas
            var canvasGO = new GameObject("TutorialCanvas");
            canvasGO.transform.SetParent(transform);

            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = CANVAS_SORT_ORDER;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1242f, 2688f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            _canvasRect = canvasGO.GetComponent<RectTransform>();
            _tutorialCanvas = canvasGO;

            // 스포트라이트 패널 (단일 + 셰이더)
            var spotShader = Shader.Find("UI/TutorialSpotlight");
            if (spotShader != null)
            {
                var spotGO = new GameObject("SpotlightDim");
                spotGO.transform.SetParent(canvasGO.transform, false);
                _spotlightImage = spotGO.AddComponent<Image>();
                _spotlightMat = new Material(spotShader);
                _spotlightImage.material = _spotlightMat;
                _spotlightImage.color = Color.white;
                _spotlightImage.raycastTarget = true;
                var spotRT = spotGO.GetComponent<RectTransform>();
                spotRT.anchorMin = Vector2.zero;
                spotRT.anchorMax = Vector2.one;
                spotRT.offsetMin = Vector2.zero;
                spotRT.offsetMax = Vector2.zero;
                // 기본: 구멍 없이 전체 어둡게
                _spotlightMat.SetVector(_propCenter, new Vector4(0.5f, 0.5f, 0, 0));
                _spotlightMat.SetVector(_propRadius, new Vector4(0, 0, 0, 0));
            }

            // Fallback: 4 dim panels (스포트라이트 셰이더 없을 때)
            _dimTop = CreateDimPanel("DimTop", canvasGO.transform, out _dimTopImage);
            _dimBottom = CreateDimPanel("DimBottom", canvasGO.transform, out _dimBottomImage);
            _dimLeft = CreateDimPanel("DimLeft", canvasGO.transform, out _dimLeftImage);
            _dimRight = CreateDimPanel("DimRight", canvasGO.transform, out _dimRightImage);

            // Create tap-anywhere overlay (sits between dim panels and instruction)
            CreateTapAnywhereOverlay(canvasGO.transform);

            // Create cutout frame (outline)
            _cutoutFrame = CreateCutoutFrame(canvasGO.transform);

            // Create arrow indicator
            _arrowIndicator = CreateArrowIndicator(canvasGO.transform);

            // Create instruction panel at bottom
            _instructionPanel = CreateInstructionPanel(canvasGO.transform);
        }

        private RectTransform CreateDimPanel(string name, Transform parent, out Image image)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            // Default: stretch to fill (will be repositioned by ApplyCutout)
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0f, 0f);

            image = go.AddComponent<Image>();
            image.color = DIM_COLOR;
            image.raycastTarget = true;

            return rect;
        }

        private void CreateTapAnywhereOverlay(Transform parent)
        {
            _tapAnywhereGO = new GameObject("TapAnywhereOverlay");
            _tapAnywhereGO.transform.SetParent(parent, false);

            var rect = _tapAnywhereGO.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = _tapAnywhereGO.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f); // fully transparent but catches raycasts
            image.raycastTarget = true;

            _tapAnywhereButton = _tapAnywhereGO.AddComponent<Button>();
            _tapAnywhereButton.transition = Selectable.Transition.None;
            _tapAnywhereButton.onClick.AddListener(OnTapAnywhere);

            _tapAnywhereGO.SetActive(false);
        }

        /// <summary>
        /// CutoutFrame 에 CutoutMaskUI + Mask 부착 후 자식 DimOverlay 추가 — frame 영역 "밖" 만 dim 처리.
        /// 기존 4-panel(DimTop/Bottom/Left/Right) 은 비활성화. ShowCutoutForHolder 가 _cutoutFrame 사이즈/위치 변경하면
        /// 자식 dim 도 함께 따라가므로 간단.
        /// </summary>
        private void SetupCutoutMaskOnFrame()
        {
            if (_cutoutFrame == null) return;

            // 기존 Image 가 일반 Image 면 CutoutMaskUI 로 교체
            var existingImage = _cutoutFrame.GetComponent<Image>();
            CutoutMaskUI cutout = _cutoutFrame.GetComponent<CutoutMaskUI>();
            if (cutout == null)
            {
                if (existingImage != null && !(existingImage is CutoutMaskUI))
                    DestroyImmediate(existingImage);
                cutout = _cutoutFrame.gameObject.AddComponent<CutoutMaskUI>();
            }
            _cutoutFrameImage = cutout;
            // 메시 보장용 흰색 sprite (스텐실 wrtie 가능)
            if (cutout.sprite == null)
            {
                var tex = new Texture2D(4, 4);
                var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white;
                tex.SetPixels(px); tex.Apply();
                cutout.sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            }
            cutout.type = Image.Type.Simple;
            cutout.color = new Color(1f, 1f, 1f, 0f); // 본체는 안 보임 — frame 표시는 Outline 컴포넌트가 담당
            cutout.raycastTarget = false;

            // Mask 컴포넌트 — showMaskGraphic=true 로 base graphic 렌더 허용 (Outline 컴포넌트의 frame 표시).
            // CutoutMaskUI 의 color.alpha=0 이라 본체는 안 보이고 Outline 만 보임.
            var mask = _cutoutFrame.GetComponent<Mask>();
            if (mask == null) mask = _cutoutFrame.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            // 자식 DimOverlay — 부모 frame 영역 "밖" 만 그려짐 (CutoutMaskUI 가 stencil-invert 수행)
            Transform existingDim = _cutoutFrame.Find("DimOverlay");
            GameObject dimGO;
            if (existingDim != null)
            {
                dimGO = existingDim.gameObject;
                _cutoutDimImage = dimGO.GetComponent<Image>();
                if (_cutoutDimImage == null) _cutoutDimImage = dimGO.AddComponent<Image>();
            }
            else
            {
                dimGO = new GameObject("DimOverlay", typeof(RectTransform), typeof(Image));
                dimGO.transform.SetParent(_cutoutFrame, false);
                _cutoutDimImage = dimGO.GetComponent<Image>();
            }
            var dimRT = dimGO.GetComponent<RectTransform>();
            dimRT.anchorMin = new Vector2(0.5f, 0.5f);
            dimRT.anchorMax = new Vector2(0.5f, 0.5f);
            dimRT.pivot     = new Vector2(0.5f, 0.5f);
            dimRT.anchoredPosition = Vector2.zero;
            dimRT.sizeDelta = new Vector2(10000f, 10000f);
            _cutoutDimImage.color = new Color(0f, 0f, 0f, 0f); // 알파는 SetDimColor 로 페이드
            _cutoutDimImage.raycastTarget = true; // dim 영역 클릭 차단 — 튜토리얼이 frame 외부 입력 가로챔

            // 기존 4-panel dim 은 비활성화 — 단일 mask 로 대체
            if (_dimTop != null) _dimTop.gameObject.SetActive(false);
            if (_dimBottom != null) _dimBottom.gameObject.SetActive(false);
            if (_dimLeft != null) _dimLeft.gameObject.SetActive(false);
            if (_dimRight != null) _dimRight.gameObject.SetActive(false);
        }

        private RectTransform CreateCutoutFrame(Transform parent)
        {
            var go = new GameObject("CutoutFrame");
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);

            // Use an Outline component on a transparent image to create the frame effect
            _cutoutFrameImage = go.AddComponent<Image>();
            _cutoutFrameImage.color = new Color(1f, 1f, 1f, 0f); // transparent fill
            _cutoutFrameImage.raycastTarget = false;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = FRAME_COLOR;
            outline.effectDistance = new Vector2(FRAME_THICKNESS, FRAME_THICKNESS);

            return rect;
        }

        private RectTransform CreateArrowIndicator(Transform parent)
        {
            var go = new GameObject("ArrowIndicator");
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(60f, 60f);

            _arrowImage = go.AddComponent<Image>();
            _arrowImage.color = Color.white;
            _arrowImage.raycastTarget = false;

            // Create a simple triangle arrow using a child with a rotated square
            // (Since we have no sprite, we use a white square rotated 45 degrees as a diamond/arrow)
            var arrowHead = new GameObject("ArrowHead");
            arrowHead.transform.SetParent(go.transform, false);

            var arrowRect = arrowHead.AddComponent<RectTransform>();
            arrowRect.anchorMin = new Vector2(0.5f, 0.5f);
            arrowRect.anchorMax = new Vector2(0.5f, 0.5f);
            arrowRect.pivot = new Vector2(0.5f, 0.5f);
            arrowRect.sizeDelta = new Vector2(30f, 30f);
            arrowRect.localEulerAngles = new Vector3(0f, 0f, 45f);

            var arrowHeadImage = arrowHead.AddComponent<Image>();
            arrowHeadImage.color = FRAME_COLOR;
            arrowHeadImage.raycastTarget = false;

            // Hide the parent image (just a container)
            _arrowImage.color = new Color(0f, 0f, 0f, 0f);

            return rect;
        }

        private GameObject CreateInstructionPanel(Transform parent)
        {
            // Panel background
            var panelGO = new GameObject("InstructionPanel");
            panelGO.transform.SetParent(parent, false);

            _instructionPanelRect = panelGO.AddComponent<RectTransform>();
            _instructionPanelRect.anchorMin = new Vector2(0f, 0f);
            _instructionPanelRect.anchorMax = new Vector2(1f, 0f);
            _instructionPanelRect.pivot = new Vector2(0.5f, 0f);
            _instructionPanelRect.anchoredPosition = new Vector2(0f, 40f);
            _instructionPanelRect.sizeDelta = new Vector2(-60f, 200f); // inset 30px on each side

            var panelImage = panelGO.AddComponent<Image>();
            panelImage.color = INSTRUCTION_BG_COLOR;
            panelImage.raycastTarget = true;

            // Round corners via outline
            var panelOutline = panelGO.AddComponent<Outline>();
            panelOutline.effectColor = new Color(1f, 1f, 1f, 0.3f);
            panelOutline.effectDistance = new Vector2(2f, 2f);

            // Instruction text (TMPro)
            var textGO = new GameObject("InstructionText");
            textGO.transform.SetParent(panelGO.transform, false);

            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 0.25f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.offsetMin = new Vector2(30f, 10f);
            textRect.offsetMax = new Vector2(-30f, -15f);

            _instructionText = textGO.AddComponent<TextMeshProUGUI>();
            _instructionText.text = string.Empty;
            _instructionText.fontSize = 36f;
            _instructionText.color = Color.white;
            _instructionText.alignment = TextAlignmentOptions.Center;
            _instructionText.enableWordWrapping = true;
            _instructionText.overflowMode = TextOverflowModes.Ellipsis;
            _instructionText.raycastTarget = false;

            // Skip button
            var skipGO = new GameObject("SkipButton");
            skipGO.transform.SetParent(panelGO.transform, false);

            var skipRect = skipGO.AddComponent<RectTransform>();
            skipRect.anchorMin = new Vector2(0.5f, 0f);
            skipRect.anchorMax = new Vector2(0.5f, 0f);
            skipRect.pivot = new Vector2(0.5f, 0f);
            skipRect.anchoredPosition = new Vector2(0f, 10f);
            skipRect.sizeDelta = new Vector2(200f, 50f);

            var skipImage = skipGO.AddComponent<Image>();
            skipImage.color = new Color(0.3f, 0.3f, 0.35f, 0.8f);
            skipImage.raycastTarget = true;

            _skipButton = skipGO.AddComponent<Button>();
            _skipButton.targetGraphic = skipImage;
            _skipButton.onClick.AddListener(OnSkipPressed);

            // Skip button text
            var skipTextGO = new GameObject("SkipText");
            skipTextGO.transform.SetParent(skipGO.transform, false);

            var skipTextRect = skipTextGO.AddComponent<RectTransform>();
            skipTextRect.anchorMin = Vector2.zero;
            skipTextRect.anchorMax = Vector2.one;
            skipTextRect.offsetMin = Vector2.zero;
            skipTextRect.offsetMax = Vector2.zero;

            var skipText = skipTextGO.AddComponent<TextMeshProUGUI>();
            skipText.text = "SKIP";
            skipText.fontSize = 28f;
            skipText.color = new Color(0.9f, 0.9f, 0.9f, 1f);
            skipText.alignment = TextAlignmentOptions.Center;
            skipText.raycastTarget = false;

            return panelGO;
        }

        #endregion

        #region Private Methods — Event Handlers

        private void HandleTutorialStarted(OnTutorialStarted evt)
        {
            Debug.Log($"[TutorialDbg] HandleTutorialStarted tutorialId={evt.tutorialId} " +
                      $"canvasActive={(_tutorialCanvas != null ? _tutorialCanvas.activeInHierarchy : false)} " +
                      $"instructionPanel={_instructionPanel != null} instructionText={_instructionText != null}");
            SetTutorialCanvasInteractive(true);
            SetDimOverlay(true);
        }

        private void HandleTutorialStepChanged(OnTutorialStepChanged evt)
        {
            Debug.Log($"[TutorialDbg] HandleTutorialStepChanged step={evt.stepIndex} instr='{evt.instruction}'");
            string highlightTarget = string.Empty;
            string requireAction = string.Empty;

            if (TutorialController.HasInstance)
            {
                TutorialStep step = TutorialController.Instance.GetCurrentStep();
                if (step != null)
                {
                    highlightTarget = step.highlightTarget ?? string.Empty;
                    requireAction = step.requireAction ?? string.Empty;
                }
            }

            // Show cutout based on target type
            if (!string.IsNullOrEmpty(highlightTarget))
            {
                if (highlightTarget.StartsWith("holder_"))
                {
                    string indexStr = highlightTarget.Substring("holder_".Length);
                    if (int.TryParse(indexStr, out int holderIndex))
                    {
                        ShowCutoutForHolder(holderIndex);

                        // Position arrow above the cutout
                        if (_isCutoutVisible && _cutoutFrame != null)
                        {
                            Vector2 arrowPos = _cutoutFrame.anchoredPosition + new Vector2(0f, _cutoutFrame.sizeDelta.y * 0.5f + 40f);
                            ShowArrow(arrowPos, Vector2.down);
                        }
                    }
                    else
                    {
                        HideCutout();
                        HideArrow();
                    }
                }
                else if (highlightTarget == "board" || highlightTarget == "holder_queue")
                {
                    ShowCutoutForBoard();
                    HideArrow();
                }
                else if (highlightTarget.StartsWith("gimmick_"))
                {
                    // Gimmick targets — show board-level cutout as fallback
                    ShowCutoutForBoard();
                    HideArrow();
                }
                else
                {
                    HideCutout();
                    HideArrow();
                }
            }
            else
            {
                HideCutout();
                HideArrow();
            }

            // Show instruction
            ShowInstruction(evt.instruction);

            // Handle tap_anywhere action
            bool isTapAnywhere = requireAction == "tap_anywhere";
            SetTapAnywherEnabled(isTapAnywhere);
        }

        private void HandleTutorialCompleted(OnTutorialCompleted evt)
        {
            HideAllVisuals();
            SetTutorialCanvasInteractive(false);
        }

        /// <summary>Tutorial canvas의 sortingOrder + raycast 인터셉트 토글. 튜토리얼 active 일 때만 ON.
        /// OFF 시: 다른 UI(HUD 아이템 등) 클릭이 Tutorial canvas에 가로채이지 않음.</summary>
        private void SetTutorialCanvasInteractive(bool active)
        {
            if (_prefabRootCanvas != null)
            {
                _prefabRootCanvas.overrideSorting = active;
                if (active) _prefabRootCanvas.sortingOrder = CANVAS_SORT_ORDER;
            }
            if (_prefabRootCanvasGroup != null)
            {
                _prefabRootCanvasGroup.blocksRaycasts = active;
                _prefabRootCanvasGroup.interactable = active;
                _prefabRootCanvasGroup.alpha = active ? 1f : 0f;
            }
            if (_prefabRootRaycaster != null)
            {
                // GraphicRaycaster.enabled 토글 — 비활성 시 Tutorial canvas 자체로 raycast 안 들어감.
                _prefabRootRaycaster.enabled = active;
            }
        }

        #endregion

        #region Private Methods — Cutout Positioning

        /// <summary>
        /// Positions the 4 dim panels around the cutout hole defined by center and size in canvas space.
        /// Canvas space: (0,0) at bottom-left, (canvasWidth, canvasHeight) at top-right.
        /// </summary>
        private void ApplyCutout(Vector2 center, Vector2 size)
        {
            _isCutoutVisible = true;

            // 1순위: CutoutFrame + CutoutMaskUI (자식 dim 이 frame 영역 밖만 그림 — 표준 hole-in-UI 패턴).
            //         frame 위치/크기 변경만으로 hole 위치 변경 됨 — ApplyCutout 종료 후 호출자에서 처리.
            if (_cutoutDimImage != null && _cutoutFrame != null)
            {
                if (_spotlightImage != null) _spotlightImage.gameObject.SetActive(false);
                SetDimPanelsActive(false);
                return;
            }

            // 스포트라이트 셰이더 방식 (부드러운 원형 투명 영역)
            if (_spotlightMat != null && _spotlightImage != null)
            {
                _spotlightImage.gameObject.SetActive(true);
                SetDimPanelsActive(false); // 4패널 사용 안 함

                Vector2 canvasSize = _canvasRect.sizeDelta;
                // UV 좌표로 변환 (0~1)
                Vector2 uvCenter = new Vector2(center.x / canvasSize.x, center.y / canvasSize.y);
                Vector2 uvRadius = new Vector2(
                    (size.x * 0.5f + CUTOUT_PADDING) / canvasSize.x,
                    (size.y * 0.5f + CUTOUT_PADDING) / canvasSize.y);

                _spotlightMat.SetVector(_propCenter, new Vector4(uvCenter.x, uvCenter.y, 0, 0));
                _spotlightMat.SetVector(_propRadius, new Vector4(uvRadius.x, uvRadius.y, 0, 0));
                _spotlightMat.SetFloat(_propSoftness, 0.15f);
            }
            else
            {
                // Fallback: 4패널 컷아웃
                SetDimPanelsActive(true);

                Vector2 canvasSize = _canvasRect.sizeDelta;
                float canvasW = canvasSize.x;
                float canvasH = canvasSize.y;
                float cutLeft = Mathf.Max(0f, center.x - size.x * 0.5f);
                float cutRight = Mathf.Min(canvasW, center.x + size.x * 0.5f);
                float cutBottom = Mathf.Max(0f, center.y - size.y * 0.5f);
                float cutTop = Mathf.Min(canvasH, center.y + size.y * 0.5f);

                if (_dimTop != null) { _dimTop.anchoredPosition = new Vector2(0f, cutTop); _dimTop.sizeDelta = new Vector2(canvasW, canvasH - cutTop); }
                if (_dimBottom != null) { _dimBottom.anchoredPosition = Vector2.zero; _dimBottom.sizeDelta = new Vector2(canvasW, cutBottom); }
                if (_dimLeft != null) { _dimLeft.anchoredPosition = new Vector2(0f, cutBottom); _dimLeft.sizeDelta = new Vector2(cutLeft, cutTop - cutBottom); }
                if (_dimRight != null) { _dimRight.anchoredPosition = new Vector2(cutRight, cutBottom); _dimRight.sizeDelta = new Vector2(canvasW - cutRight, cutTop - cutBottom); }
            }

            if (_cutoutFrame != null)
            {
                _cutoutFrame.gameObject.SetActive(true);
                _cutoutFrame.anchoredPosition = center;
                _cutoutFrame.sizeDelta = size;
            }
        }

        private void SetDimPanelsActive(bool active)
        {
            if (_dimTop != null) _dimTop.gameObject.SetActive(active);
            if (_dimBottom != null) _dimBottom.gameObject.SetActive(active);
            if (_dimLeft != null) _dimLeft.gameObject.SetActive(active);
            if (_dimRight != null) _dimRight.gameObject.SetActive(active);
        }

        /// <summary>
        /// Converts a world position to canvas position (in canvas space coordinates).
        /// Canvas coordinates: (0,0) at bottom-left, (canvasWidth, canvasHeight) at top-right.
        /// </summary>
        private Vector2 WorldToCanvasPosition(Vector3 worldPos)
        {
            Camera cam = Camera.main;
            if (cam == null) return Vector2.zero;

            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
            Vector2 canvasSize = _canvasRect.sizeDelta;

            // Screen to canvas ratio
            float ratioX = canvasSize.x / Screen.width;
            float ratioY = canvasSize.y / Screen.height;

            return new Vector2(screenPos.x * ratioX, screenPos.y * ratioY);
        }

        /// <summary>
        /// Converts a world-space UI point to canvas position.
        /// Used for converting RectTransform world corners.
        /// </summary>
        private Vector2 WorldUIToCanvasPosition(Vector3 worldUIPos)
        {
            // For ScreenSpaceOverlay canvas, world corners are in screen space
            Vector2 canvasSize = _canvasRect.sizeDelta;
            float ratioX = canvasSize.x / Screen.width;
            float ratioY = canvasSize.y / Screen.height;

            return new Vector2(worldUIPos.x * ratioX, worldUIPos.y * ratioY);
        }

        #endregion

        #region Private Methods — Utilities

        private void HideAllVisuals()
        {
            HideCutout();
            HideArrow();
            HideInstruction();
            SetTapAnywherEnabled(false);

            _isDimActive = false;

            // Ensure dim panels have zero alpha
            SetDimColor(0f);
        }

        private void SetDimColor(float alpha)
        {
            Color c = new Color(0f, 0f, 0f, alpha);
            // CutoutMaskUI 자식 dim 이 새 표준 — 기존 4-panel 은 호환성 위해 유지
            if (_cutoutDimImage != null) _cutoutDimImage.color = c;
            if (_dimTopImage != null) _dimTopImage.color = c;
            if (_dimBottomImage != null) _dimBottomImage.color = c;
            if (_dimLeftImage != null) _dimLeftImage.color = c;
            if (_dimRightImage != null) _dimRightImage.color = c;
        }

        private void OnSkipPressed()
        {
            if (TutorialController.HasInstance)
            {
                TutorialController.Instance.SkipTutorial();
            }
        }

        private void OnTapAnywhere()
        {
            if (TutorialController.HasInstance && TutorialController.Instance.IsTutorialActive())
            {
                TutorialController.Instance.AdvanceStep();
            }
        }

        #endregion

        #region Coroutines

        private IEnumerator FadeDimCoroutine(float targetAlpha)
        {
            float startAlpha = _dimTopImage != null ? _dimTopImage.color.a : 0f;
            float elapsed = 0f;

            while (elapsed < FADE_DURATION)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / FADE_DURATION);
                float currentAlpha = Mathf.Lerp(startAlpha, targetAlpha, t);
                SetDimColor(currentAlpha);
                yield return null;
            }

            SetDimColor(targetAlpha);
            _fadeDimCoroutine = null;
        }

        private IEnumerator BobArrowCoroutine(Vector2 basePosition, Vector2 direction)
        {
            if (_arrowIndicator == null) yield break;

            float elapsed = 0f;

            while (_arrowIndicator.gameObject.activeSelf)
            {
                elapsed += Time.deltaTime;
                float offset = Mathf.Sin(elapsed * Mathf.PI * ARROW_BOB_FREQUENCY) * ARROW_BOB_AMPLITUDE;
                _arrowIndicator.anchoredPosition = basePosition + direction.normalized * offset;
                yield return null;
            }
        }

        #endregion
    }
}
