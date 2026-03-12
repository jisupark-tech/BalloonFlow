using System.Collections;
using UnityEngine;

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
    public class FeedbackController : Singleton<FeedbackController>
    {
        #region Constants

        private const int COMBO_MEDIUM_THRESHOLD = 3;
        private const int COMBO_HIGH_THRESHOLD = 5;
        private const float SLOW_MO_DURATION = 0.3f;
        private const float SLOW_MO_TIME_SCALE = 0.3f;
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

        [Header("Audio Pitch")]
        [SerializeField] private float _basePitch = 1.0f;
        [SerializeField] private float _pitchIncrementPerCombo = 0.05f;
        [SerializeField] private float _maxPitch = 2.0f;

        [Header("Camera Reference")]
        [SerializeField] private Transform _cameraTransform;

        [Header("Pool Sizes")]
        [SerializeField] private int _normalParticlePoolSize = 20;
        [SerializeField] private int _comboParticlePoolSize = 10;
        [SerializeField] private int _rainbowParticlePoolSize = 5;
        [SerializeField] private int _confettiParticlePoolSize = 5;
        [SerializeField] private int _starParticlePoolSize = 6;

        #endregion

        #region Fields

        private Vector3 _cameraOriginalPosition;
        private Coroutine _shakeCoroutine;
        private Coroutine _slowMoCoroutine;
        private bool _isShaking;

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
        }

        protected override void OnDestroy()
        {
            EventBus.Unsubscribe<OnBalloonPopped>(HandleBalloonPopped);
            EventBus.Unsubscribe<OnComboIncremented>(HandleComboIncremented);
            EventBus.Unsubscribe<OnBoardCleared>(HandleBoardCleared);
            EventBus.Unsubscribe<OnBoardFailed>(HandleBoardFailed);
            EventBus.Unsubscribe<OnScoreChanged>(HandleScoreChanged);
            EventBus.Unsubscribe<OnLevelCompleted>(HandleLevelCompleted);

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
        public void PlayPopFeedback(Vector3 position, int color, bool isSpecial)
        {
            if (isSpecial)
            {
                SpawnPooledParticle(POOL_PARTICLE_COMBO, position);
                PlayRandomClip(_comboPopClips, _basePitch);
                TriggerScreenShake(_shakeIntensitySmall, _shakeDurationSmall);
            }
            else
            {
                SpawnPooledParticle(POOL_PARTICLE_NORMAL, position);
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
                // Rainbow particles + slow-mo
                Vector3 centerPos = GetScreenCenter();
                SpawnPooledParticle(POOL_PARTICLE_RAINBOW, centerPos);
                TriggerScreenShake(_shakeIntensityLarge, _shakeDurationMedium);
                PlayRandomClip(_comboPopClips, pitch);
                TriggerSlowMo();
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
            PlayPopFeedback(evt.position, evt.color, false);
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

        private void SpawnPooledParticle(string poolKey, Vector3 position)
        {
            if (!ObjectPoolManager.HasInstance)
            {
                return;
            }

            GameObject particle = ObjectPoolManager.Instance.Get(poolKey);
            if (particle == null)
            {
                return;
            }

            particle.transform.position = position;

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

        private void TriggerScreenShake(float intensity, float duration)
        {
            if (_cameraTransform == null)
            {
                return;
            }

            if (_shakeCoroutine != null)
            {
                StopCoroutine(_shakeCoroutine);
                _cameraTransform.localPosition = _cameraOriginalPosition;
            }

            _shakeCoroutine = StartCoroutine(ScreenShakeCoroutine(intensity, duration));
        }

        private void TriggerSlowMo()
        {
            if (_slowMoCoroutine != null)
            {
                StopCoroutine(_slowMoCoroutine);
                Time.timeScale = 1f;
                Time.fixedDeltaTime = 0.02f;
            }

            _slowMoCoroutine = StartCoroutine(SlowMoCoroutine());
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

        private IEnumerator ScreenShakeCoroutine(float intensity, float duration)
        {
            _isShaking = true;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                float x = Random.Range(-1f, 1f) * intensity;
                float y = Random.Range(-1f, 1f) * intensity;
                _cameraTransform.localPosition = _cameraOriginalPosition + new Vector3(x, y, 0f);

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            _cameraTransform.localPosition = _cameraOriginalPosition;
            _isShaking = false;
            _shakeCoroutine = null;
        }

        private IEnumerator SlowMoCoroutine()
        {
            Time.timeScale = SLOW_MO_TIME_SCALE;
            Time.fixedDeltaTime = 0.02f * SLOW_MO_TIME_SCALE;

            // Use unscaled time so slow-mo duration is real-world seconds
            float elapsed = 0f;
            while (elapsed < SLOW_MO_DURATION)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
            _slowMoCoroutine = null;
        }

        private IEnumerator ReturnParticleAfterPlay(string poolKey, GameObject particle, ParticleSystem ps)
        {
            // Wait until particle system stops playing
            yield return new WaitWhile(() => ps != null && ps.isPlaying);

            if (particle != null && ObjectPoolManager.HasInstance)
            {
                ObjectPoolManager.Instance.Return(poolKey, particle);
            }
        }

        private IEnumerator ReturnAfterDelay(string poolKey, GameObject obj, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (obj != null && ObjectPoolManager.HasInstance)
            {
                ObjectPoolManager.Instance.Return(poolKey, obj);
            }
        }

        private IEnumerator DelayedStarPopIn(Vector3 position, float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            SpawnPooledParticle(POOL_PARTICLE_STAR, position);

            if (_sfxSource != null && _starEarnedClip != null)
            {
                _sfxSource.pitch = _basePitch;
                _sfxSource.PlayOneShot(_starEarnedClip);
            }
        }

        #endregion
    }
}
