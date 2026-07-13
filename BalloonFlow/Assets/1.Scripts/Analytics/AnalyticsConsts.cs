namespace BalloonFlow.Analytics
{
    /// <summary>
    /// BigQuery raw event schema v3.2 매핑용 상수.
    /// Event name + param key (snake_case) — schema 컬럼명과 1:1 일치.
    /// [2026-06-16 BQ_DIRECT] Firebase Analytics 자동 export 폐기 → Cloud Function(ingestAnalyticsEvents)
    ///   경유 BigQuery 직접 streaming 적재. 직접 적재라 GA4 의 이벤트당 25-param 제한 없음
    ///   (extra_json overflow 우회는 더 이상 불필요 — 보내면 params(JSON) 에 그대로 들어감).
    /// </summary>
    public static class AnalyticsConsts
    {
        public const string GAME_ID = "balloonloop";

        // ─── Event names (8 raw event tables) ───
        public const string EVT_SESSION_START    = "session_start_event";
        public const string EVT_SESSION_END      = "session_end_event";
        public const string EVT_LEVEL_PLAY_START = "level_play_start_event";
        public const string EVT_LEVEL_PLAY       = "level_play_event";
        public const string EVT_ITEM_USE         = "item_use_event";
        public const string EVT_PURCHASE         = "purchase_event";
        public const string EVT_ECONOMY          = "economy_event";
        public const string EVT_AD               = "ad_event";
        // ROLLBACK_USER_PROPERTY_PIPELINE_20260708: R_user_property (uid 당 1행, 서버 MERGE UPSERT).
        //   다른 8개와 달리 스트리밍 append 가 아니라 Cloud Function 이 BQ DML MERGE 로 처리.
        public const string EVT_USER_PROPERTY    = "user_property_event";

        // ─── Common params ───
        public const string P_EVENT_ID            = "event_id";
        public const string P_PLAY_ID             = "play_id";
        public const string P_SESSION_ID          = "session_id";
        public const string P_GAME_ID             = "game_id";
        public const string P_UID                 = "uid";
        public const string P_EVENT_TS            = "event_timestamp";   // ISO 8601 UTC
        public const string P_APP_VERSION         = "app_version";
        public const string P_VERSION             = "version";
        public const string P_INSTALL_VERSION     = "install_version";
        public const string P_GEO_COUNTRY         = "geo_country";
        public const string P_COUNTRY             = "country";
        public const string P_PLATFORM            = "platform";
        public const string P_DEVICE_MODEL        = "device_model";

        // ─── Level params ───
        public const string P_LEVEL_NUMBER        = "level_number";
        public const string P_IS_TUTORIAL         = "is_tutorial";
        public const string P_HARD_TIER           = "hard_tier";
        public const string P_ATTEMPT_NUMBER      = "attempt_number";
        public const string P_IS_FIRST_PLAY       = "is_first_play";
        public const string P_IS_REPLAY_AFTER_CLEAR = "is_replay_after_clear";
        public const string P_LIVES_BEFORE        = "lives_before";
        public const string P_LIVES_AFTER         = "lives_after";
        public const string P_IS_INFINITE_LIVES   = "is_infinite_lives_active";

        // ─── Result/end_reason ───
        public const string P_RESULT              = "result";
        public const string P_END_REASON          = "end_reason";

        // ─── Play metrics (level_play_event 25-limit core) ───
        public const string P_MOVES_USED          = "moves_used";
        public const string P_MOVES_GIVEN         = "moves_given";
        public const string P_MOVES_REMAINING     = "moves_remaining";
        public const string P_UNDO_COUNT          = "undo_count";
        public const string P_DEADLOCK_COUNT      = "deadlock_count";
        public const string P_PEAK_RESOURCE       = "peak_resource_usage_ratio";
        // [BQ_DIRECT 2026-06-16] 직접 적재 — extra_json 해체분(play_event 테이블 개별 컬럼).
        public const string P_PLAY_TIME_SEC       = "play_time_sec";
        public const string P_BACKGROUND_TIME_SEC = "background_time_sec";
        public const string P_SCORE               = "score";
        public const string P_STAR_COUNT          = "star_count";
        // ROLLBACK_ANALYTICS_NULLFILL_20260625: play_event NULL 채우기 — 상세 지표 계측 컬럼.
        public const string P_OBJECTIVE_TOTAL      = "objective_total";
        public const string P_OBJECTIVE_DONE       = "objective_done";
        public const string P_AVG_RESOURCE         = "avg_resource_usage_ratio";
        public const string P_CONTINUE_POPUP_COUNT = "continue_popup_count";
        public const string P_CONTINUE_COUNT       = "continue_count";
        public const string P_COIN_EARNED          = "coin_earned";
        public const string P_COIN_SPENT           = "coin_spent";
        public const string P_FINAL_COIN_BALANCE   = "final_coin_balance";
        public const string P_FAIL_OUTERMOST_COLORS = "fail_outermost_colors";
        public const string P_FAIL_RAIL_COMPOSITION = "fail_rail_composition";
        public const string P_IN_PLAY_ITEM_IDS      = "in_play_item_ids";
        public const string P_IN_PLAY_ITEM_COUNT    = "in_play_item_count";
        public const string P_SHUFFLE_COUNT         = "shuffle_count";
        public const string P_HINT_COUNT            = "hint_count";
        public const string P_PRE_PLAY_ITEM_IDS     = "pre_play_item_ids";
        public const string P_PRE_PLAY_ITEM_COUNT   = "pre_play_item_count";

        // ─── Session ───
        public const string P_DURATION_SEC        = "duration_sec";

        // ─── User snapshot (P3 신규, 6 events 공통) ───
        public const string P_INSTALL_AT          = "install_at";
        public const string P_MAX_REACHED_LEVEL   = "max_reached_level";
        public const string P_TOTAL_SPEND_USD     = "total_spend_usd";
        public const string P_TOTAL_AD_REVENUE_USD = "total_ad_revenue_usd";

        // ─── user_property (R_user_property v3.2 — uid 당 1행 UPSERT) ───
        // ROLLBACK_USER_PROPERTY_PIPELINE_20260708. 미전송(NULL 유지): campaign/adgroup/creative(MMP 연동),
        //   idfa(iOS ATT), aid(GAID 네이티브 비동기 — 필요 시 후속).
        // ROLLBACK_INSTALL_MEDIA_SOURCE_20260713: install_media_source 캡처 파이프라인 추가(AppsFlyer
        //   onConversionDataSuccess → UserSnapshotCache 영속 → 여기 stamp). ※ BqUserPropertyColumns 화이트리스트
        //   등록은 서버 BQ 스키마(user_property 테이블 컬럼) + MERGE 반영 후 활성화 — 그 전엔 stamp 돼도 서버 전송 직전
        //   normalize 에서 스트립되어 무해(적재 안전).
        public const string P_INSTALL_MEDIA_SOURCE   = "install_media_source";
        public const string P_INSTALL_COUNTRY        = "install_country";
        public const string P_INSTALL_PLATFORM       = "install_platform";
        public const string P_INSTALL_DEVICE         = "install_device";
        public const string P_LAST_ACTIVE_AT         = "last_active_at";
        public const string P_LAST_ACTIVE_VERSION    = "last_active_version";
        public const string P_LAST_ACTIVE_COUNTRY    = "last_active_country";
        public const string P_LAST_PLAYED_AT         = "last_played_at";
        public const string P_TOTAL_PLAY_COUNT       = "total_play_count";
        public const string P_TOTAL_CLEAR_COUNT      = "total_clear_count";
        public const string P_TOTAL_COIN_BALANCE     = "total_coin_balance";
        public const string P_INFINITE_LIVES_EXPIRY  = "infinite_lives_expiry";
        public const string P_IS_PAYER               = "is_payer";
        public const string P_LAST_UPDATED_AT        = "last_updated_at";
        public const string P_APPSFLYER_ID           = "appsflyer_id";

        // ─── purchase_event ───
        public const string P_PRODUCT_ID          = "product_id";
        public const string P_PRODUCT_NAME        = "product_name";
        public const string P_PRODUCT_TYPE        = "product_type";
        public const string P_PRICE_USD           = "price_usd";
        public const string P_PRICE_LOCAL         = "price_local";
        public const string P_IAP_PLACEMENT       = "iap_placement";
        public const string P_COIN_GRANTED        = "coin_granted";
        public const string P_ITEMS_GRANTED       = "items_granted";
        public const string P_LIVES_GRANTED       = "lives_granted";
        public const string P_IS_VERIFIED         = "is_verified";
        public const string P_CURRENCY            = "currency";             // (BQ_DIRECT 후 미emit — currency_code 로 대체)
        public const string P_STORE               = "store";                // (BQ_DIRECT 후 미emit — purchase 테이블에 컬럼 없음)
        public const string P_TRANSACTION_ID      = "transaction_id";       // (BQ_DIRECT 후 미emit — receipt_id 로 대체)
        public const string P_PRODUCT_CATEGORY    = "product_category";     // (BQ_DIRECT 후 미emit — purchase 테이블에 컬럼 없음)
        public const string P_CURRENCY_CODE       = "currency_code";        // [BQ_DIRECT] purchase 테이블 컬럼
        public const string P_RECEIPT_ID          = "receipt_id";           // [BQ_DIRECT] purchase 테이블(거래/영수증 ID)

        // item_use_event
        public const string P_ITEM_ID             = "item_id";
        public const string P_ITEM_TYPE           = "item_type";            // booster|life|other (BQ_DIRECT 후 item_category 로 대체)
        public const string P_ITEM_CATEGORY       = "item_category";        // [BQ_DIRECT] item_use 테이블 컬럼
        public const string P_ITEM_CONTEXT        = "item_context";         // in_level|lobby|shop|other
        public const string P_QUANTITY            = "quantity";
        public const string P_BALANCE_AFTER       = "balance_after";
        // ROLLBACK_ANALYTICS_NULLFILL_20260625: item_use NULL 채우기 — 실제 획득/비용 추적 컬럼.
        public const string P_ACQUISITION_TYPE    = "acquisition_type";
        public const string P_COST_AMOUNT         = "cost_amount";
        public const string P_COST_CURRENCY_ID    = "cost_currency_id";

        // economy_event
        public const string P_CURRENCY_TYPE       = "currency_type";        // coin|gem|life|booster
        public const string P_FLOW_TYPE           = "flow_type";            // earn|spend (BQ_DIRECT 후 미emit — change_amount 부호로 대체)
        public const string P_SOURCE              = "source";
        public const string P_REF_EVENT_ID        = "ref_event_id";
        public const string P_ECONOMY_PLACEMENT   = "economy_placement";
        public const string P_SINK                = "sink";                 // (BQ_DIRECT 후 미emit — source 컬럼으로 통합)
        public const string P_AMOUNT              = "amount";               // (BQ_DIRECT 후 미emit — change_amount 로 대체)
        public const string P_CHANGE_AMOUNT       = "change_amount";        // [BQ_DIRECT] economy 테이블 부호 단일(earn=+, spend=-)

        // ─── ad_event (impression-level revenue, MAX OnAdRevenuePaidEvent) ───
        public const string P_AD_TYPE             = "ad_type";              // interstitial|rewarded|banner|mrec
        public const string P_AD_REQUEST_ID       = "ad_request_id";
        public const string P_AD_PLACEMENT        = "ad_placement";         // MAX placement (있으면)
        public const string P_AD_REVENUE_USD      = "revenue_usd";
        public const string P_AD_NETWORK          = "ad_network";           // mediated network name
        public const string P_AD_UNIT_ID          = "ad_unit_id";
        public const string P_MEDIATION_POSITION  = "mediation_position";
        public const string P_EVENT_PHASE         = "event_phase";
        public const string P_ERROR_CODE          = "error_code";
        public const string P_ERROR_MESSAGE       = "error_message";
        public const string P_LATENCY_MS          = "latency_ms";
        public const string P_WATCH_DURATION_SEC  = "watch_duration_sec";
        public const string P_AD_DURATION_SEC     = "ad_duration_sec";
        public const string P_REVENUE_USD         = "revenue_usd";
        public const string P_REVENUE_PRECISION   = "revenue_precision";
        public const string P_REWARD_TYPE         = "reward_type";
        public const string P_REWARD_AMOUNT       = "reward_amount";
        public const string P_REWARD_ITEM_ID      = "reward_item_id";

        // ─── 25-param overflow consolidation ───
        // level_play_event 44 컬럼 → 25 limit. extra_json 으로 통합:
        //   play_time_sec, background_time_sec, moves_given, moves_remaining, undo_count,
        //   objective_total, objective_done, avg_resource_usage_ratio,
        //   fail_outermost_colors, fail_rail_composition, score, star_count,
        //   in_play_item_ids, in_play_item_count, continue_popup_count, continue_count,
        //   coin_earned, coin_spent, final_coin_balance, deadlock_count, shuffle_count, hint_count
        //   pre_play_item_ids, pre_play_item_count (level_play_start_event)
        public const string P_EXTRA_JSON          = "extra_json";

        // ─── result 3종 (P1-A 확정) ───
        public const string RESULT_CLEAR = "clear";
        public const string RESULT_FAIL  = "fail";
        public const string RESULT_QUIT  = "quit";

        // ─── end_reason 5종 (fail_deadlock 제거됨) ───
        public const string END_CLEAR                = "clear";
        public const string END_FAIL_OUT_OF_RESOURCE = "fail_out_of_resource";
        public const string END_QUIT_BY_USER         = "quit_by_user";
        public const string END_QUIT_BY_SYSTEM       = "quit_by_system";
        public const string END_TIMEOUT_INFERRED     = "timeout_inferred";

        // ─── 세션 정책 ───
        public const int SESSION_BG_TIMEOUT_MIN = 30; // Firebase 기본값 (v3.2 P2-b 확정)
    }
}
