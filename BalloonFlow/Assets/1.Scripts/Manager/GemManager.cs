using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Manages gem (premium) currency — used for continues and premium boosters.
    /// Aligned with design: continue costs = 30 + (n-1) * 10 gems.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Manager | Phase: 3
    /// Design ref: economy.yaml §gem_system, outgame_life_booster.yaml §continue
    /// </remarks>
    public class GemManager : Singleton<GemManager>
    {
        #region Constants

        private const string PREFS_KEY_GEMS = "BalloonFlow_Gems";
        private const int DEFAULT_INITIAL_GEMS = 50;

        #endregion

        #region Types

        public enum GemSource { IAP, Reward, DailyReward, ChapterBonus, Other }
        public enum GemSink { Continue, PremiumBooster, Other }

        #endregion

        #region Fields

        [SerializeField] private int _initialGems = DEFAULT_INITIAL_GEMS;
        private int _currentGems;

        #endregion

        #region Properties

        public int Gems => _currentGems;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            LoadGems();
        }

        #endregion

        #region Public Methods

        public int GetGems() => _currentGems;

        public void AddGems(int amount, GemSource source)
        {
            if (amount <= 0)
            {
                Debug.LogWarning($"[GemManager] AddGems called with non-positive amount: {amount}");
                return;
            }

            _currentGems += amount;
            SaveGems();

            Debug.Log($"[GemManager] +{amount} gems from {source}. Total: {_currentGems}");
            EventBus.Publish(new OnGemChanged { currentGems = _currentGems, delta = amount });
        }

        public bool SpendGems(int amount, GemSink sink)
        {
            if (amount <= 0)
            {
                Debug.LogWarning($"[GemManager] SpendGems called with non-positive amount: {amount}");
                return false;
            }

            if (_currentGems < amount)
            {
                Debug.Log($"[GemManager] Insufficient gems: have {_currentGems}, need {amount} for {sink}");
                return false;
            }

            _currentGems -= amount;
            SaveGems();

            Debug.Log($"[GemManager] -{amount} gems for {sink}. Remaining: {_currentGems}");
            EventBus.Publish(new OnGemChanged { currentGems = _currentGems, delta = -amount });
            return true;
        }

        public bool HasEnoughGems(int amount)
        {
            return _currentGems >= amount;
        }

        public void ResetToInitial()
        {
            _currentGems = _initialGems;
            SaveGems();
            EventBus.Publish(new OnGemChanged { currentGems = _currentGems, delta = 0 });
        }

        #endregion

        #region Private Methods

        private void LoadGems()
        {
            if (PlayerPrefs.HasKey(PREFS_KEY_GEMS))
            {
                _currentGems = PlayerPrefs.GetInt(PREFS_KEY_GEMS, _initialGems);
            }
            else
            {
                _currentGems = _initialGems;
                SaveGems();
            }
        }

        private void SaveGems()
        {
            PlayerPrefs.SetInt(PREFS_KEY_GEMS, _currentGems);
            PlayerPrefs.Save();
        }

        #endregion
    }
}
