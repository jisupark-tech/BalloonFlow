#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// [2026-05-13] Assets/2.Sprite/UI/profiles.png 의 sliced sub-sprite (_0, _1, ..._8) 를
    /// ProfileAssets ScriptableObject 의 icons 배열에 자동 채움.
    ///
    /// 사용:
    ///  Menu: BalloonFlow > Profile > Build Icons from profiles.png
    ///  1) profiles.png 가 Sprite Mode = Multiple 인지 검증 (아니면 알림)
    ///  2) sub-sprite 이름 끝의 숫자(_0..._N) 추출 → 오름차순 정렬
    ///  3) Assets/2.Datas/ProfileAssets.asset 이 없으면 생성, 있으면 갱신
    ///  4) icons 배열 wire (SerializedObject 경유 dirty 처리 + AssetDatabase.SaveAssets)
    ///
    /// Frames 는 디자이너가 별도 sprite 준비 후 동일 패턴으로 빌더 또는 수동 wire.
    /// </summary>
    public static class ProfileAssetsBuilder
    {
        private const string PROFILES_TEXTURE_PATH = "Assets/2.Sprite/UI/profiles.png";
        private const string ASSET_DIR = "Assets/2.Datas";
        private const string ASSET_PATH = "Assets/2.Datas/ProfileAssets.asset";

        [MenuItem("BalloonFlow/Profile/Build Icons from profiles.png", false, 510)]
        public static void BuildIcons()
        {
            // 1. 텍스처 + sub-sprites 로드
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(PROFILES_TEXTURE_PATH);
            if (tex == null)
            {
                EditorUtility.DisplayDialog("Profile Builder", $"텍스처 없음: {PROFILES_TEXTURE_PATH}", "OK");
                return;
            }

            var importer = AssetImporter.GetAtPath(PROFILES_TEXTURE_PATH) as TextureImporter;
            if (importer == null || importer.spriteImportMode != SpriteImportMode.Multiple)
            {
                EditorUtility.DisplayDialog("Profile Builder",
                    $"{PROFILES_TEXTURE_PATH} 의 Sprite Mode 를 Multiple 로 설정 후 sliced sprite (_0, _1, ...) 생성 필요.",
                    "OK");
                return;
            }

            var subAssets = AssetDatabase.LoadAllAssetsAtPath(PROFILES_TEXTURE_PATH);
            var sprites = subAssets.OfType<Sprite>().ToList();
            if (sprites.Count == 0)
            {
                EditorUtility.DisplayDialog("Profile Builder",
                    $"{PROFILES_TEXTURE_PATH} 에 sliced sprite 없음. Sprite Editor 에서 Slice 먼저 실행.",
                    "OK");
                return;
            }

            // sprite 이름 끝의 _N 숫자로 정렬 — 디자이너 의도 순서 보존.
            sprites.Sort((a, b) => ExtractTrailingNumber(a.name).CompareTo(ExtractTrailingNumber(b.name)));

            // 2. ScriptableObject 로드/생성
            if (!Directory.Exists(ASSET_DIR)) Directory.CreateDirectory(ASSET_DIR);
            var assets = AssetDatabase.LoadAssetAtPath<ProfileAssets>(ASSET_PATH);
            if (assets == null)
            {
                assets = ScriptableObject.CreateInstance<ProfileAssets>();
                AssetDatabase.CreateAsset(assets, ASSET_PATH);
                Debug.Log($"[ProfileAssetsBuilder] 신규 생성: {ASSET_PATH}");
            }

            // 3. SerializedObject 경유 icons 배열 wire — private [SerializeField] 접근.
            var so = new SerializedObject(assets);
            var iconsProp = so.FindProperty("_icons");
            if (iconsProp == null)
            {
                EditorUtility.DisplayDialog("Profile Builder",
                    "ProfileAssets._icons SerializedProperty 못 찾음 — 필드명 변경되었는지 확인.",
                    "OK");
                return;
            }
            iconsProp.arraySize = sprites.Count;
            for (int i = 0; i < sprites.Count; i++)
            {
                var elem = iconsProp.GetArrayElementAtIndex(i);
                elem.objectReferenceValue = sprites[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 4. 로그 + Project 창에서 highlight
            Selection.activeObject = assets;
            EditorGUIUtility.PingObject(assets);
            EditorUtility.DisplayDialog("Profile Builder",
                $"icons {sprites.Count}개 등록 완료.\n\n{ASSET_PATH}\n\n" +
                "다음 단계:\n" +
                "1. UILobby._profileAssets 에 이 .asset 드래그\n" +
                "2. PopupProfile._profileAssets 에 이 .asset 드래그\n" +
                "3. PopupProfile 의 _slotPrefab + 컨테이너 wire\n" +
                "4. Lobby 좌상단 Image (_imgProfileIcon/_imgProfileFrame) wire",
                "OK");
        }

        // "profiles_0" / "profile_10" 등 trailing 숫자 추출. 못 찾으면 999 (정렬 끝).
        private static int ExtractTrailingNumber(string name)
        {
            if (string.IsNullOrEmpty(name)) return 999;
            int i = name.Length - 1;
            while (i >= 0 && char.IsDigit(name[i])) i--;
            if (i == name.Length - 1) return 999;
            return int.Parse(name.Substring(i + 1));
        }
    }
}
#endif
