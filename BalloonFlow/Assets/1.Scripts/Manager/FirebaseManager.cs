using System;
using System.Threading.Tasks;
using UnityEngine;
using Firebase;
using Firebase.Analytics;
using Firebase.Auth;
using Firebase.Firestore;
using Firebase.Storage;

namespace BalloonFlow
{
    /// <summary>
    /// Firebase 초기화 + 핸들 보관. Title 진입 시 GameManager가 EnsurePersistent로 생성.
    /// CheckAndFixDependencies → App/Auth/Firestore/Storage 핸들 캐싱 → 익명 로그인 → Analytics enable.
    /// 외부에서는 IsReady 체크 후 Auth/Db/Storage 프로퍼티 사용.
    /// </summary>
    public class FirebaseManager : Singleton<FirebaseManager>
    {
        public bool IsReady { get; private set; }
        public FirebaseApp App { get; private set; }
        public FirebaseAuth Auth { get; private set; }
        public FirebaseFirestore Db { get; private set; }
        public FirebaseStorage Storage { get; private set; }
        public string UserId => Auth?.CurrentUser?.UserId;

        public event Action OnReady;

        protected override void OnSingletonAwake()
        {
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                var _status = await FirebaseApp.CheckAndFixDependenciesAsync();
                if (_status != DependencyStatus.Available)
                {
                    Debug.LogError($"[Firebase] Dependency check failed: {_status}");
                    return;
                }

                App = FirebaseApp.DefaultInstance;
                Auth = FirebaseAuth.DefaultInstance;
                Db = FirebaseFirestore.DefaultInstance;
                Storage = FirebaseStorage.DefaultInstance;

                FirebaseAnalytics.SetAnalyticsCollectionEnabled(true);

                // 익명 로그인 — UID 발급되어야 Firestore 보안 규칙(request.auth != null) 통과
                if (Auth.CurrentUser == null)
                {
                    var _result = await Auth.SignInAnonymouslyAsync();
                    Debug.Log($"[Firebase] Anonymous sign-in: {_result.User.UserId}");
                }
                else
                {
                    Debug.Log($"[Firebase] Restored session: {Auth.CurrentUser.UserId}");
                }

                IsReady = true;
                OnReady?.Invoke();
                Debug.Log("[Firebase] Ready");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Firebase] Init failed: {e}");
            }
        }

        public void LogEvent(string _name)
        {
            if (!IsReady) return;
            FirebaseAnalytics.LogEvent(_name);
        }

        public void LogEvent(string _name, params Parameter[] _params)
        {
            if (!IsReady) return;
            FirebaseAnalytics.LogEvent(_name, _params);
        }
    }
}
