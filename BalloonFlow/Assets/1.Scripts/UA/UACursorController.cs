using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
using UnityEngine.UI;
#endif

namespace BalloonFlow.UA
{
    /// <summary>
    /// UA 크리에이티브 녹화 전용 — Editor Play 에서만 동작(빌드에선 #if UNITY_EDITOR 로 전부 제거).
    /// ROLLBACK_UA_HAND_CURSOR_20260713:
    ///   · Editor Play 시작 시 '자동 전역 소환'(DontDestroyOnLoad) → 씬에 수동 배치 없이 어디서든 C 로 사용.
    ///   · OS 커서(Cursor.SetCursor)는 크기 상한(~128px)이 있어 256px 이미지가 안 뜬다 → UI 오버레이(RawImage).
    ///   · C : 손 On/Off  ·  마우스 추종  ·  좌버튼 누름=HandClick / 떼면=Hand
    ///   · 마우스 휠 : 크기 조정(Shift=크게)  ·  방향키 : 손끝 위치 보정(Shift=10px)  ·  H : OS 커서 표시 토글
    ///   · 크기/위치는 EditorPrefs 에 영속(플레이 재시작해도 유지).
    /// 이미지는 인스펙터 미할당 시 Assets/UA/Hand.png·HandClick.png 자동 로드. 오버레이는 런타임 생성.
    /// </summary>
    public class UACursorController : MonoBehaviour
    {
        [Header("손 이미지 (비우면 Assets/UA/Hand·HandClick 자동 로드)")]
        [SerializeField] private Texture2D _handImage;
        [SerializeField] private Texture2D _handClickImage;

        [Header("초기값 (EditorPrefs 에 저장된 값이 우선)")]
        [SerializeField] private Vector2 _size = new Vector2(256f, 256f);
        [SerializeField] private Vector2 _hotspotOffset = Vector2.zero;
        [SerializeField] private bool _hideSystemCursor = true;

#if UNITY_EDITOR
        private const string HAND_PATH      = "Assets/UA/Hand.png";
        private const string HANDCLICK_PATH = "Assets/UA/HandClick.png";
        private const string PREF_SX = "UAHand_SizeX", PREF_SY = "UAHand_SizeY";
        private const string PREF_OX = "UAHand_OffX",  PREF_OY = "UAHand_OffY";

        private Canvas _canvas;
        private RawImage _raw;
        private Texture2D _handTex, _handClickTex;
        private bool _active;
        private bool _pressed;

        // Editor Play 시작 시 자동 소환 — 씬에 수동 배치 불필요(어느 씬에서든 C 로 동작).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
            if (Object.FindFirstObjectByType<UACursorController>() != null) return; // 이미 있으면 skip
            var go = new GameObject("[UAHandCursor]");
            go.AddComponent<UACursorController>();
            DontDestroyOnLoad(go);
        }

        private void Start()
        {
            _handTex      = _handImage      != null ? _handImage      : UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(HAND_PATH);
            _handClickTex = _handClickImage != null ? _handClickImage : UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(HANDCLICK_PATH);
            if (_handTex == null)
                Debug.LogWarning($"[UAHand] Hand 이미지 없음 — {HAND_PATH} 확인(또는 인스펙터 지정).");

            // 저장된 크기/위치 복원(없으면 인스펙터 초기값).
            _size.x         = UnityEditor.EditorPrefs.GetFloat(PREF_SX, _size.x);
            _size.y         = UnityEditor.EditorPrefs.GetFloat(PREF_SY, _size.y);
            _hotspotOffset.x = UnityEditor.EditorPrefs.GetFloat(PREF_OX, _hotspotOffset.x);
            _hotspotOffset.y = UnityEditor.EditorPrefs.GetFloat(PREF_OY, _hotspotOffset.y);

            BuildOverlay();
            SetActive(false);
        }

        private void BuildOverlay()
        {
            var canvasGo = new GameObject("UAHandOverlay");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32760;                 // 최상단(게임 UI 위)

            var handGo = new GameObject("Hand");
            handGo.transform.SetParent(canvasGo.transform, false);
            _raw = handGo.AddComponent<RawImage>();
            _raw.raycastTarget = false;                   // ★ 실제 게임 클릭 안 막음(손은 장식)
            _raw.texture = _handTex;
            var rt = _raw.rectTransform;
            rt.anchorMin = Vector2.zero;                  // 좌하단 기준 → anchoredPosition = 스크린 픽셀
            rt.anchorMax = Vector2.zero;
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = _size;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool shift = kb != null && (kb[Key.LeftShift].isPressed || kb[Key.RightShift].isPressed);

            if (kb != null)
            {
                if (kb[Key.C].wasPressedThisFrame) SetActive(!_active);
                if (kb[Key.H].wasPressedThisFrame) { _hideSystemCursor = !_hideSystemCursor; ApplyCursorVisibility(); }

                // 방향키 → 손끝 위치(offset) nudge
                float step = shift ? 10f : 1f;
                bool moved = false;
                if (kb[Key.LeftArrow].wasPressedThisFrame)  { _hotspotOffset.x -= step; moved = true; }
                if (kb[Key.RightArrow].wasPressedThisFrame) { _hotspotOffset.x += step; moved = true; }
                if (kb[Key.UpArrow].wasPressedThisFrame)    { _hotspotOffset.y += step; moved = true; }
                if (kb[Key.DownArrow].wasPressedThisFrame)  { _hotspotOffset.y -= step; moved = true; }
                if (moved) SaveTuning();
            }

            if (!_active || _raw == null || mouse == null) return;

            // 마우스 휠 → 크기 조정
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                float k = (shift ? 32f : 8f) * Mathf.Sign(scroll);
                _size = new Vector2(Mathf.Max(16f, _size.x + k), Mathf.Max(16f, _size.y + k));
                SaveTuning();
            }

            // press → HandClick, release → Hand
            if (mouse.leftButton.wasPressedThisFrame)  { _pressed = true;  _raw.texture = _handClickTex != null ? _handClickTex : _handTex; }
            if (mouse.leftButton.wasReleasedThisFrame) { _pressed = false; _raw.texture = _handTex; }

            // 마우스 추종
            Vector2 p = mouse.position.ReadValue();
            _raw.rectTransform.sizeDelta        = _size;
            _raw.rectTransform.anchoredPosition = p + _hotspotOffset;
        }

        private void SaveTuning()
        {
            UnityEditor.EditorPrefs.SetFloat(PREF_SX, _size.x);
            UnityEditor.EditorPrefs.SetFloat(PREF_SY, _size.y);
            UnityEditor.EditorPrefs.SetFloat(PREF_OX, _hotspotOffset.x);
            UnityEditor.EditorPrefs.SetFloat(PREF_OY, _hotspotOffset.y);
        }

        private void SetActive(bool on)
        {
            _active = on;
            if (_canvas != null) _canvas.gameObject.SetActive(on);
            ApplyCursorVisibility();
        }

        private void ApplyCursorVisibility()
        {
            Cursor.visible = !(_active && _hideSystemCursor);
        }

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
            GUI.Label(new Rect(12, 10, 720, 40),
                $"[UA Hand] C={( _active ? "ON" : "OFF")} | {(_pressed ? "HandClick" : "Hand")} | " +
                $"size={_size.x}x{_size.y}(휠) | offset=({_hotspotOffset.x},{_hotspotOffset.y})(방향키) | OS커서={( Cursor.visible ? "표시" : "숨김")}(H)", style);
        }

        private void OnDisable()
        {
            Cursor.visible = true;
            if (_canvas != null) Destroy(_canvas.gameObject);
        }
#endif
    }
}
