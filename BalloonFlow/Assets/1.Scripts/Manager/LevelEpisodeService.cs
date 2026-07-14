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
    /// - ROLLBACK_LEVEL_LOCAL_FIRST_20260713: 로컬 우선(오프라인 견고성) — StreamingAssets/episode_{NN}.json
    ///   이 있으면 네트워크 0 으로 즉시 로드. 없으면(신규/미배치 에피소드) Firestore /episodes/{packageId} 폴백.
    ///   저사양·불안정망 관객 대상 전 구간 오프라인 플레이 목적. (서버측 레벨 업데이트 반영은 앱 갱신 필요 —
    ///   추후 version 게이트로 remote>local 시 원격 우선 옵션 가능.)
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
        // ROLLBACK_AB_EDITORDATA_20260714: 전역 A/B — B 유저는 전 에피소드에서 episode_XX_B.json(Test) 우선, 없으면 base(Master).
        //   파일명 규약: base=episode_{NN}.json, B변형=episode_{NN}_B.json (대문자 _B, Android/iOS 대소문자 구분).
        private static string EpisodeFileName(int packageId, bool variantB)
            => variantB ? $"episode_{packageId:D2}_B.json" : $"episode_{packageId:D2}.json";

#if UNITY_EDITOR
        // ROLLBACK_AB_EDITORDATA_20260714: 에디터 Play 는 EditorData 를 직접 읽어 저작 즉시 반영(빌드 bake 불필요).
        //   base=Master, B변형=Test. 빌드에서는 이 폴더가 스트립되므로 StreamingAssets(bake 결과)를 읽는다(#else 경로).
        public const string EDITORDATA_MASTER_DIR = "Assets/EditorData/Master";
        public const string EDITORDATA_TEST_DIR   = "Assets/EditorData/B";
#endif
        public  const int    LEVELS_PER_EPISODE    = 20;
        // ROLLBACK_TOTAL_EPISODES_14_20260713: 실제 저작 콘텐츠는 280레벨(14에피소드)까지 — ep15(281~300)는 미저작.
        //   15(=300)로 두면 280 클리어 후 281 진입을 시도해 콘텐츠 소진 판정이 늦고 가짜 레벨이 노출됐다.
        //   14 로 낮춰 GetLevelCount=280 → all-clear 가 정확히 280 에서 발동. ep15 저작·업로드 완료 시 15 로 환원.
        public  const int    TOTAL_EPISODES        = 14;
        public  const int    BUNDLED_PACKAGE_ID    = 1;

        private LevelEpisode _cached;
        private int          _cachedPackageId = -1;
        private Task<bool>   _inflightTask;
        private int          _inflightPackageId = -1;

        // ROLLBACK_ALL_CLEAR_PLAY_BLOCK_20260708: Firebase 에 실제 존재가 '실측 확인'된 에피소드 상한.
        //   TOTAL_EPISODES 는 설계 목표치일 뿐 업로드 안 된 에피소드가 있을 수 있다(예: 14개만 업로드).
        //   원격 fetch 가 'doc 미존재'(snap.Exists=false — 네트워크/파싱 오류와 구분되는 결정적 신호)로
        //   실패하면 직전 에피소드까지로 낮춰 PlayerPrefs 기록. 이후 업로드되어 fetch 성공하면 자동 상향.
        //   GetLevelCount(전량 클리어 진입 차단 판정)의 실측 소스. 기본값 = TOTAL_EPISODES(낙관).
        private const string PREFS_MAX_AVAILABLE_EP = "BF_MaxAvailableEpisodes";
        private static int _knownAvailableEpisodes = -1;

        public static int KnownAvailableEpisodes
        {
            get
            {
                if (_knownAvailableEpisodes < 0)
                    _knownAvailableEpisodes = PlayerPrefs.GetInt(PREFS_MAX_AVAILABLE_EP, TOTAL_EPISODES);
                return Mathf.Clamp(_knownAvailableEpisodes, 1, TOTAL_EPISODES);
            }
        }

        // ROLLBACK_AB_EDITORDATA_20260714: 전량 클리어 게이트용 '실측 최대 에피소드'.
        //   에디터: EditorData/{Master,Test} 파일에서 최대 번호. 빌드: Resources/episodes_max(빌드 baker 가 기록).
        //   → GetLevelCount(=차단 기준)이 하드코딩 TOTAL_EPISODES 가 아니라 실제 보유 콘텐츠를 따른다.
        //   코호트별: A(base) = Master 최대, B = Master∪Test 최대(Test 전용 신규 에피소드까지 도달 가능).
        private static int _maxMaster = -1, _maxBoth = -1;
        public static int DetectedMaxEpisode
        {
            get
            {
                EnsureMaxDetected();
                bool wantB = AbTestService.IsEnabled && AbTestService.IsVariantB;
                int m = wantB ? _maxBoth : _maxMaster;
                return m > 0 ? m : TOTAL_EPISODES; // 폴백(탐지 실패 시 설계치)
            }
        }

        private static void EnsureMaxDetected()
        {
            if (_maxMaster > 0) return;
#if UNITY_EDITOR
            _maxMaster = MaxEpisodeNumInDir(EDITORDATA_MASTER_DIR);
            _maxBoth   = System.Math.Max(_maxMaster, MaxEpisodeNumInDir(EDITORDATA_TEST_DIR));
#else
            _maxMaster = ReadManifestInt("episodes_max");     // Master 최대
            _maxBoth   = ReadManifestInt("episodes_max_b");   // Master∪Test 최대
#endif
            if (_maxMaster <= 0) _maxMaster = TOTAL_EPISODES;
            if (_maxBoth < _maxMaster) _maxBoth = _maxMaster;
        }

