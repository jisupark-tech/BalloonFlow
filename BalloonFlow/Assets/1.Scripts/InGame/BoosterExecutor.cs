using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using DG.Tweening;
using DigitalRuby.LightningBolt;

namespace BalloonFlow
{
    /// <summary>
    /// Executes actual game effects when a booster is used.
    /// BoosterManager handles inventory; this handles gameplay logic.
    /// Design ref: 아웃게임디렉션 §부스터
    ///   Select Tool — 큐에서 원하는 보관함 선택 배치
    ///   Shuffle — 큐 보관함 순서 랜덤 셔플
    ///   Color Remove — 필드+레일+큐에서 지정 색상 전체 제거
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Handler | Phase: 3
    /// </remarks>
    public class BoosterExecutor : SceneSingleton<BoosterExecutor>
    {
        #region Fields

        private bool _awaitingColorSelection;
        private bool _awaitingHolderSelection;
        private bool _awaitingBalloonClick;

        /// <summary>Tracks which booster type is pending user interaction (for deferred consumption).</summary>
        private string _pendingBoosterType;

        /// <summary>부스터 취소 버튼 (런타임 생성).</summary>
        private GameObject _cancelButtonGO;

        /// <summary>[Optimization 2026-05-10] Canvas 캐시. ShowCancelButton 의 FindAnyObjectByType 가 매 booster 활성화 시 발생하는 O(scene) lookup 제거.
        /// 씬 재로드로 destroyed 되면 lazy 재fetch 됨. 롤백: 이 필드 제거 + ShowCancelButton 의 FindAnyObjectByType 직접 호출 라인 복원.</summary>
        private Canvas _cachedCanvas;

        private const float ZapSelectionHighlightDelay = 0.15f;
        private const float ZapAppearDuration = 0.45f;
        private const float ZapMoveDuration = 0.25f;
        private const float ZapMaxTotalEffectDuration = 2f;
        private const float ZapLineLifetime = 0.2f;
        private const float ZapMinLeadInterval = 0.03f;
        private const float ZapLineLeadBeforePop = 0.015f;
        private const float ZapFinishLifetime = 0.6f;
        private const float ZapFinishTransitionGrace = 0.4f;
        private const float ZapFinishPostPlayBuffer = 0.05f;
        private const float ZapEffectYOffset = 0.12f;

        // Hand 부스터 사용 시 카메라가 보관함 큐의 앞쪽 몇 개 행을 보여줄지 (5줄 요구).
        private const int HAND_VISIBLE_ROWS = 5;
        private static readonly Vector3 ZapSpawnPosition = new Vector3(-0.1911252f, 1.95f, -7.79f);
        private const float ZapLineWorldLift = 0.35f;
        // FxZapLine 끝점(LightningEnd) 의 월드 Y 고정값. 시작점 Y=1.95 대비 0.45 만 아래(1.5) 로 살짝 떨어뜨려 흘러내림 느낌만 표현, 전체 Y 흔들림은 LockYAxis=true 로 여전히 고정.
        private const float ZapLineEndYWorld = 1.5f;
        private const float ZapLineMinWidth = 0.08f;
        private const int ZapLineSortingOrder = 80;
        // ROLLBACK_ZAP_LINE_ALWAYS_ON_TOP_20260610: renderQueue 강제 상향. Overlay+ 영역으로 띄워 풍선 mesh 뒤로 가려지지 않게 한다.
        private const int ZapLineRenderQueue = 4000;
        private const float ZapFieldBottomPaddingCells = 1.5f;
        private const int ZapLineConcurrentCount = 4;
        // 번개 라인이 활성화된 동안 매 tick 마다 끝점에 미세 jitter 를 더해 LightningBoltScript.Trigger() 를 재호출해 자글거리게 만든다.
        private const float ZapLineJiggleMinInterval = 0.03f;
        private const float ZapLineJiggleMaxInterval = 0.06f;
        private const float ZapLineJiggleEndpointJitter = 0.07f;

        // Fade in/out 연출 — 라인이 뚝 끊겨 보이지 않도록 width+alpha 를 함께 보간한다.
        private const float ZapLineFadeInDuration = 0.07f;   // 사용자 사양 0.05~0.1s 중간값
        // 사용자 사양 0.08~0.15s 의 중간 안전값 — width/alpha 0 수렴이 끝나는 길이. (start/end 위치는 종료까지 유지)
        private const float ZapLineFadeOutDuration = 0.12f;

        private readonly List<ZapTarget> _zapTargets = new List<ZapTarget>(128);
        private GameObject _itemZapPrefab;
        private GameObject _fxZapLinePrefab;
        private GameObject _fxZapLine2Prefab;
        private bool _isColorRemoveSequenceRunning;
        private bool _isZapAnimationPlaying = false;
        private Animator _zapAnimator = null;

        // FxZapLine 자글거림(번개 재트리거) 코루틴. ConfigureZapLine 이 마지막에 설정한 baseline 을 읽고
        // jitter 를 더해 매 tick LightningBoltScript.Trigger() 를 호출한다. 정리(라인 SetActive(false)/Destroy)
        // 직전과 OnDisable 에서 반드시 중지하여 누수/이중 호출을 막는다.
        private Coroutine _zapLineJiggleCo;
        private readonly Dictionary<LightningBoltScript, ZapLineBaseline> _zapLineBaselines = new Dictionary<LightningBoltScript, ZapLineBaseline>(8);

        // 라인별 목표 widthMultiplier(프리팹의 원본 값). 첫 ConfigureZapLine 호출 시 캡처 → 페이드인의 보간 타깃이 된다.
        private readonly Dictionary<LineRenderer, float> _zapLineTargetWidths = new Dictionary<LineRenderer, float>(8);
        // 진행 중인 fade 코루틴(라인 단위). 시퀀스 중단/재시작 시 안전하게 StopCoroutine 하기 위해 보관.
        private readonly List<Coroutine> _zapLineFadeCoroutines = new List<Coroutine>(8);

        // ZAP_LINE_POOL (등급1 perf 2026-06-11):
        // zap 1회 사용마다 라인 4개를 Instantiate/Destroy 하던 것을 비활성 보관 후 재사용.
        //   - 시각 동작 불변: ConfigureZapLine/PrepareZapLineRenderer 가 매 사용 시 위치·페이드를 재설정.
        //   - fromItemZap 클론이 SetActive(false) 후 방치돼 zap 사용마다 비활성 오브젝트가
        //     누적되던 leak + LineRenderer.material 인스턴스 재생성 비용도 함께 해소.
        private readonly Dictionary<string, Stack<GameObject>> _zapLinePool = new Dictionary<string, Stack<GameObject>>(4);
        private Transform _zapLinePoolRoot;
        private const int ZapLinePoolMaxPerKey = 8;

        // ConfigureZapLine 이 타겟 스텝마다 반복하던 재귀 탐색(GetComponentsInChildren alloc) 라인별 1회 캐시.
        private struct ZapLineRefs { public Transform start; public Transform end; public LightningBoltScript bolt; }
        private readonly Dictionary<GameObject, ZapLineRefs> _zapLineRefsCache = new Dictionary<GameObject, ZapLineRefs>(8);

        private struct ZapLineBaseline
        {
            public Vector3 start;
            public Vector3 end;
        }

        private struct ZapTarget
        {
            public int balloonId;
            public Vector3 position;
        }

        #endregion

        #region Lifecycle

