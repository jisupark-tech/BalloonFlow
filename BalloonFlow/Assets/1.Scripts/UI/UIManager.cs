using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 중앙 UI 매니저. 모든 UI 로딩/열기/닫기를 관리.
    /// - OpenUI<T>(path) : Resources에서 프리팹 로드 → UiTr에 생성
    /// - CloseUI<T>()    : 열린 UI 닫기
    /// - LoadPrefab()    : 범용 프리팹 로드
    /// - FadeIn/FadeOut  : 씬 전환 페이드
    ///
    /// 씬 컨트롤러가 SetSceneCanvas()로 현재 씬 캔버스를 등록.
    /// </summary>
    public class UIManager : Singleton<UIManager>
    {
        #region Fields

        [Header("[UI Parent — 씬 컨트롤러가 SetSceneCanvas로 설정]")]
        public Transform UiTr;

        // 열린 UI 스택
        private List<UIBase> _openUIList = new List<UIBase>();

        // Fade
        private CanvasGroup _fadeOverlay;
        private Canvas _fadeCanvas;
        private Coroutine _fadeCoroutine;

        #endregion

        #region SetSceneCanvas

        /// <summary>
        /// 각 씬 컨트롤러가 Awake/Start에서 호출.
        /// 해당 씬의 Canvas Transform을 UI 부모로 설정.
        /// </summary>
        public void SetSceneCanvas(Transform _canvasTr)
        {
            UiTr = _canvasTr;
        }

        #endregion

        #region OpenUI / CloseUI

        /// <summary>
        /// Resources에서 프리팹 로드 → UiTr 아래 생성 → T 컴포넌트 리턴.
        /// path 예시: "UI/UITitle", "Popup/PopupSettings"
        /// </summary>
        public T OpenUI<T>(string _path) where T : UIBase
        {
            var _prefab = Resources.Load<GameObject>(_path);
            if (_prefab == null)
            {
                Debug.LogError($"[UIManager] Prefab not found: {_path}");
                return null;
            }

            var _go = Instantiate(_prefab, UiTr);
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
        /// 열린 UI 전부 닫기.
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

        public void FadeOut(float _duration)
        {
            EnsureFadeOverlay();
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeCoroutine(0f, 1f, _duration));
        }

        public void FadeIn(float _duration)
        {
            EnsureFadeOverlay();
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeCoroutine(1f, 0f, _duration));
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

            var _go = new GameObject("FadeOverlay");
            _go.layer = LayerMask.NameToLayer("UI");
            _go.transform.SetParent(_canvasGO.transform, false);
            var _rt = _go.AddComponent<RectTransform>();
            _rt.anchorMin = Vector2.zero;
            _rt.anchorMax = Vector2.one;
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
            var _img = _go.AddComponent<Image>();
            _img.color = Color.black;
            _img.raycastTarget = true;

            _fadeOverlay = _go.AddComponent<CanvasGroup>();
            _fadeOverlay.alpha = 0f;
            _fadeOverlay.interactable = false;
            _fadeOverlay.blocksRaycasts = false;
        }

        IEnumerator FadeCoroutine(float _from, float _to, float _duration)
        {
            if (_fadeOverlay == null) yield break;

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
        }

        #endregion
    }
}
