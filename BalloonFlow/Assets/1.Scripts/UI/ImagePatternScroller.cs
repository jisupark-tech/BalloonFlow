using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 타일 패턴 이미지를 지정 방향으로 천천히 흘려보내는 컴포넌트.
    /// ShopListAd.prefab 의 ImagePattern 용도.
    ///
    /// 자동 폴백:
    /// - RawImage 발견 → uvRect 이동 (texture wrapMode=Repeat 전제)
    /// - Image(+ tiled/sliced) 발견 → material mainTextureOffset 이동
    /// - 위 둘 다 없으면 → RectTransform anchoredPosition 이동 (부모 마스크 필요)
    /// </summary>
    [ExecuteAlways]
    public class ImagePatternScroller : MonoBehaviour
    {
        [Tooltip("초당 스크롤 속도 (UV 단위). 우상→좌하 = (-, -)")]
        [SerializeField] private Vector2 _speed = new Vector2(-0.05f, -0.05f);

        [Tooltip("RectTransform 모드에서 위치가 이 크기만큼 쌓이면 리셋 (타일 크기 픽셀).")]
        [SerializeField] private Vector2 _tileSize = new Vector2(256f, 256f);

        private RawImage _rawImage;
        private Image _image;
        private Material _runtimeMat;
        private RectTransform _rectTransform;
        private Vector2 _baseAnchored;
        private Vector2 _accumulatedOffset;

        private void Awake()
        {
            _rawImage = GetComponent<RawImage>();
            _image = GetComponent<Image>();
            _rectTransform = GetComponent<RectTransform>();

            if (_rectTransform != null)
                _baseAnchored = _rectTransform.anchoredPosition;

            // Image인 경우 공유 머티리얼을 복제해서 개별 오프셋 변경 가능하게.
            if (_rawImage == null && _image != null && Application.isPlaying)
            {
                if (_image.material != null && _image.material != _image.defaultMaterial)
                {
                    _runtimeMat = new Material(_image.material);
                    _image.material = _runtimeMat;
                }
            }
        }

        private void OnEnable()
        {
            if (_rectTransform != null)
                _baseAnchored = _rectTransform.anchoredPosition;
            _accumulatedOffset = Vector2.zero;
        }

        private void Update()
        {
            float dt = Application.isPlaying ? Time.deltaTime : 0f;
            if (dt <= 0f) return;

            Vector2 delta = _speed * dt;

            // Priority 1: RawImage uvRect (깔끔함, wrapMode=Repeat 전제)
            if (_rawImage != null)
            {
                Rect uv = _rawImage.uvRect;
                uv.x += delta.x;
                uv.y += delta.y;
                uv.x -= Mathf.Floor(uv.x);
                uv.y -= Mathf.Floor(uv.y);
                _rawImage.uvRect = uv;
                return;
            }

            // Priority 2: Image material offset (Tiled sprite 전제)
            if (_image != null && _runtimeMat != null)
            {
                _accumulatedOffset += delta;
                _accumulatedOffset.x -= Mathf.Floor(_accumulatedOffset.x);
                _accumulatedOffset.y -= Mathf.Floor(_accumulatedOffset.y);
                _runtimeMat.mainTextureOffset = _accumulatedOffset;
                return;
            }

            // Priority 3: RectTransform move (부모 마스크 + tile 크기 기준 wrap)
            if (_rectTransform != null)
            {
                // _speed를 UV→픽셀로 변환 (타일 크기 기준)
                Vector2 pixelDelta = new Vector2(delta.x * _tileSize.x, delta.y * _tileSize.y);
                Vector2 pos = _rectTransform.anchoredPosition;
                pos += pixelDelta;
                if (_tileSize.x > 0f)
                {
                    float dx = pos.x - _baseAnchored.x;
                    while (dx <= -_tileSize.x) { pos.x += _tileSize.x; dx += _tileSize.x; }
                    while (dx >= _tileSize.x)  { pos.x -= _tileSize.x; dx -= _tileSize.x; }
                }
                if (_tileSize.y > 0f)
                {
                    float dy = pos.y - _baseAnchored.y;
                    while (dy <= -_tileSize.y) { pos.y += _tileSize.y; dy += _tileSize.y; }
                    while (dy >= _tileSize.y)  { pos.y -= _tileSize.y; dy -= _tileSize.y; }
                }
                _rectTransform.anchoredPosition = pos;
            }
        }

        private void OnDestroy()
        {
            if (_runtimeMat != null)
            {
                if (_image != null && _image.material == _runtimeMat)
                    _image.material = null;
                if (Application.isPlaying) Destroy(_runtimeMat);
                else DestroyImmediate(_runtimeMat);
            }
        }
    }
}
