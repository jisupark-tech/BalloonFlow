using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Lobby 씬 컨트롤러.
    /// - GameManager.InitLobby() → 경제/상점/레벨 매니저 초기화
    /// - UIManager.OpenUI로 UILobby, PopupSettings, PopupGoldShop 로드
    /// - 버튼 이벤트 직접 연결
    /// </summary>
    public class LobbyController : MonoBehaviour
    {
        private UILobby _lobby;
        private PopupSettings _settings;
        private PopupGoldShop _goldShop;

        void Start()
        {
            // Safety: 직접 씬 로드 테스트용
            if (!GameManager.HasInstance)
            {
                var _go = new GameObject("GameManager");
                _go.AddComponent<GameManager>();
            }

            // Lobby 매니저 초기화
            GameManager.Instance.InitLobby();

            // 카메라 설정
            if (CameraManager.HasInstance)
                CameraManager.Instance.ConfigureLobby();

            // 씬 캔버스 등록
            var _canvasGO = GameObject.Find("Canvas");
            if (_canvasGO != null && UIManager.HasInstance)
                UIManager.Instance.SetSceneCanvas(_canvasGO.transform);

            // UI 로드
            LoadUI();
            RefreshDisplay();
        }

        void OnEnable()
        {
            EventBus.Subscribe<OnCoinChanged>(HandleCoinChanged);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<OnCoinChanged>(HandleCoinChanged);

            if (_lobby != null)
            {
                if (_lobby.PlayButton != null) _lobby.PlayButton.onClick.RemoveListener(OnPlayClicked);
                if (_lobby.SettingsButton != null) _lobby.SettingsButton.onClick.RemoveListener(OnSettingsClicked);
                if (_lobby.CoinButton != null) _lobby.CoinButton.onClick.RemoveListener(OnCoinClicked);
            }
            if (_settings != null && _settings.CloseButton != null)
                _settings.CloseButton.onClick.RemoveListener(OnSettingsClose);
            if (_goldShop != null && _goldShop.CloseButton != null)
                _goldShop.CloseButton.onClick.RemoveListener(OnGoldShopClose);
        }

        #region UI 로드

        void LoadUI()
        {
            if (!UIManager.HasInstance) return;

            // UILobby
            _lobby = UIManager.Instance.OpenUI<UILobby>("UI/UILobby");
            if (_lobby != null)
            {
                if (_lobby.PlayButton != null) _lobby.PlayButton.onClick.AddListener(OnPlayClicked);
                if (_lobby.SettingsButton != null) _lobby.SettingsButton.onClick.AddListener(OnSettingsClicked);
                if (_lobby.CoinButton != null) _lobby.CoinButton.onClick.AddListener(OnCoinClicked);
            }

            // PopupSettings (로드 후 숨김)
            _settings = UIManager.Instance.OpenUI<PopupSettings>("Popup/PopupSettings");
            if (_settings != null)
            {
                _settings.CloseUI();
                if (_settings.CloseButton != null)
                    _settings.CloseButton.onClick.AddListener(OnSettingsClose);
            }

            // PopupGoldShop (로드 후 숨김)
            _goldShop = UIManager.Instance.OpenUI<PopupGoldShop>("Popup/PopupGoldShop");
            if (_goldShop != null)
            {
                _goldShop.CloseUI();
                if (_goldShop.CloseButton != null)
                    _goldShop.CloseButton.onClick.AddListener(OnGoldShopClose);
            }
        }

        void RefreshDisplay()
        {
            if (_lobby == null) return;

            if (CurrencyManager.HasInstance)
                _lobby.SetCoinText(CurrencyManager.Instance.Coins);

            int _stage = 1;
            if (LevelManager.HasInstance)
            {
                int _highest = LevelManager.Instance.GetHighestCompletedLevel();
                _stage = _highest > 0 ? _highest + 1 : 1;
            }
            _lobby.SetStageText(_stage);
        }

        #endregion

        #region 버튼 이벤트

        void OnPlayClicked()
        {
            if (!GameManager.HasInstance) return;
            int _levelId = 1;
            if (LevelManager.HasInstance)
            {
                int _highest = LevelManager.Instance.GetHighestCompletedLevel();
                _levelId = _highest > 0 ? _highest + 1 : 1;
            }
            GameManager.Instance.StartLevel(_levelId);
        }

        void OnSettingsClicked()
        {
            if (_settings != null) _settings.OpenUI();
        }

        void OnSettingsClose()
        {
            if (_settings != null) _settings.CloseUI();
        }

        void OnCoinClicked()
        {
            if (_goldShop != null) _goldShop.OpenUI();
        }

        void OnGoldShopClose()
        {
            if (_goldShop != null) _goldShop.CloseUI();
        }

        void HandleCoinChanged(OnCoinChanged _evt)
        {
            if (_lobby != null) _lobby.SetCoinText(_evt.currentCoins);
        }

        #endregion
    }
}