        private void OnEnable()
        {
            EventBus.Subscribe<OnBoosterUsed>(HandleBoosterUsed);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnBoosterUsed>(HandleBoosterUsed);
            _handCamReturnSaved = false; // [HAND_CAMERA_5ROWS] 레벨 전환 시 보존 좌표 잔존 방지
            StopZapLineJiggle();
            for (int i = 0; i < _zapLineFadeCoroutines.Count; i++)
            {
                Coroutine c = _zapLineFadeCoroutines[i];
                if (c != null) StopCoroutine(c);
            }
            _zapLineFadeCoroutines.Clear();
            _zapLineTargetWidths.Clear();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Called by UI when player selects a color for Color Remove booster.
        /// </summary>
        public void OnColorSelected(int color)
        {
            if (!_awaitingColorSelection) return;
            _awaitingColorSelection = false;
            _awaitingBalloonClick = false;

            ConfirmPendingBooster();
            CloseUseItemPopup(false);
            StartCoroutine(PlayColorRemoveSequence(color));
        }

        /// <summary>
        /// Called by UI when player selects a holder for Select Tool booster.
        /// </summary>
        public void OnHolderSelected(int holderId)
        {
            if (!_awaitingHolderSelection) return;
            _awaitingHolderSelection = false;

            if (HolderVisualManager.HasInstance)
                HolderVisualManager.Instance.SetHandSelectionHighlightActive(false);

            ConfirmPendingBooster();
            //HideCancelButton();
            CloseUseItemPopup(true);
            ExecuteSelectTool(holderId);

            RestoreHandCameraOrMoveBack();

            ResumeRail();
        }

        // [HAND_CAMERA_5ROWS 원복 fix 2026-06-11] Hand 진입 시 보존한 좌표로 명시 복귀.
        // (CameraManager.MoveBack 의 _savedPosition 은 MoveToTarget 중복 호출 시 오염 가능 — 보존값 우선.)
        private Vector3 _handCamReturnPos;
        private bool _handCamReturnSaved;

        private void RestoreHandCameraOrMoveBack()
        {
            if (!CameraManager.HasInstance) return;
            if (_handCamReturnSaved)
            {
                CameraManager.Instance.RestoreTo(_handCamReturnPos);
                _handCamReturnSaved = false;
            }
            else
            {
                CameraManager.Instance.MoveBack();
            }
        }

        /// <summary>
        /// Whether the executor is waiting for player color selection (Color Remove).
        /// </summary>
        public bool IsAwaitingColorSelection => _awaitingColorSelection;

        /// <summary>
        /// Whether the executor is waiting for player holder selection (Select Tool).
        /// </summary>
        public bool IsAwaitingHolderSelection => _awaitingHolderSelection;

        /// <summary>
        /// Whether the executor is waiting for player balloon click (Color Remove).
        /// </summary>
        public bool IsAwaitingBalloonClick => _awaitingBalloonClick;

        /// <summary>Called when player clicks a balloon during Color Remove mode.</summary>
        public void OnBalloonClicked(int balloonId)
        {
            if (!_awaitingBalloonClick) return;
            _awaitingBalloonClick = false;
            _awaitingColorSelection = false;

            ConfirmPendingBooster();

            // Get clicked balloon's color
            if (!BalloonController.HasInstance) return;
            var data = BalloonController.Instance.GetBalloon(balloonId);
            if (data == null) return;
            int selectedColor = data.color;

            // Highlight selected color with white outline, others stay black
            BalloonController.Instance.SetOutlineByColor(selectedColor, true, Color.white);

            // Execute color remove after brief delay (so player sees the highlight)
            //HideCancelButton();
            CloseUseItemPopup(false);
            StartCoroutine(PlayColorRemoveSequence(selectedColor));
        }

        /// <summary>UseItem 팝업 닫기.</summary>
        private void CloseUseItemPopup(bool restoreBottomPanel = true)
        {
            if (UIManager.HasInstance)
            {
                var popup = UIManager.Instance.GetOpenUI<PopupUseItem>();
                if (popup != null) popup.CloseUI(restoreBottomPanel);
            }
        }

        #endregion

        #region Private Methods — Event Handler

        private void HandleBoosterUsed(OnBoosterUsed evt)
        {
            // Pause rail rotation while booster is active
            if (RailManager.HasInstance)
                RailManager.Instance.IsPausedByBooster = true;

            switch (evt.boosterType)
            {
                case BoosterManager.SELECT_TOOL:
                    _pendingBoosterType = BoosterManager.SELECT_TOOL;
                    _awaitingHolderSelection = true;
                    //ShowCancelButton();

                    if (CameraManager.HasInstance && HolderVisualManager.HasInstance)
                    {
                        // Hand: 보관함 큐의 앞쪽 HAND_VISIBLE_ROWS(5)개 행이 전부 화면에 들어오게 포커스.
                        // [HAND_CAMERA_5ROWS 2026-06-11] XZ 센터 이동만으론 카메라 기울기/FOV 에 따라
                        // 양끝 행이 잘렸음 → 회전·FOV 기준으로 5행 z-span 이 수직 시야(상하 8% 마진)에
                        // 들어오는 높이/Z 를 계산해 이동.
                        // [원복 fix] 복귀 좌표를 여기서 직접 보존(RestoreTo) — CameraManager._savedPosition 은
                        // MoveToTarget 중복 호출 시 이동 중간 위치로 오염돼 MoveBack 이 원위치로 못 돌아갔음.
                        if (!_handCamReturnSaved)
                        {
                            _handCamReturnPos = CameraManager.Instance.CurrentStablePosition;
                            _handCamReturnSaved = true;
                        }
                        Vector3 focusPosition = HolderVisualManager.Instance.CalculateRowFocusPosition(HAND_VISIBLE_ROWS);
                        Camera handCam = CameraManager.Instance.MainCamera;
                        if (handCam != null)
                            focusPosition = ComputeHandCameraPosition(handCam, focusPosition,
                                HolderVisualManager.Instance.RowSpacing);
                        CameraManager.Instance.MoveToTarget(focusPosition);
                    }

                    if (HolderVisualManager.HasInstance)
                        HolderVisualManager.Instance.SetHandSelectionHighlightActive(true);

                    Debug.Log("[BoosterExecutor] Select Tool activated. Waiting for holder selection.");
                    break;

                case BoosterManager.SHUFFLE:
                    ExecuteShuffle();
                    ResumeRail();
                    break;

                case BoosterManager.COLOR_REMOVE:
                    _pendingBoosterType = BoosterManager.COLOR_REMOVE;
                    //ShowCancelButton();
                    _awaitingColorSelection = true;
                    _awaitingBalloonClick = true;

                    // Move camera to field center
                    if (CameraManager.HasInstance && GameManager.HasInstance)
                    {
                        Vector3 fieldPosition = new Vector3(
                            GameManager.Instance.Board.boardCenterX,
                            0f,
                            GameManager.Instance.Board.boardCenterZ
                        );
                        CameraManager.Instance.MoveToTarget(fieldPosition);
                    }

                    // Turn ON black outline on ALL balloons
                    if (BalloonController.HasInstance)
                        BalloonController.Instance.SetAllOutlines(true, Color.black);

                    Debug.Log("[BoosterExecutor] Color Remove activated. Waiting for color selection.");
                    break;

                // HAND = SELECT_TOOL (명세 통합) → 위 SELECT_TOOL case에서 처리
            }
        }

        // [HAND_CAMERA_5ROWS 2026-06-11] 카메라 회전/FOV 로부터 holder 평면(y=rowFocus.y)에서
        // 보이는 z-span 의 높이당 기울기를 구해, 5행 span(행 4간격 + 양끝 반행 여유)이 들어오는
        // 높이와 '5행 중심 = 화면 세로 중앙'이 되는 Z 를 닫힌식으로 계산한다.
        // 현재 높이로 이미 충분하면 높이 유지(줌인 방지). 카메라가 내려보지 않는 비정상 각도면 기존 동작 폴백.
        private static Vector3 ComputeHandCameraPosition(Camera cam, Vector3 rowFocus, float rowSpacing)
        {
            Transform ct = cam.transform;
            float planeY = rowFocus.y; // CalculateRowFocusPosition 의 holder 평면 높이

            // [Orthographic 분기] 본 플로우 InGame 은 ConfigureInGame 이 ortho(size 15) 강제 —
            // 높이 변경은 프레이밍에 무효이고, '화면 중심에 보이는 지점'이 카메라 위치에서 forward 로
            // 비껴간 지점(pitch 65°·높이 20 기준 z 약 9유닛)이라 XZ=focus 이동만으론 큐가 화면
            // 위쪽으로 밀려 5행이 잘렸다 → forward 투영 오프셋을 보정해 5행 중심을 화면 중앙에.
            // (ortho size 15 의 평면 z-span ≈ 2*15/sin65° ≈ 33유닛 ≫ 5행 ~9유닛 — 중심만 맞으면 충분.)
            if (cam.orthographic)
            {
                Vector3 fwd = ct.forward;
                if (fwd.y >= -0.01f)
                    return new Vector3(rowFocus.x, ct.position.y, rowFocus.z); // 폴백: 기존 동작
                float t = (ct.position.y - planeY) / -fwd.y;
                return new Vector3(rowFocus.x - fwd.x * t, ct.position.y, rowFocus.z - fwd.z * t);
            }

            // 뷰포트 상하(8% 마진) 중앙 ray 방향 — 방향은 카메라 위치와 무관(회전/FOV/종횡비만 영향).
            Vector3 dirB = cam.ViewportPointToRay(new Vector3(0.5f, 0.08f, 0f)).direction;
            Vector3 dirT = cam.ViewportPointToRay(new Vector3(0.5f, 0.92f, 0f)).direction;
            if (dirB.y >= -0.01f || dirT.y >= -0.01f)
                return new Vector3(rowFocus.x, ct.position.y, rowFocus.z); // 폴백: 기존 XZ 이동

            float kB = dirB.z / -dirB.y;            // 높이 1 당 ray 가 닿는 평면 z 오프셋
            float kT = dirT.z / -dirT.y;
            float slopeSpan = Mathf.Abs(kT - kB);   // 높이 1 당 보이는 z-span
            if (slopeSpan < 0.0001f)
                return new Vector3(rowFocus.x, ct.position.y, rowFocus.z);

            float requiredSpan = HAND_VISIBLE_ROWS * rowSpacing;
            float requiredH = requiredSpan / slopeSpan;
            float currentH = ct.position.y - planeY;
            float h = Mathf.Max(currentH, requiredH);          // 부족할 때만 상승
            float camY = planeY + h;
            float camZ = rowFocus.z - (kT + kB) * 0.5f * h;    // 5행 중심이 화면 세로 중앙에 오게
            return new Vector3(rowFocus.x, camY, camZ);
        }

        /// <summary>Resume rail rotation after booster completes.</summary>
        private void ResumeRail()
        {
            if (RailManager.HasInstance)
                RailManager.Instance.IsPausedByBooster = false;
        }

        /// <summary>Confirm deferred booster consumption after user completes interaction.</summary>
        private void ConfirmPendingBooster()
        {
            if (!string.IsNullOrEmpty(_pendingBoosterType))
            {
                // Inventory was already decremented in UseBooster — nothing extra needed
                _pendingBoosterType = null;
            }
        }

        /// <summary>
        /// Cancel a pending interactive booster — refunds inventory and resumes rail.
        /// </summary>
        public void CancelPendingBooster()
        {
            if (string.IsNullOrEmpty(_pendingBoosterType)) return;

            if (HolderVisualManager.HasInstance)
                HolderVisualManager.Instance.SetHandSelectionHighlightActive(false);

            // Refund inventory
            if (BoosterManager.HasInstance)
                BoosterManager.Instance.AddBooster(_pendingBoosterType, 1);

            Debug.Log($"[BoosterExecutor] Cancelled {_pendingBoosterType} — inventory refunded.");

            // Reset awaiting flags
            _awaitingHolderSelection = false;
            _awaitingColorSelection = false;
            _awaitingBalloonClick = false;

            // Clear outlines if Color Remove was active
            if (BalloonController.HasInstance)
                BalloonController.Instance.ClearAllOutlines();

            // Move camera back — Hand 는 보존 좌표로 명시 복귀, 그 외(Zap 등)는 기존 MoveBack.
            RestoreHandCameraOrMoveBack();

            _pendingBoosterType = null;
            //HideCancelButton();
            CloseUseItemPopup();
            ResumeRail();
        }

        /// <summary>부스터 취소 [X] 버튼 표시.</summary>
        private void ShowCancelButton()
        {
            if (_cancelButtonGO != null) { _cancelButtonGO.SetActive(true); return; }

            // Canvas 찾기
            // [Optimization 2026-05-10] Canvas 를 _cachedCanvas 로 캐시. 씬 재로드 등으로 destroyed 시 lazy 재fetch.
            // 롤백: 아래 캐시 분기 제거 + 주석 처리된 원본 라인 복원.
            // 원본: var canvas = FindAnyObjectByType<Canvas>();
            if (_cachedCanvas == null) _cachedCanvas = FindAnyObjectByType<Canvas>();
            var canvas = _cachedCanvas;
            if (canvas == null)
            {
                Debug.LogError("[BoosterExecutor] No Canvas found — cancel button can't be created. Cancelling pending booster (inventory refunded).");
                CancelPendingBooster();
                return;
            }

            // _cancelButtonGO = new GameObject("BoosterCancelBtn");
            // _cancelButtonGO.transform.SetParent(canvas.transform, false);

            // var rt = _cancelButtonGO.AddComponent<RectTransform>();
            // rt.anchorMin = new Vector2(1f, 1f);
            // rt.anchorMax = new Vector2(1f, 1f);
            // rt.pivot = new Vector2(1f, 1f);
            // rt.anchoredPosition = new Vector2(-20f, -20f);
            // rt.sizeDelta = new Vector2(80f, 80f);

            // var img = _cancelButtonGO.AddComponent<UnityEngine.UI.Image>();
            // img.color = new Color(0.8f, 0.2f, 0.2f, 0.9f);

            // var btn = _cancelButtonGO.AddComponent<UnityEngine.UI.Button>();
            // btn.onClick.AddListener(CancelPendingBooster);

            // // X 텍스트
            // var txtGO = new GameObject("X");
            // txtGO.transform.SetParent(_cancelButtonGO.transform, false);
            // var txtRT = txtGO.AddComponent<RectTransform>();
            // txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
            // txtRT.offsetMin = Vector2.zero; txtRT.offsetMax = Vector2.zero;
            // var txt = txtGO.AddComponent<TMPro.TextMeshProUGUI>();
            // txt.text = "X";
            // txt.fontSize = 40;
            // txt.alignment = TMPro.TextAlignmentOptions.Center;
            // txt.color = Color.white;
        }

        /// <summary>부스터 취소 버튼 숨기기.</summary>
        private void HideCancelButton()
        {
            // if (_cancelButtonGO != null)
            //     _cancelButtonGO.SetActive(false);
        }

        /// <summary>Whether an interactive booster is pending (can be cancelled).</summary>
        public bool HasPendingBooster => !string.IsNullOrEmpty(_pendingBoosterType);

        #endregion

        #region Private Methods — Execution

        private IEnumerator DelayedColorRemove(int color)
        {
            yield return PlayColorRemoveSequence(color);
        }

        private IEnumerator PlayColorRemoveSequence(int color)
        {
            if (_isColorRemoveSequenceRunning)
                yield break;

            _isColorRemoveSequenceRunning = true;
            SetHudBottomPanelHiddenForZap(true);

            yield return new WaitForSeconds(ZapSelectionHighlightDelay);

            if (BalloonController.HasInstance)
                BalloonController.Instance.ClearAllOutlines();

            _awaitingColorSelection = false;
            _awaitingBalloonClick = false;
            ConfirmPendingBooster();

            CollectZapTargets(color);

            Vector3 attackPosition = GetZapAttackPosition();
            // ItemZap stays at ZapSpawnPosition for the entire effect — do not tween or reposition.
            GameObject zapObject = CreateItemZap(attackPosition);
            Vector3 zapFixedPosition = zapObject != null ? zapObject.transform.position : ZapSpawnPosition;
            List<GameObject> zapLineObjects = null;
            List<bool> zapLineFromItemZapFlags = null;

            yield return new WaitForSeconds(ZapAppearDuration);
            yield return new WaitForSeconds(ZapMoveDuration);

            // 사용자 보고 버그(ZapStart→ZapIdle 반복) 방어: 컨트롤러 전환에 의존하지 않고 ZapAttackIdle을 코드로 1회 강제 진입.
            if (!_isZapAnimationPlaying && _zapAnimator != null)
            {
                _isZapAnimationPlaying = true;
                _zapAnimator.Play("ZapAttackIdle", 0, 0f);
            }

            int fieldRemoved = 0;
            if (_zapTargets.Count > 0)
            {
                zapLineObjects = CreateZapLineObjects(zapObject, ZapLineConcurrentCount, out zapLineFromItemZapFlags);
                if (zapLineObjects != null && zapLineObjects.Count > 0)
                {
                    _zapLineBaselines.Clear();
                    _zapLineJiggleCo = StartCoroutine(JiggleZapLinesRoutine(zapLineObjects));
                    yield return null;
                }

                // ItemZap.prefab Animator는 생성과 동시에 ZapStart → ZapAttackIdle을 1회 자동 진행한다.
                // FxZapLine 출력 구간 동안에는 zapObject에 Animator.Play/CrossFade/Rebind/SetActive 등 어떤 형태로도
                // 재진입을 트리거하지 마라 — ZapAttackIdle이 처음부터 다시 재생되어 연출이 깨진다.
                // ZAP을 새로 사용할 때마다 ItemZap 인스턴스가 새로 생성되므로 ZapStart부터의 자연스러운 재생은 그쪽에서 보장된다.

                // ROLLBACK_ZAP_FIXED_TOTAL_POP_TIME:
                // Do not multiply a minimum interval by target count. The full balloon-pop
                // pass must stay inside the remaining 2s item-effect budget, so dense boards
                // can pop multiple targets in the same frame instead of stretching the item
                // effect for seconds.
                float popDurationBudget = Mathf.Max(
                    0.1f,
                    ZapMaxTotalEffectDuration - ZapSelectionHighlightDelay - ZapAppearDuration - ZapMoveDuration - ZapLineLifetime);
                float popStartTime = Time.time;
                float stepDelay = _zapTargets.Count > 1
                    ? popDurationBudget / (_zapTargets.Count - 1)
                    : 0f;
                float lineLeadBeforePop = stepDelay >= ZapMinLeadInterval
                    ? Mathf.Min(ZapLineLeadBeforePop, stepDelay * 0.5f)
                    : 0f;

                for (int i = 0; i < _zapTargets.Count; i++)
                {
                    if (stepDelay > 0f)
                    {
                        float targetTime = popStartTime + stepDelay * i;
                        while (Time.time < targetTime)
                            yield return null;
                    }

                    ZapTarget target = _zapTargets[i];
                    Vector3 targetPosition = GetZapEffectPosition(target.position);
                    float lineVisibleDuration = Mathf.Max(ZapLineLifetime, stepDelay + lineLeadBeforePop);
                    Vector3 lineStartPosition = zapFixedPosition;
                    ConfigureZapLineFan(zapLineObjects, lineStartPosition, targetPosition, lineVisibleDuration);

                    // ROLLBACK_ZAP_LINE_PREPOP_LEAD:
                    // Give FxZapLine a rendered moment only when the total-time budget can afford
                    // it. For dense boards, forcing this wait once per target breaks the 2s cap.
                    if (lineLeadBeforePop > 0f)
                        yield return new WaitForSeconds(lineLeadBeforePop);

                    if (TryPopZapTarget(target.balloonId))
                        fieldRemoved++;
                }

                if (zapLineObjects != null && zapLineObjects.Count > 0)
                    yield return new WaitForSeconds(ZapLineLifetime);
            }

            int totalRemoved = fieldRemoved + RemoveRailAndQueueColor(color);
            FinalizeColorRemove(color, totalRemoved);

            // Jiggle 코루틴을 먼저 정지한다 — Jiggle 의 baseline 덮어쓰기/Trigger 재호출이 FadeOut 의
            // width/alpha 페이드를 무효화하지 않도록 보장. (FadeOut 루틴은 더 이상 LightningBoltScript 를
            // 사용하지 않으므로 Trigger 충돌 자체가 사라짐.)
            StopZapLineJiggle();

            // 페이드아웃 — LineRenderer width/vertex alpha + material(_TintColor/_Color) alpha 를 0 으로 수렴.
            // FadeOut 중에는 LightningBoltScript 미사용 → 위치/Trigger 호출 없음 → 마지막 순간 라인 굵게 튀는 결함 없음.
            if (zapLineObjects != null && zapLineObjects.Count > 0)
                yield return StartCoroutine(FadeOutZapLinesRoutine(zapLineObjects));

            if (zapLineObjects != null)
            {
                for (int i = 0; i < zapLineObjects.Count; i++)
                {
                    GameObject line = zapLineObjects[i];
                    if (line == null) continue;
                    bool fromItemZap = zapLineFromItemZapFlags != null && i < zapLineFromItemZapFlags.Count
                        ? zapLineFromItemZapFlags[i]
                        : false;
                    // ZAP_LINE_POOL: Destroy/방치(SetActive false leak) 대신 풀 반납 — 다음 zap 에서 재사용.
                    bool isLine2 = line.name.StartsWith("FxZapLine2");
                    ReleaseZapLineToPool(line, GetZapLinePoolKey(fromItemZap, isLine2));
                }
            }
            _zapLineTargetWidths.Clear();
            _zapLineFadeCoroutines.Clear();
            // FxZapLine 라인 페이드아웃·정리 완료 후에 ZapFinish 트리거 → ZapAttackIdle → ZapAttackFinish 자연 전이
            yield return StartCoroutine(WaitForZapFinishThenDestroyRoutine(zapObject));

            if (CameraManager.HasInstance)
                CameraManager.Instance.MoveBack();

            SetHudBottomPanelHiddenForZap(false);
            ResumeRail();
            _zapTargets.Clear();
            _isColorRemoveSequenceRunning = false;
            _isZapAnimationPlaying = false;
            _zapAnimator = null;
        }

        /// <summary>
        /// Select Tool: force-deploy the chosen holder regardless of queue order.
        /// </summary>
        private void ExecuteSelectTool(int holderId)
        {
            if (!HolderManager.HasInstance) return;

            // Hand/SelectTool: 줄 순서 무시 — ForceSelectHolder 사용
            bool result = HolderManager.Instance.ForceSelectHolder(holderId);
            if (result)
            {
                Debug.Log($"[BoosterExecutor] Select Tool: deployed holder {holderId}.");
            }
            else
            {
                Debug.LogWarning($"[BoosterExecutor] Select Tool: failed to deploy holder {holderId}.");
            }
        }

        /// <summary>
        /// Shuffle: randomize the order of waiting holders in the queue.
        /// Chain groups are treated as single units — members stay together with relative column order preserved.
        /// </summary>
        private void ExecuteShuffle()
        {
            if (!HolderManager.HasInstance) return;

            HolderData[] holders = HolderManager.Instance.GetHolders();
            if (holders == null || holders.Length == 0) return;

            // Collect non-active, non-consumed holders
            var shuffleable = new List<HolderData>();
            for (int i = 0; i < holders.Length; i++)
            {
                if (!holders[i].isDeploying && !holders[i].isWaiting &&
                    !holders[i].isMovingToRail && !holders[i].isConsumed &&
                    holders[i].magazineCount > 0)
                {
                    shuffleable.Add(holders[i]);
                }
            }

            if (shuffleable.Count <= 1) return;

            // Group by chainGroupId. Holders with chainGroupId < 0 are standalone (each is its own unit).
            var chainGroups = new Dictionary<int, List<HolderData>>();
            int soloKey = -1; // unique negative keys for standalone holders
            for (int i = 0; i < shuffleable.Count; i++)
            {
                int gid = shuffleable[i].chainGroupId;
                if (gid < 0)
                {
                    // Standalone — assign unique group key
                    chainGroups[soloKey] = new List<HolderData> { shuffleable[i] };
                    soloKey--;
                }
                else
                {
                    if (!chainGroups.ContainsKey(gid))
                        chainGroups[gid] = new List<HolderData>();
                    chainGroups[gid].Add(shuffleable[i]);
                }
            }

            // Sort members within each chain group by column (preserve relative ordering)
            foreach (var kvp in chainGroups)
            {
                if (kvp.Value.Count > 1)
                    kvp.Value.Sort((a, b) => a.column.CompareTo(b.column));
            }

            // Build list of shuffle units (each unit = list of holders)
            var units = new List<List<HolderData>>();
            foreach (var kvp in chainGroups)
                units.Add(kvp.Value);

            if (units.Count <= 1) return;

            // Collect original column slots for each unit (in order)
            var unitColumns = new List<List<int>>();
            for (int i = 0; i < units.Count; i++)
            {
                var cols = new List<int>();
                for (int j = 0; j < units[i].Count; j++)
                    cols.Add(units[i][j].column);
                unitColumns.Add(cols);
            }

            // Fisher-Yates shuffle of units
            for (int i = units.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                // Swap column assignments between unit i and unit j
                var tmpCols = unitColumns[i];
                unitColumns[i] = unitColumns[j];
                unitColumns[j] = tmpCols;
            }

            // Apply new column assignments
            for (int i = 0; i < units.Count; i++)
            {
                var cols = unitColumns[i];
                var members = units[i];
                for (int m = 0; m < members.Count; m++)
                {
                    // If group has more members than available column slots, wrap
                    int colIdx = m < cols.Count ? m : m % cols.Count;
                    members[m].column = cols[colIdx];
                }
            }

            Debug.Log($"[BoosterExecutor] Shuffle: randomized {units.Count} units ({shuffleable.Count} holders).");

            // Shuffle 연출: 카메라 쉐이크
            if (CameraManager.HasInstance && CameraManager.Instance.MainCamera != null)
            {
                CameraManager.Instance.MainCamera.transform.DOShakePosition(0.2f, 0.1f, 8, 90f, false, true);
            }

            // Sync visual positions to new column assignments
            if (HolderVisualManager.HasInstance)
                HolderVisualManager.Instance.RefreshAllPositions();

            EventBus.Publish(new OnBoosterEffectApplied
            {
                boosterType = BoosterManager.SHUFFLE,
                affectedCount = shuffleable.Count
            });
        }

        /// <summary>
        /// Color Remove: remove all balloons of the chosen color from field,
        /// all darts of that color from rail, and all holders of that color from queue.
        /// </summary>
        private void ExecuteColorRemove(int color)
        {
            int totalRemoved = PopFieldBalloonsImmediately(color);
            totalRemoved += RemoveRailAndQueueColor(color);
            FinalizeColorRemove(color, totalRemoved);
        }

        private int PopFieldBalloonsImmediately(int color)
        {
            int removed = 0;
            if (BalloonController.HasInstance)
            {
                BalloonData[] balloons = BalloonController.Instance.GetAllBalloonsByColor(color);
                if (balloons != null)
                {
                    for (int i = 0; i < balloons.Length; i++)
                    {
                        if (!balloons[i].isPopped)
                        {
                            BalloonController.Instance.ForcePopBalloon(balloons[i].balloonId);
                            removed++;
                        }
                    }
                }
            }

            return removed;
        }

        private int RemoveRailAndQueueColor(int color)
        {
            int removed = 0;

            if (RailManager.HasInstance)
            {
                int slotCount = RailManager.Instance.SlotCount;
                for (int i = 0; i < slotCount; i++)
                {
                    var slot = RailManager.Instance.GetSlot(i);
                    if (slot.dartColor == color)
                    {
                        RailManager.Instance.ClearSlot(i);
                        removed++;
                    }
                }
            }

            if (HolderManager.HasInstance)
            {
                HolderData[] holders = HolderManager.Instance.GetHolders();
                if (holders != null)
                {
                    for (int i = 0; i < holders.Length; i++)
                    {
                        if (holders[i].color == color && !holders[i].isConsumed && holders[i].magazineCount > 0)
                        {
                            int hid = holders[i].holderId;

                            if (HolderVisualManager.HasInstance)
                                HolderVisualManager.Instance.RemoveHolderVisual(hid);

                            HolderManager.Instance.UndoDeploy(hid);
                            holders[i].magazineCount = 0;
                            holders[i].isConsumed = true;
                            removed++;
                        }
                    }
                }
            }

            if (HolderManager.HasInstance)
                HolderManager.Instance.CompactColumns();
            if (HolderVisualManager.HasInstance)
                HolderVisualManager.Instance.RefreshAllPositions();

            return removed;
        }

        private void FinalizeColorRemove(int color, int totalRemoved)
        {
            if (CameraManager.HasInstance && CameraManager.Instance.MainCamera != null)
            {
                CameraManager.Instance.MainCamera.transform.DOShakePosition(0.3f, 0.15f, 10, 90f, false, true);
            }

            Debug.Log($"[BoosterExecutor] Color Remove: removed {totalRemoved} objects of color {color}.");

            EventBus.Publish(new OnBoosterEffectApplied
            {
                boosterType = BoosterManager.COLOR_REMOVE,
                affectedCount = totalRemoved
            });
        }

        private void SetHudBottomPanelHiddenForZap(bool hidden)
        {
            // ROLLBACK_ZAP_HUD_BOTTOM_PANEL_SHIFT:
            // Keep the item panel below the screen while the authored Zap attack/finish
            // sequence is playing, then restore it after the effect completes.
            UIHud hud = FindAnyObjectByType<UIHud>();
            if (hud == null)
                return;

            if (hidden)
                hud.HideBottomPanel();
            else
                hud.ShowBottomPanel();
        }

        private void CollectZapTargets(int color)
        {
            _zapTargets.Clear();

            if (!BalloonController.HasInstance)
                return;

            BalloonData[] balloons = BalloonController.Instance.GetAllBalloonsByColor(color);
            if (balloons == null)
                return;

            for (int i = 0; i < balloons.Length; i++)
            {
                BalloonData data = balloons[i];
                if (data == null || data.isPopped)
                    continue;

                _zapTargets.Add(new ZapTarget
                {
                    balloonId = data.balloonId,
                    position = BalloonController.Instance.GetBalloonWorldPosition(data.balloonId)
                });
            }

            _zapTargets.Sort((a, b) =>
            {
                int z = a.position.z.CompareTo(b.position.z);
                if (z != 0) return z;
                return a.position.x.CompareTo(b.position.x);
            });
        }

        private bool TryPopZapTarget(int balloonId)
        {
            if (!BalloonController.HasInstance)
                return false;

            BalloonData data = BalloonController.Instance.GetBalloon(balloonId);
            if (data == null || data.isPopped)
                return false;

            BalloonController.Instance.ForcePopBalloon(balloonId);
            return true;
        }

        private Vector3 GetZapSpawnPosition(Vector3 attackPosition)
        {
            // ROLLBACK_ZAP_SPAWN_Y_OFFSET: ItemZap 등장 위치를 고정 좌표로 사용 (기존 동적 계산 제거).
            // fixed spawn — attackPosition unused, kept for caller compatibility.
            return ZapSpawnPosition;
        }

        private Vector3 GetZapAttackPosition()
        {
            if (_zapTargets.Count <= 0)
                return GetZapFallbackPosition();

            float minX = _zapTargets[0].position.x;
            float maxX = _zapTargets[0].position.x;
            for (int i = 1; i < _zapTargets.Count; i++)
            {
                Vector3 position = _zapTargets[i].position;
                if (position.x < minX) minX = position.x;
                if (position.x > maxX) maxX = position.x;
            }

            float cellSpacing = GameManager.HasInstance ? GameManager.Instance.Board.cellSpacing : 0.55f;
            float fieldMinZ = GetActiveFieldMinZ();
            // ROLLBACK_ZAP_FIELD_BOTTOM_ANCHOR:
            // The Zap item must enter from the bottom of the whole field, not from the first
            // selected-color row. Selected colors that only exist in middle rows otherwise spawn
            // the Zap inside the balloon grid.
            Vector3 attackPosition = new Vector3(
                (minX + maxX) * 0.5f,
                0f,
                fieldMinZ - cellSpacing * ZapFieldBottomPaddingCells);
            return GetZapEffectPosition(attackPosition);
        }

        private float GetActiveFieldMinZ()
        {
            if (!BalloonController.HasInstance)
                return _zapTargets.Count > 0 ? _zapTargets[0].position.z : 0f;

            BalloonData[] balloons = BalloonController.Instance.GetAllBalloons();
            bool found = false;
            float minZ = 0f;
            if (balloons != null)
            {
                for (int i = 0; i < balloons.Length; i++)
                {
                    BalloonData data = balloons[i];
                    if (data == null || data.isPopped)
                        continue;

                    Vector3 position = BalloonController.Instance.GetBalloonWorldPosition(data.balloonId);
                    if (!found || position.z < minZ)
                    {
                        minZ = position.z;
                        found = true;
                    }
                }
            }

            return found
                ? minZ
                : (_zapTargets.Count > 0 ? _zapTargets[0].position.z : 0f);
        }

        private Vector3 GetZapFinishPosition()
        {
            if (_zapTargets.Count <= 0)
                return GetZapFallbackPosition();

            Vector3 lastPosition = _zapTargets[_zapTargets.Count - 1].position;
            return GetZapEffectPosition(lastPosition);
        }

        private Vector3 GetZapFallbackPosition()
        {
            if (GameManager.HasInstance)
            {
                return GetZapEffectPosition(new Vector3(
                    GameManager.Instance.Board.boardCenterX,
                    0f,
                    GameManager.Instance.Board.boardCenterZ));
            }

            return GetZapEffectPosition(Vector3.zero);
        }

        private Vector3 GetZapEffectPosition(Vector3 position)
        {
            position.y += ZapEffectYOffset;
            return position;
        }

        private GameObject CreateItemZap(Vector3 attackPosition)
        {
            if (_itemZapPrefab == null)
                _itemZapPrefab = Resources.Load<GameObject>(Const.PREFAB_ITEM_ZAP);

            if (_itemZapPrefab == null)
            {
                Debug.LogWarning($"[BoosterExecutor] Missing ItemZap prefab at Resources/{Const.PREFAB_ITEM_ZAP}.");
                return null;
            }

            Vector3 spawnPosition = GetZapSpawnPosition(attackPosition);
            GameObject zapObject = Instantiate(_itemZapPrefab, spawnPosition, Quaternion.identity);
            if (zapObject != null)
            {
                _zapAnimator = zapObject.GetComponentInChildren<Animator>(true);
                if (_zapAnimator != null)
                {
                    _zapAnimator.keepAnimatorStateOnDisable = true;
                }
            }
            return zapObject;
        }

        private GameObject CreateZapLineObject(GameObject zapObject, out bool fromItemZap)
        {
            fromItemZap = false;
            if (zapObject != null)
            {
                // ROLLBACK_ITEMZAP_CHILD_LINE:
                // ItemZap.prefab owns FxZapLine. Prefer that child so the authored ZapStart /
                // ZapAttack / ZapFinish animation setup drives the same visual object.
                Transform childLine = FindChildRecursive(zapObject.transform, "FxZapLine");
                if (childLine != null)
                {
                    // ROLLBACK_ZAP_LINE_RUNTIME_CLONE:
                    // Do not drive the inactive child directly. The ItemZap animator can keep
                    // authored children disabled while ZapAttack/ZapFinish plays, so clone the
                    // line as an independent runtime effect and destroy it after the sequence.
                    GameObject clonedRuntimeLine = Instantiate(childLine.gameObject);
                    // FxZapLine_Runtime stays at fixed ZapSpawnPosition for its full lifetime per design.
                    clonedRuntimeLine.transform.position = ZapSpawnPosition;
                    clonedRuntimeLine.name = "FxZapLine_Runtime";
                    clonedRuntimeLine.SetActive(true);
                    fromItemZap = true;
                    return clonedRuntimeLine;
                }
            }

            if (_fxZapLinePrefab == null)
                _fxZapLinePrefab = Resources.Load<GameObject>(Const.PREFAB_FX_ZAP_LINE);

            if (_fxZapLinePrefab == null)
            {
                Debug.LogWarning($"[BoosterExecutor] Missing FxZapLine prefab at Resources/{Const.PREFAB_FX_ZAP_LINE}.");
                return null;
            }

            GameObject runtimeLine = Instantiate(_fxZapLinePrefab);
            // FxZapLine_Runtime stays at fixed ZapSpawnPosition for its full lifetime per design.
            runtimeLine.transform.position = ZapSpawnPosition;
            return runtimeLine;
        }

        // 4갈래(Fan) Zap 라인용 다중 복제 생성.
        // 앞쪽 절반은 FxZapLine 소스(자식 우선, 없으면 PREFAB_FX_ZAP_LINE), 뒤쪽 절반은 PREFAB_FX_ZAP_LINE2.
        // 라인별 origin(fromItemZap)을 fromItemZapFlags로 반환해 cleanup 분기에 사용한다.
        private List<GameObject> CreateZapLineObjects(GameObject zapObject, int count, out List<bool> fromItemZapFlags)
        {
            var lines = new List<GameObject>(Mathf.Max(1, count));
            fromItemZapFlags = new List<bool>(Mathf.Max(1, count));
            if (count <= 0)
                return lines;

            // FxZapLine source (앞쪽 절반): ItemZap 자식 우선, 없으면 PREFAB_FX_ZAP_LINE.
            GameObject zapLineSource = null;
            bool zapLineSourceFromItemZap = false;

            if (zapObject != null)
            {
                Transform childLine = FindChildRecursive(zapObject.transform, "FxZapLine");
                if (childLine != null)
                {
                    zapLineSource = childLine.gameObject;
                    zapLineSourceFromItemZap = true;
                }
            }

            if (zapLineSource == null)
            {
                if (_fxZapLinePrefab == null)
                    _fxZapLinePrefab = Resources.Load<GameObject>(Const.PREFAB_FX_ZAP_LINE);
                zapLineSource = _fxZapLinePrefab;
                zapLineSourceFromItemZap = false;
            }

            int halfCount = count / 2;

            for (int i = 0; i < count; i++)
            {
                bool useFxZapLine = i < halfCount;
                GameObject source;
                bool sourceFromItemZap;
                string runtimeName;

                if (useFxZapLine)
                {
                    if (zapLineSource == null)
                    {
                        Debug.LogWarning($"[BoosterExecutor] Missing FxZapLine prefab at Resources/{Const.PREFAB_FX_ZAP_LINE}.");
                        continue;
                    }
                    source = zapLineSource;
                    sourceFromItemZap = zapLineSourceFromItemZap;
                    runtimeName = $"FxZapLine_Runtime_{i}";
                }
                else
                {
                    if (_fxZapLine2Prefab == null)
                        _fxZapLine2Prefab = Resources.Load<GameObject>(Const.PREFAB_FX_ZAP_LINE2);

                    if (_fxZapLine2Prefab == null)
                    {
                        Debug.LogWarning($"[BoosterExecutor] Missing FxZapLine2 prefab at Resources/{Const.PREFAB_FX_ZAP_LINE2}.");
                        continue;
                    }
                    source = _fxZapLine2Prefab;
                    sourceFromItemZap = false;
                    runtimeName = $"FxZapLine2_Runtime_{i - halfCount}";
                }

                // ZAP_LINE_POOL: 풀에 보관된 라인 우선 재사용, 없을 때만 Instantiate.
                string poolKey = GetZapLinePoolKey(sourceFromItemZap, !useFxZapLine);
                GameObject runtimeLine = TakeZapLineFromPool(poolKey);
                if (runtimeLine == null) runtimeLine = Instantiate(source);
                else runtimeLine.transform.SetParent(null, false);
                runtimeLine.transform.position = ZapSpawnPosition;
                runtimeLine.name = runtimeName;
                runtimeLine.SetActive(true);
                lines.Add(runtimeLine);
                fromItemZapFlags.Add(sourceFromItemZap);
            }

            return lines;
        }

        // ── ZAP_LINE_POOL (등급1 perf 2026-06-11) ─────────────────────────────
        // key: FxZapLine(Resources) / FxZapLine2(Resources) / ItemZap 자식 클론 — 세 템플릿 모두
        // 각 zap 사용에서 동일 프리팹 원본이므로 이름 기반 키로 재사용해도 비주얼 동일.
        private static string GetZapLinePoolKey(bool fromItemZap, bool isLine2)
            => isLine2 ? "Line2" : (fromItemZap ? "ItemChild" : "Line1");

        private GameObject TakeZapLineFromPool(string poolKey)
        {
            if (!_zapLinePool.TryGetValue(poolKey, out Stack<GameObject> stack)) return null;
            while (stack.Count > 0)
            {
                GameObject line = stack.Pop();
                if (line != null) return line; // 씬 전환 등으로 파괴된 항목은 스킵
            }
            return null;
        }

        private void ReleaseZapLineToPool(GameObject line, string poolKey)
        {
            if (line == null) return;

            // 재사용 대비 리셋 — 페이드아웃이 0 으로 만든 width/alpha 를 캡처된 원본 값으로 복원.
            // (PrepareZapLineRenderer 가 다음 사용 시 '현재 width'를 페이드 타깃으로 캡처하므로
            //  0 인 채 보관하면 재사용 라인이 MinWidth 로만 페이드되는 문제가 생긴다.)
            LineRenderer lr = line.GetComponentInChildren<LineRenderer>(true);
            if (lr != null)
            {
                if (_zapLineTargetWidths.TryGetValue(lr, out float targetWidth))
                    lr.widthMultiplier = targetWidth;
                Color sc = lr.startColor; sc.a = 1f; lr.startColor = sc;
                Color ec = lr.endColor;   ec.a = 1f; lr.endColor   = ec;
                // 페이드아웃이 material alpha(_TintColor/_Color) 도 0 으로 만들어 두므로 vertex 만 1 로 돌리면
                // 다음 zap 재사용 시 material alpha=0 이 남아 라인 전체가 투명해진다. 가드 후 1f 복원.
                Material runtimeMat = lr.sharedMaterial != null ? lr.material : null;
                if (runtimeMat != null)
                {
                    if (runtimeMat.HasProperty("_TintColor"))
                    {
                        Color tc = runtimeMat.GetColor("_TintColor"); tc.a = 1f; runtimeMat.SetColor("_TintColor", tc);
                    }
                    if (runtimeMat.HasProperty("_Color"))
                    {
                        Color mc = runtimeMat.GetColor("_Color"); mc.a = 1f; runtimeMat.SetColor("_Color", mc);
                    }
                }
                lr.positionCount = 0;
            }

            line.SetActive(false);

            if (!_zapLinePool.TryGetValue(poolKey, out Stack<GameObject> stack))
            {
                stack = new Stack<GameObject>(ZapLinePoolMaxPerKey);
                _zapLinePool[poolKey] = stack;
            }
            if (stack.Count >= ZapLinePoolMaxPerKey)
            {
                _zapLineRefsCache.Remove(line);
                Destroy(line);
                return;
            }

            if (_zapLinePoolRoot == null)
            {
                var rootGo = new GameObject("[ZapLinePool]");
                rootGo.transform.SetParent(transform, false);
                _zapLinePoolRoot = rootGo.transform;
            }
            line.transform.SetParent(_zapLinePoolRoot, false);
            stack.Push(line);
        }

        private void ConfigureZapLine(GameObject zapLineObject, Vector3 startPosition, Vector3 endPosition, float visibleDuration)
        {
            if (zapLineObject == null)
                return;

            zapLineObject.SetActive(true);

            // 등급1 perf: 타겟 스텝마다 반복되던 재귀 탐색(GetComponentsInChildren alloc)을 라인별 1회 캐시.
            // 풀 재사용 시에도 동일 GameObject/자식 구조라 캐시 그대로 유효.
            if (!_zapLineRefsCache.TryGetValue(zapLineObject, out ZapLineRefs lineRefs))
            {
                lineRefs = new ZapLineRefs
                {
                    start = FindChildRecursive(zapLineObject.transform, "LightningStart"),
                    end   = FindChildRecursive(zapLineObject.transform, "LightningEnd"),
                    bolt  = zapLineObject.GetComponentInChildren<LightningBoltScript>(true)
                };
                _zapLineRefsCache[zapLineObject] = lineRefs;
            }
            Transform startTransform = lineRefs.start;
            Transform endTransform = lineRefs.end;
            Vector3 lineStartPosition = GetZapLineRenderPosition(startPosition);
            Vector3 lineEndPosition = GetZapLineRenderPosition(endPosition);
            lineEndPosition.y = ZapLineEndYWorld;
            if (startTransform != null) startTransform.position = lineStartPosition;
            if (endTransform != null) endTransform.position = lineEndPosition;

            LightningBoltScript bolt = lineRefs.bolt;
            if (bolt != null)
            {
                bolt.LockYAxis = true;
                LineRenderer lineRenderer = bolt.GetComponent<LineRenderer>();
                PrepareZapLineRenderer(lineRenderer);
                // 풀 재사용 라인도 매 활성화마다 width/alpha 0 → 타깃으로 페이드인 — 시작·종료 대칭 보장.
                if (lineRenderer != null && _zapLineTargetWidths.TryGetValue(lineRenderer, out float fadeTarget))
                {
                    lineRenderer.widthMultiplier = 0f;
                    Color sc0 = lineRenderer.startColor; sc0.a = 0f; lineRenderer.startColor = sc0;
                    Color ec0 = lineRenderer.endColor;   ec0.a = 0f; lineRenderer.endColor   = ec0;
                    _zapLineFadeCoroutines.Add(StartCoroutine(FadeInZapLineRoutine(lineRenderer, fadeTarget)));
                }
                // ROLLBACK_ZAP_LINE_FORCE_WORLD_SPACE:
                // ItemZap owns FxZapLine as an animated child. Force this particular lightning
                // renderer to world-space so the line always connects the Zap object and target
                // balloon instead of inheriting animated local offsets from the prefab.
                bolt.StartObject = null;
                bolt.EndObject = null;
                bolt.StartPosition = lineStartPosition;
                bolt.EndPosition = lineEndPosition;
                // ROLLBACK_ZAP_LINE_KEEP_WORLD_DEPTH:
                // In this game the board uses world Z as field depth. The generic lightning
                // script flattens Z for orthographic cameras, which makes Zap lines move only
                // left/right instead of connecting the Zap model to each balloon.
                bolt.PreserveDepthInOrthographic = true;
                // ROLLBACK_ZAP_LINE_CONTINUOUS_VISIBILITY:
                // ManualMode clears the LineRenderer after Duration. Keep each bolt visible until
                // the next Zap target so the effect does not blink out between balloon pops.
                bolt.ManualMode = true;
                bolt.Duration = Mathf.Max(0.01f, visibleDuration);
                bolt.Trigger();

                // 자글거림 코루틴이 매 tick 절대 좌표로 재설정할 수 있도록 baseline 기록.
                // (jitter 누적 드리프트 방지 — 코루틴은 baseline + jitter 로 항상 새 절대값을 쓴다.)
                _zapLineBaselines[bolt] = new ZapLineBaseline { start = lineStartPosition, end = lineEndPosition };
            }
        }

        // 4갈래 Zap 라인을 동일 타이밍에 ConfigureZapLine으로 활성화.
        // 시작점은 약하게 fan-out, 끝점은 더 크게 X/Z 분산해 부채꼴로 퍼지게 한다.
        // Y는 GetZapLineRenderPosition 가 결정한 값 그대로(위아래 튀지 않게).
        private void ConfigureZapLineFan(List<GameObject> zapLineObjects, Vector3 startPosition, Vector3 endPosition, float visibleDuration)
        {
            if (zapLineObjects == null || zapLineObjects.Count == 0)
                return;

            int count = zapLineObjects.Count;

            // 라인 수가 ZapLineConcurrentCount(=4)가 아니면 가드: 모두 동일 endPosition 사용.
            if (count != ZapLineConcurrentCount)
            {
                for (int i = 0; i < count; i++)
                    ConfigureZapLine(zapLineObjects[i], startPosition, endPosition, visibleDuration);
                return;
            }

            // 시작점 fan-out — 시작점이 너무 겹치지 않을 정도로만 약하게.
            Vector2[] startOffsetsXZ = {
                new Vector2(+0.00f, +0.00f),
                new Vector2(+0.05f, +0.03f),
                new Vector2(-0.05f, -0.03f),
                new Vector2(+0.03f, -0.05f),
            };

            // 끝점 fan-out — 4개 라인이 부채꼴로 퍼지도록 더 크게, 각 라인 부호/크기 모두 다르게.
            Vector2[] endOffsetsXZ = {
                new Vector2(+0.18f, +0.12f),
                new Vector2(-0.20f, +0.10f),
                new Vector2(+0.15f, -0.18f),
                new Vector2(-0.16f, -0.14f),
            };

            for (int i = 0; i < count; i++)
            {
                Vector3 startOff = new Vector3(startOffsetsXZ[i].x, 0f, startOffsetsXZ[i].y);
                Vector3 endOff = new Vector3(endOffsetsXZ[i].x, 0f, endOffsetsXZ[i].y);
                ConfigureZapLine(zapLineObjects[i], startPosition + startOff, endPosition + endOff, visibleDuration);
            }
        }

        // FxZapLine 라인이 활성화된 동안 매 tick LightningBoltScript.Trigger() 를 재호출하여
        // 번개 모양이 매번 새로 그려지도록(자글거리도록) 한다. 끝점에는 미세 jitter 만 더해
        // 라인이 잡고 있는 시작/타겟 방향성은 유지한다.
        private IEnumerator JiggleZapLinesRoutine(List<GameObject> zapLineObjects)
        {
            if (zapLineObjects == null || zapLineObjects.Count == 0)
                yield break;

            // 시작 시 한 번만 LightningBoltScript 캐시. ConfigureZapLine 이 baseline 을
            // 매번 갱신하므로 코루틴은 jitter 적용만 담당한다.
            var bolts = new List<LightningBoltScript>(zapLineObjects.Count);
            for (int i = 0; i < zapLineObjects.Count; i++)
            {
                GameObject line = zapLineObjects[i];
                if (line == null)
                    continue;
                LightningBoltScript bolt = line.GetComponentInChildren<LightningBoltScript>(true);
                if (bolt != null)
                    bolts.Add(bolt);
            }

            if (bolts.Count == 0)
                yield break;

            while (true)
            {
                bool anyActive = false;
                for (int i = 0; i < zapLineObjects.Count; i++)
                {
                    GameObject line = zapLineObjects[i];
                    if (line == null || !line.activeInHierarchy)
                        continue;

                    LightningBoltScript bolt = i < bolts.Count ? bolts[i] : null;
                    if (bolt == null)
                        continue;

                    if (!_zapLineBaselines.TryGetValue(bolt, out ZapLineBaseline baseline))
                        continue;

                    anyActive = true;

                    Vector3 jitterStart = new Vector3(
                        Random.Range(-ZapLineJiggleEndpointJitter, ZapLineJiggleEndpointJitter),
                        0f,
                        Random.Range(-ZapLineJiggleEndpointJitter, ZapLineJiggleEndpointJitter));
                    Vector3 jitterEnd = new Vector3(
                        Random.Range(-ZapLineJiggleEndpointJitter, ZapLineJiggleEndpointJitter),
                        0f,
                        Random.Range(-ZapLineJiggleEndpointJitter, ZapLineJiggleEndpointJitter));

                    bolt.StartPosition = baseline.start + jitterStart;
                    bolt.EndPosition = baseline.end + jitterEnd;
                    // 다음 tick 까지 라인이 사라지지 않도록 Duration 보강(ConfigureZapLine 의 값이 너무 짧을 때 대비).
                    bolt.Duration = Mathf.Max(bolt.Duration, ZapLineJiggleMaxInterval * 1.5f);
                    bolt.Trigger();
                }

                if (!anyActive)
                    yield break;

                // 등급1 perf: 매 tick `new WaitForSeconds` 할당 제거 — 수동 타이머 (동작 동일, scaled time).
                float wait = Random.Range(ZapLineJiggleMinInterval, ZapLineJiggleMaxInterval);
                for (float t = 0f; t < wait; t += Time.deltaTime)
                    yield return null;
            }
        }

        private void StopZapLineJiggle()
        {
            if (_zapLineJiggleCo != null)
            {
                StopCoroutine(_zapLineJiggleCo);
                _zapLineJiggleCo = null;
            }
            _zapLineBaselines.Clear();
        }

        private IEnumerator FadeInZapLineRoutine(LineRenderer lr, float targetWidth)
        {
            if (lr == null) yield break;
            float t = 0f;
            while (t < ZapLineFadeInDuration)
            {
                if (lr == null) yield break;
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / ZapLineFadeInDuration);
                lr.widthMultiplier = Mathf.Lerp(0f, targetWidth, k);
                Color sc = lr.startColor; sc.a = k; lr.startColor = sc;
                Color ec = lr.endColor;   ec.a = k; lr.endColor   = ec;
                yield return null;
            }
            if (lr != null)
            {
                lr.widthMultiplier = targetWidth;
                Color sc = lr.startColor; sc.a = 1f; lr.startColor = sc;
                Color ec = lr.endColor;   ec.a = 1f; lr.endColor   = ec;
            }
        }

