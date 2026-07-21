using UnityEditor;
using UnityEngine;

namespace BalloonFlow.EditorTools
{
    /// <summary>
    /// [3D 레일 2026-07-22] Rail 모델용 머티리얼 셋업 — 메뉴 1회 실행 도구.
    ///
    /// 하는 일:
    ///   ① Assets/Resources/Materials/ 에 RailOutter.mat / RailInner.mat 생성(이미 있으면 재사용).
    ///      셰이더 = Rail.prefab 이 '지금 쓰는' 셰이더(첫 렌더러의 sharedMaterial.shader)를 그대로 사용,
    ///      기존 머티리얼 속성(텍스처/색)도 복사해 시각 연속성 유지. 폴백: URP Lit → URP Unlit → Standard.
    ///   ② Rail.prefab 의 렌더러에 매핑 후 저장:
    ///      - 렌더러 2개 이상: 이름에 in/out 포함 시 그걸로, 아니면 첫 번째=Outter, 두 번째=Inner.
    ///      - 렌더러 1개 + 서브머티리얼 2개 이상: 슬롯0=Outter, 슬롯1=Inner.
    ///      - 렌더러 1개 + 머티리얼 1개: Outter 만 적용(Inner 는 생성만 — 모델 분리 후 재실행).
    ///
    /// ※ 수동 메뉴 실행 전용 — InitializeOnLoad 자동 실행 금지(아트 머신 프리팹 덮어쓰기 사고 방지 규칙).
    /// </summary>
    public static class RailMaterialSetup
    {
        private const string PREFAB_PATH = "Assets/Resources/Prefabs/Rail.prefab";
        private const string MAT_DIR = "Assets/Resources/Materials";
        private const string MAT_OUTTER = MAT_DIR + "/RailOutter.mat";
        private const string MAT_INNER = MAT_DIR + "/RailInner.mat";

        [MenuItem("Tools/BalloonFlow/Setup Rail Materials (RailOutter-RailInner)")]
        public static void Setup()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Rail Materials", $"프리팹을 찾을 수 없습니다:\n{PREFAB_PATH}", "확인");
                return;
            }

            // ── '지금 쓰는' 셰이더/원본 머티리얼 파악 ──
            var srcRenderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            Material srcMat = null;
            foreach (var r in srcRenderers)
            {
                if (r.sharedMaterial != null) { srcMat = r.sharedMaterial; break; }
            }
            Shader shader = srcMat != null ? srcMat.shader : null;
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Standard");
            if (shader == null)
            {
                EditorUtility.DisplayDialog("Rail Materials", "사용할 셰이더를 찾지 못했습니다.", "확인");
                return;
            }

            // ── 머티리얼 생성(있으면 재사용, 셰이더만 동기화) ──
            if (!AssetDatabase.IsValidFolder(MAT_DIR))
                AssetDatabase.CreateFolder("Assets/Resources", "Materials");

            Material outter = LoadOrCreateMat(MAT_OUTTER, shader, srcMat);
            Material inner = LoadOrCreateMat(MAT_INNER, shader, srcMat);

            // ── 프리팹 렌더러에 매핑 ──
            GameObject root = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            try
            {
                var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
                int assigned = 0;

                if (renderers.Length >= 2)
                {
                    // 이름 휴리스틱 우선(in/out 포함), 실패 시 순서(0=Outter, 1=Inner, 나머지=Outter).
                    bool namedAny = false;
                    foreach (var r in renderers)
                    {
                        string n = r.gameObject.name.ToLowerInvariant();
                        if (n.Contains("inner") || n.Contains("_in"))
                        { r.sharedMaterial = inner; assigned++; namedAny = true; }
                        else if (n.Contains("out"))
                        { r.sharedMaterial = outter; assigned++; namedAny = true; }
                    }
                    if (!namedAny)
                    {
                        for (int i = 0; i < renderers.Length; i++)
                        {
                            renderers[i].sharedMaterial = i == 1 ? inner : outter;
                            assigned++;
                        }
                    }
                }
                else if (renderers.Length == 1)
                {
                    var mats = renderers[0].sharedMaterials;
                    if (mats.Length >= 2)
                    {
                        mats[0] = outter;
                        mats[1] = inner;
                        renderers[0].sharedMaterials = mats;
                        assigned = 2;
                    }
                    else
                    {
                        renderers[0].sharedMaterial = outter;
                        assigned = 1;
                        Debug.LogWarning("[RailMaterialSetup] 렌더러/서브머티리얼이 1개뿐 — Outter 만 적용. " +
                                         "Inner 는 생성해 뒀으니 모델이 outer/inner 로 분리되면 재실행하세요.");
                    }
                }
                else
                {
                    Debug.LogWarning("[RailMaterialSetup] Rail.prefab 에 MeshRenderer 가 없습니다.");
                }

                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
                Debug.Log($"[RailMaterialSetup] 완료 — shader='{shader.name}', 렌더러 {renderers.Length}개, " +
                          $"머티리얼 슬롯 {assigned}개 적용. RailOutter/RailInner @ {MAT_DIR}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Selection.activeObject = outter;
            EditorGUIUtility.PingObject(outter);
        }

        private static Material LoadOrCreateMat(string path, Shader shader, Material copyFrom)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = copyFrom != null ? new Material(copyFrom) : new Material(shader);
                mat.shader = shader;
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;   // 기존 mat 재사용 시 셰이더만 동기화(속성 튜닝 보존)
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }
    }
}
