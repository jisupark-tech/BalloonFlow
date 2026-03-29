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
            "gimmick_hidden", "gimmick_spawner", "gimmick_bigobject",
            "gimmick_chain", "gimmick_pin", "gimmick_lock_key",
            "gimmick_surprise", "gimmick_wall", "gimmick_ice",
            "gimmick_frozen_dart", "gimmick_color_curtain"
        };

        private static readonly string[] ACTION_OPTIONS =
        {
            "none", "tap_holder", "wait_pop", "tap_anywhere"
        };

        #endregion

        #region Nested Types

        [System.Serializable]
        private class EditableTutorial
        {
            public int tutorialId;
            public int levelId;
            public string name = "New Tutorial";
            public List<EditableStep> steps = new List<EditableStep>();
            public bool isExpanded = true;
        }

        [System.Serializable]
        private class EditableStep
        {
            public string instruction = "Tap here!";
            public string highlightTarget = "(none)";
            public string requireAction = "none";
            public Vector2 cutoutSize = new Vector2(200, 200);
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
            if (GUILayout.Button("Load from Code", EditorStyles.toolbarButton, GUILayout.Width(110)))
                LoadFromTutorialController();
            if (GUILayout.Button("Save to Code", EditorStyles.toolbarButton, GUILayout.Width(100)))
                SaveToTutorialController();
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

                // Cutout size
                step.cutoutSize = EditorGUILayout.Vector2Field("Cutout Size", step.cutoutSize);

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
                    sb.AppendLine("            isComplete = false");
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

        private static string EscapeString(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
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
