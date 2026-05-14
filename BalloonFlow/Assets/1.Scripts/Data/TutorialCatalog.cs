using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Catalog of TutorialConfig — primary runtime source for tutorial steps.
    /// TutorialController loads this first; falls back to LevelDatabase.tutorialSteps
    /// (per-level override) and finally the legacy hardcoded BuildTutorialConfigs().
    /// Editable via Unity Inspector OR the BalloonFlow > Tutorial Editor window.
    /// </summary>
    [CreateAssetMenu(fileName = "TutorialCatalog", menuName = "BalloonFlow/Tutorial Catalog", order = 100)]
    public class TutorialCatalog : ScriptableObject
    {
        public const string RESOURCES_PATH = "TutorialCatalog";

        [SerializeField] private List<TutorialConfig> _tutorials = new List<TutorialConfig>();

        public IReadOnlyList<TutorialConfig> Tutorials => _tutorials;

        public List<TutorialConfig> GetMutableList() => _tutorials;
    }
}
