#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using TMPro;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// BalloonFlow > Setup Gimmick Prefabs
    /// Baricade, Key, Lock, Spawner 프리팹에 필요한 컴포넌트를 자동 추가/설정합니다.
    /// </summary>
    public static class GimmickPrefabSetup
    {
        [MenuItem("BalloonFlow/DON'T USE/Setup Gimmick Prefabs", false, 80)]
        public static void SetupAll()
        {
            int setupCount = 0;
            setupCount += SetupPrefab("Assets/Resources/Prefabs/Baricade.prefab", GimmickIdentifier.GimmickType.Barricade);
            setupCount += SetupPrefab("Assets/Resources/Prefabs/Key.prefab", GimmickIdentifier.GimmickType.None); // Key는 일반 풍선처럼 동작
            setupCount += SetupPrefab("Assets/Resources/Prefabs/Lock.prefab", GimmickIdentifier.GimmickType.LockKey);
            setupCount += SetupPrefab("Assets/Resources/Prefabs/Spawner.prefab", GimmickIdentifier.GimmickType.None); // Spawner는 Holder 기믹

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Gimmick Prefab Setup",
                $"{setupCount} prefabs updated.\n\n" +
                "각 프리팹을 열어서 확인:\n" +
                "- GimmickIdentifier → 타입 설정됨\n" +
                "- MagazineText (TMP) → HP 표시용\n" +
                "- HitParticle → 피격 이펙트 (수동 할당)\n" +
                "- EndParticle → 파괴 이펙트 (수동 할당)", "OK");
        }

        private static int SetupPrefab(string path, GimmickIdentifier.GimmickType type)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[GimmickPrefabSetup] Prefab not found: {path}");
                return 0;
            }

            // 프리팹 수정 모드 진입
            string assetPath = AssetDatabase.GetAssetPath(prefab);
            var root = PrefabUtility.LoadPrefabContents(assetPath);

            bool modified = false;

            // 1) GimmickIdentifier 추가 (없으면)
            var gi = root.GetComponent<GimmickIdentifier>();
            if (gi == null)
            {
                gi = root.AddComponent<GimmickIdentifier>();
                modified = true;
                Debug.Log($"[GimmickPrefabSetup] Added GimmickIdentifier to {root.name}");
            }

            // GimmickType 설정
            if (type != GimmickIdentifier.GimmickType.None)
            {
                var so = new SerializedObject(gi);
                var typeProp = so.FindProperty("_gimmickType");
                if (typeProp != null && typeProp.enumValueIndex != (int)type)
                {
                    typeProp.enumValueIndex = (int)type;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    modified = true;
                }
            }

            // 2) HP 텍스트 (TMP) 추가 (없으면)
            var hpText = root.GetComponentInChildren<TMP_Text>(true);
            if (hpText == null)
            {
                var textGO = new GameObject("MagazineText");
                textGO.transform.SetParent(root.transform, false);
                textGO.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                textGO.transform.localScale = Vector3.one * 0.1f;

                var tmp = textGO.AddComponent<TextMeshPro>();
                tmp.text = "";
                tmp.fontSize = 36;
                tmp.color = Color.white;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.sortingOrder = 10;

                // GimmickIdentifier의 _hpText에 연결
                var soGi = new SerializedObject(gi);
                var hpTextProp = soGi.FindProperty("_hpText");
                if (hpTextProp != null)
                {
                    hpTextProp.objectReferenceValue = tmp;
                    soGi.ApplyModifiedPropertiesWithoutUndo();
                }

                modified = true;
                Debug.Log($"[GimmickPrefabSetup] Added MagazineText to {root.name}");
            }

            // 3) HitParticle 플레이스홀더 (없으면 빈 오브젝트 생성)
            var hitEffect = root.transform.Find("HitParticle");
            if (hitEffect == null)
            {
                var hitGO = new GameObject("HitParticle");
                hitGO.transform.SetParent(root.transform, false);
                hitGO.SetActive(false);

                var soGi2 = new SerializedObject(gi);
                var hitProp = soGi2.FindProperty("_hitEffect");
                if (hitProp != null)
                {
                    hitProp.objectReferenceValue = hitGO;
                    soGi2.ApplyModifiedPropertiesWithoutUndo();
                }

                modified = true;
                Debug.Log($"[GimmickPrefabSetup] Added HitParticle placeholder to {root.name}");
            }

            // 4) EndParticle 플레이스홀더 (없으면)
            var endEffect = root.transform.Find("EndParticle");
            if (endEffect == null)
            {
                var endGO = new GameObject("EndParticle");
                endGO.transform.SetParent(root.transform, false);
                endGO.SetActive(false);

                var soGi3 = new SerializedObject(gi);
                var endProp = soGi3.FindProperty("_endEffect");
                if (endProp != null)
                {
                    endProp.objectReferenceValue = endGO;
                    soGi3.ApplyModifiedPropertiesWithoutUndo();
                }

                modified = true;
                Debug.Log($"[GimmickPrefabSetup] Added EndParticle placeholder to {root.name}");
            }

            if (modified)
            {
                PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            }
            PrefabUtility.UnloadPrefabContents(root);

            return modified ? 1 : 0;
        }
    }
}
#endif
