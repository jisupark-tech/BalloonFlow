using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using TMPro;

namespace BalloonFlow
{
    /// <summary>
    /// Manages all balloons on the game board.
    /// Balloons are stationary after initial placement (Spawner gimmick excepted).
    /// Provides lookup, pop, and gimmick-base behavior for each balloon.
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Manager | Phase: 1
    /// DB Reference: CatController (Expert Puzzle score 0.6) — pooled entity + state enum pattern;
    ///               ObstacleManager (Expert Puzzle score 0.6) — Init/Get/Clear pattern;
    ///               logicFlow from ingame_balloon_board.yaml contracts.
    /// </remarks>
    public class BalloonController : SceneSingleton<BalloonController>
    {
        #region Constants

        private const string PoolKey = "Balloon";
        // Gimmick prefab pool keys — must match Resources/Prefabs/<key>.prefab exactly.
        private const string BarricadePoolKey  = "Baricade";     // Barricade gimmick visual
        private const string IronBoxPoolKey    = "IronBox";      // Wall/IronWall visual
        private const string TargetBoxPoolKey  = "paint";        // Pinata_Box / Target Box visual
        private const string WoodenBoardPoolKey = "WoodenBoard"; // Pin gimmick visual (Lv.61)
        private const string FrozenLayerPoolKey = "FrozenLayer"; // Ice / Frozen_Dart overlay child
        private const int PinataRequiredHits = 2;
        private const float DEFAULT_BALLOON_SCALE = 0.5f;
        private const float BALLOON_FIXED_SPAWN_Y = 0.1f;
        // [#2] 풍선 소환 시 높이(scale.y)를 레벨/맵사이즈와 무관하게 절대 고정 — 레벨마다 풍선 높이가 달라지던 문제 보정.
        private const float BALLOON_FIXED_SCALE_Y = 0.35f;
        // [#2 개정 2026-06-10] 보드 가로(gridCols) 기준 분기 — 작은 보드(≤26)는 y = x×1.1 비례, 큰 보드(≥27)는 0.35 고정 유지.
        //   ex. 1레벨 scale.x≈0.5 → scale.y≈0.55. gridCols 미설정(0, 구버전 JSON)이면 기존 0.35 고정으로 폴백.
        private const int SMALL_BOARD_MAX_GRID_COLS = 26;
        private const float SMALL_BOARD_SCALE_Y_RATIO = 1.1f;
        // ROLLBACK_BARRICADE_BODY_1TO1_SCALE:
        // Updated Barricade/BarricadeBody art is authored at a 1:1 local scale per board cell.
        private const float BARRICADE_BODY_CELL_LOCAL_SCALE_X = 1f;
        // [Barricade] head(Barricade) 가 차지하는 축방향 칸 수(2). body 는 head 뒤에서 시작.
        private const float BARRICADE_HEAD_CELLS = 2f;

        /// <summary>
        /// Color palette for balloon visualization. Index matches BalloonData.color.
        /// </summary>
        /// <summary>PixelArtConverter 28색 팔레트와 동기화된 색상표.</summary>
        public static readonly Color[] BalloonColors = new Color[]
        {
            new Color(252/255f, 106/255f, 175/255f),  //  0: HotPink
            new Color( 80/255f, 232/255f, 246/255f),  //  1: Cyan
            new Color(137/255f,  80/255f, 248/255f),  //  2: Purple
            new Color(254/255f, 213/255f,  85/255f),  //  3: Yellow
            new Color(115/255f, 254/255f, 102/255f),  //  4: Green
            new Color(253/255f, 161/255f,  76/255f),  //  5: Orange
            new Color(255/255f, 255/255f, 255/255f),  //  6: White
            new Color( 65/255f,  65/255f,  65/255f),  //  7: DarkGray
            new Color(110/255f, 168/255f, 250/255f),  //  8: SkyBlue
            new Color( 57/255f, 174/255f,  46/255f),  //  9: Forest
            new Color(252/255f,  94/255f,  94/255f),  // 10: Red
            new Color( 50/255f, 107/255f, 248/255f),  // 11: Blue
            new Color( 58/255f, 165/255f, 139/255f),  // 12: Teal
            new Color(231/255f, 167/255f, 250/255f),  // 13: Lavender
            new Color(183/255f, 199/255f, 251/255f),  // 14: Periwinkle
            new Color(106/255f,  74/255f,  48/255f),  // 15: Brown
            new Color(254/255f, 227/255f, 169/255f),  // 16: Cream
            new Color(253/255f, 183/255f, 193/255f),  // 17: Pink
            new Color(158/255f,  61/255f,  94/255f),  // 18: Wine
            new Color(167/255f, 221/255f, 148/255f),  // 19: Mint
            new Color( 89/255f,  46/255f, 126/255f),  // 20: Indigo
            new Color(220/255f, 120/255f, 129/255f),  // 21: Rose
            new Color(174/255f, 178/255f, 194/255f),  // 22: Silver — [2026-06-12] #D9D9E7→#AEB2C2, 흰색(6)과 구분 강화
            new Color(111/255f, 114/255f, 127/255f),  // 23: Gray
            new Color(252/255f,  56/255f, 165/255f),  // 24: Magenta
            new Color(253/255f, 180/255f,  88/255f),  // 25: Amber
            new Color(137/255f,  10/255f,   8/255f),  // 26: Crimson
            new Color(111/255f, 175/255f, 177/255f),  // 27: Sage
        };

        // Gimmick type string constants — 정본: BalloonFlow_기믹명세 (2026-03-17)
        public const string GimmickNone         = "none";
        public const string GimmickHidden       = "Hidden";          // Lv.11  보관함 색상 숨김
        public const string GimmickChain        = "Chain";           // Lv.21  2~4 보관함 연결 순차 배치
        public const string GimmickPinata       = "Pinata";          // Lv.31  1×1~6×6 HP 오브젝트
        public const string GimmickSpawnerT     = "Spawner_T";       // Lv.41  투명 스포너 (큐에 보관함 생성)
        public const string GimmickPin          = "Pin";             // Lv.61  1×N 점진 제거 장애물
        public const string GimmickLockKey      = "Lock_Key";        // Lv.81  Key→Lock 해제
        public const string GimmickSurprise     = "Surprise";        // Lv.101 필드 풍선 색상 숨김 (인접 팝 공개)
        public const string GimmickWall         = "Wall";            // Lv.121 파괴 불가 벽
        public const string GimmickSpawnerO     = "Spawner_O";       // Lv.141 불투명 스포너
        public const string GimmickPinataBox    = "Pinata_Box";      // Lv.161 다중 셀 피냐타
        public const string GimmickIce          = "Ice";             // Lv.201 간접 제거 (모든 팝으로 HP 감소)
        public const string GimmickFrozenDart   = "Frozen_Dart";     // Lv.241 동결 풍선 (2히트 필요: 1히트=해동, 2히트=팝)
        public const string GimmickColorCurtain = "Color_Curtain";   // Lv.281 지정 색상 간접 제거
        public const string GimmickBarricade    = "Barricade";       // destructible wall (HP-based)
        public const string GimmickFlexTube     = "FlexTube";        // 다중 셀 ㄴ/ㄷ/ㄹ 형 튜브 — 같은 색 다트로 EndCap 쪽부터 1셀씩 제거

        // ROLLBACK_FLEXTUBE_SEGMENT_VISUAL_SCALE_20260608:
        // FlexTube_Segment authored visual scale requested by art/design. This is visual-only;
        // FlexTube HP, cell occupancy, targeting, and hit logic still use the original cell data.
        private static readonly Vector3 FlexTubeSegmentVisualScale = new Vector3(0.5f, 0.35f, 0.86f);
        // ROLLBACK_FLEXTUBE_EDGE_CONNECT_20260623:
        // Pull Start/End caps a full half-cell so the cap center lands exactly on the
        // segment-row boundary (segment cells span center ± 0.5·cellDist). At 0.35 the
        // cap stopped 0.15·cellDist short, leaving a visible gap between the tube and the
        // Edge/Head caps. Visual-only — logical cell/HP/targeting unchanged.
        private const float FLEXTUBE_ENDCAP_CONNECTOR_INSET = 0.5f;
        // ROLLBACK_FLEXTUBE_SEAM_OVERLAP_20260624:
        // Rib SPACING/density factor (NOT size — size is fixed to the grid cell). pitch = gridCell /
        // this, so rings are spaced a bit under one cell and overlap slightly → never separate.
        // 1.0 = one ring per cell butt-joined; higher = denser/more overlap. Visual-only.
        private const float FLEXTUBE_SEGMENT_SEAM_OVERLAP = 1.0f;
        // ROLLBACK_FLEXTUBE_RIB_LENGTH_OVERLAP_20260626: 리브 렌더 LENGTH 를 pitch 보다 길게(겹치게) 만드는 배수.
        //   기존엔 rib 길이=pitch(butt-join)라 메시 끝 여백/주름 neck 때문에 리브 사이에 틈이 보였다(사진의 끊긴 원통들).
        //   1.0=정확히 맞닿음(틈 위험), >1=겹침. (시각만; pitch/타게팅 무관.)
        // ROLLBACK_FLEXTUBE_RIB_OVERLAP_RAISE_20260628: 디스크 메시 가시부가 bounds(z=0.294)보다 짧아(끝 여백)
        //   1.15 로는 직선부가 여러 통(barrel)으로 끊겨 보임. 가시부가 확실히 겹치도록 1.6 으로 올림.
        private const float FLEXTUBE_RIB_LENGTH_OVERLAP = 1.6f;
        // ROLLBACK_FLEXTUBE_NONUNIFORM_PER_CELL_20260624:
        // SEGMENT DENSITY = segment LENGTH as a fraction of one grid cell (ringWorldSize = gridCell × this).
        // SMALLER = more segments per cell = finer ribbing = reads as a CONTINUOUS tube. The Hose_Segment
        // mesh has a built-in neck→bulge→neck shape; packing them tight makes the necks sub-visual. Too
        // large (1.0 = one long segment/cell) stretches each neck into a visible gap ("A....A"). ~0.2 ≈ 5
        // ribs/cell. Length(z) is pitch-locked (uniform size, no beat); diameter(x,y) is fixed separately.
        // ROLLBACK_FLEXTUBE_FEWER_RIBS_20260626: 셀당 3개(1/3) 불필요 — 디자이너가 더 적어도 된다고 함.
        //   1/2 = 셀당 ~2개로 줄여 과한 촘촘함 완화. (얇아진 몸통이라 코너 facet 부담도 작음.) 더 줄이려면 값 키움.
        private const float FLEXTUBE_RIB_SIZE_SCALE = 1f / 2f;
        // ROLLBACK_FLEXTUBE_NONUNIFORM_PER_CELL_20260624:
        // Tube DIAMETER as a fraction of one grid cell. Decoupled from segment length (non-uniform scale),
        // so the pipe keeps a constant thickness while one segment spans one cell. ~0.85 ≈ the previous
        // visual thickness; lower = thinner pipe, higher = fatter. Visual-only.
        // ROLLBACK_FLEXTUBE_2THICK_20260626: Barricade 처럼 2줄 두께 — centerline 은 row-A/row-B 짝의 중점이라
        //   메시 지름을 ~2칸으로 키워 두 줄을 시각적으로 덮는다. (0.85 → 1.8)
        // ROLLBACK_FLEXTUBE_SLIM_BODY_20260626: 몸통(Segment) 지름을 2×2 의 둥근 튜브로. 캡(2×2)은 끝 피팅으로
        //   2칸 두께 유지. "서로 끊김 없이 + 모서리에서도 부드럽게" — 얇을수록 코너 외곽 facet 이 작아 매끈.
        // ROLLBACK_FLEXTUBE_BODY_PLUS20_20260626: 1.0 이 너무 얇다는 피드백 → 20% 증가(1.0→1.2).
        private const float FLEXTUBE_TUBE_DIAMETER_FRAC = 1.2f;
        // ROLLBACK_FLEXTUBE_CAP_2X_SIZE_20260626:
        // Head/Edge caps should read as 2x2, but segments should not become huge. Scale caps
        // against their own mesh width with this larger target, while segments use the slimmer
        // FLEXTUBE_TUBE_DIAMETER_FRAC above.
        private const float FLEXTUBE_CAP_DIAMETER_FRAC = 2.0f;
        // ROLLBACK_FLEXTUBE_CAP_LENGTH_1CELL_20260626:
        // 캡(Head/Edge)의 경로 방향 LENGTH = ~1 그리드 셀(자기 논리 셀 하나). 이전 코드가 길이축(z)에
        // FLEXTUBE_CAP_DIAMETER_FRAC(=2) — '지름' 상수 — 을 곱해 캡을 2칸 길이로 늘여, Head 가 seq1 Segment 를
        // 시각적으로 먹고 Edge 가 몸통을 침범했다. 지름(x,y)은 CAP_DIAMETER_FRAC(2칸 너비) 유지, 길이(z)만 1칸.
        private const float FLEXTUBE_CAP_LENGTH_FRAC = 1.0f;
        // ROLLBACK_FLEXTUBE_NONUNIFORM_PER_CELL_20260624:
        // How far the segment tiling extends INTO each cap (as a fraction of that cap's scaled length), so
        // the segment row bridges right up to / slightly under the cap instead of stopping short → removes
        // the cap↔segment gap. 0 = stop at the cap center (can gap); ~0.5 = reach the cap's outer edge.
        private const float FLEXTUBE_CAP_SEGMENT_BRIDGE = 0.5f;
        // ROLLBACK_FLEXTUBE_SEQ_VISUAL_DOUBLE_SIZE_20260628:
        // The current FlexTube prefabs have renderer bounds larger than the visible hose/cap body.
        // Fitting those bounds to a 2x2 footprint still reads too small, so this visual-only path
        // doubles the rendered part scale while leaving logical cells/HP/targeting unchanged.
        private const float FLEXTUBE_SEQ_PART_VISUAL_MULTIPLIER = 2.0f;
        // ROLLBACK_FLEXTUBE_SPAWN_DEBUG_20260628:
        // Temporary spawn diagnostics requested for FlexTube visual QA. Logs exact Start/Segment/Edge
        // positions, rotations, scales, bounds, and segment-to-segment gaps at runtime.
        private const bool FLEXTUBE_SPAWN_DEBUG = true;
        private const int FLEXTUBE_SPAWN_DEBUG_SEGMENT_LIMIT = 220;
        // ROLLBACK_FLEXTUBE_SEQ_VISUAL_Z_OFFSET_20260628:
        // Small board-Z visual lift requested for FlexTube readability over the balloon field.
        private const float FLEXTUBE_SEQ_VISUAL_Z_OFFSET = 0.25f;
        // ROLLBACK_FLEXTUBE_SEAMLESS_TILING_20260618: 세그먼트의 자연 길이(월드 Z).
        //   측정: 유니티 (1,1,1) 박스 대비 세그먼트가 유닛당 3.5개일 때 1사이즈 = 1/3.5 ≈ 0.2857.
        //   (FlexTube_Segment mesh × FlexTubeSegmentVisualScale.z(0.86) 의 실제 월드 길이.)
        //   고정 5개 강제 대신, 셀마다 N=round(셀거리/이값) 로 계산 → visualStep ≈ 이값 → 겹침/틈 없이 타일링.
        //   시각만 영향, HP/타게팅/셀점유 무관. 롤백: cellSegments → visualSegmentsPerCell 환원 + 이 상수 제거.
        private const float FLEXTUBE_SEGMENT_WORLD_LENGTH = 1f / 3.5f;

        // ROLLBACK_PINATA_PER_CELL_20260618:
        //   plain Pinata(sized W×H>1)를 '칸당 1공격'으로 — 점유 셀마다 별도 hit 필요(총 W×H 회). 2×2=4회.
        //   객체/비주얼은 단일 그대로 유지(분할 X). 메커니즘은 Pinata_Box egg 모델과 함수적으로 동일(idx>=hitCount).
        //   타게팅: 셀 idx<hitCount = 제거됨(blocker), idx>=hitCount = 타겟. ProcessPinataHit: requiredHits=W×H +
        //   partial hit 마다 타게팅 캐시 무효화(셀 1개씩 빠지게). 1×1 Pinata 는 영향 없음(W×H==1).
        //   롤백: 이 플래그/헬퍼 + ProcessPinataHit 의 perCell 분기 + DirectionalTargeting 의 isPinataPerCell 분기 제거.
        // ROLLBACK_WOODEN_BOARD_SHARED_HP_20260623:
        // Sized Wooden Board should keep the authored HP and expose all occupied cells as the
        // same shared target. Previous per-cell mode forced HP=sizeW*sizeH, which made 2x2/3x3
        // boards ignore MapMaker HP.
        // ROLLBACK_WOODENBOARD_PER_CELL_OFF_20260625: per-cell mode DISABLED (이전 설계 번복).
        // WoodenBoard(sized Pinata) 는 어떤 크기든(1×1 / n×m) authored MapMaker HP 를 그대로 사용한다.
        //   HP 만 hit 마다 감소 → 0 이면 통째로 소멸(바리케이드식 비율 축소 X, 셀 단위 소진 X).
        // false 면 IsPinataPerCell 이 항상 false → maxHP=W×H override / requiredHits=W×H /
        //   셀단위 타게팅(idx<hitCount)·매치 분기가 전부 우회되어 공유 authored-HP 동작으로 복귀.
        public static bool EnablePinataPerCell = false;
        public static bool IsPinataPerCell(BalloonData d) =>
            EnablePinataPerCell && d.gimmickType == GimmickPinata && d.sizeW * d.sizeH > 1;

        public bool AllowsConcurrentCellTargetReservation(int balloonId)
        {
            // ROLLBACK_SHARED_MULTI_CELL_LINE_RESERVATION_20260628:
            // Some field gimmicks are one shared-HP object but occupy several exposed cells.
            // A global balloonId reservation makes a 3-wide outer edge suppress the other two
            // legal scan lines after the first dart. Let those shared multi-cell objects reserve
            // by scan line instead; DartManager's same-line/holder locks still prevent duplicate
            // fire or penetration on a single line.
            if (!_balloons.TryGetValue(balloonId, out BalloonData data) || data == null || data.isPopped)
                return false;

            if (data.gimmickType == GimmickPinata
                && !IsPinataPerCell(data)
                && (data.sizeW > 1 || data.sizeH > 1))
                return true;

            if (data.gimmickType == GimmickPinataBox
                && (data.sizeW > 1 || data.sizeH > 1)
                && data.eggHps != null)
                return true;

            if (data.gimmickType == GimmickBarricade
                && data.barricadeLength > 1
                && GetBarricadeActiveLength(data) > 1)
                return true;

            return false;
        }

        // ROLLBACK_BARRICADE_HP_INDEPENDENT_20260624:
        // Shared source for Barricade's remaining attackable length, computed as length × remainingHP/maxHP.
        // HP and length are INDEPENDENT: a length-6 / HP-3 Barricade exposes 6→4→2→0 cells (2 per hit);
        // a length-6 / HP-6 exposes 6→5→…→0 (1 per hit). maxHP = authored MapMaker HP (the `maxHP =
        // barricadeLength` override at registration is removed). Visual placement, occupancy, and
        // DirectionalTargeting all read this same value so they stay in sync.
        // NOTE: existing Barricades now honour their authored `hp` (previously ignored) — review level
        // balance. A Barricade with no authored HP falls back to PinataRequiredHits(2).
        public static int GetBarricadeActiveLength(BalloonData data)
        {
            if (data == null) return 0;
            int length = Mathf.Max(1, data.barricadeLength);
            int maxHp = Mathf.Max(1, data.maxHP);
            int remaining = Mathf.Clamp(maxHp - Mathf.Max(0, data.hitCount), 0, maxHp);
            if (remaining >= maxHp) return length;
            if (remaining <= 0) return 0;
            return Mathf.Clamp(Mathf.CeilToInt(length * (float)remaining / maxHp), 0, length);
        }

        #endregion

        #region Fields

        [SerializeField] private GameObject _balloonPrefab;

        [Tooltip("Hidden 기믹 풍선 전용 머테리얼 (BalloonHidden.mat). Inspector 할당 또는 Resources/BalloonHidden 에서 자동 로드.")]
        [SerializeField] private Material _balloonHiddenMaterial;

        [Header("[Perf — 외곽 풍선만 Outline 활성]")]
        [Tooltip("외곽 풍선만 Outline pass 활성 (_OutlineEnabled MaterialPropertyBlock). " +
                 "[2026-05-10] 항상 ON 으로 강제 — Lobby/MapMaker 등 다양한 진입 경로에서도 inspector toggle 의존 없이 자동 적용. " +
                 "RefreshOutermostRendererState 의 가드 라인도 주석 처리됨. 토글 disable 원하면 가드 라인 + default false 복원.")]
        [SerializeField] private bool _outlineOnOuterOnly = true;

        // ROLLBACK_BARRICADE_VISUAL_SETTINGS:
        // Barricade art has its own pivot/height/body length, so keep visual placement separate
        // from regular balloons and from logical cell occupancy.
        [Header("[Barricade Visual]")]
        [SerializeField] private float _barricadeVisualY = 0.5f;
        [SerializeField] private Vector3 _barricadeVisualOffset = Vector3.zero;
        // ROLLBACK_BARRICADE_Z_NUDGE_20260623:
        // Visual-only world-Z nudge. The Barricade head is centered on its 2-cell thickness for
        // targeting, but the authored art reads too centered on the board, so pull it slightly on Z.
        [SerializeField] private float _barricadeVisualZCellNudge = 0.25f;
        [SerializeField] private float _barricadeLengthMultiplier = 1f;
        [SerializeField] private float _barricadeLengthPadding = 0f;
        [SerializeField] private Vector3 _barricadeBodyVisualOffset = Vector3.zero;
        [SerializeField] private Vector3 _barricadeEdgeOffset = Vector3.zero;
        // ROLLBACK_BARRICADE_VISUAL_JOIN_20260608:
        // Visual-only yaw compensation for authored Barricade head/edge meshes. Targeting uses barricadeDir unchanged.
        [SerializeField] private float _barricadeHeadYawOffset = 0f;

        // Primary data store keyed by balloonId
        private readonly Dictionary<int, BalloonData> _balloons = new Dictionary<int, BalloonData>();

        // Visual GameObject handles keyed by balloonId
        private readonly Dictionary<int, GameObject> _balloonObjects = new Dictionary<int, GameObject>();
        private readonly List<GameObject> _flexTubeRoots = new List<GameObject>(); // FlexTube 부모 GameObject 캐시 — ClearAllBalloons 시 일괄 destroy.

        // Renderer cache per balloonId — _outermostCulling 시 매 호출 GetComponentsInChildren 비용 회피
        private readonly Dictionary<int, Renderer[]> _balloonRenderers = new Dictionary<int, Renderer[]>();

        // Hidden balloons that are currently color-concealed
        private readonly HashSet<int> _hiddenBalloons = new HashSet<int>();

        // Pinata multi-tile occupancy: key = balloonId, value = list of all occupied ids
        private readonly Dictionary<int, List<int>> _pinataGroup = new Dictionary<int, List<int>>();

        // Spatial index: position key -> balloonId  (for adjacency lookups)
        private readonly Dictionary<Vector3Int, int> _positionIndex = new Dictionary<Vector3Int, int>();

        // ROLLBACK_BARRICADE_MULTI_CELL_OCCUPANCY:
        // Barricade is one object, but it can occupy multiple logical board cells.
        private readonly Dictionary<int, List<Vector3Int>> _multiCellOccupancy = new Dictionary<int, List<Vector3Int>>();
        private readonly Dictionary<Transform, Vector3> _barricadeBodyBaseScales = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, Quaternion> _barricadeBodyBaseRotations = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, Vector3> _barricadeBodyBasePositions = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, Quaternion> _barricadeEdgeBaseRotations = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, Vector3> _barricadeEdgeBasePositions = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, Vector3> _barricadeEdgeBaseScales = new Dictionary<Transform, Vector3>();
        // ROLLBACK_BARRICADE_HEAD_ROTATION_20260608: head(Barricade) 방향(N/E/S/W) 회전용 base 캐시.
        private readonly Dictionary<Transform, Quaternion> _barricadeHeadBaseRotations = new Dictionary<Transform, Quaternion>();
        // ROLLBACK_BARRICADE_ASSEMBLY_20260608: assembly("Baricade (1)") base 회전 + body base 길이(최장축) 캐시.
        private readonly Dictionary<Transform, Quaternion> _barricadeAssemblyBaseRot = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, Vector3> _barricadeAssemblyBaseLocalPositions = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, float> _barricadeBodyBaseLen = new Dictionary<Transform, float>();
        // ROLLBACK_BARRICADE_TILING/SMOOTH_20260625: body 원본 Tiling/Offset(_BaseMap_ST) 1회 캐시 + 재사용 MPB.
        private readonly Dictionary<Transform, Vector4> _barricadeBodyBaseTileST = new Dictionary<Transform, Vector4>();
        private MaterialPropertyBlock _barricadeBodyMpb;
        private const float BARRICADE_RESHAPE_DUR = 0.22f; // 길이 줄어듦 트윈 시간(끊김 제거)
        private readonly List<Vector3Int> _reusableOccupiedCells = new List<Vector3Int>(16);

        // Key tracking: balloonId -> lockPairId (for path-based Key release)
        private readonly Dictionary<int, int> _activeKeyPairIds = new Dictionary<int, int>();

        // Scale multiplier for balloon visuals (set from LevelConfig)
        private float _balloonScale = DEFAULT_BALLOON_SCALE;

        // Grid spacing for adjacency calculations (read from GameManager.Board)
        private float _cellSpacing = 0.55f;

        private int _nextBalloonId;
        private int _currentLevelId;

        #endregion

        #region Properties

        /// <summary>Total number of non-popped balloons currently on the board.</summary>
        public int RemainingCount { get; private set; }

        /// <summary>누적 팝된 풍선 수 (Spawner로 추가된 풍선 포함). 진행률 슬라이더용.</summary>
        public int PoppedCount { get; private set; }

        /// <summary>
        /// Sets the visual scale for all balloons. Call before InitBoard.
        /// </summary>
        public void SetBalloonScale(float scale)
        {
            _balloonScale = Mathf.Clamp(scale, 0.2f, 1.0f);
        }

        // [#2 개정 2026-06-10] 레벨 보드 가로 칸수 — scale.y 분기(≤26: x×1.1 / ≥27: 0.35 고정) 기준.
        private int _boardGridCols;

        /// <summary>레벨 보드 가로 칸수 설정. LevelManager 가 레벨 적용 시 매번 호출 (0 = 미설정 → 0.35 고정 폴백).</summary>
        public void SetBoardGridCols(int cols)
        {
            _boardGridCols = Mathf.Max(0, cols);
        }

        /// <summary>
        /// Sets the grid cell spacing used for adjacency calculations.
        /// Must be called before SetupBalloons. GameManager.Board.cellSpacing 기준.
        /// </summary>
        public void SetCellSpacing(float spacing)
        {
            _cellSpacing = Mathf.Max(0.1f, spacing);
        }

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            _nextBalloonId = 1;
            _currentLevelId = -1;

            // GameManager.Board에서 설정값 읽기
            if (GameManager.HasInstance)
            {
                _cellSpacing = GameManager.Instance.Board.cellSpacing;
                _balloonScale = GameManager.Instance.Board.balloonScale;
            }
        }


        /// <summary>레벨별 안전 배율. 넘지 않으면 설정값 그대로, 넘으면 축소.</summary>
        private float _levelSafeWm = 1f, _levelSafeHm = 1f;
        /// <summary>위/아래 침범 차이 보정용 Z 이동량.</summary>
        private float _levelSafeZShift = 0f;
        private bool _levelSafeCalculated = false;

        /// <summary>SetupBalloons 완료 후 호출 — 레벨별 안전 배율 1회 계산.</summary>
        private void CalculateLevelSafeMult()
        {
            _levelSafeCalculated = false;
            _levelSafeZShift = 0f;
            if (!GameManager.HasInstance) { _levelSafeWm = 1f; _levelSafeHm = 1f; return; }

            float wm = GameManager.Instance.Board.balloonFieldWidthMult;
            float hm = GameManager.Instance.Board.balloonFieldHeightMult;
            float cz = GameManager.Instance.Board.balloonCenterZ;
            float beltCZ = GameManager.Instance.Board.boardCenterZ;

            // 벨트 내부 경계 (절대 좌표)
            float halfConveyor = BoardTileManager.CONVEYOR_HEIGHT * 0.5f;
            float halfRail = BoardTileManager.RAIL_THICKNESS * 0.5f;
            float beltTop = beltCZ + halfConveyor - halfRail;
            float beltBottom = beltCZ - halfConveyor + halfRail;
            float beltInnerSpan = beltTop - beltBottom;
            float beltInnerCenter = (beltTop + beltBottom) * 0.5f;

            // 풍선 최소/최대 Z (배율 적용 후)
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var kvp in _balloons)
            {
                float z = cz + (kvp.Value.position.z - cz) * hm;
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;
            }

            if (minZ >= maxZ) { _levelSafeWm = wm; _levelSafeHm = hm; _levelSafeCalculated = true; return; }

            float gridSpan = maxZ - minZ;
            float gridCenter = (maxZ + minZ) * 0.5f;

            if (gridSpan > beltInnerSpan)
            {
                // 벨트 초과 → 가로세로 동일 비율 축소
                float ratio = beltInnerSpan / gridSpan;
                wm *= ratio;
                hm *= ratio;
                gridCenter = cz; // 축소 후 중심은 balloonCenterZ

                // 축소 후 벨트 내부 정중앙에 배치 → 위아래 마진 동일
                _levelSafeZShift = beltInnerCenter - gridCenter;
            }

            _levelSafeWm = wm;
            _levelSafeHm = hm;
            _levelSafeCalculated = true;
        }

        private void SyncBoardMetricsForLevelSetup()
        {
            // ROLLBACK_GIMMICK_CELLSPACING_SYNC:
            // Runtime levels can change GameManager.Board.cellSpacing before SetupBalloons.
            // Keep BalloonController's grid key/adjacency spacing in sync for field gimmicks.
            if (GameManager.HasInstance)
            {
                _cellSpacing = Mathf.Max(0.1f, GameManager.Instance.Board.cellSpacing);
            }

            _levelSafeWm = 1f;
            _levelSafeHm = 1f;
            _levelSafeZShift = 0f;
            _levelSafeCalculated = false;
        }

        private void ReapplyAllBalloonVisualTransforms()
        {
            float scaleMult = _levelSafeCalculated
                ? Mathf.Max(_levelSafeWm, _levelSafeHm)
                : (GameManager.HasInstance ? Mathf.Max(GameManager.Instance.Board.balloonFieldWidthMult, GameManager.Instance.Board.balloonFieldHeightMult) : 1f);

            foreach (var kvp in _balloons)
            {
                BalloonData data = kvp.Value;
                if (data.isPopped) continue;
                if (!_balloonObjects.TryGetValue(data.balloonId, out GameObject obj) || obj == null) continue;

                if (data.gimmickType == GimmickBarricade)
                {
                    ApplyBarricadeVisualTransform(obj, data);
                    continue;
                }

                // Target Box 알 모델: PinataBoxView + eggColors 있으면 알 격자 방식으로 재배치.
                if (data.gimmickType == GimmickPinataBox)
                {
                    var pbView = obj.GetComponentInChildren<PinataBoxView>(true);
                    if (pbView != null)
                    {
                        ApplyPinataBoxVisual(obj, data, pbView);
                        continue;
                    }
                }

                if (data.gimmickType == GimmickPinata || data.gimmickType == GimmickPinataBox
                    || (data.gimmickType == GimmickWall && (data.sizeW > 1 || data.sizeH > 1)))
                {
                    // multi-cell Wall(2×2/3×3) 도 footprint 스케일 유지 — 미포함 시 rest 스케일(1×)로 리셋되어 사이즈가 사라짐.
                    ApplySizedFieldVisualTransform(obj, data);
                    continue;
                }

                // [#2-B] ROLLBACK_BALLOON_UNPIN_SPAWN_Y_20260608: 풍선만 Y 위치 고정(0.1) 해제 → 자연 Y 사용.
                // 높이는 scale.y=0.35 가 담당. 공유 헬퍼는 그대로라 다트/기믹/바리케이드 Y 는 영향 없음.
                Vector3 bp = GetAdjustedBoardPosition(data.position);
                bp.y = data.position.y;
                obj.transform.position = bp;
                obj.transform.localScale = GetBalloonRestScale(scaleMult);
            }

            _frameCachedPositions.Clear();
            _frameCachedPositionsFrame = -1;
            _lastCacheRefreshTime = 0f;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Subscribe<OnBalloonPopped>(CheckKeysOnPop);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
            EventBus.Unsubscribe<OnBalloonPopped>(CheckKeysOnPop);
        }

#if UNITY_EDITOR
        /// <summary>에디터 전용: GameManager 배율 변경 시 풍선 위치/스케일 실시간 갱신.</summary>
        private float _prevWidthMult = 1f, _prevHeightMult = 1f, _prevZOffset = 0f;
        private void Update()
        {
            if (!GameManager.HasInstance) return;
            float wm = GameManager.Instance.Board.balloonFieldWidthMult;
            float hm = GameManager.Instance.Board.balloonFieldHeightMult;
            float zo = GameManager.Instance.Board.balloonGridZOffset;
            if (Mathf.Approximately(wm, _prevWidthMult) && Mathf.Approximately(hm, _prevHeightMult) && Mathf.Approximately(zo, _prevZOffset)) return;
            _prevWidthMult = wm;
            _prevHeightMult = hm;
            _prevZOffset = zo;
            RefreshAllBalloonTransforms();
        }

        private void RefreshAllBalloonTransforms()
        {
            CalculateLevelSafeMult();
            // ROLLBACK_EDITOR_SIZED_GIMMICK_REFRESH:
            // Reuse the runtime path so Pinata/Pinata_Box/Barricade keep their sized transforms
            // when editor board multipliers change.
            ReapplyAllBalloonVisualTransforms();
            // [SHADOW_BATCH] 에디터 배율 변경으로 그림자 위치/스케일이 움직였으니 combined mesh 재 bake.
            RebuildShadowBatch();
        }
