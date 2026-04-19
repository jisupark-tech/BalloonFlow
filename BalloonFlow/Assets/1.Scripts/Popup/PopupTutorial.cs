using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// 튜토리얼 팝업. Resources/Popup/PopupTutorial 프리팹에서 로드.
    /// 4패널 컷아웃 딤 + 하이라이트 프레임 + 화살표 + 설명 텍스트 + 스킵 버튼.
    /// TutorialManager에서 ShowCutout/ShowInstruction으로 제어.
    /// </summary>
    public class PopupTutorial : UIBase
    {
        [Header("[Dim Panels — 컷아웃 구멍 주변 어둡게]")]
        [SerializeField] private RectTransform _dimTop;
        [SerializeField] private RectTransform _dimBottom;
        [SerializeField] private RectTransform _dimLeft;
        [SerializeField] private RectTransform _dimRight;

        [Header("[Cutout Frame — 구멍 테두리]")]
        [SerializeField] private RectTransform _cutoutFrame;

        [Header("[Arrow — 화살표]")]
        [SerializeField] private RectTransform _arrowIndicator;

        [Header("[Instruction — 설명 패널]")]
        [SerializeField] private RectTransform _instructionPanel;
        [SerializeField] private TextMeshProUGUI _instructionText;
        [SerializeField] private Button _skipButton;

        [Header("[Tap Anywhere — 전체 화면 탭]")]
        [SerializeField] private Button _tapAnywhereButton;

        // ── Properties ──
        public RectTransform DimTop => _dimTop;
        public RectTransform DimBottom => _dimBottom;
        public RectTransform DimLeft => _dimLeft;
        public RectTransform DimRight => _dimRight;
        public RectTransform CutoutFrame => _cutoutFrame;
        public RectTransform ArrowIndicator => _arrowIndicator;
        public RectTransform InstructionPanel => _instructionPanel;
        public TextMeshProUGUI InstructionText => _instructionText;
        public Button SkipButton => _skipButton;
        public Button TapAnywhereButton => _tapAnywhereButton;
    }
}
