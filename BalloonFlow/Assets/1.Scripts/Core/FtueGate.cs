namespace BalloonFlow
{
    /// <summary>
    /// FTUE / 진행 게이트 중앙 상수 + 판정 (UX플로우 §3-3·§4-2·§5-4, WinningStreak 명세 §2-2).
    /// 백버튼 매트릭스(#15)·FTUE 라우팅(#5/12)·전면광고(#4)·WinningStreak(#6/7)이 공유.
    ///
    /// 모든 게이트 판정은 <b>최고 클리어 레벨</b>(PlayerPrefs BF_HighestLevel 기준 — LevelManager.SaveLevelProgress가 갱신).
    /// 진행 중인 레벨(현재 진입 레벨)이 아닌 "클리어 완료" 기준이라는 점에 주의.
    /// </summary>
    public static class FtueGate
    {
        /// <summary>Lv.5 클리어 = 온보딩 종료 (v1.2.35, PO 2026-05-26). 이전까지 로비 스킵·클리어팝업 생략·Quit 비노출.</summary>
        public const int ONBOARDING_CLEAR_LEVEL = 5;

        /// <summary>Lv.20 클리어 후 전면 광고 + 광고 제거 상품 해금.</summary>
        public const int AD_UNLOCK_CLEAR_LEVEL = 20;

        /// <summary>Lv.35 클리어 후 WinningStreak 해금. Lv.36 클리어부터 Next → 로비.</summary>
        // ROLLBACK_WS_START_LEVEL_36_20260618:
        // Previous value was 35, which made Winning Streak start one level early.
        // Lv.35 clear means the lobby now reaches Lv.36, where the event starts.
        public const int WINNING_STREAK_UNLOCK_CLEAR_LEVEL = 36;

        /// <summary>현재 유저의 최고 클리어 레벨. 단일 진실 소스 = PlayerPrefs(LevelManager.PREFS_KEY_HIGHEST_LEVEL). LevelManager 인스턴스 유무와 무관하게 동작 — Title 씬(LevelManager 미배치)에서도 정확한 값 반환.</summary>
        public static int HighestClearedLevel
            => UnityEngine.PlayerPrefs.GetInt(LevelManager.PREFS_KEY_HIGHEST_LEVEL, 0);

        /// <summary>온보딩(Lv.1~5) 완료 여부. Lv.5 클리어 시 true.</summary>
        public static bool IsOnboardingComplete
            => HighestClearedLevel >= ONBOARDING_CLEAR_LEVEL;

        /// <summary>전면 광고 해금 여부 (Lv.20 클리어 후).</summary>
        public static bool IsInterstitialUnlocked
            => HighestClearedLevel >= AD_UNLOCK_CLEAR_LEVEL;

        /// <summary>WinningStreak 해금 여부 (Lv.35 클리어 후). 단, 활성 회차 여부는 서버 config 기준 별도 판정.</summary>
        public static bool IsWinningStreakUnlocked
            => HighestClearedLevel + 1 >= WINNING_STREAK_UNLOCK_CLEAR_LEVEL;
    }
}
