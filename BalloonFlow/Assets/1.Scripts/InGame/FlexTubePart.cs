using UnityEngine;
using System.Collections.Generic;

namespace BalloonFlow
{
    /// <summary>
    /// FlexTube 의 자식 부품(StartCap/Segment/EndCap) — collider + 다트 hit 진입점.
    /// DartManager 가 IDartHittable 로 인지 → OnDartHit 을 owner FlexTube 로 그대로 위임.
    /// Cap 도 hit 받지만 색 매칭/HP 감소는 owner 가 일괄 처리 (Cap 은 시각만 담당).
    /// </summary>
    public class FlexTubePart : MonoBehaviour, IDartHittable
    {
        [SerializeField] private GimmickIdentifier.FlexTubePart _partType = GimmickIdentifier.FlexTubePart.Segment;
        private FlexTube _owner;
        private int _balloonId = -1;
        private int[] _balloonIds;

        public GimmickIdentifier.FlexTubePart PartType => _partType;
        public FlexTube Owner => _owner;
        public int BalloonId => _balloonId;
        public int[] BalloonIds => _balloonIds;

        public void SetPartType(GimmickIdentifier.FlexTubePart t) => _partType = t;
        public void SetOwner(FlexTube owner) => _owner = owner;
        public void SetBalloonId(int id)
        {
            _balloonId = id;
            _balloonIds = id >= 0 ? new[] { id } : null;
        }

        // ROLLBACK_FLEXTUBE_CELL_TARGET_HIT_20260628:
        // A visual FlexTube part can represent a 2x2 logical footprint. Keep every cell id on the
        // part so a dart aimed at a row-B/secondary cell can remove that exact logical cell instead
        // of falling back to the first active visual part in the group.
        public void SetBalloonIds(IList<int> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                _balloonId = -1;
                _balloonIds = null;
                return;
            }

            _balloonIds = new int[ids.Count];
            for (int i = 0; i < ids.Count; i++)
                _balloonIds[i] = ids[i];
            _balloonId = _balloonIds[0];
        }

        public bool ContainsBalloonId(int id)
        {
            if (id < 0) return false;
            if (_balloonId == id) return true;
            if (_balloonIds == null) return false;
            for (int i = 0; i < _balloonIds.Length; i++)
                if (_balloonIds[i] == id)
                    return true;
            return false;
        }

        public void OnDartHit(int dartColor)
        {
            if (_owner == null) return;
            _owner.TryApplyDartHit(dartColor, _balloonId);
        }
    }
}