        private IEnumerator FadeOutZapLinesRoutine(List<GameObject> zapLineObjects)
        {
            if (zapLineObjects == null || zapLineObjects.Count == 0) yield break;

            // LineRenderer + runtime material + 페이드 시작 width/alpha 캐시. FadeOut 중 LightningBoltScript는 사용하지 않음(위치/Trigger 호출 금지 — 마지막 순간 라인 굵게 튀는 결함 차단).
            // GC alloc 최소화를 위해 한 번만 할당, foreach 미사용.
            int count = zapLineObjects.Count;
            var renderers = new List<LineRenderer>(count);
            var startWidths = new List<float>(count);
            var startAlphas = new List<float>(count);
            // 라인별 runtime material 캐시(매 프레임 lr.material 호출 시 인스턴스 재생성 비용 방지).
            // _TintColor / _Color 의 alpha 도 width 와 동일 widthAlphaK 로 페이드 — particle/unlit 셰이더 양쪽 대응.
            var runtimeMats = new List<Material>(count);
            var startTintAlphas = new List<float>(count);
            var startColorAlphas = new List<float>(count);

            for (int i = 0; i < count; i++)
            {
                GameObject line = zapLineObjects[i];
                if (line == null) continue;
                LineRenderer lr = line.GetComponentInChildren<LineRenderer>(true);
                if (lr == null) continue;

                renderers.Add(lr);
                startWidths.Add(lr.widthMultiplier);
                startAlphas.Add(lr.startColor.a);

                Material runtimeMat = lr.material;
                runtimeMats.Add(runtimeMat);
                startTintAlphas.Add(runtimeMat != null && runtimeMat.HasProperty("_TintColor") ? runtimeMat.GetColor("_TintColor").a : 1f);
                startColorAlphas.Add(runtimeMat != null && runtimeMat.HasProperty("_Color") ? runtimeMat.GetColor("_Color").a : 1f);
            }

            int cached = renderers.Count;
            if (cached == 0) yield break;

            float t = 0f;
            while (t < ZapLineFadeOutDuration)
            {
                t += Time.deltaTime;
                float ratio = Mathf.Clamp01(t / ZapLineFadeOutDuration);
                float widthAlphaK = 1f - ratio;

                for (int i = 0; i < cached; i++)
                {
                    // FadeOut 중 LightningBoltScript 미사용(위치 변경·Trigger 호출·jitter 일체 금지). 위치는 fade 시작 시점의 geometry 그대로 유지하고 width/vertex alpha + material alpha 만 0으로 수렴 → 마지막 순간 라인 굵게 튀는 결함 차단.
                    LineRenderer lr = renderers[i];
                    if (lr != null)
                    {
                        lr.widthMultiplier = startWidths[i] * widthAlphaK;
                        float a = startAlphas[i] * widthAlphaK;
                        Color sc = lr.startColor; sc.a = a; lr.startColor = sc;
                        Color ec = lr.endColor;   ec.a = a; lr.endColor   = ec;
                    }

                    // Material alpha fade — vertex color 만 0 으로 만들어도 particle/_TintColor 셰이더는
                    // material._TintColor.a 가 곱연산되어 끝까지 잔상이 남는다. width 와 같은 K 로 동기 페이드.
                    Material runtimeMat = runtimeMats[i];
                    if (runtimeMat != null)
                    {
                        if (runtimeMat.HasProperty("_TintColor"))
                        {
                            Color tc = runtimeMat.GetColor("_TintColor");
                            tc.a = startTintAlphas[i] * widthAlphaK;
                            runtimeMat.SetColor("_TintColor", tc);
                        }
                        if (runtimeMat.HasProperty("_Color"))
                        {
                            Color mc = runtimeMat.GetColor("_Color");
                            mc.a = startColorAlphas[i] * widthAlphaK;
                            runtimeMat.SetColor("_Color", mc);
                        }
                    }
                }

                yield return null;
            }

            // 종료 정리 — width/alpha 0 수렴 + positionCount=0 으로 LineRenderer geometry 비움. 직후 ReleaseZapLineToPool 이 SetActive(false)+풀 반납으로 마무리.
            for (int i = 0; i < cached; i++)
            {
                LineRenderer lr = renderers[i];
                if (lr != null)
                {
                    lr.widthMultiplier = 0f;
                    Color sc = lr.startColor; sc.a = 0f; lr.startColor = sc;
                    Color ec = lr.endColor;   ec.a = 0f; lr.endColor   = ec;
                    lr.positionCount = 0;
                }

                Material runtimeMat = runtimeMats[i];
                if (runtimeMat != null)
                {
                    if (runtimeMat.HasProperty("_TintColor"))
                    {
                        Color tc = runtimeMat.GetColor("_TintColor"); tc.a = 0f; runtimeMat.SetColor("_TintColor", tc);
                    }
                    if (runtimeMat.HasProperty("_Color"))
                    {
                        Color mc = runtimeMat.GetColor("_Color"); mc.a = 0f; runtimeMat.SetColor("_Color", mc);
                    }
                }
            }
        }

