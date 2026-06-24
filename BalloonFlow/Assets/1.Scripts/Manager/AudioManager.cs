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

        [Tooltip("FinishLogo congratuation 전용 AudioSource. _sfxSource(PlayOneShot 일회성, StopAllSfx 대상) 와 분리되어 BeginResultIntroSfxLock 의 StopAllSfx + _resultIntroSfxLock 화이트리스트 게이트 양쪽 모두 우회. 사용자 피드백 2026-06-23: congratuation 재생 중간 강제 정지 금지.")]
        [SerializeField] private AudioSource _finishLogoSfxSource;

        [Header("[BGM]")]
        [SerializeField] private AudioClip _bgmLobby;
        [SerializeField] private AudioClip _bgmInGame;

        [Header("[SFX — Common]")]
        [SerializeField] private AudioClip _sfxNormalTouch;
        [SerializeField] private AudioClip _sfxPopupTouch;
        [Tooltip("Common_Button_Touch — 전역 UI 버튼 탭 SFX (UIButtonClickGuard 후크).")]
        [SerializeField] private AudioClip _sfxButtonClick;

        [Header("[SFX — FXGold (2026-06-23 사용자 피드백)]")]
        [Tooltip("Gold_Appear — CoinFlyEffect.Play 시 1회 재생. 호출 위치: CoinFlyEffect.Play 진입부. 사용자 피드백 2026-06-23: FXGold 등장 시 1회.")]
        [SerializeField] private AudioClip _sfxGoldAppear;
        [Tooltip("Gold_Get — CoinFlyEffect 첫 코인 목적지 도착 시 연속 3회 재생. 호출 위치: CoinFlyEffect.RunFly landed==1 분기 → AudioManager.PlayGoldGet 코루틴. 사용자 피드백 2026-06-23: FXGold 목적지 도착 연속 3회. Common_Coin_Gain.mp3 대체.")]
        [SerializeField] private AudioClip _sfxGoldGet;

        [Header("[SFX — InGame]")]
        [SerializeField] private AudioClip _sfxBalloonPop;
        [SerializeField] private AudioClip _sfxBalloonPop2;
        [Tooltip("congratuation — FinishLogo 등장 시점 1회 재생(태스크: 사운드 추가 2026-06-23, 사용자 피드백 반영). _finishLogoSfxSource(전용 채널)에서 PlayOneShot 되므로 BeginResultIntroSfxLock 의 StopAllSfx 및 _resultIntroSfxLock whitelist 게이트 양쪽 모두 우회 — 사용자가 명시한 '중간에 강제로 정지되지 않도록' 요구 충족.")]
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
        [Tooltip("Lobby_RailBox_Start — LobbyRailBox 오픈(BoxOpen Animator trigger) 연출 시작 시 1회 재생. 호출 위치: LobbyRailBox.PlayStartGameAnimation 진입점. PlayOneShot 이라 함수당 1회 보장.")]
        [SerializeField] private AudioClip _sfxLobbyRailBoxStart;

        [Header("[SFX — Ingame Enter (2026-06-23 사용자 피드백)]")]
        [Tooltip("Ingame_Start — 로비→인게임 진입 슬라이드-인 시작 시 1회 재생. 호출 위치: UIHud.PlayIngameEnterAnimation 진입부(HUD_Top tween Join 직후). PlayOneShot 이라 호출당 1회 보장 — HUD_Top anchoredPosition 이 tween 으로 매 프레임 갱신되어도 PlayIngameEnterAnimation 자체가 진입당 1회 호출이므로 자연 1회.")]
        [SerializeField] private AudioClip _sfxIngameStart;

        [Header("[SFX — WinningStreak (2026-06-23 사용자 피드백)]")]
        [Tooltip("GaugeUp — UILobby.PlayWinningStreakLobbyFx 코루틴 내 _wsProgressSlider.DOValue 시작 시점 1회 재생. 호출 위치: PlayWinningStreakLobbyFx 의 두 DOValue 호출 직전 (1차 endRatio 채움, 2차 다음 스테이지 carry 채움). PlayOneShot 이라 호출당 1회 보장 — 두 호출 모두 코루틴 1회 진입당 각각 1회씩만 발생함이 보장됨(while/for iteration 단위 = 슬라이더 증가 이벤트 단위). DOTween OnUpdate/OnStepComplete 콜백에 절대 넣지 말 것 — 프레임당 다발 호출되어 무한 중첩 잔향 됨.")]
        [SerializeField] private AudioClip _sfxWsGaugeUp;
        [Tooltip("Common_Ding — PopupWinningStreakReward 카운터(TxtAmount/TxtAmountOutline) 증가 이벤트마다 1회 재생. (a) 호출 위치: PopupWinningStreakReward.ApplyAmount 의 amount>from(증가) 분기 직후 — _displayedAmount 갱신 직후 단 한 줄(if (from < amount) ...). (b) 1회 보장 근거: ApplyAmount 는 FXItem 비행체(FXBadge 도착 / FXMultiple 도착 / gainedPoints 최종 보정) 콜백/iteration 단위로 호출되므로 증가 이벤트 1회당 ApplyAmount 1회 = SFX 1회 → 사용자 요구 '2번 증가→2번 재생' 자동 충족. (c) 절대 DOTween.To 의 onUpdate 람다(PopupWinningStreakReward.cs:412 rolling-count `v => { rolling = v; SetAmountText(...) }`) 안에서 호출 금지 — 매 프레임 호출되어 잔향 무한 중첩(과거 도메인 원칙 4 회귀).")]
        [SerializeField] private AudioClip _sfxWsRewardDing;
        [Tooltip("Item_Get — 두 트리거 모두에서 1회씩 재생 (coexists, NOT supersede). (A) UILobby.PlayWsFireFlyAndPulse ImageIcon DOScale 펄스 시작 1회 — if (target != null) 블록 안 baseScale 캡처 직후 1회 발화, 펄스 1회당 1회 자연 게이팅. (B) UILobby.SetWinningIconMultiplierText changed-edge 1회 — WinningIcon Multiple 텍스트(TextGauge/TextGaugeOutline) 값이 이전과 다른 값으로 갱신되는 순간(INC/DEC 양방향), PlayWsMultipleFxFire/PlayWsMultiplierTextPunch 와 동일 가드 공유: (1) changed bool 가드, (2) _wsHoldMultiplierTextDuringAnim 으로 연출 hold 중 진입 자체 차단, (3) 두 텍스트(Gauge/Outline)는 동일 메서드 1회 호출 내 함께 갱신되므로 중복 발화 불가. (A)와 (B)는 시간상 분리(fire-fly + pulse + slider + multiplier slide-in 등 1.5s+ 간격) 되어 overlap/masking 없음. 절대 DOTween OnUpdate/매 프레임 lambda 에서 호출 금지(잔향 중첩 회귀 방지).")]
        [SerializeField] private AudioClip _sfxItemGet;

        [Header("[SFX — FXItem Fly (2026-06-24 사용자 피드백)]")]
        [Tooltip("Item_Fly — ItemFlyEffect.Play() 진입부에서 1회 재생. 호출 위치: ItemFlyEffect.Play 진입부(early-return 통과 직후, BeginSession 직전). PlayOneShot 이라 session 1회당 1회 보장 — 내부 코루틴 spawn N개와 무관하게 1회. 절대 RunFly/Fly 코루틴의 매 프레임 lambda 에 넣지 말 것(잔향 중첩 회귀). 새 Play() 호출 = 새 session = 새 SFX 1회 (사용자 요구 '연출마다 1회 재생' 자동 충족). owner: ProjectHub 태스크 2026-06-24 익명 사용자 피드백.")]
        [SerializeField] private AudioClip _sfxItemFly;

        [Header("[SFX — Booster]")]
        [SerializeField] private AudioClip _sfxItemHand;
        [SerializeField] private AudioClip _sfxItemShuffle;
        [SerializeField] private AudioClip _sfxItemZap;
        [Tooltip("Zap_Appear — ItemZap 등장 시 1회 재생. 호출 위치: BoosterExecutor.CreateItemZap (Instantiate 성공 직후). PlayOneShot 이라 함수당 1회 보장.")]
        [SerializeField] private AudioClip _sfxZapAppear;
        [Tooltip("Zap_Line — FxZapLine 이 새 타겟으로 이동할 때마다 재생. 호출 위치: BoosterExecutor.PlayColorRemoveSequenceBody (ConfigureZapLineFan 호출 직후, 타겟 루프 매 iteration). stepDelay 가 최소 간격을 보장.")]
        [SerializeField] private AudioClip _sfxZapLine;

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
        private bool _resultIntroSfxLock;

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

            if (_finishLogoSfxSource == null)
            {
                _finishLogoSfxSource = gameObject.AddComponent<AudioSource>();
            }
            _finishLogoSfxSource.loop = false;
            _finishLogoSfxSource.playOnAwake = false;
            if (_sfxSource != null && _sfxSource.outputAudioMixerGroup != null)
                _finishLogoSfxSource.outputAudioMixerGroup = _sfxSource.outputAudioMixerGroup;

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
            // [2026-06-23 사용자 피드백] Gold_Appear / Gold_Get — FXGold 등장/도착 SFX. 폴백 체인 없음(Zap_Appear 패턴).
            if (_sfxGoldAppear == null)   _sfxGoldAppear   = Resources.Load<AudioClip>("Sound/Effect/Gold_Appear");
            if (_sfxGoldGet == null)      _sfxGoldGet      = Resources.Load<AudioClip>("Sound/Effect/Gold_Get");
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
            // [2026-06-23] Zap_Appear / Zap_Line — ItemZap 등장 + FxZapLine 타겟 이동 SFX.
            // 폴백 체인 없이 단독 로드: 다른 클립으로 대체되면 의도 오염 (Zap 전용 SFX 추가).
            if (_sfxZapAppear == null)    _sfxZapAppear    = Resources.Load<AudioClip>("Sound/Effect/Zap_Appear");
            if (_sfxZapLine == null)      _sfxZapLine      = Resources.Load<AudioClip>("Sound/Effect/Zap_Line");

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
            if (_sfxLobbyRailBoxStart == null) _sfxLobbyRailBoxStart = Resources.Load<AudioClip>("Sound/Effect/Lobby_RailBox_Start");
            if (_sfxIngameStart == null) _sfxIngameStart = Resources.Load<AudioClip>("Sound/Effect/Ingame_Start");
            // [2026-06-23] GaugeUp — WinningStreak 슬라이더 증가 시작 시점 1회 재생. 폴백 체인 없이 단독 로드(Zap 패턴): 다른 클립 대체 시 의도 오염.
            if (_sfxWsGaugeUp == null) _sfxWsGaugeUp = Resources.Load<AudioClip>("Sound/Effect/GaugeUp");
            // [2026-06-24] Common_Ding — PopupWinningStreakReward.ApplyAmount 증가 분기 1회. 폴백 체인 없이 단독 로드(Zap_Appear/GaugeUp 패턴): 다른 클립 대체 시 의도 오염.
            if (_sfxWsRewardDing == null) _sfxWsRewardDing = Resources.Load<AudioClip>("Sound/Effect/Common_Ding");
            // [2026-06-24] Item_Get — (A)+(B) 두 트리거 모두 owner 2026-06-24 명세 (coexists, NOT supersede): (A) UILobby.PlayWsFireFlyAndPulse ImageIcon DOScale 펄스 시작 1회 + (B) UILobby.SetWinningIconMultiplierText changed-edge 1회. 폴백 체인 없이 단독 로드(Zap_Appear/GaugeUp/Ding 패턴): 다른 클립 대체 시 의도 오염.
            if (_sfxItemGet == null) _sfxItemGet = Resources.Load<AudioClip>("Sound/Effect/Item_Get");
            // [2026-06-24] Item_Fly — ItemFlyEffect.Play() 진입 1회. 폴백 체인 없이 단독 로드(Zap_Appear/GaugeUp/Ding/Item_Get 패턴): 다른 클립 대체 시 의도 오염.
            if (_sfxItemFly == null) _sfxItemFly = Resources.Load<AudioClip>("Sound/Effect/Item_Fly");

