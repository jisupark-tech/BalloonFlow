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

        private static readonly int[] Tiers = { 1, 5, 10, 25, 100 };

        // HUD (UIHud 의 Multiplier) — SelectFrame / TextYellow X 위치 (Mask 고정).
        private static readonly float[] HudSelectFrameX = { -338f, -150f,  30f,  210f,  390f };
        private static readonly float[] HudTextYellowX  = {  358f,  170f, -10f, -190f, -370f };

        // PopupWinningStreak — 위치만, 애니메이션 없음.
        private static readonly float[] PopupSelectFrameX = { -360f, -184f, -10f, 174f, 350f };
        private static readonly float[] PopupTextYellowX  = {  360f,  184f,  10f, -174f, -350f };

        /// <summary>현재 streak 의 배수 (1/5/10/25/100). State/Config 미준비 시 1 반환.</summary>
        public static int ResolveCurrentMultiplier()
        {
            if (!WinningStreakManager.HasInstance) return 1;
            var mgr = WinningStreakManager.Instance;
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
            if (multiplierRoot == null) return;
            int idx = IndexForMultiplier(multiplier);
            var selectFrame = FindChildRect(multiplierRoot, "SelectFrame");
            var textYellow  = FindChildRect(multiplierRoot, "TextYellow");
            if (selectFrame != null)
                selectFrame.anchoredPosition = new Vector2(selectFrameX[idx], selectFrame.anchoredPosition.y);
            if (textYellow != null)
                textYellow.anchoredPosition = new Vector2(textYellowX[idx], textYellow.anchoredPosition.y);
        }

        /// <summary>Popup (Quit/Continue) WinningStreak view 안 Multiplier 등장 연출 + 위치 + Animator state.
        /// 절차: (1) x=-724 → 0 슬라이드 (2) SelectFrame/TextYellow 위치 배수별 매핑 (3) Multiplier5/10/25/100 Animator state 재생.
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
                    .SetUpdate(true); // popup 이라 timescale 무관.
            }

            // (2) SelectFrame / TextYellow 배수별 위치 (Popup Quit/Continue 매핑 사용).
            ApplyHudPositionsForMultiplier(multiplierRoot, multiplier);

            // (3) Animator state — Multiplier5/10/25/100.
            var animator = multiplierRoot.GetComponentInChildren<Animator>(true);
            if (animator == null) return;
            string stateName = $"Multiplier{multiplier}";
            animator.Play(stateName, 0, 0f);
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
