using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 레벨 난이도 유형. BeatChart 포지션 규칙에 따라 결정.
    /// </summary>
    public enum DifficultyPurpose
    {
        Tutorial,
        Normal,
        Hard,
        SuperHard,
        Rest,
        Intro
    }

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
        /// <summary>Unique level identifier (1–300).</summary>
        public int levelId;

        /// <summary>Package this level belongs to (1–15, 20 levels each).</summary>
        public int packageId;

        /// <summary>Position within the package (1–20).</summary>
        public int positionInPackage;

        /// <summary>
        /// Explicit rail capacity override.
        /// 0 = auto-calculate from total dart count (default).
        /// Design: darts≤30→50, ≤60→100, ≤100→150, else→200.
        /// </summary>
        public int railCapacity;

        /// <summary>Number of distinct balloon/dart colors used (2–11).</summary>
        public int numColors;

        /// <summary>Total balloons placed on the board (6–200).</summary>
        public int balloonCount;

        /// <summary>Scale multiplier for balloon visuals (0.2–1.0, default 0.5).</summary>
        public float balloonScale = 0.5f;

        /// <summary>Queue width — number of columns in the holder queue (2–5, Hard Rule).</summary>
        public int queueColumns;

        /// <summary>Target clear rate for this level (0.12–0.95).</summary>
        public float targetClearRate;

        /// <summary>
        /// Describes the pacing role of this level.
        /// </summary>
        public DifficultyPurpose difficultyPurpose;

        /// <summary>Active gimmick type names for this level (e.g. "hidden", "chain").</summary>
        public string[] gimmickTypes;

        /// <summary>Holder configuration array.</summary>
        public HolderSetup[] holders;

        /// <summary>Balloon layout array.</summary>
        public BalloonLayout[] balloons;

        /// <summary>Rail waypoint and holder-position layout.</summary>
        public RailLayout rail;

        /// <summary>
        /// Grid positions where conveyor belt tiles are placed (tilemap coordinates).
        /// Empty or null means no conveyor tiles for this level.
        /// </summary>
        public Vector2Int[] conveyorPositions;

        /// <summary>
        /// Number of balloon grid columns used for tilemap sizing.
        /// Set by MapMaker or LevelGenerator.
        /// </summary>
        public int gridCols;

        /// <summary>
        /// Number of balloon grid rows used for tilemap sizing.
        /// Set by MapMaker or LevelGenerator.
        /// </summary>
        public int gridRows;

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

        /// <summary>큐 기믹 타입 (빈 문자열 = 없음). "Hidden", "Chain", "Spawner_T", "Spawner_O", "Frozen_Dart"</summary>
        public string queueGimmick = "";

        /// <summary>Chain 그룹 ID (-1 = Chain 아님). 같은 groupId의 보관함들이 연결됨.</summary>
        public int chainGroupId = -1;
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

        /// <summary>Piñata 가로 크기 (1=기본, 2~6=멀티셀).</summary>
        public int sizeW = 1;
        /// <summary>Piñata 세로 크기.</summary>
        public int sizeH = 1;
        /// <summary>Piñata HP (기본 2).</summary>
        public int hp = 0;
    }

    /// <summary>
    /// Describes the circular rail (conveyor belt) layout for a given level.
    /// Rail Overflow mode: darts are placed on discrete slots and rotate with the belt.
    /// </summary>
    [System.Serializable]
    public class RailLayout
    {
        /// <summary>Ordered waypoints that define the rail path in world space.</summary>
        public Vector3[] waypoints;

        /// <summary>
        /// Total number of discrete slots on the rail conveyor belt.
        /// 0 = use LevelConfig.railCapacity or auto-calculate from total darts.
        /// Design: 50/100/150/200 variable capacity based on total dart count.
        /// </summary>
        public int slotCount;

        /// <summary>
        /// Visual style of the conveyor belt.
        /// 0 = Cylinder (default), 1 = Flat2D (quad strip), 2 = Custom3D (user prefab).
        /// </summary>
        public int visualType;

        /// <summary>
        /// World-space positions where queue columns align to the rail bottom edge.
        /// Index = column index (0..queueColumns-1). Holders move up from queue to this point.
        /// </summary>
        public Vector3[] deployPoints;

        /// <summary>
        /// When true, darts follow smooth curves at corners instead of sharp 90-degree turns.
        /// </summary>
        public bool smoothCorners;

        /// <summary>
        /// Radius of the rounded corner in world units (0.5 ~ 3.0).
        /// Only used when smoothCorners is true. Default 1.0.
        /// </summary>
        public float cornerRadius = 1f;
    }
}
