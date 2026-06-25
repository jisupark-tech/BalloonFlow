using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

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

        [Header("[Popup Setting Area Difficulty Sprites]")]
        [SerializeField] private Image _imgPopupSettingArea;
        [SerializeField] private Sprite _sprPopupInnerNormal;
        [SerializeField] private Sprite _sprPopupInnerHard;
        [SerializeField] private Sprite _sprPopupInnerSuperHard;
        private readonly Dictionary<Image, Color> _toggleImageOriginalColors = new Dictionary<Image, Color>(32);

        public Button CloseButton => _frame != null ? _frame.BtnExit : null;
        public Button HomeButton => _frame != null ? _frame.BtnHorizRed : null;
        public Button ContinueButton => _frame != null ? _frame.BtnHorizGreen : null;

        protected override void Awake()
        {
            base.Awake();
            if (_btnSound != null) _btnSound.onClick.AddListener(OnSoundClicked);
            if (_btnMusic != null) _btnMusic.onClick.AddListener(OnMusicClicked);
            if (_btnHaptic != null) _btnHaptic.onClick.AddListener(OnHapticClicked);
            CacheToggleImageColors();

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
                DifficultyPurpose diff = ResolveCurrentDifficulty();
                _frame.ApplyDifficulty(diff);
                ApplyPopupSettingAreaDifficulty(diff);
                RestoreToggleImageColors();
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

        private void CacheToggleImageColors()
        {
            // ROLLBACK_SETTINGS_BUTTON_COLOR_RESTORE_20260625:
            // Earlier difficulty tinting touched Sound/Music/Haptic button child Images.
            // Cache authored colors once and restore them whenever the popup opens so only
            // PopupSettingArea changes by difficulty.
            _toggleImageOriginalColors.Clear();
            CacheToggleImageColors(_btnSound != null ? _btnSound.transform : null);
            CacheToggleImageColors(_btnMusic != null ? _btnMusic.transform : null);
            CacheToggleImageColors(_btnHaptic != null ? _btnHaptic.transform : null);
        }

        private void CacheToggleImageColors(Transform root)
        {
            if (root == null) return;
            Image[] images = root.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null || _toggleImageOriginalColors.ContainsKey(image)) continue;
                _toggleImageOriginalColors.Add(image, image.color);
            }
        }

        private void RestoreToggleImageColors()
        {
            foreach (var kv in _toggleImageOriginalColors)
            {
                Image image = kv.Key;
                if (image == null || image == _imgPopupSettingArea) continue;
                image.color = kv.Value;
            }
        }

        private void ApplyPopupSettingAreaDifficulty(DifficultyPurpose difficulty)
        {
            // ROLLBACK_SETTINGS_AREA_DIFFICULTY_SPRITE_20260625:
            // Only PopupSettingArea changes by difficulty. Sound/Music/Haptic child images
            // keep their authored colors while the inner area swaps popupInner sprites.
            EnsurePopupSettingAreaResources();
            if (_imgPopupSettingArea == null) return;

            Sprite target = difficulty switch
            {
                DifficultyPurpose.Hard => _sprPopupInnerHard,
                DifficultyPurpose.SuperHard => _sprPopupInnerSuperHard,
                _ => _sprPopupInnerNormal
            };

            if (target == null) return;
            _imgPopupSettingArea.sprite = target;
            _imgPopupSettingArea.color = Color.white;
            _imgPopupSettingArea.enabled = true;
        }

        private void EnsurePopupSettingAreaResources()
        {
            if (_imgPopupSettingArea == null)
            {
                Transform found = FindDeep(transform, "PopupSettingArea");
                if (found != null) _imgPopupSettingArea = found.GetComponent<Image>();
            }

            if (!ResourceManager.HasInstance) return;
            var rm = ResourceManager.Instance;
            _sprPopupInnerNormal = rm.UISpriteOr(Const.SPR_POPUPINNERNORMAL, _sprPopupInnerNormal);
            _sprPopupInnerHard = rm.UISpriteOr(Const.SPR_POPUPINNERHARD, _sprPopupInnerHard);
            _sprPopupInnerSuperHard = rm.UISpriteOr(Const.SPR_POPUPINNERSUPURHARD, _sprPopupInnerSuperHard);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }

            return null;
        }

        private static DifficultyPurpose ResolveCurrentDifficulty()
        {
            // ROLLBACK_QUIT_SETTINGS_DIFFICULTY_FRAME_20260623:
            // Match PopupResult/PopupBuyItem frame color behavior for in-game settings popup.
            if (!LevelManager.HasInstance) return DifficultyPurpose.Normal;
            int levelId = LevelManager.Instance.CurrentLevelId;
            return levelId > 0
                ? LevelManager.Instance.GetLevelDifficulty(levelId)
                : DifficultyPurpose.Normal;
        }
    }
}
