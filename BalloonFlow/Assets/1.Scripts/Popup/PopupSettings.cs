using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// Settings popup view. Loaded from Resources/Popup/PopupSettings prefab.
    /// Shared between Lobby and InGame scenes.
    /// All child references wired via UIPrefabBuilder at editor-time.
    /// </summary>
    public class PopupSettings : MonoBehaviour
    {
        [SerializeField] private Button _closeButton;
        [SerializeField] private Text _soundLabel;
        [SerializeField] private Text _musicLabel;

        private CanvasGroup _canvasGroup;

        public Button CloseButton => _closeButton;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
        }

        public void Show()
        {
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = 1f;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }

        public void SetSoundLabel(bool on)
        {
            if (_soundLabel != null) _soundLabel.text = on ? "Sound: ON" : "Sound: OFF";
        }

        public void SetMusicLabel(bool on)
        {
            if (_musicLabel != null) _musicLabel.text = on ? "Music: ON" : "Music: OFF";
        }
    }
}
