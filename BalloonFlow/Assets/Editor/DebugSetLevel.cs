// ROLLBACK_DEBUG_SET_LEVEL_20260714: 테스트용 — 최고 클리어 레벨(PlayerPrefs BF_HighestLevel) 설정.
//   전량-클리어 게이트(마지막 에피소드 이후 진입 차단) 검증용. 에디터/기기 PlayerPrefs 는 코드로만 쓸 수 있어 메뉴로 제공.
//   롤백: 이 파일 삭제.
using UnityEditor;
using UnityEngine;

namespace BalloonFlow.EditorTools
{
    public static class DebugSetLevel
    {
        private const string KEY = "BF_HighestLevel"; // LevelManager.PREFS_KEY_HIGHEST_LEVEL 과 동일

        [MenuItem("BalloonFlow/Debug/Set Highest Level = 299", false, 1)]
        public static void Set299() => Set(299);

        [MenuItem("BalloonFlow/Debug/Set Highest Level = 279 (ep14 경계)", false, 2)]
        public static void Set279() => Set(279);

        [MenuItem("BalloonFlow/Debug/Reset Highest Level = 0", false, 3)]
        public static void Reset0() => Set(0);

        private static void Set(int lv)
        {
            PlayerPrefs.SetInt(KEY, lv);
            PlayerPrefs.Save();
            Debug.Log($"[DebugSetLevel] {KEY} = {lv} 저장 → 다음 진입 레벨 = {lv + 1}");
            EditorUtility.DisplayDialog("Debug Set Level",
                $"최고 클리어 레벨 = {lv}\n다음 진입 = {lv + 1}\n\n(전량-클리어 게이트: 다음레벨 > 마지막에피소드×20 이면 차단)", "OK");
        }
    }
}
