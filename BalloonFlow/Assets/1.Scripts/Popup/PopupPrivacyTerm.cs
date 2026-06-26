using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// First-launch terms/privacy consent popup shown before Title loading starts.
    /// ROLLBACK_PRIVACY_TERM_GATE_20260626:
    /// Remove this script and the TitleController consent routine to restore the old direct loading flow.
    /// </summary>
    public class PopupPrivacyTerm : UIBase
    {
        private const string TERMS_URL = "https://aimed.xyz/terms";
        private const string PRIVACY_URL = "https://aimed.xyz/privacy";

        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Content]")]
        [SerializeField] private TMP_Text _txtContentDescription;
        [SerializeField] private TMP_Text _txtContentTerm;
        [SerializeField] private TMP_Text _txtContentPrivacy;

        private Action _onAccepted;
        private bool _resolved;
        private InlineTextLinkHandler _descriptionLinkHandler;

        public override BackResult OnBackPressed() => BackResult.Blocked;

        protected override void Awake()
        {
            base.Awake();
            ResolveRefs();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            RemoveAllRuntimeListeners();
        }

        public void Show(Action onAccepted)
        {
            _onAccepted = onAccepted;
            ResolveRefs();
            ApplyCopy();
            BindButtons();
            OpenUI();
        }

        private void ResolveRefs()
        {
            if (_resolved) return;
            _resolved = true;

            if (_frame == null)
                _frame = GetComponentInChildren<PopupCommonFrame>(true);

            TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text t = texts[i];
                if (t == null) continue;

                string n = t.gameObject.name;
                if (_txtContentDescription == null && n == "TxtContentDescription") _txtContentDescription = t;
                else if (_txtContentTerm == null && n == "TxtContentTerm") _txtContentTerm = t;
                else if (_txtContentPrivacy == null && n == "TxtContentPrivacy") _txtContentPrivacy = t;
            }
        }

        private void ApplyCopy()
        {
            if (_frame != null)
            {
                _frame.SetTitle(LocalizationService.Get("popup.privacyterm.title"));
                _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                _frame.SetSingleButtonText(LocalizationService.Get("popup.privacyterm.accept"));
                _frame.ShowExitButton(false);
            }

            if (_txtContentDescription != null)
            {
                _txtContentDescription.text = LocalizationService.Get("popup.privacyterm.description").Replace("\\n", "\n");
                _txtContentDescription.richText = true;
                _txtContentDescription.raycastTarget = true;
            }

            // ROLLBACK_PRIVACY_TERM_INLINE_LINKS_20260626:
            // Terms/Privacy are now inline links inside TxtContentDescription.
            // Re-enable these two labels if the prefab returns to separate link buttons.
            if (_txtContentTerm != null)
                _txtContentTerm.gameObject.SetActive(false);
            if (_txtContentPrivacy != null)
                _txtContentPrivacy.gameObject.SetActive(false);
        }

        private void BindButtons()
        {
            RemoveAllRuntimeListeners();

            if (_frame != null && _frame.BtnSingle != null)
            {
                _frame.BtnSingle.onClick.RemoveAllListeners();
                _frame.BtnSingle.onClick.AddListener(Accept);
            }

            BindDescriptionClickTarget();
        }

        private void RemoveAllRuntimeListeners()
        {
            if (_frame != null && _frame.BtnSingle != null)
                _frame.BtnSingle.onClick.RemoveListener(Accept);

            RemoveTextButtonListener(_txtContentTerm);
            RemoveTextButtonListener(_txtContentPrivacy);
            RemoveTextButtonListener(_txtContentDescription);
        }

        private void BindDescriptionClickTarget()
        {
            if (_txtContentDescription == null) return;

            _txtContentDescription.raycastTarget = true;
            _descriptionLinkHandler = _txtContentDescription.GetComponent<InlineTextLinkHandler>();
            if (_descriptionLinkHandler == null)
                _descriptionLinkHandler = _txtContentDescription.gameObject.AddComponent<InlineTextLinkHandler>();
            _descriptionLinkHandler.Configure(_txtContentDescription, OpenUrlSafe);
        }

        private static void BindTextButton(TMP_Text label, UnityEngine.Events.UnityAction action)
        {
            if (label == null || action == null) return;

            label.raycastTarget = true;
            Button btn = label.GetComponent<Button>();
            if (btn == null) btn = label.gameObject.AddComponent<Button>();
            btn.targetGraphic = label;
            btn.transition = Selectable.Transition.None;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(action);
        }

        private static void RemoveTextButtonListener(TMP_Text label)
        {
            if (label == null) return;
            Button btn = label.GetComponent<Button>();
            if (btn != null) btn.onClick.RemoveAllListeners();
        }

        private void Accept()
        {
            PlayerPrefs.SetString(Const.PREFS_PRIVACY_TERM_VERSION, Const.PRIVACY_TERM_VERSION);
            PlayerPrefs.Save();

            Action callback = _onAccepted;
            _onAccepted = null;
            CloseUI();
            callback?.Invoke();
        }

        private static void OpenUrlSafe(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try { Application.OpenURL(url); }
            catch (Exception ex) { Debug.LogWarning($"[PopupPrivacyTerm] OpenURL failed: {url} - {ex.Message}"); }
        }

        private sealed class InlineTextLinkHandler : MonoBehaviour, IPointerClickHandler
        {
            private TMP_Text _text;
            private Action<string> _openUrl;

            public void Configure(TMP_Text text, Action<string> openUrl)
            {
                _text = text;
                _openUrl = openUrl;
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (_text == null || eventData == null || _openUrl == null) return;

                int linkIndex = TMP_TextUtilities.FindIntersectingLink(_text, eventData.position, eventData.pressEventCamera);
                if (linkIndex < 0) return;

                TMP_LinkInfo link = _text.textInfo.linkInfo[linkIndex];
                string id = link.GetLinkID();
                if (string.Equals(id, "terms", StringComparison.OrdinalIgnoreCase))
                    _openUrl(TERMS_URL);
                else if (string.Equals(id, "privacy", StringComparison.OrdinalIgnoreCase))
                    _openUrl(PRIVACY_URL);
            }
        }
    }
}
