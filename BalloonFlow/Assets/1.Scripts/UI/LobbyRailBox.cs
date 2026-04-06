using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// Individual level box on the lobby rail.
    /// Shows level number only.
    /// Active = highlighted color + effects ON + open animation.
    /// Non-active = RotateLight/ImageBoxEffect OFF, ImageBox/Text blur.
    /// </summary>
    public class LobbyRailBox : MonoBehaviour
    {
        #region Constants

        private static readonly Color COLOR_ACTIVE       = new Color(0xFB / 255f, 0xB0 / 255f, 0x3B / 255f); // #FBB03B
        private static readonly Color COLOR_TEXT_ACTIVE   = Color.white;
        private const float INACTIVE_ELEMENT_ALPHA = 110f / 255f; // 110/255
        private const float LOCKED_SCALE = 0.85f;

        #endregion

        #region Serialized Fields

        [Header("[Box Visuals]")]
        [SerializeField] private Image _imgBox;
        [SerializeField] private Image _imgBoxEffect;
        [SerializeField] private Image _rotateLight;
        [SerializeField] private Image _blurOverlay;

        [Header("[Text]")]
        [SerializeField] private TMP_Text _txtLevel;
        [SerializeField] private TMP_Text _txtLevelOutline;


        #endregion

        #region Fields

        private int _levelId;
        private bool _isActive;
        #endregion

        #region Properties

        public int LevelId => _levelId;
        public bool IsActive => _isActive;

        #endregion

        #region Public Methods

        public void Setup(int levelId, bool isActive, bool isCompleted, bool isLocked)
        {
            _levelId = levelId;
            _isActive = isActive;

            string levelStr = levelId.ToString();
            if (_txtLevel != null) _txtLevel.text = levelStr;
            if (_txtLevelOutline != null) _txtLevelOutline.text = levelStr;

            if (isActive)
                SetActiveState();
            else
                SetInactiveState(isLocked);
        }

        #endregion

        #region Private Methods

        private void SetActiveState()
        {
            // Box color + full alpha
            if (_imgBox != null) _imgBox.color = COLOR_ACTIVE;

            // Text
            if (_txtLevel != null) _txtLevel.color = COLOR_TEXT_ACTIVE;
            if (_txtLevelOutline != null) _txtLevelOutline.color = COLOR_TEXT_ACTIVE;

            // Effects ON
            if (_rotateLight != null) _rotateLight.gameObject.SetActive(true);
            if (_imgBoxEffect != null) _imgBoxEffect.gameObject.SetActive(true);
            if (_blurOverlay != null) _blurOverlay.gameObject.SetActive(false);

            transform.localScale = Vector3.one * 1.2f;
            PlayOpenAnimation();
        }

        private void SetInactiveState(bool isLocked)
        {
            // TextLevel, TextLevelOutline, ImageBox → alpha 185/255
            float a = INACTIVE_ELEMENT_ALPHA;
            if (_imgBox != null)
                _imgBox.color = new Color(_imgBox.color.r, _imgBox.color.g, _imgBox.color.b, a);
            if (_txtLevel != null)
                _txtLevel.alpha = a;
            if (_txtLevelOutline != null)
                _txtLevelOutline.alpha = a;

            // Effects OFF
            if (_rotateLight != null) _rotateLight.gameObject.SetActive(false);
            if (_imgBoxEffect != null) _imgBoxEffect.gameObject.SetActive(false);
            if (_blurOverlay != null) _blurOverlay.gameObject.SetActive(false);

            transform.localScale = isLocked ? Vector3.one * LOCKED_SCALE : Vector3.one;
        }

        private void PlayOpenAnimation()
        {
            if (_imgBox == null) return;

            var rt = _imgBox.rectTransform;
            rt.localScale = Vector3.one * 0.8f;
            rt.DOScale(1f, 0.4f).SetEase(Ease.OutBack);
        }

        #endregion
    }
}
