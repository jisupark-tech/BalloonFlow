using System;
using TMPro;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// ROLLBACK_LOCALIZATION_HARDCODE_FIX_20260714:
    /// 코드로 직접 세팅되는 TMP 텍스트(= <see cref="UIText"/> 컴포넌트가 붙지 않은 텍스트)에
    /// 현재 언어에 맞는 폰트를 입히는 공용 유틸. PopupCommonFrame 등 '.text 직접 세팅' 경로에서
    /// 텍스트 세팅 직후 호출한다. (UIText 경로는 UIText 가 자체 처리 — 여기 로직과 동일 규약.)
    ///
    /// 규약: Poppins 기반 텍스트만 KO(ChironGoRoundTC-Black)로 스왑하고, 색/아웃라인 프리셋은
    ///   <see cref="UIOutlineStyle.MaterialForFont"/> 로 보존(없으면 런타임 파생 → '블랙 기본'으로 안 떨어짐).
    ///   숫자/타 폰트(비-Poppins)는 건드리지 않는다. 이미 KO 면 no-op(idempotent).
    /// 한계: 부팅 시 언어가 확정되는 1.0/1.1 기준이라 런타임 KO→EN 복원은 하지 않는다(QA 강제토글 시 팝업 재오픈 필요).
    /// </summary>
    public static class LocalizationFont
    {
        private const string KO_FONT_RES = "Fonts & Materials/ChironGoRoundTC-Black SDF";
        private const string KO_FAMILY   = "ChironGoRoundTC-Black";

        private static TMP_FontAsset _koFont;
        private static bool _koTried;

        /// <summary>KO 폰트 애셋 1회 로드(공유). UIText 도 이 로더를 재사용.</summary>
        public static TMP_FontAsset LoadKoFont()
        {
            if (!_koTried)
            {
                _koFont = Resources.Load<TMP_FontAsset>(KO_FONT_RES);
                _koTried = true;
                if (_koFont == null) Debug.LogWarning($"[LocalizationFont] KO 폰트 없음: Resources/{KO_FONT_RES}");
            }
            return _koFont;
        }

        /// <summary>현재 언어가 KO 면 Poppins 계열 TMP 를 Chiron 으로 스왑(색/아웃라인 보존). 그 외/비-Poppins/이미 KO → no-op.</summary>
        public static void Apply(TMP_Text tmp)
        {
            // ROLLBACK_FONT_SWAP_REMOVED_20260714: Poppins-Bold SDF 에 ChironGoRoundTC-Black SDF Fallback 추가로
            //   한글은 TMP fallback 이 자동 렌더 → 언어별 폰트 스왑 불필요(아웃라인/fill 이격도 사라짐). 이 메서드는 no-op.
            //   (롤백: 아래 return 제거.)
            // no-op (위 주석 참고).
        }
    }
}
