using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Firebase;
using Firebase.Auth;
using Firebase.Extensions;
using Firebase.Firestore;

namespace BalloonFlow
{
    /// <summary>
    /// Firestore /users/{uid} 의 단일 진실 소스. Anonymous Auth → Firestore 로드 → 메모리 캐시.
    /// 재화/하트/부스터 변경은 이 매니저를 거쳐 진행 (CurrencyManager/LifeManager 가 향후 wrapper로 전환).
    /// 비동기 write 는 fire-and-forget + 실패 로그 (Phase 2 에서 retry 큐 + Cloud Function 라우팅으로 강화).
    /// </summary>
    public class UserDataService : Singleton<UserDataService>
    {
        private const string LOG_TAG = "[UserDataService]";

        private FirebaseAuth      _auth;
        private FirebaseFirestore _db;
        private bool              _isReady;
        private UserData          _user;

        public bool IsReady => _isReady;
        public UserData CurrentUser => _user;
        public string Uid => _auth?.CurrentUser?.UserId ?? "";

        /// <summary>Firestore 로드/생성 완료 시 1회 발화. 이미 ready 상태로 구독하면 즉시 invoke.</summary>
        public event Action OnUserDataReady;

        protected override void OnSingletonAwake()
        {
            _ = InitAsync();
        }

        private async Task InitAsync()
        {
            try
            {
                // Firebase deps 점검 (FirebaseManager 가 별도로 처리할 수도 있지만 안전망)
                var depStatus = await FirebaseApp.CheckAndFixDependenciesAsync();
                if (depStatus != DependencyStatus.Available)
                {
                    Debug.LogError($"{LOG_TAG} Firebase dependencies unavailable: {depStatus}");
                    return;
                }

                _auth = FirebaseAuth.DefaultInstance;
                _db   = FirebaseEnvironment.GetFirestore();

                // Anonymous sign-in. 기존 세션은 token 강제 refresh — Editor 에서 stale token 으로 Firestore permission_denied 회피
                if (_auth.CurrentUser == null)
                {
                    var authResult = await _auth.SignInAnonymouslyAsync();
                    Debug.Log($"{LOG_TAG} Signed in anonymously. uid={authResult.User.UserId}");
                }
                else
                {
                    Debug.Log($"{LOG_TAG} Existing auth session. uid={_auth.CurrentUser.UserId}");
                    try
                    {
                        await _auth.CurrentUser.TokenAsync(true); // forceRefresh
                        Debug.Log($"{LOG_TAG} Auth token refreshed.");
                    }
                    catch (Exception tokenEx)
                    {
                        Debug.LogWarning($"{LOG_TAG} Token refresh failed: {tokenEx.Message}");
                    }
                }

                await LoadOrCreateUserAsync(_auth.CurrentUser.UserId);
            }
            catch (Exception e)
            {
                Debug.LogError($"{LOG_TAG} Init failed: {e}");
            }
        }

        private async Task LoadOrCreateUserAsync(string uid)
        {
            DocumentReference docRef = _db.Document($"users/{uid}");
            DocumentSnapshot  snap   = await GetSnapshotWithRetryAsync(docRef);

            if (snap.Exists)
            {
                _user = snap.ConvertTo<UserData>();
                _user.lastLoginAt = Timestamp.GetCurrentTimestamp();
                // lastLoginAt 비동기 update (실패해도 게임 진행 막지 않음)
                _ = docRef.UpdateAsync("lastLoginAt", _user.lastLoginAt);
                Debug.Log($"{LOG_TAG} UserData loaded. coins={_user.coins} lives={_user.lives}/{_user.maxLives}");
            }
            else
            {
                _user = UserData.CreateNewUser(uid);
                await docRef.SetAsync(_user);
                Debug.Log($"{LOG_TAG} New user created. uid={uid} coins={_user.coins}");
            }

            _isReady = true;
            OnUserDataReady?.Invoke();
        }

        #region Public API — Atomic increments (서버 진실)

        /// <summary>코인 증감. 양수=획득, 음수=소비. 로컬 캐시 즉시 반영 + Firestore atomic increment.</summary>
        public void AdjustCoins(int delta, string reason)
        {
            if (!_isReady || delta == 0) return;
            _user.coins = Mathf.Max(0, _user.coins + delta);
            FireAndForget(_db.Document($"users/{Uid}").UpdateAsync(new Dictionary<string, object>
            {
                ["coins"] = FieldValue.Increment(delta)
            }), $"AdjustCoins({delta}, {reason})");
        }

        /// <summary>하트 증감. 0~maxLives 클램프. nextLifeAt 갱신은 별도.</summary>
        public void AdjustLives(int delta, string reason)
        {
            if (!_isReady || delta == 0) return;
            _user.lives = Mathf.Clamp(_user.lives + delta, 0, _user.maxLives);
            FireAndForget(_db.Document($"users/{Uid}").UpdateAsync(new Dictionary<string, object>
            {
                ["lives"] = _user.lives
            }), $"AdjustLives({delta}, {reason})");
        }

