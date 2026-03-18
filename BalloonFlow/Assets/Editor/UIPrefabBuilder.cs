#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// Creates all UI/Popup prefabs with their view scripts attached and SerializeField wired.
    /// Output: Resources/UI/*.prefab, Resources/Popup/*.prefab
    /// Each prefab has its MonoBehaviour (UITitle, UILobby, UIHud, PopupResult, PopupSettings, PopupGoldShop)
    /// with [SerializeField] references auto-wired via SerializedObject.
    /// Runs once via [InitializeOnLoad]. Force re-run: BalloonFlow > Rebuild UI Prefabs.
    /// </summary>
    [InitializeOnLoad]
    public static class UIPrefabBuilder
    {
        private const string PREFS_KEY = "BalloonFlow_UIPrefabs_v2";
        private const string UI_FOLDER = "Assets/Resources/UI";
        private const string POPUP_FOLDER = "Assets/Resources/Popup";

        // Colors
        private static readonly Color BG_TITLE     = new Color(0.08f, 0.08f, 0.16f, 1f);
        private static readonly Color BG_LOBBY     = new Color(0.06f, 0.10f, 0.18f, 1f);
        private static readonly Color BG_OVERLAY   = new Color(0f, 0f, 0f, 0.85f);
        private static readonly Color COL_PLAY     = new Color(0.15f, 0.75f, 0.3f, 1f);
        private static readonly Color COL_RETRY    = new Color(0.9f, 0.55f, 0.1f, 1f);
        private static readonly Color COL_NEXT     = new Color(0.2f, 0.6f, 0.95f, 1f);
        private static readonly Color COL_HOME     = new Color(0.5f, 0.5f, 0.55f, 1f);
        private static readonly Color COL_HUD      = new Color(0f, 0f, 0f, 0.5f);
        private static readonly Color COL_COIN     = new Color(0.85f, 0.75f, 0.1f, 1f);
        private static readonly Color COL_SETTINGS = new Color(0.4f, 0.4f, 0.5f, 1f);
        private static readonly Color COL_PANEL    = new Color(0.12f, 0.12f, 0.22f, 1f);
        private static readonly Color COL_SHOP_BG  = new Color(0.08f, 0.1f, 0.2f, 1f);

        private static Font _font;

        static UIPrefabBuilder()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(PREFS_KEY, false)) return;
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                BuildAllUIPrefabs();
                EditorPrefs.SetBool(PREFS_KEY, true);
                Debug.Log("[UIPrefabBuilder] UI prefabs created (v2).");
            };
        }

        /// <summary>PopupGoldShop 프리팹만 단독 재빌드 (다른 프리팹 건드리지 않음)</summary>
        [MenuItem("BalloonFlow/DON'T USE/Rebuild GoldShop Prefab Only")]
        private static void RebuildGoldShopOnly()
        {
            EnsureFolder(POPUP_FOLDER);
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            BuildPopupGoldShop();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[UIPrefabBuilder] PopupGoldShop prefab rebuilt.");
        }

        /// <summary>PopupContinue 프리팹 생성 (이어하기 팝업)</summary>
        [MenuItem("BalloonFlow/DON'T USE/Build PopupContinue Prefab")]
        private static void BuildPopupContinueMenu()
        {
            EnsureFolder(POPUP_FOLDER);
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            BuildPopupContinue();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[UIPrefabBuilder] PopupContinue prefab built.");
        }

        /// <summary>부스터 테스트 패널 프리팹 생성</summary>
        [MenuItem("BalloonFlow/DON'T USE/Build BoosterTestPanel Prefab")]
        private static void BuildBoosterTestPanelMenu()
        {
            EnsureFolder(UI_FOLDER);
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            BuildBoosterTestPanel();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[UIPrefabBuilder] BoosterTestPanel prefab built.");
        }

        private static void BuildAllUIPrefabs()
        {
            EnsureFolder(UI_FOLDER);
            EnsureFolder(POPUP_FOLDER);
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            BuildUITitle();
            BuildUILobby();
            BuildUIHud();
            BuildPopupResult();
            BuildPopupSettings();
            BuildPopupGoldShop();
            BuildPopupContinue();
            BuildBoosterTestPanel();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // ═══════════════════════════════════════════
        // UI/UITitle
        // ═══════════════════════════════════════════

        private static void BuildUITitle()
        {
            var root = CreateUIRoot("UITitle");

            var bg = AddImage(root.transform, "Background", BG_TITLE);
            Stretch(bg);

            var logoGO = AddText(root.transform, "LogoText", "BalloonFlow", 72,
                TextAnchor.MiddleCenter, Color.white, V(0, 120), V(900, 140));
            var subtitleGO = AddText(root.transform, "SubtitleText", "Pop & Flow Puzzle", 28,
                TextAnchor.MiddleCenter, new Color(0.7f, 0.7f, 0.8f), V(0, 20), V(600, 50));
            var tapGO = AddText(root.transform, "TapToStart", "Tap to Start", 24,
                TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.5f), V(0, -250), V(400, 50));

            // AddComponent + wire SerializeField
            var comp = root.AddComponent<UITitle>();
            var so = new SerializedObject(comp);
            so.FindProperty("_logoText").objectReferenceValue = logoGO.GetComponent<Text>();
            so.FindProperty("_subtitleText").objectReferenceValue = subtitleGO.GetComponent<Text>();
            so.FindProperty("_tapToStartText").objectReferenceValue = tapGO.GetComponent<Text>();
            so.ApplyModifiedProperties();

            SaveAndCleanup(root, UI_FOLDER + "/UITitle.prefab");
        }

        // ═══════════════════════════════════════════
        // UI/UILobby
        // ═══════════════════════════════════════════

        private static void BuildUILobby()
        {
            var root = CreateUIRoot("UILobby");

            var bg = AddImage(root.transform, "Background", BG_LOBBY);
            Stretch(bg);

            AddText(root.transform, "MainTitle", "BalloonFlow", 56,
                TextAnchor.MiddleCenter, Color.white, V(0, 400), V(800, 100));

            // Top bar
            var topBar = AddImage(root.transform, "TopBar", new Color(0, 0, 0, 0.4f));
            SetTopStretch(topBar, 80);

            // Coin button (left)
            var coinBtnGO = AddButton(topBar.transform, "CoinButton", "1000", COL_COIN, 22,
                V(0, 0), V(200, 50));
            AnchorLeft(coinBtnGO);
            coinBtnGO.GetComponent<RectTransform>().anchoredPosition = new Vector2(120, 0);

            // Settings button (right)
            var settingsBtnGO = AddButton(topBar.transform, "SettingsBtn", "Settings", COL_SETTINGS, 20,
                V(0, 0), V(140, 50));
            AnchorRight(settingsBtnGO);
            settingsBtnGO.GetComponent<RectTransform>().anchoredPosition = new Vector2(-90, 0);

            // Play button
            var playBtnGO = AddButton(root.transform, "PlayButton", "Stage 1", COL_PLAY, 36,
                V(0, -50), V(380, 120));

            // AddComponent + wire
            var comp = root.AddComponent<UILobby>();
            var so = new SerializedObject(comp);
            so.FindProperty("_playButton").objectReferenceValue = playBtnGO.GetComponent<Button>();
            so.FindProperty("_playButtonLabel").objectReferenceValue = playBtnGO.transform.Find("Label").GetComponent<Text>();
            so.FindProperty("_coinButton").objectReferenceValue = coinBtnGO.GetComponent<Button>();
            so.FindProperty("_coinDisplayText").objectReferenceValue = coinBtnGO.transform.Find("Label").GetComponent<Text>();
            so.FindProperty("_settingsButton").objectReferenceValue = settingsBtnGO.GetComponent<Button>();
            so.ApplyModifiedProperties();

            SaveAndCleanup(root, UI_FOLDER + "/UILobby.prefab");
        }

        // ═══════════════════════════════════════════
        // UI/UIHud
        // ═══════════════════════════════════════════

        private static void BuildUIHud()
        {
            var root = CreateUIRoot("UIHud");

            // Row 1: top bar
            var hudTop = AddImage(root.transform, "HUD_Top", COL_HUD);
            SetTopStretch(hudTop, 80);

            // Settings button (LEFT)
            var settingsBtnGO = AddButton(hudTop.transform, "SettingsBtn", "SET", COL_SETTINGS, 18,
                V(0, 0), V(80, 50));
            AnchorLeft(settingsBtnGO);
            settingsBtnGO.GetComponent<RectTransform>().anchoredPosition = new Vector2(50, 0);

            // Level text (CENTER)
            var levelGO = AddText(hudTop.transform, "LevelText", "Level 1", 26,
                TextAnchor.MiddleCenter, Color.white, V(0, 0), V(250, 40));

            // Gold panel (RIGHT)
            var goldPanel = AddImage(hudTop.transform, "GoldPanel", new Color(0, 0, 0, 0.3f));
            var goldRT = goldPanel.GetComponent<RectTransform>();
            goldRT.anchorMin = new Vector2(1, 0.5f);
            goldRT.anchorMax = new Vector2(1, 0.5f);
            goldRT.pivot = new Vector2(1, 0.5f);
            goldRT.anchoredPosition = new Vector2(-10, 0);
            goldRT.sizeDelta = new Vector2(200, 45);

            var goldTextGO = AddText(goldPanel.transform, "GoldText", "1000", 22,
                TextAnchor.MiddleCenter, Color.yellow, V(-20, 0), V(120, 35));

            var plusBtnGO = AddButton(goldPanel.transform, "GoldPlusBtn", "+", COL_COIN, 24,
                V(0, 0), V(45, 40));
            AnchorRight(plusBtnGO);
            plusBtnGO.GetComponent<RectTransform>().anchoredPosition = new Vector2(-5, 0);

            // Row 2: sub bar
            var hudSub = AddImage(root.transform, "HUD_Sub", new Color(0, 0, 0, 0.3f));
            var hudSubRT = hudSub.GetComponent<RectTransform>();
            hudSubRT.anchorMin = new Vector2(0, 1);
            hudSubRT.anchorMax = new Vector2(1, 1);
            hudSubRT.pivot = new Vector2(0.5f, 1);
            hudSubRT.offsetMin = new Vector2(0, -130);
            hudSubRT.offsetMax = new Vector2(0, -80);

            var holderGO = AddText(hudSub.transform, "HolderText", "On Rail: 0/9", 18,
                TextAnchor.MiddleRight, new Color(0.3f, 0.9f, 0.7f), V(-20, 0), V(200, 35));
            AnchorRight(holderGO);
            holderGO.GetComponent<RectTransform>().anchoredPosition = new Vector2(-20, 0);

            // AddComponent + wire
            var comp = root.AddComponent<UIHud>();
            var so = new SerializedObject(comp);
            so.FindProperty("_settingsButton").objectReferenceValue = settingsBtnGO.GetComponent<Button>();
            so.FindProperty("_levelText").objectReferenceValue = levelGO.GetComponent<Text>();
            so.FindProperty("_goldText").objectReferenceValue = goldTextGO.GetComponent<Text>();
            so.FindProperty("_goldPlusButton").objectReferenceValue = plusBtnGO.GetComponent<Button>();
            so.FindProperty("_holderCountText").objectReferenceValue = holderGO.GetComponent<Text>();
            so.ApplyModifiedProperties();

            SaveAndCleanup(root, UI_FOLDER + "/UIHud.prefab");
        }

        // ═══════════════════════════════════════════
        // Popup/PopupResult
        // ═══════════════════════════════════════════

        private static void BuildPopupResult()
        {
            var root = CreateUIRoot("PopupResult");

            var overlay = AddImage(root.transform, "Overlay", BG_OVERLAY);
            Stretch(overlay);

            var panel = AddImage(root.transform, "ResultPanel", COL_PANEL);
            SetCenter(panel, V(0, 0), V(700, 750));

            var titleGO = AddText(panel.transform, "ResultTitle", "Level Clear!", 48,
                TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.1f), V(0, 270), V(600, 80));

            var nextGO = AddButton(panel.transform, "NextButton", "NEXT", COL_NEXT, 28,
                V(0, -30), V(260, 75));
            var retryGO = AddButton(panel.transform, "RetryButton", "RETRY", COL_RETRY, 28,
                V(0, -120), V(260, 75));
            var homeGO = AddButton(panel.transform, "HomeButton", "HOME", COL_HOME, 24,
                V(0, -210), V(200, 60));

            // AddComponent + wire
            var comp = root.AddComponent<PopupResult>();
            var so = new SerializedObject(comp);
            so.FindProperty("_titleText").objectReferenceValue = titleGO.GetComponent<Text>();
            so.FindProperty("_nextButton").objectReferenceValue = nextGO.GetComponent<Button>();
            so.FindProperty("_retryButton").objectReferenceValue = retryGO.GetComponent<Button>();
            so.FindProperty("_homeButton").objectReferenceValue = homeGO.GetComponent<Button>();
            so.ApplyModifiedProperties();

            SaveAndCleanup(root, POPUP_FOLDER + "/PopupResult.prefab");
        }

        // ═══════════════════════════════════════════
        // Popup/PopupSettings
        // ═══════════════════════════════════════════

        private static void BuildPopupSettings()
        {
            var root = CreateUIRoot("PopupSettings");

            var overlay = AddImage(root.transform, "Overlay", BG_OVERLAY);
            Stretch(overlay);

            var panel = AddImage(root.transform, "SettingsPanel", COL_PANEL);
            SetCenter(panel, V(0, 0), V(600, 500));

            AddText(panel.transform, "SettingsTitle", "Settings", 36,
                TextAnchor.MiddleCenter, Color.white, V(0, 180), V(400, 60));
            var soundGO = AddText(panel.transform, "SoundLabel", "Sound: ON", 24,
                TextAnchor.MiddleCenter, Color.white, V(0, 60), V(300, 40));
            var musicGO = AddText(panel.transform, "MusicLabel", "Music: ON", 24,
                TextAnchor.MiddleCenter, Color.white, V(0, 10), V(300, 40));
            var closeBtnGO = AddButton(panel.transform, "CloseBtn", "CLOSE", COL_HOME, 24,
                V(0, -180), V(200, 60));

            // AddComponent + wire
            var comp = root.AddComponent<PopupSettings>();
            var so = new SerializedObject(comp);
            so.FindProperty("_closeButton").objectReferenceValue = closeBtnGO.GetComponent<Button>();
            so.FindProperty("_soundLabel").objectReferenceValue = soundGO.GetComponent<Text>();
            so.FindProperty("_musicLabel").objectReferenceValue = musicGO.GetComponent<Text>();
            so.ApplyModifiedProperties();

            SaveAndCleanup(root, POPUP_FOLDER + "/PopupSettings.prefab");
        }

        // ═══════════════════════════════════════════
        // Popup/PopupGoldShop
        // ═══════════════════════════════════════════

        private static void BuildPopupGoldShop()
        {
            var root = CreateUIRoot("PopupGoldShop");

            // Full-screen dark blue background (레퍼런스 매칭)
            var bg = AddImage(root.transform, "Background", new Color(0.06f, 0.12f, 0.28f));
            Stretch(bg);

            // Close button (X) top-right
            var closeBtnGO = AddButton(root.transform, "CloseBtn", "X", COL_HOME, 28,
                V(0, 0), V(60, 60));
            var closeRT = closeBtnGO.GetComponent<RectTransform>();
            closeRT.anchorMin = new Vector2(1, 1);
            closeRT.anchorMax = new Vector2(1, 1);
            closeRT.pivot = new Vector2(1, 1);
            closeRT.anchoredPosition = new Vector2(-15, -15);

            // ScrollView — fills most of the screen (below top bar, above tab bar)
            var scrollViewGO = new GameObject("ScrollView");
            scrollViewGO.layer = LayerMask.NameToLayer("UI");
            scrollViewGO.transform.SetParent(root.transform, false);
            var svRT = scrollViewGO.AddComponent<RectTransform>();
            svRT.anchorMin = new Vector2(0, 0.08f);  // above tab bar
            svRT.anchorMax = new Vector2(1, 0.94f);   // below close button
            svRT.offsetMin = Vector2.zero;
            svRT.offsetMax = Vector2.zero;
            var svImg = scrollViewGO.AddComponent<Image>();
            svImg.color = new Color(0, 0, 0, 0);  // transparent
            svImg.raycastTarget = true;
            scrollViewGO.AddComponent<Mask>().showMaskGraphic = false;
            var sr = scrollViewGO.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.scrollSensitivity = 30;

            // Viewport
            var viewportGO = new GameObject("Viewport");
            viewportGO.layer = LayerMask.NameToLayer("UI");
            viewportGO.transform.SetParent(scrollViewGO.transform, false);
            var vpRT = viewportGO.AddComponent<RectTransform>();
            Stretch(vpRT);
            var vpImg = viewportGO.AddComponent<Image>();
            vpImg.color = new Color(0, 0, 0, 0);
            vpImg.raycastTarget = true;
            viewportGO.AddComponent<Mask>().showMaskGraphic = false;
            sr.viewport = vpRT;

            // Content (dynamic shop items go here)
            var contentGO = new GameObject("ShopContent");
            contentGO.layer = LayerMask.NameToLayer("UI");
            contentGO.transform.SetParent(viewportGO.transform, false);
            var contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = new Vector2(0, 1200); // tall enough for all items
            sr.content = contentRT;

            // Tab bar at bottom (상점 | 돼지저금통 | 잠금)
            var tabBar = AddImage(root.transform, "TabBar", new Color(0.08f, 0.10f, 0.22f));
            var tabRT = tabBar;
            tabRT.anchorMin = new Vector2(0, 0);
            tabRT.anchorMax = new Vector2(1, 0.08f);
            tabRT.offsetMin = Vector2.zero;
            tabRT.offsetMax = Vector2.zero;

            // Tab buttons (3개)
            AddText(tabBar.transform, "TabShop", "상점", 18,
                TextAnchor.MiddleCenter, Color.white, V(-200, 0), V(150, 40));
            AddText(tabBar.transform, "TabPiggy", "저금통", 18,
                TextAnchor.MiddleCenter, new Color(0.5f, 0.6f, 0.7f), V(0, 0), V(150, 40));
            AddText(tabBar.transform, "TabLocked", "잠금", 18,
                TextAnchor.MiddleCenter, new Color(0.4f, 0.4f, 0.5f), V(200, 0), V(150, 40));

            // AddComponent + wire
            var comp = root.AddComponent<PopupGoldShop>();
            var so = new SerializedObject(comp);
            so.FindProperty("_closeButton").objectReferenceValue = closeBtnGO.GetComponent<Button>();
            so.FindProperty("_contentRoot").objectReferenceValue = contentGO.transform;
            so.ApplyModifiedProperties();

            SaveAndCleanup(root, POPUP_FOLDER + "/PopupGoldShop.prefab");
        }

        // ═══════════════════════════════════════════
        // Popup/PopupContinue (이어하기)
        // ═══════════════════════════════════════════

        private static void BuildPopupContinue()
        {
            var root = CreateUIRoot("PopupContinue");

            var overlay = AddImage(root.transform, "Overlay", BG_OVERLAY);
            Stretch(overlay);

            var panel = AddImage(root.transform, "ContinuePanel", COL_PANEL);
            SetCenter(panel, V(0, 0), V(700, 600));

            AddText(panel.transform, "ContinueTitle", "Continue?", 42,
                TextAnchor.MiddleCenter, Color.white, V(0, 220), V(500, 70));

            AddText(panel.transform, "CostLabel", "Cost:", 24,
                TextAnchor.MiddleCenter, new Color(0.7f, 0.7f, 0.8f), V(0, 100), V(300, 40));

            var costTextGO = AddText(panel.transform, "CostText", "FREE", 36,
                TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.1f), V(0, 50), V(300, 55));

            var continueBtnGO = AddButton(panel.transform, "ContinueButton", "CONTINUE", COL_PLAY, 28,
                V(0, -60), V(320, 80));

            var declineBtnGO = AddButton(panel.transform, "DeclineButton", "GIVE UP", COL_HOME, 24,
                V(0, -170), V(240, 60));

            // AddComponent + wire
            var comp = root.AddComponent<PopupContinue>();
            var so = new SerializedObject(comp);
            so.FindProperty("_continueButton").objectReferenceValue = continueBtnGO.GetComponent<Button>();
            so.FindProperty("_declineButton").objectReferenceValue = declineBtnGO.GetComponent<Button>();
            so.FindProperty("_costText").objectReferenceValue = costTextGO.GetComponent<Text>();
            so.ApplyModifiedProperties();

            SaveAndCleanup(root, POPUP_FOLDER + "/PopupContinue.prefab");
        }

        // ═══════════════════════════════════════════
        // UI/BoosterTestPanel (테스트용)
        // ═══════════════════════════════════════════

        private static void BuildBoosterTestPanel()
        {
            var root = CreateUIRoot("BoosterTestPanel");

            // Bottom-left anchored panel
            var panelRT = root.GetComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0, 0);
            panelRT.anchorMax = new Vector2(0, 0);
            panelRT.pivot = new Vector2(0, 0);
            panelRT.anchoredPosition = new Vector2(10, 200);
            panelRT.sizeDelta = new Vector2(455, 416);

            var panelBG = AddImage(root.transform, "PanelBG", new Color(0, 0, 0, 0.6f));
            Stretch(panelBG);

            // Label
            AddText(root.transform, "Title", "[Booster Test]", 21,
                TextAnchor.MiddleCenter, Color.yellow, V(0, 182), V(390, 39));

            // 4 booster buttons with count text
            var stBtnGO = AddButton(root.transform, "SelectToolBtn", "SelectTool", new Color(0.2f, 0.5f, 0.9f), 21,
                V(0, 127), V(390, 49));
            var stCountGO = AddText(root.transform, "STCount", "0", 18,
                TextAnchor.MiddleRight, Color.white, V(169, 127), V(52, 39));

            var shBtnGO = AddButton(root.transform, "ShuffleBtn", "Shuffle", new Color(0.8f, 0.5f, 0.1f), 21,
                V(0, 73), V(390, 49));
            var shCountGO = AddText(root.transform, "SHCount", "0", 18,
                TextAnchor.MiddleRight, Color.white, V(169, 73), V(52, 39));

            var crBtnGO = AddButton(root.transform, "ColorRemoveBtn", "ColorRemove", new Color(0.8f, 0.2f, 0.3f), 21,
                V(0, 18), V(390, 49));
            var crCountGO = AddText(root.transform, "CRCount", "0", 18,
                TextAnchor.MiddleRight, Color.white, V(169, 18), V(52, 39));

            var handBtnGO = AddButton(root.transform, "HandBtn", "Hand", new Color(0.7f, 0.3f, 0.7f), 21,
                V(0, -36), V(390, 49));
            var handCountGO = AddText(root.transform, "HACount", "0", 18,
                TextAnchor.MiddleRight, Color.white, V(169, -36), V(52, 39));

            // Color selection panel (initially hidden)
            var colorPanelGO = new GameObject("ColorPanel");
            colorPanelGO.layer = LayerMask.NameToLayer("UI");
            colorPanelGO.transform.SetParent(root.transform, false);
            var cpRT = colorPanelGO.AddComponent<RectTransform>();
            SetCenter(cpRT, V(0, -98), V(390, 59));

            var c0 = AddButton(colorPanelGO.transform, "Color0Btn", "C0", new Color(0.9f, 0.2f, 0.2f), 18,
                V(-150, 0), V(85, 49));
            var c1 = AddButton(colorPanelGO.transform, "Color1Btn", "C1", new Color(0.2f, 0.7f, 0.2f), 18,
                V(-52, 0), V(85, 49));
            var c2 = AddButton(colorPanelGO.transform, "Color2Btn", "C2", new Color(0.2f, 0.4f, 0.9f), 18,
                V(46, 0), V(85, 49));
            var c3 = AddButton(colorPanelGO.transform, "Color3Btn", "C3", new Color(0.9f, 0.8f, 0.1f), 18,
                V(143, 0), V(85, 49));

            // AddComponent + wire
            var comp = root.AddComponent<BoosterTestPanel>();
            var so = new SerializedObject(comp);
            so.FindProperty("_selectToolButton").objectReferenceValue = stBtnGO.GetComponent<Button>();
            so.FindProperty("_shuffleButton").objectReferenceValue = shBtnGO.GetComponent<Button>();
            so.FindProperty("_colorRemoveButton").objectReferenceValue = crBtnGO.GetComponent<Button>();
            so.FindProperty("_handButton").objectReferenceValue = handBtnGO.GetComponent<Button>();
            so.FindProperty("_colorPanel").objectReferenceValue = colorPanelGO;
            so.FindProperty("_color0Button").objectReferenceValue = c0.GetComponent<Button>();
            so.FindProperty("_color1Button").objectReferenceValue = c1.GetComponent<Button>();
            so.FindProperty("_color2Button").objectReferenceValue = c2.GetComponent<Button>();
            so.FindProperty("_color3Button").objectReferenceValue = c3.GetComponent<Button>();
            so.FindProperty("_selectToolCountText").objectReferenceValue = stCountGO.GetComponent<Text>();
            so.FindProperty("_shuffleCountText").objectReferenceValue = shCountGO.GetComponent<Text>();
            so.FindProperty("_colorRemoveCountText").objectReferenceValue = crCountGO.GetComponent<Text>();
            so.FindProperty("_handCountText").objectReferenceValue = handCountGO.GetComponent<Text>();
            so.ApplyModifiedProperties();

            SaveAndCleanup(root, UI_FOLDER + "/BoosterTestPanel.prefab");
        }

        // ═══════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════

        private static GameObject CreateUIRoot(string name)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            var rt = go.AddComponent<RectTransform>();
            Stretch(rt);
            go.AddComponent<CanvasGroup>();
            return go;
        }

        private static RectTransform AddImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = (color.a > 0.3f);
            return rt;
        }

        private static GameObject AddText(Transform parent, string name, string content,
            int fontSize, TextAnchor alignment, Color color, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            SetCenter(rt, pos, size);
            var t = go.AddComponent<Text>();
            t.text = content;
            t.fontSize = fontSize;
            t.alignment = alignment;
            t.color = color;
            t.font = _font;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return go;
        }

        private static GameObject AddButton(Transform parent, string name, string label,
            Color bgColor, int fontSize, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            SetCenter(rt, pos, size);
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            go.AddComponent<Button>();

            var textGO = new GameObject("Label");
            textGO.layer = LayerMask.NameToLayer("UI");
            textGO.transform.SetParent(go.transform, false);
            var trt = textGO.AddComponent<RectTransform>();
            Stretch(trt);
            var t = textGO.AddComponent<Text>();
            t.text = label;
            t.fontSize = fontSize;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.font = _font;
            t.fontStyle = FontStyle.Bold;
            t.raycastTarget = false;

            return go;
        }

        private static void SaveAndCleanup(GameObject go, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            Debug.Log($"[UIPrefabBuilder] Created {path}");
        }

        private static Vector2 V(float x, float y) => new Vector2(x, y);

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void SetCenter(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void SetTopStretch(RectTransform rt, float height)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(0, -height);
            rt.offsetMax = Vector2.zero;
        }

        private static void AnchorLeft(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0.5f);
            rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
        }

        private static void AnchorRight(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(1, 0.5f);
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string[] parts = path.Split('/');
                string current = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(current, parts[i]);
                    current = next;
                }
            }
        }
    }
}
#endif
