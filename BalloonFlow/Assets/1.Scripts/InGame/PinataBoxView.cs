using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

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
        [Tooltip("박스 틀(paintbox) Transform. footprint 크기에 맞춰 자동 스케일. 비워두면 스킵.\n" +
                 "ROLLBACK_PAINTBOX_RATIO_MODELS_20260701: 아래 비율별 모델(1x3/2x2/2x3)이 링크돼 있으면 Build 가 비율에 맞는 모델을 골라 이 필드에 런타임 대입한다. 셋 다 비면 이 _frame 을 그대로 폴백 사용.")]
        [SerializeField] private Transform _frame;
        // ROLLBACK_PAINTBOX_RATIO_MODELS_20260701:
        //   인게임 실사용 비율은 1×3 / 2×2 / 2×3 3종뿐. 단일 프레임을 임의 W×H 로 늘리면 테두리 아트가 왜곡되므로,
        //   비율별 전용 모델을 링크해 MapMaker 에서 세팅한 paint 의 W×H(레벨데이터)에 맞는 모델을 선택한다.
        //   ※ 모델은 '세로(portrait)' 방향 authoring(긴 축 = +Z). target 이 가로(w>h)면 Build 가 Y축 90° 회전.
        [Tooltip("[비율 모델] 1×3 박스 모델(세로 authoring: 1칸 너비×3칸 길이, 긴 축 +Z).")]
        [SerializeField] private Transform _box1x3;
        [Tooltip("[비율 모델] 2×2 박스 모델(정사각).")]
        [SerializeField] private Transform _box2x2;
        [Tooltip("[비율 모델] 2×3 박스 모델(세로 authoring: 2칸 너비×3칸 길이, 긴 축 +Z).")]
        [SerializeField] private Transform _box2x3;
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

        // ROLLBACK_PAINTBOX_FX_20260630:
        //   1) 프레임(paintbox) 색 = #5E6998 (기존 머티리얼 유지, _BaseColor 만 MPB tint)
        //   2) egg 색 = Cylinder 의 'PaintColor' 머티리얼 슬롯에만 _BaseColor tint (Paint/PaintColor 머티리얼·Base Map 유지)
        //   4) 금 갈 때마다 위로 0.113 올라갔다 복귀 + HitParticle / 박스 제거 시 EndParticle (egg마다)
        private static readonly Color PAINTBOX_FRAME_COLOR = new Color32(0x5E, 0x69, 0x98, 0xFF);
        private const float EGG_HIT_RISE_Y = 0.113f;     // 금 갈 때 위로 이동량(로컬). 정확치 아닌 육안 연출용.
        private const string PAINTCOLOR_MAT_HINT = "PaintColor"; // 셀 색을 입힐 머티리얼 슬롯 이름 힌트
        private const string FX_HIT_NAME = "HitParticle";
        private const string FX_END_NAME = "EndParticle";
        private static readonly int EggBaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EggColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock _eggColorMpb;
        private MaterialPropertyBlock _frameColorMpb;
        private readonly List<Vector3> _eggBaseLocalPos = new List<Vector3>(); // 알 기준 localPosition (금연출 복귀 기준)
        // ROLLBACK_PAINTBOX_EGG_ENDPARTICLE_20260630:
        //   EndParticle 을 '박스 제거' 가 아니라 'egg 가 하나씩 죽을 때' 재생 + egg 색 적용. 박스 제거는 스케일만.
        //   최적화: Instantiate 안 쓰고 egg 의 EndParticle 을 월드로 분리 재사용, 색/재생은 재사용 MPB·List·단일 패스.
        private readonly List<Color> _eggColors = new List<Color>(); // egg 색(EndParticle tint 용)
        private MaterialPropertyBlock _particleColorMpb;
        private readonly List<ParticleSystem> _psScratch = new List<ParticleSystem>(8); // 비할당 재사용
        private static readonly int ParticleTintColorId = Shader.PropertyToID("_TintColor");
        private const bool PAINTBOX_DEBUG = false; // ROLLBACK_PAINTBOX_DIAG_20260630: 배치 진단 로그(원인 확인용). 필요 시 true.

        private static string DiagPath(Transform t)
        {
            if (t == null) return "null";
            string p = t.name;
            Transform c = t.parent;
            while (c != null) { p = c.name + "/" + p; c = c.parent; }
            return p;
        }

        public int EggCount => _eggs.Count;

        /// <summary>
        /// 알 배치 — eggColors 항목 수(N)만큼. Cylinder 만 색칠, texture 는 비활성으로 시작.
        /// </summary>
        public void Build(int w, int h, int[] eggColors, int[] eggHps, float cellSizeX, float cellSizeZ, float targetEggWorldScaleY = -1f, float targetEggWorldScaleXZ = -1f)
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
            // ROLLBACK_PAINTBOX_RATIO_MODELS_20260701: 레벨데이터 W×H 비율로 전용 박스 모델 선택 → _frame 에 대입(나머지 비활성).
            //   모델은 세로 authoring(긴 축 +Z)이라, target 이 가로(w>h)면 아래 fit 에서 Y축 90° 회전으로 방향만 맞춘다.
            SelectBoxModel(w, h);
            // 틀(paintbox)을 footprint 에 맞춰 먼저 스케일 → 이후 알을 '틀의 실제 안쪽 영역'에 맞춘다.
            // 알 크기를 footprint(cellSize) 가 아니라 '틀의 실제 bounds' 기준으로 잡아야, 틀이 footprint 와
            // 다른 크기(아트가 더 작거나, _frame 미와이어로 스케일 안 됨)여도 알이 항상 틀 안에 들어간다.
            // ROLLBACK_PAINTBOX_RATIO_MODELS_20260701: 회전 방향은 ScaleFrameToFootprint 가 모델 실측 종횡비로 자동 판별
            //   (portrait/landscape authoring 가정 없이 프레임 긴 축을 footprint 긴 축에 맞춤).
            ScaleFrameToFootprint(w, h, cellSizeX, cellSizeZ);
            ApplyFrameColor(); // req1: 프레임 색 #5E6998 (기존 머티리얼 유지)

            // ROLLBACK_PAINTBOX_EGG_AREA_FOOTPRINT_20260630:
            //   알 배치 영역 = '오토링된 footprint(w×h 셀)'. 이전엔 프레임 실측 bounds 를 썼는데, 새 paintbox
            //   메시가 world-Z 를 localScale.z 로 못 키워(평면/회전) 프레임 world 크기가 어긋나면 알이 그만큼
            //   퍼져 범위를 벗어났다. footprint 기준으로 잡아 프레임 시각 상태와 무관하게 알이 정확한 칸에 놓이게.
            float areaX = w * cellSizeX;
            float areaZ = h * cellSizeZ;

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
            // ROLLBACK_PAINTBOX_EGG_PLUMP_20260701: egg 를 조금 더 도톰하게(x,z 10% 확대). 셀을 살짝 넘겨 통통하게 채움.
            const float EGG_PLUMP_MULT = 1.1f;
            // ROLLBACK_PAINTBOX_EGG_SPACING_20260702:
            // Spread paint eggs slightly farther apart while keeping count/layout logic unchanged.
            const float EGG_GRID_SPACING_MULT = 1.08f;
            float fitK = 1f;
            if (tplSizeX > 0.0001f && tplSizeZ > 0.0001f)
                fitK = Mathf.Min(eggCellW / tplSizeX, eggCellZ / tplSizeZ) * _eggFillRatio * EGG_PLUMP_MULT;
            // NOTE: eggScale(scaleMult)을 곱하지 않는다 — cellSizeX/Z 가 이미 widthMult/heightMult 를 포함하므로
            //       여기서 또 곱하면 이중 적용되어 알이 paintbox 를 벗어난다.
            Vector3 eggScale = tplScale * fitK;
            // ROLLBACK_PAINTBOX_EGG_FIT_CELL_20260701:
            // Start from the box cell fit, then optionally override X/Z with the requested balloon
            // ratio below. Y height keeps the separate art ratio path.
            // ROLLBACK_PAINTBOX_EGG_XZ_BALLOON_RATIO_20260702:
            // Use BalloonController's requested world X/Z diameter (balloon scaleX * 1.736) instead of
            // discarding targetEggWorldScaleXZ. Gameplay HP/targeting stays in BalloonData; this is visual-only.
            if (targetEggWorldScaleXZ > 0f && tplSizeX > 0.0001f && tplSizeZ > 0.0001f)
            {
                float fitKxz = Mathf.Min(targetEggWorldScaleXZ / tplSizeX, targetEggWorldScaleXZ / tplSizeZ);
                eggScale.x = tplScale.x * fitKxz;
                eggScale.z = tplScale.z * fitKxz;
            }
            // ROLLBACK_PAINTBOX_EGG_CLAMP_BOX_20260702: Paint 실린더가 Box(외곽)를 넘지 않게 셀 fit(×plump) 상한으로 클램프.
            //   위에서 targetEggWorldScaleXZ(풍선 지름 ×1.736)로 X/Z를 덮어쓰는데, 이 값이 셀보다 크면 실린더가 박스를 벗어난다.
            //   → 셀 fit×EGG_PLUMP_MULT(=fitK) 까지만 허용. fitK=cell fit×fillRatio×1.1 이라 인접 실린더끼리 '조금 겹치는' 정도는
            //   유지되면서(통통), 박스 밖으로는 안 나간다. (겹침을 더/덜 원하면 EGG_PLUMP_MULT 179 라인 조정.)
            eggScale.x = Mathf.Min(eggScale.x, tplScale.x * fitK);
            eggScale.z = Mathf.Min(eggScale.z, tplScale.z * fitK);
            if (targetEggWorldScaleY > 0f)
            {
                // ROLLBACK_GIMMICK_HEIGHT_RATIO_20260629:
                // Target Box paint/cylinder height is balloon scaleY * 2.857.
                // Convert the requested world Y height to local scale.
                float parentY = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));
                eggScale.y = targetEggWorldScaleY / parentY;
            }

            // 월드 격자 간격 → 로컬 단위(부모 스케일 보정).
            Vector3 ls = transform.lossyScale;
            float localGridX = (layoutCellW * EGG_GRID_SPACING_MULT) / Mathf.Max(0.0001f, Mathf.Abs(ls.x));
            float localGridZ = (layoutCellZ * EGG_GRID_SPACING_MULT) / Mathf.Max(0.0001f, Mathf.Abs(ls.z));

            if (PAINTBOX_DEBUG)
            {
                Debug.Log($"[PaintBox-DIAG] root={DiagPath(transform)} eggTemplate={DiagPath(_eggTemplate.transform)} " +
                          $"frame={(_frame != null ? DiagPath(_frame) : "null")} frameScale={(_frame != null ? _frame.localScale.ToString("F3") : "-")}\n" +
                          $"  w={w} h={h} n={n} cols={cols} rows={rows} cell=({cellSizeX:F3},{cellSizeZ:F3}) area=({areaX:F3},{areaZ:F3}) " +
                          $"layoutCell=({layoutCellW:F3},{layoutCellZ:F3})\n" +
                          $"  tplSize=({tplSizeX:F3},{tplSizeZ:F3}) tplScale={tplScale.ToString("F3")} fitK={fitK:F3} eggScale={eggScale.ToString("F3")} " +
                          $"localGrid=({localGridX:F3},{localGridZ:F3}) rootLossy={ls.ToString("F3")}", this);
            }

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
                _eggBaseLocalPos.Add(egg.transform.localPosition); // 금연출 Y 복귀 기준
                _eggColors.Add(BalloonController.BalloonColors[
                    Mathf.Clamp(color, 0, BalloonController.BalloonColors.Length - 1)]); // EndParticle tint 용
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
                // ROLLBACK_PAINTBOX_NESTED_TEMPLATE_20260630: 프레임(paintbox)을 포함한 노드(래퍼)는 숨기면
                //   프레임까지 사라지므로 제외. 안쪽 알 원본은 Build 의 _eggTemplate.SetActive(false) 로 이미 숨김.
                if (_frame != null && _frame.IsChildOf(child)) continue;

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
                // ROLLBACK_GIMMICK_SFX_TABLE_20260703:
                // Paint hit SFX is per egg and is emitted only when runtime HP is updated by a dart hit.
                if (AudioManager.HasInstance) AudioManager.Instance.PlayPaintHit();
                if (currentHp <= 0)
                {
                    // ROLLBACK_PAINTBOX_EGG_ENDPARTICLE_20260630: egg 죽을 때 그 egg 색의 EndParticle 재생 후 비활성.
                    if (AudioManager.HasInstance) AudioManager.Instance.PlayPaintDisappear();
                    PlayEggEndParticle(index);
                    _eggs[index].SetActive(false);
                }
                else
                {
                    if (index < _eggTextures.Count && _eggTextures[index] != null)
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
                    // ROLLBACK_PAINTBOX_HIT_FX_20260630: 살아있는(금 가는) 히트마다 Y 0→0.113→0 + HitParticle
                    PlayEggCrackFx(index);
                }
            }

            for (int i = 0; i < _eggs.Count; i++)
                if (_eggs[i] != null && _eggs[i].activeSelf) return true;
            return false;
        }

        private void Clear()
        {
            for (int i = 0; i < _eggs.Count; i++)
                if (_eggs[i] != null) { _eggs[i].transform.DOKill(); Destroy(_eggs[i]); }
            _eggs.Clear();
            _eggBaseLocalPos.Clear();
            _eggColors.Clear();
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
            // ROLLBACK_PAINTBOX_PAINTCOLOR_MAT_20260630:
            //   머티리얼 교체 금지(기존 PaintColor/Paint 유지 = Base Map 으로 색 날아감 방지).
            //   Cylinder 의 'PaintColor' 머티리얼 슬롯에만 셀 색을 MPB(_BaseColor)로 tint. 흰색(Paint)은 그대로.
            if (body != null) TintPaintColorSlots(body.gameObject, c);
            else TintPaintColorSlots(egg, c);

            if (tex != null)
            {
                tex.gameObject.SetActive(false); // 평소 비활성
                textureChild = tex.gameObject;
            }

            // ROLLBACK_PAINTBOX_EGG_ENDPARTICLE_20260630: HitParticle/EndParticle 은 스폰 시 비활성(자동재생 방지).
            //   균열 시 HitParticle, 죽을 때 EndParticle 만 재생.
            Transform hitFx = FindChildRecursive(egg.transform, FX_HIT_NAME);
            if (hitFx != null) hitFx.gameObject.SetActive(false);
            Transform endFx = FindChildRecursive(egg.transform, FX_END_NAME);
            if (endFx != null) endFx.gameObject.SetActive(false);
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
            bool hasBounds = false;
            Bounds b = default;
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                // ROLLBACK_PAINTBOX_FX_LAYOUT_EXCLUDE_20260630: 프레임 + 파티클(HitParticle/EndParticle)은
                //   '실제 Paint 몸체' 가 아니므로 알 크기 측정에서 제외(파티클이 끼어 배치 깨지는 이슈 방지).
                if (rends[i] is ParticleSystemRenderer) continue;
                if (IsExcludedFromEggBounds(rends[i].transform)) continue;

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

            // ROLLBACK_PAINTBOX_NESTED_TEMPLATE_20260630:
            //   _eggTemplate 이 프레임을 '포함' 하면(=래퍼 paint(1) 를 잘못 가리킴) 재해석한다.
            bool templateContainsFrame = _eggTemplate != null && _frame != null
                && _frame.IsChildOf(_eggTemplate.transform);

            if (_eggTemplate == null || templateIsFrame || templateContainsFrame || _eggTemplate.transform == transform)
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

                bool hasBody = FindChildRecursive(child, _bodyChildName) != null
                            || FindChildRecursive(child, _textureChildName) != null;
                if (!hasBody) continue;

                // ROLLBACK_PAINTBOX_NESTED_TEMPLATE_20260630:
                //   프리팹이 root → 래퍼(paint(1)) → {paintbox, egg} 처럼 한 겹 더 감싸면, child(=래퍼)가
                //   프레임(paintbox)을 '포함' 한다. 그 경우 래퍼를 통째로 잡지 말고 더 내려가 '프레임을 포함하지
                //   않는 안쪽 알' 을 템플릿으로 찾는다. (평면 구조면 그대로 child 반환 — 기존 동작 동일.)
                bool containsFrame = _frame != null && _frame.IsChildOf(child);
                if (containsFrame)
                {
                    Transform inner = FindEggTemplateCandidate(child);
                    if (inner != null) return inner;
                    continue;
                }

                return child;
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

        // 틀의 renderer world bounds → footprint(w*cellSizeX × h*cellSizeZ)에 맞춰 스케일.
        // ROLLBACK_PAINTBOX_RATIO_MODELS_20260701:
        //   레벨데이터 W×H 의 '종횡비(aspect = 짧은변/긴변)' 로 가장 가까운 전용 모델을 골라 _frame 에 대입, 나머지 비활성.
        //   ※ 정확한 셀 수가 아니라 '비율' 매칭이라 크기 무관하게 확대 대응:
        //     4×4(1:1)→2×2 확대 / 4×6·6×4(2:3)→2×3 확대(6×4 는 아래 fit 에서 자동 90° 회전) / 1×3·2×3 등도 동일.
        //   방향(가로/세로)은 min/max 로 흡수 → 회전은 ScaleFrameToFootprint 가 모델 실측 종횡비로 자동 판별.
        //   3모델 전부 미링크면 no-op → 기존 단일 _frame(paintbox) 폴백.
        private void SelectBoxModel(int w, int h)
        {
            if (_box1x3 == null && _box2x2 == null && _box2x3 == null)
                return; // 비율 모델 미사용 → 기존 단일 _frame 유지

            // target 종횡비(짧은변/긴변). 4×4→1.0, 4×6/6×4→0.667, 1×3/3×1→0.333, 2×3→0.667 …
            float targetAspect = (float)Mathf.Min(w, h) / Mathf.Max(1, Mathf.Max(w, h));
            Transform sel = null;
            float bestD = float.MaxValue;
            void Consider(Transform t, float modelAspect)
            {
                if (t == null) return;
                float d = Mathf.Abs(targetAspect - modelAspect);
                if (d < bestD) { bestD = d; sel = t; }
            }
            Consider(_box1x3, 1f / 3f); // 0.333
            Consider(_box2x2, 1f);      // 1.0
            Consider(_box2x3, 2f / 3f); // 0.667

            SetBoxActive(_box1x3, ReferenceEquals(_box1x3, sel));
            SetBoxActive(_box2x2, ReferenceEquals(_box2x2, sel));
            SetBoxActive(_box2x3, ReferenceEquals(_box2x3, sel));

            if (sel != null)
                _frame = sel; // 선택 모델을 _frame 으로 → 이하 스케일/색/균열/제외 로직 그대로 재사용

            if (bestD > 0.12f) // 세 모델 어느 aspect 와도 꽤 멀면(비정상 비율) 경고 — fit 이 늘려 왜곡될 수 있음
                Debug.LogWarning($"[PinataBoxView] 박스 비율 {w}×{h}(aspect {targetAspect:F2}) 이 1×3/2×2/2×3 어느 것과도 멀어 왜곡 가능.", this);
        }

        private static void SetBoxActive(Transform box, bool active)
        {
            if (box != null && box.gameObject.activeSelf != active)
                box.gameObject.SetActive(active);
        }

        // ROLLBACK_PAINTBOX_UNIFORM_FIT_ROT_20260701: 모델 authored 회전(X=-90 로 눕힘)·스케일의 진짜 base 를 static 캐시(최초 fresh 캡처).
        //   Target Box 풀(DontDestroyOnLoad) 재사용 시 이전 Build 의 Y회전/fit 스케일이 남아 현재 localRotation/Scale 이 오염되므로,
        //   base 는 여기서만 읽고 매 Build 시작에 base 로 리셋한 뒤 맞춘다(재사용에도 정확).
        private static readonly Dictionary<Transform, Quaternion> _modelAuthoredRot = new Dictionary<Transform, Quaternion>();
        private static readonly Dictionary<Transform, Vector3> _modelAuthoredScale = new Dictionary<Transform, Vector3>();

        private void ScaleFrameToFootprint(int w, int h, float cellSizeX, float cellSizeZ)
        {
            if (_frame == null) return;

            // ROLLBACK_PAINTBOX_UNIFORM_FIT_ROT_20260701:
            //   모델은 X=-90 로 눕혀 authoring. 방향(가로/세로)이 target 과 어긋나면 '늘리기 대신' 세계-Y 90° 회전으로 눕힌 모델을
            //   돌린 뒤, aspect 는 SelectBoxModel 로 이미 맞으므로 UNIFORM 스케일(배수)만 곱해 footprint 를 채운다.
            //   → 세로 1×3 을 가로로 세팅하면 3배 늘리는 게 아니라 '회전 + 배수'로 정확히 나온다(사용자 명세). 2×6=가세로 2배 등도 동일.
            float targetX = w * cellSizeX;
            float targetZ = h * cellSizeZ;

            // authored base(회전·스케일) — 최초 fresh 캡처(static) 후 매번 base 로 리셋.
            if (!_modelAuthoredRot.TryGetValue(_frame, out Quaternion baseRot)) { baseRot = _frame.localRotation; _modelAuthoredRot[_frame] = baseRot; }
            if (!_modelAuthoredScale.TryGetValue(_frame, out Vector3 baseScale)) { baseScale = _frame.localScale; _modelAuthoredScale[_frame] = baseScale; }
            _frame.localRotation = baseRot;
            _frame.localScale = baseScale;

            // 방향 판별 — authored base 상태의 종횡비(world X vs Z)의 긴 축 vs target 긴 축.
            bool rotate90 = false;
            if (TryGetFrameWorldSize(out float ax, out float az) && ax > 0.0001f && az > 0.0001f)
            {
                bool modelLongX = ax > az * 1.05f;
                bool modelLongZ = az > ax * 1.05f;
                bool targetLongX = targetX > targetZ * 1.05f;
                bool targetLongZ = targetZ > targetX * 1.05f;
                rotate90 = (modelLongX && targetLongZ) || (modelLongZ && targetLongX);
            }
            if (rotate90)
                _frame.localRotation = Quaternion.Euler(0f, 90f, 0f) * baseRot; // base(X=-90) 위에 방향 Y90°

            // UNIFORM 스케일 — 긴 축을 target 에 정확히 맞춤(짧은 축은 aspect 일치로 자동 정합). 반복 수렴(멱등).
            for (int iter = 0; iter < 6; iter++)
            {
                if (!TryGetFrameWorldSize(out float wx, out float wz)) return;
                if (wx < 0.0001f || wz < 0.0001f) return;
                float f = (targetX >= targetZ) ? targetX / wx : targetZ / wz;
                if (Mathf.Abs(f - 1f) < 0.005f) break;
                _frame.localScale *= f;
            }
            _frame.localPosition = Vector3.zero;

            if (PAINTBOX_DEBUG && TryGetFrameWorldSize(out float fwx, out float fwz))
                Debug.Log($"[PaintBox-DIAG-FRAME] frame={DiagPath(_frame)} rot90={rotate90} target=({targetX:F3},{targetZ:F3}) " +
                          $"→ world=({fwx:F3},{fwz:F3}) localScale={_frame.localScale.ToString("F3")}", this);
        }

        // ===== ROLLBACK_PAINTBOX_FX_20260630 — 색/연출 헬퍼 =====

        // req1: 프레임(paintbox) 색 = #5E6998. 기존 머티리얼 유지, _BaseColor/_Color 만 MPB 로 덮음.
        private void ApplyFrameColor()
        {
            if (_frame == null) return;
            _frameColorMpb ??= new MaterialPropertyBlock();
            var rends = _frame.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null || rends[i] is ParticleSystemRenderer) continue;
                rends[i].GetPropertyBlock(_frameColorMpb);
                _frameColorMpb.SetColor(EggBaseColorId, PAINTBOX_FRAME_COLOR);
                _frameColorMpb.SetColor(EggColorId, PAINTBOX_FRAME_COLOR);
                rends[i].SetPropertyBlock(_frameColorMpb);
            }
        }

        // req2: 'PaintColor' 머티리얼 슬롯에만 셀 색을 _BaseColor 로 tint (PaintColor/Paint 머티리얼·Base Map 유지).
        //   흰색(Paint) 슬롯은 안 건드린다. PaintColor 슬롯을 못 찾고 단일 머티리얼이면 폴백으로 그 슬롯 tint.
        private void TintPaintColorSlots(GameObject root, Color c)
        {
            if (root == null) return;
            _eggColorMpb ??= new MaterialPropertyBlock();
            var rends = root.GetComponentsInChildren<Renderer>(true);
            for (int ri = 0; ri < rends.Length; ri++)
            {
                var r = rends[ri];
                if (r == null || r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                bool tinted = false;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] == null) continue;
                    if (mats[m].name.IndexOf(PAINTCOLOR_MAT_HINT, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    r.GetPropertyBlock(_eggColorMpb, m);
                    _eggColorMpb.SetColor(EggBaseColorId, c);
                    _eggColorMpb.SetColor(EggColorId, c);
                    r.SetPropertyBlock(_eggColorMpb, m);
                    tinted = true;
                }
                if (!tinted && mats.Length == 1 && mats[0] != null)
                {
                    r.GetPropertyBlock(_eggColorMpb, 0);
                    _eggColorMpb.SetColor(EggBaseColorId, c);
                    _eggColorMpb.SetColor(EggColorId, c);
                    r.SetPropertyBlock(_eggColorMpb, 0);
                }
            }
        }

        // req4: 금 갈 때마다 위로 EGG_HIT_RISE_Y 올라갔다 제자리 복귀 + HitParticle.
        private void PlayEggCrackFx(int index)
        {
            if (index < 0 || index >= _eggs.Count) return;
            GameObject egg = _eggs[index];
            if (egg == null || !egg.activeSelf) return;

            Transform t = egg.transform;
            Vector3 baseP = (index < _eggBaseLocalPos.Count) ? _eggBaseLocalPos[index] : t.localPosition;
            t.DOKill();
            t.localPosition = baseP;
            Sequence sq = DOTween.Sequence();
            sq.Append(t.DOLocalMoveY(baseP.y + EGG_HIT_RISE_Y, 0.08f).SetEase(Ease.OutQuad));
            sq.Append(t.DOLocalMoveY(baseP.y, 0.10f).SetEase(Ease.InQuad));

            Transform hit = FindChildRecursive(egg.transform, FX_HIT_NAME);
            if (hit != null) PlayParticle(hit.gameObject, Color.white, false); // HitParticle 은 색 적용 안 함
        }

        // ROLLBACK_PAINTBOX_EGG_ENDPARTICLE_20260630:
        //   egg 죽을 때 그 egg 의 EndParticle 을 'egg 색' 으로 재생. egg 가 곧 비활성화되고 박스도 스케일다운하므로
        //   EndParticle 을 월드로 분리해 독립 재생한다. 최적화: Instantiate 클론 없이 egg 의 원본 EndParticle 을
        //   reparent(detach)해 재사용 → 수명 후 Destroy (egg 클론은 어차피 Clear 에서 파괴되므로 분리해도 무방).
        private void PlayEggEndParticle(int index)
        {
            if (index < 0 || index >= _eggs.Count) return;
            GameObject egg = _eggs[index];
            if (egg == null) return;
            Transform end = FindChildRecursive(egg.transform, FX_END_NAME);
            if (end == null) return;

            Color c = (index < _eggColors.Count) ? _eggColors[index] : Color.white;
            end.SetParent(null, true);                 // 월드로 분리(world pose 유지) — 클론 garbage 없음
            float life = PlayParticle(end.gameObject, c, true);
            Destroy(end.gameObject, Mathf.Max(0.5f, life) + 0.5f);
        }

        // 파티클 재생(+선택적 색 적용) — 단일 패스. 색은 startColor + 렌더러 머티리얼(_BaseColor/_Color/_TintColor, MPB) 둘 다.
        //   재사용 List(_psScratch)·MPB(_particleColorMpb) 로 per-call 할당 0. 반환 = 대략 최대 수명(초).
        private float PlayParticle(GameObject go, Color c, bool applyColor)
        {
            if (go == null) return 0f;
            go.SetActive(true);
            go.GetComponentsInChildren(true, _psScratch); // 비할당 재사용
            if (applyColor) _particleColorMpb ??= new MaterialPropertyBlock();
            float life = 0f;
            for (int i = 0; i < _psScratch.Count; i++)
            {
                var ps = _psScratch[i];
                if (ps == null) continue;
                var main = ps.main;
                if (applyColor) main.startColor = c;
                float l = main.duration + (main.startLifetime.mode == ParticleSystemCurveMode.TwoConstants
                    ? main.startLifetime.constantMax : main.startLifetime.constant);
                if (l > life) life = l;
                if (applyColor)
                {
                    var r = ps.GetComponent<ParticleSystemRenderer>();
                    if (r != null)
                    {
                        r.GetPropertyBlock(_particleColorMpb);
                        _particleColorMpb.SetColor(EggBaseColorId, c);
                        _particleColorMpb.SetColor(EggColorId, c);
                        _particleColorMpb.SetColor(ParticleTintColorId, c);
                        r.SetPropertyBlock(_particleColorMpb);
                    }
                }
                ps.Clear(true);
                ps.Play(true);
            }
            return life;
        }

        // req5: 프레임 + 파티클(HitParticle/EndParticle) = 알 bounds/배치 측정에서 제외 (실제 Paint 몸체만).
        private bool IsExcludedFromEggBounds(Transform t)
        {
            string frameName = GetFrameNameForExclusion();
            if (!string.IsNullOrEmpty(frameName) && IsSelfOrParentNamed(t, frameName)) return true;
            if (IsSelfOrParentNamed(t, FX_HIT_NAME) || IsSelfOrParentNamed(t, FX_END_NAME)) return true;
            return false;
        }
    }
}
