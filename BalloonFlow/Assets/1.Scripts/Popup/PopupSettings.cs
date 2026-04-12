using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 설정 팝업. Sound/Music/Haptic 토글.
    /// PopupCommonFrame 사용. Lobby, InGame 공용.
    /// </summary>
    public class PopupSettings : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Sound Toggle]")]
        [SerializeField] private Button _btnSound;
        [SerializeField] private GameObject _soundOn;
        [SerializeField] private GameObject _soundOff;

        [Header("[Music Toggle]")]
        [SerializeField] private Button _btnMusic;
        [SerializeField] private GameObject _musicOn;
        [SerializeField] private GameObject _musicOff;

        [Header("[Haptic Toggle]")]
        [SerializeField] private Button _btnHaptic;
        [SerializeField] private GameObject _hapticOn;
        [SerializeField] private GameObject _hapticOff;

        public Button CloseButton => _frame != null ? _frame.BtnExit : null;
        public Button HomeButton => _frame != null ? _frame.BtnSingle : null;

        protected override void Awake()
        {
            base.Awake();
            if (_btnSound != null) _btnSound.onClick.AddListener(OnSoundClicked);
            if (_btnMusic != null) _btnMusic.onClick.AddListener(OnMusicClicked);
            if (_btnHaptic != null) _btnHaptic.onClick.AddListener(OnHapticClicked);

            EventBus.Subscribe<OnSettingsChanged>(HandleSettingsChanged);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_btnSound != null) _btnSound.onClick.RemoveAllListeners();
            if (_btnMusic != null) _btnMusic.onClick.RemoveAllListeners();
            if (_btnHaptic != null) _btnHaptic.onClick.RemoveAllListeners();

            EventBus.Unsubscribe<OnSettingsChanged>(HandleSettingsChanged);
        }

        public override void OpenUI()
        {
            if (_frame != null)
            {
                _frame.SetTitle("Settings");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText("Home");
                _frame.ShowExitButton(true);
            }

            RefreshToggles();
            base.OpenUI();
        }

        private void OnSoundClicked()
        {
            if (SettingsManager.HasInstance)
                SettingsManager.Instance.ToggleSound();
        }

        private void OnMusicClicked()
        {
            if (SettingsManager.HasInstance)
                SettingsManager.Instance.ToggleMusic();
        }

        private void OnHapticClicked()
        {
            if (SettingsManager.HasInstance)
                SettingsManager.Instance.ToggleHaptic();
        }

        private void HandleSettingsChanged(OnSettingsChanged evt)
        {
            UpdateToggle(_soundOn, _soundOff, evt.soundOn);
            UpdateToggle(_musicOn, _musicOff, evt.musicOn);
            UpdateToggle(_hapticOn, _hapticOff, evt.hapticOn);
        }

        private void RefreshToggles()
        {
            if (!SettingsManager.HasInstance) return;
            var sm = SettingsManager.Instance;
            UpdateToggle(_soundOn, _soundOff, sm.SoundOn);
            UpdateToggle(_musicOn, _musicOff, sm.MusicOn);
            UpdateToggle(_hapticOn, _hapticOff, sm.HapticOn);
        }

        private static void UpdateToggle(GameObject onObj, GameObject offObj, bool isOn)
        {
            if (onObj != null) onObj.SetActive(isOn);
            if (offObj != null) offObj.SetActive(!isOn);
        }
    }
}