        /// <summary>nextLifeAt 갱신. default(Seconds=0) 으로 호출하면 unset.</summary>
        public void SetNextLifeAt(Timestamp next)
        {
            if (!_isReady) return;
            _user.nextLifeAt = next;
            FireAndForget(_db.Document($"users/{Uid}").UpdateAsync("nextLifeAt", next),
                "SetNextLifeAt");
        }

        /// <summary>infiniteHeartsUntil 갱신. default = 비활성.</summary>
        public void SetInfiniteHeartsUntil(Timestamp until)
        {
            if (!_isReady) return;
            _user.infiniteHeartsUntil = until;
            FireAndForget(_db.Document($"users/{Uid}").UpdateAsync("infiniteHeartsUntil", until),
                "SetInfiniteHeartsUntil");
        }

        public void AdjustBooster(string boosterId, int delta, string reason)
        {
            if (!_isReady || delta == 0) return;
            int current = boosterId switch
            {
                "hand"    => _user.boosters.hand,
                "shuffle" => _user.boosters.shuffle,
                "zap"     => _user.boosters.zap,
                _ => 0
            };
            int next = Mathf.Max(0, current + delta);
            switch (boosterId)
            {
                case "hand":    _user.boosters.hand    = next; break;
                case "shuffle": _user.boosters.shuffle = next; break;
                case "zap":     _user.boosters.zap     = next; break;
                default:
                    Debug.LogWarning($"{LOG_TAG} Unknown booster id: {boosterId}");
                    return;
            }
            FireAndForget(_db.Document($"users/{Uid}").UpdateAsync(new Dictionary<string, object>
            {
                [$"boosters.{boosterId}"] = FieldValue.Increment(delta)
            }), $"AdjustBooster({boosterId}, {delta}, {reason})");
        }

        public void SetHighestClearedLevel(int level)
        {
            if (!_isReady || level <= _user.highestClearedLevel) return;
            _user.highestClearedLevel = level;
            FireAndForget(_db.Document($"users/{Uid}").UpdateAsync("highestClearedLevel", level),
                $"SetHighestClearedLevel({level})");
        }

        public void SetRemovedAds(bool value)
        {
            if (!_isReady) return;
            _user.removedAds = value;
            FireAndForget(_db.Document($"users/{Uid}").UpdateAsync("removedAds", value),
                $"SetRemovedAds({value})");
        }

        public void SetPurchasedOnce(string productId, bool purchased = true)
        {
            if (!_isReady || string.IsNullOrEmpty(productId)) return;
            _user.purchasedOnce[productId] = purchased;
            FireAndForget(_db.Document($"users/{Uid}").UpdateAsync(new Dictionary<string, object>
            {
                [$"purchasedOnce.{productId}"] = purchased
            }), $"SetPurchasedOnce({productId})");
        }

        public void MarkPaying()
        {
            if (!_isReady || !_user.isNPU) return;
            _user.isNPU = false;
            FireAndForget(_db.Document($"users/{Uid}").UpdateAsync("isNPU", false), "MarkPaying");
        }

        /// <summary>임의 필드 업데이트. dot-notation 으로 nested 가능 (e.g. "settings.soundOn").</summary>
        public void UpdateField(string fieldPath, object value)
        {
            if (!_isReady || string.IsNullOrEmpty(fieldPath)) return;
            FireAndForget(_db.Document($"users/{Uid}").UpdateAsync(fieldPath, value),
                $"UpdateField({fieldPath})");
        }

        /// <summary>전체 문서 강제 재저장 (덮어쓰기). 일괄 변경 시.</summary>
        public Task ForceSaveAsync()
        {
            if (!_isReady) return Task.CompletedTask;
            return _db.Document($"users/{Uid}").SetAsync(_user, SetOptions.Overwrite);
        }

        /// <summary>다른 매니저가 직접 수정한 _user 객체를 서버에 반영해야 할 때.</summary>
        public void Refresh()
        {
            if (!_isReady) return;
            FireAndForget(_db.Document($"users/{Uid}").SetAsync(_user, SetOptions.Overwrite),
                "Refresh (full overwrite)");
        }

        #endregion

        private static void FireAndForget(Task t, string label)
        {
            t.ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                    Debug.LogError($"{LOG_TAG} {label} failed: {task.Exception?.GetBaseException().Message}");
                else if (task.IsCanceled)
                    Debug.LogWarning($"{LOG_TAG} {label} cancelled");
            });
        }

        /// <summary>
        /// Firestore 첫 init에서 client 가 아직 online 전파 안 된 상태일 때 Unavailable 로 실패하는 케이스 회피.
        /// 1초 → 2초 → 3초 backoff 로 최대 3회 재시도.
        /// </summary>
        private static async Task<DocumentSnapshot> GetSnapshotWithRetryAsync(DocumentReference docRef, int maxRetries = 3)
        {
            Exception lastEx = null;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await docRef.GetSnapshotAsync();
                }
                catch (FirestoreException fe) when (fe.ErrorCode == FirestoreError.Unavailable)
                {
                    lastEx = fe;
                    Debug.LogWarning($"{LOG_TAG} Firestore unavailable (offline). Retry {attempt}/{maxRetries} in {attempt}s...");
                    await Task.Delay(1000 * attempt);
                }
            }
            throw lastEx ?? new Exception("GetSnapshotWithRetryAsync exhausted");
        }
    }
}
