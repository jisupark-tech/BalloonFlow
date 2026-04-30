using UnityEngine;
using UnityEngine.UI;
using TMPro;

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

        private Image _dimImage;
        private Image _cutoutImage;

        protected override void Awake()
        {
            base.Awake();
            if (_btnBottomExit != null) _btnBottomExit.onClick.AddListener(OnCancelClicked);
            if (_btnExit != null) _btnExit.onClick.AddListener(OnCancelClicked);
            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.AddListener(OnCancelClicked);

            // BottomExit 화면 하단 고정 — Inspector 세팅 누락 대비 anchor 강제 보정
            EnsureBottomExitAnchor();

            SetupShaders();

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

        private void SetupShaders()
        {
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
            cutout.color = new Color(1f, 1f, 1f, 0f);
            cutout.raycastTarget = false;

            // Mask 컴포넌트 보장. CutoutMaskUI 가 stencil-invert 처리 → 자식 dim 이 mask 영역 "밖" 만 그림.
            // showMaskGraphic 은 true 여야 stencil 이 정상 기록됨 (false 면 ColorMask 0 으로 stencil 도 불안정).
            // CutoutMaskUI 의 color 는 white 지만 mask 영역은 dim child 에 의해 가려지지 않으므로 결과적으로 투명한 hole 처럼 보임.
            var mask = _cutoutMask.GetComponent<Mask>();
            if (mask == null) mask = _cutoutMask.gameObject.AddComponent<Mask>();
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
            dimRT.anchorMin = new Vector2(0.5f, 0.5f);
            dimRT.anchorMax = new Vector2(0.5f, 0.5f);
            dimRT.pivot     = new Vector2(0.5f, 0.5f);
            dimRT.anchoredPosition = Vector2.zero;
            dimRT.sizeDelta = new Vector2(10000f, 10000f);

            _dimImage.sprite = GetWhiteSprite();
            _dimImage.type = Image.Type.Simple;
            _dimImage.raycastTarget = true;  // dim 영역에서 클릭 차단
            _dimImage.color = new Color(0f, 0f, 0f, 0.7f);

            dimGO.SetActive(false);
            _cutoutImage.gameObject.SetActive(false);
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

            // Cutout 위치 설정
            SetupCutout(boosterType);

            // Dim + Cutout 활성화
            if (_cutoutImage != null) _cutoutImage.gameObject.SetActive(true);
            if (_dimImage != null) _dimImage.gameObject.SetActive(true);

            OpenUI();
            _onConfirm?.Invoke();
        }

        private void SetupCutout(string boosterType)
        {
            if (_cutoutMask == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;

            if (boosterType == BoosterManager.SELECT_TOOL)
            {
                if (HolderVisualManager.HasInstance)
                {
                    Vector3 queueCenter = HolderVisualManager.Instance.CalculateQueueCenterPosition();
                    SetCutoutWorldArea(cam, queueCenter, new Vector2(6f, 4f));
                }
            }
            else if (boosterType == BoosterManager.COLOR_REMOVE)
            {
                if (GameManager.HasInstance)
                {
                    var board = GameManager.Instance.Board;
                    Vector3 boardCenter = new Vector3(board.boardCenterX, 0f, board.boardCenterZ);
                    SetCutoutWorldArea(cam, boardCenter, new Vector2(
                        BoardTileManager.CONVEYOR_WIDTH, BoardTileManager.CONVEYOR_HEIGHT));
                }
            }
        }

        private void SetCutoutWorldArea(Camera cam, Vector3 worldCenter, Vector2 worldSize)
        {
            Vector3 bl = cam.WorldToScreenPoint(worldCenter - new Vector3(worldSize.x * 0.5f, 0f, worldSize.y * 0.5f));
            Vector3 tr = cam.WorldToScreenPoint(worldCenter + new Vector3(worldSize.x * 0.5f, 0f, worldSize.y * 0.5f));

            Canvas canvas = GetComponentInParent<Canvas>();
            Camera canvasCam = (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera)
                ? canvas.worldCamera : null;
            RectTransform canvasRT = canvas != null ? canvas.GetComponent<RectTransform>() : null;
            if (canvasRT == null) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, bl, canvasCam, out Vector2 localBL);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, tr, canvasCam, out Vector2 localTR);

            Vector2 center = (localBL + localTR) * 0.5f;
            Vector2 size = new Vector2(Mathf.Abs(localTR.x - localBL.x), Mathf.Abs(localTR.y - localBL.y));
            size += new Vector2(40f, 40f);

            _cutoutMask.anchoredPosition = center;
            _cutoutMask.sizeDelta = size;
        }

        private void OnCancelClicked()
        {
            if (BoosterExecutor.HasInstance)
                BoosterExecutor.Instance.CancelPendingBooster();

            _onCancel?.Invoke();
            _activeBoosterType = null;

            HideOverlay();
            CloseUI();
        }

        /// <summary>Cutout/Dim overlay 비활성화. Cancel 및 자동 close (BoosterExecutor.CloseUseItemPopup) 모두에서 호출.</summary>
        private void HideOverlay()
        {
            if (_cutoutImage != null) _cutoutImage.gameObject.SetActive(false);
            if (_dimImage != null) _dimImage.gameObject.SetActive(false);
        }

        public override void CloseUI()
        {
            // UIBase.CloseUI()는 alpha=0 만 처리 → OnDisable이 fire 안 됨.
            // BoosterExecutor 자동 close 경로에서도 overlay 잔존 방지.
            HideOverlay();
            base.CloseUI();
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
