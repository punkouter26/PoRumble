using PoRumble.Models;
using UnityEngine;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// The fight map: after the final bell, the canvas lights up where the damage was done.
    ///
    /// A broadcast recap graphic for a free-for-all, where the story of the match is as much
    /// "where" as "who" - the corner a fighter got trapped in, the centre-ring brawl that took
    /// three of them out. Shown on the results screen only, when the director has pulled
    /// wide and the whole ring is in frame; during the fight it would paint over the canvas
    /// the fighters are being read against.
    ///
    /// A sprite over the ring rather than a UI panel, so it sits exactly on the canvas at any
    /// zoom and under the fighters and the ropes. Drawn with an unlit material: it is a
    /// graphic laid on the picture, and the house lights dimming around a knockout should not
    /// dim the recap of it.
    ///
    /// The texture is built at runtime because it is the data; the object carrying it is a
    /// scene object like every other piece of the ring, so it can be moved or re-layered in
    /// the editor.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class DamageMapView : MonoBehaviour
    {
        [Tooltip("Colour for a cell by its share of the hottest cell, 0 cold to 1 hottest. " +
                 "Alpha is part of it: cold cells should be fully clear so the canvas shows.")]
        [SerializeField] private Gradient _ramp = DefaultRamp();

        [Tooltip("Below 1 lifts the quieter cells so a match with one enormous exchange still " +
                 "shows where the rest of the fighting happened.")]
        [Range(0.2f, 1f)]
        [SerializeField] private float _gamma = 0.55f;

        [Tooltip("Seconds the map takes to come up once the results are in.")]
        [SerializeField] private float _fadeSeconds = 0.7f;

        private DamageMapModel _map;
        private MatchFlowModel _flow;
        private SpriteRenderer _renderer;
        private Texture2D _texture;
        private Sprite _sprite;
        private Color32[] _pixels;
        private Vector2 _spriteExtent;

        private readonly CompositeDisposable _disposables = new();

        private float _fade;
        private bool _showing;

        [Inject]
        public void Construct(DamageMapModel map, MatchFlowModel flow)
        {
            _map = map;
            _flow = flow;
        }

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _renderer.enabled = false;
        }

        private void Start()
        {
            if (_map == null || _flow == null)
            {
                return;
            }

            _texture = new Texture2D(DamageMapModel.RESOLUTION, DamageMapModel.RESOLUTION, TextureFormat.RGBA32, false)
            {
                name = "DamageMap",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            _pixels = new Color32[DamageMapModel.RESOLUTION * DamageMapModel.RESOLUTION];

            _flow.Phase.Subscribe(OnFlowPhaseChanged).AddTo(_disposables);
        }

        /// <summary>Fades on unscaled time: the results follow a slowed-down knockout hold.</summary>
        private void Update()
        {
            if (!_showing || _fade >= 1f)
            {
                return;
            }

            _fade = _fadeSeconds > 0f ? Mathf.Min(1f, _fade + Time.unscaledDeltaTime / _fadeSeconds) : 1f;
            _renderer.color = new Color(1f, 1f, 1f, _fade);
        }

        private void OnFlowPhaseChanged(MatchFlowPhase phase)
        {
            bool show = phase == MatchFlowPhase.Results && _map.Total > 0f;

            if (show && !_showing)
            {
                Rebuild();
                _fade = 0f;
                _renderer.color = new Color(1f, 1f, 1f, 0f);
            }

            _showing = show;
            _renderer.enabled = show;
        }

        /// <summary>Paints the grid into the texture, once per results screen.</summary>
        private void Rebuild()
        {
            float peak = Mathf.Max(0.0001f, _map.Peak);
            int resolution = DamageMapModel.RESOLUTION;

            for (int cellY = 0; cellY < resolution; cellY++)
            {
                for (int cellX = 0; cellX < resolution; cellX++)
                {
                    float heat = Mathf.Pow(Mathf.Clamp01(_map.At(cellX, cellY) / peak), _gamma);
                    _pixels[cellY * resolution + cellX] = _ramp.Evaluate(heat);
                }
            }

            _texture.SetPixels32(_pixels);
            _texture.Apply(false);

            FitSprite();
        }

        /// <summary>
        /// Sizes the sprite to the ring the grid covers. A sprite's world size is its pixel
        /// size over its pixels-per-unit, so the grid's width over the ring's width makes the
        /// map exactly as wide as the canvas; a non-square ring gets the difference as a
        /// vertical scale. Recreated only when the ring's size changes.
        /// </summary>
        private void FitSprite()
        {
            Vector2 extent = _map.HalfExtent;

            if (_sprite != null && extent == _spriteExtent)
            {
                return;
            }

            if (_sprite != null)
            {
                Destroy(_sprite);
            }

            int resolution = DamageMapModel.RESOLUTION;
            float pixelsPerUnit = resolution / (2f * extent.x);

            _sprite = Sprite.Create(
                _texture,
                new Rect(0f, 0f, resolution, resolution),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit);

            _sprite.name = "DamageMap";
            _spriteExtent = extent;
            _renderer.sprite = _sprite;
            transform.localScale = new Vector3(1f, extent.y / extent.x, 1f);
        }

        /// <summary>
        /// Clear through warm to white-hot. Deep red before orange so the edge of a hot zone
        /// reads as a bruise on the canvas rather than as a yellow halo.
        /// </summary>
        private static Gradient DefaultRamp()
        {
            Gradient gradient = new();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.55f, 0.04f, 0.06f), 0f),
                    new GradientColorKey(new Color(0.85f, 0.12f, 0.08f), 0.35f),
                    new GradientColorKey(new Color(1f, 0.55f, 0.12f), 0.7f),
                    new GradientColorKey(new Color(1f, 0.95f, 0.75f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0f, 0.04f),
                    new GradientAlphaKey(0.55f, 0.35f),
                    new GradientAlphaKey(0.8f, 1f)
                });
            return gradient;
        }

        private void OnDestroy()
        {
            _disposables.Dispose();

            if (_sprite != null)
            {
                Destroy(_sprite);
            }

            if (_texture != null)
            {
                Destroy(_texture);
            }
        }
    }
}
