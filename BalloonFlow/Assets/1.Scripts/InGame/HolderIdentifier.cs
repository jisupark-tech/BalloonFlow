using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// Attach to Holder GameObjects to identify them during raycasting.
    /// Animator 연동: Deploy(bool) = 배포 시작, end(trigger) = 배포 완료.
    /// Dart 자식 관리: Inspector에서 할당한 Dart Transform[]을 보여주고/날림.
    /// </summary>
    /// <remarks>
    /// MUST be in its own file (HolderIdentifier.cs) for Unity prefab serialization.
    /// Unity requires MonoBehaviour class name == file name for script GUID resolution.
    /// </remarks>
    public class HolderIdentifier : MonoBehaviour
    {
        [SerializeField] private int _holderId;

        [SerializeField]
        private Animator _animator;
        [SerializeField]
        [Tooltip("Frozen Dart Box 전용 Animator. 미할당 시 BoxFrozen 하위 Animator를 자동 탐색합니다.")]
        private Animator _frozenAnimator;
        private static readonly int _animDeploy = Animator.StringToHash("Deploy");
        private static readonly int _animEnd = Animator.StringToHash("end");
        private static readonly int _animHidden = Animator.StringToHash("Hidden");
        private static readonly int _animHiddenEnd = Animator.StringToHash("HiddenEnd");
        private static readonly int _animClick = Animator.StringToHash("Click");
        private static readonly int _animOpenHold = Animator.StringToHash("openHold");
        private static readonly int _animStateBoxOpenDefault = Animator.StringToHash("BoxOpenDefault");
        private static readonly int _animStateBoxOpenIdle = Animator.StringToHash("BoxOpenIdle");
        private static readonly int _animStateBoxClick = Animator.StringToHash("BoxClick");
        private static readonly int _animStateBoxDefault = Animator.StringToHash("BoxDefault");
        private static readonly int _animStateBoxClose = Animator.StringToHash("BoxClose");
        private static readonly int _animBoxFrozenHit = Animator.StringToHash("BoxFrozenHit");
        private const float MAG_DECREASE_IDLE_TIMEOUT = 0.22f;
        private const float BOX_STATE_CROSSFADE = 0.03f;
        // BoxOpen.anim m_StopTime 기준 — 변경 시 본 상수도 동기화 필요.
        private const float BOX_OPEN_ANIM_DURATION = 0.333f;
        // 명세(2026-06-15): 매거진 숫자 감소는 BoxOpen 진행률 60% 시점부터 시작.
        private const float MAGAZINE_DECREMENT_START_RATIO = 0.6f;

        [Header("[Dart Visuals — Inspector에서 할당]")]
        [SerializeField] private Transform[] _dartSlots;

        [Header("[Box Visuals — Inspector에서 할당]")]
        [Tooltip("일반 상태 박스")]
        [SerializeField] private GameObject _box;
        [Tooltip("Frozen(Ice) 상태 박스")]
        [SerializeField] private GameObject _boxFrozen;
        [Tooltip("Frozen 해동 이펙트 (ParticleFrozenExplosion)")]
        [SerializeField] private GameObject _frozenExplosionEffect;
        [Tooltip("Frozen Box 전용 Material. 미할당/기본 Material 상태일 때 런타임 fallback으로 사용.")]
        [SerializeField] private Material _frozenBoxMaterial;

        [Header("[Hidden 기믹 — Inspector에서 할당]")]
        [Tooltip("Hidden Body용 Material (색상 숨김)")]
        [SerializeField] private Material _hiddenBodyMaterial;
        [Tooltip("Hidden Lid용 Material (색상 숨김)")]
        [SerializeField] private Material _hiddenLidMaterial;
        [Tooltip("Hidden → Normal 전환 시 1회 재생하는 파티클 (평상시 비활성)")]
        [SerializeField] private GameObject _hiddenAppearParticle;

        [Header("[색상 적용 대상 Renderer — Inspector에서 할당]")]
        [Tooltip("Box Body, Handle, Dart Body 등 색상만 적용할 Renderer")]
        [SerializeField] private Renderer[] _colorRenderers;
        [Tooltip("색상 Renderer의 기반 Material (BalloonShared). 이것을 복제하여 색상만 변경")]
        [SerializeField] private Material _colorBaseMaterial;

        // ROLLBACK_SPAWNER_MATERIAL_SWAP_20260624 (#6): Pipe(Spawner_O)=불투명 / Glass Pipe(Spawner_T)=투명.
        // MPB 알파만으론 불투명 머티리얼이 안 비치므로 머티리얼 자체를 스왑한다. 인스펙터에서 할당:
        [Header("[Spawner 머티리얼 — Pipe=불투명 / Glass Pipe=투명]")]
        [Tooltip("Pipe(Spawner_O) 불투명 머티리얼 (SpawnerOriginal). 인스펙터 할당.")]
        [SerializeField] private Material _spawnerOpaqueMat;
        [Tooltip("Glass Pipe(Spawner_T) 투명 머티리얼 (SpawnerAlpha). 인스펙터 할당.")]
        [SerializeField] private Material _spawnerTransparentMat;
        // ROLLBACK_SPAWNER_OUTER_GLASS_MAT_20260701:
        // OuterGlass can be excluded from _colorRenderers on the prefab, so bind it explicitly.
        [Tooltip("Spawner/OuterGlass Renderer. Pipe(Spawner_O)=Opaque Mat, Glass Pipe(Spawner_T)=Transparent Mat.")]
        [SerializeField] private Renderer _spawnerOuterGlassRenderer;
        [Tooltip("Spawner 소멸 이펙트 (EndParticle). 미할당 시 자식 'EndParticle' 자동 탐색.")]
        [SerializeField] private GameObject _spawnerEndParticle;
        // ROLLBACK_SPAWNER_MAGAZINE_TEXT_SERIALIZE_20260624: 카운트/탄창 텍스트를 인스펙터에서 명시 할당.
        [Tooltip("카운트/탄창 텍스트 (MagazineText). 미할당 시 자식 TMP_Text 자동 탐색.")]
        [SerializeField] private TMPro.TMP_Text _magazineText;
        public TMPro.TMP_Text MagazineText => _magazineText;

        [Header("[별도 Material 대상 — Lid 등]")]
        [Tooltip("BoxLidShared 등 별도 Material을 유지하면서 색상만 바꿀 Renderer")]
        [SerializeField] private Renderer[] _customMatRenderers;
        [Tooltip("기반 Material (BoxLidShared 등). 이것을 복제하여 색상만 변경")]
        [SerializeField] private Material _customBaseMaterial;

        [Header("[Chain 기믹 — Loop 오브젝트]")]
        [Tooltip("Chain 연결 시 활성화할 Loop 오브젝트")]
        [SerializeField] private GameObject _chainLoop;

        [Header("[Hand Booster Highlight — Inspector에서 할당]")]
        [Tooltip("Hand(SelectTool) 부스터 활성 시 박스에 표시할 Stroke 오브젝트")]
        [SerializeField] private GameObject _controlBoxStroke;

        private Tweener _strokeIdleTween;
        private Vector3 _baseStrokeScale;
        private bool _strokeScaleCached;

        /// <summary>다음에 날릴 Dart 슬롯 인덱스</summary>
        private int _nextDartIndex;

        // StartDeploy 호출 시각 — BoxOpen 진행률 게이트 기준점. -1f = 미시작.
        private float _boxOpenStartTime = -1f;

        /// <summary>전체 매거진 수 (비율 계산용)</summary>
        private int _totalMagazine;

        /// <summary>남은 매거진 수</summary>
        private int _remainingMagazine;

        /// <summary>원래 로컬 위치 저장 (풀 원복용)</summary>
        private Vector3[] _dartLocalPositions;
        /// <summary>원래 부모 저장 (Dart가 Box 자식일 수 있음)</summary>
        private Transform[] _dartOriginalParents;
        /// <summary>[2026-05-13] 원래 로컬 스케일 저장 — LaunchNextDart 의 SetParent(null)
        /// 에서 worldScale 흡수로 dart localScale 누적되는 버그 ResetDarts 에서 복원.</summary>
        private Vector3[] _dartLocalScales;
        private bool _isFrozenVisual;
        private bool _isHidden;
        private readonly Dictionary<Renderer, Material[]> _frozenMaterialRestore = new Dictionary<Renderer, Material[]>();
        private Transform _frozenEffectOriginalParent;
        private Vector3 _frozenEffectOriginalLocalPosition;
        private Quaternion _frozenEffectOriginalLocalRotation;
        private bool _frozenEffectTransformCached;
        private Coroutine _frozenBreakFxRoutine;

        /// <summary>The unique identifier for this holder.</summary>
        public int HolderId => _holderId;

        /// <summary>Inspector에서 할당한 Dart 슬롯 수.</summary>
        public int DartSlotCount => _dartSlots != null ? _dartSlots.Length : 0;

        /// <summary>Animator 초기화. 외부에서 명시적으로 호출.</summary>
        public void Init()
        {
            if (_animator == null)
                _animator = GetComponent<Animator>();

            if (_animator == null)
                _animator = GetComponentInChildren<Animator>();

            // Holder prefab 은 Animator 가 2개+. 모든 Animator 에 culling 적용 (이전 패치는 첫 번째만 처리)
            // 풍선 N개 × Holder Animator 2 = N×2 의 매 프레임 evaluate 부하 차단.
            var allAnimators = GetComponentsInChildren<Animator>(includeInactive: true);
            for (int i = 0; i < allAnimators.Length; i++)
                allAnimators[i].cullingMode = AnimatorCullingMode.CullCompletely;

            // Box/BoxFrozen 미할당 시 자동 탐색 (자식 깊이 탐색)
            if (_box == null)
            {
                var t = transform.Find("Box") ?? FindDeep(transform, "Box");
                if (t != null) _box = t.gameObject;
            }
            if (_boxFrozen == null)
            {
                var t = transform.Find("BoxFrozen") ?? FindDeep(transform, "BoxFrozen");
                if (t != null) _boxFrozen = t.gameObject;
            }
            if (_frozenAnimator == null && _boxFrozen != null)
                _frozenAnimator = _boxFrozen.GetComponentInChildren<Animator>(true);

            // Dart Slots 미할당 시 자동 수집 (fallback)
            if (_dartSlots == null || _dartSlots.Length == 0)
            {
                var found = new System.Collections.Generic.List<Transform>();
                foreach (Transform child in transform)
                {
                    if (child.name.StartsWith("Dart"))
                        found.Add(child);
                }
                if (found.Count > 0)
                {
                    _dartSlots = found.ToArray();
                    Debug.Log($"[HolderIdentifier] Holder {_holderId}: Auto-collected {_dartSlots.Length} Dart children");
                }
            }

            // Dart 원래 위치 + 부모 + 스케일 캐시 (최초 1회)
            if (_dartSlots != null && _dartLocalPositions == null)
            {
                _dartLocalPositions = new Vector3[_dartSlots.Length];
                _dartOriginalParents = new Transform[_dartSlots.Length];
                _dartLocalScales = new Vector3[_dartSlots.Length];
                for (int i = 0; i < _dartSlots.Length; i++)
                {
                    if (_dartSlots[i] != null)
                    {
                        _dartLocalPositions[i] = _dartSlots[i].localPosition;
                        _dartOriginalParents[i] = _dartSlots[i].parent;
                        _dartLocalScales[i] = _dartSlots[i].localScale;
                    }
                }
            }

            // HiddenAppearParticle baseline 보장 — 프리팹 활성/PlayOnAwake로 인한 스폰 직후 오발화 차단.
            // SetHolderId→Init이 obj.SetActive(true) 직후 동일 프레임 동기 실행되므로 렌더 전에 비활성화됨.
            GameObject hiddenFx = ResolveHiddenAppearParticle();
            if (hiddenFx != null)
            {
                var baselineParticles = hiddenFx.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < baselineParticles.Length; i++)
                    baselineParticles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                hiddenFx.SetActive(false);
            }
        }

        /// <summary>Sets the holder ID (used by editor setup).</summary>
        public void SetHolderId(int id)
        {
            _holderId = id;
            Init();
        }

        #region Dart Visual Management

        /// <summary>
        /// magazineCount에 맞게 Dart를 보여줌.
        /// 슬롯 수보다 매거진이 많으면 전부 활성 (비율 기반으로 줄여감).
        /// </summary>
        public void ShowDarts(int magazineCount)
        {
            if (_dartSlots == null || _dartSlots.Length == 0)
            {
                Debug.LogWarning($"[HolderIdentifier] Holder {_holderId}: _dartSlots 미할당! Inspector에서 Dart 오브젝트를 Dart Slots에 드래그하세요.");
                return;
            }

            _totalMagazine = magazineCount;
            _remainingMagazine = magazineCount;
            _nextDartIndex = 0;

            // 전체 활성화 (비율 기반이므로 처음엔 전부 보임)
            for (int i = 0; i < _dartSlots.Length; i++)
            {
                if (_dartSlots[i] != null)
                    _dartSlots[i].gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// 다트 1발 소모. 비율에 맞춰 Dart 슬롯 하나를 날림.
        /// ex) 매거진 25, 슬롯 12 → 약 2발마다 1개 날아감.
        /// </summary>
        /// <returns>Dart 비주얼이 날아갔으면 true</returns>
        public bool LaunchNextDart(Vector3 targetWorldPos, float duration = 0.15f)
        {
            if (_dartSlots == null || _dartSlots.Length == 0)
            {
                Debug.LogWarning($"[HolderIdentifier] Holder {_holderId}: LaunchNextDart 실패 — _dartSlots 미할당");
                return false;
            }

            _remainingMagazine--;

            // 비율 기반: 현재 남은 매거진에 대응하는 보여야 할 슬롯 수
            int slotsTotal = _dartSlots.Length;
            int shouldShow = _totalMagazine > 0
                ? Mathf.CeilToInt((float)_remainingMagazine / _totalMagazine * slotsTotal)
                : 0;

            // 현재 보관함 안에 남아있는 활성 Dart 수
            int currentActive = 0;
            for (int i = 0; i < slotsTotal; i++)
            {
                if (_dartSlots[i] != null && _dartSlots[i].gameObject.activeSelf
                    && _dartSlots[i].IsChildOf(transform))
                    currentActive++;
            }

            // 줄여야 하는 수만큼만 날림
            if (currentActive <= shouldShow) return false;

            // 뒤쪽(높은 인덱스)부터 날림 — 앞쪽이 마지막까지 남음
            for (int i = slotsTotal - 1; i >= 0; i--)
            {
                Transform dart = _dartSlots[i];
                if (dart == null || !dart.gameObject.activeSelf) continue;
                if (!dart.IsChildOf(transform)) continue; // 이미 날아간 것

                // 부모에서 분리 → 월드 좌표 유지
                Vector3 startPos = dart.position;
                dart.SetParent(null);
                dart.position = startPos;

                // 포물선: 중간점을 위로 올림
                Vector3 midPoint = (startPos + targetWorldPos) * 0.5f;
                midPoint.y += Vector3.Distance(startPos, targetWorldPos) * 0.5f;

                Vector3[] path = { startPos, midPoint, targetWorldPos };
                dart.DOPath(path, duration, PathType.CatmullRom)
                    .SetEase(Ease.OutQuad)
                    .OnComplete(() =>
                    {
                        if (dart != null)
                            dart.gameObject.SetActive(false);
                    });

                return true;
            }

            return false;
        }

        /// <summary>
        /// 풀 반환 시: 모든 Dart를 다시 보관함에 붙이고, 원래 위치/스케일로 복원, 활성화.
        /// </summary>
        public void ResetDarts()
        {
            if (_dartSlots == null) return;

            for (int i = 0; i < _dartSlots.Length; i++)
            {
                if (_dartSlots[i] == null) continue;

                _dartSlots[i].DOKill();

                // 원래 부모로 복원 (Box 자식이었으면 Box로). worldPositionStays=false 로 부모 스케일을 localScale 에 흡수시키지 않음.
                // 이전: SetParent(originalParent) 기본 worldPositionStays=true → LaunchNextDart 의 SetParent(null) 과 합쳐
                // dart.localScale 에 holder DOPunchScale 값이 점진 누적되어 게임 반복 시 다트가 점점 커짐.
                Transform originalParent = (_dartOriginalParents != null && i < _dartOriginalParents.Length && _dartOriginalParents[i] != null)
                    ? _dartOriginalParents[i]
                    : transform;
                _dartSlots[i].SetParent(originalParent, worldPositionStays: false);

                if (_dartLocalPositions != null && i < _dartLocalPositions.Length)
                    _dartSlots[i].localPosition = _dartLocalPositions[i];

                // [2026-05-13] localScale 복원 — Init() 시 캐시한 원본 스케일.
                if (_dartLocalScales != null && i < _dartLocalScales.Length)
                    _dartSlots[i].localScale = _dartLocalScales[i];

                _dartSlots[i].gameObject.SetActive(true);
            }

            _nextDartIndex = 0;
        }

        #endregion

        #region Blur / Unselected State

        private static readonly int _propBlurAmount = Shader.PropertyToID("_BlurAmount");
        private static readonly int _propBlurColor = Shader.PropertyToID("_BlurColor");
        private static readonly int _propOutlineEnabled = Shader.PropertyToID("_OutlineEnabled");
        private static readonly int _propOutlineColor = Shader.PropertyToID("_OutlineColor");
        // [2026-05-11] _Color → _BaseColor 로 변경. ItemShared shader 의 [MainColor] property name 매칭.
        // 원본: private static readonly int _propMainColor = Shader.PropertyToID("_Color");
        private static readonly int _propMainColor = Shader.PropertyToID("_BaseColor");
        private static MaterialPropertyBlock _sharedMPB;
        // ROLLBACK_HOLDER_OUTLINE_SWAP_20260609 (Option A): 홀더 아웃라인 = MPB → 머티리얼 swap.
        //   ItemShared 는 single-pass 유지(배칭) + MPB 가 배칭 깸 → outline ON 상태에선 _colorRenderers/_customMatRenderers 를
        //   ItemSharedOutline 트윈으로 교체, OFF 면 원본 복원. 홀더 소수(~28)만 multi-pass 개별 draw → 풍선 1500 무손상.
        //   롤백: ApplyMPBToAll/ClearMPBFromAll 을 MPB 버전으로 원복 + 이 필드/헬퍼 제거.
        private bool _outlineSwapActive;
        private Material[] _outlineOrigColor;
        private Material[] _outlineOrigCustom;

        /// <summary>
        /// 미선택 상태: 흰색 블러 오버레이 + 흰색 아웃라인.
        /// 모든 Renderer에 MaterialPropertyBlock 적용.
        /// </summary>
        public void SetUnselected(bool unselected)
        {
            // ROLLBACK_HOLDER_FIRSTROW_ONLY_20260609: 아웃라인은 "첫 줄(SetActiveFrontRow)만"으로 제한.
            //   선택/미선택 상태는 outline 미관여 → 행 기반 결정 유지. (blur 는 셰이더 비활성이라 어차피 무효)
        }

        /// <summary>활성화 상태 (row 0): 검은색 아웃라인, 블러 없음, idle 애니메이션 재생.</summary>
        public void SetActiveFrontRow()
        {
            if (_sharedMPB == null) _sharedMPB = new MaterialPropertyBlock();
            ApplyMPBToAll(0f, Color.white, 1f, Color.black);
            if (_animator != null) _animator.enabled = true;
        }

        /// <summary>비활성화 상태 (row 1+): 아웃라인 없음, 블러 없음, idle 애니메이션 정지.</summary>
        public void SetInactiveRow()
        {
            if (_sharedMPB == null) _sharedMPB = new MaterialPropertyBlock();
            ApplyMPBToAll(0f, Color.white, 0f, Color.white);
            if (_animator != null) _animator.enabled = false;
        }

        /// <summary>선택됨 — ROLLBACK_HOLDER_FIRSTROW_ONLY_20260609: outline 은 행만 제어 → 선택은 미관여.</summary>
        public void SetSelected()
        {
        }

        /// <summary>Chain 연결 표시 — ROLLBACK_HOLDER_FIRSTROW_ONLY_20260609: 첫 줄만 아웃라인 → chain 은 outline 미사용(필요 시 별도 연출).</summary>
        public void SetChainHighlight(bool active)
        {
        }

        // ROLLBACK_HOLDER_OUTLINE_SWAP_20260609: 기존 MPB 적용 → 머티리얼 swap.
        //   outlineOn>0.5 → ItemSharedOutline 트윈으로 교체, 아니면 원본 복원. blur 는 셰이더 비활성이라 무시.
        //   (outline 색은 트윈 기본 검정 — front-row 검정. 흰색 등 색 분기는 단순화 위해 검정 통일.)
        private void ApplyMPBToAll(float blur, Color blurCol, float outlineOn, Color outlineCol)
        {
            SwapOutlineMaterial(outlineOn > 0.5f);
        }

        private void ClearMPBFromAll()
        {
            SwapOutlineMaterial(false);
        }

        private void SwapOutlineMaterial(bool on)
        {
            if (on == _outlineSwapActive) return;
            if (on)
            {
                _outlineOrigColor = SwapRenderersToTwin(_colorRenderers);
                _outlineOrigCustom = SwapRenderersToTwin(_customMatRenderers);
                _outlineSwapActive = true;
            }
            else
            {
                RestoreRenderers(_colorRenderers, _outlineOrigColor);
                RestoreRenderers(_customMatRenderers, _outlineOrigCustom);
                _outlineOrigColor = null;
                _outlineOrigCustom = null;
                _outlineSwapActive = false;
            }
        }

        // ROLLBACK_HOLDER_OUTLINE_SWAP_20260609: 공유 OutlineHull 머티리얼을 material[1] 로 추가(트윈 방식 폐기).
        //   material[0]=원본(배칭 유지), material[1]=공유 hull(외곽선들 한 배치). 복원 시 단일 머티리얼로.
        private static Material[] SwapRenderersToTwin(Renderer[] rends)
        {
            if (rends == null) return null;
            Material hull = BalloonFlow.BalloonController.GetOutlineHullMaterial();
            var orig = new Material[rends.Length];
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                orig[i] = rends[i].sharedMaterial;
                if (hull != null) rends[i].sharedMaterials = new Material[] { rends[i].sharedMaterial, hull };
            }
            return orig;
        }

        // ROLLBACK_HOLDER_OUTLINE_STALE_RESTORE_20260609: 캡처해둔 orig 복원 금지 — 풀 재사용 시 이전 스테이지 색 잔존 버그의 원인.
        //   swap-on 시점에 캡처한 orig[]는 그 스테이지의 색 머티리얼이라, 풀에서 다른 색으로 재사용된 홀더에 복원하면 이전 색이 다시 칠해짐.
        //   ApplyColor 가 현재 색을 element[0]에 유지하므로(.sharedMaterial 단일 setter 는 [0]만 교체, hull[1] 유지),
        //   복원은 "현재 [0]을 그대로 두고 hull([1])만 제거"로 처리한다. (orig 파라미터는 호환 위해 유지하되 미사용)
        private static void RestoreRenderers(Renderer[] rends, Material[] orig)
        {
            if (rends == null) return;
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                Material[] arr = rends[i].sharedMaterials;
                if (arr != null && arr.Length > 1)
                    rends[i].sharedMaterials = new Material[] { arr[0] };
            }
        }

        #endregion

        #region Color

        /// <summary>
        /// Inspector에서 지정한 Renderer들에만 색상 Material 적용.
        /// _colorRenderers: ItemShared 색상만 (Body, Handle, Dart Body)
        /// _customMatRenderers: 별도 Material 기반으로 색상만 변경 (Lid 등)
        /// </summary>
        public void ApplyColor(Color color)
        {
            // Hidden→Normal material 실제 전환 시에만 1회 재생 — _isHidden 플래그가 아니라 swap 전 sharedMaterial 비교로 판정.
            bool wasHiddenMaterial = false;
            if (_hiddenBodyMaterial != null && _colorRenderers != null)
            {
                for (int i = 0; i < _colorRenderers.Length; i++)
                {
                    if (_colorRenderers[i] != null && _colorRenderers[i].sharedMaterial == _hiddenBodyMaterial)
                    {
                        wasHiddenMaterial = true;
                        break;
                    }
                }
            }

            int colorCount = _colorRenderers != null ? _colorRenderers.Length : 0;
            int customCount = _customMatRenderers != null ? _customMatRenderers.Length : 0;
            bool hasBase = _customBaseMaterial != null;
            Debug.Log($"[HolderIdentifier] Holder {_holderId} ApplyColor: colorRenderers={colorCount}, customMatRenderers={customCount}, baseMat={hasBase}");

            // BoxHiddenBody → BoxBodyShared 클론 전이 감지를 위해 shared 를 메서드 스코프로 끌어올림.
            Material shared = null;

            // 일반 색상 Renderer — customMatRenderers에 포함된 것은 제외
            if (_colorRenderers != null && _colorRenderers.Length > 0)
            {
                // 기반 Material이 지정되어 있으면 복제+색상변경 (Outline/Metallic 유지)
                if (_colorBaseMaterial != null)
                    shared = GetOrCreateClonedVariant(_colorBaseMaterial, color);
                else
                    shared = BalloonController.GetOrCreateSharedMaterial(color);

                if (shared != null)
                {
                    for (int i = 0; i < _colorRenderers.Length; i++)
                    {
                        if (_colorRenderers[i] == null) continue;
                        if (IsInCustomRenderers(_colorRenderers[i])) continue;
                        _colorRenderers[i].sharedMaterial = shared;
                    }
                }
            }

            // 별도 Material 기반 Renderer (BoxLidShared 등)
            if (_customMatRenderers != null && _customMatRenderers.Length > 0 && _customBaseMaterial != null)
            {
                Material cloned = GetOrCreateClonedVariant(_customBaseMaterial, color);
                if (cloned != null)
                {
                    for (int i = 0; i < _customMatRenderers.Length; i++)
                    {
                        if (_customMatRenderers[i] != null)
                            _customMatRenderers[i].sharedMaterial = cloned;
                    }
                }
            }

            // _isHidden==true(게임 중 Hidden 상태였음) + 실제 material swap 발생 시에만 1회 재생. 풀 재사용으로 sharedMaterial이 stale인 경우 _isHidden=false라 차단됨.
            if (_isHidden && wasHiddenMaterial && _colorBaseMaterial != null && shared != null)
            {
                _isHidden = false;
                PlayHiddenAppearEffect();
            }
            else if (wasHiddenMaterial)
            {
                _isHidden = false;
            }
        }

        /// <summary>색상 적용 대상이 할당되었는지.</summary>
        public bool HasColorRenderers =>
            (_colorRenderers != null && _colorRenderers.Length > 0) ||
            (_customMatRenderers != null && _customMatRenderers.Length > 0);

        /// <summary>
        /// _customBaseMaterial 기반 색상별 Material 클론 캐시.
        /// Normal Map, Smoothness 등 기반 Material 설정 유지 + 색상만 변경.
        /// </summary>
        // ROLLBACK_HOLDER_MATCACHE_KEY_20260609: (baseMat,color) 복합 키.
        //   이전 `instanceID ^ color.GetHashCode()` XOR 단일 int 키는 서로 다른 (baseMat,color) 조합이 충돌 가능 →
        //   홀더가 여러 베이스 머티리얼을 한 static 캐시에 섞어 쓰므로 일부 홀더 색이 다른 색으로 표시됨(빌드마다 InstanceID 가 달라 빌드에서만/일부만 재현).
        //   튜플 키는 Dictionary 가 Equals 로 두 요소를 모두 비교 → 거짓 히트 불가.
        private static readonly Dictionary<(int, Color), Material> _customMatCache = new Dictionary<(int, Color), Material>();

        private bool IsInCustomRenderers(Renderer r)
        {
            if (_customMatRenderers == null) return false;
            for (int i = 0; i < _customMatRenderers.Length; i++)
            {
                if (_customMatRenderers[i] == r) return true;
            }
            return false;
        }

        /// <summary>
        /// 기반 Material을 복제하여 색상만 변경. 나머지 설정(Outline, Metallic 등) 유지.
        /// 색상+Material 조합별 캐시.
        /// </summary>
        private static Material GetOrCreateClonedVariant(Material baseMat, Color color)
        {
            // [Defense 2026-05-11] baseMat 인자 null 방어 — caller 가 보장 안 하면 NRE.
            if (baseMat == null)
            {
                Debug.LogWarning("[HolderIdentifier] GetOrCreateClonedVariant called with null baseMat.");
                return null;
            }

            // ROLLBACK_HOLDER_MATCACHE_KEY_20260609: XOR 단일 int 키(충돌 가능) → (instanceID, color) 튜플 키.
            var key = (baseMat.GetInstanceID(), color);
            if (_customMatCache.TryGetValue(key, out Material cached))
                return cached;

            Material clone = new Material(baseMat);
            clone.SetColor("_BaseColor", color);
            // ROLLBACK_HOLDER_VARIANT_STRIP_20260609: instancing 강제 ON 제거 (빌드 색 오류의 진짜 원인).
            //   baseMat 가 _NORMALMAP/_EMISSION(shader_feature) 를 켠 경우(BoxLidShared/IronBox), 런타임에서 instancing 을 더하면
            //   "_NORMALMAP(+_EMISSION) + INSTANCING" 조합 variant 가 필요한데 이를 참조하는 에셋이 없어 빌드에서 strip →
            //   잘못된 variant/fallback 로 렌더되어 색이 틀림(에디터는 on-demand 컴파일이라 정상). 다트/풍선은 _NORMALMAP OFF 라 무관.
            //   new Material(baseMat) 가 baseMat 의 instancing 설정을 복사 → 클론 variant == 에셋 variant → 빌드에 항상 포함.
            //   (홀더는 소수라 instancing 손실 무시 가능. 롤백: 아래 한 줄 복원.)
            // clone.enableInstancing = true;
            _customMatCache[key] = clone;
            return clone;
        }

        #endregion

        #region Hidden Visual

        /// <summary>Hidden 상태 적용 — body/lid를 Hidden Material로 교체.</summary>
        public void SetHidden(bool hidden)
        {
            _isHidden = hidden;
            if (hidden)
            {
                // Hidden Material 적용 (색상 숨김)
                if (_hiddenBodyMaterial != null && _colorRenderers != null)
                {
                    for (int i = 0; i < _colorRenderers.Length; i++)
                    {
                        if (_colorRenderers[i] != null)
                            _colorRenderers[i].sharedMaterial = _hiddenBodyMaterial;
                    }
                }
                if (_hiddenLidMaterial != null && _customMatRenderers != null)
                {
                    for (int i = 0; i < _customMatRenderers.Length; i++)
                    {
                        if (_customMatRenderers[i] != null)
                            _customMatRenderers[i].sharedMaterial = _hiddenLidMaterial;
                    }
                }
            }
            // hidden=false일 때는 ApplyColor가 호출되어 원래 색상 복원
        }

        #endregion

        #region Box / Frozen Visual

        /// <summary>
        /// Frozen 상태 설정. true면 BoxFrozen 활성 + Box 비활성.
        /// </summary>
        /// <summary>Frozen 상태 설정. frozen=true → BoxFrozen 활성, Box 비활성.</summary>
        public void SetFrozen(bool frozen, bool playBreakEffect = true)
        {
            if (frozen && _boxFrozen == null)
                Debug.LogWarning($"[HolderIdentifier] Holder {_holderId}: _boxFrozen 미할당! Inspector에서 BoxFrozen 오브젝트를 드래그하세요.");

            bool wasFrozen = _isFrozenVisual || (_boxFrozen != null && _boxFrozen.activeSelf);
            _isFrozenVisual = frozen;

            if (playBreakEffect && !frozen && wasFrozen)
                PlayFrozenBreakEffect();

            if (_box != null) _box.SetActive(!frozen || _boxFrozen == null);
            if (_boxFrozen != null)
            {
                _boxFrozen.SetActive(frozen);
                if (frozen) ApplyFrozenMaterialFallback(_boxFrozen);
            }
            else if (frozen && _box != null)
            {
                ApplyFrozenMaterialFallback(_box);
            }

            if (!frozen)
                RestoreFrozenMaterialFallback();
        }

        /// <summary>풀 반환 시 Box 상태 초기화 (일반 상태로).</summary>
        public void ResetBox()
        {
            _isFrozenVisual = false;
            _isHidden = false;
            RestoreFrozenMaterialFallback();
            RestoreFrozenEffectTransform();
            if (_box != null) _box.SetActive(true);
            if (_boxFrozen != null) _boxFrozen.SetActive(false);
            StopFrozenBreakEffect();
            GameObject hiddenFx = ResolveHiddenAppearParticle();
            if (hiddenFx != null) hiddenFx.SetActive(false);
            SetControlBoxStrokeActive(false);
        }

        public void StopFrozenBreakEffect()
        {
            if (_frozenExplosionEffect == null) return;

            if (_frozenBreakFxRoutine != null)
            {
                StopCoroutine(_frozenBreakFxRoutine);
                _frozenBreakFxRoutine = null;
            }

            var particles = _frozenExplosionEffect.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] == null) continue;
                particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            _frozenExplosionEffect.SetActive(false);
        }

        private void PlayFrozenBreakEffect()
        {
            if (_frozenExplosionEffect != null)
            {
                // ROLLBACK_FROZEN_BOX_HIT_FX_20260626:
                // Breaking a Frozen Dart Box must let ParticleFrozenExplosion finish naturally.
                // StopFrozenBreakEffect remains for pool/reset cleanup only; this path never clears
                // the particle after play until all child ParticleSystems report IsAlive(false).
                if (_frozenBreakFxRoutine != null)
                {
                    StopCoroutine(_frozenBreakFxRoutine);
                    _frozenBreakFxRoutine = null;
                }

                CacheFrozenEffectTransform();
                Vector3 worldPosition = _frozenExplosionEffect.transform.position;
                Quaternion worldRotation = _frozenExplosionEffect.transform.rotation;
                _frozenExplosionEffect.transform.SetParent(transform, true);
                _frozenExplosionEffect.transform.SetPositionAndRotation(worldPosition, worldRotation);
                _frozenExplosionEffect.SetActive(true);
                var particles = _frozenExplosionEffect.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < particles.Length; i++)
                {
                    particles[i].Clear(true);
                    particles[i].Play(true);
                }

                if (isActiveAndEnabled)
                    _frozenBreakFxRoutine = StartCoroutine(DisableFrozenBreakEffectWhenFinished(particles));
            }

            transform.DOPunchScale(Vector3.one * 0.14f, 0.26f, 8, 0.72f);
            transform.DOShakeRotation(0.22f, new Vector3(0f, 8f, 0f), 8, 55f);
        }

        private System.Collections.IEnumerator DisableFrozenBreakEffectWhenFinished(ParticleSystem[] particles)
        {
            bool anyAlive = true;
            while (anyAlive)
            {
                anyAlive = false;
                if (particles != null)
                {
                    for (int i = 0; i < particles.Length; i++)
                    {
                        if (particles[i] != null && particles[i].IsAlive(true))
                        {
                            anyAlive = true;
                            break;
                        }
                    }
                }

                if (anyAlive)
                    yield return null;
            }

            if (_frozenExplosionEffect != null)
                _frozenExplosionEffect.SetActive(false);
            RestoreFrozenEffectTransform();
            _frozenBreakFxRoutine = null;
        }

        private void PlayHiddenAppearEffect()
        {
            GameObject fx = ResolveHiddenAppearParticle();
            if (fx == null)
            {
                // 인스펙터 미할당 폴백 — 계층에서 "HiddenAppearParticle" GO 탐색(비활성 포함).
                var all = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].name == "HiddenAppearParticle") { fx = all[i].gameObject; break; }
                }
            }
            if (fx == null)
            {
                Debug.LogWarning($"[Holder {_holderId}] HiddenAppearParticle 미할당/미발견 — 파티클 재생 불가. 홀더 프리팹 인스펙터의 _hiddenAppearParticle 슬롯을 확인하세요.", this);
                return;
            }

            fx.SetActive(false);
            fx.SetActive(true);
            var particles = fx.GetComponentsInChildren<ParticleSystem>(true);
            if (particles.Length == 0)
            {
                // 구조 문제: GO 는 있는데 실제 ParticleSystem 이 없음 (빈 컨테이너).
                Debug.LogWarning($"[Holder {_holderId}] '{fx.name}' 하위에 ParticleSystem 이 없습니다 — 파티클 이펙트가 프리팹에 미배치된 구조입니다.", fx);
                return;
            }
            var diag = new System.Text.StringBuilder();
            diag.Append($"[Holder {_holderId}] HiddenAppear FX='{fx.name}' active={fx.activeInHierarchy} pos={fx.transform.position} lossyScale={fx.transform.lossyScale} PS수={particles.Length}");
            for (int i = 0; i < particles.Length; i++)
            {
                var ps = particles[i];
                ps.Clear(true);   // 잔여 파티클 제거 후 깨끗하게 1회 재생
                ps.Play(true);

                var main = ps.main;
                var rend = ps.GetComponent<ParticleSystemRenderer>();
                bool rendOn = rend != null && rend.enabled;
                string matName = (rend != null && rend.sharedMaterial != null) ? rend.sharedMaterial.name : "NULL";
                string shader = (rend != null && rend.sharedMaterial != null && rend.sharedMaterial.shader != null) ? rend.sharedMaterial.shader.name : "NULL";
                diag.Append($"\n  · '{ps.gameObject.name}' goActive={ps.gameObject.activeInHierarchy} isPlaying={ps.isPlaying} " +
                            $"startSize={main.startSize.constant} alpha={main.startColor.color.a} maxParticles={main.maxParticles} " +
                            $"emission={ps.emission.enabled} renderer={rend != null}/enabled={rendOn} mat={matName} shader={shader} " +
                            $"sortLayer={(rend != null ? rend.sortingLayerName : "-")} order={(rend != null ? rend.sortingOrder : 0)}");
            }
            Debug.Log(diag.ToString(), fx);
        }

        private void ApplyFrozenMaterialFallback(GameObject root)
        {
            if (root == null) return;
            Material mat = _frozenBoxMaterial != null ? _frozenBoxMaterial : GetOrCreateFrozenFallbackMaterial();
            if (mat == null) return;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                Material[] shared = r.sharedMaterials;
                bool changed = false;
                for (int j = 0; j < shared.Length; j++)
                {
                    if (!NeedsFrozenMaterial(shared[j])) continue;
                    shared[j] = mat;
                    changed = true;
                }

                if (!changed) continue;
                if (!_frozenMaterialRestore.ContainsKey(r))
                    _frozenMaterialRestore[r] = r.sharedMaterials;
                r.sharedMaterials = shared;
            }
        }

        private void RestoreFrozenMaterialFallback()
        {
            foreach (var kvp in _frozenMaterialRestore)
            {
                if (kvp.Key != null)
                    kvp.Key.sharedMaterials = kvp.Value;
            }
            _frozenMaterialRestore.Clear();
        }

        private void CacheFrozenEffectTransform()
        {
            if (_frozenExplosionEffect == null || _frozenEffectTransformCached) return;
            Transform t = _frozenExplosionEffect.transform;
            _frozenEffectOriginalParent = t.parent;
            _frozenEffectOriginalLocalPosition = t.localPosition;
            _frozenEffectOriginalLocalRotation = t.localRotation;
            _frozenEffectTransformCached = true;
        }

        private void RestoreFrozenEffectTransform()
        {
            if (_frozenExplosionEffect == null || !_frozenEffectTransformCached) return;
            Transform t = _frozenExplosionEffect.transform;
            t.SetParent(_frozenEffectOriginalParent, false);
            t.localPosition = _frozenEffectOriginalLocalPosition;
            t.localRotation = _frozenEffectOriginalLocalRotation;
        }

        private static bool NeedsFrozenMaterial(Material mat)
        {
            if (mat == null) return true;
            string n = mat.name;
            return n.Contains("Default")
                || n.Contains("BoxBodyShared")
                || n.Contains("BoxLidShared")
                || n.Contains("PaintBox");
        }

        private static Material _runtimeFrozenFallbackMaterial;
        private static Material GetOrCreateFrozenFallbackMaterial()
        {
            if (_runtimeFrozenFallbackMaterial != null) return _runtimeFrozenFallbackMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                ?? Shader.Find("Standard");
            if (shader == null) return null;

            var mat = new Material(shader)
            {
                name = "RuntimeFrozenBoxFallback",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true
            };
            Color ice = new Color(0.46f, 0.86f, 1f, 0.92f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", ice);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", ice);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.05f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.82f);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", new Color(0.1f, 0.55f, 0.8f) * 0.45f);
            _runtimeFrozenFallbackMaterial = mat;
            return _runtimeFrozenFallbackMaterial;
        }

        #endregion

        #region Animator

        /// <summary>Hidden 상태 세팅 — Hidden=true.</summary>
        public void SetHiddenAnim(bool hidden)
        {
            if (_animator != null)
                _animator.SetBool(_animHidden, hidden);
        }

        /// <summary>Hidden 해금 — HiddenEnd 트리거.</summary>
        public void TriggerHiddenEnd()
        {
            if (_animator != null)
            {
                _animator.SetBool(_animHidden, false);
                _animator.SetTrigger(_animHiddenEnd);
            }
        }

        /// <summary>현재 state 가 BoxDefault 일 때만 BoxClick state 를 Play.</summary>
        public void TriggerClick()
        {
            if (_animator == null) return;

            bool wasEnabled = _animator.enabled;
            if (!wasEnabled) _animator.enabled = true;
            _animator.Update(0f);

            _animator.Play(_animStateBoxClick, 0, 0f);
            _animator.Update(0f);

            if (!wasEnabled)
            {
                if (_boxClickResetRoutine != null) StopCoroutine(_boxClickResetRoutine);
                if (isActiveAndEnabled)
                    _boxClickResetRoutine = StartCoroutine(RestoreAnimatorAfterBoxClick());
            }
        }

        public void TriggerFrozenHit()
        {
            Animator targetAnimator = _frozenAnimator != null ? _frozenAnimator : _animator;
            if (targetAnimator == null) return;

            // ROLLBACK_FROZEN_BOX_HIT_FX_20260626:
            // Frozen hit is only used while frozen HP remains. Final thaw/break does not call this.
            if (!HasAnimatorParameter(targetAnimator, _animBoxFrozenHit, AnimatorControllerParameterType.Trigger))
                return;

            if (!targetAnimator.enabled) targetAnimator.enabled = true;
            targetAnimator.ResetTrigger(_animBoxFrozenHit);
            targetAnimator.SetTrigger(_animBoxFrozenHit);
        }

        private static bool HasAnimatorParameter(Animator animator, int nameHash, AnimatorControllerParameterType type)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return false;

            var parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].nameHash == nameHash && parameters[i].type == type)
                    return true;
            }

            return false;
        }

        private Coroutine _boxClickResetRoutine;

        private System.Collections.IEnumerator RestoreAnimatorAfterBoxClick()
        {
            // Play 후 Update(0f) 했으므로 state length는 이미 BoxClick 기준.
            float length = _animator != null ? _animator.GetCurrentAnimatorStateInfo(0).length : 0.8f;
            if (length <= 0f) length = 0.8f;
            yield return new WaitForSeconds(length);
            if (_animator != null) _animator.enabled = false;
            _boxClickResetRoutine = null;
        }

        /// <summary>배포 시작 — Deploy=true.</summary>
        public void StartDeploy()
        {
            _boxOpenStartTime = Time.unscaledTime;
            if (_animator != null)
            {
                if (!_animator.enabled) _animator.enabled = true;
                _animator.SetBool(_animDeploy, true);
            }
        }

        /// <summary>BoxOpen 애니메이션이 60% 이상 진행되었는지(=숫자 감소 시작 가능 시점). StartDeploy 미호출 시 false.</summary>
        public bool IsReadyForMagazineDecrement()
        {
            if (_boxOpenStartTime < 0f) return false;
            return (Time.unscaledTime - _boxOpenStartTime) >= BOX_OPEN_ANIM_DURATION * MAGAZINE_DECREMENT_START_RATIO;
        }

        private Coroutine _magDecreaseRoutine;
        private Coroutine _magDecreaseDecayRoutine;
        private Coroutine _boxCloseRoutine;
        private float _magDecreaseLastTick;

        /// <summary>매거진 숫자가 감소 중임을 알림. openHold=true 로 두고 BoxOpenIdle 상태로 끌어올린 뒤
        /// idle-decay 타이머를 재시작. 타임아웃 동안 추가 호출이 없으면 BoxOpenDefault 로 복귀(remaining>0 한정).</summary>
        public void NotifyMagazineDecreasing()
        {
            if (_animator == null) return;
            if (!_animator.enabled) _animator.enabled = true;
            _animator.SetBool(_animOpenHold, true);
            _magDecreaseLastTick = Time.unscaledTime;

            if (_magDecreaseRoutine != null) StopCoroutine(_magDecreaseRoutine);
            if (isActiveAndEnabled)
                _magDecreaseRoutine = StartCoroutine(EnterBoxOpenIdleWhenReady());
            else
                _animator.Play(_animStateBoxOpenIdle, 0, 0f);

            if (_magDecreaseDecayRoutine != null) StopCoroutine(_magDecreaseDecayRoutine);
            if (isActiveAndEnabled)
                _magDecreaseDecayRoutine = StartCoroutine(MagazineDecreaseIdleDecay());
        }

        private System.Collections.IEnumerator EnterBoxOpenIdleWhenReady()
        {
            float timeout = 2f;
            while (timeout > 0f && _animator != null)
            {
                var info = _animator.GetCurrentAnimatorStateInfo(0);
                if (info.shortNameHash == _animStateBoxOpenIdle)
                {
                    _magDecreaseRoutine = null;
                    yield break;
                }
                if (info.shortNameHash == _animStateBoxOpenDefault)
                {
                    _animator.CrossFadeInFixedTime(_animStateBoxOpenIdle, BOX_STATE_CROSSFADE, 0);
                    _magDecreaseRoutine = null;
                    yield break;
                }
                // BoxOpen(뚜껑 원샷) 등 다른 state → 끝날 때까지 대기.
                timeout -= Time.deltaTime;
                yield return null;
            }
            if (_animator != null)
                _animator.CrossFadeInFixedTime(_animStateBoxOpenIdle, BOX_STATE_CROSSFADE, 0);
            _magDecreaseRoutine = null;
        }

        private System.Collections.IEnumerator MagazineDecreaseIdleDecay()
        {
            while (Time.unscaledTime - _magDecreaseLastTick < MAG_DECREASE_IDLE_TIMEOUT)
                yield return null;

            if (_animator != null)
            {
                _animator.SetBool(_animOpenHold, false);
                if (_remainingMagazine > 0)
                {
                    var info = _animator.GetCurrentAnimatorStateInfo(0);
                    if (info.shortNameHash != _animStateBoxOpenDefault)
                        _animator.CrossFadeInFixedTime(_animStateBoxOpenDefault, BOX_STATE_CROSSFADE, 0);
                }
            }
            _magDecreaseDecayRoutine = null;
        }

        /// <summary>매거진 0 도달 시 BoxClose 원샷 후 BoxDefault 로 복귀. idle-decay 코루틴은 중단.
        /// 이미 BoxClose/BoxDefault 진행 중이면 idempotent — no-op.</summary>
        public void PlayBoxCloseToDefault()
        {
            if (_animator == null) return;

            var info = _animator.GetCurrentAnimatorStateInfo(0);
            if (info.shortNameHash == _animStateBoxClose || info.shortNameHash == _animStateBoxDefault)
                return;

            if (_magDecreaseRoutine != null) { StopCoroutine(_magDecreaseRoutine); _magDecreaseRoutine = null; }
            if (_magDecreaseDecayRoutine != null) { StopCoroutine(_magDecreaseDecayRoutine); _magDecreaseDecayRoutine = null; }
            if (_boxCloseRoutine != null) { StopCoroutine(_boxCloseRoutine); _boxCloseRoutine = null; }

            _animator.SetBool(_animOpenHold, false);

            if (!isActiveAndEnabled)
            {
                if (!_animator.enabled) _animator.enabled = true;
                _animator.Play(_animStateBoxDefault, 0, 0f);
                return;
            }

            if (!_animator.enabled) _animator.enabled = true;
            _animator.Play(_animStateBoxClose, 0, 0f);
            _animator.Update(0f);
            _boxCloseRoutine = StartCoroutine(BoxCloseToDefaultRoutine());
        }

        private System.Collections.IEnumerator BoxCloseToDefaultRoutine()
        {
            float length = _animator != null ? _animator.GetCurrentAnimatorStateInfo(0).length : 0.5f;
            if (length <= 0f) length = 0.5f;
            yield return new WaitForSeconds(length);
            if (_animator != null)
                _animator.Play(_animStateBoxDefault, 0, 0f);
            _boxCloseRoutine = null;
        }

        /// <summary>호환 시그니처: onRail==true → NotifyMagazineDecreasing 으로 위임. onRail==false → idle-decay 강제 종료 후 BoxOpenDefault 복귀.</summary>
        public void SetDartsOnRail(bool onRail)
        {
            if (_animator == null) return;
            if (onRail)
            {
                NotifyMagazineDecreasing();
                return;
            }

            if (_magDecreaseRoutine != null) { StopCoroutine(_magDecreaseRoutine); _magDecreaseRoutine = null; }
            if (_magDecreaseDecayRoutine != null) { StopCoroutine(_magDecreaseDecayRoutine); _magDecreaseDecayRoutine = null; }
            if (!_animator.enabled) _animator.enabled = true;
            _animator.SetBool(_animOpenHold, false);
            _animator.CrossFadeInFixedTime(_animStateBoxOpenDefault, BOX_STATE_CROSSFADE, 0);
        }

        /// <summary>배포 완료 — Deploy=false + end 트리거.</summary>
        public void EndDeploy()
        {
            if (_animator != null)
            {
                _animator.SetBool(_animDeploy, false);
                _animator.SetTrigger(_animEnd);
            }
        }

