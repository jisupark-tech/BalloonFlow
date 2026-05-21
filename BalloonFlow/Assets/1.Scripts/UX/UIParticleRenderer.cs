using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace BalloonFlow
{
    /// <summary>
    /// Bakes a ParticleSystemRenderer into a CanvasRenderer so particle effects can be shown
    /// on Screen Space - Overlay canvases.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    [RequireComponent(typeof(CanvasRenderer))]
    public class UIParticleRenderer : MaskableGraphic
    {
        [Tooltip("Particle local mesh scale for UI pixels. 100 means 1 particle world unit = 100 UI pixels.")]
        [SerializeField] private float _meshScale = 100f;

        private ParticleSystem _ps;
        private ParticleSystemRenderer _psr;
        private Mesh _bakedMesh;
        private float _defaultMeshScale;
        private readonly List<Vector3> _vertices = new List<Vector3>(512);
        private readonly List<Color32> _colors = new List<Color32>(512);
        private readonly List<Vector2> _uvs = new List<Vector2>(512);
        private readonly List<int> _triangles = new List<int>(1024);
        private readonly List<UIVertex> _uiVertices = new List<UIVertex>(512);
        private Texture _textureSheetSpriteTexture;
        private bool _spriteTextureResolved;
        private bool _warnedMultipleSpriteTextures;

        protected override void Awake()
        {
            base.Awake();

            _ps = GetComponent<ParticleSystem>();
            _psr = GetComponent<ParticleSystemRenderer>();
            _bakedMesh = new Mesh();
            _defaultMeshScale = _meshScale;

            raycastTarget = false;
            maskable = false;
            canvasRenderer.cullTransparentMesh = false;

            if (_psr != null)
            {
                if (_psr.sharedMaterial != null)
                    material = _psr.sharedMaterial;

                // ROLLBACK_UI_PARTICLE_HIDE_RAW_RENDERER
                // Raw ParticleSystemRenderer does not sort with Overlay UI, so CanvasRenderer owns the draw.
                _psr.enabled = false;
            }

            SetMaterialDirty();

            // ROLLBACK_UI_PARTICLE_DISABLE_CHILD_RENDERERS
            // Do not disable child particle renderers here. Each particle object owns its own
            // UIParticleRenderer, and disabling children can hide nested PopupResult/FXGold effects.
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (_bakedMesh != null)
                DestroyImmediate(_bakedMesh);
        }

        public void SetMeshScale(float scale)
        {
            _meshScale = Mathf.Max(0.01f, scale);
            SetVerticesDirty();
        }

        public void ResetMeshScale()
        {
            SetMeshScale(_defaultMeshScale > 0f ? _defaultMeshScale : _meshScale);
        }

        private void LateUpdate()
        {
            if (_ps == null || _psr == null)
                return;

            // ROLLBACK_UI_PARTICLE_REBUILD_THROUGH_GRAPHIC
            // CanvasRenderer.SetMesh from LateUpdate can be overwritten by the Graphic rebuild
            // that runs just before UI rendering. Marking vertices dirty makes Unity call
            // OnPopulateMesh at the correct point, so PopupResult FX and FXGold are not cleared.
            if (_ps.isPlaying || _ps.particleCount > 0)
                SetVerticesDirty();
        }

        public override void Cull(Rect clipRect, bool validRect)
        {
            // ROLLBACK_UI_PARTICLE_DISABLE_RECT_CULL
            // PopupResult reward FX sits inside nested popup UI. RectMask/Graphic culling can
            // hide baked particles even when the raw ParticleSystemRenderer is visible outside
            // the canvas hierarchy, so UI particles opt out of this cull path.
            canvasRenderer.cull = false;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            RebuildParticleMesh(vh);
        }

        private void RebuildParticleMesh(VertexHelper vh)
        {
            if (_ps == null || _psr == null || _ps.particleCount == 0)
                return;

            bool restoreEnabled = _psr.enabled;
            if (!restoreEnabled)
                _psr.enabled = true;

            _psr.BakeMesh(_bakedMesh, useTransform: false);

            if (!restoreEnabled)
                _psr.enabled = false;

            if (_bakedMesh.vertexCount == 0)
                return;

            _bakedMesh.GetVertices(_vertices);
            _bakedMesh.GetColors(_colors);
            _bakedMesh.GetUVs(0, _uvs);
            _bakedMesh.GetTriangles(_triangles, 0);

            if (_vertices.Count == 0 || _triangles.Count == 0)
                return;

            if (_colors.Count != _vertices.Count)
            {
                _colors.Clear();
                for (int i = 0; i < _vertices.Count; i++)
                    _colors.Add(Color.white);
            }

            if (_uvs.Count != _vertices.Count)
            {
                _uvs.Clear();
                for (int i = 0; i < _vertices.Count; i++)
                    _uvs.Add(Vector2.zero);
            }

            float scale = _meshScale;
            _uiVertices.Clear();
            for (int i = 0; i < _vertices.Count; i++)
            {
                UIVertex vertex = UIVertex.simpleVert;
                Vector3 pos = _vertices[i];
                vertex.position = new Vector3(pos.x * scale, pos.y * scale, 0f);
                vertex.color = _colors[i];
                vertex.uv0 = _uvs[i];
                _uiVertices.Add(vertex);
            }

            vh.AddUIVertexStream(_uiVertices, _triangles);
        }

        public override Texture mainTexture
        {
            get
            {
                Texture spriteTexture = ResolveTextureSheetSpriteTexture();
                if (spriteTexture != null)
                    return spriteTexture;

                if (_psr != null && _psr.sharedMaterial != null && _psr.sharedMaterial.mainTexture != null)
                    return _psr.sharedMaterial.mainTexture;
                return Texture2D.whiteTexture;
            }
        }

        public override Material materialForRendering
        {
            get
            {
                if (_psr != null && _psr.sharedMaterial != null)
                    return _psr.sharedMaterial;
                return base.materialForRendering;
            }
        }

        private Texture ResolveTextureSheetSpriteTexture()
        {
            if (_spriteTextureResolved)
                return _textureSheetSpriteTexture;

            _spriteTextureResolved = true;
            _textureSheetSpriteTexture = null;

            if (_ps == null)
                return null;

            var sheet = _ps.textureSheetAnimation;
            if (!sheet.enabled || sheet.mode != ParticleSystemAnimationMode.Sprites)
                return null;

            int spriteCount = sheet.spriteCount;
            for (int i = 0; i < spriteCount; i++)
            {
                Sprite sprite = sheet.GetSprite(i);
                if (sprite == null || sprite.texture == null)
                    continue;

                if (_textureSheetSpriteTexture == null)
                {
                    _textureSheetSpriteTexture = sprite.texture;
                    continue;
                }

                if (_textureSheetSpriteTexture != sprite.texture && !_warnedMultipleSpriteTextures)
                {
                    // ROLLBACK_UI_PARTICLE_SPRITES_MODE_TEXTURE
                    // A single MaskableGraphic can submit one texture. Sprites mode is supported
                    // when the animation sprites share one atlas/texture; mixed textures need
                    // separate particle objects/materials.
                    _warnedMultipleSpriteTextures = true;
                    Debug.LogWarning(
                        $"[UIParticleRenderer] '{name}' uses TextureSheetAnimation Sprites with multiple textures. " +
                        "Only the first texture can be rendered by one UI particle graphic.");
                }
            }

            return _textureSheetSpriteTexture;
        }
    }
}
