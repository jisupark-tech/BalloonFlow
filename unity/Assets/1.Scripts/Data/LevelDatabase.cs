using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// ScriptableObject container for all level configurations.
    /// Create one instance via Assets > Create > BalloonFlow > LevelDatabase
    /// and populate the levels array in the Inspector.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Config | Phase: 1
    /// DB Reference: No DB match — generated from L3 YAML logicFlow
    /// </remarks>
    [CreateAssetMenu(fileName = "LevelDatabase", menuName = "BalloonFlow/LevelDatabase")]
    public class LevelDatabase : ScriptableObject
    {
        /// <summary>All level configurations, indexed 0-based (levels[0] = level 1).</summary>
        public LevelConfig[] levels;
    }
}
