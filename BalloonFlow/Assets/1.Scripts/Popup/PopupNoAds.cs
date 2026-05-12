using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 광고 제거 안내 팝업. PopupCommonFrame 사용.
    /// ExitButton 클릭 시 CloseUI()로 로비 복귀.
    /// IAP 결제 등 비즈니스 로직은 본 태스크 범위 외.
    /// </summary>
    public class PopupNoAds : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        protected override void Awake()
        {
            base.Awake();
            if (_frame != null)
            {
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.AddListener(() => CloseUI());
                // OK 클릭 시 결제 라우팅 대기용 스피너 노출 — 비즈니스 로직은 본 태스크 범위 외.
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.AddListener(() => OpenLoadingSpinner());
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_frame != null)
            {
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.RemoveAllListeners();
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.RemoveAllListeners();
            }
        }

        private void OpenLoadingSpinner()
        {
            if (!UIManager.HasInstance) return;
            UIManager.Instance.OpenUI<PopupLoadingSpinner>(Const.POPUP_LOADING_SPINNER);
        }

        public override void OpenUI()
        {
            if (_frame != null)
            {
                _frame.SetTitle("No Ads");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText("OK");
                _frame.ShowExitButton(true);
            }
            base.OpenUI();
        }
    }
}
