using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 디바이스에서 실시간 FPS / Frame time 표시 (우상단).
    /// Development Build 또는 Editor 에서만 자동 spawn.
    /// 수치로 부하 정량화 — sustained low (e.g. 35 항상) vs spike (60→30 순간) 식별용.
    ///
    /// 색상:
    ///  - 55+ FPS  녹색
    ///  - 40~55 FPS 노랑
    ///  - 40 미만   빨강
    ///
    /// 표시 항목:
    ///  - 평균 FPS (60 frame 슬라이딩)
    ///  - Frame time (ms)
    ///  - 최근 5초 min/max FPS (5초마다 리셋 — 스테이지 진입 후 spike 감지)
    /// </summary>
    public class PerfHud : MonoBehaviour
    {
        private const int SAMPLE_WINDOW = 60;
        private const float RESET_INTERVAL = 5f;

        private readonly float[] _frameTimes = new float[SAMPLE_WINDOW];
        private int _frameIdx;
        private int _frameCount;

        private float _minFps = float.MaxValue;
        private float _maxFps;
        private float _lastResetTime;

        private GUIStyle _styleMain;
        private GUIStyle _styleSub;
        private Texture2D _bgTex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
#if BALLOONFLOW_ENABLE_IMGUI_DEBUG
            const string GO_NAME = "[PerfHud]";
            if (GameObject.Find(GO_NAME) != null) return;
            var go = new GameObject(GO_NAME);
            DontDestroyOnLoad(go);
            go.AddComponent<PerfHud>();
#endif
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _frameTimes[_frameIdx] = dt;
            _frameIdx = (_frameIdx + 1) % SAMPLE_WINDOW;
            if (_frameCount < SAMPLE_WINDOW) _frameCount++;

            float fps = dt > 0f ? 1f / dt : 0f;
            if (fps < _minFps) _minFps = fps;
            if (fps > _maxFps) _maxFps = fps;

            if (Time.unscaledTime - _lastResetTime > RESET_INTERVAL)
            {
                _minFps = float.MaxValue;
                _maxFps = 0f;
                _lastResetTime = Time.unscaledTime;
            }
        }

        private float ComputeAvgFps()
        {
            if (_frameCount == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < _frameCount; i++) sum += _frameTimes[i];
            return sum > 0f ? _frameCount / sum : 0f;
        }

        private void OnGUI()
        {
            EnsureStyles();

            float avg     = ComputeAvgFps();
            float frameMs = avg > 0f ? 1000f / avg : 0f;

            Color color;
            if (avg >= 55f)      color = Color.green;
            else if (avg >= 40f) color = Color.yellow;
            else                 color = Color.red;

            string range = (_minFps == float.MaxValue) ? "..." : $"{_minFps:F0}-{_maxFps:F0}";

            // DPI scale (모바일 고해상도에서 글자 너무 작아지는 것 방지)
            float scale = Mathf.Max(1f, Screen.dpi / 160f);
            float fontSize = 22f * scale;
            float lineH = fontSize * 1.4f;
            float w = 280f * scale;
            float h = lineH * 2f + 10f;
            float margin = 20f * scale;
            float x = Screen.width - w - margin;
            float y = margin;

            _styleMain.fontSize = (int)fontSize;
            _styleSub.fontSize  = (int)(fontSize * 0.75f);

            // 반투명 검정 배경
            GUI.DrawTexture(new Rect(x - 10f, y - 5f, w + 20f, h), _bgTex);

            _styleMain.normal.textColor = color;
            GUI.Label(new Rect(x, y,         w, lineH),
                $"FPS {avg:F0}   ({frameMs:F1}ms)", _styleMain);

            _styleSub.normal.textColor = Color.white;
            GUI.Label(new Rect(x, y + lineH, w, lineH),
                $"min-max(5s)  {range}", _styleSub);
        }

        private void EnsureStyles()
        {
            if (_styleMain == null)
            {
                _styleMain = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                _styleSub  = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Normal };
            }
            if (_bgTex == null)
            {
                _bgTex = new Texture2D(1, 1);
                _bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
                _bgTex.Apply();
            }
        }

        private void OnDestroy()
        {
            if (_bgTex != null) Destroy(_bgTex);
        }
    }
}
