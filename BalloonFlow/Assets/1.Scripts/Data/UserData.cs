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

        // ── Boosters ──────────────────────────────────────────────
        [FirestoreProperty] public BoosterInventory boosters { get; set; } = new BoosterInventory();

        // ── Continue Session ──────────────────────────────────────
        [FirestoreProperty] public ContinueState continueState { get; set; } = new ContinueState();

        // ── Shop / Ads ────────────────────────────────────────────
        [FirestoreProperty] public bool removedAds { get; set; } = false;
        /// <summary>1회 한정 상품 구매 이력. key = productId.</summary>
        [FirestoreProperty] public Dictionary<string, bool> purchasedOnce { get; set; } = new Dictionary<string, bool>();
        /// <summary>NPU = Non-Paying User. 첫 결제 후 false. Best Value Pack 노출 조건.</summary>
        [FirestoreProperty] public bool isNPU { get; set; } = true;
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
                boosters = new BoosterInventory(),
                continueState = new ContinueState(),
                removedAds = false,
                purchasedOnce = new Dictionary<string, bool>(),
                isNPU = true,
                dailyReward = new DailyRewardState(),
                settings = new SettingsData(),
                consents = new ConsentsData(),
                attribution = new AttributionData()
            };
        }
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
