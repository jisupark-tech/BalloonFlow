using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// UIText 인스펙터 — CSV(TextData) Key 를 검색형 드롭다운으로 선택하고 결과 텍스트를 미리보기.
    /// 키 목록은 LocalizationService(=Resources/TextData/TextData.csv) 에서 읽는다.
    /// </summary>
    [CustomEditor(typeof(UIText))]
    [CanEditMultipleObjects]
    public class UITextEditor : UnityEditor.Editor
    {
        private SerializedProperty _keyProp;

        private void OnEnable() => _keyProp = serializedObject.FindProperty("_key");

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            string current = _keyProp.stringValue;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Text Key");
            string shown = string.IsNullOrEmpty(current) ? "(none)" : current;
            if (GUILayout.Button(shown, EditorStyles.popup))
            {
                var dd = new KeyDropdown(new AdvancedDropdownState(), picked =>
                {
                    serializedObject.Update();
                    foreach (var t in targets)
                    {
                        var so = new SerializedObject(t);
                        so.FindProperty("_key").stringValue = picked;
                        so.ApplyModifiedProperties();
                        (t as UIText)?.Apply();
                    }
                });
                var r = GUILayoutUtility.GetLastRect();
                dd.Show(r);
            }
            // 직접 입력도 허용(드물게 신규 키 수동 지정).
            EditorGUI.BeginChangeCheck();
            string typed = EditorGUILayout.TextField(current);
            if (EditorGUI.EndChangeCheck()) _keyProp.stringValue = typed;
            EditorGUILayout.EndHorizontal();

            // 미리보기
            if (!string.IsNullOrEmpty(current))
            {
                string preview = LocalizationService.Has(current)
                    ? LocalizationService.Get(current)
                    : "⚠ CSV 에 없는 Key";
                EditorGUILayout.HelpBox($"[{LocalizationService.CurrentLanguageCode}] {preview}",
                    LocalizationService.Has(current) ? MessageType.None : MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private class KeyDropdown : AdvancedDropdown
        {
            private readonly System.Action<string> _onPick;

            public KeyDropdown(AdvancedDropdownState state, System.Action<string> onPick) : base(state)
            {
                _onPick = onPick;
                minimumSize = new Vector2(320, 420);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("Text Keys");
                root.AddChild(new AdvancedDropdownItem("(none)"));
                var keys = new List<string>(LocalizationService.AllKeys);
                keys.Sort(System.StringComparer.Ordinal);
                foreach (var k in keys)
                    root.AddChild(new AdvancedDropdownItem(k));
                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                _onPick?.Invoke(item.name == "(none)" ? string.Empty : item.name);
            }
        }
    }
}
