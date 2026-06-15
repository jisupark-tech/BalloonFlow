using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using Coffee.UIExtensions;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace BalloonFlow
{
    /// <summary>
    /// 아이템 사용 중 팝업.
    /// "Hole in UI" 패턴 — _cutoutMask 에 CutoutMaskUI + Mask 부착, 그 자식 DimOverlay 가
    /// CutoutMask 영역 바깥에만 그려져 구멍 효과. 셰이더 없이 표준 Unity UI 만 사용.
    /// Hand: Queue 영역, Remove: Board 영역.
    /// </summary>
    public class PopupUseItem : UIBase
    {
        private static PopupUseItem _activePopup;

        protected override bool TriggersHudPopupAnimation => false;

        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Item Display]")]
        [SerializeField] private Image _imgItem;
        [SerializeField] private TMP_Text _txtItemDescription;
        [SerializeField] private TMP_Text _txtItemDescriptionOutline;
        [SerializeField] private RectTransform _rtItemDescription;

        [Header("[아이템별 Description 위치 — anchoredPosition]")]
        [SerializeField] private Vector2 _descPosHand = Vector2.zero;
        [SerializeField] private Vector2 _descPosShuffle = Vector2.zero;
        [SerializeField] private Vector2 _descPosZap = Vector2.zero;

        [Header("[Cutout 기준 — 프리팹에서 할당. 자동으로 CutoutMaskUI + Mask + 자식 DimOverlay 추가]")]
        [SerializeField] private RectTransform _cutoutMask;

        [Header("[Cutout Size — boosterType 별 hole sizeDelta]")]
        [Tooltip("Hand (SELECT_TOOL) 시 hole 크기 — 보관함 영역 cover")]
        [SerializeField] private Vector2 _cutoutSizeHand = new Vector2(600f, 400f);
        [Tooltip("Zap (COLOR_REMOVE) 시 hole 크기 — 보드 영역 cover")]
        [SerializeField] private Vector2 _cutoutSizeZap = new Vector2(900f, 1200f);
        [Tooltip("Hand (SELECT_TOOL) CutoutMask anchoredPosition.y")]
        [SerializeField] private float _cutoutAnchoredYHand = -830f;

        [Header("[Buttons]")]
        [SerializeField] private Button _btnBottomExit;
        [SerializeField] private Button _btnExit;

        [Header("[Item Sprites — Inspector에서 할당]")]
        [Tooltip("iconHand.png 드래그")]
        [SerializeField] private Sprite _sprHand;
        [Tooltip("iconSuffle.png 드래그 (파일명 그대로 — typo 유지)")]
        [SerializeField] private Sprite _sprShuffle;
        [Tooltip("iconZap.png 드래그")]
        [SerializeField] private Sprite _sprZap;

        private System.Action _onConfirm;
        private System.Action _onCancel;
        private string _activeBoosterType;

        private const int USEITEM_POPUP_SORTING_BUMP = 20;
        private const float CUTOUT_CONTENT_PADDING = 24f;
        private const float CUTOUT_MIN_CANVAS_HEIGHT = 120f;
        private const float HAND_CUTOUT_Y_OVERRIDE = -830f;

        [Header("[Cutout Materials - shared assets preferred]")]
        [SerializeField] private Material _matCutoutDim;
        private Material _runtimeCutoutDimMaterial;
        private AsyncOperationHandle<Material> _cutoutDimMaterialHandle;
        // Addressables 에는 full asset path 로 등록됨 (audit: 'Assets/3.Material/UICutoutDim.mat')
        // 짧은 alias 'mat_UICutoutDim' 은 미등록.
        private const string CUTOUT_DIM_MATERIAL_ADDRESS = "Assets/3.Material/UICutoutDim.mat";
        private static readonly int OVERLAY_RECT_ID = Shader.PropertyToID("_OverlayRect");
        private static readonly int CUTOUT_CENTER_ID = Shader.PropertyToID("_CutoutCenter");
        private static readonly int CUTOUT_SIZE_ID = Shader.PropertyToID("_CutoutSize");
        private static readonly int CUTOUT_SOFTNESS_ID = Shader.PropertyToID("_CutoutSoftness");

        protected override void Awake()
        {
            base.Awake();
            if (_btnBottomExit != null) _btnBottomExit.onClick.AddListener(OnCancelClicked);
            if (_btnExit != null) _btnExit.onClick.AddListener(OnCancelClicked);
            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.AddListener(OnCancelClicked);

            EnsurePopupSortingCanvas();

            // BottomExit 화면 하단 고정 — Inspector 세팅 누락 대비 anchor 강제 보정
            //EnsureBottomExitAnchor();

            // [2026-05-12] CutoutMask 시스템 재활성 — UI Mask + Stencil 패턴 (부하 최소).
            SetupSimpleCutoutMaterial();

            // 'iconSuffle' 은 atlas 측 의도된 typo
            if (ResourceManager.HasInstance)
            {
                var rm = ResourceManager.Instance;
                _sprHand    = rm.UISpriteOr("iconHand",    _sprHand);
                _sprShuffle = rm.UISpriteOr("iconSuffle",  _sprShuffle);
                _sprZap     = rm.UISpriteOr("iconZap",     _sprZap);
            }
        }

        /// <summary>
        /// BottomExit 버튼이 항상 화면 하단에 고정되도록 RectTransform anchor/pivot 보정.
        /// (ItemDescription 위치는 가변, BottomExit 만 고정.)
        /// </summary>
        private void EnsureBottomExitAnchor()
        {
            if (_btnBottomExit == null) return;
            var rt = _btnBottomExit.transform as RectTransform;
            if (rt == null) return;

            // Bottom-stretch (가로 stretch, 세로 하단 고정)
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot     = new Vector2(0.5f, 0f);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_btnBottomExit != null) _btnBottomExit.onClick.RemoveAllListeners();
            if (_btnExit != null) _btnExit.onClick.RemoveAllListeners();
            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.RemoveAllListeners();

            _btnBottomExitTween?.Kill();
            _topTween?.Kill();

            ReleaseRuntimeMaterial(ref _runtimeCutoutDimMaterial);
            ReleaseAddressableMaterialHandle(ref _cutoutDimMaterialHandle);
        }

        // [2026-05-12] UseItem 사용 시 게임 일시 정지 + Camera Far 증가 + UIHud BottomPanel 비킴.
        // 최적화 보존: Pause 중 모든 update 정지, Far / BottomPanel 도 popup 닫힐 때 원복.
        private bool _paused;
        private Camera _mainCameraCached;
        private float _savedFarClip;
        private const float USEITEM_FAR_CLIP = 1000f;

        private void OnEnable()
        {
            _activePopup = this;

            if (!_paused) { PauseManager.Pause(); _paused = true; }

            // Camera Far 증가
            if (_mainCameraCached == null && CameraManager.HasInstance)
                _mainCameraCached = CameraManager.Instance.MainCamera;
            if (_mainCameraCached == null) _mainCameraCached = Camera.main;
            if (_mainCameraCached != null)
            {
                _savedFarClip = _mainCameraCached.farClipPlane;
                if (_savedFarClip < USEITEM_FAR_CLIP)
                    _mainCameraCached.farClipPlane = USEITEM_FAR_CLIP;
            }

            // [2026-05-13] HUD popup-open 연출은 UIBase.OpenUI() 에서 NotifyPopupOpened 로 중앙 트리거.
            // 직접 PlayPopupOpenAnimation() 을 호출하면 count 가 2 로 올라가 close 시 미일치 발생 → 제거.

            // [2026-05-12] BottomExit 버튼 -200 → 0 tween (위로 등장)
            AnimateBottomExitIn();
            AnimateTopIn();
            SetFxParticlesVisible(true);
        }

        private const float BTN_BOTTOM_EXIT_HIDDEN_Y = -200f;
        private const float BTN_BOTTOM_EXIT_TWEEN_DUR = 0.4f;
        [SerializeField] private RectTransform _BottomExit;
        private Tweener _btnBottomExitTween;

        [Header("[Top Slide Animation]")]
        [Tooltip("팝업 표시 중 위에서 내려오는 Top 오브젝트. Y=600(off) → 0(on) tween.")]
        [SerializeField] private RectTransform _Top;

        [Header("[FX Particles — 팝업 표시 중 항상 보이도록 유지]")]
        [SerializeField] private GameObject _fxLight;
        [SerializeField] private GameObject _fxBackLightR;
        [SerializeField] private GameObject _fxFire;

        private const float TOP_HIDDEN_Y = 600f;
        private const float TOP_SHOWN_Y = 0f;
        private const float TOP_TWEEN_DUR = 0.4f;
        private Tweener _topTween;

        private void AnimateBottomExitIn()
        {
            if (_BottomExit == null) return;

            _btnBottomExitTween?.Kill();
            // 시작 위치 즉시 -200 으로 set (첫 frame 깜빡임 회피 위해 같은 frame 내)
            _BottomExit.anchoredPosition = new Vector2(_BottomExit.anchoredPosition.x, BTN_BOTTOM_EXIT_HIDDEN_Y);
            _btnBottomExitTween = _BottomExit.DOAnchorPosY(0f, BTN_BOTTOM_EXIT_TWEEN_DUR)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true); // PauseManager (timeScale=0) 환경에서도 동작
        }

        /// <summary>BottomExit 0 → -200 tween. 완료 콜백 가능.</summary>
        private void AnimateBottomExitOut(System.Action onComplete)
        {
            if (_BottomExit == null) { onComplete?.Invoke(); return; }

            _btnBottomExitTween?.Kill();
            _btnBottomExitTween = _BottomExit.DOAnchorPosY(BTN_BOTTOM_EXIT_HIDDEN_Y, BTN_BOTTOM_EXIT_TWEEN_DUR)
                .SetEase(Ease.InCubic)
                .SetUpdate(true)
                .OnComplete(() => onComplete?.Invoke());
        }

        private void AnimateTopIn()
        {
            if (_Top == null) return;
            _topTween?.Kill();
            _Top.anchoredPosition = new Vector2(_Top.anchoredPosition.x, TOP_HIDDEN_Y);
            _topTween = _Top.DOAnchorPosY(TOP_SHOWN_Y, TOP_TWEEN_DUR).SetEase(Ease.OutCubic).SetUpdate(true);
        }

        private void AnimateTopOut(System.Action onComplete)
        {
            if (_Top == null) { onComplete?.Invoke(); return; }
            _topTween?.Kill();
            _topTween = _Top.DOAnchorPosY(TOP_HIDDEN_Y, TOP_TWEEN_DUR).SetEase(Ease.InCubic).SetUpdate(true)
                .OnComplete(() => onComplete?.Invoke());
        }

        private void SetFxParticlesVisible(bool visible)
        {
            ResolveFxReferences();
            SetFxParticleVisible(_fxLight, visible);
            SetFxParticleVisible(_fxBackLightR, visible);
            SetFxParticleVisible(_fxFire, visible);
        }

        private void ResolveFxReferences()
        {
            if (_fxLight == null)
                _fxLight = FindDeep(transform, "FX_Light")?.gameObject;
            if (_fxBackLightR == null)
                _fxBackLightR = FindDeep(transform, "FX_BackLightR")?.gameObject;
            if (_fxFire == null)
                _fxFire = FindDeep(transform, "FX_Fire")?.gameObject;
        }

        private static void SetFxParticleVisible(GameObject root, bool visible)
        {
            if (root == null) return;

            root.SetActive(visible);

            UIParticle[] uiParticles = root.GetComponentsInChildren<UIParticle>(true);
            for (int i = 0; i < uiParticles.Length; i++)
            {
                UIParticle uiParticle = uiParticles[i];
                if (uiParticle == null) continue;
                uiParticle.gameObject.SetActive(visible);
                if (visible)
                {
                    uiParticle.RefreshParticles();
                    uiParticle.Clear();
                    uiParticle.Play();
                }
                else
                {
                    uiParticle.Stop();
                }
            }

            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                if (ps == null) continue;

                if (visible)
                {
                    ps.gameObject.SetActive(true);
                    ParticleSystem.MainModule main = ps.main;
                    main.useUnscaledTime = true;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Play(true);
                }
                else
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }

            return null;
        }

        private void OnDisable()
        {
            if (_activePopup == this)
                _activePopup = null;

            if (_paused) { PauseManager.Resume(); _paused = false; }

            // Camera Far 원복 (이전 최적화 보존)
            if (_mainCameraCached != null && _savedFarClip > 0f)
            {
                _mainCameraCached.farClipPlane = _savedFarClip;
                _savedFarClip = 0f;
            }

            // [2026-05-13] HUD popup-close 연출도 UIBase.CloseUI() 에서 NotifyPopupClosed 로 중앙 트리거 → 직접 호출 제거.
        }

        private Sprite _whiteSprite;

        private Sprite GetWhiteSprite()
        {
            if (_whiteSprite == null)
            {
                var tex = new Texture2D(4, 4);
                var pixels = new Color[16];
                for (int i = 0; i < 16; i++) pixels[i] = Color.white;
                tex.SetPixels(pixels);
                tex.Apply();
                _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            }
            return _whiteSprite;
        }

        private void SetupSimpleCutoutMaterial()
        {
            if (_cutoutMask != null)
            {
                // ROLLBACK_USEITEM_SIMPLE_CUTOUT_MATERIAL:
                // UseItem uses one dim Image with mat_UICutoutDim. This RectTransform only
                // supplies the hole position/size; it must not render, mask, or own a child dim.
                Graphic graphic = _cutoutMask.GetComponent<Graphic>();
                if (graphic != null)
                {
                    graphic.enabled = false;
                    graphic.raycastTarget = false;
                    // ROLLBACK_USEITEM_CUTOUT_MARKER_ONLY:
                    // CutoutMask is not the dim Image. Clear any prefab material accidentally
                    // left on it so only Overlay owns UICutoutDim.
                    graphic.material = null;
                }

                Mask mask = _cutoutMask.GetComponent<Mask>();
                if (mask != null) mask.enabled = false;

                SpriteMask spriteMask = _cutoutMask.GetComponent<SpriteMask>();
                if (spriteMask != null) spriteMask.enabled = false;

                // [2026-05-27] PopupUseItem 은 Tutorial 과 흐름이 다름 — dim 처리는 Overlay 가 shader 로 함.
                // CutoutMask 는 marker. SoftMask 부착해도 효과 X (Image.enabled=false, child DimOverlay 비활성).
                // hole 9-slice 가 필요하면 shader 측 _BorderRect/_BorderSprite 전달로 처리 — UpdateCutoutMaterialRect 에서.

                Transform dimOverlay = _cutoutMask.Find("DimOverlay");
                if (dimOverlay != null) dimOverlay.gameObject.SetActive(false);
            }

            ApplyCutoutMaterialToOverlay();
        }

        private void EnsurePopupSortingCanvas()
        {
            // ROLLBACK_USEITEM_POPUP_SORTING_CANVAS:
            // UseItem intentionally shows the board/holders through a shader cutout. Give the
            // popup its own nested sorting canvas so popup controls stay above world renderers
            // and any stale parent-canvas mode/order from scene transitions.
            Canvas parentCanvas = transform.parent != null ? transform.parent.GetComponentInParent<Canvas>() : null;
            Canvas popupCanvas = GetComponent<Canvas>();
            if (popupCanvas == null)
                popupCanvas = gameObject.AddComponent<Canvas>();

            popupCanvas.overrideSorting = true;
            popupCanvas.sortingOrder = (parentCanvas != null ? parentCanvas.sortingOrder : 200) + USEITEM_POPUP_SORTING_BUMP;

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();
        }

        private void EnsurePopupVisualsAboveCutout()
        {
            // ROLLBACK_USEITEM_CUTOUT_SIBLING_GUARD:
            // Dim/Cutout 만 최후방 가드. 그 외 표시 순서(FX/Frame/ImgItem/Desc/Exit/_Top)는 Prefab Hierarchy 가 single source of truth — 코드에서 강제 재정렬하지 않는다.
            // [SSOT 예외] UseItem.prefab 이 binary 직렬화이므로 prefab hierarchy 를 SSOT 로 둘 수 없는 단 하나의 ordering 관계(ImgItem > FX_Light/FX_BackLightR/FX_Fire)에 한해, 이 메서드가 SSOT 다. 그 외 모든 sibling 관계는 여전히 prefab 이 SSOT.
            // [의도] ImageItem (아이템 아이콘) 은 파티클 FX 위에 렌더되어야 한다 (사용자 디자인 요구 2026-06-15).
            RectTransform dimRect = GetBaseDimRectTransform();
            if (dimRect != null) dimRect.SetAsFirstSibling();
            if (_cutoutMask != null) _cutoutMask.SetAsFirstSibling();

            if (_imgItem != null)
            {
                Transform imgT = _imgItem.transform;
                Transform parent = imgT.parent;
                if (parent != null)
                {
                    ResolveFxReferences();
                    int maxFxIndex = -1;
                    if (_fxLight != null && _fxLight.transform.parent == parent)
                        maxFxIndex = Mathf.Max(maxFxIndex, _fxLight.transform.GetSiblingIndex());
                    if (_fxBackLightR != null && _fxBackLightR.transform.parent == parent)
                        maxFxIndex = Mathf.Max(maxFxIndex, _fxBackLightR.transform.GetSiblingIndex());
                    if (_fxFire != null && _fxFire.transform.parent == parent)
                        maxFxIndex = Mathf.Max(maxFxIndex, _fxFire.transform.GetSiblingIndex());
                    if (maxFxIndex >= 0 && imgT.GetSiblingIndex() <= maxFxIndex)
                        imgT.SetSiblingIndex(maxFxIndex + 1);
                }
            }
        }

        private bool ApplyCutoutMaterialToOverlay()
        {
            if (_runtimeCutoutDimMaterial == null)
                _runtimeCutoutDimMaterial = CreateCutoutRuntimeMaterial(
                    _matCutoutDim,
                    CUTOUT_DIM_MATERIAL_ADDRESS,
                    "UI/CutoutDim",
                    ref _cutoutDimMaterialHandle);

            if (_runtimeCutoutDimMaterial == null) return false;

            _runtimeCutoutDimMaterial.color = new Color(0f, 0f, 0f, 143f / 255f); // Overlay alpha = 143/255 (디자인 요구)
            _runtimeCutoutDimMaterial.SetFloat(CUTOUT_SOFTNESS_ID, 0.001f);

            RectTransform dimRect = GetBaseDimRectTransform();
            if (dimRect == null) return false;

            // ROLLBACK_USEITEM_OVERLAY_ONLY_CUTOUT_DIM:
            // Apply the cutout dim material only to Overlay's own Image. Do not walk children,
            // because CutoutMask is only a rect marker and must not receive the dim material.
            Image overlayImage = dimRect.GetComponent<Image>();
            if (overlayImage == null) return false;

            if (overlayImage.sprite == null)
                overlayImage.sprite = GetWhiteSprite();
            overlayImage.type = Image.Type.Simple;
            overlayImage.raycastTarget = true;
            overlayImage.color = Color.white;
            overlayImage.material = _runtimeCutoutDimMaterial;
            return true;
        }

        private void SetupShaders()
        {
#if false
            if (_cutoutMask == null) return;

            // CutoutMaskUI 컴포넌트 보장 — 기존 Image 가 있으면 교체.
            // CutoutMaskUI 는 Image 를 상속하므로 GetComponent<Image>() 로도 잡힘.
            var existingImage = _cutoutMask.GetComponent<Image>();
            CutoutMaskUI cutout = _cutoutMask.GetComponent<CutoutMaskUI>();
            if (cutout == null)
            {
                if (existingImage != null && !(existingImage is CutoutMaskUI))
                    DestroyImmediate(existingImage);
                cutout = _cutoutMask.gameObject.AddComponent<CutoutMaskUI>();
            }
            _cutoutImage = cutout;
            if (cutout.sprite == null) cutout.sprite = GetWhiteSprite();
            cutout.type = Image.Type.Simple;
            // alpha=0 으로 본체는 보이지 않게 — geometry 만 stencil 기록용. dim child 가 mask 영역 밖만 그려져 hole 효과
            // CutoutMask shader uses ColorMask 0, so it stays invisible while writing stencil.
            cutout.color = Color.white;
            cutout.raycastTarget = false;

            // Mask 컴포넌트 보장. CutoutMaskUI 가 stencil-invert 처리 → 자식 dim 이 mask 영역 "밖" 만 그림.
            // showMaskGraphic 은 true 여야 stencil 이 정상 기록됨 (false 면 ColorMask 0 으로 stencil 도 불안정).
            // CutoutMaskUI 의 color 는 white 지만 mask 영역은 dim child 에 의해 가려지지 않으므로 결과적으로 투명한 hole 처럼 보임.
            var mask = _cutoutMask.GetComponent<Mask>();
            if (mask == null) mask = _cutoutMask.gameObject.AddComponent<Mask>();
            // ROLLBACK_USEITEM_CUTOUT_MASK_ENABLE:
            // Tutorial uses this same CutoutMaskUI + Mask pattern. The Mask must stay enabled
            // and showMaskGraphic must be true so the cutout image writes stencil before the
            // child DimOverlay draws only outside the hole.
            mask.enabled = true;
            mask.showMaskGraphic = true;

            // DimOverlay: CutoutMask 의 자식 — 부모 Mask 영역 "바깥" 에만 그려져 dim 효과
            Transform existingDim = _cutoutMask.Find("DimOverlay");
            GameObject dimGO;
            if (existingDim != null)
            {
                dimGO = existingDim.gameObject;
                _dimImage = dimGO.GetComponent<Image>();
                if (_dimImage == null) _dimImage = dimGO.AddComponent<Image>();
            }
            else
            {
                dimGO = new GameObject("DimOverlay", typeof(RectTransform), typeof(Image));
                dimGO.transform.SetParent(_cutoutMask, false);
                _dimImage = dimGO.GetComponent<Image>();
            }

            // 부모(_cutoutMask)가 작아도 자식이 화면 전체를 덮도록 절대 크기로 설정.
            // CutoutMaskUI 의 stencil-invert 로 cutoutMask 영역 "밖"에만 렌더 → 구멍 + 전체 dim.
            var dimRT = dimGO.GetComponent<RectTransform>();
            if(dimRT !=null)
            {
                dimRT.anchorMin = new Vector2(0.5f, 0.5f);
                dimRT.anchorMax = new Vector2(0.5f, 0.5f);
                dimRT.pivot     = new Vector2(0.5f, 0.5f);
                dimRT.anchoredPosition = Vector2.zero;
                dimRT.sizeDelta = new Vector2(10000f, 10000f);
            }

            _dimImage.sprite = GetWhiteSprite();
            _dimImage.type = Image.Type.Simple;
            _dimImage.raycastTarget = true;  // dim 영역에서 클릭 차단
            _dimImage.color = new Color(0f, 0f, 0f, 0.7f);
            ApplyCutoutMaterials();

            dimGO.SetActive(false);
            _cutoutImage.gameObject.SetActive(false);
#endif
        }

        private void ApplyCutoutMaterials()
        {
#if false
            if (_cutoutImage != null)
            {
                if (_runtimeCutoutMaskMaterial == null)
                    _runtimeCutoutMaskMaterial = CreateCutoutRuntimeMaterial(
                        _matCutoutMask,
                        CUTOUT_MASK_MATERIAL_ADDRESS,
                        "UI/CutoutMask",
                        ref _cutoutMaskMaterialHandle);
                _cutoutImage.material = _runtimeCutoutMaskMaterial;
            }

            if (_dimImage != null)
            {
                if (_runtimeCutoutDimMaterial == null)
                    _runtimeCutoutDimMaterial = CreateCutoutRuntimeMaterial(
                        _matCutoutDim,
                        CUTOUT_DIM_MATERIAL_ADDRESS,
                        "UI/CutoutDim",
                        ref _cutoutDimMaterialHandle);
                if (_runtimeCutoutDimMaterial != null)
                    _runtimeCutoutDimMaterial.color = new Color(0f, 0f, 0f, 0.7f);
                _dimImage.material = _runtimeCutoutDimMaterial;
            }
#endif
        }

        private static Material CreateCutoutRuntimeMaterial(
            Material assignedMaterial,
            string addressableKey,
            string shaderName,
            ref AsyncOperationHandle<Material> handle)
        {
            // ROLLBACK_USEITEM_ADDRESSABLE_CUTOUT_MATERIALS:
            // Use the authored Addressable materials (mat_UICutoutMask / mat_UICutoutDim).
            // Clone once for runtime color changes; do not create duplicate Resources materials.
            // Addressables are intentionally preferred over Inspector fields because old prefab
            // assignments can accidentally point both Mask and Dim at UICutoutDim.mat.
            Material source = LoadAddressableCutoutMaterial(addressableKey, ref handle);
            if (source == null && IsExpectedCutoutMaterial(assignedMaterial, shaderName))
                source = assignedMaterial;
            if (source != null)
                return new Material(source);

            Shader shader = Shader.Find(shaderName);
            return shader != null ? new Material(shader) : null;
        }

        private static bool IsExpectedCutoutMaterial(Material material, string shaderName)
        {
            return material != null &&
                   material.shader != null &&
                   material.shader.name == shaderName;
        }

        private static Material LoadAddressableCutoutMaterial(
            string addressableKey,
            ref AsyncOperationHandle<Material> handle)
        {
            if (handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded)
                return handle.Result;

            try
            {
                handle = Addressables.LoadAssetAsync<Material>(addressableKey);
                Material material = handle.WaitForCompletion();
                if (material != null) return material;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PopupUseItem] Addressable material '{addressableKey}' load failed: {e.Message}");
            }

            if (handle.IsValid())
                Addressables.Release(handle);
            handle = default;
            return null;
        }

        private void SetHudBottomPanelHidden(bool hidden)
        {
            UIHud hud = UIManager.HasInstance ? UIManager.Instance.GetOpenUI<UIHud>() : null;
            if (hud == null)
                hud = FindAnyObjectByType<UIHud>();
            if (hud == null) return;

            if (hidden) hud.HideBottomPanel();
            else hud.ShowBottomPanel();
        }

        private static void ReleaseRuntimeMaterial(ref Material material)
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
            material = null;
        }

        private static void ReleaseAddressableMaterialHandle(ref AsyncOperationHandle<Material> handle)
        {
            if (handle.IsValid())
                Addressables.Release(handle);
            handle = default;
        }

        public void Show(string boosterType, string description,
                         System.Action onConfirm = null, System.Action onCancel = null)
        {
            _onConfirm = onConfirm;
            _onCancel = onCancel;
            _activeBoosterType = boosterType;

            if (_frame != null)
            {
                _frame.SetTitle("Use Item");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.None);
                _frame.ShowExitButton(false);
            }

            if (_imgItem != null)
            {
                Sprite spr = GetBoosterSprite(boosterType);
                if (spr != null) _imgItem.sprite = spr;
            }

            if (_txtItemDescription != null) _txtItemDescription.text = description;
            if (_txtItemDescriptionOutline != null) _txtItemDescriptionOutline.text = description;

            // 아이템별 Description 위치 적용
            if (_rtItemDescription != null)
            {
                _rtItemDescription.anchoredPosition = boosterType switch
                {
                    BoosterManager.SELECT_TOOL  => _descPosHand,
                    BoosterManager.SHUFFLE      => _descPosShuffle,
                    BoosterManager.COLOR_REMOVE => _descPosZap,
                    _                           => _rtItemDescription.anchoredPosition
                };
            }

            // [2026-05-12] CutoutMask 재활성 — Hand: 보관함 / Zap: 보드 영역 hole + 외부 dim.
            // [2026-05-20] ROLLBACK_USEITEM_SIMPLE_CUTOUT_MATERIAL:
            // UseItem uses one dim Image with mat_UICutoutDim. CutoutMask only supplies
            // the hole rect, so it must not render, mask, or own a second dim layer.
            EnsurePopupSortingCanvas();
            if (_cutoutMask != null) _cutoutMask.gameObject.SetActive(true);
            SetupCutout(boosterType);
            ClampCutoutAwayFromPopupContent();
            ApplyHandCutoutYOverride(boosterType);
            ApplyCutoutMaterialToOverlay();

            // [2026-05-13] description/icon 가림 방지 — Dim/Cutout 활성화 후 sibling 순서를
            // 가장 마지막으로 옮겨 Dim alpha 0.7 위로 덮어 그림. Mask 의 자식이라면
            // popup root 로 reparent 해 stencil 클리핑 회피.
            BringDescriptionToFront();
            EnsurePopupVisualsAboveCutout();

            OpenUI();
            Canvas.ForceUpdateCanvases();
            UpdateCutoutMaterialRect();
            SetHudBottomPanelHidden(true);
            _onConfirm?.Invoke();
        }

        private void BringDescriptionToFront()
        {
            // Prefab Hierarchy 순서 보존 — cutoutMask 의 자식이면 stencil 클리핑 회피를 위해서만 reparent. sibling index 는 prefab 그대로.
            Transform popupRoot = transform;
            if (_rtItemDescription != null)
            {
                if (_cutoutMask != null && _rtItemDescription.IsChildOf(_cutoutMask))
                    _rtItemDescription.SetParent(popupRoot, false);
            }
            if (_imgItem != null)
            {
                var iconRT = _imgItem.transform as RectTransform;
                if (iconRT != null)
                {
                    if (_cutoutMask != null && iconRT.IsChildOf(_cutoutMask))
                        iconRT.SetParent(popupRoot, false);
                }
            }
        }

        private void SetupCutout(string boosterType)
        {
            if (_cutoutMask == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;

            // [2026-05-12] boosterType 별 sizeDelta + 위치 변경. SerializeField 로 Inspector 조정 가능.
            if (boosterType == BoosterManager.SELECT_TOOL)
            {
                if (HolderVisualManager.HasInstance)
                {
                    Vector3 queueCenter = HolderVisualManager.Instance.CalculateQueueCenterPosition();
                    SetCutoutScreenArea(cam, queueCenter, _cutoutSizeHand);
                    // [2026-05-27] Hand cutout Y fixed to -830 per design
                    Vector2 handPos = _cutoutMask.anchoredPosition;
                    handPos.y = HAND_CUTOUT_Y_OVERRIDE;
                    _cutoutMask.anchoredPosition = handPos;
                }
            }
            else if (boosterType == BoosterManager.COLOR_REMOVE)
            {
                if (GameManager.HasInstance)
                {
                    var board = GameManager.Instance.Board;
                    Vector3 boardCenter = new Vector3(board.boardCenterX, 0f, board.boardCenterZ);
                    SetCutoutScreenArea(cam, boardCenter, _cutoutSizeZap);
                }
            }
        }

        /// <summary>world center → cutoutMask anchoredPosition + sizeDelta.
        /// [2026-05-13] sizeDelta 단위 fix — RectTransformUtility 로 산출한 localCenter 는 canvas-units 인데
        /// 기존 코드는 sizeDelta 에 screen-pixel 값을 그대로 박아 CanvasScaler reference 와 device 해상도가
        /// 다를 때 hole 크기가 부정확했음. Tutorial.WorldToCanvasPosition 패턴(canvas/screen 비율) 적용으로
        /// position 과 size 둘 다 canvas-units 로 일관성 확보.</summary>
        private void ApplyHandCutoutYOverride(string boosterType)
        {
            if (_cutoutMask == null || boosterType != BoosterManager.SELECT_TOOL) return;
            Vector2 pos = _cutoutMask.anchoredPosition;
            pos.y = _cutoutAnchoredYHand;
            _cutoutMask.anchoredPosition = pos;
            UpdateCutoutMaterialRect();
        }

        private void ClampCutoutAwayFromPopupContent()
        {
            if (_cutoutMask == null) return;

            RectTransform dimRect = GetBaseDimRectTransform();
            if (dimRect == null) return;

            float protectedTop = float.NegativeInfinity;
            IncludeProtectedTop(dimRect, _imgItem != null ? _imgItem.transform as RectTransform : null, ref protectedTop);
            IncludeProtectedTop(dimRect, _rtItemDescription, ref protectedTop);
            IncludeProtectedTop(dimRect, _btnExit != null ? _btnExit.transform as RectTransform : null, ref protectedTop);
            IncludeProtectedTop(dimRect, _btnBottomExit != null ? _btnBottomExit.transform as RectTransform : null, ref protectedTop);
            IncludeProtectedTop(dimRect, _BottomExit, ref protectedTop);

            if (float.IsNegativeInfinity(protectedTop)) return;

            Bounds cutoutBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(dimRect, _cutoutMask);
            float safeBottom = protectedTop + CUTOUT_CONTENT_PADDING;
            if (cutoutBounds.min.y >= safeBottom || cutoutBounds.max.y <= safeBottom)
                return;

            float trim = safeBottom - cutoutBounds.min.y;
            float newHeight = Mathf.Max(CUTOUT_MIN_CANVAS_HEIGHT, _cutoutMask.sizeDelta.y - trim);
            float appliedTrim = _cutoutMask.sizeDelta.y - newHeight;
            if (appliedTrim <= 0.01f) return;

            // ROLLBACK_USEITEM_CUTOUT_CONTENT_CLAMP:
            // Keep the transparent hole away from UseItem's own icon/text/close controls.
            // Field objects should show through only the intended board/holder area, not the
            // popup content area, otherwise they appear to render above the popup.
            _cutoutMask.sizeDelta = new Vector2(_cutoutMask.sizeDelta.x, newHeight);
            _cutoutMask.anchoredPosition = new Vector2(
                _cutoutMask.anchoredPosition.x,
                _cutoutMask.anchoredPosition.y + appliedTrim * 0.5f);
        }

        private static void IncludeProtectedTop(RectTransform space, RectTransform target, ref float protectedTop)
        {
            if (space == null || target == null || !target.gameObject.activeInHierarchy)
                return;

            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(space, target);
            protectedTop = Mathf.Max(protectedTop, bounds.max.y);
        }

        private void SetCutoutScreenArea(Camera cam, Vector3 worldCenter, Vector2 screenSize)
        {
            Vector3 screen = cam.WorldToScreenPoint(worldCenter);

            // ROLLBACK_USEITEM_PARENT_CANVAS_COORDS:
            // PopupUseItem owns a nested sorting canvas, but cutout coordinates must still be
            // calculated against the full popup canvas. Using the nested canvas can skew the
            // hole if the popup root is not exactly the same rect as its parent canvas.
            Canvas canvas = transform.parent != null
                ? transform.parent.GetComponentInParent<Canvas>()
                : GetComponentInParent<Canvas>();
            if (canvas == null)
                canvas = GetComponentInParent<Canvas>();
            Camera canvasCam = (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera)
                ? canvas.worldCamera : null;
            RectTransform canvasRT = canvas != null ? canvas.GetComponent<RectTransform>() : null;
            if (canvasRT == null) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screen, canvasCam, out Vector2 localCenter);
            _cutoutMask.anchoredPosition = localCenter;

            // screen-pixel size → canvas-units size. Tutorial 의 WorldToCanvasPosition 과 동일 비율.
            // canvasRT.rect.size 는 reference resolution 기반 (CanvasScaler 가 scale factor 분리).
            // Screen.width/height 가 0 인 경우 (편집기 첫 frame 등) 변환 skip → screenSize 그대로 폴백.
            float sw = Screen.width;
            float sh = Screen.height;
            if (sw > 1f && sh > 1f)
            {
                Vector2 canvasSize = canvasRT.rect.size;
                float ratioX = canvasSize.x / sw;
                float ratioY = canvasSize.y / sh;
                _cutoutMask.sizeDelta = new Vector2(screenSize.x * ratioX, screenSize.y * ratioY);
            }
            else
            {
                _cutoutMask.sizeDelta = screenSize;
            }

            UpdateCutoutMaterialRect();
        }

        private void UpdateCutoutMaterialRect()
        {
            if (_runtimeCutoutDimMaterial == null || _cutoutMask == null) return;

            Canvas.ForceUpdateCanvases();
            RectTransform dimRect = GetBaseDimRectTransform();
            if (dimRect == null) return;

            Rect rect = dimRect.rect;
            if (rect.width <= 0f || rect.height <= 0f) return;

            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(dimRect, _cutoutMask);
            Vector2 center = new Vector2(
                Mathf.InverseLerp(rect.xMin, rect.xMax, bounds.center.x),
                Mathf.InverseLerp(rect.yMin, rect.yMax, bounds.center.y));
            Vector2 size = new Vector2(
                Mathf.Clamp01(bounds.size.x / rect.width),
                Mathf.Clamp01(bounds.size.y / rect.height));

            _runtimeCutoutDimMaterial.SetVector(OVERLAY_RECT_ID, new Vector4(rect.xMin, rect.yMin, rect.width, rect.height));
            _runtimeCutoutDimMaterial.SetVector(CUTOUT_CENTER_ID, new Vector4(center.x, center.y, 0f, 0f));
            _runtimeCutoutDimMaterial.SetVector(CUTOUT_SIZE_ID, new Vector4(size.x, size.y, 0f, 0f));
        }

        private void OnCancelClicked()
        {
            // ROLLBACK_USEITEM_CLOSE_INPUT_SUPPRESS:
            // Close buttons overlap active gameplay colliders. Swallow only this close input,
            // regardless of item type; normal Zap/Hand cutout clicks remain allowed.
            if (InputHandler.HasInstance)
                InputHandler.Instance.SuppressInput(0.15f);

            _onCancel?.Invoke();

            if (BoosterExecutor.HasInstance)
            {
                bool hadPendingBooster = BoosterExecutor.Instance.HasPendingBooster;
                BoosterExecutor.Instance.CancelPendingBooster();

                if (hadPendingBooster)
                {
                    _activeBoosterType = null;
                    return;
                }
            }

            _activeBoosterType = null;

            HideOverlay();
            CloseUI();
        }

        /// <summary>Cutout/Dim overlay 비활성화. Cancel 및 자동 close (BoosterExecutor.CloseUseItemPopup) 모두에서 호출.</summary>
        public static bool IsScreenPointOverActiveCloseButton(Vector2 screenPosition)
        {
            return _activePopup != null
                && _activePopup.isActiveAndEnabled
                && _activePopup.IsScreenPointOverCloseButton(screenPosition);
        }

        private bool IsScreenPointOverCloseButton(Vector2 screenPosition)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera canvasCamera = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera
                ? canvas.worldCamera
                : null;

            return IsScreenPointOverButton(_btnBottomExit, screenPosition, canvasCamera)
                || IsScreenPointOverButton(_btnExit, screenPosition, canvasCamera)
                || (_frame != null && IsScreenPointOverButton(_frame.BtnExit, screenPosition, canvasCamera));
        }

        private static bool IsScreenPointOverButton(Button button, Vector2 screenPosition, Camera canvasCamera)
        {
            if (button == null || !button.gameObject.activeInHierarchy)
                return false;

            RectTransform rect = button.transform as RectTransform;
            return rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPosition, canvasCamera);
        }

        private void HideOverlay()
        {
            if (_cutoutMask != null) _cutoutMask.gameObject.SetActive(false);
            SetHudBottomPanelHidden(false);
        }

        public override void CloseUI()
        {
            CloseUI(true);
        }

        public void CloseUI(bool restoreBottomPanel)
        {
            // UIBase.CloseUI()는 alpha=0 만 처리 → OnDisable이 fire 안 됨.
            // BoosterExecutor 자동 close 경로에서도 overlay 잔존 방지.
            HideOverlay();
            if (restoreBottomPanel)
                SetHudBottomPanelHidden(false);
            // [2026-05-12] BottomExit 0 → -200 tween + 완료 후 base.CloseUI 호출.
            SetFxParticlesVisible(false);
            AnimateTopOut(null);
            AnimateBottomExitOut(() => base.CloseUI());
        }

        public Sprite GetBoosterSprite(string boosterType)
        {
            Sprite spr = boosterType switch
            {
                BoosterManager.SELECT_TOOL  => _sprHand,
                BoosterManager.SHUFFLE      => _sprShuffle,
                BoosterManager.COLOR_REMOVE => _sprZap,
                _                           => null
            };

            if (spr == null && !string.IsNullOrEmpty(boosterType))
            {
                string filename = boosterType switch
                {
                    BoosterManager.SELECT_TOOL  => "iconHand.png",
                    BoosterManager.SHUFFLE      => "iconSuffle.png",
                    BoosterManager.COLOR_REMOVE => "iconZap.png",
                    _                           => "(unknown)"
                };
                Debug.LogWarning($"[PopupUseItem] '{boosterType}' Sprite 미할당. " +
                                 $"Inspector 에서 {filename} 드래그 필요. " +
                                 "(Assets/2.Sprite/UI/ 위치)");
            }

            return spr;
        }
    }
}
