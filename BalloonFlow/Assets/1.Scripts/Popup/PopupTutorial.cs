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

        // ── Properties ──
        public RectTransform CutoutMask => _cutoutMask;
        public RectTransform CutoutFrame => _cutoutFrame;
        public RectTransform ArrowIndicator => _arrowIndicator;
        public RectTransform HandIndicator => _handIndicator;
        public RectTransform InstructionPanel => _instructionPanel;
        public TextMeshProUGUI InstructionText => _instructionText;
        public Button SkipButton => _skipButton;
        public Button TapAnywhereButton => _tapAnywhereButton;
    }
}
