using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>RawImage uvRect를 X/Y로 스크롤하면서 화면/텍스처 비율에 맞춰 uvRect.W/H를 자동 보정한다.</summary>
    public class ImagePatternScroller : MonoBehaviour
    {
        [SerializeField] private RawImage rawImage;
        [SerializeField] private Vector2 speed = new Vector2(-0.05f, -0.05f);

        private Rect uvRect;
        private RectTransform _rawImageRT;
        private Vector2 _baseUvSize;
        private Vector2 _lastRectSize;
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

        // 픽셀당 텍셀 동일 조건: rect.x/(uv.w·tex.w) = rect.y/(uv.h·tex.h)
        // → uv.h = uv.w · (texAspect / rawAspect), _baseUvSize.x를 X anchor로 보존.
        private void RecalculateUvSize()
        {
            if (rawImage == null || rawImage.texture == null) return;

            Vector2 rectSize = _rawImageRT.rect.size;
            if (rectSize.x <= 0f || rectSize.y <= 0f) return;

            float rawAspect = rectSize.x / rectSize.y;
            var tex = rawImage.texture;
            float texAspect = (float)tex.width / tex.height;

            uvRect.width  = _baseUvSize.x;
            uvRect.height = _baseUvSize.x * (texAspect / rawAspect);

            rawImage.uvRect = uvRect;

            _lastRectSize = rectSize;
            _lastTexId = tex.GetInstanceID();
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
