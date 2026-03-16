using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// 카메라 매니저. Title 씬에서 SceneBuilder가 배치.
    /// MainCamera + UICamera를 자식으로 보유, 씬별 설정 변경.
    /// Singleton → DontDestroyOnLoad → 씬 전환 후에도 유지.
    /// </summary>
    public class CameraManager : Singleton<CameraManager>
    {
        [Header("[Camera]")]
        public Camera MainCamera;
        public Camera UICamera;

        #region Fields

        // 카메라 위치 강제 유지용
        private bool _enforcePosition;
        private Vector3 _expectedPosition;
        private Vector3 _expectedEuler;

        #endregion

        #region Configure Per Scene

        /// <summary>Title: 2D, 어두운 배경</summary>
        public void ConfigureTitle()
        {
            if (MainCamera == null) return;
            MainCamera.orthographic = true;
            MainCamera.clearFlags = CameraClearFlags.SolidColor;
            MainCamera.backgroundColor = new Color(0.08f, 0.08f, 0.16f);
            MainCamera.depth = 0;
            SetCameraTransform(new Vector3(0f, 0f, -10f), Vector3.zero);

            if (UICamera != null) UICamera.gameObject.SetActive(false);
        }

        /// <summary>Lobby: 2D, 네이비 배경</summary>
        public void ConfigureLobby()
        {
            if (MainCamera == null) return;
            MainCamera.orthographic = true;
            MainCamera.clearFlags = CameraClearFlags.SolidColor;
            MainCamera.backgroundColor = new Color(0.06f, 0.10f, 0.18f);
            MainCamera.depth = 0;
            SetCameraTransform(new Vector3(0f, 0f, -10f), Vector3.zero);

            if (UICamera != null) UICamera.gameObject.SetActive(false);
        }

        /// <summary>InGame: 3D, 스카이박스, UICamera 활성</summary>
        public void ConfigureInGame()
        {
            if (MainCamera == null) return;
            MainCamera.orthographic = false;
            MainCamera.clearFlags = CameraClearFlags.Skybox;
            MainCamera.fieldOfView = 45f;
            MainCamera.depth = 0;
            SetCameraTransform(new Vector3(0f, 20f, -12f), new Vector3(65f, 0f, 0f));

            if (UICamera != null)
            {
                UICamera.gameObject.SetActive(true);
                TrySetURPOverlay();
            }
        }

        #endregion

        #region Camera Transform — 위치 설정 + LateUpdate 강제 유지

        void SetCameraTransform(Vector3 _pos, Vector3 _euler)
        {
            _expectedPosition = _pos;
            _expectedEuler = _euler;
            _enforcePosition = true;

            MainCamera.transform.position = _pos;
            MainCamera.transform.eulerAngles = _euler;
        }

        void LateUpdate()
        {
            if (!_enforcePosition || MainCamera == null) return;

            if (MainCamera.transform.position != _expectedPosition)
            {
                Debug.LogWarning($"[CameraManager] 카메라 위치 외부 변경 감지: {MainCamera.transform.position} → 복구: {_expectedPosition}");
                MainCamera.transform.position = _expectedPosition;
                MainCamera.transform.eulerAngles = _expectedEuler;
            }
        }

        #endregion

        #region URP

        void TrySetURPOverlay()
        {
            var _urpType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (_urpType == null) return;

            // UICamera → Overlay
            var _uiData = UICamera.gameObject.GetComponent(_urpType);
            if (_uiData == null) _uiData = UICamera.gameObject.AddComponent(_urpType);
            var _renderType = _urpType.GetProperty("renderType");
            if (_renderType != null) _renderType.SetValue(_uiData, 1);

            // MainCamera → Stack에 UICamera 추가
            var _mainData = MainCamera.gameObject.GetComponent(_urpType);
            if (_mainData == null) _mainData = MainCamera.gameObject.AddComponent(_urpType);
            var _stackProp = _urpType.GetProperty("cameraStack");
            if (_stackProp != null)
            {
                var _stack = _stackProp.GetValue(_mainData) as System.Collections.IList;
                if (_stack != null) _stack.Add(UICamera);
            }
        }

        #endregion
    }
}
