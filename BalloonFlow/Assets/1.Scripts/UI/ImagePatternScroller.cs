using UnityEngine;
using UnityEngine.UI;

namespace AimedPuzzle.BalloonFlow.UI
{
    // Per-instance background scroller component (not a singleton — multiple RawImages may have their own).
    /// <summary>RawImage uvRect를 X/Y로 스크롤하면서 CanvasScaler referenceResolution 대비 실제 rect 크기 비율로 uvRect.W/H를 자동 보정한다.</summary>
    public class ImagePatternScroller : MonoBehaviour
    {
        [SerializeField] private RawImage rawImage;
        [SerializeField] private Vector2 speed = new Vector2(-0.05f, -0.05f);

        private Rect uvRect;
        private RectTransform _rawImageRT;
        private Vector2 _baseUvSize;
        private int _lastTexId;

        private void Awake()
        {
            if (rawImage == null)
                rawImage = GetComponent<RawImage>();

            _rawImageRT = rawImage.rectTransform;
            _baseUvSize = rawImage.uvRect.size;
            uvRect = rawImage.uvRect;

            RecalculateUvSize();
        }

        // 세로 reference(예: 1242x2688)에서는 _baseUvSize 그대로 사용.
        // 화면 rect가 reference보다 커지면 같은 픽셀 크기의 패턴이 더 많이 반복되도록 uv 범위만 비례 확장.
        private void RecalculateUvSize()
        {
            if (rawImage == null || rawImage.texture == null) return;

            Vector2 rectSize = _rawImageRT.rect.size;
            if (rectSize.x <= 0f || rectSize.y <= 0f) return;

            Vector2 refRes = GetReferenceResolution();
            if (refRes.x <= 0f || refRes.y <= 0f) return;

            uvRect.width  = _baseUvSize.x * (rectSize.x / refRes.x);
            uvRect.height = _baseUvSize.y * (rectSize.y / refRes.y);

            rawImage.uvRect = uvRect;

            _lastTexId = rawImage.texture.GetInstanceID();
        }

        private Vector2 GetReferenceResolution()
        {
            var scaler = GetComponentInParent<CanvasScaler>();
            if (scaler != null && scaler.referenceResolution.x > 0f && scaler.referenceResolution.y > 0f)
                return scaler.referenceResolution;
            return new Vector2(1242f, 2688f);
        }

        private void OnRectTransformDimensionsChange()
        {
            if (_rawImageRT == null) return;
            RecalculateUvSize();
        }

        private void Update()
        {
            if (rawImage.texture != null && rawImage.texture.GetInstanceID() != _lastTexId)
                RecalculateUvSize();

            uvRect.position += speed * Time.deltaTime;
            rawImage.uvRect = uvRect;
        }
    }
}
