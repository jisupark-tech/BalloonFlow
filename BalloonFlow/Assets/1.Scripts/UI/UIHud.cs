using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// In-game HUD view. Loaded from Resources/UI/UIHud prefab.
    /// All child references wired via UIPrefabBuilder at editor-time.
    /// HUDController binds to this view for event-driven updates.
    /// </summary>
    public class UIHud : MonoBehaviour
    {
        [Header("Row 1")]
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Text _levelText;
        [SerializeField] private Text _goldText;
        [SerializeField] private Button _goldPlusButton;

        [Header("Row 2")]
        [SerializeField] private Text _scoreText;
        [SerializeField] private Text _remainingText;
        [SerializeField] private Text _holderCountText;

        [Header("Optional")]
        [SerializeField] private Text _moveCountText;

        // Accessors for HUDController binding
        public Button SettingsButton => _settingsButton;
        public Text LevelText => _levelText;
        public Text GoldText => _goldText;
        public Button GoldPlusButton => _goldPlusButton;
        public Text ScoreText => _scoreText;
        public Text RemainingText => _remainingText;
        public Text HolderCountText => _holderCountText;
        public Text MoveCountText => _moveCountText;

        public void SetScore(int score)
        {
            if (_scoreText != null) _scoreText.text = score.ToString("N0");
        }

        public void SetRemainingBalloons(int count)
        {
            if (_remainingText != null) _remainingText.text = $"Balloons: {count}";
        }

        public void SetHolderInfo(int onRail, int max)
        {
            if (_holderCountText != null) _holderCountText.text = $"On Rail: {onRail}/{max}";
        }

        public void SetLevel(int levelId)
        {
            if (_levelText != null) _levelText.text = $"Level {levelId}";
        }

        public void SetGold(int amount)
        {
            if (_goldText != null) _goldText.text = amount.ToString("N0");
        }

        public void SetMoveCount(int used, int total)
        {
            if (_moveCountText != null) _moveCountText.text = $"{used}/{total}";
        }
    }
}
