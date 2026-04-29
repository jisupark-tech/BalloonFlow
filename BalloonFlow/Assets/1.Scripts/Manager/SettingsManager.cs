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

        private const string KEY_SOUND             = "BF_Setting_Sound";
        private const string KEY_MUSIC             = "BF_Setting_Music";
        private const string KEY_HAPTIC            = "BF_Setting_Haptic";
        private const string KEY_NOTIFICATION      = "BF_Setting_Notification";
        private const string KEY_HAPTIC_INTENSITY  = "BF_Setting_HapticIntensity";
        private const string KEY_HAPTIC_DURATION   = "BF_Setting_HapticDuration";

        private const float DEFAULT_HAPTIC_INTENSITY = 1f;
        private const float DEFAULT_HAPTIC_DURATION  = 1f;

        #endregion

        #region Fields

        private bool _soundOn;
        private bool _musicOn;
        private bool _hapticOn;
        private bool _notificationOn;
        private float _hapticIntensity = DEFAULT_HAPTIC_INTENSITY;
        private float _hapticDuration  = DEFAULT_HAPTIC_DURATION;

        #endregion

        #region Properties

        public bool SoundOn => _soundOn;
        public bool MusicOn => _musicOn;
        public bool HapticOn => _hapticOn;
        public bool NotificationOn => _notificationOn;

        /// <summary>진동 강도 multiplier (0~1). VibrationManager.Vibrate amplitude에 곱해짐.</summary>
        public float HapticIntensity => _hapticIntensity;

        /// <summary>진동 지속시간 multiplier (0~1). VibrationManager.Vibrate ms에 곱해짐.</summary>
        public float HapticDuration => _hapticDuration;

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

        /// <summary>진동 강도 multiplier 설정 (0~1).</summary>
        public void SetHapticIntensity(float v)
        {
            float clamped = Mathf.Clamp01(v);
            if (Mathf.Approximately(clamped, _hapticIntensity)) return;
            _hapticIntensity = clamped;
            PlayerPrefs.SetFloat(KEY_HAPTIC_INTENSITY, _hapticIntensity);
            PlayerPrefs.Save();
            PublishChanged();
        }

        /// <summary>진동 지속시간 multiplier 설정 (0~1).</summary>
        public void SetHapticDuration(float v)
        {
            float clamped = Mathf.Clamp01(v);
            if (Mathf.Approximately(clamped, _hapticDuration)) return;
            _hapticDuration = clamped;
            PlayerPrefs.SetFloat(KEY_HAPTIC_DURATION, _hapticDuration);
            PlayerPrefs.Save();
            PublishChanged();
        }

        #endregion

        #region Private Methods

        private void Load()
        {
            _soundOn        = PlayerPrefs.GetInt(KEY_SOUND, 1) == 1;
            _musicOn        = PlayerPrefs.GetInt(KEY_MUSIC, 1) == 1;
            _hapticOn       = PlayerPrefs.GetInt(KEY_HAPTIC, 1) == 1;
            _notificationOn = PlayerPrefs.GetInt(KEY_NOTIFICATION, 1) == 1;
            _hapticIntensity = Mathf.Clamp01(PlayerPrefs.GetFloat(KEY_HAPTIC_INTENSITY, DEFAULT_HAPTIC_INTENSITY));
            _hapticDuration  = Mathf.Clamp01(PlayerPrefs.GetFloat(KEY_HAPTIC_DURATION,  DEFAULT_HAPTIC_DURATION));
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
                notificationOn = _notificationOn,
                hapticIntensity = _hapticIntensity,
                hapticDuration  = _hapticDuration
            });
        }

        #endregion
    }
}
