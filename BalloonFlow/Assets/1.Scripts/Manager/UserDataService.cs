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

        /// <summary>프로필 아이콘/프레임 변경 시 발화. Lobby UI 등 표시 위치 refresh 용.</summary>
        public event Action OnProfileChanged;

        protected override void OnSingletonAwake()
        {
            _ = InitAsync();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnBoosterEffectApplied>(HandleBoosterEffectApplied);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnBoosterEffectApplied>(HandleBoosterEffectApplied);
        }

        private async Task InitAsync()
        {
            try
            {
                // FirebaseManager 의 dep check 완료까지 대기. 이 매니저가 단독으로 CheckAndFixDependencies 호출.
                // 동시 호출 시 "Don't call other Firebase functions while CheckDependencies is running" InvalidOperationException.
                if (!await WaitForFirebaseReady())
                {
                    Debug.LogError($"{LOG_TAG} FirebaseManager not ready (timeout)");
                    return;
                }

                _auth = FirebaseAuth.DefaultInstance;

                // Sign-in 을 Firestore 첫 init 전에 — Unity Firebase SDK 13.10.0 의 Firestore lazy init 시점에 Auth state 캡처.
                // 이전 순서 (Firestore 먼저 → sign-in) 에서 token sync quirk 로 permission_denied 발생.
                await EnsureSignedInAsync(forceFresh: false);

                // [2026-05-20] UID 변경 감지 — 이전 부팅에서 성공했던 UID 와 다르면 데이터 손실 가능성 경고.
                // Editor: Firebase Auth Unity SDK 가 영속화 파일을 안 만들어 매 Play UID 가 바뀜 (알려진 한계).
                // Device: 정상 케이스에선 UID 동일. 다르면 secure storage 손상/리셋/uninstall+reinstall 또는 forceFresh 발동 의심.
                DetectAndLogUidChange();

                _db = FirebaseEnvironment.GetFirestore();

                // permission_denied 처리 정책 (2026-05-20 변경):
                // - 자동 forceFresh 제거. 이전엔 permission_denied 발생 시 무조건 SignOut → 새 UID → CreateNewUser 했는데,
                //   네트워크 지연 / 토큰 sync quirk / Firestore 일시 장애 같은 비-stale 원인에도 동작해 유저 데이터를 1000코인
                //   디폴트로 덮어쓰는 사고가 가능. forceFresh 는 "진짜 stale" 케이스에서도 데이터 복구 못 함.
                // - 새 정책: 토큰 refresh + 단순 retry 1회. 또 실패하면 _isReady false 유지 → 다른 매니저들은 offline 모드로
                //   진행. 다음 앱 실행 시 자연 재시도. 진짜 stale user 는 telemetry/수동 절차로 별도 처리.
                try
                {
                    await LoadOrCreateUserAsync(_auth.CurrentUser.UserId);
                }
                catch (FirestoreException fe) when (fe.ErrorCode == FirestoreError.PermissionDenied)
                {
                    Debug.LogWarning($"{LOG_TAG} permission_denied — 토큰 refresh 후 재시도 1회. (forceFresh 안 함 — 데이터 손실 방어)");
                    try { await _auth.CurrentUser.TokenAsync(true); }
                    catch (Exception tokenEx) { Debug.LogWarning($"{LOG_TAG} 토큰 refresh 실패: {tokenEx.Message}"); }
                    await Task.Delay(500);

                    try
                    {
                        await LoadOrCreateUserAsync(_auth.CurrentUser.UserId);
                    }
                    catch (FirestoreException retryFe) when (retryFe.ErrorCode == FirestoreError.PermissionDenied)
                    {
                        // 재시도도 실패 — IsReady false 유지. 게임은 LifeManager 등의 PlayerPrefs offline cache 로 진행.
                        // 같은 UID 의 Firestore doc 은 다음 앱 실행 시 자연 회복 시도. 절대로 forceFresh 안 함.
                        Debug.LogError($"{LOG_TAG} permission_denied retry 실패 — Firestore 동기화 skip. uid={_auth.CurrentUser?.UserId ?? "(null)"}. msg={retryFe.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"{LOG_TAG} Init failed: {e}");
            }
        }

        /// <summary>
        /// Anonymous Auth 보장. forceFresh=true 면 SignOut 후 새로 sign-in (stale token / 삭제된 user 회복용).
        /// </summary>
        private async Task EnsureSignedInAsync(bool forceFresh)
        {
            if (forceFresh && _auth.CurrentUser != null)
            {
                Debug.Log($"{LOG_TAG} SignOut existing user uid={_auth.CurrentUser.UserId}");
                _auth.SignOut();
            }

            if (_auth.CurrentUser == null)
            {
                var authResult = await _auth.SignInAnonymouslyAsync();
                Debug.Log($"{LOG_TAG} Signed in anonymously. uid={authResult.User.UserId}");
                return;
            }

            Debug.Log($"{LOG_TAG} Existing auth session. uid={_auth.CurrentUser.UserId}");
            try
            {
                await _auth.CurrentUser.TokenAsync(true); // forceRefresh
                Debug.Log($"{LOG_TAG} Auth token refreshed.");
            }
            catch (Exception tokenEx)
            {
                // ⚠️ UID 유지 정책 — TokenAsync 실패 시 SignOut 안 함.
                // 네트워크 불안정 (offline / 지하철 / 비행기 모드 등) 으로 refresh 실패해도 캐시 UID 유지.
                // 후속 Firestore 호출에서 자동 재시도 (SDK 가 토큰 만료 인지 시 자동 갱신 시도).
                // 토큰이 영구 무효화 (revoked / 계정 삭제) 인 경우엔 그 시점에 permission_denied 분기 (line 66~) 가 흡수.
                // 이전 동작 (SignOut + 새 Anonymous Auth) 은 새 UID 생성으로 진행도 손실 유발했음 — 2026-05-18 변경.
                Debug.LogWarning($"{LOG_TAG} Token refresh failed: {tokenEx.Message} — 캐시 세션 유지 (uid={_auth.CurrentUser?.UserId ?? "(null)"})");
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

            // 성공한 UID 캐시 — 다음 부팅 시 UID 변경 감지에 사용.
            PersistAuthUid(uid);

            _isReady = true;
            OnUserDataReady?.Invoke();
        }

        /// <summary>
        /// 이전 부팅에서 성공한 UID 와 이번 sign-in UID 비교.
        /// 다르면 데이터 손실 위험 신호 — Editor 매 Play, Device 에선 secure storage 손상/reinstall/forceFresh 의심.
        /// 처음 사인인 (이전 UID 없음) 케이스는 정상 — 로그만 info 레벨.
        /// </summary>
        private void DetectAndLogUidChange()
        {
            string currentUid = _auth?.CurrentUser?.UserId ?? "";
            if (string.IsNullOrEmpty(currentUid)) return;

            string previousUid = PlayerPrefs.GetString(Const.PREFS_LAST_AUTH_UID, "");

            if (string.IsNullOrEmpty(previousUid))
            {
                Debug.Log($"{LOG_TAG} First-ever sign-in or fresh install. uid={currentUid}");
                return;
            }

            if (previousUid == currentUid)
            {
                Debug.Log($"{LOG_TAG} Auth session restored — same UID as previous launch. uid={currentUid}");
                return;
            }

            // UID 가 변경됨 — 데이터 손실 가능성. Editor 에선 SDK 한계로 흔하지만 Device 에선 비정상.
#if UNITY_EDITOR
            Debug.LogWarning($"{LOG_TAG} [EDITOR] UID changed across Play sessions (Firebase Auth Unity SDK Editor 영속화 미작동). prev={previousUid} new={currentUid}");
#else
            Debug.LogError($"{LOG_TAG} [CRITICAL] UID changed across launches — 데이터 손실 위험. prev={previousUid} new={currentUid}. " +
                           "원인 후보: secure storage 손상 / reinstall / forceFresh 발동 / Auth state 외부 변경.");
            if (FirebaseManager.HasInstance)
                FirebaseManager.Instance.LogEvent("auth_uid_changed");
#endif
        }

        private void PersistAuthUid(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            string existing = PlayerPrefs.GetString(Const.PREFS_LAST_AUTH_UID, "");
            if (existing == uid) return;
            PlayerPrefs.SetString(Const.PREFS_LAST_AUTH_UID, uid);
            PlayerPrefs.Save();
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

        /// <summary>FTUE 무한 하트 24h 부여를 서버 doc 에 1회 기록.
        /// 신규 uid 발급(UserData.CreateNewUser) → 평생 1회 → Lv.1 인게임 로딩 완료 후 호출.
        /// pending=true 일 때만 동작. pending=false 면 early return (중복 set 방지 — 동일 doc 두 번 업데이트 금지).</summary>
        public void MarkFtueInfiniteHeartsGranted()
        {
            if (!_isReady || _user == null) return;
            if (!_user.ftueInfiniteHeartsPending) return;

            var now = Timestamp.GetCurrentTimestamp();
            _user.ftueInfiniteHeartsPending = false;
            _user.ftueInfiniteHeartsGrantedAt = now;
            FireAndForget(_db.Document($"users/{Uid}").UpdateAsync(new Dictionary<string, object>
            {
                ["ftueInfiniteHeartsPending"]   = false,
                ["ftueInfiniteHeartsGrantedAt"] = now
            }), "MarkFtueInfiniteHeartsGranted");
        }

        /// <summary>FCM 등록 토큰 갱신. 빈 문자열 = unregister(서버 push 대상에서 제외). Phase 2 서버 cron에서 참조.</summary>
        public void SetFcmToken(string token)
        {
            if (!_isReady) return;
            token ??= "";
            if (_user.fcmToken == token) return;
            _user.fcmToken = token;
            FireAndForget(_db.Document($"users/{Uid}").UpdateAsync("fcmToken", token),
                "SetFcmToken");
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
            SetRemovedAds(value, null);
        }

        /// <summary>[2026-05-13] 광고 제거 플래그 + 구매 시각 + productId 일괄 저장. 유료 상품 추적용 (CS/환불/분석).
        /// productId 미지정 (null) 시 productId 필드 갱신 생략. value=true 일 때만 timestamp 갱신.</summary>
        public void SetRemovedAds(bool value, string productId)
        {
            if (!_isReady) return;
            _user.removedAds = value;
            var updates = new Dictionary<string, object> { ["removedAds"] = value };
            if (value)
            {
                var now = Firebase.Firestore.Timestamp.GetCurrentTimestamp();
                _user.removedAdsPurchasedAt = now;
                updates["removedAdsPurchasedAt"] = now;
                if (!string.IsNullOrEmpty(productId))
                {
                    _user.removedAdsProductId = productId;
                    updates["removedAdsProductId"] = productId;
                }
            }
            FireAndForget(_db.Document($"users/{Uid}").UpdateAsync(updates),
                $"SetRemovedAds({value},{productId})");
        }

        /// <summary>프로필 아이콘 슬롯 변경. 동일 값이면 no-op. OnProfileChanged 발화.</summary>
        public void SetZapTutorialCompleted(bool value = true)
        {
            PlayerPrefs.SetInt(Const.PREFS_ZAP_TUTORIAL_COMPLETED, value ? 1 : 0);
            PlayerPrefs.Save();

            if (!_isReady || _user == null) return;
            if (_user.zapTutorialCompleted == value) return;
            _user.zapTutorialCompleted = value;
            FireAndForget(_db.Document($"users/{Uid}").UpdateAsync("zapTutorialCompleted", value),
                $"SetZapTutorialCompleted({value})");
        }

        public void SetFirstInterstitialShown(bool value = true)
        {
            PlayerPrefs.SetInt(Const.PREFS_FIRST_INTERSTITIAL_SHOWN, value ? 1 : 0);
            PlayerPrefs.Save();

            if (!_isReady || _user == null) return;
            if (_user.firstInterstitialShown == value) return;
            _user.firstInterstitialShown = value;
            FireAndForget(_db.Document($"users/{Uid}").UpdateAsync("firstInterstitialShown", value),
                $"SetFirstInterstitialShown({value})");
        }

        private void HandleBoosterEffectApplied(OnBoosterEffectApplied evt)
        {
            if (evt.boosterType == BoosterManager.COLOR_REMOVE || evt.boosterType == BoosterManager.ZAP)
                SetZapTutorialCompleted(true);
        }

        public void SetProfileIconNumber(int iconIndex)
        {
            if (!_isReady || iconIndex < 0) return;
            if (_user.profileIconNumber == iconIndex) return;
            _user.profileIconNumber = iconIndex;
            OnProfileChanged?.Invoke();
            FireAndForget(_db.Document($"users/{Uid}").UpdateAsync("profileIconNumber", iconIndex),
                $"SetProfileIconNumber({iconIndex})");
        }

        /// <summary>프로필 프레임 슬롯 변경. 동일 값이면 no-op. OnProfileChanged 발화.</summary>
        public void SetProfileFrameNumber(int frameIndex)
        {
            if (!_isReady || frameIndex < 0) return;
            if (_user.profileFrameNumber == frameIndex) return;
            _user.profileFrameNumber = frameIndex;
            OnProfileChanged?.Invoke();
            FireAndForget(_db.Document($"users/{Uid}").UpdateAsync("profileFrameNumber", frameIndex),
                $"SetProfileFrameNumber({frameIndex})");
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

        // ── WinningStreak ────────────────────────────────────────
        /// <summary>WinningStreak 진행 상태 일괄 저장 (currentStage / currentStagePoints / currentStreak / lifetimePoints / eventFinished).
        /// claimedStages 는 별도 메서드 사용.</summary>
        public void SaveWinningStreakProgress()
        {
            if (!_isReady || _user.winningStreak == null) return;
            var ws = _user.winningStreak;
            FireAndForget(_db.Document($"users/{Uid}").UpdateAsync(new Dictionary<string, object>
            {
                ["winningStreak.currentStage"] = ws.currentStage,
                ["winningStreak.currentStagePoints"] = ws.currentStagePoints,
                ["winningStreak.currentStreak"] = ws.currentStreak,
                ["winningStreak.lifetimePoints"] = ws.lifetimePoints,
                ["winningStreak.eventFinished"] = ws.eventFinished,
                ["winningStreak.activeRoundId"] = ws.activeRoundId
            }), "SaveWinningStreakProgress");
        }

        /// <summary>claimedStages 에 stage 1개 append + 전체 list 저장. 로컬 캐시는 이미 갱신됐다고 가정.</summary>
        public void SaveWinningStreakClaimedStages()
        {
            if (!_isReady || _user.winningStreak == null) return;
            FireAndForget(_db.Document($"users/{Uid}").UpdateAsync(new Dictionary<string, object>
            {
                ["winningStreak.claimedStages"] = _user.winningStreak.claimedStages
            }), "SaveWinningStreakClaimedStages");
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

        /// <summary>
        /// FirebaseManager.IsReady 까지 대기. FirebaseManager 가 단독으로 CheckAndFixDependenciesAsync 호출.
        /// 다른 매니저가 Firebase API 호출하기 전에 반드시 호출.
        /// </summary>
        private static async Task<bool> WaitForFirebaseReady(int timeoutMs = 15000)
        {
            // FirebaseManager 인스턴스 attach 대기 (5s)
            for (int i = 0; i < 50 && !FirebaseManager.HasInstance; i++)
                await Task.Delay(100);
            if (!FirebaseManager.HasInstance) return false;

            // IsReady 이벤트 또는 polling
            var fm = FirebaseManager.Instance;
            if (fm.IsReady) return true;

            var tcs = new TaskCompletionSource<bool>();
            Action onReady = null;
            onReady = () =>
            {
                if (FirebaseManager.HasInstance)
                    FirebaseManager.Instance.OnReady -= onReady;
                tcs.TrySetResult(true);
            };
            fm.OnReady += onReady;

            var winner = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            return winner == tcs.Task && fm.IsReady;
        }

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
