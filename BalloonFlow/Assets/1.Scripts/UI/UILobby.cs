using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 로비 UI. Resources/UI/UILobby 프리팹에서 로드.
    /// </summary>
    public class UILobby : UIBase
    {
        [Header("[버튼]")]
        [SerializeField] private Button _playButton;
        [SerializeField] private Button _coinButton;
        [SerializeField] private Button _settingsButton;

        [Header("[텍스트]")]
        [SerializeField] private Text _playButtonLabel;
        [SerializeField] private Text _coinDisplayText;

        public Button PlayButton => _playButton;
        public Text PlayButtonLabel => _playButtonLabel;
        public Button CoinButton => _coinButton;
        public Text CoinDisplayText => _coinDisplayText;
        public Button SettingsButton => _settingsButton;

        public void SetStageText(int _stage)
        {
            if (_playButtonLabel != null) _playButtonLabel.text = $"Stage {_stage}";
        }

        public void SetCoinText(int _coins)
        {
            if (_coinDisplayText != null) _coinDisplayText.text = _coins.ToString("N0");
        }
    }
}
