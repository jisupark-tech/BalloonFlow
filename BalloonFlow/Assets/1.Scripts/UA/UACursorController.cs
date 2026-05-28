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
    /// hotspot offset 으로 이미지에 맞춰 픽셀 단위 좌표 보정.
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
                Cursor.SetCursor(tex, ResolveHotspot(), _cursorMode);
            }
            else
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            }
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

        // 녹화/플레이 종료 시 기본 커서 복원.
        private void OnDisable()
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            Cursor.visible = true;
        }
#endif
    }
}
