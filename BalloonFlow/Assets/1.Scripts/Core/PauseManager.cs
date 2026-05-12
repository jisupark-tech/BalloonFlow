using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 게임 일시 정지. Time.timeScale 0 / 복원. 중첩 popup 카운팅 — 마지막 popup 닫을 때 resume.
    /// 사용처: Setting / UseItem popup 등.
    /// 주의: DOTween 의 SetUpdate(true) 사용 tween 은 timeScale 영향 X (UI tween 동작 유지).
    /// </summary>
    public static class PauseManager
    {
        private static int _pauseCount;
        private static float _savedTimeScale = 1f;

        public static bool IsPaused => _pauseCount > 0;

        public static void Pause()
        {
            _pauseCount++;
            if (_pauseCount == 1)
            {
                _savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
            }
        }

        public static void Resume()
        {
            _pauseCount--;
            if (_pauseCount <= 0)
            {
                _pauseCount = 0;
                Time.timeScale = _savedTimeScale;
            }
        }

        /// <summary>강제 reset — scene 전환 또는 비정상 상태 복구용.</summary>
        public static void ForceReset()
        {
            _pauseCount = 0;
            _savedTimeScale = 1f;
            Time.timeScale = 1f;
        }
    }
}
