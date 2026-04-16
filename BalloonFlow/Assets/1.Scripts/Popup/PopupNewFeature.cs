using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 신규 기믹 해금 팝업. NewFeature.prefab에 부착.
    /// ImageObject에 기믹 종류별 이미지를 교체하여 표시.
    /// </summary>
    public class PopupNewFeature : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Content]")]
        [SerializeField] private Image _imageObject;
        [SerializeField] private TMP_Text _txtDescription;
        [SerializeField] private TMP_Text _txtDescriptionOutline;

        [Header("[Feature Images — Inspector에서 할당]")]
        [SerializeField] private Sprite _sprLoop;
        [SerializeField] private Sprite _sprPinata;
        [SerializeField] private Sprite _sprHidden;
        [SerializeField] private Sprite _sprIronBox;

        protected override void Awake()
        {
            base.Awake();
            if (_frame != null)
            {
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.AddListener(() => CloseUI());
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.AddListener(() => CloseUI());
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_frame != null)
            {
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.RemoveAllListeners();
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.RemoveAllListeners();
            }
        }

        /// <summary>기믹 이름으로 팝업 표시. featureKey: "Loop", "Pinata", "Hidden", "IronBox"</summary>
        public void Show(string featureKey, string description = null)
        {
            Sprite spr = featureKey switch
            {
                "Loop"    => _sprLoop,
                "Pinata"  => _sprPinata,
                "Hidden"  => _sprHidden,
                "IronBox" => _sprIronBox,
                _         => null
            };
            ShowWithSprite(spr, description ?? $"New feature unlocked: {featureKey}!");
        }

        /// <summary>직접 Sprite 지정하여 팝업 표시.</summary>
        public void ShowWithSprite(Sprite sprite, string description)
        {
            if (_frame != null)
            {
                _frame.SetTitle("New Feature!");
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText("OK");
                _frame.ShowExitButton(true);
            }

            if (_imageObject != null && sprite != null)
            {
                _imageObject.sprite = sprite;
                _imageObject.gameObject.SetActive(true);
            }

            if (_txtDescription != null) _txtDescription.text = description;
            if (_txtDescriptionOutline != null) _txtDescriptionOutline.text = description;

            OpenUI();
        }
    }
}
