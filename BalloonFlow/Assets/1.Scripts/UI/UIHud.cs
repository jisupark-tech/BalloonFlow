using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 인게임 HUD UI. Resources/UI/UIHud 프리팹에서 로드.
    /// HUDController가 BindView로 참조 연결.
    /// </summary>
    public class UIHud : UIBase
    {
        [Header("[Row 1 — 레벨/골드]")]
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Text _levelText;
        [SerializeField] private Text _goldText;
        [SerializeField] private Button _goldPlusButton;

        [Header("[Row 2 — 홀더]")]
        [SerializeField] private Text _holderCountText;

        [Header("[Optional]")]
        [SerializeField] private Text _moveCountText;

        #region Accessors

        public Button SettingsButton => _settingsButton;
        public Text LevelText => _levelText;
        public Text GoldText => _goldText;
        public Button GoldPlusButton => _goldPlusButton;
        public Text HolderCountText => _holderCountText;
        public Text MoveCountText => _moveCountText;

        #endregion

        #region Set Methods

        public void SetHolderInfo(int _onRail, int _max)
        {
            if (_holderCountText != null) _holderCountText.text = $"On Rail: {_onRail}/{_max}";
        }

        public void SetLevel(int _levelId)
        {
            if (_levelText != null) _levelText.text = $"Level {_levelId}";
        }

        public void SetGold(int _amount)
        {
            if (_goldText != null) _goldText.text = _amount.ToString("N0");
        }

        public void SetMoveCount(int _used, int _total)
        {
            if (_moveCountText != null) _moveCountText.text = $"{_used}/{_total}";
        }

        #endregion
    }
}
