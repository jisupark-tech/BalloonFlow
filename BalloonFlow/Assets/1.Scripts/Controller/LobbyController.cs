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
    public class LobbyController : MonoBehaviour
    {
        private UILobby _lobby;

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

            if (UIManager.HasInstance)
            {
                var uiCanvas = GameObject.Find("UICanvas");
                if (uiCanvas == null) uiCanvas = GameObject.Find("Canvas");
                if (uiCanvas == null)
                {
                    uiCanvas = CreateCanvas("UICanvas", 0);
                }

                var popupCanvas = GameObject.Find("PopupCanvas");
                if (popupCanvas == null)
                {
                    popupCanvas = CreateCanvas("PopupCanvas", 10);
                }

                UIManager.Instance.SetSceneCanvas(uiCanvas.transform, popupCanvas.transform);
            }

            LoadUI();
            RefreshDisplay();
        }

        void OnEnable()
        {
            EventBus.Subscribe<OnCoinChanged>(HandleCoinChanged);
            EventBus.Subscribe<OnLifeChanged>(HandleLifeChanged);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<OnCoinChanged>(HandleCoinChanged);
            EventBus.Unsubscribe<OnLifeChanged>(HandleLifeChanged);

            if (_lobby != null)
            {
                if (_lobby.BtnPlay != null) _lobby.BtnPlay.onClick.RemoveListener(OnPlayClicked);
                if (_lobby.BtnGoldPlus != null) _lobby.BtnGoldPlus.onClick.RemoveListener(OnGoToShop);
                if (_lobby.BtnLifePlus != null) _lobby.BtnLifePlus.onClick.RemoveListener(OnGoToShop);
                if (_lobby.BtnLifeBar != null) _lobby.BtnLifeBar.onClick.RemoveListener(OnLifeBarClicked);
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
                if (_lobby.BtnPlay != null) _lobby.BtnPlay.onClick.AddListener(OnPlayClicked);
                if (_lobby.BtnGoldPlus != null) _lobby.BtnGoldPlus.onClick.AddListener(OnGoToShop);
                if (_lobby.BtnLifePlus != null) _lobby.BtnLifePlus.onClick.AddListener(OnGoToShop);
                if (_lobby.BtnLifeBar != null) _lobby.BtnLifeBar.onClick.AddListener(OnLifeBarClicked);
            }
        }

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

            int currentLevel = highest > 0 ? highest + 1 : 1;
            _lobby.SetupLevelBoxes(currentLevel, highest);

            DifficultyPurpose diff = DifficultyPurpose.Normal;
            if (LevelManager.HasInstance)
                diff = LevelManager.Instance.GetLevelDifficulty(currentLevel);
            _lobby.UpdatePlayButton(currentLevel, diff);
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

        /// <summary>하트 바 터치 시 상태별 분기.</summary>
        void OnLifeBarClicked()
        {
            if (!LifeManager.HasInstance || !UIManager.HasInstance) return;

            // 무한 하트 → PopupMoreLive
            if (LifeManager.Instance.IsInfiniteHeartsActive)
            {
                UIManager.Instance.OpenUI<PopupMoreLive>("Popup/PopupMoreLive");
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
            if (_lobby != null) _lobby.SetGoldText(evt.currentCoins);
        }

        void HandleLifeChanged(OnLifeChanged evt)
        {
            if (_lobby == null) return;

            _lobby.SetLifeText(evt.currentLives, evt.maxLives);

            // (+) 버튼: Full 또는 무한 하트 시 숨김
            bool hidePlus = evt.currentLives >= evt.maxLives
                || (LifeManager.HasInstance && LifeManager.Instance.IsInfiniteHeartsActive);
            _lobby.SetLifePlusButtonVisible(!hidePlus);
        }

        #endregion

        #region Toast

        void ShowToast(string message)
        {
            if (!UIManager.HasInstance) return;
            Transform parent = UIManager.Instance.PopupTr ?? UIManager.Instance.UiTr;
            if (parent == null) return;

            var prefab = Resources.Load<GameObject>("Popup/TxtToast");
            if (prefab == null) return;

            var go = Instantiate(prefab, parent);
            var txt = go.GetComponentInChildren<TMPro.TMP_Text>();
            if (txt != null) txt.text = message;

            // 2초 후 자동 소멸
            Destroy(go, 2f);
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
