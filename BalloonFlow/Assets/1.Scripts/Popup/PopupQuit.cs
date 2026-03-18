using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 나가기 확인 팝업. Resources/Popup/PopupQuit 프리팹에서 로드.
    /// InGame에서 나가기 버튼 클릭 시 표시.
    /// HomeButton = Lobby로 이동, NextButton = 팝업 닫고 게임 계속.
    /// </summary>
    public class PopupQuit : UIBase
    {
        [Header("[버튼]")]
        [SerializeField] private Button _homeButton;
        [SerializeField] private Button _nextButton;

        public Button HomeButton => _homeButton;
        public Button NextButton => _nextButton;
    }
}
