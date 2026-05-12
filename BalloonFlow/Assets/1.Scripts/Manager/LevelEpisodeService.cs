using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Firebase.Firestore;

namespace BalloonFlow
{
    /// <summary>
    /// 에피소드 단위 레벨 데이터 공급자.
    /// - 1 에피소드 = 20 레벨 (packageId 1..15, 총 300 레벨)
    /// - Episode 1: StreamingAssets/episode_01.json (앱 번들, 오프라인 OK)
    /// - Episode 2~: Firestore /episodes/{packageId} (online required)
    /// - 동시에 1 에피소드만 메모리 보유 — 다음 에피소드 prefetch 시 이전 즉시 해제
    ///
    /// 사용 흐름:
    ///   Title 진입 → EnsureEpisodeForLevelAsync(userHighestLevel+1)
    ///   레벨 로드 시점엔 이미 캐시 hit → GetLevel(levelId) 동기 반환
    /// </summary>
    public class LevelEpisodeService : Singleton<LevelEpisodeService>
    {
        private const string LOG_TAG               = "[LevelEpisodeService]";
        private const string FIRESTORE_COLLECTION  = "episodes";
        private const string BUNDLED_EP1_FILENAME  = "episode_01.json";
        public  const int    LEVELS_PER_EPISODE    = 20;
        public  const int    TOTAL_EPISODES        = 15;
        public  const int    BUNDLED_PACKAGE_ID    = 1;

        private LevelEpisode _cached;
        private int          _cachedPackageId = -1;
        private Task<bool>   _inflightTask;
        private int          _inflightPackageId = -1;

        /// <summary>현재 캐시된 에피소드 (-1 = 없음).</summary>
        public int CurrentPackageId => _cachedPackageId;

        /// <summary>현재 캐시된 에피소드 doc.</summary>
        public LevelEpisode Current => _cached;

        /// <summary>유효한 캐시 보유 여부.</summary>
        public bool IsReady => _cached != null && _cached.levels != null && _cached.levels.Length > 0;

        /// <summary>levelId (1-based) → packageId (1-based).</summary>
        public static int PackageIdForLevel(int levelId)
        {
            if (levelId < 1) return 1;
            return ((levelId - 1) / LEVELS_PER_EPISODE) + 1;
        }

        /// <summary>현재 캐시에서 동기 조회. 캐시 miss 면 null + 경고.</summary>
        public LevelConfig GetLevel(int levelId)
        {
            int pkg = PackageIdForLevel(levelId);
            if (_cached == null || _cachedPackageId != pkg)
            {
                Debug.LogWarning($"{LOG_TAG} GetLevel({levelId}) 캐시 miss. 현재 캐시 pkg={_cachedPackageId}, 필요 pkg={pkg}. EnsureEpisodeForLevelAsync 선행 필요.");
                return null;
            }

            int positionInPackage = ((levelId - 1) % LEVELS_PER_EPISODE) + 1;
            int index = positionInPackage - 1;
            if (_cached.levels == null || index < 0 || index >= _cached.levels.Length)
            {
                Debug.LogWarning($"{LOG_TAG} levelId {levelId} (pkg {pkg} pos {positionInPackage}) — 캐시 doc 에 인덱스 없음 (length={_cached.levels?.Length ?? 0}).");
                return null;
            }

            return _cached.levels[index];
        }

        /// <summary>
        /// 주어진 levelId 가 속한 에피소드를 캐시에 보장.
        /// 이미 같은 에피소드 캐시면 즉시 true. 다른 에피소드면 이전 폐기 + 새로 fetch.
        /// </summary>
        public Task<bool> EnsureEpisodeForLevelAsync(int levelId)
        {
            int pkg = PackageIdForLevel(levelId);
            return EnsureEpisodeAsync(pkg);
        }

        /// <summary>
        /// packageId 의 에피소드를 캐시에 보장. 같은 pkg 동시 호출은 1개의 inflight Task 로 합쳐짐.
        /// </summary>
        public Task<bool> EnsureEpisodeAsync(int packageId)
        {
            if (packageId < 1 || packageId > TOTAL_EPISODES)
            {
                Debug.LogWarning($"{LOG_TAG} EnsureEpisodeAsync({packageId}) — 범위 밖.");
                return Task.FromResult(false);
            }

            if (_cached != null && _cachedPackageId == packageId)
                return Task.FromResult(true);

            if (_inflightTask != null && _inflightPackageId == packageId)
                return _inflightTask;

            _inflightPackageId = packageId;
            _inflightTask = LoadEpisodeAsync(packageId);
            return _inflightTask;
        }

        private async Task<bool> LoadEpisodeAsync(int packageId)
        {
            try
            {
                LevelEpisode loaded = (packageId == BUNDLED_PACKAGE_ID)
                    ? await LoadBundledEpisodeAsync()
                    : await LoadRemoteEpisodeAsync(packageId);

                if (loaded == null || loaded.levels == null || loaded.levels.Length == 0)
                {
                    Debug.LogError($"{LOG_TAG} pkg {packageId} — 로드 실패 또는 빈 에피소드.");
                    return false;
                }

                // 이전 에피소드 해제 (메모리 1개 유지 정책)
                if (_cached != null && _cachedPackageId != packageId)
                    Debug.Log($"{LOG_TAG} 이전 캐시 pkg {_cachedPackageId} 해제 → pkg {packageId} 로 교체.");

                _cached = loaded;
                _cachedPackageId = packageId;
                Debug.Log($"{LOG_TAG} pkg {packageId} 캐시 완료 ({loaded.levels.Length}레벨 v{loaded.version}).");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"{LOG_TAG} pkg {packageId} 로드 예외: {e}");
                return false;
            }
            finally
            {
                _inflightTask = null;
                _inflightPackageId = -1;
            }
        }

