using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BalloonFlow
{
    /// <summary>
    /// Lobby scene controller.
    /// - GameManager.InitLobby() initializes economy/shop/level managers
    /// - Opens UILobby with page-swipe navigation (Shop/Home/Setting)
    /// - BtnGoldPlus / BtnLifePlus → Shop 페이지로 이동 (PopupGoldShop 미사용)
    /// - Updates Gold, Life, Level display via EventBus
    /// </summary>
    /// <remarks>Project-wide convention: flat 'namespace BalloonFlow' — do not nest.</remarks>
    /// <remarks>Not a singleton — scene-level MonoBehaviour managed by Unity lifecycle.</remarks>
    public class LobbyController : MonoBehaviour
    {
        private const float COIN_FLY_FLAG_RESET_DELAY = 0.3f;
        // 게임 시작 직전 표시되던 레벨. 로비 복귀 시 새 레벨(highest+1) 과 비교해 레벨업 여부 판정.
        private const string PREFS_KEY_LOBBY_LEVEL_AT_GAME_START = "BF_LobbyLevelAtGameStart";

        private UILobby _lobby;
        private bool _isCoinFlyInFlight;
        private Coroutine _coinFlyResetCoroutine;
        // [2026-05-19] 하트 증가 감지용 — currentLives 가 이전보다 커지면 LifePanel 펄스 트리거.
        // -1 sentinel: 첫 HandleLifeChanged 호출은 비교 skip (초기값 설정만).
        private int _lastDisplayedLives = -1;

        void Start()
        {
            if (!GameManager.HasInstance)
            {
                var go = new GameObject("GameManager");
                go.AddComponent<GameManager>();
            }

            #if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetBool("BalloonFlow_UseTestLevel", false);
            #endif
            GameManager.IsTestPlayMode = false;

            GameManager.Instance.InitLobby();

            if (AudioManager.HasInstance)
                AudioManager.Instance.PlayLobbyBGM();

            if (CameraManager.HasInstance)
                CameraManager.Instance.ConfigureLobby();

            // Title 의 UI/Popup 제거 (Lobby 진입 시 직전 씬 UI 잔여 정리)
            if (UIManager.HasInstance) UIManager.Instance.DestroyAllUI();
            if (PopupManager.HasInstance) PopupManager.Instance.UnregisterAll();

            if (UIManager.HasInstance && !UIManager.Instance.HasLiveSceneCanvas)
            {
                // UIManager 가 캔버스를 갖고있지 않을 때만 (Editor 직접 Lobby Play 등) 새로 생성
                var uiCanvas = GameObject.Find("UICanvas");
                if (uiCanvas == null) uiCanvas = GameObject.Find("Canvas");
                if (uiCanvas == null) uiCanvas = CreateCanvas("UICanvas", 0);

                var popupCanvas = GameObject.Find("PopupCanvas");
                if (popupCanvas == null) popupCanvas = CreateCanvas("PopupCanvas", 10);

                var effectCanvas = GameObject.Find("EffectCanvas");
                if (effectCanvas == null) effectCanvas = CreateCanvas("EffectCanvas", 15);

                UIManager.Instance.SetSceneCanvas(uiCanvas.transform, popupCanvas.transform, effectCanvas.transform);
            }

            LoadUI();
            RefreshDisplay();

            // 인게임 종료 후 로비 복귀 시점에도 Rail 슬라이드 인 보장 (Awake 자동 호출 의존하지 않고 컨트롤러에서 명시).
            if (_lobby != null)
            {
                _lobby.PlayRailEnterAnimation();
                _lobby.PlayLevelObjectEnterAnimation();
            }
        }

        void OnEnable()
        {
            EventBus.Subscribe<OnCoinChanged>(HandleCoinChanged);
            EventBus.Subscribe<OnLifeChanged>(HandleLifeChanged);
            EventBus.Subscribe<OnCoinFlyLanded>(HandleCoinFlyLanded);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<OnCoinChanged>(HandleCoinChanged);
            EventBus.Unsubscribe<OnLifeChanged>(HandleLifeChanged);
            EventBus.Unsubscribe<OnCoinFlyLanded>(HandleCoinFlyLanded);

            if (_lobby != null)
            {
                if (_lobby.BtnPlay != null) _lobby.BtnPlay.onClick.RemoveListener(OnPlayClicked);
                if (_lobby.BtnGoldPlus != null) _lobby.BtnGoldPlus.onClick.RemoveListener(OnGoToShop);
                if (_lobby.BtnLifePlus != null) _lobby.BtnLifePlus.onClick.RemoveListener(OnGoToShop);
                if (_lobby.BtnLifeBar != null) _lobby.BtnLifeBar.onClick.RemoveListener(OnLifeBarClicked);
                if (_lobby.BtnNoAds != null) _lobby.BtnNoAds.onClick.RemoveListener(OnNoAdsClicked);
                if (_lobby.BtnProfilePanel != null) _lobby.BtnProfilePanel.onClick.RemoveListener(OnProfileClicked);
            }
        }

        void Update()
        {
            UpdateLifeTimer();

            // 백버튼(Escape) → 종료 확인 팝업
            if (Keyboard.current != null && Keyboard.current[Key.Escape].wasPressedThisFrame)
                ShowQuitConfirm();
        }

        void ShowQuitConfirm()
        {
            if (!UIManager.HasInstance) return;
            var popup = UIManager.Instance.OpenUI<PopupDescription>("Popup/PopupDescription");
            if (popup != null)
                popup.Show("Quit", "Are you sure you want to quit?", "Quit",
                    () => Application.Quit());
        }

        #region UI Load

        void LoadUI()
        {
            if (!UIManager.HasInstance) return;

            _lobby = UIManager.Instance.OpenUI<UILobby>("UI/UILobby");
            if (_lobby != null)
            {
                // ROLLBACK_LOBBY_RETURN_FORCE_MAIN_PANEL:
                // Returning from InGame can reuse an inactive UILobby that still remembers
                // Shop/Setting page position. Lobby entry must always start from Main/Home.
                _lobby.ShowMainPanelImmediate();

                if (_lobby.BtnPlay != null) _lobby.BtnPlay.onClick.AddListener(OnPlayClicked);
                if (_lobby.BtnGoldPlus != null) _lobby.BtnGoldPlus.onClick.AddListener(OnGoToShop);
                if (_lobby.BtnLifePlus != null) _lobby.BtnLifePlus.onClick.AddListener(OnGoToShop);
                if (_lobby.BtnLifeBar != null) _lobby.BtnLifeBar.onClick.AddListener(OnLifeBarClicked);
                if (_lobby.BtnNoAds != null) _lobby.BtnNoAds.onClick.AddListener(OnNoAdsClicked);
                if (_lobby.BtnProfilePanel != null) _lobby.BtnProfilePanel.onClick.AddListener(OnProfileClicked);
            }
        }

        /// <summary>
        /// 인게임 종료 후 로비 복귀 시 레벨업 여부 판정 + 분기.
        /// 레벨업 케이스에서만 LobbyBtnChange 애니메이션을 트리거하며,
        /// first-launch / 레벨 변화 없는 로비 복귀(no-levelup) 양 케이스에서는
        /// 버튼·난이도 UI 를 NEW 값으로 즉시 스냅하고 idle 상태를 강제한다.
        /// </summary>
        void RefreshDisplay()
        {
            if (_lobby == null) return;

            if (CurrencyManager.HasInstance)
                _lobby.SetGoldText(CurrencyManager.Instance.Coins);

            if (LifeManager.HasInstance)
                _lobby.SetLifeText(LifeManager.Instance.CurrentLives, LifeManager.Instance.MaxLives);

            int highest = 0;
            if (LevelManager.HasInstance)
                highest = LevelManager.Instance.GetHighestCompletedLevel();

            int newLevel = highest > 0 ? highest + 1 : 1;

            DifficultyPurpose newDiff = DifficultyPurpose.Normal;
            if (LevelManager.HasInstance)
                newDiff = LevelManager.Instance.GetLevelDifficulty(newLevel);

            // 레벨업 감지 — 직전 게임 시작 시 표시 레벨과 비교.
            // 키 소비(DeleteKey)는 비교 직후 1회만 — 재 RefreshDisplay 호출 시 중복 트리거 방지.
            bool hasPrev = PlayerPrefs.HasKey(PREFS_KEY_LOBBY_LEVEL_AT_GAME_START);
            int prevLevel = hasPrev ? PlayerPrefs.GetInt(PREFS_KEY_LOBBY_LEVEL_AT_GAME_START, newLevel) : newLevel;
            if (hasPrev)
            {
                PlayerPrefs.DeleteKey(PREFS_KEY_LOBBY_LEVEL_AT_GAME_START);
                PlayerPrefs.Save();
            }

            bool isLobbyReturn = hasPrev;                          // hasPrev=true → 인게임 거쳐 복귀
            bool isLevelUp = isLobbyReturn && newLevel > prevLevel;

            if (isLevelUp)
            {
                // 기존 그대로 — OLD 레벨/난이도 + OLD Rail 상태 먼저 표시 → LobbyBtnChange 20f 시점에 NEW 일괄 교체
                int prevHighest = Mathf.Max(0, prevLevel - 1);
                DifficultyPurpose prevDiff = DifficultyPurpose.Normal;
                if (LevelManager.HasInstance)
                    prevDiff = LevelManager.Instance.GetLevelDifficulty(prevLevel);

                _lobby.SetupLevelBoxes(prevLevel, prevHighest);
                _lobby.UpdatePlayButton(prevLevel, prevDiff);

                int capturedNewLevel = newLevel;
                int capturedHighest = highest;
                _lobby.PlayLobbyBtnChangeAnim(newLevel, newDiff, () =>
                {
                    if (_lobby != null) _lobby.SetupLevelBoxes(capturedNewLevel, capturedHighest);
                });
            }
            else
            {
                // first-launch 와 'lobby-return-no-levelup' 양 케이스.
                // 사용자 피드백: 레벨 변화가 없으면 LobbyBtnChange 절대 재생 금지 + 버튼/난이도 UI 기존 상태 유지.
                _lobby.SetupLevelBoxes(newLevel, highest);
                _lobby.UpdatePlayButton(newLevel, newDiff);
                _lobby.EnsureLobbyBtnIdle();  // default state(LobbyBtnChange) 자동 재생 차단 안전망
            }
        }

        void UpdateLifeTimer()
        {
            if (_lobby == null || !LifeManager.HasInstance) return;

            var lm = LifeManager.Instance;

            // 무한 하트 상태: ImageInfinite 노출, (+) 숨김, 남은 시간 표시
            if (lm.IsInfiniteHeartsActive)
            {
                _lobby.SetLifeText(lm.MaxLives, lm.MaxLives);
                float secs = lm.GetRemainingInfiniteSeconds();
                TimeSpan ts = TimeSpan.FromSeconds(secs);
                string timerStr = ts.TotalHours >= 1
                    ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                    : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
                _lobby.SetLifeTimerText(timerStr);
                _lobby.SetLifePlusButtonVisible(false);
                _lobby.SetInfiniteImageVisible(true);
                return;
            }

            _lobby.SetInfiniteImageVisible(false);

            // FULL 상태: FULL 텍스트, (+) 숨김
            if (lm.IsFullLives())
            {
                _lobby.SetLifeText(lm.MaxLives, lm.MaxLives);
                _lobby.SetLifeTimerText("FULL");
                _lobby.SetLifePlusButtonVisible(false);
                return;
            }

            // 충전 중: (+) 보임, 시간 표시
            _lobby.SetLifePlusButtonVisible(true);
            TimeSpan remaining = lm.GetTimeToNextLife();
            if (remaining.TotalSeconds > 0)
                _lobby.SetLifeTimerText($"{remaining.Minutes:D2}:{remaining.Seconds:D2}");
            else
                _lobby.SetLifeTimerText(null);
        }

        #endregion

        #region Button Events

        void OnPlayClicked()
        {
            if (_lobby != null) _lobby.PlayButtonPressAnim();

            if (!GameManager.HasInstance) return;

            if (LifeManager.HasInstance && !LifeManager.Instance.HasLife())
            {
                // 라이프 부족 → PopupMoreLive 표시
                if (UIManager.HasInstance)
                    UIManager.Instance.OpenUI<PopupMoreLive>("Popup/PopupMoreLive");
                return;
            }

            int levelId = 1;
            if (LevelManager.HasInstance)
            {
                int highest = LevelManager.Instance.GetHighestCompletedLevel();
                levelId = highest > 0 ? highest + 1 : 1;
            }

            // 로비 복귀 시 레벨업 감지에 사용 — 게임 시작 직전 PlayButton 에 표시되던 레벨을 저장.
            PlayerPrefs.SetInt(PREFS_KEY_LOBBY_LEVEL_AT_GAME_START, levelId);
            PlayerPrefs.Save();

            // 현재 레벨 RailBox 열림 연출 후 씬 이동
            var activeBox = _lobby.GetActiveRailBox();
            if (activeBox != null)
            {
                int capturedLevelId = levelId;
                activeBox.PlayStartGameAnimation(() =>
                {
                    GameManager.Instance.StartLevel(capturedLevelId);
                });
            }
            else
            {
                GameManager.Instance.StartLevel(levelId);
            }
        }

        /// <summary>BtnGoldPlus / BtnLifePlus → Shop 페이지로 스와이프 이동</summary>
        void OnGoToShop()
        {
            if (_lobby != null) _lobby.GoToPage(0);
        }

        void OnNoAdsClicked()
        {
            if (UIManager.HasInstance)
                UIManager.Instance.OpenUI<PopupNoAds>("Popup/PopupNoAds");
        }

        void OnProfileClicked()
        {
            if (UIManager.HasInstance)
                UIManager.Instance.OpenUI<PopupProfile>(Const.POPUP_PROFILE);
        }

        /// <summary>하트 바 터치 시 상태별 분기.</summary>
        void OnLifeBarClicked()
        {
            if (!LifeManager.HasInstance || !UIManager.HasInstance) return;

            // [2026-05-15] 무한 하트 → PopupMoreLive 안 띄움. 토스트로만 안내. (사용자 요구)
            if (LifeManager.Instance.IsInfiniteHeartsActive)
            {
                ShowToast("Unlimited hearts!");
                return;
            }

            // FULL → TxtToast 토스트
            if (LifeManager.Instance.IsFullLives())
            {
                ShowToast("Your lives are full!");
                return;
            }

            // 하트 미만 → PopupMoreLive
            UIManager.Instance.OpenUI<PopupMoreLive>("Popup/PopupMoreLive");
        }

        #endregion

        #region EventBus Handlers

        void HandleCoinChanged(OnCoinChanged evt)
        {
            if (_lobby == null) return;

            // delta == 0 이면 단순 스냅
            if (evt.delta == 0)
            {
                _lobby.SetGoldText(evt.currentCoins);
                return;
            }

            // 코인 fly 시퀀스 진행 중이면 OnCoinFlyLanded 가 점진적으로 +1 갱신함 — 이 이벤트는 무시.
            // 시퀀스 종료 후 ResetCoinFlyFlag 가 최종 값으로 동기화함.
            if (evt.delta > 0 && _isCoinFlyInFlight) return;

            _lobby.SetGoldTextAnimated(evt.currentCoins);
        }

        /// <summary>
        /// PopupResult 의 코인 fly 연출에서 코인 한 알 도착 시 호출.
        /// 표시값을 +1 카운트업하고, 마지막 도착 후 0.3s 뒤 플래그 해제 + 최종 동기화.
        /// </summary>
        void HandleCoinFlyLanded(OnCoinFlyLanded evt)
        {
            if (_lobby == null) return;

            _isCoinFlyInFlight = true;
            _lobby.AddDisplayedGold(1);

            if (_coinFlyResetCoroutine != null) StopCoroutine(_coinFlyResetCoroutine);
            _coinFlyResetCoroutine = StartCoroutine(ResetCoinFlyFlag());
        }

        System.Collections.IEnumerator ResetCoinFlyFlag()
        {
            yield return new WaitForSecondsRealtime(COIN_FLY_FLAG_RESET_DELAY);
            _isCoinFlyInFlight = false;
            _coinFlyResetCoroutine = null;

            // 시퀀스 종료 후 최종 정합성 보정 — CurrencyManager 의 실제 값과 동기화
            if (_lobby != null && CurrencyManager.HasInstance)
                _lobby.SetGoldText(CurrencyManager.Instance.Coins);
        }

        void HandleLifeChanged(OnLifeChanged evt)
        {
            if (_lobby == null) return;

            _lobby.SetLifeText(evt.currentLives, evt.maxLives);

            // (+) 버튼: Full 또는 무한 하트 시 숨김
            bool hidePlus = evt.currentLives >= evt.maxLives
                || (LifeManager.HasInstance && LifeManager.Instance.IsInfiniteHeartsActive);
            _lobby.SetLifePlusButtonVisible(!hidePlus);

            // [2026-05-19] 하트 증가 시 LifePanel 펄스 — 골드 패널과 동일 패턴.
            // 음수 delta (레벨 실패 차감) 시는 펄스 안 함. 첫 호출 (초기값 설정) 도 skip.
            if (_lastDisplayedLives >= 0 && evt.currentLives > _lastDisplayedLives)
                _lobby.PulseLifePanel();
            _lastDisplayedLives = evt.currentLives;
        }

        #endregion

        #region Toast

        void ShowToast(string message)
        {
            if (!UIManager.HasInstance) return;
            Transform parent = UIManager.Instance.PopupTr ?? UIManager.Instance.UiTr;
            if (parent == null) return;

            TxtToast.Spawn(parent, message, Vector2.zero);
        }

        #endregion

        #region Helpers

        static GameObject CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new UnityEngine.Vector2(1242f, 2688f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            return go;
        }

        #endregion
    }
}
