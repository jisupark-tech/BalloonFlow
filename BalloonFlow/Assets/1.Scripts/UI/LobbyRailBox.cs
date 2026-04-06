using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// Individual level box on the lobby rail.
    /// Active = highlighted + effects ON + open animation.
    /// Inactive = ImgBoxDim ON (난이도별 색상), text color by difficulty.
    /// 난이도: Normal=Blue, Hard=Purple, SuperHard=Red.
    /// </summary>
    public class LobbyRailBox : MonoBehaviour
    {
        #region Constants

        private static readonly Color COLOR_ACTIVE = new Color(0xFB / 255f, 0xB0 / 255f, 0x3B / 255f); // #FBB03B
        private static readonly Color COLOR_TEXT_ACTIVE = Color.white;
        private const float LOCKED_SCALE = 0.85f;

        // ImgBoxDim 색상 (alpha 0.7 통일)
        private static readonly Color DIM_BLUE   = new Color(0x27 / 255f, 0x2D / 255f, 0x3A / 255f, 0.7f); // Normal
        private static readonly Color DIM_PURPLE = new Color(0x92 / 255f, 0x5D / 255f, 0x5D / 255f, 0.7f); // Hard
        private static readonly Color DIM_RED    = new Color(0x2F / 255f, 0x22 / 255f, 0x23 / 255f, 0.7f); // SuperHard

        // TextLevel 색상 (alpha 100%)
        private static readonly Color TXT_BLUE   = new Color(0x77 / 255f, 0x9A / 255f, 0xC4 / 255f, 1f); // Normal
        private static readonly Color TXT_PURPLE = new Color(0x96 / 255f, 0x77 / 255f, 0xC4 / 255f, 1f); // Hard
        private static readonly Color TXT_RED    = new Color(0xC4 / 255f, 0x77 / 255f, 0x77 / 255f, 1f); // SuperHard

        // TextLevelOutline: alpha 0.45 (색상 변경 없이)
        private const float OUTLINE_INACTIVE_ALPHA = 0.45f;

        #endregion

        #region Serialized Fields

        [Header("[Box Visuals]")]
        [SerializeField] private Image _imgBox;
        [SerializeField] private Image _imgBoxEffect;
        [SerializeField] private Image _rotateLight;
        [SerializeField] private Image _imgBoxDim;

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

        /// <summary>
        /// Setup with difficulty for inactive color.
        /// </summary>
        public void Setup(int levelId, bool isActive, bool isCompleted, bool isLocked,
                          DifficultyPurpose difficulty = DifficultyPurpose.Normal)
        {
            _levelId = levelId;
            _isActive = isActive;

            string levelStr = levelId.ToString();
            if (_txtLevel != null) _txtLevel.text = levelStr;
            if (_txtLevelOutline != null) _txtLevelOutline.text = levelStr;

            if (isActive)
                SetActiveState();
            else
                SetInactiveState(isLocked, difficulty);
        }

        #endregion

        #region Private Methods

        private void SetActiveState()
        {
            // Text
            if (_txtLevel != null) _txtLevel.color = COLOR_TEXT_ACTIVE;
            if (_txtLevelOutline != null)
            {
                _txtLevelOutline.color = new Color(
                    _txtLevelOutline.color.r, _txtLevelOutline.color.g,
                    _txtLevelOutline.color.b, 1f);
            }

            // Effects ON
            if (_rotateLight != null) _rotateLight.gameObject.SetActive(true);
            if (_imgBoxEffect != null) _imgBoxEffect.gameObject.SetActive(true);

            // ImgBoxDim OFF
            if (_imgBoxDim != null) _imgBoxDim.gameObject.SetActive(false);

            transform.localScale = Vector3.one * 1.2f;
            PlayOpenAnimation();
        }

        private void SetInactiveState(bool isLocked, DifficultyPurpose difficulty)
        {
            // ImgBoxDim ON with difficulty color
            if (_imgBoxDim != null)
            {
                _imgBoxDim.gameObject.SetActive(true);
                _imgBoxDim.color = difficulty switch
                {
                    DifficultyPurpose.SuperHard => DIM_RED,
                    DifficultyPurpose.Hard      => DIM_PURPLE,
                    _                           => DIM_BLUE
                };
            }

            // TextLevel: difficulty color, alpha 100%
            if (_txtLevel != null)
            {
                _txtLevel.color = difficulty switch
                {
                    DifficultyPurpose.SuperHard => TXT_RED,
                    DifficultyPurpose.Hard      => TXT_PURPLE,
                    _                           => TXT_BLUE
                };
            }

            // TextLevelOutline: keep color, alpha 0.45
            if (_txtLevelOutline != null)
            {
                _txtLevelOutline.color = new Color(
                    _txtLevelOutline.color.r, _txtLevelOutline.color.g,
                    _txtLevelOutline.color.b, OUTLINE_INACTIVE_ALPHA);
            }

            // Effects OFF
            if (_rotateLight != null) _rotateLight.gameObject.SetActive(false);
            if (_imgBoxEffect != null) _imgBoxEffect.gameObject.SetActive(false);

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
