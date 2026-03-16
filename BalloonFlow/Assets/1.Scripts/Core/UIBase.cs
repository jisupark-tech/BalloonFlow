using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 모든 UI 패널의 베이스 클래스.
    /// OpenUI/CloseUI/ShowUI/HideUI 라이프사이클 제공.
    /// CanvasGroup이 있으면 alpha/interactable/blocksRaycasts 자동 제어.
    /// </summary>
    public class UIBase : MonoBehaviour
    {
        protected CanvasGroup _canvasGroup;

        protected virtual void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
        }

        /// <summary>초기화. 필요한 데이터 전달 시 사용.</summary>
        public virtual void Init(object[] _data) { }

        /// <summary>UI 열기 (활성화 + CanvasGroup ON)</summary>
        public virtual void OpenUI()
        {
            gameObject.SetActive(true);
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
            }
        }

        /// <summary>UI 닫기 (비활성화 + CanvasGroup OFF)</summary>
        public virtual void CloseUI()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
            gameObject.SetActive(false);
        }

        /// <summary>UI 보이기 (OpenUI와 동일)</summary>
        public virtual void ShowUI() { OpenUI(); }

        /// <summary>UI 숨기기 (CloseUI와 동일)</summary>
        public virtual void HideUI() { CloseUI(); }
    }
}
