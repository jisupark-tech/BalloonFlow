using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Winning Streak 'Info' 팝업. PopupWinningStreak 에서 BtnInfo 클릭 시 진입.
    /// PopupCommonFrame 의 BtnExit 만 listener 등록.
    /// _frame 이 prefab 단에서 미할당이면 silent skip — 런타임 NPE 회피 (BtnInfo 미배치 시나리오 동일).
    /// 컨텐츠/디자인은 prefab 단의 SerializeField 로 들어옴.
    /// </summary>
    public class PopupWinningStreakInfo : UIBase
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
                _frame.SetTitle("Winning Streak Info");
                _frame.ShowExitButton(true);
            }
            base.OpenUI();
        }
    }
}
