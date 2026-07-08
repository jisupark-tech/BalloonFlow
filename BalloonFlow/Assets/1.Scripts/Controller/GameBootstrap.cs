using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace BalloonFlow
{
    /// <summary>
    /// InGame 씬 컨트롤러.
    /// - GameManager.InitInGame() → InGame 매니저 생성 (GameManager 자식)
    /// - UIManager.OpenUI로 UIHud, PopupResult, PopupSettings, PopupGoldShop 로드
    /// - 레벨 로드, 결과 팝업, Retry/Next/Home 처리
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        private UIHud _hud;
        private PopupResult _result;
        private PopupContinue _continuePopup;
        private PopupSettings _settings;
        private PopupGoldShop _goldShop;
        private GameObject _finishLogoPrefab;
        private bool _pendingResultIsWin;
        private bool _isTestMode;
        // OnLevelLoaded 다중 발화 race 차단 — yield 이전 동기 latch 필수.
        private bool _ftueInfiniteHeartsRequested;

        private const string PREFS_KEY_UNLOCK_POPUP_SHOWN = "BalloonFlow_BoosterUnlockPopupShown_";
        private const float FINISH_LOGO_HOLD_SECONDS = 1.7f;
        // 온보딩(Lv.1~4) 자동 진행 경로에서 PopupResult를 띄우지 않고 FinishLogo만 1.7초간 노출 후 자동 종료
        private const float FINISH_LOGO_ONBOARDING_HOLD_SECONDS = 1.7f;
        private const int SPINE_LOGO_SORTING_ORDER = 10;
        private const int FINISH_LOGO_PARTICLE_SORTING_ORDER = 0;

        void Start()
        {
            // Detect test mode from MapMaker
            #if UNITY_EDITOR
            _isTestMode = UnityEditor.EditorPrefs.GetBool("BalloonFlow_UseTestLevel", false)
                          || GameManager.IsTestPlayMode;
            #else
            _isTestMode = GameManager.IsTestPlayMode;
            #endif

            // Ensure core singletons exist (may be missing if MapMaker → InGame directly)
            EnsureCoreSingletons();

            // Lobby 매니저 확보 (Lobby 안 거쳤을 때를 위해)
            // Test mode에서도 LevelManager가 필요하므로 InitLobby 호출
            GameManager.Instance.InitLobby();

            // EventSystem 확인
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var _go = new GameObject("EventSystem");
                _go.AddComponent<EventSystem>();
                _go.AddComponent<InputSystemUIInputModule>();
            }

            // 씬 캔버스 등록
            EnsureSceneCanvas();

            // 직전 씬(Lobby/Title) 의 UI/Popup 제거 — InGame 전용만 새로 로드
            if (UIManager.HasInstance) UIManager.Instance.DestroyAllUI();
            if (PopupManager.HasInstance) PopupManager.Instance.UnregisterAll();

            // TutorialManager opens/binds Popup/Tutorial during Awake, so create it after stale UI cleanup.
            GameManager.Instance.InitInGame();

            // 카메라 설정
            if (CameraManager.HasInstance)
                CameraManager.Instance.ConfigureInGame();

            // UI 로드
            LoadUI();

            // 레벨 로드
            LoadPendingLevel();

            // [#5/12] FTUE 무한 하트 24h (UX플로우 §3-2·§3-3): 첫 Lv.1 진입 시각 기준 24h, 평생 1회.
            // 신규 uid 발급(UserData.CreateNewUser) = 트리거 기준. 부여 트리거는 HandleLevelLoaded
            // (인게임 로딩 완료 + 페이드 종료 이후) — Start() 시점엔 LevelManager/UIManager 로딩 중일 수 있어
            // 화면이 비어 있는 상태에서 무한 하트 UI 가 노출되는 플릭커 + 서버 doc 미준비 케이스를 회피.
            // WHY: 부스터 언락 팝업 트리거는 HandleLevelLoaded 에서 처리.
            // Start() 만으로 트리거하면 같은 씬 안 Next/Retry 로 9/12/15 진입 시 미발화 → 사용자가 영원히 못 봄.

            // 레벨 로드 후 난이도별 배경색 적용
            if (CameraManager.HasInstance)
                CameraManager.Instance.ConfigureInGame();

            if (AudioManager.HasInstance)
                AudioManager.Instance.PlayInGameBGM();

            Debug.Log($"[GameBootstrap] InGame 초기화 완료 (testMode={_isTestMode})");
        }

#if UNITY_EDITOR
        // 테스트 플레이 중 F6 → MapMaker 에디터로 즉시 복귀. (F5 는 에디터→테스트 진입용이라 분리)
        void Update()
        {
            if (!_isTestMode) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb[Key.F6].wasPressedThisFrame)
                ReturnToMapMaker();
        }

        private void ReturnToMapMaker()
        {
            GameManager.IsTestPlayMode = false;
            if (GameManager.HasInstance)
                GameManager.Instance.GoToMapMaker();
            else
                UnityEngine.SceneManagement.SceneManager.LoadScene(GameManager.SCENE_MAPMAKER);
        }
