using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Lobby scene controller. Loads UILobby, PopupSettings, PopupGoldShop prefabs.
    /// Wires button events and manages popup show/hide.
    /// </summary>
    public class LobbyController : MonoBehaviour
    {
        private const string LOBBY_PREFAB    = "UI/UILobby";
        private const string SETTINGS_PREFAB = "Popup/PopupSettings";
        private const string GOLDSHOP_PREFAB = "Popup/PopupGoldShop";

        private UILobby _lobby;
        private PopupSettings _settingsPopup;
        private PopupGoldShop _goldShopPopup;

        private void Awake()
        {
            LoadUI();
        }

        private void Start()
        {
            RefreshDisplay();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnCoinChanged>(HandleCoinChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnCoinChanged>(HandleCoinChanged);

            if (_lobby != null)
            {
                if (_lobby.PlayButton != null) _lobby.PlayButton.onClick.RemoveListener(OnPlayClicked);
                if (_lobby.SettingsButton != null) _lobby.SettingsButton.onClick.RemoveListener(OnSettingsClicked);
                if (_lobby.CoinButton != null) _lobby.CoinButton.onClick.RemoveListener(OnCoinClicked);
            }
            if (_settingsPopup != null && _settingsPopup.CloseButton != null)
                _settingsPopup.CloseButton.onClick.RemoveListener(OnSettingsCloseClicked);
            if (_goldShopPopup != null && _goldShopPopup.CloseButton != null)
                _goldShopPopup.CloseButton.onClick.RemoveListener(OnGoldShopCloseClicked);
        }

        private void LoadUI()
        {
            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null) return;
            Transform root = canvas.transform;

            // UILobby
            var lobbyPrefab = Resources.Load<GameObject>(LOBBY_PREFAB);
            if (lobbyPrefab != null)
            {
                var go = Instantiate(lobbyPrefab, root);
                _lobby = go.GetComponent<UILobby>();
                WireLobbyButtons();
            }
            else
            {
                Debug.LogError($"[LobbyController] Prefab not found: {LOBBY_PREFAB}");
            }

            // PopupSettings
            var settingsPrefab = Resources.Load<GameObject>(SETTINGS_PREFAB);
            if (settingsPrefab != null)
            {
                var go = Instantiate(settingsPrefab, root);
                _settingsPopup = go.GetComponent<PopupSettings>();
                _settingsPopup.Hide();
                if (_settingsPopup.CloseButton != null)
                    _settingsPopup.CloseButton.onClick.AddListener(OnSettingsCloseClicked);
            }

            // PopupGoldShop
            var goldShopPrefab = Resources.Load<GameObject>(GOLDSHOP_PREFAB);
            if (goldShopPrefab != null)
            {
                var go = Instantiate(goldShopPrefab, root);
                _goldShopPopup = go.GetComponent<PopupGoldShop>();
                _goldShopPopup.Hide();
                if (_goldShopPopup.CloseButton != null)
                    _goldShopPopup.CloseButton.onClick.AddListener(OnGoldShopCloseClicked);
            }
        }

        private void WireLobbyButtons()
        {
            if (_lobby == null) return;
            if (_lobby.PlayButton != null) _lobby.PlayButton.onClick.AddListener(OnPlayClicked);
            if (_lobby.SettingsButton != null) _lobby.SettingsButton.onClick.AddListener(OnSettingsClicked);
            if (_lobby.CoinButton != null) _lobby.CoinButton.onClick.AddListener(OnCoinClicked);
        }

        private void RefreshDisplay()
        {
            if (_lobby == null) return;

            if (CurrencyManager.HasInstance)
                _lobby.SetCoinText(CurrencyManager.Instance.Coins);

            int currentStage = 1;
            if (LevelManager.HasInstance)
            {
                int highest = LevelManager.Instance.GetHighestCompletedLevel();
                currentStage = highest > 0 ? highest + 1 : 1;
            }
            _lobby.SetStageText(currentStage);
        }

        // ── Button Handlers ──

        private void OnPlayClicked()
        {
            if (!GameManager.HasInstance) return;
            int levelId = 1;
            if (LevelManager.HasInstance)
            {
                int highest = LevelManager.Instance.GetHighestCompletedLevel();
                levelId = highest > 0 ? highest + 1 : 1;
            }
            GameManager.Instance.StartLevel(levelId);
        }

        private void OnSettingsClicked()
        {
            if (_settingsPopup != null) _settingsPopup.Show();
        }

        private void OnSettingsCloseClicked()
        {
            if (_settingsPopup != null) _settingsPopup.Hide();
        }

        private void OnCoinClicked()
        {
            if (_goldShopPopup != null) _goldShopPopup.Show();
        }

        private void OnGoldShopCloseClicked()
        {
            if (_goldShopPopup != null) _goldShopPopup.Hide();
        }

        private void HandleCoinChanged(OnCoinChanged evt)
        {
            if (_lobby != null) _lobby.SetCoinText(evt.currentCoins);
        }
    }
}
