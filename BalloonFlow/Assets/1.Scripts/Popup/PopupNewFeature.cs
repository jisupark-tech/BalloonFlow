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
        private const float OkButtonDelaySeconds = 2f;
        private const float OkButtonScaleUpDuration = 0.18f;
        private const float OkButtonScaleDownDuration = 0.12f;
        private const float OkButtonOvershootScale = 1.1f;

        private Coroutine _okDelayCo;
        private Tween _okRootTween;

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

        [Header("[OK Button Root — 등장 연출 대상]")]
        [Tooltip("OKButton 루트 GameObject. 텍스트·이미지·프레임 모든 자식이 함께 스케일 등장. 미할당 시 _btnOk.transform.parent 자동 사용")]
        [SerializeField] private Transform _okButtonRoot;

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
        [Tooltip("newFeatureFlexTube.png 드래그 (미할당 시 Awake 의 UISpriteOr 로 자동 로드)")]
        [SerializeField] private Sprite _sprFlexTube;

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
                _sprFlexTube    = rm.UISpriteOr("newFeatureFlexTube",      _sprFlexTube);
            }
        }

        protected override void OnDestroy()
        {
            if (_okDelayCo != null)
            {
                StopCoroutine(_okDelayCo);
                _okDelayCo = null;
            }
            _okRootTween?.Kill();
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
        /// featureKey: "Loop"/"Pinata"/"TargetBox"/"Hidden"/"IronBox"/"Spawner"/"KeyLock"/"FrozenLayer"/"Baricade"/"FrozenBox"/"FlexTube"
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
                "FlexTube"    => _sprFlexTube,
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

            _okRootTween?.Kill();
            var root = ResolveOkButtonRoot();
            if (_btnOk != null) _btnOk.interactable = false;
            if (_frame != null && _frame.BtnSingle != null) _frame.BtnSingle.interactable = false;
            if (root != null)
            {
                root.localScale = Vector3.zero;
                root.gameObject.SetActive(false);
            }

            OpenUI();

            if (_okDelayCo != null) StopCoroutine(_okDelayCo);
            _okDelayCo = StartCoroutine(EnableOkButtonAfterDelay());
        }

        private Transform ResolveOkButtonRoot()
        {
            if (_okButtonRoot != null) return _okButtonRoot;
            if (_btnOk != null && _btnOk.transform.parent != null) return _btnOk.transform.parent;
            if (_btnOk != null) return _btnOk.transform;
            return null;
        }

        private IEnumerator EnableOkButtonAfterDelay()
        {
            yield return new WaitForSecondsRealtime(OkButtonDelaySeconds);

            var root = ResolveOkButtonRoot();
            if (root == null) { _okDelayCo = null; yield break; }

            root.gameObject.SetActive(true);
            root.localScale = Vector3.zero;

            _okRootTween?.Kill();
            var rootRef = root;
            var btnOkRef = _btnOk;
            var btnSingleRef = _frame != null ? _frame.BtnSingle : null;
            _okRootTween = DOTween.Sequence()
                .Append(rootRef.DOScale(Vector3.one * OkButtonOvershootScale, OkButtonScaleUpDuration).SetEase(Ease.OutQuad))
                .Append(rootRef.DOScale(Vector3.one, OkButtonScaleDownDuration).SetEase(Ease.InQuad))
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    if (btnOkRef != null) btnOkRef.interactable = true;
                    if (btnSingleRef != null) btnSingleRef.interactable = true;
                });

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
                "FlexTube"    => "Flex Tube",
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