#if !UNITY_EDITOR
        private static int ReadManifestInt(string res)
        {
            var ta = Resources.Load<TextAsset>(res);
            return (ta != null && int.TryParse(ta.text.Trim(), out int n)) ? n : 0;
        }
#endif

#if UNITY_EDITOR
        // episode_XX.json / episode_XX_B.json 파일명에서 최대 XX. 폴더 없으면 0.
        private static int MaxEpisodeNumInDir(string dir)
        {
            if (!System.IO.Directory.Exists(dir)) return 0;
            int max = 0;
            foreach (var f in System.IO.Directory.GetFiles(dir, "episode_*.json"))
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(f); // episode_14 or episode_14_B
                string[] parts = name.Split('_');
                if (parts.Length >= 2 && int.TryParse(parts[1], out int n)) max = System.Math.Max(max, n);
            }
            return max;
        }
#endif

        private static void SetKnownAvailableEpisodes(int value)
        {
            value = Mathf.Clamp(value, 1, TOTAL_EPISODES);
            if (value == KnownAvailableEpisodes) return;
            _knownAvailableEpisodes = value;
            PlayerPrefs.SetInt(PREFS_MAX_AVAILABLE_EP, value);
            PlayerPrefs.Save();
            Debug.Log($"{LOG_TAG} 가용 에피소드 상한(실측) 갱신 → {value} (총 레벨 {value * LEVELS_PER_EPISODE})");
        }

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
                // ROLLBACK_AB_EDITORDATA_20260714: 전역 A/B + 로컬 전용(Firebase 보류).
                //   B 유저: episode_{NN}_B(Test/StreamingAssets) 우선, 없으면 base(Master) 폴백. A 유저: base.
                //   원격(Firestore) 폴백 없음 — 모든 에피소드는 EditorData(→빌드 bake) 에서만.
                bool wantB = AbTestService.IsEnabled && AbTestService.IsVariantB;
                LevelEpisode loaded = null;
                if (wantB)
                {
                    loaded = await LoadBundledEpisodeAsync(packageId, true);
                    if (loaded == null)
                        Debug.Log($"{LOG_TAG} pkg {packageId} B변형(_B) 없음 → base 폴백.");
                }
                if (loaded == null)
                    loaded = await LoadBundledEpisodeAsync(packageId, false);

                if (loaded == null || loaded.levels == null || loaded.levels.Length == 0)
                {
                    Debug.LogError($"{LOG_TAG} pkg {packageId} — 로컬 로드 실패 또는 빈 에피소드(원격 폴백 없음/Firebase 보류).");
                    return false;
                }

                // 이전 에피소드 해제 (메모리 1개 유지 정책)
                if (_cached != null && _cachedPackageId != packageId)
                    Debug.Log($"{LOG_TAG} 이전 캐시 pkg {_cachedPackageId} 해제 → pkg {packageId} 로 교체.");

                _cached = loaded;
                _cachedPackageId = packageId;
                Debug.Log($"{LOG_TAG} pkg {packageId} 캐시 완료 ({loaded.levels.Length}레벨 v{loaded.version}).");
                // ROLLBACK_ALL_CLEAR_PLAY_BLOCK_20260708: 상한 이하로 기록돼 있던 에피소드가 실제로
                //   존재(뒤늦게 업로드) → 실측 상한 자동 상향(전량 클리어 차단 자동 해제).
                if (packageId > KnownAvailableEpisodes)
                    SetKnownAvailableEpisodes(packageId);
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
        // ROLLBACK_AB_EDITORDATA_20260714: variantB=true 면 episode_{NN}_B, false 면 base episode_{NN}.
        //   에디터: EditorData(Test/Master) 직접 읽기. 빌드: StreamingAssets(bake 결과). 없으면 null(상위에서 base 폴백/에러).
        private async Task<LevelEpisode> LoadBundledEpisodeAsync(int packageId, bool variantB)
        {
            string fileName = EpisodeFileName(packageId, variantB);
            byte[] bytes;

#if UNITY_EDITOR
            string filePath = System.IO.Path.Combine(variantB ? EDITORDATA_TEST_DIR : EDITORDATA_MASTER_DIR, fileName);
            if (!System.IO.File.Exists(filePath))
            {
                Debug.Log($"{LOG_TAG} EditorData 없음: {filePath}");
                return null;
            }
            bytes = System.IO.File.ReadAllBytes(filePath);
            await Task.Yield();
#else
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            // Android 는 jar:file:// URI 지만 macOS 등은 스킴 없는 절대경로 → file:// 보정.
            string url = path.Contains("://") ? path : "file://" + path;
            using (var req = UnityWebRequest.Get(url))
            {
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.Log($"{LOG_TAG} 번들 없음 {fileName}: {req.error} (path={path})");
                    return null;
                }
                bytes = req.downloadHandler.data;
            }
