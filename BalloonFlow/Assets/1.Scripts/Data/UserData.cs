using System.Collections.Generic;
using Firebase.Firestore;

namespace BalloonFlow
{
    /// <summary>
    /// Firestore /users/{uid} document. 아웃게임디렉션 기반 1.0 소프트런칭 모델.
    /// 신규 유저: CreateNewUser() 사용. PlayerPrefs 로컬 캐시와 양방향 동기화.
    /// </summary>
    [FirestoreData]
    public class UserData
    {
        public const int SCHEMA_VERSION = 1;
        public const int DEFAULT_INITIAL_COINS = 1000;
        public const int DEFAULT_MAX_LIVES = 5;

        // ── Identity ──────────────────────────────────────────────
        [FirestoreProperty] public string uid { get; set; } = "";
        [FirestoreProperty] public Timestamp createdAt { get; set; }
        [FirestoreProperty] public Timestamp lastLoginAt { get; set; }
        [FirestoreProperty] public int schemaVersion { get; set; } = SCHEMA_VERSION;

        // ── Currency / Lives ──────────────────────────────────────
        [FirestoreProperty] public int coins { get; set; }
        [FirestoreProperty] public int lives { get; set; }
        [FirestoreProperty] public int maxLives { get; set; } = DEFAULT_MAX_LIVES;
        /// <summary>다음 1개 충전 시각 (UTC). default(Seconds=0) = unset → 즉시 +1 처리.
        /// Firestore Unity SDK가 nullable struct 미지원으로 sentinel 사용.</summary>
        [FirestoreProperty] public Timestamp nextLifeAt { get; set; }
        /// <summary>무한 하트 종료 시각 (UTC). default = 비활성.</summary>
        [FirestoreProperty] public Timestamp infiniteHeartsUntil { get; set; }

        // ── Progress ──────────────────────────────────────────────
        [FirestoreProperty] public int highestClearedLevel { get; set; } = 0;
        [FirestoreProperty] public bool allClearedFlag { get; set; } = false;

        // ── Profile ───────────────────────────────────────────────
        /// <summary>프로필 아이콘 슬롯 index (0 base). 기본 0 = 첫 슬롯.</summary>
        [FirestoreProperty] public int profileIconNumber { get; set; } = 0;
        /// <summary>프로필 프레임 슬롯 index (0 base). 기본 0 = 첫 슬롯.</summary>
        [FirestoreProperty] public int profileFrameNumber { get; set; } = 0;

        // ── Boosters ──────────────────────────────────────────────
        [FirestoreProperty] public BoosterInventory boosters { get; set; } = new BoosterInventory();

        // ── Continue Session ──────────────────────────────────────
        [FirestoreProperty] public ContinueState continueState { get; set; } = new ContinueState();

        // ── Shop / Ads ────────────────────────────────────────────
        [FirestoreProperty] public bool removedAds { get; set; } = false;
        /// <summary>광고 제거 구매 시각 (UTC). default(Seconds=0) = 미구매. 환불/CS/분석 용도.</summary>
        [FirestoreProperty] public Timestamp removedAdsPurchasedAt { get; set; }
        /// <summary>광고 제거를 부여한 productId. 여러 상품(noads 단품/bundle 포함) 중 어떤 경로로 구매했는지 추적.</summary>
        [FirestoreProperty] public string removedAdsProductId { get; set; } = "";
        /// <summary>1회 한정 상품 구매 이력. key = productId.</summary>
        [FirestoreProperty] public Dictionary<string, bool> purchasedOnce { get; set; } = new Dictionary<string, bool>();
        /// <summary>NPU = Non-Paying User. 첫 결제 후 false. Best Value Pack 노출 조건.</summary>
        [FirestoreProperty] public bool isNPU { get; set; } = true;
        /// <summary>Lv.15 Zap unlock 후 실제 1회 사용 완료. Store Stage 2 노출 조건.</summary>
        [FirestoreProperty] public bool zapTutorialCompleted { get; set; } = false;
        /// <summary>첫 전면 광고 노출 경험. Store Stage 3 노출 조건.</summary>
        [FirestoreProperty] public bool firstInterstitialShown { get; set; } = false;
        /// <summary>스페셜오퍼 마지막 노출 시각 (20분 쿨타임용). default = 미노출.</summary>
        [FirestoreProperty] public Timestamp lastSpecialOfferAt { get; set; }

