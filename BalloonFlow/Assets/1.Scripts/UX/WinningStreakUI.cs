using DG.Tweening;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// WinningStreak UI 헬퍼 — HUD / Popup (Quit/Continue/WinningStreak) 공통 사용.
    /// - 현재 streak 의 배수 (1/5/10/25/100) 결정
    /// - Multiplier prefab 안 SelectFrame / TextYellow 의 배수별 위치 매핑 (HUD / Popup 위치 다름)
    /// - PopupQuit/Continue 의 Multiplier Animator state 재생 (Multiplier5/10/25/100)
    ///
    /// Mask 는 위치 고정. SelectFrame + TextYellow 만 이동.
    /// </summary>
    public static class WinningStreakUI
    {
        /// <summary>PopupQuit/Continue 의 Multiplier 등장 시작 X. 사용자 명세 -724.</summary>
        public const float MULTIPLIER_ENTER_FROM_X = -724f;
        public const float MULTIPLIER_ENTER_TO_X   = 0f;
        public const float MULTIPLIER_ENTER_DURATION = 0.35f;
        public const float MULTIPLIER_SELECT_MOVE_DURATION = 0.18f;

        private static readonly int[] Tiers = { 1, 5, 10, 25, 100 };

        // HUD (UIHud 의 Multiplier) — SelectFrame / TextYellow X 위치 (Mask 고정).
        private static readonly float[] HudSelectFrameX = { -338f, -150f,  30f,  210f,  390f };
        private static readonly float[] HudTextYellowX  = {  358f,  170f, -10f, -190f, -370f };

        // UILobby WS — SelectFrame / TextYellow X 위치 (Mask 고정). 디자이너 명세 2026-06-15.
        private static readonly float[] LobbySelectFrameX = { -338f, -150f,  30f,  210f,  390f };
        private static readonly float[] LobbyTextYellowX  = {  358f,  170f, -10f, -190f, -370f };

        // PopupWinningStreak — 위치만, 애니메이션 없음.
        // ROLLBACK_WINNING_STREAK_MULTIPLIER_POS_20260605:
        // Popup/Lobby use the same designer-provided positions. Mask stays fixed.
        private static readonly float[] PopupSelectFrameX = { -360f, -184f,  -10f,  174f,  350f };
        private static readonly float[] PopupTextYellowX  = {  360f,  184f, -10f, -174f, -350f };

        /// <summary>현재 streak 의 배수 (1/5/10/25/100). State/Config 미준비 시 1 반환.</summary>
        public static int ResolveCurrentMultiplier()
        {
            if (!WinningStreakManager.HasInstance) return 1;
            var mgr = WinningStreakManager.Instance;
            if (mgr.TryPeekPendingLobbyAnimation(out var pending)
                && pending != null
                && pending.startMultiplier > 0)
            {
                // ROLLBACK_WS_GLOBAL_PENDING_MULTIPLIER_20260624:
                // State.currentStreak is advanced on clear, but lobby UI must keep the pre-clear
                // multiplier until the queued lobby multiplier animation has played.
                return pending.startMultiplier;
            }
            // [#1] WS 미해금/미활성(서버 config 미로드 포함) 시 배수 1 → Continue/Quit 팝업의 WS view 미노출.
            // (OnLevelCleared 는 미해금에서도 currentStreak 를 올리므로 여기서 게이트하지 않으면 비-WS 유저도 연승 UI 노출됨)
            if (!mgr.IsUnlocked) return 1;
            var state = mgr.State;
            if (state == null) return 1;
            int streak = Mathf.Max(1, state.currentStreak);
            if (WinningStreakConfigService.HasInstance)
                return WinningStreakConfigService.Instance.ResolveStreakMultiplier(streak);
            return TierFromStreak(streak);
        }

        /// <summary>streak 1..5+ → 배수 1/5/10/25/100. Firestore config 미준비 시 fallback.</summary>
        public static int TierFromStreak(int streak)
        {
            if (streak <= 1) return 1;
            if (streak == 2) return 5;
            if (streak == 3) return 10;
            if (streak == 4) return 25;
            return 100;
        }

        /// <summary>HUD Multiplier 의 SelectFrame / TextYellow 를 현재 배수 위치로 이동. Mask 는 안 만짐.</summary>
        public static void ApplyHudPositionsForMultiplier(Transform multiplierRoot, int multiplier)
        {
            ApplyPositions(multiplierRoot, multiplier, HudSelectFrameX, HudTextYellowX);
        }

        /// <summary>PopupWinningStreak 의 Multiplier — 정적 위치 (애니메이션 없음).</summary>
        public static void ApplyPopupPositionsForMultiplier(Transform multiplierRoot, int multiplier)
        {
            ApplyPositions(multiplierRoot, multiplier, PopupSelectFrameX, PopupTextYellowX);
        }

        private static void ApplyPositions(Transform multiplierRoot, int multiplier, float[] selectFrameX, float[] textYellowX)
        {
            ApplyPositions(multiplierRoot, multiplier, selectFrameX, textYellowX, 0f);
        }

        private static void ApplyPositions(Transform multiplierRoot, int multiplier, float[] selectFrameX, float[] textYellowX, float duration)
        {
            if (multiplierRoot == null) return;
            int idx = IndexForMultiplier(multiplier);
            var selectFrame = FindChildRect(multiplierRoot, "SelectFrame");
            var textYellow  = FindChildRect(multiplierRoot, "TextYellow");
#if UNITY_EDITOR
            // [2026-06-10] "이동 안 됨" 진단용 — 프리팹 이름 불일치 시 원인 즉시 노출.
            if (selectFrame == null || textYellow == null)
                Debug.LogWarning($"[WinningStreakUI] SelectFrame/TextYellow 탐색 실패 — root={multiplierRoot.name}, " +
                                 $"SelectFrame={(selectFrame != null)}, TextYellow={(textYellow != null)}");
#endif
            MoveRectX(selectFrame, selectFrameX[idx], duration);
            MoveRectX(textYellow, textYellowX[idx], duration);
        }

        private static void MoveRectX(RectTransform rect, float x, float duration)
        {
            if (rect == null) return;
            rect.DOKill();
            if (duration <= 0f)
            {
                rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
                return;
            }

            rect.DOAnchorPosX(x, duration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true);
        }

        /// <summary>Popup (Quit/Continue) WinningStreak view 안 Multiplier 등장 연출 + 위치 (전부 코드 트윈, Animator 미사용).
        /// 절차: (1) x=-724 → 0 슬라이드 (2) 완료 후 SelectFrame/TextYellow 배수 위치로 동시 이동.
        /// [2026-06-10] (3) Animator state 재생 폐기 — PlayMultiplierSelect 로 통일 (Animator 가 위치 덮어쓰던 문제).
        /// multiplier=1 이면 호출하지 않음 (caller 가 view 자체 skip).</summary>
        public static void PlayMultiplierIdle(GameObject winningStreakView, int multiplier)
        {
            if (winningStreakView == null || multiplier <= 1) return;
            var multiplierRoot = FindChild(winningStreakView.transform, "Multiplier");
            if (multiplierRoot == null) return;

            // (1) 등장 슬라이드 — Multiplier root 의 anchoredPosition.x.
            if (multiplierRoot is RectTransform rt)
            {
                rt.DOKill();
                Vector2 start = rt.anchoredPosition;
                start.x = MULTIPLIER_ENTER_FROM_X;
                rt.anchoredPosition = start;
                rt.DOAnchorPosX(MULTIPLIER_ENTER_TO_X, MULTIPLIER_ENTER_DURATION)
                    .SetEase(Ease.OutCubic)
                    .SetUpdate(true) // popup 이라 timescale 무관.
                    .OnComplete(() => PlayMultiplierSelect(multiplierRoot, multiplier));
                return;
            }

            // (2) RectTransform 아님 — 슬라이드 생략, select 이동만 (안전망).
            PlayMultiplierSelect(multiplierRoot, multiplier);
        }

        /// <summary>SelectFrame / TextYellow 를 현재 배수 위치로 "동시" 코드 트윈 이동 — Animator 미사용 (코드 방식 확정).
        /// - PopupWinningStreak: Multiplier root 슬라이드 불필요 → 이것만 호출.
        /// - UILobby WS: root 슬라이드(코드, PlayWsMultiplierSlide)는 호출측이 재생, 완료 후 이것 호출.
        /// [2026-06-10 fix] 기존 PlayMultiplierState 는 위치 트윈과 동시에 Animator state 를 재생 — Animator 가
        /// SelectFrame/TextYellow 위치를 매 프레임 덮어써 이동이 안 먹던 원인. 여기선 Animator 를 비활성화해 코드가 이기게 함.
        /// Mask(TextMask) 는 안 만짐. 롤백: 호출부를 PlayMultiplierState 로 복원 + animator.enabled 라인 제거.</summary>
        public static void PlayMultiplierSelect(Transform multiplierRoot, int multiplier)
        {
            if (multiplierRoot == null) return;
            var animator = multiplierRoot.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.enabled) animator.enabled = false; // 코드 이동을 Animator 가 덮어쓰지 않게 차단
            ApplyPositions(multiplierRoot, multiplier, PopupSelectFrameX, PopupTextYellowX, MULTIPLIER_SELECT_MOVE_DURATION);
        }

        /// <summary>UILobby WS 의 Multiplier — SelectFrame / TextYellow 를 로비 명세 X 좌표로 동시 코드 트윈 이동. Animator 비활성화. Mask 미터치.
        /// [2026-06-22 v4 — 사용자 추가 지시 3rd-pass] v3 floor 0.35/speed 1000 은 짧은 거리도 0.35s 강제·긴 거리 0.73s 로 user 예시(x5→x1≈0.1s / x100→x1≈0.5s) 대비 과함. floor=0.10, speed=1500 으로 재튜닝(supersede v3). 산식·콜러·상승 분기는 동일.
        /// [2026-06-22 v2 — 사용자 추가 지시] 배수 감소 시 SelectFrame 이동이 상승과 동일 duration(0.18s)이라 거리(x100→x1=728px)가 커도 시간이 같아 속도가 4x 빨라 인지 불가 → 거리 비례 duration 도입(상승은 v1 동작 유지, 감소만 시간 증가).
        /// v1 (2026-06-15): 코드 트윈으로 통일, Animator 비활성화 — 이 의도는 v2 에서도 유지(누적, supersede 아님).
        /// owner: 본 ProjectHub task [사용자 추가 지시] 2026-06-22.</summary>
        public static void PlayLobbyMultiplierSelect(Transform multiplierRoot, int fromMultiplier, int toMultiplier)
        {
            if (multiplierRoot == null) return;
            var animator = multiplierRoot.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.enabled) animator.enabled = false;
            float duration = ResolveLobbyMultiplierMoveDuration(fromMultiplier, toMultiplier);
            ApplyPositions(multiplierRoot, toMultiplier, LobbySelectFrameX, LobbyTextYellowX, duration);
        }

        /// <summary>[WS Multiplier 감소 연출 속도 보정 v5 — 2026-06-22 4th-pass] 거리 비례 duration 유지, speed 만 절반 감속: 1500→750 px/s. floor(0.10s)·산식·상승 분기는 v4 그대로.
        /// 본 변경은 직전 v4(PR #366 머지: speed=1500, floor=0.10 → x100→x1=0.49s)를 **supersede** — v4 머지 후 사용자가 'x100→x1 현재 너무 빠름, 정확히 2배 느리게' 추가 피드백.
        /// owner 출처: 본 ProjectHub 태스크 [사용자 추가 지시] 2026-06-22 4th-pass — "x100→x1 v4(0.49s)는 너무 빠름, 2배 느리게(≈0.98s) 요청".
        /// 산식: speed=750px/s, floor=0.10s → duration = max(0.10, distance / 750). 산식 형태(Mathf.Max(floor, distance/speed))는 v4 와 동일, SPEED 상수만 절반.
        /// 검산표(LobbySelectFrameX 기준): x100→x1(728px)=0.97s / x5→x1(188px)=0.25s / x100→x25(180px)=0.24s / x10→x5(180px)=0.24s — v4(0.49s) × 2 = 0.97s ≈ 사용자 요청 충족.
        /// floor 0.10s 는 distance ≥ 75px (즉 LobbySelectFrameX 표상 모든 인접 단계 ≥ 180px) 에서 자연 무효화 — 짧은 거리도 0.20s 로 강제되지 않도록 floor 는 의도적으로 유지.
        /// 상승/동일(toIdx&gt;=fromIdx) 경로는 기존 0.18s(MULTIPLIER_SELECT_MOVE_DURATION) 유지 — 사용자 의도(v2~v5 모두 동일).
        /// 롤백: v4 상수(REDUCE_SPEED_PX_PER_SEC=1500f, MIN_REDUCE_DURATION=0.10f)로 복원.</summary>
        public static float ResolveLobbyMultiplierMoveDuration(int fromMultiplier, int toMultiplier)
        {
            int fromIdx = IndexForMultiplier(fromMultiplier);
            int toIdx   = IndexForMultiplier(toMultiplier);
            if (toIdx >= fromIdx) return MULTIPLIER_SELECT_MOVE_DURATION; // 상승/동일: 기존 유지
            float distance = Mathf.Abs(LobbySelectFrameX[toIdx] - LobbySelectFrameX[fromIdx]);
            const float REDUCE_SPEED_PX_PER_SEC = 750f;
            const float MIN_REDUCE_DURATION = 0.10f;
            return Mathf.Max(MIN_REDUCE_DURATION, distance / REDUCE_SPEED_PX_PER_SEC);
        }

        /// <summary>[2026-06-15] PopupQuit 의 Multiplier 연출 — 노출 즉시 Animator state 진입 + 슬라이드인 동시 진행 + 닫힐 때까지 무한 Loop.
        /// 절차: (1) Animator 활성화 + UnscaledTime + Multiplier{N} 재생 (노출 즉시) — 슬라이드인보다 먼저 호출해 첫 프레임부터 키프레임 적용.
        ///        (2) Multiplier root x=-724 → 0 슬라이드 SetUpdate(true) — 루트 위치와 Animator(자식 SelectFrame/TextYellow) 키프레임은 독립이라 동시 안전.
        ///        (3) 클립 loop=true 로 PopupQuit 닫힐 때까지 무한 재생. MultiplierDefault 복귀는 ResetPopupQuitMultiplierAnimation 가 담당.
        /// PauseManager 가 timeScale=0 으로 잡으므로 animator.updateMode=UnscaledTime 필수.
        /// 반드시 Multiplier5/10/25/100.anim 클립의 Loop Time 플래그가 Unity Inspector 에서 켜져 있어야 함 (바이너리 직렬화라 코드로 확인 불가).</summary>
        /// <remarks>PopupQuit과 PopupContinue 양쪽에서 호출됨 (2026-06-15 PopupContinue 멀티플라이어 통일). 메서드명에 'PopupQuit' 이 남아있는 것은 호환성 유지 위함.</remarks>
        public static void PlayMultiplierAnimationForPopupQuit(GameObject winningStreakView, int multiplier)
        {
            if (winningStreakView == null || multiplier <= 1) return;
            var multiplierRoot = FindChild(winningStreakView.transform, "Multiplier");
            if (multiplierRoot == null) return;

            // (1) 노출 즉시 Animator state 진입 — 슬라이드인 시작 전.
            PlayPopupQuitAnimatorStage(multiplierRoot, multiplier);

            // (2) 슬라이드인 — 루트 anchoredPosition.x. 자식 Animator 와 독립.
            if (multiplierRoot is RectTransform rt)
            {
                rt.DOKill();
                Vector2 start = rt.anchoredPosition;
                start.x = MULTIPLIER_ENTER_FROM_X;
                rt.anchoredPosition = start;
                rt.DOAnchorPosX(MULTIPLIER_ENTER_TO_X, MULTIPLIER_ENTER_DURATION)
                    .SetEase(Ease.OutCubic)
                    .SetUpdate(true);
            }
        }

        /// <remarks>PopupQuit과 PopupContinue 양쪽에서 호출됨 (2026-06-15 PopupContinue 멀티플라이어 통일). 메서드명에 'PopupQuit' 이 남아있는 것은 호환성 유지 위함.</remarks>
        private static void PlayPopupQuitAnimatorStage(Transform multiplierRoot, int multiplier)
        {
            if (multiplierRoot == null) return;
            var animator = multiplierRoot.GetComponentInChildren<Animator>(true);
            if (animator == null) return;
            animator.enabled = true;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime; // PauseManager timeScale=0 대응
            // 중복 진입 가드 — 이미 동일 state 재생 중이면 0f 로 되감지 않음 (Loop 끊김 방지).
            int targetHash = Animator.StringToHash($"Multiplier{multiplier}");
            if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == targetHash) return;
            animator.Play($"Multiplier{multiplier}", 0, 0f);
            animator.Update(0f);
        }

        /// <summary>[2026-06-15] PopupQuit.CloseUI 호출 직전 — base.CloseUI 가 SetActive(false) 하기 전에 animator state 초기화.
        /// 동일 instance 가 재오픈될 때 Multiplier{N} 잔존 방지. Loop 클립이라 자동 복귀가 없으므로 명시적으로 리셋.</summary>
        /// <remarks>PopupQuit과 PopupContinue 양쪽에서 호출됨 (2026-06-15 PopupContinue 멀티플라이어 통일). 메서드명에 'PopupQuit' 이 남아있는 것은 호환성 유지 위함.</remarks>
        public static void ResetPopupQuitMultiplierAnimation(GameObject winningStreakView)
        {
            if (winningStreakView == null) return;
            var multiplierRoot = FindChild(winningStreakView.transform, "Multiplier");
            if (multiplierRoot == null) return;
            if (multiplierRoot is RectTransform rt) rt.DOKill(); // 진행 중 슬라이드인 tween 정리.
            var animator = multiplierRoot.GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.enabled) return;
            animator.Play("MultiplierDefault", 0, 0f);
            animator.Update(0f);
        }

        /// <summary>[미사용 2026-06-10 — Animator 방식 롤백용 보존] Multiplier 루트의 Animator 로 현재 배수 상태를 재생.
        /// 전 호출부가 PlayMultiplierSelect(코드 트윈) 로 전환됨. Animator 가 SelectFrame/TextYellow 위치를 덮어쓰는 문제로 폐기.
        /// 배수 5/10/25/100 → Multiplier{N} 상태 재생. 배수 1 은 Animator 기본 상태 리셋.</summary>
        public static void PlayMultiplierState(Transform multiplierRoot, int multiplier)
        {
            if (multiplierRoot == null) return;
            ApplyPositions(multiplierRoot, multiplier, HudSelectFrameX, HudTextYellowX, MULTIPLIER_SELECT_MOVE_DURATION);
            var animator = multiplierRoot.GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isActiveAndEnabled) return;
            if (multiplier > 1)
                animator.Play($"Multiplier{multiplier}", 0, 0f);
            else
                animator.Rebind();   // 배수 1 → 기본(x1/idle) 상태로 복귀
        }

        // ─── helpers ─────────────────────────────────────────────

        private static int IndexForMultiplier(int multiplier)
        {
            for (int i = 0; i < Tiers.Length; i++)
                if (Tiers[i] == multiplier) return i;
            // 알 수 없는 배수 — 가장 가까운 하위 tier 로 fallback.
            int idx = 0;
            for (int i = 0; i < Tiers.Length; i++)
                if (multiplier >= Tiers[i]) idx = i;
            return idx;
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (root == null) return null;
            var arr = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < arr.Length; i++)
                if (arr[i].name == name) return arr[i];
            return null;
        }

        private static RectTransform FindChildRect(Transform root, string name)
        {
            var t = FindChild(root, name);
            return t != null ? t as RectTransform : null;
        }
    }
}
