using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// 결제/네트워크 대기용 로딩 스피너 팝업. DIM 터치로 즉시 닫힘.
    /// Prefab 에셋(Resources/Popup/PopupLoadingSpinner.prefab) 별도 제작 필요.
    /// DIM 또는 외부 트리거로 닫힐 때 SetCloseCallback으로 등록된 콜백을 1회 실행한다.
    /// </summary>
    public class PopupLoadingSpinner : UIBase
    {
        [Header("[Overlay (DIM)]")]
        [SerializeField] private Button _btnDim;

        [Header("[Spinner]")]
        [SerializeField] private RectTransform _spinnerIcon;

        [SerializeField] private float _rotateDuration = 1f;

        private Tween _rotateTween;

        /// <summary>CloseUI 시 1회만 발화하고 자동 클리어되는 콜백.</summary>
        private System.Action _onCloseOnce;

        /// <summary>닫힘 시 호출할 콜백 등록. 마지막 호출이 이전 콜백을 덮어쓴다(1회 발화 후 자동 클리어).</summary>
        public void SetCloseCallback(System.Action callback)
        {
            _onCloseOnce = callback;
        }

        protected override void Awake()
        {
            base.Awake();
            if (_btnDim != null)
                _btnDim.onClick.AddListener(() => CloseUI());
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_btnDim != null)
                _btnDim.onClick.RemoveAllListeners();
            KillRotateTween();
        }

        public override void OpenUI()
        {
            base.OpenUI();
            // PopupTr 내에서 항상 최상단 — 다른 popup 위에 오버레이로 깔리도록 보장.
            transform.SetAsLastSibling();
            StartRotateTween();
        }

        public override void CloseUI()
        {
            KillRotateTween();
            base.CloseUI();

            // 콜백 내부에서 재진입(Open/Close) 호출 시 무한루프 회피 위해 null 처리 먼저 → 호출.
            var cb = _onCloseOnce;
            _onCloseOnce = null;
            cb?.Invoke();
        }

        private void StartRotateTween()
        {
            if (_spinnerIcon == null) return;
            KillRotateTween();
            _spinnerIcon.localEulerAngles = Vector3.zero;
            _rotateTween = _spinnerIcon
                .DOLocalRotate(new Vector3(0f, 0f, -360f), _rotateDuration, RotateMode.FastBeyond360)
                .SetEase(Ease.Linear)
                .SetLoops(-1, LoopType.Restart)
                .SetLink(gameObject);
        }

        private void KillRotateTween()
        {
            if (_rotateTween != null && _rotateTween.IsActive())
                _rotateTween.Kill();
            _rotateTween = null;
        }
    }
}
