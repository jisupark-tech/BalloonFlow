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
        [SerializeField] private TMP_Text _txtTitleOutline;

        public RectTransform ContentRoot => _contentRoot;

        protected override void Awake()
        {
            base.Awake();
            if (_txtTitle != null) _txtTitle.text = "Shop";
            if (_txtTitleOutline != null) _txtTitleOutline.text = "Shop";
        }
    }
}
