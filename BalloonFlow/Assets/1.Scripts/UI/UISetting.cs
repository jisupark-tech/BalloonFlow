using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// Setting page — spawned inside UILobby PageContainer (right page).
    /// Contains settings controls. Content populated by LobbyController.
    /// </summary>
    public class UISetting : UIBase
    {
        [Header("[Setting Content]")]
        [SerializeField] private RectTransform _contentRoot;
        [SerializeField] private TMP_Text _txtTitle;

        [Header("[Controls]")]
        [SerializeField] private Toggle _toggleBGM;
        [SerializeField] private Toggle _toggleSFX;
        [SerializeField] private Toggle _toggleVibration;

        public RectTransform ContentRoot => _contentRoot;
        public Toggle ToggleBGM => _toggleBGM;
        public Toggle ToggleSFX => _toggleSFX;
        public Toggle ToggleVibration => _toggleVibration;
    }
}
