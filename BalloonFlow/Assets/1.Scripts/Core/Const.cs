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
        // Addressables — Label / Key 정책
        // ─────────────────────────────────────────────────────────────────────
        //
        // Group 구조 (BalloonFlow):
        //   - Local_Always       : 빌드 포함, 시작 시 즉시 사용 (label "core")
        //   - Local_OnDemand     : 빌드 포함, 첫 사용 시 lazy (label "ui")
        //   - Remote_CDM         : Title download 단계에서 fetch (label "cdm" + 세부 "bgm"/"sfx" 등)
        //
        // 명명:
        //   - Label : ADDR_LABEL_*    예 ADDR_LABEL_CDM = "cdm"
        //   - Key   : ADDR_<카테고리>_<이름>  예 ADDR_ICON_HAND = "icon_hand"
        //   - 기존 Resources/ path 와 구분 위해 별도 prefix 사용

        #region Addressable Labels

        public const string ADDR_LABEL_CORE = "core";        // Local_Always
        public const string ADDR_LABEL_UI   = "ui";          // Local_OnDemand
        public const string ADDR_LABEL_CDM  = "cdm";         // Remote_CDM (Title 다운로드 대상)
        public const string ADDR_LABEL_BGM  = "bgm";
        public const string ADDR_LABEL_SFX  = "sfx";

        #endregion

        #region Addressable Keys — TODO: 마이그레이션 진행하며 채워넣기
        // ── AUTO-GENERATED by AddressableAutoMigrate (do not edit manually) ──
        // generated from Addressable groups Local_Always / Local_OnDemand / Remote_CDM

        // ── Atlas keys ──────────────────────────────────
        public const string ADDR_ATLAS_UI                                 = "atlas_ui";

        // ── Atlas sprite names (UI atlas) ───────────────
        public const string SPR_ARROW                                    = "arrow";
        public const string SPR_BADGEX3                                  = "badgex3";
        public const string SPR_BADGEX5                                  = "badgex5";
        public const string SPR_BALLOONSHADOW                            = "balloonShadow";
        public const string SPR_BG_TITLE                                 = "bg_title";
        public const string SPR_BOX10                                    = "box10";
        public const string SPR_BOX20                                    = "box20";
        public const string SPR_BOX30                                    = "box30";
        public const string SPR_BOXGLOW                                  = "boxGlow";
        public const string SPR_BOXLIGHT                                 = "boxLight";
        public const string SPR_BOXNORMAL                                = "boxNormal";
        public const string SPR_BOXPURPLE                                = "boxPurple";
        public const string SPR_BOXRED                                   = "boxRed";
        public const string SPR_BOXSHADOW                                = "boxShadow";
        public const string SPR_BTN                                      = "Btn";
        public const string SPR_BTN_BEIGE                                = "btn_beige";
        public const string SPR_BTN_BLUE                                 = "btn_blue";
        public const string SPR_BTN_GRAY                                 = "btn_gray";
        public const string SPR_BTN_GREEN                                = "btn_green";
        public const string SPR_BTN_ORANGE                               = "btn_orange";
        public const string SPR_BTN_PURPLE                               = "btn_purple";
        public const string SPR_BTN_RED                                  = "btn_red";
        public const string SPR_BTN_ROUND_GREEN                          = "btn_round_green";
        public const string SPR_BTN_ROUND_OFF                            = "btn_round_off";
        public const string SPR_BTN_ROUND_PINK                           = "btn_round_pink";
        public const string SPR_BTN_ROUND_RED                            = "btn_round_red";
        public const string SPR_BTN_ROUND_YELLOW                         = "btn_round_yellow";
        public const string SPR_BTN_TAB_BASIC_OFF                        = "btn_tab_basic_off";
        public const string SPR_BTN_TAB_PINK_ON                          = "btn_tab_pink_on";
        public const string SPR_BTN_TEAL                                 = "btn_teal";
        public const string SPR_BTN_YELLOW                               = "btn_yellow";
        public const string SPR_BTNBLUE                                  = "btnBlue";
        public const string SPR_BTNEXIT                                  = "btnExit";
        public const string SPR_BTNEXITFRAME                             = "BtnExitFrame";
        public const string SPR_BTNFRAME                                 = "btnFrame";
        public const string SPR_BTNGREEN                                 = "btnGreen";
        public const string SPR_BTNNOTIFICATION                          = "btnNotification";
        public const string SPR_BTNPURPLE                                = "btnPurple";
        public const string SPR_BTNRED                                   = "btnRed";
        public const string SPR_BTNSETTINGFRAME                          = "btnSettingFrame";
        public const string SPR_BTNSETTINGHARD                           = "btnSettingHard";
        public const string SPR_BTNSETTINGNORMAL                         = "btnSettingNormal";
        public const string SPR_BTNSETTINGOPTIONINNER                    = "btnSettingOptionInner";
        public const string SPR_BTNSETTINGOPTIONINNEROFF                 = "btnSettingOptionInnerOff";
        public const string SPR_BTNSETTINGSUPERHARD                      = "btnSettingSuperHard";
        public const string SPR_CIRCLE                                   = "circle";
        public const string SPR_CLOCK                                    = "clock";
        public const string SPR_CLOCKLINE                                = "clockLine";
        public const string SPR_DARTBLUE                                 = "dartBlue";
        public const string SPR_DARTPURPLE                               = "dartPurple";
        public const string SPR_DARTRED                                  = "dartRed";
        public const string SPR_DARTSHADOW                               = "dartShadow";
        public const string SPR_DARTYELLOW                               = "dartYellow";
        public const string SPR_DISCOUNT                                 = "discount";
        public const string SPR_FRAMEBOTTOMHARD                          = "frameBottomHard";
        public const string SPR_FRAMEBOTTOMNORMAL                        = "frameBottomNormal";
        public const string SPR_FRAMEBOTTOMSUPERHARD                     = "frameBottomSuperHard";
        public const string SPR_FRAMECLOCK                               = "frameClock";
        public const string SPR_FRAMEGOLD                                = "frameGold";
        public const string SPR_FRAMEHARD                                = "framehard";
        public const string SPR_FRAMEITEM                                = "frameItem";
        public const string SPR_FRAMEITEMHARD                            = "frameItemHard";
        public const string SPR_FRAMEITEMNORMAL                          = "frameItemNormal";
        public const string SPR_FRAMEITEMSUPERHARD                       = "frameItemSuperHard";
        public const string SPR_FRAMELEVEL                               = "frameLevel";
        public const string SPR_FRAMELEVELHARD                           = "frameLevelHard";
        public const string SPR_FRAMELEVELNORMAL                         = "frameLevelNormal";
        public const string SPR_FRAMELEVELSUPERHARD                      = "frameLevelSuperHard";
        public const string SPR_FRAMEPOPUPCONTINUEHARD                   = "framePopupContinueHard";
        public const string SPR_FRAMEPOPUPCONTINUENORMAL                 = "framePopupContinueNormal";
        public const string SPR_FRAMEPOPUPCONTINUESUPERHARD              = "framePopupContinueSuperHard";
        public const string SPR_FRAMEPOPUPHARD                           = "framePopupHard";
        public const string SPR_FRAMEPOPUPNORMAL                         = "framePopupNormal";
        public const string SPR_FRAMEPOPUPSUPERHARD                      = "framePopupSuperHard";
        public const string SPR_FRAMERESULTHARD                          = "frameResultHard";
        public const string SPR_FRAMERESULTNORMAL                        = "frameResultNormal";
        public const string SPR_FRAMERESULTSUPERHARD                     = "frameResultSuperHard";
        public const string SPR_FRAMESUPERHARD                           = "frameSuperhard";
        public const string SPR_FRAMETUTORIAL                            = "frameTutorial";
        public const string SPR_FXZAP                                    = "Fxzap";
        public const string SPR_GAME_TOPUI_SETTING                       = "Game_TopUi_Setting";
        public const string SPR_GAME_TOPUI_STARBACKGROUND                = "Game_TopUi_StarBackground";
        public const string SPR_GLOW                                     = "glow";
        public const string SPR_GOLD                                     = "Gold";
        public const string SPR_GOLD01                                   = "gold01";
        public const string SPR_GOLD02                                   = "gold02";
        public const string SPR_GOLD03                                   = "gold03";
        public const string SPR_GOLD04                                   = "gold04";
        public const string SPR_GOLD05                                   = "gold05";
        public const string SPR_GOLD06                                   = "gold06";
        public const string SPR_GOLD07                                   = "gold07";
        public const string SPR_GRADATION                                = "gradation";
        public const string SPR_ICONAD                                   = "iconAd";
        public const string SPR_ICONADBTN                                = "iconAdBtn";
        public const string SPR_ICONCANCEL                               = "iconCancel";
        public const string SPR_ICONCHECK                                = "iconCheck";
        public const string SPR_ICONGOLD                                 = "iconGold";
        public const string SPR_ICONHAND                                 = "iconHand";
        public const string SPR_ICONHAPTIC                               = "iconHaptic";
        public const string SPR_ICONHAPTICOFF                            = "iconHapticOff";
        public const string SPR_ICONHEARINFINITE                         = "iconHearInfinite";
        public const string SPR_ICONHEARTBREAK                           = "iconHeartBreak";
        public const string SPR_ICONHOME                                 = "iconHome";
        public const string SPR_ICONINFINITE                             = "iconInfinite";
        public const string SPR_ICONLIFE                                 = "iconLife";
        public const string SPR_ICONMUSIC                                = "iconMusic";
        public const string SPR_ICONMUSICOFF                             = "iconMusicOff";
        public const string SPR_ICONNONE                                 = "iconNone";
        public const string SPR_ICONNOTIFICATION                         = "iconNotification";
        public const string SPR_ICONPLUS                                 = "iconPlus";
        public const string SPR_ICONSETTING                              = "iconSetting";
        public const string SPR_ICONSETTINGLOBBY                         = "iconSettingLobby";
        public const string SPR_ICONSHOP                                 = "iconShop";
        public const string SPR_ICONSKULLHARD                            = "iconSkullHard";
        public const string SPR_ICONSKULLSUPERHARD                       = "iconSkullSuperHard";
        public const string SPR_ICONSOUND                                = "iconSound";
        public const string SPR_ICONSOUNDOFF                             = "iconSoundOff";
        public const string SPR_ICONSUFFLE                               = "iconSuffle";
        public const string SPR_ICONSUFFLEINGAME                         = "iconSuffleIngame";
        public const string SPR_ICONWIFI                                 = "iconWifi";
        public const string SPR_ICONZAP                                  = "iconZap";
        public const string SPR_ICONZAPINGAME                            = "iconZapIngame";
        public const string SPR_IMGBOXDIM                                = "ImgBoxDim";
        public const string SPR_INGAMEBTNFASTARROW                       = "ingameBtnFastArrow";
        public const string SPR_INGAMEBTNFASTFRAME                       = "ingameBtnFastFrame";
        public const string SPR_INGAMEBTNFASTFRAMEGLOW                   = "ingameBtnFastFrameGlow";
        public const string SPR_INGAMEBTNFASTHARD                        = "ingameBtnFastHard";
        public const string SPR_INGAMEBTNFASTNORMAL                      = "ingameBtnFastNormal";
        public const string SPR_INGAMEBTNFASTSUPERHARD                   = "ingameBtnFastSuperHard";
        public const string SPR_INGAMEGAUGEBAR                           = "ingamegaugebar";
        public const string SPR_INGAMEGAUGEBARGREEN                      = "ingameGaugebarGreen";
        public const string SPR_INGAMEGAUGEBARHARD                       = "ingameGaugebarHard";
        public const string SPR_INGAMEGAUGEBARLV                         = "ingamegaugebarLV";
        public const string SPR_INGAMEITEMHAND                           = "ingameItemHand";
        public const string SPR_INGAMEITEMSUFFLE                         = "ingameItemSuffle";
        public const string SPR_INGAMEITEMZAP                            = "ingameItemZap";
        public const string SPR_INGAMELOCK                               = "ingameLock";
        public const string SPR_INGAMESETTINGFRAME                       = "ingameSettingFrame";
        public const string SPR_IRONBOXSHADOW                            = "ironBoxShadow";
        public const string SPR_ITEMCOUNTFRAME                           = "itemCountFrame";
        public const string SPR_LIGHT                                    = "light";
        public const string SPR_LIGHT2                                   = "light2";
        public const string SPR_LOADING                                  = "Loading";
        public const string SPR_LOADINGBAR                               = "loadingBar";
        public const string SPR_LOADINGBARFRAME                          = "LoadingBarFrame";
        public const string SPR_LOBBYBTN_1                               = "LobbyBtn_1";
        public const string SPR_LOBBYBTN_2                               = "LobbyBtn_2";
        public const string SPR_LOGO                                     = "Logo";
        public const string SPR_LOGOEFFECT                               = "logoEffect";
        public const string SPR_MAIN                                     = "Main";
        public const string SPR_NAVIFRAME                                = "naviFrame";
        public const string SPR_NAVIPRESS                                = "naviPress";
        public const string SPR_NEWFEATUREBARICADE                       = "newFeatureBaricade";
        public const string SPR_NEWFEATUREFROZENBOX                      = "newFeatureFrozenBox";
        public const string SPR_NEWFEATUREFROZENLAYER                    = "newFeatureFrozenLayer";
        public const string SPR_NEWFEATUREHIDDENBALLOON                  = "newFeatureHiddenBalloon";
        public const string SPR_NEWFEATUREHIDDENBOX                      = "newFeatureHiddenbox";
        public const string SPR_NEWFEATUREIRONBOX                        = "newFeatureIronBox";
        public const string SPR_NEWFEATUREKEYLOCK                        = "newFeatureKeyLock";
        public const string SPR_NEWFEATURELOOP                           = "newFeatureLoop";
        public const string SPR_NEWFEATUREPINATA                         = "newFeaturePinata";
        public const string SPR_NEWFEATURESPAWNER                        = "newFeatureSpawner";
        public const string SPR_NEWFEATURETITLE                          = "newFeatureTitle";
        public const string SPR_NOTIFICATIONOFF                          = "notificationOff";
        public const string SPR_NOTIFICATIONON                           = "notificationOn";
        public const string SPR_PARTICLE                                 = "particle";
        public const string SPR_PINATAPARTICLE                           = "pinataParticle";
        public const string SPR_POPUPINNER                               = "popupInner";
        public const string SPR_POPUPINNERHARD                           = "popupInnerHard";
        public const string SPR_POPUPINNERNORMAL                         = "popupInnerNormal";
        public const string SPR_POPUPINNERSUPURHARD                      = "popupInnerSupurHard";
        public const string SPR_POPUPRAIL                                = "popupRail";
        public const string SPR_RAIL                                     = "rail";
        public const string SPR_RAIL_CAP_B                               = "rail_cap_b";
        public const string SPR_RAIL_CAP_B_DANGER                        = "rail_cap_b_danger";
        public const string SPR_RAIL_CAP_L                               = "rail_cap_l";
        public const string SPR_RAIL_CAP_L_DANGER                        = "rail_cap_l_danger";
        public const string SPR_RAIL_CAP_R                               = "rail_cap_r";
        public const string SPR_RAIL_CAP_R_DANGER                        = "rail_cap_r_danger";
        public const string SPR_RAIL_CAP_T                               = "rail_cap_t";
        public const string SPR_RAIL_CAP_T_DANGER                        = "rail_cap_t_danger";
        public const string SPR_RAIL_CAVE_B                              = "rail_cave_b";
        public const string SPR_RAIL_CAVE_L                              = "rail_cave_l";
        public const string SPR_RAIL_CAVE_R                              = "rail_cave_r";
        public const string SPR_RAIL_CORNER_BL                           = "rail_corner_bl";
        public const string SPR_RAIL_CORNER_BL_DANGER                    = "rail_corner_bl_danger";
        public const string SPR_RAIL_CORNER_BR                           = "rail_corner_br";
        public const string SPR_RAIL_CORNER_BR_DANGER                    = "rail_corner_br_danger";
        public const string SPR_RAIL_CORNER_H_DANGER                     = "rail_corner_h_danger";
        public const string SPR_RAIL_CORNER_TL                           = "rail_corner_tl";
        public const string SPR_RAIL_CORNER_TL_DANGER                    = "rail_corner_tl_danger";
        public const string SPR_RAIL_CORNER_TR                           = "rail_corner_tr";
        public const string SPR_RAIL_CORNER_TR_DANGER                    = "rail_corner_tr_danger";
        public const string SPR_RAIL_CORNER_V_DANGER                     = "rail_corner_v_danger";
        public const string SPR_RAIL_H_B                                 = "rail_h_b";
        public const string SPR_RAIL_H_T                                 = "rail_h_t";
        public const string SPR_RAIL_VL                                  = "rail_vl";
        public const string SPR_RAIL_VR                                  = "rail_vr";
        public const string SPR_RAILSHADOW                               = "railShadow";
        public const string SPR_SETTINGINNER                             = "settingInner";
        public const string SPR_SHOPADPATTERN                            = "shopADPattern";
        public const string SPR_SHOPBGPATTERN                            = "shopBgPattern";
        public const string SPR_SHOPBTNBLUE                              = "shopBtnBlue";
        public const string SPR_SHOPBTNFRAMEBLUE                         = "shopBtnFrameBlue";
        public const string SPR_SHOPBTNFRAMEPURPLE                       = "shopBtnFramePurple";
        public const string SPR_SHOPBTNFRAMERED                          = "shopBtnFrameRed";
        public const string SPR_SHOPBTNFRAMEYELLOW                       = "shopBtnFrameYellow";
        public const string SPR_SHOPBTNGREEN                             = "shopBtnGreen";
        public const string SPR_SHOPFRAMEBLUE                            = "shopFrameBlue";
        public const string SPR_SHOPFRAMEBLUEWHOLE                       = "shopFrameBlueWhole";
        public const string SPR_SHOPFRAMENORMAL                          = "shopFrameNormal";
        public const string SPR_SHOPFRAMEOUTSIDE                         = "shopFrameOutside";
        public const string SPR_SHOPFRAMEPURPLE                          = "shopFramePurple";
        public const string SPR_SHOPFRAMERED                             = "shopFrameRed";
        public const string SPR_SHOPFRAMESPECIAL                         = "shopFrameSpecial";
        public const string SPR_SHOPFRAMEYELLOW                          = "shopFrameYellow";
        public const string SPR_SHOPTITLEFRAME                           = "ShopTitleFrame";
        public const string SPR_SQUARE                                   = "square";
        public const string SPR_TEXTFAIL                                 = "textFail";
        public const string SPR_TEXTHARD                                 = "textHard";
        public const string SPR_TEXTNORMAL                               = "textNormal";
        public const string SPR_TEXTSUPERHARD                            = "textSuperHard";
        public const string SPR_TOPPANEL                                 = "TopPanel";
        public const string SPR_TS                                       = "TS";

        // ── Popup keys ──────────────────────────────────
        public const string ADDR_POPUP_NEWFEATURE                         = "popup_NewFeature";
        public const string ADDR_POPUP_POPUPBUYITEM                       = "popup_PopupBuyItem";
        public const string ADDR_POPUP_POPUPCOMMONFRAME                   = "popup_PopupCommonFrame";
        public const string ADDR_POPUP_POPUPCONTINUE                      = "popup_PopupContinue";
        public const string ADDR_POPUP_POPUPDESCRIPTION                   = "popup_PopupDescription";
        public const string ADDR_POPUP_POPUPFAIL01                        = "popup_PopupFail01";
        public const string ADDR_POPUP_POPUPFAIL02                        = "popup_PopupFail02";
        public const string ADDR_POPUP_POPUPGOLDSHOP                      = "popup_PopupGoldShop";
        public const string ADDR_POPUP_POPUPITEMDESCRIPTION               = "popup_PopupItemDescription";
        public const string ADDR_POPUP_POPUPMORELIVE                      = "popup_PopupMoreLive";
        public const string ADDR_POPUP_POPUPNOADS                         = "popup_PopupNoAds";
        public const string ADDR_POPUP_POPUPQUIT                          = "popup_PopupQuit";
        public const string ADDR_POPUP_POPUPRESULT                        = "popup_PopupResult";
        public const string ADDR_POPUP_POPUPSETTINGS                      = "popup_PopupSettings";
        public const string ADDR_POPUP_TUTORIAL                           = "popup_Tutorial";
        public const string ADDR_POPUP_TXTTOAST                           = "popup_TxtToast";
        public const string ADDR_POPUP_USEITEM                            = "popup_UseItem";

        // ── UI keys ─────────────────────────────────────
        public const string ADDR_UI_FXGOLD                                = "ui_FXGold";
        public const string ADDR_UI_FXZAPLINE                             = "ui_FxZapLine";
        public const string ADDR_UI_GOLDPANEL                             = "ui_GoldPanel";
        public const string ADDR_UI_ITEMBTN                               = "ui_ItemBtn";
        public const string ADDR_UI_LOBBYRAILBOX                          = "ui_LobbyRailBox";
        public const string ADDR_UI_SHOPITEM                              = "ui_ShopItem";
        public const string ADDR_UI_SHOPLISTAD                            = "ui_ShopListAd";
        public const string ADDR_UI_SHOPLISTGOLD                          = "ui_ShopListGold";
        public const string ADDR_UI_SHOPLISTGOLDALIGN                     = "ui_ShopListGoldAlign";
        public const string ADDR_UI_SHOPLISTITEM                          = "ui_ShopListItem";
        public const string ADDR_UI_UIHUD                                 = "ui_UIHud";
        public const string ADDR_UI_UILOBBY                               = "ui_UILobby";
        public const string ADDR_UI_UISETTING                             = "ui_UISetting";
        public const string ADDR_UI_UISHOP                                = "ui_UIShop";

        // ── InGame Prefab keys (Local_Always / core) ────
        public const string ADDR_PREFAB_BALLOON                           = "prefab_Balloon";
        public const string ADDR_PREFAB_BARICADE                          = "prefab_Baricade";
        public const string ADDR_PREFAB_CIRCLEPARTICLE                    = "prefab_CircleParticle";
        public const string ADDR_PREFAB_DART                              = "prefab_Dart";
        public const string ADDR_PREFAB_FROZENLAYER                       = "prefab_FrozenLayer";
        public const string ADDR_PREFAB_GROUND                            = "prefab_Ground";
        public const string ADDR_PREFAB_HOLDER                            = "prefab_Holder";
        public const string ADDR_PREFAB_IRONBOX                           = "prefab_IronBox";
        public const string ADDR_PREFAB_ITEMZAP                           = "prefab_ItemZap";
        public const string ADDR_PREFAB_KEY                               = "prefab_Key";
        public const string ADDR_PREFAB_LOCK                              = "prefab_Lock";
        public const string ADDR_PREFAB_RAIL                              = "prefab_Rail";
        public const string ADDR_PREFAB_SPAWNER                           = "prefab_Spawner";
        public const string ADDR_PREFAB_WOODENBOARD                       = "prefab_WoodenBoard";

        // ── Audio keys (Remote_CDM / cdm) ───────────────
        public const string ADDR_AUDIO_COMMON_COIN_GAIN                   = "audio_Common_Coin_Gain";
        public const string ADDR_AUDIO_COMMON_NORMAL_TOUCH                = "audio_Common_Normal_Touch";
        public const string ADDR_AUDIO_COMMON_POPUP_TOUCH                 = "audio_Common_Popup_Touch";
        public const string ADDR_AUDIO_INGAME                             = "audio_InGame";
        public const string ADDR_AUDIO_LOBBY                              = "audio_Lobby";
        public const string ADDR_AUDIO_MAIN_BGM                           = "audio_Main_BGM";
        public const string ADDR_AUDIO_MAIN_DAILYREWARD_POPUP             = "audio_Main_DailyReward_Popup";
        public const string ADDR_AUDIO_MAIN_TREASUREBOX_OPEN              = "audio_Main_TreasureBox_Open";
        public const string ADDR_AUDIO_STAGE_BGM                          = "audio_Stage_BGM";
        public const string ADDR_AUDIO_STAGE_CLEAR                        = "audio_Stage_Clear";
        public const string ADDR_AUDIO_STAGE_CLEAR_STARSTAMP              = "audio_Stage_Clear_StarStamp";
        public const string ADDR_AUDIO_STAGE_EXPLOSION_BOMB               = "audio_Stage_Explosion_Bomb";
        public const string ADDR_AUDIO_STAGE_EXPLOSION_COLORBOMB          = "audio_Stage_Explosion_ColorBomb";
        public const string ADDR_AUDIO_STAGE_EXPLOSION_STRIPE             = "audio_Stage_Explosion_Stripe";
        public const string ADDR_AUDIO_STAGE_EXPLOSION_TARGET             = "audio_Stage_Explosion_Target";
        public const string ADDR_AUDIO_STAGE_FAIL                         = "audio_Stage_Fail";
        public const string ADDR_AUDIO_STAGE_ITEMUSE_COLORBOMB            = "audio_Stage_ItemUse_ColorBomb";
        public const string ADDR_AUDIO_STAGE_ITEMUSE_CROSS                = "audio_Stage_ItemUse_Cross";
        public const string ADDR_AUDIO_STAGE_ITEMUSE_ONEDESTROY           = "audio_Stage_ItemUse_Onedestroy";
        public const string ADDR_AUDIO_STAGE_MATCH_BOMB                   = "audio_Stage_Match_Bomb";
        public const string ADDR_AUDIO_STAGE_MATCH_COLORBOMB              = "audio_Stage_Match_ColorBomb";
        public const string ADDR_AUDIO_STAGE_MATCH_NORMAL                 = "audio_Stage_Match_Normal";
        public const string ADDR_AUDIO_STAGE_MATCH_SLIDE                  = "audio_Stage_Match_Slide";
        public const string ADDR_AUDIO_STAGE_MATCH_STRIPE                 = "audio_Stage_Match_Stripe";
        public const string ADDR_AUDIO_STAGE_MATCH_TARGET                 = "audio_Stage_Match_Target";
        public const string ADDR_AUDIO_STAGE_MISSION_POPUP                = "audio_Stage_Mission_Popup";
        public const string ADDR_AUDIO_STAGE_OBJECT_DROP                  = "audio_Stage_Object_Drop";

        // 사용 예:
        //   var prefab = await AddressableSystem.LoadAssetAsync<GameObject>(Const.ADDR_POPUP_PopupResult);
        //   var clip   = await AddressableSystem.LoadAssetAsync<AudioClip>(Const.ADDR_AUDIO_Stage_Clear);

        // ── /AUTO-GENERATED ──

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
