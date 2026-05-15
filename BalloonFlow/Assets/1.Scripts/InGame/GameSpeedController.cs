using UnityEngine;
using UnityEngine.InputSystem;

namespace BalloonFlow
{
    /// <summary>
    /// 유저 속도 조절.
    /// (A) 터치/마우스 홀드 중 일시 가속.
    /// (B) 우상단 x2 토글 버튼(지속 가속). SettingsManager가 아닌 세션 단위 유지.
    /// 두 배율은 곱해져서 RailManager.UserSpeedMultiplier로 반영.
    /// </summary>
    public class GameSpeedController : SceneSingleton<GameSpeedController>
    {
        #region Constants

        private const float HOLD_BOOST_MULTIPLIER   = 1.5f;
        private const float TOGGLE_BOOST_MULTIPLIER = 2f;
        private const float HOLD_ACTIVATE_DELAY     = 0.1f; // 탭을 홀드로 오인하지 않도록 짧은 지연

        #endregion

        #region Fields

        private bool _toggleOn;
        private bool _holdActive;
        private float _touchStartTime = -1f;

        #endregion

        #region Properties

        /// <summary>x2 토글 상태.</summary>
        public bool ToggleOn => _toggleOn;

        #endregion

        #region Public Methods

        /// <summary>우상단 x2 버튼 토글.</summary>
        public void SetToggleOn(bool on)
        {
            _toggleOn = on;
            ApplyMultiplier();
        }

        public void ToggleSpeedBoost() => SetToggleOn(!_toggleOn);

        #endregion

        #region Lifecycle

        private void OnEnable()
        {
            // 스테이지 in-place 전환(LoadLevel) 시 RailManager.ResetAll()이 UserSpeedMultiplier=1f 로 되돌리므로,
            // 살아있는 _toggleOn latch 를 새 RailManager 에 재푸시해야 X2 가 끊기지 않는다.
            EventBus.Subscribe<OnLevelLoaded>(HandleLevelLoaded);
        }

        private void Update()
        {
            // ROLLBACK_DISABLE_HOLD_SPEED_BOOST:
            // In-game long press is regular interaction space for holders/boosters. It must not
            // alter gameplay speed; only the explicit x2 toggle button may change speed now.
            if (_holdActive || _touchStartTime >= 0f)
            {
                _touchStartTime = -1f;
                _holdActive = false;
                ApplyMultiplier();
            }
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelLoaded>(HandleLevelLoaded);

            // 씬 전환 시 가속 잔재 모두 리셋 — 사용자 요구: 로비로 나오면 x2 토글도 원복.
            _holdActive = false;
            _touchStartTime = -1f;
            _toggleOn = false;
            if (RailManager.HasInstance)
                RailManager.Instance.UserSpeedMultiplier = 1f;
        }

        private void HandleLevelLoaded(OnLevelLoaded _)
        {
            // 새 RailManager 인스턴스 / ResetAll() 직후 현재 latch 를 재푸시.
            ApplyMultiplier();
        }

        #endregion

        #region Private Methods

        private static bool IsTouching()
        {
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
                return true;
            if (Mouse.current != null && Mouse.current.leftButton.isPressed)
                return true;
            return false;
        }

        private void ApplyMultiplier()
        {
            if (!RailManager.HasInstance) return;

            float mult = 1f;
            if (_holdActive) mult *= HOLD_BOOST_MULTIPLIER;
            if (_toggleOn)   mult *= TOGGLE_BOOST_MULTIPLIER;

            RailManager.Instance.UserSpeedMultiplier = mult;
        }

        #endregion
    }
}
