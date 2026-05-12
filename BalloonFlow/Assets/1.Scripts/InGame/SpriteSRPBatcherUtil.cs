using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BalloonFlow
{
    /// <summary>
    /// SpriteRenderer 는 PerRendererData MPB 자동 binding 으로 SRP Batcher / GPU Instancing 미호환.
    /// "Balloon Shared" 패턴: MeshRenderer + Quad mesh + Custom/SpriteInstanced shader + 공용 mat.
    /// 같은 sprite 끼리는 같은 mat instance 공유 → SRP Batcher 가 묶음.
    /// </summary>
    public static class SpriteSRPBatcherUtil
    {
        // Legacy — SpriteRenderer 유지 path 의 fallback shader 적용 용.
        private static Material _sharedMat;
        public static Material GetSharedMat()
        {
            if (_sharedMat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                _sharedMat = new Material(shader);
                _sharedMat.hideFlags = HideFlags.HideAndDontSave;
            }
            return _sharedMat;
        }

        // 신규 — Sprite → MeshRenderer 전환 path. 같은 (sprite, tint) 끼리 1 mat / 1 mesh 공유.
        // mat 우선순위: Resources/SpriteSharedInstanced.mat (사용자 직접 생성) > 동적 생성 fallback.
        // 사용자 권장: Editor 에서 mat asset 직접 생성하고 shader / texture / instancing 명시.
        private const string SHARED_MAT_RESOURCE_PATH = "SpriteSharedInstanced";
        private static readonly Dictionary<(Sprite, Color), Material> _spriteMatCache = new Dictionary<(Sprite, Color), Material>();
        private static readonly Dictionary<Sprite, Mesh> _spriteMeshCache = new Dictionary<Sprite, Mesh>();
        private static Material _resourceMatTemplate;
        private static bool _resourceMatLoadAttempted;
        private static Shader _cachedInstancedShader;

        private static Material LoadOrGetTemplateMat()
        {
            if (!_resourceMatLoadAttempted)
            {
                _resourceMatLoadAttempted = true;
                _resourceMatTemplate = Resources.Load<Material>(SHARED_MAT_RESOURCE_PATH);
                if (_resourceMatTemplate == null)
                    Debug.LogWarning($"[SpriteSRPBatcherUtil] Resources/{SHARED_MAT_RESOURCE_PATH}.mat 없음 — 동적 생성 fallback. Editor 에서 mat asset 생성 권장.");
            }
            return _resourceMatTemplate;
        }

        public static Material GetInstancedMatForSprite(Sprite sprite, Color tint)
        {
            if (sprite == null) return null;
            var key = (sprite, tint);
            if (_spriteMatCache.TryGetValue(key, out var mat)) return mat;

            // 우선: 사용자가 Editor 에서 생성한 mat asset 의 instance 복제 (shader + property 보존)
            var template = LoadOrGetTemplateMat();
            if (template != null)
            {
                mat = new Material(template);
            }
            else
            {
                // Fallback: 동적 shader 검색
                if (_cachedInstancedShader == null)
                {
                    _cachedInstancedShader = Shader.Find("Custom/SpriteInstanced");
                    if (_cachedInstancedShader == null)
                        _cachedInstancedShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                    if (_cachedInstancedShader == null)
                        _cachedInstancedShader = Shader.Find("Sprites/Default");
                }
                if (_cachedInstancedShader == null) return null;
                mat = new Material(_cachedInstancedShader);
                mat.enableInstancing = true;
            }

            mat.SetTexture("_BaseMap", sprite.texture);
            mat.SetTexture("_MainTex", sprite.texture);
            mat.SetColor("_BaseColor", tint);
            mat.hideFlags = HideFlags.HideAndDontSave;
            _spriteMatCache[key] = mat;
            return mat;
        }

        /// <summary>Sprite 의 atlas UV rect 를 반영한 Quad mesh. 같은 sprite 끼리 공유.
        /// sprite.uv 사용 불가 (Tight mesh type 시 4 이상 vertex 가능) → textureRect 직접 계산.</summary>
        public static Mesh GetMeshForSprite(Sprite sprite)
        {
            if (sprite == null) return null;
            if (_spriteMeshCache.TryGetValue(sprite, out var mesh)) return mesh;

            mesh = new Mesh { name = $"SpriteMesh_{sprite.name}" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
            };

            // textureRect 기반 atlas UV 직접 계산 (Tight mesh type 호환)
            Texture tex = sprite.texture;
            float texW = tex != null ? tex.width : 1f;
            float texH = tex != null ? tex.height : 1f;
            Rect r = sprite.textureRect;
            float u0 = r.x / texW;
            float v0 = r.y / texH;
            float u1 = (r.x + r.width) / texW;
            float v1 = (r.y + r.height) / texH;
            mesh.uv = new[]
            {
                new Vector2(u0, v0),
                new Vector2(u1, v0),
                new Vector2(u0, v1),
                new Vector2(u1, v1),
            };

            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            mesh.hideFlags = HideFlags.HideAndDontSave;
            _spriteMeshCache[sprite] = mesh;
            return mesh;
        }

        /// <summary>GameObject 에 SpriteRenderer 대신 MeshFilter + MeshRenderer 설정.
        /// 같은 (sprite, tint) 끼리 mesh/mat 공유 → SRP Batcher 묶음 (MPB 미사용).</summary>
        public static MeshRenderer SetupAsMeshSprite(GameObject go, Sprite sprite, Color tint)
        {
            if (go == null || sprite == null) return null;

            Mesh meshAsset = GetMeshForSprite(sprite);
            Material matAsset = GetInstancedMatForSprite(sprite, tint);
            if (meshAsset == null || matAsset == null) return null;

            // 기존 SpriteRenderer 즉시 제거 (Destroy 는 next frame — 같은 frame AddComponent 시 race)
            var existingSr = go.GetComponent<SpriteRenderer>();
            if (existingSr != null)
            {
#if UNITY_EDITOR
                if (Application.isPlaying) Object.DestroyImmediate(existingSr);
                else Object.DestroyImmediate(existingSr);
#else
                Object.DestroyImmediate(existingSr);
#endif
            }

            var mf = go.GetComponent<MeshFilter>();
            if (mf == null) mf = go.AddComponent<MeshFilter>();
            if (mf == null) return null;
            mf.sharedMesh = meshAsset;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) mr = go.AddComponent<MeshRenderer>();
            if (mr == null) return null;
            mr.sharedMaterial = matAsset;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

            return mr;
        }

        // Legacy — SpriteRenderer 유지 path (current Shadow 변환 helper).
        public static void ApplyToShadowRenderers(GameObject root)
        {
            if (root == null) return;
            var mat = GetSharedMat();
            var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                if (renderers[i].gameObject.name == "Shadow" && renderers[i].sharedMaterial != mat)
                    renderers[i].sharedMaterial = mat;
            }
        }

        /// <summary>"Shadow" 이름의 SpriteRenderer 를 MeshRenderer 로 swap. 1회만 실행 (이미 변환된 경우 noop).</summary>
        public static void ConvertShadowToMeshSprite(GameObject root)
        {
            if (root == null) return;
            var spriteRenderers = root.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                var sr = spriteRenderers[i];
                if (sr == null) continue;
                if (sr.gameObject.name != "Shadow") continue;
                if (sr.sprite == null) continue;

                var shadowGo = sr.gameObject;
                Sprite sprite = sr.sprite;
                Color color = sr.color;

                // bounds.size 기반 scale 보존 — sprite 의 world size 와 quad mesh (1 unit) 매핑
                Vector3 originalScale = shadowGo.transform.localScale;
                float sw = sprite.bounds.size.x;
                float sh = sprite.bounds.size.y;

                SetupAsMeshSprite(shadowGo, sprite, color);

                // localScale 보존 — quad mesh 가 1 unit (vs sprite.bounds.size) 차이를 보정
                if (sw > 0.001f && sh > 0.001f)
                {
                    shadowGo.transform.localScale = new Vector3(originalScale.x * sw, originalScale.y * sh, originalScale.z);
                }
            }
        }
    }
}
