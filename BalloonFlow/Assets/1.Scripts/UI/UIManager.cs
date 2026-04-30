using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 중앙 UI 매니저. 모든 UI 로딩/열기/닫기를 관리.
    /// Canvas를 두 개로 분리:
    ///   - UiTr (UICanvas): 일반 UI (UILobby, UIHud 등)
    ///   - PopupTr (PopupCanvas): 팝업 (PopupSettings, PopupGoldShop 등) — UI 위에 렌더링
    /// 씬 컨트롤러가 SetSceneCanvas()로 현재 씬 캔버스를 등록.
    /// </summary>
    public class UIManager : Singleton<UIManager>
    {
        #region Fields

        [Header("[UI Canvas — 일반 UI 부모]")]
        public Transform UiTr;

        [Header("[Popup Canvas — 팝업 부모 (UI 위에 렌더링)]")]
        public Transform PopupTr;

        [Header("[Effect Canvas — 팝업 위에 렌더링되는 이펙트 전용]")]
        public Transform EffectTr;

        [Header("[Fade — 기본 전환 이미지 (선택)]")]
        [SerializeField] private Sprite _fadeImage;

        // 열린 UI 스택
        private List<UIBase> _openUIList = new List<UIBase>();

        // Fade
        private CanvasGroup _fadeOverlay;
        private Canvas _fadeCanvas;
        private Coroutine _fadeCoroutine;
        private Image _fadeImageDisplay;
        private Sprite _currentFadeSprite;

        /// <summary>현재 fade 진행 중 또는 overlay가 가시 상태인지. 중복 fade 호출 방지용.</summary>
        public bool IsFading => _fadeCoroutine != null || (_fadeOverlay != null && _fadeOverlay.alpha > 0.01f);

        #endregion

        #region SetSceneCanvas

        /// <summary>
        /// 각 씬 컨트롤러가 Awake/Start에서 호출.
        /// UICanvas와 PopupCanvas를 동시에 설정.
        /// PopupCanvas가 없으면 UICanvas를 공유.
        /// 등록된 캔버스의 root 를 DontDestroyOnLoad 처리해 씬 전환 후에도 유지 — 다음 씬은 동일 캔버스 재사용.
        /// </summary>
        public void SetSceneCanvas(Transform _uiCanvasTr, Transform _popupCanvasTr = null, Transform _effectCanvasTr = null)
        {
            UiTr = _uiCanvasTr;
            PopupTr = _popupCanvasTr != null ? _popupCanvasTr : _uiCanvasTr;
            EffectTr = _effectCanvasTr != null ? _effectCanvasTr : PopupTr;

            PersistRoot(UiTr);
            if (PopupTr != UiTr) PersistRoot(PopupTr);
            if (EffectTr != PopupTr && EffectTr != UiTr) PersistRoot(EffectTr);
        }

        /// <summary>현재 등록된 UI/Popup/Effect 캔버스가 모두 살아있는지 (씬 전환 후 fake-null 아닌지).</summary>
        public bool HasLiveSceneCanvas
            => UiTr != null && PopupTr != null && EffectTr != null;

        private static void PersistRoot(Transform t)
        {
            if (t == null) return;
            var root = t.root.gameObject;
            // 이미 DontDestroyOnLoad 인 GameObject 는 무시 (root.scene.buildIndex == -1)
            if (root.scene.buildIndex < 0) return;
            DontDestroyOnLoad(root);
        }

        #endregion

        #region OpenUI / CloseUI

        /// <summary>
        /// Resources에서 프리팹 로드 → 부모 Canvas에 생성 → T 컴포넌트 리턴.
        /// path가 "Popup/"으로 시작하면 PopupTr에, 아니면 UiTr에 생성.
        /// 중복 생성 방지: (1) _openUIList stale cleanup → (2) 리스트 재사용 → (3) Scene FindAny → (4) Instantiate.
        /// </summary>
        public T OpenUI<T>(string _path) where T : UIBase
        {
            // (1) 씬 전환 등으로 destroyed 된 항목을 리스트에서 정리 (Unity fake-null)
            for (int i = _openUIList.Count - 1; i >= 0; i--)
            {
                if (_openUIList[i] == null) _openUIList.RemoveAt(i);
            }

            // (2) 살아있는 인스턴스가 리스트에 이미 있으면 재사용
            for (int i = 0; i < _openUIList.Count; i++)
            {
                if (_openUIList[i] is T existing && existing != null)
                {
                    existing.OpenUI();
                    return existing;
                }
            }

            // (3) 리스트엔 없지만 씬 hierarchy 에 prefab 이 미리 배치돼 있거나,
            //     다른 경로로 instantiate 된 경우 그것을 채택 (중복 instantiate 방지).
            //     비활성 상태도 포함하기 위해 FindObjectsInactive.Include 사용.
            var existingInScene = FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (existingInScene != null)
            {
                _openUIList.Add(existingInScene);
                existingInScene.OpenUI();
                return existingInScene;
            }

            // (4) Addressables 우선 시도 — Resources 외 마이그레이션 된 prefab 들 (popup_*, ui_* 키).
            //     Addressables 가 sync API 가 없어 동기 path 호환 위해 ResourceManager 의 사전 로드 캐시 사용.
            //     Cache miss 시 Resources.Load 폴백 (UITitle, PopupError 가 그 경로로 동작).
            var _prefab = ResourceManager.HasInstance
                ? ResourceManager.Instance.GetCachedAddressablePrefab(_path)
                : null;
            if (_prefab == null) _prefab = Resources.Load<GameObject>(_path);
            if (_prefab == null)
            {
                Debug.LogError($"[UIManager] Prefab not found (Addressables/Resources): {_path}");
                return null;
            }

            Transform parent = _path.StartsWith("Popup/") ? PopupTr : UiTr;
            var _go = Instantiate(_prefab, parent);
            var _ui = _go.GetComponent<T>();

            if (_ui != null)
            {
                _ui.OpenUI();
                _openUIList.Add(_ui);
            }

            return _ui;
        }

        /// <summary>
        /// OpenUI + Init 데이터 전달.
        /// </summary>
        public T OpenUI<T>(string _path, object[] _data) where T : UIBase
        {
            T _ui = OpenUI<T>(_path);
            if (_ui != null)
                _ui.Init(_data);
            return _ui;
        }

        /// <summary>
        /// 타입으로 열린 UI 닫기.
        /// </summary>
        public void CloseUI<T>() where T : UIBase
        {
            for (int i = _openUIList.Count - 1; i >= 0; i--)
            {
                if (_openUIList[i] is T)
                {
                    _openUIList[i].CloseUI();
                    _openUIList.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// 열린 UI 전부 닫기 (alpha=0 으로 숨김. GameObject 는 유지).
        /// </summary>
        public void CloseUIAll()
        {
            for (int i = _openUIList.Count - 1; i >= 0; i--)
            {
                if (_openUIList[i] != null)
                    _openUIList[i].CloseUI();
            }
            _openUIList.Clear();
        }

        /// <summary>
        /// 열린 UI 전부 GameObject 단위로 파괴 + 리스트 초기화. 씬 전환 시 직전 씬의 UI 제거용.
        /// 캔버스(UiTr/PopupTr/EffectTr)는 DontDestroyOnLoad 로 살아있으므로 보존됨.
        /// </summary>
        public void DestroyAllUI()
        {
            for (int i = _openUIList.Count - 1; i >= 0; i--)
            {
                var ui = _openUIList[i];
                if (ui != null) Destroy(ui.gameObject);
            }
            _openUIList.Clear();
        }

        /// <summary>
        /// 특정 UI가 열려있는지 확인.
        /// </summary>
        public bool IsOpenUI<T>() where T : UIBase
        {
            for (int i = 0; i < _openUIList.Count; i++)
            {
                if (_openUIList[i] is T)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 열린 UI 중 T 타입 찾아서 리턴.
        /// </summary>
        public T GetOpenUI<T>() where T : UIBase
        {
            for (int i = 0; i < _openUIList.Count; i++)
            {
                if (_openUIList[i] is T _found)
                    return _found;
            }
            return null;
        }

        #endregion

        #region LoadPrefab

        /// <summary>
        /// 범용 프리팹 로드. Resources 기준 경로.
        /// </summary>
        public GameObject LoadPrefab(string _path, Transform _parent)
        {
            var _go = Instantiate(Resources.Load<GameObject>(_path), _parent);
            if (_go != null)
            {
                _go.transform.localScale = Vector3.one;
                _go.transform.localPosition = Vector3.zero;
            }
            return _go;
        }

        #endregion

        #region Fade

        /// <summary>Fade to opaque (black or custom image). Standard call.</summary>
        public void FadeOut(float _duration)
        {
            FadeOut(_duration, null);
        }

        /// <summary>Fade to opaque with optional custom image overlay.</summary>
        public void FadeOut(float _duration, Sprite _image)
        {
            EnsureFadeOverlay();
            ApplyFadeImage(_image);
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeCoroutine(0f, 1f, _duration));
        }

        /// <summary>Fade from opaque to transparent. Standard call.</summary>
        public void FadeIn(float _duration)
        {
            FadeIn(_duration, null);
        }

        /// <summary>Fade from opaque to transparent with optional custom image overlay.</summary>
        public void FadeIn(float _duration, Sprite _image)
        {
            EnsureFadeOverlay();
            ApplyFadeImage(_image);
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeCoroutine(1f, 0f, _duration));
        }

        /// <summary>
        /// Set the default fade image. Used by Inspector or at runtime.
        /// Pass null to revert to solid black.
        /// </summary>
        public void SetFadeImage(Sprite _sprite)
        {
            _fadeImage = _sprite;
        }

        /// <summary>Revert to solid black fade.</summary>
        public void ClearFadeImage()
        {
            _fadeImage = null;
            _currentFadeSprite = null;
            if (_fadeImageDisplay != null)
                _fadeImageDisplay.enabled = false;
        }

        void EnsureFadeOverlay()
        {
            if (_fadeOverlay != null) return;

            var _canvasGO = new GameObject("FadeCanvas");
            _canvasGO.layer = LayerMask.NameToLayer("UI");
            _canvasGO.transform.SetParent(transform, false);
            _fadeCanvas = _canvasGO.AddComponent<Canvas>();
            _fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _fadeCanvas.sortingOrder = 999;
            _canvasGO.AddComponent<GraphicRaycaster>();

            // Solid #1B58A5 background overlay (letterbox area during scene transition)
            var _go = new GameObject("FadeOverlay");
            _go.layer = LayerMask.NameToLayer("UI");
            _go.transform.SetParent(_canvasGO.transform, false);
            var _rt = _go.AddComponent<RectTransform>();
            _rt.anchorMin = Vector2.zero;
            _rt.anchorMax = Vector2.one;
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
            var _img = _go.AddComponent<Image>();
            _img.color = new Color(0x1B / 255f, 0x58 / 255f, 0xA5 / 255f);
            _img.raycastTarget = true;

            _fadeOverlay = _go.AddComponent<CanvasGroup>();
            _fadeOverlay.alpha = 0f;
            _fadeOverlay.interactable = false;
            _fadeOverlay.blocksRaycasts = false;

            // Custom image overlay (on top of black, disabled by default)
            var _imgGO = new GameObject("FadeImage");
            _imgGO.layer = LayerMask.NameToLayer("UI");
            _imgGO.transform.SetParent(_canvasGO.transform, false);
            var _imgRT = _imgGO.AddComponent<RectTransform>();
            _imgRT.anchorMin = Vector2.zero;
            _imgRT.anchorMax = Vector2.one;
            _imgRT.offsetMin = Vector2.zero;
            _imgRT.offsetMax = Vector2.zero;
            _fadeImageDisplay = _imgGO.AddComponent<Image>();
            _fadeImageDisplay.preserveAspect = true;
            _fadeImageDisplay.raycastTarget = false;
            _fadeImageDisplay.enabled = false;

            // Add the image GO under the same CanvasGroup so alpha controls both
            _imgGO.transform.SetParent(_go.transform, false);
            _imgRT.anchorMin = Vector2.zero;
            _imgRT.anchorMax = Vector2.one;
            _imgRT.offsetMin = Vector2.zero;
            _imgRT.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Applies a fade image for the current transition.
        /// If _image is null, uses the default _fadeImage. If both are null, shows solid black.
        /// </summary>
        void ApplyFadeImage(Sprite _image)
        {
            if (_fadeImageDisplay == null) return;

            Sprite _resolved = _image != null ? _image : _fadeImage;

            if (_resolved != null)
            {
                _fadeImageDisplay.sprite = _resolved;
                _fadeImageDisplay.color = Color.white;
                _fadeImageDisplay.enabled = true;
                _currentFadeSprite = _resolved;
            }
            else
            {
                _fadeImageDisplay.enabled = false;
                _currentFadeSprite = null;
            }
        }

        IEnumerator FadeCoroutine(float _from, float _to, float _duration)
        {
            if (_fadeOverlay == null)
            {
                _fadeCoroutine = null;
                yield break;
            }

            _fadeOverlay.alpha = _from;
            _fadeOverlay.blocksRaycasts = true;

            float _elapsed = 0f;
            while (_elapsed < _duration)
            {
                _elapsed += Time.unscaledDeltaTime;
                _fadeOverlay.alpha = Mathf.Lerp(_from, _to, _elapsed / _duration);
                yield return null;
            }

            _fadeOverlay.alpha = _to;
            _fadeOverlay.blocksRaycasts = _to > 0.01f;
            _fadeOverlay.interactable = false;

            // Disable custom image when fully transparent
            if (_to < 0.01f && _fadeImageDisplay != null)
                _fadeImageDisplay.enabled = false;

            // 코루틴 완료 표시 — IsFading 이 영구 true 로 남아 LevelManager 의 next-stage FadeOut 이 skip 되는 버그 방지.
            // (FadeOut 으로 alpha=1 도달했더라도 코루틴 자체는 끝났으므로 null 처리. IsFading 의 alpha 체크가 overlay 가시성을 별도로 다룸.)
            _fadeCoroutine = null;
        }

        #endregion
    }
}
