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
            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.AddListener(() => CloseUI());
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.RemoveAllListeners();
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
