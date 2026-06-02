using System.Collections.Generic;
using UnityEngine;
using Firebase;
using Firebase.Analytics;
using Firebase.Extensions;
using Facebook.Unity;

namespace BalloonFlow
{
    /// <summary>
    /// 통합 analytics 매니저. 한 번 LogEvent 호출로 Firebase + Facebook + AppsFlyer 전송.
    /// Firebase init은 비동기 (CheckAndFixDependencies). 준비 전 호출은 큐잉 없이 drop.
    /// </summary>
    public class AnalyticsManager : Singleton<AnalyticsManager>
    {
        private const string LOG_TAG = "[AnalyticsManager]";
        private const int MAX_PENDING_FIREBASE_EVENTS = 128;

        private bool _firebaseReady;
        private bool _facebookReady;
        private readonly Queue<PendingFirebaseEvent> _pendingFirebaseEvents = new Queue<PendingFirebaseEvent>();

        public bool FirebaseReady => _firebaseReady;
        public bool FacebookReady => _facebookReady;

        protected override void OnSingletonAwake()
        {
            InitFirebase();
            InitFacebook();
        }

        #region Init

        private void InitFirebase()
        {
            // FirebaseManager 가 단독으로 CheckAndFixDependenciesAsync 호출. 여기선 IsReady 만 wait.
            // 직접 호출 시 InvalidOperationException ("Don't call other Firebase functions while CheckDependencies is running")
            _ = WaitFirebaseReadyThenInit();
        }

        private async System.Threading.Tasks.Task WaitFirebaseReadyThenInit()
        {
            for (int i = 0; i < 150 && !(FirebaseManager.HasInstance && FirebaseManager.Instance.IsReady); i++)
                await System.Threading.Tasks.Task.Delay(100);

            if (!FirebaseManager.HasInstance || !FirebaseManager.Instance.IsReady)
            {
                Debug.LogError($"{LOG_TAG} FirebaseManager not ready (timeout)");
                return;
            }

            _firebaseReady = true;
            FirebaseAnalytics.SetAnalyticsCollectionEnabled(true);
            Debug.Log($"{LOG_TAG} Firebase Analytics ready.");
            FlushPendingFirebaseEvents();
        }

        private void InitFacebook()
        {
            if (FB.IsInitialized)
            {
                FB.ActivateApp();
                _facebookReady = true;
                Debug.Log($"{LOG_TAG} Facebook already initialized.");
                return;
            }

            FB.Init(
                onInitComplete: OnFacebookInitComplete,
                onHideUnity:    OnFacebookHidden);
        }

        private void OnFacebookInitComplete()
        {
            if (FB.IsInitialized)
            {
                FB.ActivateApp();
                _facebookReady = true;
                Debug.Log($"{LOG_TAG} Facebook initialized.");
            }
            else
            {
                Debug.LogError($"{LOG_TAG} Facebook init failed.");
            }
        }

        private void OnFacebookHidden(bool isUnityShown)
        {
            // App resumed/paused
        }

        #endregion

        #region LogEvent — 통합 인터페이스

        /// <summary>이벤트 발행. Firebase + Facebook + AppsFlyer 모두 전송.</summary>
        public void LogEvent(string eventName, Dictionary<string, object> parameters = null)
        {
            if (string.IsNullOrEmpty(eventName)) return;

            LogToFirebase(eventName, parameters);
            LogToFacebook(eventName, parameters);
            LogToAppsFlyer(eventName, parameters);
        }

        /// <summary>단일 string 파라미터 편의 오버로드.</summary>
        public void LogEvent(string eventName, string paramName, string paramValue)
        {
            var p = new Dictionary<string, object> { [paramName] = paramValue };
            LogEvent(eventName, p);
        }

        #endregion

        #region Per-platform dispatchers

        private void LogToFirebase(string eventName, Dictionary<string, object> parameters)
        {
            if (!_firebaseReady)
            {
                EnqueueFirebaseEvent(eventName, parameters);
                return;
            }

            if (parameters == null || parameters.Count == 0)
            {
                FirebaseAnalytics.LogEvent(eventName);
                return;
            }

            var fbParams = new Parameter[parameters.Count];
            int i = 0;
            foreach (var kv in parameters)
            {
                fbParams[i++] = ToFirebaseParameter(kv.Key, kv.Value);
            }
            FirebaseAnalytics.LogEvent(eventName, fbParams);
        }

        private void EnqueueFirebaseEvent(string eventName, Dictionary<string, object> parameters)
        {
            // ROLLBACK_ANALYTICS_FIREBASE_QUEUE_20260602:
            // Previous behavior dropped events fired before Firebase Analytics became ready.
            if (_pendingFirebaseEvents.Count >= MAX_PENDING_FIREBASE_EVENTS)
            {
                _pendingFirebaseEvents.Dequeue();
                Debug.LogWarning($"{LOG_TAG} Firebase event queue full. Dropping oldest pending event.");
            }

            _pendingFirebaseEvents.Enqueue(new PendingFirebaseEvent
            {
                eventName = eventName,
                parameters = parameters != null
                    ? new Dictionary<string, object>(parameters)
                    : null
            });
        }

        private void FlushPendingFirebaseEvents()
        {
            while (_pendingFirebaseEvents.Count > 0)
            {
                var pending = _pendingFirebaseEvents.Dequeue();
                LogToFirebase(pending.eventName, pending.parameters);
            }
        }

        private void LogToFacebook(string eventName, Dictionary<string, object> parameters)
        {
            if (!_facebookReady) return;

            if (parameters == null || parameters.Count == 0)
            {
                FB.LogAppEvent(eventName);
                return;
            }
            // Facebook은 Dictionary<string, object>를 그대로 받음
            FB.LogAppEvent(eventName, parameters: parameters);
        }

        private void LogToAppsFlyer(string eventName, Dictionary<string, object> parameters)
        {
            if (!AttributionManager.HasInstance) return;

            Dictionary<string, string> stringParams = null;
            if (parameters != null && parameters.Count > 0)
            {
                stringParams = new Dictionary<string, string>(parameters.Count);
                foreach (var kv in parameters)
                {
                    stringParams[kv.Key] = kv.Value?.ToString() ?? "";
                }
            }
            AttributionManager.Instance.LogEvent(eventName, stringParams);
        }

        private static Parameter ToFirebaseParameter(string key, object value)
        {
            switch (value)
            {
                case long lv:   return new Parameter(key, lv);
                case int iv:    return new Parameter(key, iv);
                case bool bv:   return new Parameter(key, bv ? 1L : 0L);
                case double dv: return new Parameter(key, dv);
                case float fv:  return new Parameter(key, fv);
                default:        return new Parameter(key, value?.ToString() ?? "");
            }
        }

        private struct PendingFirebaseEvent
        {
            public string eventName;
            public Dictionary<string, object> parameters;
        }

        #endregion
    }
}
