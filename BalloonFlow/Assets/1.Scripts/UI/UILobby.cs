using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// Lobby page view. Loaded from Resources/UI/UILobby prefab.
    /// All child references wired via UIPrefabBuilder at editor-time.
    /// </summary>
    public class UILobby : MonoBehaviour
    {
        [SerializeField] private Button _playButton;
        [SerializeField] private Text _playButtonLabel;
        [SerializeField] private Button _coinButton;
        [SerializeField] private Text _coinDisplayText;
        [SerializeField] private Button _settingsButton;

        public Button PlayButton => _playButton;
        public Text PlayButtonLabel => _playButtonLabel;
        public Button CoinButton => _coinButton;
        public Text CoinDisplayText => _coinDisplayText;
        public Button SettingsButton => _settingsButton;

        public void SetStageText(int stage)
        {
            if (_playButtonLabel != null) _playButtonLabel.text = $"Stage {stage}";
        }

        public void SetCoinText(int coins)
        {
            if (_coinDisplayText != null) _coinDisplayText.text = coins.ToString("N0");
        }
    }
}
