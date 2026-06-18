using System;
using System.Threading.Tasks;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using Unity.Notifications.Android;
#endif
#if UNITY_IOS && !UNITY_EDITOR
using Unity.Notifications.iOS;
#endif

namespace BalloonFlow
{
    /// <summary>
    /// 로컬 푸시 알림 매니저. 권한 흐름 + 스케줄링 + 설정 토글 연동.
    /// 업계 표준 — 가치 입증 후 권한 요청(첫 레벨 클리어), OS 설정 deep link, 첫 24h 가드.
    /// 사양: BalloonFlow_아웃게임디렉션.md §9 / LiveOps_디렉션.md §9
    ///
    /// Phase 1(1.0): #1 하트 풀충전 로컬 알림만 처리.
    /// Phase 2(1.0.x): #2 이탈 복귀, #3 데일리 보상 — 서버(FCM) 발송.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Manager | Phase: 1
    /// </remarks>
    public class NotificationManager : Singleton<NotificationManager>
    {
        #region Constants

        public const string ANDROID_CHANNEL_ID   = "balloonflow_default";
        public const string ANDROID_CHANNEL_NAME = "Balloon Loop";

        private const string PREFS_INSTALL_UTC      = "BF_NotifInstallUtcTicks";
        private const string PREFS_FIRST_PERM_ASKED = "BF_NotifFirstPermAsked";

        /// <summary>설치 후 24시간 가드 (아웃게임 §9 L675). 신규 유저 24h 가드 — 모든 알림에 적용.</summary>
        private static readonly TimeSpan FirstDayGuard = TimeSpan.FromHours(24);

        #endregion

        #region Types

        public enum PermissionState
        {
            NotDetermined,
            Granted,
            Denied
        }

        #endregion

        #region Fields

        private long _installUtcTicks;
        private PermissionState _cachedState = PermissionState.NotDetermined;
        private bool _firstPermAsked;

        #endregion

        #region Properties

        /// <summary>OS 권한 상태 (마지막 refresh 시점 기준).</summary>
        public PermissionState Status => _cachedState;

        /// <summary>SettingsManager 의 사용자 토글.</summary>
        public bool ToggleOn =>
            SettingsManager.HasInstance && SettingsManager.Instance.NotificationOn;

        /// <summary>토글 ON + OS 권한 Granted — 알림 발송 가능 상태.</summary>
        public bool CanSend => ToggleOn && _cachedState == PermissionState.Granted;

        /// <summary>설치 후 24h 이내 — 신규 유저 24h 가드 적용 시 알림 미발송 (§9 L675).</summary>
        public bool IsWithinFirst24Hours
        {
            get
            {
                var elapsed = DateTime.UtcNow - new DateTime(_installUtcTicks, DateTimeKind.Utc);
                return elapsed < FirstDayGuard;
            }
        }

        #endregion

        #region Lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate() => EnsureCreated();

        /// <summary>인스턴스가 없으면 생성·초기화(채널등록·권한상태 refresh)를 보장. AfterSceneLoad 타이밍이
        /// 호출자(예: TitleController.Start)보다 늦어 HasInstance 가 false 인 경우를 위한 명시적 보장용.</summary>
        public static void EnsureCreated()
        {
            if (HasInstance) return;
            var go = new GameObject("NotificationManager");
            go.AddComponent<NotificationManager>();
        }

        protected override void OnSingletonAwake()
        {
            LoadInstallTime();
            _firstPermAsked = PlayerPrefs.GetInt(PREFS_FIRST_PERM_ASKED, 0) == 1;

            RegisterAndroidChannel();
            RefreshPermissionStatus();

            EventBus.Subscribe<OnSettingsChanged>(HandleSettingsChanged);
            EventBus.Subscribe<OnLifeChanged>(HandleLifeChanged);
            EventBus.Subscribe<OnLevelCompleted>(HandleLevelCompleted);

            // 토글 OFF 상태로 시작 시 잔존 예약 정리.
            if (!ToggleOn) CancelAll();
        }

        protected override void OnDestroy()
        {
            EventBus.Unsubscribe<OnSettingsChanged>(HandleSettingsChanged);
            EventBus.Unsubscribe<OnLifeChanged>(HandleLifeChanged);
            EventBus.Unsubscribe<OnLevelCompleted>(HandleLevelCompleted);
            base.OnDestroy();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) return;
            // OS 설정에서 권한이 변경됐을 수 있음 — refresh.
            RefreshPermissionStatus();
            // 시간 흐름 반영해 하트 풀충전 알림 재계산.
            RescheduleHeartFull();
            // 앱 안에 들어왔으므로 표시된 알림은 정리.
            DismissAllDelivered();
        }

        #endregion

        #region Permission API

