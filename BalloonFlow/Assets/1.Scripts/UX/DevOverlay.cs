using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Development Build / Editor 전용 좌하단 dev panel.
    ///
    /// 기능:
    ///  1) [Perf] log 캡처 — Application.logMessageReceived subscribe, 최근 N 줄 표시.
    ///  2) Level skip cheat — 현재 LevelId ±1 / ±10 / Reload 버튼.
    ///
    /// UI:
    ///  - 평상시: 좌하단 작은 [DEV] 토글 버튼만 (게임 화면 안 가림).
    ///  - 토글 시 패널 expand — log + cheat 표시.
    ///  - PerfHud (우상단) 와 충돌 안 함.
    /// </summary>
    public class DevOverlay : MonoBehaviour
    {
        private const int MAX_LOG_LINES = 12;
        private const string LOG_FILTER = "[Perf]"; // 이 substring 포함 line 만 캡처

        private readonly Queue<string> _logLines = new Queue<string>();
        private bool _expanded;
        private string _levelInput = "";

        private GUIStyle _styleBtn;
        private GUIStyle _styleLog;
        private GUIStyle _styleHeader;
        private GUIStyle _styleField;
        private Texture2D _bgTex;
        private Texture2D _bgTexLog;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            const string GO_NAME = "[DevOverlay]";
            if (GameObject.Find(GO_NAME) != null) return;
            var go = new GameObject(GO_NAME);
            DontDestroyOnLoad(go);
            go.AddComponent<DevOverlay>();
#endif
        }

        private void OnEnable()
        {
            Application.logMessageReceived += HandleLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= HandleLog;
        }

        private void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (string.IsNullOrEmpty(condition)) return;
            if (!condition.Contains(LOG_FILTER)) return;

            // [Perf] log 가 stack trace 포함 멀티라인 → 첫 줄만.
            int newline = condition.IndexOf('\n');
            string line = newline > 0 ? condition.Substring(0, newline) : condition;

            _logLines.Enqueue(line);
            while (_logLines.Count > MAX_LOG_LINES) _logLines.Dequeue();
        }

        private void OnGUI()
        {
            EnsureStyles();

            float scale = Mathf.Max(1f, Screen.dpi / 160f);
            float margin = 16f * scale;
            float btnW = 90f * scale;
            float btnH = 56f * scale;

            // ── 토글 버튼 (좌하단 항상 표시) ──
            float btnX = margin;
            float btnY = Screen.height - btnH - margin;

            string btnLabel = _expanded ? "DEV ▼" : "DEV ▲";
            if (GUI.Button(new Rect(btnX, btnY, btnW, btnH), btnLabel, _styleBtn))
            {
                _expanded = !_expanded;
            }

            if (!_expanded) return;

            // ── 패널 expand ──
            float panelW = Mathf.Min(640f * scale, Screen.width - margin * 2f);
            float lineH = 28f * scale;
            float logH  = lineH * MAX_LOG_LINES + 16f;
            float cheatH = lineH * 3f + 24f;
            float panelH = logH + cheatH + 24f;
            float panelX = margin;
            float panelY = btnY - panelH - 8f;
            if (panelY < margin) panelY = margin; // 화면 위로 안 나가게

            // 반투명 배경
            GUI.DrawTexture(new Rect(panelX, panelY, panelW, panelH), _bgTex);

            // ── Header: [Perf] log ──
            float curY = panelY + 8f;
            GUI.Label(new Rect(panelX + 12f, curY, panelW - 24f, lineH),
                $"── [Perf] log (last {MAX_LOG_LINES}) ──", _styleHeader);
            curY += lineH;

            // log lines 배경
            GUI.DrawTexture(new Rect(panelX + 6f, curY, panelW - 12f, logH - lineH), _bgTexLog);

            int idx = 0;
            foreach (string l in _logLines)
            {
                GUI.Label(new Rect(panelX + 12f, curY + idx * lineH, panelW - 24f, lineH),
                    l, _styleLog);
                idx++;
            }
            curY += logH - lineH + 8f;

            // ── Cheat: Level skip ──
            int currentLv = LevelManager.HasInstance ? LevelManager.Instance.CurrentLevelId : 0;
            GUI.Label(new Rect(panelX + 12f, curY, panelW - 24f, lineH),
                $"── Cheat: Level (current = {currentLv}) ──", _styleHeader);
            curY += lineH;

            // 버튼 row 1: -10 / -1 / +1 / +10 / Reload
            float bw = (panelW - 24f - 4f * 6f) / 5f;
            float bx = panelX + 12f;
            if (GUI.Button(new Rect(bx, curY, bw, lineH * 1.4f), "-10", _styleBtn))
                JumpLevel(currentLv - 10);
            bx += bw + 6f;
            if (GUI.Button(new Rect(bx, curY, bw, lineH * 1.4f), "-1", _styleBtn))
                JumpLevel(currentLv - 1);
            bx += bw + 6f;
            if (GUI.Button(new Rect(bx, curY, bw, lineH * 1.4f), "+1", _styleBtn))
                JumpLevel(currentLv + 1);
            bx += bw + 6f;
            if (GUI.Button(new Rect(bx, curY, bw, lineH * 1.4f), "+10", _styleBtn))
                JumpLevel(currentLv + 10);
            bx += bw + 6f;
            if (GUI.Button(new Rect(bx, curY, bw, lineH * 1.4f), "Reload", _styleBtn))
                JumpLevel(currentLv);
            curY += lineH * 1.5f + 6f;

            // 직접 입력 row
            GUI.Label(new Rect(panelX + 12f, curY, 80f * scale, lineH),
                "Goto LV:", _styleHeader);
            float fldX = panelX + 12f + 90f * scale;
            float fldW = 140f * scale;
            _levelInput = GUI.TextField(new Rect(fldX, curY, fldW, lineH * 1.2f),
                _levelInput ?? "", 4, _styleField);
            float goX = fldX + fldW + 8f;
            if (GUI.Button(new Rect(goX, curY, 100f * scale, lineH * 1.4f), "Go", _styleBtn))
            {
                if (int.TryParse(_levelInput, out int lv)) JumpLevel(lv);
            }
        }

        private void JumpLevel(int targetLevel)
        {
            if (!LevelManager.HasInstance)
            {
                Debug.LogWarning("[DevOverlay] LevelManager 없음 — JumpLevel skip");
                return;
            }
            int clamped = Mathf.Max(1, targetLevel);
            Debug.Log($"[DevOverlay] LoadLevel({clamped})");
            LevelManager.Instance.LoadLevel(clamped);
            _expanded = false; // 즉시 collapse 해서 게임 화면 보이게
        }

        private void EnsureStyles()
        {
            if (_styleBtn == null)
            {
                _styleBtn = new GUIStyle(GUI.skin.button)
                {
                    fontSize = (int)(16f * Mathf.Max(1f, Screen.dpi / 160f)),
                    fontStyle = FontStyle.Bold
                };
                _styleLog = new GUIStyle(GUI.skin.label)
                {
                    fontSize = (int)(13f * Mathf.Max(1f, Screen.dpi / 160f)),
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = false,
                    clipping = TextClipping.Clip
                };
                _styleLog.normal.textColor = new Color(0.85f, 1f, 0.85f);
                _styleHeader = new GUIStyle(GUI.skin.label)
                {
                    fontSize = (int)(14f * Mathf.Max(1f, Screen.dpi / 160f)),
                    fontStyle = FontStyle.Bold
                };
                _styleHeader.normal.textColor = Color.cyan;
                _styleField = new GUIStyle(GUI.skin.textField)
                {
                    fontSize = (int)(16f * Mathf.Max(1f, Screen.dpi / 160f)),
                    alignment = TextAnchor.MiddleLeft
                };
            }
            if (_bgTex == null)
            {
                _bgTex = new Texture2D(1, 1);
                _bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.78f));
                _bgTex.Apply();
            }
            if (_bgTexLog == null)
            {
                _bgTexLog = new Texture2D(1, 1);
                _bgTexLog.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.45f));
                _bgTexLog.Apply();
            }
        }

        private void OnDestroy()
        {
            if (_bgTex != null) Destroy(_bgTex);
            if (_bgTexLog != null) Destroy(_bgTexLog);
        }
    }
}
