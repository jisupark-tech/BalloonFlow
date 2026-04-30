namespace BalloonFlow
{
    /// <summary>
    /// 리소스 경로 / 상수 모음. 모든 Resources.Load 호출 / Sprite path 등 하드코딩 string 은 여기로 모으는 정책.
    ///
    /// 사용법:
    ///   var sprite = Resources.Load&lt;Sprite&gt;(Const.SPR_ICON_HAND);
    ///   var prefab = Resources.Load&lt;GameObject&gt;(Const.PREFAB_FXGOLD);
    ///
    /// 정책:
    ///   - Resources/ 상대 path 는 확장자 제외 ("Sprites/UI/iconHand")
    ///   - Addressable 키는 별도 ADDR_ prefix 사용 (도입 시)
    ///   - SpriteAtlas 키는 ATLAS_ prefix
    ///   - Const 추가 시 PrefabReferenceScanner 출력에서 path 그대로 복사
    /// </summary>
    public static class Const
    {
        #region UI Prefab Paths

        public const string UI_TITLE     = "UI/UITitle";
        public const string UI_LOBBY     = "UI/UILobby";
        public const string UI_HUD       = "UI/UIHud";
        public const string UI_SHOP      = "UI/UIShop";
        public const string UI_SETTING   = "UI/UISetting";

        #endregion

        #region Popup Prefab Paths

        public const string POPUP_RESULT          = "Popup/PopupResult";
        public const string POPUP_CONTINUE        = "Popup/PopupContinue";
        public const string POPUP_FAIL01          = "Popup/PopupFail01";
        public const string POPUP_FAIL02          = "Popup/PopupFail02";
        public const string POPUP_SETTINGS        = "Popup/PopupSettings";
        public const string POPUP_GOLD_SHOP       = "Popup/PopupGoldShop";
        public const string POPUP_QUIT            = "Popup/PopupQuit";
        public const string POPUP_DESCRIPTION     = "Popup/PopupDescription";
        public const string POPUP_ITEM_DESC       = "Popup/PopupItemDescription";
        public const string POPUP_BUY_ITEM        = "Popup/PopupBuyItem";
        public const string POPUP_USE_ITEM        = "Popup/UseItem";
        public const string POPUP_MORE_LIVE       = "Popup/PopupMoreLive";
        public const string POPUP_ERROR           = "Popup/PopupError";
        public const string POPUP_TUTORIAL        = "Popup/Tutorial";
        public const string POPUP_NEW_FEATURE     = "Popup/NewFeature";
        public const string POPUP_COMMON_FRAME    = "Popup/PopupCommonFrame";
        public const string POPUP_TXT_TOAST       = "Popup/TxtToast";

        #endregion

        #region UI Asset Prefab Paths

        public const string PREFAB_FXGOLD             = "UI/UIAssets/FXGold";
        public const string PREFAB_FX_ZAP_LINE        = "UI/UIAssets/FxZapLine";
        public const string PREFAB_GOLD_PANEL         = "UI/UIAssets/GoldPanel";
        public const string PREFAB_ITEM_BTN           = "UI/UIAssets/ItemBtn";
        public const string PREFAB_LOBBY_RAIL_BOX     = "UI/UIAssets/LobbyRailBox";
        public const string PREFAB_SHOP_ITEM          = "UI/UIAssets/ShopItem";
        public const string PREFAB_SHOP_LIST_AD       = "UI/UIAssets/ShopListAd";
        public const string PREFAB_SHOP_LIST_GOLD     = "UI/UIAssets/ShopListGold";
        public const string PREFAB_SHOP_LIST_GOLD_ALIGN = "UI/UIAssets/ShopListGoldAlign";
        public const string PREFAB_SHOP_LIST_ITEM     = "UI/UIAssets/ShopListItem";

        #endregion

        #region InGame Prefab Paths

        public const string PREFAB_BALLOON         = "Prefabs/Balloon";
        public const string PREFAB_DART            = "Prefabs/Dart";
        public const string PREFAB_HOLDER          = "Prefabs/Holder";
        public const string PREFAB_SPAWNER         = "Prefabs/Spawner";
        public const string PREFAB_KEY             = "Prefabs/Key";
        public const string PREFAB_LOCK            = "Prefabs/Lock";
        public const string PREFAB_BARICADE        = "Prefabs/Baricade";
        public const string PREFAB_IRON_BOX        = "Prefabs/IronBox";
        public const string PREFAB_WOODEN_BOARD    = "Prefabs/WoodenBoard";
        public const string PREFAB_FROZEN_LAYER    = "Prefabs/FrozenLayer";
        public const string PREFAB_CIRCLE_PARTICLE = "Prefabs/CircleParticle";
        public const string PREFAB_RAIL            = "Prefabs/Rail";
        public const string PREFAB_GROUND          = "Prefabs/Ground";
        public const string PREFAB_ITEM_ZAP        = "Prefabs/ItemZap";

        #endregion

        #region Pool Keys (ObjectPoolManager)

        public const string POOL_BALLOON          = "Balloon";
        public const string POOL_DART             = "Dart";
        public const string POOL_HOLDER           = "Holder";
        public const string POOL_SPAWNER          = "Spawner";
        public const string POOL_KEY              = "Key";
        public const string POOL_LOCK             = "Lock";
        public const string POOL_BARICADE         = "Baricade";
        public const string POOL_IRON_BOX         = "IronBox";
        public const string POOL_WOODEN_BOARD     = "WoodenBoard";
        public const string POOL_FROZEN_LAYER     = "FrozenLayer";
        public const string POOL_CIRCLE_PARTICLE  = "CircleParticle";
        public const string POOL_FXGOLD           = "FXGold";
        public const string POOL_TXT_TOAST        = "TxtToast";

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        // Sprite paths — PrefabReferenceScanner 결과 보고 채울 영역.
        // 명명 규칙: SPR_<카테고리>_<이름> (UI 아이콘은 SPR_ICON_*, 결과 패널은 SPR_RESULT_* 등)
        // ─────────────────────────────────────────────────────────────────────

        #region Sprite Paths — TODO: PrefabReferenceScanner 결과로 채워넣기

        // 예시 (실제 path 는 스캐너 출력 보고 확정):
        // public const string SPR_ICON_HAND          = "Sprites/UI/iconHand";
        // public const string SPR_ICON_SHUFFLE       = "Sprites/UI/iconSuffle";
        // public const string SPR_ICON_ZAP           = "Sprites/UI/iconZap";
        // public const string SPR_ICON_COIN          = "Sprites/UI/iconCoin";
        // public const string SPR_ICON_INFINITE_HEART= "Sprites/UI/iconInfiniteHeart";
        // public const string SPR_ICON_REMOVE_ADS    = "Sprites/UI/iconRemoveAds";

        #endregion

        #region Audio Clip Paths

        public const string SFX_POPUP_TOUCH = "Sound/Effect/Common_Popup_Touch";

        #endregion

        #region PlayerPrefs Keys

        public const string PREFS_AD_REMOVED        = "BalloonFlow_AdRemoved";
        public const string PREFS_NO_ADS_OWNED      = "BalloonFlow_NoAdsOwned";
        public const string PREFS_STARTER_PURCHASED = "BalloonFlow_StarterPurchased";
        public const string PREFS_CURRENT_LIVES     = "BF_CurrentLives";
        public const string PREFS_LAST_RECHARGE_UTC = "BF_LastRechargeUtc";
        public const string PREFS_DAILY_LAST_CLAIM  = "BF_DailyReward_LastClaim";
        public const string PREFS_DAILY_STREAK_DAY  = "BF_DailyReward_StreakDay";
        public const string PREFS_PENDING_LEVEL_ID  = "BF_PendingLevelId";

        #endregion
    }
}