        /// <summary>
        /// StreamingAssets/episode_01.json 로드.
        /// Android 는 jar:file:// URI 라 UnityWebRequest 필요. 다른 플랫폼도 통일.
        /// </summary>
        private async Task<LevelEpisode> LoadBundledEpisodeAsync()
        {
            string path = Path.Combine(Application.streamingAssetsPath, BUNDLED_EP1_FILENAME);
            string json;

            using (var req = UnityWebRequest.Get(path))
            {
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"{LOG_TAG} 번들 ep1 로드 실패: {req.error} (path={path})");
                    return null;
                }
                json = req.downloadHandler.text;
            }

            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError($"{LOG_TAG} 번들 ep1 JSON 이 비어있음 (path={path}). Editor 로 export 하지 않은 듯.");
                return null;
            }

            return JsonUtility.FromJson<LevelEpisode>(json);
        }

        /// <summary>
        /// Firestore /episodes/{packageId} 로드.
        /// doc.levelsJson 문자열을 JsonUtility 로 LevelEpisode 복원.
        /// FirebaseManager.IsReady 까지 대기 (UserDataService 와 동일 패턴).
        /// </summary>
        private async Task<LevelEpisode> LoadRemoteEpisodeAsync(int packageId)
        {
            if (!await WaitForFirebaseReadyAsync())
            {
                Debug.LogError($"{LOG_TAG} FirebaseManager 미준비 — 원격 에피소드 fetch 불가.");
                return null;
            }

            var db = FirebaseEnvironment.GetFirestore();
            string docPath = $"{FIRESTORE_COLLECTION}/{packageId}";
            DocumentReference docRef = db.Document(docPath);

            DocumentSnapshot snap;
            try
            {
                snap = await docRef.GetSnapshotAsync();
            }
            catch (FirestoreException fe)
            {
                Debug.LogError($"{LOG_TAG} {docPath} fetch 실패: {fe.ErrorCode} {fe.Message}");
                return null;
            }

            if (!snap.Exists)
            {
                Debug.LogError($"{LOG_TAG} {docPath} doc 미존재 — 업로드 안 됐을 가능성.");
                return null;
            }

            var data = snap.ToDictionary();
            if (!data.TryGetValue("levelsJson", out var levelsJsonObj) || levelsJsonObj is not string levelsJson || string.IsNullOrEmpty(levelsJson))
            {
                Debug.LogError($"{LOG_TAG} {docPath} 에 levelsJson 필드 없음 또는 비어있음.");
                return null;
            }

            string encoding = data.TryGetValue("encoding", out var eObj) && eObj is string eStr ? eStr : "plain";

            // encoding 별 디코딩 — 업로더는 "gzip+b64" 사용, 미래 형식 분기 가능
            string json;
            try
            {
                json = encoding switch
                {
                    "gzip+b64" => DecodeGzipBase64(levelsJson),
                    "plain"    => levelsJson,
                    _          => null
                };
            }
            catch (Exception decodeEx)
            {
                Debug.LogError($"{LOG_TAG} {docPath} 디코딩 실패 (encoding={encoding}): {decodeEx.Message}");
                return null;
            }

            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError($"{LOG_TAG} {docPath} encoding={encoding} 미지원 또는 디코딩 결과 비어있음.");
                return null;
            }

            var wrapper = JsonUtility.FromJson<LevelEpisode>(json);
            if (wrapper != null && wrapper.levels != null) return wrapper;

            Debug.LogError($"{LOG_TAG} {docPath} levelsJson 파싱 실패.");
            return null;
        }

        private static string DecodeGzipBase64(string b64)
        {
            byte[] gzipBytes = Convert.FromBase64String(b64);
            using var ms = new MemoryStream(gzipBytes);
            using var gz = new GZipStream(ms, CompressionMode.Decompress);
            using var sr = new StreamReader(gz, Encoding.UTF8);
            return sr.ReadToEnd();
        }

        private static async Task<bool> WaitForFirebaseReadyAsync(int timeoutMs = 15000)
        {
            for (int i = 0; i < 50 && !FirebaseManager.HasInstance; i++)
                await Task.Delay(100);
            if (!FirebaseManager.HasInstance) return false;

            var fm = FirebaseManager.Instance;
            if (fm.IsReady) return true;

            var tcs = new TaskCompletionSource<bool>();
            Action onReady = null;
            onReady = () =>
            {
                if (FirebaseManager.HasInstance) FirebaseManager.Instance.OnReady -= onReady;
                tcs.TrySetResult(true);
            };
            fm.OnReady += onReady;

            var winner = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            return winner == tcs.Task && fm.IsReady;
        }
    }
}
