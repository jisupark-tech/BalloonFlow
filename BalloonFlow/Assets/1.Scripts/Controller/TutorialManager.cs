using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// First Time User Experience (FTUE) visual manager.
    /// Listens to OnTutorialStepChanged and drives all tutorial UI elements:
    /// single cutout dim overlay (CutoutMaskUI), highlight frame, directional arrow, and instruction panel.
    /// Prefab-based (Resources/Popup/Tutorial). Code fallback creates minimal UI when prefab missing.
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
        private static readonly Color FRAME_COLOR = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color INSTRUCTION_BG_COLOR = new Color(0.1f, 0.1f, 0.15f, 0.92f);

        #endregion

        #region Fields

        // Root
        private GameObject _tutorialCanvas;
        private Canvas _canvas;
        private RectTransform _canvasRect;

        // Cutout mask (UseItem 패턴): _cutoutMask = hole 정의 RectTransform.
        // CutoutMaskUI + Mask + 자식 DimOverlay 자동 셋업 → mask 영역 "밖"만 dim 렌더 (hole-in-UI).
        private RectTransform _cutoutMask;
        private RectTransform _cutoutFrame;
        private Image _cutoutFrameImage;
        /// <summary>풀스크린 dim Image — CutoutMaskUI 가 자식 영역(=CutoutFrame) "밖"에만 그려지도록 펀칭.</summary>
        private Image _cutoutDimImage;

        // Arrow indicator
        private RectTransform _arrowIndicator;
        // [2026-05-13] step.useArrowIndicator=false 시 ShowArrow 호출 무시. ApplyStepVisualOverride 가 설정.
        private bool _arrowSuppressedByStep;
        // [2026-05-12] Hand indicator — step 별 override layout 지원
        private RectTransform _handIndicator;
        private Image _arrowImage;
        private Image _handImage;
        private Sprite _defaultCutoutFrameSprite;
        private Color _defaultCutoutFrameColor;
        private Sprite _defaultHandSprite;
        private Vector3 _defaultHandScale = Vector3.one;
        private Vector3 _defaultHandRotation = Vector3.zero;
        private Tween _handTween;

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
                _cutoutMask = popup.CutoutMask;
                _cutoutFrame = popup.CutoutFrame;
                _cutoutFrameImage = _cutoutFrame?.GetComponent<Image>();
                if (_cutoutFrameImage != null)
                {
                    _defaultCutoutFrameSprite = _cutoutFrameImage.sprite;
                    _defaultCutoutFrameColor = _cutoutFrameImage.color;
                }

                // _cutoutMask 에 CutoutMaskUI + Mask 부착 → 자식 DimOverlay 자동 생성 (hole "밖"만 dim 렌더, UseItem 패턴).
                SetupCutoutMask();

                // [2026-05-13] Unity SerializeField null 은 fake-null 이라 `?.` 가 GetComponent 호출까지 진행해 예외.
                // 명시적 Unity null 검사 (`x != null` = overloaded ==) 로 변경.
                _arrowIndicator = popup.ArrowIndicator;
                _arrowImage = (_arrowIndicator != null) ? _arrowIndicator.GetComponent<Image>() : null;
                _handIndicator = popup.HandIndicator;
                _handImage = (_handIndicator != null) ? _handIndicator.GetComponent<Image>() : null;
                if (_handIndicator != null)
                {
                    _defaultHandScale = _handIndicator.localScale;
                    _defaultHandRotation = _handIndicator.localEulerAngles;
                }
                if (_handImage != null)
                    _defaultHandSprite = _handImage.sprite;

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
        /// Hides the cutout and dim overlay.
        /// </summary>
        public void HideCutout()
        {
            _isCutoutVisible = false;

            if (_cutoutMask != null)
                _cutoutMask.gameObject.SetActive(false);

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
            // [2026-05-13] step.useArrowIndicator=false 면 ShowArrow 무시.
            if (_arrowSuppressedByStep) return;

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

            // 단일 _cutoutMask (hole 정의 RectTransform). SetupCutoutMask 이 CutoutMaskUI/Mask + 자식 DimOverlay 추가.
            _cutoutMask = CreateCutoutMaskRect("CutoutMask", canvasGO.transform);

            // CutoutFrame (frame Outline 시각화). _cutoutMask 의 형제로 둠 — mask 영향 안 받게.
            _cutoutFrame = CreateCutoutFrame(canvasGO.transform);

            // _cutoutMask 에 mask + 자식 DimOverlay 셋업
            SetupCutoutMask();

            // Tap-anywhere, arrow, instruction
            CreateTapAnywhereOverlay(canvasGO.transform);
            _arrowIndicator = CreateArrowIndicator(canvasGO.transform);
            _instructionPanel = CreateInstructionPanel(canvasGO.transform);
        }

        private RectTransform CreateCutoutMaskRect(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot     = new Vector2(0.5f, 0.5f);
            // hole 영역은 ApplyCutout 에서 갱신
            rect.sizeDelta = Vector2.zero;
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
        /// UseItem 패턴 (PopupUseItem.SetupShaders 와 동일):
        /// _cutoutMask (RectTransform) 에 CutoutMaskUI + Mask 부착 → _cutoutMask 영역 = hole. 자식 DimOverlay 자동 생성 → hole "밖"만 dim 렌더.
        /// _cutoutFrame 이 _cutoutMask 자식이면 mask 영향으로 frame 가시성 깨지므로 형제로 reparent.
        /// </summary>
        private void SetupCutoutMask()
        {
            if (_cutoutMask == null) return;

            // CutoutMaskUI 보장 — 기존 Image 가 있으면 교체.
            var existingImage = _cutoutMask.GetComponent<Image>();
            CutoutMaskUI cutout = _cutoutMask.GetComponent<CutoutMaskUI>();
            if (cutout == null)
            {
                if (existingImage != null && !(existingImage is CutoutMaskUI))
                    DestroyImmediate(existingImage);
                cutout = _cutoutMask.gameObject.AddComponent<CutoutMaskUI>();
            }
            // 메시 보장용 흰색 sprite (stencil write 가능)
            if (cutout.sprite == null)
            {
                var tex = new Texture2D(4, 4);
                var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white;
                tex.SetPixels(px); tex.Apply();
                cutout.sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            }
            cutout.type = Image.Type.Simple;
            cutout.color = new Color(1f, 1f, 1f, 0f); // 본체는 안 보임 — geometry/stencil 만
            cutout.raycastTarget = false;

            // Mask — showMaskGraphic=true 여야 stencil 정상 기록.
            var mask = _cutoutMask.GetComponent<Mask>();
            if (mask == null) mask = _cutoutMask.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            // _cutoutFrame 이 _cutoutMask 자식이면 mask 영향으로 frame 안 보임 → 한 단계 위로 reparent.
            if (_cutoutFrame != null && _cutoutFrame.parent == _cutoutMask)
            {
                Transform grand = _cutoutMask.parent != null ? _cutoutMask.parent
                    : (_tutorialCanvas != null ? _tutorialCanvas.transform : null);
                if (grand != null) _cutoutFrame.SetParent(grand, false);
            }

            // 자식 DimOverlay — 부모 _cutoutMask 의 mask 영역 "밖" 만 그려짐.
            Transform existingDim = _cutoutMask.Find("DimOverlay");
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
                dimGO.transform.SetParent(_cutoutMask, false);
                _cutoutDimImage = dimGO.GetComponent<Image>();
            }
            // 부모 _cutoutMask 이 작아도 자식이 화면 전체를 덮도록 절대 크기.
            var dimRT = dimGO.GetComponent<RectTransform>();
            dimRT.anchorMin = new Vector2(0.5f, 0.5f);
            dimRT.anchorMax = new Vector2(0.5f, 0.5f);
            dimRT.pivot     = new Vector2(0.5f, 0.5f);
            dimRT.anchoredPosition = Vector2.zero;
            dimRT.sizeDelta = new Vector2(10000f, 10000f);
            _cutoutDimImage.color = new Color(0f, 0f, 0f, 0f); // 알파는 SetDimColor 로 페이드
            _cutoutDimImage.raycastTarget = true; // dim 영역 클릭 차단
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
            _defaultCutoutFrameSprite = _cutoutFrameImage.sprite;
            _defaultCutoutFrameColor = _cutoutFrameImage.color;

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

        private void EnsureTutorialUI()
        {
            if (_tutorialCanvas != null && _instructionText != null)
                return;

            Debug.Log("[TutorialDbg] Tutorial UI reference missing. Rebinding Popup/Tutorial.");
            LoadOrCreateTutorialUI();
            HideAllVisuals();
        }

        /// <summary>
        /// [2026-05-12] step 별 visual layout override 적용.
        /// step.overrideVisualLayout = false 면 noop (highlightTarget 기반 자동 layout 사용).
        /// CutoutMask sprite, CutoutFrame pos/size, InstructionPanel pos/size, Arrow/Hand pos 모두 처리.
        /// </summary>
        private void ApplyStepVisualOverride(TutorialStep step)
        {
            if (step == null || !step.overrideVisualLayout) return;

            if (_cutoutFrame != null)
            {
                _isCutoutVisible = true;
                _cutoutFrame.gameObject.SetActive(step.useCutoutFrame);
                _cutoutFrame.anchoredPosition = step.cutoutFramePosition;
                _cutoutFrame.sizeDelta = step.cutoutFrameSize;
                if (_cutoutFrameImage != null)
                {
                    if (step.cutoutFrameSprite != null)
                    {
                        _cutoutFrameImage.sprite = step.cutoutFrameSprite;
                        _cutoutFrameImage.color = Color.white;
                    }
                    else
                    {
                        _cutoutFrameImage.sprite = _defaultCutoutFrameSprite;
                        _cutoutFrameImage.color = _defaultCutoutFrameColor;
                    }
                }
            }
            if (_cutoutMask != null)
            {
                _cutoutMask.gameObject.SetActive(true);
                _cutoutMask.anchoredPosition = step.cutoutFramePosition;
                _cutoutMask.sizeDelta = step.cutoutFrameSize + new Vector2(CUTOUT_PADDING * 2f, CUTOUT_PADDING * 2f);

                if (step.cutoutMaskSprite != null)
                {
                    var cutoutImg = _cutoutMask.GetComponent<Image>();
                    if (cutoutImg != null) cutoutImg.sprite = step.cutoutMaskSprite;
                }
            }
            if (_instructionPanelRect != null)
            {
                _instructionPanelRect.anchoredPosition = step.instructionPanelPosition;
                _instructionPanelRect.sizeDelta = step.instructionPanelSize;
            }
            // [2026-05-13] Arrow toggle — useArrowIndicator=false 시 비활성 + 후속 ShowArrow 호출도 차단.
            _arrowSuppressedByStep = !step.useArrowIndicator;
            if (_arrowIndicator != null)
            {
                if (!step.useArrowIndicator)
                    _arrowIndicator.gameObject.SetActive(false);
                _arrowIndicator.anchoredPosition = step.arrowIndicatorPosition;
            }
            if (_handIndicator != null)
            {
                _handIndicator.gameObject.SetActive(step.useHandIndicator);
                _handIndicator.anchoredPosition = step.handIndicatorPosition;
                _handIndicator.localScale = _defaultHandScale;
                _handIndicator.localEulerAngles = _defaultHandRotation;
                if (step.handIndicatorSprite != null)
                {
                    if (_handImage == null)
                    {
                        _handImage = _handIndicator.GetComponent<Image>();
                        if (_handImage == null)
                        {
                            _handImage = _handIndicator.gameObject.AddComponent<Image>();
                            _handImage.raycastTarget = false;
                        }
                    }
                    if (_handImage != null)
                        _handImage.sprite = step.handIndicatorSprite;
                }
                else if (_handImage != null)
                {
                    _handImage.sprite = _defaultHandSprite;
                }

                if (step.useHandIndicator)
                    PlayHandTween(step);
            }
        }

        private void ResetStepVisualOverrideState()
        {
            StopHandTween();

            if (_cutoutFrameImage != null)
            {
                _cutoutFrameImage.sprite = _defaultCutoutFrameSprite;
                _cutoutFrameImage.color = _defaultCutoutFrameColor;
            }

            if (_handIndicator != null)
            {
                _handIndicator.localScale = _defaultHandScale;
                _handIndicator.localEulerAngles = _defaultHandRotation;
                _handIndicator.gameObject.SetActive(false);
            }

            if (_handImage != null)
                _handImage.sprite = _defaultHandSprite;
        }

        private void PlayHandTween(TutorialStep step)
        {
            if (_handIndicator == null || step.handTweenType == TutorialHandTweenType.None)
                return;

            StopHandTween();

            float duration = Mathf.Max(0.05f, step.handTweenDuration);
            Vector2 basePosition = step.handIndicatorPosition;
            Vector2 targetPosition = basePosition + step.handTweenMoveOffset;
            Vector3 baseScale = _defaultHandScale;
            Vector3 targetScale = baseScale * Mathf.Max(0.01f, step.handTweenScale);
            Vector3 targetRotation = _defaultHandRotation + new Vector3(0f, 0f, step.handTweenRotation);

            Sequence sequence = DOTween.Sequence();
            sequence.SetUpdate(true);
            bool hasTween = false;

            if (step.handTweenType == TutorialHandTweenType.Move || step.handTweenType == TutorialHandTweenType.MoveAndPulse)
            {
                sequence.Join(_handIndicator.DOAnchorPos(targetPosition, duration).SetEase(Ease.InOutSine));
                hasTween = true;
            }

            if (step.handTweenType == TutorialHandTweenType.Pulse || step.handTweenType == TutorialHandTweenType.MoveAndPulse)
            {
                sequence.Join(_handIndicator.DOScale(targetScale, duration).SetEase(Ease.InOutSine));
                hasTween = true;
            }

            if (step.handTweenType == TutorialHandTweenType.Rotate || Mathf.Abs(step.handTweenRotation) > 0.001f)
            {
                sequence.Join(_handIndicator.DOLocalRotate(targetRotation, duration, RotateMode.Fast).SetEase(Ease.InOutSine));
                hasTween = true;
            }

            if (!hasTween)
            {
                sequence.Kill();
                return;
            }

            _handTween = sequence.SetLoops(-1, LoopType.Yoyo);
        }

        private void StopHandTween()
        {
            if (_handTween != null)
            {
                _handTween.Kill();
                _handTween = null;
            }

            if (_handIndicator != null)
                _handIndicator.DOKill();
        }

        private void HandleTutorialStarted(OnTutorialStarted evt)
        {
            EnsureTutorialUI();
            Debug.Log($"[TutorialDbg] HandleTutorialStarted tutorialId={evt.tutorialId} " +
                      $"canvasActive={(_tutorialCanvas != null ? _tutorialCanvas.activeInHierarchy : false)} " +
                      $"instructionPanel={_instructionPanel != null} instructionText={_instructionText != null}");
            SetTutorialCanvasInteractive(true);
            SetDimOverlay(true);
        }

        private void HandleTutorialStepChanged(OnTutorialStepChanged evt)
        {
            EnsureTutorialUI();
            Debug.Log($"[TutorialDbg] HandleTutorialStepChanged step={evt.stepIndex} instr='{evt.instruction}'");
            string highlightTarget = string.Empty;
            string requireAction = string.Empty;
            TutorialStep currentStep = null;

            if (TutorialController.HasInstance)
            {
                currentStep = TutorialController.Instance.GetCurrentStep();
                if (currentStep != null)
                {
                    highlightTarget = currentStep.highlightTarget ?? string.Empty;
                    requireAction = currentStep.requireAction ?? string.Empty;
                }
            }

            // [2026-05-12] step 별 visual layout override 적용 (Inspector / Data 에서 지정 시).
            ResetStepVisualOverrideState();

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

            // [2026-05-12] step 별 visual layout override 적용.
            // 자동 target 배치가 끝난 뒤 적용해야 수동 Cutout/Indicator 위치가 덮이지 않는다.
            ApplyStepVisualOverride(currentStep);

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
        /// _cutoutMask 의 RectTransform 을 hole 위치/크기로 갱신 (UseItem 패턴). center/size 는 canvas space.
        /// CutoutMaskUI 가 _cutoutMask 영역을 stencil-invert → 자식 DimOverlay 가 hole "밖"만 dim 렌더.
        /// _cutoutFrame 도 동일 위치/크기로 따라가서 hole 가장자리 frame 표시.
        /// </summary>
        private void ApplyCutout(Vector2 center, Vector2 size)
        {
            _isCutoutVisible = true;

            if (_cutoutMask != null)
            {
                _cutoutMask.gameObject.SetActive(true);
                _cutoutMask.anchoredPosition = center;
                _cutoutMask.sizeDelta = size + new Vector2(CUTOUT_PADDING * 2f, CUTOUT_PADDING * 2f);
            }

            if (_cutoutFrame != null)
            {
                _cutoutFrame.gameObject.SetActive(true);
                _cutoutFrame.anchoredPosition = center;
                _cutoutFrame.sizeDelta = size;
            }
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
            ResetStepVisualOverrideState();
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
            if (_cutoutDimImage != null)
                _cutoutDimImage.color = new Color(0f, 0f, 0f, alpha);
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
            float startAlpha = _cutoutDimImage != null ? _cutoutDimImage.color.a : 0f;
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