        private Vector3 GetZapLineRenderPosition(Vector3 position)
        {
            // ROLLBACK_ZAP_LINE_RENDER_LIFT:
            // Keep the lightning slightly above holder/balloon meshes. This affects only the
            // visual line, not Zap pop timing or target selection.
            position.y += ZapLineWorldLift;
            return position;
        }

        private void PrepareZapLineRenderer(LineRenderer lineRenderer)
        {
            if (lineRenderer == null)
                return;

            lineRenderer.enabled = true;
            lineRenderer.useWorldSpace = true;
            lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
            lineRenderer.sortingOrder = Mathf.Max(lineRenderer.sortingOrder, ZapLineSortingOrder);
            // ROLLBACK_ZAP_LINE_ALWAYS_ON_TOP_20260610:
            // 풍선(MeshRenderer, Geometry queue 2000) depth buffer 에 의해 LineRenderer 가 가려지는 문제 해결.
            // sortingOrder 만으로는 부족 — Transparent shader 의 ZTest LEqual 기본값이 픽셀을 컬링.
            // material 인스턴스(공유 X)에 _ZTest = Always(8) 강제 + renderQueue 상향.
            Material runtimeMat = lineRenderer.material; // instance 생성(원본 prefab 보호)
            if (runtimeMat != null)
            {
                if (runtimeMat.HasProperty("_ZTest"))
                    runtimeMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                if (runtimeMat.HasProperty("_ZWrite"))
                    runtimeMat.SetInt("_ZWrite", 0);
                runtimeMat.renderQueue = ZapLineRenderQueue;
            }
            if (!_zapLineTargetWidths.ContainsKey(lineRenderer))
            {
                float targetWidth = Mathf.Max(lineRenderer.widthMultiplier, ZapLineMinWidth);
                _zapLineTargetWidths[lineRenderer] = targetWidth;
                lineRenderer.widthMultiplier = 0f;
                Color sc = lineRenderer.startColor; sc.a = 0f; lineRenderer.startColor = sc;
                Color ec = lineRenderer.endColor;   ec.a = 0f; lineRenderer.endColor   = ec;
            }
            lineRenderer.numCapVertices = Mathf.Max(lineRenderer.numCapVertices, 2);
        }

