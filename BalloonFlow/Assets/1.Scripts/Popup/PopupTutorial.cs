using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 튜토리얼 팝업. Resources/Popup/PopupTutorial 프리팹에서 로드.
    /// 단일 컷아웃 딤 + 하이라이트 프레임 + 화살표 + 설명 텍스트 + 스킵 버튼.
    /// TutorialManager에서 ShowCutout/ShowInstruction으로 제어.
    /// </summary>
    public class PopupTutorial : UIBase
    {
        // [#15] 튜토리얼 모달 — 백버튼 차단 (튜토리얼 완료 강제, UX플로우 §5-3-0). 안 눌림.
        public override BackResult OnBackPressed() => BackResult.Blocked;

        [Header("[Cutout 기준 — 프리팹에서 할당. 자동으로 CutoutMaskUI + Mask + 자식 DimOverlay 추가]")]
        [SerializeField] private RectTransform _cutoutMask;

        [Header("[Cutout Frame — 구멍 테두리]")]
        [SerializeField] private RectTransform _cutoutFrame;

        [Header("[Arrow — 화살표]")]
        [SerializeField] private RectTransform _arrowIndicator;

        [Header("[Hand — 손 아이콘 (step 별 override 위치)]")]
        [SerializeField] private RectTransform _handIndicator;

        [Header("[Instruction — 설명 패널]")]
        [SerializeField] private RectTransform _instructionPanel;
        [SerializeField] private TextMeshProUGUI _instructionText;
        [SerializeField] private Button _skipButton;

        [Header("[Tap Anywhere — 전체 화면 탭]")]
        [SerializeField] private Button _tapAnywhereButton;

        [Header("[Tap Anywhere — 안내 텍스트 (tap_anywhere step 에서만 표시 + 깜빡)]")]
        // [2026-05-15] tap_anywhere 액션 활성 시 TextTap + TextTapOutline 동시 ON/OFF.
        // step.textTapPosition 으로 위치 조절. DOTween yoyo alpha 로 깜빡 애니메이션.
        [SerializeField] private RectTransform _textTap;
        [SerializeField] private RectTransform _textTapOutline;

        // ── Properties ──
        public RectTransform CutoutMask => _cutoutMask;
        public RectTransform CutoutFrame => _cutoutFrame;
        public RectTransform ArrowIndicator => _arrowIndicator;
        public RectTransform HandIndicator => _handIndicator;
        public RectTransform InstructionPanel => _instructionPanel;
        public TextMeshProUGUI InstructionText => _instructionText;
        public Button SkipButton => _skipButton;
        public Button TapAnywhereButton => _tapAnywhereButton;
        public RectTransform TextTap => _textTap;
        public RectTransform TextTapOutline => _textTapOutline;

        protected override void Awake()
        {
            base.Awake();
            EnsureSlicedImages();
        }

        private void EnsureSlicedImages()
        {
            ApplySliced(_cutoutMask);
            ApplySliced(_cutoutFrame);
            ApplySliced(_instructionPanel);
        }

        private static void ApplySliced(RectTransform rt)
        {
            if (rt == null) return;
            Image image = rt.GetComponent<Image>();
            if (image == null || image.sprite == null) return;
            // Sprite border=0 일 때 Sliced 적용 시 깨짐 방어
            if (image.sprite.border == Vector4.zero) return;
            image.type = Image.Type.Sliced;
        }
    }
}
