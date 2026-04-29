using Firebase.Firestore;

namespace BalloonFlow
{
    /// <summary>
    /// Firestore /users/{uid}/transactions/{txId} 문서 — IAP 영수증 + 검증 결과 audit log.
    /// status: pending → validated → rewarded (또는 invalid / refunded).
    /// 이중 지급 방지: platformOrderId 유니크 제약 (Cloud Function 에서 체크).
    /// CurrencyManager 의 inner Transaction struct 와 이름 충돌 방지를 위해 UserTransaction.
    /// </summary>
    [FirestoreData]
    public class UserTransaction
    {
        [FirestoreProperty] public string transactionId { get; set; } = "";
        [FirestoreProperty] public string productId { get; set; } = "";
        /// <summary>"android" | "ios"</summary>
        [FirestoreProperty] public string platform { get; set; } = "";

        // ── Receipt ───────────────────────────────────────────────
        /// <summary>Unity IAP raw receipt JSON. Cloud Function 에서 Google Play Developer API 검증에 사용.</summary>
        [FirestoreProperty] public string platformReceipt { get; set; } = "";
        /// <summary>Google Play orderId 또는 Apple transactionId. 이중 지급 방지 키.</summary>
        [FirestoreProperty] public string platformOrderId { get; set; } = "";
        /// <summary>Google Play SKU 또는 App Store productId.</summary>
        [FirestoreProperty] public string platformProductId { get; set; } = "";

        // ── Pricing ───────────────────────────────────────────────
        [FirestoreProperty] public double priceUsd { get; set; } = 0d;
        [FirestoreProperty] public string currency { get; set; } = "USD";

        // ── Status ────────────────────────────────────────────────
        [FirestoreProperty] public Timestamp purchasedAt { get; set; }
        [FirestoreProperty] public Timestamp? validatedAt { get; set; }
        /// <summary>"pending" | "validated" | "invalid" | "refunded" | "rewarded"</summary>
        [FirestoreProperty] public string status { get; set; } = "pending";
        [FirestoreProperty] public bool rewardsApplied { get; set; } = false;
        [FirestoreProperty] public string errorReason { get; set; } = "";
    }
}
