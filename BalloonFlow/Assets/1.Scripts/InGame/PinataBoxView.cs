using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Pinata_Box(Target Box) 비주얼 — 틀(frame) + 명시 egg 리스트(N개)의 알(paint).
    ///
    /// [egg 템플릿(paint) 구조]  paint → Cylinder(몸체) + texture(균열 오버레이)
    ///  - 색상은 Cylinder 에만 적용. texture 는 평소 비활성, HP 가 절반 이하로 닳으면 활성화(균열 표시).
    ///
    /// [배치]  알을 footprint 의 W×H 격자에 row-major(index = r*W + c)로 1:1 배치.
    ///         각 알 = 풍선 1칸 크기(레벨 cellSize 기준) — 레벨 풍선 사이즈에 맞춰 자동 스케일.
    ///         eggColors 길이는 W*H 여야 함(불일치 시 알이 잘리지 않도록 행만 확장하고 경고).
    ///
    /// [Inspector 링크 — paint 프리팹의 알/틀 노드에 부착]
    ///  - _frame      : 박스 틀(paintbox). footprint 에 맞춰 bounds-fit. 없으면 스킵.
    ///  - _eggTemplate: 알 1개(paint) 템플릿. Build 가 N개 복제 → 원본 자동 비활성.
    /// </summary>
    [DisallowMultipleComponent]
    public class PinataBoxView : MonoBehaviour
    {
        [Header("[Inspector 링크 — 드래그해서 연결]")]
        [Tooltip("박스 틀(paintbox) Transform. footprint 크기에 맞춰 자동 스케일. 비워두면 스킵.")]
        [SerializeField] private Transform _frame;
        [Tooltip("알 1개 템플릿(paint) GameObject. Build 가 N개 복제. (자식: Cylinder=몸체, texture=균열)")]
        [SerializeField] private GameObject _eggTemplate;

        [Header("[튜닝]")]
        [Tooltip("격자 한 칸 대비 알 크기 배수 (1=칸 꽉 채움, 0.9=약간 여백). 보통 0.9~1.")]
        [SerializeField, Range(0.3f, 1.2f)] private float _eggFillRatio = 0.95f;

        [Tooltip("알 격자가 차지하는 박스 안쪽 영역 비율 — paintbox 테두리 안에 들어가도록. 1=footprint 전체(테두리에 닿음), 0.85=안쪽 85%. 알이 틀을 벗어나면 줄이세요.")]
        [SerializeField, Range(0.3f, 1f)] private float _innerAreaRatio = 0.85f;

        [Header("[알 자식 링크 — 템플릿(_eggTemplate) 안의 노드를 드래그]")]
        [Tooltip("색 적용 대상(몸체) — 템플릿 안의 Cylinder 를 드래그. 비우면 이름으로 탐색.")]
        [SerializeField] private Transform _bodyOnTemplate;
        [Tooltip("균열 오버레이 — 템플릿 안의 texture 를 드래그. 비우면 이름으로 탐색.")]
        [SerializeField] private Transform _textureOnTemplate;

        [Header("[자식 이름 — 링크 안 돼 있을 때만 사용하는 fallback]")]
        [Tooltip("색 적용 대상(몸체) 자식 이름. _bodyOnTemplate 미링크 시 이 이름으로 탐색.")]
        [SerializeField] private string _bodyChildName = "Cylinder";
        [Tooltip("균열 오버레이 자식 이름. _textureOnTemplate 미링크 시 이 이름으로 탐색.")]
        [SerializeField] private string _textureChildName = "texture";

        private readonly List<GameObject> _eggs = new List<GameObject>();
        private readonly List<GameObject> _eggTextures = new List<GameObject>();
        private readonly List<int> _eggMaxHps = new List<int>();

        public int EggCount => _eggs.Count;

        /// <summary>
        /// 알 배치 — eggColors 항목 수(N)만큼. Cylinder 만 색칠, texture 는 비활성으로 시작.
        /// </summary>
        public void Build(int w, int h, int[] eggColors, int[] eggHps, float cellSizeX, float cellSizeZ)
        {
            Clear();

            if (_eggTemplate == null)
            {
                Debug.LogError("[PinataBoxView] _eggTemplate 미할당 — paint 1개를 _eggTemplate 에 링크하세요.", this);
                return;
            }

            w = Mathf.Max(1, w);
            h = Mathf.Max(1, h);
            int n = (eggColors != null && eggColors.Length > 0) ? eggColors.Length : 1;

            // [타겟박스] authoring 된 footprint W×H 격자에 row-major(index = r*W + c)로 1:1 배치.
            // 각 알 = 풍선 1칸. eggColors 길이는 W*H 여야 하며, 불일치 시 알이 잘리지 않도록 행만 확장.
            int cols = Mathf.Max(1, w);
            int rows = Mathf.Max(h, Mathf.CeilToInt((float)n / cols));
            if (n != w * h)
                Debug.LogWarning($"[PinataBoxView] egg 수({n}) != footprint {w}×{h}(={w * h}). " +
                                 "각 알=풍선 1칸 모델이므로 eggColors 길이를 W*H 로 맞추세요.", this);

            // 격자 한 칸 = footprint 안쪽 영역(_innerAreaRatio)을 cols×rows 로 나눈 크기.
            // paintbox 테두리 안에 알이 들어가도록 footprint 전체가 아니라 안쪽만 사용한다.
            float ir = Mathf.Clamp(_innerAreaRatio, 0.1f, 1f);
            float gridCellW = (w * cellSizeX * ir) / cols;
            float gridCellZ = (h * cellSizeZ * ir) / rows;

            // 템플릿 월드 bounds 측정 → 격자 칸에 맞출 스케일 계수 산출. 측정 위해 잠깐 활성화.
            bool tplWasActive = _eggTemplate.activeSelf;
            if (!tplWasActive) _eggTemplate.SetActive(true);
            float tplSizeX, tplSizeZ;
            MeasureTemplateSize(out tplSizeX, out tplSizeZ);

            _eggTemplate.SetActive(false); // 원본 숨김(복제본만 표시)

            Vector3 tplScale = _eggTemplate.transform.localScale;
            Quaternion tplRot = _eggTemplate.transform.localRotation;
            float tplY = _eggTemplate.transform.localPosition.y;

            // 격자 칸(월드)에 맞춘 균일 스케일 계수 — 작은 축 기준으로 셀 안에 들어가게.
            float fitK = 1f;
            if (tplSizeX > 0.0001f && tplSizeZ > 0.0001f)
                fitK = Mathf.Min(gridCellW / tplSizeX, gridCellZ / tplSizeZ) * _eggFillRatio;
            // NOTE: eggScale(scaleMult)을 곱하지 않는다 — cellSizeX/Z 가 이미 widthMult/heightMult 를 포함하므로
            //       여기서 또 곱하면 이중 적용되어 알이 paintbox 를 벗어난다.

            // 월드 격자 간격 → 로컬 단위(부모 스케일 보정).
            Vector3 ls = transform.lossyScale;
            float localGridX = gridCellW / Mathf.Max(0.0001f, Mathf.Abs(ls.x));
            float localGridZ = gridCellZ / Mathf.Max(0.0001f, Mathf.Abs(ls.z));

            // 링크된 Cylinder/texture 의 템플릿 기준 자식 경로(인덱스) 미리 계산 — 클론에서 동일 경로로 해석.
            // 미링크면 null → 이름으로 fallback.
            var bodyPath = (_bodyOnTemplate != null) ? GetChildIndexPath(_eggTemplate.transform, _bodyOnTemplate) : null;
            var texPath = (_textureOnTemplate != null) ? GetChildIndexPath(_eggTemplate.transform, _textureOnTemplate) : null;

            for (int i = 0; i < n; i++)
            {
                int gc = i % cols;
                int gr = i / cols;

                GameObject egg = Instantiate(_eggTemplate, transform);
                egg.SetActive(true);
                egg.transform.localRotation = tplRot;
                egg.transform.localScale = tplScale * fitK;
                float ox = (gc - (cols - 1) * 0.5f) * localGridX;
                float oz = (gr - (rows - 1) * 0.5f) * localGridZ;
                egg.transform.localPosition = new Vector3(ox, tplY, oz);

                int color = (eggColors != null && i < eggColors.Length) ? eggColors[i] : 0;
                int maxHp = (eggHps != null && i < eggHps.Length && eggHps[i] > 0) ? eggHps[i] : 1;

                GameObject texChild;
                SetupEggVisual(egg, color, bodyPath, texPath, out texChild);

                _eggs.Add(egg);
                _eggTextures.Add(texChild);
                _eggMaxHps.Add(maxHp);
            }

            ScaleFrameToFootprint(w, h, cellSizeX, cellSizeZ);
        }

        /// <summary>알 항목 i 의 현재 HP 반영 — 0 이하면 알 제거, 절반 이하면 균열(texture) 활성. 남은 알 있으면 true.</summary>
        public bool UpdateEggHp(int index, int currentHp)
        {
            if (index >= 0 && index < _eggs.Count && _eggs[index] != null)
            {
                if (currentHp <= 0)
                {
                    _eggs[index].SetActive(false);
                }
                else if (index < _eggTextures.Count && _eggTextures[index] != null)
                {
                    int maxHp = index < _eggMaxHps.Count ? _eggMaxHps[index] : currentHp;
                    bool damaged = currentHp * 2 <= maxHp; // 절반 이상 닳음
                    _eggTextures[index].SetActive(damaged);
                }
            }

            for (int i = 0; i < _eggs.Count; i++)
                if (_eggs[i] != null && _eggs[i].activeSelf) return true;
            return false;
        }

        private void Clear()
        {
            for (int i = 0; i < _eggs.Count; i++)
                if (_eggs[i] != null) Destroy(_eggs[i]);
            _eggs.Clear();
            _eggTextures.Clear();
            _eggMaxHps.Clear();
        }

        // Cylinder(몸체)만 색 적용, texture(균열)는 비활성으로 시작.
        // 링크된 경로(bodyPath/texPath) 우선, 없으면 이름으로 fallback.
        private void SetupEggVisual(GameObject egg, int colorIndex, List<int> bodyPath, List<int> texPath, out GameObject textureChild)
        {
            textureChild = null;

            Transform body = ResolveChild(egg.transform, bodyPath, _bodyChildName);
            Transform tex = ResolveChild(egg.transform, texPath, _textureChildName);

            Color c = BalloonController.BalloonColors[
                Mathf.Clamp(colorIndex, 0, BalloonController.BalloonColors.Length - 1)];
            Material mat = BalloonController.GetOrCreateSharedMaterial(c);

            // 색은 Cylinder 에만 (못 찾으면 texture 제외한 알 전체 폴백).
            if (body != null) ApplyMatToRenderers(body.gameObject, mat);
            else if (mat != null) ApplyMatToRenderersExcept(egg, mat, tex);

            if (tex != null)
            {
                tex.gameObject.SetActive(false); // 평소 비활성
                textureChild = tex.gameObject;
            }
        }

        // 클론에서 자식 해석: 인덱스 경로(링크) 우선 → 없으면 이름으로 Find.
        private static Transform ResolveChild(Transform cloneRoot, List<int> indexPath, string fallbackName)
        {
            if (indexPath != null)
            {
                Transform t = cloneRoot;
                bool ok = true;
                for (int i = 0; i < indexPath.Count; i++)
                {
                    int idx = indexPath[i];
                    if (idx < 0 || idx >= t.childCount) { ok = false; break; }
                    t = t.GetChild(idx);
                }
                if (ok && t != cloneRoot) return t;
            }
            return !string.IsNullOrEmpty(fallbackName) ? cloneRoot.Find(fallbackName) : null;
        }

        // target 의 root 기준 자식 sibling-index 경로. target 이 root 하위가 아니면 null.
        private static List<int> GetChildIndexPath(Transform root, Transform target)
        {
            if (root == null || target == null) return null;
            var path = new List<int>();
            Transform t = target;
            while (t != null && t != root)
            {
                path.Insert(0, t.GetSiblingIndex());
                t = t.parent;
            }
            return (t == root && path.Count > 0) ? path : null;
        }

        private static void ApplyMatToRenderers(GameObject go, Material mat)
        {
            if (mat == null) return;
            var rends = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
                if (rends[i] != null) rends[i].sharedMaterial = mat;
        }

        private static void ApplyMatToRenderersExcept(GameObject root, Material mat, Transform exclude)
        {
            var rends = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                if (exclude != null && rends[i].transform.IsChildOf(exclude)) continue;
                rends[i].sharedMaterial = mat;
            }
        }

        private void MeasureTemplateSize(out float sizeX, out float sizeZ)
        {
            sizeX = 0f; sizeZ = 0f;
            var rends = _eggTemplate.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return;
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            sizeX = b.size.x; sizeZ = b.size.z;
        }

        // 틀의 renderer bounds → footprint(w*cellSizeX × h*cellSizeZ)에 맞춰 스케일.
        private void ScaleFrameToFootprint(int w, int h, float cellSizeX, float cellSizeZ)
        {
            if (_frame == null) return;
            var rends = _frame.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return;

            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            Vector3 cur = _frame.lossyScale;
            float baseX = cur.x != 0f ? b.size.x / cur.x : b.size.x;
            float baseZ = cur.z != 0f ? b.size.z / cur.z : b.size.z;
            if (baseX <= 0.0001f || baseZ <= 0.0001f) return;

            float targetX = w * cellSizeX;
            float targetZ = h * cellSizeZ;
            Vector3 ls = _frame.localScale;
            _frame.localScale = new Vector3(targetX / baseX, ls.y, targetZ / baseZ);
            _frame.localPosition = Vector3.zero;
        }
    }
}