#endif

        /// <summary>
        /// Ensures GameManager, UIManager, CameraManager exist.
        /// Required when entering InGame directly from MapMaker without passing through Title/Lobby.
        /// </summary>
        void EnsureCoreSingletons()
        {
            // GameManager
            if (!GameManager.HasInstance)
            {
                var _go = new GameObject("GameManager");
                _go.AddComponent<GameManager>();
            }

            // ObjectPoolManager (required for balloon & dart spawning)
            if (!ObjectPoolManager.HasInstance)
            {
                var _go = new GameObject("ObjectPoolManager");
                _go.AddComponent<ObjectPoolManager>();
                Debug.Log("[GameBootstrap] Created ObjectPoolManager (was missing — test mode or direct scene load)");
            }

            // ResourceManager
            if (!ResourceManager.HasInstance)
            {
                var _go = new GameObject("ResourceManager");
                _go.AddComponent<ResourceManager>();
                Debug.Log("[GameBootstrap] Created ResourceManager (was missing — test mode or direct scene load)");
            }

            // GimmickProcessor (SceneSingleton) — Surprise/Hidden·Color Curtain 공개, Pin/Barricade 타격,
            // 다트 blocker 가 이 매니저에 의존. InGame 씬에 없으면 RegisterBalloonGimmick/OnBalloonPopped 구독이
            // 전부 스킵되어 공개가 작동 안 함. SetupBalloons(Surprise 등록) 전에 보장돼야 하므로 여기서 생성.
            if (!GimmickProcessor.HasInstance)
            {
                var _go = new GameObject("GimmickProcessor");
                _go.AddComponent<GimmickProcessor>();
                Debug.Log("[GameBootstrap] Created GimmickProcessor (was missing — Surprise/Curtain reveal depends on it)");
            }

            // UIManager (required for all UI: HUD, popups, fade transitions)
            if (!UIManager.HasInstance)
            {
                var _go = new GameObject("Mgr_UI");
                _go.AddComponent<UIManager>();
                Debug.Log("[GameBootstrap] Created UIManager (was missing — test mode or direct scene load)");
            }

            // CameraManager (wraps Main Camera for scene-specific config + shake)
            if (!CameraManager.HasInstance)
            {
                var _go = new GameObject("Mgr_Camera");
                var _cmgr = _go.AddComponent<CameraManager>();
                Camera _mainCam = Camera.main;
                if (_mainCam == null)
                {
                    // No camera in scene (e.g. MapMaker → InGame direct) — create one
                    var _camGO = new GameObject("Main Camera");
                    _camGO.tag = "MainCamera";
                    _mainCam = _camGO.AddComponent<Camera>();
                    _camGO.AddComponent<AudioListener>();
                    Debug.Log("[GameBootstrap] Created Main Camera (no camera in InGame scene)");
                }
                _cmgr.MainCamera = _mainCam;
                Debug.Log("[GameBootstrap] Created CameraManager (was missing — test mode or direct scene load)");
            }
            else
            {
                // CameraManager exists but camera reference may be lost after scene transition
                CameraManager.Instance.RefreshMainCamera();
                if (CameraManager.Instance.MainCamera == null)
                {
                    var _camGO = new GameObject("Main Camera");
                    _camGO.tag = "MainCamera";
                    var _cam = _camGO.AddComponent<Camera>();
                    _camGO.AddComponent<AudioListener>();
                    CameraManager.Instance.MainCamera = _cam;
                    Debug.Log("[GameBootstrap] Re-created Main Camera (reference lost after scene transition)");
                }
            }
        }

        /// <summary>
        /// UIManager 가 이미 캔버스를 가지고 있으면 (Title/Lobby 에서 등록된 후 DontDestroyOnLoad 으로 유지) 재사용.
        /// 그 외 경우(Editor 직접 InGame Play 등) 에만 새로 생성.
        /// </summary>
        void EnsureSceneCanvas()
        {
            if (!UIManager.HasInstance) return;

            // 이미 살아있는 캔버스가 있으면 그대로 사용 — InGame 진입 때 추가 캔버스 생성 안 함
            if (UIManager.Instance.HasLiveSceneCanvas)
            {
                EnsureEventSystem();
                return;
            }

            // Fallback: Editor 에서 InGame 직접 Play 한 경우 등 — 기존 동작 유지
            var _canvasGO = GameObject.Find("SceneCanvas");
            if (_canvasGO == null)
            {
                _canvasGO = new GameObject("SceneCanvas");
                var _canvas = _canvasGO.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 0;
                var _scaler = _canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
                _scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                _scaler.referenceResolution = new Vector2(1242f, 2688f);
                _scaler.matchWidthOrHeight = 0.5f;
                _canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                Debug.Log("[GameBootstrap] Created SceneCanvas (fallback — UIManager 미보유)");
            }

            var _popupCanvasGO = GameObject.Find("PopupCanvas");
            if (_popupCanvasGO == null)
            {
                _popupCanvasGO = new GameObject("PopupCanvas");
                var _popupCanvas = _popupCanvasGO.AddComponent<Canvas>();
                _popupCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _popupCanvas.sortingOrder = 10;
                var _popupScaler = _popupCanvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
                _popupScaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                _popupScaler.referenceResolution = new Vector2(1242f, 2688f);
                _popupScaler.matchWidthOrHeight = 0.5f;
                _popupCanvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }

            var _effectCanvasGO = GameObject.Find("EffectCanvas");
            if (_effectCanvasGO == null)
            {
                _effectCanvasGO = new GameObject("EffectCanvas");
                var _effectCanvas = _effectCanvasGO.AddComponent<Canvas>();
                _effectCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _effectCanvas.sortingOrder = 15;
                var _effectScaler = _effectCanvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
                _effectScaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                _effectScaler.referenceResolution = new Vector2(1242f, 2688f);
                _effectScaler.matchWidthOrHeight = 0.5f;
            }

            UIManager.Instance.SetSceneCanvas(_canvasGO.transform, _popupCanvasGO.transform, _effectCanvasGO.transform);
            EnsureEventSystem();
        }

        static void EnsureEventSystem()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null) return;
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            Debug.Log("[GameBootstrap] Created EventSystem (was missing)");
        }

        /// <summary>이벤트 구독·해제</summary>
        void OnEnable()
        {
            EventBus.Subscribe<OnLevelCompleted>(HandleLevelCompleted);
            EventBus.Subscribe<OnLevelFailed>(HandleLevelFailed);
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
        }

        /// <summary>이벤트 구독·해제</summary>
        void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelCompleted>(HandleLevelCompleted);
            EventBus.Unsubscribe<OnLevelFailed>(HandleLevelFailed);
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);

            if (_result != null)
            {
                _result.RemoveNextButtonListener(OnNextClicked);
                if (_result.RetryButton != null) _result.RetryButton.onClick.RemoveListener(OnRetryClicked);
                if (_result.HomeButton != null) _result.HomeButton.onClick.RemoveListener(OnHomeClicked);
            }

            if (_continuePopup != null)
            {
                if (_continuePopup.ContinueButton != null)
                    _continuePopup.ContinueButton.onClick.RemoveListener(_continuePopup.OnContinueClicked);
                if (_continuePopup.DeclineButton != null)
                    _continuePopup.DeclineButton.onClick.RemoveListener(_continuePopup.OnDeclineClicked);
            }
        }

        #region UI 로드

        /// <summary>InGame 진입 시 HUD/Result/Continue/Fail/Settings/GoldShop/Quit 등 UI·팝업을 로드하고 버튼 리스너와 HUDController 바인딩을 설정한다.</summary>
        void LoadUI()
        {
            if (!UIManager.HasInstance) return;

            // UIHud
            _hud = UIManager.Instance.OpenUI<UIHud>("UI/UIHud");

            // [2026-05-13] 로비→인게임 진입 시 UI 화면 밖에서 슬라이드 인 연출 —
            // OpenUI 직후, 어떤 RectTransform 레이아웃/렌더 패스도 발생하기 전에 시작 위치를 즉시 강제 세팅해야
            // 초기 위치(HUD_Top -60 / BottomPanel 0)가 1프레임 노출되는 플릭커를 차단한다.
            // tween 시작은 HandleLevelLoaded → PlayHudEnterAnimationDeferred 코루틴이 IsLoading/IsFading 완료 후 트리거.
            if (_hud != null) _hud.PrimeIngameEnterStartPos();

            // HUDController 바인딩
            if (HUDController.HasInstance && _hud != null)
            {
                HUDController.Instance.BindView(_hud);
            }

            // PopupResult (로드 후 숨김) — Silent: HUD popup-tween 발사 안 함 (인게임 진입 슬라이드 연출과 충돌 차단)
            _result = UIManager.Instance.OpenUISilent<PopupResult>("Popup/PopupResult");
            if (_result != null)
            {
                _result.CloseUISilent();
                _result.SetNextButtonListener(OnNextClicked);
                if (_result.RetryButton != null) _result.RetryButton.onClick.AddListener(OnRetryClicked);
                if (_result.HomeButton != null) _result.HomeButton.onClick.AddListener(OnHomeClicked);

                // Wire gold target for coin fly effect
                if (_result.GoldTarget == null && _hud != null && _hud.GoldText != null)
                {
                    _result.SetGoldTarget(_hud.GoldText.rectTransform);
                }
            }

            // PopupContinue (실패 흐름 두 번째)
            _continuePopup = UIManager.Instance.OpenUISilent<PopupContinue>("Popup/PopupContinue");
            if (_continuePopup != null)
            {
                // CanvasGroup 보장
                var cgCont = _continuePopup.GetComponent<CanvasGroup>();
                if (cgCont == null) cgCont = _continuePopup.gameObject.AddComponent<CanvasGroup>();
                _continuePopup.CloseUISilent();

                if (_continuePopup.ContinueButton != null)
                    _continuePopup.ContinueButton.onClick.AddListener(_continuePopup.OnContinueClicked);
                if (_continuePopup.DeclineButton != null)
                    _continuePopup.DeclineButton.onClick.AddListener(_continuePopup.OnDeclineClicked);

                if (PopupManager.HasInstance)
                    PopupManager.Instance.RegisterPopup("popup_continue", cgCont);
            }

            // PopupFail01 (실패 흐름 첫 번째: Continue/Decline + 난이도프레임)
            var _fail01 = UIManager.Instance.OpenUISilent<PopupFail01>("Popup/PopupFail01");
            if (_fail01 != null)
            {
                // CanvasGroup 보장 (CloseUI가 사용하므로 먼저 추가)
                var cg01 = _fail01.GetComponent<CanvasGroup>();
                if (cg01 == null) cg01 = _fail01.gameObject.AddComponent<CanvasGroup>();
                _fail01.CloseUISilent();
                if (PopupManager.HasInstance)
                    PopupManager.Instance.RegisterPopup("popup_fail01", cg01);
            }

            // PopupFail02 (실패 흐름 마지막: Retry/Home)
            var _fail02 = UIManager.Instance.OpenUISilent<PopupFail02>("Popup/PopupFail02");
            if (_fail02 != null)
            {
                var cg02 = _fail02.GetComponent<CanvasGroup>();
                if (cg02 == null) cg02 = _fail02.gameObject.AddComponent<CanvasGroup>();
                _fail02.CloseUISilent();
                if (PopupManager.HasInstance)
                    PopupManager.Instance.RegisterPopup("popup_fail02", cg02);
            }

            // PopupSettings (로드 후 숨김)
            _settings = UIManager.Instance.OpenUISilent<PopupSettings>("Popup/PopupSettings");
            if (_settings != null) _settings.CloseUISilent();

            // PopupGoldShop (로드 후 숨김)
            _goldShop = UIManager.Instance.OpenUISilent<PopupGoldShop>("Popup/PopupGoldShop");
            if (_goldShop != null) _goldShop.CloseUISilent();

            // PopupQuit (로드 후 숨김)
            var _quitPopup = UIManager.Instance.OpenUISilent<PopupQuit>("Popup/PopupQuit");
            if (_quitPopup != null) _quitPopup.CloseUISilent();

            // BoosterTestPanel 삭제됨 — 부스터 기능은 UIHud의 ItemBtn으로 이전

            // HUDController에 팝업 연결
            if (HUDController.HasInstance)
            {
                HUDController.Instance.SetSettingsPopup(_settings);
                HUDController.Instance.SetGoldShopPopup(_goldShop);
                HUDController.Instance.SetQuitPopup(_quitPopup);
            }

            // FinishLogo 프리팹 사전 로드 — Win 결과 직전 1.7초 컨페티 연출용. 런타임 hitching 방지.
            _finishLogoPrefab = Resources.Load<GameObject>(Const.POPUP_FINISH_LOGO);
        }


        /// <summary>
        /// [#5/12] FTUE 무한 하트 24h 부여 (UX플로우 §3-2·§3-3).
        /// 트리거 기준: 신규 uid 발급(UserData.CreateNewUser) = ftueInfiniteHeartsPending=true.
        /// 부여 시점: 첫 Lv.1 인게임 로딩 완료(LevelManager.IsLoading=false + UIManager.IsFading=false) 직후, 평생 1회.
        /// 진실 소스: Firestore(_user.ftueInfiniteHeartsPending). PlayerPrefs 는 Firestore 미준비 시 fallback 보조 가드.
        /// </summary>
        private const string PREFS_FTUE_INFINITE_HEARTS = "BF_FtueInfiniteHeartsGranted";
        private const float  FTUE_INFINITE_HEARTS_SECONDS = 24f * 60f * 60f; // 24h

        /// <summary>레거시 진입점 — 본문은 새 deferred 코루틴과 동일하게 단순화. 외부 호출 안전.
        /// 직접 호출하지 말 것 (HandleLevelLoaded → TryGrantFtueInfiniteHeartsDeferred 가 정식 경로).</summary>
        void TryGrantFtueInfiniteHearts()
        {
            if (_ftueInfiniteHeartsRequested) return;
            if (!CanGrantFtueInfiniteHearts(levelId: 1)) return;
            _ftueInfiniteHeartsRequested = true;
            GrantFtueInfiniteHeartsOnce();
        }

        /// <summary>
        /// 인게임 로딩/페이드 완료 후 FTUE 무한 하트 부여 (ShowBoosterUnlockPopupDeferred 와 동일 패턴).
        /// 평생 1회 가드는 Firestore(_user.ftueInfiniteHeartsPending) 기준. PlayerPrefs 는 fallback.
        /// </summary>
        IEnumerator TryGrantFtueInfiniteHeartsDeferred(int levelId)
        {
            // 안전판: latch 없이 외부에서 직접 코루틴을 띄운 경우 차단(정식 경로는 HandleLevelLoaded 동기 latch 통과).
            if (!_ftueInfiniteHeartsRequested) yield break;
            yield return null;
            while (LevelManager.HasInstance && LevelManager.Instance.IsLoading) yield return null;
            while (UIManager.HasInstance && UIManager.Instance.IsFading) yield return null;
            if (!CanGrantFtueInfiniteHearts(levelId)) yield break;
            GrantFtueInfiniteHeartsOnce();
        }

        /// <summary>FTUE 무한 하트 부여 조건 가드. 서버 doc(ftueInfiniteHeartsPending) = 진실, 보조 가드는 중복 방지용.</summary>
        bool CanGrantFtueInfiniteHearts(int levelId)
        {
            if (_isTestMode) return false;
            if (levelId != 1) return false;
            if (!LifeManager.HasInstance) return false;

            // 서버 진실: Firestore /users/{uid}.ftueInfiniteHeartsPending
            if (!UserDataService.HasInstance) return false;
            var uds = UserDataService.Instance;
            if (!uds.IsReady || uds.CurrentUser == null) return false;
            if (!uds.CurrentUser.ftueInfiniteHeartsPending) return false;

            // 보조 가드 — 진행한 유저 소급 차단(서버 pending=true 와 모순 시 안전판).
            if (FtueGate.HighestClearedLevel > 0) return false;

            // 보조 가드 — PlayerPrefs 로컬 중복 방지(Firestore write 지연 사이 재호출 흡수).
            if (PlayerPrefs.GetInt(PREFS_FTUE_INFINITE_HEARTS, 0) != 0) return false;

            return true;
        }

        void GrantFtueInfiniteHeartsOnce()
        {
            LifeManager.Instance.ActivateInfiniteHearts(FTUE_INFINITE_HEARTS_SECONDS);
            if (UserDataService.HasInstance)
                UserDataService.Instance.MarkFtueInfiniteHeartsGranted();
            PlayerPrefs.SetInt(PREFS_FTUE_INFINITE_HEARTS, 1);
            PlayerPrefs.Save();
            Debug.Log("[GameBootstrap] FTUE 무한 하트 24h 부여 (Lv.1 인게임 로딩 완료 / 평생 1회 / 신규 uid 기준)");
        }

        /// <summary>PlayerPrefs("BF_PendingLevelId") 또는 최고 클리어 레벨+1을 기준으로 LevelManager에 레벨을 로드하고 해당 levelId를 반환한다.</summary>
        int LoadPendingLevel()
        {
            // 이전 InGame 잔여 풀 오브젝트 정리 (Lobby → InGame 경로에서 오염 방지)
            if (ObjectPoolManager.HasInstance)
                ObjectPoolManager.Instance.ReturnAllPools();

            int _levelId = PlayerPrefs.GetInt("BF_PendingLevelId", 0);
            if (_levelId <= 0)
            {
                if (LevelManager.HasInstance)
                {
                    int _highest = LevelManager.Instance.GetHighestCompletedLevel();
                    _levelId = _highest > 0 ? _highest + 1 : 1;
                }
                else
                {
                    _levelId = 1;
                }
            }
            PlayerPrefs.DeleteKey("BF_PendingLevelId");

            if (LevelManager.HasInstance)
                LevelManager.Instance.LoadLevel(_levelId);

            return _levelId;
        }

        /// <summary>레벨 로딩·페이드가 끝난 다음 프레임에 부스터 언락 팝업을 표시해, 로딩 오버레이 뒤에 가려졌다가 갑자기 나타나는 문제를 차단한다.</summary>
        IEnumerator ShowBoosterUnlockPopupDeferred(int levelId)
        {
            yield return null;
            while (LevelManager.HasInstance && LevelManager.Instance.IsLoading) yield return null;
            while (UIManager.HasInstance && UIManager.Instance.IsFading) yield return null;
            TryShowBoosterUnlockPopup(levelId);
        }

        /// <summary>특정 레벨(9/12/15)에서 최초 1회에 한해 HAND/SHUFFLE/COLOR_REMOVE 부스터 언락 팝업을 표시한다.</summary>
        void TryShowBoosterUnlockPopup(int levelId)
        {
            if (_isTestMode) return;
            if (!UIManager.HasInstance || !BoosterManager.HasInstance) return;

            string boosterType = levelId switch
            {
                9  => BoosterManager.HAND,
                12 => BoosterManager.SHUFFLE,
                15 => BoosterManager.COLOR_REMOVE,
                _  => null
            };
            if (boosterType == null) return;

            string shownKey = PREFS_KEY_UNLOCK_POPUP_SHOWN + boosterType;
            if (BoosterManager.Instance.IsUnlockRewardClaimed(boosterType)) return;

            var popup = UIManager.Instance.OpenUI<PopupBuyItem>("Popup/PopupBuyItem");
            if (popup == null) return;
            Sprite spr = popup.GetBoosterSprite(boosterType);
            // ROLLBACK_UNLOCK_POPUP_TITLE_TEXTDATA_20260623: 해금 팝업 타이틀 = TextData "Item Unlocked!".
            popup.ShowUnlock(LocalizationService.Get("popup.txttitle.itemunlocked"), spr, levelId, $"x{BoosterManager.UNLOCK_REWARD_COUNT}",
                onConfirm: () =>
                {
                    // ROLLBACK_ITEM_ACQUIRE_INPUT_LOCK_20260708: Claim 확정 → 튜토("Tap your item!") 시작까지
                    //   전역 입력 잠금(UI 레이캐스트/홀더 터치/백버튼). 해제는 StartTutorial 및 실패 경로들이 담당.
                    TutorialController.BeginItemAcquisitionInputLock();

                    void ClaimAfterFx()
                    {
                        if (BoosterManager.Instance.TryClaimUnlockReward(boosterType))
                        {
                            if (_hud != null)
                            {
                                _hud.RefreshBoosterCounts();
                                _hud.RefreshLockState();
                            }
                        }

                        PlayerPrefs.SetInt(shownKey, 1);
                        PlayerPrefs.Save();

                        // ROLLBACK_TUTORIAL_START_AFTER_UNLOCK_20260623:
                        // Level-entry booster unlock must be "Claim -> HUD reward fly -> Tutorial".
                        // Tutorial Editor can mark the sequence as Manual Trigger Only, so trigger it
                        // here after the reward has actually landed in UIHud.
                        bool tutorialStarted = TutorialController.HasInstance && LevelManager.HasInstance
                            && TutorialController.Instance.StartTutorialForLevel(LevelManager.Instance.CurrentLevelId);
                        if (!tutorialStarted)
                            TutorialController.EndItemAcquisitionInputLock(); // ROLLBACK_ITEM_ACQUIRE_INPUT_LOCK_20260708
                    }

                    if (_hud != null)
                        _hud.PlayBoosterRewardFly(boosterType, BoosterManager.UNLOCK_REWARD_COUNT, spr, ClaimAfterFx);
                    else
                        ClaimAfterFx();
                });
        }

        #endregion

        #region 게임 결과 처리

        /// <summary>
        /// 모든 레벨 로드 이벤트 수신 시 HUD_Top(160→-60) + BottomPanel(-300→0) 슬라이드-인 연출 트리거.
        /// 첫 진입/Next/Retry/Continue 경로 일관 보장.
        /// </summary>
        // WHY: 모든 스테이지 진입(첫 진입/Next/Retry/Continue)에서 HUD_Top 160→-60, BottomPanel -300→0 슬라이드-인 연출 일관 트리거. 로딩 오버레이(LevelManager.IsLoading) + 페이드(UIManager.IsFading) 완료 후 tween 시작 — 화면 가려진 상태에서 연출 소비 방지.
        void HandleLevelLoaded(OnLevelLoaded _evt)
        {
            _pendingResultIsWin = false;
            StartCoroutine(PlayHudEnterAnimationDeferred());
            // 첫 진입 + Next/Retry/Continue 경로 일관 — Start() 단발 트리거였을 때 같은 씬 안 레벨 전환 9/12/15 미발화 버그 차단.
            StartCoroutine(ShowBoosterUnlockPopupDeferred(_evt.levelId));
            // FTUE 무한 하트 24h — 서버 pending 플래그 + Lv.1 로딩 완료 후 평생 1회 부여.
            // OnLevelLoaded 다중 발화 race 차단 — yield 이전 동기 latch 필수(같은 프레임 N개 이벤트 중 1번만 코루틴 진입).
            if (_evt.levelId == 1 && !_ftueInfiniteHeartsRequested && CanGrantFtueInfiniteHearts(_evt.levelId))
            {
                _ftueInfiniteHeartsRequested = true;
                StartCoroutine(TryGrantFtueInfiniteHeartsDeferred(_evt.levelId));
            }
        }

        /// <summary>로딩/페이드 완료 후 HUD 슬라이드-인 연출 트리거. ShowBoosterUnlockPopupDeferred 와 동일 패턴.</summary>
        IEnumerator PlayHudEnterAnimationDeferred()
        {
            yield return null;
            while (LevelManager.HasInstance && LevelManager.Instance.IsLoading) yield return null;
            while (UIManager.HasInstance && UIManager.Instance.IsFading) yield return null;
            if (_hud != null) _hud.PlayIngameEnterAnimation();
        }

        /// <summary>레벨 완료 이벤트를 받아 win 결과 팝업 표시 코루틴을 시작한다.</summary>
        void HandleLevelCompleted(OnLevelCompleted _evt)
        {
            _pendingResultIsWin = true;

            // [#5/12] 온보딩 강제 집중 (UX플로우 §3-3·§5-4): Lv.1~4 클리어는 클리어 팝업 생략 + 자동 다음 레벨.
            // 코인 보상은 CurrencyManager.HandleLevelCompleted 가 백그라운드로 이미 지급(토스트 없음).
            // Lv.5 클리어부터 정상 클리어 팝업 노출 (온보딩 종료 신호). 테스트 모드는 예외.
            if (!_isTestMode && _evt.levelId < FtueGate.ONBOARDING_CLEAR_LEVEL)
            {
                StartCoroutine(AutoAdvanceAfterClear());
                return;
            }

            StartCoroutine(ShowResultDelayed(true, _evt.score, _evt.starCount));
        }

        /// <summary>[#5/12] Lv.1~4 온보딩 — 클리어 팝업 없이 칭찬 연출 후 자동으로 다음 레벨 진입.</summary>
        IEnumerator AutoAdvanceAfterClear()
        {
            if (_hud != null) _hud.PlayStageEndPanelShift();
            // 칭찬 문구/클리어 FX 가 짧게 노출되도록 결과 팝업과 동일한 지연 유지.
            yield return new WaitForSecondsRealtime(0.8f);

            if (_finishLogoPrefab != null)
                yield return StartCoroutine(PlayFinishLogoSequence(FINISH_LOGO_ONBOARDING_HOLD_SECONDS));

            _pendingResultIsWin = false;
            if (_hud != null) _hud.OpenUI();

            if (LevelManager.HasInstance)
            {
                int _next = LevelManager.Instance.GetNextLevelId();
                int _current = LevelManager.Instance.CurrentLevelId;
                if (_next > _current) LevelManager.Instance.LoadLevel(_next);
            }
        }

        /// <summary>Continue 소진·거절 이후 발생하는 최종 실패 이벤트를 받아 fail 결과 팝업 표시 코루틴을 시작한다.</summary>
        void HandleLevelFailed(OnLevelFailed _evt)
        {
            // OnLevelFailed now only fires after continues are exhausted or declined.
            // LevelManager defers FailLevel when continues are available.
            if (_pendingResultIsWin) return;
            StartCoroutine(ShowResultDelayed(false, 0, 0));
        }

        /// <summary>스테이지 종료 시 HUD 패널 시프트 후 0.8초 뒤 win/fail 결과 팝업을 표시한다.
        /// Win 경로는 PopupResult 직전 FinishLogo 프리팹을 띄워 1.7초간 FxConfetti를 재생 후 종료.</summary>
        IEnumerator ShowResultDelayed(bool _isWin, int _score, int _stars)
        {
            // [2026-05-13] 스테이지 종료 시 popup 노출 전 panel shift — popup open 자체 트리거는 latch로 차단, 다음 스테이지 enter 애니에서 원위치로 복귀.
            if (_hud != null) _hud.PlayStageEndPanelShift();
            yield return new WaitForSecondsRealtime(0.8f);

            if (_isWin && _finishLogoPrefab != null)
                yield return StartCoroutine(PlayFinishLogoSequence(FINISH_LOGO_HOLD_SECONDS));

            if (_result != null)
            {
                if (_isWin)
                {
                    DifficultyPurpose diff = DifficultyPurpose.Normal;
                    if (LevelManager.HasInstance)
                        diff = LevelManager.Instance.GetLevelDifficulty(LevelManager.Instance.CurrentLevelId);
                    _result.ShowWin(_score, _stars, diff);
                }
                else _result.ShowFail();
            }
        }

        IEnumerator PlayFinishLogoSequence(float _holdSeconds)
        {
            Transform _parent = null;
            if (UIManager.HasInstance)
                _parent = UIManager.Instance.EffectTr != null ? UIManager.Instance.EffectTr
                        : (UIManager.Instance.PopupTr != null ? UIManager.Instance.PopupTr
                                                              : UIManager.Instance.UiTr);
            if (_parent == null) _parent = transform;

            // [2026-06-23 사용자 추가지시 revert] FinishLogo 표시 구간 SE 화이트리스트 락 시작 +
            // Stage_Result_Firework(loop)만 시작. Stage_Result 1-shot 은 PopupResult.ShowWin 오픈 시점으로 분리(사용자 지시 2026-06-23 task 코멘트).
            // FinishLogo 구간으로 Stage_Result 를 이동했던 직전 변경(PR #378)을 사용자 지시로 revert — design supersede 근거: 본 태스크 코멘트(2026-06-23).
            if (AudioManager.HasInstance)
            {
                AudioManager.Instance.BeginResultIntroSfxLock();
                AudioManager.Instance.PlayStageResultFireworkLoop();
                // [2026-06-23 사용자 피드백] congratuation 1-shot — _finishLogoSfxSource 전용 채널 사용이라 직전 BeginResultIntroSfxLock 의 StopAllSfx 및 PlaySFX 화이트리스트 게이트 통과 안 받음. Firework 루프와 동시 시작.
                AudioManager.Instance.PlayFinishLogoCongratuation();
            }
            GameObject _logoGO = Instantiate(_finishLogoPrefab, _parent, false);
            var _rt = _logoGO.transform as RectTransform;
            if (_rt != null)
            {
                _rt.anchoredPosition = Vector2.zero;
                _rt.localScale = Vector3.one;
                _rt.localRotation = Quaternion.identity;
            }
            else
            {
                _logoGO.transform.localPosition = Vector3.zero;
                _logoGO.transform.localScale = Vector3.one;
                _logoGO.transform.localRotation = Quaternion.identity;
            }

            // SpineLogo는 항상 파티클 위에 — 사용자 요구사항.
            Transform _spineLogoTr = null;
            var _allChildren = _logoGO.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < _allChildren.Length; i++)
            {
                if (_allChildren[i] != null && _allChildren[i].name == "SpineLogo") { _spineLogoTr = _allChildren[i]; break; }
            }
            if (_spineLogoTr == null)
            {
                for (int i = 0; i < _allChildren.Length; i++)
                {
                    if (_allChildren[i] == null) continue;
                    string _n = _allChildren[i].name;
                    if (_n.Contains("Spine") || _n.Contains("Logo")) { _spineLogoTr = _allChildren[i]; break; }
                }
            }
            if (_spineLogoTr != null)
            {
                _spineLogoTr.SetAsLastSibling();
                var _spineCanvas = _spineLogoTr.GetComponent<Canvas>();
                if (_spineCanvas != null) { _spineCanvas.overrideSorting = true; _spineCanvas.sortingOrder = SPINE_LOGO_SORTING_ORDER; }
            }
            else Debug.LogWarning("[GameBootstrap] FinishLogo prefab에 SpineLogo 자식이 없습니다 — 정렬 스킵.");

            var _particles = _logoGO.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < _particles.Length; i++)
            {
                if (_particles[i] == null) continue;
                var _psr = _particles[i].GetComponent<ParticleSystemRenderer>();
                if (_psr != null) _psr.sortingOrder = FINISH_LOGO_PARTICLE_SORTING_ORDER;
                _particles[i].gameObject.SetActive(true);
                _particles[i].Play(true);
            }

            yield return new WaitForSecondsRealtime(_holdSeconds);

            // SFX loop stop — 사용자 지시 2026-06-23 (task #370 comment). FinishLogo 종료 시 정지.
            if (AudioManager.HasInstance) AudioManager.Instance.StopStageResultFirework();
            if (_logoGO != null) Destroy(_logoGO);
        }

        #endregion

        #region 버튼 이벤트

        /// <summary>결과 팝업의 Next 버튼 처리 — 테스트 모드에서는 동일 레벨 재시작, 일반 모드에서는 다음 레벨 로드.</summary>
        void OnNextClicked()
        {
            _pendingResultIsWin = false;
            if (_result != null) _result.CloseUI();
            if (_hud != null) _hud.OpenUI();

            if (_isTestMode)
            {
                // In test mode, "Next" replays the same level (no progression)
                if (LevelManager.HasInstance)
                    LevelManager.Instance.RetryLevel();
                return;
            }

            if (LevelManager.HasInstance)
            {
                int _current = LevelManager.Instance.CurrentLevelId;
                int _next = LevelManager.Instance.GetNextLevelId();
                if (_next <= _current)
                {
                    if (UIManager.HasInstance)
                    {
                        var popup = UIManager.Instance.OpenUI<PopupDescription>("Popup/PopupDescription");
                        if (popup != null)
                            popup.Show(LocalizationService.Get("popup.txttitle.allclear"),
                                LocalizationService.Get("popup.txtdescription.allclear"),
                                LocalizationService.Get("ui.common.continue"),
                                () => { if (GameManager.HasInstance) GameManager.Instance.LoadScene(GameManager.SCENE_LOBBY); });
                    }
                    return;
                }

                // [#4] 전면 광고 — Clear → Next 지면 (interstitial_clear_next). 오버레이이므로 이후 동선은 그대로 진행.
                if (AdManager.HasInstance)
                    AdManager.Instance.TryShowInterstitial(AdManager.InterstitialPlacement.ClearNext);

                // [#5/12] Next 분기 (UX플로우 §5-4·§435 + Key Blaze 스펙):
                //   - Lv.5~33 클리어 → 다음 레벨 자동 진입 (학습·진행 흐름 유지)
                //   - 해금 바로 전 스테이지(=UNLOCK_LEVEL-1, Lv.34) 클리어부터 → 로비 강제.
                //     34 클리어 → highestCleared=34 → WS 노출(IsUnlocked, 활성화만) → 로비.
                //     35 클리어 → highestCleared=35 → 점수·보상 적립 시작(IsScoringActive). (노출과 적립 게이트 분리)
                //   - 이벤트 진행 중(IsEventActive=IsUnlocked)이면 하위 레벨 재도전 포함 매 클리어마다 로비.
                bool wsActive = WinningStreakManager.HasInstance && WinningStreakManager.Instance.IsEventActive;
                if (_current >= FtueGate.WINNING_STREAK_UNLOCK_CLEAR_LEVEL - 1 || wsActive)
                {
                    if (GameManager.HasInstance) GameManager.Instance.GoToLobby();
                    return;
                }

                LevelManager.Instance.LoadLevel(_next);
            }
        }

        /// <summary>결과 팝업의 Retry 버튼 처리 — 현재 레벨을 재시작한다.</summary>
        void OnRetryClicked()
        {
            _pendingResultIsWin = false;
            if (_result != null) _result.CloseUI();
            if (_hud != null) _hud.OpenUI();

            if (LevelManager.HasInstance)
                LevelManager.Instance.RetryLevel();
        }

        /// <summary>결과 팝업의 Home 버튼 처리 — 테스트 모드에서는 MapMaker로, 일반 모드에서는 Lobby 씬으로 이동.</summary>
        void OnHomeClicked()
        {
            if (_result != null) _result.CloseUI();

            if (_isTestMode && GameManager.HasInstance)
            {
                // Return to MapMaker scene in test mode
                GameManager.Instance.GoToMapMaker();
                return;
            }

            if (GameManager.HasInstance) GameManager.Instance.GoToLobby();
        }

        #endregion
    }
}
