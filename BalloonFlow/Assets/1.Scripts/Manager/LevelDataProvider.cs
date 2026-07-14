using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace BalloonFlow
{
    /// <summary>
    /// 레벨 데이터 동기 조회 어댑터.
    /// 실제 데이터는 LevelEpisodeService 가 에피소드(20레벨) 단위로 캐시 보유.
    /// 이 클래스는 LevelManager 가 levelId 로 동기 조회할 수 있도록 wrap.
    ///
    /// 호출 전 LevelEpisodeService.EnsureEpisodeForLevelAsync(levelId) 가 완료되어 있어야 함.
    /// Title 진입 시 prefetch 됨. 다른 에피소드 진입 시점은 PackageManager / Lobby 가 prefetch.
    ///
    /// Editor: 캐시 miss 시 EditorData/LevelDatabase.asset 으로 폴백 (디자이너 편의).
    /// 디바이스 빌드: 캐시 miss = 경고 + null (호출자가 LevelGenerator 폴백 처리).
    /// </summary>
    public class LevelDataProvider : MonoBehaviour
    {
        #region Fields

        /// <summary>Editor 폴백 전용 — runtime 에선 LevelEpisodeService 사용. 비워둬도 무관.</summary>
        [SerializeField]
        private LevelDatabase _editorFallbackDatabase;

        #endregion

        #region Properties

        /// <summary>
        /// 현재 에피소드 캐시가 준비됐는지. LevelEpisodeService.IsReady 위임.
        /// Editor 폴백이 있으면 항상 true.
        /// </summary>
        public bool IsReady
        {
            get
            {
                if (LevelEpisodeService.HasInstance && LevelEpisodeService.Instance.IsReady) return true;
#if UNITY_EDITOR
                return TryLoadEditorFallback() && _editorFallbackDatabase.levels != null && _editorFallbackDatabase.levels.Length > 0;
#else
                return false;
#endif
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// levelId (1-based) 의 LevelConfig 반환. 캐시 miss 시 Editor 폴백 또는 null.
        /// </summary>
        public LevelConfig GetLevelData(int levelId)
        {
            // 1) Episode cache hit
            if (LevelEpisodeService.HasInstance)
            {
                var fromCache = LevelEpisodeService.Instance.GetLevel(levelId);
                if (fromCache != null) return fromCache;
            }

#if UNITY_EDITOR
            // 2) Editor 폴백 — EditorData/LevelDatabase.asset
            if (TryLoadEditorFallback())
            {
                int index = levelId - 1;
                if (index >= 0 && index < _editorFallbackDatabase.levels.Length)
                {
                    Debug.LogWarning($"[LevelDataProvider] Episode 캐시 miss → Editor 폴백 사용 (level {levelId}). 디바이스 빌드에선 prefetch 필요.");
                    return _editorFallbackDatabase.levels[index];
                }
                Debug.LogWarning($"[LevelDataProvider] Level {levelId} out of range " +
                                 $"(editor fallback has {_editorFallbackDatabase.levels.Length} levels).");
                return null;
            }
#endif

            Debug.LogWarning($"[LevelDataProvider] Level {levelId} 조회 실패 — LevelEpisodeService.EnsureEpisodeForLevelAsync 선행 필요.");
            return null;
        }

        public HolderSetup[] GetHolderSetup(int levelId)
        {
            var config = GetLevelData(levelId);
            return config?.holders ?? System.Array.Empty<HolderSetup>();
        }

        public BalloonLayout[] GetBalloonLayout(int levelId)
        {
            var config = GetLevelData(levelId);
            return config?.balloons ?? System.Array.Empty<BalloonLayout>();
        }

        public RailLayout GetRailLayout(int levelId)
        {
            return GetLevelData(levelId)?.rail;
        }

        /// <summary>
        /// 게임 전체 레벨 수 = Firebase 실측 가용 에피소드 × LEVELS_PER_EPISODE.
        /// ROLLBACK_ALL_CLEAR_PLAY_BLOCK_20260708: 설계 목표치(TOTAL_EPISODES) → 실측 상한으로 교체.
        ///   업로드 안 된 에피소드(예: 15개 중 14개만 존재)를 포함하면 전량 클리어 진입 차단이 뚫린다
        ///   (280 클리어 후 281 진입 시도 → 로드 실패). 롤백: TOTAL_EPISODES 환원.
        /// </summary>
        public int GetLevelCount()
        {
            // ROLLBACK_AB_EDITORDATA_20260714: 하드코딩 TOTAL_EPISODES(KnownAvailableEpisodes) 대신
            //   EditorData/{Master,Test}(에디터) 또는 빌드 매니페스트(빌드)에서 실측한 최대 에피소드 기반.
            //   → 폴더의 최후 에피소드가 14 면 280, 15 면 300 에서 전량-클리어 차단.
            return LevelEpisodeService.DetectedMaxEpisode * LevelEpisodeService.LEVELS_PER_EPISODE;
        }

        #endregion

        #region Editor fallback

#if UNITY_EDITOR
        private bool TryLoadEditorFallback()
        {
            if (_editorFallbackDatabase != null) return true;
            _editorFallbackDatabase = AssetDatabase.LoadAssetAtPath<LevelDatabase>("Assets/EditorData/LevelDatabase.asset");
            return _editorFallbackDatabase != null;
        }
#endif

        #endregion
    }
}
