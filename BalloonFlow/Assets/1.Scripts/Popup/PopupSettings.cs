using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 설정 팝업. PopupCommonFrame 사용.
    /// Single 버튼 레이아웃 (Close).
    /// Lobby, InGame 공용.
    /// </summary>
    public class PopupSettings : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[설정 라벨]")]
        [SerializeField] private Text _soundLabel;
        [SerializeField] private Text _musicLabel;

        public Button CloseButton => _frame != null ? _frame.BtnExit : null;
        public Button HomeButton => _frame != null ? _frame.BtnSingle : null;

        public override void OpenUI()
        {
            if (_frame != null)
            {
                _frame.SetTitle("Settings");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText("Home");
                _frame.ShowExitButton(true);
            }
            base.OpenUI();
        }

        public void SetSoundLabel(bool on)
        {
            if (_soundLabel != null) _soundLabel.text = on ? "Sound: ON" : "Sound: OFF";
        }

        public void SetMusicLabel(bool on)
        {
            if (_musicLabel != null) _musicLabel.text = on ? "Music: ON" : "Music: OFF";
        }
    }
}
