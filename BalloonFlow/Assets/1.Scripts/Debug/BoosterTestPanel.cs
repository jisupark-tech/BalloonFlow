using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 부스터 테스트 패널. InGame 씬에서 부스터 기능 테스트용.
    /// GameBootstrap이 Canvas에 로드. UIHud와 별도.
    ///
    /// 버튼 4개: Select Tool / Shuffle / Color Remove / Hand
    /// Color Remove 사용 시 → 색상 선택 버튼 4개 표시
    /// Select Tool 사용 시 → 보관함 탭으로 선택 (기존 InputManager 활용)
    /// </summary>
    public class BoosterTestPanel : UIBase
    {
        [Header("[부스터 버튼]")]
        [SerializeField] private Button _selectToolButton;
        [SerializeField] private Button _shuffleButton;
        [SerializeField] private Button _colorRemoveButton;
        [SerializeField] private Button _handButton;

        [Header("[색상 선택 버튼 — Color Remove용]")]
        [SerializeField] private GameObject _colorPanel;
        [SerializeField] private Button _color0Button;
        [SerializeField] private Button _color1Button;
        [SerializeField] private Button _color2Button;
        [SerializeField] private Button _color3Button;

        [Header("[부스터 수량 텍스트]")]
        [SerializeField] private Text _selectToolCountText;
        [SerializeField] private Text _shuffleCountText;
        [SerializeField] private Text _colorRemoveCountText;
        [SerializeField] private Text _handCountText;

        public Button SelectToolButton => _selectToolButton;
        public Button ShuffleButton => _shuffleButton;
        public Button ColorRemoveButton => _colorRemoveButton;
        public Button HandButton => _handButton;
        public GameObject ColorPanel => _colorPanel;
        public Button Color0Button => _color0Button;
        public Button Color1Button => _color1Button;
        public Button Color2Button => _color2Button;
        public Button Color3Button => _color3Button;
        public Text SelectToolCountText => _selectToolCountText;
        public Text ShuffleCountText => _shuffleCountText;
        public Text ColorRemoveCountText => _colorRemoveCountText;
        public Text HandCountText => _handCountText;

        private void Start()
        {
            if (_colorPanel != null) _colorPanel.SetActive(false);

            WireButtons();
            RefreshCounts();
        }

        private void OnDestroy()
        {
            if (_selectToolButton != null) _selectToolButton.onClick.RemoveAllListeners();
            if (_shuffleButton != null) _shuffleButton.onClick.RemoveAllListeners();
            if (_colorRemoveButton != null) _colorRemoveButton.onClick.RemoveAllListeners();
            if (_handButton != null) _handButton.onClick.RemoveAllListeners();
            if (_color0Button != null) _color0Button.onClick.RemoveAllListeners();
            if (_color1Button != null) _color1Button.onClick.RemoveAllListeners();
            if (_color2Button != null) _color2Button.onClick.RemoveAllListeners();
            if (_color3Button != null) _color3Button.onClick.RemoveAllListeners();
        }

        private void WireButtons()
        {
            if (_selectToolButton != null) _selectToolButton.onClick.AddListener(OnSelectToolClicked);
            if (_shuffleButton != null) _shuffleButton.onClick.AddListener(OnShuffleClicked);
            if (_colorRemoveButton != null) _colorRemoveButton.onClick.AddListener(OnColorRemoveClicked);
            if (_handButton != null) _handButton.onClick.AddListener(OnHandClicked);
            if (_color0Button != null) _color0Button.onClick.AddListener(() => OnColorPicked(0));
            if (_color1Button != null) _color1Button.onClick.AddListener(() => OnColorPicked(1));
            if (_color2Button != null) _color2Button.onClick.AddListener(() => OnColorPicked(2));
            if (_color3Button != null) _color3Button.onClick.AddListener(() => OnColorPicked(3));
        }

        private void OnSelectToolClicked()
        {
            if (!BoosterManager.HasInstance) return;
            if (BoosterManager.Instance.GetBoosterCount(BoosterManager.SELECT_TOOL) <= 0)
                BoosterManager.Instance.AddBooster(BoosterManager.SELECT_TOOL, 1);
            BoosterManager.Instance.UseBooster(BoosterManager.SELECT_TOOL);
            RefreshCounts();
            Debug.Log("[BoosterTestPanel] Select Tool 사용 — 보관함을 탭하세요.");
        }

        private void OnShuffleClicked()
        {
            if (!BoosterManager.HasInstance) return;
            if (BoosterManager.Instance.GetBoosterCount(BoosterManager.SHUFFLE) <= 0)
                BoosterManager.Instance.AddBooster(BoosterManager.SHUFFLE, 1);
            BoosterManager.Instance.UseBooster(BoosterManager.SHUFFLE);
            RefreshCounts();
        }

        private void OnColorRemoveClicked()
        {
            if (!BoosterManager.HasInstance) return;
            if (BoosterManager.Instance.GetBoosterCount(BoosterManager.COLOR_REMOVE) <= 0)
                BoosterManager.Instance.AddBooster(BoosterManager.COLOR_REMOVE, 1);
            BoosterManager.Instance.UseBooster(BoosterManager.COLOR_REMOVE);
            RefreshCounts();
            if (_colorPanel != null) _colorPanel.SetActive(true);
        }

        private void OnHandClicked()
        {
            if (!BoosterManager.HasInstance) return;
            if (BoosterManager.Instance.GetBoosterCount(BoosterManager.HAND) <= 0)
                BoosterManager.Instance.AddBooster(BoosterManager.HAND, 1);
            BoosterManager.Instance.UseBooster(BoosterManager.HAND);
            RefreshCounts();
        }

        private void OnColorPicked(int color)
        {
            if (BoosterExecutor.HasInstance)
                BoosterExecutor.Instance.OnColorSelected(color);
            if (_colorPanel != null) _colorPanel.SetActive(false);
        }

        private void RefreshCounts()
        {
            if (!BoosterManager.HasInstance) return;

            if (_selectToolCountText != null)
                _selectToolCountText.text = BoosterManager.Instance.GetBoosterCount(BoosterManager.SELECT_TOOL).ToString();
            if (_shuffleCountText != null)
                _shuffleCountText.text = BoosterManager.Instance.GetBoosterCount(BoosterManager.SHUFFLE).ToString();
            if (_colorRemoveCountText != null)
                _colorRemoveCountText.text = BoosterManager.Instance.GetBoosterCount(BoosterManager.COLOR_REMOVE).ToString();
            if (_handCountText != null)
                _handCountText.text = BoosterManager.Instance.GetBoosterCount(BoosterManager.HAND).ToString();
        }
    }
}
