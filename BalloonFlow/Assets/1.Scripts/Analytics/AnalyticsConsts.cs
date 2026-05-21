namespace BalloonFlow.Analytics
{
    /// <summary>
    /// BigQuery raw event schema v3.2 매핑용 상수.
    /// Event name + param key (snake_case) — schema 컬럼명과 1:1 일치.
    /// Firebase Analytics LogEvent → BigQuery 자동 export 경로 가정.
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

        // ─── Common params ───
        public const string P_EVENT_ID            = "event_id";
        public const string P_PLAY_ID             = "play_id";
        public const string P_SESSION_ID          = "session_id";
        public const string P_GAME_ID             = "game_id";
        public const string P_UID                 = "uid";
        public const string P_EVENT_TS            = "event_ts";          // ISO 8601 UTC
        public const string P_APP_VERSION         = "app_version";
        public const string P_INSTALL_VERSION     = "install_version";
        public const string P_GEO_COUNTRY         = "geo_country";
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
        public const string P_PEAK_RESOURCE       = "peak_resource_usage_ratio";

        // ─── Session ───
        public const string P_DURATION_SEC        = "duration_sec";

        // ─── User snapshot (P3 신규, 6 events 공통) ───
        public const string P_INSTALL_AT          = "install_at";
        public const string P_MAX_REACHED_LEVEL   = "max_reached_level";
        public const string P_TOTAL_SPEND_USD     = "total_spend_usd";
        public const string P_TOTAL_AD_REVENUE_USD = "total_ad_revenue_usd";

        // ─── purchase_event ───
        public const string P_PRODUCT_ID          = "product_id";
        public const string P_PRICE_USD           = "price_usd";
        public const string P_CURRENCY            = "currency";
        public const string P_STORE               = "store";                // google_play | app_store | editor
        public const string P_TRANSACTION_ID      = "transaction_id";
        public const string P_PRODUCT_CATEGORY    = "product_category";     // coin|bundle|noads|offer

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
