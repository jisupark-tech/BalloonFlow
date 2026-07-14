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
        // Play 클릭 후 GameManager.StartLevel 호출 전 의도된 추가 지연(초).
        // 이 시간 동안 RailBox 열림 잔여 프레임 + PlayButtonPressAnim 의 buttonPress 연출이 자연스럽게 유지된다.
        private const float PLAY_TO_LOADING_DELAY = 0.3f;
        // 게임 시작 직전 표시되던 레벨. 로비 복귀 시 새 레벨(highest+1) 과 비교해 레벨업 여부 판정.
        private const string PREFS_KEY_LOBBY_LEVEL_AT_GAME_START = "BF_LobbyLevelAtGameStart";

        private UILobby _lobby;
        private bool _isCoinFlyInFlight;
        private Coroutine _coinFlyResetCoroutine;
        // ROLLBACK_WAIT_WSFX_BEFORE_BTN_CHANGE_20260616:
        // RefreshDisplay 의 isLevelUp 분기에서 PlayLobbyBtnChangeAnim 을 WS 로비 연출 종료 후로 미루는 대기 코루틴 핸들.
        // 씬 재진입/디스에이블 시 누적된 죽은 핸들러로 인한 leak/중복 콜백 방지를 위해 보관 후 정리한다.
        private Coroutine _pendingBtnChangeAnimCoroutine;
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
            ItemFlyEffect.OnActiveStateChanged += HandleFxItemActiveChanged;
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<OnCoinChanged>(HandleCoinChanged);
            EventBus.Unsubscribe<OnLifeChanged>(HandleLifeChanged);
            EventBus.Unsubscribe<OnCoinFlyLanded>(HandleCoinFlyLanded);
            ItemFlyEffect.OnActiveStateChanged -= HandleFxItemActiveChanged;

            if (_lobby != null)
            {
                if (_lobby.BtnPlay != null) _lobby.BtnPlay.onClick.RemoveListener(OnPlayClicked);
                if (_lobby.BtnGoldPlus != null) _lobby.BtnGoldPlus.onClick.RemoveListener(OnGoToShop);
                if (_lobby.BtnLifePlus != null) _lobby.BtnLifePlus.onClick.RemoveListener(OnLifeBarClicked);
                if (_lobby.BtnLifeBar != null) _lobby.BtnLifeBar.onClick.RemoveListener(OnLifeBarClicked);
                if (_lobby.BtnNoAds != null) _lobby.BtnNoAds.onClick.RemoveListener(OnNoAdsClicked);
                if (_lobby.BtnProfilePanel != null) _lobby.BtnProfilePanel.onClick.RemoveListener(OnProfileClicked);
            }

            if (_pendingBtnChangeAnimCoroutine != null)
            {
                StopCoroutine(_pendingBtnChangeAnimCoroutine);
                _pendingBtnChangeAnimCoroutine = null;
            }
        }

        void OnDestroy()
        {
            if (_pendingBtnChangeAnimCoroutine != null)
            {
                StopCoroutine(_pendingBtnChangeAnimCoroutine);
                _pendingBtnChangeAnimCoroutine = null;
            }
        }

        void Update()
        {
            UpdateLifeTimer();
            // [#15] 백버튼(Escape) 처리는 BackButtonRouter 로 중앙화됨 (로비 컨텍스트 → Quit Game).
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

                if (_lobby.BtnPlay != null)
                {
                    _lobby.BtnPlay.onClick.AddListener(OnPlayClicked);
                    // UIManager.OpenUI 가 캐시된 UILobby 를 재사용해 직전 진입 시 false 상태가 잔존하는 경우 방어.
                    _lobby.BtnPlay.interactable = true;
                    // 씬 재진입 중 FX 잔존 케이스 안전망 — FX 진행 중이면 즉시 비활성화.
                    if (ItemFlyEffect.IsAnyActive) _lobby.BtnPlay.interactable = false;
                }
                if (_lobby.BtnGoldPlus != null) _lobby.BtnGoldPlus.onClick.AddListener(OnGoToShop);
                if (_lobby.BtnLifePlus != null) _lobby.BtnLifePlus.onClick.AddListener(OnLifeBarClicked);
                if (_lobby.BtnLifeBar != null) _lobby.BtnLifeBar.onClick.AddListener(OnLifeBarClicked);
                if (_lobby.BtnNoAds != null) _lobby.BtnNoAds.onClick.AddListener(OnNoAdsClicked);

                // [1.0] Profile/Avatar 는 1.1 기능 → 1.0 빌드에서 좌상단 프로필 패널 숨김 + 진입 비배선.
                if (Const.PROFILE_ENABLED)
                {
                    _lobby.SetProfilePanelActive(true);
                    if (_lobby.BtnProfilePanel != null) _lobby.BtnProfilePanel.onClick.AddListener(OnProfileClicked);
                }
                else
                {
                    _lobby.SetProfilePanelActive(false);
                }
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
            {
                // ROLLBACK_WS_LOBBY_LEVEL_CLEAR_GOLD_FX_20260621:
                // Clear coins are already granted before returning to lobby. If a Winning Streak
                // lobby FX is queued, start the displayed balance before those clear coins so the
                // delayed GoldPanel fly can count up instead of flashing final -> lower -> final.
                int pendingWsClearCoins = WinningStreakManager.HasInstance
                    ? WinningStreakManager.Instance.PendingLobbyLevelClearCoins
                    : 0;
                _lobby.SetGoldText(Mathf.Max(0, CurrencyManager.Instance.Coins - pendingWsClearCoins));
            }

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
                // achieve — Play 버튼 레벨 갱신(레벨업) 성취감 chime.
                if (AudioManager.HasInstance) AudioManager.Instance.PlayAchieve();

                // 기존 그대로 — OLD 레벨/난이도 + OLD Rail 상태 먼저 표시 → LobbyBtnChange 20f 시점에 NEW 일괄 교체
                int prevHighest = Mathf.Max(0, prevLevel - 1);
                DifficultyPurpose prevDiff = DifficultyPurpose.Normal;
                if (LevelManager.HasInstance)
                    prevDiff = LevelManager.Instance.GetLevelDifficulty(prevLevel);

                _lobby.SetupLevelBoxes(prevLevel, prevHighest);
                _lobby.UpdatePlayButton(prevLevel, prevDiff);

                int capturedNewLevel = newLevel;
                int capturedHighest = highest;
                // WS 로비 보상 팝업/FX 가 같은 프레임 OpenUI 에서 트리거될 수 있어, 두 연출이 겹치지 않도록
                // PlayLobbyBtnChangeAnim 을 IsWinningStreakCoreFxPlaying 종료 시점 뒤로 미룬다.
                // (Core 게이트는 FXGold 를 제외해 LobbyBtnChange 가 FXGold 와 병렬로 시작될 수 있도록 한다 — UILobby.cs:333 주석 참조)
                // [2026-06-23] 이벤트 기반 동시 시작 + 폴링 fallback — UILobby.OnWinningStreakCoreFxReleased 구독으로 release 와
                // PlayLobbyBtnChangeAnim 을 같은 프레임에 동시 시작(폴링 1프레임 race 제거). 폴링은 미발화/예외 경로 안전망.
                // SUPERSEDES 2026-06-23 PR #375 (poll-only) — owner 출처: 본 ProjectHub 태스크 [재시도 피드백] 2026-06-23 '동시에 시작'.
                if (_pendingBtnChangeAnimCoroutine != null) StopCoroutine(_pendingBtnChangeAnimCoroutine);
                _pendingBtnChangeAnimCoroutine = StartCoroutine(
                    WaitForWinningStreakFxThenPlayBtnChangeAnim(newLevel, newDiff, capturedNewLevel, capturedHighest));
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

            // Full 상태: Full 텍스트, (+) 숨김
            if (lm.IsFullLives())
            {
                _lobby.SetLifeText(lm.MaxLives, lm.MaxLives);
                _lobby.SetLifeTimerText(LocalizationService.Get("uilobby.txtlife"));
                _lobby.SetLifePlusButtonVisible(false);
                return;
            }

            // 충전 중: (+) 보임, 시간 표시
            _lobby.SetLifePlusButtonVisible(true);
            // ROLLBACK_LIFE_TEXT_CHARGING_REFRESH:
            // RefreshDisplay 의 1회 호출이 LifeManager init race 로 skip 되거나, _txtLife 가 prefab default ("") 인 채
            // OnLifeChanged 이벤트 전까지 빈 텍스트로 남는 케이스 차단. 매 frame 호출이라 stale 도 자동 회복.
            _lobby.SetLifeText(lm.CurrentLives, lm.MaxLives);
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
            // ROLLBACK_LOBBY_PLAY_BLOCK_DURING_WSFX_20260615:
            // WS 로비 연출(FXItem 비행 / Winning Streak 게이지·배수) 또는 0단계 보상 팝업 재생 중엔 인게임 진입 차단.
            //   연출이 모두 끝난 후(IsWinningStreakFxPlaying=false)에만 진입 허용. 롤백: 아래 if 블록 제거.
            if (_lobby != null && _lobby.IsWinningStreakFxPlaying)
            {
                if (AudioManager.HasInstance) AudioManager.Instance.PlayDeny();
                return;
            }

            // 인게임 진입 연출 중복 트리거 방지 — onClick 코드 invoke / 연타 양쪽 모두 차단.
            if (_lobby != null && _lobby.BtnPlay != null && !_lobby.BtnPlay.interactable) return;

            if (_lobby != null) _lobby.PlayButtonPressAnim();

            if (!GameManager.HasInstance) return;

            // ROLLBACK_ALL_CLEAR_PLAY_BLOCK_20260708: Firebase 에피소드 전량 클리어 시 인게임 진입 차단.
            //   다음 도전 레벨(최고 클리어+1)이 보유 총 레벨 수를 넘으면 콘텐츠 소진 — 안내 팝업만 표시,
            //   라이프 소모/씬 전환 없음. 총 레벨 0(에피소드 데이터 미로딩)이면 판정 건너뜀(오차단 방지).
            //   1.0 EN-only 정책으로 문구 하드코딩(TextData.csv 인코딩 리스크 회피). 롤백: 이 블록 + GetTotalLevelCount 제거.
            if (LevelManager.HasInstance)
            {
                int totalLevels = LevelManager.Instance.GetTotalLevelCount();
                int nextLevel = LevelManager.Instance.GetHighestCompletedLevel() + 1;
                if (totalLevels > 0 && nextLevel > totalLevels)
                {
                    if (UIManager.HasInstance)
                    {
                        var allClearPopup = UIManager.Instance.OpenUI<PopupDescription>(Const.POPUP_DESCRIPTION);
                        if (allClearPopup != null)
                            // ROLLBACK_ALLCLEAR_POPUP_UNIFY_20260713: 로비 all-clear 문구/버튼을 인게임(GameBootstrap/
                            //   PopupResult)과 동일 로컬라이즈 키로 통일. 기존엔 하드코딩("All Levels Cleared!"/장문/OK)
                            //   이라 인게임(Congratulations!/New Levels Coming Soon!/Continue)과 불일치했다.
                            //   버튼 초록색은 PopupDescription 프리팹 기본값. 롤백: 아래 3줄을 하드코딩 원문으로 환원.
                            allClearPopup.Show(LocalizationService.Get("popup.txttitle.allclear"),
                                LocalizationService.Get("popup.txtdescription.allclear"),
                                LocalizationService.Get("ui.common.continue"));
                    }

                    // ROLLBACK_ALL_CLEAR_PLAY_BLOCK_20260708: 새 에피소드가 뒤늦게 업로드된 경우 자동 해제 —
                    //   차단 중 다음 에피소드 존재를 백그라운드 재확인(성공 시 실측 상한 자동 상향 → 다음 클릭부터 진입).
                    //   캐시(메모리 1개)가 잠시 교체될 수 있으나 StartLevel 의 prefetch 가 필요 에피소드를 재보장한다.
                    if (LevelEpisodeService.HasInstance
                        && LevelEpisodeService.KnownAvailableEpisodes < LevelEpisodeService.TOTAL_EPISODES)
                        _ = LevelEpisodeService.Instance.EnsureEpisodeAsync(LevelEpisodeService.KnownAvailableEpisodes + 1);

                    return;
                }
            }

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

            // 인게임 진입 연출 중복 트리거 방지 — 씬 전환까지 차단 유지.
            if (_lobby != null && _lobby.BtnPlay != null) _lobby.BtnPlay.interactable = false;

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
                    StartCoroutine(DelayedStartLevel(capturedLevelId));
                });
            }
            else
            {
                StartCoroutine(DelayedStartLevel(levelId));
            }
        }

        System.Collections.IEnumerator DelayedStartLevel(int levelId)
        {
            yield return new WaitForSecondsRealtime(PLAY_TO_LOADING_DELAY);
            if (GameManager.HasInstance)
                GameManager.Instance.StartLevel(levelId);
        }

        /// <summary>WS 로비 연출(보상 팝업 + LobbyFx)이 끝난 뒤 PlayLobbyBtnChangeAnim 을 트리거.
        /// 첫 yield 로 한 프레임 양보해 UILobby.OpenUI → TriggerPendingWinningStreakLobbyFx 가 armed 비트를 세팅할 시간을 확보.
        /// [2026-06-23] 이벤트(OnWinningStreakCoreFxReleased) 기반 동시 시작 + 폴링(IsWinningStreakCoreFxPlaying) fallback.
        /// 정상 경로: 이벤트가 같은 프레임에 도착해 폴링이 한 번 더 yield 하기 전에 PlayLobbyBtnChangeAnim 을 실행 → frame N 동시 시작.
        /// SUPERSEDES 2026-06-23 PR #375 (poll-only, frame N+1 race) — owner 출처: 본 ProjectHub 태스크 [재시도 피드백] 2026-06-23.</summary>
        System.Collections.IEnumerator WaitForWinningStreakFxThenPlayBtnChangeAnim(
            int newLevel, DifficultyPurpose newDiff, int capturedNewLevel, int capturedHighest)
        {
            yield return null;

            if (_lobby == null) { _pendingBtnChangeAnimCoroutine = null; yield break; }

            // Fallback 1: WS 코어 연출이 아예 시작되지 않은 케이스 (armed 도 false) → 즉시 실행.
            if (!_lobby.IsWinningStreakCoreFxPlaying)
            {
                _lobby.PlayLobbyBtnChangeAnim(newLevel, newDiff, () =>
                {
                    if (_lobby != null) _lobby.SetupLevelBoxes(capturedNewLevel, capturedHighest);
                });
                _pendingBtnChangeAnimCoroutine = null;
                yield break;
            }

            // 이벤트 기반 동시 시작 + 폴링 fallback. 둘 중 먼저 도착하는 시그널로 1회 트리거.
            bool playBtnTriggered = false;
            UILobby subscribedLobby = _lobby;
            System.Action handler = null;
            handler = () =>
            {
                if (playBtnTriggered) return;
                playBtnTriggered = true;
                if (_lobby != null)
                {
                    _lobby.PlayLobbyBtnChangeAnim(newLevel, newDiff, () =>
                    {
                        if (_lobby != null) _lobby.SetupLevelBoxes(capturedNewLevel, capturedHighest);
                    });
                }
            };
            subscribedLobby.OnWinningStreakCoreFxReleased += handler;

            try
            {
                // [WS 코어 연출 병렬화 2026-06-23] FXGold(PlayWinningStreakLevelClearGoldFx)는 LobbyBtnChange 와 병렬 실행 허용.
                // 코어 연출(보상 팝업 + 게이지/배수) 만 끝나면 release. click 차단은 OnPlayClicked 의 IsWinningStreakFxPlaying 가드가 별도로 유지.
                // 정상 경로에서는 이벤트가 release 사이트(UILobby L1047/L1063)에서 동기 발화 → handler 가 같은 프레임에 PlayLobbyBtnChangeAnim 호출 → 본 while 가 한 번 더 yield 하기 전 종료.
                while (!playBtnTriggered && _lobby != null && _lobby.IsWinningStreakCoreFxPlaying)
                    yield return null;

                // Fallback 2: 이벤트 미도달 + 게이트는 풀린 경로(예외/StopCoroutine 으로 finally 안전망 미발화 등) — handler 미발화면 직접 호출.
                if (!playBtnTriggered && _lobby != null)
                    handler.Invoke();
            }
            finally
            {
                // 누수 시 다음 levelup 에 중복 발화 가능 → 모든 종료 경로(정상/yield break/예외)에서 unsubscribe.
                if (subscribedLobby != null)
                    subscribedLobby.OnWinningStreakCoreFxReleased -= handler;
                _pendingBtnChangeAnimCoroutine = null;
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
                ShowToast(LocalizationService.Get("toast.life.unlimited"));
                return;
            }

            // Full → TxtToast 토스트
            if (LifeManager.Instance.IsFullLives())
            {
                ShowToast(LocalizationService.Get("toast.life.full"));
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
            _lobby.PulseGoldPanel();

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

        void HandleFxItemActiveChanged(bool isActive)
        {
            if (_lobby == null || _lobby.BtnPlay == null) return;
            _lobby.BtnPlay.interactable = !isActive;
        }

        void HandleLifeChanged(OnLifeChanged evt)
        {
            if (_lobby == null) return;

            _lobby.SetLifeText(evt.currentLives, evt.maxLives);

            // (+) 버튼: Full 또는 무한 하트 시 숨김
            bool hidePlus = evt.currentLives >= evt.maxLives
                || (LifeManager.HasInstance && LifeManager.Instance.IsInfiniteHeartsActive);
            _lobby.SetLifePlusButtonVisible(!hidePlus);

            // PulseLifePanel 내부에서 isIncrease(_lastShownLife 캐시) + FxFire debounce 처리.
            // 첫 호출(초기 진입) / 감소(라이프 사용) 시는 내부에서 no-op.
            _lobby.PulseLifePanel(evt.currentLives);
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
