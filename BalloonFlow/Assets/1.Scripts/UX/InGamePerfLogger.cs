using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BalloonFlow
{
    /// <summary>
    /// 인게임 부하 진단용 logcat 자동 dumper.
    /// Development Build / Editor 에서만 활성. Release 영향 0.
    ///
    /// 동작:
    ///  1) 매 frame time 측정. 33ms (30FPS) 초과 시 즉시 logcat warning.
    ///  2) 매 1초 누적 통계 (avg/max/spike count) logcat info.
    ///  3) 외부 매니저가 RecordSection(label, ms) 호출 시 1초 통계에 합산.
    ///
    /// 사용:
    ///  - 자동 spawn (RuntimeInitializeOnLoadMethod, AfterSceneLoad)
    ///  - 매니저 측에서 RecordSection 호출 (선택):
    ///      var sw = InGamePerfLogger.StartSection();
    ///      // ... 작업
    ///      InGamePerfLogger.EndSection(sw, "BoardStateManager.Update");
    ///
    /// logcat filter: adb logcat -s Unity Perf
    /// </summary>
    public class InGamePerfLogger : MonoBehaviour
    {
        private const float SPIKE_THRESHOLD_MS = 33f;     // 30FPS 한계
        private const float REPORT_INTERVAL_S  = 1f;       // 1초마다 누적 통계

        // ROLLBACK_PERF_LOGGER_NO_SPIKE_WARNINGS:
        // Restore true / BALLOONFLOW_PERF_SPIKE_WARNINGS if per-frame spike callstacks are needed.
        // In Editor those warnings can become the measured frame spike.
#if BALLOONFLOW_PERF_SPIKE_WARNINGS
        private static readonly bool LOG_EACH_FRAME_SPIKE = true;
#else
        private static readonly bool LOG_EACH_FRAME_SPIKE = false;
#endif

        private float _accumTime;
        private int   _accumFrames;
        private float _accumMaxMs;
        private int   _accumSpikes;
        private float _reportTimer;

        // 매니저별 누적 ms (1초 윈도우)
        private static readonly System.Collections.Generic.Dictionary<string, float> _sectionTotalMs = new();
        private static readonly System.Collections.Generic.Dictionary<string, int>   _sectionCount   = new();
        private static readonly System.Collections.Generic.Dictionary<string, float> _sectionMaxMs   = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
#if BALLOONFLOW_ENABLE_PERF_LOGGER
            // ROLLBACK_PERF_LOGGER_SYMBOL_ONLY:
            // Restore the UNITY_EDITOR / DEVELOPMENT_BUILD gate if the logger should auto-run there.
            const string GO_NAME = "[InGamePerfLogger]";
            if (GameObject.Find(GO_NAME) != null) return;
            var go = new GameObject(GO_NAME);
            DontDestroyOnLoad(go);
            go.AddComponent<InGamePerfLogger>();
#endif
        }

        private void Awake()
        {
#if BALLOONFLOW_ENABLE_PERF_LOGGER && !BALLOONFLOW_PERF_KEEP_LOG_STACKTRACE
            // ROLLBACK_PERF_LOGGER_DISABLE_LOG_STACKTRACE:
            // Define BALLOONFLOW_PERF_KEEP_LOG_STACKTRACE if perf log callstacks are needed.
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
#endif
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float ms = dt * 1000f;

            _accumTime   += dt;
            _accumFrames += 1;
            if (ms > _accumMaxMs) _accumMaxMs = ms;
            if (ms > SPIKE_THRESHOLD_MS)
            {
                _accumSpikes += 1;
                if (LOG_EACH_FRAME_SPIKE)
                    Debug.LogWarning($"[Perf] Frame spike: {ms:F1}ms ({1000f/ms:F0}FPS) at {Time.frameCount}");
            }

            _reportTimer += dt;
            if (_reportTimer >= REPORT_INTERVAL_S)
            {
                ReportPeriodic();
                _reportTimer = 0f;
                _accumTime = 0f;
                _accumFrames = 0;
                _accumMaxMs = 0f;
                _accumSpikes = 0;
                lock (_sectionTotalMs)
                {
                    _sectionTotalMs.Clear();
                    _sectionCount.Clear();
                    _sectionMaxMs.Clear();
                }
            }
        }

        private void ReportPeriodic()
        {
            if (_accumFrames == 0) return;

            float avgMs = (_accumTime / _accumFrames) * 1000f;
            float avgFps = 1000f / avgMs;

            string sectionDump = "";
            lock (_sectionTotalMs)
            {
                if (_sectionTotalMs.Count > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var kv in _sectionTotalMs)
                    {
                        int cnt = _sectionCount.TryGetValue(kv.Key, out int c) ? c : 1;
                        float max = _sectionMaxMs.TryGetValue(kv.Key, out float m) ? m : 0f;
                        sb.Append(' ')
                          .Append(kv.Key)
                          .Append('=')
                          .Append((kv.Value / cnt).ToString("F2"))
                          .Append("ms/")
                          .Append(max.ToString("F2"))
                          .Append("max#")
                          .Append(cnt);
                    }
                    sectionDump = sb.ToString();
                }
            }

            // LogWarning 으로 승격 — Editor Console / logcat 의 priority filter 에서 묻히지 않게
            Debug.Log($"[Perf] 1s avg={avgFps:F0}FPS ({avgMs:F1}ms), max={_accumMaxMs:F1}ms, spikes={_accumSpikes}/{_accumFrames}{sectionDump}");
        }

        // ─────────────────────────────────────────
        // Static API — 매니저에서 직접 측정 후 보고
        // ─────────────────────────────────────────

        /// <summary>매니저 측에서 hot path 시작 시 호출. 끝에 EndSection.</summary>
        public static Stopwatch StartSection()
        {
#if BALLOONFLOW_ENABLE_PERF_LOGGER
            return Stopwatch.StartNew();
#else
            return null;
#endif
        }

        /// <summary>Hot path 안쪽의 작은 구간을 allocation 없이 재기 위한 timestamp.</summary>
        public static float StartStampMs()
        {
#if BALLOONFLOW_ENABLE_PERF_LOGGER
            return Time.realtimeSinceStartup * 1000f;
#else
            return 0f;
#endif
        }

        public static void EndSection(float startMs, string label)
        {
#if BALLOONFLOW_ENABLE_PERF_LOGGER
            RecordSectionMs(label, ElapsedMs(startMs));
#endif
        }

        public static float ElapsedMs(float startMs)
        {
#if BALLOONFLOW_ENABLE_PERF_LOGGER
            return Time.realtimeSinceStartup * 1000f - startMs;
#else
            return 0f;
#endif
        }

        /// <summary>매니저 측 hot path 끝. 1초 누적에 합산. label 별 평균 ms 표시.</summary>
        public static void EndSection(Stopwatch sw, string label)
        {
#if BALLOONFLOW_ENABLE_PERF_LOGGER
            if (sw == null) return;
            sw.Stop();
            RecordSectionMs(label, (float)sw.Elapsed.TotalMilliseconds);
#endif
        }

        public static void RecordSectionMs(string label, float ms)
        {
#if BALLOONFLOW_ENABLE_PERF_LOGGER
            lock (_sectionTotalMs)
            {
                if (_sectionTotalMs.ContainsKey(label))
                {
                    _sectionTotalMs[label] += ms;
                    _sectionCount[label]   += 1;
                    if (!_sectionMaxMs.ContainsKey(label) || ms > _sectionMaxMs[label])
                        _sectionMaxMs[label] = ms;
                }
                else
                {
                    _sectionTotalMs[label] = ms;
                    _sectionCount[label]   = 1;
                    _sectionMaxMs[label]   = ms;
                }
            }
#endif
        }
    }
}
