namespace BalloonFlow
{
    /// <summary>
    /// 로컬/서버 푸시 알림 텍스트 상수. 1.0 EN-only (LiveOps §9 L208).
    /// 1.1+ 다국어 시 LocalizationManager 도입 후 키 매핑으로 이전.
    /// 원본: BalloonFlow_아웃게임디렉션.md §9
    /// </summary>
    public static class PushTexts
    {
        // #1 하트 풀충전 — 로컬 알림 (Phase 1)
        public const string HEART_FULL_TITLE = "Hearts fully recharged!";
        public const string HEART_FULL_BODY  = "💖 Lives are full! Time to pop some balloons!";

        // #2 이탈 복귀 D1~D7 — 서버 푸시 (Phase 2). 클라 참조용으로 보관.

        public const string RETURN_D1 = "🎈 Your balloons are waiting! Come back and let's pop a few!";
        public const string RETURN_D2 = "🎈 Got 3 minutes? That's all it takes to pop and unwind.";
        public const string RETURN_D3 = "🎈 Your balloons are all lined up! Pop them and hear the pitch climb!";
        public const string RETURN_D4 = "🎈 Busy day? Pop a balloon or two and let your mind float.";
        public const string RETURN_D5 = "🎈 Pop! Pop! Each one rings higher! Don't you miss that feeling?";
        public const string RETURN_D6 = "🎈 You've earned a break! Treat yourself to a few pops.";
        public const string RETURN_D7 = "🎈 Remember how good that balloon pop felt? Let's do it again!";

        // #3 데일리 보상 미수령 — 서버 푸시 (Phase 2)
        public const string DAILY_REWARD = "⏰ Don't miss today's reward! Tap to collect before it's gone.";

        // #4 Winning Streak 이벤트 푸시 — 서버 푸시 (Phase 2). 클라 참조용으로 보관.
        // string.Format({0} ← WINNING_STREAK_EVENT_NAME) 으로 사용.
        public const string WINNING_STREAK_EVENT_NAME       = "Winning Streak";
        public const string WINNING_STREAK_EVENT_START_FMT  = "🎉 {0} is live! Jump in and earn exclusive rewards.";
        public const string WINNING_STREAK_EVENT_END_1H_FMT = "🏆 {0} ends in 1 hour! Grab your rewards before time runs out.";
    }
}
