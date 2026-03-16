using UnityEngine;
using UnityEngine.InputSystem;
using Touchscreen = UnityEngine.InputSystem.Touchscreen;

namespace BalloonFlow
{
    /// <summary>
    /// Title 씬 컨트롤러.
    /// - GameManager, CameraManager, UIManager는 SceneBuilder가 씬에 배치 → Awake에서 Instance 자동 설정
    /// - UITitle 프리팹을 UIManager.OpenUI로 로드
    /// - 탭 or 타임아웃 → Lobby 이동
    /// </summary>
    public class TitleController : MonoBehaviour
    {
        private const float AUTO_TRANSITION_DELAY = 3.0f;

        private bool _tapped;
        private float _timer;

        void Start()
        {
            // 카메라 설정
            if (CameraManager.HasInstance)
                CameraManager.Instance.ConfigureTitle();

            // 씬 캔버스를 UIManager에 등록
            var _canvasGO = GameObject.Find("Canvas");
            if (_canvasGO != null && UIManager.HasInstance)
            {
                UIManager.Instance.SetSceneCanvas(_canvasGO.transform);
                UIManager.Instance.OpenUI<UITitle>("UI/UITitle");
            }
        }

        void Update()
        {
            if (_tapped) return;
            _timer += Time.deltaTime;

            bool _mousePressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            bool _touchPressed = Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame;

            if (_mousePressed || _touchPressed || _timer >= AUTO_TRANSITION_DELAY)
            {
                _tapped = true;
                if (GameManager.HasInstance)
                    GameManager.Instance.LoadScene(GameManager.SCENE_LOBBY);
            }
        }
    }
}
