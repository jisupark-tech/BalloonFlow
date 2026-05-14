using UnityEngine;
using UnityEngine.UI;

namespace AimedPuzzle.BalloonFlow.UI
{
    /// <summary>RawImage uvRect를 X/Y로 스크롤하면서 CanvasScaler referenceResolution 대비 실제 rect 크기 비율로 uvRect.W/H를 자동 보정한다.</summary>
    /// <remarks>Per-instance MonoBehaviour; intentionally NOT a singleton — multiple RawImage instances each own their own scroller.</remarks>
    [DisallowMultipleComponent]
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

        private void RecalculateUvSize()
        {
            if (rawImage == null || rawImage.texture == null) return;

            Vector2 rectSize = _rawImageRT.rect.size;
            if (rectSize.x <= 0f || rectSize.y <= 0f) return;

            Vector2 refRes = GetReferenceResolution();
            if (refRes.x <= 0f || refRes.y <= 0f) return;

            var tex = rawImage.texture;
            if (tex.width <= 0 || tex.height <= 0) return;

            uvRect.width = _baseUvSize.x * (rectSize.x / refRes.x);

            // 한 타일의 화면 표시 aspect == 텍스처 원본 aspect 가 되도록 uv.h를 uv.w로부터 derive.
            // 결과: 화면 비율 변화 시 타일은 stretch 되지 않고 반복 개수만 증가.
            uvRect.height = uvRect.width * ((float)tex.width * rectSize.y) / ((float)tex.height * rectSize.x);

            rawImage.uvRect = uvRect;
            _lastTexId = tex.GetInstanceID();
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
