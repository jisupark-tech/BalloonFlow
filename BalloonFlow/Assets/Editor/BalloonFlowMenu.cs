#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// BalloonFlow 메뉴 탭 유틸리티.
    /// </summary>
    public static class BalloonFlowMenu
    {
        [MenuItem("BalloonFlow/Reset User Data", false, 200)]
        private static void ResetUserData()
        {
            if (!EditorUtility.DisplayDialog("Reset User Data",
                "모든 유저 데이터를 초기화합니다.\n\n" +
                "- 레벨 진행 상황\n" +
                "- 별 점수\n" +
                "- 재화 (Gold, Gem, Life)\n" +
                "- 일일 보상\n" +
                "- 부스터/아이템\n" +
                "- 설정\n\n" +
                "계속하시겠습니까?", "초기화", "취소"))
            {
                return;
            }

            // Level progress
            PlayerPrefs.DeleteKey("BF_HighestLevel");
            PlayerPrefs.DeleteKey("BF_PendingLevelId");
            for (int i = 1; i <= 300; i++)
                PlayerPrefs.DeleteKey("BF_Stars_" + i);

            // Currency
            PlayerPrefs.DeleteKey("BF_Gold");
            PlayerPrefs.DeleteKey("BF_Gem");
            PlayerPrefs.DeleteKey("BF_Life");
            PlayerPrefs.DeleteKey("BF_LifeTimestamp");

            // Daily reward
            PlayerPrefs.DeleteKey("BF_DailyRewardDay");
            PlayerPrefs.DeleteKey("BF_DailyRewardDate");

            // Booster / Continue
            PlayerPrefs.DeleteKey("BF_Booster_Hammer");
            PlayerPrefs.DeleteKey("BF_Booster_Shuffle");
            PlayerPrefs.DeleteKey("BF_Booster_ExtraMoves");
            PlayerPrefs.DeleteKey("BF_ContinueCount");

            // Settings
            PlayerPrefs.DeleteKey("BF_BGM");
            PlayerPrefs.DeleteKey("BF_SFX");
            PlayerPrefs.DeleteKey("BF_Vibration");

            // EditorPrefs (scene/prefab builder 재실행 플래그)
            // 이건 초기화하지 않음 — 씬/프리팹 리빌드는 별도

            PlayerPrefs.Save();

            Debug.Log("[BalloonFlow] 유저 데이터 초기화 완료");
            EditorUtility.DisplayDialog("완료", "유저 데이터가 초기화되었습니다.", "OK");
        }

        [MenuItem("BalloonFlow/Generate 50 Levels (50x50)", false, 100)]
        private static void GenerateFiftyLevels()
        {
            LevelDatabaseGenerator50.Generate();
        }
    }
}
#endif