        // ── Daily Reward ──────────────────────────────────────────
        [FirestoreProperty] public DailyRewardState dailyReward { get; set; } = new DailyRewardState();

        // ── Settings (cross-device 동기화용. SettingsManager 와 양방향) ──
        [FirestoreProperty] public SettingsData settings { get; set; } = new SettingsData();

        // ── Consents (GDPR / IDFA / CCPA) ─────────────────────────
        [FirestoreProperty] public ConsentsData consents { get; set; } = new ConsentsData();

        // ── Attribution (AppsFlyer conversion data 캐시) ──────────
        [FirestoreProperty] public AttributionData attribution { get; set; } = new AttributionData();

        // ── Winning Streak Event ──────────────────────────────────
        [FirestoreProperty] public WinningStreakState winningStreak { get; set; } = new WinningStreakState();

        // ── Push Notification (Phase 2) ───────────────────────────
        /// <summary>FCM 등록 토큰. 빈 문자열 = 미등록 / 사용자 거부.</summary>
        [FirestoreProperty] public string fcmToken { get; set; } = "";
        /// <summary>D1~D7 이탈 복귀 푸시 최근 발송 일자 ("YYYY-MM-DD" UTC). 같은 날 중복 발송 방지.</summary>
        [FirestoreProperty] public string lastReturnPushSent { get; set; } = "";
        /// <summary>데일리 보상 미수령 푸시 최근 발송 일자 ("YYYY-MM-DD" UTC). 같은 날 중복 발송 방지.</summary>
        [FirestoreProperty] public string lastDailyPushSent { get; set; } = "";

        // ── Factory ───────────────────────────────────────────────
        /// <summary>신규 유저 초기값. 1,000코인 + 5하트 + NPU.</summary>
        public static UserData CreateNewUser(string uid)
        {
            var now = Timestamp.GetCurrentTimestamp();
            return new UserData
            {
                uid = uid,
                createdAt = now,
                lastLoginAt = now,
                schemaVersion = SCHEMA_VERSION,
                coins = DEFAULT_INITIAL_COINS,
                lives = DEFAULT_MAX_LIVES,
                maxLives = DEFAULT_MAX_LIVES,
                // default Timestamp(Seconds=0) = unset
                highestClearedLevel = 0,
                allClearedFlag = false,
                profileIconNumber = 0,
                profileFrameNumber = 0,
                boosters = new BoosterInventory(),
                continueState = new ContinueState(),
                removedAds = false,
                purchasedOnce = new Dictionary<string, bool>(),
                isNPU = true,
                zapTutorialCompleted = false,
                firstInterstitialShown = false,
                dailyReward = new DailyRewardState(),
                settings = new SettingsData(),
                consents = new ConsentsData(),
                attribution = new AttributionData(),
                winningStreak = new WinningStreakState(),
                fcmToken = "",
                lastReturnPushSent = "",
                lastDailyPushSent = ""
            };
        }
    }

