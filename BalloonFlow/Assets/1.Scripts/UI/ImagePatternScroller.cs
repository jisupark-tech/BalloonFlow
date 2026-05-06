using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// 타일 패턴 이미지를 지정 방향으로 천천히 흘려보내는 컴포넌트.
    /// ShopListAd.prefab 의 ImagePattern 용도.
    /// 전용 Material 인스턴스의 mainTextureOffset(UV)만 갱신한다.
    /// </summary>
    [ExecuteAlways]
    public class ImagePatternScroller : MonoBehaviour
    {
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        [Tooltip("초당 스크롤 속도 (UV/sec). 좌하 = (-, -)")]
        [SerializeField] private Vector2 _speed = new Vector2(-0.05f, -0.05f);

        private Image _image;
        private Material _runtimeMat;
        private Vector2 _uvOffset;

        private void Awake()
        {
            _image = GetComponent<Image>();
            if (_image == null)
            {
                Debug.LogWarning($"[ImagePatternScroller] Image 컴포넌트를 찾지 못했습니다. ({name})", this);
                enabled = false;
                return;
            }

            if (Application.isPlaying)
            {
                EnsureRuntimeMaterial();
            }
        }

        private void EnsureRuntimeMaterial()
        {
            if (_runtimeMat != null || _image == null) return;

            Material sourceMat = _image.material != null ? _image.material : _image.defaultMaterial;
            if (sourceMat == null) return;

            // sharedMaterial을 직접 수정하면 동일 머티리얼을 쓰는 다른 UI까지 영향 → 인스턴스를 만들어 _image.material에 대입.
            _runtimeMat = new Material(sourceMat) { name = sourceMat.name + " (PatternRuntime)" };
            _image.material = _runtimeMat;
            _uvOffset = _runtimeMat.GetTextureOffset(MainTexId);
        }

        private void Update()
        {
            if (!Application.isPlaying || _runtimeMat == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            _uvOffset.x += _speed.x * dt;
            _uvOffset.y += _speed.y * dt;
            _uvOffset.x -= Mathf.Floor(_uvOffset.x);
            _uvOffset.y -= Mathf.Floor(_uvOffset.y);
            _runtimeMat.SetTextureOffset(MainTexId, _uvOffset);
        }

        private void OnDisable()
        {
            ReleaseRuntimeMaterial();
        }

        private void OnDestroy()
        {
            ReleaseRuntimeMaterial();
        }

        private void ReleaseRuntimeMaterial()
        {
            if (_runtimeMat == null) return;

            if (_image != null && _image.material == _runtimeMat)
                _image.material = null;

            if (Application.isPlaying) Destroy(_runtimeMat);
            else DestroyImmediate(_runtimeMat);

            _runtimeMat = null;
        }
    }
}
