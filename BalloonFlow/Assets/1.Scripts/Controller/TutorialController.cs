using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Data model for a single step in a tutorial sequence.
    /// </summary>
    [System.Serializable]
    public class TutorialStep
    {
        /// <summary>Zero-based index of this step within its tutorial.</summary>
        public int stepIndex;

        /// <summary>Human-readable instruction shown to the player.</summary>
        public string instruction;

        /// <summary>
        /// Identifier of the UI/game element to highlight.
        /// Examples: "holder_0", "balloon_3", "board".
        /// Empty string means no highlight.
        /// </summary>
        public string highlightTarget;

        /// <summary>
        /// Action the player must perform to advance past this step.
        /// Values: "tap_holder", "wait_pop", "none".
        /// </summary>
        public string requireAction;

        /// <summary>Whether this step has been completed by the player.</summary>
        public bool isComplete;
    }

    /// <summary>
    /// Configuration for a complete tutorial sequence bound to a level.
    /// </summary>
    [System.Serializable]
    public class TutorialConfig
    {
        /// <summary>Unique identifier for this tutorial.</summary>
        public int tutorialId;

        /// <summary>Level ID that triggers this tutorial when loaded.</summary>
        public int levelId;

        /// <summary>Display name for this tutorial (for debugging).</summary>
        public string tutorialName;

        /// <summary>Ordered list of steps in this tutorial.</summary>
        public TutorialStep[] steps;
    }

    /// <summary>
    /// Controls tutorial level flow — step-by-step guided gameplay for the
    /// first 5 levels and gimmick introduction levels (11, 21, 31, 41, 61).
    /// Restricts input to guide the player through correct actions, tracks
    /// completion in PlayerPrefs, and coordinates visual guidance via
    /// TutorialManager through EventBus events.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Controller | Phase: 2
    /// DB Reference: No DB match — generated from logicFlow (ux_pages_tutorial)
    /// requires: InputHandler (input restriction), HolderManager (highlight targets)
    /// </remarks>
    public class TutorialController : SceneSingleton<TutorialController>
    {
        #region Constants

        private const string PREFS_PREFIX = "BF_Tutorial_Complete_";
        private const string ACTION_TAP_HOLDER = "tap_holder";
        private const string ACTION_WAIT_POP = "wait_pop";
        private const string ACTION_TAP_ANYWHERE = "tap_anywhere";
        private const string ACTION_NONE = "none";

        #endregion

        #region Fields

        // All tutorial configs indexed by levelId for O(1) lookup
        private readonly Dictionary<int, TutorialConfig> _configByLevel = new Dictionary<int, TutorialConfig>();

        private TutorialConfig _activeTutorial;
        private int _currentStepIndex;
        private bool _isTutorialActive;

        #endregion

        #region Properties

        /// <summary>Whether a tutorial is currently running.</summary>
        public bool IsTutorialActive() => _isTutorialActive;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            BuildTutorialConfigs();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Subscribe<OnHolderTapped>(HandleHolderTapped);
            EventBus.Subscribe<OnBalloonPopped>(HandleBalloonPopped);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnHolderTapped>(HandleHolderTapped);
            EventBus.Unsubscribe<OnBalloonPopped>(HandleBalloonPopped);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts the tutorial with the given ID.
        /// Disables free input so the tutorial can control player actions.
        /// </summary>
        /// <param name="tutorialId">ID of the tutorial to start.</param>
        public void StartTutorial(int tutorialId)
        {
            TutorialConfig config = FindConfigById(tutorialId);
            if (config == null)
            {
                Debug.LogWarning($"[TutorialController] Tutorial {tutorialId} not found.");
                return;
            }

            if (_isTutorialActive)
            {
                Debug.LogWarning("[TutorialController] A tutorial is already active. Stopping it first.");
                StopActiveTutorial();
            }

            _activeTutorial = config;
            _currentStepIndex = 0;
            _isTutorialActive = true;

            // Reset step completion flags
            if (_activeTutorial.steps != null)
            {
                foreach (TutorialStep step in _activeTutorial.steps)
                {
                    step.isComplete = false;
                }
            }

            // Restrict input — tutorial controls what the player can tap
            if (InputHandler.HasInstance)
            {
                InputHandler.Instance.DisableInput();
            }

            EventBus.Publish(new OnTutorialStarted { tutorialId = tutorialId });

            // Show the first step immediately
            PublishCurrentStep();
        }

        /// <summary>
        /// Advances to the next tutorial step.
        /// Marks the current step complete and either shows the next step or
        /// completes the tutorial if there are no more steps.
        /// </summary>
        public void AdvanceStep()
        {
            if (!_isTutorialActive || _activeTutorial == null)
            {
                return;
            }

            TutorialStep currentStep = GetCurrentStep();
            if (currentStep != null)
            {
                currentStep.isComplete = true;
            }

            _currentStepIndex++;

            if (_activeTutorial.steps == null || _currentStepIndex >= _activeTutorial.steps.Length)
            {
                CompleteTutorial();
            }
            else
            {
                PublishCurrentStep();
            }
        }

        /// <summary>
        /// Skips the active tutorial immediately, marks it as complete.
        /// Re-enables input.
        /// </summary>
        public void SkipTutorial()
        {
            if (!_isTutorialActive || _activeTutorial == null)
            {
                return;
            }

            int tutorialId = _activeTutorial.tutorialId;
            StopActiveTutorial();
            SaveCompletion(tutorialId);

            EventBus.Publish(new OnTutorialCompleted { tutorialId = tutorialId });

            if (InputHandler.HasInstance)
            {
                InputHandler.Instance.EnableInput();
            }
        }

        /// <summary>
        /// Returns the current TutorialStep, or null if no tutorial is active.
        /// </summary>
        public TutorialStep GetCurrentStep()
        {
            if (!_isTutorialActive || _activeTutorial == null)
            {
                return null;
            }

            if (_activeTutorial.steps == null || _currentStepIndex < 0 || _currentStepIndex >= _activeTutorial.steps.Length)
            {
                return null;
            }

            return _activeTutorial.steps[_currentStepIndex];
        }

        /// <summary>
        /// Whether the tutorial with the given ID has been completed previously.
        /// </summary>
        /// <param name="tutorialId">ID of the tutorial to check.</param>
        public bool IsTutorialComplete(int tutorialId)
        {
            return PlayerPrefs.GetInt(PREFS_PREFIX + tutorialId, 0) == 1;
        }

        #endregion

        #region Private Methods — Tutorial Config Construction

        /// <summary>
        /// Builds all tutorial configs in code. First 5 levels are basic tutorials;
        /// levels 11, 21, 31, 41, 61 are gimmick introduction tutorials.
        /// </summary>
        private void BuildTutorialConfigs()
        {
            _configByLevel.Clear();

            // ── Basic tutorials (Levels 1–5) ──────────────────────────────────

            RegisterConfig(new TutorialConfig
            {
                tutorialId = 1,
                levelId = 1,
                tutorialName = "Tap a holder to deploy",
                steps = new TutorialStep[]
                {
                    new TutorialStep
                    {
                        stepIndex = 0,
                        instruction = "Tap a holder to deploy its darts!",
                        highlightTarget = "holder_0",
                        requireAction = ACTION_NONE,
                        isComplete = false
                    },
                    new TutorialStep
                    {
                        stepIndex = 1,
                        instruction = "Well done! Watch the darts fly.",
                        highlightTarget = "board",
                        requireAction = ACTION_WAIT_POP,
                        isComplete = false
                    },
                    new TutorialStep
                    {
                        stepIndex = 2,
                        instruction = "Pop all the balloons to clear the level!",
                        highlightTarget = string.Empty,
                        requireAction = ACTION_NONE,
                        isComplete = false
                    }
                }
            });

            RegisterConfig(new TutorialConfig
            {
                tutorialId = 2,
                levelId = 2,
                tutorialName = "Match colors",
                steps = new TutorialStep[]
                {
                    new TutorialStep
                    {
                        stepIndex = 0,
                        instruction = "Darts only pop balloons of the same color!",
                        highlightTarget = "holder_0",
                        requireAction = ACTION_NONE,
                        isComplete = false
                    },
                    new TutorialStep
                    {
                        stepIndex = 1,
                        instruction = "Tap the holder that matches the balloon colors.",
                        highlightTarget = "holder_0",
                        requireAction = ACTION_TAP_HOLDER,
                        isComplete = false
                    },
                    new TutorialStep
                    {
                        stepIndex = 2,
                        instruction = "Great! Now try the other holder.",
                        highlightTarget = "holder_1",
                        requireAction = ACTION_TAP_HOLDER,
                        isComplete = false
                    }
                }
            });

            RegisterConfig(new TutorialConfig
            {
                tutorialId = 3,
                levelId = 3,
                tutorialName = "Multiple holders",
                steps = new TutorialStep[]
                {
                    new TutorialStep
                    {
                        stepIndex = 0,
                        instruction = "Three colors now! Match each holder to its balloons.",
                        highlightTarget = "board",
                        requireAction = ACTION_NONE,
                        isComplete = false
                    },
                    new TutorialStep
                    {
                        stepIndex = 1,
                        instruction = "Tap the red holder to clear red balloons.",
                        highlightTarget = "holder_0",
                        requireAction = ACTION_TAP_HOLDER,
                        isComplete = false
                    },
                    new TutorialStep
                    {
                        stepIndex = 2,
                        instruction = "Now pick the best holder to clear the board!",
                        highlightTarget = string.Empty,
                        requireAction = ACTION_NONE,
                        isComplete = false
                    }
                }
            });

            RegisterConfig(new TutorialConfig
            {
                tutorialId = 4,
                levelId = 4,
                tutorialName = "Watch the overflow",
                steps = new TutorialStep[]
                {
                    new TutorialStep
                    {
                        stepIndex = 0,
                        instruction = "Watch out! If too many holders pile up you'll fail.",
                        highlightTarget = "holder_queue",
                        requireAction = ACTION_NONE,
                        isComplete = false
                    },
                    new TutorialStep
                    {
                        stepIndex = 1,
                        instruction = "Keep the holder queue short — tap holders quickly!",
                        highlightTarget = "holder_0",
                        requireAction = ACTION_TAP_HOLDER,
                        isComplete = false
                    },
                    new TutorialStep
                    {
                        stepIndex = 2,
                        instruction = "Keep tapping before the queue overflows!",
                        highlightTarget = string.Empty,
                        requireAction = ACTION_NONE,
                        isComplete = false
                    }
                }
            });

            RegisterConfig(new TutorialConfig
            {
                tutorialId = 5,
                levelId = 5,
                tutorialName = "Choose wisely",
                steps = new TutorialStep[]
                {
                    new TutorialStep
                    {
                        stepIndex = 0,
                        instruction = "Four colors! Think before you tap.",
                        highlightTarget = "board",
                        requireAction = ACTION_NONE,
                        isComplete = false
                    },
                    new TutorialStep
                    {
                        stepIndex = 1,
                        instruction = "Pick the holder with the most matching balloons first.",
                        highlightTarget = string.Empty,
                        requireAction = ACTION_TAP_HOLDER,
                        isComplete = false
                    },
                    new TutorialStep
                    {
                        stepIndex = 2,
                        instruction = "Strategy matters — clear the board for 3 stars!",
                        highlightTarget = string.Empty,
                        requireAction = ACTION_NONE,
                        isComplete = false
                    }
                }
            });

            // ── Gimmick introduction tutorials ────────────────────────────────

            // Level 11: Hidden balloon gimmick
            RegisterConfig(new TutorialConfig
            {
                tutorialId = 11,
                levelId = 11,
                tutorialName = "Hidden Balloon",
                steps = new TutorialStep[]
                {
                    new TutorialStep
                    {
                        stepIndex = 0,
                        instruction = "Some balloons are hidden! Pop nearby balloons to reveal them.",
                        highlightTarget = "gimmick_hidden",
                        requireAction = ACTION_NONE,
                        isComplete = false
                    },
                    new TutorialStep
                    {
                        stepIndex = 1,
                        instruction = "Clear the visible balloons first to uncover hidden ones.",
                        highlightTarget = string.Empty,
                        requireAction = ACTION_WAIT_POP,
                        isComplete = false
                    }
                }
            });

            // Level 21: Spawner gimmick
            RegisterConfig(new TutorialConfig
            {
                tutorialId = 21,
                levelId = 21,
                tutorialName = "Balloon Spawner",
                steps = new TutorialStep[]
                {
                    new TutorialStep
                    {
                        stepIndex = 0,
                        instruction = "A spawner keeps producing balloons! Destroy it fast.",
                        highlightTarget = "gimmick_spawner",
                        requireAction = ACTION_NONE,
                        isComplete = false
                    },
                    new TutorialStep
                    {
                        stepIndex = 1,
                        instruction = "Target the spawner balloon directly to stop it!",
                        highlightTarget = "gimmick_spawner",
                        requireAction = ACTION_WAIT_POP,
                        isComplete = false
                    }
                }
            });

            // Level 31: Big Object gimmick
            RegisterConfig(new TutorialConfig
            {
                tutorialId = 31,
                levelId = 31,
                tutorialName = "Big Object",
                steps = new TutorialStep[]
                {
                    new TutorialStep
                    {
                        stepIndex = 0,
                        instruction = "A giant balloon needs multiple hits to pop!",
                        highlightTarget = "gimmick_bigobject",
                        requireAction = ACTION_NONE,
                        isComplete = false
                    },
                    new TutorialStep
                    {
                        stepIndex = 1,
                        instruction = "Keep sending matching darts until the big one bursts!",
                        highlightTarget = "gimmick_bigobject",
                        requireAction = ACTION_WAIT_POP,
                        isComplete = false
                    }
                }
            });

            // Level 41: Chain gimmick
            RegisterConfig(new TutorialConfig
            {
                tutorialId = 41,
                levelId = 41,
                tutorialName = "Chain Reaction",
                steps = new TutorialStep[]
                {
                    new TutorialStep
                    {
                        stepIndex = 0,
                        instruction = "Chain balloons explode together when one is popped!",
                        highlightTarget = "gimmick_chain",
                        requireAction = ACTION_NONE,
                        isComplete = false
                    },
                    new TutorialStep
                    {
                        stepIndex = 1,
                        instruction = "Pop one chain balloon to clear the whole group at once!",
                        highlightTarget = string.Empty,
                        requireAction = ACTION_WAIT_POP,
                        isComplete = false
                    }
                }
            });

            // Level 61: (future gimmick placeholder — combo milestone)
            RegisterConfig(new TutorialConfig
            {
                tutorialId = 61,
                levelId = 61,
                tutorialName = "Combo Bonus",
                steps = new TutorialStep[]
                {
                    new TutorialStep
                    {
                        stepIndex = 0,
                        instruction = "Pop balloons in quick succession to build a combo!",
                        highlightTarget = "board",
                        requireAction = ACTION_NONE,
                        isComplete = false
                    },
                    new TutorialStep
                    {
                        stepIndex = 1,
                        instruction = "Higher combos mean bonus score — go for 3 stars!",
                        highlightTarget = string.Empty,
                        requireAction = ACTION_NONE,
                        isComplete = false
                    }
                }
            });
        }

        private void RegisterConfig(TutorialConfig config)
        {
            if (config == null)
            {
                return;
            }
            _configByLevel[config.levelId] = config;
        }

        private TutorialConfig FindConfigById(int tutorialId)
        {
            foreach (TutorialConfig config in _configByLevel.Values)
            {
                if (config.tutorialId == tutorialId)
                {
                    return config;
                }
            }
            return null;
        }

        /// <summary>
        /// LevelDatabase에서 해당 레벨의 tutorialSteps를 읽어 TutorialConfig를 생성.
        /// tutorialSteps가 없으면 null 반환 → 하드코딩 fallback 사용.
        /// </summary>
        private TutorialConfig TryBuildFromLevelData(int levelId)
        {
            var db = Resources.Load<LevelDatabase>("LevelDatabase");
            if (db == null || db.levels == null) return null;

            LevelConfig levelConfig = null;
            for (int i = 0; i < db.levels.Length; i++)
            {
                if (db.levels[i].levelId == levelId)
                {
                    levelConfig = db.levels[i];
                    break;
                }
            }

            if (levelConfig == null || levelConfig.tutorialSteps == null || levelConfig.tutorialSteps.Length == 0)
                return null;

            // TutorialStepData[] → TutorialStep[] 변환
            var steps = new TutorialStep[levelConfig.tutorialSteps.Length];
            for (int i = 0; i < steps.Length; i++)
            {
                var src = levelConfig.tutorialSteps[i];
                steps[i] = new TutorialStep
                {
                    stepIndex = i,
                    instruction = src.instruction ?? "",
                    highlightTarget = src.highlightTarget ?? "",
                    requireAction = string.IsNullOrEmpty(src.requireAction) ? ACTION_NONE : src.requireAction,
                    isComplete = false
                };
            }

            return new TutorialConfig
            {
                tutorialId = levelId, // levelId를 tutorialId로 사용
                levelId = levelId,
                tutorialName = $"Level {levelId} Tutorial (from data)",
                steps = steps
            };
        }

        #endregion

        #region Private Methods — Flow Control

        private void PublishCurrentStep()
        {
            TutorialStep step = GetCurrentStep();
            if (step == null)
            {
                return;
            }

            // For action steps that require a holder tap, re-enable input briefly
            // so the player can actually perform the required action.
            if (step.requireAction == ACTION_TAP_HOLDER || step.requireAction == ACTION_NONE)
            {
                if (InputHandler.HasInstance)
                {
                    InputHandler.Instance.EnableInput();
                }
            }
            else if (step.requireAction == ACTION_TAP_ANYWHERE)
            {
                // tap_anywhere: enable input but also let TutorialManager handle the tap overlay
                if (InputHandler.HasInstance)
                {
                    InputHandler.Instance.EnableInput();
                }
            }
            else
            {
                // wait_pop steps: disable input while waiting for animation
                if (InputHandler.HasInstance)
                {
                    InputHandler.Instance.DisableInput();
                }
            }

            EventBus.Publish(new OnTutorialStepChanged
            {
                tutorialId = _activeTutorial.tutorialId,
                stepIndex = step.stepIndex,
                instruction = step.instruction
            });
        }

        private void CompleteTutorial()
        {
            if (_activeTutorial == null)
            {
                return;
            }

            int tutorialId = _activeTutorial.tutorialId;
            SaveCompletion(tutorialId);
            StopActiveTutorial();

            EventBus.Publish(new OnTutorialCompleted { tutorialId = tutorialId });

            // Re-enable input for normal gameplay
            if (InputHandler.HasInstance)
            {
                InputHandler.Instance.EnableInput();
            }
        }

        private void StopActiveTutorial()
        {
            _activeTutorial = null;
            _currentStepIndex = 0;
            _isTutorialActive = false;
        }

        private void SaveCompletion(int tutorialId)
        {
            PlayerPrefs.SetInt(PREFS_PREFIX + tutorialId, 1);
            PlayerPrefs.Save();
        }

        #endregion

        #region Private Methods — Event Handlers

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            // 로딩/fade 끝난 뒤 시작 — 튜토리얼이 로딩 화면 위로 떠 보이는 것 방지
            StartCoroutine(StartTutorialAfterLoad(evt.levelId));
        }

        private IEnumerator StartTutorialAfterLoad(int levelId)
        {
            while (LevelManager.HasInstance && LevelManager.Instance.IsLoading) yield return null;
            while (UIManager.HasInstance && UIManager.Instance.IsFading) yield return null;

            Debug.Log($"[TutorialDbg] HandleLevelLoaded levelId={levelId}");

            // 1) LevelConfig에 tutorialSteps가 있으면 우선 사용
            TutorialConfig configFromData = TryBuildFromLevelData(levelId);
            if (configFromData != null)
            {
                bool complete1 = IsTutorialComplete(configFromData.tutorialId);
                Debug.Log($"[TutorialDbg] LevelData config found: tutorialId={configFromData.tutorialId} complete={complete1}");
                if (complete1) yield break;
                _configByLevel[configFromData.levelId] = configFromData;
                StartTutorial(configFromData.tutorialId);
                yield break;
            }

            // 2) Fallback: 코드에 하드코딩된 튜토리얼
            bool hasConfig = _configByLevel.TryGetValue(levelId, out TutorialConfig config);
            Debug.Log($"[TutorialDbg] Hardcoded config for level {levelId}: {(hasConfig ? $"tutorialId={config.tutorialId}" : "NONE")}");
            if (!hasConfig) yield break;

            bool complete2 = IsTutorialComplete(config.tutorialId);
            Debug.Log($"[TutorialDbg] tutorialId={config.tutorialId} alreadyComplete={complete2}");
            if (complete2) yield break;

            StartTutorial(config.tutorialId);
        }

        private void HandleHolderTapped(OnHolderTapped evt)
        {
            if (!_isTutorialActive)
            {
                return;
            }

            TutorialStep step = GetCurrentStep();
            if (step == null)
            {
                return;
            }

            if (step.requireAction == ACTION_TAP_HOLDER)
            {
                AdvanceStep();
            }
        }

        private void HandleBalloonPopped(OnBalloonPopped evt)
        {
            if (!_isTutorialActive)
            {
                return;
            }

            TutorialStep step = GetCurrentStep();
            if (step == null)
            {
                return;
            }

            if (step.requireAction == ACTION_WAIT_POP)
            {
                AdvanceStep();
            }
        }

        #endregion
    }
}
