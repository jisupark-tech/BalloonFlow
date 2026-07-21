using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Visualizes the conveyor belt rail path.
    /// Supports multiple visual styles: Cylinder (3D tubes), Flat2D (quad strips),
    /// Custom3D (user-provided prefab segments).
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: UX | Phase: 1
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow
    /// </remarks>
    public class RailRenderer : MonoBehaviour
    {
        #region Constants

        public const int VISUAL_CYLINDER = 0;
        public const int VISUAL_FLAT2D = 1;
        public const int VISUAL_CUSTOM3D = 2;
        public const int VISUAL_SPRITE_TILE = 3;

        private const float DEFAULT_TRACK_WIDTH = 0.3f;
        private const float DEFAULT_TILE_SIZE = 1.5f;
        private static readonly Color DEFAULT_RAIL_COLOR = new Color(0.4f, 0.4f, 0.45f, 1f);

        #endregion

        #region Serialized Fields

        [SerializeField] private float _trackWidth = DEFAULT_TRACK_WIDTH;
        [SerializeField] private Color _railColor = DEFAULT_RAIL_COLOR;
        [SerializeField] private int _visualType = VISUAL_SPRITE_TILE;
        [SerializeField] private GameObject _customSegmentPrefab; // For VISUAL_CUSTOM3D
        [SerializeField] private float _tileWorldSize = DEFAULT_TILE_SIZE;

        #endregion

        #region Fields

        private readonly List<GameObject> _trackSegments = new List<GameObject>();
        private Material _trackMaterial;
        private RailTileSet _tileSet;
        private bool _isInitialized;

        #endregion

        #region Properties

        /// <summary>Current visual type (0=Cylinder, 1=Flat2D, 2=Custom3D).</summary>
        public int VisualType
        {
            get => _visualType;
            set { _visualType = value; }
        }

        #endregion

        #region Lifecycle

        private void Awake()
        {
            _tileSet = Resources.Load<RailTileSet>("RailTileSet");

            // Create default material for non-sprite visual modes (Cylinder, Flat2D)
            // [Optimization 2026-05-11] mat asset 우선 로드 (Resources 또는 Addressable) → 없으면 shader 기반 fallback.
            // mat asset 사용 시: shader variant 메모리 절감 + 디자이너가 mat 속성 조정 가능.
            // shader fallback 시: URP/Unlit (variant 적음) → Unlit/Color → Sprites/Default 순.
            // 롤백: 아래 코드 전체 제거 + 주석 처리된 원본 복원.
            // 원본:
            // _trackMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")
            //     ?? Shader.Find("Standard")
            //     ?? Shader.Find("Sprites/Default"));
            // _trackMaterial.color = _railColor;
            // _trackMaterial.enableInstancing = true;

            // 1) Resources 에서 미리 빌드된 mat asset 로드 시도 (sync, addressable 등록 안 됐어도 동작)
            //    "Resources/Materials/RailTrack.mat" 위치에 사용자가 mat 만들어두면 자동 사용됨.
            Material assetMat = Resources.Load<Material>("Materials/RailTrack");
            if (assetMat != null)
            {
                // asset 직접 사용 시 mat 의 color 변경이 Editor 의 asset 에 반영됨 → instance 복제 후 color override.
                _trackMaterial = new Material(assetMat);
                _trackMaterial.color = _railColor;
                _trackMaterial.enableInstancing = true;
            }
            else
            {
                // 2) Fallback: shader 기반으로 새 mat 생성. URP/Unlit 우선 (variant 적음, 메모리 작음).
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Unlit/Color")
                    ?? Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    _trackMaterial = new Material(shader);
                    _trackMaterial.color = _railColor;
                    _trackMaterial.enableInstancing = true;
                }
                else
                {
                    // 3) shader 도 못 찾으면 track 렌더 불가. 에러 로그만 — _trackMaterial 은 null 상태.
                    Debug.LogError("[RailRenderer] No shader found for track material — track will not render.");
                }
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
        }

        private void OnDestroy()
        {
            if (_trackMaterial != null)
            {
                Destroy(_trackMaterial);
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Reads the current rail path from RailManager and builds cylinder track segments.
        /// Call after RailManager.SetRailLayout() has been invoked.
        /// </summary>
        public void RefreshPath()
        {
            if (!RailManager.HasInstance)
            {
                Debug.LogWarning("[RailRenderer] RailManager not available. Cannot render rail path.");
                ClearPath();
                return;
            }

            RailManager rail = RailManager.Instance;
            Vector3[] waypoints = rail.GetRailPath();

            if (waypoints == null || waypoints.Length < 2)
            {
                Debug.LogWarning("[RailRenderer] Rail path has fewer than 2 waypoints. Clearing track.");
                ClearPath();
                return;
            }

            ClearPath();

            bool isLoop = rail.IsClosedLoop;

#if BF_RAIL_HOLDER
            // [3D 레일 2026-07-22] 홀더 모드: 이미지 타일 대신 아트 모델링(Resources/Prefabs/Rail) 배치.
            //   (BoardTileManager.BuildConveyorBelt 는 같은 조건에서 이미지 빌드를 스킵 — 이중 비주얼 방지.)
            //   모델 로드 실패 시 false → 기존 이미지 경로 폴백.
            if (RailHolderController.ModeActiveForCurrentLevel && TryBuildModelRail(waypoints))
            {
                _isInitialized = true;
                return;
            }
#endif

            // Sprite tile mode — BoardTileManager.ConveyorSprites가 처리하므로 여기서 생성하지 않음
            // 다중 생성 방지: RailRenderer는 타일 비주얼을 생성하지 않고 경로 데이터만 제공
            if (_visualType == VISUAL_SPRITE_TILE)
            {
                _isInitialized = true;
                return; // ConveyorSprites가 이미 BoardTileManager.BuildConveyorBelt()에서 생성됨
            }

            int segmentCount = isLoop ? waypoints.Length : waypoints.Length - 1;

            for (int i = 0; i < segmentCount; i++)
            {
                Vector3 start = waypoints[i];
                Vector3 end = (i == waypoints.Length - 1) ? waypoints[0] : waypoints[i + 1];

                Vector3 midpoint = (start + end) * 0.5f;
                float length = Vector3.Distance(start, end);

                if (length < 0.001f)
                {
                    continue;
                }

                GameObject segment;

                switch (_visualType)
                {
                    case VISUAL_FLAT2D:
                        segment = GameObject.CreatePrimitive(PrimitiveType.Quad);
                        segment.name = $"RailSegment2D_{i}";
                        segment.transform.SetParent(transform);
                        segment.transform.position = midpoint;
                        // Quad lies in XY by default; rotate to XZ plane, then align with direction
                        Vector3 flatDir = (end - start).normalized;
                        segment.transform.rotation = Quaternion.LookRotation(flatDir, Vector3.up);
                        segment.transform.localScale = new Vector3(_trackWidth * 3f, length, 1f);
                        break;

                    case VISUAL_CUSTOM3D:
                        if (_customSegmentPrefab != null)
                        {
                            segment = Instantiate(_customSegmentPrefab, midpoint, Quaternion.identity, transform);
                        }
                        else
                        {
                            segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            segment.transform.SetParent(transform);
                            segment.transform.position = midpoint;
                        }
                        segment.name = $"RailSegmentCustom_{i}";
                        // Align prefab forward (Z) along segment direction, keep upright
                        Vector3 segDir = (end - start).normalized;
                        if (segDir.sqrMagnitude > 0.001f)
                            segment.transform.rotation = Quaternion.LookRotation(segDir, Vector3.up);
                        // Scale: X=width, Y=width, Z=length (stretch along forward axis)
                        segment.transform.localScale = new Vector3(_trackWidth * 2f, _trackWidth * 2f, length);
                        break;

                    default: // VISUAL_CYLINDER
                        segment = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        segment.name = $"RailSegment_{i}";
                        segment.transform.SetParent(transform);
                        segment.transform.position = midpoint;
                        segment.transform.localScale = new Vector3(_trackWidth, length * 0.5f, _trackWidth);
                        segment.transform.up = (end - start).normalized;
                        break;
                }

                // Only override material for primitive segments (not custom prefab which has its own).
                // sharedMaterial 사용: 매 segment 마다 instance material 생성 방지 (기존 .material 은 instance 자동 생성).
                if (_visualType != VISUAL_CUSTOM3D)
                {
                    var meshRenderer = segment.GetComponent<MeshRenderer>();
                    if (meshRenderer != null)
                    {
                        meshRenderer.sharedMaterial = _trackMaterial;
                    }
                }

                // Disable colliders on rail segments (visual only)
                var colliders = segment.GetComponentsInChildren<Collider>();
                for (int c = 0; c < colliders.Length; c++)
                {
                    colliders[c].enabled = false;
                }

                _trackSegments.Add(segment);
            }

            _isInitialized = true;
        }

#if BF_RAIL_HOLDER
        // ── [3D 레일 2026-07-22] 아트 모델링 레일 (Resources/Prefabs/Rail) ─────────────────────────
        //   아트 기준: 기존 이미지 레일과 동일 사이즈 제작 → 기준 보드에선 scale≈1. 보드 치수가 다른
        //   레벨은 waypoint 사각형(ㅁ자)에 XZ 오토핏(모델 bounds 실측 대비 비율). 위치는 bounds 중심을
        //   사각형 중심에 정렬(피벗 어긋남 자동 보정), 바닥은 y=0(피벗 기준 아트 저작 그대로).
        private static GameObject s_railModelPrefab;
        private static bool s_railModelLoadTried;
        /// <summary>프리팹 저작 스케일(100) 대비 X/Z 추가 배율. 1.0 = 아트 원본 그대로(100,100,100).
        /// [2026-07-22 원복] 한때 1.1(110,100,110)로 키웠던 건 화살표 연출이 모델 트랙과 어긋나서였음 —
        /// 근본 수정은 화살표 경로 정합(BoardTileManager.UpdateArrowPositions, railArrowPathFit/Height)으로 이동.</summary>
        private const float RAIL_MODEL_SCALE_MULT_XZ = 1.0f;

        /// <summary>모델 프리팹 로드 가능 여부(1회 로드 캐시). BoardTileManager 가 이미지 빌드 스킵 판단에 사용.</summary>
        public static bool IsModelRailAvailable()
        {
            if (!s_railModelLoadTried)
            {
                s_railModelLoadTried = true;
                s_railModelPrefab = Resources.Load<GameObject>("Prefabs/Rail");
                if (s_railModelPrefab == null)
                    Debug.LogWarning("[RailRenderer] Resources/Prefabs/Rail 로드 실패 — 이미지 타일 레일로 폴백합니다.");
            }
            return s_railModelPrefab != null;
        }

        private bool TryBuildModelRail(Vector3[] waypoints)
        {
            if (!IsModelRailAvailable()) return false;

            // waypoint 사각형(ㅁ자) 치수 — 홀더 모드는 항상 4면 폐루프.
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < waypoints.Length; i++)
            {
                Vector3 w = waypoints[i];
                if (w.x < minX) minX = w.x;
                if (w.x > maxX) maxX = w.x;
                if (w.z < minZ) minZ = w.z;
                if (w.z > maxZ) maxZ = w.z;
            }
            float railW = maxX - minX;
            float railD = maxZ - minZ;
            if (railW < 0.1f || railD < 0.1f) return false;

            GameObject model = Instantiate(s_railModelPrefab, transform);
            model.name = "RailModel3D";
            model.transform.position = Vector3.zero;
            model.transform.rotation = Quaternion.identity;

            // 스케일: 아트 저작 스케일(프리팹 루트 100,100,100) 보존 + X/Z 배율 1.1 → 110,100,110
            //   (사용자 지정 2026-07-22). ※ 이전 오토핏은 루트 스케일을 비율값으로 '덮어써' 모델이
            //   1/100 로 축소되는 버그 — 아트가 이미지 레일과 동일 사이즈로 저작했으므로 배율만 얹는다.
            Vector3 baseScale = model.transform.localScale;
            model.transform.localScale = new Vector3(
                baseScale.x * RAIL_MODEL_SCALE_MULT_XZ,
                baseScale.y,
                baseScale.z * RAIL_MODEL_SCALE_MULT_XZ);

            // 위치: 스케일 반영 bounds 중심을 레일 사각형 중심(XZ)에 정렬, 바닥 y=0.
            Bounds scaled = CalcRendererBounds(model);
            Vector3 center = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
            Vector3 pos = model.transform.position;
            pos.x += center.x - scaled.center.x;
            pos.z += center.z - scaled.center.z;
            pos.y = 0f;
            model.transform.position = pos;

            // 비주얼 전용 — 콜라이더 비활성(기존 세그먼트 정책과 동일).
            var cols = model.GetComponentsInChildren<Collider>();
            for (int c = 0; c < cols.Length; c++) cols[c].enabled = false;

            _trackSegments.Add(model);   // ClearPath 가 함께 정리
            return true;
        }

        private static Bounds CalcRendererBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }
#endif

        /// <summary>
        /// Destroys all track segment GameObjects and clears the list.
        /// </summary>
        public void ClearPath()
        {
            for (int i = _trackSegments.Count - 1; i >= 0; i--)
            {
                if (_trackSegments[i] != null)
                {
                    Destroy(_trackSegments[i]);
                }
            }
            _trackSegments.Clear();
            _isInitialized = false;
        }

        #endregion

        #region Sprite Tile Path

        /// <summary>
        /// Builds tile visuals from LevelConfig.conveyorPositions using grid-aligned placement.
        /// Each position maps to a world coordinate via the same formula as MapMaker.
        /// Neighbor-based auto-tiling picks the correct sprite (h, v, bl, br, tl, tr).
        /// </summary>
        private void BuildGridBasedTilePath(LevelConfig config)
        {
            if (_tileSet == null) return;

            var positions = config.conveyorPositions;
            int gridCols = config.gridCols > 0 ? config.gridCols : 5;
            int gridRows = config.gridRows > 0 ? config.gridRows : 5;

            float boardCX = 0f, boardCY = 2f;
            float cellSpacing = 1.6f;
            if (GameManager.HasInstance)
            {
                boardCX = GameManager.Instance.Board.boardCenterX;
                boardCY = GameManager.Instance.Board.boardCenterZ;
                cellSpacing = GameManager.Instance.Board.cellSpacing;
            }

            // Build lookup grid (offset to handle negative coords)
            int minX = 0, minY = 0, maxX = 0, maxY = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                if (positions[i].x < minX) minX = positions[i].x;
                if (positions[i].y < minY) minY = positions[i].y;
                if (positions[i].x > maxX) maxX = positions[i].x;
                if (positions[i].y > maxY) maxY = positions[i].y;
            }
            int gw = maxX - minX + 1;
            int gh = maxY - minY + 1;
            bool[,] grid = new bool[gw, gh];
            for (int i = 0; i < positions.Length; i++)
                grid[positions[i].x - minX, positions[i].y - minY] = true;

            // Place tiles at grid-aligned world positions
            for (int i = 0; i < positions.Length; i++)
            {
                int bx = positions[i].x; // balloon-grid-relative coord
                int by = positions[i].y;

                // World position (same formula as MapMaker.PathGridToWorld)
                float wx = boardCX + (bx - (gridCols - 1) * 0.5f) * cellSpacing;
                float wz = boardCY + (by - (gridRows - 1) * 0.5f) * cellSpacing;
                Vector3 wpos = new Vector3(wx, 0f, wz);

                // Auto-tile: check 4 neighbors in the offset grid
                int gx = bx - minX, gy = by - minY;
                bool hasUp    = (gy + 1 < gh) && grid[gx, gy + 1];
                bool hasDown  = (gy - 1 >= 0) && grid[gx, gy - 1];
                bool hasLeft  = (gx - 1 >= 0) && grid[gx - 1, gy];
                bool hasRight = (gx + 1 < gw) && grid[gx + 1, gy];

                int midCol = (gw - 1) / 2;

                Sprite tile;
                // Corners
                if      (hasRight && hasUp   && !hasLeft && !hasDown) tile = _tileSet.tileBL;
                else if (hasLeft  && hasUp   && !hasRight && !hasDown) tile = _tileSet.tileBR;
                else if (hasRight && hasDown && !hasLeft && !hasUp)   tile = _tileSet.tileTL;
                else if (hasLeft  && hasDown && !hasRight && !hasUp)  tile = _tileSet.tileTR;
                // Straights
                else if (hasLeft && hasRight) tile = _tileSet.GetH();
                else if (hasUp   && hasDown)  tile = gx <= midCol ? _tileSet.GetVL() : _tileSet.GetVR();
                // Single-neighbor fallback
                else if (hasLeft || hasRight) tile = _tileSet.GetH();
                else if (hasUp   || hasDown)  tile = gx <= midCol ? _tileSet.GetVL() : _tileSet.GetVR();
                else tile = _tileSet.GetH();

                PlaceSpriteTileAtSize(tile, wpos, cellSpacing);
            }
        }

        /// <summary>
        /// Places a sprite tile at exact world position with specified tile size.
        /// </summary>
        private void PlaceSpriteTileAtSize(Sprite sprite, Vector3 position, float tileSize)
        {
            if (sprite == null) return;

            var tileGO = new GameObject($"RailTile_{_trackSegments.Count}");
            tileGO.transform.SetParent(transform);
            tileGO.transform.position = new Vector3(position.x, -0.02f, position.z);
            tileGO.transform.eulerAngles = new Vector3(90f, 0f, 0f);

            var sr = tileGO.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = -1;

            float spriteW = sprite.bounds.size.x;
            float spriteH = sprite.bounds.size.y;
            if (spriteW > 0.001f && spriteH > 0.001f)
            {
                float scaleX = tileSize / spriteW;
                float scaleY = tileSize / spriteH;
                tileGO.transform.localScale = new Vector3(scaleX, scaleY, 1f);
            }

            _trackSegments.Add(tileGO);
        }

        /// <summary>
        /// Builds the rail visual using 2D sprite tiles from RailTileSet.
        /// Places straight and corner tiles along the waypoint loop.
        /// </summary>
        private void BuildSpriteTilePath(Vector3[] waypoints, bool isLoop)
        {
            if (_tileSet == null)
            {
                Debug.LogWarning("[RailRenderer] RailTileSet not loaded. Run BalloonFlow > Setup Rail Tiles.");
                return;
            }

            int count = isLoop ? waypoints.Length : waypoints.Length - 1;

            // 컨베이어 중심 X — waypoint 평균값으로 좌/우 세로 레일 판정.
            float centerX = 0f;
            for (int wi = 0; wi < waypoints.Length; wi++) centerX += waypoints[wi].x;
            centerX /= Mathf.Max(1, waypoints.Length);

            for (int i = 0; i < count; i++)
            {
                int prev = (i - 1 + waypoints.Length) % waypoints.Length;
                int next = (i + 1) % waypoints.Length;
                int nextNext = (i + 2) % waypoints.Length;

                Vector3 start = waypoints[i];
                Vector3 end = waypoints[(i + 1) % waypoints.Length];
                Vector3 delta = end - start;
                float segLen = delta.magnitude;
                if (segLen < 0.01f) continue;

                bool isHorizontal = Mathf.Abs(delta.x) > Mathf.Abs(delta.z);
                Sprite tile = isHorizontal
                    ? _tileSet.GetH()
                    : (start.x < centerX ? _tileSet.GetVL() : _tileSet.GetVR());

                // Check if start/end are corners to avoid tile overlap
                bool startIsCorner = false;
                bool endIsCorner = false;
                if (isLoop && waypoints.Length >= 3)
                {
                    Vector3 inAtStart = (waypoints[i] - waypoints[prev]).normalized;
                    Vector3 outAtStart = delta.normalized;
                    startIsCorner = Mathf.Abs(inAtStart.x) > Mathf.Abs(inAtStart.z)
                                 != Mathf.Abs(outAtStart.x) > Mathf.Abs(outAtStart.z);

                    Vector3 inAtEnd = delta.normalized;
                    Vector3 outAtEnd = (waypoints[nextNext] - waypoints[(i + 1) % waypoints.Length]).normalized;
                    endIsCorner = Mathf.Abs(inAtEnd.x) > Mathf.Abs(inAtEnd.z)
                               != Mathf.Abs(outAtEnd.x) > Mathf.Abs(outAtEnd.z);
                }

                int tileCount = Mathf.Max(1, Mathf.RoundToInt(segLen / _tileWorldSize));
                int straightCount = tileCount;
                if (startIsCorner) straightCount--;
                if (endIsCorner) straightCount--;
                if (straightCount <= 0) continue;

                for (int t = 0; t < straightCount; t++)
                {
                    float offset = startIsCorner ? 1f : 0.5f;
                    float frac = (t + offset) / tileCount;
                    Vector3 pos = Vector3.Lerp(start, end, frac);
                    PlaceSpriteTile(tile, pos);
                }
            }

            // Place corner tiles at waypoints where direction changes
            if (isLoop && waypoints.Length >= 3)
            {
                for (int i = 0; i < waypoints.Length; i++)
                {
                    int prev = (i - 1 + waypoints.Length) % waypoints.Length;
                    int next = (i + 1) % waypoints.Length;

                    Vector3 inDir = (waypoints[i] - waypoints[prev]).normalized;
                    Vector3 outDir = (waypoints[next] - waypoints[i]).normalized;

                    bool inH = Mathf.Abs(inDir.x) > Mathf.Abs(inDir.z);
                    bool outH = Mathf.Abs(outDir.x) > Mathf.Abs(outDir.z);

                    if (inH == outH) continue;

                    // Direction-based corner selection (works for any path shape)
                    Sprite cornerTile = _tileSet.GetTileForDirections(inDir, outDir);
                    PlaceSpriteTile(cornerTile, waypoints[i]);
                }
            }
        }

        private void PlaceSpriteTile(Sprite sprite, Vector3 position)
        {
            if (sprite == null) return;

            var tileGO = new GameObject($"RailTile_{_trackSegments.Count}");
            tileGO.transform.SetParent(transform);
            tileGO.transform.position = position;
            tileGO.transform.eulerAngles = new Vector3(90f, 0f, 0f); // Lie flat on XZ plane

            var sr = tileGO.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = -1;

            // Scale sprite to exactly fill tile size (center-aligned, no overlap)
            float spriteW = sprite.bounds.size.x;
            float spriteH = sprite.bounds.size.y;
            if (spriteW > 0.001f && spriteH > 0.001f)
            {
                float scaleX = _tileWorldSize / spriteW;
                float scaleY = _tileWorldSize / spriteH;
                tileGO.transform.localScale = new Vector3(scaleX, scaleY, 1f);
            }

            _trackSegments.Add(tileGO);
        }

        #endregion

        #region Visual Controls

        /// <summary>
        /// Updates the rail color at runtime.
        /// </summary>
        public void SetRailColor(Color color)
        {
            _railColor = color;
            if (_trackMaterial != null)
            {
                _trackMaterial.color = _railColor;
            }
        }

        /// <summary>
        /// Updates the track width (cylinder X/Z scale) at runtime.
        /// </summary>
        public void SetTrackWidth(float width)
        {
            _trackWidth = width;
            foreach (GameObject segment in _trackSegments)
            {
                if (segment == null) continue;
                Vector3 scale = segment.transform.localScale;
                scale.x = _trackWidth;
                scale.z = _trackWidth;
                segment.transform.localScale = scale;
            }
        }

        #endregion

        #region Private Methods

        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            RefreshPath();
        }

        #endregion
    }
}
