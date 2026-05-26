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
        public const string HEART_FULL_BODY  = "Your hearts are full. Time to pop!";

        // #2 이탈 복귀 D1~D7 — 서버 푸시 (Phase 2). 클라 참조용으로 보관.
        public const string RETURN_D1 = "Take a break? Come pop some balloons!";
        public const string RETURN_D2 = "🎈 Pop the day off! Hearts are ready, friends await.";
        public const string RETURN_D3 = "🎈 Stress? Pop. Pop. Pop. Three taps to your daily smile.";
        public const string RETURN_D4 = "🎈 Boredom won't pop itself — your balloons are waiting!";
        public const string RETURN_D5 = "🎈 Pop! Pop! Don't you miss that sound? Come back and feel it again.";
        public const string RETURN_D6 = "🎈 So many balloons left to pop! They won't pop themselves.";
        public const string RETURN_D7 = "🎈 Remember the joy of popping balloons? It's time for one more round!";

        // #3 데일리 보상 미수령 — 서버 푸시 (Phase 2)
        public const string DAILY_REWARD = "⏰ Don't miss today's reward! Tap to collect before it's gone.";
    }
}
