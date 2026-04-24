using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// 모든 UI 패널의 베이스 클래스.
    /// OpenUI/CloseUI/ShowUI/HideUI 라이프사이클 제공.
    /// CanvasGroup이 있으면 alpha/interactable/blocksRaycasts 자동 제어.
    /// DOTween 애니메이션 연출 옵션 내장 (PopupAni에서 이식).
    /// </summary>
    public class UIBase : MonoBehaviour
    {
        protected CanvasGroup _canvasGroup;

        [Header("[애니메이션 연출]")]
        [SerializeField] private bool _useAnimation = false;
        [SerializeField] private AnimationType _animationType = AnimationType.ScalePopup;
        [SerializeField] private float _animDuration = 0.35f;
        [SerializeField] private Ease _animEase = Ease.OutBack;

        [Header("[딤 (어두운 배경) — 선택]")]
        [SerializeField] private CanvasGroup _dim;

        [Header("[팝업 윈도우 — Scale 애니 대상]")]
        [SerializeField] private RectTransform _popupWindow;

        private Sequence _currentSequence;

        public enum AnimationType
        {
            None,
            ScalePopup,     // 스케일 0→1 (OutBack)
            FadeIn,         // 알파 0→1
            SlideFromBottom,// 아래에서 올라옴
            SlideFromTop,   // 위에서 내려옴
        }

        protected virtual void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            UIParticleBinder.Bind(gameObject);
        }

        /// <summary>초기화. 필요한 데이터 전달 시 사용.</summary>
        public virtual void Init(object[] _data) { }

        /// <summary>UI 열기 (활성화 + CanvasGroup ON + 애니메이션). 이미 열려있으면 중복 실행 안 함.</summary>
        public virtual void OpenUI()
        {
            // 이미 활성 + 보이는 상태면 중복 실행 방지
            if (gameObject.activeSelf && _canvasGroup != null && _canvasGroup.alpha > 0.99f && _canvasGroup.interactable)
                return;

            gameObject.SetActive(true);

            if (_useAnimation && _animationType != AnimationType.None)
            {
                PlayOpenAnimation();
            }
            else
            {
                if (_canvasGroup != null)
                {
                    _canvasGroup.alpha = 1f;
                    _canvasGroup.interactable = true;
                    _canvasGroup.blocksRaycasts = true;
                }
            }
        }

        /// <summary>UI 닫기 (CanvasGroup으로 숨김. SetActive 토글 없이 Canvas 리빌드 최소화)</summary>
        public virtual void CloseUI()
        {
            KillAnimation();

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        /// <summary>UI 보이기 (OpenUI와 동일)</summary>
        public virtual void ShowUI() { OpenUI(); }

        /// <summary>UI 숨기기 (CloseUI와 동일)</summary>
        public virtual void HideUI() { CloseUI(); }

        #region Animation

        /// <summary>열기 애니메이션 재생. _useAnimation이 true일 때 OpenUI에서 자동 호출.</summary>
        protected void PlayOpenAnimation()
        {
            KillAnimation();

            // 초기 상태
            if (_canvasGroup != null)
            {
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = true;
            }

            _currentSequence = DOTween.Sequence();

            // 딤 페이드
            if (_dim != null)
            {
                _dim.alpha = 0f;
                _currentSequence.Append(_dim.DOFade(1f, _animDuration * 0.6f));
            }

            // 애니메이션 타입별 연출
            switch (_animationType)
            {
                case AnimationType.ScalePopup:
                    if (_popupWindow != null)
                    {
                        _popupWindow.localScale = Vector3.zero;
                        _currentSequence.Join(
                            _popupWindow.DOScale(1f, _animDuration).SetEase(_animEase));
                    }
                    if (_canvasGroup != null)
                    {
                        _canvasGroup.alpha = 1f;
                    }
                    break;

                case AnimationType.FadeIn:
                    if (_canvasGroup != null)
                    {
                        _canvasGroup.alpha = 0f;
                        _currentSequence.Join(
                            _canvasGroup.DOFade(1f, _animDuration).SetEase(Ease.OutQuad));
                    }
                    break;

                case AnimationType.SlideFromBottom:
                    if (_popupWindow != null)
                    {
                        Vector2 startPos = _popupWindow.anchoredPosition;
                        _popupWindow.anchoredPosition = new Vector2(startPos.x, -Screen.height);
                        _currentSequence.Join(
                            _popupWindow.DOAnchorPosY(startPos.y, _animDuration).SetEase(Ease.OutQuad));
                    }
                    if (_canvasGroup != null) _canvasGroup.alpha = 1f;
                    break;

                case AnimationType.SlideFromTop:
                    if (_popupWindow != null)
                    {
                        Vector2 startPos = _popupWindow.anchoredPosition;
                        _popupWindow.anchoredPosition = new Vector2(startPos.x, Screen.height);
                        _currentSequence.Join(
                            _popupWindow.DOAnchorPosY(startPos.y, _animDuration).SetEase(Ease.OutQuad));
                    }
                    if (_canvasGroup != null) _canvasGroup.alpha = 1f;
                    break;
            }

            _currentSequence.SetUpdate(true); // timeScale=0에서도 동작
            _currentSequence.OnComplete(() =>
            {
                if (_canvasGroup != null)
                {
                    _canvasGroup.alpha = 1f;
                    _canvasGroup.interactable = true;
                    _canvasGroup.blocksRaycasts = true;
                }
            });
        }

        private void KillAnimation()
        {
            if (_currentSequence != null && _currentSequence.IsActive())
            {
                _currentSequence.Kill();
                _currentSequence = null;
            }
        }

        protected virtual void OnDestroy()
        {
            KillAnimation();
        }

        #endregion
    }
}
