// [ROLLBACK_GIMMICK_DISPLAY_NAME]
// 기믹 코드 식별자(string constant) → 사용자 표기명(UI display name) 매핑.
// 코드 내부 string ("Pin", "Hidden", ...) 은 변경 없음. UI 표기만 매핑.
// 롤백: 이 파일 통째로 제거 + MapMakerController / Inspector / 인게임 UI 의 GimmickDisplayName.Get 호출처 원복.
using System.Collections.Generic;

namespace BalloonFlow
{
    /// <summary>
    /// 기믹 코드 식별자 → UI 표기명 변환.
    /// 명칭 매핑 (BL v1.2.x 기준):
    ///   Pin       → Barricade
    ///   Pinata    → Wooden Board
    ///   Pinata_Box → Target Box
    ///   Spawner_T → Glass Pipe
    ///   Spawner_O → Pipe
    ///   Surprise  → Hidden Balloon
    ///   Wall      → Iron Wall
    ///   Hidden    → Hidden Dart Box
    ///   Chain     → Linked Dart Box
    ///   Ice       → Ice (그대로)
    ///   Color_Curtain → Curtain
    ///   Frozen_Dart → Frozen Dart Box
    ///   Barricade → (Pin 과 통합 후 단일 표기, 통합 전엔 별도 표기)
    ///   FlexTube  → FlexTube
    /// </summary>
    public static class GimmickDisplayName
    {
        // 코드 식별자 → 표기명 lookup.
        // 매핑에 없는 식별자는 그대로 반환 (예: "none").
        private static readonly Dictionary<string, string> _map = new Dictionary<string, string>
        {
            { "Pin",           "Barricade" },
            { "Pinata",        "Wooden Board" },
            { "Pinata_Box",    "Target Box" },
            { "Spawner_T",     "Glass Pipe" },
            { "Spawner_O",     "Pipe" },
            { "Surprise",      "Hidden Balloon" },
            { "Wall",          "Iron Wall" },
            { "Hidden",        "Hidden Dart Box" },
            { "Chain",         "Linked Dart Box" },
            { "Ice",           "Ice" },
            { "Color_Curtain", "Curtain" },
            { "Frozen_Dart",   "Frozen Dart Box" },
            { "Barricade",     "Iron Barricade" }, // Pin 과 충돌 회피 — Pin 표기는 Barricade, Barricade 표기는 Iron Barricade
            { "FlexTube",      "FlexTube" },
            { "Lock_Key",      "(deprecated) Lock_Key" }, // dead 처리. dropdown 에 표시 안 됨.
            { "none",          "(none)" },
            { "(none)",        "(none)" },
        };

        /// <summary>코드 식별자(예: "Pin") → 표기명(예: "Barricade") 반환. 매핑 없으면 원본 그대로.</summary>
        public static string Get(string codeName)
        {
            if (string.IsNullOrEmpty(codeName)) return "";
            return _map.TryGetValue(codeName, out string display) ? display : codeName;
        }
    }
}
