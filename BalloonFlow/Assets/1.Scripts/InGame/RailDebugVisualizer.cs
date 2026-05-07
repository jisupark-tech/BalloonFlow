using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 레일 slot 인덱스 + deploy point 시각 확인용 디버그 MB.
    /// Scene view 의 Gizmo 로 표시 (Editor only). Inspector toggle 로 ON/OFF.
    ///
    /// 사용법:
    ///   1. 빈 GameObject 에 이 컴포넌트 추가
    ///   2. Inspector 의 _showSlots / _showDeployPoints / _showLabels 체크
    ///   3. Play 후 Scene view 에서 슬롯 cyan 점 + 인덱스 라벨 + deploy point 노란 큐브 확인
    /// </summary>
    public sealed class RailDebugVisualizer : MonoBehaviour
    {
        [Header("표시 항목")]
        [SerializeField] private bool _showSlots = true;
        [SerializeField] private bool _showDeployPoints = true;
        [SerializeField] private bool _showLabels = true;

        [Header("외형")]
        [SerializeField] private float _slotSphereSize = 0.06f;
        [SerializeField] private float _deployCubeSize = 0.18f;
        [SerializeField] private Color _slotColor = new Color(0f, 1f, 1f, 0.6f);
        [SerializeField] private Color _slotOccupiedColor = new Color(1f, 0.4f, 0.4f, 0.9f);
        [SerializeField] private Color _deployRegisteredColor = new Color(1f, 1f, 0f, 0.9f);
        [SerializeField] private Color _deployActiveColor = new Color(1f, 0.5f, 0f, 1f);

        [Tooltip("0 부터 N step 마다 인덱스 라벨 표시 (N=1: 모든 slot, N=10: 0,10,20,...).")]
        [SerializeField] private int _labelEvery = 5;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!RailManager.HasInstance) return;
            var rail = RailManager.Instance;
            int slotCount = rail.SlotCount;
            if (slotCount <= 0) return;

            if (_showSlots)
            {
                for (int i = 0; i < slotCount; i++)
                {
                    Vector3 pos = rail.GetSlotWorldPosition(i);
                    bool occupied = !rail.IsSlotEmpty(i);
                    Gizmos.color = occupied ? _slotOccupiedColor : _slotColor;
                    Gizmos.DrawWireSphere(pos, _slotSphereSize);

                    if (_showLabels && _labelEvery > 0 && (i % _labelEvery == 0))
                    {
                        UnityEditor.Handles.color = Color.white;
                        UnityEditor.Handles.Label(pos + Vector3.up * 0.1f, i.ToString());
                    }
                }
            }

            if (_showDeployPoints)
            {
                if (rail.TryGetDeployPointsDebug(out var holderIds, out var progresses, out var actives))
                {
                    for (int i = 0; i < holderIds.Count; i++)
                    {
                        Vector3 pos = rail.GetPositionAtDistance(progresses[i]);
                        Gizmos.color = actives[i] ? _deployActiveColor : _deployRegisteredColor;
                        Gizmos.DrawCube(pos, Vector3.one * _deployCubeSize);

                        if (_showLabels)
                        {
                            UnityEditor.Handles.color = actives[i] ? Color.red : Color.yellow;
                            int slotIdx = rail.GetSlotAtPathDistance(progresses[i]);
                            UnityEditor.Handles.Label(pos + Vector3.up * 0.25f,
                                $"H{holderIds[i]}→slot{slotIdx} {(actives[i] ? "[A]" : "[R]")}");
                        }
                    }
                }
            }
        }
#endif
    }
}