#if UNITY_EDITOR
            // Editor 전용 진단 — 폴백조차 실패해 여전히 null 인 SFX 의 '원본 명세 파일명' 을 한 줄로 보고.
            // Runtime 빌드에서는 제외되어 로그 부담 없음.
            var missing = new System.Collections.Generic.List<string>();
            if (_sfxNormalTouch == null)       missing.Add("Common_Normal_Touch");
            if (_sfxPopupTouch == null)        missing.Add("Common_Popup_Touch");
            if (_sfxButtonClick == null)       missing.Add("Common_Button_Touch");
            if (_sfxGoldAppear == null)        missing.Add("Gold_Appear");
            if (_sfxGoldGet == null)           missing.Add("Gold_Get");
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
            if (_sfxZapAppear == null)         missing.Add("Zap_Appear");
            if (_sfxZapLine == null)           missing.Add("Zap_Line");
            if (_sfxWoodBreak == null)         missing.Add("woodbreak");
            if (_sfxAchieve == null)           missing.Add("achieve");
            if (_sfxShortFail == null)         missing.Add("shortfail");
            if (_sfxCoinUse == null)           missing.Add("coinuse");
            if (_sfxDeny == null)              missing.Add("deny");
            if (_sfxStageResult == null)       missing.Add("Stage_Result");
            if (_sfxStageResultFirework == null) missing.Add("Stage_Result_Firework");
            if (_sfxLobbyRail == null)         missing.Add("Lobby_Rail");
            if (_sfxLobbyRailBoxStart == null) missing.Add("Lobby_RailBox_Start");
            if (_sfxIngameStart == null)       missing.Add("Ingame_Start");
            if (_sfxWsGaugeUp == null)         missing.Add("GaugeUp");
            if (_sfxWsRewardDing == null)      missing.Add("Common_Ding");
            if (_sfxItemGet == null)           missing.Add("Item_Get");
            if (_sfxItemFly == null)           missing.Add("Item_Fly");
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

        /// <summary>congratuation (FinishLogo 등장 1-shot). 트리거: GameBootstrap.PlayFinishLogoSequence — BeginResultIntroSfxLock + PlayStageResultFireworkLoop 와 동일 프레임에 호출.
        /// 사용자 피드백 2026-06-23: 재생 중간에 강제로 정지되지 않아야 함 → 전용 _finishLogoSfxSource 사용으로 StopAllSfx(_sfxSource/_popSource 대상)·_resultIntroSfxLock 게이트(PlaySFX 내부) 양쪽 모두 우회.
        /// Stage_Result_Firework 루프(_loopSfxSource) 와는 별개 채널이라 동시 재생/상호 비간섭 보장.</summary>
        public void PlayFinishLogoCongratuation()
        {
            if (_finishLogoSfxSource == null || _sfxClear == null || !_sfxEnabled) return;
            _finishLogoSfxSource.PlayOneShot(_sfxClear);
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

        /// <summary>FinishLogo 표시 구간 동안 SE 화이트리스트(Stage_Result_Firework only) 외 SFX 차단 게이트.
        /// 외부에서 읽기 전용(컨트롤러/뷰가 가드용으로 참조). 사용자 지시 2026-06-23 task #377 후속.
        /// [2026-06-23 revert] Stage_Result 는 화이트리스트에서 제외 — PopupResult 오픈 시점에서만 재생되므로 lock 구간 통과 불필요.</summary>
        public bool IsResultIntroSfxLocked => _resultIntroSfxLock;

        /// <summary>
        /// FinishLogo 표시 구간 SE 화이트리스트 락 시작.
        /// [트리거 시작: GameBootstrap.PlayFinishLogoSequence 진입
        ///  / 종료: PopupResult.ShowWin·ShowFail (다음 팝업 진입) — 사용자 지시 2026-06-23 task #377 후속]
        /// 이미 재생 중인 잔여 SE 즉시 정지(StopAllSfx) + 풍선 팝 콤보 카운터 리셋.
        /// _loopSfxSource(Firework 루프)는 정지하지 않음 — 이 함수 직후 PlayStageResultFireworkLoop 가 시작되는 순서 보호.
        /// </summary>
        public void BeginResultIntroSfxLock()
        {
            _resultIntroSfxLock = true;
            StopAllSfx();
            _popComboCount = 0;
            _lastPopClip = null;
        }

        /// <summary>
        /// FinishLogo 표시 구간 SE 화이트리스트 락 종료.
        /// 트리거: PopupResult.ShowWin / ShowFail (다음 팝업 진입). 사용자 지시 2026-06-23 task #377 후속.
        /// </summary>
        public void EndResultIntroSfxLock()
        {
            _resultIntroSfxLock = false;
        }

        /// <summary>Lobby_Rail — UILobby Rail(Top/Bottom) 이동 시작 시 1회 재생.
        /// 호출 위치: UILobby.PlayRailEnterAnimation / PlayRailPullDownAnimation 진입점.
        /// PlayOneShot 이라 함수당 1회 보장 — Top/Bottom 동시 변경 케이스에서도 호출자가 1번만 부르면 1회 재생.</summary>
        public void PlayLobbyRail()
        {
            PlaySFX(_sfxLobbyRail);
        }

        /// <summary>Lobby_RailBox_Start — LobbyRailBox 오픈 연출(BoxOpen) 시작 시 1회 재생.
        /// 호출 위치: LobbyRailBox.PlayStartGameAnimation 진입점.
        /// PlayOneShot 채널이라 함수당 1회 보장, 동일 프레임 중복 호출은 호출자(LobbyController.cs:338 BtnPlay.interactable=false)에서 이미 차단.</summary>
        public void PlayLobbyRailBoxStart()
        {
            PlaySFX(_sfxLobbyRailBoxStart);
        }

        /// <summary>Ingame_Start — 로비→인게임 진입 HUD 슬라이드-인 시작 시 1회 재생.
        /// 호출 위치: UIHud.PlayIngameEnterAnimation 진입부.
        /// PlayOneShot 이라 호출당 1회 보장 — HUD_Top tween 으로 위치가 여러 프레임 갱신되어도 PlayIngameEnterAnimation 자체가 진입당 1회.</summary>
        public void PlayIngameStart()
        {
            PlaySFX(_sfxIngameStart);
        }

        /// <summary>GaugeUp — WinningStreak 게이지 슬라이더 증가(_wsProgressSlider.DOValue) 시작 시점 1회 재생.
        /// 호출 위치: UILobby.PlayWinningStreakLobbyFx 의 두 DOValue 호출 직전(① 메인 endRatio 채움, ② 다음 스테이지 carry 채움).
        /// PlayOneShot 이라 호출당 1회 보장 — 두 호출 모두 코루틴 1회 진입당 각각 1회씩 (while/for iteration 단위 = 슬라이더 증가 이벤트 단위와 1:1).
        /// 절대 DOTween OnUpdate/OnStepComplete 콜백 안에서 호출 금지 — 프레임당 다발 호출되어 무한 중첩 잔향이 됨.</summary>
        public void PlayWsGaugeUp()
        {
            PlaySFX(_sfxWsGaugeUp);
        }

        /// <summary>Common_Ding — PopupWinningStreakReward 카운터(TxtAmount/TxtAmountOutline) 증가 이벤트마다 1회 재생.
        /// 호출 위치: PopupWinningStreakReward.ApplyAmount 의 amount>from(증가) 분기 — _displayedAmount 갱신 직후 `if (from &lt; amount &amp;&amp; AudioManager.HasInstance) AudioManager.Instance.PlayWsRewardDing();` 단 한 줄.
        /// 1회 보장 근거: ApplyAmount 는 FXBadge 도착 콜백 / FXMultiple 도착 콜백 / gainedPoints 최종 보정에서 각각 1회씩 호출되므로 증가 N번 → SFX N번 (사용자 요구 '2번 증가→2번 재생' 자동 충족).
        /// 절대 DOTween.To 의 onUpdate 람다(PopupWinningStreakReward.cs:412 rolling-count tween) 안에서 호출 금지 — 매 프레임 호출되어 잔향 무한 중첩(과거 도메인 원칙 4 회귀).
        /// PlayOneShot 이라 호출당 1회 보장.</summary>
        public void PlayWsRewardDing()
        {
            PlaySFX(_sfxWsRewardDing);
        }

        /// <summary>Item_Get — (A) UILobby.PlayWsFireFlyAndPulse ImageIcon DOScale 펄스 시작 1회 + (B) UILobby.SetWinningIconMultiplierText changed-edge (PlayWsMultipleFxFire / PlayWsMultiplierTextPunch 와 동일 가드 라인) 1회 — 두 트리거 coexists, NOT supersede.
        /// (A) 1회 보장 근거: PlayWsFireFlyAndPulse 의 `if (target != null)` 블록 내부 baseScale 캡처 직후, 펄스 시퀀스 시작 직전 1회만 호출 — 펄스가 실행되는 케이스로 자연 게이팅(펄스 1회당 1회).
        /// (B) 1회 보장 근거: (1) `_wsLastMultiplierText != mult` changed-edge — 이전 값과 다른 값으로 갱신되는 순간만 발화, (2) `_wsHoldMultiplierTextDuringAnim` 가드로 연출 hold 중 메서드 진입 자체 차단, (3) Gauge/Outline 두 텍스트는 SetWinningIconMultiplierText 1회 호출 내에서 함께 갱신되므로 호출 1회 = SFX 1회.
        /// (A)와 (B)는 시간상 분리(fire-fly + pulse + slider + multiplier slide-in + select move + hold ≈ 1.5s+ 간격)되어 overlap/masking 없음.
        /// owner 출처: ProjectHub 태스크 6a3a4dbc 2026-06-24 익명 사용자 피드백 — 직전 2026-06-24 'supersede' 해석(B 만 사용)을 명시적으로 정정, BOTH 트리거 사용으로 확정.
        /// 절대 (A) DOTween OnUpdate / (B) SetWinningIconMultiplierText 외부(매 프레임 lambda / Update / DOTween OnUpdate) 에서 호출 금지(과거 도메인 원칙 4 회귀).</summary>
        public void PlayItemGet()
        {
            PlaySFX(_sfxItemGet);
        }

        /// <summary>Item_Fly — ItemFlyEffect.Play() 진입부 1회 재생.
        /// 호출 위치: ItemFlyEffect.Play 진입부(count>0 + UIManager.HasInstance + parent!=null 통과 직후, BeginSession 직전).
        /// PlayOneShot 이라 session 1회당 1회 보장 — 내부 RunFly/Fly 코루틴이 N개 spawn 하더라도 entry point 1회 호출이므로 항상 1회.
        /// 새 Play() 호출 = 새 session = 새 SFX 1회 (사용자 요구 '연출마다 1회 재생' 자동 충족, '이동 중 반복 재생되지 않도록' 자동 충족).
        /// owner 출처: ProjectHub 태스크 2026-06-24 익명 사용자 피드백 — 'FXItem 연출 시작 시 1회, 이동 중 비반복, 새 연출마다 1회'.
        /// 절대 RunFly/Fly 코루틴의 OnUpdate/매 프레임 lambda 안에서 호출 금지(과거 도메인 원칙 4: 잔향 무한 중첩 회귀).</summary>
        public void PlayItemFly()
        {
            PlaySFX(_sfxItemFly);
        }

        /// <summary>Zap_Appear — ItemZap 등장 시 1회 재생.
        /// 호출 위치: BoosterExecutor.CreateItemZap (Instantiate 성공 직후, zapObject != null 분기 안).
        /// PlayOneShot 이라 함수당 1회 보장 — Zap 부스터 1회 사용당 1회 재생.</summary>
        public void PlayZapAppear()
        {
            PlaySFX(_sfxZapAppear);
        }

        /// <summary>Zap_Line — FxZapLine 이 새 타겟으로 이동할 때마다 1회 재생.
        /// 호출 위치: BoosterExecutor.PlayColorRemoveSequenceBody (ConfigureZapLineFan 호출 직후, 타겟 루프 매 iteration).
        /// 추가 cooldown 없음 — stepDelay 자체가 최소 간격, PlayOneShot 이라 짧은 잔향만 중첩.</summary>
        public void PlayZapLine()
        {
            PlaySFX(_sfxZapLine);
        }

        /// <summary>Gold_Appear — FXGold(CoinFlyEffect) 등장 시 1회 재생.
        /// 호출 위치: CoinFlyEffect.Play 진입부 (count>0 + UIManager.HasInstance 통과 직후).
        /// 사용자 피드백 2026-06-23: FXGold 등장 1회. 기존 Common_Coin_Gain(coinearn) 대체.</summary>
        public void PlayGoldAppear()
        {
            PlaySFX(_sfxGoldAppear);
        }

        /// <summary>Gold_Get — FXGold 첫 코인 목적지 도착 시 연속 3회 재생(코루틴).
        /// 호출 위치: CoinFlyEffect.RunFly Fly 콜백, landed==1 시점 1회 호출.
        /// 사용자 피드백 2026-06-23: FXGold 목적지 도착 연속 3회. WaitForSecondsRealtime 사용 — Time.timeScale 영향 차단.</summary>
        public void PlayGoldGet()
        {
            if (_sfxGoldGet == null || !_sfxEnabled) return;
            StartCoroutine(PlayGoldGetSequence());
        }

        private System.Collections.IEnumerator PlayGoldGetSequence()
        {
            const float GAP = 0.08f;
            for (int i = 0; i < 3; i++)
            {
                PlaySFX(_sfxGoldGet);
                if (i < 2) yield return new UnityEngine.WaitForSecondsRealtime(GAP);
            }
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
            if (_resultIntroSfxLock) return;
            _popSource.PlayOneShot(_lastPopClip);
        }

        private void HandleBoardCleared(OnBoardCleared evt)
        {
            // congratuation (클리어 팡파레) + play BGM 정지
            StopBGM();
            // [2026-06-23 사용자 피드백] congratuation 트리거를 OnBoardCleared 에서 FinishLogo 진입(GameBootstrap.PlayFinishLogoSequence)으로 이동. 보드 클리어 시 BGM 정지만 수행.
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
            // coinuse — 코인 사용(소비)만. 획득(delta>0)은 CoinFlyEffect → AudioManager.PlayGoldAppear/PlayGoldGet 가 처리.
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
            if (_resultIntroSfxLock && clip != _sfxStageResultFirework) return;
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
