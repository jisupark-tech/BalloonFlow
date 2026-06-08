using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace BalloonFlow.UA
{
    /// <summary>
    /// UA 크리에이티브 녹화 전용 — Editor 에서만 동작.
    /// C : 기본 커서 ↔ 업로드 이미지 커서 토글 (cursor)
    /// H : 커서 표시 On/Off 토글 (hide)
    /// 방향키 : hotspot offset 픽셀 단위 실시간 보정(←→=x, ↑↓=y, Shift=10px). 인스펙터 _hotspotOffset 로도 입력 가능.
    /// 화면 좌상단에 현재 상태(커스텀/표시/offset) 표시 — 보정값 읽어 인스펙터에 옮겨두면 영구 저장.
    /// 빌드에서는 모든 로직이 #if UNITY_EDITOR 로 제거됨(no-op).
    /// </summary>
    public class UACursorController : MonoBehaviour
    {
        [Header("커서 이미지 (비우면 PlayerSettings.defaultCursor 사용)")]
        [SerializeField] private Texture2D _cursorImage;

        [Header("좌표 보정 (픽셀, + / - 입력)")]
        [Tooltip("PlayerSettings hotspot 에 더해지는 보정값. x=오른쪽+, y=아래쪽+")]
        [SerializeField] private Vector2 _hotspotOffset = Vector2.zero;

        [Header("옵션")]
        [Tooltip("녹화에 잡히도록 기본 ForceSoftware(Unity 렌더). Auto 는 하드웨어 커서라 녹화 누락 가능.")]
        [SerializeField] private CursorMode _cursorMode = CursorMode.ForceSoftware;
        [SerializeField] private bool _startWithCustomCursor = false;

#if UNITY_EDITOR
        private bool _customActive;
        private bool _cursorVisible = true;

        private void Start()
        {
            if (_startWithCustomCursor) SetCustomCursor(true);
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb[Key.C].wasPressedThisFrame) SetCustomCursor(!_customActive);
            if (kb[Key.H].wasPressedThisFrame) SetCursorVisible(!_cursorVisible);

            // 좌표 보정 실시간 입력 — 방향키로 hotspot offset 픽셀 nudge (Shift=10px). x=오른쪽+, y=아래쪽+.
            float step = (kb[Key.LeftShift].isPressed || kb[Key.RightShift].isPressed) ? 10f : 1f;
            Vector2 d = Vector2.zero;
            if (kb[Key.LeftArrow].wasPressedThisFrame)  d.x -= step;
            if (kb[Key.RightArrow].wasPressedThisFrame) d.x += step;
            if (kb[Key.UpArrow].wasPressedThisFrame)    d.y -= step;
            if (kb[Key.DownArrow].wasPressedThisFrame)  d.y += step;
            if (d != Vector2.zero)
            {
                _hotspotOffset += d;
                if (_customActive) SetCustomCursor(true);   // 보정 즉시 반영
            }
        }

        // 녹화 중 현재 상태/보정값 확인용 오버레이 (Editor 전용).
        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
            GUI.Label(new Rect(12, 10, 560, 60),
                $"[UA Cursor] C 커스텀={(_customActive ? "ON" : "OFF")}  |  H 표시={(_cursorVisible ? "ON" : "OFF")}\n" +
                $"hotspot offset = ({_hotspotOffset.x}, {_hotspotOffset.y})   방향키 nudge (Shift=10px)", style);
        }

        private void SetCustomCursor(bool on)
        {
            _customActive = on;

            if (on)
            {
                Texture2D tex = ResolveCursorTexture();
                if (tex == null)
                {
                    Debug.LogWarning("[UACursor] 커서 이미지가 없습니다. " +
                        "_cursorImage 를 지정하거나 PlayerSettings > Default Cursor 에 추가하세요.");
                    _customActive = false;
                    return;
                }
                // Cursor.SetCursor 는 CPU 접근 가능한(Read/Write) RGBA32 텍스처만 받음.
                // 인게임 스프라이트는 보통 압축/non-readable 이라 거부됨("not CPU accessible").
                // → 읽기 가능한 RGBA32 사본을 런타임 생성해 사용(임포트 설정 무관, 어떤 텍스처든 OK).
                Cursor.SetCursor(MakeCursorReadable(tex), ResolveHotspot(), _cursorMode);
            }
            else
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            }
        }

        private Texture2D _readableCopy;   // MakeCursorReadable 캐시
        private Texture2D _readableSource;

        /// <summary>src 를 RGBA32 read/write 사본으로 변환(Blit→ReadPixels). 이미 만든 사본은 캐시 재사용.</summary>
        private Texture2D MakeCursorReadable(Texture2D src)
        {
            if (src == null) return null;
            if (_readableCopy != null && _readableSource == src) return _readableCopy;

            RenderTexture rt = RenderTexture.GetTemporary(
                src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;

            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            copy.Apply(false, false);   // CPU 접근 유지(makeNoLongerReadable=false)

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            if (_readableCopy != null) Destroy(_readableCopy);   // 이전 사본 정리
            _readableCopy = copy;
            _readableSource = src;
            copy.name = src.name + "_cursorReadable";
            return copy;
        }

        private void SetCursorVisible(bool visible)
        {
            _cursorVisible = visible;
            Cursor.visible = visible;
        }

        private Texture2D ResolveCursorTexture()
        {
            if (_cursorImage != null) return _cursorImage;
            return UnityEditor.PlayerSettings.defaultCursor;
        }

        private Vector2 ResolveHotspot()
        {
            Vector2 baseHotspot = (_cursorImage != null)
                ? Vector2.zero
                : UnityEditor.PlayerSettings.cursorHotspot;
            return baseHotspot + _hotspotOffset;
        }

        // 플레이 중 보정값/이미지를 바꾸면 즉시 반영.
        private void OnValidate()
        {
            if (Application.isPlaying && _customActive)
                SetCustomCursor(true);
        }

        // 녹화/플레이 종료 시 기본 커서 복원 + 생성한 읽기 사본 정리.
        private void OnDisable()
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            Cursor.visible = true;
            if (_readableCopy != null) { Destroy(_readableCopy); _readableCopy = null; _readableSource = null; }
        }
#endif
    }
}
