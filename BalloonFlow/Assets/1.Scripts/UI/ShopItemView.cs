using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// ShopItem.prefab(보상 표시 단위)에 attach. 인스펙터 와이어 없이 자식의 Image / TMP_Text 자동 검색.
    /// PopupShopListItem 이 instantiate 시점에 AddComponent + Setup 호출 → prefab 수정 불필요.
    /// </summary>
    public class ShopItemView : MonoBehaviour
    {
        [SerializeField] private Image    _icon;
        [SerializeField] private TMP_Text _txtCount;
        [SerializeField] private TMP_Text _txtCountOutline;

        private bool _bound;

        public void Setup(Sprite icon, string countText)
        {
            EnsureBound();

            if (_icon != null)
            {
                if (icon != null)
                {
                    _icon.sprite  = icon;
                    _icon.enabled = true;
                }
                else
                {
                    // 아이콘 sprite 미연결 시 Image 비활성 (UI 깨짐 방지)
                    _icon.enabled = false;
                }
            }

            if (_txtCount != null)         _txtCount.text         = countText ?? "";
            if (_txtCountOutline != null)  _txtCountOutline.text  = countText ?? "";
        }

        private void EnsureBound()
        {
            if (_bound) return;
            _bound = true;

            if (_icon == null)
            {
                // 자식의 첫 Image. ShopItem prefab 자체 Image (예: 배경) 가 root 에 있으면 그게 우선이 될 수 있음 → 깊이 우선 children 으로
                _icon = GetComponentsInChildren<Image>(true).FirstOrDefault(img => img.transform != transform)
                       ?? GetComponentInChildren<Image>(true);
            }

            if (_txtCount == null || _txtCountOutline == null)
            {
                var texts = GetComponentsInChildren<TMP_Text>(true);
                if (texts.Length >= 1 && _txtCount == null)        _txtCount        = texts[0];
                if (texts.Length >= 2 && _txtCountOutline == null) _txtCountOutline = texts[1];
            }
        }
    }
}
