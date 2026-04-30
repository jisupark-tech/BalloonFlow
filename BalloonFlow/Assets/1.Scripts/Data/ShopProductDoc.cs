using Firebase.Firestore;

namespace BalloonFlow
{
    /// <summary>
    /// Firestore /products/{productId} 문서 모델. 콘솔에서 가격/구성 라이브 조정용.
    /// 클라이언트의 ShopProductData 와는 별도 — 서버 fetch 후 변환.
    /// 카테고리(1.0): coin | bundle | noads | offer  (xlsx 시트 기준)
    /// </summary>
    [FirestoreData]
    public class ShopProductDoc
    {
        [FirestoreProperty] public string productId { get; set; } = "";
        [FirestoreProperty] public string title_loc_key { get; set; } = "";
        [FirestoreProperty] public string description_loc_key { get; set; } = "";
        [FirestoreProperty] public string category { get; set; } = ""; // coin|bundle|noads|offer

        // ── Pricing ──────────────────────────────────────────────
        [FirestoreProperty] public double priceUsd { get; set; } = 0d;
        [FirestoreProperty] public string currency { get; set; } = "USD";
        [FirestoreProperty] public string playStoreSku { get; set; } = "";
        [FirestoreProperty] public string appStoreSku { get; set; } = "";

        // ── Visuals (Addressable atlas sprite name) ──────────────
        /// <summary>UI atlas (Const.ADDR_ATLAS_UI) 안의 sprite 이름. 빈 문자열이면 카테고리별 fallback.</summary>
        [FirestoreProperty] public string imageKey { get; set; } = "";

        // ── Rewards ──────────────────────────────────────────────
        [FirestoreProperty] public ShopRewards rewards { get; set; } = new ShopRewards();

        // ── Visibility / limits ──────────────────────────────────
        [FirestoreProperty] public int unlockLevel { get; set; } = 1;
        /// <summary>1 = 1회 한정 (NPU Best Value Pack 등). 0 또는 null = 무제한.</summary>
        [FirestoreProperty] public int maxPurchases { get; set; } = 0;
        /// <summary>광고 보상 쿨타임 등. 0 = 없음.</summary>
        [FirestoreProperty] public int cooldownSeconds { get; set; } = 0;
        [FirestoreProperty] public bool visibleInShop { get; set; } = true;
        [FirestoreProperty] public int sortOrder { get; set; } = 0;
        [FirestoreProperty] public bool hasTimeLimit { get; set; } = false;
        [FirestoreProperty] public int timeLimitSeconds { get; set; } = 0;

        // ── Design metadata (디자이너 추적용. 클라엔 안 쓰임) ──
        [FirestoreProperty] public double totalValueUsd { get; set; } = 0d;
        [FirestoreProperty] public int discountPercent { get; set; } = 0;
    }

    [FirestoreData]
    public class ShopRewards
    {
        [FirestoreProperty] public int coins { get; set; } = 0;
        [FirestoreProperty] public BoosterInventory boosters { get; set; } = new BoosterInventory();
        /// <summary>무한 하트 지급 시간 (초). Tier 2 번들 1시간 = 3600 등.</summary>
        [FirestoreProperty] public int infiniteHeartsSeconds { get; set; } = 0;
        [FirestoreProperty] public bool removeAds { get; set; } = false;
    }
}
