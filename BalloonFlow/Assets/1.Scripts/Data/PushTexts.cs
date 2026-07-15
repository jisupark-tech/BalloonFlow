namespace BalloonFlow
{
    /// <summary>
    /// 로컬/서버 푸시 알림 텍스트 상수. 1.0 EN-only (LiveOps §9 L208).
    /// 1.1+ 다국어 시 LocalizationManager 도입 후 키 매핑으로 이전.
    /// 원본: BalloonFlow_아웃게임디렉션.md §9
    /// </summary>
    public static class PushTexts
    {
        // #1 하트 풀충전 — 로컬 알림 (Phase 1). ROLLBACK_PUSH_KO_20260715: 한국어(device 언어 KO)만 KO, 나머지 EN.
        public const string HEART_FULL_TITLE = "Hearts fully recharged!";
        public const string HEART_FULL_BODY  = "💖 Lives are full! Time to pop some balloons!";
        public const string HEART_FULL_TITLE_KO = "하트 충전 완료!";
        public const string HEART_FULL_BODY_KO  = "💖 하트가 가득 찼어요! 지금 풍선을 터뜨리러 가요!";

        // 로컬 알림(NotificationManager)용 언어 인지 게터 — device 언어 KO 면 KO, 그 외 EN.
        private static bool IsKo => LocalizationService.CurrentLanguageCode == "KO";
        public static string HeartFullTitle => IsKo ? HEART_FULL_TITLE_KO : HEART_FULL_TITLE;
        public static string HeartFullBody  => IsKo ? HEART_FULL_BODY_KO  : HEART_FULL_BODY;

        // #2 이탈 복귀 D1~D7 — 서버 푸시 (Phase 2). 실제 발송은 서버(index.js RETURN_PUSH_BODY/_KO). 여기는 참조/동기용.
        public const string RETURN_D1 = "🎈 Your balloons are waiting! Come back and let's pop a few!";
        public const string RETURN_D2 = "🎈 Got 3 minutes? That's all it takes to pop and unwind.";
        public const string RETURN_D3 = "🎈 Your balloons are all lined up! Pop them and hear the pitch climb!";
        public const string RETURN_D4 = "🎈 Busy day? Pop a balloon or two and let your mind float.";
        public const string RETURN_D5 = "🎈 Pop! Pop! Each one rings higher! Don't you miss that feeling?";
        public const string RETURN_D6 = "🎈 You've earned a break! Treat yourself to a few pops.";
        public const string RETURN_D7 = "🎈 Remember how good that balloon pop felt? Let's do it again!";
        // ROLLBACK_PUSH_KO_20260715: KO 참조(서버 index.js RETURN_PUSH_BODY_KO 와 동기 유지).
        public const string RETURN_D1_KO = "🎈 풍선들이 기다리고 있어요! 돌아와서 몇 개만 터뜨려요!";
        public const string RETURN_D2_KO = "🎈 3분이면 충분해요. 풍선 터뜨리며 잠깐 쉬어 가요.";
        public const string RETURN_D3_KO = "🎈 풍선이 줄지어 기다려요! 터뜨릴수록 높아지는 소리를 들어보세요!";
        public const string RETURN_D4_KO = "🎈 바쁜 하루였나요? 풍선 한두 개 터뜨리며 머리를 식혀요.";
        public const string RETURN_D5_KO = "🎈 팡! 팡! 점점 높아지는 그 소리, 그립지 않으세요?";
        public const string RETURN_D6_KO = "🎈 휴식이 필요한 순간이에요! 풍선 몇 개로 기분 전환해요.";
        public const string RETURN_D7_KO = "🎈 풍선 터뜨리던 그 손맛, 기억나죠? 다시 한 판 해요!";

        // #3 데일리 보상 미수령 — 서버 푸시 (Phase 2)
        // ROLLBACK_DAILY_PUSH_DISABLED_20260618: 1.0 에는 데일리 리워드 기능이 없음(GameManager 부트스트랩 제거,
        //   "[1.0 비포함] DailyReward 는 1.0 명세/문서에 없고 UI 진입점도 없음"). 보상이 없는데 '오늘의 보상 받으세요'
        //   푸시가 나가면 안 되므로 데일리 리워드 푸시 텍스트를 비활성(주석)한다. 데일리 리워드 도입 시 주석 해제.
        //   (클라 참조 0 = unused 라 주석 처리해도 컴파일 영향 없음.)
        // public const string DAILY_REWARD = "⏰ Don't miss today's reward! Tap to collect before it's gone.";

        // #4 Winning Streak 이벤트 푸시 — 서버 푸시 (Phase 2). 클라 참조용으로 보관.
        // string.Format({0} ← WINNING_STREAK_EVENT_NAME) 으로 사용.
        public const string WINNING_STREAK_EVENT_NAME       = "Winning Streak";
        public const string WINNING_STREAK_EVENT_START_FMT  = "🎉 {0} is live! Jump in and earn exclusive rewards.";
        public const string WINNING_STREAK_EVENT_END_1H_FMT = "🏆 {0} ends in 1 hour! Grab your rewards before time runs out.";
    }
}
