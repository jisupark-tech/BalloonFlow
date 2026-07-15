using System.Collections;
using AimedPuzzle.BalloonFlow.UI;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
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
        // [2026-06-04] 튜토리얼 등장 직후 입력 차단 grace period — 사용자가 내용을 읽기 전 오클릭 방지.
        private const float INPUT_BLOCK_AFTER_SHOW_SECONDS = 2f;
        private const float ARROW_BOB_AMPLITUDE = 10f;
        private const float ARROW_BOB_FREQUENCY = 2f;
        private const float CUTOUT_PADDING = 20f;
        private const float FRAME_THICKNESS = 4f;
        // PopupTr (=200) 와 EffectTr (=300) 사이. PopupCanvas 안 다른 popup 위에 dim 이 떠야 가시화됨.
        private const int CANVAS_SORT_ORDER = 250;
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
        // hole 안쪽 raycast pass-through 의도 — ICanvasRaycastFilter 가 frame 영역만 클릭 통과시킴.
        private TutorialCutoutRaycastFilter _cutoutRaycastFilter;

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
        // ROLLBACK_TOUCH_GUIDE_HINT_20260713: 터치 유도 힌트가 튜토와 동일한 핸드 스프라이트를 재사용하도록 노출.
        //   튜토 팝업 바인딩(_defaultHandSprite = _handImage.sprite) 이후에 유효. 롤백: 이 프로퍼티 삭제.
        public Sprite HandSprite => _defaultHandSprite;
        private Vector3 _defaultHandScale = Vector3.one;
        private Vector3 _defaultHandRotation = Vector3.zero;
        private Tween _handTween;

        // Instruction panel
        private GameObject _instructionPanel;
        private RectTransform _instructionPanelRect;
        private Image _instructionPanelImage;
        private Color _defaultInstructionPanelColor = INSTRUCTION_BG_COLOR;
        private TextMeshProUGUI _instructionText;
        // ROLLBACK_TUTORIAL_INSTRUCTION_COLOR_20260622: 프리팹 기본 instruction 색상(override 안 하는 스텝 복원용).
        private Color _defaultInstructionColor = Color.white;
        private Button _skipButton;
        // ROLLBACK_TUTORIAL_HIDE_ALL_SKIP_20260623: 모든 튜토리얼 Skip 버튼 전역 비노출. false 로 두면 스텝별 동작.
        private static readonly bool HIDE_ALL_SKIP_BUTTONS = true;

        // Tap-anywhere overlay (invisible button that covers the cutout hole area)
        private Button _tapAnywhereButton;
        private GameObject _tapAnywhereGO;
        private float _tapAnywhereShownTime = -1f; // ROLLBACK_TUTORIAL_MIN_DISPLAY_20260715: 최소 표시 시간 게이트 기준

        // [2026-05-15] TextTap / TextTapOutline — tap_anywhere step 에서만 표시 + alpha yoyo 깜빡.
        private RectTransform _textTap;
        private RectTransform _textTapOutline;
        private CanvasGroup _textTapGroup;
        private CanvasGroup _textTapOutlineGroup;
        private Vector2 _defaultTextTapPosition;
        private Vector2 _defaultTextTapOutlinePosition;
        private Tween _textTapBlinkTween;
        // ROLLBACK_TUTORIAL_DELAY_TEXT_20260715: 지연 텍스트 — TMP 캐시 + 프리팹 기본 내용(스텝 간 복원용) + 지연 표시 코루틴.
        private TMPro.TMP_Text _textTapTmp;
        private TMPro.TMP_Text _textTapOutlineTmp;
        private string _defaultTextTapContent;
        private string _defaultTextTapOutlineContent;
        private Coroutine _delayedTextCo;
        // ROLLBACK_TUTORIAL_DELAY_STATIC_TEXT_20260715: 지연 텍스트는 정적(점멸/스케일 없음)으로 표시.
        //   프리팹에 Animator(스케일 펄스 등)가 붙어 있을 수 있어 캐시해 두고 정적 표시 시 비활성 + 스케일 기본값 복원.
        private Animator _textTapAnimator;
        private Animator _textTapOutlineAnimator;
        private Vector3 _defaultTextTapScale = Vector3.one;
        private Vector3 _defaultTextTapOutlineScale = Vector3.one;

        // State
        private Coroutine _fadeDimCoroutine;
        private Coroutine _arrowBobCoroutine;
        // [2026-06-04] 튜토리얼 등장 직후 INPUT_BLOCK_AFTER_SHOW_SECONDS 동안 입력 차단 코루틴 핸들.
        private Coroutine _inputBlockCoroutine;
        private bool _isDimActive;
        private bool _isCutoutVisible;

        // Tutorial prefab root canvas/canvasgroup/raycaster — 튜토리얼 active 시에만 raycast 인터셉트.
        private Canvas _prefabRootCanvas;
        private CanvasGroup _prefabRootCanvasGroup;
        private UnityEngine.UI.GraphicRaycaster _prefabRootRaycaster;

        // [2026-05-21] PopupUseItem 와 동일 패턴 — mat_UICutoutDim shader 로 hole-in-UI dim 처리.
        private Material _runtimeCutoutDimMaterial;
        private AsyncOperationHandle<Material> _cutoutDimMaterialHandle;
        // Addressables 에는 full asset path 로 등록됨 (audit: 'Assets/3.Material/UICutoutDim.mat')
        // 짧은 alias 'mat_UICutoutDim' 은 미등록 — InvalidKeyException 회피용 full path 사용.
        private const string CUTOUT_DIM_MATERIAL_ADDRESS = "Assets/3.Material/UICutoutDim.mat";
        private static readonly int OVERLAY_RECT_ID = Shader.PropertyToID("_OverlayRect");
        private static readonly int CUTOUT_CENTER_ID = Shader.PropertyToID("_CutoutCenter");
        private static readonly int CUTOUT_SIZE_ID = Shader.PropertyToID("_CutoutSize");
        private static readonly int CUTOUT_SOFTNESS_ID = Shader.PropertyToID("_CutoutSoftness");
        private static readonly int CUTOUT_MASK_TEX_ID = Shader.PropertyToID("_CutoutMaskTex");
        private static readonly int CUTOUT_MASK_UV_RECT_ID = Shader.PropertyToID("_CutoutMaskUVRect");
        // ROLLBACK_CUTOUTDIM_9SLICE: 9-slice 호환을 위해 sprite border 를 shader 에 전달.
        private static readonly int BORDER_RECT_ID = Shader.PropertyToID("_BorderRect");
        private static readonly int BORDER_SPRITE_ID = Shader.PropertyToID("_BorderSprite");
        // 9-slice 캐시 — UpdateCutoutMaterialHole 에서 holePx 기준 정규화에 사용.
        private Vector4 _currentCutoutSpriteBorder;
        private Vector2 _currentCutoutSpriteSize;
        private Texture2D _whiteMaskTex; // _CutoutMaskTex default — sprite 없을 때 사각형 hole.

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
            //
            // [2026-05-21] Tutorial.prefab 의 root 에 Canvas 가 없으면 dim 이 PopupCanvas batch 안에서
            //   sibling order 만으로 결정돼 다른 popup 뒤에 깔리는 회귀. overrideSorting=true 가 적용되려면
            //   Canvas 컴포넌트가 필수이므로 없을 시 런타임 부착.
            _prefabRootCanvas = root.GetComponent<Canvas>();
            if (_prefabRootCanvas == null)
                _prefabRootCanvas = root.AddComponent<Canvas>();
            _prefabRootCanvasGroup = root.GetComponent<CanvasGroup>();
            if (_prefabRootCanvasGroup == null)
                _prefabRootCanvasGroup = root.AddComponent<CanvasGroup>();
            _prefabRootRaycaster = root.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (_prefabRootRaycaster == null)
                _prefabRootRaycaster = root.AddComponent<UnityEngine.UI.GraphicRaycaster>();
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
                // ROLLBACK_TUTORIAL_INSTRUCTION_COLOR_20260622: 프리팹 기본 색상 보존 — override 안 하는 스텝에서 복원용.
                if (_instructionText != null) _defaultInstructionColor = _instructionText.color;
                // Prefab에서 InstructionPanel이 명시 지정되어 있으면 우선 사용 (디자이너가 위치 이동 가능).
                // 없으면 기존처럼 InstructionText의 parent로 폴백.
                _instructionPanelRect = popup.InstructionPanel;
                if (_instructionPanelRect == null && _instructionText != null)
                    _instructionPanelRect = _instructionText.transform.parent as RectTransform;
                _instructionPanel = _instructionPanelRect != null ? _instructionPanelRect.gameObject : null;
                _instructionPanelImage = _instructionPanelRect != null ? _instructionPanelRect.GetComponent<Image>() : null;
                if (_instructionPanelImage != null)
                    _defaultInstructionPanelColor = _instructionPanelImage.color;

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
                        if (!TutorialController.HasInstance) return;
                        // ROLLBACK_TUTORIAL_MIN_DISPLAY_20260715: 최소 표시 시간 게이트 — 경과 전 탭은 무시(닫힘 방지).
                        if (IsMinDisplayBlocking()) return;
                        TutorialController.Instance.AdvanceStep();
                    });

                // [2026-05-15] TextTap / TextTapOutline 바인딩. CanvasGroup 없으면 부착 (alpha 제어용).
                _textTap = popup.TextTap;
                _textTapOutline = popup.TextTapOutline;
                if (_textTap != null)
                {
                    _defaultTextTapPosition = _textTap.anchoredPosition;
                    _textTapGroup = _textTap.GetComponent<CanvasGroup>();
                    if (_textTapGroup == null) _textTapGroup = _textTap.gameObject.AddComponent<CanvasGroup>();
                    _textTapTmp = _textTap.GetComponent<TMPro.TMP_Text>(); // ROLLBACK_TUTORIAL_DELAY_TEXT_20260715
                    if (_textTapTmp != null) _defaultTextTapContent = _textTapTmp.text;
                    _textTapAnimator = _textTap.GetComponent<Animator>();      // ROLLBACK_TUTORIAL_DELAY_STATIC_TEXT_20260715
                    _defaultTextTapScale = _textTap.localScale;
                    _textTap.gameObject.SetActive(false);
                }
                if (_textTapOutline != null)
                {
                    _defaultTextTapOutlinePosition = _textTapOutline.anchoredPosition;
                    _textTapOutlineGroup = _textTapOutline.GetComponent<CanvasGroup>();
                    if (_textTapOutlineGroup == null) _textTapOutlineGroup = _textTapOutline.gameObject.AddComponent<CanvasGroup>();
                    _textTapOutlineTmp = _textTapOutline.GetComponent<TMPro.TMP_Text>(); // ROLLBACK_TUTORIAL_DELAY_TEXT_20260715
                    if (_textTapOutlineTmp != null) _defaultTextTapOutlineContent = _textTapOutlineTmp.text;
                    _textTapOutlineAnimator = _textTapOutline.GetComponent<Animator>(); // ROLLBACK_TUTORIAL_DELAY_STATIC_TEXT_20260715
                    _defaultTextTapOutlineScale = _textTapOutline.localScale;
                    _textTapOutline.gameObject.SetActive(false);
                }
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
            // [2026-05-15] DOTween yoyo loop kill — singleton destroy 시 tween 누수 방지.
            StopTextTapBlink();

            // [2026-05-21] Addressable cutout dim material 정리.
            if (_runtimeCutoutDimMaterial != null)
            {
                if (Application.isPlaying) Destroy(_runtimeCutoutDimMaterial);
                else DestroyImmediate(_runtimeCutoutDimMaterial);
                _runtimeCutoutDimMaterial = null;
            }
            if (_cutoutDimMaterialHandle.IsValid())
            {
                Addressables.Release(_cutoutDimMaterialHandle);
                _cutoutDimMaterialHandle = default;
            }
            if (_whiteMaskTex != null)
            {
                if (Application.isPlaying) Destroy(_whiteMaskTex);
                else DestroyImmediate(_whiteMaskTex);
                _whiteMaskTex = null;
            }

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
        /// <remarks>
        /// [2026-05-15] 실제 visual GameObject 의 transform.position 사용 (추정 columnSpacing 제거).
        /// step.cutoutWidth/Height 가 지정돼 있으면 우선 사용, 아니면 200x200 fallback.
        /// </remarks>
        public void ShowCutoutForHolder(int holderIndex)
        {
            if (!HolderManager.HasInstance) return;

            HolderData[] holders = HolderManager.Instance.GetHolders();
            if (holders == null || holderIndex < 0 || holderIndex >= holders.Length) return;

            // [2026-05-15] step 에서 cutout size override 시도. 0/음수면 default 200.
            Vector2 cutSize = GetStepCutoutSize();

            if (HolderVisualManager.HasInstance)
            {
                // 실제 visual GameObject 의 world position 사용 — column 추정값 대신.
                int holderId = holders[holderIndex].holderId;
                var holderGO = HolderVisualManager.Instance.GetHolderGameObject(holderId);
                if (holderGO != null)
                {
                    ShowCutout(holderGO.transform.position, cutSize);
                    return;
                }

                // Visual 아직 spawn 전이면 queue center 로 fallback.
                Vector3 queueCenter = HolderVisualManager.Instance.CalculateQueueCenterPosition();
                ShowCutout(queueCenter, cutSize);
            }
            else
            {
                // Fallback: show cutout at screen center bottom area
                Vector2 canvasSize = _canvasRect.sizeDelta;
                ApplyCutout(new Vector2(canvasSize.x * 0.5f, canvasSize.y * 0.25f), cutSize);
            }
        }

        /// <summary>현재 step 의 cutoutWidth/Height 또는 default(200x200) 반환.</summary>
        private Vector2 GetStepCutoutSize()
        {
            const float DEFAULT = 200f;
            if (!TutorialController.HasInstance) return new Vector2(DEFAULT, DEFAULT);
            TutorialStep step = TutorialController.Instance.GetCurrentStep();
            if (step == null) return new Vector2(DEFAULT, DEFAULT);

            float w = step.cutoutWidth > 0f ? step.cutoutWidth : DEFAULT;
            float h = step.cutoutHeight > 0f ? step.cutoutHeight : DEFAULT;
            return new Vector2(w, h);
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
        /// Hide cutout highlight. shader 패턴에선 _cutoutMask (=Dim) 가 항상 stretch 로 화면 전체 dim;
        /// hole 은 shader 의 _CutoutSize 를 0 으로 만들어 닫음.
        /// </summary>
        public void HideCutout()
        {
            _isCutoutVisible = false;

            if (_runtimeCutoutDimMaterial != null)
                _runtimeCutoutDimMaterial.SetVector(CUTOUT_SIZE_ID, new Vector4(0f, 0f, 0f, 0f));

            if (_cutoutMask != null && !_isDimActive)
                _cutoutMask.gameObject.SetActive(false);

            if (_cutoutFrame != null)
                _cutoutFrame.gameObject.SetActive(false);

            if (_arrowIndicator != null)
                _arrowIndicator.gameObject.SetActive(false);

            // hole 닫힘 → filter 가 풀스크린 차단 (기존 동작 유지).
            if (_cutoutRaycastFilter != null) _cutoutRaycastFilter.ClearHole();
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
            {
                _instructionText.text = text ?? string.Empty;
                // ROLLBACK_TUTORIAL_KO_FONT_20260714: 지시문은 코드로 .text 세팅(UIText 미부착)이라 폰트 스왑이 없어
                //   KO 텍스트가 Poppins 로 tofu 가 됐다. 세팅 직후 언어 폰트 적용(KO=Chiron, 색/아웃라인 보존).
                LocalizationFont.Apply(_instructionText);
            }
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
        // ROLLBACK_TUTORIAL_ITEM_FADE_HOLDER_BLOCK_20260706: dim(페이드) 가 아직 '적용 중'(페이드 인 진행)인지.
        //   아이템 튜토리얼 진입 시 페이드가 다 적용되기 전엔 홀더가 클릭되면 안 됨(InputHandler 에서 사용).
        //   SetDimOverlay(true) 로 시작해 FadeDimCoroutine 이 끝나면(_fadeDimCoroutine=null) false.
        public bool IsDimFadeInProgress => _isDimActive && _fadeDimCoroutine != null;

        public void SetDimOverlay(bool active)
        {
            _isDimActive = active;

            if (_fadeDimCoroutine != null)
                StopCoroutine(_fadeDimCoroutine);

            if (active)
            {
                // _cutoutMask (=Dim) 는 prefab 의 stretch anchor 그대로 화면 전체 dim. 활성 보장만.
                if (_cutoutMask != null && !_cutoutMask.gameObject.activeSelf)
                    _cutoutMask.gameObject.SetActive(true);
                if (_cutoutDimImage != null && !_cutoutDimImage.gameObject.activeSelf)
                    _cutoutDimImage.gameObject.SetActive(true);
                UpdateOverlayRect();
            }

            float targetAlpha = active ? DIM_ALPHA : 0f;
            _fadeDimCoroutine = StartCoroutine(FadeDimCoroutine(targetAlpha));
        }

        #endregion

        #region Public Methods — Tap Anywhere

        /// <summary>
        /// Enables or disables the "tap anywhere to continue" overlay.
        /// [2026-05-15] TextTap/TextTapOutline 도 함께 ON/OFF + alpha 깜빡 yoyo.
        /// step.useTextTap=false 시 텍스트는 비활성 (overlay 만 enable).
        /// </summary>
        /// <summary>
        /// ROLLBACK_TUTORIAL_MIN_DISPLAY_20260715: 현재 tap_anywhere 스텝이 최소 표시 시간(minDisplaySeconds)
        /// 창 안에 있는지 — 이 구간 동안 tap_anywhere 버튼 클릭과 홀더(월드) 클릭을 모두 무시한다.
        /// (InputHandler 가 홀더 레이캐스트 전에 이 값을 조회.)
        /// </summary>
        public bool IsMinDisplayBlocking()
        {
            if (!TutorialController.HasInstance) return false;
            TutorialStep s = TutorialController.Instance.GetCurrentStep();
            return s != null && s.useMinDisplayTime && _tapAnywhereShownTime >= 0f
                && (Time.unscaledTime - _tapAnywhereShownTime) < s.minDisplaySeconds;
        }

        public void SetTapAnywherEnabled(bool enabled)
        {
            if (_tapAnywhereGO != null)
                _tapAnywhereGO.SetActive(enabled);

            // ROLLBACK_TUTORIAL_MIN_DISPLAY_20260715: 활성(=tap_anywhere 스텝 시작) 시점 기록 → 탭 게이트 기준.
            //   비활성 전환 시 -1 로 리셋 — 이전 tap_anywhere 스텝의 잔여 기준시각으로 홀더가 오차단되지 않게.
            _tapAnywhereShownTime = enabled ? Time.unscaledTime : -1f;

            // ROLLBACK_TUTORIAL_DELAY_TEXT_20260715: 이전 지연 텍스트 코루틴 정리(스텝 전환).
            if (_delayedTextCo != null) { StopCoroutine(_delayedTextCo); _delayedTextCo = null; }

            TutorialStep step = (enabled && TutorialController.HasInstance) ? TutorialController.Instance.GetCurrentStep() : null;

            // step.useTextTap 토글 — null 이면 default true.
            bool wantText = enabled && (step == null || step.useTextTap);

            // ROLLBACK_TUTORIAL_DELAY_TEXT_20260715: TextTap 내용 — delayedTextKey 있으면 TextData, 없으면 프리팹 기본(스텝 간 복원).
            ApplyTextTapContent(step != null ? step.delayedTextKey : null);

            if (!wantText) { ShowTextTap(false); return; }

            // ROLLBACK_TUTORIAL_DELAY_TEXT_20260715: min 게이트면 minDisplaySeconds 후(탭 가능 시점)에 표시, 아니면 즉시(기존 동작).
            if (step != null && step.useMinDisplayTime && step.minDisplaySeconds > 0f)
            {
                ShowTextTap(false);
                _delayedTextCo = StartCoroutine(ShowTextTapDelayed(step.minDisplaySeconds));
            }
            else
            {
                ShowTextTap(true);
            }
        }

        // ROLLBACK_TUTORIAL_DELAY_TEXT_20260715: min 게이트 경과 후 TextTap 표시(탭 가능 시점과 일치). unscaled — 일시정지 무관.
        private System.Collections.IEnumerator ShowTextTapDelayed(float delaySec)
        {
            yield return new WaitForSecondsRealtime(delaySec);
            ShowTextTap(true); // ROLLBACK_TUTORIAL_TEXTTAP_NO_BLINK_20260715: 정적 표시(점멸 없음)
            _delayedTextCo = null;
        }

        // ROLLBACK_TUTORIAL_TEXTTAP_NO_BLINK_20260715: TextTap 은 점멸(alpha yoyo)·스케일 펄스 없이 항상 정적으로 표시.
        //   (스테이지 무관 — 즉시/지연 모두 정적.)
        private void ShowTextTap(bool show)
        {
            if (_textTap != null) _textTap.gameObject.SetActive(show);
            if (_textTapOutline != null) _textTapOutline.gameObject.SetActive(show);

            if (!show) { StopTextTapBlink(); return; }

            StopTextTapBlink();     // 점멸 제거(잔여 tween kill + alpha=1)
            SetTextTapStatic(true); // 스케일 펄스 제거(Animator off + 스케일 기본값)
        }

        // ROLLBACK_TUTORIAL_DELAY_STATIC_TEXT_20260715: 정적 표시 토글 — Animator(스케일 펄스 등) on/off + 스케일 리셋.
        private void SetTextTapStatic(bool isStatic)
        {
            if (_textTapAnimator != null) _textTapAnimator.enabled = !isStatic;
            if (_textTapOutlineAnimator != null) _textTapOutlineAnimator.enabled = !isStatic;
            if (isStatic)
            {
                if (_textTap != null) _textTap.localScale = _defaultTextTapScale;
                if (_textTapOutline != null) _textTapOutline.localScale = _defaultTextTapOutlineScale;
            }
        }

        // ROLLBACK_TUTORIAL_DELAY_TEXT_20260715: key 있으면 LocalizationService.Get, 없으면 프리팹 기본 복원(스텝 간 내용 누수 방지).
        private void ApplyTextTapContent(string key)
        {
            bool useKey = !string.IsNullOrEmpty(key);
            if (_textTapTmp != null)
                _textTapTmp.text = useKey ? LocalizationService.Get(key) : _defaultTextTapContent;
            if (_textTapOutlineTmp != null)
                _textTapOutlineTmp.text = useKey ? LocalizationService.Get(key) : _defaultTextTapOutlineContent;
        }

        private const float TEXTTAP_BLINK_DURATION = 0.55f;
        private const float TEXTTAP_BLINK_MIN_ALPHA = 0.25f;

        private void StartTextTapBlink()
        {
            StopTextTapBlink();
            // 시작 alpha 1.0 → MIN_ALPHA yoyo. tap 안내라 빠른 깜빡 (0.55s) + 부드러운 ease.
            Sequence seq = DOTween.Sequence();
            seq.SetUpdate(true); // unscaled — 일시정지 영향 안 받음
            if (_textTapGroup != null)
            {
                _textTapGroup.alpha = 1f;
                seq.Join(_textTapGroup.DOFade(TEXTTAP_BLINK_MIN_ALPHA, TEXTTAP_BLINK_DURATION).SetEase(Ease.InOutSine));
            }
            if (_textTapOutlineGroup != null)
            {
                _textTapOutlineGroup.alpha = 1f;
                seq.Join(_textTapOutlineGroup.DOFade(TEXTTAP_BLINK_MIN_ALPHA, TEXTTAP_BLINK_DURATION).SetEase(Ease.InOutSine));
            }
            seq.SetLoops(-1, LoopType.Yoyo);
            _textTapBlinkTween = seq;
        }

        private void StopTextTapBlink()
        {
            if (_textTapBlinkTween != null)
            {
                _textTapBlinkTween.Kill();
                _textTapBlinkTween = null;
            }
            if (_textTapGroup != null) _textTapGroup.alpha = 1f;
            if (_textTapOutlineGroup != null) _textTapOutlineGroup.alpha = 1f;
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
        /// [2026-05-21] PopupUseItem 와 동일한 mat_UICutoutDim shader 패턴 — hole-in-UI 지원.
        ///   _cutoutMask (=Dim, stretch anchor) 의 Image 에 runtime 클론한 mat_UICutoutDim 적용.
        ///   shader 가 _OverlayRect / _CutoutCenter / _CutoutSize 로 hole 영역을 제외한 영역만 dim 렌더.
        /// 옛 CutoutMaskUI + Mask + 자식 DimOverlay 패턴은 stencil 동작이 hole-in-UI 가 아니어서 제거.
        /// </summary>
        private void SetupCutoutMask()
        {
            if (_cutoutMask == null) return;

            // 옛 stencil 패턴 잔재 정리.
            var oldCutoutMaskUI = _cutoutMask.GetComponent<CutoutMaskUI>();
            if (oldCutoutMaskUI != null) DestroyImmediate(oldCutoutMaskUI);
            var oldMask = _cutoutMask.GetComponent<Mask>();
            if (oldMask != null) oldMask.enabled = false;
            var oldDimOverlay = _cutoutMask.Find("DimOverlay");
            if (oldDimOverlay != null) oldDimOverlay.gameObject.SetActive(false);

            // [2026-05-21] CutoutFrame reparent 제거 — shader 패턴에선 Mask 가 없어 clipping 영향 없음.
            // designer 가 prefab 의 Dim 자식으로 배치한 위치/anchor/sprite 그대로 사용.

            // CutoutFrame.Image 가 prefab 에서 mat_UICutoutMask (legacy stencil mask, ColorMask=0)
            // 로 할당된 경우 sprite 의 픽셀이 안 그려져 invisible. default UI material 로 reset.
            if (_cutoutFrameImage != null && _cutoutFrameImage.material != null)
            {
                var sh = _cutoutFrameImage.material.shader;
                if (sh != null && (sh.name == "UI/CutoutMask" || sh.name == "UI/CutoutDim"))
                    _cutoutFrameImage.material = null; // 기본 UI/Default 로 폴백
            }

            // _cutoutMask (=Dim) 의 Image 확보.
            _cutoutDimImage = _cutoutMask.GetComponent<Image>();
            if (_cutoutDimImage == null)
                _cutoutDimImage = _cutoutMask.gameObject.AddComponent<Image>();
            if (_cutoutDimImage.sprite == null)
            {
                _cutoutDimImage.sprite = GetOrCreateWhiteSprite();
                _cutoutDimImage.type = Image.Type.Simple;
            }
            _cutoutDimImage.color = Color.white; // 색은 shader 가 결정
            _cutoutDimImage.raycastTarget = true;

            // mat_UICutoutDim 로드 + 클론 + Image 에 적용.
            if (_runtimeCutoutDimMaterial == null)
                _runtimeCutoutDimMaterial = CreateCutoutRuntimeMaterial();
            if (_runtimeCutoutDimMaterial != null)
            {
                _cutoutDimImage.material = _runtimeCutoutDimMaterial;
                _runtimeCutoutDimMaterial.color = new Color(0f, 0f, 0f, 0f); // alpha 0 시작 → SetDimColor 가 페이드
                _runtimeCutoutDimMaterial.SetFloat(CUTOUT_SOFTNESS_ID, 0.001f);
                // 초기엔 hole 없음 (size=0).
                _runtimeCutoutDimMaterial.SetVector(CUTOUT_CENTER_ID, new Vector4(0.5f, 0.5f, 0f, 0f));
                _runtimeCutoutDimMaterial.SetVector(CUTOUT_SIZE_ID, new Vector4(0f, 0f, 0f, 0f));
                UpdateOverlayRect();
            }

            // hole 안쪽 raycast pass-through 용 filter — Image 가 있는 같은 GO 에 부착해야 ICanvasRaycastFilter 호출됨.
            _cutoutRaycastFilter = _cutoutMask.gameObject.GetComponent<TutorialCutoutRaycastFilter>()
                ?? _cutoutMask.gameObject.AddComponent<TutorialCutoutRaycastFilter>();
            _cutoutRaycastFilter.Initialize(_cutoutMask);
        }

        private Material CreateCutoutRuntimeMaterial()
        {
            Material source = null;
            try
            {
                _cutoutDimMaterialHandle = Addressables.LoadAssetAsync<Material>(CUTOUT_DIM_MATERIAL_ADDRESS);
                source = _cutoutDimMaterialHandle.WaitForCompletion();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[TutorialManager] Addressable material '{CUTOUT_DIM_MATERIAL_ADDRESS}' load failed: {e.Message}");
            }
            if (source != null) return new Material(source);

            // fallback — 같은 shader 가 다른 경로로 로드돼 있는 경우.
            Shader shader = Shader.Find("UI/CutoutDim");
            return shader != null ? new Material(shader) : null;
        }

        private void UpdateOverlayRect()
        {
            if (_runtimeCutoutDimMaterial == null || _cutoutMask == null) return;
            Rect r = _cutoutMask.rect;
            if (r.width <= 0f || r.height <= 0f) return;
            _runtimeCutoutDimMaterial.SetVector(OVERLAY_RECT_ID, new Vector4(r.xMin, r.yMin, r.width, r.height));
        }

        private Sprite _whiteSprite;
        private Sprite GetOrCreateWhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            var tex = new Texture2D(4, 4);
            var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white;
            tex.SetPixels(px); tex.Apply();
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            return _whiteSprite;
        }

        private Texture2D GetOrCreateWhiteMaskTex()
        {
            if (_whiteMaskTex != null) return _whiteMaskTex;
            _whiteMaskTex = new Texture2D(4, 4);
            var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white;
            _whiteMaskTex.SetPixels(px); _whiteMaskTex.Apply();
            return _whiteMaskTex;
        }

        /// <summary>
        /// CutoutFrame 의 sprite 를 hole 모양 mask 로 shader 에 전달. sprite alpha = hole.
        /// CutoutFrame.Image 자체는 안 보이게 (alpha=0) — sprite 의 픽셀이 화면에 안 나옴.
        /// sprite=null 이면 mask 를 white 로 reset (사각형 hole).
        /// Atlas sprite 호환 — Sprite.textureRect 로 atlas 안 sub-rect 계산해 shader 에 전달.
        /// </summary>
        private void ApplyCutoutFrameSpriteAsHoleMask(Sprite sprite)
        {
            if (_cutoutFrameImage != null)
            {
                _cutoutFrameImage.sprite = sprite; // 참조 유지 (texture 접근용) — 화면엔 안 보임.
                _cutoutFrameImage.color = new Color(0f, 0f, 0f, 0f);
                // 9-slice 지원 — Sprite Editor 에서 Slice 한 border 가 있으면 size 변경 시 모서리 보존.
                // sprite.border 가 모두 0 이면 Sliced 가 Simple 처럼 동작 — 안전 default.
                _cutoutFrameImage.type = Image.Type.Sliced;
                _cutoutFrameImage.pixelsPerUnitMultiplier = 1f;
            }
            if (_runtimeCutoutDimMaterial == null) return;

            Texture tex;
            Vector4 uvRect;
            if (sprite != null && sprite.texture != null)
            {
                tex = sprite.texture;
                Rect r = sprite.textureRect;
                float tw = tex.width, th = tex.height;
                if (tw > 0f && th > 0f)
                    uvRect = new Vector4(r.x / tw, r.y / th, r.width / tw, r.height / th);
                else
                    uvRect = new Vector4(0f, 0f, 1f, 1f);
            }
            else
            {
                tex = GetOrCreateWhiteMaskTex();
                uvRect = new Vector4(0f, 0f, 1f, 1f);
            }

            _runtimeCutoutDimMaterial.SetTexture(CUTOUT_MASK_TEX_ID, tex);
            _runtimeCutoutDimMaterial.SetVector(CUTOUT_MASK_UV_RECT_ID, uvRect);

            // 9-slice 메타데이터 캐싱 — 실제 _BorderRect/_BorderSprite 계산은 UpdateCutoutMaterialHole 에서 holePx 기준 정규화.
            if (sprite != null && sprite.border.sqrMagnitude > 0.0001f)
            {
                _currentCutoutSpriteBorder = sprite.border;
                _currentCutoutSpriteSize = sprite.rect.size;
            }
            else
            {
                _currentCutoutSpriteBorder = Vector4.zero;
                _currentCutoutSpriteSize = Vector2.zero;
            }

            // sprite 변경 직후 즉시 9-slice 반영 (기존엔 다음 ApplyCutout 까지 대기).
            if (_isCutoutVisible && _cutoutMask != null && _cutoutMask.rect.width > 0f && _cutoutFrame != null)
            {
                UpdateCutoutMaterialHole(_cutoutFrame.anchoredPosition,
                    _cutoutFrame.sizeDelta + new Vector2(CUTOUT_PADDING * 2f, CUTOUT_PADDING * 2f));
            }
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
            _cutoutFrameImage.type = Image.Type.Sliced; // 9-slice 활용 — sprite border 따라 자동 작동.
            _defaultCutoutFrameSprite = _cutoutFrameImage.sprite;
            _defaultCutoutFrameColor = _cutoutFrameImage.color;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = FRAME_COLOR;
            outline.effectDistance = new Vector2(FRAME_THICKNESS, FRAME_THICKNESS);

            // ROLLBACK_TUTORIAL_CUTOUT_SOFTMASK: start
            // SpriteMask 가 9-slice 미지원 → mob-sakai/SoftMaskForUGUI 의 SoftMask 사용.
            // SoftMask 는 같은 GameObject 의 Image.sprite (Sliced) 를 mask 로 활용 — 9-slice 모서리 보존.
            // 롤백: 이 블록 제거 + prefab 에 기존 SpriteMask 복원.
            TryAttachSoftMask(go);
            // ROLLBACK_TUTORIAL_CUTOUT_SOFTMASK: end

            return rect;
        }

        // ROLLBACK_TUTORIAL_CUTOUT_SOFTMASK: start
        // SoftMask 패키지 (com.coffee.softmask-for-ugui) 타입을 reflection 으로 부착.
        // 패키지가 다른 asmdef (Coffee.SoftMaskForUGUI) 라 직접 using 시 컴파일 의존 — reflection 으로 안전 fallback.
        // 패키지 정보 (Library/PackageCache/com.coffee.softmask-for-ugui@...): namespace=Coffee.UISoftMask, assembly=Coffee.SoftMaskForUGUI.
        public static System.Type GetSoftMaskType() => SoftMaskTypeResolver.Resolve();

        public static void TryAttachSoftMask(GameObject go)
        {
            var t = SoftMaskTypeResolver.Resolve();
            if (t == null) return;
            if (go.GetComponent(t) != null) return;
            go.AddComponent(t);
        }

        private static class SoftMaskTypeResolver
        {
            private static System.Type s_type;
            private static bool s_resolved;

            public static System.Type Resolve()
            {
                if (s_resolved) return s_type;
                s_resolved = true;
                // Assembly-qualified — Coffee.UISoftMask.SoftMask, Coffee.SoftMaskForUGUI
                s_type = System.Type.GetType("Coffee.UISoftMask.SoftMask, Coffee.SoftMaskForUGUI");
                if (s_type == null)
                {
                    // Fallback — 모든 loaded assembly 에서 검색 (assembly 이름 변경 대비).
                    foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        s_type = asm.GetType("Coffee.UISoftMask.SoftMask", false);
                        if (s_type != null) break;
                    }
                }
                if (s_type == null)
                    Debug.LogWarning("[TutorialManager] Coffee.UISoftMask.SoftMask not found — com.coffee.softmask-for-ugui 패키지 확인 필요.");
                return s_type;
            }
        }
        // ROLLBACK_TUTORIAL_CUTOUT_SOFTMASK: end

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
            _instructionPanelImage = panelImage;
            _defaultInstructionPanelColor = panelImage.color;

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
            skipText.text = LocalizationService.Get("ui.tutorial.skip");
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
                    Sprite spr = step.cutoutFrameSprite != null ? step.cutoutFrameSprite : _defaultCutoutFrameSprite;
                    ApplyCutoutFrameSpriteAsHoleMask(spr);
                }
            }
            // [2026-05-21] shader 패턴: _cutoutMask 는 stretch dim 본체 — transform 건드리면 dim 영역이 깨짐.
            //   hole 위치/크기는 shader 파라미터로만 갱신. cutoutMaskSprite 도 shader 가 mesh quad 만 쓰므로 무시.
            if (_cutoutMask != null && !_cutoutMask.gameObject.activeSelf)
                _cutoutMask.gameObject.SetActive(true);
            UpdateCutoutMaterialHole(step.cutoutFramePosition,
                step.cutoutFrameSize + new Vector2(CUTOUT_PADDING * 2f, CUTOUT_PADDING * 2f));
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

            // ROLLBACK_TUTORIAL_TEXTTAP_POS_PARENT_20260715: 계층 = TextTapOutline(부모) → TextTap(자식).
            //   부모(Outline)만 위치를 조정하면 자식(TextTap)은 부모를 따라 함께 이동한다.
            //   (이전엔 자식에도 custom 을 직접 넣어 부모 프레임 기준으로 어긋났던 것 — 자식은 로컬 기본값 유지.)
            //   조부모 LayoutGroup 이 SetActive 시 부모 anchoredPosition 을 덮으므로 커스텀 위치일 때 LayoutElement.ignoreLayout 로 무시.
            ApplyTextTapPosition(_textTapOutline, step.textTapPosition, _defaultTextTapOutlinePosition);
            if (_textTap != null)
            {
                var childLe = _textTap.GetComponent<LayoutElement>();
                if (childLe != null) childLe.ignoreLayout = false; // 자식은 부모 따라감 — 레이아웃 무시 해제
                _textTap.anchoredPosition = _defaultTextTapPosition; // 부모 기준 로컬 기본 위치 유지(이전 offset 복원)
            }
        }

        // ROLLBACK_TUTORIAL_TEXTTAP_POS_LAYOUT_20260715: 커스텀 위치(zero 아님)면 LayoutElement.ignoreLayout=true 로
        //   부모 LayoutGroup 을 무시하고 anchoredPosition 을 그대로 반영. 기본(zero)이면 레이아웃 복귀 + prefab 기본 위치.
        private void ApplyTextTapPosition(RectTransform rt, Vector2 custom, Vector2 def)
        {
            if (rt == null) return;
            bool useCustom = custom != Vector2.zero;
            var le = rt.GetComponent<LayoutElement>();
            if (useCustom && le == null) le = rt.gameObject.AddComponent<LayoutElement>();
            if (le != null) le.ignoreLayout = useCustom;
            rt.anchoredPosition = useCustom ? custom : def;
        }

        private void ResetStepVisualOverrideState()
        {
            StopHandTween();
            StopTextTapBlink();

            if (_cutoutFrameImage != null)
            {
                ApplyCutoutFrameSpriteAsHoleMask(_defaultCutoutFrameSprite);
            }

            if (_handIndicator != null)
            {
                _handIndicator.localScale = _defaultHandScale;
                _handIndicator.localEulerAngles = _defaultHandRotation;
                _handIndicator.gameObject.SetActive(false);
            }

            // [2026-05-15] TextTap 비활성 + 위치 default 복원.
            if (_textTap != null)
            {
                _textTap.anchoredPosition = _defaultTextTapPosition;
                _textTap.gameObject.SetActive(false);
            }
            if (_textTapOutline != null)
            {
                _textTapOutline.anchoredPosition = _defaultTextTapOutlinePosition;
                _textTapOutline.gameObject.SetActive(false);
            }

            if (_handImage != null)
                _handImage.sprite = _defaultHandSprite;
        }

        private void PlayHandTween(TutorialStep step)
        {
            if (_handIndicator == null || step.handTweenType == TutorialHandTweenType.None)
                return;

            StopHandTween();
            _handTween = BuildHandTweenSequence(
                _handIndicator, step.handIndicatorPosition, _defaultHandScale, _defaultHandRotation,
                step.handTweenType, step.handTweenMoveOffset, step.handTweenScale, step.handTweenRotation, step.handTweenDuration);
        }

        // ROLLBACK_TOUCH_GUIDE_HINT_20260713: 튜토 핸드 tween 시퀀스 생성 로직 추출 → 터치유도 힌트가 '그대로' 재사용.
        //   기존 PlayHandTween 본문과 동일(Move/Pulse/Rotate Join + InOutSine + unscaled + yoyo 무한). 롤백: PlayHandTween 인라인 복원 + 이 메서드 삭제.
        public static Sequence BuildHandTweenSequence(
            RectTransform hand, Vector2 basePosition, Vector3 baseScale, Vector3 baseRotation,
            TutorialHandTweenType type, Vector2 moveOffset, float scale, float rotation, float duration)
        {
            if (hand == null || type == TutorialHandTweenType.None) return null;

            duration = Mathf.Max(0.05f, duration);
            Vector2 targetPosition = basePosition + moveOffset;
            Vector3 targetScale = baseScale * Mathf.Max(0.01f, scale);
            Vector3 targetRotation = baseRotation + new Vector3(0f, 0f, rotation);

            Sequence sequence = DOTween.Sequence();
            sequence.SetUpdate(true);
            bool hasTween = false;

            if (type == TutorialHandTweenType.Move || type == TutorialHandTweenType.MoveAndPulse)
            {
                sequence.Join(hand.DOAnchorPos(targetPosition, duration).SetEase(Ease.InOutSine));
                hasTween = true;
            }

            if (type == TutorialHandTweenType.Pulse || type == TutorialHandTweenType.MoveAndPulse)
            {
                sequence.Join(hand.DOScale(targetScale, duration).SetEase(Ease.InOutSine));
                hasTween = true;
            }

            if (type == TutorialHandTweenType.Rotate || Mathf.Abs(rotation) > 0.001f)
            {
                sequence.Join(hand.DOLocalRotate(targetRotation, duration, RotateMode.Fast).SetEase(Ease.InOutSine));
                hasTween = true;
            }

            if (!hasTween)
            {
                sequence.Kill();
                return null;
            }

            return sequence.SetLoops(-1, LoopType.Yoyo);
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
            // [2026-06-04] 튜토리얼 등장 직후 INPUT_BLOCK_AFTER_SHOW_SECONDS 동안 Skip/TapAnywhere 등
            //   모든 튜토리얼 캔버스 내부 버튼 입력 차단 — 사용자가 내용을 충분히 읽도록.
            //   blocksRaycasts 는 SetTutorialCanvasInteractive 가 이미 true 로 설정 → 게임 UI 로 클릭 안 떨어짐.
            StartInputBlockGrace();
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
                else if (highlightTarget.StartsWith("item_"))
                {
                    // ROLLBACK_TUTORIAL_ITEM_TARGET_20260622: UIHud 하단 아이템(부스터) 버튼 하이라이트.
                    //   highlightTarget = "item_hand" / "item_shuffle" / "item_remove"(=zap). 컷아웃을 그 버튼에 맞춤.
                    RectTransform itemRt = ResolveItemButtonRect(highlightTarget.Substring("item_".Length));
                    if (itemRt != null)
                    {
                        ShowCutout(itemRt);
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

            // ROLLBACK_TUTORIAL_INSTRUCTION_COLOR_20260622: 스텝별 instruction 텍스트 색상 적용.
            //   useInstructionColor=true 면 그 색, 아니면 프리팹 기본색으로 복원(이전 스텝 override 잔존 방지).
            if (_instructionText != null)
                _instructionText.color = (currentStep != null && currentStep.useInstructionColor)
                    ? currentStep.instructionColor
                    : _defaultInstructionColor;

            ApplyInstructionPanelAlpha(requireAction == "tap_item");

            // ROLLBACK_TUTORIAL_HIDE_SKIP_ON_ITEM_USE_20260622: 스텝별 Skip(X) 노출 토글.
            //   기본 노출, currentStep.hideSkipButton=true(튜토리얼 통한 아이템 사용 강제 스텝) 면 숨김.
            // ROLLBACK_TUTORIAL_HIDE_ALL_SKIP_20260623: 전역으로 모든 Skip 버튼 비노출(사용자 요구).
            //   HIDE_ALL_SKIP_BUTTONS=true 면 스텝 무관 항상 숨김. false 로 두면 위 스텝별 동작으로 환원.
            if (_skipButton != null)
                _skipButton.gameObject.SetActive(
                    !HIDE_ALL_SKIP_BUTTONS && (currentStep == null || !currentStep.hideSkipButton));

            // Handle tap_anywhere action
            bool isTapAnywhere = requireAction == "tap_anywhere";
            bool isTapItem = requireAction == "tap_item";
            // ROLLBACK_TUTORIAL_TAP_ITEM_PASS_THROUGH_20260623:
            // For item button steps, keep the highlight visible but let the real UIHud button
            // receive the click through the Tutorial canvas.
            SetTutorialRaycastBlocking(!isTapItem);
            SetTapAnywherEnabled(isTapAnywhere);
        }

        public void HideVisualsForItemUse()
        {
            // ROLLBACK_TUTORIAL_HIDE_AFTER_TAP_ITEM_20260623:
            // After the HUD item button is tapped, UseItem owns the dim/cutout experience.
            // Keep TutorialController active so PopupUseItem can hide Exit, but remove
            // tutorial text/dim and stop intercepting input.
            HideAllVisuals();
            SetTutorialRaycastBlocking(false);
        }

        private void ApplyInstructionPanelAlpha(bool transparent)
        {
            if (_instructionPanelImage == null)
                return;

            // ROLLBACK_TUTORIAL_ITEM_INSTRUCTION_PANEL_ALPHA_20260623:
            // Item-use tutorial steps keep the text/layout alive but make only the
            // InstructionPanel background image transparent. Other steps restore the
            // prefab-authored alpha so Editor and player builds behave the same.
            Color c = _defaultInstructionPanelColor;
            if (transparent)
                c.a = 0f;
            _instructionPanelImage.color = c;
        }

        /// <summary>ROLLBACK_TUTORIAL_ITEM_TARGET_20260622: UIHud 하단 아이템 버튼 RectTransform 해석 (hand/shuffle/remove|zap).
        ///   UIHud 는 싱글톤이 아니라 FindAnyObjectByType 로 1회 조회(스텝 전환 시점 한정 호출이라 비용 무시).</summary>
        private RectTransform ResolveItemButtonRect(string key)
        {
            var hud = UnityEngine.Object.FindAnyObjectByType<UIHud>();
            if (hud == null) return null;
            Button btn = key == "hand" ? hud.ItemBtnHand
                : key == "shuffle" ? hud.ItemBtnShuffle
                : (key == "remove" || key == "zap") ? hud.ItemBtnRemove
                : null;
            return btn != null ? btn.transform as RectTransform : null;
        }

        private void HandleTutorialCompleted(OnTutorialCompleted evt)
        {
            HideAllVisuals();
            if (_inputBlockCoroutine != null) { StopCoroutine(_inputBlockCoroutine); _inputBlockCoroutine = null; }
            if (_cutoutRaycastFilter != null) _cutoutRaycastFilter.ClearHole();
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

        private void SetTutorialRaycastBlocking(bool active)
        {
            if (_prefabRootCanvasGroup != null)
            {
                _prefabRootCanvasGroup.blocksRaycasts = active;
                _prefabRootCanvasGroup.interactable = active;
            }
            if (_prefabRootRaycaster != null)
                _prefabRootRaycaster.enabled = active;
        }

        #endregion

        #region Private Methods — Cutout Positioning

        /// <summary>
        /// hole 영역을 mat_UICutoutDim shader 의 _CutoutCenter/_CutoutSize 로 전달.
        /// 입력 center 는 canvas-pixel (bottom-left 0..W) — WorldToCanvasPosition 의 출력.
        /// 내부에서 Dim 의 local pivot-centered (-W/2..W/2) 로 변환 후 UpdateCutoutMaterialHole 전달.
        /// _cutoutFrame 은 별도 highlight outline — anchor center 라 local pivot-centered 로 위치 지정.
        /// </summary>
        private void ApplyCutout(Vector2 canvasPixelCenter, Vector2 size)
        {
            _isCutoutVisible = true;

            if (_cutoutMask != null && !_cutoutMask.gameObject.activeSelf)
                _cutoutMask.gameObject.SetActive(true);

            // canvas-pixel (0..W bottom-left) → Dim local pivot-centered (-W/2..W/2).
            Vector2 localCenter = canvasPixelCenter;
            if (_cutoutMask != null)
            {
                Rect r = _cutoutMask.rect;
                localCenter = canvasPixelCenter + new Vector2(r.xMin, r.yMin); // r.xMin = -W/2 → 0 → -W/2 (left edge)
            }

            UpdateCutoutMaterialHole(localCenter, size + new Vector2(CUTOUT_PADDING * 2f, CUTOUT_PADDING * 2f));

            if (_cutoutFrame != null)
            {
                _cutoutFrame.gameObject.SetActive(true);
                _cutoutFrame.anchoredPosition = localCenter;
                _cutoutFrame.sizeDelta = size;
            }

            // raycast hole 은 padding 없는 실제 frame 영역만 (시각적 강조 frame 영역만 클릭 통과).
            if (_cutoutRaycastFilter != null) _cutoutRaycastFilter.SetHole(localCenter, size);
        }

        /// <summary>
        /// hole 위치/크기를 mat_UICutoutDim shader 의 normalized UV (0..1) 로 변환해서 전달.
        /// 입력 좌표계: Dim 의 local pivot-centered (-W/2..W/2). step.cutoutFramePosition (CutoutFrame 의
        /// center-anchored anchoredPosition) 과 동일.
        /// shader: local01 = (vertex - OverlayRect.xy) / OverlayRect.zw. local pivot-centered 라
        /// OverlayRect.xy = (-W/2, -H/2), zw = (W, H) → vertex 0 (center) 시 local01 = 0.5 (UV center).
        /// </summary>
        private void UpdateCutoutMaterialHole(Vector2 localCenter, Vector2 localSize)
        {
            if (_runtimeCutoutDimMaterial == null || _cutoutMask == null) return;
            Rect rect = _cutoutMask.rect;
            if (rect.width <= 0f || rect.height <= 0f) return;

            // local pivot-centered → (0..1) UV.
            float normCx = Mathf.Clamp01((localCenter.x - rect.xMin) / rect.width);
            float normCy = Mathf.Clamp01((localCenter.y - rect.yMin) / rect.height);
            float normSx = Mathf.Clamp01(localSize.x / rect.width);
            float normSy = Mathf.Clamp01(localSize.y / rect.height);

            _runtimeCutoutDimMaterial.SetVector(OVERLAY_RECT_ID, new Vector4(rect.xMin, rect.yMin, rect.width, rect.height));
            _runtimeCutoutDimMaterial.SetVector(CUTOUT_CENTER_ID, new Vector4(normCx, normCy, 0f, 0f));
            _runtimeCutoutDimMaterial.SetVector(CUTOUT_SIZE_ID, new Vector4(normSx, normSy, 0f, 0f));

            // 9-slice: holePx (=localSize, padding 포함) 기준 정규화. border 합이 hole 보다 크면 stretch fallback (셰이더 division-by-tiny 방지).
            Vector4 borderRect = Vector4.zero;
            Vector4 borderSprite = Vector4.zero;
            Vector4 spriteBorder = _currentCutoutSpriteBorder;
            Vector2 spriteSize = _currentCutoutSpriteSize;
            Vector2 holePx = localSize;
            if (spriteBorder.sqrMagnitude > 0.0001f
                && spriteSize.x > 0.001f && spriteSize.y > 0.001f
                && holePx.x > spriteBorder.x + spriteBorder.z
                && holePx.y > spriteBorder.y + spriteBorder.w)
            {
                borderRect = new Vector4(
                    spriteBorder.x / holePx.x, spriteBorder.y / holePx.y,
                    spriteBorder.z / holePx.x, spriteBorder.w / holePx.y);
                borderSprite = new Vector4(
                    spriteBorder.x / spriteSize.x, spriteBorder.y / spriteSize.y,
                    spriteBorder.z / spriteSize.x, spriteBorder.w / spriteSize.y);
            }
            _runtimeCutoutDimMaterial.SetVector(BORDER_RECT_ID, borderRect);
            _runtimeCutoutDimMaterial.SetVector(BORDER_SPRITE_ID, borderSprite);
        }

        /// <summary>
        /// Converts a world position to canvas position (in canvas space coordinates).
        /// Canvas coordinates: (0,0) at bottom-left, (canvasWidth, canvasHeight) at top-right.
        /// [2026-05-21] sizeDelta → rect.size. stretched RectTransform 에선 sizeDelta 가 offset (0,0)
        ///   이라 변환비가 0 으로 깨졌음. rect.size 는 실제 local rect width/height.
        /// </summary>
        private Vector2 WorldToCanvasPosition(Vector3 worldPos)
        {
            Camera cam = Camera.main;
            if (cam == null || _canvasRect == null) return Vector2.zero;

            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
            Vector2 canvasSize = _canvasRect.rect.size;
            if (canvasSize.x <= 0f || canvasSize.y <= 0f) return Vector2.zero;

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
            // dim flag 먼저 끄기 — HideCutout 이 _isDimActive 를 보고 _cutoutMask 비활성 여부를 결정하기 때문.
            _isDimActive = false;
            SetDimColor(0f);

            ResetStepVisualOverrideState();
            HideCutout();
            HideArrow();
            ApplyInstructionPanelAlpha(false);
            HideInstruction();
            SetTapAnywherEnabled(false);
        }

        private void SetDimColor(float alpha)
        {
            // mat_UICutoutDim shader: color.rgb 가 dim tint, alpha 가 dim 강도. RGB 보존, alpha 만 변경.
            if (_runtimeCutoutDimMaterial == null) return;
            var c = _runtimeCutoutDimMaterial.color;
            c.a = alpha;
            _runtimeCutoutDimMaterial.color = c;
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

        // [2026-06-04] 튜토리얼 등장 직후 INPUT_BLOCK_AFTER_SHOW_SECONDS 동안 캔버스 interactable 차단.
        //   timeScale=0 (popup pause) 상황에서도 정확히 동작하도록 WaitForSecondsRealtime 사용.
        //   hole 이 있는 step 에서는 필터가 hole 만 통과시키므로 grace 동안에도 강조 영역은 즉시 클릭 가능.
        private void StartInputBlockGrace()
        {
            if (_inputBlockCoroutine != null) StopCoroutine(_inputBlockCoroutine);
            _inputBlockCoroutine = StartCoroutine(BlockInputForGraceCoroutine());
        }

        private IEnumerator BlockInputForGraceCoroutine()
        {
            if (_prefabRootCanvasGroup != null)
                _prefabRootCanvasGroup.interactable = false;
            // hole 없는 step 에서만 dim 전체 차단 효과 — hole 있는 step 은 filter 가 hole 을 항상 pass-through.
            if (_cutoutRaycastFilter != null) _cutoutRaycastFilter.SetGraceActive(true);
            yield return new WaitForSecondsRealtime(INPUT_BLOCK_AFTER_SHOW_SECONDS);
            if (_prefabRootCanvasGroup != null)
                _prefabRootCanvasGroup.interactable = true;
            if (_cutoutRaycastFilter != null) _cutoutRaycastFilter.SetGraceActive(false);
            _inputBlockCoroutine = null;
        }

        private IEnumerator FadeDimCoroutine(float targetAlpha)
        {
            float startAlpha = _runtimeCutoutDimMaterial != null ? _runtimeCutoutDimMaterial.color.a : 0f;
            float elapsed = 0f;

            while (elapsed < FADE_DURATION)
            {
                // ROLLBACK_DIM_FADE_UNSCALED_20260713: dim 페이드를 unscaledDeltaTime 으로 — rail_warning 튜토가
                //   임계 도달 즉시 PauseManager.Pause()(timeScale=0)로 정지하는데, scaled deltaTime 이면 페이드가
                //   시작하자마자 얼어붙어 dim 이 '뿌옇게(낮은 alpha)' 고정됐다. unscaled 로 정지 중에도 페이드가
                //   완료돼 풀 다크(찐)에 도달. 일반 튜토(timeScale=1)에선 unscaled≈scaled 라 무변화. 롤백: deltaTime 환원.
                elapsed += Time.unscaledDeltaTime;
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
