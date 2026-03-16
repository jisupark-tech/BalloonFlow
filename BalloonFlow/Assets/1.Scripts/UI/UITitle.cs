using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// Title page view. Loaded from Resources/UI/UITitle prefab.
    /// All child references wired via UIPrefabBuilder at editor-time.
    /// </summary>
    public class UITitle : MonoBehaviour
    {
        [SerializeField] private Text _logoText;
        [SerializeField] private Text _subtitleText;
        [SerializeField] private Text _tapToStartText;

        public Text LogoText => _logoText;
        public Text SubtitleText => _subtitleText;
        public Text TapToStartText => _tapToStartText;
    }
}