        /// <summary>
        /// 권한 요청 다이얼로그를 띄움. 이미 결정된 상태면 결과만 반환.
        /// Android 13+ POST_NOTIFICATIONS / iOS UNUserNotificationCenter.
        /// </summary>
        public async Task<bool> RequestPermissionAsync()
        {
            _firstPermAsked = true;
            PlayerPrefs.SetInt(PREFS_FIRST_PERM_ASKED, 1);
            PlayerPrefs.Save();

#if UNITY_ANDROID && !UNITY_EDITOR
            if (AndroidNotificationCenter.UserPermissionToPost
                == Unity.Notifications.Android.PermissionStatus.Allowed)
            {
                _cachedState = PermissionState.Granted;
                return true;
            }
            var request = new PermissionRequest();
            while (request.Status == Unity.Notifications.Android.PermissionStatus.RequestPending)
                await Task.Yield();
            _cachedState = request.Status
                == Unity.Notifications.Android.PermissionStatus.Allowed
                ? PermissionState.Granted : PermissionState.Denied;
            return _cachedState == PermissionState.Granted;
#elif UNITY_IOS && !UNITY_EDITOR
            // registerForRemoteNotifications: true — APNs 등록까지 같이 진행해야
            // Firebase Messaging 이 APNs token 받고 FCM token 발급 가능 (Phase 2).
            using (var req = new AuthorizationRequest(
                AuthorizationOption.Alert | AuthorizationOption.Badge | AuthorizationOption.Sound,
                registerForRemoteNotifications: true))
            {
                while (!req.IsFinished) await Task.Yield();
                _cachedState = req.Granted ? PermissionState.Granted : PermissionState.Denied;
                return req.Granted;
            }
#else
            // Editor mock — 권한 시스템 없음. 항상 Granted 로 간주.
            _cachedState = PermissionState.Granted;
            await Task.Yield();
            Debug.Log("[Notif/EditorMock] Permission auto-granted.");
            return true;
#endif
        }

        /// <summary>OS 권한 상태 재조회. OS 설정 변경 후 호출.</summary>
        public void RefreshPermissionStatus()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _cachedState = AndroidNotificationCenter.UserPermissionToPost switch
            {
                Unity.Notifications.Android.PermissionStatus.Allowed => PermissionState.Granted,
                Unity.Notifications.Android.PermissionStatus.Denied  => PermissionState.Denied,
                _ => PermissionState.NotDetermined,
            };
#elif UNITY_IOS && !UNITY_EDITOR
            var settings = iOSNotificationCenter.GetNotificationSettings();
            _cachedState = settings.AuthorizationStatus switch
            {
                AuthorizationStatus.Authorized => PermissionState.Granted,
                AuthorizationStatus.Provisional => PermissionState.Granted,
                AuthorizationStatus.Ephemeral  => PermissionState.Granted,
                AuthorizationStatus.Denied     => PermissionState.Denied,
                _ => PermissionState.NotDetermined,
            };
#else
            _cachedState = PermissionState.Granted;
#endif
        }

        /// <summary>OS 설정 화면 deep link. 권한 거부 후 재요청 불가 케이스에서 사용.</summary>
        public void OpenSystemNotificationSettings()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
                using var intent = new AndroidJavaObject(
                    "android.content.Intent", "android.settings.APP_NOTIFICATION_SETTINGS");
                intent.Call<AndroidJavaObject>("putExtra",
                    "android.provider.extra.APP_PACKAGE",
                    activity.Call<string>("getPackageName"));
                activity.Call("startActivity", intent);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Notif] OS settings deep link 실패: {ex.Message}");
            }
#elif UNITY_IOS && !UNITY_EDITOR
            Application.OpenURL("app-settings:");
#else
            Debug.Log("[Notif/EditorMock] OS 설정 화면 (mock).");
#endif
        }

        #endregion

        #region Schedule API

        /// <summary>
        /// 로컬 알림 스케줄. fireAt 은 UTC.
        /// 토글 OFF / 권한 미허용 / 과거 시각이면 no-op.
        /// </summary>
        /// <param name="respectFirst24h">true 면 §9 L675 신규 유저 24h 가드 적용 (기본 정책: 모든 알림 적용).</param>
        public void Schedule(NotificationKind kind, DateTime fireAtUtc,
            string title, string body, bool respectFirst24h = true)
        {
            if (!CanSend) return;
            if (respectFirst24h && IsWithinFirst24Hours) return;
            if (fireAtUtc <= DateTime.UtcNow) return;

            DateTime fireAtLocal = fireAtUtc.ToLocalTime();

#if UNITY_ANDROID && !UNITY_EDITOR
            var notification = new AndroidNotification
            {
                Title = title,
                Text = body,
                FireTime = fireAtLocal,
                ShowTimestamp = true,
            };
            AndroidNotificationCenter.SendNotificationWithExplicitID(
                notification, ANDROID_CHANNEL_ID, (int)kind);
#elif UNITY_IOS && !UNITY_EDITOR
            var notification = new iOSNotification
            {
                Identifier = $"bf_{(int)kind}",
                Title = title,
                Body = body,
                ShowInForeground = false,
                Trigger = new iOSNotificationCalendarTrigger
                {
                    Year   = fireAtLocal.Year,
                    Month  = fireAtLocal.Month,
                    Day    = fireAtLocal.Day,
                    Hour   = fireAtLocal.Hour,
                    Minute = fireAtLocal.Minute,
                    Second = fireAtLocal.Second,
                    Repeats = false,
                }
            };
            iOSNotificationCenter.ScheduleNotification(notification);
#else
            Debug.Log($"[Notif/EditorMock] Schedule {kind} at {fireAtLocal:yyyy-MM-dd HH:mm:ss} | {title} — {body}");
#endif
        }

        public void Cancel(NotificationKind kind)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidNotificationCenter.CancelScheduledNotification((int)kind);
            AndroidNotificationCenter.CancelDisplayedNotification((int)kind);