#endif

        // ROLLBACK_SHADOW_HIDE_COALESCE_20260616: 한 프레임에 누적된 그림자 HideShadow(정점 collapse)들을
        //   그룹당 1회 mesh 업로드로 합친다. zap/다트연쇄 다중팝의 O(팝수×N) 그림자 버퍼 재업로드 → O(N)/프레임.
        //   (런타임 전용 — Update 는 #if UNITY_EDITOR 라 별도 LateUpdate 사용. _shadowBatcher null-safe.)
        //   롤백: 이 메서드 제거 + HideShadow 의 즉시 SetVertices 복원.
        private void LateUpdate()
        {
            _shadowBatcher?.FlushDirty();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Sets up the balloon board from a layout data list.
        /// Clears any existing balloons first.
        /// </summary>
        /// <param name="layout">List of balloon setup entries defining initial board state.</param>
        /// <param name="levelId">Level identifier for event publishing.</param>
        public void SetupBalloons(List<BalloonSetupData> layout, int levelId)
        {
            ClearAllBalloons();
            _currentLevelId = levelId;
            SyncBoardMetricsForLevelSetup();

            if (layout == null || layout.Count == 0)
            {
                Debug.LogWarning("[BalloonController] SetupBalloons called with empty or null layout.");
                return;
            }

            foreach (BalloonSetupData entry in layout)
            {
                // ROLLBACK_ICE_OVERLAY_UNDER_BALLOONS_20260626:
                // Legacy authored Ice may still arrive as one sizeW/sizeH anchor. Split it into
                // real underlying balloons; ApplyInitialIceState renders one region overlay.
                if (TrySpawnSizedIceAsCellBrush(entry))
                    continue;

                SpawnBalloonFromSetup(entry);
            }

            // FlexTube 그룹 단위 prefab 인스턴스화 — 모든 BalloonData 등록 직후, 자식 부품 GameObject 를 _balloonObjects 에 매핑.
            BuildFlexTubes(layout);

            // ROLLBACK_GIMMICK_LEVEL_METRICS_REAPPLY:
            // Level-specific safe multipliers require the full balloon data set. Spawn first,
            // calculate once, then re-apply visuals so sized gimmicks do not keep stale level
            // offsets/scales from the previous board.
            CalculateLevelSafeMult();
            ReapplyAllBalloonVisualTransforms();
            // [RAW_GRID_SPACE 2026-06-12] safe mult 재계산으로 렌더 그리드가 바뀌었으므로
            // 라인/셀 캐시를 1회 무효화 — 이전 mult 로 빌드된 키 잔존 방지.
            DirectionalTargeting.InvalidateCache();
            if (BoardStateManager.HasInstance)
                BoardStateManager.Instance.InvalidateOutermostCache();
            // [LATTICE_PHASE 2026-06-12] 격자 위상 앵커 — 모든 셀/라인 스냅의 기준점.
            RecomputeRawLatticePhase();

            // Wall / FlexTube cells don't count toward clear condition
            int excludeCount = 0;
            foreach (BalloonData d in _balloons.Values)
            {
                if (d.gimmickType == GimmickWall) excludeCount++;
                else if (d.gimmickType == GimmickFlexTube) excludeCount++;
            }
            RemainingCount = _balloons.Count - excludeCount;
            PoppedCount = 0;
            BuildPositionIndex();

            // Apply gimmick visual states after all balloons are placed
            ApplyInitialHiddenState();
            ApplyInitialIceState();
            ApplyInitialFrozenDartState();
            ApplyInitialColorCurtainState();

            // [#13/§11] Ice 영역(인접 연결 성분) 그룹핑 + 영역별 공유 HP 확정. ice 등록·위치 인덱스 완료 후.
            if (GimmickProcessor.HasInstance)
            {
                GimmickProcessor.Instance.InitIceRegions();
                // ROLLBACK_CURTAIN_WINNABILITY_CLAMP_20260616: 셋업 완료(전 풍선 등록) 후 커튼 counter 클램프.
                GimmickProcessor.Instance.ClampCurtainCounters();
            }

            // 레벨별 안전 배율 계산 (벨트 초과 레벨만 축소)
            CalculateLevelSafeMult();

            // [Outline 2026-05-10] 맵 세팅 직후 외곽 풍선만 outline 적용 자동 트리거.
            // throttle reset 으로 첫 호출 보장. _outlineOnOuterOnly = false 면 RefreshOutermostRendererState 가 즉시 return.
            // 롤백: 아래 두 라인 제거.
            _lastOutermostRefreshTime = -1f;
            RefreshOutermostRendererState();

            // [SHADOW_BATCH 2026-06-11] 정적 풍선 그림자 렌더러 N개 → combined mesh 통합 (렌더러 수 비례
            // 컬링/추출/Submit 비용 절감 — 대형 보드 프레임 드랍 대응). 모든 비주얼 변환 적용 후 1회 bake.
            RebuildShadowBatch();
        }

        // ── [SHADOW_BATCH 2026-06-11] ─────────────────────────────
        private BalloonShadowBatcher _shadowBatcher;

        /// <summary>정적 풍선(스케일 트윈 없는 타입)만 그림자 통합 대상.
        /// Pinata/Barricade/FlexTube 등은 hit 펀치/축소 트윈이 그림자에 함께 걸리므로 개별 유지.</summary>
        private static bool IsShadowBatchable(string gimmickType)
            => gimmickType == GimmickNone
            || gimmickType == GimmickSurprise
            || gimmickType == GimmickHidden;

        private void RebuildShadowBatch()
        {
            if (_shadowBatcher == null) _shadowBatcher = new BalloonShadowBatcher();

            // ROLLBACK_LARGE_RAIL_BALLOON_VISUALS_20260624:
            // The old 600+ balloon guard suppressed every shadow/highlight, so 120/160-capacity
            // boards lost both visuals. Keep the combined-shadow batch instead; it is rebuilt on
            // board changes and rendered as grouped meshes, not per-balloon SpriteRenderers.
            // Rollback: restore SHADOW_BATCH_MAX_BALLOONS suppress block.

            _shadowBatcher.BeginBuild();
            foreach (var kvp in _balloonObjects)
            {
                if (kvp.Value == null) continue;
                bool batchable = _balloons.TryGetValue(kvp.Key, out BalloonData d)
                                 && !d.isPopped && IsShadowBatchable(d.gimmickType);
                _shadowBatcher.AddOrRestore(kvp.Key, kvp.Value, batchable);
                // ROLLBACK_BALLOON_HIGHLIGHT_SUPPRESS_LOWEND_20260617: 임계 미만 — 광택 복원(그림자 restore 와 동일 경로).
                SetBalloonHighlightActive(kvp.Value, true);
            }
            _shadowBatcher.EndBuild();
        }

        // ROLLBACK_BALLOON_HIGHLIGHT_SUPPRESS_LOWEND_20260617:
        //   풍선 GO 의 BalloonIdentifier 광택 렌더러 on/off. RebuildShadowBatch(비 per-frame, 풍선 set 변경 시) 에서만 호출.
        //   롤백: 이 메서드 + 위 두 호출 제거.
        private void SetBalloonHighlightActive(GameObject balloonGo, bool active)
        {
            if (balloonGo == null) return;
            var id = balloonGo.GetComponent<BalloonIdentifier>();
            if (id != null) id.SetHighlightActive(active);
        }

        /// <summary>
        /// Returns a snapshot copy of BalloonData for the given balloonId.
        /// Returns null if not found or already popped.
        /// </summary>
        public BalloonData GetBalloon(int balloonId)
        {
            if (_balloons.TryGetValue(balloonId, out BalloonData data))
            {
                return data;
            }
            return null;
        }

        /// <summary>Frame 단위 position cache — transform.position 호출 N×M → 1×N 으로 절감.</summary>
        private readonly Dictionary<int, Vector3> _frameCachedPositions = new Dictionary<int, Vector3>(512);
        private int _frameCachedPositionsFrame = -1;

        /// <summary>풍선 위치 cache. 풍선 정적 가정 (spawn 후 안 움직임) — 0.1s 마다 갱신.
        /// 이전: 매 frame 1620 iterate × transform.position 호출 = 20%+ 부하 (Profiler 측정).
        /// 변경: 0.1s throttle — 평균 부하 14배 절감. 풍선 정적이라 0.1s stale OK.</summary>
        private float _lastCacheRefreshTime;
        private const float CACHE_REFRESH_INTERVAL = 0.1f;

        private void RefreshFramePositionCacheIfNeeded()
        {
            float now = Time.unscaledTime;
            // 첫 호출 또는 0.1s 경과 시만 rebuild — 풍선이 정적이라 stale 영향 미미.
            if (_frameCachedPositions.Count > 0 && now - _lastCacheRefreshTime < CACHE_REFRESH_INTERVAL) return;
            _lastCacheRefreshTime = now;

            _frameCachedPositions.Clear();
            var en = _balloonObjects.GetEnumerator();
            try
            {
                while (en.MoveNext())
                {
                    var obj = en.Current.Value;
                    if (obj != null) _frameCachedPositions[en.Current.Key] = obj.transform.position;
                }
            }
            finally { en.Dispose(); }
        }

        /// <summary>풍선의 실제 월드 위치 (배율/오프셋 적용 후). 오브젝트 없으면 데이터 위치 반환.
        /// 정확한 위치 — 매 호출 transform.position 직접. 다트 발사 등 정밀 위치 필요한 곳 사용.</summary>
        public Vector3 GetBalloonWorldPosition(int balloonId)
        {
            if (_balloonObjects.TryGetValue(balloonId, out GameObject obj) && obj != null)
                return obj.transform.position;
            if (_balloons.TryGetValue(balloonId, out BalloonData data))
                return GetAdjustedBoardPosition(data.position);
            return Vector3.zero;
        }

        public Vector3 GetAdjustedBoardPosition(Vector3 position)
        {
            Vector3 adjustedPos = position;
            adjustedPos.y = BALLOON_FIXED_SPAWN_Y;
            if (GameManager.HasInstance)
            {
                float cx = GameManager.Instance.Board.boardCenterX;
                float cz = GameManager.Instance.Board.balloonCenterZ;
                float wm = _levelSafeCalculated ? _levelSafeWm : GameManager.Instance.Board.balloonFieldWidthMult;
                float hm = _levelSafeCalculated ? _levelSafeHm : GameManager.Instance.Board.balloonFieldHeightMult;
                float zOffset = GameManager.Instance.Board.balloonGridZOffset;
                adjustedPos.x = cx + (position.x - cx) * wm;
                adjustedPos.z = cz + (position.z - cz) * hm + zOffset + _levelSafeZShift;
            }
            return adjustedPos;
        }

        public void GetAdjustedCellSize(out float cellSizeX, out float cellSizeZ)
        {
            GetFieldVisualMetrics(out float widthMult, out float heightMult, out _, out cellSizeX, out cellSizeZ, out _);
        }

        /// <summary>[RAW_GRID_SPACE 2026-06-12] GetAdjustedBoardPosition 의 역변환 —
        /// 월드 좌표를 원시 보드 공간(레벨 데이터 그리드, cellSpacing 단위)으로 되돌린다.
        /// 스케일/시프트된 대형 보드에서 타겟팅·스캔라인이 "시각적 한 줄 = 라인 키 1개"를 유지하려면
        /// 라인 키 단위를 바꾸지 말고(기존 튜닝 상수 전제) 좌표를 이 함수로 정규화한 뒤 원시 spacing 으로
        /// 나눠야 한다. 정적 풍선은 data.position 과 일치, 이동체(스포너/컨베이어)도 같은 변환으로 정합.
        /// y 는 통과(보존).</summary>
        public Vector3 WorldToRawBoardPosition(Vector3 worldPos)
        {
            Vector3 raw = worldPos;
            if (GameManager.HasInstance)
            {
                float cx = GameManager.Instance.Board.boardCenterX;
                float cz = GameManager.Instance.Board.balloonCenterZ;
                float wm = _levelSafeCalculated ? _levelSafeWm : GameManager.Instance.Board.balloonFieldWidthMult;
                float hm = _levelSafeCalculated ? _levelSafeHm : GameManager.Instance.Board.balloonFieldHeightMult;
                float zOffset = GameManager.Instance.Board.balloonGridZOffset;
                if (wm <= 0.0001f) wm = 1f;
                if (hm <= 0.0001f) hm = 1f;
                raw.x = cx + (worldPos.x - cx) / wm;
                raw.z = cz + (worldPos.z - zOffset - _levelSafeZShift - cz) / hm;
            }
            return raw;
        }

        // [LATTICE_PHASE 2026-06-12] 격자 위상 앵커 — 레벨 풍선들의 최소 raw 좌표.
        //   레벨 데이터는 "보드 폭 고정 / 그리드 수 분할" spacing(예: 30컬럼 → cs=0.22)에
        //   center 오프셋(balloonCenterZ=2.0 등)이 섞여 있어, 절대 라운딩 Round(raw/cs)는
        //   짝수 그리드 레벨에서 좌표가 정확히 .5×cs 경계에 놓인다 → Unity 라운딩(짝수行)으로
        //   인접 두 컬럼/행이 한 라인 키로 합쳐지고 사이 키는 건너뜀 → 관통(합쳐진 라인의
        //   전면 셀이 옆 컬럼 풍선) + 놓침(건너뛴 키 라인엔 후보 없음). 위상(min) 기준 상대
        //   라운딩이면 모든 풍선 키가 정확한 정수가 되어 경계 문제가 구조적으로 소멸.
        //   (MapMaker 미리보기 빈줄 fix 와 동일 클래스 — 2026-06-12)
        private float _latticePhaseX;
        private float _latticePhaseZ;

        private void RecomputeRawLatticePhase()
        {
            _latticePhaseX = 0f;
            _latticePhaseZ = 0f;
            float minX = float.MaxValue, minZ = float.MaxValue;
            foreach (BalloonData d in _balloons.Values)
            {
                if (d == null) continue;
                if (d.position.x < minX) minX = d.position.x;
                if (d.position.z < minZ) minZ = d.position.z;
            }
            if (minX < float.MaxValue) _latticePhaseX = minX;
            if (minZ < float.MaxValue) _latticePhaseZ = minZ;
        }

        /// <summary>모든 셀/라인 스냅(DirectionalTargeting/DartManager/BoardStateManager)이 공유하는
        /// 격자 위상. raw 보드 공간 좌표에서 이 값을 뺀 뒤 cellSpacing 으로 나눠야 정수 키가 보장된다.</summary>
        public void GetRawLatticePhase(out float phaseX, out float phaseZ)
        {
            phaseX = _latticePhaseX;
            phaseZ = _latticePhaseZ;
        }

        /// <summary>풍선 월드 위치 — frame 단위 cache. 같은 frame 안에서 N번 호출돼도 transform.position 은 frame 당 1회만.
        /// 1 frame stale 허용되는 hot path 전용 (FindTarget candidates loop 등 — perp tolerance 가 cellSpacing 100% 라 stale 영향 없음).
        /// 정확한 위치 필요하면 GetBalloonWorldPosition() 사용.</summary>
        public Vector3 GetBalloonWorldPositionCached(int balloonId)
        {
            RefreshFramePositionCacheIfNeeded();
            if (_frameCachedPositions.TryGetValue(balloonId, out Vector3 cached))
                return cached;
            // Cache 미스 — fallback to direct.
            return GetBalloonWorldPosition(balloonId);
        }

        /// <summary>
        /// Active(non-popped) 풍선의 world position 을 caller-provided list 에 채워줌.
        /// Frame cache 활용 — 같은 frame 안에서 BuildOccupancyMap 등 다른 호출이 있었으면 0 transform 호출.
        /// </summary>
        public void GetActivePositions(List<Vector3> output)
        {
            if (output == null) return;
            output.Clear();
            RefreshFramePositionCacheIfNeeded();
            foreach (var kvp in _frameCachedPositions)
                output.Add(kvp.Value);
        }

        /// <summary>풍선 오브젝트의 현재 localScale. 오브젝트 없으면 추정 스케일 반환.</summary>
        public Vector3 GetBalloonWorldScale(int balloonId)
        {
            if (_balloonObjects.TryGetValue(balloonId, out GameObject obj) && obj != null)
                return obj.transform.localScale;
            float scaleMult = _levelSafeCalculated
                ? Mathf.Max(_levelSafeWm, _levelSafeHm)
                : (GameManager.HasInstance ? Mathf.Max(GameManager.Instance.Board.balloonFieldWidthMult, GameManager.Instance.Board.balloonFieldHeightMult) : 1f);
            return GetBalloonRestScale(scaleMult);
        }

        // 밀집 레벨 안전 배율(scaleMult)로 풍선이 축소돼도 화면 높이(Y, 월드 업)는 유지.
        // footprint(X/Z)만 축소해 제거 시 pop 피드백이 납작해지지 않게 한다.
        // ROLLBACK_BALLOON_FIXED_SCALE_Y_20260608: 이전 Y = _balloonScale (레벨/맵별 가변 → 레벨마다 높이 차이).
        // [#2] Y 를 0.35 절대 고정. X/Z(footprint)는 맵사이즈(_balloonScale*scaleMult)대로 유지.
        // [#2 개정 2026-06-10] 보드 가로(gridCols) ≤26 이면 y = x×1.1 비례 (작은 보드는 풍선이 커서 0.35 가 납작해 보임),
        //   ≥27 또는 미설정(0)이면 기존 0.35 고정. 롤백: 아래를 0.35 고정 단일식으로 복원 + SetBoardGridCols 제거.
        private Vector3 GetBalloonRestScale(float scaleMult)
        {
            float xz = _balloonScale * scaleMult;
            float y = (_boardGridCols > 0 && _boardGridCols <= SMALL_BOARD_MAX_GRID_COLS)
                ? xz * SMALL_BOARD_SCALE_Y_RATIO
                : BALLOON_FIXED_SCALE_Y;
            return new Vector3(xz, y, xz);
        }

        /// <summary>
        /// Returns all non-popped balloons matching the specified color.
        /// Hidden balloons whose color is concealed are excluded until revealed.
        /// </summary>
        // 재사용 리스트 (GC 할당 방지)
        private readonly List<BalloonData> _reusableColorResult = new List<BalloonData>(64);

        public BalloonData[] GetBalloonsByColor(int color)
        {
            _reusableColorResult.Clear();
            foreach (KeyValuePair<int, BalloonData> pair in _balloons)
            {
                BalloonData data = pair.Value;
                if (data.isPopped) continue;
                if (_hiddenBalloons.Contains(data.balloonId)) continue;
                if (data.gimmickType == GimmickWall) continue;
                if (data.gimmickType == GimmickIce) continue;
                if (data.gimmickType == GimmickColorCurtain) continue;
                if (data.gimmickType == GimmickLockKey) continue;
                if (data.color == color)
                    _reusableColorResult.Add(data);
            }
            return _reusableColorResult.ToArray();
        }

        /// <summary>
        /// Returns all non-popped balloons matching the specified color, INCLUDING gimmick
        /// balloons (Wall, Pin, Ice, Hidden, etc.). Used by the Color Remove booster so that
        /// every balloon of the chosen color is cleared regardless of gimmick state.
        /// </summary>
        /// <summary>외곽 풍선만 Outline pass 활성. 매 pop 시 호출되니까 throttle + 외곽 diff 적용.
        /// 첫 호출: 모든 풍선 0 + 외곽 set 1. 이후: 이전 외곽 vs 새 외곽 diff 만 update.</summary>
        private static readonly int _propBalloonOutlineEnabled = Shader.PropertyToID("_OutlineEnabled");
        private static MaterialPropertyBlock _balloonMpb;
        // ROLLBACK_OUTLINE_MATERIAL_SWAP_20260609 (Option A): 외곽 풍선 아웃라인 = MPB 대신 머티리얼 swap.
        //   MPB 는 SRP Batcher 를 깸 → 내부 1500 배칭 유지 위해, 외곽선 대상만 multi-pass outline 셰이더 머티리얼로 교체.
        //   _outlinedTwins: 원본 머티리얼 → outline 트윈(셰이더만 ItemSharedOutline) 캐시(공유). _outlinedBalloonIds: 현재 hull 부착 중인 풍선 ID.
        //   롤백: ApplyOutlineToBalloon 을 MPB 버전으로 복원 + 이 3 필드 제거 + ItemSharedOutline.shader 삭제.
        private static Shader _outlineShaderCached;
        private static Material _outlineHullMat; // 공유 Custom/OutlineHull 머티리얼(모든 외곽선 material[1] 공용 → 한 배치)
        private static readonly Dictionary<Material, Material> _outlinedTwins = new Dictionary<Material, Material>(); // (구 트윈 방식 — 미사용)
        private readonly HashSet<int> _outlinedBalloonIds = new HashSet<int>();
        private static readonly List<Material> _sharedMatBuffer = new List<Material>(4); // GetSharedMaterials 무할당 조회용
        // ROLLBACK_OUTLINE_MATARRAY_SCRATCH_20260616: sharedMaterials 세터는 배열 '내용'을 복사하므로 공유 static
        //   scratch 재사용이 안전 → outline enable/disable 전환마다의 new Material[] GC(히치) 제거. 롤백: new Material[]{...} 복원.
        private static readonly Material[] _outlineMatScratch2 = new Material[2]; // [body, hull]
        private static readonly Material[] _outlineMatScratch1 = new Material[1]; // [body] (hull 제거 복원)
        private readonly HashSet<int> _prevOutermostSet = new HashSet<int>();
        private readonly List<int> _outerDiffBuffer = new List<int>(64);
        private bool _hasAppliedOutermostOutline;
        private float _lastOutermostRefreshTime;

        // ROLLBACK_OUTLINE_PHASES_20260609: 3D 퀄업 아웃라인 단계별 게이트 (ItemShared Outline Pass 부활 전제).
        //   PHASE1 = 보관함 front-row (HolderIdentifier 가 직접, 별도 플래그 없음 — 항상 ON).
        //   PHASE2 = 다트 아웃라인 → 진행 시 EnableDartOutline_Phase2 = true.
        //   PHASE3 = 외곽 contour 풍선 아웃라인 → 진행 시 EnableContourOutline_Phase3 = true.
        //   PHASE4 = 풍선 광택(_GLOSS) — 셰이더/머티리얼 별도(미구현).
        //   롤백: 두 플래그 false 로 두면 해당 단계 비활성(Pass 는 살아있어도 ON 오브젝트 없음 → batch 유지).
        // ROLLBACK_DART_OUTLINE_BAKE_20260615: PHASE2(레일 다트 아웃라인) ON.
        //   메커니즘을 MPB → 공유 머티리얼 베이크로 교체(DartIdentifier 참조)했으므로 배칭 유지된 채 활성.
        //   롤백: 이 줄을 다시 `= false;` 로 두면 다트 아웃라인 비활성(배칭 영향 없음).
        // ROLLBACK_DART_OUTLINE_OFF_PERFTEST_20260617: true → false (프레임드랍 원인 A/B 테스트).
        //   증상: 풍선 많을 땐 정상, '레일에 다트 깔면' 일정수치 드랍. 분석: 다트 아웃라인은 contour 만 하는
        //   풍선과 달리 ApplyColor 가 '모든 다트'에 hull material[1] 부착 → body 렌더러가 2머티리얼=메시 2회
        //   렌더(색+법선확장 hull). 드로우콜은 배칭돼 0 증가지만 GPU 정점+outline fill 은 다트수(≤160)에 비례 →
        //   GPU-bound 저사양서 '다트 깔수록 드랍'. 끄면 다트 렌더가 사실상 절반.
        //   → false 로 빌드/측정해 드랍이 사라지면 '다트 아웃라인이 원인' 확정. 확정 후: 유지(off) 또는
        //     '일부 다트만 아웃라인'(풍선 contour 처럼) 으로 비용 낮춰 부활 결정.
        //   롤백: false → true (아웃라인 복원).
        public static bool EnableDartOutline_Phase2 = false;
        // ROLLBACK_OUTLINE_PHASE3_ON_20260609: 풍선 contour(최외각) 아웃라인 ON (Option A 머티리얼 swap).
        //   contour 풍선만 ItemSharedOutline 머티리얼로 swap(MPB 아님) → 그 소수(~수십)만 multi-pass 개별 draw.
        //   내부 1500 은 ItemShared(single-pass) 유지 → SRP Batch. (과거 프레임드랍 = 공유 셰이더 multi-pass 였음 — 해결.)
        public static bool EnableContourOutline_Phase3 = true;

        public void RefreshOutermostRendererState()
        {
            // ROLLBACK_OUTLINE_PHASE3_CONTOUR_20260609: PHASE3 OFF 동안 외곽 contour 아웃라인 미적용
            //   → 풍선에 MPB 안 찍어 instanced batch 유지(PHASE1 격리). PHASE3 진행 시 플래그 true.
            if (!EnableContourOutline_Phase3) return;
            // [Outline 2026-05-10] 가드 제거 — Lobby/MapMaker/직접 InGame 진입 등 다양한 경로에서 inspector toggle 의존 없이 항상 작동.
            // 롤백: 아래 주석 라인 해제.
            // if (!_outlineOnOuterOnly) return;

            // 사용자 보고: 매 pop 시 1620 iterate spike → throttle.
            // 0.1s 안 중복 호출은 skip — 마지막 pop 후 한 번만 정리.
            float now = Time.unscaledTime;
            if (now - _lastOutermostRefreshTime < 0.1f) return;
            _lastOutermostRefreshTime = now;

            if (_balloonMpb == null) _balloonMpb = new MaterialPropertyBlock();

            // [Outline 2026-05-10] 사용자 의도 정정 — outline = "공격 가능한 풍선 (DirectionalTargeting contour)" 이지
            // BoardStateManager.IsOutermost (Wall/Ice 포함 단순 외곽) 가 아님.
            // DirectionalTargeting.GetAttackableContourIds 가 외곽 + targetable + targetable 풍선 ID 집합 반환.
            // 롤백: 아래 contour 사용 분기 제거 + 주석 처리된 원본 BoardStateManager 분기 복원.
            // ── 원본 BoardStateManager.IsOutermost 분기 ──
            // if (!BoardStateManager.HasInstance) return;
            // var bsm = BoardStateManager.Instance;
            // bsm.IsOutermost(0);  // dummy 로 cache 갱신
            // _outerDiffBuffer.Clear();
            // var en = _balloonObjects.GetEnumerator();
            // try {
            //     while (en.MoveNext()) {
            //         int id = en.Current.Key;
            //         if (bsm.IsOutermost(id)) _outerDiffBuffer.Add(id);
            //     }
            // } finally { en.Dispose(); }

            // [Outline 2026-05-10 fix] diff 방식 → 전수 처리 변경.
            // 이전 diff 방식은 _prevOutermostSet 비어있는 첫 호출 시 비외곽 풍선들에 _OutlineEnabled=0 적용 못함 →
            // mat default (=1) 그대로 → 모든 풍선 outline 보임. 사용자 보고 "Outline On Outer Only ON 인데 모두 outline".
            // 전수 sweep 으로 매 호출 시 모든 풍선에 정확한 값 적용 보장.
            // 비용: _balloonObjects.Count × O(1) HashSet lookup × 0.1s throttle → 부담 작음.
            // 롤백: 아래 sweep 코드 제거 + 위 주석 처리된 원본 diff 코드 복원.
            float __contourStamp = InGamePerfLogger.StartStampMs();
            HashSet<int> contourSet = null;
            var contourCol = DirectionalTargeting.GetAttackableContourIds();
            if (contourCol is HashSet<int> hs) contourSet = hs;
            InGamePerfLogger.EndSection(__contourStamp, "Balloon.RefreshOutline.BuildContour");

            float __applyStamp = InGamePerfLogger.StartStampMs();
            _outerDiffBuffer.Clear();
            if (!_hasAppliedOutermostOutline)
            {
                // ROLLBACK_OUTLINE_SUBSET_MPB_20260609: 첫 패스 전수 sweep 폐지 → contour(외곽)만 MPB.
                //   비-contour 1500 은 머티리얼 default _OutlineEnabled=0 이라 MPB 불필요 → MPB 안 찍어 인스턴싱/배칭 유지.
                //   (과거: 전부 sweep → 1500 MPB → SRP Batcher/GRD 깨짐 → 2140 개별 draw. 그게 프레임드랍 원인.)
                //   롤백: 아래를 _balloonObjects 전수 루프 + ApplyOutlineToBalloon(id, isContour) 로 복원.
                foreach (int id in contourCol)
                {
                    ApplyOutlineToBalloon(id, true);
                    _outerDiffBuffer.Add(id);
                }
                _hasAppliedOutermostOutline = true;
            }
            else
            {
                foreach (int id in contourCol)
                {
                    if (!_prevOutermostSet.Contains(id))
                        ApplyOutlineToBalloon(id, true);
                    _outerDiffBuffer.Add(id);
                }

                foreach (int id in _prevOutermostSet)
                {
                    bool stillContour = contourSet != null
                        ? contourSet.Contains(id)
                        : ContainsContour(contourCol, id);
                    if (!stillContour)
                        ApplyOutlineToBalloon(id, false);
                }
            }

            // _prevOutermostSet 갱신 (호환 유지 — 외부에서 읽는 코드 있을 수 있음)
            _prevOutermostSet.Clear();
            for (int i = 0; i < _outerDiffBuffer.Count; i++) _prevOutermostSet.Add(_outerDiffBuffer[i]);
            InGamePerfLogger.EndSection(__applyStamp, "Balloon.RefreshOutline.ApplyRenderers");
        }

        private static bool ContainsContour(IReadOnlyCollection<int> col, int id)
        {
            foreach (int c in col) if (c == id) return true;
            return false;
        }

        // ROLLBACK_OUTLINE_MATERIAL_SWAP_20260609 (Option A): 머티리얼 swap 방식.
        //   enable: 렌더러 sharedMaterial 을 outline 트윈으로 교체(원본 보관). disable: 원본 복원. MPB 미사용 → 내부 batch 무손상.
        // public: 공유 outline-hull 머티리얼 1개(Custom/OutlineHull, single-pass). 모든 외곽선이 이 1개를 material[1] 로 공유 → 한 배치.
        public static Material GetOutlineHullMaterial()
        {
            if (_outlineHullMat != null) return _outlineHullMat;

            // ROLLBACK_OUTLINEHULL_BUILD_STRIP_20260609: 빌드 스트리핑 방지.
            //   OutlineHull 셰이더는 코드(Shader.Find)에서만 참조 → 이를 쓰는 머티리얼 에셋이 없으면 빌드에서 strip 되어
            //   Shader.Find 가 null → 빌드에서만 아웃라인 사라짐(에디터는 정상). Resources 머티리얼을 두면 빌드에 항상 포함됨.
            //   Resources 우선 로드, 실패 시 기존 Shader.Find 폴백(에디터/안전망).
            _outlineHullMat = Resources.Load<Material>("Materials/OutlineHull");
            if (_outlineHullMat == null)
            {
                if (_outlineShaderCached == null) _outlineShaderCached = Shader.Find("Custom/OutlineHull");
                if (_outlineShaderCached == null) return null;
                _outlineHullMat = new Material(_outlineShaderCached);
                _outlineHullMat.SetColor("_OutlineColor", Color.black);
                _outlineHullMat.SetFloat("_OutlineWidth", 0.0005f); // 기본 두께 0.0005 (요구)
            }
            _outlineHullMat.enableInstancing = true;
            // ROLLBACK_OUTLINE_GROUP_SILHOUETTE_20260610: 바디(Geometry=2000) 전부 이후에 hull 을 그려야
            //   stencil 마스크(바디=1, hull=NotEqual)가 전체 유니온에 성립 → 그룹 실루엣 아웃라인.
            //   Resources .mat 에 옛 queue 가 직렬화돼 있을 수 있어 셰이더 Queue+10 과 별개로 코드에서 명시.
            _outlineHullMat.renderQueue = 2010;
            _outlinedTwins.Clear(); // 더 이상 사용 안 함(트윈 방식 폐기)
            return _outlineHullMat;
        }

        // ROLLBACK_OUTLINE_MATERIAL_SWAP_20260609: 외곽선 = body 렌더러의 material[1] 에 공유 hull 추가/제거.
        //   body = BalloonIdentifier.ColorRenderers (그림자/라벨 제외 — prefab 구성 유지). MPB/멀티패스 셰이더 안 씀.
        //   → body(material[0]) 는 그대로 SRP batch, hull(material[1]=공유 1개) 끼리 한 배치. 풍선 1500 무손상.
        // [Outline fix 2026-06-10] disable 가 .sharedMaterial(=[0]만 교체) 로 복원해 hull([1]) 이 영구 잔존하던 버그 수정.
        //   HolderIdentifier.RestoreRenderers 와 동일 패턴 — 현재 [0](현재 색) 유지 + hull 만 제거. 원본 배열 보관 폐기.
        private void ApplyOutlineToBalloon(int balloonId, bool enableOutline)
        {
            if (!_balloonObjects.TryGetValue(balloonId, out GameObject obj) || obj == null)
            {
                _outlinedBalloonIds.Remove(balloonId);
                return;
            }
            BalloonIdentifier bi = obj.GetComponent<BalloonIdentifier>();
            Renderer[] bodyRenderers = (bi != null) ? bi.ColorRenderers : null;
            if (bodyRenderers == null || bodyRenderers.Length == 0) return;

            if (enableOutline)
            {
                if (!_outlinedBalloonIds.Add(balloonId)) return; // 이미 outlined
                Material hull = GetOutlineHullMaterial();
                if (hull == null) { _outlinedBalloonIds.Remove(balloonId); return; }
                for (int r = 0; r < bodyRenderers.Length; r++)
                {
                    var rend = bodyRenderers[r];
                    if (rend == null) continue;
                    _outlineMatScratch2[0] = rend.sharedMaterial; // [SCRATCH] new Material[2] 대신 공유 배열(세터가 내용 복사)
                    _outlineMatScratch2[1] = hull;
                    rend.sharedMaterials = _outlineMatScratch2;   // material[1] 에 hull 추가
                }
            }
            else
            {
                if (!_outlinedBalloonIds.Remove(balloonId)) return;
                StripOutlineHull(bodyRenderers);
            }
        }

        // hull([1]) 제거 — [0](현재 색)은 유지. disable 복원 + 풀 재사용 풍선의 잔존 hull 정리 공용.
        private static void StripOutlineHull(Renderer[] bodyRenderers)
        {
            if (bodyRenderers == null) return;
            for (int r = 0; r < bodyRenderers.Length; r++)
            {
                var rend = bodyRenderers[r];
                if (rend == null) continue;
                rend.GetSharedMaterials(_sharedMatBuffer);
                if (_sharedMatBuffer.Count > 1)
                {
                    _outlineMatScratch1[0] = _sharedMatBuffer[0]; // [SCRATCH] new Material[1] 대신 공유 배열
                    rend.sharedMaterials = _outlineMatScratch1;
                }
            }
        }

        /// <summary>재사용 리스트 — GetAllBalloonsByColor GC 방지</summary>
        private readonly List<BalloonData> _reusableColorList = new List<BalloonData>(256);

        public BalloonData[] GetAllBalloonsByColor(int color)
        {
            _reusableColorList.Clear();
            foreach (KeyValuePair<int, BalloonData> pair in _balloons)
            {
                BalloonData data = pair.Value;
                if (data.isPopped) continue;
                if (data.gimmickType == GimmickLockKey) continue;
                if (data.color == color)
                    _reusableColorList.Add(data);
            }
            return _reusableColorList.ToArray();
        }

        // ROLLBACK_ZAP_GIMMICK_COLOR_TARGETS_20260623:
        // Zap removes the selected color's live components, not always the whole object.
        // TargetBox contributes one hit per live matching egg HP, and FlexTube contributes only
        // logical Segment cells. Start/End caps are visual endpoints, not extra Zap HP.
        public int GetZapHitCountForColor(BalloonData data, int color)
        {
            if (data == null || data.isPopped) return 0;
            if (data.gimmickType == GimmickLockKey) return 0;

            if (data.gimmickType == GimmickPinataBox && data.eggColors != null && data.eggHps != null)
            {
                int count = 0;
                int n = Mathf.Min(data.eggColors.Length, data.eggHps.Length);
                for (int i = 0; i < n; i++)
                {
                    if (data.eggColors[i] == color && data.eggHps[i] > 0)
                        count += data.eggHps[i];
                }
                return count;
            }

            if (data.gimmickType == GimmickFlexTube)
            {
                if (data.color != color) return 0;
                if (GimmickIdentifier.FlexTubePartFromString(data.flexTubePartType) != GimmickIdentifier.FlexTubePart.Segment)
                    return 0;
                // ROLLBACK_FLEXTUBE_2X2_PARTS_20260626: 한 Segment 파트(seq)는 2×2 = 4셀이지만 튜브 HP/타격은
                //   파트 단위(HP = 중간 파트 수). zap 도 파트당 1회만 세야 함 — 안 그러면 4셀×= HP 의 4배 타겟이
                //   생겨 부스터 다트가 이미 죽은 튜브에 낭비된다. 그룹+seq 의 대표(최소 balloonId) 셀에서만 1 반환.
                return IsFlexTubeSeqRepresentative(data) ? 1 : 0;
            }

            return data.color == color ? 1 : 0;
        }

        /// <summary>같은 group+seq 의 살아있는 FlexTube 셀 중 최소 balloonId 면 true — 파트(2×2) 당 1회 집계용.</summary>
        private bool IsFlexTubeSeqRepresentative(BalloonData data)
        {
            int g = data.flexTubeGroupId, s = data.flexTubeSequenceIndex;
            if (g < 0) return true;
            int minId = data.balloonId;
            foreach (var kv in _balloons)
            {
                BalloonData c = kv.Value;
                if (c == null || c.isPopped || c.gimmickType != GimmickFlexTube) continue;
                if (c.flexTubeGroupId == g && c.flexTubeSequenceIndex == s && c.balloonId < minId)
                    minId = c.balloonId;
            }
            return minId == data.balloonId;
        }

        public bool TryApplyZapColorHit(int balloonId, int color)
        {
            if (!_balloons.TryGetValue(balloonId, out BalloonData data)) return false;
            if (data == null || data.isPopped) return false;
            if (GetZapHitCountForColor(data, color) <= 0) return false;

            if (data.gimmickType == GimmickPinataBox && data.eggColors != null && data.eggHps != null)
            {
                PopResult result = ProcessPinataBoxEggHit(data, color);
                return result.success || result.hitAccepted;
            }

            if (data.gimmickType == GimmickFlexTube)
            {
                PopResult result = PopBalloonWithDart(balloonId, color);
                return result.success || result.hitAccepted;
            }

            ForcePopBalloon(balloonId);
            return true;
        }

        // 이어하기 랜덤 풍선 제거용 후보 id 버퍼 (재사용). balloonId 는 안정적이라
        // pop 중 발생하는 이벤트가 _reusableColorList 를 건드려도 영향 없음.
        private List<int> _continueColorBalloonIds;

        /// <summary>
        /// 이어하기 전용: 지정 색 풍선을 '랜덤'하게 최대 count 개 pop. 실제 pop 개수 반환.
        /// LockKey / FlexTube 기믹은 force-pop 대상에서 제외 (있는 만큼만).
        /// </summary>
        public int PopRandomBalloonsByColor(int color, int count)
        {
            if (count <= 0) return 0;

            if (_continueColorBalloonIds == null) _continueColorBalloonIds = new List<int>(64);
            _continueColorBalloonIds.Clear();
            foreach (KeyValuePair<int, BalloonData> pair in _balloons)
            {
                BalloonData data = pair.Value;
                if (data.isPopped) continue;
                if (data.gimmickType == GimmickLockKey) continue;
                if (data.gimmickType == GimmickFlexTube) continue;
                if (data.color == color) _continueColorBalloonIds.Add(data.balloonId);
            }

            int available = _continueColorBalloonIds.Count;
            int toPop = Mathf.Min(count, available);
            if (toPop <= 0) return 0;

            // Fisher-Yates 부분 셔플 — 앞 toPop 개를 무작위로 선정.
            for (int i = 0; i < toPop; i++)
            {
                int j = UnityEngine.Random.Range(i, available);
                int tmp = _continueColorBalloonIds[i];
                _continueColorBalloonIds[i] = _continueColorBalloonIds[j];
                _continueColorBalloonIds[j] = tmp;
            }

            int popped = 0;
            for (int i = 0; i < toPop; i++)
            {
                int id = _continueColorBalloonIds[i];
                if (!_balloons.TryGetValue(id, out BalloonData bd) || bd.isPopped) continue;
                ForcePopBalloon(id);
                popped++;
            }
            return popped;
        }

        /// <summary>재사용 배열 — GetAllBalloons GC 방지</summary>
        private BalloonData[] _reusableAllBalloons;

        /// <summary>
        /// Returns all balloon data entries (including popped), for board state inspection.
        /// </summary>
        public BalloonData[] GetAllBalloons()
        {
            if (_reusableAllBalloons == null || _reusableAllBalloons.Length != _balloons.Count)
                _reusableAllBalloons = new BalloonData[_balloons.Count];

            int i = 0;
            foreach (BalloonData d in _balloons.Values)
            {
                _reusableAllBalloons[i++] = d;
            }
            return _reusableAllBalloons;
        }

        /// <summary>재사용 배열 — GetAliveBalloons GC 방지 (GetAllBalloons 와 분리해 동일프레임 동시호출 안전)</summary>
        private BalloonData[] _reusableAliveBalloons;

        // ROLLBACK_ALIVE_BALLOON_ITERATION_20260616:
        //   팝된 풍선은 _balloons 에서 제거되지 않아(isPopped=true 로 영구 잔존) _balloons.Count 가
        //   레벨 내내 초기값(예: 729)에 고정된다. 매 팝마다 invalidate 되는 보드 전수 빌드
        //   (DirectionalTargeting.BuildEdgeTargetCache / BoardStateManager.GetOutermostBalloonColors)는
        //   살아있는 풍선이 50개여도 729 전체를 복사·순회하며 700+ 死엔트리를 매번 skip 한다.
        //   이 메서드는 non-popped 만 앞쪽에 채워 반환(out aliveCount) → 소비처가 aliveCount 까지만 순회.
        //   isPopped skip 가드는 소비처에 그대로 두어(now no-op) 동작 100% 불변, 순회량만 감소.
        //   롤백: 이 메서드 제거 + 두 소비처를 GetAllBalloons()/all.Length 로 환원.
        /// <summary>
        /// Returns a reused array containing only non-popped balloons in [0, aliveCount).
        /// Entries beyond aliveCount are stale — callers MUST iterate only up to aliveCount.
        /// Additive sibling of GetAllBalloons (which still returns the full popped-inclusive set).
        /// </summary>
        public BalloonData[] GetAliveBalloons(out int aliveCount)
        {
            if (_reusableAliveBalloons == null || _reusableAliveBalloons.Length != _balloons.Count)
                _reusableAliveBalloons = new BalloonData[_balloons.Count];

            int i = 0;
            foreach (BalloonData d in _balloons.Values)
            {
                if (d == null || d.isPopped) continue;
                _reusableAliveBalloons[i++] = d;
            }
            aliveCount = i;
            return _reusableAliveBalloons;
        }

        /// <summary>
        /// Returns the current count of non-popped balloons.
        /// </summary>
        public int GetRemainingCount()
        {
            return RemainingCount;
        }

        /// <summary>
        /// Attempts to pop a balloon by id. Applies gimmick behavior before/after pop.
        /// </summary>
        /// <returns>PopResult describing outcome and any side effects.</returns>
        public PopResult PopBalloon(int balloonId)
        {
            if (!_balloons.TryGetValue(balloonId, out BalloonData data))
            {
                return new PopResult { success = false, reason = "NotFound" };
            }

            if (data.isPopped)
            {
                return new PopResult { success = false, reason = "AlreadyPopped" };
            }

            // Delegate pre-pop guard check to GimmickProcessor
            if (GimmickProcessor.HasInstance)
            {
                string blockReason = GimmickProcessor.Instance.CheckDartBlocker(data.balloonId, data.gimmickType, -1);
                if (blockReason != null)
                {
                    return new PopResult { success = false, reason = blockReason, balloonId = data.balloonId, gimmickType = data.gimmickType };
                }
            }
            else
            {
                // Fallback: Wall/Ice always blocked
                if (data.gimmickType == GimmickWall)
                    return new PopResult { success = false, reason = "Wall: indestructible", balloonId = data.balloonId, gimmickType = GimmickWall };
                if (data.gimmickType == GimmickIce)
                    return new PopResult { success = false, reason = "Ice: indirect only", balloonId = data.balloonId, gimmickType = GimmickIce };
                if (data.gimmickType == GimmickColorCurtain)
                    return new PopResult { success = false, reason = "ColorCurtain: indirect only", balloonId = data.balloonId, gimmickType = GimmickColorCurtain };
            }

            // Pin: same-color dart progressive removal
            if (data.gimmickType == GimmickPin && GimmickProcessor.HasInstance)
            {
                // dartColor is unknown here — caller should use PopBalloonWithDart for Pin
                return new PopResult { success = false, reason = "Pin: use PopBalloonWithDart", balloonId = data.balloonId, gimmickType = GimmickPin };
            }

            // Frozen Dart: 2-hit field gimmick (1st hit = thaw, 2nd hit = pop)
            if (data.gimmickType == GimmickFrozenDart)
            {
                return ProcessFrozenDartHit(data);
            }

            // Barricade: destructible wall with HP
            if (data.gimmickType == GimmickBarricade)
            {
                return ProcessBarricadeHit(data);
            }

            // FlexTube: dartColor 없는 경로(force-pop 시도, indirect)는 무조건 reject — 색 검증 필요.
            if (data.gimmickType == GimmickFlexTube)
            {
                return new PopResult { success = false, reason = "FlexTube: requires dart color", balloonId = data.balloonId, gimmickType = GimmickFlexTube };
            }

            // Pinata and Pinata Box require multiple hits
            if (data.gimmickType == GimmickPinata || data.gimmickType == GimmickPinataBox)
            {
                return ProcessPinataHit(data);
            }

            // Standard pop
            return ExecutePop(data);
        }

        /// <summary>
        /// Returns the remaining non-popped balloon count.
        /// </summary>
        public int GetRemainingBalloonCount()
        {
            return RemainingCount;
        }

        /// <summary>
        /// Pops a balloon with dart color context. Required for Pin (same-color progressive)
        /// and ColorCurtain (specific color required).
        /// </summary>
        public PopResult PopBalloonWithDart(int balloonId, int dartColor)
        {
            if (!_balloons.TryGetValue(balloonId, out BalloonData data))
                return new PopResult { success = false, reason = "NotFound" };

            // FlexTube: isPopped 가드 이전에 처리 — stale target(이미 비활성 cell 향한 다트) 도 부모 FlexTube 에 위임.
            // FlexTube 가 _destroying / 활성 segment 유무 / 색 매칭 자체 판단. 다트는 hit 인정 후 소진.
            if (data.gimmickType == GimmickFlexTube)
            {
                // ROLLBACK_FLEXTUBE_CELL_TARGET_HIT_20260628:
                // Keep the target balloonId when delegating to the FlexTube owner. The previous
                // IDartHittable fallback could pick the first active visual part in the group and
                // remove a different logical cell/seq than the one DirectionalTargeting selected.
                FlexTube flexTube = null;
                for (int i = 0; i < _flexTubeRoots.Count; i++)
                {
                    var root = _flexTubeRoots[i];
                    if (root == null) continue;
                    var ft = root.GetComponent<FlexTube>();
                    if (ft != null && ft.GroupId == data.flexTubeGroupId)
                    {
                        flexTube = ft;
                        break;
                    }
                }

                bool accepted = flexTube != null && flexTube.TryApplyDartHit(dartColor, balloonId);
                return new PopResult
                {
                    success = false,
                    hitAccepted = accepted,
                    reason = accepted ? "FlexTube: target cell delegated to owner" : "FlexTube: no live target cell",
                    balloonId = data.balloonId,
                    gimmickType = GimmickFlexTube
                };
            }

            if (data.isPopped)
                return new PopResult { success = false, reason = "AlreadyPopped" };

            // GimmickProcessor pre-pop guard with dart color
            if (GimmickProcessor.HasInstance)
            {
                string blockReason = GimmickProcessor.Instance.CheckDartBlocker(data.balloonId, data.gimmickType, dartColor);
                if (blockReason != null)
                    return new PopResult { success = false, reason = blockReason, balloonId = data.balloonId, gimmickType = data.gimmickType };
            }

            // Pin: same-color dart progressive removal
            if (data.gimmickType == GimmickPin && GimmickProcessor.HasInstance)
            {
                bool destroyed = GimmickProcessor.Instance.ProcessPinHit(data.balloonId, dartColor, data.color);
                UpdatePinVisual(data.balloonId, destroyed);
                if (!destroyed)
                    return new PopResult { success = false, hitAccepted = true, reason = "Pin: segment removed, not fully destroyed", balloonId = data.balloonId, gimmickType = GimmickPin };
                return ExecutePop(data);
            }

            // Frozen Dart: 2-hit field gimmick
            if (data.gimmickType == GimmickFrozenDart)
                return ProcessFrozenDartHit(data);

            // [ROLLBACK_PIN_BARRICADE_MERGE]
            // Barricade 가 Pin mechanic (색 매칭 + segment 점진 제거) 사용 + Barricade visual transform.
            // 롤백 시 이 블록 제거 + 아래 ProcessBarricadeHit 분기 복원.
            if (data.gimmickType == GimmickBarricade && GimmickProcessor.HasInstance)
            {
                bool destroyed = GimmickProcessor.Instance.ProcessPinHit(data.balloonId, dartColor, data.color);
                UpdateBarricadeVisualAfterHit(data, destroyed);
                if (!destroyed)
                    return new PopResult { success = false, hitAccepted = true, reason = "Barricade: segment removed, not fully destroyed", balloonId = data.balloonId, gimmickType = GimmickBarricade };
                return ExecutePop(data);
            }

            // Target Box 알 모델: 다트 색과 같은 색 알의 HP 차감, 해당 알 0이면 제거, 전부 제거 시 박스 파괴.
            if (data.gimmickType == GimmickPinataBox
                && data.eggColors != null && data.eggHps != null && data.eggColors.Length > 0)
                return ProcessPinataBoxEggHit(data, dartColor);

            // Pinata/PinataBox (legacy 단일 박스)
            if (data.gimmickType == GimmickPinata || data.gimmickType == GimmickPinataBox)
                return ProcessPinataHit(data);

            return ExecutePop(data);
        }

        // Target Box 알 모델 타격: 같은 색 살아있는 알 1개의 HP -1 → 0 이면 시각 제거(HideEgg) → 전부 제거 시 박스 pop.
        private PopResult ProcessPinataBoxEggHit(BalloonData data, int dartColor)
        {
            int[] colors = data.eggColors;
            int[] hps = data.eggHps;

            int target = -1;
            for (int i = 0; i < colors.Length; i++)
                if (colors[i] == dartColor && i < hps.Length && hps[i] > 0) { target = i; break; }

            // 박스에 없는 색 다트 — 타격 무효 (타겟팅이 정상이면 거의 발생 안 함).
            if (target < 0)
                return new PopResult { success = false, reason = "TargetBox: no live egg of color", balloonId = data.balloonId, gimmickType = GimmickPinataBox };

            hps[target] = Mathf.Max(0, hps[target] - 1);

            // 비주얼: egg 항목별 1:1. HP 반영 — 절반 이하면 균열(texture) 활성, 0 이면 알 제거.
            bool eggDied = hps[target] == 0;
            if (_balloonObjects.TryGetValue(data.balloonId, out GameObject obj) && obj != null)
            {
                var view = obj.GetComponentInChildren<PinataBoxView>(true);
                if (view != null) view.UpdateEggHp(target, hps[target]);
            }

            bool anyAlive = false;
            for (int i = 0; i < hps.Length; i++) if (hps[i] > 0) { anyAlive = true; break; }

            if (!anyAlive)
                return ExecutePop(data); // 모든 알 제거 → 박스 파괴

            // 알 1개가 죽었으면 그 색 셀이 조준 대상에서 빠질 수 있음 → 타겟팅 재빌드.
            if (eggDied)
                DirectionalTargeting.InvalidateCache();

            // 알 1개 제거됐지만 박스는 유지 — 다트는 hit 인정(소진).
            return new PopResult { success = false, hitAccepted = true, reason = "TargetBox: egg removed", balloonId = data.balloonId, color = dartColor, gimmickType = GimmickPinataBox };
        }

        private void UpdatePinVisual(int balloonId, bool destroyed)
        {
            if (!_balloonObjects.TryGetValue(balloonId, out GameObject hitObj) || hitObj == null)
                return;

            var gi = hitObj.GetComponent<GimmickIdentifier>();
            if (gi == null) return;

            int remaining = 0;
            if (!destroyed && GimmickProcessor.HasInstance)
                remaining = Mathf.Max(0, GimmickProcessor.Instance.GetPinRemainingSegments(balloonId));

            gi.UpdateHP(remaining);
            gi.PlayHitEffect();
            if (destroyed)
                gi.PlayEndEffect();
        }

        // [ROLLBACK_PIN_BARRICADE_MERGE]
        // Pin mechanic + Barricade visual 통합. HP 텍스트 차감(Pin 처럼) + 히트마다 body/edge 를 HP 비율로 reshape
        // (ROLLBACK_BARRICADE_HP_RESHAPE_20260608) + footprint 재빌드.
        // 롤백 시 이 메서드 + PopBalloonWithDart Barricade 분기의 호출 제거.
        private void UpdateBarricadeVisualAfterHit(BalloonData data, bool destroyed)
        {
            if (!_balloonObjects.TryGetValue(data.balloonId, out GameObject hitObj) || hitObj == null)
                return;

            var gi = hitObj.GetComponent<GimmickIdentifier>();
            if (gi == null) return;

            int remaining = 0;
            if (!destroyed && GimmickProcessor.HasInstance)
                remaining = Mathf.Max(0, GimmickProcessor.Instance.GetPinRemainingSegments(data.balloonId));

            // ROLLBACK_BARRICADE_HP_RESHAPE_20260608:
            // 이전엔 spawn 시점 비주얼만 유지(HP 텍스트만 차감) → 이제 히트마다 body/edge 를 HP 비율로 reshape.
            // _pinSegments(=remaining) 가 진짜 HP. ApplyBarricadeVisualTransform 은 hitCount/maxHP 로 비율 계산하므로
            // hitCount 를 (maxHP - remaining) 으로 동기화한 뒤 재배치. maxHP==등록 hp 라 분모 일치.
            data.hitCount = Mathf.Clamp(data.maxHP - remaining, 0, data.maxHP);
            // ROLLBACK_BARRICADE_LENGTH_SEGMENTS_20260623:
            // Persist the reshaped hitCount before invalidating targeting. Without this,
            // DirectionalTargeting reads the old BalloonData and keeps attacking the stale length.
            _balloons[data.balloonId] = data;
            ApplyBarricadeVisualTransform(hitObj, data, animate: true); // 히트 시 길이 줄어듦 트윈(끊김 제거)
            // footprint(DirectionalTargeting) 도 hitCount 기반 → 재빌드해 막는/조준 범위 축소 반영.
            DirectionalTargeting.InvalidateCache();

            gi.UpdateHP(remaining);
            gi.PlayHitEffect();
            if (destroyed)
                gi.PlayEndEffect();
        }

        /// <summary>
        /// Force-pops a balloon by ID (used by GimmickProcessor for indirect removal like Ice).
        /// Bypasses gimmick guards.
        /// </summary>
        public void ForcePopBalloon(int balloonId)
        {
            if (!_balloons.TryGetValue(balloonId, out BalloonData data)) return;
            if (data.isPopped) return;
            if (data.gimmickType == GimmickLockKey) return;
            if (data.gimmickType == GimmickFlexTube) return; // FlexTube 는 같은 색 다트로만 제거 가능 — indirect/force-pop 차단.
            ExecutePop(data);
        }

        /// <summary>
        /// FlexTube 의 비활성된 Segment cell 을 다트 target 후보에서 제외 — _balloons.isPopped=true 마킹 + _balloonObjects 에서 분리.
        /// Pop 점수/이벤트 발화 없이 silent 제거 (FlexTube 는 RemainingCount 영향 제외).
        /// </summary>
        public void MarkFlexTubeSingleCellInactive(int balloonId)
        {
            // ROLLBACK_FLEXTUBE_CELL_TARGET_HIT_20260628:
            // Regular dart hits consume the exact logical cell selected by DirectionalTargeting.
            // Do not remove the whole same-seq 2x2 footprint here; sibling cells may already have
            // darts flying toward them and must remain valid until they are individually hit.
            if (!_balloons.TryGetValue(balloonId, out BalloonData data)) return;
            if (data == null || data.gimmickType != GimmickFlexTube || data.isPopped) return;
            MarkFlexTubeCellInactiveInternal(balloonId, data);
            DirectionalTargeting.InvalidateCache();
            if (BoardStateManager.HasInstance)
                BoardStateManager.Instance.InvalidateOutermostCache();
        }

        public bool HasLiveFlexTubeGroupCells(int groupId)
        {
            foreach (var kv in _balloons)
            {
                BalloonData data = kv.Value;
                if (data == null || data.isPopped || data.gimmickType != GimmickFlexTube) continue;
                if (data.flexTubeGroupId == groupId) return true;
            }
            return false;
        }

        public void MarkFlexTubeCellInactive(int balloonId)
        {
            if (!_balloons.TryGetValue(balloonId, out BalloonData data)) return;
            if (data.gimmickType != GimmickFlexTube) return;
            if (data.isPopped) return;

            // ROLLBACK_FLEXTUBE_SEQ_FOOTPRINT_INACTIVE_20260626:
            // A visible FlexTube part can represent a 2-cell-thick footprint, and a corner can
            // occupy a 2x2 footprint. When one logical seq is depleted, every cell in the same
            // group+seq must leave the targeting cache together. Otherwise row-B/corner cells stay
            // attackable and the exposed edge does not shrink with HP.
            int groupId = data.flexTubeGroupId;
            int seq = data.flexTubeSequenceIndex;
            var idsToDeactivate = new List<int>();
            foreach (var kv in _balloons)
            {
                BalloonData candidate = kv.Value;
                if (candidate.gimmickType != GimmickFlexTube || candidate.isPopped) continue;
                if (candidate.flexTubeGroupId == groupId && candidate.flexTubeSequenceIndex == seq)
                    idsToDeactivate.Add(kv.Key);
            }

            if (idsToDeactivate.Count > 0)
            {
                for (int i = 0; i < idsToDeactivate.Count; i++)
                {
                    int id = idsToDeactivate[i];
                    if (!_balloons.TryGetValue(id, out BalloonData cellData)) continue;
                    if (cellData.gimmickType != GimmickFlexTube || cellData.isPopped) continue;
                    MarkFlexTubeCellInactiveInternal(id, cellData);
                }

                DirectionalTargeting.InvalidateCache();
                if (BoardStateManager.HasInstance)
                    BoardStateManager.Instance.InvalidateOutermostCache();
                return;
            }

            data.isPopped = true;
            _balloons[balloonId] = data;
            // _balloonObjects 는 유지 — PopBalloonWithDart FlexTube 분기가 stale target 일 때도 IDartHittable 위임 가능하게.
            // (단 FlexTube.OnDartHit 가 _destroying / 활성 segment 유무 자체 판단.)
            _frameCachedPositions.Remove(balloonId);
            // _positionIndex 는 Vector3Int → balloonId 매핑이라 reverse lookup 필요 — 해당 entry 제거.
            Vector3Int? keyToRemove = null;
            foreach (var kv in _positionIndex)
            {
                if (kv.Value == balloonId) { keyToRemove = kv.Key; break; }
            }
            if (keyToRemove.HasValue) _positionIndex.Remove(keyToRemove.Value);

            // [2026-06-11 fix] silent 제거는 ExecutePop 을 안 타서 타게팅 contour 와 실패판정 외곽 캐시가
            // 다음 실제 팝까지 stale — 튜브 셀 제거로 노출된 풍선을 못 쏘는 '놓침' + fail 오판 위험.
            // ExecutePop 과 동일하게 양쪽 캐시를 직접 무효화한다.
            DirectionalTargeting.InvalidateCache();
            if (BoardStateManager.HasInstance)
                BoardStateManager.Instance.InvalidateOutermostCache();
            // 같은 이유로 DartManager 의 head 스캔 수락 캐시도 라인 단위 재개방 — 팝 경로의
            // InvalidateDartScanLinesForPoppedBalloon 대응. 누락 시 같은 라인의 head 가
            // 새로 노출된 타겟을 영영 재스캔하지 않음 (홀더 선택 등 전체 무효화 전까지 공격 정지).
            if (DartManager.HasInstance)
                DartManager.Instance.NotifySilentCellRemoved(GetAdjustedBoardPosition(data.position));
        }

        private void MarkFlexTubeCellInactiveInternal(int balloonId, BalloonData data)
        {
            if (data == null || data.isPopped) return;

            data.isPopped = true;
            _balloons[balloonId] = data;
            _frameCachedPositions.Remove(balloonId);

            Vector3Int? keyToRemove = null;
            foreach (var kv in _positionIndex)
            {
                if (kv.Value == balloonId) { keyToRemove = kv.Key; break; }
            }
            if (keyToRemove.HasValue) _positionIndex.Remove(keyToRemove.Value);

            if (DartManager.HasInstance)
                DartManager.Instance.NotifySilentCellRemoved(GetAdjustedBoardPosition(data.position));
        }

        /// <summary>
        /// Reveals a specific hidden balloon by removing its concealed state.
        /// Used by Hand booster. Returns true if a balloon was revealed.
        /// </summary>
        public bool RevealHiddenBalloon(int balloonId)
        {
            if (!_hiddenBalloons.Contains(balloonId)) return false;
            if (!_balloons.TryGetValue(balloonId, out BalloonData data)) return false;
            if (data.isPopped) return false;

            _hiddenBalloons.Remove(balloonId);

            if (_balloonObjects.TryGetValue(balloonId, out GameObject obj) && obj != null)
            {
                int colorIdx = Mathf.Clamp(data.color, 0, BalloonColors.Length - 1);
                ApplyTintToObject(obj, BalloonColors[colorIdx]);
                // [Leak fix 2026-05-11] SetLink(KillOnDisable) — pool 반환 시 SetActive(false) 에서 tween 자동 kill.
                // 원본: obj.transform.DOPunchScale(Vector3.one * 0.15f, 0.2f, 8, 0.5f);
                PlayRevealEffect(obj, colorIdx, balloonId);
            }

            // ROLLBACK_REVEAL_TARGET_CACHE_INVALIDATION:
            // A concealed Surprise/Hidden balloon is excluded from targeting. Revealing it must
            // rebuild the contour cache immediately, otherwise it can stay invisible to darts.
            DirectionalTargeting.InvalidateCache();
            RefreshOutermostRendererState();
            // ROLLBACK_REVEAL_BSM_OUTERMOST_INVALIDATE_20260622: pop 으로 트리거된 reveal 은 BoardStateManager 도 같은
            //   OnBalloonPopped 로 outermost 캐시를 dirty 하지만, Hand 부스터 등 'pop 없는' 직접 reveal 경로에선 BSM
            //   캐시가 stale 로 남아 fail/매칭 판정이 어긋난다(관통 아님 — 놓침/오판). 명시적으로 무효화해 보강.
            if (BoardStateManager.HasInstance)
                BoardStateManager.Instance.InvalidateOutermostCache();

            EventBus.Publish(new OnGimmickTriggered
            {
                gimmickType = GimmickSurprise, // 필드 풍선 색상 공개 = Surprise 기믹
                targetId    = balloonId
            });

            return true;
        }

        public bool IsBalloonConcealed(int balloonId)
        {
            return _hiddenBalloons.Contains(balloonId);
        }

        /// <summary>
        /// Returns one random hidden (Surprise) balloon ID, or -1 if none.
        /// Used by Hand booster to pick a target.
        /// </summary>
        public int GetRandomHiddenBalloonId()
        {
            if (_hiddenBalloons.Count == 0) return -1;

            // Pick a random one from the set
            int idx = Random.Range(0, _hiddenBalloons.Count);
            int i = 0;
            foreach (int id in _hiddenBalloons)
            {
                if (i == idx) return id;
                i++;
            }
            return -1;
        }

        /// <summary>
        /// Public accessor for adjacent balloon IDs (used by GimmickProcessor).
        /// </summary>
        public List<int> GetAdjacentBalloonIdsPublic(Vector3 position)
        {
            return GetAdjacentBalloonIds(position);
        }

        public List<int> GetAdjacentBalloonIdsForBalloonPublic(int balloonId, Vector3 fallbackPosition)
        {
            return GetAdjacentBalloonIdsForBalloon(balloonId, fallbackPosition);
        }

        /// <summary>
        /// Returns all non-popped balloons matching the given color index.
        /// Unlike GetBalloonsByColor, this returns a List and includes hidden balloons.
        /// Used by DirectionalTargeting and other gameplay systems.
        /// </summary>
        public List<BalloonData> GetActiveBalloonsByColor(int color)
        {
            List<BalloonData> result = new List<BalloonData>();
            foreach (KeyValuePair<int, BalloonData> pair in _balloons)
            {
                BalloonData data = pair.Value;
                if (data.isPopped) continue;
                if (data.gimmickType == GimmickLockKey) continue;
                if (data.color == color)
                {
                    result.Add(data);
                }
            }
            return result;
        }

        /// <summary>
        /// Returns all non-popped balloons regardless of color.
        /// Used by DirectionalTargeting to build the spatial grid for path calculations.
        /// </summary>
        public List<BalloonData> GetAllActiveBalloons()
        {
            List<BalloonData> result = new List<BalloonData>();
            foreach (KeyValuePair<int, BalloonData> pair in _balloons)
            {
                BalloonData data = pair.Value;
                if (!data.isPopped)
                {
                    result.Add(data);
                }
            }
            return result;
        }

        /// <summary>
        /// Clears all balloons and returns them to the pool.
        /// Called at level end or restart.
        /// </summary>
        public void ClearAllBalloons()
        {
            // [Leak fix 2026-05-11] 진행 중 coroutine 정리 — FlyKeyToLock / PopEffectPool.ReturnAfterDelay (runner=this) 등 stale 방지.
            // 누락 시 SetActive(false) 풍선의 transform.position 을 코루틴이 계속 갱신하거나, pool 반환 timing 어긋남.
            StopAllCoroutines();

            foreach (KeyValuePair<int, GameObject> pair in _balloonObjects)
            {
                if (pair.Value != null && ObjectPoolManager.HasInstance)
                {
                    // [Leak fix 2026-05-11] pool 반환 전 DOTween tween 정리.
                    // 진행 중인 Sequence (DOScale/DOPunchScale 등) 가 SetActive(false) 된 GameObject 의 transform 을
                    // 계속 update 하면서 DOTween internal active list 누적 → 매 frame DOTween.Update 비용 증가.
                    pair.Value.transform.DOKill();
                    // FlexTube 부품은 pool 외 (Instantiate 직접 생성) — 부모 FlexTube 와 함께 아래 _flexTubeRoots 루프에서 destroy.
                    if (_balloons.TryGetValue(pair.Key, out BalloonData bdFt) && bdFt.gimmickType == GimmickFlexTube)
                        continue;
                    // 기믹별 전용 풀로 반환 (없으면 기본 Balloon 풀)
                    string returnKey = PoolKey;
                    if (_balloons.TryGetValue(pair.Key, out BalloonData bd))
                    {
                        returnKey = ResolveGimmickPoolKey(bd.gimmickType);
                        if (bd.gimmickType == GimmickLockKey) returnKey = "Key";
                    }
                    ObjectPoolManager.Instance.Return(returnKey, pair.Value);
                }
            }

            // FlexTube 부모 일괄 destroy — 자식 FlexTubePart 들도 같이 정리됨.
            for (int i = 0; i < _flexTubeRoots.Count; i++)
            {
                if (_flexTubeRoots[i] != null) Destroy(_flexTubeRoots[i]);
            }
            _flexTubeRoots.Clear();

            // FrozenLayer 오버레이 자식들도 모두 풀로 반환
            ReturnAllFrozenOverlays();

            // [SHADOW_BATCH] 통합 그림자 mesh 정리 — 다음 레벨 RebuildShadowBatch 가 재구성.
            _shadowBatcher?.Clear();

            _balloons.Clear();
            _balloonObjects.Clear();
            _hiddenBalloons.Clear();
            _pinataGroup.Clear();
            _positionIndex.Clear();
            _multiCellOccupancy.Clear();
            _activeKeyPairIds.Clear();
            // [Leak fix 2026-05-11] _balloonRenderers / _prevOutermostSet / _frameCachedPositions 정리 추가.
            // 이전: ClearAllBalloons 가 _nextBalloonId=1 로 reset 하는데 이 dict 들이 stale entry 보유 →
            //       다음 레벨의 balloonId=1 풍선이 stale Renderer[] / outline state / cached position 과 collision.
            _balloonRenderers.Clear();
            _prevOutermostSet.Clear();
            _outlinedBalloonIds.Clear(); // ROLLBACK_OUTLINE_MATERIAL_SWAP_20260609: outline swap 상태 정리(stale 방지)
            _hasAppliedOutermostOutline = false;
            _frameCachedPositions.Clear();
            _frameCachedPositionsFrame = -1;
            RemainingCount = 0;
            PoppedCount = 0;
            _nextBalloonId = 1;

            // ROLLBACK_GIMMICK_LEVEL_SAFE_RESET:
            // Do not let the previous level's safe scale/shift affect the next level's first spawn.
            _levelSafeWm = 1f;
            _levelSafeHm = 1f;
            _levelSafeZShift = 0f;
            _levelSafeCalculated = false;
        }

        #endregion

        #region Private Methods — Setup

        private bool TrySpawnSizedIceAsCellBrush(BalloonSetupData entry)
        {
            if (entry == null) return false;

            string normalized = GimmickDisplayName.Normalize(entry.gimmickType);
            int width = Mathf.Max(1, entry.sizeW);
            int height = Mathf.Max(1, entry.sizeH);
            if (normalized != GimmickIce || (width <= 1 && height <= 1))
                return false;

            // ROLLBACK_ICE_OVERLAY_UNDER_BALLOONS_20260626:
            // Ice looks like one object, but the balloons under it must remain real cells:
            // 2x2 = 4 balloons, 3x3 = 9 balloons. Visual merging happens later by region overlay.
            float spacing = _cellSpacing > 0.0001f ? _cellSpacing : 0.55f;
            for (int dx = 0; dx < width; dx++)
            {
                for (int dz = 0; dz < height; dz++)
                {
                    SpawnBalloonFromSetup(CreateIceCellSetup(entry, normalized, dx, dz, spacing));
                }
            }

            return true;
        }

        private BalloonSetupData CreateIceCellSetup(BalloonSetupData source, string normalizedGimmick, int dx, int dz, float spacing)
        {
            return new BalloonSetupData
            {
                color = source.color,
                position = new Vector3(
                    source.position.x + dx * spacing,
                    source.position.y,
                    source.position.z + dz * spacing),
                gimmickType = normalizedGimmick,
                groupId = source.groupId,
                sizeW = 1,
                sizeH = 1,
                hp = source.hp,
                lockPairId = source.lockPairId,
                iceBlockSize = Mathf.Max(1, source.iceBlockSize),
                iceGroupId = source.iceGroupId,
                iceGroupHp = source.iceGroupHp,
                iceGroupHpMode = source.iceGroupHpMode,
                barricadeDir = source.barricadeDir,
                barricadeLength = source.barricadeLength,
                eggColors = source.eggColors,
                eggHps = source.eggHps,
                flexTubeGroupId = source.flexTubeGroupId,
                flexTubePartType = source.flexTubePartType,
                flexTubeSequenceIndex = source.flexTubeSequenceIndex,
                flexTubeHp = source.flexTubeHp
            };
        }

        private void SpawnBalloonFromSetup(BalloonSetupData entry)
        {
            int id = _nextBalloonId++;
            entry.gimmickType = GimmickDisplayName.Normalize(entry.gimmickType);

            // [ROLLBACK_LOCKKEY_DEPRECATE]
            // Lock_Key 기믹 dead 처리 — 기존 LevelData 호환을 위해 정규화: Lock_Key → none (일반 풍선).
            // 새 레벨에서 사용 안 함. 롤백 시 이 if 블록 제거.
            if (entry.gimmickType == GimmickLockKey)
            {
                entry.gimmickType = GimmickNone;
                entry.lockPairId = -1;
            }

            // [ROLLBACK_PIN_BARRICADE_MERGE]
            // Pin → Barricade 통합 (옵션 A): Pin mechanic (같은 색 다트만 점진 제거) + Barricade 시각.
            // SpawnBalloonFromSetup 진입 시 Pin gimmickType 을 Barricade 로 마이그. 기존 LevelData 호환.
            // 통합 후: Barricade visual prefab + 색 매칭 + segment 점진 제거 동작.
            // 롤백 시: 이 if 블록 제거 + GimmickProcessor.RegisterBalloonGimmick Barricade 케이스 원복 + PopBalloonWithDart Barricade 분기 원복.
            if (entry.gimmickType == GimmickPin)
            {
                entry.gimmickType = GimmickBarricade;
            }

            int resolvedHP = entry.hp > 0 ? entry.hp : PinataRequiredHits;
            BalloonData data = new BalloonData
            {
                balloonId   = id,
                color       = entry.color,
                position    = entry.position,
                isPopped    = false,
                gimmickType = string.IsNullOrEmpty(entry.gimmickType) ? GimmickNone : entry.gimmickType,
                hitCount    = 0,
                maxHP       = resolvedHP,
                sizeW       = entry.sizeW > 0 ? entry.sizeW : 1,
                sizeH       = entry.sizeH > 0 ? entry.sizeH : 1,
                iceBlockSize = entry.iceBlockSize > 0 ? entry.iceBlockSize : 1,
                iceGroupId = entry.iceGroupId,
                iceGroupHp = entry.iceGroupHp,
                iceGroupHpMode = entry.iceGroupHpMode,
                barricadeDir    = entry.barricadeDir,
                barricadeLength = entry.barricadeLength > 0 ? entry.barricadeLength : 1,
                // 알 배열: eggHps 는 런타임 차감되므로 clone (레벨 에셋 원본 보호). eggColors 는 read-only 라 공유.
                eggColors   = entry.eggColors,
                eggHps      = entry.eggHps != null ? (int[])entry.eggHps.Clone() : null,
                lockPairId  = entry.lockPairId,
                flexTubeGroupId       = entry.flexTubeGroupId,
                flexTubeSequenceIndex = entry.flexTubeSequenceIndex,
                flexTubePartType      = entry.flexTubePartType,
                flexTubeHp            = entry.flexTubeHp
            };

            // ROLLBACK_PINATA_PER_CELL_20260618: per-cell Pinata 는 maxHP 를 점유 셀수(W×H)로 강제한다.
            //   → requiredHits(ProcessPinataHit) · GimmickIdentifier HP 비율 · UpdateHP 표시가 전부 셀수 기준 일치.
            //   레벨 데이터의 entry.hp 는 sized Pinata 에서 무시됨(칸당 1공격 = 셀수). 롤백: 이 if 제거.
            // ROLLBACK_BARRICADE_LENGTH_SEGMENTS_20260623:
            // Barricade length is the number of attackable cells/segments. Keep the shared Pin
            // segment counter, visual footprint, and targeting footprint on that same value.
            // ROLLBACK_WOODENBOARD_PER_CELL_HP_20260623:
            // Sized Wooden Board is attacked by exposed occupied cells. A 2x2 board therefore
            // needs four cell hits total, with two cells immediately attackable when an outer
            // side exposes two rows/columns.
            if (IsPinataPerCell(data))
                data.maxHP = Mathf.Max(1, data.sizeW * data.sizeH);
            // ROLLBACK_BARRICADE_HP_INDEPENDENT_20260624: HP independent of length (design request).
            // maxHP now stays = authored HP (resolvedHP). GetBarricadeActiveLength shrinks the visible/
            // attackable length as length × remainingHP/maxHP, so a length-N / HP-K barricade drops N/K
            // cells per hit. (Was: forced maxHP = barricadeLength → always 1 cell/hit.) If a barricade has
            // no authored HP it falls back to PinataRequiredHits(2) via resolvedHP — set HP in MapMaker.

            // ROLLBACK_PINATABOX_EGG_CLAMP_20260616: eggColors 가 footprint 셀 수(sizeW*sizeH) 를 초과하면
            //   초과 알은 타게팅 셀에 매핑 안 됨(eggIdx=(dz*W+dx)%eggN 의 인덱스 범위가 0..W*H-1) → 영원히 타격 불가
            //   → 박스가 영영 안 터져 언위너블. 알 배열을 footprint 셀 수로 클램프. 정상(eggN ≤ W*H)은 불변.
            //   롤백: 아래 if 블록 제거.
            if (data.gimmickType == GimmickPinataBox && data.eggColors != null)
            {
                // ROLLBACK_TARGETBOX_AUTHORED_EGG_COUNT_20260623:
                // Keep every egg authored in MapMaker. The footprint exposes live egg colors
                // through DirectionalTargeting, so the egg list must not clamp to sizeW*sizeH.
                int maxEggs = data.eggColors.Length;
                if (data.eggColors.Length > maxEggs)
                {
                    Debug.LogWarning($"[BalloonController] Pinata_Box eggColors({data.eggColors.Length}) > footprint({data.sizeW}x{data.sizeH}={maxEggs}) — 언위너블 방지 클램프. 레벨 데이터 검토 권장.");
                    int[] clampedColors = new int[maxEggs];
                    System.Array.Copy(data.eggColors, clampedColors, maxEggs);
                    data.eggColors = clampedColors;
                    if (data.eggHps != null)
                    {
                        int[] clampedHps = new int[maxEggs];
                        System.Array.Copy(data.eggHps, clampedHps, Mathf.Min(maxEggs, data.eggHps.Length));
                        data.eggHps = clampedHps;
                    }
                }
                if (data.eggHps == null || data.eggHps.Length < data.eggColors.Length)
                {
                    int[] normalizedHps = new int[data.eggColors.Length];
                    for (int i = 0; i < normalizedHps.Length; i++)
                    {
                        int hp = data.eggHps != null && i < data.eggHps.Length ? data.eggHps[i] : 1;
                        normalizedHps[i] = Mathf.Max(1, hp);
                    }
                    data.eggHps = normalizedHps;
                }
            }

            _balloons[id] = data;

            // FlexTube cell — visual 은 BuildFlexTubes 가 spawn. 일반 풍선 풀에서 visual 가져오지 않음.
            // GimmickProcessor 등록도 skip (CheckDartBlocker 가 BalloonData 만으로 색 매칭).
            if (data.gimmickType == GimmickFlexTube)
            {
                return;
            }

            // Lock_Key: 풍선 대신 Key 프리팹을 셀에 독립 배치 (풀링)
            if (data.gimmickType == GimmickLockKey)
            {
                if (ObjectPoolManager.HasInstance)
                {
                    GameObject keyObj = ObjectPoolManager.Instance.Get("Key", entry.position, Quaternion.Euler(90f, 0f, 0f));
                    if (keyObj != null)
                    {
                        keyObj.SetActive(true);
                        keyObj.transform.localScale = Vector3.one * _balloonScale;
                        _balloonObjects[id] = keyObj;
                    }
                }
                _activeKeyPairIds[id] = data.lockPairId;
                Debug.Log($"[Key SETUP] Key {id} registered: pairId={data.lockPairId}, gimmick={data.gimmickType}");
            }
            else
            {
                // 일반 풍선/기믹 — 풀에서 오브젝트 생성
                GameObject obj = GetOrCreateBalloonObject(id, entry.position, entry.color);
                if (obj != null)
                {
                    _balloonObjects[id] = obj;

                    // Override visuals for special gimmick types
                    if (data.gimmickType == GimmickWall)
                    {
                        // ROLLBACK_WALL_IRONBOX_PREFAB:
                        // Wall/IronWall uses the IronBox visual; it is still indestructible.
                        // ROLLBACK_IRONWALL_KEEP_IRONBOX_MATERIAL_20260626:
                        // Keep the authored IronBox.prefab material (Assets/3.Material/IronBox.mat).
                        // The old path called ApplyTintToObject/gi.ApplyColor(WALL_COLOR), which
                        // replaced Renderer.sharedMaterial with a generated grey material.
                        // Restore those calls if Iron Wall should return to flat runtime tinting.
                        var gi = obj.GetComponent<GimmickIdentifier>();
                        if (gi != null)
                        {
                            gi.Initialize();
                        }

                        // multi-cell Wall(2×2/3×3) 만 footprint 에 맞춰 시각 스케일/중앙정렬.
                        // 1×1 은 기존 기본 배치를 유지해 기존 레벨 Wall 외형 회귀 방지.
                        if (data.sizeW > 1 || data.sizeH > 1)
                            ApplySizedFieldVisualTransform(obj, data);
                    }
                    else if (data.gimmickType == GimmickPin)
                    {
                        // WoodenBoard 프리팹 사용 — Pin은 같은 색 다트로 점진 제거
                        ApplyTintToObject(obj, PIN_COLOR);
                        var gi = obj.GetComponent<GimmickIdentifier>();
                        if (gi != null)
                        {
                            gi.Initialize();
                            int ci = Mathf.Clamp(data.color, 0, BalloonColors.Length - 1);
                            gi.UpdateHP(data.maxHP > 0 ? data.maxHP : 3);
                            if (gi.HasColorRenderers)
                                gi.ApplyColor(BalloonColors[ci]);
                        }
                    }
                    else if (data.gimmickType == GimmickBarricade)
                    {
                        // [ROLLBACK_PIN_BARRICADE_MERGE]
                        // Pin → Barricade 통합 (옵션 A): Pin mechanic + Barricade visual.
                        // 색 적용을 WALL_COLOR(grey) 대신 BalloonColors[data.color] 로 변경 — Pin 처럼 색 매칭.
                        // 롤백 시 ApplyColor(WALL_COLOR) 로 원복.
                        var gi = obj.GetComponent<GimmickIdentifier>();
                        if (gi != null)
                        {
                            gi.Initialize();
                            int hp = data.maxHP - data.hitCount;
                            gi.UpdateHP(Mathf.Max(1, hp));
                            int ci = Mathf.Clamp(data.color, 0, BalloonColors.Length - 1);
                            if (gi.HasColorRenderers)
                                gi.ApplyColor(BalloonColors[ci]);
                        }

                        ApplyBarricadeVisualTransform(obj, data);
                    }
                    else if (data.gimmickType == GimmickPinataBox)
                    {
                        // Target Box 알 모델: eggColors + PinataBoxView 있으면 W×H 알 격자 생성(셀별 색).
                        var view = obj.GetComponentInChildren<PinataBoxView>(true);
                        if (view != null)
                        {
                            ApplyPinataBoxVisual(obj, data, view);
                        }
                        else
                        {
                            // 폴백(레거시/뷰 미부착): 단일 박스 — 기존 색+스케일.
                            var gi = obj.GetComponent<GimmickIdentifier>();
                            if (gi != null)
                            {
                                gi.Initialize();
                                int hp = data.maxHP - data.hitCount;
                                gi.UpdateHP(Mathf.Max(1, hp));
                                int ci = Mathf.Clamp(data.color, 0, BalloonColors.Length - 1);
                                if (gi.HasColorRenderers) gi.ApplyColor(BalloonColors[ci]);
                            }
                            ApplySizedFieldVisualTransform(obj, data);
                        }
                    }
                    else if (data.gimmickType == GimmickPinata)
                    {
                        var gi = obj.GetComponent<GimmickIdentifier>();
                        if (gi != null)
                        {
                            gi.Initialize();
                            int hp = data.maxHP - data.hitCount;
                            gi.UpdateHP(Mathf.Max(1, hp));
                            int ci = Mathf.Clamp(data.color, 0, BalloonColors.Length - 1);
                            if (gi.HasColorRenderers)
                                gi.ApplyColor(BalloonColors[ci]);
                        }

                        ApplySizedFieldVisualTransform(obj, data);
                    }
                }
            }

            // Register Pinata group membership
            if (data.gimmickType == GimmickPinata && entry.groupId >= 0)
            {
                if (!_pinataGroup.TryGetValue(entry.groupId, out List<int> group))
                {
                    group = new List<int>();
                    _pinataGroup[entry.groupId] = group;
                }
                group.Add(id);
            }

            // Surprise(Lv.101) + Hidden(필드 풍선 기믹) 모두 색상 은닉 → BalloonHidden.mat 적용
            // Hidden을 큐 기믹으로 쓰는 보관함은 별개로 HolderManager에서 처리
            if (data.gimmickType == GimmickSurprise || data.gimmickType == GimmickHidden)
            {
                _hiddenBalloons.Add(id);
            }

            if (GimmickProcessor.HasInstance)
            {
                GimmickProcessor.Instance.RegisterBalloonGimmick(
                    data.balloonId,
                    data.gimmickType,
                    data.color,
                    data.maxHP,
                    data.iceGroupId,
                    data.iceGroupHp,
                    data.iceGroupHpMode);
            }
        }

        /// <summary>
        /// FlexTube 그룹별 prefab 인스턴스화 — SetupBalloons 의 SpawnBalloonFromSetup 일괄 완료 직후 호출.
        /// 같은 flexTubeGroupId 의 BalloonData 들을 sequenceIndex 순으로 정렬 → FlexTube 부모 + StartCap/Segment/EndCap 자식 배치.
        /// 각 자식 GameObject 는 _balloonObjects[balloonId] 에 등록되어 다트 hit 시 IDartHittable(FlexTubePart) 진입점이 됨.
        /// HP = Segment 수 (Cap 제외), color = 첫 cell 색상.
        /// </summary>
        private void BuildFlexTubes(List<BalloonSetupData> layout)
        {
            // 그룹별 balloonId 수집 + sequenceIndex 정렬
            var groups = new Dictionary<int, List<int>>();
            foreach (var kv in _balloons)
            {
                var d = kv.Value;
                if (d.gimmickType != GimmickFlexTube) continue;
                if (d.flexTubeGroupId < 0) continue;
                if (!groups.TryGetValue(d.flexTubeGroupId, out var ids))
                {
                    ids = new List<int>();
                    groups[d.flexTubeGroupId] = ids;
                }
                ids.Add(d.balloonId);
            }

            if (groups.Count == 0) return;

            // prefab 일괄 로드 (Resources 캐시)
            GameObject flexTubePrefab = Resources.Load<GameObject>("Prefabs/FlexTube");
            GameObject startCapPrefab = Resources.Load<GameObject>("Prefabs/FlexTube_StartCap");
            GameObject segmentPrefab  = Resources.Load<GameObject>("Prefabs/FlexTube_Segment");
            GameObject endCapPrefab   = Resources.Load<GameObject>("Prefabs/FlexTube_EndCap");
            if (flexTubePrefab == null || startCapPrefab == null || segmentPrefab == null || endCapPrefab == null)
            {
                Debug.LogWarning("[BalloonController] FlexTube prefab(s) missing in Resources/Prefabs/. FlexTube spawn skipped.");
                return;
            }

            // ROLLBACK_FLEXTUBE_MEASURE_RIB_LENGTH_20260624:
            // Measure the Segment mesh's REAL length along its +z axis (at prefab scale 1). Ribs are
            // scaled UNIFORMLY (x=y=z=s), so rendered length = rawMeshZ × s. Measuring removes the
            // dependence on the stale hard-coded constant that under-sized ribs and left gaps.
            float segMeshZ = FLEXTUBE_SEGMENT_WORLD_LENGTH / Mathf.Max(0.0001f, FlexTubeSegmentVisualScale.z); // fallback ≈ raw
            if (TryGetLocalProjectedRendererLength(segmentPrefab.transform, Vector3.forward, out float measuredSegZ)
                && measuredSegZ > 0.0001f)
                segMeshZ = measuredSegZ;
            // ROLLBACK_FLEXTUBE_NONUNIFORM_PER_CELL_20260624:
            // Also measure the mesh's NATURAL DIAMETER (local x-extent). Hose_Segment is authored ~1 grid
            // cell LONG but ~2.8 cells WIDE; forcing a single uniform scale to fit the diameter shrank the
            // length to 1/3 cell → 3 squashed copies per cell whose ridge necks read as gaps. We now scale
            // length (z) and diameter (x,y) INDEPENDENTLY, so one mesh = one cell (one gentle ridge), the
            // diameter stays correct, and segments butt-join seamlessly. (Prefab renderer is on the root —
            // single GameObject — so a non-uniform localScale under the rib's LookRotation does NOT shear.)
            float segMeshX = 1f;
            if (TryGetLocalProjectedRendererLength(segmentPrefab.transform, Vector3.right, out float measuredSegX)
                && measuredSegX > 0.0001f)
                segMeshX = measuredSegX;
            // Cap mesh lengths (along +z) — StartCap/EndCap are DIFFERENT meshes with different natural z
            // lengths, so a single uniform scale leaves them looking longer/shorter than the segments. We
            // match each cap's rendered length to the segment pitch (below), and use these to bridge the
            // segment tiling right up to each cap (no gap).
            float startCapMeshZ = segMeshZ, endCapMeshZ = segMeshZ;
            if (TryGetLocalProjectedRendererLength(startCapPrefab.transform, Vector3.forward, out float msc) && msc > 0.0001f)
                startCapMeshZ = msc;
            if (TryGetLocalProjectedRendererLength(endCapPrefab.transform, Vector3.forward, out float mec) && mec > 0.0001f)
                endCapMeshZ = mec;
            // ROLLBACK_FLEXTUBE_CAP_2X_SIZE_20260626:
            // Cap meshes can have a different authored width than the rib segment. Measuring cap width
            // separately prevents a 2x2 Head from rendering like a 1x1 cap.
            float startCapMeshX = segMeshX, endCapMeshX = segMeshX;
            if (TryGetLocalProjectedRendererLength(startCapPrefab.transform, Vector3.right, out float mscX) && mscX > 0.0001f)
                startCapMeshX = mscX;
            if (TryGetLocalProjectedRendererLength(endCapPrefab.transform, Vector3.right, out float mecX) && mecX > 0.0001f)
                endCapMeshX = mecX;
            // [FlexTube-DIAG] mesh shape — renderer count + local bounds reveal whether one Segment is
            // a single ring or a multi-ring/graduated section (would explain interleaved big/small).
            var segRenderers = segmentPrefab.GetComponentsInChildren<Renderer>(true);
            string segBoundsStr = segRenderers.Length > 0 && segRenderers[0] != null
                ? segRenderers[0].localBounds.size.ToString("F3") : "-";
            string segType = segRenderers.Length > 0 && segRenderers[0] != null
                ? segRenderers[0].GetType().Name : "-";
            Debug.Log($"[FlexTube] segMeshZ(raw)={segMeshZ:F3} measured={measuredSegZ:F3} renderers={segRenderers.Length} type={segType} firstBounds={segBoundsStr}");

            foreach (var kv in groups)
            {
                int groupId = kv.Key;
                var ids = kv.Value;
                ids.Sort((a, b) => _balloons[a].flexTubeSequenceIndex.CompareTo(_balloons[b].flexTubeSequenceIndex));

                // 디버그: 그룹별 cells 정보. spawn 누락 추적용.
                var sb = new System.Text.StringBuilder();
                sb.Append($"[FlexTube] Group {groupId}: {ids.Count} cells —");
                for (int k = 0; k < ids.Count; k++)
                {
                    var d = _balloons[ids[k]];
                    sb.Append($" [{k}: id={ids[k]} seq={d.flexTubeSequenceIndex} pos=({d.position.x:F2},{d.position.z:F2})]");
                }
                Debug.Log(sb.ToString());

                if (ids.Count < 2)
                {
                    Debug.LogWarning($"[FlexTube] Group {groupId}: cells={ids.Count} (need at least 2: StartCap + EndCap). Skipping.");
                    continue;
                }

                // FlexTube 부모 생성 — Animator + FlexTube 컴포넌트는 prefab 에 부착돼 있다고 가정.
                // world origin 으로 강제 — 자식 부품의 world position 이 prefab transform offset 영향 안 받게.
                var tubeObj = Instantiate(flexTubePrefab);
                tubeObj.name = $"FlexTube_Group{groupId}";
                tubeObj.transform.position = Vector3.zero;
                tubeObj.transform.rotation = Quaternion.identity;
                tubeObj.transform.localScale = Vector3.one;
                var tube = tubeObj.GetComponent<FlexTube>() ?? tubeObj.AddComponent<FlexTube>();

                // [FlexTube fix] flexTubePrefab 에 디자인 시점 템플릿 자식(예시 StartCap/Segment/EndCap)이 남아 있으면
                // 런타임에 스폰하는 실제 캡 클론과 중복 노출됨(스크린샷의 'FlexTube > 캡들' + 'FlexTube_StartCap(Clone)').
                // tubeObj 는 컨테이너로만 쓰고(자식 참조 X, parts 리스트만 사용) 실제 파츠는 아래에서 root 에 붙이므로,
                // 인스턴스화 직후 기존 자식을 모두 제거해 템플릿 잔재를 정리한다.
                ClearFlexTubeTemplateChildren(tubeObj);

                _flexTubeRoots.Add(tubeObj);
                Quaternion extraRot = Quaternion.Euler(0f, tube.ExtraYRotation, 0f);

                // 다른 모든 필드 요소와 동일 좌표계로 정렬 — raw position 을 보드 보정(GetAdjustedBoardPosition)에 통과.
                // 누락 시 보드가 스케일/오프셋된 레벨에서 튜브가 셀 그리드를 벗어남(캡 미정렬 포함).
                // ROLLBACK_FLEXTUBE_2THICK_20260626: 2줄 두께 — 같은 flexTubeSequenceIndex 짝(row-A/row-B)을
                //   centerline 한 점으로 평균(중점). ids 는 seq 정렬이라 같은 seq 가 연속 → 묶어서 평균.
                //   (1줄 튜브는 seq 가 모두 유일 → 1:1 그대로.) 이후 rib/cap/Bezier 파이프라인은 centerline 기준이라 무변경.
                var cellPositions = new List<Vector3>(ids.Count);
                // ROLLBACK_FLEXTUBE_2THICK_INDEXFIX_20260626: averaging 으로 cellPositions(seq당 1점)가 ids(2줄=2배)보다
                //   짧아진다. 이후 모든 인덱스/캡·rib cellId 매핑은 ids 가 아니라 이 seqIds(seq당 대표 cell)·cellPositions.Count
                //   기준이어야 한다(안 그러면 ids.Count 로 돌다 cellPositions[k] 범위 초과 → ArgumentOutOfRangeException).
                var seqIds = new List<int>(ids.Count);
                // ROLLBACK_FLEXTUBE_CELL_TARGET_HIT_20260628:
                // Store all logical cell ids per seq. The visual part uses the seq center, but
                // exposed edge targeting still consumes individual cells.
                var seqCellIds = new List<List<int>>(ids.Count);
                for (int fi = 0; fi < ids.Count; )
                {
                    int sq = _balloons[ids[fi]].flexTubeSequenceIndex;
                    int repId = ids[fi]; // 이 seq 의 대표 cell(첫 셀)
                    Vector3 sum = Vector3.zero; int cnt = 0;
                    var cellsForSeq = new List<int>(4);
                    while (fi < ids.Count && _balloons[ids[fi]].flexTubeSequenceIndex == sq)
                    {
                        cellsForSeq.Add(ids[fi]);
                        sum += GetAdjustedBoardPosition(_balloons[ids[fi]].position);
                        cnt++;
                        fi++;
                    }
                    cellPositions.Add(sum / cnt);
                    seqIds.Add(repId);
                    seqCellIds.Add(cellsForSeq);
                }

                int groupColor = _balloons[ids[0]].color;
                int colorIdx = Mathf.Clamp(groupColor, 0, BalloonColors.Length - 1);

                // ROLLBACK_FLEXTUBE_NATURAL_RIB_TILING_20260624: 실제 rib 개수는 아래 루프에서 셀마다
                //   N = round(셀거리 / 자연 rib 길이) 로 계산한다(자연 크기 rib 를 겹침/틈 없이 타일 → 연속 주름관).
                //   이 값(3)은 parts List 용량 추정 힌트로만 남는다(과/소추정 무해 — List 가 알아서 grow).
                int visualSegmentsPerCell = 3;
                // ROLLBACK_FLEXTUBE_2THICK_20260626: 2줄 두께라도 centerline 은 평균된 cellPositions 기준 → ids.Count(2배) 대신
                //   cellPositions.Count 사용. rib 용량·auto-HP(셀수−2)가 자동으로 1줄 기준값 유지(난이도 불변).
                int segmentCellCount = Mathf.Max(0, cellPositions.Count - 2);

                // parts list 용량 = 2 Cap + (cells - 2) × N visual segment (rough hint)
                int partsCapacity = 2 + segmentCellCount * visualSegmentsPerCell;
                var parts = new List<FlexTubePart>(partsCapacity);

                // ROLLBACK_FLEXTUBE_NONUNIFORM_PER_CELL_20260624:
                // The Hose_Segment mesh is authored ~1 grid cell LONG (segMeshZ≈gridCell) but ~2.8 cells
                // WIDE. So: place ONE segment per grid cell (the authored density → one gentle ridge per
                // cell) and scale length (z) and diameter (x,y) SEPARATELY.
                //  • ringWorldSize = gridCell  → ~1 segment per cell (was gridCell/3 → 3 squashed copies).
                //  • length: z-scale is pitch-locked in the rib loop (rendered length == spacing) so
                //    segments butt-join seamlessly with no overlap and no gap.
                //  • diameter: x,y-scale = (gridCell × DIAMETER_FRAC) / segMeshX, INDEPENDENT of length,
                //    so the tube keeps a constant sensible thickness regardless of cell spacing.
                //  HP/targeting still map each rib to its nearest logical cell.
                GetAdjustedCellSize(out float gridCellX, out float gridCellZ);
                float ftMinX = float.MaxValue, ftMaxX = float.MinValue, ftMinZ = float.MaxValue, ftMaxZ = float.MinValue;
                foreach (var cp in cellPositions)
                {
                    if (cp.x < ftMinX) ftMinX = cp.x;
                    if (cp.x > ftMaxX) ftMaxX = cp.x;
                    if (cp.z < ftMinZ) ftMinZ = cp.z;
                    if (cp.z > ftMaxZ) ftMaxZ = cp.z;
                }
                // grid cell size along the tube's dominant axis (vertical → Z, horizontal → X)
                float tubeGridCell = (ftMaxZ - ftMinZ) >= (ftMaxX - ftMinX) ? gridCellZ : gridCellX;
                float globalCellLength = ComputeFlexTubeMedianCellLength(cellPositions); // (diag only: FlexTube sub-cell spacing)
                float ringWorldSize = tubeGridCell * FLEXTUBE_RIB_SIZE_SCALE;            // ~1 segment per grid cell
                float ringScale = ringWorldSize / Mathf.Max(0.0001f, segMeshZ);          // diag baseline only — actual z-scale is pitch-locked below
                float ribPitchTarget = ringWorldSize / FLEXTUBE_SEGMENT_SEAM_OVERLAP;    // density target → rib COUNT only
                // diameter (x,y) scale — decoupled from length so the tube thickness is constant.
                float ribDiameterScale = (tubeGridCell * FLEXTUBE_TUBE_DIAMETER_FRAC) / Mathf.Max(0.0001f, segMeshX);
                float startCapDiameterScale = (tubeGridCell * FLEXTUBE_CAP_DIAMETER_FRAC) / Mathf.Max(0.0001f, startCapMeshX);
                float endCapDiameterScale = (tubeGridCell * FLEXTUBE_CAP_DIAMETER_FRAC) / Mathf.Max(0.0001f, endCapMeshX);

                // ROLLBACK_FLEXTUBE_CONTINUOUS_PATH_TILING_20260624:
                // Root cause of the irregular big/small/big rhythm: ribs were placed in PER-CELL
                // groups of 3 (cellCenter ± step). The gap at every cell boundary (D − 2·step) only
                // equals the in-cell gap (step) when each cell's spacing D is exactly 3·step. Any
                // board-spacing variation made every 3rd boundary a different gap → ribs overlapped
                // (look big) or separated (look small) in a period-3 beat, and the last cell's ribs
                // never reached the EndCap (the gap the report shows). Fix: ignore cell boundaries
                // and tile ribs at ONE uniform pitch along the whole cap-to-cap path. Every rib is
                // identical and evenly spaced; HP/targeting still map each rib to its nearest cell.
                // ROLLBACK_FLEXTUBE_2THICK_INDEXFIX_20260626: centerline(cellPositions) 기준 — ids.Count(2배) 금지.
                int lastIdx = cellPositions.Count - 1;

                // cumulative arc length along the cell-center polyline (handles ㄴ/ㄷ/ㄹ bends)
                var cumArc = new List<float>(cellPositions.Count) { 0f };
                for (int k = 1; k < cellPositions.Count; k++)
                    cumArc.Add(cumArc[k - 1] + Vector3.Distance(cellPositions[k - 1], cellPositions[k]));
                float pathTotal = cumArc[lastIdx];
                float minCell = Mathf.Max(0.0001f, Mathf.Min(gridCellX, gridCellZ));

                // shared instantiate/parent/color path for caps and ribs.
                FlexTubePart SpawnFlexPart(GameObject prefab, Vector3 pos, Quaternion rot,
                                           bool applyScale, Vector3 scale,
                                           GimmickIdentifier.FlexTubePart pType, int cellId)
                {
                    var obj = Instantiate(prefab);
                    if (obj == null)
                    {
                        Debug.LogWarning($"[FlexTube] Instantiate failed (group {groupId}, {pType}, cell {cellId}).");
                        return null;
                    }
                    obj.transform.position = pos;
                    obj.transform.rotation = rot;
                    obj.transform.SetParent(tubeObj.transform, worldPositionStays: true);
                    if (applyScale) obj.transform.localScale = scale;

                    var p = obj.GetComponent<FlexTubePart>() ?? obj.AddComponent<FlexTubePart>();
                    if (p == null) { Destroy(obj); return null; }
                    p.SetPartType(pType);
                    p.SetBalloonId(cellId);
                    parts.Add(p);

                    var gi = obj.GetComponent<GimmickIdentifier>();
                    if (gi != null)
                    {
                        gi.Initialize();
                        if (gi.HasColorRenderers) gi.ApplyColor(BalloonColors[colorIdx]);
                    }
                    // ROLLBACK_FLEXTUBE_TINT_ALL_RENDERERS_20260608: tint every child renderer so
                    // parts not wired to GimmickIdentifier._colorRenderers don't stay black/gray.
                    ApplyTintToRenderersInChildren(obj, BalloonColors[colorIdx]);
                    // ROLLBACK_FLEXTUBE_GRID_2CELL_SCALE_20260628:
                    // GimmickIdentifier.Initialize/ApplyColor can touch child renderers after spawn.
                    // Re-apply the grid-derived scale last so every part keeps the same final world size.
                    if (applyScale) obj.transform.localScale = scale;
                    return p;
                }

                string Fmt(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";

                void LogFlexTubeSpawn(string kind, FlexTubePart part, int index, int seq, float distance, float prevGap, float targetPitch)
                {
                    if (!FLEXTUBE_SPAWN_DEBUG || part == null) return;

                    Transform t = part.transform;
                    string boundsInfo = "bounds=none";
                    if (TryMeasureVisualRendererBounds(part.transform, out Bounds b))
                        boundsInfo = $"boundsCenter={Fmt(b.center)} boundsSize={Fmt(b.size)}";

                    int idsCount = part.BalloonIds != null ? part.BalloonIds.Length : 0;
                    Debug.Log(
                        $"[FlexTube-DIAG-SPAWN] G{groupId} {kind}#{index} seq={seq} cell={part.BalloonId} ids={idsCount} " +
                        $"dist={distance:F3} gapPrev={prevGap:F3} pitch={targetPitch:F3} " +
                        $"pos={Fmt(t.position)} rot={Fmt(t.eulerAngles)} scale={Fmt(t.localScale)} {boundsInfo}");

                    Color rayColor = kind == "START" ? Color.green : kind == "EDGE" ? Color.red : Color.cyan;
                    Debug.DrawRay(t.position, t.forward * Mathf.Max(0.1f, minCell * 0.6f), rayColor, 20f);
                }

                // ROLLBACK_FLEXTUBE_SEQ_FOOTPRINT_VISUAL_20260628:
                // FlexTube visual parts must sit on the actual cells authored in MapMaker:
                // seq0 = Head(Start), seqN = Edge(End), middle seqs = body/corner Segments.
                // Previous rib/path tiling placed many small Segment clones on a centerline, so
                // Head/Edge could be buried, part sizes drifted, and 2x2 authored footprints read
                // as thin disconnected ribs. This block scales each prefab to the seq footprint
                // bounds and recenters its visible renderer bounds on that footprint center.
                Vector3 GetSeqTangent(int seqIndex)
                {
                    Vector3 dir;
                    if (seqIndex <= 0)
                        dir = cellPositions[Mathf.Min(1, lastIdx)] - cellPositions[0];
                    else if (seqIndex >= lastIdx)
                        dir = cellPositions[lastIdx] - cellPositions[Mathf.Max(0, lastIdx - 1)];
                    else
                    {
                        Vector3 prev = cellPositions[seqIndex] - cellPositions[seqIndex - 1];
                        Vector3 next = cellPositions[seqIndex + 1] - cellPositions[seqIndex];
                        prev.y = 0f;
                        next.y = 0f;
                        dir = (prev.sqrMagnitude > 0.0001f && next.sqrMagnitude > 0.0001f)
                            ? (prev.normalized + next.normalized)
                            : (cellPositions[seqIndex + 1] - cellPositions[seqIndex - 1]);
                    }
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
                    return dir.normalized;
                }

                float ProjectedCellExtent(Vector3 axis)
                {
                    axis.y = 0f;
                    if (axis.sqrMagnitude < 0.0001f) return Mathf.Max(gridCellX, gridCellZ);
                    axis.Normalize();
                    return Mathf.Abs(axis.x) * gridCellX + Mathf.Abs(axis.z) * gridCellZ;
                }

                bool TryGetSeqFootprint(
                    int seqIndex,
                    Vector3 tangent,
                    out Vector3 center,
                    out float alongWorld,
                    out float perpWorld)
                {
                    center = seqIndex >= 0 && seqIndex < cellPositions.Count ? cellPositions[seqIndex] : Vector3.zero;
                    // ROLLBACK_FLEXTUBE_FORCE_2X2_PART_FOOTPRINT_20260628:
                    // A FlexTube visual part is authored as a 2x2 footprint. New MapMaker data stamps
                    // four cells with the same seq, but older/imported levels may still contain only
                    // the clicked anchor cell. Do not let those legacy cells render as 1x1 pieces:
                    // synthesize a minimum 2x2 board footprint at runtime for Head/Body/Edge.
                    alongWorld = ProjectedCellExtent(tangent) * 2f;
                    Vector3 perp = new Vector3(-tangent.z, 0f, tangent.x);
                    perpWorld = ProjectedCellExtent(perp) * 2f;

                    if (seqIndex < 0 || seqIndex >= seqCellIds.Count || seqCellIds[seqIndex] == null || seqCellIds[seqIndex].Count == 0)
                        return false;

                    float minAlong = float.PositiveInfinity, maxAlong = float.NegativeInfinity;
                    float minPerp = float.PositiveInfinity, maxPerp = float.NegativeInfinity;
                    Vector3 sum = Vector3.zero;
                    int count = 0;
                    for (int ci = 0; ci < seqCellIds[seqIndex].Count; ci++)
                    {
                        int cellId = seqCellIds[seqIndex][ci];
                        if (!_balloons.TryGetValue(cellId, out BalloonData cellData)) continue;
                        Vector3 p = GetAdjustedBoardPosition(cellData.position);
                        sum += p;
                        count++;
                        float a = Vector3.Dot(p, tangent);
                        float b = Vector3.Dot(p, perp);
                        if (a < minAlong) minAlong = a;
                        if (a > maxAlong) maxAlong = a;
                        if (b < minPerp) minPerp = b;
                        if (b > maxPerp) maxPerp = b;
                    }

                    if (count <= 0)
                        return false;

                    center = sum / count;
                    if (count < 4)
                    {
                        // Legacy/sparse seq: treat the stored cell as the lower-left anchor of a
                        // 2x2 stamp, matching MapMaker PaintFlexTube2x2(baseC, baseR, seq).
                        center += new Vector3(gridCellX * 0.5f, 0f, gridCellZ * 0.5f);
                    }

                    alongWorld = Mathf.Max(alongWorld, (maxAlong - minAlong) + ProjectedCellExtent(tangent));
                    perpWorld = Mathf.Max(perpWorld, (maxPerp - minPerp) + ProjectedCellExtent(perp));
                    return true;
                }

                bool TryGetSeqWorldFootprintXZ(int seqIndex, out Vector3 center, out float sizeX, out float sizeZ)
                {
                    // ROLLBACK_FLEXTUBE_CAP_RENDERER_BOUNDS_FIT_20260628:
                    // Final cap visuals must be fitted by actual renderer bounds in world X/Z, not only
                    // by predicted local X/Z scale. The cap prefabs are binary/authored assets with child
                    // meshes whose visible axis can differ from root local axes.
                    center = seqIndex >= 0 && seqIndex < cellPositions.Count ? cellPositions[seqIndex] : Vector3.zero;
                    sizeX = gridCellX * 2f;
                    sizeZ = gridCellZ * 2f;

                    if (seqIndex < 0 || seqIndex >= seqCellIds.Count || seqCellIds[seqIndex] == null || seqCellIds[seqIndex].Count == 0)
                        return false;

                    float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
                    float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
                    Vector3 sum = Vector3.zero;
                    int count = 0;
                    for (int ci = 0; ci < seqCellIds[seqIndex].Count; ci++)
                    {
                        int cellId = seqCellIds[seqIndex][ci];
                        if (!_balloons.TryGetValue(cellId, out BalloonData cellData)) continue;
                        Vector3 p = GetAdjustedBoardPosition(cellData.position);
                        sum += p;
                        count++;
                        if (p.x < minX) minX = p.x;
                        if (p.x > maxX) maxX = p.x;
                        if (p.z < minZ) minZ = p.z;
                        if (p.z > maxZ) maxZ = p.z;
                    }

                    if (count <= 0)
                        return false;

                    center = sum / count;
                    if (count < 4)
                    {
                        center += new Vector3(gridCellX * 0.5f, 0f, gridCellZ * 0.5f);
                        sizeX = gridCellX * 2f;
                        sizeZ = gridCellZ * 2f;
                    }
                    else
                    {
                        sizeX = Mathf.Max(gridCellX * 2f, (maxX - minX) + gridCellX);
                        sizeZ = Mathf.Max(gridCellZ * 2f, (maxZ - minZ) + gridCellZ);
                    }

                    return true;
                }

                Vector3 BuildPartScale(float naturalLocalX, float naturalLocalZ)
                {
                    // ROLLBACK_FLEXTUBE_GRID_2CELL_SCALE_20260628:
                    // The visual request is "2 balloon-grid cells", not "prefab transform scale x2".
                    // Measure the prefab's natural visible local size and convert it to a uniform
                    // Transform scale whose largest horizontal visible dimension becomes 2 grid cells.
                    float targetWorld = minCell * FLEXTUBE_SEQ_PART_VISUAL_MULTIPLIER;
                    float natural = Mathf.Max(0.0001f, Mathf.Max(naturalLocalX, naturalLocalZ));
                    return Vector3.one * (targetWorld / natural);
                }

                Vector3 BuildFootprintPartScale(float naturalLocalX, float naturalLocalZ, float targetAlongWorld, float targetPerpWorld)
                {
                    // ROLLBACK_FLEXTUBE_CAP_FOOTPRINT_SCALE_20260628:
                    // Start/Edge are single cap meshes that must occupy the authored 2x2 footprint.
                    // A uniform max-axis scale made EndCap width correct but kept its short local-Z,
                    // so it rendered like a tiny cap inside the footprint. Scale local X to the
                    // footprint width and local Z to the footprint length; keep Y tied to X.
                    float sx = targetPerpWorld / Mathf.Max(0.0001f, naturalLocalX);
                    float sz = targetAlongWorld / Mathf.Max(0.0001f, naturalLocalZ);
                    return new Vector3(sx, sx, sz);
                }

                int FindNearestSeqByPosition(Vector3 pos)
                {
                    int nearest = 0;
                    float best = float.PositiveInfinity;
                    for (int i = 0; i < cellPositions.Count; i++)
                    {
                        Vector3 d = cellPositions[i] - pos;
                        d.y = 0f;
                        float sq = d.sqrMagnitude;
                        if (sq < best)
                        {
                            best = sq;
                            nearest = i;
                        }
                    }
                    return Mathf.Clamp(nearest, 0, lastIdx);
                }

                void BuildCurveSamples(out List<Vector3> curvePoints, out List<float> curveCum)
                {
                    // ROLLBACK_FLEXTUBE_ARCLENGTH_CURVE_SAMPLING_20260628:
                    // EvalFlexTubePath bends corners with Bezier after the original straight-line arc is
                    // chosen. Sampling directly by that straight-line arc makes corner spacing uneven.
                    // Build a rendered curve polyline first, then place Segments by the curve's own
                    // cumulative length so every Segment uses the same size and a predictable position.
                    int samplesPerLogicalSpan = 12;
                    int sampleCount = Mathf.Max(2, (cellPositions.Count - 1) * samplesPerLogicalSpan + 1);
                    curvePoints = new List<Vector3>(sampleCount);
                    curveCum = new List<float>(sampleCount);

                    for (int si = 0; si < sampleCount; si++)
                    {
                        float t = sampleCount <= 1 ? 0f : si / (float)(sampleCount - 1);
                        float sourceArc = Mathf.Lerp(0f, pathTotal, t);
                        EvalFlexTubePath(cellPositions, cumArc, sourceArc, out Vector3 p, out _);

                        if (curvePoints.Count == 0)
                        {
                            curvePoints.Add(p);
                            curveCum.Add(0f);
                            continue;
                        }

                        float step = Vector3.Distance(curvePoints[curvePoints.Count - 1], p);
                        if (step <= 0.0001f)
                            continue;

                        curvePoints.Add(p);
                        curveCum.Add(curveCum[curveCum.Count - 1] + step);
                    }

                    if (curvePoints.Count < 2)
                    {
                        curvePoints.Clear();
                        curveCum.Clear();
                        curvePoints.Add(cellPositions[0]);
                        curvePoints.Add(cellPositions[lastIdx]);
                        curveCum.Add(0f);
                        curveCum.Add(Vector3.Distance(cellPositions[0], cellPositions[lastIdx]));
                    }
                }

                void SampleCurveByDistance(List<Vector3> curvePoints, List<float> curveCum, float distance, float tangentWindow, out Vector3 pos, out Vector3 tangent)
                {
                    void SamplePositionOnly(float d, out Vector3 p)
                    {
                        int pn = curvePoints != null ? curvePoints.Count : 0;
                        if (pn == 0)
                        {
                            p = Vector3.zero;
                            return;
                        }
                        if (pn == 1)
                        {
                            p = curvePoints[0];
                            return;
                        }

                        float pTotal = curveCum[curveCum.Count - 1];
                        d = Mathf.Clamp(d, 0f, pTotal);
                        int pk = 0;
                        while (pk < pn - 2 && curveCum[pk + 1] < d) pk++;

                        float pSpan = Mathf.Max(0.0001f, curveCum[pk + 1] - curveCum[pk]);
                        float pt = Mathf.Clamp01((d - curveCum[pk]) / pSpan);
                        p = Vector3.Lerp(curvePoints[pk], curvePoints[pk + 1], pt);
                    }

                    int n = curvePoints != null ? curvePoints.Count : 0;
                    if (n == 0)
                    {
                        pos = Vector3.zero;
                        tangent = Vector3.forward;
                        return;
                    }
                    if (n == 1)
                    {
                        pos = curvePoints[0];
                        tangent = Vector3.forward;
                        return;
                    }

                    float total = curveCum[curveCum.Count - 1];
                    distance = Mathf.Clamp(distance, 0f, total);
                    SamplePositionOnly(distance, out pos);
                    // ROLLBACK_FLEXTUBE_SEGMENT_TANGENT_SMOOTH_20260628:
                    // Using only curvePoints[k] -> curvePoints[k+1] made the rotation jump at Bezier
                    // branch seams, so one Segment near a corner looked smaller/off-position even
                    // though its Transform scale was identical. Use a centered tangent window so the
                    // rotation follows the same continuous sampled curve as the position.
                    float window = Mathf.Max(0.0001f, tangentWindow);
                    float before = Mathf.Max(0f, distance - window);
                    float after = Mathf.Min(total, distance + window);
                    SamplePositionOnly(before, out Vector3 beforePos);
                    SamplePositionOnly(after, out Vector3 afterPos);
                    tangent = afterPos - beforePos;
                    tangent.y = 0f;
                    // ROLLBACK_FLEXTUBE_DEGENERATE_TANGENT_ZERO_20260628:
                    // 퇴화(before≈after) 시 Vector3.forward(크기 1)를 주면 호출부의 sqrMagnitude<0.0001 폴백을
                    // 우회해 그 세그먼트가 +z 로 비틀린다. 0 을 반환해 호출부가 GetSeqTangent 로 보정하게 한다.
                    if (tangent.sqrMagnitude < 0.000001f)
                        tangent = Vector3.zero;
                    else
                        tangent.Normalize();
                }

                void AttachPartToSeq(FlexTubePart part, int seq)
                {
                    if (part == null || seq < 0 || seq >= seqCellIds.Count) return;
                    part.SetBalloonIds(seqCellIds[seq]);
                    for (int cellIndex = 0; cellIndex < seqCellIds[seq].Count; cellIndex++)
                        _balloonObjects[seqCellIds[seq][cellIndex]] = part.gameObject;
                }

                Vector3 startCapGridScale = BuildPartScale(startCapMeshX, startCapMeshZ);
                Vector3 segmentGridScale = BuildPartScale(segMeshX, segMeshZ);
                Vector3 endCapGridScale = BuildPartScale(endCapMeshX, endCapMeshZ);
                if (FLEXTUBE_SPAWN_DEBUG)
                {
                    Debug.Log(
                        $"[FlexTube-DIAG-CONFIG] G{groupId} cells={ids.Count} seqs={cellPositions.Count} " +
                        $"grid=({gridCellX:F3},{gridCellZ:F3}) minCell={minCell:F3} pathTotal={pathTotal:F3} " +
                        $"meshX/Z start=({startCapMeshX:F3},{startCapMeshZ:F3}) seg=({segMeshX:F3},{segMeshZ:F3}) edge=({endCapMeshX:F3},{endCapMeshZ:F3}) " +
                        $"scale start={Fmt(startCapGridScale)} seg={Fmt(segmentGridScale)} edge={Fmt(endCapGridScale)}");
                    for (int si = 0; si < cellPositions.Count; si++)
                    {
                        int seqCellCount = si < seqCellIds.Count && seqCellIds[si] != null ? seqCellIds[si].Count : 0;
                        Debug.Log($"[FlexTube-DIAG-SEQ] G{groupId} seq={si} cells={seqCellCount} center={Fmt(cellPositions[si])} tangent={Fmt(GetSeqTangent(si))}");
                    }
                }

                // ROLLBACK_FLEXTUBE_AUTHORED_SEQ_ONLY_20260628:
                // Rollback: authored-seq-only generation made the tube too sparse. Restore the previous
                // continuous path tiling so Segment count follows path length/pitch, while Head and Edge
                // remain explicit cap parts.
                if (TryGetSeqFootprint(0, GetSeqTangent(0), out Vector3 startCenter, out float startAlongWorld, out float startPerpWorld))
                {
                    startCenter.z += FLEXTUBE_SEQ_VISUAL_Z_OFFSET;
                    Quaternion startRot = Quaternion.LookRotation(GetSeqTangent(0), Vector3.up) * extraRot;
                    Vector3 startFootprintScale = BuildFootprintPartScale(startCapMeshX, startCapMeshZ, startAlongWorld, startPerpWorld);
                    var startPart = SpawnFlexPart(startCapPrefab, startCenter, startRot,
                                                  true, startFootprintScale, GimmickIdentifier.FlexTubePart.StartCap, seqIds[0]);
                    if (startPart != null)
                    {
                        if (TryGetSeqWorldFootprintXZ(0, out Vector3 startFitCenter, out float startSizeX, out float startSizeZ))
                        {
                            startFitCenter.z += FLEXTUBE_SEQ_VISUAL_Z_OFFSET;
                            FitRendererBoundsToFootprint(startPart.gameObject, startFitCenter, startSizeX, startSizeZ);
                        }
                        else
                        {
                            RecenterToWorldBounds(startPart.gameObject, startCenter);
                        }
                        AttachPartToSeq(startPart, 0);
                        LogFlexTubeSpawn("START", startPart, 0, 0, 0f, -1f, 0f);
                    }
                }

                int visualPerCell = Mathf.Max(1, tube.VisualSegmentsPerCell);
                float pitch = minCell / visualPerCell;
                BuildCurveSamples(out List<Vector3> curvePoints, out List<float> curveCum);
                float curveTotal = curveCum[curveCum.Count - 1];
                // ROLLBACK_FLEXTUBE_CAP_INSET_SEGMENTS_20260628:
                // Cap inset made the first/last Segment stop one cell away from Start/Edge, so the caps
                // looked disconnected and Start was effectively changed to match the broken Edge. Keep the
                // body sampled through both cap centers so Edge uses the same visual connection rule as Start.
                float capInset = 0f;
                float bodyStart = 0f;
                float bodyEnd = curveTotal;
                float bodySpan = Mathf.Max(0f, bodyEnd - bodyStart);
                int bodyCount = bodySpan > 0.0001f ? Mathf.Max(1, Mathf.CeilToInt(bodySpan / Mathf.Max(0.0001f, pitch))) : 0;
                if (FLEXTUBE_SPAWN_DEBUG)
                {
                    Debug.Log(
                        $"[FlexTube-DIAG-CURVE] G{groupId} curveSamples={curvePoints.Count} curveTotal={curveTotal:F3} " +
                        $"capInset={capInset:F3} bodyStart={bodyStart:F3} bodyEnd={bodyEnd:F3} bodySpan={bodySpan:F3} " +
                        $"pitch={pitch:F3} bodyCount={bodyCount}");
                }
                bool hasPrevSegment = false;
                Vector3 prevSegmentPos = Vector3.zero;
                float minSegGap = float.PositiveInfinity;
                float maxSegGap = 0f;
                float sumSegGap = 0f;
                int segGapCount = 0;
                for (int bi = 0; bi <= bodyCount; bi++)
                {
                    float curveDistance = bodyCount <= 0 ? bodyStart : Mathf.Lerp(bodyStart, bodyEnd, bi / (float)bodyCount);
                    SampleCurveByDistance(curvePoints, curveCum, curveDistance, Mathf.Max(0.0001f, pitch), out Vector3 segPos, out Vector3 segTan);
                    segPos.z += FLEXTUBE_SEQ_VISUAL_Z_OFFSET;
                    if (segTan.sqrMagnitude < 0.0001f) segTan = GetSeqTangent(FindNearestSeqByPosition(segPos));
                    Quaternion segRot = Quaternion.LookRotation(segTan.normalized, Vector3.up) * extraRot;
                    int nearestSeq = FindNearestSeqByPosition(segPos);
                    var segPart = SpawnFlexPart(segmentPrefab, segPos, segRot,
                                                true, segmentGridScale, GimmickIdentifier.FlexTubePart.Segment, seqIds[nearestSeq]);
                    if (segPart != null)
                    {
                        AttachPartToSeq(segPart, nearestSeq);
                        float gap = -1f;
                        if (hasPrevSegment)
                        {
                            gap = Vector3.Distance(prevSegmentPos, segPart.transform.position);
                            minSegGap = Mathf.Min(minSegGap, gap);
                            maxSegGap = Mathf.Max(maxSegGap, gap);
                            sumSegGap += gap;
                            segGapCount++;
                            if (FLEXTUBE_SPAWN_DEBUG)
                                Debug.DrawLine(prevSegmentPos, segPart.transform.position, Color.cyan, 20f);
                        }

                        if (FLEXTUBE_SPAWN_DEBUG && bi < FLEXTUBE_SPAWN_DEBUG_SEGMENT_LIMIT)
                            LogFlexTubeSpawn("SEG", segPart, bi, nearestSeq, curveDistance, gap, pitch);

                        prevSegmentPos = segPart.transform.position;
                        hasPrevSegment = true;
                    }
                }
                if (FLEXTUBE_SPAWN_DEBUG)
                {
                    float avgGap = segGapCount > 0 ? sumSegGap / segGapCount : 0f;
                    string minGapText = segGapCount > 0 ? minSegGap.ToString("F3") : "-";
                    Debug.Log(
                        $"[FlexTube-DIAG-SEG-SUMMARY] G{groupId} segs={bodyCount + 1} loggedLimit={FLEXTUBE_SPAWN_DEBUG_SEGMENT_LIMIT} " +
                        $"gapMin={minGapText} gapMax={maxSegGap:F3} gapAvg={avgGap:F3} pitch={pitch:F3}");
                }

                if (TryGetSeqFootprint(lastIdx, GetSeqTangent(lastIdx), out Vector3 endCenter, out float endAlongWorld, out float endPerpWorld))
                {
                    endCenter.z += FLEXTUBE_SEQ_VISUAL_Z_OFFSET;
                    Quaternion endRot = Quaternion.LookRotation(GetSeqTangent(lastIdx), Vector3.up) * extraRot;
                    Vector3 endFootprintScale = BuildFootprintPartScale(endCapMeshX, endCapMeshZ, endAlongWorld, endPerpWorld);
                    var endPart = SpawnFlexPart(endCapPrefab, endCenter, endRot,
                                                true, endFootprintScale, GimmickIdentifier.FlexTubePart.EndCap, seqIds[lastIdx]);
                    if (endPart != null)
                    {
                        if (TryGetSeqWorldFootprintXZ(lastIdx, out Vector3 endFitCenter, out float endSizeX, out float endSizeZ))
                        {
                            endFitCenter.z += FLEXTUBE_SEQ_VISUAL_Z_OFFSET;
                            FitRendererBoundsToFootprint(endPart.gameObject, endFitCenter, endSizeX, endSizeZ);
                        }
                        else
                        {
                            RecenterToWorldBounds(endPart.gameObject, endCenter);
                        }
                        AttachPartToSeq(endPart, lastIdx);
                        LogFlexTubeSpawn("EDGE", endPart, 0, lastIdx, curveTotal, -1f, 0f);
                    }
                }

                ringScale = 1f;
                Debug.Log($"[FlexTube] Group {groupId} seqFootprint parts={parts.Count} seqs={cellPositions.Count} cells={ids.Count}");

#if false // ROLLBACK_FLEXTUBE_UNIFIED_PATH_TILING_20260628
                // --- Tube tiling params (computed BEFORE the caps so the caps can match the segment Z) ---
                // ROLLBACK_FLEXTUBE_CAP_MATCH_BRIDGE_20260624:
                //  • Segments fill the span between the caps. The span is EXTENDED toward each cap by a
                //    fraction of that cap's own scaled length (BRIDGE) so the segment row reaches right up
                //    to / slightly under each cap → removes the cap↔segment gap.
                //  • zScale is pitch-locked → every segment the SAME rendered length (uniform Z).
                //  • Each cap's z-scale is set so the cap's rendered length == actualPitch too, so the caps
                //    read as the SAME Z size as the segments (the two caps have different natural lengths,
                //    which is why one looked stretched). Diameter (x,y) = ribDiameterScale for all.
                // Extend ~one rib-width into each cap (caps are z-matched to one rib, so this reaches the
                // cap's outer edge without poking past it) → fills the cap↔segment junction with a segment.
                float capBridge = ribPitchTarget * FLEXTUBE_CAP_SEGMENT_BRIDGE;
                // ROLLBACK_FLEXTUBE_HEAD2X2_20260626: head(StartCap)를 경로 첫 2칸(seq0+seq1)으로 = 2칸 길이 × 2줄 두께 = 2×2.
                //   centerline 점이 3개 이상일 때만(head 2칸 + tail 1칸 최소). 미만이면 기존 1칸 head 폴백(짧은 튜브 안전).
                // ROLLBACK_FLEXTUBE_INPUT_ORDER_20260626:
                // MapMaker input order is seq0=Head, seq1..max-1=Segment, max=Edge.
                // Do not consume seq1 as part of the Head; the Head is visually 2x via cap scale below.
                bool head2 = false;
                // head2 면 body(rib)는 cell1 '다음'(cell1↔cell2 사이)에서 시작 → 2칸 head 와 겹치지 않음.
                float startInset = head2
                    ? cumArc[1] + (cumArc[2] - cumArc[1]) * FLEXTUBE_ENDCAP_CONNECTOR_INSET
                    : cumArc[1] * FLEXTUBE_ENDCAP_CONNECTOR_INSET;
                float endInset   = pathTotal - (pathTotal - cumArc[lastIdx - 1]) * FLEXTUBE_ENDCAP_CONNECTOR_INSET;
                float startArc = Mathf.Max(0f, startInset - capBridge);
                float endArc   = Mathf.Min(pathTotal, endInset + capBridge);
                float ribSpan  = Mathf.Max(0.0001f, endArc - startArc);
                int ribCount = (segmentCellCount >= 1) ? Mathf.Max(1, Mathf.RoundToInt(ribSpan / ribPitchTarget)) : 0;
                float actualPitch = (ribCount > 0) ? ribSpan / ribCount : ribPitchTarget;
                // ROLLBACK_FLEXTUBE_RIB_LENGTH_OVERLAP_20260626: 리브 배치 간격은 actualPitch 그대로지만, 렌더 길이는
                //   pitch×OVERLAP 로 늘려 이웃 리브와 겹치게 한다 → 메시 끝 여백/neck 으로 생기던 틈 제거(직선·곡선 공통).
                float zScale = (actualPitch * FLEXTUBE_RIB_LENGTH_OVERLAP) / Mathf.Max(0.0001f, segMeshZ);
                ringScale = zScale; // 진단 로그용
                Vector3 ribScale = new Vector3(ribDiameterScale, ribDiameterScale, zScale);
                Vector3 startCapScale = new Vector3(startCapDiameterScale, startCapDiameterScale, actualPitch / Mathf.Max(0.0001f, startCapMeshZ));
                Vector3 endCapScale   = new Vector3(endCapDiameterScale, endCapDiameterScale, actualPitch / Mathf.Max(0.0001f, endCapMeshZ));

                // --- StartCap (logical cell 0) ---
                Vector3 startCapPos;
                Quaternion startCapRot = CalculateFlexTubePartRotation(cellPositions, 0) * extraRot;
                if (head2)
                {
                    // ROLLBACK_FLEXTUBE_HEAD2X2_20260626: head 가 cell0→cell1 2칸을 덮도록 — 중점에 두고 길이(z)를
                    //   [0]→[1] 거리 + bridge 로 확장(중심 pivot 이라 양쪽으로 뻗음). 두께(x,y=diameter 1.8)는 이미 2줄.
                    Vector3 headCenter = (cellPositions[0] + cellPositions[1]) * 0.5f;
                    Vector3 headDir = cellPositions[1] - cellPositions[0]; headDir.y = 0f;
                    float headLen = cumArc[1] + capBridge;
                    startCapPos = headCenter + (headDir.sqrMagnitude > 0.0001f ? headDir.normalized * (capBridge * 0.5f) : Vector3.zero);
                    startCapScale = new Vector3(startCapDiameterScale, startCapDiameterScale, headLen / Mathf.Max(0.0001f, startCapMeshZ));
                }
                else
                {
                    // ROLLBACK_FLEXTUBE_CAP_LENGTH_1CELL_20260626: Head 는 첫 클릭 칸(seq0)에 그대로 앉는다.
                    //   이전엔 INSET 0.5 로 seq0/seq1 경계까지 당겨져, 1칸 길이여도 seq1 절반을 덮었다(=Segment 먹음).
                    //   셀 중심에 두면 [seq0±0.5] 만 차지하고, 리브는 capBridge 로 겹쳐 시작해 틈 없이 이어진다.
                    startCapPos = cellPositions[0];
                }
                // ROLLBACK_FLEXTUBE_CAP_UNIFORM_SCALE_20260628:
                // 캡을 균일 스케일(x=y=z=diameterScale)로 — 메시 비율 보존. 이전엔 길이(z)를 2칸으로 강제했는데,
                // StartCap(메시 z≈0.87)과 EndCap(z≈0.227) 자연 길이가 크게 달라 EndCap 이 4.19배로 늘어나 찌그러졌다
                // (Head/Edge 모양이 서로 다른 '이상함'의 원인). 균일 스케일이면 둘 다 2칸 너비 + 왜곡 없는 자기 모양.
                startCapScale = new Vector3(startCapDiameterScale, startCapDiameterScale, startCapDiameterScale);
                var startCapPart = SpawnFlexPart(startCapPrefab, startCapPos, startCapRot,
                                                 true, startCapScale, GimmickIdentifier.FlexTubePart.StartCap, ids[0]);
                if (startCapPart != null)
                {
                    // ROLLBACK_FLEXTUBE_CAP_RECENTER_WORLDBOUNDS_20260626: 캡 메시 pivot 이 끝/모서리라 위치만으론
                    //   2×2 중심에 안 옴(클릭 셀에 쏠림). 스폰 후 실제 월드 renderer bounds 중심을 4셀 중심(cellPositions[0])에
                    //   맞춰 평행이동 → pivot/메시 형태와 무관하게 geometry 가 2×2 정중앙에 앉는다.
                    RecenterToWorldBounds(startCapPart.gameObject, cellPositions[0]);
                    startCapPart.SetBalloonIds(seqCellIds[0]);
                    _balloonObjects[ids[0]] = startCapPart.gameObject;
                }

                // --- Ribs: uniform tiling across the cap-bridged span ---
                var nearestRibSqr = new Dictionary<int, float>();
                if (segmentCellCount >= 1) // need at least one segment cell between the caps
                {
                    var ribWorldPos = new List<Vector3>(ribCount + 1); // [FlexTube-DIAG] gap uniformity check
                    for (int j = 0; j <= ribCount; j++)
                    {
                        float arc = startArc + j * actualPitch;
                        EvalFlexTubePath(cellPositions, cumArc, arc, out Vector3 ribPos, out Vector3 ribTan);
                        ribWorldPos.Add(ribPos);
                        Quaternion ribRot = ribTan.sqrMagnitude > 0.0001f
                            ? Quaternion.LookRotation(ribTan, Vector3.up) * extraRot
                            : startCapRot;

                        // nearest logical segment cell (1..lastIdx-1) for HP/targeting mapping
                        int nearestSeq = 1;
                        float nearestSqr = float.MaxValue;
                        for (int c = 1; c <= lastIdx - 1; c++)
                        {
                            float dc = (cellPositions[c] - ribPos).sqrMagnitude;
                            if (dc < nearestSqr) { nearestSqr = dc; nearestSeq = c; }
                        }
                        int ribCellId = seqIds[nearestSeq]; // ROLLBACK_FLEXTUBE_2THICK_INDEXFIX_20260626: centerline seq → 대표 cell

                        var ribPart = SpawnFlexPart(segmentPrefab, ribPos, ribRot,
                                                    true, ribScale, GimmickIdentifier.FlexTubePart.Segment, ribCellId);
                        if (ribPart == null) continue;
                        ribPart.SetBalloonIds(seqCellIds[nearestSeq]);

                        if (!nearestRibSqr.TryGetValue(ribCellId, out float best) || nearestSqr < best)
                        {
                            nearestRibSqr[ribCellId] = nearestSqr;
                            _balloonObjects[ribCellId] = ribPart.gameObject;
                        }
                    }

                    // [FlexTube-DIAG] rib spacing uniformity — if min≈max≈pitch the tiling is uniform.
                    if (ribWorldPos.Count >= 2)
                    {
                        float mn = float.MaxValue, mx = 0f, sum = 0f;
                        for (int q = 1; q < ribWorldPos.Count; q++)
                        {
                            float d = Vector3.Distance(ribWorldPos[q - 1], ribWorldPos[q]);
                            mn = Mathf.Min(mn, d); mx = Mathf.Max(mx, d); sum += d;
                        }
                        Debug.Log($"[FlexTube] Group {groupId} rib gaps min={mn:F3} max={mx:F3} avg={sum / (ribWorldPos.Count - 1):F3} pitch={actualPitch:F3} ringThickness≈{ringWorldSize:F3} ribs={ribWorldPos.Count}");
                    }
                } // segmentCellCount >= 1

                // --- EndCap (logical cell last) ---
                // ROLLBACK_FLEXTUBE_CAP_LENGTH_1CELL_20260626: Edge 는 '마지막으로 찍은 칸'에 그대로 앉아야 한다.
                //   INSET 0.5 로 당기면 마지막 셀이 아닌 last-1/last 경계에 앉아 "마지막 칸이 Edge" 의도와 어긋났다.
                Vector3 endCapPos = cellPositions[lastIdx];
                Quaternion endCapRot = CalculateFlexTubePartRotation(cellPositions, lastIdx) * extraRot;
                // ROLLBACK_FLEXTUBE_CAP_UNIFORM_SCALE_20260628: Edge 도 균일 스케일 — 메시 비율 보존(4.19배 늘림 제거).
                endCapScale = new Vector3(endCapDiameterScale, endCapDiameterScale, endCapDiameterScale);
                var endCapPart = SpawnFlexPart(endCapPrefab, endCapPos, endCapRot,
                                               true, endCapScale, GimmickIdentifier.FlexTubePart.EndCap, seqIds[lastIdx]); // ROLLBACK_FLEXTUBE_2THICK_INDEXFIX_20260626
                if (endCapPart != null)
                {
                    // ROLLBACK_FLEXTUBE_CAP_RECENTER_WORLDBOUNDS_20260626: Edge 도 실제 월드 bounds 중심을 마지막 4셀 중심에 맞춤.
                    RecenterToWorldBounds(endCapPart.gameObject, cellPositions[lastIdx]);
                    endCapPart.SetBalloonIds(seqCellIds[lastIdx]);
                    _balloonObjects[seqIds[lastIdx]] = endCapPart.gameObject;
                }

                Debug.Log($"[FlexTube] Group {groupId}: ribs={ribCount + 1} pitch={actualPitch:F3} ringScale={ringScale:F3} gridCell={tubeGridCell:F3} ringSize(=cell/3)={ringWorldSize:F3} subCellSpacing={globalCellLength:F3} cells={ids.Count}");
                // [FlexTube-DIAG CAP] Head/Edge 최종 월드 pos·rot + 경로 방향 — 세로 튜브 캡 이상 진단용.
                if (startCapPart != null && endCapPart != null)
                {
                    Vector3 dir0 = (cellPositions.Count > 1) ? (cellPositions[1] - cellPositions[0]) : Vector3.zero;
                    bool vertical = Mathf.Abs(dir0.z) >= Mathf.Abs(dir0.x);
                    Debug.Log($"[FlexTube-DIAG CAP] G{groupId} {(vertical ? "VERTICAL" : "HORIZONTAL")} dir0=({dir0.x:F2},{dir0.z:F2}) | "
                        + $"HEAD target={cellPositions[0]:F2} pos={startCapPart.transform.position:F2} rotY={startCapPart.transform.eulerAngles.y:F1} scale={startCapPart.transform.localScale:F2} | "
                        + $"EDGE target={cellPositions[lastIdx]:F2} pos={endCapPart.transform.position:F2} rotY={endCapPart.transform.eulerAngles.y:F1} scale={endCapPart.transform.localScale:F2} | extraY={tube.ExtraYRotation:F1}");
                }

                // HP — 작가 지정(flexTubeHp>0)이면 그 값, 아니면 segment cell 수(튜브 길이)로 자동.
                //  • 자동: cell 당 1히트(기존 동작).
                //  • 작가 지정: 한 hit 당 (전체 visual segment / HP)개가 줄어 길이가 HP 비례로 축소되고,
                //    cell 이 소진될 때마다 MarkFlexTubeCellInactive 로 공격 가능 셀도 같은 비율로 갱신됨.
                //  그룹 내 셀들은 동일 HP 를 갖지만, 안전하게 그룹의 최댓값(>0)을 사용.
#endif

                int authoredHp = 0;
                for (int ai = 0; ai < ids.Count; ai++)
                    if (_balloons.TryGetValue(ids[ai], out var bdHp) && bdHp.flexTubeHp > authoredHp)
                        authoredHp = bdHp.flexTubeHp;
                // ROLLBACK_FLEXTUBE_CELL_TARGET_HIT_20260628:
                // Auto HP follows the authored logical footprint cells, not the averaged centerline count.
                // A 2x2 Head/Segment/Edge part occupies four targetable cells, so default HP must not
                // collapse to one centerline point and finish the tube before exposed cells are hit.
                int flexTubeHp = (authoredHp > 0) ? authoredHp : Mathf.Max(1, ids.Count);
                int color = _balloons[ids[0]].color;
                tube.Initialize(flexTubeHp, color, groupId, parts);
            }
        }

        /// <summary>튜브 전체에서 대표(중앙값) 셀 중심 간격. 모든 rib 를 동일 크기로 만들기 위한 단일 pitch 산출용.
        /// 중앙값이라 코너의 대각 거리/끝셀 편차 같은 outlier 에 흔들리지 않음.</summary>
        private static float ComputeFlexTubeMedianCellLength(List<Vector3> cellPositions)
        {
            if (cellPositions == null || cellPositions.Count < 2)
                return FLEXTUBE_SEGMENT_WORLD_LENGTH * 3f;

            var dists = new List<float>(cellPositions.Count - 1);
            for (int k = 0; k < cellPositions.Count - 1; k++)
            {
                float d = Vector3.Distance(cellPositions[k], cellPositions[k + 1]);
                if (d > 0.0001f) dists.Add(d);
            }
            if (dists.Count == 0) return FLEXTUBE_SEGMENT_WORLD_LENGTH * 3f;
            dists.Sort();
            return dists[dists.Count / 2];
        }

        /// <summary>cell-center polyline 위 arc-length 위치의 좌표 + 진행 방향(y=0 평면). 연속 rib 타일링용.</summary>
        private static void EvalFlexTubePath(List<Vector3> pts, List<float> cum, float arc, out Vector3 pos, out Vector3 tangent)
        {
            int n = pts != null ? pts.Count : 0;
            if (n == 0) { pos = Vector3.zero; tangent = Vector3.forward; return; }
            if (n == 1) { pos = pts[0]; tangent = Vector3.forward; return; }

            float total = cum[n - 1];
            arc = Mathf.Clamp(arc, 0f, total);
            int k = 0;
            while (k < n - 2 && cum[k + 1] < arc) k++;
            float segLen = Mathf.Max(0.0001f, cum[k + 1] - cum[k]);
            float t = Mathf.Clamp01((arc - cum[k]) / segLen);

            // ROLLBACK_FLEXTUBE_CORNER_SMOOTH_20260626: 코너(방향 전환) 셀 주변에서 직선 lerp 대신 2차 Bezier 라운딩 적용.
            //   기존엔 EvaluateFlexTubeCornerPosition/Tangent 가 정의만 되고 호출처가 없어(dead) ㄴ/ㄷ/ㄹ 코너가
            //   셀 중심 직선으로 직각 꺾였다. 각 코너 c 의 Bezier 는 mid(c-1,c)[bt0] → corner c[apex] → mid(c,c+1)[bt1]
            //   를 잇는다(반경 ~0.5셀). segment k 의 뒷절반(t>=0.5)=다음 코너(k+1)로 진입(bt=t-0.5∈[0,0.5]),
            //   앞절반(t<0.5)=이전 코너(k)에서 진출(bt=0.5+t∈[0.5,1)). t=0.5/세그먼트 경계에서 연속(양쪽 mid-edge/apex 일치).
            //   코너가 아닌 구간은 기존 직선 보간 그대로(무회귀).
            if (t >= 0.5f && IsFlexTubeCorner(pts, k + 1))
            {
                float bt = t - 0.5f;                            // [0.5,1] → [0,0.5] : start(mid-edge)→corner apex
                pos = EvaluateFlexTubeCornerPosition(pts, k + 1, bt);
                tangent = EvaluateFlexTubeCornerTangent(pts, k + 1, bt);
            }
            else if (t < 0.5f && IsFlexTubeCorner(pts, k))
            {
                float bt = 0.5f + t;                            // [0,0.5) → [0.5,1) : corner apex→end(mid-edge)
                pos = EvaluateFlexTubeCornerPosition(pts, k, bt);
                tangent = EvaluateFlexTubeCornerTangent(pts, k, bt);
            }
            else
            {
                pos = Vector3.Lerp(pts[k], pts[k + 1], t);
                Vector3 dir = pts[k + 1] - pts[k]; dir.y = 0f;
                tangent = dir.sqrMagnitude > 0.000001f ? dir.normalized : Vector3.forward;
            }
            tangent.y = 0f;
            if (tangent.sqrMagnitude < 0.000001f) tangent = Vector3.forward;
        }

        /// <summary>cell index i 의 forward tangent (visual segment 분산 시 사용). 직선/대각 모두 정상 동작. y=0 평면 기준.</summary>
        private static Vector3 ComputeFlexTubeTangent(List<Vector3> cellPositions, int i)
        {
            int n = cellPositions.Count;
            Vector3 dir;
            if (i == 0)                dir = cellPositions[1] - cellPositions[0];
            else if (i == n - 1)       dir = cellPositions[n - 1] - cellPositions[n - 2];
            else                        dir = cellPositions[i + 1] - cellPositions[i - 1];
            dir.y = 0f;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.zero;
        }

        /// <summary>보정된 cell index i 의 인접 셀 간 실제 거리. 양끝은 한쪽, 중간은 양쪽 평균.
        /// visual segment 분산 간격(visualStep = 거리/N) 계산용 — 보드 스케일 반영된 셀 폭과 일치.</summary>
        private static float AdjacentCellDistance(List<Vector3> cellPositions, int i)
        {
            int n = cellPositions.Count;
            if (n < 2) return 0f;
            if (i <= 0)       return Vector3.Distance(cellPositions[0], cellPositions[1]);
            if (i >= n - 1)   return Vector3.Distance(cellPositions[n - 1], cellPositions[n - 2]);
            return 0.5f * (Vector3.Distance(cellPositions[i], cellPositions[i - 1])
                         + Vector3.Distance(cellPositions[i], cellPositions[i + 1]));
        }

        private static float ResolveFlexTubeCellWorldLength(List<Vector3> cellPositions, int i)
        {
            // ROLLBACK_FLEXTUBE_CELL_BASED_THIRD_SCALE_20260623:
            // One logical FlexTube cell must contain exactly three visual Segment meshes.
            // Use the actual adjusted neighbour spacing of this cell; BuildFlexTubes divides
            // this value by visualSegmentsPerCell.
            float length = AdjacentCellDistance(cellPositions, i);
            if (length > 0.0001f) return length;

            if (cellPositions != null && cellPositions.Count >= 2)
            {
                for (int k = 0; k < cellPositions.Count - 1; k++)
                {
                    Vector3 d = cellPositions[k + 1] - cellPositions[k];
                    d.y = 0f;
                    if (d.sqrMagnitude > 0.0001f)
                        return d.magnitude;
                }
            }

            return 1f;
        }

        /// <summary>FlexTube 부품의 회전 계산 — prefab forward = +z (Unity 기본) 가정. cell index i 의 이전/다음 위치로 방향 추론.</summary>
        private static float ResolveFlexTubeSegmentTargetWorldSize(List<Vector3> cellPositions, int visualSegmentsPerCell)
        {
            int segmentCount = Mathf.Max(1, visualSegmentsPerCell);
            if (cellPositions == null || cellPositions.Count < 2)
                return FLEXTUBE_SEGMENT_WORLD_LENGTH * FLEXTUBE_SEGMENT_SEAM_OVERLAP;

            float maxCellDistance = 0f;
            int firstSegmentCell = Mathf.Min(1, cellPositions.Count - 1);
            int lastSegmentCell = Mathf.Max(firstSegmentCell, cellPositions.Count - 2);

            for (int i = firstSegmentCell; i <= lastSegmentCell; i++)
            {
                float cellDist = AdjacentCellDistance(cellPositions, i);
                if (cellDist > maxCellDistance)
                    maxCellDistance = cellDist;
            }

            if (maxCellDistance <= 0.0001f)
                maxCellDistance = AdjacentCellDistance(cellPositions, 0);

            return Mathf.Max(0.0001f, maxCellDistance / segmentCount) * FLEXTUBE_SEGMENT_SEAM_OVERLAP;
        }

        // ROLLBACK_FLEXTUBE_SEGMENT_XY_DECOUPLE_20260623:
        // One logical grid cell must hold exactly 3 Segments that read as a CONTINUOUS tube,
        // not fat per-cell "joints". Only the length axis (Z) is sized so the rendered length
        // equals targetWorldLength (= cell / 3); the tube cross-section (X,Y) stays at the
        // authored art scale, INDEPENDENT of cell spacing.
        // Previous behaviour scaled X/Y by the same length factor, so on a normal/large cell
        // each piece ended up wider than it was long → a knuckle/vertebra look (관절). Decoupling
        // keeps a constant pipe diameter while three equal pieces tile the cell end-to-end.
        private static Vector3 CalculateFlexTubeSegmentScale(float targetWorldLength, float rawMeshZLength)
        {
            if (targetWorldLength <= 0.0001f || rawMeshZLength <= 0.0001f) return Vector3.one;

            // ROLLBACK_FLEXTUBE_TRUE_UNIFORM_SCALE_20260624:
            // TRUE uniform scale (x = y = z = s). A non-uniform localScale combined with the rib's
            // LookRotation sheared the mesh (child renderers rotated under a non-uniform parent scale),
            // which made ribs look inconsistent — some touching, some apart. A single scalar removes
            // the shear so every rib is identical. s is chosen so rendered length (rawMeshZ × s)
            // = pitch · overlap → ribs touch/overlap evenly with no gaps.
            float s = targetWorldLength * FLEXTUBE_SEGMENT_SEAM_OVERLAP / rawMeshZLength;
            return new Vector3(s, s, s);
        }

        private static bool TryGetLocalProjectedRendererLength(Transform root, Vector3 localAxis, out float length)
        {
            length = 0f;
            if (root == null || localAxis.sqrMagnitude < 0.0001f) return false;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return false;

            Vector3 axis = localAxis.normalized;
            float near = float.PositiveInfinity;
            float far = float.NegativeInfinity;
            bool hasBounds = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;

                Bounds bounds = renderer.localBounds;
                Vector3 center = bounds.center;
                Vector3 extents = bounds.extents;

                for (int sx = -1; sx <= 1; sx += 2)
                {
                    for (int sy = -1; sy <= 1; sy += 2)
                    {
                        for (int sz = -1; sz <= 1; sz += 2)
                        {
                            Vector3 rendererLocal = center + new Vector3(extents.x * sx, extents.y * sy, extents.z * sz);
                            Vector3 rootLocal = root.InverseTransformPoint(renderer.transform.TransformPoint(rendererLocal));
                            float projected = Vector3.Dot(rootLocal, axis);
                            if (projected < near) near = projected;
                            if (projected > far) far = projected;
                            hasBounds = true;
                        }
                    }
                }
            }

            if (!hasBounds) return false;
            length = Mathf.Max(0f, far - near);
            return length > 0.0001f;
        }

        /// <summary>스폰된 오브젝트의 visible geometry(월드) 중심이 targetCenter 에 오도록 평행이동.
        /// pivot/메시 형태와 무관하게 2×2 4셀 중심에 정확히 앉힌다.
        /// renderer.bounds 는 Instantiate 직후 프레임 타이밍에 따라 stale 일 수 있어, MeshFilter.sharedMesh.bounds
        /// (메시 로컬, 항상 유효)를 각 renderer 의 localToWorld 로 변환해 결정적으로 계산한다.</summary>
        private static void RecenterToWorldBounds(GameObject obj, Vector3 targetCenter)
        {
            if (obj == null) return;
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            bool hasBounds = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;

                var mf = renderer.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    Bounds lb = mf.sharedMesh.bounds;
                    Vector3 c = lb.center, e = lb.extents;
                    for (int sx = -1; sx <= 1; sx += 2)
                        for (int sy = -1; sy <= 1; sy += 2)
                            for (int sz = -1; sz <= 1; sz += 2)
                            {
                                Vector3 world = renderer.transform.TransformPoint(c + new Vector3(e.x * sx, e.y * sy, e.z * sz));
                                min = Vector3.Min(min, world);
                                max = Vector3.Max(max, world);
                                hasBounds = true;
                            }
                }
                else
                {
                    Bounds wb = renderer.bounds;
                    min = Vector3.Min(min, wb.min);
                    max = Vector3.Max(max, wb.max);
                    hasBounds = true;
                }
            }
            if (!hasBounds) return;

            Vector3 center = (min + max) * 0.5f;
            obj.transform.position += (targetCenter - center);
        }

        private static void ClearFlexTubeTemplateChildren(GameObject tubeObj)
        {
            if (tubeObj == null) return;

            for (int ci = tubeObj.transform.childCount - 1; ci >= 0; ci--)
            {
                Transform child = tubeObj.transform.GetChild(ci);
                if (child == null) continue;

                // ROLLBACK_FLEXTUBE_HIDE_TEMPLATE_CHILDREN_20260608:
                // FlexTube.prefab may contain authored sample parts. Destroy() removes them at end of frame,
                // so disable first to prevent black/default template pieces from being visible with runtime parts.
                child.gameObject.SetActive(false);
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        private static bool IsFlexTubeCorner(List<Vector3> cellPositions, int i)
        {
            if (cellPositions == null || i <= 0 || i >= cellPositions.Count - 1) return false;
            Vector3 a = cellPositions[i] - cellPositions[i - 1];
            Vector3 b = cellPositions[i + 1] - cellPositions[i];
            a.y = 0f;
            b.y = 0f;
            if (a.sqrMagnitude < 0.0001f || b.sqrMagnitude < 0.0001f) return false;
            return Mathf.Abs(Vector3.Dot(a.normalized, b.normalized)) < 0.95f;
        }

        private static Vector3 EvaluateFlexTubeCornerPosition(List<Vector3> cellPositions, int i, float t)
        {
            Vector3 prev = cellPositions[i - 1];
            Vector3 self = cellPositions[i];
            Vector3 next = cellPositions[i + 1];
            Vector3 start = Vector3.Lerp(prev, self, 0.5f);
            Vector3 end = Vector3.Lerp(self, next, 0.5f);
            Vector3 control = GetFlexTubeCornerControlPoint(prev, self, next);
            t = Mathf.Clamp01(t);
            float inv = 1f - t;
            return inv * inv * start + 2f * inv * t * control + t * t * end;
        }

        private static Vector3 EvaluateFlexTubeCornerTangent(List<Vector3> cellPositions, int i, float t)
        {
            Vector3 prev = cellPositions[i - 1];
            Vector3 self = cellPositions[i];
            Vector3 next = cellPositions[i + 1];
            Vector3 start = Vector3.Lerp(prev, self, 0.5f);
            Vector3 end = Vector3.Lerp(self, next, 0.5f);
            Vector3 control = GetFlexTubeCornerControlPoint(prev, self, next);
            t = Mathf.Clamp01(t);
            Vector3 tangent = 2f * (1f - t) * (control - start) + 2f * t * (end - control);
            tangent.y = 0f;
            return tangent;
        }

        private static Vector3 GetFlexTubeCornerControlPoint(Vector3 prev, Vector3 self, Vector3 next)
        {
            Vector3 fromPrev = self - prev;
            Vector3 toNext = next - self;
            fromPrev.y = 0f;
            toNext.y = 0f;
            if (fromPrev.sqrMagnitude < 0.0001f || toNext.sqrMagnitude < 0.0001f)
                return self;

            // ROLLBACK_FLEXTUBE_CORNER_CONTROL_SELF_20260628:
            //   control=self(코너 꼭짓점). start=mid(prev,self), end=mid(self,next) 와 함께 두 직선부에 접하는
            //   깔끔한 2차 라운딩 — 삼각형 start-self-end 안에 머물러 cusp/역전이 없다(tangent 항상 매끄러움).
            //   이전 inward 당김(0.45×halfEdge)은 코너에서 곡선이 안쪽으로 꺾여 cusp 가 생기면 그 지점 세그먼트의
            //   곡선-tangent 가 비틀리고(틀어진 방향) 겹쳐 작아 보이던 원인 → 제거하고 self 로 복원.
            return self;
        }

        private static Quaternion CalculateFlexTubePartRotation(List<Vector3> cellPositions, int i)
        {
            int n = cellPositions.Count;
            Vector3 self = cellPositions[i];
            Vector3 dir;
            if (i == 0)
                dir = cellPositions[1] - self;                       // StartCap — 다음 Segment 방향으로 향함
            else if (i == n - 1)
                dir = self - cellPositions[n - 2];                   // EndCap — 이전 Segment 에서 자신 쪽 방향
            else
                dir = cellPositions[i + 1] - cellPositions[i - 1];   // Segment — 이전→다음 (직선/대각)

            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return Quaternion.identity;
            return Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        private GameObject GetOrCreateBalloonObject(int balloonId, Vector3 position, int color)
        {
            if (!ObjectPoolManager.HasInstance)
            {
                Debug.LogWarning("[BalloonController] ObjectPoolManager not available.");
                return null;
            }

            // 기믹별 전용 풀 라우팅 — 기믹 비주얼 프리팹이 있는 경우 해당 풀 사용
            string poolKey = PoolKey;
            if (_balloons.TryGetValue(balloonId, out BalloonData bData))
            {
                poolKey = ResolveGimmickPoolKey(bData.gimmickType);
            }
            GameObject obj = ObjectPoolManager.Instance.Get(poolKey);
            if (obj == null)
            {
                Debug.LogWarning($"[BalloonController] Pool returned null for key '{poolKey}'.");
                return null;
            }

            // 풍선 타일 영역 배율 적용 (레벨별 안전 배율 사용)
            Vector3 adjustedPos = GetAdjustedBoardPosition(position);
            adjustedPos.y = position.y;   // [#2-B] 풍선 Y 위치 고정(0.1) 해제 → 자연 Y. 높이는 scale.y=0.35. (다트/기믹 무관)
            obj.transform.position = adjustedPos;
            float scaleMult = _levelSafeCalculated
                ? Mathf.Max(_levelSafeWm, _levelSafeHm)
                : (GameManager.HasInstance ? Mathf.Max(GameManager.Instance.Board.balloonFieldWidthMult, GameManager.Instance.Board.balloonFieldHeightMult) : 1f);
            obj.transform.localScale = GetBalloonRestScale(scaleMult);
            obj.SetActive(true);

            // [Optimization 2026-05-12] Balloon Shadow 의 SpriteRenderer → MeshRenderer 전환.
            // SpriteRenderer 의 PerRendererData MPB 가 SRP Batcher / GPU Instancing 차단 → MeshRenderer + Quad + 공용 mat 으로 swap.
            // 같은 sprite (모든 balloon 의 shadow 가 같은 sprite) → 1 mat → SRP Batcher batch.
            // 이미 변환된 경우 (pool 재사용) SpriteRenderer 없어 noop.
            SpriteSRPBatcherUtil.ConvertShadowToMeshSprite(obj);

            // per-object 색상 변주 (같은 색이라도 톤이 약간씩 다름)
            int colorIdx = Mathf.Clamp(color, 0, BalloonColors.Length - 1);
            Color variedColor = GetVariedColor(colorIdx);
            // ROLLBACK_IRONWALL_KEEP_IRONBOX_MATERIAL_20260626: Wall=IronBox 는 authored IronBox.mat(색 무관) 유지 → 틴트 스킵.
            //   (poolKey==IronBoxPoolKey ⟺ Wall. 이 '일반 틴트'가 GimmickWall override 전에 IronBox 를 색으로 칠해
            //    IronBox.mat 이 덮이던 진짜 원인. override 분기의 틴트 제거만으론 부족했음.)
            if (poolKey != IronBoxPoolKey)
                ApplyTintToObject(obj, variedColor);

            // Initialize BalloonIdentifier for dart hit detection
            BalloonIdentifier identifier = obj.GetComponent<BalloonIdentifier>();
            if (identifier != null)
            {
                // [Outline fix 2026-06-10] pop 풍선(=외곽이라 hull 부착)이 hull 단 채 풀 반환 → 재사용 시
                // ApplyColor 가 [0]만 교체해 hull 잔존 → "모든 풍선 아웃라인" 증상. 스폰 시 잔존 hull 제거.
                StripOutlineHull(identifier.ColorRenderers);
                identifier.Initialize(balloonId, color);
            }

            return obj;
        }

        private void BuildPositionIndex()
        {
            _positionIndex.Clear();
            _multiCellOccupancy.Clear();
            foreach (BalloonData d in _balloons.Values)
            {
                RegisterPositionIndexForBalloon(d);
            }
        }

        private bool UsesMultiCellOccupancy(BalloonData data)
        {
            if (data == null) return false;
            // ROLLBACK_BARRICADE_OCCUPANCY_FOOTPRINT_20260616: 다중셀 바리케이드는 sizeW/H 가 1 이어도
            //   barricadeLength>1 방향 footprint 를 점유해야 인접 쿼리(Ice flood/Surprise reveal)가 body 셀을 본다.
            //   (기존엔 sizeW/H>1 만 봐서 length 기반 바리케이드가 1셀로만 등록 → 인접 누락.) 롤백: 이 if 제거.
            if (data.gimmickType == GimmickBarricade && data.barricadeLength > 1) return true;
            return IsSizedFieldGimmick(data.gimmickType)
                && (data.sizeW > 1 || data.sizeH > 1);
        }

        // 멀티셀 footprint(occupancy/blocking/targeting)을 갖는 sized field 기믹의 단일 소스.
        // 모든 소비처(UsesMultiCellOccupancy / DirectionalTargeting / BoardStateManager / DartManager)가 이 함수를 참조.
        // Wall 도 정사각 2×2/3×3 footprint 전체가 blocker 로 깔려야 하므로 포함 (미포함 시 anchor 1칸만 막혀 다트가 관통).
        public static bool IsSizedFieldGimmick(string gimmickType)
        {
            string normalized = GimmickDisplayName.Normalize(gimmickType);
            return normalized == GimmickPinata
                || normalized == GimmickPinataBox
                || normalized == GimmickBarricade
                // ROLLBACK_ICE_OVERLAY_UNDER_BALLOONS_20260626:
                // Ice 2x2/3x3 is 4/9 real balloon cells covered by one visual overlay, so it must
                // not be treated as one sized targeting/occupancy object here.
                || normalized == GimmickWall;
        }

        private void BuildOccupiedCells(BalloonData data, List<Vector3Int> output)
        {
            output.Clear();
            if (data == null) return;

            Vector3Int anchor = ToGridKey(data.position);

            // ROLLBACK_BARRICADE_OCCUPANCY_FOOTPRINT_20260616: 바리케이드는 barricadeDir/Length 방향 footprint(두께 2)로 점유.
            //   DirectionalTargeting 의 타게팅 footprint 와 동일 키 규칙(axisZ: along=Z·perp=X / else: along=X·perp=Z).
            //   full 길이로 등록(HP 축소분도 보수적으로 점유 유지 — 인접 쿼리엔 over-occupy 가 안전).
            //   롤백: 이 if 블록 제거(아래 sizeW/H 루프만 남김).
            if (data.gimmickType == GimmickBarricade && data.barricadeLength > 1)
            {
                // ROLLBACK_BARRICADE_LENGTH_SEGMENTS_20260623:
                // Occupancy follows remaining attackable length: length 3 -> 3, then 2, then 1.
                int totalLen = GetBarricadeActiveLength(data);
                if (totalLen <= 0) return;
                int bdir = ((data.barricadeDir % 4) + 4) % 4;   // 0=N(+Z) 1=E(+X) 2=S(-Z) 3=W(-X)
                bool axisZ = (bdir == 0 || bdir == 2);
                int sign = (bdir == 0 || bdir == 1) ? 1 : -1;
                for (int a = 0; a < totalLen; a++)
                {
                    for (int p = 0; p < 2; p++)   // 두께 2칸
                    {
                        int cx = axisZ ? anchor.x + p : anchor.x + a * sign;
                        int cz = axisZ ? anchor.z + a * sign : anchor.z + p;
                        output.Add(new Vector3Int(cx, 0, cz));
                    }
                }
                return;
            }

            int width = Mathf.Max(1, data.sizeW);
            int height = Mathf.Max(1, data.sizeH);
            for (int dx = 0; dx < width; dx++)
            {
                for (int dz = 0; dz < height; dz++)
                    output.Add(new Vector3Int(anchor.x + dx, 0, anchor.z + dz));
            }
        }

        private void RegisterPositionIndexForBalloon(BalloonData data)
        {
            if (data == null || data.isPopped) return;

            if (!UsesMultiCellOccupancy(data))
            {
                _positionIndex[ToGridKey(data.position)] = data.balloonId;
                return;
            }

            BuildOccupiedCells(data, _reusableOccupiedCells);
            var cells = new List<Vector3Int>(_reusableOccupiedCells.Count);
            for (int i = 0; i < _reusableOccupiedCells.Count; i++)
            {
                Vector3Int cell = _reusableOccupiedCells[i];
                cells.Add(cell);
                _positionIndex[cell] = data.balloonId;
            }
            _multiCellOccupancy[data.balloonId] = cells;
        }

        private void RemovePositionIndexForBalloon(BalloonData data)
        {
            if (data == null) return;

            if (_multiCellOccupancy.TryGetValue(data.balloonId, out List<Vector3Int> cells))
            {
                for (int i = 0; i < cells.Count; i++)
                    _positionIndex.Remove(cells[i]);
                _multiCellOccupancy.Remove(data.balloonId);
                return;
            }

            _positionIndex.Remove(ToGridKey(data.position));
        }

        private void GetFieldVisualMetrics(
            out float widthMult,
            out float heightMult,
            out float scaleMult,
            out float cellSizeX,
            out float cellSizeZ,
            out float scaleBase)
        {
            float cs = _cellSpacing > 0 ? _cellSpacing : 0.3f;
            widthMult = 1f;
            heightMult = 1f;
            scaleMult = _levelSafeCalculated
                ? Mathf.Max(_levelSafeWm, _levelSafeHm)
                : (GameManager.HasInstance ? Mathf.Max(GameManager.Instance.Board.balloonFieldWidthMult, GameManager.Instance.Board.balloonFieldHeightMult) : 1f);
            if (_levelSafeCalculated)
            {
                widthMult = _levelSafeWm;
                heightMult = _levelSafeHm;
            }
            else if (GameManager.HasInstance)
            {
                widthMult = GameManager.Instance.Board.balloonFieldWidthMult;
                heightMult = GameManager.Instance.Board.balloonFieldHeightMult;
            }

            cellSizeX = cs * widthMult;
            cellSizeZ = cs * heightMult;
            scaleBase = _balloonScale * scaleMult;
        }

        // Target Box(Pinata_Box) 알 모델 비주얼: 박스 루트를 footprint 중앙·identity scale 로 놓고
        // PinataBoxView 가 W×H 알을 셀 간격으로 배치·색칠. 단일 mesh 확대가 아니므로 이웃 셀 침범 없음.
        private void ApplyPinataBoxVisual(GameObject obj, BalloonData data, PinataBoxView view)
        {
            if (obj == null || data == null || view == null) return;

            GetFieldVisualMetrics(
                out _, out _, out _,
                out float cellSizeX, out float cellSizeZ, out _);

            int width = Mathf.Max(1, data.sizeW);
            int height = Mathf.Max(1, data.sizeH);

            Vector3 anchor = GetAdjustedBoardPosition(data.position);
            Vector3 center = new Vector3(
                anchor.x + (width - 1) * cellSizeX * 0.5f,
                obj.transform.position.y,
                anchor.z + (height - 1) * cellSizeZ * 0.5f);
            obj.transform.position = center;
            obj.transform.localScale = Vector3.one; // 알/틀 크기는 view 가 제어 (루트 scale=1 가정)

            // 알은 footprint 안쪽 격자 셀에 자동 맞춤(cellSize 기준). eggHps 로 초기 균열 상태 판단.
            // scaleMult 는 전달하지 않는다 — cellSizeX/Z 가 이미 field mult 를 포함하므로 알엔 재적용 불필요.
            view.Build(width, height, data.eggColors, data.eggHps, cellSizeX, cellSizeZ);
        }

        private void ApplySizedFieldVisualTransform(GameObject obj, BalloonData data)
        {
            if (obj == null || data == null) return;

            GetFieldVisualMetrics(
                out float widthMult,
                out float heightMult,
                out float scaleMult,
                out float cellSizeX,
                out float cellSizeZ,
                out _);

            int width = Mathf.Max(1, data.sizeW);
            int height = Mathf.Max(1, data.sizeH);

            // ROLLBACK_SIZED_GIMMICK_ANCHOR_VISUAL_CENTER:
            // MapMaker stores the authored cell as the bottom-left anchor of the sized gimmick.
            // Most field prefabs scale from their center pivot, so move only the visual root to
            // the center of that anchored rectangle. Logical occupancy/targeting still uses
            // data.position as the bottom-left anchor.
            Vector3 adjustedAnchor = GetAdjustedBoardPosition(data.position);
            Vector3 visualCenter = new Vector3(
                adjustedAnchor.x + (width - 1) * cellSizeX * 0.5f,
                obj.transform.position.y,
                adjustedAnchor.z + (height - 1) * cellSizeZ * 0.5f);
            obj.transform.localScale = new Vector3(
                _balloonScale * widthMult * width,
                _balloonScale * scaleMult,
                _balloonScale * heightMult * height);
            obj.transform.position = visualCenter;

            // ROLLBACK_SIZED_FIELD_BOUNDS_FIT_20260608:
            // Multi-cell 1x1-authored prefabs such as Wall can have renderer bounds smaller than
            // one board cell. Fit the visible x/z bounds to the occupied footprint instead of
            // assuming localScale * sizeW/H fills the board cells.
            FitRendererBoundsToFootprint(obj, visualCenter, cellSizeX * width, cellSizeZ * height);
        }

        // ROLLBACK_BARRICADE_SMOOTH_RESHAPE_20260625: animate=true(히트 경로)면 길이 변화를 트윈으로 보간(끊김 제거)
        //   + Tiling Y 를 스케일 비율로 따라가게(패턴 유지). animate=false(스폰/풀재사용)는 즉시.
        private void ApplyBarricadeVisualTransform(GameObject obj, BalloonData data, bool animate = false)
        {
            if (obj == null || data == null) return;

            GetFieldVisualMetrics(
                out float widthMult,
                out float heightMult,
                out float scaleMult,
                out float cellSizeX,
                out float cellSizeZ,
                out _);

            int width = Mathf.Max(1, data.sizeW);
            int height = Mathf.Max(1, data.sizeH);
            // ROLLBACK_BARRICADE_DIR_LENGTH_20260608: 이전 vertical=height>width(2방향) → barricadeDir(N/S/E/W 4방향)+barricadeLength.
            // bdir: 0=N(+Z) 1=E(+X) 2=S(-Z) 3=W(-X). vertical(=N/S, Z축) 은 기존 회전/축 로직 재사용용 별칭, dirSign 은 S/W 음수 확장.
            int bdir = ((data.barricadeDir % 4) + 4) % 4;
            bool vertical = (bdir == 0 || bdir == 2);
            float dirSign = (bdir == 0 || bdir == 1) ? 1f : -1f;

            // ROLLBACK_BARRICADE_VISUAL_SETTINGS:
            // The root/head stays on the authored anchor cell. Only BarricadeBody covers the extra cells.
            Vector3 adjustedAnchor = GetAdjustedBoardPosition(data.position);
            // ROLLBACK_BARRICADE_UNIFORM_SCALE_20260608: 수평 균일 스케일(X=Z) — Y회전 shear 방지 + head 두께 fit 시 모든 조각 비율 유지.
            // ROLLBACK_BARRICADE_HEAD_CENTER_20260609:
            // MapMaker's anchor is the first occupied cell (a=0,p=0), while the Barricade head
            // is authored as a 2x2 visual. Move the visual target from the first cell center to
            // the head footprint center so x/z lines up with the authored footprint.
            Vector3 alongDir = bdir == 1 ? Vector3.right : bdir == 3 ? Vector3.left : bdir == 0 ? Vector3.forward : Vector3.back;
            Vector3 perpDir = vertical ? Vector3.right : Vector3.forward;
            Vector3 headCenterOffset =
                alongDir * ((BARRICADE_HEAD_CELLS - 1f) * 0.5f * (vertical ? cellSizeZ : cellSizeX)) +
                perpDir * (0.5f * (vertical ? cellSizeX : cellSizeZ));
            float baseUniform = _balloonScale * scaleMult;
            obj.transform.localScale = new Vector3(baseUniform, baseUniform, baseUniform);
            // ROLLBACK_BARRICADE_Z_NUDGE_20260623:
            // Only adjust the rendered art in world Z. Logical occupancy/targeting remains on
            // the authored grid cells, so this cannot change attack reach or blocker behavior.
            float zNudge = _barricadeVisualZCellNudge * cellSizeZ;
            obj.transform.position = new Vector3(
                adjustedAnchor.x + headCenterOffset.x + _barricadeVisualOffset.x,
                _barricadeVisualY + _barricadeVisualOffset.y,
                adjustedAnchor.z + headCenterOffset.z + _barricadeVisualOffset.z + zNudge);

            // GimmickIdentifier 에 Head/Body/Edge 가 할당됨(사용자 확인). 미할당 시 이름 폴백.
            GimmickIdentifier gid = obj.GetComponent<GimmickIdentifier>();
            Transform head = (gid != null && gid.BarricadeHead != null)
                ? gid.BarricadeHead
                : FindChildRecursive(obj.transform, "Barricade") ?? FindChildRecursive(obj.transform, "BarricadeHead");
            Transform body = (gid != null && gid.BarricadeBody != null)
                ? gid.BarricadeBody
                : FindChildRecursive(obj.transform, "BarricadeBody") ?? FindChildRecursive(obj.transform, "BaricadeBody");
            Transform edge = (gid != null && gid.BarricadeEdge != null)
                ? gid.BarricadeEdge
                : FindChildRecursive(obj.transform, "Edge") ?? FindChildRecursive(obj.transform, "BarricadeEdge");

            // ROLLBACK_BARRICADE_ASSEMBLY_20260608 [재설계]:
            //   head/body/edge 는 "Baricade (1)"(=body.parent) 아래 author 된 연결 유닛. 개별 월드배치(이전 방식) 폐기.
            //   1) assembly 통째 방향 회전 → 연결 그대로 유지, 회전 후 head 를 anchor 에 재고정.
            //   2) body(피벗=Head쪽, 로컬+X)만 localScale.x 로 늘림 → Head쪽 고정·Edge쪽으로 자람.
            //   3) edge 를 body 끝(body.position + body.right × bodyLen)으로 이동. (회전은 assembly 가 담당)
            Transform assembly = (body != null) ? body.parent : (head != null ? head.parent : null);
            if (body == null || assembly == null)
            {
                obj.transform.localScale = new Vector3(
                    _balloonScale * widthMult * width, _balloonScale * scaleMult, _balloonScale * heightMult * height);
                return;
            }

            // ROLLBACK_BARRICADE_SMOOTH_HIT_20260625:
            // Partial hit reshapes should start from the currently visible body length, not jump
            // back to the authored base before tweening. Cache the visible state before the pooled
            // transform reset below, then restore only the X length for the visual tween.
            Vector3 visibleBodyScale = body.localScale;
            bool visibleBodyActive = body.gameObject.activeSelf;

            // ROLLBACK_BARRICADE_POOL_VISUAL_RESET_20260624:
            // Barricade body/edge are moved and scaled in world space below. Pooled reuse must
            // start from the authored child transforms, otherwise MapMaker re-play or Retry can
            // inherit the previous direction's edge/body offsets and look rotated/misaligned.
            RestoreBarricadeAuthoredPartState(body, edge);

            // 1) 방향 회전 + head 를 anchor 에 재고정.
            //    authored 기본 = +X = East = bdir1 → yaw=(bdir-1)*90. 머리 방향이 어긋나면 _barricadeHeadYawOffset 로 90° 단위 보정.
            if (!_barricadeAssemblyBaseRot.TryGetValue(assembly, out Quaternion asmBase))
            {
                asmBase = assembly.localRotation;
                _barricadeAssemblyBaseRot[assembly] = asmBase;
            }
            // ROLLBACK_BARRICADE_ASSEMBLY_REBASE_20260623:
            // ApplyBarricadeVisualTransform can run repeatedly after hits. Reset the authored
            // assembly local position before head/body/edge fitting so the "move head to root"
            // correction below does not accumulate and leave Head/Body/Edge slightly offset.
            if (!_barricadeAssemblyBaseLocalPositions.TryGetValue(assembly, out Vector3 asmBaseLocalPos))
            {
                asmBaseLocalPos = assembly.localPosition;
                _barricadeAssemblyBaseLocalPositions[assembly] = asmBaseLocalPos;
            }
            assembly.localPosition = asmBaseLocalPos;
            assembly.localRotation = asmBase * Quaternion.Euler(0f, (bdir - 1) * 90f + _barricadeHeadYawOffset, 0f);
            if (head != null)
                assembly.position += (obj.transform.position - head.position);

            // ROLLBACK_BARRICADE_UNIFORM_SCALE_20260608: 두께 2칸 = head 두께가 2칸이 되도록 obj 를 "균일" 스케일 fit.
            //   → body/edge 는 같은 배율로 묶여 프리팹 비율 유지(독립 두께 보정 X, body 가 과하게 넓어지지 않음).
            float cellPerp = vertical ? cellSizeX : cellSizeZ;
            if (head != null && TryGetProjectedBounds(head, head.forward, out float hNear, out float hFar))
            {
                float headThick = Mathf.Max(0.0001f, hFar - hNear);
                float fUniform = (2f * cellPerp) / headThick;
                float u = baseUniform * fUniform;
                obj.transform.localScale = new Vector3(u, u, u);
                assembly.position += (obj.transform.position - head.position); // 스케일 변경으로 head 가 움직였으니 재고정
            }

            // 2) 레거시(length<=1)는 멀티셀 비활성 → head 만(body/edge 숨김).
            // ROLLBACK_BARRICADE_LENGTH_SEGMENTS_20260623:
            // Active visual length follows the same remaining segment count used by targeting.
            float activeLengthCells = GetBarricadeActiveLength(data);
            int barricadeMaxHp = Mathf.Max(1, data.maxHP);
            int barricadeRemainHp = Mathf.Clamp(barricadeMaxHp - Mathf.Max(0, data.hitCount), 0, barricadeMaxHp);
            bool barricadeAlive = barricadeRemainHp > 0;

            if (activeLengthCells <= 0f && !animate)
            {
                body.gameObject.SetActive(false);
                if (edge != null) edge.gameObject.SetActive(false);
                return;
            }

            // 길이/HP → body 월드 길이.
            // ROLLBACK_BARRICADE_LENGTH_SEGMENTS_20260623:
            // A length value means occupied/attackable cells, not a separate body ratio.
            // Head covers the first authored cells; body extends only the remaining live length.
            // ROLLBACK_BARRICADE_VISUAL_BODY_UNTIL_HP0_20260625:
            // Targeting may shrink to the head footprint while HP is still > 0 (ex: length 3, HP 2/3 -> activeLength 2).
            // Visually, the Body must keep shrinking by HP ratio and disappear only at HP 0.
            float fullBodyCells = Mathf.Max(0f, Mathf.Max(1, data.barricadeLength) - BARRICADE_HEAD_CELLS);
            float logicalBodyCells = Mathf.Max(0f, activeLengthCells - BARRICADE_HEAD_CELLS);
            float hpBodyCells = barricadeAlive && fullBodyCells > 0f
                ? fullBodyCells * (barricadeRemainHp / (float)barricadeMaxHp)
                : 0f;
            float bodyCells = Mathf.Max(logicalBodyCells, hpBodyCells);
            float cellAlong = vertical ? cellSizeZ : cellSizeX;
            float bodyWorldLen = Mathf.Max(0f, bodyCells * cellAlong * _barricadeLengthMultiplier + (bodyCells > 0f ? _barricadeLengthPadding : 0f));

            // 3) body 늘리기 — 피벗 Head쪽·로컬 +X. base 최장축 길이 대비 비율로 localScale.x.
            if (!_barricadeBodyBaseScales.TryGetValue(body, out Vector3 baseScale))
            {
                baseScale = body.localScale;
                _barricadeBodyBaseScales[body] = baseScale;
            }
            // baseLen = body 의 "길이축(body.right)" 월드 길이 — 최장축(max size) 이 아니라 진행축으로 측정해야 5칸이 정확히 참.
            if (!_barricadeBodyBaseLen.TryGetValue(body, out float baseLen))
            {
                Vector3 prev = body.localScale;
                body.localScale = baseScale;
                baseLen = TryGetProjectedBounds(body, body.right, out float bN, out float bF)
                    ? Mathf.Max(0.0001f, bF - bN) : 1f;
                _barricadeBodyBaseLen[body] = baseLen;
                body.localScale = prev;
            }
            bool hasBody = bodyWorldLen > 0.001f;
            bool animateBody = animate && (hasBody || visibleBodyActive);
            if (!hasBody && !animateBody)
            {
                body.gameObject.SetActive(false);
                if (edge != null) edge.gameObject.SetActive(false);
                return;
            }

            body.gameObject.SetActive(hasBody || animateBody);
            float targetScaleX = baseScale.x * (bodyWorldLen / baseLen);

            // 4) edge 를 body 끝으로 붙이는 로컬 함수(instant/트윈 공용). bounds 실측 우선, 실패 시 수식 폴백.
            //    body.position=피벗(Head쪽 끝), body.right=진행축 월드방향. 회전은 assembly 가 담당.
            void PlaceEdgeAccurate()
            {
                if (edge == null) return;
                edge.gameObject.SetActive(true);
                Vector3 along = body.right;
                if (body.gameObject.activeSelf && TryGetProjectedBounds(body, along, out _, out float bodyFar)
                    && MoveProjectedNearTo(edge, along, bodyFar))
                    edge.position += _barricadeEdgeOffset;
                else
                    edge.position = body.position + along.normalized * bodyWorldLen + _barricadeEdgeOffset;
            }

            if (animateBody)
            {
                // ROLLBACK_BARRICADE_SMOOTH_RESHAPE_20260625: 길이(X) 즉시 대입 대신 DOScale 로 보간(끊김 제거).
                //   OnUpdate 에서 현재 스케일 비율로 Tiling Y(#2 패턴 유지)·엣지 위치를 따라가게, OnComplete 에 정확 마감.
                if (edge != null) edge.gameObject.SetActive(true);
                body.DOKill();
                if (visibleBodyActive)
                    body.localScale = new Vector3(visibleBodyScale.x, baseScale.y, baseScale.z);
                PlaceEdgeAccurate();
                body.DOScale(new Vector3(targetScaleX, baseScale.y, baseScale.z), BARRICADE_RESHAPE_DUR)
                    .SetEase(Ease.OutQuad)
                    .OnUpdate(() =>
                    {
                        float r = baseScale.x > 0.0001f ? body.localScale.x / baseScale.x : 1f;
                        SetBarricadeBodyTileY(body, r);
                        PlaceEdgeAccurate();
                    })
                    .OnComplete(() =>
                    {
                        if (hasBody)
                        {
                            PlaceEdgeAccurate();
                        }
                        else
                        {
                            body.gameObject.SetActive(false);
                            if (edge != null) edge.gameObject.SetActive(false);
                        }
                    });
            }
            else
            {
                if (hasBody)
                {
                    body.DOKill();
                    // 길이(X)만. 두께(Z)는 baseScale 유지(균일 obj 스케일로 프리팹 비율 그대로).
                    body.localScale = new Vector3(targetScaleX, baseScale.y, baseScale.z);
                    SetBarricadeBodyTileY(body, bodyWorldLen / baseLen); // #2 패턴 유지
                }
                PlaceEdgeAccurate();
            }
        }

        // ROLLBACK_BARRICADE_TILING_20260625: body 길이(스케일) 비율만큼 Tiling Y 조절 → 패턴 밀도 유지.
        //   공유 머티리얼 안 건드리고 MaterialPropertyBlock 으로 per-instance 적용. baseST(원본 Tiling/Offset)는 1회 캡처.
        private void SetBarricadeBodyTileY(Transform body, float ratio)
        {
            if (body == null) return;
            var rend = body.GetComponent<Renderer>();
            if (rend == null) return;
            if (!_barricadeBodyBaseTileST.TryGetValue(body, out Vector4 baseST))
            {
                var m = rend.sharedMaterial;
                Vector2 sc = m != null ? m.GetTextureScale("_BaseMap") : Vector2.one;
                Vector2 of = m != null ? m.GetTextureOffset("_BaseMap") : Vector2.zero;
                baseST = new Vector4(sc.x, sc.y, of.x, of.y);
                _barricadeBodyBaseTileST[body] = baseST;
            }
            if (_barricadeBodyMpb == null) _barricadeBodyMpb = new MaterialPropertyBlock();
            rend.GetPropertyBlock(_barricadeBodyMpb);
            _barricadeBodyMpb.SetVector("_BaseMap_ST", new Vector4(baseST.x, baseST.y * Mathf.Max(0.0001f, ratio), baseST.z, baseST.w));
            rend.SetPropertyBlock(_barricadeBodyMpb);
        }

        private void RestoreBarricadeAuthoredPartState(Transform body, Transform edge)
        {
            RestoreBarricadePartState(body, _barricadeBodyBasePositions, _barricadeBodyBaseRotations, _barricadeBodyBaseScales);
            RestoreBarricadePartState(edge, _barricadeEdgeBasePositions, _barricadeEdgeBaseRotations, _barricadeEdgeBaseScales);
        }

        private static void RestoreBarricadePartState(
            Transform part,
            Dictionary<Transform, Vector3> positionCache,
            Dictionary<Transform, Quaternion> rotationCache,
            Dictionary<Transform, Vector3> scaleCache)
        {
            if (part == null) return;

            if (!positionCache.TryGetValue(part, out Vector3 basePosition))
            {
                basePosition = part.localPosition;
                positionCache[part] = basePosition;
            }

            if (!rotationCache.TryGetValue(part, out Quaternion baseRotation))
            {
                baseRotation = part.localRotation;
                rotationCache[part] = baseRotation;
            }

            if (!scaleCache.TryGetValue(part, out Vector3 baseScale))
            {
                baseScale = part.localScale;
                scaleCache[part] = baseScale;
            }

            part.localPosition = basePosition;
            part.localRotation = baseRotation;
            part.localScale = baseScale;
            if (!part.gameObject.activeSelf)
                part.gameObject.SetActive(true);
        }

        private Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName)) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName) return child;

                Transform nested = FindChildRecursive(child, childName);
                if (nested != null) return nested;
            }
            return null;
        }

        private Transform FindChildRecursiveContains(Transform root, string namePart)
        {
            if (root == null || string.IsNullOrEmpty(namePart)) return null;
            string needle = namePart.ToLowerInvariant();
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (!string.IsNullOrEmpty(child.name) && child.name.ToLowerInvariant().Contains(needle))
                    return child;

                Transform nested = FindChildRecursiveContains(child, namePart);
                if (nested != null) return nested;
            }
            return null;
        }

        private Transform FindFirstRenderableChild(Transform root)
        {
            if (root == null) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.GetComponent<Renderer>() != null) return child;

                Transform nested = FindFirstRenderableChild(child);
                if (nested != null) return nested;
            }
            return null;
        }

        private float MeasureRendererLength(Transform root, bool vertical)
        {
            if (!TryMeasureRendererBounds(root, out Bounds bounds)) return 0f;
            return vertical ? bounds.size.z : bounds.size.x;
        }

        // ROLLBACK_BARRICADE_VISUAL_JOIN_20260608:
        // Visual-only projected bounds helpers for chaining Barricade Head -> Body -> Edge without
        // assuming prefab pivots or authored mesh lengths match board cells exactly.
        private bool TryGetProjectedBounds(Transform root, Vector3 axis, out float near, out float far)
        {
            near = 0f;
            far = 0f;
            if (axis.sqrMagnitude < 0.0001f || !TryMeasureRendererBounds(root, out Bounds bounds))
                return false;

            Vector3 n = axis.normalized;
            float center = Vector3.Dot(bounds.center, n);
            float extent =
                Mathf.Abs(n.x) * bounds.extents.x +
                Mathf.Abs(n.y) * bounds.extents.y +
                Mathf.Abs(n.z) * bounds.extents.z;
            near = center - extent;
            far = center + extent;
            return true;
        }

        private bool MoveProjectedNearTo(Transform root, Vector3 axis, float targetNear)
        {
            if (root == null || axis.sqrMagnitude < 0.0001f) return false;
            Vector3 n = axis.normalized;
            if (!TryGetProjectedBounds(root, n, out float near, out _)) return false;
            root.position += n * (targetNear - near);
            return true;
        }

        // ROLLBACK_BARRICADE_THICKNESS_FIT_20260608:
        //   조각(head/body/edge)의 두께(perpendicular, 로컬 Z=forward)를 worldTarget(=2칸×cellPerp)에 맞춤.
        //   현재 렌더 두께를 실측 → localScale.z 비례 보정(수렴형). 길이축(로컬 X)은 안 건드림.
        private void FitThicknessToWorld(Transform t, float worldTarget)
        {
            if (t == null || worldTarget <= 0.0001f) return;
            Vector3 axis = t.forward; // 로컬 +Z = 두께 방향 (조각은 로컬 +X 로 뻗음)
            if (!TryGetProjectedBounds(t, axis, out float near, out float far)) return;
            float ext = Mathf.Max(0.0001f, far - near);
            Vector3 ls = t.localScale;
            ls.z *= worldTarget / ext;
            t.localScale = ls;
        }

        private bool TryMeasureRendererBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            if (root == null) return false;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return false;

            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                if (!hasBounds)
                {
                    bounds = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            return hasBounds;
        }

        // ROLLBACK_SIZED_FIELD_BOUNDS_FIT_20260608:
        // Visual-only bounds fitting for 1x1-authored prefabs stretched to multi-cell footprints.
        // Excludes TMP and shadow renderers so labels/shadows do not shrink the actual obstacle art.
        private void FitRendererBoundsToFootprint(GameObject obj, Vector3 targetCenter, float targetSizeX, float targetSizeZ)
        {
            if (obj == null) return;
            if (!TryMeasureVisualRendererBounds(obj.transform, out Bounds bounds)) return;

            Vector3 localScale = obj.transform.localScale;
            if (bounds.size.x > 0.0001f)
                localScale.x *= Mathf.Max(0.0001f, targetSizeX) / bounds.size.x;
            if (bounds.size.z > 0.0001f)
                localScale.z *= Mathf.Max(0.0001f, targetSizeZ) / bounds.size.z;
            obj.transform.localScale = localScale;

            if (TryMeasureVisualRendererBounds(obj.transform, out Bounds fitted))
            {
                Vector3 delta = new Vector3(
                    targetCenter.x - fitted.center.x,
                    0f,
                    targetCenter.z - fitted.center.z);
                obj.transform.position += delta;
            }
        }

        private bool TryMeasureVisualRendererBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            if (root == null) return false;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return false;

            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r.GetComponent<TMP_Text>() != null) continue;
                if (IsShadowRenderer(r)) continue;

                if (!hasBounds)
                {
                    bounds = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            return hasBounds;
        }

        private static bool IsShadowRenderer(Renderer renderer)
        {
            if (renderer == null) return false;
            Transform t = renderer.transform;
            while (t != null)
            {
                if (t.name.IndexOf("Shadow", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                t = t.parent;
            }
            return false;
        }

        private static readonly Color HIDDEN_COLOR = new Color(0.45f, 0.45f, 0.50f);   // Grey mystery balloon
        private static readonly Color ICE_COLOR = new Color(0.65f, 0.85f, 0.95f);      // Frozen blue tint
        private static readonly Color FROZEN_DART_COLOR = new Color(0.50f, 0.70f, 0.90f); // Darker frozen tint (distinct from Ice)
        private static readonly Color WALL_COLOR = new Color(0.35f, 0.35f, 0.38f);     // Dark grey stone wall
        private static readonly Color PIN_COLOR = new Color(0.70f, 0.50f, 0.20f);      // Brown wooden pin
        private static readonly Color CURTAIN_COLOR = new Color(0.85f, 0.55f, 0.85f);  // Purple curtain tint
        private static readonly Color PINATA_COLOR = new Color(0.95f, 0.70f, 0.20f);   // Gold pinata

        private void ApplyInitialHiddenState()
        {
            // 머테리얼 우선순위: (1) BalloonIdentifier 프리팹 할당 → (2) BalloonController 할당/Resources
            Material fallbackMat = GetHiddenMaterialFallback();

            foreach (int id in _hiddenBalloons)
            {
                if (!_balloonObjects.TryGetValue(id, out GameObject obj) || obj == null) continue;

                BalloonIdentifier bi = obj.GetComponent<BalloonIdentifier>();
                if (bi != null && bi.HasColorRenderers && (bi.HasHiddenMaterial || fallbackMat != null))
                {
                    // 프리팹 자체 할당된 머테리얼 우선, 없으면 fallback 전달
                    bi.ApplyHiddenMaterial(bi.HasHiddenMaterial ? null : fallbackMat);
                }
                else
                {
                    // 최종 폴백: 기존 grey tint
                    ApplyTintToObject(obj, HIDDEN_COLOR);
                }
            }
        }

        private Material GetHiddenMaterialFallback()
        {
            if (_balloonHiddenMaterial != null) return _balloonHiddenMaterial;
            // Resources 폴백 로드 (Assets/Resources/BalloonHidden.mat 에 복사 시 자동 사용)
            _balloonHiddenMaterial = Resources.Load<Material>("BalloonHidden");
            return _balloonHiddenMaterial;
        }

        /// <summary>
        /// Applies the Ice visual tint (frozen blue) to Ice gimmick balloons.
        /// Called during setup for any balloon with GimmickIce type.
        /// Also overlays FrozenLayer prefab as a child for visual ice shell.
        /// </summary>
        private void ApplyInitialIceState()
        {
            // [#13/§11] Ice 영역을 인접 연결 성분으로 묶고, 영역의 blockSize(2=2×2 등) 로 타일링 렌더.
            // blockSize<=1 이면 셀당 오버레이(기본·하위호환). >1 이면 블록당 1개 오버레이로 병합 렌더.
            var regions = GetIceRegions();
            if (regions.Count == 0) return;

            for (int r = 0; r < regions.Count; r++)
            {
                var region = regions[r];
                // ROLLBACK_ICE_OVERLAY_UNDER_BALLOONS_20260626:
                // 2x2/3x3 Ice keeps 4/9 real balloon cells, but the region renders as one
                // FrozenLayer block until the shared Ice HP breaks.
                int blockSize = 1;
                for (int i = 0; i < region.Count; i++)
                {
                    if (!_balloons.TryGetValue(region[i], out BalloonData d)) continue;
                    blockSize = Mathf.Max(blockSize, d.iceBlockSize, d.sizeW, d.sizeH);
                }
                if (blockSize > 1)
                {
                    float cellSize = _cellSpacing > 0.0001f ? _cellSpacing : 0.55f;
                    RenderIceRegionBlocks(region, blockSize, cellSize, cellSize);
                    continue;
                }

                // ROLLBACK_ICE_CELL_BRUSH_20260609:
                // Ice brush size paints multiple real 1x1 Ice cells. Render every authored cell.
                {
                    // 셀당 오버레이 (기본)
                    for (int i = 0; i < region.Count; i++)
                    {
                        int id = region[i];
                        if (_balloonObjects.TryGetValue(id, out GameObject obj) && obj != null)
                        {
                            ApplyTintToObject(obj, ICE_COLOR);
                            AttachFrozenOverlay(id, obj);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// [#13/§11] 한 Ice 영역을 blockSize×blockSize 블록으로 분할해 블록당 FrozenLayer 1개를 부착(병합 렌더).
        /// 블록 내 모든 셀 본체는 숨김, 앵커(블록 내 최소 col,row 셀)에 blockSize 배율 오버레이를 블록 중앙으로 오프셋해 부착.
        /// 그리드 좌표는 월드 위치를 셀 크기로 스냅해 산출. (월드 축 정렬 가정 — 시각 오프셋/스케일은 Editor 미세조정 대상)
        /// </summary>
        private void RenderIceRegionBlocks(List<int> region, int blockSize, float cellSizeX, float cellSizeZ)
        {
            float invX = 1f / Mathf.Max(0.0001f, cellSizeX);
            float invZ = 1f / Mathf.Max(0.0001f, cellSizeZ);

            float minX = float.MaxValue, minZ = float.MaxValue;
            for (int i = 0; i < region.Count; i++)
                if (_balloons.TryGetValue(region[i], out BalloonData d))
                {
                    if (d.position.x < minX) minX = d.position.x;
                    if (d.position.z < minZ) minZ = d.position.z;
                }

            var cellCol = new Dictionary<int, int>();
            var cellRow = new Dictionary<int, int>();
            var blocks  = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < region.Count; i++)
            {
                int id = region[i];
                if (!_balloons.TryGetValue(id, out BalloonData d)) continue;
                int col = Mathf.RoundToInt((d.position.x - minX) * invX);
                int row = Mathf.RoundToInt((d.position.z - minZ) * invZ);
                cellCol[id] = col;
                cellRow[id] = row;
                var key = (col / blockSize, row / blockSize);
                if (!blocks.TryGetValue(key, out var list)) { list = new List<int>(); blocks[key] = list; }
                list.Add(id);
            }

            foreach (var kv in blocks)
            {
                var cells = kv.Value;
                // 앵커 = 블록 내 (row, col) 최소 셀
                int anchorId = -1, best = int.MaxValue;
                for (int i = 0; i < cells.Count; i++)
                {
                    int id = cells[i];
                    int rank = cellRow[id] * 100000 + cellCol[id];
                    if (rank < best) { best = rank; anchorId = id; }
                }
                if (anchorId < 0) continue;

                // ROLLBACK_ICE_OVERLAY_WORLD_BOUNDS_CENTER_20260626:
                // Ice is one merged visual over real underlying balloons, so place the overlay from
                // the measured world bounds of those balloons instead of guessing from the click anchor.

                // 블록 내 모든 셀 본체 숨김 (얼음 블록만 보이게)
                for (int i = 0; i < cells.Count; i++)
                {
                    if (_balloonObjects.TryGetValue(cells[i], out GameObject cobj) && cobj != null)
                    {
                        var cbi = cobj.GetComponent<BalloonIdentifier>();
                        if (cbi != null) cbi.SetVisible(false);
                    }
                }

                // 앵커에 blockSize 배율 오버레이 — 블록 중앙으로 오프셋
                if (_balloonObjects.TryGetValue(anchorId, out GameObject aobj) && aobj != null)
                {
                    // ROLLBACK_ICE_OVERLAY_WORLD_BOUNDS_CENTER_20260626:
                    // Place merged Ice from the real world bounds of its underlying balloon cells.
                    float minBlockX = float.MaxValue;
                    float maxBlockX = float.MinValue;
                    float minBlockZ = float.MaxValue;
                    float maxBlockZ = float.MinValue;
                    float sumBlockY = 0f;
                    int measuredCells = 0;
                    Vector3 sumCellScale = Vector3.zero;
                    int measuredScaleCells = 0;
                    for (int j = 0; j < cells.Count; j++)
                    {
                        int cid = cells[j];
                        if (!TryGetBalloonWorldPosition(cid, out Vector3 cellWorldPosition)) continue;
                        minBlockX = Mathf.Min(minBlockX, cellWorldPosition.x);
                        maxBlockX = Mathf.Max(maxBlockX, cellWorldPosition.x);
                        minBlockZ = Mathf.Min(minBlockZ, cellWorldPosition.z);
                        maxBlockZ = Mathf.Max(maxBlockZ, cellWorldPosition.z);
                        sumBlockY += cellWorldPosition.y;
                        measuredCells++;
                        if (_balloonObjects.TryGetValue(cid, out GameObject cellObj) && cellObj != null)
                        {
                            sumCellScale += cellObj.transform.lossyScale;
                            measuredScaleCells++;
                        }
                    }
                    if (measuredCells <= 0) continue;

                    Vector3 targetCenter = new Vector3(
                        (minBlockX + maxBlockX) * 0.5f,
                        sumBlockY / measuredCells,
                        (minBlockZ + maxBlockZ) * 0.5f);
                    Vector3 baseCellScale = measuredScaleCells > 0 ? sumCellScale / measuredScaleCells : Vector3.one;
                    AttachIceBlockOverlay(anchorId, targetCenter, blockSize, baseCellScale);
                }
            }
        }

        /// <summary>
        /// [#13/§11] Ice 블록 앵커에 blockSize 배율 FrozenLayer 오버레이 부착 (블록 중앙 오프셋). 본체 숨김은 호출 측이 처리.
        /// </summary>
        private void AttachIceBlockOverlay(int anchorId, Vector3 targetCenter, int blockSize, Vector3 baseCellScale)
        {
            if (!ObjectPoolManager.HasInstance) return;
            if (_frozenOverlays.ContainsKey(anchorId)) return;
            if (!ObjectPoolManager.Instance.HasPool(FrozenLayerPoolKey)) return;

            GameObject overlay = ObjectPoolManager.Instance.Get(FrozenLayerPoolKey);
            if (overlay == null) return;

            ResetFrozenOverlayMagazineText(overlay);

            // ROLLBACK_ICE_OVERLAY_STANDALONE_FIELD_20260626:
            // Merged 2x2/3x3 Ice is a field overlay, not a child of one hidden balloon.
            // Keeping it outside the balloon hierarchy prevents parent scale/visibility from moving it.
            overlay.transform.SetParent(null, false);
            overlay.transform.rotation = Quaternion.identity;

            // 스케일: 1×1 은 FROZEN_OVERLAY_SCALE(여백 포함) 그대로. 블록은 셀 수만큼 확장하되 여백은 고정 →
            // (blockSize-1) + FROZEN_OVERLAY_SCALE. (1.3*B 는 여백이 B 배로 과대해져 footprint 를 벗어남)

            // 위치 보정(Wall 패턴): 앵커=블록 코너 셀 → footprint 중앙으로 이동. localPosition 은 부모(_balloonScale)
            // 스케일에 곱해져 어긋나므로 월드 위치로 직접 설정 (offsetX/Z 는 이미 월드 단위 = (B-1)*0.5*cellSize).
            overlay.transform.position = targetCenter;
            // ROLLBACK_ICE_OVERLAY_DIRECT_BLOCK_SCALE_20260626:
            // One FrozenLayer sits at the block center. X/Z scale by the block size (2x2/3x3),
            // while Y keeps the visible balloon height scale instead of inheriting a balloon parent.
            overlay.transform.localScale = GetFrozenOverlayWorldScale(baseCellScale, blockSize);
            overlay.SetActive(true);

            // ROLLBACK_ICE_OVERLAY_XZ_ONLY_SCALE_20260626:
            // Ice block size is a board-footprint size, not height. A 3x3 Ice must cover X/Z 3x3
            // cells while keeping the original 1x1 FrozenLayer height/thickness on Y.

            // ROLLBACK_ICE_BLOCK_FOOTPRINT_FIT_20260608:
            // FrozenLayer is authored as a 1x1 visual shell, but its renderer bounds are not
            // guaranteed to equal one board cell. Fit x/z bounds to the block footprint so
            // 2x2/3x3 Ice covers the same area as the hidden balloons beneath it.

            _frozenOverlays[anchorId] = overlay;
        }

        /// <summary>
        /// Applies Frozen Dart visual tint. Darker blue than Ice to distinguish.
        /// Called during setup for balloons with GimmickFrozenDart type.
        /// Also overlays FrozenLayer prefab as a child for visual frost shell.
        /// </summary>
        private void ApplyInitialFrozenDartState()
        {
            foreach (BalloonData d in _balloons.Values)
            {
                if (d.isPopped || d.gimmickType != GimmickFrozenDart) continue;
                if (_balloonObjects.TryGetValue(d.balloonId, out GameObject obj) && obj != null)
                {
                    ApplyTintToObject(obj, FROZEN_DART_COLOR);
                    AttachFrozenOverlay(d.balloonId, obj);
                }
            }
        }

        private void ApplyInitialColorCurtainState()
        {
            foreach (BalloonData d in _balloons.Values)
            {
                if (d.isPopped || d.gimmickType != GimmickColorCurtain) continue;
                if (_balloonObjects.TryGetValue(d.balloonId, out GameObject obj) && obj != null)
                {
                    ApplyTintToObject(obj, CURTAIN_COLOR);
                }
            }
        }

        /// <summary>
        /// 색상 변주: 기본색에서 3가지 톤 (기본, 진한, 연한) 중 랜덤 선택.
        /// 머티리얼은 색상별 캐시되므로 동일 톤끼리 배칭 가능.
        /// </summary>
        private const int VARIATION_COUNT = 3; // 기본, 진한, 연한

        public static Color GetVariedColor(int colorIndex)
        {
            Color baseColor = BalloonColors[Mathf.Clamp(colorIndex, 0, BalloonColors.Length - 1)];

            int variant = Random.Range(0, VARIATION_COUNT);
            switch (variant)
            {
                // ROLLBACK_BALLOON_COLOR_VARIATION_HALF_20260609: 변주 범위 절반(체감 5~8% → 2~4%).
                //   진한: S+0.03→+0.015, V-0.03→-0.015 / 연한: S-0.04→-0.02, V+0.03→+0.015. 롤백 시 원래 델타 복원.
                case 1: // 진한 톤 (미세 변주)
                    Color.RGBToHSV(baseColor, out float h1, out float s1, out float v1);
                    // 그레이스케일(s≈0)은 saturation 변주 생략 (h=0 + s>0 = 빨강 끼어들음)
                    float newS1 = s1 < 0.01f ? 0f : Mathf.Min(s1 + 0.015f, 1f);
                    return Color.HSVToRGB(h1, newS1, Mathf.Max(v1 - 0.015f, 0.2f));
                case 2: // 연한 톤 (미세 변주)
                    Color.RGBToHSV(baseColor, out float h2, out float s2, out float v2);
                    float newS2 = s2 < 0.01f ? 0f : Mathf.Max(s2 - 0.02f, 0.1f);
                    return Color.HSVToRGB(h2, newS2, Mathf.Min(v2 + 0.015f, 1f));
                default: // 기본 톤
                    return baseColor;
            }
        }

        /// <summary>색상별 공유 Material 캐시. sharedMaterial 할당 → SRP Batcher 배칭 유지.</summary>
        private static readonly Dictionary<Color, Material> _sharedColorMats = new Dictionary<Color, Material>();
        private static Shader _cachedLitShader;

        public static Material GetOrCreateSharedMaterial(Color color)
        {
            if (_sharedColorMats.TryGetValue(color, out Material mat))
                return mat;

            if (_cachedLitShader == null)
                _cachedLitShader = Shader.Find("Custom/ItemShared")
                    ?? Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard");

            if (_cachedLitShader == null)
            {
                Debug.LogError("[BalloonController] No shader found for balloon material!");
                return null;
            }

            mat = new Material(_cachedLitShader);
            mat.SetColor("_BaseColor", color);
            // [Optimization 2026-05-10 revert] GPU Instancing 채택 — 같은 색 풍선들 1 draw 로 묶임.
            mat.enableInstancing = true;
            _sharedColorMats[color] = mat;
            return mat;
        }

        /// <summary>아웃라인 ON/OFF 설정 (검은색=활성, 흰색=비활성)</summary>
        public static void SetOutline(GameObject obj, bool active, Color outlineColor)
        {
            Renderer r = obj.GetComponent<Renderer>();
            if (r == null) return;

            // MaterialPropertyBlock으로 per-object 아웃라인 제어 (SRP Batcher는 Unlit이라 영향 없음)
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetFloat("_OutlineEnabled", active ? 1f : 0f);
            mpb.SetColor("_OutlineColor", outlineColor);
            r.SetPropertyBlock(mpb);
        }

        /// <summary>Set outline on ALL non-popped balloons.</summary>
        public void SetAllOutlines(bool active, Color outlineColor)
        {
            foreach (var kvp in _balloonObjects)
            {
                if (kvp.Value == null) continue;
                if (_balloons.TryGetValue(kvp.Key, out BalloonData data) && data.isPopped) continue;
                SetOutline(kvp.Value, active, outlineColor);
            }
        }

        /// <summary>Set outline only on balloons of a specific color.</summary>
        public void SetOutlineByColor(int color, bool active, Color outlineColor)
        {
            foreach (var kvp in _balloonObjects)
            {
                if (kvp.Value == null) continue;
                if (!_balloons.TryGetValue(kvp.Key, out BalloonData data)) continue;
                if (data.isPopped) continue;
                if (data.color == color)
                    SetOutline(kvp.Value, active, outlineColor);
            }
        }

        /// <summary>
        /// 화면 클릭 위치에서 가장 가까운 풍선 ID 반환. Collider 없이 동작.
        /// 월드 좌표 XZ 거리 기반. threshold 이내만 반환, 없으면 -1.
        /// </summary>
        public int FindNearestBalloonAtWorldPos(Vector3 worldPos, float threshold = 1f)
        {
            int bestId = -1;
            float bestDist = threshold * threshold; // sqr 비교

            foreach (var kvp in _balloons)
            {
                if (kvp.Value.isPopped) continue;
                float dx = kvp.Value.position.x - worldPos.x;
                float dz = kvp.Value.position.z - worldPos.z;
                float sqrDist = dx * dx + dz * dz;
                if (sqrDist < bestDist)
                {
                    bestDist = sqrDist;
                    bestId = kvp.Key;
                }
            }
            return bestId;
        }

        /// <summary>
        /// Finds the nearest active balloon to a screen point using the rendered transform position.
        /// Used by Zap item picking so camera tilt and runtime field scaling do not shift selection.
        /// </summary>
        public int FindNearestBalloonAtScreenPoint(Camera camera, Vector2 screenPosition, float thresholdPixels = 0f)
        {
            if (camera == null) return -1;

            int bestId = -1;
            float baseThreshold = thresholdPixels > 0f
                ? thresholdPixels
                : EstimateBalloonPickRadiusPixels(camera);
            float bestDist = baseThreshold * baseThreshold;

            foreach (var kvp in _balloons)
            {
                BalloonData data = kvp.Value;
                if (data == null || data.isPopped) continue;

                Vector3 world = GetBalloonWorldPosition(data.balloonId);
                Vector3 screen = camera.WorldToScreenPoint(world);
                if (screen.z < camera.nearClipPlane || screen.z > camera.farClipPlane) continue;

                float candidateRadius = baseThreshold;
                if (IsSizedFieldGimmick(data.gimmickType))
                    candidateRadius *= Mathf.Max(1f, Mathf.Max(data.sizeW, data.sizeH) * 0.75f);

                float dx = screen.x - screenPosition.x;
                float dy = screen.y - screenPosition.y;
                float sqrDist = dx * dx + dy * dy;
                float allowed = candidateRadius * candidateRadius;
                if (sqrDist < bestDist && sqrDist <= allowed)
                {
                    bestDist = sqrDist;
                    bestId = kvp.Key;
                }
            }

            return bestId;
        }

        private float EstimateBalloonPickRadiusPixels(Camera camera)
        {
            const float fallback = 72f;
            if (camera == null || !GameManager.HasInstance)
                return fallback;

            float cellSpacing = Mathf.Max(0.1f, GameManager.Instance.Board.cellSpacing);
            Vector3 origin = new Vector3(
                GameManager.Instance.Board.boardCenterX,
                0.1f,
                GameManager.Instance.Board.balloonCenterZ);

            Vector3 center = camera.WorldToScreenPoint(origin);
            Vector3 right = camera.WorldToScreenPoint(origin + Vector3.right * cellSpacing);
            Vector3 forward = camera.WorldToScreenPoint(origin + Vector3.forward * cellSpacing);

            float pxPerCell = Mathf.Max(
                Vector2.Distance(new Vector2(center.x, center.y), new Vector2(right.x, right.y)),
                Vector2.Distance(new Vector2(center.x, center.y), new Vector2(forward.x, forward.y)));

            if (pxPerCell <= 1f) return fallback;
            return Mathf.Clamp(pxPerCell * 0.65f, 42f, 110f);
        }

        /// <summary>Clear all outlines on all balloons.</summary>
        public void ClearAllOutlines()
        {
            foreach (var kvp in _balloonObjects)
            {
                if (kvp.Value == null) continue;
                SetOutline(kvp.Value, false, Color.black);
            }
        }

        /// <summary>프리팹 고유 컴포넌트(Shadow, Particle 등) 건드리지 않고 색상만 적용.
        /// tag "BalloonMesh"가 있는 Renderer만 변경. 없으면 루트 Renderer만.</summary>
        private static void ApplyTintToObject(GameObject obj, Color color)
        {
            // BalloonIdentifier에 Renderer + 기반 Material이 할당되어 있으면 복제 방식
            BalloonIdentifier bi = obj.GetComponent<BalloonIdentifier>();
            if (bi != null && bi.HasColorRenderers)
            {
                bi.ApplyColor(color);
                return;
            }

            // fallback: 기존 방식
            Material shared = GetOrCreateSharedMaterial(color);
            if (shared == null) return;

            Renderer r = obj.GetComponent<Renderer>();
            if (r != null)
            {
                r.enabled = true;
                r.sharedMaterial = shared;
                return;
            }

            MeshRenderer mr = obj.GetComponentInChildren<MeshRenderer>();
            if (mr != null)
            {
                mr.enabled = true;
                mr.sharedMaterial = shared;
            }
        }

        private static void ApplyTintToRenderersInChildren(GameObject obj, Color color)
        {
            if (obj == null) return;
            Material shared = GetOrCreateSharedMaterial(color);
            if (shared == null) return;

            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                ApplyTintToObject(obj, color);
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                renderer.enabled = true;
                renderer.sharedMaterial = shared;
            }
        }

        /// <summary>
        /// 기믹 타입에 따라 사용할 풀 키를 반환. 전용 비주얼 프리팹이 없는 기믹은
        /// 기본 Balloon 풀로 폴백. (Lock_Key는 별도 처리이므로 여기서 다루지 않음)
        /// </summary>
        private static string ResolveGimmickPoolKey(string gimmickType)
        {
            string normalized = GimmickDisplayName.Normalize(gimmickType);
            if (string.IsNullOrEmpty(normalized)) return PoolKey;
            switch (normalized)
            {
                case GimmickBarricade: return BarricadePoolKey;
                // ROLLBACK_PINATA_WOODENBOARD_PREFAB:
                // The current Pinata visual is authored in Resources/Prefabs/WoodenBoard
                // and addressed as prefab_WoodenBoard.
                case GimmickPinata:    return WoodenBoardPoolKey;
                case GimmickPinataBox: return TargetBoxPoolKey;
                case GimmickPin:       return WoodenBoardPoolKey;
                // ROLLBACK_WALL_IRONBOX_PREFAB:
                // NewFeature maps Wall/IronWall to IronBox; use the same runtime prefab.
                case GimmickWall:      return IronBoxPoolKey;
                default:               return PoolKey;
            }
        }

        // FrozenLayer 오버레이 추적: balloonId → 자식으로 부착된 FrozenLayer GameObject
        private readonly Dictionary<int, GameObject> _frozenOverlays = new Dictionary<int, GameObject>();

        /// <summary>풍선보다 커 보이게 하는 오버레이 스케일 배율 (얼음 쉘이 풍선을 감싼 모습).
        /// 명세: "FrozenLayer 가 풍선보다 커보여야함."</summary>
        private const float FROZEN_OVERLAY_SCALE = 1.3f;

        private static Vector3 GetFrozenOverlayWorldScale(Vector3 balloonWorldScale, int blockSize)
        {
            int size = Mathf.Max(1, blockSize);
            float x = Mathf.Max(0.0001f, Mathf.Abs(balloonWorldScale.x));
            float y = Mathf.Max(0.0001f, Mathf.Abs(balloonWorldScale.y));
            float z = Mathf.Max(0.0001f, Mathf.Abs(balloonWorldScale.z));
            return new Vector3(x * FROZEN_OVERLAY_SCALE * size, y, z * FROZEN_OVERLAY_SCALE * size);
        }

        /// <summary>
        /// FrozenLayer 프리팹을 풍선의 자식으로 부착 (얼음 쉘 비주얼).
        /// Ice (Lv.201) / Frozen_Dart (Lv.241) 기믹용. 풍선 자체는 교체하지 않고 오버레이만 추가.
        /// 부착 시 풍선 본체 비주얼 숨김 → 얼음만 보임. 해동 시 ReturnFrozenOverlay 로 제거 →
        /// 원본 풍선 노출.
        /// </summary>
        private void AttachFrozenOverlay(int balloonId, GameObject parentBalloon)
        {
            if (parentBalloon == null) return;
            if (!ObjectPoolManager.HasInstance) return;
            if (_frozenOverlays.ContainsKey(balloonId)) return; // 중복 부착 방지
            if (!ObjectPoolManager.Instance.HasPool(FrozenLayerPoolKey)) return;

            GameObject overlay = ObjectPoolManager.Instance.Get(FrozenLayerPoolKey);
            if (overlay == null) return;

            ResetFrozenOverlayMagazineText(overlay);

            // ROLLBACK_FROZEN_OVERLAY_STANDALONE_FIELD_20260626:
            // Do not put FrozenLayer under Balloon/Balloon_Pooled_*.
            // It is a field overlay that covers the balloon from world space; parenting it to the
            // balloon makes pooled hierarchy/balloon scale move the ice to the wrong place.
            overlay.transform.SetParent(null, false);
            overlay.transform.position = parentBalloon.transform.position;
            overlay.transform.rotation = Quaternion.identity;
            // 풍선보다 크게 — 얼음이 풍선을 감싼 시각.
            // ROLLBACK_FROZEN_OVERLAY_DIRECT_SCALE_20260626:
            // Standalone FrozenLayer no longer inherits the balloon scale, so apply the same world
            // scale explicitly: X/Z use the ice margin, Y remains the balloon height.
            overlay.transform.localScale = GetFrozenOverlayWorldScale(parentBalloon.transform.lossyScale, 1);
            overlay.SetActive(true);

            // 풍선 본체 숨김 (얼음만 보이게).
            var bi = parentBalloon.GetComponent<BalloonIdentifier>();
            if (bi != null) bi.SetVisible(false);

            _frozenOverlays[balloonId] = overlay;
        }

        // ROLLBACK_ICE_MAGAZINE_TEXT_20260608:
        // Grouped Ice HP now uses the MagazineText child already authored inside FrozenLayer.prefab.
        // Remove these helpers and restore GimmickProcessor's legacy TextMesh label path to roll back.
        // ROLLBACK_ICE_HP_TEXT_OFFSET_20260626:
        // Keep the grouped Ice HP label at the authored local offset inside FrozenLayer/MagazineText.
        // Roll back by restoring the previous world-center placement path.
        private static readonly Vector3 ICE_HP_TEXT_LOCAL_POSITION = new Vector3(0f, 1.5f, 0.05f);

        public void SetIceRegionHpText(IEnumerable<int> ids, int hp)
        {
            if (ids == null) return;

            Vector3 center = Vector3.zero;
            int count = 0;
            foreach (int id in ids)
            {
                if (TryGetBalloonWorldPosition(id, out Vector3 pos))
                {
                    center += pos;
                    count++;
                }
            }
            if (count <= 0) return;

            center /= count;

            GameObject selectedOverlay = null;
            float selectedDistance = float.MaxValue;
            foreach (int id in ids)
            {
                if (!_frozenOverlays.TryGetValue(id, out GameObject overlay) || overlay == null) continue;

                ResetFrozenOverlayMagazineText(overlay);
                float distance = (overlay.transform.position - center).sqrMagnitude;
                if (distance < selectedDistance)
                {
                    selectedDistance = distance;
                    selectedOverlay = overlay;
                }
            }

            if (selectedOverlay != null && hp > 0)
                ShowFrozenOverlayMagazineText(selectedOverlay, hp, center);
        }

        public void ClearIceRegionHpText(IEnumerable<int> ids)
        {
            if (ids == null) return;

            foreach (int id in ids)
            {
                if (_frozenOverlays.TryGetValue(id, out GameObject overlay) && overlay != null)
                    ResetFrozenOverlayMagazineText(overlay);
            }
        }

        private static void ShowFrozenOverlayMagazineText(GameObject overlay, int hp, Vector3 worldPosition)
        {
            FrozenOverlayMagazineTextState state = GetFrozenOverlayMagazineTextState(overlay);
            if (state == null || state.Text == null) return;

            state.RestoreDefaults();
            state.Text.text = Mathf.Max(0, hp).ToString();
            state.Text.transform.localPosition = ICE_HP_TEXT_LOCAL_POSITION;
            state.Text.gameObject.SetActive(true);
            state.Text.ForceMeshUpdate();
        }

        private static void ResetFrozenOverlayMagazineText(GameObject overlay)
        {
            FrozenOverlayMagazineTextState state = GetFrozenOverlayMagazineTextState(overlay);
            if (state == null || state.Text == null) return;

            state.Text.text = string.Empty;
            state.RestoreDefaults();
            state.Text.gameObject.SetActive(false);
        }

        private static FrozenOverlayMagazineTextState GetFrozenOverlayMagazineTextState(GameObject overlay)
        {
            if (overlay == null) return null;

            FrozenOverlayMagazineTextState state = overlay.GetComponent<FrozenOverlayMagazineTextState>();
            if (state != null && state.Text != null) return state;

            TMP_Text text = null;
            TMP_Text[] texts = overlay.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                string n = texts[i].name;
                if (n == "MagazineText" || n == "MagaineText")
                {
                    text = texts[i];
                    break;
                }
            }
            if (text == null && texts.Length > 0)
                text = texts[0];
            if (text == null) return null;

            if (state == null)
                state = overlay.AddComponent<FrozenOverlayMagazineTextState>();
            state.Capture(text);
            return state;
        }

        private sealed class FrozenOverlayMagazineTextState : MonoBehaviour
        {
            public TMP_Text Text { get; private set; }

            private Vector3 _defaultLocalPosition;
            private Quaternion _defaultLocalRotation;
            private Vector3 _defaultLocalScale;

            public void Capture(TMP_Text text)
            {
                Text = text;
                Transform t = text.transform;
                _defaultLocalPosition = t.localPosition;
                _defaultLocalRotation = t.localRotation;
                _defaultLocalScale = t.localScale;
            }

            public void RestoreDefaults()
            {
                if (Text == null) return;
                Transform t = Text.transform;
                t.localPosition = _defaultLocalPosition;
                t.localRotation = _defaultLocalRotation;
                t.localScale = _defaultLocalScale;
            }
        }

        /// <summary>
        /// 특정 풍선의 FrozenLayer 오버레이를 풀로 반환 (해동/팝/클리어 시 호출).
        /// 풍선 본체 비주얼 다시 보이게 복원.
        /// </summary>
        private void ReturnFrozenOverlayInstance(GameObject overlay)
        {
            if (overlay == null) return;
            ResetFrozenOverlayMagazineText(overlay);
            overlay.transform.SetParent(null, false);
            if (ObjectPoolManager.HasInstance)
                ObjectPoolManager.Instance.Return(FrozenLayerPoolKey, overlay);
            else
                overlay.SetActive(false);
        }

        private void ReturnFrozenOverlay(int balloonId)
        {
            if (!_frozenOverlays.TryGetValue(balloonId, out GameObject overlay)) return;
            _frozenOverlays.Remove(balloonId);

            // 풍선 본체 다시 보이게 (얼음 깨짐 → 원래 풍선 노출).
            if (_balloonObjects.TryGetValue(balloonId, out GameObject balloonObj) && balloonObj != null)
            {
                var bi = balloonObj.GetComponent<BalloonIdentifier>();
                if (bi != null) bi.SetVisible(true);
            }

            ReturnFrozenOverlayInstance(overlay);
        }

        /// <summary>모든 FrozenLayer 오버레이를 풀로 반환 (보드 클리어 시).
        /// 풍선 본체 visibility 도 복원 — 다음 풀 재사용 안전성 확보.</summary>
        private void ReturnAllFrozenOverlays()
        {
            foreach (var kvp in _frozenOverlays)
            {
                int balloonId = kvp.Key;
                GameObject overlay = kvp.Value;

                // 풍선 본체 visibility 복원
                if (_balloonObjects.TryGetValue(balloonId, out GameObject balloonObj) && balloonObj != null)
                {
                    var bi = balloonObj.GetComponent<BalloonIdentifier>();
                    if (bi != null) bi.SetVisible(true);
                }

                ReturnFrozenOverlayInstance(overlay);
            }
            _frozenOverlays.Clear();
        }

        #endregion

        #region Private Methods — Pop Logic

        private PopResult ExecutePop(BalloonData data)
        {
            float __popTotalStamp = InGamePerfLogger.StartStampMs();
            // Mark popped in data
            float __markStamp = InGamePerfLogger.StartStampMs();
            data.isPopped = true;
            _balloons[data.balloonId] = data;
            RemainingCount = Mathf.Max(0, RemainingCount - 1);
            PoppedCount++;
            InGamePerfLogger.EndSection(__markStamp, "Balloon.ExecutePop.MarkData");

            float __scaleStamp = InGamePerfLogger.StartStampMs();
            float effectScaleMultiplier = GetBalloonEffectScaleMultiplier(data.balloonId);
            InGamePerfLogger.EndSection(__scaleStamp, "Balloon.ExecutePop.GetScale");

            // Return visual to pool
            float __returnObjStamp = InGamePerfLogger.StartStampMs();
            ReturnBalloonObject(data.balloonId, effectScaleMultiplier);
            // [SHADOW_BATCH] 통합 mesh 에서 이 풍선의 그림자 쿼드 제거 (비대상이면 no-op).
            _shadowBatcher?.HideShadow(data.balloonId);
            InGamePerfLogger.EndSection(__returnObjStamp, "Balloon.ExecutePop.ReturnObject");

            // Remove from position index
            float __invalidateStamp = InGamePerfLogger.StartStampMs();
            RemovePositionIndexForBalloon(data);

            // ROLLBACK_DART_TARGET_CACHE_DIRTY:
            // DirectionalTargeting is now dirty-driven instead of frame-driven. A pop is the exact
            // moment the outer contour changes, so invalidate once here and rebuild on the next
            // targeting query instead of rebuilding every frame.
            DirectionalTargeting.InvalidateCache();
            InGamePerfLogger.EndSection(__invalidateStamp, "Balloon.ExecutePop.Invalidate");

            // Publish pop event
            float __publishStamp = InGamePerfLogger.StartStampMs();
            EventBus.Publish(new OnBalloonPopped
            {
                balloonId = data.balloonId,
                color     = data.color,
                position  = data.position,
                effectScaleMultiplier = effectScaleMultiplier
            });
            InGamePerfLogger.EndSection(__publishStamp, "Balloon.ExecutePop.PublishPop");

            // Trigger gimmick side-effects (base behavior only)
            PopResult result = new PopResult
            {
                success    = true,
                balloonId  = data.balloonId,
                color      = data.color,
                position   = data.position,
                gimmickType = data.gimmickType
            };

            float __gimmickStamp = InGamePerfLogger.StartStampMs();
            ProcessGimmickAfterPop(data, result);
            InGamePerfLogger.EndSection(__gimmickStamp, "Balloon.ExecutePop.Gimmick");

            // ROLLBACK_CONTOUR_TARGET_DIAG:
            // Diagnose whether DirectionalTargeting's frame cache still points at
            // the popped balloon or shifts contour candidates unexpectedly.
            float __diagStamp = InGamePerfLogger.StartStampMs();
            DirectionalTargeting.LogContourAfterPop(data.balloonId, data.color, data.position, data.gimmickType);
            InGamePerfLogger.EndSection(__diagStamp, "Balloon.ExecutePop.ContourDiag");

            // 외곽 변경 가능 — 외곽 풍선만 렌더링 toggle 시 Renderer state 갱신
            float __outlineStamp = InGamePerfLogger.StartStampMs();
            RefreshOutermostRendererState();
            InGamePerfLogger.EndSection(__outlineStamp, "Balloon.ExecutePop.RefreshOutline");
            InGamePerfLogger.EndSection(__popTotalStamp, "Balloon.ExecutePop.Total");

            return result;
        }

        private PopResult ProcessPinataHit(BalloonData data)
        {
            data.hitCount++;
            _balloons[data.balloonId] = data;

            // HP 텍스트 + 피격/파괴 이펙트
            // ROLLBACK_PINATA_PER_CELL_20260618: per-cell 모드면 총 hit = 점유 셀수(W×H). 아니면 기존 maxHP.
            int requiredHits = IsPinataPerCell(data)
                ? data.sizeW * data.sizeH
                : (data.maxHP > 0 ? data.maxHP : PinataRequiredHits);
            if (_balloonObjects.TryGetValue(data.balloonId, out GameObject hitObj) && hitObj != null)
            {
                int remainHP = Mathf.Max(0, requiredHits - data.hitCount);
                var gi = hitObj.GetComponent<GimmickIdentifier>();
                if (gi != null)
                {
                    gi.UpdateHP(remainHP);
                    gi.PlayHitEffect();
                    if (remainHP <= 0) gi.PlayEndEffect();
                }
            }

            if (data.hitCount < requiredHits)
            {
                // ROLLBACK_PINATA_PER_CELL_20260618: per-cell 모드는 hit 마다 셀 1개가 blocker 로 빠지므로
                //   타게팅 캐시를 무효화해 다음 스캔이 줄어든 셀 집합(idx>=hitCount)을 반영하게 한다. (egg 모델 동일.)
                if (IsPinataPerCell(data))
                    DirectionalTargeting.InvalidateCache();

                // Partial hit — not yet destroyed → 풍선 pop SFX 라우팅용 (isDestroyed=false)
                EventBus.Publish(new OnGimmickTriggered
                {
                    gimmickType  = GimmickPinata,
                    targetId     = data.balloonId,
                    isDestroyed  = false
                });

                return new PopResult
                {
                    success     = false,
                    hitAccepted = true,
                    reason      = "PinataPartialHit",
                    balloonId   = data.balloonId,
                    gimmickType = GimmickPinata
                };
            }

            // Final hit — execute full pop. 파괴 SFX(woodbreak) 라우팅용 publish (isDestroyed=true).
            EventBus.Publish(new OnGimmickTriggered
            {
                gimmickType  = GimmickPinata,
                targetId     = data.balloonId,
                isDestroyed  = true
            });
            return ExecutePop(data);
        }

        /// <summary>
        /// Barricade: destructible wall with HP.
        /// Each hit reduces HP by 1. Destroyed when HP reaches 0.
        /// While alive, blocks dart path (occupancy map). Default HP = 2.
        /// </summary>
        private PopResult ProcessBarricadeHit(BalloonData data)
        {
            data.hitCount++;
            _balloons[data.balloonId] = data;

            int requiredHits = data.maxHP > 0 ? data.maxHP : 2;

            if (_balloonObjects.TryGetValue(data.balloonId, out GameObject hitObj) && hitObj != null)
            {
                var gi = hitObj.GetComponent<GimmickIdentifier>();
                int remainHP = Mathf.Max(0, requiredHits - data.hitCount);
                if (gi != null)
                {
                    gi.UpdateHP(remainHP);
                    gi.PlayHitEffect();
                    if (remainHP <= 0) gi.PlayEndEffect();
                }

                // ROLLBACK_BARRICADE_PARTIAL_HIT_ANIMATE_20260625:
                // Barricade already has a smooth reshape path, but partial hits were calling the
                // default instant path. Animate only while still alive; the final hit keeps the
                // current full visual for the 1 -> 1.1 -> 0 destroy tween in ReturnBalloonObject.
                if (remainHP > 0)
                    ApplyBarricadeVisualTransform(hitObj, data, animate: true);
            }

            if (data.hitCount < requiredHits)
            {
                return new PopResult
                {
                    success     = false,
                    hitAccepted = true,
                    reason      = "BarricadePartialHit",
                    balloonId   = data.balloonId,
                    gimmickType = GimmickBarricade
                };
            }

            return ExecutePop(data);
        }

        /// <summary>
        /// Frozen Dart: 2-hit field gimmick.
        /// 1st hit (hitCount 0→1): thaw — removes frozen layer, converts to normal balloon.
        /// 2nd hit: standard pop.
        /// Adjacent pops also thaw (like Ice, but requires direct hit to pop afterward).
        /// </summary>
        private PopResult ProcessFrozenDartHit(BalloonData data)
        {
            data.hitCount++;
            _balloons[data.balloonId] = data;

            if (data.hitCount < 2)
            {
                // Thaw: convert to normal balloon (still alive, now poppable in 1 hit)
                data.gimmickType = GimmickNone;
                _balloons[data.balloonId] = data;

                // 얼음 쉘 오버레이 제거 (해동)
                ReturnFrozenOverlay(data.balloonId);

                // Visual: restore original color from frozen tint
                if (_balloonObjects.TryGetValue(data.balloonId, out GameObject obj) && obj != null)
                {
                    int colorIdx = Mathf.Clamp(data.color, 0, BalloonColors.Length - 1);
                    ApplyTintToObject(obj, BalloonColors[colorIdx]);
                }

                // ROLLBACK_FROZEN_THAW_TARGET_CACHE_INVALIDATION:
                // Thawing changes this cell from a frozen target to a normal target without a pop.
                DirectionalTargeting.InvalidateCache();
                RefreshOutermostRendererState();

                EventBus.Publish(new OnGimmickTriggered
                {
                    gimmickType = GimmickFrozenDart,
                    targetId    = data.balloonId
                });

                return new PopResult
                {
                    success     = false,
                    hitAccepted = true,
                    reason      = "FrozenDartThawed",
                    balloonId   = data.balloonId,
                    gimmickType = GimmickFrozenDart
                };
            }

            // 2nd hit — fully thawed, execute pop
            return ExecutePop(data);
        }

        private void ProcessGimmickAfterPop(BalloonData data, PopResult result)
        {
            // Post-pop gimmick side effects:
            // - Ice HP, Lock-Key, Surprise, Hidden → GimmickProcessor.HandleAnyBalloonPopped (EventBus)
            // - Chain, Pin, PinataBox → here (requires BalloonController internal access)
            switch (data.gimmickType)
            {
                case GimmickChain:
                    // ROLLBACK_DISABLE_FIELD_CHAIN_POP_20260602:
                    // Previous behavior chain-popped same-color adjacent balloons after a normal dart hit.
                    // Linked Dart Box now uses the same "Chain" key on holder/queue data, so field balloon
                    // pops must stay dart:balloon = 1:1 and should not trigger extra balloon pops here.
                    break;

                case GimmickPinataBox:
                    // PinataBox 는 단일 타격으로 파괴 — isDestroyed=true 로 woodbreak SFX 라우팅.
                    EventBus.Publish(new OnGimmickTriggered { gimmickType = GimmickPinataBox, targetId = data.balloonId, isDestroyed = true });
                    break;

                case GimmickLockKey:
                    // OnKeyReleased는 ReturnBalloonObject에서 발행 (KeyVisual 분리 후)
                    break;

                case GimmickNone:
                default:
                    break;
            }

            // Pin은 인접 팝으로 제거 안 됨 — 같은 색 다트 직접 타격으로만 제거
            // RemoveAdjacentPins(data.position);  // 문서 기준 비활성

            // Ice(§11): 영역 공유 HP 모델 — 어떤 풍선이든 제거 시 GimmickProcessor.HandleAnyBalloonPopped
            // (OnBalloonPopped 구독)가 영역 HP 를 깎고, HP 0 시 BalloonController.BreakAllIce 로 일괄 해제.
            // (이전 인접 팝 해동 ThawAdjacentIce 모델은 폐기 — 명세 HP 모델로 리라이트.)

            // All pops thaw adjacent Frozen Dart balloons
            ThawAdjacentFrozenDarts(data);
        }

        private void PlayRevealEffect(GameObject obj, int colorIdx, int balloonId)
        {
            if (obj == null) return;

            // ROLLBACK_FIELD_REVEAL_VISIBLE_EFFECT:
            // The previous reveal was only a tiny punch scale, so Surprise/Hidden and Ice thaw
            // could look like a silent color swap. Reuse the pooled pop particle at a smaller
            // scale and run one short pulse without changing gameplay state.
            obj.transform.DOKill();
            Vector3 baseScale = obj.transform.localScale;
            Sequence seq = DOTween.Sequence();
            seq.Append(obj.transform.DOScale(baseScale * 1.18f, 0.10f).SetEase(Ease.OutQuad));
            seq.Append(obj.transform.DOScale(baseScale, 0.12f).SetEase(Ease.OutBack));
            seq.SetLink(obj, LinkBehaviour.KillOnDisable);

            int ci = Mathf.Clamp(colorIdx, 0, BalloonColors.Length - 1);
            float effectScale = Mathf.Max(0.15f, GetBalloonEffectScaleMultiplier(balloonId) * 0.65f);
            PopEffectPool.Play(obj.transform.position, BalloonColors[ci], this, effectScale);
        }

        private void RevealAdjacentHiddenBalloons(Vector3 position)
        {
            int count = CopyAdjacentIds(GetAdjacentBalloonIds(position));
            for (int i = 0; i < count; i++)
            {
                int id = _adjCopyBuffer[i];
                if (!_hiddenBalloons.Contains(id)) continue;
                if (!_balloons.TryGetValue(id, out BalloonData neighbor)) continue;
                if (neighbor.isPopped) continue;

                _hiddenBalloons.Remove(id);

                // Restore actual balloon color (was showing grey)
                if (_balloonObjects.TryGetValue(id, out GameObject obj) && obj != null)
                {
                    int colorIdx = Mathf.Clamp(neighbor.color, 0, BalloonColors.Length - 1);
                    ApplyTintToObject(obj, BalloonColors[colorIdx]);

                    // Reveal punch animation
                    // [Leak fix 2026-05-11] SetLink(KillOnDisable) — pool 반환 시 자동 kill. 원본: 동일 코드 .SetLink 없이.
                    PlayRevealEffect(obj, colorIdx, id);
                }

                EventBus.Publish(new OnGimmickTriggered
                {
                    gimmickType = GimmickSurprise, // 인접 팝으로 필드 Surprise 공개
                    targetId    = id
                });
            }
        }

        /// <summary>BFS 큐 기반 체인 팝 (재귀 대신 → StackOverflow 방지)</summary>
        private readonly Queue<int> _chainPopQueue = new Queue<int>();
        private readonly HashSet<int> _chainPopVisited = new HashSet<int>();

        private void ChainPopAdjacentSameColor(BalloonData source)
        {
            _chainPopQueue.Clear();
            _chainPopVisited.Clear();
            _chainPopVisited.Add(source.balloonId);

            // 시작 풍선의 인접 같은 색 추가
            List<int> startAdj = GetAdjacentBalloonIds(source.position);
            for (int i = 0; i < startAdj.Count; i++)
            {
                if (_chainPopVisited.Add(startAdj[i]))
                    _chainPopQueue.Enqueue(startAdj[i]);
            }

            int safety = 0;
            const int MAX_CHAIN = 500;

            while (_chainPopQueue.Count > 0 && safety++ < MAX_CHAIN)
            {
                int id = _chainPopQueue.Dequeue();
                if (!_balloons.TryGetValue(id, out BalloonData neighbor)) continue;
                if (neighbor.isPopped) continue;
                if (neighbor.color != source.color) continue;
                if (_hiddenBalloons.Contains(id)) continue;

                ExecutePop(neighbor);

                // 팝된 풍선의 인접도 큐에 추가 (HashSet으로 O(1) 중복 체크)
                List<int> adj = GetAdjacentBalloonIds(neighbor.position);
                for (int i = 0; i < adj.Count; i++)
                {
                    if (_chainPopVisited.Add(adj[i]))
                        _chainPopQueue.Enqueue(adj[i]);
                }
            }
        }

        /// <summary>
        /// Destroys all adjacent Pin balloons. Pins cannot be targeted by darts —
        /// they are only removed when a neighboring balloon is popped.
        /// </summary>
        private void RemoveAdjacentPins(Vector3 position)
        {
            int count = CopyAdjacentIds(GetAdjacentBalloonIds(position));
            for (int i = 0; i < count; i++)
            {
                int id = _adjCopyBuffer[i];
                if (!_balloons.TryGetValue(id, out BalloonData neighbor)) continue;
                if (neighbor.isPopped) continue;
                if (neighbor.gimmickType != GimmickPin) continue;

                ExecutePop(neighbor);
            }
        }

        /// <summary>
        /// [#13 / 기믹명세 §11] 한 Ice 영역의 공유 HP 가 0 에 도달하면 GimmickProcessor 가 호출 —
        /// 해당 영역의 얼음만 동시에 해제한다. 얼음이 깨지며 아래 가려진 풍선(=Ice 풍선 본체)이 노출되어
        /// 다트로 타격 가능해진다. (이전 인접 팝 해동 ThawAdjacentIce / 전체 일괄 BreakAllIce 모델은 폐기.)
        /// </summary>
        public void BreakIceRegion(IEnumerable<int> ids)
        {
            if (ids == null) return;
            bool thawedAny = false;
            foreach (int id in ids)
            {
                if (!_balloons.TryGetValue(id, out BalloonData ice)) continue;
                if (ice.isPopped || ice.gimmickType != GimmickIce) continue;

                // 얼음 해제: 일반 풍선으로 전환 (이제 다트로 타격 가능)
                ice.gimmickType = GimmickNone;
                _balloons[id] = ice;

                // 얼음 쉘 오버레이 제거 → (앵커) 풍선 본체 재표시
                ReturnFrozenOverlay(id);

                // Visual: 본체 강제 표시(블록 비앵커 셀은 오버레이 없이 숨겨져 있었음) → 원래 색 복원 + 노출 연출
                if (_balloonObjects.TryGetValue(id, out GameObject obj) && obj != null)
                {
                    var bi = obj.GetComponent<BalloonIdentifier>();
                    if (bi != null) bi.SetVisible(true);
                    int colorIdx = Mathf.Clamp(ice.color, 0, BalloonColors.Length - 1);
                    ApplyTintToObject(obj, BalloonColors[colorIdx]);
                    PlayRevealEffect(obj, colorIdx, id);
                }

                EventBus.Publish(new OnGimmickTriggered
                {
                    gimmickType = GimmickIce,
                    targetId    = id
                });

                thawedAny = true;
            }

            if (thawedAny)
            {
                // Ice 가 팝 없이 타격 가능해지므로 캐시 윤곽을 재구성.
                DirectionalTargeting.InvalidateCache();
                RefreshOutermostRendererState();
                // ROLLBACK_BREAKICE_BSM_INVALIDATE_20260622: pop 없는 해동도 외곽 매칭이 바뀌므로 BoardStateManager
                //   outermost 캐시도 명시적으로 무효화(RevealHiddenBalloon 과 동일 self-contained 패턴). 기존엔 구동 다트 hit 에 의존.
                if (BoardStateManager.HasInstance) BoardStateManager.Instance.InvalidateOutermostCache();
            }
        }

        /// <summary>
        /// [#13 / 기믹명세 §11] 필드의 Ice 풍선들을 인접 연결 성분(영역)으로 묶어 반환.
        /// 각 영역 = 공유 HP 단위. GimmickProcessor.InitIceRegions 가 셋업 직후 1회 호출.
        /// 4방향 인접(상하좌우) flood-fill 기준.
        /// </summary>
        public List<List<int>> GetIceRegions()
        {
            var regions = new List<List<int>>();
            var visited = new HashSet<int>();
            var stack = new Stack<int>();
            // ROLLBACK_ICE_MANUAL_GROUP_20260608:
            // Manual MapMaker Ice groups override adjacency. Ice cells with groupId > 0 are grouped
            // by that id even when they are not adjacent. groupId == 0 keeps the previous flood-fill behavior.
            var manualGroups = new Dictionary<int, List<int>>();

            foreach (var kvp in _balloons)
            {
                if (kvp.Value.isPopped || kvp.Value.gimmickType != GimmickIce) continue;
                if (kvp.Value.iceGroupId > 0)
                {
                    if (!manualGroups.TryGetValue(kvp.Value.iceGroupId, out var manual))
                    {
                        manual = new List<int>();
                        manualGroups[kvp.Value.iceGroupId] = manual;
                    }
                    manual.Add(kvp.Key);
                    visited.Add(kvp.Key);
                    continue;
                }
                if (visited.Contains(kvp.Key)) continue;

                var region = new List<int>();
                stack.Clear();
                stack.Push(kvp.Key);
                visited.Add(kvp.Key);

                while (stack.Count > 0)
                {
                    int id = stack.Pop();
                    region.Add(id);
                    if (!_balloons.TryGetValue(id, out BalloonData cur)) continue;

                    int cnt = CopyAdjacentIds(GetAdjacentBalloonIdsForBalloon(id, cur.position));
                    for (int i = 0; i < cnt; i++)
                    {
                        int nb = _adjCopyBuffer[i];
                        if (visited.Contains(nb)) continue;
                        if (!_balloons.TryGetValue(nb, out BalloonData nbData)) continue;
                        if (nbData.isPopped || nbData.gimmickType != GimmickIce) continue;
                        if (nbData.iceGroupId > 0) continue;
                        visited.Add(nb);
                        stack.Push(nb);
                    }
                }
                regions.Add(region);
            }
            foreach (var kvp in manualGroups)
                if (kvp.Value != null && kvp.Value.Count > 0)
                    regions.Add(kvp.Value);
            return regions;
        }

        public bool TryGetBalloonWorldPosition(int balloonId, out Vector3 position)
        {
            if (_balloonObjects.TryGetValue(balloonId, out GameObject obj) && obj != null)
            {
                position = obj.transform.position;
                return true;
            }
            if (_balloons.TryGetValue(balloonId, out BalloonData data))
            {
                position = GetAdjustedBoardPosition(data.position);
                return true;
            }
            position = default;
            return false;
        }

        /// <summary>
        /// Thaws adjacent Frozen Dart balloons. Unlike Ice (which becomes targetable),
        /// Frozen Dart thaw converts it to a normal balloon that can be popped in 1 hit.
        /// </summary>
        private void ThawAdjacentFrozenDarts(BalloonData source)
        {
            int count = CopyAdjacentIds(GetAdjacentBalloonIdsForBalloon(source.balloonId, source.position));
            bool thawedAny = false;
            for (int i = 0; i < count; i++)
            {
                int id = _adjCopyBuffer[i];
                if (!_balloons.TryGetValue(id, out BalloonData neighbor)) continue;
                if (neighbor.isPopped) continue;
                if (neighbor.gimmickType != GimmickFrozenDart) continue;

                // Thaw: convert to normal balloon (now poppable in 1 hit)
                neighbor.gimmickType = GimmickNone;
                neighbor.hitCount = 1; // Mark as already thawed so next hit pops
                _balloons[id] = neighbor;

                // 얼음 쉘 오버레이 제거
                ReturnFrozenOverlay(id);

                // Visual: restore original color
                if (_balloonObjects.TryGetValue(id, out GameObject obj) && obj != null)
                {
                    int colorIdx = Mathf.Clamp(neighbor.color, 0, BalloonColors.Length - 1);
                    ApplyTintToObject(obj, BalloonColors[colorIdx]);
                }

                EventBus.Publish(new OnGimmickTriggered
                {
                    gimmickType = GimmickFrozenDart,
                    targetId    = id
                });

                thawedAny = true;
            }

            if (thawedAny)
            {
                // ROLLBACK_FROZEN_THAW_TARGET_CACHE_INVALIDATION:
                // Adjacent thaw changes targetability without removing the object.
                DirectionalTargeting.InvalidateCache();
                RefreshOutermostRendererState();
            }
        }

        /// <summary>
        /// Spawns new balloons at random adjacent empty grid cells.
        /// Used by Spawner_T (1 balloon) and Spawner_O (2 balloons).
        /// Returns the actual number of balloons spawned.
        /// </summary>
        private int SpawnAtAdjacentEmpty(Vector3 position, int count)
        {
            List<Vector3> emptyPositions = GetAdjacentEmptyPositions(position);
            if (emptyPositions.Count == 0) return 0;

            // Shuffle empty positions for randomness
            for (int i = emptyPositions.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                Vector3 tmp = emptyPositions[i];
                emptyPositions[i] = emptyPositions[j];
                emptyPositions[j] = tmp;
            }

            int spawned = 0;
            int maxColors = Mathf.Min(BalloonColors.Length, 8); // Use first 8 colors

            for (int i = 0; i < count && i < emptyPositions.Count; i++)
            {
                int color = Random.Range(0, maxColors);
                int id = _nextBalloonId++;

                BalloonData newData = new BalloonData
                {
                    balloonId   = id,
                    color       = color,
                    position    = emptyPositions[i],
                    isPopped    = false,
                    gimmickType = GimmickNone,
                    hitCount    = 0
                };

                _balloons[id] = newData;
                RemainingCount++;

                GameObject obj = GetOrCreateBalloonObject(id, emptyPositions[i], color);
                if (obj != null)
                {
                    _balloonObjects[id] = obj;

                    // Spawn animation: scale up from zero
                    obj.transform.localScale = Vector3.zero;
                    // [Leak fix 2026-05-11] SetLink(KillOnDisable) — pool 재사용 시 SetActive(false) 에서 자동 kill. 원본: .SetLink 없이.
                    obj.transform.DOScale(Vector3.one * _balloonScale, 0.25f)
                        .SetEase(Ease.OutBack)
                        .SetLink(obj, LinkBehaviour.KillOnDisable);
                }

                // Update position index
                _positionIndex[ToGridKey(emptyPositions[i])] = id;

                EventBus.Publish(new OnBalloonSpawned
                {
                    balloonId = id,
                    color     = color,
                    position  = emptyPositions[i]
                });

                spawned++;
            }

            return spawned;
        }

        /// <summary>
        /// Returns world positions of empty adjacent grid cells (4-directional).
        /// </summary>
        private List<Vector3> GetAdjacentEmptyPositions(Vector3 position)
        {
            List<Vector3> empty = new List<Vector3>();
            Vector3Int center = ToGridKey(position);

            Vector3Int[] directions =
            {
                new Vector3Int( 1, 0,  0),
                new Vector3Int(-1, 0,  0),
                new Vector3Int( 0, 0,  1),
                new Vector3Int( 0, 0, -1)
            };

            foreach (Vector3Int dir in directions)
            {
                Vector3Int neighbor = center + dir;
                if (!_positionIndex.ContainsKey(neighbor))
                {
                    // Convert grid key back to world position
                    // [LATTICE_PHASE 2026-06-12] 키가 위상 기준이므로 역변환도 위상을 더해 복원.
                    Vector3 worldPos = new Vector3(
                        _latticePhaseX + neighbor.x * _cellSpacing,
                        position.y,
                        _latticePhaseZ + neighbor.z * _cellSpacing
                    );
                    empty.Add(worldPos);
                }
            }

            return empty;
        }

        private void ReturnBalloonObject(int balloonId, float effectScaleMultiplier)
        {
            float __totalStamp = InGamePerfLogger.StartStampMs();
            if (!_balloonObjects.TryGetValue(balloonId, out GameObject obj)) return;
            if (obj == null) return;

            _balloonObjects.Remove(balloonId);

            var identifier = obj.GetComponent<BalloonIdentifier>();

            float savedScale = _balloonScale;
            string returnKey = PoolKey;
            int popColorIdx = 0;
            bool isBarricade = false;
            if (_balloons.TryGetValue(balloonId, out BalloonData retData))
            {
                returnKey = ResolveGimmickPoolKey(retData.gimmickType);
                popColorIdx = retData.color;
                isBarricade = retData.gimmickType == GimmickBarricade; // #4 파괴 연출 분기
            }

            // FrozenLayer 오버레이가 붙어있다면 먼저 풀로 반환
            float __overlayStamp = InGamePerfLogger.StartStampMs();
            ReturnFrozenOverlay(balloonId);
            InGamePerfLogger.EndSection(__overlayStamp, "Balloon.ReturnObject.FrozenOverlay");

            // 풍선 스케일업 → 완료 후 PopEffectPool 재생 + 풍선 풀 반환.
            // 이전: BalloonIdentifier에 _popEffect 자식 부착 → detach/reattach + 풍선마다 별도 인스턴스 → 부하.
            // 이후: 단일 CircleParticle 풀에서 가져와 색상 적용 + play, 끝나면 풀 반환.
            // [Leak fix 2026-05-11] 새 Sequence 시작 전 stale tween 정리 — DOPunchScale 등 진행 중이면 leak + 시각 충돌.
            float __tweenSetupStamp = InGamePerfLogger.StartStampMs();
            obj.transform.DOKill();
            float scaleUpDuration = GameManager.Instance.Board.popScaleDuration;
            float scaleUpMult = GameManager.Instance.Board.popScaleMultiplier;
            Vector3 popPos = obj.transform.position;
            Sequence seq = DOTween.Sequence();
            // SetUpdate(true): 이어하기는 fail 팝업(PauseManager, timeScale=0)이 열린 상태에서 풍선을
            //   pop 하므로, scaled tween 이면 시퀀스(스케일업→풀반환 콜백)가 정지해 풍선이 화면에 남는다.
            //   unscaled 로 돌려 timeScale=0 에서도 시각 제거가 완료되게 함. (timeScale=1 일반 플레이에선 동일 동작.)
            seq.SetUpdate(true);
            if (isBarricade)
            {
                // ROLLBACK_BARRICADE_DESTROY_SCALE_20260625: 바리케이드 파괴 시 1 → 1.1 → 0 으로 팝 후 소멸.
                seq.Append(obj.transform.DOScale(Vector3.one * savedScale * 1.1f, scaleUpDuration * 0.45f).SetEase(Ease.OutQuad));
                seq.Append(obj.transform.DOScale(Vector3.zero, scaleUpDuration * 0.55f).SetEase(Ease.InQuad));
            }
            else
            {
                seq.Append(obj.transform.DOScale(Vector3.one * savedScale * scaleUpMult, scaleUpDuration).SetEase(Ease.OutQuad));
            }
            seq.AppendCallback(() =>
            {
                // 스케일업 완료 시점에 애니메이터 Pop 트리거 (파티클은 PopEffectPool 가 처리)
                if (identifier != null)
                    identifier.MarkPopped();

                int ci = Mathf.Clamp(popColorIdx, 0, BalloonColors.Length - 1);
                PopEffectPool.Play(popPos, BalloonColors[ci], this, effectScaleMultiplier);

                if (obj != null && ObjectPoolManager.HasInstance)
                {
                    obj.transform.localScale = Vector3.one * savedScale;
                    ObjectPoolManager.Instance.Return(returnKey, obj);
                }
            });
            InGamePerfLogger.EndSection(__tweenSetupStamp, "Balloon.ReturnObject.TweenSetup");
            InGamePerfLogger.EndSection(__totalStamp, "Balloon.ReturnObject.Total");
        }

        private float GetBalloonEffectScaleMultiplier(int balloonId)
        {
            if (_balloonObjects.TryGetValue(balloonId, out GameObject obj) && obj != null)
            {
                Vector3 scale = obj.transform.localScale;
                float visualScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                if (visualScale > 0.001f)
                    return Mathf.Max(0.01f, visualScale / DEFAULT_BALLOON_SCALE);
            }

            return Mathf.Max(0.01f, _balloonScale / DEFAULT_BALLOON_SCALE);
        }

        private IEnumerator FlyKeyToLock(Vector3 startPos, int pairId)
        {
            // Lock 보관함 찾기
            Vector3 targetPos = startPos + Vector3.up * 2f; // fallback
            if (HolderManager.HasInstance && HolderVisualManager.HasInstance)
            {
                HolderData[] holders = HolderManager.Instance.GetHolders();
                for (int i = 0; i < holders.Length; i++)
                {
                    if (holders[i].lockPairId == pairId && holders[i].isLocked)
                    {
                        GameObject targetObj = HolderVisualManager.Instance.GetHolderGameObject(holders[i].holderId);
                        if (targetObj != null)
                            targetPos = targetObj.transform.position + Vector3.up * 1.1f;
                        break;
                    }
                }
            }

            // Key 오브젝트 풀에서 가져오기
            if (!ObjectPoolManager.HasInstance)
            {
                if (HolderManager.HasInstance) HolderManager.Instance.UnlockHolder(pairId);
                yield break;
            }

            Vector3 spawnPos = startPos + Vector3.up * 0.3f;
            GameObject keyObj = ObjectPoolManager.Instance.Get("Key", spawnPos, Quaternion.Euler(90f, 0f, 0f));
            if (keyObj == null)
            {
                if (HolderManager.HasInstance) HolderManager.Instance.UnlockHolder(pairId);
                yield break;
            }
            keyObj.SetActive(true);

            // Phase 1: 위로 튕김 (0.15초)
            Vector3 bounceTop = spawnPos + Vector3.up * 1.2f;
            float t = 0f;
            while (t < 0.15f)
            {
                t += Time.deltaTime;
                float p = t / 0.15f;
                keyObj.transform.position = Vector3.Lerp(spawnPos, bounceTop, Mathf.Sin(p * Mathf.PI * 0.5f));
                yield return null;
            }

            // Phase 2: 포물선 비행 (0.5초)
            t = 0f;
            while (t < 0.5f)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / 0.5f);
                Vector3 linear = Vector3.Lerp(bounceTop, targetPos, p);
                float arc = 2f * 4f * p * (1f - p);
                keyObj.transform.position = linear + Vector3.up * arc;
                keyObj.transform.Rotate(Vector3.forward, 540f * Time.deltaTime);
                yield return null;
            }

            keyObj.transform.position = targetPos;
            keyObj.SetActive(false);
            if (ObjectPoolManager.HasInstance)
                ObjectPoolManager.Instance.Return("Key", keyObj);

            // 잠금 해제
            if (HolderManager.HasInstance)
                HolderManager.Instance.UnlockHolder(pairId);
        }

        #endregion

        #region Private Methods — Key Path Checking

        private void CheckKeysOnPop(OnBalloonPopped evt)
        {
            if (_activeKeyPairIds.Count == 0) return;
            var keysToRelease = new List<int>();
            foreach (var kvp in _activeKeyPairIds)
            {
                if (!_balloons.TryGetValue(kvp.Key, out BalloonData kd)) continue;
                if (kd.isPopped) continue;
                Vector3Int keyGrid = ToGridKey(kd.position);
                bool canReach = CanKeyReachBelt(keyGrid);
#if UNITY_EDITOR
                Debug.Log($"[Key A*]" + "stripped");
#endif
                if (canReach)
                    keysToRelease.Add(kvp.Key);
            }
            foreach (int keyId in keysToRelease)
            {
#if UNITY_EDITOR
                Debug.Log($"[Key A*]" + "stripped");
#endif
                ReleaseKey(keyId);
            }
        }

        private bool CanKeyReachBelt(Vector3Int startGrid)
        {
            // BFS from Key grid position to any edge of the balloon field
            // A cell is walkable if no non-popped balloon exists there
            var visited = new HashSet<Vector3Int>();
            var queue = new Queue<Vector3Int>();
            visited.Add(startGrid);
            queue.Enqueue(startGrid);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                // Check if current is at edge of grid (can escape to belt)
                if (!_positionIndex.ContainsKey(current) || current.Equals(startGrid))
                {
                    // Check if this position is at the boundary or outside the populated area
                    bool atEdge = false;
                    Vector3Int[] dirs = { new Vector3Int(1,0,0), new Vector3Int(-1,0,0), new Vector3Int(0,0,1), new Vector3Int(0,0,-1) };
                    foreach (var d in dirs)
                    {
                        Vector3Int neighbor = current + d;
                        if (!_positionIndex.ContainsKey(neighbor) && !visited.Contains(neighbor))
                        {
                            // Neighbor is empty and not in grid = belt edge reached
                            // A position with no balloon registered means it's open
                            atEdge = true;
                        }
                    }
                    if (atEdge && !current.Equals(startGrid)) return true;
                }

                // Expand to neighbors
                Vector3Int[] directions = { new Vector3Int(1,0,0), new Vector3Int(-1,0,0), new Vector3Int(0,0,1), new Vector3Int(0,0,-1) };
                foreach (var dir in directions)
                {
                    Vector3Int next = current + dir;
                    if (visited.Contains(next)) continue;
                    visited.Add(next);

                    // Check if this cell is passable (no non-popped balloon, or it's a popped balloon)
                    if (_positionIndex.TryGetValue(next, out int balloonId))
                    {
                        if (_balloons.TryGetValue(balloonId, out BalloonData bd) && !bd.isPopped)
                            continue; // Blocked by non-popped balloon
                    }
                    queue.Enqueue(next);
                }
            }
            return false;
        }

        private void ReleaseKey(int keyId)
        {
            if (!_balloons.TryGetValue(keyId, out BalloonData keyData)) return;
            if (!_activeKeyPairIds.TryGetValue(keyId, out int pairId)) return;

            _activeKeyPairIds.Remove(keyId);
            float effectScaleMultiplier = GetBalloonEffectScaleMultiplier(keyId);

            // Mark as popped so it's removed from position tracking
            keyData.isPopped = true;
            _balloons[keyId] = keyData;
            RemainingCount = Mathf.Max(0, RemainingCount - 1);
            PoppedCount++;
            RemovePositionIndexForBalloon(keyData);

            // Return Key visual to pool
            if (_balloonObjects.TryGetValue(keyId, out GameObject keyObj) && keyObj != null)
            {
                Vector3 keyPos = keyObj.transform.position;
                _balloonObjects.Remove(keyId);
                keyObj.SetActive(false);
                if (ObjectPoolManager.HasInstance)
                    ObjectPoolManager.Instance.Return("Key", keyObj);

                // Start flight animation
                if (pairId >= 0)
                    StartCoroutine(FlyKeyToLock(keyPos, pairId));
            }

            EventBus.Publish(new OnBalloonPopped
            {
                balloonId = keyId,
                color = keyData.color,
                position = keyData.position,
                effectScaleMultiplier = effectScaleMultiplier
            });
        }

        #endregion

        #region Private Methods — Spatial Helpers

        /// <summary>재사용 리스트 + 방향 배열 (GC 방지)</summary>
        private readonly List<int> _reusableAdjacentIds = new List<int>(4);
        /// <summary>순회 중 재진입 방지용 로컬 복사 버퍼 (최대 4방향)</summary>
        private readonly int[] _adjCopyBuffer = new int[4];

        /// <summary>_reusableAdjacentIds를 로컬 버퍼로 복사. 순회 중 재진입 안전.</summary>
        private int CopyAdjacentIds(List<int> src)
        {
            int count = Mathf.Min(src.Count, _adjCopyBuffer.Length);
            for (int i = 0; i < count; i++) _adjCopyBuffer[i] = src[i];
            return count;
        }
        private static readonly Vector3Int[] _adjacentDirs =
        {
            new Vector3Int( 1, 0,  0),
            new Vector3Int(-1, 0,  0),
            new Vector3Int( 0, 0,  1),
            new Vector3Int( 0, 0, -1)
        };

        private List<int> GetAdjacentBalloonIds(Vector3 position)
        {
            _reusableAdjacentIds.Clear();
            Vector3Int center = ToGridKey(position);

            for (int i = 0; i < _adjacentDirs.Length; i++)
            {
                Vector3Int neighbor = center + _adjacentDirs[i];
                if (_positionIndex.TryGetValue(neighbor, out int neighborId))
                {
                    if (!_reusableAdjacentIds.Contains(neighborId))
                        _reusableAdjacentIds.Add(neighborId);
                }
            }

            return _reusableAdjacentIds;
        }

        private List<int> GetAdjacentBalloonIdsForBalloon(int balloonId, Vector3 fallbackPosition)
        {
            _reusableAdjacentIds.Clear();

            if (!_balloons.TryGetValue(balloonId, out BalloonData data) || !UsesMultiCellOccupancy(data))
                return GetAdjacentBalloonIds(fallbackPosition);

            // ROLLBACK_MULTI_CELL_GIMMICK_ADJACENCY:
            // Sized field gimmicks occupy a rectangle from the authored bottom-left anchor. Adjacent
            // effects such as Surprise reveal and Frozen thaw must inspect every occupied edge cell,
            // not just the anchor cell.
            BuildOccupiedCells(data, _reusableOccupiedCells);
            for (int c = 0; c < _reusableOccupiedCells.Count; c++)
            {
                Vector3Int cell = _reusableOccupiedCells[c];
                for (int d = 0; d < _adjacentDirs.Length; d++)
                {
                    Vector3Int neighbor = cell + _adjacentDirs[d];
                    if (_positionIndex.TryGetValue(neighbor, out int neighborId)
                        && neighborId != balloonId
                        && !_reusableAdjacentIds.Contains(neighborId))
                    {
                        _reusableAdjacentIds.Add(neighborId);
                    }
                }
            }

            return _reusableAdjacentIds;
        }

        /// <summary>
        /// Converts a world-space Vector3 position to a grid cell key.
        /// cellSpacing 기준으로 나누어 정수 그리드 좌표로 변환.
        /// </summary>
        private Vector3Int ToGridKey(Vector3 worldPos)
        {
            // cellSpacing으로 나누어 인접 셀이 정확히 ±1 차이가 되도록 함
            // [LATTICE_PHASE 2026-06-12] 위상 기준 상대 라운딩 — 짝수 그리드 레벨의 .5×cs 경계 붕괴 방지.
            // (입력은 원시 데이터 좌표 — 모든 호출부가 data.position 계열을 넘김)
            return new Vector3Int(
                Mathf.RoundToInt((worldPos.x - _latticePhaseX) / _cellSpacing),
                0,
                Mathf.RoundToInt((worldPos.z - _latticePhaseZ) / _cellSpacing)
            );
        }

        #endregion

        #region Private Methods — Event Handlers

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            _currentLevelId = evt.levelId;

            // 모든 풍선 spawn 끝난 후 외곽 풍선만 렌더링 1회 적용 (toggle ON 시).
            // BoardStateManager 의 outermost cache 가 첫 호출에 build 되므로 자동.
            RefreshOutermostRendererState();
        }

        #endregion
    }

    // ─────────────────────────────────────────────
    // Data Types
    // ─────────────────────────────────────────────

    /// <summary>
    /// Runtime snapshot of a single balloon's state on the board.
    /// </summary>
    [System.Serializable]
    public class BalloonData
    {
        /// <summary>Unique identifier assigned at board setup.</summary>
        public int balloonId;

        /// <summary>Color index used for dart-matching logic.</summary>
        public int color;

        /// <summary>World-space position on the board.</summary>
        public Vector3 position;

        /// <summary>Whether this balloon has been popped.</summary>
        public bool isPopped;

        /// <summary>
        /// Gimmick type string. One of:
        /// "none", "Hidden", "Chain", "Pinata", "Spawner_T", "Pin", "Lock_Key",
        /// "Surprise", "Wall", "Spawner_O", "Pinata_Box", "Ice", "Frozen_Dart", "Color_Curtain".
        /// </summary>
        public string gimmickType;

        /// <summary>Hit counter for Pinata gimmick.</summary>
        public int hitCount;
        /// <summary>Piñata 최대 HP (설정값).</summary>
        public int maxHP = 2;

        /// <summary>Piñata 가로 크기 (1=기본).</summary>
        public int sizeW = 1;
        /// <summary>Piñata 세로 크기.</summary>
        public int sizeH = 1;
        public int lockPairId = -1;

        /// <summary>[Ice §11] 얼음 블록 변 길이(셀). 2=2×2 타일. 1=셀당. 영역 공유 HP·렌더 블록화에 사용.</summary>
        public int iceBlockSize = 1;
        // ROLLBACK_ICE_MANUAL_GROUP_20260608:
        // 0 = old adjacency grouping. >0 = explicit MapMaker-authored Ice group.
        // hpMode: 0/1 sum member HP, 2 override with iceGroupHp.
        public int iceGroupId = 0;
        public int iceGroupHp = 0;
        public int iceGroupHpMode = 0;

        /// <summary>[Barricade] 방향 0=N/1=E/2=S/3=W (head→body). 회전·footprint 결정. sizeW/H 미사용.</summary>
        public int barricadeDir = 1;
        /// <summary>[Barricade] body 길이(셀). 전체 head(2)+body+edge(1), 두께2. 표시/막는범위 = length×남은HP/maxHP.</summary>
        public int barricadeLength = 1;

        /// <summary>Pinata_Box 셀별 알 색상 (len=sizeW*sizeH, row-major). null=레거시 단일 박스.</summary>
        public int[] eggColors;
        /// <summary>Pinata_Box 셀별 알 HP (eggColors 와 동일 길이/순서). 런타임에 차감됨.</summary>
        public int[] eggHps;

        /// <summary>FlexTube 그룹 ID. -1 = FlexTube cell 아님.</summary>
        public int flexTubeGroupId = -1;
        /// <summary>FlexTube paint 순서 인덱스. 0=StartCap, max=EndCap.</summary>
        public int flexTubeSequenceIndex = -1;
        /// <summary>FlexTube 부품 종류 — "StartCap" / "Segment" / "EndCap".</summary>
        public string flexTubePartType = "";
        /// <summary>FlexTube 작가 지정 HP. 0 = 셀 수로 자동. >0 = HP 비례 길이 축소.</summary>
        public int flexTubeHp = 0;
    }

    /// <summary>
    /// Input data for placing a single balloon during board setup.
    /// </summary>
    [System.Serializable]
    public class BalloonSetupData
    {
        public int color;
        public Vector3 position;
        public string gimmickType;

        /// <summary>
        /// Group id for Pinata multi-tile balloons.
        /// -1 means not part of a group.
        /// </summary>
        public int groupId = -1;
        public int sizeW = 1;
        public int sizeH = 1;
        public int hp = 0;
        public int lockPairId = -1;

        /// <summary>[Ice §11] 얼음 블록 변 길이(셀). 2=2×2 타일. 1=셀당(기본).</summary>
        public int iceBlockSize = 1;
        // ROLLBACK_ICE_MANUAL_GROUP_20260608:
        // Manual Ice group metadata passed from LevelConfig/MapMaker.
        public int iceGroupId = 0;
        public int iceGroupHp = 0;
        public int iceGroupHpMode = 0;

        /// <summary>[Barricade] 방향 0=N/1=E/2=S/3=W.</summary>
        public int barricadeDir = 1;
        /// <summary>[Barricade] body 길이(셀).</summary>
        public int barricadeLength = 1;

        /// <summary>Pinata_Box 셀별 알 색상 (len=sizeW*sizeH, row-major). null=레거시 단일 박스.</summary>
        public int[] eggColors;
        /// <summary>Pinata_Box 셀별 알 HP (eggColors 와 동일 길이/순서).</summary>
        public int[] eggHps;

        /// <summary>FlexTube 그룹 ID. -1 = FlexTube 셀 아님.</summary>
        public int flexTubeGroupId = -1;
        /// <summary>FlexTube 부품 종류 (StartCap/Segment/EndCap).</summary>
        public string flexTubePartType = "";
        /// <summary>FlexTube 그룹 안 paint 순서 (0..N).</summary>
        public int flexTubeSequenceIndex = -1;
        /// <summary>FlexTube 작가 지정 HP. 0 = 셀 수로 자동.</summary>
        public int flexTubeHp = 0;
    }

    /// <summary>
    /// Result returned from BalloonController.PopBalloon().
    /// </summary>
    public class PopResult
    {
        public bool success;
        public bool hitAccepted;
        public string reason;
        public int balloonId;
        public int color;
        public Vector3 position;
        public string gimmickType;

        /// <summary>Number of new balloons spawned by Spawner gimmick. 0 for non-spawner types.</summary>
        public int spawnCount;
    }
}
