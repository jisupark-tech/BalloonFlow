using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// BGM + SFX 관리. 싱글톤.
    /// SettingsManager의 Sound/Music 토글 연동.
    /// </summary>
    public class AudioManager : Singleton<AudioManager>
    {
        [Header("[Audio Sources]")]
        [SerializeField] private AudioSource _bgmSource;
        [SerializeField] private AudioSource _sfxSource;
        private AudioSource _popSource;

        [Header("[BGM]")]
        [SerializeField] private AudioClip _bgmLobby;
        [SerializeField] private AudioClip _bgmInGame;

        [Header("[SFX — Common]")]
        [SerializeField] private AudioClip _sfxNormalTouch;
        [SerializeField] private AudioClip _sfxPopupTouch;
        [SerializeField] private AudioClip _sfxCoinGain;

        [Header("[SFX — InGame]")]
        [SerializeField] private AudioClip _sfxBalloonPop;
        [SerializeField] private AudioClip _sfxBalloonPop2;
        [SerializeField] private AudioClip _sfxClear;
        [SerializeField] private AudioClip _sfxFail;
        [SerializeField] private AudioClip _sfxHolderDeploy;
        [SerializeField] private AudioClip _sfxHolderReveal;
        [SerializeField] private AudioClip _sfxHolderFrozenBreak; // = icebreak (Frozen Dart Box 해동 + Ice 풍선 HP 0)

        [Header("[SFX — InGame v2 (2026-05-31 사운드 추가)]")]
        [Tooltip("woodbreak — Wooden Board / Target Box / Pinata 나무 타격")]
        [SerializeField] private AudioClip _sfxWoodBreak;
        [Tooltip("achieve — 인게임 진행도 100% / Play 버튼 레벨 갱신 성취감 chime")]
        [SerializeField] private AudioClip _sfxAchieve;
        [Tooltip("shortfail — 실패 판정(out-of-space 팝업 등장) 짧은 멈칫음")]
        [SerializeField] private AudioClip _sfxShortFail;
        [Tooltip("coinuse — 코인 사용(돈 짤랑)")]
        [SerializeField] private AudioClip _sfxCoinUse;
        [Tooltip("deny — 이동 불가 보관함 잘못 탭(덜컹/거부)")]
        [SerializeField] private AudioClip _sfxDeny;

        [Header("[SFX — Booster]")]
        [SerializeField] private AudioClip _sfxItemHand;
        [SerializeField] private AudioClip _sfxItemShuffle;
        [SerializeField] private AudioClip _sfxItemZap;

        [Header("[Pop Combo Pitch]")]
        [Tooltip("연속 팝 SFX 피치 상승 사용 여부.")]
        [SerializeField] private bool _popPitchComboEnabled = true;
        [Tooltip("팝 SFX 기본 피치.")]
        [Range(0.5f, 2f)]
        [SerializeField] private float _popPitchBase = 1f;
        [Tooltip("연속 팝마다 더해지는 피치 증가량.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float _popPitchStep = 0.06f;
        [Tooltip("최대 피치(상한).")]
        [Range(1f, 3f)]
        [SerializeField] private float _popPitchMax = 1.8f;
        [Tooltip("이 시간(초) 동안 다음 팝이 없으면 콤보/피치 초기화.")]
        [Range(0.1f, 2f)]
        [SerializeField] private float _popComboResetSec = 0.6f;

        private bool _sfxEnabled = true;
        private bool _bgmEnabled = true;

        protected override void OnSingletonAwake()
        {
            if (_bgmSource == null)
            {
                _bgmSource = gameObject.AddComponent<AudioSource>();
                _bgmSource.loop = true;
                _bgmSource.playOnAwake = false;
            }
            if (_sfxSource == null)
            {
                _sfxSource = gameObject.AddComponent<AudioSource>();
                _sfxSource.playOnAwake = false;
            }

            _popSource = gameObject.AddComponent<AudioSource>();
            _popSource.playOnAwake = false;

            AutoLoadClips();

            if (SettingsManager.HasInstance)
            {
                _sfxEnabled = SettingsManager.Instance.SoundOn;
                _bgmEnabled = SettingsManager.Instance.MusicOn;
            }
        }

        private void AutoLoadClips()
        {
            // Inspector에서 미할당된 클립만 Resources에서 자동 로드
            if (_sfxNormalTouch == null)  _sfxNormalTouch  = Resources.Load<AudioClip>("Sound/Effect/Common_Normal_Touch");
            if (_sfxPopupTouch == null)   _sfxPopupTouch   = Resources.Load<AudioClip>("Sound/Effect/Common_Popup_Touch");
            if (_sfxCoinGain == null)     _sfxCoinGain     = Resources.Load<AudioClip>("Sound/Effect/coinearn")
                ?? Resources.Load<AudioClip>("Sound/Effect/Common_Coin_Gain");
            if (_sfxBalloonPop == null)   _sfxBalloonPop   = Resources.Load<AudioClip>("Sound/Effect/Stage_Match_Normal");
            if (_sfxBalloonPop2 == null)  _sfxBalloonPop2  = Resources.Load<AudioClip>("Sound/Effect/Stage_Match_Normal_2");
            if (_sfxClear == null)        _sfxClear        = Resources.Load<AudioClip>("Sound/Effect/congratuation")
                ?? Resources.Load<AudioClip>("Sound/Effect/Stage_Clear");
            if (_sfxFail == null)         _sfxFail         = Resources.Load<AudioClip>("Sound/Effect/fail")
                ?? Resources.Load<AudioClip>("Sound/Effect/Stage_Fail");
            if (_sfxHolderDeploy == null) _sfxHolderDeploy = Resources.Load<AudioClip>("Sound/Effect/Stage_Object_Drop");
            if (_sfxHolderReveal == null) _sfxHolderReveal = Resources.Load<AudioClip>("Sound/Effect/Stage_Holder_Reveal")
                ?? Resources.Load<AudioClip>("Sound/Effect/Stage_Object_Drop");
            // icebreak — Frozen Dart Box 해동 + Ice 풍선 HP 0 공용
            if (_sfxHolderFrozenBreak == null) _sfxHolderFrozenBreak = Resources.Load<AudioClip>("Sound/Effect/icebreak")
                ?? Resources.Load<AudioClip>("Sound/Effect/Stage_Holder_FrozenBreak")
                ?? Resources.Load<AudioClip>("Sound/Effect/Stage_ItemUse_Onedestroy");
            if (_sfxItemHand == null)     _sfxItemHand     = Resources.Load<AudioClip>("Sound/Effect/Stage_ItemUse_Onedestroy");
            if (_sfxItemShuffle == null)  _sfxItemShuffle  = Resources.Load<AudioClip>("Sound/Effect/Stage_ItemUse_Cross");
            if (_sfxItemZap == null)      _sfxItemZap      = Resources.Load<AudioClip>("Sound/Effect/Stage_ItemUse_ColorBomb");

            // [2026-05-31] 신규 사운드 — Resources/Sound/Effect/ 에 파일 배치 (이름 일치).
            if (_sfxWoodBreak == null)    _sfxWoodBreak    = Resources.Load<AudioClip>("Sound/Effect/woodbreak");
            if (_sfxAchieve == null)      _sfxAchieve      = Resources.Load<AudioClip>("Sound/Effect/achieve");
            if (_sfxShortFail == null)    _sfxShortFail    = Resources.Load<AudioClip>("Sound/Effect/shortfail");
            if (_sfxCoinUse == null)      _sfxCoinUse      = Resources.Load<AudioClip>("Sound/Effect/coinuse");
            if (_sfxDeny == null)         _sfxDeny         = Resources.Load<AudioClip>("Sound/Effect/deny");
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnBalloonPopped>(HandleBalloonPopped);
            EventBus.Subscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Subscribe<OnBoardFailed>(HandleBoardFailed);
            EventBus.Subscribe<OnBoosterUsed>(HandleBoosterUsed);
            EventBus.Subscribe<OnHolderSelected>(HandleHolderSelected);
            EventBus.Subscribe<OnHolderTapped>(HandleHolderTapped);
            EventBus.Subscribe<OnHolderClickAnim>(HandleHolderClickAnim);
            EventBus.Subscribe<OnHolderRevealed>(HandleHolderRevealed);
            EventBus.Subscribe<OnHolderThawed>(HandleHolderThawed);
            EventBus.Subscribe<OnCoinFlyLanded>(HandleCoinFlyLanded);
            EventBus.Subscribe<OnSettingsChanged>(HandleSettingsChanged);
            // [2026-05-31] 신규 사운드 라우팅
            EventBus.Subscribe<OnLevelFailed>(HandleLevelFailed);               // fail (최종 실패)
            EventBus.Subscribe<OnGimmickTriggered>(HandleGimmickTriggered);     // woodbreak / icebreak(Ice 풍선)
            EventBus.Subscribe<OnHolderColumnBlocked>(HandleHolderColumnBlocked); // deny (컬럼풀/체인)
            EventBus.Subscribe<OnCoinChanged>(HandleCoinChangedAudio);          // coinuse (delta<0)
            EventBus.Subscribe<OnContinueApplied>(HandleContinueApplied);       // BGM 재개
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnBalloonPopped>(HandleBalloonPopped);
            EventBus.Unsubscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Unsubscribe<OnBoardFailed>(HandleBoardFailed);
            EventBus.Unsubscribe<OnBoosterUsed>(HandleBoosterUsed);
            EventBus.Unsubscribe<OnHolderSelected>(HandleHolderSelected);
            EventBus.Unsubscribe<OnHolderTapped>(HandleHolderTapped);
            EventBus.Unsubscribe<OnHolderClickAnim>(HandleHolderClickAnim);
            EventBus.Unsubscribe<OnHolderRevealed>(HandleHolderRevealed);
            EventBus.Unsubscribe<OnHolderThawed>(HandleHolderThawed);
            EventBus.Unsubscribe<OnCoinFlyLanded>(HandleCoinFlyLanded);
            EventBus.Unsubscribe<OnSettingsChanged>(HandleSettingsChanged);
            EventBus.Unsubscribe<OnLevelFailed>(HandleLevelFailed);
            EventBus.Unsubscribe<OnGimmickTriggered>(HandleGimmickTriggered);
            EventBus.Unsubscribe<OnHolderColumnBlocked>(HandleHolderColumnBlocked);
            EventBus.Unsubscribe<OnCoinChanged>(HandleCoinChangedAudio);
            EventBus.Unsubscribe<OnContinueApplied>(HandleContinueApplied);
        }

        #region Public — BGM

        public void PlayLobbyBGM()
        {
            PlayBGM(_bgmLobby);
        }

        public void PlayInGameBGM()
        {
            PlayBGM(_bgmInGame);
        }

        public void StopBGM()
        {
            if (_bgmSource != null) _bgmSource.Stop();
        }

        #endregion

        #region Public — SFX

        public void PlayPopupTouch()
        {
            PlaySFX(_sfxPopupTouch);
        }

        public void PlayNormalTouch()
        {
            PlaySFX(_sfxNormalTouch);
        }

        /// <summary>achieve — 인게임 진행도 100% / Play 버튼 레벨 갱신 성취감 chime.</summary>
        public void PlayAchieve()
        {
            PlaySFX(_sfxAchieve);
        }

        /// <summary>deny — 이동 불가 보관함 잘못 탭(덜컹/거부). Hidden/Frozen/Lock reject 분기에서 직접 호출.</summary>
        public void PlayDeny()
        {
            PlaySFX(_sfxDeny);
        }

        #endregion

        #region Event Handlers

        private float _lastPopTime;
        private int _popComboCount;
        private AudioClip _lastPopClip;
        private const float POP_SFX_COOLDOWN = 0.05f; // 50ms 쿨다운

        private void HandleBalloonPopped(OnBalloonPopped evt)
        {
            float now = Time.unscaledTime;
            if (now - _lastPopTime < POP_SFX_COOLDOWN) return;

            if (_popPitchComboEnabled && now - _lastPopTime > _popComboResetSec)
            {
                _popComboCount = 0;
                _lastPopClip = null;
            }

            float pitch = _popPitchComboEnabled
                ? Mathf.Min(_popPitchBase + _popPitchStep * _popComboCount, _popPitchMax)
                : 1f;

            _lastPopTime = now;
            _popComboCount++;

            if (_popSource == null || !_sfxEnabled || _sfxBalloonPop == null) return;

            // 콤보 시작 시 한 번 50% 랜덤으로 클립을 정하고, 피치 상승 중엔 그 클립을 고정 재생.
            // 최고 피치(_popPitchMax) 도달 이후에만 매 팝마다 두 클립 사이에서 50% 랜덤 재추첨해 단조로움을 줄인다.
            bool atMaxPitch = _popPitchComboEnabled && pitch >= _popPitchMax;
            if (_lastPopClip == null)
            {
                _lastPopClip = (_sfxBalloonPop2 != null && Random.value < 0.5f) ? _sfxBalloonPop2 : _sfxBalloonPop;
            }
            else if (atMaxPitch)
            {
                _lastPopClip = (_sfxBalloonPop2 != null && Random.value < 0.5f) ? _sfxBalloonPop2 : _sfxBalloonPop;
            }

            _popSource.pitch = pitch;
            _popSource.PlayOneShot(_lastPopClip);
        }

        private void HandleBoardCleared(OnBoardCleared evt)
        {
            // congratuation (클리어 팡파레) + play BGM 정지
            StopBGM();
            PlaySFX(_sfxClear);
        }

        private void HandleBoardFailed(OnBoardFailed evt)
        {
            // shortfail (실패 판정 → out-of-space/PopupFail01 등장, 멈칫) + play BGM 정지.
            // 최종 실패음(fail)은 OnLevelFailed 에서 (이어하기 거절 후).
            StopBGM();
            PlaySFX(_sfxShortFail != null ? _sfxShortFail : _sfxFail);
        }

        private void HandleLevelFailed(OnLevelFailed evt)
        {
            // fail (최종 실패음, ~2-3s). BGM 은 이미 OnBoardFailed 에서 정지됨.
            PlaySFX(_sfxFail);
        }

        // 기믹 타격/파괴 사운드 — Ice(icebreak) / Pinata·Pin·PinataBox(woodbreak). 연속 셀 spam 쿨다운.
        private float _lastGimmickSfxTime;
        private const float GIMMICK_SFX_COOLDOWN = 0.08f;
        private void HandleGimmickTriggered(OnGimmickTriggered evt)
        {
            float now = Time.unscaledTime;
            if (now - _lastGimmickSfxTime < GIMMICK_SFX_COOLDOWN) return;

            AudioClip clip = null;
            if (evt.gimmickType == BalloonController.GimmickIce)
                clip = _sfxHolderFrozenBreak; // = icebreak
            else if (evt.gimmickType == BalloonController.GimmickPinata
                  || evt.gimmickType == BalloonController.GimmickPin
                  || evt.gimmickType == BalloonController.GimmickPinataBox)
                clip = _sfxWoodBreak;

            if (clip == null) return; // 그 외 기믹(Spawner/Lock/Wall 등)은 무음
            _lastGimmickSfxTime = now;
            PlaySFX(clip);
        }

        private void HandleHolderColumnBlocked(OnHolderColumnBlocked evt)
        {
            // deny (이동 불가 보관함 — 컬럼풀 3번째 탭 / 체인 앞줄 미충족)
            PlayDeny();
        }

        private void HandleCoinChangedAudio(OnCoinChanged evt)
        {
            // coinuse — 코인 사용(소비)만. 획득(delta>0)은 OnCoinFlyLanded 가 처리.
            if (evt.delta < 0) PlaySFX(_sfxCoinUse);
        }

        private void HandleContinueApplied(OnContinueApplied evt)
        {
            // 이어하기 → 게임 재개 시 InGame BGM 복구 (OnBoardFailed 에서 정지했으므로).
            PlayInGameBGM();
        }

        private void HandleBoosterUsed(OnBoosterUsed evt)
        {
            AudioClip clip = evt.boosterType switch
            {
                BoosterManager.SELECT_TOOL  => _sfxItemHand,
                BoosterManager.SHUFFLE      => _sfxItemShuffle,
                BoosterManager.COLOR_REMOVE => _sfxItemZap,
                _                           => null
            };
            PlaySFX(clip);
        }

        private void HandleHolderSelected(OnHolderSelected evt)
        {
            PlaySFX(_sfxHolderDeploy);
        }

        private void HandleHolderTapped(OnHolderTapped evt)
        {
        }

        private void HandleHolderClickAnim(OnHolderClickAnim evt)
        {
        }

        private void HandleHolderRevealed(OnHolderRevealed evt)
        {
            PlaySFX(_sfxHolderReveal);
        }

        private void HandleHolderThawed(OnHolderThawed evt)
        {
            PlaySFX(_sfxHolderFrozenBreak);
        }

        // [2026-05-12] 코인 흡수 사운드 1/3 감소 — count 별 매번 재생 시 청각 부담. 3 번째마다 1번 재생.
        private int _coinSfxCounter;
        private void HandleCoinFlyLanded(OnCoinFlyLanded evt)
        {
            if (_coinSfxCounter++ % 3 == 0) PlaySFX(_sfxCoinGain);
        }

        private void HandleSettingsChanged(OnSettingsChanged evt)
        {
            if (SettingsManager.HasInstance)
            {
                _sfxEnabled = SettingsManager.Instance.SoundOn;
                _bgmEnabled = SettingsManager.Instance.MusicOn;

                if (_bgmSource != null)
                    _bgmSource.mute = !_bgmEnabled;
            }
        }

        #endregion

        #region Private

        private void PlayBGM(AudioClip clip)
        {
            if (_bgmSource == null || clip == null) return;
            if (_bgmSource.clip == clip && _bgmSource.isPlaying) return;

            _bgmSource.loop = true;
            _bgmSource.clip = clip;
            _bgmSource.mute = !_bgmEnabled;
            _bgmSource.Play();
        }

        private void PlaySFX(AudioClip clip)
        {
            if (_sfxSource == null || clip == null || !_sfxEnabled) return;
            _sfxSource.PlayOneShot(clip);
        }

        /// <summary>
        /// 모든 SFX 즉시 중단 (PlayOneShot으로 재생 중인 클립 포함).
        /// 씬 전환 시 보상 사운드 등이 다음 씬으로 넘어가 이어지는 현상 방지.
        /// </summary>
        public void StopAllSfx()
        {
            if (_sfxSource != null) _sfxSource.Stop();
            if (_popSource != null) _popSource.Stop();
        }

        #endregion
    }
}
