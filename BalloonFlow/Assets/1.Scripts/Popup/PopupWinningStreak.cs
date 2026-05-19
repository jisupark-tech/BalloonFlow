using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Winning Streak 안내 팝업. PopupCommonFrame 사용.
    /// 컨텐츠/디자인은 prefab 단의 SerializeField 로 들어옴.
    /// </summary>
    public class PopupWinningStreak : UIBase
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
                _frame.SetTitle("Winning Streak");
                _frame.ShowExitButton(true);
            }
            base.OpenUI();
        }
    }
}
