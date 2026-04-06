using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 유저 설정 관리. PlayerPrefs 기반 저장/로드.
    /// Sound, Music, Haptic, Notification 4가지 ON/OFF 설정.
    /// 변경 시 EventBus로 OnSettingsChanged 발행.
    /// </summary>
    public class SettingsManager : Singleton<SettingsManager>
    {
        #region Constants

        private const string KEY_SOUND        = "BF_Setting_Sound";
        private const string KEY_MUSIC        = "BF_Setting_Music";
        private const string KEY_HAPTIC       = "BF_Setting_Haptic";
        private const string KEY_NOTIFICATION = "BF_Setting_Notification";

        #endregion

        #region Fields

        private bool _soundOn;
        private bool _musicOn;
        private bool _hapticOn;
        private bool _notificationOn;

        #endregion

        #region Properties

        public bool SoundOn => _soundOn;
        public bool MusicOn => _musicOn;
        public bool HapticOn => _hapticOn;
        public bool NotificationOn => _notificationOn;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            Load();
            ApplyAll();
        }

        #endregion

        #region Public Methods

        public void SetSound(bool on)
        {
            _soundOn = on;
            Save(KEY_SOUND, on);
            ApplySound();
            PublishChanged();
        }

        public void SetMusic(bool on)
        {
            _musicOn = on;
            Save(KEY_MUSIC, on);
            ApplyMusic();
            PublishChanged();
        }

        public void SetHaptic(bool on)
        {
            _hapticOn = on;
            Save(KEY_HAPTIC, on);
            ApplyHaptic();
            PublishChanged();
        }

        public void SetNotification(bool on)
        {
            _notificationOn = on;
            Save(KEY_NOTIFICATION, on);
            PublishChanged();
        }

        public void ToggleSound()   => SetSound(!_soundOn);
        public void ToggleMusic()   => SetMusic(!_musicOn);
        public void ToggleHaptic()  => SetHaptic(!_hapticOn);
        public void ToggleNotification() => SetNotification(!_notificationOn);

        #endregion

        #region Private Methods

        private void Load()
        {
            _soundOn        = PlayerPrefs.GetInt(KEY_SOUND, 1) == 1;
            _musicOn        = PlayerPrefs.GetInt(KEY_MUSIC, 1) == 1;
            _hapticOn       = PlayerPrefs.GetInt(KEY_HAPTIC, 1) == 1;
            _notificationOn = PlayerPrefs.GetInt(KEY_NOTIFICATION, 1) == 1;
        }

        private void Save(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void ApplyAll()
        {
            ApplySound();
            ApplyMusic();
            ApplyHaptic();
        }

        private void ApplySound()
        {
            // SFX 볼륨: AudioListener 또는 개별 AudioSource 제어
            // 현재는 전역 SFX 볼륨으로 처리
            AudioListener.volume = _soundOn ? 1f : (_musicOn ? 1f : 0f);
        }

        private void ApplyMusic()
        {
            // BGM AudioSource가 있으면 mute 제어
            // AudioListener.volume은 Sound와 공유하므로 별도 처리 필요
            // TODO: BGM AudioSource 직접 제어 (현재는 간단 구현)
        }

        private void ApplyHaptic()
        {
            // FeedbackController의 _hapticEnabled 연동
            if (FeedbackController.HasInstance)
            {
                FeedbackController.Instance.SetHapticEnabled(_hapticOn);
            }
        }

        private void PublishChanged()
        {
            EventBus.Publish(new OnSettingsChanged
            {
                soundOn = _soundOn,
                musicOn = _musicOn,
                hapticOn = _hapticOn,
                notificationOn = _notificationOn
            });
        }

        #endregion
    }
}
