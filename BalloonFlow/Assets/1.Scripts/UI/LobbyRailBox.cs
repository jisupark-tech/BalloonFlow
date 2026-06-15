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
    /// 난이도: Normal=Blue, Hard=Purple, SuperHard: ImageBox=RedBox.png, ImgBoxDim=default.
    /// </summary>
    public class LobbyRailBox : MonoBehaviour
    {
        #region Constants

        private static readonly Color COLOR_ACTIVE = new Color(0xFB / 255f, 0xB0 / 255f, 0x3B / 255f); // #FBB03B
        private static readonly Color COLOR_TEXT_ACTIVE = Color.white;
        private const float LOCKED_SCALE = 0.85f;

        // ImgBoxDim 색상 (alpha 0.7 통일) — Normal=#272D3A / Hard=#925D5D / SuperHard=#2F2223
        private static readonly Color DIM_BLUE   = new Color(0x27 / 255f, 0x2D / 255f, 0x3A / 255f, 0.7f); // Normal
        private static readonly Color DIM_PURPLE = new Color(0x92 / 255f, 0x5D / 255f, 0x5D / 255f, 0.7f); // Hard
        private static readonly Color DIM_RED    = new Color(0x2F / 255f, 0x22 / 255f, 0x23 / 255f, 0.7f); // SuperHard

        // TextLevel 색상 (alpha 100%) — Normal=#779AC4 / Hard=#9677C4 / SuperHard=#C47777
        private static readonly Color TXT_BLUE   = new Color(0x77 / 255f, 0x9A / 255f, 0xC4 / 255f, 1f); // Normal
        private static readonly Color TXT_PURPLE = new Color(0x96 / 255f, 0x77 / 255f, 0xC4 / 255f, 1f); // Hard
        private static readonly Color TXT_RED    = new Color(0xC4 / 255f, 0x77 / 255f, 0x77 / 255f, 1f); // SuperHard

        // TextLevelOutline: alpha 0.45 (색상 변경 없이)
        private const float OUTLINE_INACTIVE_ALPHA = 0.45f;

        // Difficulty-specific dim sprites. Normal keeps the prefab original.
        private const string PURPLE_BOX_SPRITE_PATH = "Sprites/purpleBox";
        private const string RED_BOX_SPRITE_PATH = "Sprites/RedBox";

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

        [Header("[TextLevelOutline Difficulty Material Preset]")]
        [SerializeField] private Material _matLevelOutlineNormal;
        [SerializeField] private Material _matLevelOutlineHard;
        [SerializeField] private Material _matLevelOutlineSuperHard;

        [Header("[Animator]")]
        [SerializeField] private Animator _animator;

        #endregion

        private static readonly int _animIdle = Animator.StringToHash("BoxIdle");
        private static readonly int _animOpen = Animator.StringToHash("BoxOpen");

        #region Fields

        private int _levelId;
        private bool _isActive;
        private DifficultyPurpose _difficulty = DifficultyPurpose.Normal;

        private static Sprite s_purpleBoxSprite;
        private static Sprite s_redBoxSprite;
        private Sprite _originalDimSprite;
        private Sprite _originalBoxSprite;

        #endregion

        #region Properties

        public int LevelId => _levelId;
        public bool IsActive => _isActive;

        #endregion

        #region Public Methods

        private void Awake()
        {
            if (_imgBoxDim != null) _originalDimSprite = _imgBoxDim.sprite;
            if (_imgBox != null) _originalBoxSprite = _imgBox.sprite;
        }

        /// <summary>
        /// Setup with difficulty for inactive color.
        /// </summary>
        public void Setup(int levelId, bool isActive, bool isCompleted, bool isLocked,
                          DifficultyPurpose difficulty = DifficultyPurpose.Normal)
        {
            _levelId = levelId;
            _isActive = isActive;
            _difficulty = difficulty;

            string levelStr = levelId.ToString();
            if (_txtLevel != null) _txtLevel.text = levelStr;
            if (_txtLevelOutline != null) _txtLevelOutline.text = levelStr;

            if (isActive)
            {
                ApplyAnimator(difficulty);
                SetActiveState();
            }
            else
            {
                if (_animator != null) _animator.enabled = false;
                SetInactiveState(isLocked, difficulty);
            }
        }

        #endregion

        #region Private Methods

        private void SetActiveState()
        {
            // Text
            if (_txtLevel != null) _txtLevel.color = COLOR_TEXT_ACTIVE;
            if (_txtLevelOutline != null)
            {
                ApplyLevelOutline(_difficulty, 1f);
            }

            // Effects ON
            if (_rotateLight != null) _rotateLight.gameObject.SetActive(true);
            if (_imgBoxEffect != null) _imgBoxEffect.gameObject.SetActive(true);

            // ImgBoxDim OFF
            if (_imgBoxDim != null) _imgBoxDim.gameObject.SetActive(false);

            if (_imgBox != null) _imgBox.sprite = GetBoxSprite(_difficulty);
            if (_imgBox != null) _imgBox.rectTransform.localScale = Vector3.one * 1.5f;

            // 현재 레벨 박스: DefaultToIdle 애니메이션
            if (_animator != null)
                _animator.SetTrigger(_animIdle);

            PlayOpenAnimation();
        }

        private void SetInactiveState(bool isLocked, DifficultyPurpose difficulty)
        {
            // ImageBox sprite + scale
            if (_imgBox != null) _imgBox.sprite = GetBoxSprite(difficulty);
            if (_imgBox != null) _imgBox.rectTransform.localScale = Vector3.one * 1.5f;

            // ImgBoxDim ON with difficulty color
            if (_imgBoxDim != null)
            {
                _imgBoxDim.gameObject.SetActive(true);

                // Hard/SuperHard use dedicated box sprites. Normal keeps the prefab original.
                // SuperHard RedBox only when locked; Hard purpleBox applies to all dimmed Hard boxes.
                Sprite targetSprite = GetDimBoxSprite(difficulty, isLocked);
                if (targetSprite != null) _imgBoxDim.sprite = targetSprite;

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
                ApplyLevelOutline(difficulty, OUTLINE_INACTIVE_ALPHA);
            }

            // Effects OFF
            if (_rotateLight != null) _rotateLight.gameObject.SetActive(false);
            if (_imgBoxEffect != null) _imgBoxEffect.gameObject.SetActive(false);

            transform.localScale = isLocked ? Vector3.one * LOCKED_SCALE : Vector3.one;
        }

        private static Sprite GetPurpleBoxSprite()
        {
            if (s_purpleBoxSprite == null)
                s_purpleBoxSprite = Resources.Load<Sprite>(PURPLE_BOX_SPRITE_PATH);
            return s_purpleBoxSprite;
        }

        private Sprite GetBoxSprite(DifficultyPurpose difficulty)
        {
            return difficulty == DifficultyPurpose.SuperHard
                ? (GetRedBoxSprite() ?? _originalBoxSprite)
                : _originalBoxSprite;
        }

        private Sprite GetDimBoxSprite(DifficultyPurpose difficulty, bool isLocked)
        {
            switch (difficulty)
            {
                case DifficultyPurpose.SuperHard:
                    return _originalDimSprite;  // ImgBoxDim.png (prefab default) 유지
                case DifficultyPurpose.Hard:
                    return GetPurpleBoxSprite() ?? _originalDimSprite;
                default:
                    return _originalDimSprite;
            }
        }

        private static Sprite GetRedBoxSprite()
        {
            if (s_redBoxSprite == null)
                s_redBoxSprite = Resources.Load<Sprite>(RED_BOX_SPRITE_PATH);
            return s_redBoxSprite;
        }

        private void ApplyLevelOutline(DifficultyPurpose difficulty, float alpha)
        {
            if (_txtLevelOutline == null) return;

            Material mat = UIOutlineStyle.SelectDifficultyMaterial(
                difficulty,
                _matLevelOutlineNormal,
                _matLevelOutlineHard,
                _matLevelOutlineSuperHard);
            UIOutlineStyle.ApplyMaterialOrColor(_txtLevelOutline, mat, UIOutlineStyle.ForDifficulty(difficulty));

            Color color = _txtLevelOutline.color;
            color.a = alpha;
            _txtLevelOutline.color = color;
        }

        private void ApplyAnimator(DifficultyPurpose difficulty)
        {
            if (_animator == null) return;

            string controllerName = difficulty switch
            {
                DifficultyPurpose.SuperHard => "Animator/LobbyRailBoxRed",
                DifficultyPurpose.Hard      => "Animator/LobbyRailBoxPurple",
                _                           => "Animator/LobbyRailBoxBlue"
            };

            var controller = Resources.Load<RuntimeAnimatorController>(controllerName);
            if (controller != null)
                _animator.runtimeAnimatorController = controller;
        }

        /// <summary>게임 시작 시 호출 — 박스 열림 연출, 0.5초 후 콜백.</summary>
        public void PlayStartGameAnimation(System.Action onComplete = null)
        {
            if (_animator != null)
                _animator.SetTrigger(_animOpen);

            if (onComplete != null)
                StartCoroutine(DelayedCallback(0.5f, onComplete));
        }

        private System.Collections.IEnumerator DelayedCallback(float delay, System.Action callback)
        {
            yield return new WaitForSeconds(delay);
            callback?.Invoke();
        }

        private void PlayOpenAnimation()
        {
            if (_imgBox == null) return;
            var rt = _imgBox.rectTransform;
            rt.localScale = Vector3.one * 1.2f;
            rt.DOScale(1.5f, 0.4f).SetEase(Ease.OutBack);
        }

        #endregion
    }
}
