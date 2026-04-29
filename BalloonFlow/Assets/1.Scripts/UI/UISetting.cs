using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// Setting page — UILobby PageContainer 오른쪽 페이지.
    /// Sound, Music, Haptic, Notification 4가지 토글.
    /// 각 항목: Button(토글) + On/Off Image + Label Text.
    /// SettingsManager와 연동하여 PlayerPrefs 저장/로드.
    /// </summary>
    public class UISetting : UIBase
    {
        #region Serialized Fields

        [Header("[Title]")]
        [SerializeField] private TMP_Text _txtTitle;
        [SerializeField] private TMP_Text _txtTitleOutline;

        [Header("[Sound]")]
        [SerializeField] private Button _btnSound;
        [SerializeField] private GameObject _soundOn;
        [SerializeField] private GameObject _soundOff;
        [SerializeField] private TMP_Text _txtSound;

        [Header("[Music]")]
        [SerializeField] private Button _btnMusic;
        [SerializeField] private GameObject _musicOn;
        [SerializeField] private GameObject _musicOff;
        [SerializeField] private TMP_Text _txtMusic;

        [Header("[Haptic]")]
        [SerializeField] private Button _btnHaptic;
        [SerializeField] private GameObject _hapticOn;
        [SerializeField] private GameObject _hapticOff;
        [SerializeField] private TMP_Text _txtHaptic;

        [Header("[Haptic Intensity — 진동 강도 슬라이더 (0~1)]")]
        [SerializeField] private Slider _sliderHapticIntensity;
        [Tooltip("선택. 슬라이더 옆에 % 또는 수치 표시할 텍스트")]
        [SerializeField] private TMP_Text _txtHapticIntensityValue;
        [Tooltip("선택. 강도 슬라이더 행을 통째로 숨기고 싶을 때 (Haptic OFF 시)")]
        [SerializeField] private GameObject _hapticIntensityRow;

        [Header("[Haptic Duration — 진동 지속시간 슬라이더 (0~1)]")]
        [SerializeField] private Slider _sliderHapticDuration;
        [SerializeField] private TMP_Text _txtHapticDurationValue;
        [SerializeField] private GameObject _hapticDurationRow;

        [Header("[Notification]")]
        [SerializeField] private Button _btnNotification;
        [SerializeField] private GameObject _notificationOn;
        [SerializeField] private GameObject _notificationOff;
        [SerializeField] private TMP_Text _txtNotification;
        [SerializeField] private TMP_Text _txtNotificationOutline;
        [SerializeField] private TMP_Text _txtNotificationOn;
        [SerializeField] private TMP_Text _txtNotificationOnOutline;
        [SerializeField] private TMP_Text _txtNotificationOff;
        [SerializeField] private TMP_Text _txtNotificationOffOutline;

        [Header("[Notification — 사양: ToggleBtn 이동 + Frame 스프라이트 교체]")]
        [SerializeField] private RectTransform _notificationToggleBtn;
        [SerializeField] private Image _frameNotification;
        [SerializeField] private Sprite _sprNotificationOn;
        [SerializeField] private Sprite _sprNotificationOff;
        private const float NOTIFICATION_TOGGLE_X_ON  = 96f;
        private const float NOTIFICATION_TOGGLE_X_OFF = -96f;
        private const float NOTIFICATION_TOGGLE_DUR   = 0.15f;

        #endregion

        #region Lifecycle

        protected override void Awake()
        {
            base.Awake();

            if (_btnSound != null) _btnSound.onClick.AddListener(OnSoundClicked);
            if (_btnMusic != null) _btnMusic.onClick.AddListener(OnMusicClicked);
            if (_btnHaptic != null) _btnHaptic.onClick.AddListener(OnHapticClicked);
            if (_btnNotification != null) _btnNotification.onClick.AddListener(OnNotificationClicked);

            if (_sliderHapticIntensity != null)
            {
                _sliderHapticIntensity.minValue = 0f;
                _sliderHapticIntensity.maxValue = 1f;
                _sliderHapticIntensity.onValueChanged.AddListener(OnHapticIntensityChanged);
            }
            if (_sliderHapticDuration != null)
            {
                _sliderHapticDuration.minValue = 0f;
                _sliderHapticDuration.maxValue = 1f;
                _sliderHapticDuration.onValueChanged.AddListener(OnHapticDurationChanged);
            }
        }

        private void OnEnable()
        {
            if (_txtTitle != null) _txtTitle.text = "Settings";
            if (_txtTitleOutline != null) _txtTitleOutline.text = "Settings";
            RefreshAll();
            EventBus.Subscribe<OnSettingsChanged>(HandleSettingsChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnSettingsChanged>(HandleSettingsChanged);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_btnSound != null) _btnSound.onClick.RemoveListener(OnSoundClicked);
            if (_btnMusic != null) _btnMusic.onClick.RemoveListener(OnMusicClicked);
            if (_btnHaptic != null) _btnHaptic.onClick.RemoveListener(OnHapticClicked);
            if (_btnNotification != null) _btnNotification.onClick.RemoveListener(OnNotificationClicked);

            if (_sliderHapticIntensity != null) _sliderHapticIntensity.onValueChanged.RemoveListener(OnHapticIntensityChanged);
            if (_sliderHapticDuration  != null) _sliderHapticDuration.onValueChanged.RemoveListener(OnHapticDurationChanged);
        }

        #endregion

        #region Button Handlers

        private void OnSoundClicked()
        {
            if (SettingsManager.HasInstance) SettingsManager.Instance.ToggleSound();
        }

        private void OnMusicClicked()
        {
            if (SettingsManager.HasInstance) SettingsManager.Instance.ToggleMusic();
        }

        private void OnHapticClicked()
        {
            if (SettingsManager.HasInstance) SettingsManager.Instance.ToggleHaptic();
        }

        private void OnNotificationClicked()
        {
            if (SettingsManager.HasInstance) SettingsManager.Instance.ToggleNotification();
        }

        private void OnHapticIntensityChanged(float v)
        {
            if (SettingsManager.HasInstance) SettingsManager.Instance.SetHapticIntensity(v);
            UpdateHapticIntensityLabel(v);
        }

        private void OnHapticDurationChanged(float v)
        {
            if (SettingsManager.HasInstance) SettingsManager.Instance.SetHapticDuration(v);
            UpdateHapticDurationLabel(v);
        }

        #endregion

        #region Display

        private void RefreshAll()
        {
            // ON/OFF 텍스트 대소문자 통일 (prefab 기본값 "On"/"OFF" → "ON"/"OFF")
            EnsureToggleLabel(_soundOn, "ON");
            EnsureToggleLabel(_soundOff, "OFF");
            EnsureToggleLabel(_musicOn, "ON");
            EnsureToggleLabel(_musicOff, "OFF");
            EnsureToggleLabel(_hapticOn, "ON");
            EnsureToggleLabel(_hapticOff, "OFF");
            EnsureToggleLabel(_notificationOn, "ON");
            EnsureToggleLabel(_notificationOff, "OFF");

            if (_txtNotificationOn != null) _txtNotificationOn.text = "ON";
            if (_txtNotificationOnOutline != null) _txtNotificationOnOutline.text = "ON";
            if (_txtNotificationOff != null) _txtNotificationOff.text = "OFF";
            if (_txtNotificationOffOutline != null) _txtNotificationOffOutline.text = "OFF";

            if (!SettingsManager.HasInstance) return;

            var sm = SettingsManager.Instance;
            UpdateToggle(_soundOn, _soundOff, sm.SoundOn);
            UpdateToggle(_musicOn, _musicOff, sm.MusicOn);
            UpdateToggle(_hapticOn, _hapticOff, sm.HapticOn);
            UpdateToggle(_notificationOn, _notificationOff, sm.NotificationOn);
            ApplyNotificationSpec(sm.NotificationOn, animate: false);

            ApplyHapticSliders(sm.HapticIntensity, sm.HapticDuration, sm.HapticOn);
        }

        /// <summary>
        /// Notification 사양 적용.
        /// On: ToggleBtn x=+96, OnOutline 노출, Frame=notificationOn 스프라이트.
        /// Off: ToggleBtn x=-96, OffOutline 노출, Frame=notificationOff 스프라이트.
        /// </summary>
        private void ApplyNotificationSpec(bool isOn, bool animate)
        {
            if (_notificationToggleBtn != null)
            {
                float targetX = isOn ? NOTIFICATION_TOGGLE_X_ON : NOTIFICATION_TOGGLE_X_OFF;
                _notificationToggleBtn.DOKill();
                if (animate)
                    _notificationToggleBtn.DOAnchorPosX(targetX, NOTIFICATION_TOGGLE_DUR).SetEase(Ease.OutCubic);
                else
                    _notificationToggleBtn.anchoredPosition = new Vector2(targetX, _notificationToggleBtn.anchoredPosition.y);
            }

            if (_txtNotificationOnOutline != null) _txtNotificationOnOutline.gameObject.SetActive(isOn);
            if (_txtNotificationOffOutline != null) _txtNotificationOffOutline.gameObject.SetActive(!isOn);

            if (_frameNotification != null)
            {
                Sprite target = isOn ? _sprNotificationOn : _sprNotificationOff;
                if (target != null) _frameNotification.sprite = target;
            }
        }

        private static void EnsureToggleLabel(GameObject obj, string label)
        {
            if (obj == null) return;
            var txt = obj.GetComponentInChildren<TMP_Text>(true);
            if (txt != null) txt.text = label;
        }

        private void UpdateToggle(GameObject onObj, GameObject offObj, bool isOn)
        {
            if (onObj != null) onObj.SetActive(isOn);
            if (offObj != null) offObj.SetActive(!isOn);
        }

        private void HandleSettingsChanged(OnSettingsChanged evt)
        {
            UpdateToggle(_soundOn, _soundOff, evt.soundOn);
            UpdateToggle(_musicOn, _musicOff, evt.musicOn);
            UpdateToggle(_hapticOn, _hapticOff, evt.hapticOn);
            UpdateToggle(_notificationOn, _notificationOff, evt.notificationOn);
            ApplyNotificationSpec(evt.notificationOn, animate: true);

            ApplyHapticSliders(evt.hapticIntensity, evt.hapticDuration, evt.hapticOn);
        }

        /// <summary>슬라이더 위치 + 라벨 + Haptic OFF 시 행 숨김 동기화.</summary>
        private void ApplyHapticSliders(float intensity, float duration, bool hapticOn)
        {
            // SetValueWithoutNotify 로 onValueChanged 재발화 방지 (무한 루프 차단)
            if (_sliderHapticIntensity != null)
            {
                _sliderHapticIntensity.SetValueWithoutNotify(intensity);
                _sliderHapticIntensity.interactable = hapticOn;
            }
            if (_sliderHapticDuration != null)
            {
                _sliderHapticDuration.SetValueWithoutNotify(duration);
                _sliderHapticDuration.interactable = hapticOn;
            }
            UpdateHapticIntensityLabel(intensity);
            UpdateHapticDurationLabel(duration);

            // 행 자체 노출/숨김 (선택) — Haptic OFF면 슬라이더 행을 숨겨 UI 깔끔하게
            if (_hapticIntensityRow != null) _hapticIntensityRow.SetActive(hapticOn);
            if (_hapticDurationRow  != null) _hapticDurationRow.SetActive(hapticOn);
        }

        private void UpdateHapticIntensityLabel(float v)
        {
            if (_txtHapticIntensityValue != null)
                _txtHapticIntensityValue.text = Mathf.RoundToInt(v * 100f) + "%";
        }

        private void UpdateHapticDurationLabel(float v)
        {
            if (_txtHapticDurationValue != null)
                _txtHapticDurationValue.text = Mathf.RoundToInt(v * 100f) + "%";
        }

        #endregion
    }
}
