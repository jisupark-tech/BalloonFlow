using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Android 진동(haptic) 호출 wrapper.
    /// 참조: github.com/tekken5953/VibratorExam (MakeVibrator.kt)
    ///   - API 31+ (Android 12, S): Context.VIBRATOR_MANAGER_SERVICE → VibratorManager → defaultVibrator
    ///   - 그 이전: Context.VIBRATOR_SERVICE → Vibrator (deprecated since API 31)
    ///   - VibrationEffect.createOneShot(ms, DEFAULT_AMPLITUDE) 로 발진
    ///
    /// 호출은 정적 API: <see cref="Vibrate(long)"/>. 첫 호출 시 vibrator 인스턴스를 캐시해
    /// 매 호출마다 system service 조회를 반복하지 않음.
    /// 에디터/iOS는 no-op (iOS는 <c>Handheld.Vibrate()</c> fallback).
    /// </summary>
    public static class VibrationManager
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private const int SDK_INT_S = 31; // Android 12
        private const int DEFAULT_AMPLITUDE = -1;
        private const int MAX_AMPLITUDE     = 255; // VibrationEffect 1~255 (255 = 최대)

        private static AndroidJavaObject _vibrator;
        private static AndroidJavaClass _vibrationEffectClass;
        private static bool _initialized;
        private static bool _initFailed;
        private static bool _hasAmplitudeControl;

        private static void InitIfNeeded()
        {
            if (_initialized || _initFailed) return;

            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    int sdkInt = version.GetStatic<int>("SDK_INT");

                    if (sdkInt >= SDK_INT_S)
                    {
                        // API 31+: VibratorManager.defaultVibrator
                        var vibratorManager = activity.Call<AndroidJavaObject>(
                            "getSystemService", "vibrator_manager"); // Context.VIBRATOR_MANAGER_SERVICE
                        if (vibratorManager != null)
                        {
                            _vibrator = vibratorManager.Call<AndroidJavaObject>("getDefaultVibrator");
                        }
                    }

                    if (_vibrator == null)
                    {
                        // Fallback (API 26~30 + 누락 시): VIBRATOR_SERVICE
                        _vibrator = activity.Call<AndroidJavaObject>(
                            "getSystemService", "vibrator"); // Context.VIBRATOR_SERVICE
                    }
                }

                _vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect");
                _initialized = _vibrator != null && _vibrationEffectClass != null;
                if (!_initialized) _initFailed = true;

                // amplitude control 지원 여부 (지원 안 되면 항상 max 강도로 발진)
                if (_initialized)
                {
                    try { _hasAmplitudeControl = _vibrator.Call<bool>("hasAmplitudeControl"); }
                    catch { _hasAmplitudeControl = false; }
                }
            }
            catch (System.Exception e)
            {
                _initFailed = true;
                Debug.LogWarning($"[VibrationManager] Init failed: {e.Message}");
            }
        }
#endif

        /// <summary>
        /// 한 번 진동. milliseconds 만큼 발진. amplitude 1~255 (기본 255 = 최대).
        /// SettingsManager.HapticOn 토글이 OFF 면 무시.
        /// </summary>
        /// <param name="milliseconds">진동 지속 시간 (ms). 양수.</param>
        /// <param name="amplitude">진동 강도 1~255. 255 = 최대 (디바이스 지원 시).</param>
        public static void Vibrate(long milliseconds, int amplitude = 255)
        {
            if (milliseconds <= 0) return;
            if (SettingsManager.HasInstance && !SettingsManager.Instance.HapticOn) return;

            // Slider multiplier 적용 (UISetting의 강도/지속시간 슬라이더 반영)
            if (SettingsManager.HasInstance)
            {
                var sm = SettingsManager.Instance;
                milliseconds = (long)(milliseconds * sm.HapticDuration);
                amplitude    = Mathf.RoundToInt(amplitude * sm.HapticIntensity);
                if (milliseconds <= 0 || amplitude <= 0) return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            InitIfNeeded();
            if (!_initialized || _vibrator == null || _vibrationEffectClass == null) return;

            try
            {
                int amp = _hasAmplitudeControl
                    ? Mathf.Clamp(amplitude, 1, MAX_AMPLITUDE)
                    : DEFAULT_AMPLITUDE;
                using (var effect = _vibrationEffectClass.CallStatic<AndroidJavaObject>(
                    "createOneShot", milliseconds, amp))
                {
                    _vibrator.Call("vibrate", effect);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[VibrationManager] Vibrate failed: {e.Message}");
            }
#elif UNITY_IOS && !UNITY_EDITOR
            // iOS는 세분화된 ms 제어 불가 — 표준 햅틱으로 fallback
            Handheld.Vibrate();
#else
            // Editor / 기타 플랫폼: no-op
#endif
        }

        /// <summary>편의: light tap (40ms, amp 200) — 체감 가능하도록 강화.</summary>
        public static void Light() => Vibrate(40L, 200);

        /// <summary>편의: medium tap (90ms, amp 230).</summary>
        public static void Medium() => Vibrate(90L, 230);

        /// <summary>편의: heavy tap (180ms, amp 255).</summary>
        public static void Heavy() => Vibrate(180L, 255);
    }
}
