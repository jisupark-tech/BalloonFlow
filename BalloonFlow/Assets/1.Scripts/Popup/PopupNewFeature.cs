using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// 신규 기믹 해금 팝업. NewFeature.prefab에 부착.
    /// ImageObject에 기믹 종류별 이미지를 교체하여 표시.
    /// </summary>
    public class PopupNewFeature : UIBase
    {
        private const float OkButtonDelaySeconds = 3f;
        private const float OkButtonScaleUpDuration = 0.18f;
        private const float OkButtonScaleDownDuration = 0.12f;
        private const float OkButtonOvershootScale = 1.1f;

        private Coroutine _okDelayCo;
        private Tween _btnOkTween;
        private Tween _btnSingleTween;

        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Content]")]
        [SerializeField] private Image _imageObject;
        [SerializeField] private TMP_Text _txtName;
        [SerializeField] private TMP_Text _txtNameOutline;
        [SerializeField] private TMP_Text _txtDescription;
        [SerializeField] private TMP_Text _txtDescriptionOutline;

        [Header("[Buttons]")]
        [Tooltip("프리팹의 OK 버튼 직접 링크")]
        [SerializeField] private Button _btnOk;

        [Header("[Feature Images — Inspector에서 할당]")]
        [Tooltip("newFeatureLoop.png 드래그")]
        [SerializeField] private Sprite _sprLoop;
        [Tooltip("newFeaturePinata.png 드래그")]
        [SerializeField] private Sprite _sprPinata;
        [Tooltip("newFeatureHiddenBalloon.png 또는 newFeatureHiddenbox.png 드래그")]
        [SerializeField] private Sprite _sprHidden;
        [Tooltip("newFeatureIronBox.png 드래그")]
        [SerializeField] private Sprite _sprIronBox;
        [Tooltip("newFeatureSpawner.png 드래그")]
        [SerializeField] private Sprite _sprSpawner;
        [Tooltip("newFeatureKeyLock.png 드래그")]
        [SerializeField] private Sprite _sprKeyLock;
        [Tooltip("newFeatureFrozenLayer.png 드래그")]
        [SerializeField] private Sprite _sprFrozenLayer;
        [Tooltip("newFeatureBaricade.png 드래그")]
        [SerializeField] private Sprite _sprBaricade;
        [Tooltip("newFeatureFrozenBox.png 드래그")]
        [SerializeField] private Sprite _sprFrozenBox;

        protected override void Awake()
        {
            base.Awake();
            if (_btnOk != null) _btnOk.onClick.AddListener(CloseUI);
            if (_frame != null)
            {
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.AddListener(() => CloseUI());
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.AddListener(() => CloseUI());
            }

            // 기믹 sprite override
            if (ResourceManager.HasInstance)
            {
                var rm = ResourceManager.Instance;
                _sprLoop        = rm.UISpriteOr("newFeatureLoop",          _sprLoop);
                _sprPinata      = rm.UISpriteOr("newFeaturePinata",        _sprPinata);
                _sprHidden      = rm.UISpriteOr("newFeatureHiddenBalloon", _sprHidden);
                _sprIronBox     = rm.UISpriteOr("newFeatureIronBox",       _sprIronBox);
                _sprSpawner     = rm.UISpriteOr("newFeatureSpawner",       _sprSpawner);
                _sprKeyLock     = rm.UISpriteOr("newFeatureKeyLock",       _sprKeyLock);
                _sprFrozenLayer = rm.UISpriteOr("newFeatureFrozenLayer",   _sprFrozenLayer);
                _sprBaricade    = rm.UISpriteOr("newFeatureBaricade",      _sprBaricade);
                _sprFrozenBox   = rm.UISpriteOr("newFeatureFrozenBox",     _sprFrozenBox);
            }
        }

        protected override void OnDestroy()
        {
            if (_okDelayCo != null)
            {
                StopCoroutine(_okDelayCo);
                _okDelayCo = null;
            }
            _btnOkTween?.Kill();
            _btnSingleTween?.Kill();
            base.OnDestroy();
            if (_btnOk != null) _btnOk.onClick.RemoveAllListeners();
            if (_frame != null)
            {
                if (_frame.BtnSingle != null) _frame.BtnSingle.onClick.RemoveAllListeners();
                if (_frame.BtnExit != null) _frame.BtnExit.onClick.RemoveAllListeners();
            }
        }

        /// <summary>
        /// 기믹 이름으로 팝업 표시.
        /// featureKey: "Loop"/"Pinata"/"TargetBox"/"Hidden"/"IronBox"/"Spawner"/"KeyLock"/"FrozenLayer"/"Baricade"/"FrozenBox"
        /// 매핑된 Inspector Sprite 가 null 이면 경고 로그 + 이미지 비활성.
        /// </summary>
        public void Show(string featureKey, string description = null)
        {
            Sprite spr = featureKey switch
            {
                "Loop"        => _sprLoop,
                "Pinata"      => _sprPinata,
                "TargetBox"   => _sprPinata,
                "Hidden"      => _sprHidden,
                "IronBox"     => _sprIronBox,
                "Spawner"     => _sprSpawner,
                "KeyLock"     => _sprKeyLock,
                "FrozenLayer" => _sprFrozenLayer,
                "Baricade"    => _sprBaricade,
                "FrozenBox"   => _sprFrozenBox,
                _             => null
            };

            if (spr == null)
            {
                Debug.LogWarning($"[PopupNewFeature] '{featureKey}' Sprite 미할당. " +
                                 "Inspector 에서 newFeature{featureKey}.png 드래그 필요. " +
                                 "(Assets/2.Sprite/UI/ 위치)");
            }

            string displayName = GetDisplayName(featureKey);
            ShowWithSprite(spr, displayName, description ?? GetDescription(featureKey) ?? $"New feature unlocked: {displayName}!");
        }

        /// <summary>직접 Sprite 지정하여 팝업 표시.</summary>
        public void ShowWithSprite(Sprite sprite, string itemName, string description)
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

            if (_txtName != null) _txtName.text = itemName;
            if (_txtNameOutline != null) _txtNameOutline.text = itemName;

            if (_txtDescription != null) _txtDescription.text = description;
            if (_txtDescriptionOutline != null) _txtDescriptionOutline.text = description;

            _btnOkTween?.Kill();
            _btnSingleTween?.Kill();

            if (_btnOk != null)
            {
                _btnOk.interactable = false;
                _btnOk.transform.localScale = Vector3.zero;
                _btnOk.gameObject.SetActive(false);
            }
            if (_frame != null && _frame.BtnSingle != null)
            {
                _frame.BtnSingle.interactable = false;
                _frame.BtnSingle.transform.localScale = Vector3.zero;
                _frame.BtnSingle.gameObject.SetActive(false);
            }

            OpenUI();

            if (_okDelayCo != null) StopCoroutine(_okDelayCo);
            _okDelayCo = StartCoroutine(EnableOkButtonAfterDelay());
        }

        private IEnumerator EnableOkButtonAfterDelay()
        {
            yield return new WaitForSecondsRealtime(OkButtonDelaySeconds);

            if (_btnOk != null)
            {
                _btnOk.gameObject.SetActive(true);
                _btnOk.transform.localScale = Vector3.zero;

                _btnOkTween?.Kill();
                var btnOk = _btnOk;
                _btnOkTween = DOTween.Sequence()
                    .Append(btnOk.transform.DOScale(Vector3.one * OkButtonOvershootScale, OkButtonScaleUpDuration).SetEase(Ease.OutQuad))
                    .Append(btnOk.transform.DOScale(Vector3.one, OkButtonScaleDownDuration).SetEase(Ease.InQuad))
                    .SetUpdate(true)
                    .OnComplete(() =>
                    {
                        if (btnOk != null) btnOk.interactable = true;
                    });
            }

            if (_frame != null && _frame.BtnSingle != null)
            {
                var btnSingle = _frame.BtnSingle;
                btnSingle.gameObject.SetActive(true);
                btnSingle.transform.localScale = Vector3.zero;

                _btnSingleTween?.Kill();
                _btnSingleTween = DOTween.Sequence()
                    .Append(btnSingle.transform.DOScale(Vector3.one * OkButtonOvershootScale, OkButtonScaleUpDuration).SetEase(Ease.OutQuad))
                    .Append(btnSingle.transform.DOScale(Vector3.one, OkButtonScaleDownDuration).SetEase(Ease.InQuad))
                    .SetUpdate(true)
                    .OnComplete(() =>
                    {
                        if (btnSingle != null) btnSingle.interactable = true;
                    });
            }

            _okDelayCo = null;
        }

        private static string GetDisplayName(string featureKey)
        {
            return featureKey switch
            {
                "Loop"        => "Loop",
                "Pinata"      => "Pinata",
                "TargetBox"   => "Target Box",
                "Hidden"      => "Hidden Balloon",
                "IronBox"     => "Iron Box",
                "Spawner"     => "Spawner",
                "KeyLock"     => "Key & Lock",
                "FrozenLayer" => "Frozen Layer",
                "Baricade"    => "Barricade",
                "FrozenBox"   => "Frozen Box",
                _             => featureKey
            };
        }

        /// <summary>
        /// 기믹별 튜토리얼 본문. 명세에 명시된 키만 채우고, 미명시 키는 null 반환하여
        /// 호출부에서 기존 generic fallback("New feature unlocked: {name}!")으로 폴백되도록 한다.
        /// </summary>
        private static string GetDescription(string featureKey)
        {
            return featureKey switch
            {
                "Hidden" => "Bring them to the front to reveal!",
                "Pinata" => "Shoot the same color to break it!",
                _        => null
            };
        }
    }
}
