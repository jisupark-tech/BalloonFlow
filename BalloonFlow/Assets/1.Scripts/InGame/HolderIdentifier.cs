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

            // BoxHiddenBody → BoxBodyShared 클론으로 실제 교체된 순간에만 HiddenAppear 1회 재생.
            if (wasHiddenMaterial && _colorBaseMaterial != null && shared != null)
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
        public void SetFrozen(bool frozen)
        {
            if (frozen && _boxFrozen == null)
                Debug.LogWarning($"[HolderIdentifier] Holder {_holderId}: _boxFrozen 미할당! Inspector에서 BoxFrozen 오브젝트를 드래그하세요.");

            bool wasFrozen = _isFrozenVisual || (_boxFrozen != null && _boxFrozen.activeSelf);
            _isFrozenVisual = frozen;

            if (!frozen && wasFrozen)
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
            if (_frozenExplosionEffect != null) _frozenExplosionEffect.SetActive(false);
            GameObject hiddenFx = ResolveHiddenAppearParticle();
            if (hiddenFx != null) hiddenFx.SetActive(false);
            SetControlBoxStrokeActive(false);
        }

        private void PlayFrozenBreakEffect()
        {
            if (_frozenExplosionEffect != null)
            {
                CacheFrozenEffectTransform();
                Vector3 worldPosition = _frozenExplosionEffect.transform.position;
                Quaternion worldRotation = _frozenExplosionEffect.transform.rotation;
                _frozenExplosionEffect.transform.SetParent(transform, true);
                _frozenExplosionEffect.transform.SetPositionAndRotation(worldPosition, worldRotation);
                _frozenExplosionEffect.SetActive(false);
                _frozenExplosionEffect.SetActive(true);
                var particles = _frozenExplosionEffect.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < particles.Length; i++)
                    particles[i].Play(true);
            }

            transform.DOPunchScale(Vector3.one * 0.14f, 0.26f, 8, 0.72f);
            transform.DOShakeRotation(0.22f, new Vector3(0f, 8f, 0f), 8, 55f);
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
            if (_animator != null)
            {
                if (!_animator.enabled) _animator.enabled = true;
                _animator.SetBool(_animDeploy, true);
            }
        }

        /// <summary>첫 다트가 레일에 배치되는 시점에 true → BoxOpenIdle로 강제 전환. 풀 반환/취소 시 false → BoxOpenDefault. BoxOpen.ani(뚜껑 원샷) 진행 중이면 완료될 때까지 코루틴으로 대기 후 Play — 컨트롤러 transition이 깨져도 강건.</summary>
        public void SetDartsOnRail(bool onRail)
        {
            if (_animator == null) return;
            if (!_animator.enabled) _animator.enabled = true;
            _animator.SetBool(_animOpenHold, onRail);
            if (_onRailRoutine != null) StopCoroutine(_onRailRoutine);
            if (isActiveAndEnabled)
                _onRailRoutine = StartCoroutine(ApplyOnRailState(onRail));
            else
                _animator.Play(onRail ? _animStateBoxOpenIdle : _animStateBoxOpenDefault, 0, 0f);
        }

        private Coroutine _onRailRoutine;

        private System.Collections.IEnumerator ApplyOnRailState(bool onRail)
        {
            int target = onRail ? _animStateBoxOpenIdle : _animStateBoxOpenDefault;
            float timeout = 2f;
            while (timeout > 0f && _animator != null)
            {
                var info = _animator.GetCurrentAnimatorStateInfo(0);
                if (info.shortNameHash == _animStateBoxOpenDefault || info.shortNameHash == _animStateBoxOpenIdle)
                {
                    if (info.shortNameHash != target)
                        _animator.Play(target, 0);
                    _onRailRoutine = null;
                    yield break;
                }
                timeout -= Time.deltaTime;
                yield return null;
            }
            if (_animator != null)
            {
                _animator.Play(target, 0, 0f);
                _animator.Update(0f);
            }
            _onRailRoutine = null;
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

        /// <summary>재사용 시 애니메이터 전체 리셋 (풀 반환 시 enabled 복원).</summary>
        public void ResetAnimator()
        {
            if (_animator != null)
            {
                _animator.enabled = true;
                _animator.Rebind(); // 모든 상태/파라미터 초기화 → Entry 상태로 복귀
                _animator.Update(0f);
            }
        }

        #endregion

        #region Spawner Visual

        /// <summary>Spawner_T: 반투명으로 다음 색상 미리보기.</summary>
        public void SetSpawnerTransparent(bool transparent)
        {
            if (_colorRenderers == null) return;
            // [Leak fix 2026-05-11] material.color 직접 접근은 매 호출 unique material 인스턴스 복제 → leak.
            // MaterialPropertyBlock 의 _BaseColor.a override 로 대체. 시각 동등 (alpha 만 변경).
            // 원본:
            // for (int i = 0; i < _colorRenderers.Length; i++) {
            //     if (_colorRenderers[i] == null) continue;
            //     Color c = _colorRenderers[i].material.color;
            //     c.a = transparent ? 0.4f : 1f;
            //     _colorRenderers[i].material.color = c;
            // }
            if (_sharedMPB == null) _sharedMPB = new MaterialPropertyBlock();
            float alpha = transparent ? 0.4f : 1f;
            for (int i = 0; i < _colorRenderers.Length; i++)
            {
                if (_colorRenderers[i] == null) continue;
                _colorRenderers[i].GetPropertyBlock(_sharedMPB);
                // sharedMaterial 의 _BaseColor 가져와서 alpha 만 override
                Color baseCol = _colorRenderers[i].sharedMaterial != null
                    ? _colorRenderers[i].sharedMaterial.GetColor(_propMainColor)
                    : Color.white;
                baseCol.a = alpha;
                _sharedMPB.SetColor(_propMainColor, baseCol);
                _colorRenderers[i].SetPropertyBlock(_sharedMPB);
            }
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
