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
        private readonly List<Renderer> _eggTexRenderers = new List<Renderer>();

        // [균열 단계 텍스처] paint0(데미지 시작) → paint1 → paint2(저체력). 풀피 = texture 비활성(균열 없음).
        // CrackOverlay 셰이더 = Custom/SpriteInstanced, [MainTexture] _BaseMap. 알별 독립 교체는 MaterialPropertyBlock.
        private static Texture[] s_crackTex;
        private static Texture[] CrackTex => s_crackTex ??= new[]
        {
            Resources.Load<Texture>("Texture/paint0"),
            Resources.Load<Texture>("Texture/paint1"),
            Resources.Load<Texture>("Texture/paint2"),
        };
        private static readonly int CrackBaseMap = Shader.PropertyToID("_BaseMap");
        private MaterialPropertyBlock _crackMpb;

        public int EggCount => _eggs.Count;

        /// <summary>
        /// 알 배치 — eggColors 항목 수(N)만큼. Cylinder 만 색칠, texture 는 비활성으로 시작.
        /// </summary>
        public void Build(int w, int h, int[] eggColors, int[] eggHps, float cellSizeX, float cellSizeZ, float targetEggWorldScaleY = -1f)
        {
            Clear();
            ResolvePaintBoxLinks();

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
            // ROLLBACK_TARGETBOX_BALANCED_EGG_LAYOUT_20260608:
            // Egg count is authored in MapMaker; choose a balanced centered grid for that count.
            ChooseEggGrid(n, w, h, out int cols, out int rows);
            // 틀(paintbox)을 footprint 에 맞춰 먼저 스케일 → 이후 알을 '틀의 실제 안쪽 영역'에 맞춘다.
            // 알 크기를 footprint(cellSize) 가 아니라 '틀의 실제 bounds' 기준으로 잡아야, 틀이 footprint 와
            // 다른 크기(아트가 더 작거나, _frame 미와이어로 스케일 안 됨)여도 알이 항상 틀 안에 들어간다.
            ScaleFrameToFootprint(w, h, cellSizeX, cellSizeZ);

            // 알 격자가 들어갈 영역(월드 크기): 틀이 있으면 틀의 실제 bounds, 없으면 footprint 로 폴백.
            float areaX, areaZ;
            if (!TryGetFrameWorldSize(out areaX, out areaZ))
            {
                areaX = w * cellSizeX;
                areaZ = h * cellSizeZ;
            }

            // 격자 한 칸 = 영역 안쪽(_innerAreaRatio)을 cols×rows 로 나눈 크기. 테두리 여백 확보.
            float ir = Mathf.Clamp(_innerAreaRatio, 0.1f, 1f);
            // ROLLBACK_TARGETBOX_AUTHORED_EGG_SCALE_20260623:
            // Eggs divide the actual inner paintbox area by authored egg count. This restores
            // the previous TargetBox behavior where a 2x1 box with fewer eggs scales eggs up
            // inside the box instead of clamping every egg to one board cell.
            float layoutCellW = (areaX * ir) / Mathf.Max(1, cols);
            float layoutCellZ = (areaZ * ir) / Mathf.Max(1, rows);
            float eggCellW = layoutCellW;
            float eggCellZ = layoutCellZ;

            // 템플릿 월드 bounds 측정 → 격자 칸에 맞출 스케일 계수 산출. 측정 위해 잠깐 활성화.
            bool tplWasActive = _eggTemplate.activeSelf;
            if (!tplWasActive) _eggTemplate.SetActive(true);
            float tplSizeX, tplSizeZ;
            MeasureTemplateSize(out tplSizeX, out tplSizeZ);

            _eggTemplate.SetActive(false); // 원본 숨김(복제본만 표시)

            HideAuthoredEggSamples();

            Vector3 tplScale = _eggTemplate.transform.localScale;
            Quaternion tplRot = _eggTemplate.transform.localRotation;
            float tplY = _eggTemplate.transform.localPosition.y;

            // 격자 칸(월드)에 맞춘 균일 스케일 계수 — 작은 축 기준으로 셀 안에 들어가게.
            float fitK = 1f;
            if (tplSizeX > 0.0001f && tplSizeZ > 0.0001f)
                fitK = Mathf.Min(eggCellW / tplSizeX, eggCellZ / tplSizeZ) * _eggFillRatio;
            // NOTE: eggScale(scaleMult)을 곱하지 않는다 — cellSizeX/Z 가 이미 widthMult/heightMult 를 포함하므로
            //       여기서 또 곱하면 이중 적용되어 알이 paintbox 를 벗어난다.
            Vector3 eggScale = tplScale * fitK;
            if (targetEggWorldScaleY > 0f)
            {
                // ROLLBACK_GIMMICK_HEIGHT_RATIO_20260629:
                // Target Box paint/cylinder height is balloon scaleY * 2.857.
                // Convert the requested world Y height to local scale. X/Z still use fitK.
                float parentY = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));
                eggScale.y = targetEggWorldScaleY / parentY;
            }

            // 월드 격자 간격 → 로컬 단위(부모 스케일 보정).
            Vector3 ls = transform.lossyScale;
            float localGridX = layoutCellW / Mathf.Max(0.0001f, Mathf.Abs(ls.x));
            float localGridZ = layoutCellZ / Mathf.Max(0.0001f, Mathf.Abs(ls.z));

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
                egg.transform.localScale = eggScale;
                int rowStart = gr * cols;
                int rowCount = Mathf.Min(cols, n - rowStart);
                float ox = (gc - (rowCount - 1) * 0.5f) * localGridX;
                float oz = (gr - (rows - 1) * 0.5f) * localGridZ;
                egg.transform.localPosition = new Vector3(ox, tplY, oz);

                int color = (eggColors != null && i < eggColors.Length) ? eggColors[i] : 0;
                int maxHp = (eggHps != null && i < eggHps.Length && eggHps[i] > 0) ? eggHps[i] : 1;

                GameObject texChild;
                SetupEggVisual(egg, color, bodyPath, texPath, out texChild);

                _eggs.Add(egg);
                _eggTextures.Add(texChild);
                _eggMaxHps.Add(maxHp);
                // [균열 단계] texture 자식의 Renderer 캐시 — HP 단계별 _BaseMap 텍스처 교체용.
                _eggTexRenderers.Add(texChild != null ? texChild.GetComponentInChildren<Renderer>(true) : null);
            }
            // 틀 스케일은 위(격자 산출 전)에서 이미 수행했다.
        }

        /// <summary>MapMaker가 지정한 egg 개수를 footprint 비율에 맞춰 균형 격자로 배치한다.</summary>
        private static void ChooseEggGrid(int count, int footprintW, int footprintH, out int cols, out int rows)
        {
            count = Mathf.Max(1, count);
            footprintW = Mathf.Max(1, footprintW);
            footprintH = Mathf.Max(1, footprintH);

            float targetAspect = (float)footprintW / footprintH;
            cols = count;
            rows = 1;
            float bestScore = float.MaxValue;

            for (int candidateRows = 1; candidateRows <= count; candidateRows++)
            {
                int candidateCols = Mathf.CeilToInt((float)count / candidateRows);
                int emptySlots = candidateCols * candidateRows - count;
                float aspect = (float)candidateCols / candidateRows;
                float score = Mathf.Abs(Mathf.Log(Mathf.Max(0.0001f, aspect / targetAspect))) + emptySlots * 0.08f;

                if (score < bestScore - 0.0001f ||
                    (Mathf.Abs(score - bestScore) <= 0.0001f && candidateCols > cols))
                {
                    bestScore = score;
                    cols = candidateCols;
                    rows = candidateRows;
                }
            }
        }

        private void HideAuthoredEggSamples()
        {
            if (_eggTemplate == null) return;

            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child == null || child.gameObject == _eggTemplate) continue;
                if (_frame != null && (child == _frame || child.IsChildOf(_frame))) continue;

                if (LooksLikeEggSample(child))
                    child.gameObject.SetActive(false);
            }
        }

        private bool LooksLikeEggSample(Transform root)
        {
            if (root == null) return false;
            if (!string.IsNullOrEmpty(_bodyChildName) && FindChildRecursive(root, _bodyChildName) != null) return true;
            if (!string.IsNullOrEmpty(_textureChildName) && FindChildRecursive(root, _textureChildName) != null) return true;
            return false;
        }

        /// <summary>틀(_frame)의 현재 renderer world bounds 크기. 미와이어/렌더러 없음/0크기면 false.</summary>
        private bool TryGetFrameWorldSize(out float sizeX, out float sizeZ)
        {
            sizeX = 0f; sizeZ = 0f;
            if (_frame == null) return false;
            var rends = _frame.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return false;
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            if (b.size.x <= 0.0001f || b.size.z <= 0.0001f) return false;
            sizeX = b.size.x; sizeZ = b.size.z;
            return true;
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
                    // ROLLBACK_PINATABOX_CRACK_STAGE_20260608: 이전 = 절반 이하면 texture 단일 토글.
                    // 변경(박지수 명세): 풀피=비활성(균열X), 데미지 받으면 paint0→paint1→paint2 (3등분, 저체력일수록 paint2).
                    int maxHp = index < _eggMaxHps.Count && _eggMaxHps[index] > 0 ? _eggMaxHps[index] : currentHp;
                    if (currentHp >= maxHp)
                    {
                        _eggTextures[index].SetActive(false);   // 풀피 = 균열 없음
                    }
                    else
                    {
                        float ratio = (float)currentHp / Mathf.Max(1, maxHp);
                        int stage = Mathf.Clamp(Mathf.FloorToInt((1f - ratio) * 3f), 0, 2); // 데미지 시작=paint0
                        _eggTextures[index].SetActive(true);
                        ApplyCrackTexture(index, stage);
                    }
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
            _eggTexRenderers.Clear();
        }

        /// <summary>[균열 단계] 알 index 의 texture Renderer 의 _BaseMap 을 paint{stage} 로 교체 (MaterialPropertyBlock — 공유 머티리얼 오염 없음).</summary>
        private void ApplyCrackTexture(int index, int stage)
        {
            if (index < 0 || index >= _eggTexRenderers.Count) return;
            var r = _eggTexRenderers[index];
            var tex = CrackTex;
            if (r == null || tex == null || stage < 0 || stage >= tex.Length || tex[stage] == null) return;
            _crackMpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(_crackMpb);
            _crackMpb.SetTexture(CrackBaseMap, tex[stage]);
            r.SetPropertyBlock(_crackMpb);
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
            else if (mat != null) ApplyMatToRenderersExcept(egg, mat, tex, GetFrameNameForExclusion());

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
            return !string.IsNullOrEmpty(fallbackName) ? FindChildRecursive(cloneRoot, fallbackName) : null;
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

        private static void ApplyMatToRenderersExcept(GameObject root, Material mat, Transform exclude, string excludeName)
        {
            var rends = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                if (exclude != null && rends[i].transform.IsChildOf(exclude)) continue;
                if (!string.IsNullOrEmpty(excludeName) && IsSelfOrParentNamed(rends[i].transform, excludeName)) continue;
                rends[i].sharedMaterial = mat;
            }
        }

        private void MeasureTemplateSize(out float sizeX, out float sizeZ)
        {
            sizeX = 0f; sizeZ = 0f;
            var rends = _eggTemplate.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return;
            string frameName = GetFrameNameForExclusion();
            bool hasBounds = false;
            Bounds b = default;
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                if (!string.IsNullOrEmpty(frameName) && IsSelfOrParentNamed(rends[i].transform, frameName)) continue;

                if (!hasBounds)
                {
                    b = rends[i].bounds;
                    hasBounds = true;
                }
                else
                {
                    b.Encapsulate(rends[i].bounds);
                }
            }
            if (!hasBounds) return;
            sizeX = b.size.x; sizeZ = b.size.z;
        }

        private void ResolvePaintBoxLinks()
        {
            // ROLLBACK_TARGETBOX_PREFAB_LINK_REPAIR_20260609:
            // paint.prefab should keep "paintbox" as a frame only. Build must clone/color the
            // egg template, not the frame, even if the prefab links were left empty or miswired.
            if (_frame == null)
                _frame = FindChildRecursive(transform, "paintbox");

            bool templateIsFrame = _eggTemplate != null
                && _frame != null
                && (_eggTemplate.transform == _frame || _eggTemplate.transform.IsChildOf(_frame));

            if (_eggTemplate == null || templateIsFrame || _eggTemplate.transform == transform)
            {
                Transform candidate = FindEggTemplateCandidate(transform);
                if (candidate != null)
                    _eggTemplate = candidate.gameObject;
            }

            if (_eggTemplate != null)
            {
                if (_bodyOnTemplate == null || !_bodyOnTemplate.IsChildOf(_eggTemplate.transform))
                    _bodyOnTemplate = FindChildRecursive(_eggTemplate.transform, _bodyChildName);
                if (_textureOnTemplate == null || !_textureOnTemplate.IsChildOf(_eggTemplate.transform))
                    _textureOnTemplate = FindChildRecursive(_eggTemplate.transform, _textureChildName);
            }
        }

        private Transform FindEggTemplateCandidate(Transform root)
        {
            if (root == null) return null;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child == null) continue;
                if (_frame != null && (child == _frame || child.IsChildOf(_frame))) continue;

                if (FindChildRecursive(child, _bodyChildName) != null
                    || FindChildRecursive(child, _textureChildName) != null)
                {
                    return child;
                }
            }

            return null;
        }

        private string GetFrameNameForExclusion()
        {
            return _frame != null && !string.IsNullOrEmpty(_frame.name) ? _frame.name : "paintbox";
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName)) return null;
            if (root.name == childName) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildRecursive(root.GetChild(i), childName);
                if (found != null) return found;
            }

            return null;
        }

        private static bool IsSelfOrParentNamed(Transform transform, string name)
        {
            while (transform != null)
            {
                if (transform.name == name) return true;
                transform = transform.parent;
            }
            return false;
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
