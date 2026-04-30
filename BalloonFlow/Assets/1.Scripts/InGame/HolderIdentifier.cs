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

        [Header("[Dart Visuals — Inspector에서 할당]")]
        [SerializeField] private Transform[] _dartSlots;

        [Header("[Box Visuals — Inspector에서 할당]")]
        [Tooltip("일반 상태 박스")]
        [SerializeField] private GameObject _box;
        [Tooltip("Frozen(Ice) 상태 박스")]
        [SerializeField] private GameObject _boxFrozen;
        [Tooltip("Frozen 해동 이펙트 (ParticleFrozenExplosion)")]
        [SerializeField] private GameObject _frozenExplosionEffect;

        [Header("[Hidden 기믹 — Inspector에서 할당]")]
        [Tooltip("Hidden Body용 Material (색상 숨김)")]
        [SerializeField] private Material _hiddenBodyMaterial;
        [Tooltip("Hidden Lid용 Material (색상 숨김)")]
        [SerializeField] private Material _hiddenLidMaterial;

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

            // 화면 밖 holder 의 Animator update 차단 — 풍선과 동일 사유.
            if (_animator != null)
                _animator.cullingMode = AnimatorCullingMode.CullCompletely;

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

            // Dart 원래 위치 + 부모 캐시 (최초 1회)
            if (_dartSlots != null && _dartLocalPositions == null)
            {
                _dartLocalPositions = new Vector3[_dartSlots.Length];
                _dartOriginalParents = new Transform[_dartSlots.Length];
                for (int i = 0; i < _dartSlots.Length; i++)
                {
                    if (_dartSlots[i] != null)
                    {
                        _dartLocalPositions[i] = _dartSlots[i].localPosition;
                        _dartOriginalParents[i] = _dartSlots[i].parent;
                    }
                }
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
        /// 풀 반환 시: 모든 Dart를 다시 보관함에 붙이고, 원래 위치로 복원, 활성화.
        /// </summary>
        public void ResetDarts()
        {
            if (_dartSlots == null) return;

            for (int i = 0; i < _dartSlots.Length; i++)
            {
                if (_dartSlots[i] == null) continue;

                _dartSlots[i].DOKill();

                // 원래 부모로 복원 (Box 자식이었으면 Box로)
                Transform originalParent = (_dartOriginalParents != null && i < _dartOriginalParents.Length && _dartOriginalParents[i] != null)
                    ? _dartOriginalParents[i]
                    : transform;
                _dartSlots[i].SetParent(originalParent);

                if (_dartLocalPositions != null && i < _dartLocalPositions.Length)
                    _dartSlots[i].localPosition = _dartLocalPositions[i];

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
        private static MaterialPropertyBlock _sharedMPB;

        /// <summary>
        /// 미선택 상태: 흰색 블러 오버레이 + 흰색 아웃라인.
        /// 모든 Renderer에 MaterialPropertyBlock 적용.
        /// </summary>
        public void SetUnselected(bool unselected)
        {
            if (_sharedMPB == null) _sharedMPB = new MaterialPropertyBlock();

            float blur = unselected ? 0.45f : 0f;
            Color blurCol = Color.white;
            float outlineOn = unselected ? 1f : 0f;
            Color outlineCol = Color.white;

            ApplyMPBToAll(blur, blurCol, outlineOn, outlineCol);
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

        /// <summary>선택됨 — 블러 해제 + 아웃라인 원복 (기반 Material 설정 따름).</summary>
        public void SetSelected()
        {
            if (_sharedMPB == null) _sharedMPB = new MaterialPropertyBlock();
            // 블러 0 + 아웃라인은 기반 Material 기본값 사용 (MPB 제거)
            ClearMPBFromAll();
        }

        /// <summary>Chain 연결 표시 — 검은색 아웃라인만 적용 (블러 없음).</summary>
        public void SetChainHighlight(bool active)
        {
            if (_sharedMPB == null) _sharedMPB = new MaterialPropertyBlock();
            ApplyMPBToAll(0f, Color.white, active ? 1f : 0f, Color.black);
        }

        private void ApplyMPBToAll(float blur, Color blurCol, float outlineOn, Color outlineCol)
        {
            // _colorRenderers
            if (_colorRenderers != null)
            {
                for (int i = 0; i < _colorRenderers.Length; i++)
                {
                    if (_colorRenderers[i] == null) continue;
                    _colorRenderers[i].GetPropertyBlock(_sharedMPB);
                    _sharedMPB.SetFloat(_propBlurAmount, blur);
                    _sharedMPB.SetColor(_propBlurColor, blurCol);
                    _sharedMPB.SetFloat(_propOutlineEnabled, outlineOn);
                    _sharedMPB.SetColor(_propOutlineColor, outlineCol);
                    _colorRenderers[i].SetPropertyBlock(_sharedMPB);
                }
            }
            // _customMatRenderers
            if (_customMatRenderers != null)
            {
                for (int i = 0; i < _customMatRenderers.Length; i++)
                {
                    if (_customMatRenderers[i] == null) continue;
                    _customMatRenderers[i].GetPropertyBlock(_sharedMPB);
                    _sharedMPB.SetFloat(_propBlurAmount, blur);
                    _sharedMPB.SetColor(_propBlurColor, blurCol);
                    _sharedMPB.SetFloat(_propOutlineEnabled, outlineOn);
                    _sharedMPB.SetColor(_propOutlineColor, outlineCol);
                    _customMatRenderers[i].SetPropertyBlock(_sharedMPB);
                }
            }
        }

        private void ClearMPBFromAll()
        {
            _sharedMPB.Clear();
            if (_colorRenderers != null)
                for (int i = 0; i < _colorRenderers.Length; i++)
                    if (_colorRenderers[i] != null)
                        _colorRenderers[i].SetPropertyBlock(_sharedMPB);
            if (_customMatRenderers != null)
                for (int i = 0; i < _customMatRenderers.Length; i++)
                    if (_customMatRenderers[i] != null)
                        _customMatRenderers[i].SetPropertyBlock(_sharedMPB);
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
            int colorCount = _colorRenderers != null ? _colorRenderers.Length : 0;
            int customCount = _customMatRenderers != null ? _customMatRenderers.Length : 0;
            bool hasBase = _customBaseMaterial != null;
            Debug.Log($"[HolderIdentifier] Holder {_holderId} ApplyColor: colorRenderers={colorCount}, customMatRenderers={customCount}, baseMat={hasBase}");

            // 일반 색상 Renderer — customMatRenderers에 포함된 것은 제외
            if (_colorRenderers != null && _colorRenderers.Length > 0)
            {
                // 기반 Material이 지정되어 있으면 복제+색상변경 (Outline/Metallic 유지)
                Material shared;
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
        }

        /// <summary>색상 적용 대상이 할당되었는지.</summary>
        public bool HasColorRenderers =>
            (_colorRenderers != null && _colorRenderers.Length > 0) ||
            (_customMatRenderers != null && _customMatRenderers.Length > 0);

        /// <summary>
        /// _customBaseMaterial 기반 색상별 Material 클론 캐시.
        /// Normal Map, Smoothness 등 기반 Material 설정 유지 + 색상만 변경.
        /// </summary>
        private static readonly Dictionary<int, Material> _customMatCache = new Dictionary<int, Material>();

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
            int key = baseMat.GetInstanceID() ^ color.GetHashCode();
            if (_customMatCache.TryGetValue(key, out Material cached))
                return cached;

            Material clone = new Material(baseMat);
            clone.SetColor("_BaseColor", color);
            clone.enableInstancing = true;
            _customMatCache[key] = clone;
            return clone;
        }

        #endregion

        #region Hidden Visual

        /// <summary>Hidden 상태 적용 — body/lid를 Hidden Material로 교체.</summary>
        public void SetHidden(bool hidden)
        {
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

            if (_box != null) _box.SetActive(!frozen);
            if (_boxFrozen != null) _boxFrozen.SetActive(frozen);
            if (!frozen && _frozenExplosionEffect != null)
                _frozenExplosionEffect.SetActive(true);
        }

        /// <summary>풀 반환 시 Box 상태 초기화 (일반 상태로).</summary>
        public void ResetBox()
        {
            if (_box != null) _box.SetActive(true);
            if (_boxFrozen != null) _boxFrozen.SetActive(false);
            if (_frozenExplosionEffect != null) _frozenExplosionEffect.SetActive(false);
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

        /// <summary>클릭 시 대기 박스 애니메이션 — Click 트리거.</summary>
        public void TriggerClick()
        {
            if (_animator != null)
                _animator.SetTrigger(_animClick);
        }

        /// <summary>배포 시작 — Deploy=true.</summary>
        public void StartDeploy()
        {
            if (_animator != null)
                _animator.SetBool(_animDeploy, true);
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
            for (int i = 0; i < _colorRenderers.Length; i++)
            {
                if (_colorRenderers[i] == null) continue;
                Color c = _colorRenderers[i].material.color;
                c.a = transparent ? 0.4f : 1f;
                _colorRenderers[i].material.color = c;
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

        #region Utility

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
