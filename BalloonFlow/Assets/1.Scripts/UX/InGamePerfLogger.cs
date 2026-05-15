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

        private float _accumTime;
        private int   _accumFrames;
        private float _accumMaxMs;
        private int   _accumSpikes;
        private float _reportTimer;

        // 매니저별 누적 ms (1초 윈도우)
        private static readonly System.Collections.Generic.Dictionary<string, float> _sectionTotalMs = new();
        private static readonly System.Collections.Generic.Dictionary<string, int>   _sectionCount   = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
#if BALLOONFLOW_ENABLE_PERF_LOGGER
            const string GO_NAME = "[InGamePerfLogger]";
            if (GameObject.Find(GO_NAME) != null) return;
            var go = new GameObject(GO_NAME);
            DontDestroyOnLoad(go);
            go.AddComponent<InGamePerfLogger>();
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
                        sb.Append(' ').Append(kv.Key).Append('=').Append((kv.Value / cnt).ToString("F2")).Append("ms");
                    }
                    sectionDump = sb.ToString();
                }
            }

            // LogWarning 으로 승격 — Editor Console / logcat 의 priority filter 에서 묻히지 않게
            Debug.LogWarning($"[Perf] 1s avg={avgFps:F0}FPS ({avgMs:F1}ms), max={_accumMaxMs:F1}ms, spikes={_accumSpikes}/{_accumFrames}{sectionDump}");
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

        /// <summary>매니저 측 hot path 끝. 1초 누적에 합산. label 별 평균 ms 표시.</summary>
        public static void EndSection(Stopwatch sw, string label)
        {
#if BALLOONFLOW_ENABLE_PERF_LOGGER
            if (sw == null) return;
            sw.Stop();
            float ms = (float)sw.Elapsed.TotalMilliseconds;
            lock (_sectionTotalMs)
            {
                if (_sectionTotalMs.ContainsKey(label))
                {
                    _sectionTotalMs[label] += ms;
                    _sectionCount[label]   += 1;
                }
                else
                {
                    _sectionTotalMs[label] = ms;
                    _sectionCount[label]   = 1;
                }
            }
#endif
        }
    }
}
