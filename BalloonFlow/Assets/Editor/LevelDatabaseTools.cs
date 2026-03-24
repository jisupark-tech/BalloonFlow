#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// LevelDatabase 관리 도구.
    /// LevelJsonImporterWindow의 toolbar에서 호출.
    /// </summary>
    public static class LevelDatabaseTools
    {
        private const string DB_PATH = "Assets/Resources/LevelDatabase.asset";
        private const string BACKUP_FOLDER = "Assets/LevelBackups";

        public static void ExportAll()
        {
            var db = AssetDatabase.LoadAssetAtPath<LevelDatabase>(DB_PATH);
            if (db == null || db.levels == null) { Debug.LogError("LevelDatabase not found"); return; }

            string json = JsonUtility.ToJson(db, true);
            string path = EditorUtility.SaveFilePanel("Export LevelDatabase",
                "Assets", $"LevelDB_{DateTime.Now:yyyyMMdd_HHmmss}", "json");
            if (string.IsNullOrEmpty(path)) return;

            File.WriteAllText(path, json);
            Debug.Log($"[LevelDB] Exported {db.levels.Length} levels → {path}");
            EditorUtility.DisplayDialog("Export 완료", $"{db.levels.Length}개 레벨 저장됨", "OK");
        }

        public static void ImportAll()
        {
            string path = EditorUtility.OpenFilePanel("Import LevelDatabase JSON", "Assets", "json");
            if (string.IsNullOrEmpty(path)) return;

            var db = AssetDatabase.LoadAssetAtPath<LevelDatabase>(DB_PATH);
            if (db == null) { Debug.LogError("LevelDatabase not found"); return; }

            CreateBackup(db, "before_import");

            string json = File.ReadAllText(path);
            JsonUtility.FromJsonOverwrite(json, db);

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LevelDB] Imported {db.levels.Length} levels (백업 생성됨)");
            EditorUtility.DisplayDialog("Import 완료",
                $"{db.levels.Length}개 레벨 로드됨\n백업이 자동 생성되었습니다.", "OK");
        }

        public static void ManualBackup()
        {
            var db = AssetDatabase.LoadAssetAtPath<LevelDatabase>(DB_PATH);
            if (db == null) { Debug.LogError("LevelDatabase not found"); return; }

            string backupPath = CreateBackup(db, "manual");
            EditorUtility.DisplayDialog("백업 완료", $"백업 저장: {backupPath}", "OK");
        }

        public static string CreateBackup(LevelDatabase db, string tag = "auto")
        {
            if (!Directory.Exists(BACKUP_FOLDER))
                Directory.CreateDirectory(BACKUP_FOLDER);

            string fileName = $"LevelDB_{tag}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            string fullPath = Path.Combine(BACKUP_FOLDER, fileName);

            string json = JsonUtility.ToJson(db, true);
            File.WriteAllText(fullPath, json);
            Debug.Log($"[LevelDB] Backup: {fileName} ({db.levels.Length} levels)");
            return fullPath;
        }

        public static void DoRollback()
        {
            if (!Directory.Exists(BACKUP_FOLDER))
            {
                EditorUtility.DisplayDialog("롤백 실패", "백업 폴더가 없습니다.", "OK");
                return;
            }

            var files = Directory.GetFiles(BACKUP_FOLDER, "*.json")
                .OrderByDescending(f => f).ToArray();

            if (files.Length == 0)
            {
                EditorUtility.DisplayDialog("롤백 실패", "백업 파일이 없습니다.", "OK");
                return;
            }

            string[] names = files.Take(5).Select(Path.GetFileName).ToArray();
            int choice = EditorUtility.DisplayDialogComplex("롤백 선택",
                $"가장 최근 백업:\n\n1) {names[0]}" +
                (names.Length > 1 ? $"\n2) {names[1]}" : "") +
                (names.Length > 2 ? $"\n3) {names[2]}" : "") +
                "\n\n1번(가장 최근)으로 롤백하시겠습니까?",
                "롤백", "취소", "파일 선택...");

            string selectedPath = null;
            if (choice == 0) selectedPath = files[0];
            else if (choice == 2) selectedPath = EditorUtility.OpenFilePanel("백업 파일 선택", BACKUP_FOLDER, "json");
            else return;

            if (string.IsNullOrEmpty(selectedPath)) return;

            var db = AssetDatabase.LoadAssetAtPath<LevelDatabase>(DB_PATH);
            if (db == null) { Debug.LogError("LevelDatabase not found"); return; }

            CreateBackup(db, "before_rollback");

            string json = File.ReadAllText(selectedPath);
            JsonUtility.FromJsonOverwrite(json, db);

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LevelDB] Rolled back from {Path.GetFileName(selectedPath)}");
            EditorUtility.DisplayDialog("롤백 완료",
                $"{Path.GetFileName(selectedPath)}에서 복원됨\n{db.levels.Length}개 레벨", "OK");
        }

        public static void SwapLevels()
        {
            var db = AssetDatabase.LoadAssetAtPath<LevelDatabase>(DB_PATH);
            if (db == null || db.levels == null) { Debug.LogError("LevelDatabase not found"); return; }

            string inputA = EditorInputDialog.Show("Swap Levels", "첫 번째 Level ID:", "1");
            if (string.IsNullOrEmpty(inputA)) return;
            string inputB = EditorInputDialog.Show("Swap Levels", "두 번째 Level ID:", "2");
            if (string.IsNullOrEmpty(inputB)) return;

            if (!int.TryParse(inputA, out int idA) || !int.TryParse(inputB, out int idB))
            {
                EditorUtility.DisplayDialog("오류", "숫자를 입력하세요.", "OK");
                return;
            }

            var levels = new List<LevelConfig>(db.levels);
            int idxA = levels.FindIndex(l => l.levelId == idA);
            int idxB = levels.FindIndex(l => l.levelId == idB);

            if (idxA < 0 || idxB < 0)
            {
                EditorUtility.DisplayDialog("오류",
                    $"Level {(idxA < 0 ? idA : idB)} not found.", "OK");
                return;
            }

            CreateBackup(db, "before_swap");

            levels[idxA].levelId = idB;
            levels[idxB].levelId = idA;
            int posA = levels[idxA].positionInPackage;
            levels[idxA].positionInPackage = levels[idxB].positionInPackage;
            levels[idxB].positionInPackage = posA;

            levels.Sort((a, b) => a.levelId.CompareTo(b.levelId));
            db.levels = levels.ToArray();

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LevelDB] Swapped Level {idA} ↔ Level {idB}");
            EditorUtility.DisplayDialog("Swap 완료", $"Level {idA} ↔ Level {idB} 교환됨", "OK");
        }
    }

    public class EditorInputDialog : EditorWindow
    {
        private string _value;
        private string _message;
        private static string _result;

        public static string Show(string title, string message, string defaultValue = "")
        {
            _result = null;
            var win = CreateInstance<EditorInputDialog>();
            win.titleContent = new GUIContent(title);
            win._message = message;
            win._value = defaultValue;
            win.minSize = new Vector2(300, 100);
            win.maxSize = new Vector2(300, 100);
            win.ShowModalUtility();
            return _result;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(_message);
            _value = EditorGUILayout.TextField(_value);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("OK")) { _result = _value; Close(); }
            if (GUILayout.Button("Cancel")) { _result = null; Close(); }
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
