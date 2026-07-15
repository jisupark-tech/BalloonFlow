using Firebase.Firestore;

namespace BalloonFlow
{
    /// <summary>
    /// ROLLBACK_FORCE_UPDATE_20260715: Firestore /config/app 단일 doc — 앱 전역 config.
    ///   minSupportedVersion: 이 값보다 앱 버전이 낮으면 강제 업데이트(로딩 전 차단). 비었거나 미존재 시 검사 스킵(통과).
    ///   (WinningStreakConfigDoc / config/winningStreak 패턴 동일.)
    /// </summary>
    [FirestoreData]
    public class AppConfigDoc
    {
        [FirestoreProperty] public string minSupportedVersion { get; set; } = "";
    }
}
