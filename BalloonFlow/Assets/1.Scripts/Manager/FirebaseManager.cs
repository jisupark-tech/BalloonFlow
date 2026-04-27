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

                // Auth 자동 로그인은 안 함 — 외부 인증 키 사용 예정.
                // 로그인 필요할 때 FirebaseManager.Instance.Auth로 직접 호출.
                if (Auth.CurrentUser != null)
                    Debug.Log($"[Firebase] Restored session: {Auth.CurrentUser.UserId}");

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