#if BF_RAIL_HOLDER
        /// <summary>PROTO_RAIL_HOLDER_20260716: 발사 순간 상자 스케일 업/다운 펀치.
        /// 기존 얼음깨짐 연출(line 731)과 동일한 DOPunchScale 패턴을 발사용으로 재사용.
        /// 누적 방지: baseScale 기준 상대 펀치라 반복해도 커지지 않는다(DOPunchScale 는 원복 보장).</summary>
        public void PlayFireRecoilScale()
        {
            transform.DOComplete();   // 진행 중 펀치가 있으면 원복 후 재시작 — 스케일 누적 차단
            transform.DOPunchScale(Vector3.one * 0.18f, 0.14f, 6, 0.6f);
        }
#endif

        /// <summary>재사용 시 애니메이터 전체 리셋 (풀 반환 시 enabled 복원). 진행 중인 magazine FSM 코루틴 모두 중단.</summary>
        public void ResetAnimator()
        {
            if (_magDecreaseRoutine != null) { StopCoroutine(_magDecreaseRoutine); _magDecreaseRoutine = null; }
            if (_magDecreaseDecayRoutine != null) { StopCoroutine(_magDecreaseDecayRoutine); _magDecreaseDecayRoutine = null; }
            if (_boxCloseRoutine != null) { StopCoroutine(_boxCloseRoutine); _boxCloseRoutine = null; }
            if (_animator != null)
            {
                _animator.enabled = true;
                _animator.Rebind(); // 모든 상태/파라미터 초기화 → Entry 상태로 복귀
                // ROLLBACK_ANIMATOR_UPDATE_INACTIVE_GUARD_20260626: Animator.Update 는 active 오브젝트에서만 호출 가능.
                //   비활성(Frozen 아닌 홀더의 frozen 오버레이, 풀 재사용 중 미활성 등)에서 호출 시 "Can't call Animator.Update
                //   on inactive object" 에러 → 가드. 비활성은 렌더 안 되므로 스킵 무해(활성화 시 Rebind 상태 적용).
                if (_animator.gameObject.activeInHierarchy)
                    _animator.Update(0f);
            }
            if (_frozenAnimator != null && _frozenAnimator != _animator)
            {
                _frozenAnimator.enabled = true;
                _frozenAnimator.Rebind();
                // ROLLBACK_ANIMATOR_UPDATE_INACTIVE_GUARD_20260626: 위와 동일 — 비활성 frozen 애니메이터 Update 가드.
                if (_frozenAnimator.gameObject.activeInHierarchy)
                    _frozenAnimator.Update(0f);
            }
            _boxOpenStartTime = -1f;
        }

        #endregion

        #region Spawner Visual

        /// <summary>Spawner_T(Glass Pipe)=투명 / Spawner_O(Pipe)=불투명. 머티리얼 스왑.</summary>
        public void SetSpawnerTransparent(bool transparent)
        {
            // ROLLBACK_SPAWNER_MATERIAL_SWAP_20260624 (#6):
            // 이전엔 MaterialPropertyBlock 으로 _BaseColor 알파만 0.4/1 로 바꿨으나, 불투명 URP 머티리얼은
            // 알파만 낮춰도 렌더 상태(_Surface/_SrcBlend/_ZWrite)가 불투명이라 안 비친다. → 머티리얼 자체를 스왑.
            //   transparent(Glass Pipe) → _spawnerTransparentMat(SpawnerAlpha, 투명)
            //   opaque(Pipe)            → _spawnerOpaqueMat(SpawnerOriginal, 불투명)
            // sharedMaterial 할당이라 인스턴스 복제 leak 없음. 풀 재사용 시 매 스폰마다 양 타입이 명시 set 하므로 안전.
            Material target = transparent ? _spawnerTransparentMat : _spawnerOpaqueMat;
            if (target == null) return; // 인스펙터 미할당이면 프리팹 원본 유지 (no-op).
            if (_colorRenderers != null)
            {
                for (int i = 0; i < _colorRenderers.Length; i++)
                {
                    if (_colorRenderers[i] == null) continue;
                    _colorRenderers[i].sharedMaterial = target;
                }
            }

            // ROLLBACK_SPAWNER_OUTER_GLASS_MAT_20260701:
            // Ensure OuterGlass specifically gets the selected material. This fixes Spawner_O when
            // OuterGlass is not part of _colorRenderers and remains transparent after pooling.
            Renderer outerGlass = ResolveSpawnerOuterGlassRenderer();
            if (outerGlass != null)
                outerGlass.sharedMaterial = target;
        }

        /// <summary>ROLLBACK_SPAWNER_END_PARTICLE_20260624 (#4): Spawner 소멸 시 EndParticle 1회 재생.</summary>
        private Renderer ResolveSpawnerOuterGlassRenderer()
        {
            if (_spawnerOuterGlassRenderer != null)
                return _spawnerOuterGlassRenderer;

            Transform t = transform.Find("Spawner/Spawner/OuterGlass") ?? FindDeep(transform, "OuterGlass");
            if (t != null)
            {
                _spawnerOuterGlassRenderer = t.GetComponent<Renderer>();
                if (_spawnerOuterGlassRenderer == null)
                    _spawnerOuterGlassRenderer = t.GetComponentInChildren<Renderer>(true);
            }

            return _spawnerOuterGlassRenderer;
        }

        public void PlaySpawnerEndParticle()
        {
            GameObject template = _spawnerEndParticle;
            if (template == null)
            {
                // 인스펙터 미할당 폴백 — 계층에서 "EndParticle" 자동 탐색(비활성 포함).
                var all = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && all[i].name == "EndParticle") { template = all[i].gameObject; break; }
            }
            if (template == null) return;

            // ROLLBACK_SPAWNER_END_SCALE_20260707: in-place 활성화 → detached clone 재생으로 변경.
            //   스포너 소멸에 스케일다운(1→1.1→0) 트윈이 추가되면서, 자식 파티클을 제자리에서 켜면
            //   루트와 함께 0 으로 줄어들어 안 보임 + 풀 반환 시 활성 상태로 오염됨
            //   (WoodenBoard PlayEndEffectCloneDetached 와 동일 원리). 롤백: clone 블록 → SetActive+Play 환원.
            Transform source = template.transform;
            GameObject fx = Instantiate(template, source.position, source.rotation);
            fx.name = template.name + "_RT";
            fx.transform.localScale = source.lossyScale;
            fx.SetActive(true);
            var pss = fx.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < pss.Length; i++)
                pss[i].Play(true);
            Destroy(fx, 2f);
        }

        #endregion

        #region Chain Visual

        /// <summary>Chain Loop 오브젝트 활성화/비활성화.</summary>
        public void SetChainLoop(bool active)
        {
            if (_chainLoop != null)
                _chainLoop.SetActive(active);
        }

        #endregion

        #region Hand Booster Highlight

        /// <summary>
        /// Hand(SelectTool) 부스터 활성 동안 ControlBoxStroke 오브젝트를 토글 + Scale yoyo idle 연출.
        /// active=true: stroke 켜고 1→1.05→1 yoyo 루프 (popup 중에도 돌도록 SetUpdate(true)).
        /// active=false: tween Kill + 원본 scale 복원 + stroke 끔.
        /// _controlBoxStroke 가 Inspector 미할당이면 no-op.
        /// </summary>
        public void SetControlBoxStrokeActive(bool active)
        {
            if (_controlBoxStroke == null) return;

            Transform strokeT = _controlBoxStroke.transform;

            if (active)
            {
                if (!_strokeScaleCached)
                {
                    _baseStrokeScale = strokeT.localScale;
                    _strokeScaleCached = true;
                }

                _controlBoxStroke.SetActive(true);
                strokeT.DOKill();
                strokeT.localScale = _baseStrokeScale;

                _strokeIdleTween = strokeT
                    .DOScale(_baseStrokeScale * 1.05f, 0.5f)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            }
            else
            {
                _strokeIdleTween?.Kill();
                _strokeIdleTween = null;
                strokeT.DOKill();
                if (_strokeScaleCached)
                    strokeT.localScale = _baseStrokeScale;
                _controlBoxStroke.SetActive(false);
            }
        }

        #endregion

        #region Utility

        private GameObject ResolveHiddenAppearParticle()
        {
            if (_hiddenAppearParticle != null) return _hiddenAppearParticle;

            Transform found = FindDeep(transform, "HiddenAppearParticle");
            if (found != null)
                _hiddenAppearParticle = found.gameObject;

            return _hiddenAppearParticle;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        #endregion
    }
}
