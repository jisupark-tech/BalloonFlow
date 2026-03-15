using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Complete configuration for a single level.
    /// Holds all data required to set up balloons, holders, rail, and scoring.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Config | Phase: 1
    /// DB Reference: No DB match — generated from L3 YAML logicFlow
    /// </remarks>
    [System.Serializable]
    public class LevelConfig
    {
        /// <summary>Unique level identifier (1–100).</summary>
        public int levelId;

        /// <summary>Package this level belongs to (1–5).</summary>
        public int packageId;

        /// <summary>Position within the package (1–20).</summary>
        public int positionInPackage;

        /// <summary>Number of distinct balloon/dart colors used (2–8).</summary>
        public int numColors;

        /// <summary>Total balloons placed on the board (6–65).</summary>
        public int balloonCount;

        /// <summary>
        /// Describes the pacing role of this level.
        /// Valid values: tutorial | normal | hard | super_hard | rest
        /// </summary>
        public string difficultyPurpose;

        /// <summary>Active gimmick type names for this level (e.g. "hidden", "chain").</summary>
        public string[] gimmickTypes;

        /// <summary>Holder configuration array.</summary>
        public HolderSetup[] holders;

        /// <summary>Balloon layout array.</summary>
        public BalloonLayout[] balloons;

        /// <summary>Rail waypoint and holder-position layout.</summary>
        public RailLayout rail;

        /// <summary>Score required for 1 star (= balloonCount * 100).</summary>
        public int star1Threshold;

        /// <summary>Score required for 2 stars (= ceil(star1 * 1.5)).</summary>
        public int star2Threshold;

        /// <summary>Score required for 3 stars (= ceil(star1 * 2.2)).</summary>
        public int star3Threshold;
    }

    /// <summary>
    /// Describes one holder placed on the board for a given level.
    /// </summary>
    [System.Serializable]
    public class HolderSetup
    {
        /// <summary>Unique holder identifier within the level.</summary>
        public int holderId;

        /// <summary>Color index of the darts in this holder's magazine.</summary>
        public int color;

        /// <summary>Number of darts loaded in the magazine.</summary>
        public int magazineCount;

        /// <summary>2D grid position of the holder on the board.</summary>
        public Vector2 position;
    }

    /// <summary>
    /// Describes one balloon placed on the board for a given level.
    /// </summary>
    [System.Serializable]
    public class BalloonLayout
    {
        /// <summary>Unique balloon identifier within the level.</summary>
        public int balloonId;

        /// <summary>Color index of this balloon.</summary>
        public int color;

        /// <summary>2D grid position on the board.</summary>
        public Vector2 gridPosition;

        /// <summary>Optional gimmick type applied to this balloon (empty string = none).</summary>
        public string gimmickType;
    }

    /// <summary>
    /// Describes the circular rail layout for a given level.
    /// </summary>
    [System.Serializable]
    public class RailLayout
    {
        /// <summary>Ordered waypoints that define the rail path in world space.</summary>
        public Vector3[] waypoints;

        /// <summary>World-space positions where holders wait on the rail.</summary>
        public Vector3[] holderPositions;

        /// <summary>
        /// Visual style of the conveyor belt.
        /// 0 = Cylinder (default), 1 = Flat2D (quad strip), 2 = Custom3D (user prefab).
        /// </summary>
        public int visualType;

        /// <summary>Maximum holders allowed on the rail simultaneously.</summary>
        public int maxOnRail = 2;
    }
}
