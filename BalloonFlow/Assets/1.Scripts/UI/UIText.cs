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
            if (tmp != null) { tmp.text = s; return; }

            var ui = GetComponent<Text>();
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
