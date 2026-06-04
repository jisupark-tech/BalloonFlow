using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 설정 팝업. Sound/Music/Haptic 토글.
    /// PopupCommonFrame 사용. Lobby, InGame 공용.
    /// Notification 토글은 UILobby Setting Panel에만 존재 (여기엔 없음).
    /// </summary>
    public class PopupSettings : UIBase
    {
        [Header("[Common Frame]")]
        [SerializeField] private PopupCommonFrame _frame;

        [Header("[Sound Toggle]")]
        [SerializeField] private Button _btnSound;
        [SerializeField] private GameObject _soundOn;
        [SerializeField] private GameObject _soundOff;

        [Header("[Music Toggle]")]
        [SerializeField] private Button _btnMusic;
        [SerializeField] private GameObject _musicOn;
        [SerializeField] private GameObject _musicOff;

        [Header("[Haptic Toggle]")]
        [SerializeField] private Button _btnHaptic;
        [SerializeField] private GameObject _hapticOn;
        [SerializeField] private GameObject _hapticOff;

        public Button CloseButton => _frame != null ? _frame.BtnExit : null;
        public Button HomeButton => _frame != null ? _frame.BtnHorizRed : null;
        public Button ContinueButton => _frame != null ? _frame.BtnHorizGreen : null;

        protected override void Awake()
        {
            base.Awake();
            if (_btnSound != null) _btnSound.onClick.AddListener(OnSoundClicked);
            if (_btnMusic != null) _btnMusic.onClick.AddListener(OnMusicClicked);
            if (_btnHaptic != null) _btnHaptic.onClick.AddListener(OnHapticClicked);

            // ExitButton 직접 바인딩 — HUDController.SetSettingsPopup가 호출 안 돼도 닫힘 동작 보장.
            // (HUDController는 추가 listener를 더 등록하지만, 중복 등록은 onClick.Invoke가 모두 호출해 안전.)
            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.AddListener(OnExitClickedSelf);
            // [#2] 온보딩 시 단일버튼(BtnSingle="Continue") = 닫기. HUDController 가 BtnSingle 은 안 묶으므로 직접 배선.
            if (_frame != null && _frame.BtnSingle != null)
                _frame.BtnSingle.onClick.AddListener(OnExitClickedSelf);

            EventBus.Subscribe<OnSettingsChanged>(HandleSettingsChanged);
        }

        // [2026-05-12] InGame 중 Setting 열림 시 게임 일시 정지. Lobby 에선 timeScale 의존 X 라 영향 미미.
        private bool _paused;
        private void OnEnable()
        {
            if (!_paused) { PauseManager.Pause(); _paused = true; }
        }
        private void OnDisable()
        {
            if (_paused) { PauseManager.Resume(); _paused = false; }
        }

        private void OnExitClickedSelf()
        {
            CloseUI();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_btnSound != null) _btnSound.onClick.RemoveAllListeners();
            if (_btnMusic != null) _btnMusic.onClick.RemoveAllListeners();
            if (_btnHaptic != null) _btnHaptic.onClick.RemoveAllListeners();
            if (_frame != null && _frame.BtnExit != null)
                _frame.BtnExit.onClick.RemoveListener(OnExitClickedSelf);
            if (_frame != null && _frame.BtnSingle != null)
                _frame.BtnSingle.onClick.RemoveListener(OnExitClickedSelf);

            EventBus.Unsubscribe<OnSettingsChanged>(HandleSettingsChanged);
        }

        public override void OpenUI()
        {
            // [#2/#5/12] 온보딩(Lv.5 클리어 전): 하단 단일버튼 [Continue]만 (Home/Quit 비노출 — 이탈 경로 차단).
            //            온보딩 후: Horizontal [Continue]+[Home].
            bool onboarding = !FtueGate.IsOnboardingComplete;
            if (_frame != null)
            {
                _frame.SetTitle("Settings");
                if (onboarding)
                {
                    _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Single);
                    _frame.SetSingleButtonText("Continue");
                }
                else
                {
                    _frame.SetButtonLayout(PopupCommonFrame.ButtonLayout.Horizontal);
                    _frame.SetHorizGreenText("Stay");
                    _frame.SetHorizRedText("Quit");
                }
                _frame.ShowExitButton(true);
            }

            RefreshToggles();
            base.OpenUI();

            // 애니메이션 사용 시 base.OpenUI 가 interactable=false 로 시작 → ExitButton 클릭 안 됨.
            // 즉시 클릭 가능하도록 강제.
            if (_canvasGroup != null)
            {
                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
            }
            if (_frame != null && _frame.BtnExit != null)
            {
                _frame.BtnExit.interactable = true;
                _frame.BtnExit.gameObject.SetActive(true);
            }
        }

        private void OnSoundClicked()
        {
            if (SettingsManager.HasInstance)
                SettingsManager.Instance.ToggleSound();
        }

        private void OnMusicClicked()
        {
            if (SettingsManager.HasInstance)
                SettingsManager.Instance.ToggleMusic();
        }

        private void OnHapticClicked()
        {
            if (SettingsManager.HasInstance)
                SettingsManager.Instance.ToggleHaptic();
        }

        private void HandleSettingsChanged(OnSettingsChanged evt)
        {
            UpdateToggle(_soundOn, _soundOff, evt.soundOn);
            UpdateToggle(_musicOn, _musicOff, evt.musicOn);
            UpdateToggle(_hapticOn, _hapticOff, evt.hapticOn);
        }

        private void RefreshToggles()
        {
            if (!SettingsManager.HasInstance) return;
            var sm = SettingsManager.Instance;
            UpdateToggle(_soundOn, _soundOff, sm.SoundOn);
            UpdateToggle(_musicOn, _musicOff, sm.MusicOn);
            UpdateToggle(_hapticOn, _hapticOff, sm.HapticOn);
        }

        private static void UpdateToggle(GameObject onObj, GameObject offObj, bool isOn)
        {
            if (onObj != null) onObj.SetActive(isOn);
            if (offObj != null) offObj.SetActive(!isOn);
        }
    }
}
