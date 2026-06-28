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
        [Tooltip("newFeatureHiddenbox.png 드래그")]
        [SerializeField] private Sprite _sprHiddenBox;
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
        [Tooltip("newFeatureTargetBox.png 드래그 (레벨 161 등장. 미할당 시 _sprPinata 폴백)")]
        [SerializeField] private Sprite _sprTargetBox;

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
                _sprHiddenBox   = rm.UISpriteOr("newFeatureHiddenbox",     _sprHiddenBox);
                _sprHidden      = rm.UISpriteOr("newFeatureHiddenBalloon", _sprHidden);
                _sprIronBox     = rm.UISpriteOr("newFeatureIronBox",       _sprIronBox);
                _sprSpawner     = rm.UISpriteOr("newFeatureSpawner",       _sprSpawner);
                _sprKeyLock     = rm.UISpriteOr("newFeatureKeyLock",       _sprKeyLock);
                _sprFrozenLayer = rm.UISpriteOr("newFeatureFrozenLayer",   _sprFrozenLayer);
                _sprBaricade    = rm.UISpriteOr("newFeatureBaricade",      _sprBaricade);
                _sprFrozenBox   = rm.UISpriteOr("newFeatureFrozenBox",     _sprFrozenBox);
                _sprFlexTube    = rm.UISpriteOr("newFeatureFlexTube",      _sprFlexTube);
                _sprTargetBox   = rm.UISpriteOr("newFeatureTargetBox",     _sprTargetBox);
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
            string textDataFeatureKey = GetTextDataFeatureKey(featureKey);
            Sprite spr = GetFeatureSprite(featureKey, textDataFeatureKey);

            if (spr == null)
            {
                Debug.LogWarning($"[PopupNewFeature] '{featureKey}' Sprite 미할당. " +
                                 "Inspector 에서 newFeature{featureKey}.png 드래그 필요. " +
                                 "(Assets/2.Sprite/UI/ 위치)");
            }

            string displayName = GetDisplayName(textDataFeatureKey);
            string resolvedDescription = description ?? GetDescription(textDataFeatureKey) ?? $"New feature unlocked: {displayName}!";
            ShowWithSprite(spr, displayName, resolvedDescription);
        }

        /// <summary>직접 Sprite 지정하여 팝업 표시.</summary>
        public void ShowWithSprite(Sprite sprite, string itemName, string description)
        {
            if (_frame != null)
            {
                _frame.SetTitle(LocalizationService.Get("newfeature.textunlock"));
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText(LocalizationService.Get("ui.common.ok"));
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

        private Sprite GetFeatureSprite(string featureKey, string textDataFeatureKey)
        {
            string key = string.IsNullOrEmpty(textDataFeatureKey) ? featureKey : textDataFeatureKey;
            return key switch
            {
                "Loop"          => _sprLoop,
                // ROLLBACK_NEWFEATURE_LINKED_DART_BOX_SPRITE_20260625:
                // Linked Dart Box uses the existing newFeatureLoop.png artwork.
                "LinkedDartBox" => _sprLoop,
                "Chain"         => _sprLoop,
                "Wooden Board"  => _sprPinata,
                "TargetBox"     => _sprTargetBox != null ? _sprTargetBox : _sprPinata,
                "HiddenDartBox" => _sprHiddenBox != null ? _sprHiddenBox : _sprHidden,
                "HiddenBalloon" => _sprHidden,
                "IronWall"      => _sprIronBox,
                "GlassPipe"     => _sprSpawner,
                "Pipe"          => _sprSpawner,
                "KeyLock"       => _sprKeyLock,
                "Ice"           => _sprFrozenLayer,
                "FrozenDartBox" => _sprFrozenBox,
                "Barricade"     => _sprBaricade,
                "Baricade"      => _sprBaricade,
                "flextube"      => _sprFlexTube,
                "FlexTube"      => _sprFlexTube,
                _               => null
            };
        }

        private static string GetDisplayName(string textDataFeatureKey)
        {
            string key = $"newfeature.textname.{textDataFeatureKey}";
            return LocalizationService.Has(key) ? LocalizationService.Get(key) : textDataFeatureKey;
        }

        /// <summary>
        /// 기믹별 튜토리얼 본문. 명세에 명시된 키만 채우고, 미명시 키는 null 반환하여
        /// 호출부에서 기존 generic fallback("New feature unlocked: {name}!")으로 폴백되도록 한다.
        /// </summary>
        private static string GetDescription(string textDataFeatureKey)
        {
            string key = $"newfeature.textdesctiption.{textDataFeatureKey}";
            return LocalizationService.Has(key) ? LocalizationService.Get(key) : null;
        }

        private static string GetTextDataFeatureKey(string featureKey)
        {
            // ROLLBACK_NEWFEATURE_TEXTDATA_KEYS_20260624:
            // Accept legacy popup/internal keys, then resolve to Resources/TextData/TextData.csv keys.
            return featureKey switch
            {
                "Hidden"      => "HiddenDartBox",
                "Chain"       => "LinkedDartBox",
                "Pinata"      => "Wooden Board",
                "Pinata_Box"  => "TargetBox",
                "Spawner_T"   => "GlassPipe",
                "Spawner_O"   => "Pipe",
                "Spawner"     => "GlassPipe",
                "Surprise"    => "HiddenBalloon",
                "Wall"        => "IronWall",
                "IronBox"     => "IronWall",
                "FrozenLayer" => "Ice",
                "FrozenBox"   => "FrozenDartBox",
                "Frozen_Dart" => "FrozenDartBox",
                "Baricade"    => "Barricade",
                "FlexTube"    => "flextube",
                _             => featureKey
            };
        }
    }
}
