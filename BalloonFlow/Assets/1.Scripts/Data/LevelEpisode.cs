using System;

namespace BalloonFlow
{
    /// <summary>
    /// 한 에피소드(=package, 20레벨) 단위 직렬화 컨테이너.
    /// Firestore /episodes/{packageId} 와 StreamingAssets/episode_01.json 양쪽에서 사용.
    /// JsonUtility 직렬화 가능 — LevelConfig 의 Vector2/3 등 native 처리.
    /// </summary>
    [Serializable]
    public class LevelEpisode
    {
        public int packageId;
        public int levelCount;
        public int version;
        public LevelConfig[] levels;
    }
}
