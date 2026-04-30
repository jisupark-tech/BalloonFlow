using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.U2D;

namespace BalloonFlow
{
    /// <summary>
    /// Persistent resource manager. Loads assets from Resources/ folder
    /// and integrates with ObjectPoolManager for pooled instantiation.
    /// </summary>
    /// <remarks>
    /// Layer: Core | Genre: Puzzle | Role: Manager | Phase: 0
    /// </remarks>
    public class ResourceManager : Singleton<ResourceManager>
    {
        #region Fields

        private readonly Dictionary<string, Object> _cache = new Dictionary<string, Object>();

        #endregion

        #region Public Methods

        /// <summary>
        /// Loads an asset from Resources/ with caching.
        /// </summary>
        public T Load<T>(string path) where T : Object
        {
            if (_cache.TryGetValue(path, out Object cached))
            {
                return cached as T;
            }

            T asset = Resources.Load<T>(path);
            if (asset != null)
            {
                _cache[path] = asset;
            }
            else
            {
                Debug.LogWarning($"[ResourceManager] Asset not found: {path}");
            }

            return asset;
        }

        /// <summary>
        /// Loads a prefab and instantiates it.
        /// </summary>
        public GameObject Instantiate(string prefabPath, Vector3 position, Quaternion rotation)
        {
            GameObject prefab = Load<GameObject>(prefabPath);
            if (prefab == null) return null;
            return Object.Instantiate(prefab, position, rotation);
        }

        /// <summary>
        /// Instantiates a prefab under a parent transform.
        /// </summary>
        public GameObject Instantiate(string prefabPath, Transform parent)
        {
            GameObject prefab = Load<GameObject>(prefabPath);
            if (prefab == null) return null;
            return Object.Instantiate(prefab, parent);
        }

        /// <summary>
        /// Gets or creates an object from the pool. Falls back to Resources.Load if pool has no prefab registered.
        /// </summary>
        public GameObject GetPooled(string poolKey)
        {
            if (ObjectPoolManager.HasInstance)
            {
                return ObjectPoolManager.Instance.Get(poolKey);
            }

            Debug.LogWarning($"[ResourceManager] ObjectPoolManager not available for key '{poolKey}'. Using direct instantiate.");
            return Instantiate($"Prefabs/{poolKey}", Vector3.zero, Quaternion.identity);
        }

        /// <summary>
        /// Returns an object to the pool.
        /// </summary>
        public void ReturnPooled(string poolKey, GameObject obj)
        {
            if (ObjectPoolManager.HasInstance)
            {
                ObjectPoolManager.Instance.Return(poolKey, obj);
                return;
            }

            if (obj != null) Destroy(obj);
        }

        /// <summary>
        /// Clears the asset cache, releasing references.
        /// </summary>
        public void ClearCache()
        {
            _cache.Clear();
        }

        // ───────────────────────────────────────────────
        // UI Atlas — Addressable 기반 중앙 로딩 + sync 접근
        // ───────────────────────────────────────────────

        private SpriteAtlas _uiAtlas;
        private bool _uiAtlasLoading;

        /// <summary>UI atlas 가 메모리에 로드된 상태인지.</summary>
        public bool IsUIAtlasLoaded => _uiAtlas != null;

        /// <summary>
        /// UI atlas (Const.ADDR_ATLAS_UI) 를 Addressables 에서 로드 후 캐시.
        /// TitleController.LoadingFlow Step 0 에서 호출 권장 — popup 들이 sync 로 sprite 받을 수 있게 사전 로드.
        /// idempotent: 이미 로드됐거나 로딩 중이면 그 결과 기다리고 반환.
        /// </summary>
        public async Task<bool> PreloadUIAtlasAsync()
        {
            if (_uiAtlas != null) return true;
            if (_uiAtlasLoading)
            {
                while (_uiAtlasLoading) await Task.Yield();
                return _uiAtlas != null;
            }

            _uiAtlasLoading = true;
            _uiAtlas = await AddressableSystem.LoadAtlasAsync(Const.ADDR_ATLAS_UI);
            _uiAtlasLoading = false;

            if (_uiAtlas == null)
                Debug.LogWarning("[ResourceManager] UI atlas load 실패 — popup 들은 Inspector fallback sprite 사용");
            else
                Debug.Log($"[ResourceManager] UI atlas 로드 완료 ({_uiAtlas.spriteCount} sprites)");
            return _uiAtlas != null;
        }

        /// <summary>UI atlas 에서 sprite 단건 sync 추출. atlas 미로드 또는 sprite 없음이면 null.</summary>
        public Sprite GetUISprite(string spriteName)
        {
            if (_uiAtlas == null || string.IsNullOrEmpty(spriteName)) return null;
            return _uiAtlas.GetSprite(spriteName);
        }

