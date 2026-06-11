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

        /// <summary>Title: Inspector 의 카메라 설정 그대로 사용. UICamera 활성 유지.</summary>
        public void ConfigureTitle()
        {
            ReleaseEnforcement();
            if (UICamera != null) UICamera.gameObject.SetActive(true);
        }

        /// <summary>Lobby: Inspector 의 카메라 설정 그대로 사용. UICamera 만 활성화.</summary>
        public void ConfigureLobby()
        {
            // Inspector 에서 설정한 MainCamera/UICamera 값 (clearFlags/배경/depth/Stack/URP Overlay 등) override 안 함
            ReleaseEnforcement();
            if (UICamera != null) UICamera.gameObject.SetActive(true);
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
            MainCamera.backgroundColor = new Color(0x46 / 255f, 0x4A / 255f, 0x5B / 255f); // #464a5b
            MainCamera.nearClipPlane = -10f;
            MainCamera.farClipPlane = 80f;
            MainCamera.depth = 0;

            // 레이어별 컬링 거리 — 먼 오브젝트 일찍 컬링
            float[] layerCullDist = new float[32];
            for (int i = 0; i < 32; i++) layerCullDist[i] = 80f; // 기본
            layerCullDist[0] = 60f; // Default 레이어 (풍선/다트/홀더) — 60m 넘으면 컬링
            MainCamera.layerCullDistances = layerCullDist;

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

            // 기존 tween 정리 — 중복 호출 시 두 tween 동시 진행으로 인한 jitter 차단.
            MainCamera.transform.DOKill();

            // 이전 enforce 위치가 있으면 그것 저장 (현재 transform.position 은 이전 tween 의 중간 위치일 수 있음).
            _savedPosition = _enforcePosition ? _expectedPosition : MainCamera.transform.position;
            _enforcePosition = false;

            // InOutSine — 끝 부분 deceleration 이 부드러워서 OutQuad 보다 자연스러움.
            // SetUpdate(true): 아이템(부스터) 사용 시 PopupUseItem 이 PauseManager 로 timeScale=0 을
            //   만든 상태에서 카메라를 옮기므로 unscaled time 으로 돌려야 tween 이 진행됨 (안 그러면 폰 빌드에서 멈춤).
            MainCamera.transform.DOMove(targetPosition, duration).SetEase(Ease.InOutSine).SetUpdate(true);
        }

        /// <summary>tween 중간값이 아닌 '안정 위치' — enforce 중이면 확정 위치, 아니면 현재 transform.
        /// 부스터 등에서 복귀 좌표를 직접 보존할 때 사용 (이동 중 캡처로 인한 오염 방지).</summary>
        public Vector3 CurrentStablePosition
            => MainCamera == null ? Vector3.zero
             : (_enforcePosition ? _expectedPosition : MainCamera.transform.position);

        /// <summary>[2026-06-11] 명시 좌표 복귀 — MoveBack 과 동일 동작이지만 _savedPosition 대신
        /// 호출자가 보존한 좌표 사용. MoveToTarget 이 중복 호출되면 _savedPosition 이 이동 중간
        /// 위치로 오염돼 MoveBack 이 원위치로 못 돌아가는 케이스(Hand 카메라 원복 실패)를 우회.</summary>
        public void RestoreTo(Vector3 position, float duration = 0.5f)
        {
            if (MainCamera == null) return;

            MainCamera.transform.DOKill();
            MainCamera.transform.DOMove(position, duration).SetEase(Ease.InOutSine).SetUpdate(true)
                .OnComplete(() =>
                {
                    _expectedPosition = position;
                    MainCamera.transform.position = position;
                    _enforcePosition = true;
                });
        }

        /// <summary>Smoothly move camera back to the saved position.</summary>
        public void MoveBack(float duration = 0.5f)
        {
            if (MainCamera == null) return;

            // 기존 tween 정리.
            MainCamera.transform.DOKill();

            // SetUpdate(true): MoveToTarget 과 대칭 — 일시정지(timeScale=0) 중에도 복귀 tween 이 진행되도록.
            MainCamera.transform.DOMove(_savedPosition, duration).SetEase(Ease.InOutSine).SetUpdate(true)
                .OnComplete(() =>
                {
                    // floating point 미세 오차 보정 — 끝 위치를 정확히 _savedPosition 으로.
                    // 이게 없으면 다음 LateUpdate 의 enforce 가 1 frame 안에 즉시 jump 시켜 덜컹 느낌 발생.
                    _expectedPosition = _savedPosition;
                    MainCamera.transform.position = _savedPosition;
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

            // MainCamera → Stack 에 UICamera 추가. 중복 add 방지 — 매 씬 전환마다 호출되므로 누적되면
            // 같은 UICamera 가 stack 에 N 번 들어가 N 번 렌더링됨 (Profiler UICamera 47% 부하 핵심 원인).
            var _mainData = MainCamera.gameObject.GetComponent(_urpType);
            if (_mainData == null) _mainData = MainCamera.gameObject.AddComponent(_urpType);
            var _stackProp = _urpType.GetProperty("cameraStack");
            if (_stackProp != null)
            {
                var _stack = _stackProp.GetValue(_mainData) as System.Collections.IList;
                if (_stack != null)
                {
                    // 1) 기존 stack 안의 null / destroyed reference 정리
                    for (int i = _stack.Count - 1; i >= 0; i--)
                    {
                        var item = _stack[i] as Object;
                        if (item == null) _stack.RemoveAt(i);
                    }
                    // 2) UICamera 가 이미 stack 안에 있으면 skip (중복 방지)
                    bool already = false;
                    for (int i = 0; i < _stack.Count; i++)
                    {
                        if (ReferenceEquals(_stack[i], UICamera)) { already = true; break; }
                    }
                    if (!already) _stack.Add(UICamera);
                }
            }
        }

        #endregion
    }
}