#elif UNITY_IOS && !UNITY_EDITOR
            string id = $"bf_{(int)kind}";
            iOSNotificationCenter.RemoveScheduledNotification(id);
            iOSNotificationCenter.RemoveDeliveredNotification(id);
#else
            Debug.Log($"[Notif/EditorMock] Cancel {kind}");
#endif
        }

        public void CancelAll()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidNotificationCenter.CancelAllScheduledNotifications();
            AndroidNotificationCenter.CancelAllDisplayedNotifications();
#elif UNITY_IOS && !UNITY_EDITOR
            iOSNotificationCenter.RemoveAllScheduledNotifications();
            iOSNotificationCenter.RemoveAllDeliveredNotifications();
#else
            Debug.Log("[Notif/EditorMock] Cancel ALL");
#endif
        }

        private void DismissAllDelivered()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidNotificationCenter.CancelAllDisplayedNotifications();
#elif UNITY_IOS && !UNITY_EDITOR
            iOSNotificationCenter.RemoveAllDeliveredNotifications();
#endif
        }

        #endregion

        #region #1 Heart Full (Phase 1)

        /// <summary>하트 풀충전 예상 시각에 로컬 알림 등록. 신규 유저 24h 가드 적용 (§9 L675).</summary>
        public void ScheduleHeartFull(DateTime fireAtUtc)
        {
            Schedule(NotificationKind.HeartFull, fireAtUtc,
                PushTexts.HEART_FULL_TITLE, PushTexts.HEART_FULL_BODY,
                respectFirst24h: true);
        }

        public void CancelHeartFull() => Cancel(NotificationKind.HeartFull);

        /// <summary>현재 LifeManager 상태로 하트 풀충전 알림 재예약.</summary>
        private void RescheduleHeartFull()
        {
            if (!LifeManager.HasInstance) return;
            var life = LifeManager.Instance;

            if (life.IsFullLives() || life.IsInfiniteHeartsActive)
            {
                CancelHeartFull();
                return;
            }

            DateTime fullAt = life.PredictFullLivesUtc();
            if (fullAt <= DateTime.UtcNow)
            {
                CancelHeartFull();
                return;
            }

            // 시간 변경 반영을 위해 기존 예약 제거 후 재예약.
            CancelHeartFull();
            ScheduleHeartFull(fullAt);
        }

        #endregion

        #region Event Handlers

        private void HandleSettingsChanged(OnSettingsChanged evt)
        {
            if (!evt.notificationOn)
            {
                CancelAll();
                return;
            }
            RescheduleHeartFull();
        }

        private void HandleLifeChanged(OnLifeChanged evt)
        {
            // 풀충전 도달 → 취소 / 미만 → 재예약. RescheduleHeartFull 내부 분기.
            RescheduleHeartFull();
        }

        /// <summary>첫 레벨 클리어 시점에 권한 요청 (가치 입증 후 — 업계 표준).</summary>
        private async void HandleLevelCompleted(OnLevelCompleted evt)
        {
            if (_firstPermAsked) return;
            if (!ToggleOn) return;
            if (_cachedState != PermissionState.NotDetermined) return;

            bool granted = await RequestPermissionAsync();
            if (granted) RescheduleHeartFull();
        }

        #endregion

        #region Private Helpers

        private void LoadInstallTime()
        {
            if (PlayerPrefs.HasKey(PREFS_INSTALL_UTC) &&
                long.TryParse(PlayerPrefs.GetString(PREFS_INSTALL_UTC), out long ticks))
            {
                _installUtcTicks = ticks;
            }
            else
            {
                _installUtcTicks = DateTime.UtcNow.Ticks;
                PlayerPrefs.SetString(PREFS_INSTALL_UTC, _installUtcTicks.ToString());
                PlayerPrefs.Save();
            }
        }

        private void RegisterAndroidChannel()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var channel = new AndroidNotificationChannel
            {
                Id          = ANDROID_CHANNEL_ID,
                Name        = ANDROID_CHANNEL_NAME,
                Importance  = Importance.Default,
                Description = "Balloon Loop notifications",
            };
            AndroidNotificationCenter.RegisterNotificationChannel(channel);
#endif
        }

        #endregion
    }

    /// <summary>알림 종류 — int 값이 Android/iOS 알림 ID. 변경 시 기존 예약 충돌 주의.</summary>
    public enum NotificationKind
    {
        HeartFull = 1,
        // Phase 2 추가 예정 — 서버 푸시(FCM) 이므로 enum 미정의 가능:
        //   ReturnD1 = 2 ... ReturnD7 = 8 / DailyReward = 10
    }
}
