namespace BalloonFlow
{
    /// <summary>
    /// 다트 hit 진입점 — 풍선 시스템(BalloonController.PopBalloon) 과 별개 경로.
    /// 다트 collider 가 GameObject 에 닿았을 때 DartManager 가 IDartHittable 을 우선 체크.
    /// 발견되면 OnDartHit 만 호출하고 풍선 경로는 우회.
    /// 부속(자식) 부품 collider 에 붙어도 OnDartHit 안에서 owner 로 전달하는 패턴 가능.
    /// </summary>
    public interface IDartHittable
    {
        /// <summary>다트가 적중했을 때 호출. dartColor 매칭/무시는 구현체가 결정.</summary>
        /// <param name="dartColor">다트 색 인덱스 (LevelConfig 색 팔레트 기준).</param>
        void OnDartHit(int dartColor);
    }
}
