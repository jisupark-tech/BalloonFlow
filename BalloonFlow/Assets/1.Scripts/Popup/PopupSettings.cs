using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 설정 팝업. Resources/Popup/PopupSettings 프리팹에서 로드.
    /// Lobby, InGame 공용.
    /// </summary>
    public class PopupSettings : UIBase
    {
        [Header("[버튼]")]
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _homeButton;

        [Header("[설정 라벨]")]
        [SerializeField] private Text _soundLabel;
        [SerializeField] private Text _musicLabel;

        public Button CloseButton => _closeButton;
        public Button HomeButton => _homeButton;

        public void SetSoundLabel(bool _on)
        {
            if (_soundLabel != null) _soundLabel.text = _on ? "Sound: ON" : "Sound: OFF";
        }

        public void SetMusicLabel(bool _on)
        {
            if (_musicLabel != null) _musicLabel.text = _on ? "Music: ON" : "Music: OFF";
        }
    }
}
