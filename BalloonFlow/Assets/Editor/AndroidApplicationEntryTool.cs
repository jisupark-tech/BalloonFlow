#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// ROLLBACK_ANDROID_ENTRY_ACTIVITY_20260713
// GameActivity onPause 핸드셰이크 ANR(Task#1 ①) 우회 — Application Entry Point 를
//   GameActivity(UnityPlayerGameActivity) → Activity(UnityPlayerActivity) 로 전환.
//
// 왜 코드(메뉴) 로 하나:
//   ProjectSettings.asset 이 '바이너리 직렬화'라 androidApplicationEntry 값을 수기 편집하면 파일이
//   손상된다. Unity API(PlayerSettings.Android.applicationEntry)로 설정해야 결정적·안전하며 리비전에
//   남길 수 있다. 메뉴 1회 실행 → AssetDatabase.SaveAssets 로 ProjectSettings 에 반영.
//
// ⚠️ 이 전환은 '빌드 설정' 변경이라 회귀 QA 필수(광고 show/close, 입력, 알림, 스플래시, 라이프사이클).
//   A/B 로 Play Console ANR율 비교 후 확정. 되돌리려면 'GameActivity 로 복귀' 메뉴 실행.
public static class AndroidApplicationEntryTool
{
    private const string MENU_ROOT = "Tools/BalloonFlow/Android Application Entry/";

    [MenuItem(MENU_ROOT + "→ Activity (ANR 우회)")]
    public static void SetActivity()
    {
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
        AssetDatabase.SaveAssets();
        Debug.Log($"[AndroidEntry] Application Entry Point → Activity 로 설정 완료. 현재값={PlayerSettings.Android.applicationEntry}. " +
                  "빌드 후 Play Console ANR율(A/B) 확인 필요.");
    }

    [MenuItem(MENU_ROOT + "→ GameActivity (되돌리기)")]
    public static void SetGameActivity()
    {
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
        AssetDatabase.SaveAssets();
        Debug.Log($"[AndroidEntry] Application Entry Point → GameActivity 로 복귀. 현재값={PlayerSettings.Android.applicationEntry}.");
    }

    [MenuItem(MENU_ROOT + "현재값 로그")]
    public static void LogCurrent()
    {
        Debug.Log($"[AndroidEntry] 현재 Application Entry Point = {PlayerSettings.Android.applicationEntry}");
    }
}
#endif
