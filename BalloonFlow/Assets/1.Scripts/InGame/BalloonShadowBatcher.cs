using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BalloonFlow
{
    /// <summary>
    /// [SHADOW_BATCH 2026-06-11] 풍선 그림자 렌더러 통합.
    /// 프로파일 결과 대형 보드 프레임 드랍의 주범은 렌더러 '개수'였다 —
    /// CullScriptable / CreateSharedRendererScene / Submit 비용이 렌더러 수에 비례 (SRP 배칭과 무관).
    /// 풍선마다 1개씩 붙던 Shadow MeshRenderer(쿼드)를 (sharedMaterial) 그룹당 combined mesh 1개로
    /// 합쳐 N개 렌더러 → 그룹 수(보통 1)개로 줄인다.
    ///
    /// - 대상: BalloonController 가 batchable 로 판단한 정적 풍선만 (none/Surprise/Hidden).
    ///   스케일 트윈이 있는 기믹(Pinata/Barricade/FlexTube 등)은 combined quad 가 transform 을
    ///   따라가지 못하므로 기존 개별 그림자 유지.
    /// - 팝 시 HideShadow(balloonId) 로 해당 쿼드 4버텍스만 degenerate 처리 (SetVertices 갱신).
    /// - 레벨 셋업마다 Build 재실행: batchable 그림자는 원본 GO 비활성+bake,
    ///   비대상 그림자는 active 복원 (풀 재사용으로 꺼진 채 돌아온 경우 정상화).
    /// 롤백: BalloonController 의 RebuildShadowBatch/HideShadow/Clear 호출 제거 +
    ///        SHADOW_BATCH_ENABLED=false (개별 그림자 active 복원 경로가 Build 안에 있어 안전).
    /// </summary>
    public class BalloonShadowBatcher
    {
        private class Group
        {
            public Mesh mesh;
            public GameObject go;
            public Material material;
            public readonly List<Vector3> verts = new List<Vector3>(1024);
            public readonly List<Vector2> uvs = new List<Vector2>(1024);
            public readonly List<int> tris = new List<int>(1536);
            public readonly Dictionary<int, int> balloonToVertStart = new Dictionary<int, int>(256);
            public bool dirty; // [SHADOW_HIDE_COALESCE] HideShadow 가 정점 collapse 후 마킹 → FlushDirty 가 1회 업로드
        }

        private readonly Dictionary<Material, Group> _groups = new Dictionary<Material, Group>(2);
        private readonly Dictionary<int, Group> _balloonToGroup = new Dictionary<int, Group>(512);
        // 공유 quad mesh(SpriteSRPBatcherUtil 캐시)의 verts/uvs 읽기 — mesh.vertices 는 호출마다 alloc 이라 캐시.
        private readonly Dictionary<Mesh, (Vector3[] verts, Vector2[] uvs, int[] tris)> _srcMeshCache
            = new Dictionary<Mesh, (Vector3[], Vector2[], int[])>(2);
        private GameObject _root;

        public void BeginBuild()
        {
            _balloonToGroup.Clear();
            foreach (var kvp in _groups)
            {
                kvp.Value.verts.Clear();
                kvp.Value.uvs.Clear();
                kvp.Value.tris.Clear();
                kvp.Value.balloonToVertStart.Clear();
            }
        }

        /// <summary>풍선 1개 처리. batchable 이면 Shadow 쿼드를 그룹 mesh 에 bake 후 원본 비활성,
        /// 아니면(기믹/변환 전) 원본 Shadow 를 active 복원해 기존 개별 경로 유지.</summary>
        public void AddOrRestore(int balloonId, GameObject balloonGo, bool batchable)
        {
            if (balloonGo == null) return;
            Transform shadowTr = FindShadowChild(balloonGo.transform);
            if (shadowTr == null) return;

            MeshFilter mf = batchable ? shadowTr.GetComponent<MeshFilter>() : null;
            MeshRenderer mr = batchable ? shadowTr.GetComponent<MeshRenderer>() : null;
            if (!batchable || mf == null || mr == null || mf.sharedMesh == null || mr.sharedMaterial == null)
            {
                // 비대상 — 이전 레벨 batching 으로 꺼진 채 풀에서 나왔을 수 있어 복원.
                if (!shadowTr.gameObject.activeSelf) shadowTr.gameObject.SetActive(true);
                return;
            }

            if (!_srcMeshCache.TryGetValue(mf.sharedMesh, out var src))
            {
                src = (mf.sharedMesh.vertices, mf.sharedMesh.uv, mf.sharedMesh.triangles);
                _srcMeshCache[mf.sharedMesh] = src;
            }
            if (src.verts == null || src.verts.Length == 0) return;

            Group group = GetOrCreateGroup(mr.sharedMaterial);
            int vertStart = group.verts.Count;
            for (int i = 0; i < src.verts.Length; i++)
            {
                group.verts.Add(shadowTr.TransformPoint(src.verts[i])); // 월드 좌표 bake (root=identity)
                group.uvs.Add(i < src.uvs.Length ? src.uvs[i] : Vector2.zero);
            }
            for (int i = 0; i < src.tris.Length; i++)
                group.tris.Add(vertStart + src.tris[i]);

            group.balloonToVertStart[balloonId] = vertStart;
            _balloonToGroup[balloonId] = group;
            shadowTr.gameObject.SetActive(false);
        }

        public void EndBuild()
        {
            foreach (var kvp in _groups)
            {
                Group g = kvp.Value;
                g.dirty = false; // 전체 재빌드 — 아래에서 mesh 통째 설정하므로 보류 중 flush 불필요
                bool empty = g.verts.Count == 0;
                if (g.go != null) g.go.SetActive(!empty);
                if (empty) { if (g.mesh != null) g.mesh.Clear(); continue; }

                g.mesh.Clear();
                g.mesh.SetVertices(g.verts);
                g.mesh.SetUVs(0, g.uvs);
                g.mesh.SetTriangles(g.tris, 0, false);
                g.mesh.RecalculateBounds();
            }
        }

        // ROLLBACK_SHADOW_HIDE_COALESCE_20260616: 팝마다 mesh.SetVertices(전체 결합버퍼) 재업로드는
        //   O(전체 그림자 verts). zap/다트연쇄로 한 프레임 다수 팝 시 O(팝수 × N) GPU 버퍼 처닝이 부하.
        //   → 정점 collapse 는 즉시 하되 실제 SetVertices 는 dirty 마킹 후 FlushDirty()(프레임당 1회)로 합친다.
        //   롤백: 아래 dirty 마킹을 `if (g.mesh != null) g.mesh.SetVertices(g.verts);` 로 환원 + FlushDirty/dirty/LateUpdate 제거.
        /// <summary>팝된 풍선의 그림자 쿼드를 degenerate 처리 — 실제 mesh 업로드는 FlushDirty 로 coalesce.</summary>
        public void HideShadow(int balloonId)
        {
            if (!_balloonToGroup.TryGetValue(balloonId, out Group g)) return;
            _balloonToGroup.Remove(balloonId);
            if (!g.balloonToVertStart.TryGetValue(balloonId, out int vertStart)) return;
            g.balloonToVertStart.Remove(balloonId);

            int end = Mathf.Min(vertStart + 4, g.verts.Count);
            if (vertStart >= end) return;
            Vector3 collapse = g.verts[vertStart];
            for (int i = vertStart + 1; i < end; i++) g.verts[i] = collapse;
            g.dirty = true; // 실제 업로드는 FlushDirty()(프레임당 1회)로 coalesce — bounds 는 collapse 가 기존 범위 내라 유지
        }

        /// <summary>[SHADOW_HIDE_COALESCE 2026-06-16] 한 프레임에 누적된 HideShadow 들을 그룹당 1회만 mesh 업로드.
        /// BalloonController.LateUpdate 에서 매 프레임 호출 — 다중팝(zap/다트연쇄)의 O(팝수×N) 재업로드를 O(N)/프레임으로 축소.</summary>
        public void FlushDirty()
        {
            foreach (var kvp in _groups)
            {
                Group g = kvp.Value;
                if (!g.dirty) continue;
                g.dirty = false;
                if (g.mesh != null) g.mesh.SetVertices(g.verts);
            }
        }

        // ROLLBACK_SHADOW_BATCH_BALLOON_THRESHOLD_20260616:
        //   고부하 레벨(풍선 1000+) 그림자 overdraw 억제용. 개별 Shadow GO 1개를 비활성.
        //   combined mesh 는 호출측에서 Clear() 로 별도 정리. 롤백: 이 메서드 + 호출부 제거.
        /// <summary>[고부하 억제] balloonGo 의 "Shadow" 자식을 비활성(이미 꺼졌으면 noop).</summary>
        public void SuppressShadow(GameObject balloonGo)
        {
            if (balloonGo == null) return;
            Transform shadowTr = FindShadowChild(balloonGo.transform);
            if (shadowTr != null && shadowTr.gameObject.activeSelf)
                shadowTr.gameObject.SetActive(false);
        }

        /// <summary>레벨 정리 — combined mesh 비우기 (GO/Mesh 인스턴스는 재사용).</summary>
        public void Clear()
        {
            _balloonToGroup.Clear();
            foreach (var kvp in _groups)
            {
                Group g = kvp.Value;
                g.dirty = false;
                g.verts.Clear(); g.uvs.Clear(); g.tris.Clear(); g.balloonToVertStart.Clear();
                if (g.mesh != null) g.mesh.Clear();
                if (g.go != null) g.go.SetActive(false);
            }
        }

        private Group GetOrCreateGroup(Material mat)
        {
            if (_groups.TryGetValue(mat, out Group g) && g.go != null) return g;

            g = new Group { material = mat };
            g.mesh = new Mesh { name = "BalloonShadowBatch" };
            // 65k 초과 보드 대비 32bit 인덱스 (대형 테스트 맵 안전).
            g.mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            g.mesh.MarkDynamic();

            EnsureRoot();
            g.go = new GameObject($"ShadowBatch_{mat.name}");
            g.go.transform.SetParent(_root.transform, false); // root=identity → bake 좌표 = 월드
            var mf = g.go.AddComponent<MeshFilter>();
            mf.sharedMesh = g.mesh;
            var mr = g.go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

            _groups[mat] = g;
            return g;
        }

        private void EnsureRoot()
        {
            if (_root != null) return;
            _root = new GameObject("[BalloonShadowBatch]");
            _root.transform.position = Vector3.zero;
            _root.transform.rotation = Quaternion.identity;
            _root.transform.localScale = Vector3.one;
        }

        private static Transform FindShadowChild(Transform root)
        {
            // 비활성 포함 탐색 — 이전 레벨 batching 으로 꺼진 Shadow 도 찾아 복원/재사용.
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].name == "Shadow") return all[i];
            return null;
        }
    }
}
