using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.U2D;

namespace BalloonFlow
{
    /// <summary>
    /// Addressables 런타임 wrapper.
    ///
    /// 사용 패턴:
    ///   var sprite = await AddressableSystem.LoadAssetAsync&lt;Sprite&gt;(Const.ADDR_ICON_HAND);
    ///   ...
    ///   AddressableSystem.Release(sprite);
    ///
    ///   // 또는 prefab 인스턴스화
    ///   var go = await AddressableSystem.InstantiateAsync(Const.ADDR_PREFAB_FXGOLD, parent);
    ///   AddressableSystem.ReleaseInstance(go);
    ///
    /// CDM (콘텐츠 원격 다운로드):
    ///   var size = await AddressableSystem.GetDownloadSizeAsync(Const.ADDR_LABEL_CDM);
    ///   if (size > 0)
    ///       await AddressableSystem.DownloadDependenciesAsync(Const.ADDR_LABEL_CDM, progress);
    ///
    /// 정책:
    ///   - 첫 호출 시 Addressables.InitializeAsync 자동 (idempotent)
    ///   - 핸들 캐싱: 같은 key 로 LoadAsset 호출하면 동일 핸들 재사용 (refcount)
    ///   - Release 명시 호출 책임은 호출자. 미Release 시 메모리 누수 (Addressables 표준)
    /// </summary>
    public static class AddressableSystem
    {
        private const string LOG_TAG = "[Addressable]";

        private static bool _initialized;
        private static AsyncOperationHandle<IResourceLocator> _initHandle;
        private static readonly Dictionary<string, AsyncOperationHandle> _loadedAssets = new Dictionary<string, AsyncOperationHandle>();

        /// <summary>이미 init 됐는지. false 면 LoadAsset 호출 시 자동 init.</summary>
        public static bool IsInitialized => _initialized;

        // ───────────────────────────────────────────────
        // Initialization
        // ───────────────────────────────────────────────

        /// <summary>
        /// Addressables 초기화. 카탈로그 로드 + 의존성 그래프 구성. 한 번만 실행 (idempotent).
        /// 명시 호출 안 해도 LoadAssetAsync 첫 호출 시 자동 init.
        /// </summary>
        public static async Task<bool> InitializeAsync()
        {
            if (_initialized) return true;

            try
            {
                _initHandle = Addressables.InitializeAsync(false);
                await _initHandle.Task;

                if (_initHandle.Status != AsyncOperationStatus.Succeeded)
                {
                    Debug.LogError($"{LOG_TAG} Init 실패: {_initHandle.OperationException}");
                    return false;
                }

                _initialized = true;
                Debug.Log($"{LOG_TAG} Init 완료. Locator={_initHandle.Result?.LocatorId}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"{LOG_TAG} Init 예외: {e.Message}");
                return false;
            }
        }

        // ───────────────────────────────────────────────
        // CDM Download (원격 카탈로그)
        // ───────────────────────────────────────────────

        /// <summary>해당 label/key 가 catalog 에 등록된 location 이 있는지. 없으면 download/load 시 InvalidKeyException 발생.</summary>
        public static async Task<bool> HasLocationsAsync(object keyOrLabel)
        {
            if (!await EnsureInitialized()) return false;

            try
            {
                var handle = Addressables.LoadResourceLocationsAsync(keyOrLabel);
                await handle.Task;
                bool has = handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null && handle.Result.Count > 0;
                Addressables.Release(handle);
                return has;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>해당 label/key 다운로드 필요량 (bytes). 0 이면 모두 cache 됐거나 등록된 asset 없음.</summary>
        public static async Task<long> GetDownloadSizeAsync(object keyOrLabel)
        {
            if (!await EnsureInitialized()) return 0;

            // label 에 등록된 asset 없으면 InvalidKeyException 회피 — 0 반환
            if (!await HasLocationsAsync(keyOrLabel)) return 0;

            try
            {
                var handle = Addressables.GetDownloadSizeAsync(keyOrLabel);
                await handle.Task;
                long size = handle.Result;
                Addressables.Release(handle);
                return size;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LOG_TAG} GetDownloadSize({keyOrLabel}) 실패: {e.Message}");
                return 0;
            }
        }

        /// <summary>
        /// label/key 의존성 다운로드. progress 0~1 콜백 (선택).
        /// Title scene 의 "Downloading data..." 단계에서 호출 권장.
        /// </summary>
        public static async Task<bool> DownloadDependenciesAsync(object keyOrLabel, Action<float> onProgress = null)
        {
            if (!await EnsureInitialized()) return false;

            // label 에 등록된 asset 없으면 (CDM 그룹 비어있는 상태 등) 즉시 성공 처리
            if (!await HasLocationsAsync(keyOrLabel))
            {
                onProgress?.Invoke(1f);
                return true;
            }

            AsyncOperationHandle handle = default;
            try
            {
                handle = Addressables.DownloadDependenciesAsync(keyOrLabel, autoReleaseHandle: false);

                while (!handle.IsDone)
                {
                    onProgress?.Invoke(handle.PercentComplete);
                    await Task.Yield();
                }
                onProgress?.Invoke(1f);

                bool ok = handle.Status == AsyncOperationStatus.Succeeded;
                if (!ok) Debug.LogError($"{LOG_TAG} Download({keyOrLabel}) 실패: {handle.OperationException}");
                return ok;
            }
            catch (Exception e)
            {
                Debug.LogError($"{LOG_TAG} Download({keyOrLabel}) 예외: {e.Message}");
                return false;
            }
            finally
            {
                if (handle.IsValid()) Addressables.Release(handle);
            }
        }

        // ───────────────────────────────────────────────
        // Asset Load
        // ───────────────────────────────────────────────

        /// <summary>
        /// 단일 asset 로드. 같은 key 의 두 번째 호출은 cached 핸들 결과 반환 (refcount 증가).
        /// 호출자가 Release(asset) 또는 ReleaseAll 책임.
        /// </summary>
        public static async Task<T> LoadAssetAsync<T>(string key) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!await EnsureInitialized()) return null;

            if (_loadedAssets.TryGetValue(key, out var existingHandle) && existingHandle.IsValid())
            {
                if (existingHandle.Result is T cachedResult)
                    return cachedResult;
            }

            try
            {
                var handle = Addressables.LoadAssetAsync<T>(key);
                await handle.Task;

                if (handle.Status != AsyncOperationStatus.Succeeded)
                {
                    Debug.LogWarning($"{LOG_TAG} LoadAsset<{typeof(T).Name}>({key}) 실패: {handle.OperationException?.Message}");
                    Addressables.Release(handle);
                    return null;
                }

                _loadedAssets[key] = handle;
                return handle.Result;
            }
            catch (Exception e)
            {
                Debug.LogError($"{LOG_TAG} LoadAsset<{typeof(T).Name}>({key}) 예외: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Prefab 인스턴스화 (Addressables 가 spawn + ReleaseInstance 로 destroy 까지 추적).
        /// 일반 Instantiate 대신 사용하면 ref counting 자동 관리.
        /// </summary>
        public static async Task<GameObject> InstantiateAsync(string key, Transform parent = null)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!await EnsureInitialized()) return null;

            try
            {
                var handle = Addressables.InstantiateAsync(key, parent);
                await handle.Task;

                if (handle.Status != AsyncOperationStatus.Succeeded)
                {
                    Debug.LogWarning($"{LOG_TAG} Instantiate({key}) 실패");
                    return null;
                }
                return handle.Result;
            }
            catch (Exception e)
            {
                Debug.LogError($"{LOG_TAG} Instantiate({key}) 예외: {e.Message}");
                return null;
            }
        }

        // ───────────────────────────────────────────────
        // Release
        // ───────────────────────────────────────────────

        /// <summary>LoadAssetAsync 로 받은 asset 의 핸들 release. 같은 key 의 동일 인스턴스만 효과.</summary>
        public static void Release(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_loadedAssets.TryGetValue(key, out var handle))
            {
                if (handle.IsValid()) Addressables.Release(handle);
                _loadedAssets.Remove(key);
            }
        }

