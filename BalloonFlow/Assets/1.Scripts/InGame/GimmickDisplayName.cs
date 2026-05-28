// [ROLLBACK_GIMMICK_DISPLAY_NAME]
// 기믹 코드 식별자(string constant) → 사용자 표기명(UI display name) 매핑.
// 코드 내부 string ("Pin", "Hidden", ...) 은 변경 없음. UI 표기만 매핑.
// 롤백: 이 파일 통째로 제거 + MapMakerController / Inspector / 인게임 UI 의 GimmickDisplayName.Get 호출처 원복.
using System;
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
        private static readonly Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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

        private static readonly Dictionary<string, string> _canonicalMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "",              "" },
            { "none",          "none" },
            { "(none)",        "none" },
            { "Hidden",        "Hidden" },
            { "Hidden_Dart_Box", "Hidden" },
            { "Hidden Dart Box", "Hidden" },
            { "Chain",         "Chain" },
            { "Linked_Dart_Box", "Chain" },
            { "Linked Dart Box", "Chain" },
            { "Pinata",        "Pinata" },
            { "Wooden_Board",  "Pinata" },
            { "Wooden Board",  "Pinata" },
            { "Spawner_T",     "Spawner_T" },
            { "Glass_Pipe",    "Spawner_T" },
            { "Glass Pipe",    "Spawner_T" },
            { "Pin",           "Pin" },
            { "Lock_Key",      "Lock_Key" },
            { "Surprise",      "Surprise" },
            { "Hidden_Balloon", "Surprise" },
            { "Hidden Balloon", "Surprise" },
            { "Wall",          "Wall" },
            { "Iron_Wall",     "Wall" },
            { "Iron Wall",     "Wall" },
            { "Spawner_O",     "Spawner_O" },
            { "Pipe",          "Spawner_O" },
            { "Pinata_Box",    "Pinata_Box" },
            { "Pinata Box",    "Pinata_Box" },
            { "Target_Box",    "Pinata_Box" },
            { "Target Box",    "Pinata_Box" },
            { "Ice",           "Ice" },
            { "Frozen_Dart",   "Frozen_Dart" },
            { "Frozen Dart Box", "Frozen_Dart" },
            { "Color_Curtain", "Color_Curtain" },
            { "Color Curtain", "Color_Curtain" },
            { "Curtain",       "Color_Curtain" },
            { "Barricade",     "Barricade" },
            { "Iron_Barricade", "Barricade" },
            { "Iron Barricade", "Barricade" },
            { "FlexTube",      "FlexTube" },
            { "Flex_Tube",     "FlexTube" },
        };

        /// <summary>코드 식별자(예: "Pin") → 표기명(예: "Barricade") 반환. 매핑 없으면 원본 그대로.</summary>
        public static string Get(string codeName)
        {
            if (string.IsNullOrEmpty(codeName)) return "";
            string canonical = Normalize(codeName);
            return _map.TryGetValue(canonical, out string display) ? display : codeName;
        }

        /// <summary>DB/JSON/editor display aliases를 런타임에서 쓰는 canonical gimmick code로 정규화.</summary>
        public static string Normalize(string codeName)
        {
            if (string.IsNullOrWhiteSpace(codeName)) return "";

            string trimmed = codeName.Trim();
            if (_canonicalMap.TryGetValue(trimmed, out string canonical))
                return canonical;

            string underscore = trimmed.Replace(' ', '_');
            return _canonicalMap.TryGetValue(underscore, out canonical) ? canonical : trimmed;
        }
    }
}
