using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 같은 GameObject 의 TMP_Text(우선) 또는 legacy UI.Text 에 CSV Key 의 텍스트를 적용.
    /// OnEnable 마다 적용 → 풀링/재사용·언어 전환에 자동 대응.
    /// Key 선택은 인스펙터 드롭다운(UITextDrawer)으로. 빈 Key 면 아무것도 하지 않음(기존 텍스트 유지).
    /// 자동 부착: BalloonFlow > Localization > Migrate &amp; Attach UIText (수동 메뉴).
    /// </summary>
    [DisallowMultipleComponent]
    public class UIText : MonoBehaviour
    {
        [SerializeField] private string _key;

        public string Key
        {
            get => _key;
            set { _key = value; Apply(); }
        }

        private void OnEnable()
        {
            LocalizationService.OnLanguageChanged += Apply;
            Apply();
        }

        private void OnDisable()
        {
            LocalizationService.OnLanguageChanged -= Apply;
        }

        /// <summary>현재 언어 텍스트를 컴포넌트에 적용.</summary>
        public void Apply()
        {
            if (string.IsNullOrEmpty(_key)) return;
            string s = LocalizationService.Get(_key);

            var tmp = GetComponent<TMP_Text>();
            var ui = tmp == null ? GetComponent<Text>() : null;

            // [{n} 가드 2026-06-16] 치환되지 않은 동적 placeholder({n}/{0} 등)를 포함한 키는
            //   런타임 값을 코드(GetWith / string.Format / 직접 .text)가 채운다. UIText 가 OnEnable 마다
            //   raw "{n}" 으로 덮어쓰면 풀 재사용·재활성 시 코드가 채운 가격/금액 값을 placeholder 로 되돌려버린다.
            //   → placeholder 포함 시 적용 스킵(코드 소유). 단 현재 텍스트도 placeholder 면(코드 미주입 상태)
            //     빈 값으로 비워 raw "{n}" 노출 자체를 차단. (CSV 의 '{' 는 전부 포맷 토큰 — 컬러태그는 '<'.)
            if (s.IndexOf('{') >= 0)
            {
                if (tmp != null && tmp.text != null && tmp.text.IndexOf('{') >= 0) tmp.text = string.Empty;
                else if (ui != null && ui.text != null && ui.text.IndexOf('{') >= 0) ui.text = string.Empty;
                return;
            }

            if (tmp != null) { tmp.text = s; return; }
            if (ui != null) ui.text = s;
        }

#if UNITY_EDITOR
        /// <summary>에디터에서 Key 변경/프리필 시 즉시 미리보기.</summary>
        private void OnValidate()
        {
            if (!Application.isPlaying && !string.IsNullOrEmpty(_key))
                Apply();
        }
#endif
    }
}