        /// <summary>InstantiateAsync 로 만든 GameObject 정리. 일반 Destroy 대신 사용.</summary>
        public static bool ReleaseInstance(GameObject go)
        {
            if (go == null) return false;
            return Addressables.ReleaseInstance(go);
        }

        /// <summary>모든 loaded asset 핸들 release. 씬 종료 / 메모리 압박 시.</summary>
        public static void ReleaseAll()
        {
            foreach (var kv in _loadedAssets)
                if (kv.Value.IsValid()) Addressables.Release(kv.Value);
            _loadedAssets.Clear();
        }

        // ───────────────────────────────────────────────
        // Sprite Atlas 헬퍼
        // ───────────────────────────────────────────────

        private static readonly Dictionary<string, SpriteAtlas> _atlasCache = new Dictionary<string, SpriteAtlas>();

        /// <summary>
        /// SpriteAtlas 를 Addressable 로 로드 + 캐시. 같은 atlasKey 두 번째 호출은 캐시 반환.
        /// Atlas 자체 핸들은 _loadedAssets 에 남아있어 ReleaseAll 시 함께 정리됨.
        /// </summary>
        public static async Task<SpriteAtlas> LoadAtlasAsync(string atlasKey)
        {
            if (string.IsNullOrEmpty(atlasKey)) return null;
            if (_atlasCache.TryGetValue(atlasKey, out var cached) && cached != null)
                return cached;

            var atlas = await LoadAssetAsync<SpriteAtlas>(atlasKey);
            if (atlas != null) _atlasCache[atlasKey] = atlas;
            return atlas;
        }

        /// <summary>
        /// Atlas 에서 sprite name 으로 단건 추출. atlas 미로드면 자동 로드.
        /// 사용 예:
        ///   img.sprite = await AddressableSystem.GetSpriteAsync(Const.ADDR_ATLAS_UI, Const.SPR_ICON_HAND);
        /// </summary>
        public static async Task<Sprite> GetSpriteAsync(string atlasKey, string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return null;
            var atlas = await LoadAtlasAsync(atlasKey);
            if (atlas == null)
            {
                Debug.LogWarning($"{LOG_TAG} Atlas '{atlasKey}' 로드 실패 — sprite '{spriteName}' 못 가져옴");
                return null;
            }
            var sprite = atlas.GetSprite(spriteName);
            if (sprite == null)
                Debug.LogWarning($"{LOG_TAG} Atlas '{atlasKey}' 에 sprite '{spriteName}' 없음");
            return sprite;
        }

        /// <summary>Atlas 캐시 비우기 (로드 핸들은 _loadedAssets 에 남아있음 — Release(atlasKey) 별도 필요)</summary>
        public static void ClearAtlasCache() => _atlasCache.Clear();

        // ───────────────────────────────────────────────
        // Internal
        // ───────────────────────────────────────────────

        private static async Task<bool> EnsureInitialized()
        {
            if (_initialized) return true;
            return await InitializeAsync();
        }
    }
}
