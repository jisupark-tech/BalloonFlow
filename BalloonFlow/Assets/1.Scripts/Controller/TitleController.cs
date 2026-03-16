using UnityEngine;
using UnityEngine.InputSystem;
using Touchscreen = UnityEngine.InputSystem.Touchscreen;

namespace BalloonFlow
{
    /// <summary>
    /// Title scene controller. Loads UITitle prefab, transitions to Lobby on tap or timeout.
    /// </summary>
    public class TitleController : MonoBehaviour
    {
        private const float AUTO_TRANSITION_DELAY = 3.0f;
        private const string PREFAB_PATH = "UI/UITitle";

        private bool _tapped;
        private float _timer;

        private void Awake()
        {
            if (!GameManager.HasInstance)
            {
                var go = new GameObject("GameManager");
                go.AddComponent<GameManager>();
            }

            if (!ResourceManager.HasInstance)
            {
                var go = new GameObject("Mgr_Resource");
                go.AddComponent<ResourceManager>();
            }

            LoadUI();
        }

        private void Update()
        {
            if (_tapped) return;
            _timer += Time.deltaTime;

            bool mousePressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            bool touchPressed = Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame;

            if (mousePressed || touchPressed || _timer >= AUTO_TRANSITION_DELAY)
            {
                _tapped = true;
                if (GameManager.HasInstance)
                    GameManager.Instance.LoadScene(GameManager.SCENE_LOBBY);
            }
        }

        private void LoadUI()
        {
            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null) return;

            var prefab = Resources.Load<GameObject>(PREFAB_PATH);
            if (prefab == null)
            {
                Debug.LogError($"[TitleController] Prefab not found: {PREFAB_PATH}");
                return;
            }

            Instantiate(prefab, canvas.transform);
        }
    }
}
