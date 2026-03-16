using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 타이틀 UI. Resources/UI/UITitle 프리팹에서 로드.
    /// </summary>
    public class UITitle : UIBase
    {
        [Header("[Title 텍스트]")]
        [SerializeField] private Text _logoText;
        [SerializeField] private Text _subtitleText;
        [SerializeField] private Text _tapToStartText;

        public Text LogoText => _logoText;
        public Text SubtitleText => _subtitleText;
        public Text TapToStartText => _tapToStartText;
    }
}
