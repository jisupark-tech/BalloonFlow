using System.Collections;
using UnityEngine;
using DG.Tweening;

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

        [Header("[InGame Camera Position — Inspector에서 조절]")]
        [SerializeField] private Vector3 _inGamePosition = new Vector3(0f, 20f, -12f);
        [SerializeField] private Vector3 _inGameRotation = new Vector3(65f, 0f, 0f);
        [SerializeField] private float _inGameFOV = 45f;
        [SerializeField] private bool _inGameOrthographic = false;
        [SerializeField] private float _inGameOrthoSize = 10f; // Inspector 기본값

        [Header("[Camera Shake]")]
        [Tooltip("기본 흔들림 강도 (유닛)")]
        [SerializeField] private float _shakeIntensity = 0.3f;
        [Tooltip("기본 흔들림 지속 시간 (초)")]
        [SerializeField] private float _shakeDuration = 0.25f;
        [Tooltip("감쇠 속도 (클수록 빠르게 멈춤)")]
        [SerializeField] private float _shakeDamping = 5f;

        #region Fields

        // 카메라 위치 강제 유지용
        private bool _enforcePosition;
        private Vector3 _expectedPosition;
        private Vector3 _expectedEuler;

        // Shake
        private Coroutine _shakeCoroutine;
        private Vector3 _shakeOffset;
        private bool _isShaking;

        // Smooth camera move (MoveToTarget / MoveBack)
        private Vector3 _savedPosition;

        #endregion

        #region Properties

        /// <summary>InGame 카메라 위치 (런타임에서도 변경 가능)</summary>
        public Vector3 InGamePosition { get => _inGamePosition; set => _inGamePosition = value; }
        public Vector3 InGameRotation { get => _inGameRotation; set => _inGameRotation = value; }
        public float InGameFOV { get => _inGameFOV; set => _inGameFOV = value; }

        #endregion

        #region Configure Per Scene

        /// <summary>Title: 2D, 어두운 배경</summary>
        public void ConfigureTitle()
        {
            if (MainCamera == null) return;
            MainCamera.orthographic = true;
            MainCamera.clearFlags = CameraClearFlags.SolidColor;
            MainCamera.backgroundColor = new Color(0.08f, 0.08f, 0.16f);
            MainCamera.nearClipPlane = 0.3f;
            MainCamera.farClipPlane = 50f;
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
            MainCamera.nearClipPlane = 0.3f;
            MainCamera.farClipPlane = 50f;
            MainCamera.depth = 0;
            SetCameraTransform(new Vector3(0f, 0f, -10f), Vector3.zero);

            if (UICamera != null) UICamera.gameObject.SetActive(false);
        }

        /// <summary>
        /// Re-acquires Camera.main if the current reference was lost (scene transition).
        /// Call before ConfigureInGame when camera may have changed.
        /// </summary>
        public void RefreshMainCamera()
        {
            if (MainCamera == null)
            {
                MainCamera = Camera.main;
                if (MainCamera != null)
                    Debug.Log("[CameraManager] Re-acquired Main Camera after scene transition.");
            }
        }

        /// <summary>Stops enforcing camera position (used when entering scenes with own camera setup).</summary>
        public void ReleaseEnforcement()
        {
            _enforcePosition = false;
        }

        /// <summary>InGame: Inspector에서 설정한 위치/FOV/모드 적용, UICamera 활성</summary>
        public void ConfigureInGame()
        {
            RefreshMainCamera();
            if (MainCamera == null) return;
            MainCamera.orthographic = true; // InGame은 항상 Orthographic
            MainCamera.orthographicSize = 15f;

            MainCamera.clearFlags = CameraClearFlags.SolidColor;
            MainCamera.backgroundColor = new Color(0.255f, 0.235f, 0.392f); // #413C64
            MainCamera.nearClipPlane = 0.3f;
            MainCamera.farClipPlane = 80f; // 기본 1000 → 80 (퍼즐 게임 범위 충분)
            MainCamera.depth = 0;

            // 레이어별 컬링 거리 — 먼 오브젝트 일찍 컬링
            float[] layerCullDist = new float[32];
            for (int i = 0; i < 32; i++) layerCullDist[i] = 80f; // 기본
            layerCullDist[0] = 60f; // Default 레이어 (풍선/다트/홀더) — 60m 넘으면 컬링
            MainCamera.layerCullDistances = layerCullDist;
            MainCamera.layerCullSpherical = true; // 구형 컬링 (사각형보다 정확)

            SetCameraTransform(_inGamePosition, _inGameRotation);

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

            // Shake 중이면 offset 적용 후 원래 위치에서 흔들림
            if (_isShaking)
            {
                MainCamera.transform.position = _expectedPosition + _shakeOffset;
                MainCamera.transform.eulerAngles = _expectedEuler;
            }
            else
            {
                if (MainCamera.transform.position != _expectedPosition)
                {
                    MainCamera.transform.position = _expectedPosition;
                    MainCamera.transform.eulerAngles = _expectedEuler;
                }
            }
        }

        #endregion

        #region Camera Shake

        /// <summary>기본 강도/시간으로 카메라 흔들기</summary>
        public void Shake()
        {
            Shake(_shakeIntensity, _shakeDuration);
        }

        /// <summary>강도 지정 카메라 흔들기 (기본 시간)</summary>
        public void Shake(float intensity)
        {
            Shake(intensity, _shakeDuration);
        }

        /// <summary>강도 + 시간 지정 카메라 흔들기</summary>
        public void Shake(float intensity, float duration)
        {
            if (MainCamera == null) return;

            if (_shakeCoroutine != null)
                StopCoroutine(_shakeCoroutine);

            _shakeCoroutine = StartCoroutine(ShakeCoroutine(intensity, duration));
        }

        /// <summary>즉시 흔들림 중지</summary>
        public void StopShake()
        {
            if (_shakeCoroutine != null)
            {
                StopCoroutine(_shakeCoroutine);
                _shakeCoroutine = null;
            }
            _isShaking = false;
            _shakeOffset = Vector3.zero;

            if (MainCamera != null && _enforcePosition)
            {
                MainCamera.transform.position = _expectedPosition;
                MainCamera.transform.eulerAngles = _expectedEuler;
            }
        }

        private IEnumerator ShakeCoroutine(float intensity, float duration)
        {
            _isShaking = true;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;

                // 감쇠: 시간이 지날수록 강도 줄어듦
                float remaining = 1f - (elapsed / duration);
                float damped = remaining;
                if (_shakeDamping > 0)
                    damped = Mathf.Pow(remaining, _shakeDamping * 0.5f);

                float currentIntensity = intensity * damped;

                // 랜덤 오프셋 (XY 평면 + 약간의 Z)
                _shakeOffset = new Vector3(
                    Random.Range(-1f, 1f) * currentIntensity,
                    Random.Range(-1f, 1f) * currentIntensity,
                    Random.Range(-0.3f, 0.3f) * currentIntensity
                );

                yield return null;
            }

            _shakeOffset = Vector3.zero;
            _isShaking = false;
            _shakeCoroutine = null;
        }

        #endregion

        #region Smooth Camera Move

        /// <summary>Smoothly move camera to a target position over duration seconds. Saves current position for MoveBack.</summary>
        public void MoveToTarget(Vector3 targetPosition, float duration = 0.5f)
        {
            if (MainCamera == null) return;

            _savedPosition = _expectedPosition;
            _enforcePosition = false;

            MainCamera.transform.DOMove(targetPosition, duration).SetEase(Ease.OutQuad);
        }

        /// <summary>Smoothly move camera back to the saved position.</summary>
        public void MoveBack(float duration = 0.5f)
        {
            if (MainCamera == null) return;

            MainCamera.transform.DOMove(_savedPosition, duration).SetEase(Ease.OutQuad)
                .OnComplete(() =>
                {
                    _expectedPosition = _savedPosition;
                    _enforcePosition = true;
                });
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
