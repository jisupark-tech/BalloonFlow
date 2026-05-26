using System;
using UnityEngine;
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
using Firebase.Messaging;
#endif

namespace BalloonFlow
{
    /// <summary>
    /// Firebase Cloud Messaging 토큰 관리. 발급/갱신 시 Firestore /users/{uid}.fcmToken 동기화.
    /// 서버 측 Cloud Functions cron(#2 이탈 복귀, #3 데일리 보상)에서 발송 대상 식별.
    ///
    /// 토큰 라이프사이클:
    ///   1. SDK 초기화 시 자동 발급 (Firebase.Messaging.TokenReceived 이벤트)
    ///   2. 앱 재설치/PlayerPrefs 클리어 시 갱신
    ///   3. 사용자가 OS 권한 거부 → 토큰 무효화 (서버에서 send 시 자동 NotRegistered)
    ///
    /// 초기화 트리거: UserDataService.OnUserDataReady (uid 확보 후 등록)
    /// </summary>
    /// <remarks>
    /// Phase 2 — Cloud Functions cron 발송용. FCM 메시지 수신은 OS/SDK가 자동 처리(앱이 백그라운드일 때).
    /// 포어그라운드 수신은 MessageReceived 이벤트에서 표시 안 함(게임 중 산만함 방지).
    /// </remarks>
    public class FCMTokenManager : Singleton<FCMTokenManager>
    {
        private const string PREFS_LAST_TOKEN = "BF_FcmLastToken";

        private string _currentToken = "";
        private bool _initialized;

        /// <summary>가장 최근 발급된 FCM 토큰. 미초기화 시 빈 문자열.</summary>
        public string CurrentToken => _currentToken;

        public bool IsInitialized => _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (HasInstance) return;
            var go = new GameObject("FCMTokenManager");
            go.AddComponent<FCMTokenManager>();
        }

        protected override void OnSingletonAwake()
        {
            _currentToken = PlayerPrefs.GetString(PREFS_LAST_TOKEN, "");
            TrySubscribeUserData();
        }

        /// <summary>UserDataService ready 후 SDK 초기화. uid가 있어야 Firestore 동기화 가능.</summary>
        private void TrySubscribeUserData()
        {
            if (!UserDataService.HasInstance) return;
            UserDataService.Instance.OnUserDataReady += InitializeMessaging;
            if (UserDataService.Instance.IsReady) InitializeMessaging();
        }

        protected override void OnDestroy()
        {
            if (UserDataService.HasInstance)
                UserDataService.Instance.OnUserDataReady -= InitializeMessaging;

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            FirebaseMessaging.TokenReceived  -= HandleTokenReceived;
            FirebaseMessaging.MessageReceived -= HandleMessageReceived;
#endif
            base.OnDestroy();
        }

        private void InitializeMessaging()
        {
            if (_initialized) return;
            _initialized = true;

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            FirebaseMessaging.TokenReceived  += HandleTokenReceived;
            FirebaseMessaging.MessageReceived += HandleMessageReceived;
            // SDK 가 이미 발급한 캐시 토큰 즉시 수령 보장.
            FirebaseMessaging.TokenRegistrationOnInitEnabled = true;

            // SDK 초기 호출 시 보유 중인 토큰을 강제 동기화. (PlayerPrefs와 Firestore 불일치 가능)
            FirebaseMessaging.GetTokenAsync().ContinueWith(task =>
            {
                if (task.IsFaulted) { Debug.LogWarning($"[FCMToken] GetTokenAsync 실패: {task.Exception}"); return; }
                if (task.IsCanceled) return;
                if (!string.IsNullOrEmpty(task.Result))
                    PushTokenToFirestore(task.Result);
            });
#else
            // Editor / Messaging 미통합 빌드 — mock 토큰만 로컬 기록, 서버 sync 생략.
            Debug.Log("[FCMToken/EditorMock] Skipped Messaging init.");
#endif
        }

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        private void HandleTokenReceived(object sender, TokenReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Token)) return;
            PushTokenToFirestore(e.Token);
        }

        private void HandleMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            // 백그라운드 수신은 OS가 자동 처리(시스템 알림 표시). 여기는 포어그라운드만.
            // 포어그라운드 표시는 게임 중 산만함 방지를 위해 생략. analytics 만 발행.
            Debug.Log($"[FCMToken] Foreground message received: id={e.Message?.MessageId}");
        }
#endif

        private void PushTokenToFirestore(string token)
        {
            if (token == _currentToken) return;
            _currentToken = token;
            PlayerPrefs.SetString(PREFS_LAST_TOKEN, token);
            PlayerPrefs.Save();

            if (UserDataService.HasInstance && UserDataService.Instance.IsReady)
            {
                UserDataService.Instance.SetFcmToken(token);
                Debug.Log($"[FCMToken] Firestore 동기화 완료 (token len={token.Length})");
            }
        }
    }
}
