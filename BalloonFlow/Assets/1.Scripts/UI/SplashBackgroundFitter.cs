using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 1:1 스플래시 배경을 화면 비율에 맞춰 잘림 없이 fit. AspectRatioFitter 모드를
    /// 화면 종횡비에 따라 런타임에서 전환해 풍선이 가장 많이 보이는 방향을 유지한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class SplashBackgroundFitter : MonoBehaviour
    {
        private const float SOURCE_ASPECT = 1f;

        private RectTransform _rect;
        private AspectRatioFitter _fitter;
        private Vector2 _lastScreenSize;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _fitter = GetComponent<AspectRatioFitter>();
            if (_fitter == null) _fitter = gameObject.AddComponent<AspectRatioFitter>();
            _fitter.aspectRatio = SOURCE_ASPECT;
        }

        private void OnEnable() { ApplyFit(); }
        private void Start() { ApplyFit(); }

        private void Update()
        {
            Vector2 cur = new Vector2(Screen.width, Screen.height);
            if (cur != _lastScreenSize) ApplyFit();
        }

        private void ApplyFit()
        {
            if (_fitter == null) return;
            float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            _fitter.aspectMode = screenAspect <= SOURCE_ASPECT
                ? AspectRatioFitter.AspectMode.WidthControlsHeight
                : AspectRatioFitter.AspectMode.HeightControlsWidth;
            _lastScreenSize = new Vector2(Screen.width, Screen.height);
        }
    }
}
