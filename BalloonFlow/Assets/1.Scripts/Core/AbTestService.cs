using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 레벨 A/B 테스트 — 에피소드1 variant(A/B) 배정.
    /// ROLLBACK_AB_EP1_20260713: B세트는 '에피소드1만' 변인(ep2~ 는 양쪽 A 공용). ep1 을 50:50 로 A/B 제공.
    ///   · 배정: 최초 1회 random 50:50 → PlayerPrefs 영속(불변). uid 비의존(타이틀 로딩 초기 uid 미확보 대비).
    ///   · 재현성: 서버 재계산이 아니라 user_property.ab_ep1_variant 기록값으로 확보(BQ 에서 A/B 분리).
    ///   · 로더: LevelEpisodeService 가 pkg1 로드 시 IsVariantB 면 episode_01_b.json 을 읽음(로컬 전용).
    ///   · 종료: FORCE_VARIANT_A=true → 신규 전원 A(기존 영속 B 는 replay 시에만 노출).
    ///   ※ PlayerPrefs 접근이라 '메인스레드 전용'. 로더/분석 stamp 모두 메인스레드에서 호출됨.
    /// </summary>
    public static class AbTestService
    {
        public const string VARIANT_A = "A";
        public const string VARIANT_B = "B";
        private const string PREFS_KEY = "BF_AB_Ep1Variant";

        // 테스트 종료 스위치: true 면 배정 무시하고 전원 A.
        private const bool FORCE_VARIANT_A = false;

        private static string _cached;

        /// <summary>에피소드1 variant("A"/"B"). 최초 1회 50:50 배정 후 불변. (메인스레드 전용)</summary>
        public static string Episode1Variant
        {
            get
            {
                if (FORCE_VARIANT_A) return VARIANT_A;
                if (_cached == VARIANT_A || _cached == VARIANT_B) return _cached;

                string stored = PlayerPrefs.GetString(PREFS_KEY, string.Empty);
                if (stored == VARIANT_A || stored == VARIANT_B) { _cached = stored; return _cached; }

                _cached = (Random.value < 0.5f) ? VARIANT_A : VARIANT_B; // 50:50
                PlayerPrefs.SetString(PREFS_KEY, _cached);
                PlayerPrefs.Save();
                Debug.Log($"[AbTest] Episode1 variant 배정 → {_cached}");
                return _cached;
            }
        }

        /// <summary>B variant 여부(= 에피소드1 을 episode_01_b.json 으로 로드).</summary>
        public static bool IsVariantB => Episode1Variant == VARIANT_B;
    }
}