        private Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null)
                return null;

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == childName)
                    return children[i];
            }

            return null;
        }

        private void PlayZapFinish(GameObject zapObject)
        {
            if (zapObject == null)
                return;
            _isZapAnimationPlaying = false;

            // ROLLBACK_ZAP_FINISH_STAY_ON_ATTACK_ORIGIN:
            // ZapFinish is the authored finish animation for the Zap model. Do not snap the
            // model to the last popped balloon; FxZapLine already points at each target.

            bool triggered = TrySetZapFinishTrigger(zapObject);
            ParticleSystem[] particles = zapObject.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                particles[i].Clear(true);
                particles[i].Play(true);
            }

            if (!triggered)
                zapObject.transform.DOPunchScale(Vector3.one * 0.15f, ZapFinishLifetime, 6, 0.4f);
        }

        // FxZapLine 종료 → ZapFinish 트리거 발사 → ZapAttackFinish 클립 재생 완료(또는 Grace 경과) 후 비활성화 + Destroy. 타이머 기반 Destroy(0.6f) 대비 ZapAttackFinish 클립이 컷되지 않도록 Animator 상태 머신을 직접 폴링한다.
        private IEnumerator WaitForZapFinishThenDestroyRoutine(GameObject zapObject)
        {
            if (zapObject == null)
                yield break;

            PlayZapFinish(zapObject);

            Animator animator = zapObject.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                yield return new WaitForSeconds(ZapFinishLifetime);
            }
            else
            {
                float graceElapsed = 0f;
                while (graceElapsed < ZapFinishTransitionGrace)
                {
                    if (animator == null)
                        break;
                    if (animator.IsInTransition(0))
                    {
                        graceElapsed += Time.deltaTime;
                        yield return null;
                        continue;
                    }
                    if (animator.GetCurrentAnimatorStateInfo(0).IsName("ZapAttackFinish"))
                        break;
                    graceElapsed += Time.deltaTime;
                    yield return null;
                }

                if (animator != null && animator.GetCurrentAnimatorStateInfo(0).IsName("ZapAttackFinish"))
                {
                    float stateLength = animator.GetCurrentAnimatorStateInfo(0).length;
                    float playElapsed = 0f;
                    while (animator != null)
                    {
                        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                        if (!info.IsName("ZapAttackFinish"))
                            break;
                        if (info.normalizedTime >= 0.98f)
                            break;
                        if (playElapsed >= stateLength)
                            break;
                        playElapsed += Time.deltaTime;
                        yield return null;
                    }
                }
            }

            yield return new WaitForSeconds(ZapFinishPostPlayBuffer);

            if (zapObject != null)
            {
                zapObject.SetActive(false);
                Destroy(zapObject);
            }
        }

        private bool TrySetZapFinishTrigger(GameObject zapObject)
        {
            return TrySetZapTrigger(zapObject, "ZapFinish", "Finish", "Zap Finish Trigger");
        }

        private bool TrySetZapTrigger(GameObject zapObject, params string[] triggerNames)
        {
            Animator animator = zapObject.GetComponentInChildren<Animator>(true);
            if (animator == null)
                return false;

            for (int i = 0; i < animator.parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = animator.parameters[i];
                if (parameter.type != AnimatorControllerParameterType.Trigger)
                    continue;

                for (int j = 0; j < triggerNames.Length; j++)
                {
                    if (parameter.name == triggerNames[j])
                    {
                        animator.SetTrigger(parameter.name);
                        return true;
                    }
                }
            }

            return false;
        }

        // Hand booster now uses SELECT_TOOL behavior (holder selection mode).
        // The HAND case in HandleBoosterUsed sets _awaitingHolderSelection = true
        // and moves camera to queue, identical to SELECT_TOOL.
        // OnHolderSelected handles the actual deployment for both boosters.

        #endregion
    }
}
