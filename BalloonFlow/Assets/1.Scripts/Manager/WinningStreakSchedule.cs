using System;

namespace BalloonFlow
{
    /// <summary>
    /// Key Blaze(WinningStreak) 회차 스케줄 — 클라 UTC 기준 반복형 (명세: 월 00:00~목 24:00 / 금 00:00~일 24:00).
    /// 경계: 매주 월 00:00 UTC, 금 00:00 UTC.
    ///  - Round A: 월 00:00 ~ 금 00:00 (월~목)
    ///  - Round B: 금 00:00 ~ 다음 월 00:00 (금~일)
    /// 두 윈도가 한 주를 빈틈없이 덮으므로 이벤트는 상시 진행, 경계에서만 회차 리셋.
    /// ⚠ 클라 시계 기준 — 기기 시간 조작 시 회차 조작 가능(서버 시간 동기화 인프라 없음).
    /// </summary>
    public static class WinningStreakSchedule
    {
        /// <summary>현재 UTC 의 회차 ID. 경계를 넘으면 값이 바뀜 → 리셋 트리거.</summary>
        public static string GetCurrentRoundId() => GetCurrentRoundId(DateTime.UtcNow);

        /// <summary>현재 회차 종료 UTC 시각(다음 경계).</summary>
        public static DateTime GetCurrentRoundEndUtc() => GetCurrentRoundEndUtc(DateTime.UtcNow);

        /// <summary>현재 회차 종료까지 남은 시간(0 미만이면 0).</summary>
        public static TimeSpan GetRemaining()
        {
            TimeSpan r = GetCurrentRoundEndUtc() - DateTime.UtcNow;
            return r > TimeSpan.Zero ? r : TimeSpan.Zero;
        }

        public static string GetCurrentRoundId(DateTime utcNow)
        {
            GetWindow(utcNow, out DateTime start, out _, out char half);
            return $"{start:yyyyMMdd}-{half}";
        }

        public static DateTime GetCurrentRoundEndUtc(DateTime utcNow)
        {
            GetWindow(utcNow, out _, out DateTime end, out _);
            return end;
        }

        /// <summary>utcNow 가 속한 윈도의 시작·끝(UTC)과 half('A'/'B') 산출.</summary>
        private static void GetWindow(DateTime utcNow, out DateTime start, out DateTime end, out char half)
        {
            // 자정(00:00) 기준 날짜. DateTime.Date 는 Kind 유지.
            DateTime today = utcNow.Date;
            // 이번 주 월요일 00:00 (DayOfWeek: Sun=0..Sat=6 → 월 기준 오프셋)
            int daysSinceMon = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            DateTime monday     = today.AddDays(-daysSinceMon);
            DateTime friday     = monday.AddDays(4);
            DateTime nextMonday = monday.AddDays(7);

            if (utcNow < friday) { start = monday; end = friday;     half = 'A'; } // 월~목
            else                 { start = friday; end = nextMonday; half = 'B'; } // 금~일
        }
    }
}