#endif

            if (bytes == null || bytes.Length == 0)
            {
                Debug.LogWarning($"{LOG_TAG} 번들 {fileName} 비어있음.");
                return null;
            }

            // ROLLBACK_LEVEL_LOCAL_FIRST_20260713: 용량 절감을 위해 gzip 번들도 허용(평문 대비 ~10x 절감).
            //   gzip 매직(0x1f 0x8b)이면 해제, 아니면 평문 UTF8 — 같은 episode_NN.json 이름으로 평문/gzip 둘 다 지원.
            //   (평문 ep1 은 그대로 동작. 신흥시장 다운로드 용량 민감성 대응.)
            string json;
            try
            {
                if (bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
                {
                    using var ms = new MemoryStream(bytes);
                    using var gz = new GZipStream(ms, CompressionMode.Decompress);
                    using var sr = new StreamReader(gz, Encoding.UTF8);
                    json = sr.ReadToEnd();
                }
                else
                {
                    json = Encoding.UTF8.GetString(bytes);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LOG_TAG} 로컬 번들 pkg {packageId} 디코딩 실패: {e.Message} → 원격 폴백.");
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
                // ROLLBACK_ALL_CLEAR_PLAY_BLOCK_20260708: doc 미존재 = 콘텐츠 소진의 결정적 신호
                //   (FirestoreException/파싱 실패 같은 일시 오류와 구분) → 실측 상한을 직전까지로 기록.
                SetKnownAvailableEpisodes(packageId - 1);
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
