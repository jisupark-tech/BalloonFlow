using UnityEngine;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// Shop page — spawned inside UILobby PageContainer (left page).
    /// Manages shop item display. Content populated by LobbyController.
    /// </summary>
    public class UIShop : UIBase
    {
        [Header("[Shop Content]")]
        [SerializeField] private RectTransform _contentRoot;
        [SerializeField] private TMP_Text _txtTitle;

        public RectTransform ContentRoot => _contentRoot;
    }
}
