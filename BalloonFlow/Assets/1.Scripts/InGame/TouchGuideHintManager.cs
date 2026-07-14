using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// ROLLBACK_TOUCH_GUIDE_HINT_20260713: 터치 유도(핸드 포인터) 힌트.
    ///   1~5 스테이지 한정. 현재 터뜨려야 할 풍선(최외곽 색)과 같은 색의 대기 다트박스를 1~3행에서 찾아,
    ///   박스 실제 행과 무관하게 '그 열의 1행(탭 가능한 앞줄)' 위에 핸드 연출을 띄운다.
    ///   - 우선순위: 가장 상단행(작은 row) → 동률이면 가장 우측열(큰 column). (터치 쉬운 것)
    ///   - Case2: 타겟이 2행이면 1행 위에 뜨고, 같은 열 비-타겟 탭으론 안 꺼짐(타겟이 1행으로 승격 후 탭돼야 종료).
    ///   - 종료: 타겟 holder 소멸(탭됨) / 다른 열 탭 / 1~3행 매칭 소멸.
    ///   핸드 스프라이트: Resources/UI/tutorialHand 동적 로드(폴백 TutorialManager.HandSprite). 연출: 튜토와 동일 좌우 회전(까딱까딱) yoyo.
    ///   트리거(ROLLBACK_TOUCH_GUIDE_IDLE_20260713): 튜토 전부 끝난 뒤 + '마지막 입력 후 5~10초 무입력(idle)' 일 때만 노출.
    ///     한 번 탭하면 idle 리셋 → 숨김 → 다시 5~10초 기다리면 재노출. 단 힌트가 이미 떠 있는 동안(Case2)엔 idle 무시.
    ///   Dim 없음.
    /// </summary>
    public class TouchGuideHintManager : SceneSingleton<TouchGuideHintManager>
    {
        private const int MAX_HINT_LEVEL = 5;
        private const int MAX_QUEUE_ROW = 3;           // 1~3행만 탐색
        private const float ACTIVATE_GRACE = 1.0f;     // 레벨 진입 후 이만큼 뒤부터(튜토 시작 전 깜빡임 방지)
        private const float IDLE_MIN = 5f;             // 튜토 종료 후 이만큼 무입력이어야 노출(하한)
        private const float IDLE_MAX = 10f;            // 〃 상한. 매 대기마다 [MIN,MAX] 랜덤(기계적 반복 방지)
        private const int HAND_SORTING_ORDER = 150;    // 게임 UI 위 / 팝업(200) 아래

        // ROLLBACK_TOUCH_GUIDE_TUNE_20260714: 핸드 연출 튜닝값 — Play 중 런타임 GameObject(TouchGuideHintManager)를
        //   선택해 인스펙터에서 조정(크기/회전/마진 즉시 반영). AddComponent 생성이라 Play 종료 시 코드 기본값 복귀 →
        //   확정되면 아래 기본값을 그 값으로 수정해 고정 사용.
        [Header("[Touch Hand — 튜닝값 (Play 중 조정 후 확정값을 기본값으로)]")]
        [SerializeField, Tooltip("핸드 이미지 크기(px)")] private float _handSize = 120f;
        [SerializeField, Tooltip("좌우 회전 각도(±도) — 까딱까딱 스윙 폭")] private float _rotAngle = 18f;
        [SerializeField, Tooltip("한쪽 스윙 시간(초)")] private float _rotDuration = 0.5f;
        [SerializeField, Tooltip("회전축 pivot(0~1) — 손끝/손목 기준")] private Vector2 _handPivot = new Vector2(0.75f, 0.1f);
        [SerializeField, Tooltip("타겟 위치 대비 스크린 오프셋(px): +x 오른쪽, +y 위")] private Vector2 _positionMargin = Vector2.zero;
        private float _bobRotAngle = float.NaN, _bobRotDuration = float.NaN; // 라이브 회전 튜닝 반영용 캐시

        private int _targetHolderId = -1;
        private int _targetColumn = -1;
        private float _activateAtTime;
        private float _lastInputTime;                  // idle 기준(레벨진입/힌트종료 시 now 로 리셋, 매프레임 InputHandler 와 동기화)
        private float _idleThreshold;                  // 이번 대기 사이클의 목표 무입력 시간(5~10s 랜덤)

        private Camera _cam;
        private Canvas _canvas;
        private RectTransform _handRoot;   // 타겟 스크린 좌표를 매 프레임 추종
        private Image _handImg;            // 실제 손 이미지(자식) — Move+Pulse
        private const string HAND_SPRITE_PATH = "UI/tutorialHand"; // Resources 동적 로드 경로(Assets/Resources/UI/tutorialHand.png)
        private Sprite _spriteIdle;        // 실제 사용 스프라이트(지연 확정)
        private Sequence _bobSeq;
        private bool _visible;

        protected override void OnSingletonAwake()
        {
            _activateAtTime = Time.unscaledTime + ACTIVATE_GRACE;
            RestartIdleWait();
            BuildHandUi();     // 먼저 _handImg 생성 → 이후 EnsureSprite 가 스프라이트를 Image 에 반영
            EnsureSprite();
            EventBus.Subscribe<OnHolderTapped>(HandleHolderTapped);
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Subscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Subscribe<OnBoardFailed>(HandleBoardFailed);
        }

        protected override void OnDestroy()
        {
            EventBus.Unsubscribe<OnHolderTapped>(HandleHolderTapped);
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Unsubscribe<OnBoardFailed>(HandleBoardFailed);
            _bobSeq?.Kill();
            base.OnDestroy();
        }

        private void Update()
        {
            // 마지막 물리 입력 동기화(억제/로딩 무관 로우 프레스) + 새 입력이 들어오면 이번 대기의 idle 목표 재추첨.
            if (InputHandler.LastInputTime > _lastInputTime)
            {
                _lastInputTime = InputHandler.LastInputTime;
                _idleThreshold = Random.Range(IDLE_MIN, IDLE_MAX);
            }

            if (!ShouldRun()) { ClearTarget(); HideHint(); return; }

            // 최근 입력(idle 미달) → 무조건 숨김. "클릭하면 바로 사라지고, 무입력 5~10초 뒤 다시 나옴."
            //   표시중(Case2)이든 아니든 입력 즉시 숨김 → idle 재충전되면 아래에서 다시 노출.
            if (Time.unscaledTime - _lastInputTime < _idleThreshold) { HideHint(); return; }

            // idle 충분 → 타겟 유지검증(Case2: 2행 타겟이 1행으로 승격돼도 같은 holderId 유지) or 신규 획득 후 표시.
            if (_targetHolderId >= 0)
            {
                var t = HolderManager.Instance.FindHolderPublic(_targetHolderId);
                if (t == null || t.isConsumed || !IsColorStillOutermost(t.color)) ClearTarget();
                else { ShowHintAtColumn(_targetColumn); return; }
            }
            if (TryAcquireTarget(out int hid, out int col))
            {
                _targetHolderId = hid; _targetColumn = col;
                ShowHintAtColumn(col);
            }
            else HideHint();
        }

        private void RestartIdleWait()
        {
            _lastInputTime = Time.unscaledTime;
            _idleThreshold = Random.Range(IDLE_MIN, IDLE_MAX);
        }

        private bool ShouldRun()
        {
            if (!HolderManager.HasInstance || !BoardStateManager.HasInstance || !HolderVisualManager.HasInstance) return false;
            if (!LevelManager.HasInstance || LevelManager.Instance.CurrentLevelId > MAX_HINT_LEVEL) return false;
            if (Time.unscaledTime < _activateAtTime) return false;
            // 튜토리얼과 겹치면 안 됨 → 튜토 활성 중 억제(끝난 뒤에만 노출).
            if (TutorialController.HasInstance && TutorialController.Instance.IsTutorialActive()) return false;
            return true;
        }

        // ─── 타겟 선정 ───

        private bool TryAcquireTarget(out int holderId, out int column)
        {
            holderId = -1; column = -1;
            HashSet<int> colors = BoardStateManager.Instance.GetReachableOutermostColors();
            if (colors == null || colors.Count == 0) return false;

            int cols = HolderManager.Instance.QueueColumns;
            int bestRow = int.MaxValue, bestCol = -1, bestId = -1;
            for (int c = 0; c < cols; c++)
            {
                List<HolderData> list = HolderManager.Instance.GetColumnHolders(c);
                if (list == null) continue;
                int rows = Mathf.Min(MAX_QUEUE_ROW, list.Count);
                for (int r = 0; r < rows; r++)
                {
                    var h = list[r];
                    if (h == null || h.isDeploying || h.isMovingToRail || h.isConsumed) continue;
                    if (!colors.Contains(h.color)) continue;
                    // 우선순위: 상단행(작은 r) 우선, 동률이면 우측열(큰 c) 우선.
                    if (r < bestRow || (r == bestRow && c > bestCol))
                    { bestRow = r; bestCol = c; bestId = h.holderId; }
                }
            }
            if (bestId < 0) return false;
            holderId = bestId; column = bestCol; return true;
        }

        private bool IsColorStillOutermost(int color)
        {
            var colors = BoardStateManager.Instance.GetReachableOutermostColors();
            return colors != null && colors.Contains(color);
        }

        private void ClearTarget() { _targetHolderId = -1; _targetColumn = -1; }

        // ─── 이벤트 ───

        private void HandleHolderTapped(OnHolderTapped evt)
        {
            // 어떤 홀더 탭이든 타겟 리셋 → 다음 idle(무입력 5~10초) 때 현재 최적 매칭으로 신규 획득.
            //   (탭 자체로 InputHandler.LastInputTime 갱신 → Update 의 idle 게이트가 즉시 숨김 처리.)
            ClearTarget();
        }

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            ClearTarget();
            _activateAtTime = Time.unscaledTime + ACTIVATE_GRACE;
            RestartIdleWait();
            HideHint();
        }

        private void HandleBoardCleared(OnBoardCleared _) { ClearTarget(); HideHint(); }
        private void HandleBoardFailed(OnBoardFailed _) { ClearTarget(); HideHint(); }

        // ─── 핸드 UI ───

        private void ShowHintAtColumn(int column)
        {
            if (_handRoot == null) return;
            EnsureSprite(); // 튜토 종료 후 시점엔 HandSprite 바인딩됨 → 여기서 확정 반영
            if (!HolderVisualManager.Instance.TryGetColumnFrontRowWorldPos(column, out Vector3 world)) { HideHint(); return; }
            Camera cam = ResolveCamera();
            if (cam == null) { HideHint(); return; }
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z <= 0f) { HideHint(); return; } // 카메라 뒤
            // 위치 마진(스크린 오프셋) 적용.
            _handRoot.position = new Vector3(screen.x + _positionMargin.x, screen.y + _positionMargin.y, 0f);
            // 라이브 튜닝: 크기/피벗을 매 프레임 반영(인스펙터 변경 즉시).
            if (_handImg != null)
            {
                var rt = _handImg.rectTransform;
                if (rt.sizeDelta.x != _handSize) { rt.sizeDelta = new Vector2(_handSize, _handSize); _handRoot.sizeDelta = rt.sizeDelta; }
                if (rt.pivot != _handPivot) rt.pivot = _handPivot;
            }
            if (!_visible) { _visible = true; _canvas.enabled = true; PlayBob(); }
            else if (_rotAngle != _bobRotAngle || _rotDuration != _bobRotDuration) PlayBob(); // 회전 튜닝 라이브 반영
        }

        private void HideHint()
        {
            if (!_visible) return;
            _visible = false;
            _bobSeq?.Kill(); _bobSeq = null;
            if (_canvas != null) _canvas.enabled = false;
        }

        // 튜토리얼 핸드 연출 '그대로' 재사용: 좌우 회전(까딱까딱). +ANGLE 에서 시작해 -ANGLE 로 yoyo → 대칭 좌우 스윙.
        //   TutorialManager.BuildHandTweenSequence 를 Rotate 타입으로 호출(스케일/이동 없음, InOutSine·unscaled·yoyo 동일).
        private void PlayBob()
        {
            _bobSeq?.Kill();
            if (_handImg == null) return;
            RectTransform h = _handImg.rectTransform;
            h.anchoredPosition = Vector2.zero;
            h.localScale = Vector3.one;
            h.localEulerAngles = new Vector3(0f, 0f, _rotAngle); // 시작 = +ANGLE
            // base=+ANGLE, rotation=-2*ANGLE → target=-ANGLE. yoyo 가 +ANGLE↔-ANGLE 대칭 스윙.
            _bobSeq = TutorialManager.BuildHandTweenSequence(
                h, Vector2.zero, Vector3.one, new Vector3(0f, 0f, _rotAngle),
                TutorialHandTweenType.Rotate, Vector2.zero, 1f, -2f * _rotAngle, _rotDuration);
            _bobRotAngle = _rotAngle; _bobRotDuration = _rotDuration; // 라이브 튜닝 반영 캐시
        }

        private void BuildHandUi()
        {
            var canvasGo = new GameObject("TouchGuideHintCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = HAND_SORTING_ORDER;
            canvasGo.AddComponent<CanvasScaler>();
            // GraphicRaycaster 미부착 — 힌트는 입력을 절대 먹지 않음(터치 그대로 통과).

            var rootGo = new GameObject("HandRoot");
            rootGo.transform.SetParent(canvasGo.transform, false);
            _handRoot = rootGo.AddComponent<RectTransform>();
            _handRoot.sizeDelta = new Vector2(_handSize, _handSize);

            var imgGo = new GameObject("Hand");
            imgGo.transform.SetParent(rootGo.transform, false);
            _handImg = imgGo.AddComponent<Image>();
            _handImg.raycastTarget = false;
            _handImg.preserveAspect = true;
            _handImg.rectTransform.sizeDelta = new Vector2(_handSize, _handSize);
            // 회전축 pivot — 인스펙터 튜닝값.
            _handImg.rectTransform.pivot = _handPivot;
            if (_spriteIdle != null) _handImg.sprite = _spriteIdle;
            else _handImg.color = new Color(1f, 1f, 1f, 0.35f); // 스프라이트 미로드 시 최소 가시(에셋 배치 전 진단)

            _canvas.enabled = false;
        }

        // 핸드 스프라이트 확정(지연 + 동적 로드). 1순위 Resources/UI/tutorialHand → 2순위 튜토 런타임 HandSprite 폴백.
        //   tutorialHand 를 Assets/Resources/UI/ 로 옮겨 Resources.Load 로 런타임 동적 로드(인스펙터 할당 불필요).
        private void EnsureSprite()
        {
            if (_spriteIdle != null) return;
            _spriteIdle = Resources.Load<Sprite>(HAND_SPRITE_PATH);
            if (_spriteIdle == null && TutorialManager.HasInstance)
                _spriteIdle = TutorialManager.Instance.HandSprite; // 폴백: 튜토 런타임 바인딩 스프라이트
            if (_spriteIdle == null)
                Debug.LogWarning($"[TouchGuideHint] Resources/{HAND_SPRITE_PATH} 로드 실패 — 경로/에셋 확인 필요.");
            if (_spriteIdle != null && _handImg != null)
            {
                _handImg.sprite = _spriteIdle;
                _handImg.color = Color.white;
            }
        }

        private Camera ResolveCamera()
        {
            if (_cam != null && _cam.isActiveAndEnabled) return _cam;
            _cam = Camera.main;
            if (_cam == null)
            {
                var cams = Camera.allCameras;
                if (cams != null && cams.Length > 0) _cam = cams[0];
            }
            return _cam;
        }
    }
}