        /// <summary>
        /// UI atlas 에서 sprite 추출 — 없으면 fallback 반환. popup Awake 에서 1줄로 sprite 교체용.
        /// 사용 예:  _sprHand = ResourceManager.Instance.UISpriteOr(\"iconHand\", _sprHand);
        /// </summary>
        public Sprite UISpriteOr(string spriteName, Sprite fallback)
        {
            var s = GetUISprite(spriteName);
            return s != null ? s : fallback;
        }

        // ───────────────────────────────────────────────
        // Addressable Prefab 사전 로드 + Resources-path 호환
        // ───────────────────────────────────────────────

        /// <summary>Addressable 키 (popup_* / ui_* / prefab_*) 로 사전 로드된 prefab 캐시.</summary>
        private readonly Dictionary<string, GameObject> _addrPrefabCache = new Dictionary<string, GameObject>();
        /// <summary>Resources path 형태 (\"Popup/PopupResult\") → Addressable 키 (\"popup_PopupResult\") 매핑 캐시.</summary>
        private readonly Dictionary<string, string> _resourcePathToAddrKey = new Dictionary<string, string>();

        /// <summary>
        /// Local_Always (core) + Local_OnDemand (ui) 라벨의 모든 prefab 을 일괄 사전 로드.
        /// Title.LoadingFlow Step 0 에서 호출 권장 — UIManager.OpenUI 가 sync 로 prefab 사용 가능해짐.
        /// </summary>
        public async Task PreloadAddressablePrefabsAsync()
        {
            await PreloadByLabelAsync(Const.ADDR_LABEL_CORE);
            await PreloadByLabelAsync(Const.ADDR_LABEL_UI);
        }

        /// <summary>label 단위 일괄 사전 로드. asset key 와 Resources-path 매핑 둘 다 캐시.</summary>
        public async Task PreloadByLabelAsync(string label)
        {
            if (string.IsNullOrEmpty(label)) return;
            if (!await AddressableSystem.HasLocationsAsync(label)) return;

            // label 의 모든 location 을 통해 GameObject 만 batch 로드
            var locTask = UnityEngine.AddressableAssets.Addressables.LoadResourceLocationsAsync(label, typeof(GameObject));
            await locTask.Task;
            if (locTask.Status != UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
            {
                UnityEngine.AddressableAssets.Addressables.Release(locTask);
                return;
            }

            int loaded = 0;
            foreach (var loc in locTask.Result)
            {
                string addrKey = loc.PrimaryKey;
                if (_addrPrefabCache.ContainsKey(addrKey)) continue;

                var go = await AddressableSystem.LoadAssetAsync<GameObject>(addrKey);
                if (go == null) continue;
                _addrPrefabCache[addrKey] = go;

                // popup_PopupResult / ui_UIHud / prefab_Balloon → Resources path 매핑
                string resourcesPath = AddrKeyToResourcesPath(addrKey);
                if (!string.IsNullOrEmpty(resourcesPath))
                    _resourcePathToAddrKey[resourcesPath] = addrKey;
                loaded++;
            }
            UnityEngine.AddressableAssets.Addressables.Release(locTask);
            Debug.Log($"[ResourceManager] Preload label '{label}' — {loaded} prefab cached");
        }

        /// <summary>UIManager.OpenUI 가 호출 — Resources-path 형태로 prefab 캐시 lookup.</summary>
        public GameObject GetCachedAddressablePrefab(string resourcesPath)
        {
            if (string.IsNullOrEmpty(resourcesPath)) return null;
            if (!_resourcePathToAddrKey.TryGetValue(resourcesPath, out var addrKey)) return null;
            return _addrPrefabCache.TryGetValue(addrKey, out var go) ? go : null;
        }

        /// <summary>Addressable key 직접 조회용 (이미 알고 있을 때).</summary>
        public GameObject GetCachedPrefabByKey(string addressableKey)
            => _addrPrefabCache.TryGetValue(addressableKey, out var go) ? go : null;

        /// <summary>"popup_PopupResult" → "Popup/PopupResult", "ui_UIHud" → "UI/UIHud", "prefab_Balloon" → "Prefabs/Balloon".</summary>
        private static string AddrKeyToResourcesPath(string addrKey)
        {
            if (string.IsNullOrEmpty(addrKey)) return null;
            if (addrKey.StartsWith("popup_"))  return "Popup/"   + addrKey.Substring("popup_".Length);
            if (addrKey.StartsWith("ui_"))     return "UI/"      + addrKey.Substring("ui_".Length);
            if (addrKey.StartsWith("prefab_")) return "Prefabs/" + addrKey.Substring("prefab_".Length);
            return null;
        }

        /// <summary>
        /// Removes a specific asset from the cache.
        /// </summary>
        public void Unload(string path)
        {
            _cache.Remove(path);
        }

        #endregion
    }
}
