using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Pinata_Box(Target Box) 비주얼 — 틀(frame) + W×H 알(paint) 격자.
    ///
    /// [Inspector 링크 — IronBox 프리팹 루트에 부착]
    ///  - _frame      : 박스 틀 (paintbox). footprint 에 맞춰 bounds-fit 스케일됨. 없으면 스킵.
    ///  - _eggTemplate: 알 1개 (paint) 템플릿. Build 가 이것을 W×H 복제해서 사용 → 원본은 자동 비활성.
    ///                  프리팹에 남아있는 다른 paint(정적 알)들은 제거하거나 비활성 권장(이중 렌더 방지).
    ///
    /// [동작]
    ///  - BalloonController 가 스폰 시 Build() 호출. 알을 개별 mesh 로 셀 간격에 배치하므로
    ///    단일 mesh 확대(ApplySizedFieldVisualTransform)로 인한 이웃 셀 침범이 없다.
    ///  - 각 알에 eggColors[i] 색을 적용 (BalloonController 공용 머티리얼 캐시 재사용).
    ///  - Phase D(메커닉)에서 알 제거는 HideEgg(index) 로 시각 처리.
    /// </summary>
    [DisallowMultipleComponent]
    public class PinataBoxView : MonoBehaviour
    {
        [Header("[Inspector 링크 — 드래그해서 연결]")]
        [Tooltip("박스 틀(paintbox) Transform. footprint 크기에 맞춰 자동 스케일. 비워두면 스킵.")]
        [SerializeField] private Transform _frame;
        [Tooltip("알 1개 템플릿(paint) GameObject. Build 가 이걸 복제해 W×H 격자 생성 → 원본은 자동 비활성.")]
        [SerializeField] private GameObject _eggTemplate;

        [Header("[튜닝]")]
        [Tooltip("알 크기 배수(축별). 프리팹 메시가 3× 크면 X/Z 를 0.333 으로 설정. Y 는 보통 1 유지. (1,1,1)=원본.")]
        [SerializeField] private Vector3 _eggScaleMultiplier = Vector3.one;

        private readonly List<GameObject> _eggs = new List<GameObject>();

        /// <summary>생성된 알 개수 (= W*H, 범위 내).</summary>
        public int EggCount => _eggs.Count;

        /// <summary>index 번째 알 GameObject (row-major). 범위 밖이면 null.</summary>
        public GameObject GetEgg(int index)
            => (index >= 0 && index < _eggs.Count) ? _eggs[index] : null;

        /// <summary>
        /// W×H 알 격자 생성 + 색 적용 + 틀 스케일.
        /// 박스 루트는 footprint 중앙·identity scale 로 BalloonController 가 배치하고,
        /// 여기서는 월드 셀 간격(cellSizeX/Z)으로 알을 로컬 배치한다(루트 scale=1 가정).
        /// </summary>
        /// <param name="w">가로 셀 수</param>
        /// <param name="h">세로 셀 수</param>
        /// <param name="eggColors">셀별 색 인덱스 (len=w*h, row-major). null 이면 색 미적용.</param>
        /// <param name="cellSizeX">월드 셀 가로 간격</param>
        /// <param name="cellSizeZ">월드 셀 세로 간격</param>
        /// <param name="eggScale">알 1개 스케일 (보통 _balloonScale*scaleMult). 템플릿 로컬 스케일에 곱.</param>
        public void Build(int w, int h, int[] eggColors, float cellSizeX, float cellSizeZ, float eggScale)
        {
            Clear();

            if (_eggTemplate == null)
            {
                Debug.LogError("[PinataBoxView] _eggTemplate 미할당 — 알을 생성할 수 없습니다. IronBox 프리팹에서 paint 1개를 _eggTemplate 에 링크하세요.", this);
                return;
            }

            w = Mathf.Max(1, w);
            h = Mathf.Max(1, h);
            _eggTemplate.SetActive(false); // 원본 템플릿 숨김 (복제본만 표시)

            Vector3 tplScale = _eggTemplate.transform.localScale;
            Quaternion tplRot = _eggTemplate.transform.localRotation;
            float tplY = _eggTemplate.transform.localPosition.y;

            // 월드 셀 간격을 이 컴포넌트(부모) 로컬 단위로 변환 — 부모가 스케일돼 있어도 알이 정확히 셀 간격으로 배치.
            Vector3 ls = transform.lossyScale;
            float localCellX = cellSizeX / Mathf.Max(0.0001f, Mathf.Abs(ls.x));
            float localCellZ = cellSizeZ / Mathf.Max(0.0001f, Mathf.Abs(ls.z));

            for (int dy = 0; dy < h; dy++)
            {
                for (int dx = 0; dx < w; dx++)
                {
                    int idx = dy * w + dx;
                    GameObject egg = Instantiate(_eggTemplate, transform);
                    egg.SetActive(true);
                    egg.transform.localRotation = tplRot;
                    // 축별 배수(_eggScaleMultiplier: 예 X/Z=0.333)로 메시 과대 보정 + 밀집 축소(eggScale).
                    egg.transform.localScale = Vector3.Scale(tplScale, _eggScaleMultiplier) * Mathf.Max(0.01f, eggScale);
                    // 컴포넌트 로컬 원점 기준 중앙 정렬 (BalloonController 가 이 노드를 footprint 중앙에 둠)
                    float ox = (dx - (w - 1) * 0.5f) * localCellX;
                    float oz = (dy - (h - 1) * 0.5f) * localCellZ;
                    egg.transform.localPosition = new Vector3(ox, tplY, oz);

                    if (eggColors != null && idx < eggColors.Length)
                        ApplyEggColor(egg, eggColors[idx]);

                    _eggs.Add(egg);
                }
            }

            ScaleFrameToFootprint(w, h, cellSizeX, cellSizeZ);
        }

        /// <summary>index 번째 알을 시각적으로 제거(비활성). 남은 활성 알이 있으면 true.</summary>
        public bool HideEgg(int index)
        {
            if (index >= 0 && index < _eggs.Count && _eggs[index] != null)
                _eggs[index].SetActive(false);

            for (int i = 0; i < _eggs.Count; i++)
                if (_eggs[i] != null && _eggs[i].activeSelf) return true;
            return false;
        }

        private void Clear()
        {
            for (int i = 0; i < _eggs.Count; i++)
                if (_eggs[i] != null) Destroy(_eggs[i]);
            _eggs.Clear();
        }

        private static void ApplyEggColor(GameObject egg, int colorIndex)
        {
            var renderers = egg.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            Color c = BalloonController.BalloonColors[
                Mathf.Clamp(colorIndex, 0, BalloonController.BalloonColors.Length - 1)];
            Material mat = BalloonController.GetOrCreateSharedMaterial(c);
            if (mat == null) return;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].sharedMaterial = mat;
        }

        // 틀의 renderer bounds 를 측정해 footprint(w*cellSizeX × h*cellSizeZ)에 맞춰 스케일 (self-calibrating).
        // 모서리 늘어남(2×2 틀을 1×3 등으로)은 아트 보강 영역 — 단순 스케일로 진행.
        private void ScaleFrameToFootprint(int w, int h, float cellSizeX, float cellSizeZ)
        {
            if (_frame == null) return;

            var rends = _frame.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return;

            // 현재 스케일 기준 bounds → 단위 스케일 base size 역산.
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
