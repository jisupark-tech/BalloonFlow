#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// EditorWindow for configuring tutorial steps.
    /// Left panel shows a list of tutorials; right panel shows editable steps.
    /// Tutorials can be loaded from / saved to TutorialController's BuildTutorialConfigs.
    /// </summary>
    public class TutorialEditorWindow : EditorWindow
    {
        #region Constants

        private static readonly string[] HIGHLIGHT_TARGET_OPTIONS =
        {
            "(none)",
            "holder_0", "holder_1", "holder_2", "holder_3", "holder_4",
            "holder_5", "holder_6", "holder_7", "holder_8", "holder_9",
            "board", "holder_queue",
            // ROLLBACK_TUTORIAL_ITEM_TARGET_20260622: UIHud 하단 아이템(부스터) 버튼 하이라이트 타겟.
            "item_hand", "item_shuffle", "item_remove",
            "gimmick_hidden", "gimmick_spawner", "gimmick_bigobject",
            "gimmick_chain", "gimmick_pin", "gimmick_lock_key",
            "gimmick_surprise", "gimmick_wall", "gimmick_ice",
            "gimmick_frozen_dart", "gimmick_color_curtain"
        };

        private static readonly string[] ACTION_OPTIONS =
        {
            "none", "tap_holder", "tap_item", "wait_pop", "tap_anywhere"
        };

        #endregion

        #region Nested Types

        [System.Serializable]
        private class EditableTutorial
        {
            public int tutorialId;
            public int levelId;
            public string name = "New Tutorial";
            // ROLLBACK_TUTORIAL_MANUAL_TRIGGER_20260622: true 면 레벨 진입 시 자동 시작 X — 외부(아이템 언락 Claim 등)에서 명시 호출 시에만 시작.
            public bool manualTriggerOnly;
            // ROLLBACK_POPUP_ITEM_DESC_TUTORIAL_GATE_20260622: true 면 PopupItemDescription 이 닫힐 때까지(ButtonSingle 클릭) 튜토리얼 시작 보류.
            public bool waitForItemDescription;
            public List<EditableStep> steps = new List<EditableStep>();
            public bool isExpanded = true;
        }

        [System.Serializable]
        private class EditableStep
        {
            public string instruction = "Tap here!";
            public string highlightTarget = "(none)";
            public string requireAction = "none";
            // ROLLBACK_TUTORIAL_HIDE_SKIP_ON_ITEM_USE_20260622: 튜토리얼 통한 아이템 사용 강제 스텝에서 Skip(X) 숨김.
            public bool hideSkipButton;
            public bool overrideVisualLayout;
            public bool useCutoutFrame = true;
            public Vector2 cutoutFramePosition = Vector2.zero;
            public Vector2 cutoutSize = new Vector2(200, 200);
            public Sprite cutoutFrameSprite;
            public Vector2 instructionPanelPosition = new Vector2(0f, 40f);
            public Vector2 instructionPanelSize = new Vector2(-60f, 200f);
            // [2026-05-13] Arrow on/off toggle — 기본 true.
            public bool useArrowIndicator = true;
            public Vector2 arrowIndicatorPosition = Vector2.zero;
            public bool useHandIndicator;
            public Vector2 handIndicatorPosition = Vector2.zero;
            public Sprite handIndicatorSprite;
            public TutorialHandTweenType handTweenType;
            public Vector2 handTweenMoveOffset = new Vector2(0f, -30f);
            public float handTweenScale = 1.12f;
            public float handTweenRotation;
            public float handTweenDuration = 0.55f;
            public Sprite cutoutMaskSprite;
            // ROLLBACK_TUTORIAL_INSTRUCTION_COLOR_20260622: instruction 텍스트 색상 override.
            public bool useInstructionColor;
            public Color instructionColor = Color.white;
        }

        #endregion

        #region State

        private Vector2 _scrollPosLeft;
        private Vector2 _scrollPosRight;
        private int _selectedTutorial = -1;
        private List<EditableTutorial> _tutorials = new List<EditableTutorial>();

        #endregion

        #region Menu

        [MenuItem("BalloonFlow/Tutorial Editor", false, 60)]
        public static void ShowWindow()
        {
            GetWindow<TutorialEditorWindow>("Tutorial Editor").minSize = new Vector2(500, 600);
        }

        #endregion

        #region OnGUI

        private void OnGUI()
        {
            // Toolbar
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // [2026-05-14] Primary workflow — read/write directly to TutorialCatalog.asset.
            // 인게임 런타임이 이 SO 를 우선 로드 (TutorialController.BuildTutorialConfigs Priority 1).
            GUI.backgroundColor = new Color(0.55f, 0.85f, 0.55f);
            if (GUILayout.Button("💾 Save → Catalog", EditorStyles.toolbarButton, GUILayout.Width(140)))
                SaveToCatalog();
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("📂 Load Catalog", EditorStyles.toolbarButton, GUILayout.Width(110)))
                LoadFromCatalog();

            GUILayout.Space(12);

            // Legacy — 코드/JSON 백업. Catalog 도입 전 워크플로우 유지 (롤백 호환).
            if (GUILayout.Button("Load from Code", EditorStyles.toolbarButton, GUILayout.Width(110)))
                LoadFromTutorialController();
            if (GUILayout.Button("Save to Code (clipboard)", EditorStyles.toolbarButton, GUILayout.Width(165)))
                SaveToTutorialController();
            if (GUILayout.Button("JSON Save", EditorStyles.toolbarButton, GUILayout.Width(90)))
                SaveToFile();
            if (GUILayout.Button("JSON Load", EditorStyles.toolbarButton, GUILayout.Width(90)))
                LoadFromFile();

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ New Tutorial", EditorStyles.toolbarButton, GUILayout.Width(110)))
                AddNewTutorial();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            // Left panel: tutorial list
            EditorGUILayout.BeginVertical(GUILayout.Width(220));
            DrawTutorialList();
            EditorGUILayout.EndVertical();

            // Separator
            GUILayout.Box("", GUILayout.Width(2), GUILayout.ExpandHeight(true));

            // Right panel: step editor
            EditorGUILayout.BeginVertical();
            DrawStepEditor();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Left Panel — Tutorial List

        private void DrawTutorialList()
        {
            EditorGUILayout.LabelField("Tutorials", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            _scrollPosLeft = EditorGUILayout.BeginScrollView(_scrollPosLeft);

            for (int i = 0; i < _tutorials.Count; i++)
            {
                var tut = _tutorials[i];
                bool isSelected = (_selectedTutorial == i);

                // Draw selectable row
                var style = isSelected ? "selectionRect" : "box";
                EditorGUILayout.BeginHorizontal(style);

                if (GUILayout.Button($"ID:{tut.tutorialId}  Lv:{tut.levelId}\n{tut.name}",
                    EditorStyles.wordWrappedLabel, GUILayout.Height(38)))
                {
                    _selectedTutorial = i;
                    GUI.FocusControl(null);
                }

                // Delete button
                GUI.color = new Color(1f, 0.5f, 0.5f);
                if (GUILayout.Button("X", GUILayout.Width(22), GUILayout.Height(38)))
                {
                    if (EditorUtility.DisplayDialog("Delete Tutorial",
                        $"Delete tutorial '{tut.name}' (ID:{tut.tutorialId})?", "Delete", "Cancel"))
                    {
                        _tutorials.RemoveAt(i);
                        if (_selectedTutorial >= _tutorials.Count)
                            _selectedTutorial = _tutorials.Count - 1;
                        GUIUtility.ExitGUI();
                    }
                }
                GUI.color = Color.white;

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            if (_tutorials.Count == 0)
            {
                EditorGUILayout.HelpBox("No tutorials loaded. Click 'Load from Code' or '+ New Tutorial'.",
                    MessageType.Info);
            }
        }

        #endregion

        #region Right Panel — Step Editor

        private void DrawStepEditor()
        {
            if (_selectedTutorial < 0 || _selectedTutorial >= _tutorials.Count)
            {
                EditorGUILayout.LabelField("Select a tutorial from the list.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var tut = _tutorials[_selectedTutorial];

            EditorGUILayout.LabelField("Tutorial Properties", EditorStyles.boldLabel);

            tut.tutorialId = EditorGUILayout.IntField("Tutorial ID", tut.tutorialId);
            tut.levelId = EditorGUILayout.IntField("Level ID", tut.levelId);
            tut.name = EditorGUILayout.TextField("Name", tut.name);
            // ROLLBACK_TUTORIAL_MANUAL_TRIGGER_20260622: ON 이면 레벨 진입 시 자동 시작하지 않고,
            //   아이템 언락 Claim → 보상연출 종료 후처럼 외부 호출(StartTutorialForLevel)로만 시작.
            tut.manualTriggerOnly = EditorGUILayout.Toggle(
                new GUIContent("Manual Trigger Only", "ON: 레벨 진입 시 자동 시작 안 함. 아이템 언락 Claim 등 외부 트리거로만 시작."),
                tut.manualTriggerOnly);
            // ROLLBACK_UNLOCK_POPUP_TO_BUYITEM_20260623: 해금 팝업 PopupBuyItem 일원화 — 라벨만 변경(필드명 waitForItemDescription 유지).
            tut.waitForItemDescription = EditorGUILayout.Toggle(
                new GUIContent("Wait For PopupBuyItem (Unlock)", "ON: 아이템 해금 팝업(PopupBuyItem)이 떠 있으면 Claim 으로 닫힌 뒤에 튜토리얼 시작(동시 노출 방지)."),
                tut.waitForItemDescription);

            EditorGUILayout.Space(8);

            // Steps header
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Steps ({tut.steps.Count})", EditorStyles.boldLabel);
            if (GUILayout.Button("+ Add Step", GUILayout.Width(90)))
            {
                tut.steps.Add(new EditableStep());
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            _scrollPosRight = EditorGUILayout.BeginScrollView(_scrollPosRight);

            int removeIndex = -1;
            int moveUpIndex = -1;
            int moveDownIndex = -1;

            for (int i = 0; i < tut.steps.Count; i++)
            {
                var step = tut.steps[i];

                EditorGUILayout.BeginVertical("box");

                // Step header with controls
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Step {i}", EditorStyles.miniBoldLabel, GUILayout.Width(60));
                GUILayout.FlexibleSpace();

                GUI.enabled = i > 0;
                if (GUILayout.Button("\u25B2", GUILayout.Width(24))) moveUpIndex = i;
                GUI.enabled = i < tut.steps.Count - 1;
                if (GUILayout.Button("\u25BC", GUILayout.Width(24))) moveDownIndex = i;
                GUI.enabled = true;

                GUI.color = new Color(1f, 0.5f, 0.5f);
                if (GUILayout.Button("X", GUILayout.Width(22))) removeIndex = i;
                GUI.color = Color.white;

                EditorGUILayout.EndHorizontal();

                // Instruction text (multi-line)
                EditorGUILayout.LabelField("Instruction:");
                step.instruction = EditorGUILayout.TextArea(step.instruction, GUILayout.Height(40));

                // ROLLBACK_TUTORIAL_INSTRUCTION_COLOR_20260622: instruction 텍스트 색상.
                //   체크 시 그 색 적용, 해제 시 프리팹 기본색 사용.
                step.useInstructionColor = EditorGUILayout.Toggle(
                    new GUIContent("Use Text Color", "ON: 아래 색을 instruction 텍스트에 적용. OFF: 프리팹 기본색."),
                    step.useInstructionColor);
                using (new EditorGUI.DisabledScope(!step.useInstructionColor))
                    step.instructionColor = EditorGUILayout.ColorField("Text Color", step.instructionColor);

                // Highlight target popup
                int targetIdx = System.Array.IndexOf(HIGHLIGHT_TARGET_OPTIONS, step.highlightTarget);
                if (targetIdx < 0) targetIdx = 0;
                targetIdx = EditorGUILayout.Popup("Highlight Target", targetIdx, HIGHLIGHT_TARGET_OPTIONS);
                step.highlightTarget = HIGHLIGHT_TARGET_OPTIONS[targetIdx];

                // Require action popup
                int actionIdx = System.Array.IndexOf(ACTION_OPTIONS, step.requireAction);
                if (actionIdx < 0) actionIdx = 0;
                actionIdx = EditorGUILayout.Popup("Require Action", actionIdx, ACTION_OPTIONS);
                step.requireAction = ACTION_OPTIONS[actionIdx];

                // ROLLBACK_TUTORIAL_HIDE_SKIP_ON_ITEM_USE_20260622: 스텝 동안 Skip(X) 숨김 + PopupUseItem 의 X(취소) 버튼도 숨김.
                //   강제 아이템 사용 스텝(아이템 해금 튜토리얼 등)에서 사용하지 않고 빠져나가지 못하게 한다.
                step.hideSkipButton = EditorGUILayout.Toggle(
                    new GUIContent("Hide Skip/UseItem X", "ON: 튜토리얼 Skip(X) + UseItem 팝업의 X(취소) 버튼을 함께 숨김. 강제 아이템 사용 스텝용."),
                    step.hideSkipButton);

                step.overrideVisualLayout = EditorGUILayout.Toggle("Override Visual Layout", step.overrideVisualLayout);
                using (new EditorGUI.DisabledScope(!step.overrideVisualLayout))
                {
                    EditorGUILayout.LabelField("Cutout Frame", EditorStyles.miniBoldLabel);
                    step.useCutoutFrame = EditorGUILayout.Toggle("Use Cutout Frame", step.useCutoutFrame);
                    step.cutoutFramePosition = EditorGUILayout.Vector2Field("Position", step.cutoutFramePosition);
                    step.cutoutSize = EditorGUILayout.Vector2Field("Size", step.cutoutSize);
                    step.cutoutFrameSprite = (Sprite)EditorGUILayout.ObjectField("Frame Sprite", step.cutoutFrameSprite, typeof(Sprite), false);

                    EditorGUILayout.LabelField("Instruction Panel", EditorStyles.miniBoldLabel);
                    step.instructionPanelPosition = EditorGUILayout.Vector2Field("Position", step.instructionPanelPosition);
                    step.instructionPanelSize = EditorGUILayout.Vector2Field("Size", step.instructionPanelSize);

                    EditorGUILayout.LabelField("Indicators", EditorStyles.miniBoldLabel);
                    // [2026-05-13] Arrow on/off toggle.
                    step.useArrowIndicator = EditorGUILayout.Toggle("Use Arrow Indicator", step.useArrowIndicator);
                    using (new EditorGUI.DisabledScope(!step.useArrowIndicator))
                    {
                        step.arrowIndicatorPosition = EditorGUILayout.Vector2Field("Arrow Position", step.arrowIndicatorPosition);
                    }
                    step.useHandIndicator = EditorGUILayout.Toggle("Use Hand Indicator", step.useHandIndicator);
                    step.handIndicatorPosition = EditorGUILayout.Vector2Field("Hand Position", step.handIndicatorPosition);
                    step.handIndicatorSprite = (Sprite)EditorGUILayout.ObjectField("Hand Sprite", step.handIndicatorSprite, typeof(Sprite), false);
                    step.handTweenType = (TutorialHandTweenType)EditorGUILayout.EnumPopup("Hand Tween", step.handTweenType);
                    using (new EditorGUI.DisabledScope(step.handTweenType == TutorialHandTweenType.None))
                    {
                        step.handTweenMoveOffset = EditorGUILayout.Vector2Field("Move Offset", step.handTweenMoveOffset);
                        step.handTweenScale = EditorGUILayout.FloatField("Scale Multiplier", step.handTweenScale);
                        step.handTweenRotation = EditorGUILayout.FloatField("Rotation Delta", step.handTweenRotation);
                        step.handTweenDuration = EditorGUILayout.FloatField("Tween Duration", step.handTweenDuration);
                    }
                    step.cutoutMaskSprite = (Sprite)EditorGUILayout.ObjectField("Cutout Mask Sprite", step.cutoutMaskSprite, typeof(Sprite), false);
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndScrollView();

            // Apply deferred modifications
            if (removeIndex >= 0)
            {
                tut.steps.RemoveAt(removeIndex);
            }
            if (moveUpIndex > 0)
            {
                var temp = tut.steps[moveUpIndex];
                tut.steps[moveUpIndex] = tut.steps[moveUpIndex - 1];
                tut.steps[moveUpIndex - 1] = temp;
            }
            if (moveDownIndex >= 0 && moveDownIndex < tut.steps.Count - 1)
            {
                var temp = tut.steps[moveDownIndex];
                tut.steps[moveDownIndex] = tut.steps[moveDownIndex + 1];
                tut.steps[moveDownIndex + 1] = temp;
            }
        }

        #endregion

        #region Load / Save

        private void LoadFromTutorialController()
        {
            _tutorials.Clear();
            _selectedTutorial = -1;

            // Try to find TutorialController in scene
            var controller = FindFirstObjectByType<TutorialController>();
            if (controller == null)
            {
                // Build defaults from known config
                LoadDefaultConfigs();
                Debug.Log("[TutorialEditorWindow] No TutorialController in scene. Loaded default configs.");
                return;
            }

            // Use reflection or known structure to read configs
            // Since BuildTutorialConfigs is private and configs are in a dictionary,
            // we'll load from the known defaults for now
            LoadDefaultConfigs();
            Debug.Log("[TutorialEditorWindow] Loaded tutorial configs from defaults.");
        }

        private void LoadDefaultConfigs()
        {
            _tutorials = new List<EditableTutorial>
            {
                CreateEditableTutorial(1, 1, "Tap a holder to deploy", new[]
                {
                    new EditableStep { instruction = "Tap a holder to deploy its darts!", highlightTarget = "holder_0", requireAction = "none", cutoutSize = new Vector2(200, 200) },
                    new EditableStep { instruction = "Well done! Watch the darts fly.", highlightTarget = "board", requireAction = "wait_pop", cutoutSize = new Vector2(800, 600) },
                    new EditableStep { instruction = "Pop all the balloons to clear the level!", highlightTarget = "(none)", requireAction = "none", cutoutSize = new Vector2(200, 200) },
                }),
                CreateEditableTutorial(2, 2, "Match colors", new[]
                {
                    new EditableStep { instruction = "Darts only pop balloons of the same color!", highlightTarget = "holder_0", requireAction = "none", cutoutSize = new Vector2(200, 200) },
                    new EditableStep { instruction = "Tap the holder that matches the balloon colors.", highlightTarget = "holder_0", requireAction = "tap_holder", cutoutSize = new Vector2(200, 200) },
                    new EditableStep { instruction = "Great! Now try the other holder.", highlightTarget = "holder_1", requireAction = "tap_holder", cutoutSize = new Vector2(200, 200) },
                }),
                CreateEditableTutorial(3, 3, "Multiple holders", new[]
                {
                    new EditableStep { instruction = "Three colors now! Match each holder to its balloons.", highlightTarget = "board", requireAction = "none", cutoutSize = new Vector2(800, 600) },
                    new EditableStep { instruction = "Tap the red holder to clear red balloons.", highlightTarget = "holder_0", requireAction = "tap_holder", cutoutSize = new Vector2(200, 200) },
                    new EditableStep { instruction = "Now pick the best holder to clear the board!", highlightTarget = "(none)", requireAction = "none", cutoutSize = new Vector2(200, 200) },
                }),
                CreateEditableTutorial(4, 4, "Watch the overflow", new[]
                {
                    new EditableStep { instruction = "Watch out! If too many holders pile up you'll fail.", highlightTarget = "holder_queue", requireAction = "none", cutoutSize = new Vector2(800, 400) },
                    new EditableStep { instruction = "Keep the holder queue short \u2014 tap holders quickly!", highlightTarget = "holder_0", requireAction = "tap_holder", cutoutSize = new Vector2(200, 200) },
                    new EditableStep { instruction = "Keep tapping before the queue overflows!", highlightTarget = "(none)", requireAction = "none", cutoutSize = new Vector2(200, 200) },
                }),
                CreateEditableTutorial(5, 5, "Choose wisely", new[]
                {
                    new EditableStep { instruction = "Four colors! Think before you tap.", highlightTarget = "board", requireAction = "none", cutoutSize = new Vector2(800, 600) },
                    new EditableStep { instruction = "Pick the holder with the most matching balloons first.", highlightTarget = "(none)", requireAction = "tap_holder", cutoutSize = new Vector2(200, 200) },
                    new EditableStep { instruction = "Strategy matters \u2014 clear the board for 3 stars!", highlightTarget = "(none)", requireAction = "none", cutoutSize = new Vector2(200, 200) },
                }),
                CreateEditableTutorial(11, 11, "Hidden Balloon", new[]
                {
                    new EditableStep { instruction = "Some balloons are hidden! Pop nearby balloons to reveal them.", highlightTarget = "gimmick_hidden", requireAction = "none", cutoutSize = new Vector2(400, 400) },
                    new EditableStep { instruction = "Clear the visible balloons first to uncover hidden ones.", highlightTarget = "(none)", requireAction = "wait_pop", cutoutSize = new Vector2(200, 200) },
                }),
                CreateEditableTutorial(21, 21, "Balloon Spawner", new[]
                {
                    new EditableStep { instruction = "A spawner keeps producing balloons! Destroy it fast.", highlightTarget = "gimmick_spawner", requireAction = "none", cutoutSize = new Vector2(400, 400) },
                    new EditableStep { instruction = "Target the spawner balloon directly to stop it!", highlightTarget = "gimmick_spawner", requireAction = "wait_pop", cutoutSize = new Vector2(400, 400) },
                }),
                CreateEditableTutorial(31, 31, "Big Object", new[]
                {
                    new EditableStep { instruction = "A giant balloon needs multiple hits to pop!", highlightTarget = "gimmick_bigobject", requireAction = "none", cutoutSize = new Vector2(400, 400) },
                    new EditableStep { instruction = "Keep sending matching darts until the big one bursts!", highlightTarget = "gimmick_bigobject", requireAction = "wait_pop", cutoutSize = new Vector2(400, 400) },
                }),
                CreateEditableTutorial(41, 41, "Chain Reaction", new[]
                {
                    new EditableStep { instruction = "Chain balloons explode together when one is popped!", highlightTarget = "gimmick_chain", requireAction = "none", cutoutSize = new Vector2(400, 400) },
                    new EditableStep { instruction = "Pop one chain balloon to clear the whole group at once!", highlightTarget = "(none)", requireAction = "wait_pop", cutoutSize = new Vector2(200, 200) },
                }),
                CreateEditableTutorial(61, 61, "Combo Bonus", new[]
                {
                    new EditableStep { instruction = "Pop balloons in quick succession to build a combo!", highlightTarget = "board", requireAction = "none", cutoutSize = new Vector2(800, 600) },
                    new EditableStep { instruction = "Higher combos mean bonus score \u2014 go for 3 stars!", highlightTarget = "(none)", requireAction = "none", cutoutSize = new Vector2(200, 200) },
                }),
            };

            if (_tutorials.Count > 0)
                _selectedTutorial = 0;
        }

        private EditableTutorial CreateEditableTutorial(int tutorialId, int levelId, string name, EditableStep[] steps)
        {
            return new EditableTutorial
            {
                tutorialId = tutorialId,
                levelId = levelId,
                name = name,
                steps = new List<EditableStep>(steps)
            };
        }

        private void SaveToTutorialController()
        {
            // Generate the C# code for BuildTutorialConfigs and copy to clipboard
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("// ===== AUTO-GENERATED by TutorialEditorWindow =====");
            sb.AppendLine("// Paste this inside TutorialController.BuildTutorialConfigs()");
            sb.AppendLine("_configByLevel.Clear();");
            sb.AppendLine();

            foreach (var tut in _tutorials)
            {
                sb.AppendLine($"RegisterConfig(new TutorialConfig");
                sb.AppendLine("{");
                sb.AppendLine($"    tutorialId = {tut.tutorialId},");
                sb.AppendLine($"    levelId = {tut.levelId},");
                sb.AppendLine($"    tutorialName = \"{EscapeString(tut.name)}\",");
                sb.AppendLine($"    manualTriggerOnly = {tut.manualTriggerOnly.ToString().ToLowerInvariant()},"); // ROLLBACK_TUTORIAL_MANUAL_TRIGGER_20260622
                sb.AppendLine($"    waitForItemDescription = {tut.waitForItemDescription.ToString().ToLowerInvariant()},"); // ROLLBACK_POPUP_ITEM_DESC_TUTORIAL_GATE_20260622
                sb.AppendLine("    steps = new TutorialStep[]");
                sb.AppendLine("    {");

                for (int i = 0; i < tut.steps.Count; i++)
                {
                    var step = tut.steps[i];
                    string target = step.highlightTarget == "(none)" ? "string.Empty" : $"\"{EscapeString(step.highlightTarget)}\"";
                    string action = $"\"{step.requireAction}\"";

                    sb.AppendLine("        new TutorialStep");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            stepIndex = {i},");
                    sb.AppendLine($"            instruction = \"{EscapeString(step.instruction)}\",");
                    sb.AppendLine($"            highlightTarget = {target},");
                    sb.AppendLine($"            requireAction = {action},");
                    sb.AppendLine($"            hideSkipButton = {step.hideSkipButton.ToString().ToLowerInvariant()},"); // ROLLBACK_TUTORIAL_HIDE_SKIP_ON_ITEM_USE_20260622
                    // ROLLBACK_TUTORIAL_INSTRUCTION_COLOR_20260622: instruction 텍스트 색상.
                    sb.AppendLine($"            useInstructionColor = {step.useInstructionColor.ToString().ToLowerInvariant()},");
                    sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "            instructionColor = new Color({0}f, {1}f, {2}f, {3}f),",
                        step.instructionColor.r, step.instructionColor.g, step.instructionColor.b, step.instructionColor.a));
                    sb.AppendLine("            isComplete = false,");
                    sb.AppendLine($"            overrideVisualLayout = {step.overrideVisualLayout.ToString().ToLowerInvariant()},");
                    sb.AppendLine($"            useCutoutFrame = {step.useCutoutFrame.ToString().ToLowerInvariant()},");
                    sb.AppendLine($"            cutoutFramePosition = {Vector2Code(step.cutoutFramePosition)},");
                    sb.AppendLine($"            cutoutFrameSize = {Vector2Code(step.cutoutSize)},");
                    sb.AppendLine($"            cutoutFrameSprite = {SpriteCode(step.cutoutFrameSprite)},");
                    sb.AppendLine($"            instructionPanelPosition = {Vector2Code(step.instructionPanelPosition)},");
                    sb.AppendLine($"            instructionPanelSize = {Vector2Code(step.instructionPanelSize)},");
                    sb.AppendLine($"            useArrowIndicator = {step.useArrowIndicator.ToString().ToLowerInvariant()},");
                    sb.AppendLine($"            arrowIndicatorPosition = {Vector2Code(step.arrowIndicatorPosition)},");
                    sb.AppendLine($"            useHandIndicator = {step.useHandIndicator.ToString().ToLowerInvariant()},");
                    sb.AppendLine($"            handIndicatorPosition = {Vector2Code(step.handIndicatorPosition)},");
                    sb.AppendLine($"            handIndicatorSprite = {SpriteCode(step.handIndicatorSprite)},");
                    sb.AppendLine($"            handTweenType = TutorialHandTweenType.{step.handTweenType},");
                    sb.AppendLine($"            handTweenMoveOffset = {Vector2Code(step.handTweenMoveOffset)},");
                    sb.AppendLine($"            handTweenScale = {FloatCode(step.handTweenScale)},");
                    sb.AppendLine($"            handTweenRotation = {FloatCode(step.handTweenRotation)},");
                    sb.AppendLine($"            handTweenDuration = {FloatCode(step.handTweenDuration)},");
                    sb.AppendLine($"            cutoutMaskSprite = {SpriteCode(step.cutoutMaskSprite)}");
                    sb.AppendLine("        },");
                }

                sb.AppendLine("    }");
                sb.AppendLine("});");
                sb.AppendLine();
            }

            sb.AppendLine("// ===== END AUTO-GENERATED =====");

            EditorGUIUtility.systemCopyBuffer = sb.ToString();
            Debug.Log($"[TutorialEditorWindow] Generated code for {_tutorials.Count} tutorials copied to clipboard. Paste into BuildTutorialConfigs().");
            EditorUtility.DisplayDialog("Save to Code",
                $"Generated code for {_tutorials.Count} tutorials has been copied to your clipboard.\n\n" +
                "Paste the code inside TutorialController.BuildTutorialConfigs() to apply changes.",
                "OK");
        }

        // [2026-05-14] TutorialCatalog SO 직접 read/write — 인게임 런타임 primary source.
        //   TutorialController.BuildTutorialConfigs() 가 Priority 1 으로 이 asset 로드.
        //   Resources 폴더에 위치해야 Resources.Load 로 런타임 접근 가능.
        private const string CATALOG_ASSET_PATH = "Assets/Resources/TutorialCatalog.asset";

        private static TutorialCatalog FindOrCreateCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<TutorialCatalog>(CATALOG_ASSET_PATH);
            if (catalog != null) return catalog;

            string dir = System.IO.Path.GetDirectoryName(CATALOG_ASSET_PATH);
            if (!AssetDatabase.IsValidFolder(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            catalog = ScriptableObject.CreateInstance<TutorialCatalog>();
            AssetDatabase.CreateAsset(catalog, CATALOG_ASSET_PATH);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[TutorialEditorWindow] Created new TutorialCatalog at {CATALOG_ASSET_PATH}");
            return catalog;
        }

        private void SaveToCatalog()
        {
            var catalog = FindOrCreateCatalog();
            Undo.RecordObject(catalog, "Save Tutorial Catalog");

            var list = catalog.GetMutableList();
            list.Clear();

            for (int t = 0; t < _tutorials.Count; t++)
            {
                var src = _tutorials[t];
                var config = new TutorialConfig
                {
                    tutorialId = src.tutorialId,
                    levelId = src.levelId,
                    tutorialName = src.name,
                    manualTriggerOnly = src.manualTriggerOnly, // ROLLBACK_TUTORIAL_MANUAL_TRIGGER_20260622
                    waitForItemDescription = src.waitForItemDescription, // ROLLBACK_POPUP_ITEM_DESC_TUTORIAL_GATE_20260622
                    steps = new TutorialStep[src.steps.Count]
                };
                for (int i = 0; i < src.steps.Count; i++)
                {
                    var s = src.steps[i];
                    config.steps[i] = new TutorialStep
                    {
                        stepIndex = i,
                        instruction = s.instruction,
                        highlightTarget = s.highlightTarget == "(none)" ? string.Empty : s.highlightTarget,
                        requireAction = s.requireAction,
                        hideSkipButton = s.hideSkipButton, // ROLLBACK_TUTORIAL_HIDE_SKIP_ON_ITEM_USE_20260622
                        isComplete = false,
                        overrideVisualLayout = s.overrideVisualLayout,
                        useCutoutFrame = s.useCutoutFrame,
                        cutoutFramePosition = s.cutoutFramePosition,
                        cutoutFrameSize = s.cutoutSize,
                        cutoutFrameSprite = s.cutoutFrameSprite,
                        instructionPanelPosition = s.instructionPanelPosition,
                        instructionPanelSize = s.instructionPanelSize,
                        useArrowIndicator = s.useArrowIndicator,
                        arrowIndicatorPosition = s.arrowIndicatorPosition,
                        useHandIndicator = s.useHandIndicator,
                        handIndicatorPosition = s.handIndicatorPosition,
                        handIndicatorSprite = s.handIndicatorSprite,
                        handTweenType = s.handTweenType,
                        handTweenMoveOffset = s.handTweenMoveOffset,
                        handTweenScale = s.handTweenScale,
                        handTweenRotation = s.handTweenRotation,
                        handTweenDuration = s.handTweenDuration,
                        cutoutMaskSprite = s.cutoutMaskSprite,
                        useInstructionColor = s.useInstructionColor, // ROLLBACK_TUTORIAL_INSTRUCTION_COLOR_20260622
                        instructionColor = s.instructionColor
                    };
                }
                list.Add(config);
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[TutorialEditorWindow] Saved {list.Count} tutorials → {CATALOG_ASSET_PATH}");
            EditorUtility.DisplayDialog("Catalog 저장 완료",
                $"{list.Count}개 tutorial 을 TutorialCatalog.asset 에 저장했습니다.\n\n인게임 다음 레벨 진입 시 자동 반영됩니다.\n(Play 중이면 재시작 필요)",
                "OK");
        }

        private void LoadFromCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<TutorialCatalog>(CATALOG_ASSET_PATH);
            if (catalog == null)
            {
                EditorUtility.DisplayDialog("Catalog 없음",
                    $"{CATALOG_ASSET_PATH} 가 존재하지 않습니다.\n\n'Load from Code' 로 기본값을 불러온 뒤 'Save → Catalog' 를 누르면 자동 생성됩니다.",
                    "OK");
                return;
            }

            _tutorials.Clear();
            _selectedTutorial = -1;

            for (int t = 0; t < catalog.Tutorials.Count; t++)
            {
                var src = catalog.Tutorials[t];
                var edit = new EditableTutorial
                {
                    tutorialId = src.tutorialId,
                    levelId = src.levelId,
                    name = string.IsNullOrEmpty(src.tutorialName) ? "Untitled" : src.tutorialName,
                    manualTriggerOnly = src.manualTriggerOnly, // ROLLBACK_TUTORIAL_MANUAL_TRIGGER_20260622
                    waitForItemDescription = src.waitForItemDescription, // ROLLBACK_POPUP_ITEM_DESC_TUTORIAL_GATE_20260622
                    steps = new List<EditableStep>()
                };
                if (src.steps != null)
                {
                    for (int i = 0; i < src.steps.Length; i++)
                    {
                        var s = src.steps[i];
                        edit.steps.Add(new EditableStep
                        {
                            instruction = s.instruction ?? "",
                            highlightTarget = string.IsNullOrEmpty(s.highlightTarget) ? "(none)" : s.highlightTarget,
                            requireAction = string.IsNullOrEmpty(s.requireAction) ? "none" : s.requireAction,
                            hideSkipButton = s.hideSkipButton, // ROLLBACK_TUTORIAL_HIDE_SKIP_ON_ITEM_USE_20260622
                            overrideVisualLayout = s.overrideVisualLayout,
                            useCutoutFrame = s.useCutoutFrame,
                            cutoutFramePosition = s.cutoutFramePosition,
                            cutoutSize = s.cutoutFrameSize,
                            cutoutFrameSprite = s.cutoutFrameSprite,
                            instructionPanelPosition = s.instructionPanelPosition,
                            instructionPanelSize = s.instructionPanelSize,
                            useArrowIndicator = s.useArrowIndicator,
                            arrowIndicatorPosition = s.arrowIndicatorPosition,
                            useHandIndicator = s.useHandIndicator,
                            handIndicatorPosition = s.handIndicatorPosition,
                            handIndicatorSprite = s.handIndicatorSprite,
                            handTweenType = s.handTweenType,
                            handTweenMoveOffset = s.handTweenMoveOffset,
                            handTweenScale = s.handTweenScale,
                            handTweenRotation = s.handTweenRotation,
                            handTweenDuration = s.handTweenDuration,
                            cutoutMaskSprite = s.cutoutMaskSprite,
                            useInstructionColor = s.useInstructionColor, // ROLLBACK_TUTORIAL_INSTRUCTION_COLOR_20260622
                            instructionColor = s.instructionColor
                        });
                    }
                }
                _tutorials.Add(edit);
            }

            _selectedTutorial = _tutorials.Count > 0 ? 0 : -1;
            Repaint();
            Debug.Log($"[TutorialEditorWindow] Loaded {_tutorials.Count} tutorials from {CATALOG_ASSET_PATH}");
        }

        // [2026-05-13] JSON file 으로 editor 상태 저장/복원 — legacy backup.
        //   Sprite asset reference 는 GUID + sub-asset 명으로 직렬화 (Unity asset 시스템 외부 JSON 직접 처리 불가).
        private const string SAVE_FILE_PATH = "Assets/Editor/TutorialEditorState.json";

        [System.Serializable]
        private class SaveData
        {
            public List<EditableTutorial> tutorials = new List<EditableTutorial>();
            // Sprite 직렬화 보조 — EditableStep 1개 당 cutoutFrame/hand/cutoutMask 3개의 sprite path.
            // Index = tutorial * 1000 + step (단순 매핑 — tutorial 당 step 1000개 이하 가정).
            public List<string> spriteRefs = new List<string>();
        }

        private void SaveToFile()
        {
            var data = new SaveData();
            foreach (var t in _tutorials) data.tutorials.Add(t);
            foreach (var t in _tutorials)
                foreach (var s in t.steps)
                {
                    data.spriteRefs.Add(SpriteGuid(s.cutoutFrameSprite));
                    data.spriteRefs.Add(SpriteGuid(s.handIndicatorSprite));
                    data.spriteRefs.Add(SpriteGuid(s.cutoutMaskSprite));
                }

            string json = EditorJsonUtility.ToJson(data, prettyPrint: true);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SAVE_FILE_PATH));
            System.IO.File.WriteAllText(SAVE_FILE_PATH, json);
            AssetDatabase.Refresh();
            Debug.Log($"[TutorialEditorWindow] Saved {_tutorials.Count} tutorials → {SAVE_FILE_PATH}");
            EditorUtility.DisplayDialog("저장 완료", $"{_tutorials.Count}개 tutorial 을 {SAVE_FILE_PATH} 에 저장했습니다.", "OK");
        }

        private void LoadFromFile()
        {
            if (!System.IO.File.Exists(SAVE_FILE_PATH))
            {
                EditorUtility.DisplayDialog("불러오기 실패", $"{SAVE_FILE_PATH} 가 존재하지 않습니다.", "OK");
                return;
            }
            string json = System.IO.File.ReadAllText(SAVE_FILE_PATH);
            var data = new SaveData();
            EditorJsonUtility.FromJsonOverwrite(json, data);
            _tutorials = data.tutorials ?? new List<EditableTutorial>();

            // Sprite GUID → asset 복원
            int idx = 0;
            foreach (var t in _tutorials)
                foreach (var s in t.steps)
                {
                    if (idx + 2 < data.spriteRefs.Count)
                    {
                        s.cutoutFrameSprite   = LoadSpriteByGuid(data.spriteRefs[idx]);
                        s.handIndicatorSprite = LoadSpriteByGuid(data.spriteRefs[idx + 1]);
                        s.cutoutMaskSprite    = LoadSpriteByGuid(data.spriteRefs[idx + 2]);
                    }
                    idx += 3;
                }

            _selectedTutorial = _tutorials.Count > 0 ? 0 : -1;
            Debug.Log($"[TutorialEditorWindow] Loaded {_tutorials.Count} tutorials ← {SAVE_FILE_PATH}");
            Repaint();
        }

        private static string SpriteGuid(Sprite spr) => spr == null
            ? string.Empty
            : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(spr));

        private static Sprite LoadSpriteByGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static string EscapeString(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }

        private static string Vector2Code(Vector2 value)
        {
            return $"new Vector2({FloatCode(value.x)}, {FloatCode(value.y)})";
        }

        private static string FloatCode(float value)
        {
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "f";
        }

        private static string SpriteCode(Sprite sprite)
        {
            if (sprite == null) return "null";

            string assetPath = AssetDatabase.GetAssetPath(sprite);
            const string resourcesMarker = "/Resources/";
            int resourcesIndex = assetPath.IndexOf(resourcesMarker, System.StringComparison.OrdinalIgnoreCase);
            if (resourcesIndex < 0)
            {
                Debug.LogWarning($"[TutorialEditorWindow] Sprite '{sprite.name}' is not under a Resources folder. Generated code will keep it null.");
                return "null";
            }

            string resourcePath = assetPath.Substring(resourcesIndex + resourcesMarker.Length);
            resourcePath = System.IO.Path.ChangeExtension(resourcePath, null).Replace("\\", "/");
            return $"Resources.Load<Sprite>(\"{EscapeString(resourcePath)}\")";
        }

        #endregion

        #region Helpers

        private void AddNewTutorial()
        {
            int nextId = 1;
            int nextLevel = 1;
            foreach (var t in _tutorials)
            {
                if (t.tutorialId >= nextId) nextId = t.tutorialId + 1;
                if (t.levelId >= nextLevel) nextLevel = t.levelId + 1;
            }

            var newTut = new EditableTutorial
            {
                tutorialId = nextId,
                levelId = nextLevel,
                name = "New Tutorial",
                steps = new List<EditableStep>
                {
                    new EditableStep
                    {
                        instruction = "Tap here!",
                        highlightTarget = "holder_0",
                        requireAction = "none",
                        cutoutSize = new Vector2(200, 200)
                    }
                }
            };

            _tutorials.Add(newTut);
            _selectedTutorial = _tutorials.Count - 1;
        }

        #endregion
    }
}
#endif
