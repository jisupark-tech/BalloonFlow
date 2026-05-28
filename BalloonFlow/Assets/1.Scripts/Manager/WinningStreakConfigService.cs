using System;
using System.Threading.Tasks;
using UnityEngine;
using Firebase.Firestore;

namespace BalloonFlow
{
    /// <summary>
    /// Firestore /config/winningStreak 단일 doc fetch + 메모리 캐시.
    /// 앱 시작 시 1회 fetch — 콘솔에서 조정해도 다음 fetch 부터 자동 반영.
    /// 1.0 정책: 실시간 listen 안 함 (latency·cost 절약).
    /// 패턴은 ShopCatalogService 와 동일.
    /// </summary>
    public class WinningStreakConfigService : Singleton<WinningStreakConfigService>
    {
        private const string LOG_TAG = "[WinningStreakConfigService]";
        private const string DOC_PATH = "config/winningStreak";

        private WinningStreakConfigDoc _config;
        private bool _isLoaded;

        public bool IsLoaded => _isLoaded;
        public WinningStreakConfigDoc Config => _config;
        public event Action OnConfigLoaded;

        protected override void OnSingletonAwake()
        {
            _ = FetchAsync();
        }

        public async Task FetchAsync()
        {
            // 엄격 서버 기준: 서버 config 를 못 읽으면 클라 기본값으로 진행하지 않음 (Config=null → 이벤트 미노출/미진행).
            // FirebaseManager dep check 완료까지 대기 — 직접 호출 시 InvalidOperationException.
            for (int i = 0; i < 150 && !(FirebaseManager.HasInstance && FirebaseManager.Instance.IsReady); i++)
                await Task.Delay(100);
            if (!FirebaseManager.HasInstance || !FirebaseManager.Instance.IsReady)
            {
                Debug.LogError($"{LOG_TAG} FirebaseManager not ready (timeout) — 서버 config 미로드. 이벤트 미진행(엄격 서버 기준).");
                return;
            }

            const int MAX_RETRIES = 5;
            Exception lastEx = null;

            for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
            {
                try
                {
                    var db = FirebaseEnvironment.GetFirestore();
                    var snap = await db.Document(DOC_PATH).GetSnapshotAsync();

                    if (!snap.Exists)
                    {
                        // 시드 미업로드 — fallback 없이 미진행. Editor uploader 로 업로드 필요.
                        Debug.LogError($"{LOG_TAG} {DOC_PATH} doc 미존재 — Editor uploader 로 시드 업로드 필요. 이벤트 미진행.");
                        return;
                    }

                    _config = snap.ConvertTo<WinningStreakConfigDoc>();
                    if (_config.stages == null)
                        _config.stages = new System.Collections.Generic.List<WinningStreakStage>();

                    _isLoaded = true;
                    Debug.Log($"{LOG_TAG} Loaded(server) — unlockLevel={_config.unlockLevel}, stages={_config.stages.Count}");
                    OnConfigLoaded?.Invoke();
                    return;
                }
                catch (FirestoreException fe) when (fe.ErrorCode == FirestoreError.Unavailable && attempt < MAX_RETRIES)
                {
                    // 일시적 네트워크 오류만 재시도. 권한 거부 등은 재시도해도 안 되므로 아래 generic catch 로.
                    lastEx = fe;
                    Debug.LogWarning($"{LOG_TAG} Firestore unavailable. Retry {attempt}/{MAX_RETRIES} in {attempt}s...");
                    await Task.Delay(1000 * attempt);
                }
                catch (Exception e)
                {
                    // 권한 거부(Missing or insufficient permissions) 등 — Rules 수정 전까지 재시도 무의미. 미진행.
                    Debug.LogError($"{LOG_TAG} Fetch failed: {e.Message}. 이벤트 미진행(엄격 서버 기준 — Firestore Rules /config/winningStreak read 허용 확인).");
                    return;
                }
            }
            Debug.LogError($"{LOG_TAG} Fetch retries exhausted: {lastEx?.Message}. 이벤트 미진행(엄격 서버 기준).");
        }

        public WinningStreakStage GetStage(int stage1Based)
        {
            if (_config == null || _config.stages == null) return null;
            int idx = stage1Based - 1;
            if (idx < 0 || idx >= _config.stages.Count) return null;
            return _config.stages[idx];
        }

        /// <summary>연승 수에 대응되는 배수. 1..4 는 각각 streak1..4, 5 이상은 streak5Plus.</summary>
        public int ResolveStreakMultiplier(int streakCount)
        {
            if (_config == null || _config.streakMultipliers == null) return 1;
            var m = _config.streakMultipliers;
            return streakCount switch
            {
                <= 1 => m.streak1,
                2 => m.streak2,
                3 => m.streak3,
                4 => m.streak4,
                _ => m.streak5Plus
            };
        }

        public int ResolveDifficultyMultiplier(DifficultyPurpose difficulty)
        {
            if (_config == null || _config.difficultyMultipliers == null) return 1;
            var m = _config.difficultyMultipliers;
            return difficulty switch
            {
                DifficultyPurpose.Hard => m.hard,
                DifficultyPurpose.SuperHard => m.superHard,
                _ => m.normal
            };
        }
    }
}
