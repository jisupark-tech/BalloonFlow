using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// Manages asymmetric feedback — subtle (담백) for normal actions,
    /// explosive (과하게) for special moments like combos and clears.
    /// Triggers particle effects, screen shake, slow-mo, and SFX through
    /// pooled objects and AudioSource references.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Controller | Phase: 3
    /// DB Reference: No DB match found — generated from L3 YAML logicFlow (ux_pages_tutorial)
    /// Requires: ScoreManager, PopProcessor (events), BoardStateManager (events)
    /// </remarks>
    public class FeedbackController : SceneSingleton<FeedbackController>
    {
        #region Constants

        private const int COMBO_MEDIUM_THRESHOLD = 3;
        private const int COMBO_HIGH_THRESHOLD = 5;
        private const string POOL_PARTICLE_NORMAL = "ParticleNormal";
        private const string POOL_PARTICLE_COMBO = "ParticleCombo";
        private const string POOL_PARTICLE_RAINBOW = "ParticleRainbow";
        private const string POOL_PARTICLE_CONFETTI = "ParticleConfetti";
        private const string POOL_PARTICLE_STAR = "ParticleStar";

        #endregion

        #region Serialized Fields

        [Header("Particle Prefabs")]
        [SerializeField] private GameObject _normalPopParticlePrefab;
        [SerializeField] private GameObject _comboParticlePrefab;
        [SerializeField] private GameObject _rainbowParticlePrefab;
        [SerializeField] private GameObject _confettiParticlePrefab;
        [SerializeField] private GameObject _starPopParticlePrefab;

        [Header("Screen Shake")]
        [SerializeField] private float _shakeIntensitySmall = 0.05f;
        [SerializeField] private float _shakeIntensityMedium = 0.12f;
        [SerializeField] private float _shakeIntensityLarge = 0.2f;
        [SerializeField] private float _shakeDurationSmall = 0.1f;
        [SerializeField] private float _shakeDurationMedium = 0.2f;
        [SerializeField] private float _shakeDurationLarge = 0.35f;

        [Header("Scale Punch")]
        [SerializeField] private float _normalPunchScale = 1.15f;
        [SerializeField] private float _comboPunchScale = 1.3f;
        [SerializeField] private float _punchDuration = 0.15f;

        [Header("Audio")]
        [SerializeField] private AudioSource _sfxSource;
        [SerializeField] private AudioClip[] _normalPopClips;
        [SerializeField] private AudioClip[] _comboPopClips;
        [SerializeField] private AudioClip _clearClip;
        [SerializeField] private AudioClip _failClip;
        [SerializeField] private AudioClip _starEarnedClip;
        [SerializeField] private AudioClip _holderWarningClip;
        [SerializeField] private AudioClip _holderDangerClip;
        [SerializeField] private AudioClip _boosterActivateClip;
        [SerializeField] private AudioClip _coinEarnedClip;
        [SerializeField] private AudioClip _gaugeWarningClip;
        [SerializeField] private AudioClip _gaugeCriticalClip;

        [Header("Audio Pitch")]
        [SerializeField] private float _basePitch = 1.0f;
        [SerializeField] private float _pitchIncrementPerCombo = 0.05f;
        [SerializeField] private float _maxPitch = 2.0f;

        [Header("Camera Reference")]
        [SerializeField] private Transform _cameraTransform;

        [Header("Haptic (진동)")]
        [Tooltip("햅틱 활성화 여부")]
        [SerializeField] private bool _hapticEnabled = true;

        [Header("Pool Sizes")]
        [SerializeField] private int _normalParticlePoolSize = 20;
        [SerializeField] private int _comboParticlePoolSize = 10;
        [SerializeField] private int _rainbowParticlePoolSize = 5;
        [SerializeField] private int _confettiParticlePoolSize = 5;
        [SerializeField] private int _starParticlePoolSize = 6;

        #endregion

        #region Fields

        private Vector3 _cameraOriginalPosition;
        private Tweener _shakeTweener;
        private bool _isShaking;
        private readonly Dictionary<GameObject, Vector3> _particleBaseScaleByObject = new Dictionary<GameObject, Vector3>();

        // [2026-06-23 사용자 추가지시 task #377 후속] FinishLogo 표시 구간 SE 화이트리스트 락 검사.
        // 락 활성 시 PopFeedback/Clear/Fail/Holder/Gauge/Booster/Coin/Star/RandomClip 등 모든 SFX 차단.
        private bool ResultIntroLocked() => AudioManager.HasInstance && AudioManager.Instance.IsResultIntroSfxLocked;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            RegisterPools();
            CacheCameraPosition();

            EventBus.Subscribe<OnBalloonPopped>(HandleBalloonPopped);
            EventBus.Subscribe<OnComboIncremented>(HandleComboIncremented);
            EventBus.Subscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Subscribe<OnBoardFailed>(HandleBoardFailed);
            EventBus.Subscribe<OnScoreChanged>(HandleScoreChanged);
            EventBus.Subscribe<OnLevelCompleted>(HandleLevelCompleted);
            EventBus.Subscribe<OnHolderWarning>(HandleHolderWarning);
            EventBus.Subscribe<OnBoosterUsed>(HandleBoosterUsed);
            EventBus.Subscribe<OnGaugeStageChanged>(HandleGaugeStageChanged);
            EventBus.Subscribe<OnCoinEarned>(HandleCoinEarned);
        }

        protected override void OnDestroy()
        {
            EventBus.Unsubscribe<OnBalloonPopped>(HandleBalloonPopped);
            EventBus.Unsubscribe<OnComboIncremented>(HandleComboIncremented);
            EventBus.Unsubscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Unsubscribe<OnBoardFailed>(HandleBoardFailed);
            EventBus.Unsubscribe<OnScoreChanged>(HandleScoreChanged);
            EventBus.Unsubscribe<OnLevelCompleted>(HandleLevelCompleted);
            EventBus.Unsubscribe<OnHolderWarning>(HandleHolderWarning);
            EventBus.Unsubscribe<OnBoosterUsed>(HandleBoosterUsed);
            EventBus.Unsubscribe<OnGaugeStageChanged>(HandleGaugeStageChanged);
            EventBus.Unsubscribe<OnCoinEarned>(HandleCoinEarned);

            base.OnDestroy();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Plays pop feedback at the given position. Normal pops are subtle;
        /// special pops use larger particles and stronger punch.
        /// </summary>
        /// <param name="position">World position of the pop.</param>
        /// <param name="color">Color index for tinting (0-based).</param>
        /// <param name="isSpecial">True for combo/special pops with enhanced feedback.</param>
        public void PlayPopFeedback(Vector3 position, int color, bool isSpecial, float scaleMultiplier = 1f)
        {
            if (isSpecial)
            {
                SpawnPooledParticle(POOL_PARTICLE_COMBO, position, scaleMultiplier);
                PlayRandomClip(_comboPopClips, _basePitch);
                TriggerScreenShake(_shakeIntensitySmall, _shakeDurationSmall);
            }
            else
            {
                SpawnPooledParticle(POOL_PARTICLE_NORMAL, position, scaleMultiplier);
                PlayRandomClip(_normalPopClips, _basePitch);
            }
        }

        /// <summary>
        /// Plays board-clear celebration feedback. Confetti, big shake, and clear SFX.
        /// Star count determines celebration intensity.
        /// </summary>
        /// <param name="starCount">Number of stars earned (1-3).</param>
        public void PlayClearFeedback(int starCount)
        {
            // Confetti burst at screen center
            Vector3 centerPos = Vector3.zero;
            if (_cameraTransform != null)
            {
                centerPos = _cameraTransform.position + _cameraTransform.forward * 5f;
            }

            SpawnPooledParticle(POOL_PARTICLE_CONFETTI, centerPos);
            TriggerScreenShake(_shakeIntensityLarge, _shakeDurationLarge);

            if (_sfxSource != null && _clearClip != null)
            {
                _sfxSource.pitch = _basePitch;
                if (ResultIntroLocked()) return;
                _sfxSource.PlayOneShot(_clearClip);
            }

            // Spawn star pop-in particles for each earned star
            for (int i = 0; i < starCount && i < 3; i++)
            {
                float xOffset = (i - 1) * 1.5f;
                Vector3 starPos = centerPos + new Vector3(xOffset, 1f, 0f);
                StartCoroutine(DelayedStarPopIn(starPos, i * 0.3f));
            }
        }

        /// <summary>
        /// Plays subtle fail feedback. Gentle shake, muted SFX — no harsh punishment feel.
        /// </summary>
        public void PlayFailFeedback()
        {
            TriggerScreenShake(_shakeIntensitySmall, _shakeDurationSmall);

            if (_sfxSource != null && _failClip != null)
            {
                _sfxSource.pitch = _basePitch * 0.8f;
                if (ResultIntroLocked()) return;
                _sfxSource.PlayOneShot(_failClip);
            }
        }

        /// <summary>
        /// Plays combo feedback with escalating intensity.
        /// 3+: screen shake + bigger particles + pitch-up SFX.
        /// 5+: slow-mo 0.3s + rainbow particles.
        /// </summary>
        /// <param name="comboCount">Current combo count.</param>
        public void PlayComboFeedback(int comboCount)
        {
            if (comboCount < COMBO_MEDIUM_THRESHOLD)
            {
                return;
            }

            float pitch = Mathf.Min(_basePitch + comboCount * _pitchIncrementPerCombo, _maxPitch);

            if (comboCount >= COMBO_HIGH_THRESHOLD)
            {
                // Rainbow particles (no slow-mo — causes perceived game slowdown)
                Vector3 centerPos = GetScreenCenter();
                SpawnPooledParticle(POOL_PARTICLE_RAINBOW, centerPos);
                TriggerScreenShake(_shakeIntensityLarge, _shakeDurationMedium);
                PlayRandomClip(_comboPopClips, pitch);
            }
            else
            {
                // Medium combo: bigger particles + shake
                Vector3 centerPos = GetScreenCenter();
                SpawnPooledParticle(POOL_PARTICLE_COMBO, centerPos);
                TriggerScreenShake(_shakeIntensityMedium, _shakeDurationMedium);
                PlayRandomClip(_comboPopClips, pitch);
            }
        }

        /// <summary>
        /// Plays streak feedback for consecutive level clears.
        /// Intensity scales with streak count.
        /// </summary>
        /// <param name="streakCount">Current streak count.</param>
        public void PlayStreakFeedback(int streakCount)
        {
            if (streakCount < 2)
            {
                return;
            }

            Vector3 centerPos = GetScreenCenter();
            float pitch = Mathf.Min(_basePitch + streakCount * 0.1f, _maxPitch);

            SpawnPooledParticle(POOL_PARTICLE_COMBO, centerPos);
            PlayRandomClip(_comboPopClips, pitch);

            if (streakCount >= 5)
            {
                SpawnPooledParticle(POOL_PARTICLE_RAINBOW, centerPos);
                TriggerScreenShake(_shakeIntensityMedium, _shakeDurationSmall);
            }
        }

        #endregion

        #region Private Methods — Event Handlers

        private void HandleBalloonPopped(OnBalloonPopped evt)
        {
            // ROLLBACK_POP_FEEDBACK_COOLDOWN_ZAP_20260618: burst(Zap 대량팝) 시 햅틱(JNI 진동+AndroidJavaObject GC)/
            //   보조 SFX(PlayOneShot)/보조 파티클이 쿨다운 없이 프레임당 3~4회 실행돼 프레임 드랍. 0.05s 게이트로 burst 시
            //   프레임당 ~1회로 coalesce(AudioManager 50ms 팝 쿨다운과 동일 패턴). 단일/저속 팝(>50ms 간격)은 영향 없음.
            //   ★실제 팝 비주얼(PopEffectPool CircleParticle)은 ReturnBalloonObject 에서 매 팝 그대로 재생 → 풍선은 항상 보이게 팝됨.
            //   ★팝/클리어/실패 판정은 BoardStateManager/DartManager(별도, O(1))라 이 게이트와 무관. 롤백: 아래 2줄 제거.
            if (Time.unscaledTime - _lastPopFeedbackTime < POP_FEEDBACK_COOLDOWN) return;
            _lastPopFeedbackTime = Time.unscaledTime;

            float scaleMultiplier = evt.effectScaleMultiplier > 0f ? evt.effectScaleMultiplier : 1f;
            PlayPopFeedback(evt.position, evt.color, false, scaleMultiplier);
            // [2026-05-13] 골드 연출 동일 햅틱 (180ms, amp=38) — 이전: HapticLight() (40ms, amp=200).
            HapticDefault();
        }

        private void HandleComboIncremented(OnComboIncremented evt)
        {
            PlayComboFeedback(evt.comboCount);
        }

        private void HandleBoardCleared(OnBoardCleared evt)
        {
            PlayClearFeedback(evt.starCount);
        }

        private void HandleBoardFailed(OnBoardFailed evt)
        {
            PlayFailFeedback();
        }

        private void HandleScoreChanged(OnScoreChanged evt)
        {
            // Score change is handled visually by UI;
            // feedback controller only reacts to discrete events
        }

        private void HandleLevelCompleted(OnLevelCompleted evt)
        {
            // Level completed triggers clear feedback via OnBoardCleared;
            // additional celebration can layer here if needed
        }

        /// <summary>
        /// P0 feedback: Holder warning (4/5) and danger (5/5).
        /// Design ref: 피드백디렉션 P0 #2 (warning beep), #3 (danger tense loop).
        /// </summary>
        private void HandleHolderWarning(OnHolderWarning evt)
        {
            if (evt.isDanger)
            {
                TriggerScreenShake(_shakeIntensityMedium, _shakeDurationSmall);
                if (_sfxSource != null && _holderDangerClip != null)
                {
                    _sfxSource.pitch = _basePitch;
                    if (ResultIntroLocked()) return;
                    _sfxSource.PlayOneShot(_holderDangerClip);
                }
            }
            else
            {
                TriggerScreenShake(_shakeIntensitySmall, _shakeDurationSmall);
                if (_sfxSource != null && _holderWarningClip != null)
                {
                    _sfxSource.pitch = _basePitch * 1.2f;
                    if (ResultIntroLocked()) return;
                    _sfxSource.PlayOneShot(_holderWarningClip);
                }
            }
        }

        /// <summary>
        /// P0 #2/#3: 6-stage gauge feedback. WARNING = red blink + heartbeat, CRITICAL = full red + vibrate.
        /// Design ref: 피드백디렉션 (2026-03-17) P0 #2 게이지 90%+, P0 #3 게이지 허용량-1
        /// </summary>
        private void HandleGaugeStageChanged(OnGaugeStageChanged evt)
        {
            GaugeStage stage = (GaugeStage)evt.currentStage;

            switch (stage)
            {
                case GaugeStage.Warning:
                    // P0 #2: 게이지 빨간색 깜빡임 + 경고 비프음
                    TriggerScreenShake(_shakeIntensitySmall, _shakeDurationSmall);
                    if (_sfxSource != null && _gaugeWarningClip != null)
                    {
                        _sfxSource.pitch = _basePitch;
                        if (ResultIntroLocked()) return;
                        _sfxSource.PlayOneShot(_gaugeWarningClip);
                    }
                    break;

                case GaugeStage.Critical:
                    // P0 #3: 화면 테두리 경고 + 진동 + 긴장 사운드
                    TriggerScreenShake(_shakeIntensityMedium, _shakeDurationMedium);
                    if (_sfxSource != null && _gaugeCriticalClip != null)
                    {
                        _sfxSource.pitch = _basePitch;
                        if (ResultIntroLocked()) return;
                        _sfxSource.PlayOneShot(_gaugeCriticalClip);
                    }
                    break;

                case GaugeStage.Fail:
                    // Fail is handled by HandleBoardFailed
                    break;
            }
        }

        /// <summary>
        /// P0 #7: 부스터 사용 시 활성화 이펙트 + 효과음.
        /// Design ref: 피드백디렉션 P0 #7 부스터 사용 (공통)
        /// </summary>
        private void HandleBoosterUsed(OnBoosterUsed evt)
        {
            Vector3 pos = GetScreenCenter();
            SpawnPooledParticle(POOL_PARTICLE_COMBO, pos);
            TriggerScreenShake(_shakeIntensitySmall, _shakeDurationSmall);

            if (_sfxSource != null && _boosterActivateClip != null)
            {
                _sfxSource.pitch = _basePitch;
                if (ResultIntroLocked()) return;
                _sfxSource.PlayOneShot(_boosterActivateClip);
            }
        }

        /// <summary>
        /// P0 #8: 코인 획득 시 코인 사운드.
        /// Design ref: 피드백디렉션 P0 #8 코인 획득
        /// </summary>
        private void HandleCoinEarned(OnCoinEarned evt)
        {
            if (_sfxSource != null && _coinEarnedClip != null)
            {
                _sfxSource.pitch = _basePitch + 0.1f;
                if (ResultIntroLocked()) return;
                _sfxSource.PlayOneShot(_coinEarnedClip);
            }
        }

        #endregion

        #region Private Methods — Effects

        private void RegisterPools()
        {
            if (!ObjectPoolManager.HasInstance)
            {
                return;
            }

            RegisterPoolIfValid(POOL_PARTICLE_NORMAL, _normalPopParticlePrefab, _normalParticlePoolSize);
            RegisterPoolIfValid(POOL_PARTICLE_COMBO, _comboParticlePrefab, _comboParticlePoolSize);
            RegisterPoolIfValid(POOL_PARTICLE_RAINBOW, _rainbowParticlePrefab, _rainbowParticlePoolSize);
            RegisterPoolIfValid(POOL_PARTICLE_CONFETTI, _confettiParticlePrefab, _confettiParticlePoolSize);
            RegisterPoolIfValid(POOL_PARTICLE_STAR, _starPopParticlePrefab, _starParticlePoolSize);
        }

        private void RegisterPoolIfValid(string poolKey, GameObject prefab, int size)
        {
            if (prefab != null)
            {
                ObjectPoolManager.Instance.CreatePool(poolKey, prefab, size);
            }
        }

        private void SpawnPooledParticle(string poolKey, Vector3 position, float scaleMultiplier = 1f)
        {
            if (!ObjectPoolManager.HasInstance) return;

            // 풀이 등록 안 됐으면 (프리팹 없음) 스킵 — 크래시 방지
            if (!ObjectPoolManager.Instance.HasPool(poolKey)) return;

            GameObject particle = ObjectPoolManager.Instance.Get(poolKey);
            if (particle == null)
            {
                return;
            }

            particle.transform.position = position;
            if (!_particleBaseScaleByObject.TryGetValue(particle, out Vector3 baseScale))
            {
                baseScale = particle.transform.localScale;
                _particleBaseScaleByObject[particle] = baseScale;
            }
            particle.transform.localScale = baseScale * Mathf.Max(0.01f, scaleMultiplier);

            ParticleSystem ps = particle.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                ps.Play();
                StartCoroutine(ReturnParticleAfterPlay(poolKey, particle, ps));
            }
            else
            {
                // If no particle system, return after a default duration
                StartCoroutine(ReturnAfterDelay(poolKey, particle, 1f));
            }
        }

        private void CacheCameraPosition()
        {
            if (_cameraTransform != null)
            {
                _cameraOriginalPosition = _cameraTransform.localPosition;
            }
            else
            {
                Camera mainCam = Camera.main;
                if (mainCam != null)
                {
                    _cameraTransform = mainCam.transform;
                    _cameraOriginalPosition = _cameraTransform.localPosition;
                }
            }
        }

        private float _lastShakeTime;
        private const float SHAKE_COOLDOWN = 0.1f;
        // ROLLBACK_POP_FEEDBACK_COOLDOWN_ZAP_20260618: 팝당 보조 파티클+SFX+햅틱(JNI 진동) 쿨다운. Zap 대량팝
        //   (프레임당 3~4팝) 시 JNI 6~12회+오디오 보이스스틸로 프레임 드랍 → burst 시 프레임당 ~1회로 coalesce.
        private float _lastPopFeedbackTime = -1f;
        private const float POP_FEEDBACK_COOLDOWN = 0.05f;

        private void TriggerScreenShake(float intensity, float duration)
        {
            if (_cameraTransform == null) return;

            // 쿨다운: 연속 pop 시 셰이크 Tween 스팸 방지
            if (Time.unscaledTime - _lastShakeTime < SHAKE_COOLDOWN) return;
            _lastShakeTime = Time.unscaledTime;

            if (_shakeTweener != null && _shakeTweener.IsActive())
            {
                _shakeTweener.Kill();
                _cameraTransform.localPosition = _cameraOriginalPosition;
            }

            _isShaking = true;
            _shakeTweener = _cameraTransform.DOShakePosition(duration, intensity, 10, 90f, false, true, ShakeRandomnessMode.Harmonic)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    _cameraTransform.localPosition = _cameraOriginalPosition;
                    _isShaking = false;
                });
        }

        private void PlayRandomClip(AudioClip[] clips, float pitch)
        {
            if (_sfxSource == null || clips == null || clips.Length == 0)
            {
                return;
            }

            // Filter out null clips
            AudioClip clip = null;
            int attempts = 0;
            while (clip == null && attempts < clips.Length)
            {
                int index = Random.Range(0, clips.Length);
                clip = clips[index];
                attempts++;
            }

            if (clip == null)
            {
                return;
            }

            _sfxSource.pitch = pitch;
            if (ResultIntroLocked()) return;
            _sfxSource.PlayOneShot(clip);
        }

        private Vector3 GetScreenCenter()
        {
            if (_cameraTransform != null)
            {
                return _cameraTransform.position + _cameraTransform.forward * 5f;
            }
            return Vector3.zero;
        }

        #endregion

        #region Private Methods — Coroutines

        private IEnumerator ReturnParticleAfterPlay(string poolKey, GameObject particle, ParticleSystem ps)
        {
            // Wait until particle system stops playing
            yield return new WaitWhile(() => ps != null && ps.isPlaying);

            if (particle != null && ObjectPoolManager.HasInstance)
            {
                ObjectPoolManager.Instance.Return(poolKey, particle);
            }
        }

        /// <summary>WaitForSeconds 캐시 — 동일 delay 값 재사용으로 GC 방지</summary>
        private static readonly Dictionary<float, WaitForSeconds> _waitCache = new Dictionary<float, WaitForSeconds>();
        private const int WAIT_CACHE_MAX = 32;

        private static WaitForSeconds GetWait(float seconds)
        {
            // float 정밀도 문제 방지: 소수점 2자리로 반올림
            seconds = Mathf.Round(seconds * 100f) / 100f;

            if (!_waitCache.TryGetValue(seconds, out WaitForSeconds w))
            {
                if (_waitCache.Count >= WAIT_CACHE_MAX)
                    _waitCache.Clear(); // 과도한 성장 방지
                w = new WaitForSeconds(seconds);
                _waitCache[seconds] = w;
            }
            return w;
        }

        private IEnumerator ReturnAfterDelay(string poolKey, GameObject obj, float delay)
        {
            yield return GetWait(delay);

            if (obj != null && ObjectPoolManager.HasInstance)
            {
                ObjectPoolManager.Instance.Return(poolKey, obj);
            }
        }

        private IEnumerator DelayedStarPopIn(Vector3 position, float delay)
        {
            if (delay > 0f)
            {
                yield return GetWait(delay);
            }

            SpawnPooledParticle(POOL_PARTICLE_STAR, position);

            if (_sfxSource != null && _starEarnedClip != null)
            {
                _sfxSource.pitch = _basePitch;
                // ROLLBACK_FEEDBACK_ITERATOR_RETURN_FIX_20260623: 코루틴에선 return; 불가(CS1622) → yield break.
                //   hermes "사운드 추가 #378"(565ba578)이 return 으로 넣어 origin 빌드가 깨져 있었음.
                if (ResultIntroLocked()) yield break;
                _sfxSource.PlayOneShot(_starEarnedClip);
            }
        }

        #endregion

        #region Haptic

        /// <summary>외부에서 햅틱 ON/OFF 설정.</summary>
        public void SetHapticEnabled(bool enabled)
        {
            _hapticEnabled = enabled;
        }

        /// <summary>Light 진동 (10ms) — 풍선 터짐, 다트 배치, UI 터치.</summary>
        public void HapticLight()
        {
            if (!_hapticEnabled) return;
            VibrationManager.Light();
        }

        /// <summary>Medium 진동 (25ms) — 콤보, 가벼운 강조.</summary>
        public void HapticMedium()
        {
            if (!_hapticEnabled) return;
            VibrationManager.Medium();
        }

        /// <summary>Heavy 진동 (40ms) — 보관함 비활성 터치, 경고.</summary>
        public void HapticHeavy()
        {
            if (!_hapticEnabled) return;
            VibrationManager.Heavy();
        }

        /// <summary>[2026-05-13] Default 진동 — 골드 흡수 연출과 동일 (180ms, amp=38). 풍선 pop 에 사용.</summary>
        public void HapticDefault()
        {
            if (!_hapticEnabled) return;
            VibrationManager.VibrateDefault();
        }

        /// <summary>임의 길이 진동 (ms).</summary>
        public void HapticVibrate(long milliseconds)
        {
            if (!_hapticEnabled) return;
            VibrationManager.Vibrate(milliseconds);
        }

        #endregion
    }
}
