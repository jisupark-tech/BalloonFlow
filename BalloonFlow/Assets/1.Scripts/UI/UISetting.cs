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
        // [#15] 씬 UI(로비 세팅 탭) — 백버튼 비소비. 로비 컨텍스트라 라우터가 Quit Game 처리.
        public override bool ConsumesBackButton => false;

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

        // [2026-05-12] Haptic Intensity / Duration Slider 삭제 — default 0.3 intensity / 0.18s duration 고정 사용.
        // 관련 SerializeField 제거. prefab 의 wire 는 자동 무시 (missing reference).

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

        [Header("[Legal / Support]")]
        [Tooltip("개인정보처리방침 페이지로 이동 (외부 브라우저)")]
        [SerializeField] private Button _btnPrivacy;
        [Tooltip("이용약관 페이지로 이동 (외부 브라우저)")]
        [SerializeField] private Button _btnTerms;
        [Tooltip("이메일 문의 (mailto:support@aimed.xyz)")]
        [SerializeField] private Button _btnSupports;

        // [TODO] 실제 정책 페이지 URL 로 교체 — 현재는 support 이메일 도메인(aimed.xyz) 기반 추정 placeholder.
        private const string URL_PRIVACY   = "https://aimed.xyz/privacy";
        private const string URL_TERMS     = "https://aimed.xyz/terms";
        private const string SUPPORT_EMAIL = "support@aimed.xyz";

        #endregion

        #region Lifecycle

        protected override void Awake()
        {
            base.Awake();

            if (_btnSound != null) _btnSound.onClick.AddListener(OnSoundClicked);
            if (_btnMusic != null) _btnMusic.onClick.AddListener(OnMusicClicked);
            if (_btnHaptic != null) _btnHaptic.onClick.AddListener(OnHapticClicked);
            if (_btnNotification != null) _btnNotification.onClick.AddListener(OnNotificationClicked);

            if (_btnPrivacy != null) _btnPrivacy.onClick.AddListener(OnPrivacyClicked);
            if (_btnTerms != null) _btnTerms.onClick.AddListener(OnTermsClicked);
            if (_btnSupports != null) _btnSupports.onClick.AddListener(OnSupportsClicked);
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
            if (_btnPrivacy != null) _btnPrivacy.onClick.RemoveListener(OnPrivacyClicked);
            if (_btnTerms != null) _btnTerms.onClick.RemoveListener(OnTermsClicked);
            if (_btnSupports != null) _btnSupports.onClick.RemoveListener(OnSupportsClicked);
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

        private async void OnNotificationClicked()
        {
            if (!SettingsManager.HasInstance) return;
            var sm = SettingsManager.Instance;

            // OFF → ON 전환: OS 권한 확인 후 처리. (업계 표준 — 권한 거부 상태로 토글만 ON 되는 상태 방지)
            if (!sm.NotificationOn && NotificationManager.HasInstance)
            {
                var nm = NotificationManager.Instance;
                nm.RefreshPermissionStatus();

                if (nm.Status == NotificationManager.PermissionState.Denied)
                {
                    // 한 번 거부된 권한은 앱에서 재요청 불가 → OS 설정 화면 deep link.
                    nm.OpenSystemNotificationSettings();
                    return;
                }

                if (nm.Status == NotificationManager.PermissionState.NotDetermined)
                {
                    bool granted = await nm.RequestPermissionAsync();
                    if (!granted) return; // 거부 시 앱 내 토글도 ON 시키지 않음.
                }
            }

            sm.ToggleNotification();
        }

        /// <summary>개인정보처리방침 페이지를 외부 브라우저로 연다.</summary>
        private void OnPrivacyClicked() => OpenUrlSafe(URL_PRIVACY);

        /// <summary>이용약관 페이지를 외부 브라우저로 연다.</summary>
        private void OnTermsClicked() => OpenUrlSafe(URL_TERMS);

        /// <summary>support 이메일로 문의 메일 작성 화면을 연다 (mailto).</summary>
        private void OnSupportsClicked()
        {
            string subject = System.Uri.EscapeDataString("[BalloonFlow] 문의");
            OpenUrlSafe($"mailto:{SUPPORT_EMAIL}?subject={subject}");
        }

        private static void OpenUrlSafe(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try { Application.OpenURL(url); }
            catch (System.Exception ex) { Debug.LogWarning($"[UISetting] OpenURL 실패: {url} — {ex.Message}"); }
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
        }

        #endregion
    }
}
