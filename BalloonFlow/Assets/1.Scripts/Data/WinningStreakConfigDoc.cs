using System.Collections.Generic;
using Firebase.Firestore;

namespace BalloonFlow
{
    /// <summary>
    /// Firestore /config/winningStreak 단일 doc. 앱 시작 시 1회 fetch + 메모리 캐시.
    /// 25 stage 보상 테이블 + streak/난이도 배수 + 부스터 가격을 한 doc 에 묶음.
    /// 콘솔에서 조정해도 다음 fetch 부터 자동 반영.
    /// </summary>
    [FirestoreData]
    public class WinningStreakConfigDoc
    {
        /// <summary>WinningStreak 이벤트 해금 레벨 (highestClearedLevel 이 이 값 이상이면 노출). 명세 §2.2 = 35.</summary>
        [FirestoreProperty] public int unlockLevel { get; set; } = 35;

        /// <summary>현재 활성 회차(round) ID (예: "2026-W30-A"). 서버가 회차마다 갱신.
        /// 클라 State.activeRoundId 와 다르면 새 회차 → 상태 리셋 (명세 §2.3·§8.2). 빈 값이면 회차 미운영 → 리셋 안 함.</summary>
        [FirestoreProperty] public string activeRoundId { get; set; } = "";

        [FirestoreProperty] public StreakMultipliers streakMultipliers { get; set; } = new StreakMultipliers();
        [FirestoreProperty] public DifficultyMultipliers difficultyMultipliers { get; set; } = new DifficultyMultipliers();
        [FirestoreProperty] public BoosterCosts boosterCosts { get; set; } = new BoosterCosts();

        /// <summary>stage 1..N 순서 보상 테이블. 길이는 디자인 따라 가변 가능 (현재 25).</summary>
        [FirestoreProperty] public List<WinningStreakStage> stages { get; set; } = new List<WinningStreakStage>();

        // 엄격 서버 기준: 클라 기본 config(CreateDefault) 제거 — 서버(config/winningStreak) 만이 단일 진실.
        // 시드 원본은 firebase/seed/winningStreak/config.json, 업로드는 WinningStreakConfigUploader(Editor).
    }

    [FirestoreData]
    public class WinningStreakStage
    {
        /// <summary>1-base stage 번호.</summary>
        [FirestoreProperty] public int stage { get; set; }
        /// <summary>이 stage 를 통과하는 데 필요한 누적 포인트.</summary>
        [FirestoreProperty] public int requiredPoints { get; set; }
        /// <summary>이 stage 통과 시 지급되는 기본 보상.</summary>
        [FirestoreProperty] public ShopRewards rewards { get; set; } = new ShopRewards();
        /// <summary>"콜렉션 X" 조건 만족 시 추가 보상 (현재 미사용 — 비워둠).</summary>
        [FirestoreProperty] public ShopRewards collectionXRewards { get; set; } = new ShopRewards();
    }

    [FirestoreData]
    public class StreakMultipliers
    {
        [FirestoreProperty] public int streak1 { get; set; } = 1;
        [FirestoreProperty] public int streak2 { get; set; } = 5;
        [FirestoreProperty] public int streak3 { get; set; } = 10;
        [FirestoreProperty] public int streak4 { get; set; } = 25;
        /// <summary>5연승 이상은 모두 동일 배수.</summary>
        [FirestoreProperty] public int streak5Plus { get; set; } = 100;
    }

    [FirestoreData]
    public class DifficultyMultipliers
    {
        [FirestoreProperty] public int normal { get; set; } = 1;
        [FirestoreProperty] public int hard { get; set; } = 3;
        [FirestoreProperty] public int superHard { get; set; } = 5;
    }

    [FirestoreData]
    public class BoosterCosts
    {
        /// <summary>되돌리기 1개당 코인.</summary>
        [FirestoreProperty] public int undo { get; set; } = 300;
        /// <summary>셔플 1개당 코인.</summary>
        [FirestoreProperty] public int shuffle { get; set; } = 300;
        /// <summary>슬롯확장 3칸 묶음당 코인 (단가 아님).</summary>
        [FirestoreProperty] public int slotExpand3 { get; set; } = 900;
        /// <summary>자석(자동매치) 1개당 코인.</summary>
        [FirestoreProperty] public int magnet { get; set; } = 600;
    }
}
