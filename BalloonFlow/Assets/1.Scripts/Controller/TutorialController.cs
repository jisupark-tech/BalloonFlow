using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

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

        /// <summary>Human-readable instruction shown to the player. (instructionKey 비었을 때 폴백)</summary>
        public string instruction;

        /// <summary>CSV(TextData) Key. 지정 시 LocalizationService.Get 으로 해석해 instruction 대신 표시.</summary>
        public string instructionKey;

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

        /// <summary>ROLLBACK_TUTORIAL_HIDE_SKIP_ON_ITEM_USE_20260622: true 면 이 스텝 동안 Skip(X) 버튼을 숨긴다.
        /// 튜토리얼을 통한 아이템 사용(강제 완료) 스텝에서 X 로 빠져나가지 못하게 함. 기본 false(=기존처럼 X 노출).</summary>
        public bool hideSkipButton;

        /// <summary>Whether this step has been completed by the player.</summary>
        public bool isComplete;

        // [2026-05-12] Visual layout override — Inspector / Data 에서 step 별 layout 직접 지정.
        // overrideVisualLayout = false 면 highlightTarget 기반 자동 layout 사용 (기존 동작).
        public bool overrideVisualLayout;
        public bool useCutoutFrame = true;
        public Vector2 cutoutFramePosition;
        public Vector2 cutoutFrameSize;
        /// <summary>CutoutFrame Image sprite. null이면 prefab/default outline 유지.</summary>
        public Sprite cutoutFrameSprite;
        public Vector2 instructionPanelPosition;
        public Vector2 instructionPanelSize;
        // [2026-05-13] Arrow 활성 토글 — 기본 true (backward compat). false 면 step 시작 시 비활성.
        public bool useArrowIndicator = true;
        public Vector2 arrowIndicatorPosition;
        public bool useHandIndicator;
        public Vector2 handIndicatorPosition;
        /// <summary>HandIndicator Image sprite. null이면 prefab default 유지.</summary>
        public Sprite handIndicatorSprite;
        public TutorialHandTweenType handTweenType;
        public Vector2 handTweenMoveOffset = new Vector2(0f, -30f);
        public float handTweenScale = 1.12f;
        public float handTweenRotation;
        public float handTweenDuration = 0.55f;
        /// <summary>CutoutMask 의 Image sprite. null 이면 prefab default 유지.</summary>
        public Sprite cutoutMaskSprite;

        // [2026-05-15] cutout size — overrideVisualLayout=false 시 ShowCutoutForHolder 가 참조.
        // 0 이면 기본 200x200 사용 (backward compat).
        public float cutoutWidth;
        public float cutoutHeight;

        // [2026-05-15] tap_anywhere 액션 시 TextTap/TextTapOutline 활성 + 위치 override.
        public bool useTextTap = true;
        public Vector2 textTapPosition;

        // ROLLBACK_TUTORIAL_INSTRUCTION_COLOR_20260622: instruction 텍스트 색상 override.
        //   useInstructionColor=true 면 instructionColor 적용, false 면 프리팹 기본색 사용(기존 동작).
        public bool useInstructionColor;
        public Color instructionColor = Color.white;
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

        /// <summary>ROLLBACK_TUTORIAL_MANUAL_TRIGGER_20260622: true 면 levelId 진입 시 자동 시작하지 않고,
        /// 외부에서 명시적으로 StartTutorialForLevel/StartTutorial 을 호출할 때만 시작한다.
        /// (예: 아이템 언락 Claim → 보상연출 종료 후 트리거하는 흐름.) 기본 false(=레벨 진입 시 자동 시작).</summary>
        public bool manualTriggerOnly;

        /// <summary>ROLLBACK_POPUP_ITEM_DESC_TUTORIAL_GATE_20260622: true 면 PopupItemDescription 이 떠 있는 동안
        /// 튜토리얼 시작을 보류하고, 그 팝업의 ButtonSingle(또는 X)로 닫힌 뒤에 시작한다.
        /// 아이템 해금 레벨에서 "설명 팝업 → ButtonSingle 클릭 → 튜토리얼" 순서 보장(동시 노출 방지).
        /// 기본 false. PopupItemDescription.IsShowing 으로 게이트.</summary>
        public bool waitForItemDescription;

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

        // [2026-05-15] rail_warning 글로벌 튜토리얼 — gauge stage Warning(>=90%) 진입 시 1회 등장.
        // 일반 level 기반 tutorialId 와 충돌 없는 1000 사용. PlayerPrefs 영구 저장 (앱 단위 1회).
        private const int RAIL_WARNING_TUTORIAL_ID = 1000;
        private const string PREFS_RAIL_WARNING_SHOWN = "BF_RailWarningTutorialShown";

        #endregion

        #region Fields

        // All tutorial configs indexed by levelId for O(1) lookup
        private readonly Dictionary<int, TutorialConfig> _configByLevel = new Dictionary<int, TutorialConfig>();

        private TutorialConfig _activeTutorial;
        private int _currentStepIndex;
        private bool _isTutorialActive;

        // [2026-05-15] 미클리어 재진입 시 튜토리얼 재등장 — 튜토리얼 완료/스킵 후에도 즉시 SaveCompletion 하지 않고,
        // OnLevelCompleted (실제 스테이지 클리어) 시점에만 영구 저장. 클리어 못 하면 다음 진입 때 다시 등장.
        // rail_warning 같은 글로벌 튜토리얼은 PREFS_RAIL_WARNING_SHOWN 별도 키로 즉시 저장.
        private int _pendingCompletionTutorialId = -1;

        // [2026-05-15] rail_warning 글로벌 튜토리얼 config — _configByLevel 안에 들어가지 않음.
        private TutorialConfig _railWarningConfig;
        private bool _loadedTutorialCatalog;
        private Coroutine _startTutorialForLevelCoroutine;

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
            // [2026-05-15] 미클리어 재진입 시 재등장 — Level 클리어 시점에만 영구 저장.
            EventBus.Subscribe<OnLevelCompleted>(HandleLevelCompletedForTutorial);
            // [2026-05-15] rail_warning — gauge stage Warning(>=90%) 진입 시 1회 트리거.
            EventBus.Subscribe<OnGaugeStageChanged>(HandleGaugeStageForRailWarning);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnHolderTapped>(HandleHolderTapped);
            EventBus.Unsubscribe<OnBalloonPopped>(HandleBalloonPopped);
            EventBus.Unsubscribe<OnLevelCompleted>(HandleLevelCompletedForTutorial);
            EventBus.Unsubscribe<OnGaugeStageChanged>(HandleGaugeStageForRailWarning);
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
        /// <remarks>
        /// [2026-05-15] SaveCompletion 즉시 호출 제거. _pendingCompletionTutorialId 에만 저장 →
        /// 스테이지 클리어(OnLevelCompleted) 시점에 영구 저장. Skip 만 하고 못 깨면 다음 진입 시 다시 등장.
        /// </remarks>
        public void SkipTutorial()
        {
            if (!_isTutorialActive || _activeTutorial == null)
            {
                return;
            }

            int tutorialId = _activeTutorial.tutorialId;
            _pendingCompletionTutorialId = tutorialId;
            StopActiveTutorial();

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
        /// Builds the tutorial config table.
        /// Priority: (1) TutorialCatalog SO from Resources, (2) hardcoded fallback below.
        /// LevelDatabase.tutorialSteps is read per-level in TryBuildFromLevelData() and
        /// always takes precedence over both — see HandleLevelLoaded flow.
        /// </summary>
        private void BuildTutorialConfigs()
        {
            _configByLevel.Clear();
            _loadedTutorialCatalog = false;

            // Priority 1: TutorialCatalog SO (primary runtime source — edited via TutorialEditorWindow or Inspector).
            TutorialCatalog catalog = Resources.Load<TutorialCatalog>(TutorialCatalog.RESOURCES_PATH);
            if (catalog != null && catalog.Tutorials != null && catalog.Tutorials.Count > 0)
            {
                for (int i = 0; i < catalog.Tutorials.Count; i++)
                {
                    RegisterConfig(catalog.Tutorials[i]);
                }
                _loadedTutorialCatalog = true;
                return;
            }

            // Priority 2: hardcoded fallback (legacy — kept for safety until Catalog asset is committed).
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
            // [2026-05-15] rail_warning 글로벌 튜토리얼 우선 체크.
            if (_railWarningConfig != null && _railWarningConfig.tutorialId == tutorialId)
                return _railWarningConfig;

            foreach (TutorialConfig config in _configByLevel.Values)
            {
                if (config.tutorialId == tutorialId)
                {
                    return config;
                }
            }
            return null;
        }

        private bool TryGetTutorialEditorConfig(int levelId, out TutorialConfig config)
        {
            // ROLLBACK_TUTORIAL_EDITOR_PRIORITY_20260622:
            // Tutorial Editor saves TutorialCatalog.asset with sequence-level metadata
            // such as manualTriggerOnly and waitForItemDescription. When that catalog is
            // loaded, do not rebuild a metadata-less config from LevelData for the same level.
            config = null;
            return _loadedTutorialCatalog
                   && _configByLevel.TryGetValue(levelId, out config)
                   && config != null;
        }

        private TutorialConfig ResolveTutorialConfigForLevel(int levelId, out string source)
        {
            if (TryGetTutorialEditorConfig(levelId, out TutorialConfig editorConfig))
            {
                source = "TutorialCatalog";
                return editorConfig;
            }

            TutorialConfig configFromData = TryBuildFromLevelData(levelId);
            if (configFromData != null)
            {
                source = "LevelData";
                _configByLevel[configFromData.levelId] = configFromData;
                return configFromData;
            }

            if (_configByLevel.TryGetValue(levelId, out TutorialConfig fallbackConfig))
            {
                source = _loadedTutorialCatalog ? "TutorialCatalog" : "Hardcoded";
                return fallbackConfig;
            }

            source = "None";
            return null;
        }

        /// <summary>
        /// LevelDatabase에서 해당 레벨의 tutorialSteps를 읽어 TutorialConfig를 생성.
        /// tutorialSteps가 없으면 null 반환 → 하드코딩 fallback 사용.
        /// </summary>
        private TutorialConfig TryBuildFromLevelData(int levelId)
        {
            LevelConfig levelConfig = null;
            if (LevelEpisodeService.HasInstance)
                levelConfig = LevelEpisodeService.Instance.GetLevel(levelId);

#if UNITY_EDITOR
            // ROLLBACK_TUTORIAL_LEVELDATABASE_NO_RESOURCES:
            // Keep LevelDatabase editor-only so Resources does not pull it into builds.
            if (levelConfig == null)
            {
                var db = AssetDatabase.LoadAssetAtPath<LevelDatabase>("Assets/EditorData/LevelDatabase.asset");
                if (db != null && db.levels != null)
                {
                    for (int i = 0; i < db.levels.Length; i++)
                    {
                        if (db.levels[i].levelId == levelId)
                        {
                            levelConfig = db.levels[i];
                            break;
                        }
                    }
                }
            }
#endif

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
                    instructionKey = src.instructionKey ?? "",
                    highlightTarget = src.highlightTarget ?? "",
                    requireAction = string.IsNullOrEmpty(src.requireAction) ? ACTION_NONE : src.requireAction,
                    isComplete = false,
                    // [2026-05-12] Visual override field 전달
                    overrideVisualLayout = src.overrideVisualLayout,
                    useCutoutFrame = src.useCutoutFrame,
                    cutoutFramePosition = src.cutoutFramePosition,
                    cutoutFrameSize = src.cutoutFrameSize,
                    cutoutFrameSprite = src.cutoutFrameSprite,
                    instructionPanelPosition = src.instructionPanelPosition,
                    instructionPanelSize = src.instructionPanelSize,
                    useArrowIndicator = src.useArrowIndicator,
                    arrowIndicatorPosition = src.arrowIndicatorPosition,
                    useHandIndicator = src.useHandIndicator,
                    handIndicatorPosition = src.handIndicatorPosition,
                    handIndicatorSprite = src.handIndicatorSprite,
                    handTweenType = src.handTweenType,
                    handTweenMoveOffset = src.handTweenMoveOffset,
                    handTweenScale = src.handTweenScale,
                    handTweenRotation = src.handTweenRotation,
                    handTweenDuration = src.handTweenDuration,
                    cutoutMaskSprite = src.cutoutMaskSprite,
                    cutoutWidth = src.cutoutWidth,
                    cutoutHeight = src.cutoutHeight,
                    useTextTap = src.useTextTap,
                    textTapPosition = src.textTapPosition,
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
                // instructionKey 지정 시 CSV 텍스트로 해석, 없으면 직접입력 instruction 사용(하위호환).
                instruction = !string.IsNullOrEmpty(step.instructionKey)
                    ? LocalizationService.Get(step.instructionKey)
                    : step.instruction
            });
        }

        private void CompleteTutorial()
        {
            if (_activeTutorial == null)
            {
                return;
            }

            int tutorialId = _activeTutorial.tutorialId;
            // [2026-05-15] 즉시 SaveCompletion 하지 않음 — 스테이지 클리어 시점에 저장.
            // 튜토리얼만 끝내고 fail/quit 하면 다음 진입에서 다시 등장.
            _pendingCompletionTutorialId = tutorialId;
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

        // OnLevelLoaded 가 짧은 시간 내 여러 번 발화될 수 있어 (scene 전환 / loadingFlow / continue 등)
        // 코루틴 동시 다수 실행 시 StartTutorial 여러 번 호출 → "tutorial already active" 반복 → Stop/Start 사이클로 CPU 낭비.
        // 진행 중 코루틴 1개만 보장.
        private Coroutine _startTutorialCoroutine;

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            // 진행 중 코루틴 cancel — race 방지
            if (_startTutorialCoroutine != null)
            {
                StopCoroutine(_startTutorialCoroutine);
                _startTutorialCoroutine = null;
            }
            // [2026-05-15] 이전 레벨에서 pending 완료가 있었지만 클리어 안 한 채 다음 레벨 진입 → pending discard.
            //   (해당 레벨 재진입이 아니라 LoadPendingLevel 등으로 넘어간 경우 클리어 안 한 거니까 pending 폐기.)
            _pendingCompletionTutorialId = -1;
            // 로딩/fade 끝난 뒤 시작 — 튜토리얼이 로딩 화면 위로 떠 보이는 것 방지
            _startTutorialCoroutine = StartCoroutine(StartTutorialAfterLoad(evt.levelId));
        }

        /// <summary>
        /// [2026-05-15] OnLevelCompleted — pending 완료된 튜토리얼이 있으면 영구 저장.
        /// 클리어 못 한 채 튜토리얼만 끝낸 경우 pending 폐기되어 다음 진입 시 재등장.
        /// </summary>
        private void HandleLevelCompletedForTutorial(OnLevelCompleted evt)
        {
            if (_pendingCompletionTutorialId > 0)
            {
                SaveCompletion(_pendingCompletionTutorialId);
                Debug.Log($"[TutorialDbg] Stage cleared → tutorialId={_pendingCompletionTutorialId} 영구 저장.");
                _pendingCompletionTutorialId = -1;
            }
        }

        /// <summary>
        /// [2026-05-15] rail_warning — gauge stage Warning(>=90%) 진입 시 1회 글로벌 튜토리얼.
        /// PlayerPrefs(PREFS_RAIL_WARNING_SHOWN) 영구 저장 — 앱 단위 평생 1회.
        /// </summary>
        private void HandleGaugeStageForRailWarning(OnGaugeStageChanged evt)
        {
            // 영구 저장된 적 있으면 skip.
            if (PlayerPrefs.GetInt(PREFS_RAIL_WARNING_SHOWN, 0) == 1) return;
            // 다른 튜토리얼 진행 중이면 중첩 방지.
            if (_isTutorialActive) return;
            // [2026-05-18] 1렙 이후 (lv >= 2) 에만 트리거 — 1렙 첫 진입 튜토리얼과 텍스트 충돌 방지.
            // CurrentLevelId 가 0/미준비 (LevelManager 부재 등) 인 fallback 케이스도 차단.
            if (!LevelManager.HasInstance) return;
            if (LevelManager.Instance.CurrentLevelId <= 1) return;
            // Warning 단계 (=3) 로 처음 진입할 때만 — 이미 Warning 이상에서 더 올라간 transition 제외.
            if (evt.currentStage != (int)GaugeStage.Warning) return;
            if (evt.previousStage >= (int)GaugeStage.Warning) return;

            StartRailWarningTutorial();
        }

        private void StartRailWarningTutorial()
        {
            // 즉시 영구 저장 — 시작했으면 본 것으로 간주 (skip 해도 동일).
            PlayerPrefs.SetInt(PREFS_RAIL_WARNING_SHOWN, 1);
            PlayerPrefs.Save();

            if (_railWarningConfig == null)
            {
                _railWarningConfig = BuildRailWarningConfigFromCatalog() ?? BuildRailWarningConfigFallback();
            }
            StartTutorial(RAIL_WARNING_TUTORIAL_ID);
        }

        /// <summary>
        /// Resources/TutorialCatalog.asset에서 tutorialName == "tutorial_rail_warning" 엔트리를 찾아
        /// Tutorial Editor에 저장된 모든 step 필드(레이아웃 좌표·크기·인디케이터·스프라이트 등)를 그대로 복제한다.
        /// tutorialId/levelId만 글로벌 런타임 식별값으로 덮어쓴다. 카탈로그/엔트리 부재 시 null 반환.
        /// </summary>
        private TutorialConfig BuildRailWarningConfigFromCatalog()
        {
            TutorialCatalog catalog = Resources.Load<TutorialCatalog>(TutorialCatalog.RESOURCES_PATH);
            if (catalog == null || catalog.Tutorials == null) return null;

            TutorialConfig source = null;
            for (int i = 0; i < catalog.Tutorials.Count; i++)
            {
                TutorialConfig candidate = catalog.Tutorials[i];
                if (candidate != null && candidate.tutorialName == "tutorial_rail_warning")
                {
                    source = candidate;
                    break;
                }
            }
            if (source == null || source.steps == null || source.steps.Length == 0) return null;

            TutorialStep[] clonedSteps = new TutorialStep[source.steps.Length];
            for (int i = 0; i < source.steps.Length; i++)
            {
                TutorialStep src = source.steps[i];
                if (src == null) continue;
                clonedSteps[i] = new TutorialStep
                {
                    stepIndex = i,
                    instruction = src.instruction,
                    instructionKey = src.instructionKey,
                    highlightTarget = src.highlightTarget,
                    requireAction = src.requireAction,
                    isComplete = false,
                    overrideVisualLayout = src.overrideVisualLayout,
                    useCutoutFrame = src.useCutoutFrame,
                    cutoutFramePosition = src.cutoutFramePosition,
                    cutoutFrameSize = src.cutoutFrameSize,
                    cutoutFrameSprite = src.cutoutFrameSprite,
                    instructionPanelPosition = src.instructionPanelPosition,
                    instructionPanelSize = src.instructionPanelSize,
                    useArrowIndicator = src.useArrowIndicator,
                    arrowIndicatorPosition = src.arrowIndicatorPosition,
                    useHandIndicator = src.useHandIndicator,
                    handIndicatorPosition = src.handIndicatorPosition,
                    handIndicatorSprite = src.handIndicatorSprite,
                    handTweenType = src.handTweenType,
                    handTweenMoveOffset = src.handTweenMoveOffset,
                    handTweenScale = src.handTweenScale,
                    handTweenRotation = src.handTweenRotation,
                    handTweenDuration = src.handTweenDuration,
                    cutoutMaskSprite = src.cutoutMaskSprite,
                    cutoutWidth = src.cutoutWidth,
                    cutoutHeight = src.cutoutHeight,
                    useTextTap = src.useTextTap,
                    textTapPosition = src.textTapPosition
                };
            }

            return new TutorialConfig
            {
                tutorialId = RAIL_WARNING_TUTORIAL_ID,
                levelId = -1, // 글로벌 (특정 레벨 종속 X)
                tutorialName = source.tutorialName,
                steps = clonedSteps
            };
        }

        /// <summary>
        /// Catalog 부재/엔트리 누락 시 안전망 — 기존 하드코딩 config. 1.0 EN-only.
        /// </summary>
        private TutorialConfig BuildRailWarningConfigFallback()
        {
            return new TutorialConfig
            {
                tutorialId = RAIL_WARNING_TUTORIAL_ID,
                levelId = -1, // 글로벌 (특정 레벨 종속 X)
                tutorialName = "Rail Warning",
                steps = new TutorialStep[]
                {
                    new TutorialStep
                    {
                        stepIndex = 0,
                        // [#4] 1.0 EN-only — 한국어 글리프 미포함 폰트라 깨져 보임. 명세 §4-3 영문으로 교체.
                        instruction = "Conveyor almost full!\nClear it or fail!",
                        highlightTarget = string.Empty,
                        requireAction = ACTION_TAP_ANYWHERE,
                        isComplete = false,
                        useTextTap = true
                    }
                }
            };
        }

        private IEnumerator StartTutorialAfterLoad(int levelId)
        {
            while (LevelManager.HasInstance && LevelManager.Instance.IsLoading) yield return null;
            while (UIManager.HasInstance && UIManager.Instance.IsFading) yield return null;

            // [2026-05-31] 기믹/아이템 설명 팝업(PopupNewFeature)과 튜토리얼이 둘 다 OnLevelLoaded 에서
            //   발화돼 동시에 뜨던 문제 수정: 설명 팝업을 '먼저' 보여주고, 모두 닫힌 뒤에 튜토리얼 시작.
            //   yield 1회 보장 — OnLevelLoaded 동기 dispatch 가 끝나 NewFeatureManager 가 _isShowingPopup 를
            //   설정한 뒤 검사하도록 (이벤트 구독 순서와 무관하게 안전, no-loading 진입 케이스 포함).
            yield return null;
            while (NewFeatureManager.HasInstance && NewFeatureManager.Instance.IsShowingPopup)
                yield return null;

            // ROLLBACK_TUTORIAL_WAIT_UNLOCK_FX_20260622: 부스터 언락 Claim 후 아이콘이 HUD 하단으로 날아가 펄스하는
            //   보상 연출이 진행 중이면 끝날 때까지 대기 → "아이템 HUD 추가 + 연출 종료 → 튜토리얼" 순서 보장.
            //   언락이 없으면 즉시 통과(no-op). 롤백: 이 while 블록 삭제.
            {
                UIHud hud = UnityEngine.Object.FindAnyObjectByType<UIHud>();
                while (hud != null && hud.IsBoosterRewardFxPlaying)
                    yield return null;
            }

            Debug.Log($"[TutorialDbg] HandleLevelLoaded levelId={levelId}");

            // ROLLBACK_TUTORIAL_EDITOR_PRIORITY_20260622:
            // Use Tutorial Editor/TutorialCatalog metadata first. The legacy block below
            // is kept as rollback context but skipped by this resolved flow.
            {
                TutorialConfig resolvedConfig = ResolveTutorialConfigForLevel(levelId, out string source);
                Debug.Log($"[TutorialDbg] Resolved config for level {levelId}: source={source} tutorialId={(resolvedConfig != null ? resolvedConfig.tutorialId.ToString() : "NONE")}");
                if (resolvedConfig == null) yield break;

                bool complete = IsTutorialComplete(resolvedConfig.tutorialId);
                Debug.Log($"[TutorialDbg] tutorialId={resolvedConfig.tutorialId} alreadyComplete={complete} manualTriggerOnly={resolvedConfig.manualTriggerOnly} waitForItemDescription={resolvedConfig.waitForItemDescription}");
                if (complete) yield break;

                if (resolvedConfig.manualTriggerOnly)
                {
                    Debug.Log("[TutorialDbg] manualTriggerOnly - auto start deferred");
                    yield break;
                }

                if (resolvedConfig.waitForItemDescription) yield return WaitForItemDescriptionClosed();
                StartTutorial(resolvedConfig.tutorialId);
                yield break;
            }

            // 1) LevelConfig에 tutorialSteps가 있으면 우선 사용
#if false // ROLLBACK_TUTORIAL_LEGACY_LEVELDATA_PRIORITY_20260622
            TutorialConfig configFromData = TryBuildFromLevelData(levelId);
            if (configFromData != null)
            {
                bool complete1 = IsTutorialComplete(configFromData.tutorialId);
                Debug.Log($"[TutorialDbg] LevelData config found: tutorialId={configFromData.tutorialId} complete={complete1}");
                if (complete1) yield break;
                _configByLevel[configFromData.levelId] = configFromData;
                // ROLLBACK_TUTORIAL_MANUAL_TRIGGER_20260622: 수동 트리거 전용이면 자동 시작 안 함(외부 StartTutorialForLevel 대기).
                if (configFromData.manualTriggerOnly) { Debug.Log("[TutorialDbg] manualTriggerOnly — 자동 시작 보류"); yield break; }
                // ROLLBACK_POPUP_ITEM_DESC_TUTORIAL_GATE_20260622: PopupItemDescription 닫힐 때까지 대기 후 시작.
                if (configFromData.waitForItemDescription) yield return WaitForItemDescriptionClosed();
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

            // ROLLBACK_TUTORIAL_MANUAL_TRIGGER_20260622: 수동 트리거 전용이면 자동 시작 안 함.
            if (config.manualTriggerOnly) { Debug.Log("[TutorialDbg] manualTriggerOnly — 자동 시작 보류"); yield break; }

            // ROLLBACK_POPUP_ITEM_DESC_TUTORIAL_GATE_20260622: PopupItemDescription 닫힐 때까지 대기 후 시작.
            if (config.waitForItemDescription) yield return WaitForItemDescriptionClosed();

            StartTutorial(config.tutorialId);
#endif
        }

        /// <summary>ROLLBACK_POPUP_ITEM_DESC_TUTORIAL_GATE_20260622: PopupItemDescription(아이템 설명 팝업)이
        ///   ButtonSingle/Exit 로 닫힐 때까지 대기. 같은 OnLevelLoaded 프레임에 팝업이 뜨도록 1프레임 양보 후 검사.
        ///   팝업이 아예 안 뜨면 IsShowing=false 라 즉시 통과(no-op). 타임아웃은 IsShowing 이 비정상적으로
        ///   고착될 경우의 안전 백스톱(모달이라 실제론 유저 클릭으로 곧 닫힘).</summary>
        private IEnumerator WaitForItemDescriptionClosed()
        {
            const float ITEM_DESC_WAIT_TIMEOUT = 120f;
            yield return null; // 팝업이 OnLevelLoaded 동기 dispatch 에서 Show→IsShowing=true 하도록 한 프레임 양보.
            float deadline = Time.unscaledTime + ITEM_DESC_WAIT_TIMEOUT;
            while (PopupItemDescription.IsShowing && Time.unscaledTime < deadline)
                yield return null;
        }

        /// <summary>ROLLBACK_TUTORIAL_MANUAL_TRIGGER_20260622: 특정 레벨의 튜토리얼을 '명시적으로' 시작.
        ///   manualTriggerOnly 여부와 무관하게 시작한다(예: 아이템 언락 Claim → 보상연출 종료 콜백에서 호출).
        ///   우선순위는 자동 흐름과 동일(LevelData override → Catalog/하드코딩). 이미 완료/진행 중이면 무시.</summary>
        /// <returns>실제로 시작했으면 true.</returns>
        public bool StartTutorialForLevel(int levelId)
        {
            if (_isTutorialActive) return false;

            // ROLLBACK_TUTORIAL_EDITOR_PRIORITY_20260622:
            // Manual item-unlock starts must use the same Tutorial Editor/TutorialCatalog
            // resolution as the auto-load path, including waitForItemDescription.
            {
                TutorialConfig resolvedConfig = ResolveTutorialConfigForLevel(levelId, out string source);
                if (resolvedConfig == null)
                {
                    Debug.Log($"[TutorialDbg] StartTutorialForLevel({levelId}) - config not found");
                    return false;
                }

                if (IsTutorialComplete(resolvedConfig.tutorialId)) return false;

                Debug.Log($"[TutorialDbg] StartTutorialForLevel({levelId}) source={source} tutorialId={resolvedConfig.tutorialId} waitForItemDescription={resolvedConfig.waitForItemDescription}");
                if (_startTutorialForLevelCoroutine != null)
                    StopCoroutine(_startTutorialForLevelCoroutine);
                _startTutorialForLevelCoroutine = StartCoroutine(StartTutorialForLevelDeferred(resolvedConfig));
                return true;
            }

#if false // ROLLBACK_TUTORIAL_LEGACY_MANUAL_LEVELDATA_PRIORITY_20260622
            TutorialConfig config = TryBuildFromLevelData(levelId);
            if (config != null)
            {
                _configByLevel[config.levelId] = config;
            }
            else if (!_configByLevel.TryGetValue(levelId, out config))
            {
                Debug.Log($"[TutorialDbg] StartTutorialForLevel({levelId}) — config 없음");
                return false;
            }

            if (IsTutorialComplete(config.tutorialId)) return false;

            StartTutorial(config.tutorialId);
            return true;
#endif
        }

        private IEnumerator StartTutorialForLevelDeferred(TutorialConfig config)
        {
            // ROLLBACK_TUTORIAL_MANUAL_WAIT_ITEM_DESC_20260622:
            // Manual starts must also honor Tutorial Editor's waitForItemDescription flag,
            // and should never overlap PopupItemDescription if it is still open.
            if (config.waitForItemDescription || PopupItemDescription.IsShowing)
                yield return WaitForItemDescriptionClosed();

            _startTutorialForLevelCoroutine = null;
            if (_isTutorialActive || IsTutorialComplete(config.tutorialId)) yield break;
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
