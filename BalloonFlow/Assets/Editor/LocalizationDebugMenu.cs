#if UNITY_EDITOR
using UnityEditor;

// ROLLBACK_LOCALIZATION_KO_20260713: 로컬라이징 QA — 강제 언어 토글(검수용).
//   Play 중 실행하면 즉시 언어 전환 → KO 텍스트 + Chiron 폰트/Outline 스왑을 디바이스 언어 변경 없이 검증.
//   설정은 PlayerPrefs(BF_LangOverride)에 남아 '빌드에서도' 강제 검수 가능(예: 내부 QA 빌드에서 KO 고정 확인).
//   실제 출시 동작(디바이스 언어 기반)은 'Clear override' 로 되돌린 뒤 확인.
public static class LocalizationDebugMenu
{
    private const string ROOT = "Tools/BalloonFlow/Localization/";

    [MenuItem(ROOT + "Force KO", false, 0)]
    public static void ForceKO() => BalloonFlow.LocalizationService.SetLanguageOverride("KO");

    [MenuItem(ROOT + "Force EN", false, 1)]
    public static void ForceEN() => BalloonFlow.LocalizationService.SetLanguageOverride("EN");

    [MenuItem(ROOT + "Clear override (use device language)", false, 2)]
    public static void ClearOverride() => BalloonFlow.LocalizationService.SetLanguageOverride("");

    [MenuItem(ROOT + "Reload CSV", false, 20)]
    public static void ReloadCsv() => BalloonFlow.LocalizationService.Reload();
}
#endif