    [FirestoreData]
    public class WinningStreakState
    {
        /// <summary>현재 진행 중인 stage (1-base, stage[N-1] 이 활성). eventFinished 이면 사용 안 함.</summary>
        [FirestoreProperty] public int currentStage { get; set; } = 1;
        /// <summary>currentStage 내에서 쌓인 누적 포인트. requiredPoints 도달 시 overflow → 다음 stage 로 carry.</summary>
        [FirestoreProperty] public int currentStagePoints { get; set; } = 0;
        /// <summary>현재 연승 수. 0 = 시작 또는 직전 실패. 클리어 시 +1. 실패 시 0 으로 리셋.</summary>
        [FirestoreProperty] public int currentStreak { get; set; } = 0;
        /// <summary>이벤트 전체에서 누적된 총 포인트 (통계용).</summary>
        [FirestoreProperty] public long lifetimePoints { get; set; } = 0;
        /// <summary>이미 수령 완료한 stage 번호 (1-base). 중복 수령 방지 + UI 체크 표시.</summary>
        [FirestoreProperty] public List<int> claimedStages { get; set; } = new List<int>();
        /// <summary>마지막 stage 까지 통과 완료 — 더 이상 포인트 누적 없음.</summary>
        [FirestoreProperty] public bool eventFinished { get; set; } = false;
        /// <summary>현재 진행 중인 회차(round) ID. 서버 config.activeRoundId 와 다르면 새 회차 → 상태 리셋(명세 §2.3·§11.1).
        /// 빈 문자열 = 아직 회차 배정 전. lifetimePoints 만 회차 무관 누적 유지.</summary>
        [FirestoreProperty] public string activeRoundId { get; set; } = "";
    }

    [FirestoreData]
    public class BoosterInventory
    {
        // 문서 (balloonflow_IAP.xlsx) 표기와 통일: Hand / Shuffle / Zap
        [FirestoreProperty] public int hand { get; set; } = 0;
        [FirestoreProperty] public int shuffle { get; set; } = 0;
        [FirestoreProperty] public int zap { get; set; } = 0;
    }

    [FirestoreData]
    public class ContinueState
    {
        /// <summary>현재 플레이 회차에서 이어하기 사용 횟수 (0~3). 다음 비용: 0→900, 1→1900, 2→2900.</summary>
        [FirestoreProperty] public int attemptCount { get; set; } = 0;
        /// <summary>이어하기 세션이 적용된 레벨 ID. 다른 레벨 진입 시 0으로 리셋.</summary>
        [FirestoreProperty] public int sessionLevelId { get; set; } = 0;
    }

    [FirestoreData]
    public class DailyRewardState
    {
        /// <summary>현재 streak (0~7). 7일째 보상 수령 후 다음 회차는 1로 시작.</summary>
        [FirestoreProperty] public int streak { get; set; } = 0;
        /// <summary>마지막 수령 일자 ("YYYY-MM-DD"). 디바이스 로컬 timezone 기준이지만 보안용으로 서버 timestamp 동시 저장 권장.</summary>
        [FirestoreProperty] public string lastClaimDate { get; set; } = "";
    }

    [FirestoreData]
    public class SettingsData
    {
        [FirestoreProperty] public bool soundOn { get; set; } = true;
        [FirestoreProperty] public bool musicOn { get; set; } = true;
        [FirestoreProperty] public bool hapticOn { get; set; } = true;
        [FirestoreProperty] public bool notificationOn { get; set; } = true;
        [FirestoreProperty] public float hapticIntensity { get; set; } = 1f;
        [FirestoreProperty] public float hapticDuration { get; set; } = 1f;
    }

    [FirestoreData]
    public class ConsentsData
    {
        [FirestoreProperty] public bool gdpr { get; set; } = false;
        [FirestoreProperty] public bool idfa { get; set; } = false;
        [FirestoreProperty] public bool ccpa { get; set; } = false;
        [FirestoreProperty] public bool ageGate { get; set; } = false;
        [FirestoreProperty] public Timestamp agreedAt { get; set; }
    }

    [FirestoreData]
    public class AttributionData
    {
        [FirestoreProperty] public string source { get; set; } = "";
        [FirestoreProperty] public string campaign { get; set; } = "";
        [FirestoreProperty] public string mediaSource { get; set; } = "";
        [FirestoreProperty] public bool isOrganic { get; set; } = false;
        [FirestoreProperty] public Timestamp firstSeenAt { get; set; }
    }
}
