#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// Imports the generated 300 JSON level files through the existing JSON importer
    /// conversion path, then exports 20-level episode files.
    /// </summary>
    public static class Generated300EpisodeBuilder
    {
        private const string SourceDir = @"E:\BalloonFlow\BalloonFlow_Level\Generated_300";
        private const string DbPath = "Assets/EditorData/LevelDatabase.asset";
        private const string StreamingEp1Path = "Assets/StreamingAssets/episode_01.json";
        private const int LevelsPerEpisode = 20;
        private const int EpisodeCount = 15;
        private const int EpisodeVersion = 1;

        // [2026-06-12 메뉴 정리] [MenuItem("BalloonFlow/Level Episodes/Build Episodes From Generated_300")]
        public static void BuildFromMenu()
        {
            BuildGenerated300Episodes();
        }

        public static void BuildGenerated300Episodes()
        {
            if (!Directory.Exists(SourceDir))
                throw new DirectoryNotFoundException(SourceDir);

            var configs = ImportGeneratedConfigs();
            if (configs.Count != EpisodeCount * LevelsPerEpisode)
                throw new InvalidOperationException($"Expected 300 levels, imported {configs.Count}.");

            SaveLevelDatabase(configs);
            ExportEpisodes(configs);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Generated300EpisodeBuilder] Complete. levels={configs.Count}, episodes={EpisodeCount}");
        }

        private static List<LevelConfig> ImportGeneratedConfigs()
        {
            var importer = ScriptableObject.CreateInstance<LevelJsonImporterWindow>();
            try
            {
                Type importerType = typeof(LevelJsonImporterWindow);
                MethodInfo loadJsonFile = importerType.GetMethod("LoadJsonFile", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo entriesField = importerType.GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo overwriteField = importerType.GetField("_overwriteConflicts", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo targetDbField = importerType.GetField("_targetDBIndex", BindingFlags.Instance | BindingFlags.NonPublic);

                if (loadJsonFile == null || entriesField == null || overwriteField == null || targetDbField == null)
                    throw new MissingMemberException("LevelJsonImporterWindow reflection target changed.");

                overwriteField.SetValue(importer, true);
                targetDbField.SetValue(importer, 0);

                string[] files = Directory.GetFiles(SourceDir, "Lv*.json")
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (string file in files)
                    loadJsonFile.Invoke(importer, new object[] { file });

                var configs = new List<LevelConfig>(files.Length);
                IList entries = (IList)entriesField.GetValue(importer);
                foreach (object entry in entries)
                {
                    FieldInfo configField = entry.GetType().GetField("config", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    FieldInfo errorField = entry.GetType().GetField("error", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var error = errorField?.GetValue(entry) as string;
                    var config = configField?.GetValue(entry) as LevelConfig;
                    if (!string.IsNullOrEmpty(error))
                        throw new InvalidOperationException($"Import error: {error}");
                    if (config != null)
                        configs.Add(config);
                }

                configs.Sort((a, b) => a.levelId.CompareTo(b.levelId));
                for (int i = 0; i < configs.Count; i++)
                    NormalizeEpisodeFields(configs[i]);

                return configs;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(importer);
            }
        }

        private static void SaveLevelDatabase(List<LevelConfig> configs)
        {
            if (!AssetDatabase.IsValidFolder("Assets/EditorData"))
                AssetDatabase.CreateFolder("Assets", "EditorData");

            var db = AssetDatabase.LoadAssetAtPath<LevelDatabase>(DbPath);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<LevelDatabase>();
                AssetDatabase.CreateAsset(db, DbPath);
            }

            Undo.RecordObject(db, "Build Episodes From Generated_300");
            db.levels = configs.ToArray();
            EditorUtility.SetDirty(db);
            Debug.Log($"[Generated300EpisodeBuilder] LevelDatabase updated: {DbPath} ({configs.Count} levels)");
        }

        private static void ExportEpisodes(List<LevelConfig> configs)
        {
            string seedEpisodesDir = GetSeedEpisodesDir();
            Directory.CreateDirectory(seedEpisodesDir);
            Directory.CreateDirectory(Path.GetDirectoryName(StreamingEp1Path) ?? "Assets/StreamingAssets");

            for (int pkg = 1; pkg <= EpisodeCount; pkg++)
            {
                LevelEpisode episode = BuildEpisode(configs, pkg);
                string json = JsonUtility.ToJson(episode, prettyPrint: false);

                string seedPath = Path.Combine(seedEpisodesDir, $"episode_{pkg:D2}.json");
                File.WriteAllText(seedPath, json);
                Debug.Log($"[Generated300EpisodeBuilder] Exported {seedPath} levels={episode.levelCount} bytes={json.Length}");

                if (pkg == 1)
                {
                    File.WriteAllText(StreamingEp1Path, json);
                    Debug.Log($"[Generated300EpisodeBuilder] Synced {StreamingEp1Path}");
                }
            }
        }

        private static LevelEpisode BuildEpisode(List<LevelConfig> configs, int packageId)
        {
            int firstLevel = ((packageId - 1) * LevelsPerEpisode) + 1;
            int lastLevel = firstLevel + LevelsPerEpisode - 1;
            LevelConfig[] levels = configs
                .Where(level => level.levelId >= firstLevel && level.levelId <= lastLevel)
                .OrderBy(level => level.levelId)
                .ToArray();

            if (levels.Length != LevelsPerEpisode)
                throw new InvalidOperationException($"Episode {packageId:D2} expected {LevelsPerEpisode} levels, got {levels.Length}.");

            return new LevelEpisode
            {
                packageId = packageId,
                levelCount = levels.Length,
                version = EpisodeVersion,
                levels = levels
            };
        }

        private static void NormalizeEpisodeFields(LevelConfig level)
        {
            if (level == null) return;
            level.packageId = PackageIdForLevel(level.levelId);
            level.positionInPackage = ((level.levelId - 1) % LevelsPerEpisode) + 1;
        }

        private static int PackageIdForLevel(int levelId)
        {
            if (levelId < 1) return 1;
            return ((levelId - 1) / LevelsPerEpisode) + 1;
        }

        private static string GetSeedEpisodesDir()
        {
            string assets = Application.dataPath.Replace('\\', '/');
            string unityProject = Path.GetDirectoryName(assets);
            string repoRoot = Path.GetDirectoryName(unityProject);
            return Path.Combine(repoRoot ?? unityProject ?? ".", "firebase", "seed", "episodes");
        }
    }
}
#endif
