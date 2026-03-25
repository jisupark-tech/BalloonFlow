using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// All game event struct definitions for the EventBus system.
    /// Each struct represents a discrete game event with relevant data.
    /// </summary>
    /// <remarks>
    /// Layer: Core | Genre: Puzzle | Role: Config | Phase: 0
    /// DB Reference: No DB match found — generated from L3 YAML contracts
    /// </remarks>

    // ────────────────────────────────────────
    // Input Events
    // ────────────────────────────────────────

    /// <summary>Player tapped a holder.</summary>
    public struct OnHolderTapped
    {
        public int holderId;
    }

    /// <summary>Input enabled/disabled state changed.</summary>
    public struct OnInputStateChanged
    {
        public bool enabled;
    }

    // ────────────────────────────────────────
    // Holder Events
    // ────────────────────────────────────────

    /// <summary>A holder was selected and its darts are being deployed.</summary>
    public struct OnHolderSelected
    {
        public int holderId;
        public int color;
        public int magazineCount;
    }

    /// <summary>A holder's magazine is empty.</summary>
    public struct OnMagazineEmpty
    {
        public int holderId;
    }

    /// <summary>Holder returned to queue after rail loop with remaining ammo.</summary>
    public struct OnHolderReturned
    {
        public int holderId;
        public int remainingMagazine;
    }

    /// <summary>Continue applied — darts removed from rail, matching balloons should pop.
    /// Design ref: 아웃게임디렉션 (2026-03-17) §이어하기</summary>
    public struct OnContinueApplied
    {
        public int dartsRemoved;
        public int levelId;
    }

    /// <summary>Holder waiting area is filling up — P0 feedback trigger.
    /// Design: 4/5 = warning (yellow), 5/5 = danger (red).</summary>
    public struct OnHolderWarning
    {
        public int waitingCount;
        public int maxSlots;
        public bool isDanger; // true = 5/5 (critical), false = 4/5 (warning)
    }

    /// <summary>All holders are empty.</summary>
    public struct OnAllHoldersEmpty { }

    // ────────────────────────────────────────
    // Dart Events
    // ────────────────────────────────────────

    /// <summary>A dart was fired from a holder.</summary>
    public struct OnDartFired
    {
        public int dartId;
        public int holderId;
        public int color;
    }

    /// <summary>A dart deployed onto the rail.</summary>
    public struct OnDartDeployed
    {
        public int dartId;
        public int color;
        public Vector3 position;
    }

    /// <summary>A dart hit a matching balloon.</summary>
    public struct OnDartHitBalloon
    {
        public int dartId;
        public int balloonId;
        public int color;
    }

    /// <summary>All darts from a holder deployment are complete.</summary>
    public struct OnDeploymentComplete
    {
        public int color;
        public int count;
    }

    // ────────────────────────────────────────
    // Rail Events
    // ────────────────────────────────────────

    /// <summary>Rail occupancy changed. Occupancy = activeDarts / totalSlots.</summary>
    public struct OnRailOccupancyChanged
    {
        public int activeDarts;
        public int totalSlots;
        public float occupancy; // 0.0 ~ 1.0
    }

    /// <summary>A dart was placed on a rail slot by a deploying holder.</summary>
    public struct OnDartPlacedOnSlot
    {
        public int slotIndex;
        public int color;
        public int holderId;
    }

    /// <summary>A dart on a rail slot auto-fired at a matching balloon.</summary>
    public struct OnSlotDartFired
    {
        public int slotIndex;
        public int color;
        public int targetBalloonId;
        public Vector3 from;
        public Vector3 to;
    }

    /// <summary>Rail reached critical occupancy (99.5%+). Fail check in progress.</summary>
    public struct OnRailCritical
    {
        public float occupancy;
        public bool hasOutermostMatch; // true = recovery possible
    }

    /// <summary>6-stage gauge changed. Design ref: 레일초과_코어메카닉_명세 (2026-03-17).
    /// Stages: 0=Safe, 1=Caution, 2=NormalHigh, 3=Warning, 4=Critical, 5=Fail.</summary>
    public struct OnGaugeStageChanged
    {
        public int previousStage;
        public int currentStage;
        public float occupancy;
    }

    /// <summary>Coins earned (level clear reward, daily reward, etc.).</summary>
    public struct OnCoinEarned
    {
        public int amount;
        public Vector3 sourcePosition; // world pos for fly-to-HUD animation
    }

    /// <summary>Holder deployment completed (magazine=0, holder disappears).</summary>
    public struct OnHolderDeploymentDone
    {
        public int holderId;
        public int column;
    }

    // ────────────────────────────────────────
    // Balloon / Pop Events
    // ────────────────────────────────────────

    /// <summary>A balloon was popped.</summary>
    public struct OnBalloonPopped
    {
        public int balloonId;
        public int color;
        public Vector3 position;
    }

    /// <summary>A balloon was spawned onto the board.</summary>
    public struct OnBalloonSpawned
    {
        public int balloonId;
        public int color;
        public Vector3 position;
    }

    /// <summary>A pop sequence completed (single dart hit).</summary>
    public struct OnPopComplete
    {
        public int balloonId;
        public int color;
        public Vector3 position;
        public int popIndex;
    }

    /// <summary>Combo count incremented.</summary>
    public struct OnComboIncremented
    {
        public int comboCount;
    }

    // ────────────────────────────────────────
    // Board Events
    // ────────────────────────────────────────

    /// <summary>Board state changed (balloon removed, etc.).</summary>
    public struct OnBoardStateChanged
    {
        public int remainingBalloons;
    }

    /// <summary>All target balloons cleared — level win.</summary>
    public struct OnBoardCleared
    {
        public int levelId;
        public int score;
        public int starCount;
    }

    /// <summary>Board entered fail state.</summary>
    public struct OnBoardFailed
    {
        public int levelId;
        public string reason;
    }

    // ────────────────────────────────────────
    // Score Events
    // ────────────────────────────────────────

    /// <summary>Score value changed.</summary>
    public struct OnScoreChanged
    {
        public int currentScore;
        public int delta;
    }

    // ────────────────────────────────────────
    // Level Events
    // ────────────────────────────────────────

    /// <summary>A level was loaded and is ready to play.</summary>
    public struct OnLevelLoaded
    {
        public int levelId;
        public int packageId;
    }

    /// <summary>Level completed successfully.</summary>
    public struct OnLevelCompleted
    {
        public int levelId;
        public int score;
        public int starCount;
    }

    /// <summary>Level failed.</summary>
    public struct OnLevelFailed
    {
        public int levelId;
        public int attemptCount;
    }

    // ────────────────────────────────────────
    // Gimmick Events
    // ────────────────────────────────────────

    /// <summary>A gimmick was triggered (Hidden, Spawner, BigObject, Chain).</summary>
    public struct OnGimmickTriggered
    {
        public string gimmickType;
        public int targetId;
    }

    // ────────────────────────────────────────
    // Economy Events
    // ────────────────────────────────────────

    /// <summary>Coin count changed.</summary>
    public struct OnCoinChanged
    {
        public int currentCoins;
        public int delta;
    }

    /// <summary>Gem count changed.</summary>
    public struct OnGemChanged
    {
        public int currentGems;
        public int delta;
    }

    /// <summary>Life count changed.</summary>
    public struct OnLifeChanged
    {
        public int currentLives;
        public int maxLives;
    }

    /// <summary>A booster was used.</summary>
    public struct OnBoosterUsed
    {
        public string boosterType;
    }

    /// <summary>A booster effect was applied to the game state.</summary>
    public struct OnBoosterEffectApplied
    {
        public string boosterType;
        public int affectedCount;
    }

    // ────────────────────────────────────────
    // IAP Events
    // ────────────────────────────────────────

    /// <summary>An in-app purchase completed (success or failure).</summary>
    public struct OnPurchaseCompleted
    {
        public string productId;
        public bool success;
    }

    /// <summary>A previously purchased product was restored.</summary>
    public struct OnPurchaseRestored
    {
        public string productId;
    }

    // ────────────────────────────────────────
    // UI Events
    // ────────────────────────────────────────

    /// <summary>A popup was requested to show.</summary>
    public struct OnPopupRequested
    {
        public string popupId;
        public int priority;
    }

    /// <summary>A popup was closed.</summary>
    public struct OnPopupClosed
    {
        public string popupId;
    }

    /// <summary>Page navigation occurred.</summary>
    public struct OnPageChanged
    {
        public string fromPage;
        public string toPage;
    }

    // ────────────────────────────────────────
    // Holder Rail Events
    // ────────────────────────────────────────

    /// <summary>A holder was placed onto the rail for dart deployment.</summary>
    public struct OnHolderPlacedOnRail
    {
        public int holderId;
        public int color;
    }

    /// <summary>A frozen holder was thawed (unfrozen).</summary>
    public struct OnHolderThawed
    {
        public int holderId;
    }

    /// <summary>A hidden holder was revealed.</summary>
    public struct OnHolderRevealed
    {
        public int holderId;
    }

    /// <summary>A dart was fired at a specific target balloon with trajectory info.</summary>
    public struct OnDartFiredAtTarget
    {
        public int dartId;
        public int balloonId;
        public Vector3 from;
        public Vector3 to;
    }

    // ────────────────────────────────────────
    // Tutorial Events
    // ────────────────────────────────────────

    /// <summary>A tutorial sequence has started.</summary>
    public struct OnTutorialStarted
    {
        public int tutorialId;
    }

    /// <summary>Tutorial advanced to a new step.</summary>
    public struct OnTutorialStepChanged
    {
        public int tutorialId;
        public int stepIndex;
        public string instruction;
    }

    /// <summary>A tutorial sequence was completed.</summary>
    public struct OnTutorialCompleted
    {
        public int tutorialId;
    }

    // ────────────────────────────────────────
    // Scene & Game State Events
    // ────────────────────────────────────────

    /// <summary>Scene transition started.</summary>
    public struct OnSceneTransitionStarted
    {
        public string fromScene;
        public string toScene;
    }

    /// <summary>Scene transition completed.</summary>
    public struct OnSceneTransitionCompleted
    {
        public string sceneName;
    }

    /// <summary>Game was paused (Time.timeScale = 0).</summary>
    public struct OnGamePaused { }

    /// <summary>Game was resumed (Time.timeScale = 1).</summary>
    public struct OnGameResumed { }

    // ────────────────────────────────────────
    // UX Effect Events
    // ────────────────────────────────────────

    /// <summary>A single coin from the fly effect has landed on the gold target.</summary>
    public struct OnCoinFlyLanded { }

    // ────────────────────────────────────────
    // Deploy Freeze Events
    // ────────────────────────────────────────

    /// <summary>A dart was frozen in place during holder deployment.
    /// Visual stays at frozen world position while belt continues.</summary>
    public struct OnDartFrozen
    {
        public int dartId;
        public int slotIndex;
    }

    /// <summary>All frozen darts resumed normal belt movement after deployment ends.</summary>
    public struct OnDartsFrozenCleared { }
}
