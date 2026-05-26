using UnityEngine;

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

        public GimmickIdentifier.FlexTubePart PartType => _partType;
        public FlexTube Owner => _owner;
        public int BalloonId => _balloonId;

        public void SetPartType(GimmickIdentifier.FlexTubePart t) => _partType = t;
        public void SetOwner(FlexTube owner) => _owner = owner;
        public void SetBalloonId(int id) => _balloonId = id;

        public void OnDartHit(int dartColor)
        {
            if (_owner == null) return;
            _owner.OnDartHit(dartColor);
        }
    }
}
