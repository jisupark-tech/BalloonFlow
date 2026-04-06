using System;
using UnityEngine;

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

            if (CameraManager.HasInstance)
                CameraManager.Instance.ConfigureLobby();

            var canvasGO = GameObject.Find("Canvas");
            if (canvasGO != null && UIManager.HasInstance)
                UIManager.Instance.SetSceneCanvas(canvasGO.transform);

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
            }
        }

        void Update()
        {
            UpdateLifeTimer();
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
        }

        void UpdateLifeTimer()
        {
            if (_lobby == null || !LifeManager.HasInstance) return;

            if (LifeManager.Instance.IsFullLives())
            {
                _lobby.SetLifeTimerText(null);
                return;
            }

            TimeSpan remaining = LifeManager.Instance.GetTimeToNextLife();
            if (remaining.TotalSeconds > 0)
                _lobby.SetLifeTimerText($"{remaining.Minutes:D2}:{remaining.Seconds:D2}");
            else
                _lobby.SetLifeTimerText(null);
        }

        #endregion

        #region Button Events

        void OnPlayClicked()
        {
            if (!GameManager.HasInstance) return;

            if (LifeManager.HasInstance && !LifeManager.Instance.HasLife())
            {
                // 라이프 없으면 Shop 페이지로 이동
                if (_lobby != null) _lobby.GoToPage(0);
                return;
            }

            int levelId = 1;
            if (LevelManager.HasInstance)
            {
                int highest = LevelManager.Instance.GetHighestCompletedLevel();
                levelId = highest > 0 ? highest + 1 : 1;
            }
            GameManager.Instance.StartLevel(levelId);
        }

        /// <summary>BtnGoldPlus / BtnLifePlus → Shop 페이지로 스와이프 이동</summary>
        void OnGoToShop()
        {
            if (_lobby != null) _lobby.GoToPage(0);
        }

        #endregion

        #region EventBus Handlers

        void HandleCoinChanged(OnCoinChanged evt)
        {
            if (_lobby != null) _lobby.SetGoldText(evt.currentCoins);
        }

        void HandleLifeChanged(OnLifeChanged evt)
        {
            if (_lobby != null) _lobby.SetLifeText(evt.currentLives, evt.maxLives);
        }

        #endregion
    }
}
