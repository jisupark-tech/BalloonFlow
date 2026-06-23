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

        [Header("[Loop SFX Source]")]
        [Tooltip("루프 재생용 별도 AudioSource. _sfxSource(PlayOneShot, 일회성) / _popSource(풍선 팝 피치 콤보)와 분리되어 FinishLogo 불꽃 등 장시간 SFX 루프를 안전하게 재생.")]
        [SerializeField] private AudioSource _loopSfxSource;

        [Header("[BGM]")]
        [SerializeField] private AudioClip _bgmLobby;
        [SerializeField] private AudioClip _bgmInGame;

        [Header("[SFX — Common]")]
        [SerializeField] private AudioClip _sfxNormalTouch;
        [SerializeField] private AudioClip _sfxPopupTouch;
        [Tooltip("Common_Button_Touch — 전역 UI 버튼 탭 SFX (UIButtonClickGuard 후크).")]
        [SerializeField] private AudioClip _sfxButtonClick;
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

        [Header("[SFX — Result]")]
        [Tooltip("Stage_Result — PopupResult 오픈 직후 재생되는 결과 화면 SFX.")]
        [SerializeField] private AudioClip _sfxStageResult;
        [Tooltip("FinishLogo 연출 동안 루프 재생되는 불꽃 SFX. Stage_Result(1회)와는 별개 트랙.")]
        [SerializeField] private AudioClip _sfxStageResultFirework;

        [Header("[SFX — Lobby]")]
        [Tooltip("Lobby_Rail — UILobby Top/Bottom Rail 이동 시작 시 1회 재생. PlayRailEnterAnimation/PlayRailPullDownAnimation 진입점에서 호출. PlayOneShot 이므로 호출당 1회 보장.")]
        [SerializeField] private AudioClip _sfxLobbyRail;

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

            if (_loopSfxSource == null)
            {
                _loopSfxSource = gameObject.AddComponent<AudioSource>();
            }
            _loopSfxSource.loop = true;
            _loopSfxSource.playOnAwake = false;
            if (_sfxSource != null && _sfxSource.outputAudioMixerGroup != null)
                _loopSfxSource.outputAudioMixerGroup = _sfxSource.outputAudioMixerGroup;

            AutoLoadClips();

            if (SettingsManager.HasInstance)
            {
                _sfxEnabled = SettingsManager.Instance.SoundOn;
                _bgmEnabled = SettingsManager.Instance.MusicOn;
            }
        }

        private void AutoLoadClips()
        {
            // Inspector에서 미할당된 클립만 Resources에서 자동 로드.
            // 태스크 명세 파일명(Common_Button_Touch, shortfail, woodbreak, achieve, coinuse, deny 등)이
            // 아직 Resources 에 배치되지 않은 상태에서도 무음이 되지 않도록 ?? 체인으로 현존 클립에 폴백.
            if (_sfxNormalTouch == null)  _sfxNormalTouch  = Resources.Load<AudioClip>("Sound/Effect/Common_Normal_Touch");
            if (_sfxPopupTouch == null)   _sfxPopupTouch   = Resources.Load<AudioClip>("Sound/Effect/Common_Popup_Touch");
            if (_sfxButtonClick == null)  _sfxButtonClick  = Resources.Load<AudioClip>("Sound/Effect/Common_Button_Touch")
                ?? Resources.Load<AudioClip>("Sound/Effect/Common_Normal_Touch")
                ?? Resources.Load<AudioClip>("Sound/Effect/Common_Popup_Touch");
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
            // 파일이 아직 없는 동안에도 청취 가능한 폴백을 적용해 사일런트 회귀 방지.
            if (_sfxWoodBreak == null)    _sfxWoodBreak    = Resources.Load<AudioClip>("Sound/Effect/woodbreak")
                ?? Resources.Load<AudioClip>("Sound/Effect/Stage_Object_Drop")
                ?? Resources.Load<AudioClip>("Sound/Effect/Stage_Match_Normal");
            if (_sfxAchieve == null)      _sfxAchieve      = Resources.Load<AudioClip>("Sound/Effect/achieve")
                ?? Resources.Load<AudioClip>("Sound/Effect/Stage_Clear");
            if (_sfxShortFail == null)    _sfxShortFail    = Resources.Load<AudioClip>("Sound/Effect/shortfail")
                ?? Resources.Load<AudioClip>("Sound/Effect/Stage_Fail");
            if (_sfxCoinUse == null)      _sfxCoinUse      = Resources.Load<AudioClip>("Sound/Effect/coinuse")
                ?? Resources.Load<AudioClip>("Sound/Effect/Common_Coin_Gain")
                ?? Resources.Load<AudioClip>("Sound/Effect/Common_Normal_Touch");
            if (_sfxDeny == null)         _sfxDeny         = Resources.Load<AudioClip>("Sound/Effect/deny")
                ?? Resources.Load<AudioClip>("Sound/Effect/Common_Normal_Touch");

            // [2026-06-23] Stage_Result — PopupResult 오픈 직후 1회 재생.
            // _sfxClear(congratuation)는 보드클리어 팡파레로 별개 트랙이라 폴백 체인에 두지 않음.
            if (_sfxStageResult == null) _sfxStageResult = Resources.Load<AudioClip>("Sound/Effect/Stage_Result");
            // [2026-06-23] Stage_Result_Firework — FinishLogo 연출 동안 _loopSfxSource 로 루프 재생.
            // _sfxStageResult(1회 재생)와는 별개 트랙·별개 시점이므로 폴백 체인 없이 단독 로드.
            if (_sfxStageResultFirework == null) _sfxStageResultFirework = Resources.Load<AudioClip>("Sound/Effect/Stage_Result_Firework");
            // [2026-06-23] Lobby_Rail — UILobby Rail 이동 1회 재생 (사용자 추가 지시).
            if (_sfxLobbyRail == null) _sfxLobbyRail = Resources.Load<AudioClip>("Sound/Effect/Lobby_Rail");

#if UNITY_EDITOR
            // Editor 전용 진단 — 폴백조차 실패해 여전히 null 인 SFX 의 '원본 명세 파일명' 을 한 줄로 보고.
            // Runtime 빌드에서는 제외되어 로그 부담 없음.
            var missing = new System.Collections.Generic.List<string>();
            if (_sfxNormalTouch == null)       missing.Add("Common_Normal_Touch");
            if (_sfxPopupTouch == null)        missing.Add("Common_Popup_Touch");
            if (_sfxButtonClick == null)       missing.Add("Common_Button_Touch");
            if (_sfxCoinGain == null)          missing.Add("coinearn");
            if (_sfxBalloonPop == null)        missing.Add("Stage_Match_Normal");
            if (_sfxBalloonPop2 == null)       missing.Add("Stage_Match_Normal_2");
            if (_sfxClear == null)             missing.Add("congratuation");
            if (_sfxFail == null)              missing.Add("fail");
            if (_sfxHolderDeploy == null)      missing.Add("Stage_Object_Drop");
            if (_sfxHolderReveal == null)      missing.Add("Stage_Holder_Reveal");
            if (_sfxHolderFrozenBreak == null) missing.Add("icebreak");
            if (_sfxItemHand == null)          missing.Add("Stage_ItemUse_Onedestroy");
            if (_sfxItemShuffle == null)       missing.Add("Stage_ItemUse_Cross");
            if (_sfxItemZap == null)           missing.Add("Stage_ItemUse_ColorBomb");
            if (_sfxWoodBreak == null)         missing.Add("woodbreak");
            if (_sfxAchieve == null)           missing.Add("achieve");
            if (_sfxShortFail == null)         missing.Add("shortfail");
            if (_sfxCoinUse == null)           missing.Add("coinuse");
            if (_sfxDeny == null)              missing.Add("deny");
            if (_sfxStageResult == null)       missing.Add("Stage_Result");
            if (_sfxStageResultFirework == null) missing.Add("Stage_Result_Firework");
            if (_sfxLobbyRail == null)         missing.Add("Lobby_Rail");
            if (missing.Count > 0)
            {
                Debug.LogWarning($"[AudioManager] Missing SFX clips (place files under Resources/Sound/Effect/): {string.Join(", ", missing)}");
            }
#endif
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
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);               // 레벨 진입 시 InGame BGM 재시작
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
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);
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

        /// <summary>Common_Button_Touch — UIButtonClickGuard 전역 후크에서 모든 UI 버튼 탭 시 호출.</summary>
        public void PlayButtonClick()
        {
            PlaySFX(_sfxButtonClick);
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

        /// <summary>Stage_Result — PopupResult 오픈 직후 1회 재생(태스크: 사운드 추가 2026-06-23).
        /// PlayOneShot 이므로 호출당 1회 재생 보장. 팝업이 표시될 때마다 호출하면 됨.</summary>
        public void PlayStageResult()
        {
            PlaySFX(_sfxStageResult);
        }

        /// <summary>FinishLogo 연출 동안 루프 재생. 트리거: GameBootstrap.PlayFinishLogoSequence 시작 /
        /// 정지 트리거: 동 코루틴 종료 또는 PopupResult.ShowWin/ShowFail 진입(방어).
        /// 사용자 지시 2026-06-23 (task 코멘트), 직전 PR #369(_sfxStageResult 1회 재생)와 별개 트랙.</summary>
        public void PlayStageResultFireworkLoop()
        {
            if (_loopSfxSource == null || _sfxStageResultFirework == null || !_sfxEnabled) return;
            if (_loopSfxSource.isPlaying && _loopSfxSource.clip == _sfxStageResultFirework) return;
            _loopSfxSource.clip = _sfxStageResultFirework;
            _loopSfxSource.loop = true;
            _loopSfxSource.Play();
        }

        /// <summary>FinishLogo 연출 종료 또는 다음 팝업(PopupResult) 진입 시 정지.
        /// 다른 루프 SFX 와의 오작동 방지를 위해 현재 재생 중 클립이 _sfxStageResultFirework 일 때만 정지.
        /// 사용자 지시 2026-06-23 (task 코멘트).</summary>
        public void StopStageResultFirework()
        {
            if (_loopSfxSource == null) return;
            if (!_loopSfxSource.isPlaying) return;
            if (_loopSfxSource.clip != _sfxStageResultFirework) return;
            _loopSfxSource.Stop();
            _loopSfxSource.clip = null;
        }

        /// <summary>Lobby_Rail — UILobby Rail(Top/Bottom) 이동 시작 시 1회 재생.
        /// 호출 위치: UILobby.PlayRailEnterAnimation / PlayRailPullDownAnimation 진입점.
        /// PlayOneShot 이라 함수당 1회 보장 — Top/Bottom 동시 변경 케이스에서도 호출자가 1번만 부르면 1회 재생.</summary>
        public void PlayLobbyRail()
        {
            PlaySFX(_sfxLobbyRail);
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
        // isDestroyed=false (HP>0, 타격 only) → 풍선 pop SFX (_sfxBalloonPop=Stage_Match_Normal).
        // isDestroyed=true  (HP=0, 파괴)     → 기존 woodbreak/icebreak.
        private float _lastGimmickSfxTime;
        private const float GIMMICK_SFX_COOLDOWN = 0.08f;
        private void HandleGimmickTriggered(OnGimmickTriggered evt)
        {
            float now = Time.unscaledTime;
            if (now - _lastGimmickSfxTime < GIMMICK_SFX_COOLDOWN) return;

            AudioClip clip = null;
            if (evt.gimmickType == BalloonController.GimmickIce)
                clip = _sfxHolderFrozenBreak; // = icebreak (Ice 영역 해제 = 파괴)
            else if (evt.gimmickType == BalloonController.GimmickPinata
                  || evt.gimmickType == BalloonController.GimmickPin
                  || evt.gimmickType == BalloonController.GimmickPinataBox)
                clip = evt.isDestroyed ? _sfxWoodBreak : _sfxBalloonPop;

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

        // 모든 레벨 진입(첫 진입/Next/Retry/Continue/CheatJump) 시 InGame BGM 재시작.
        // HandleBoardCleared 가 StopBGM() 하므로 Next Level 전환에서 무음이 되는 케이스 차단.
        // PlayBGM 의 same-clip 가드 우회를 위해 Stop 후 Play 시퀀스.
        private void HandleLevelLoaded(OnLevelLoaded evt)
        {
            if (_bgmSource != null) _bgmSource.Stop();
            PlayInGameBGM();
        }

        private void HandleBoosterUsed(OnBoosterUsed evt)
        {
            // 부스터 SFX 제거 — 전역 Button Click SFX(UIButtonClickGuard)만 사용
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
