using PoRumble.Models;
using UnityEngine;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// The canvas taking the fight: sweat and blood soaking into the ring where the punches
    /// land, live, for the whole match.
    ///
    /// Reads the same grid the results-screen fight map is built from - DamageMapSystem has
    /// been recording every landed punch's position all along, and only the recap ever looked
    /// at it. The two draw the data differently on purpose. The map is a graphic laid on the
    /// picture: unlit, normalised to its hottest cell, shown once. This is part of the ring:
    /// lit, so it dims with the house lights and sits in the key light like the canvas it is
    /// on, and absolute, so a patch darkens as punishment accumulates and never fades back.
    ///
    /// A sprite on the Floor layer between the canvas dressing and the fight map. Its texture
    /// is data rebuilt from the grid, so like the map it is the one kind of sprite that lives
    /// outside the atlas - one draw call, and only once anything has landed.
    ///
    /// Rebuilt a few times a second, never per punch: a ten-way lands several punches a second
    /// and a stain that grew on exactly the frame of each one would buy nothing the eye can
    /// see for a texture upload per hit.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class CanvasStainView : MonoBehaviour
    {
        /// <summary>
        /// Texels per side. Twice the damage grid, so the splotch noise below has room to
        /// break a cell's edge into something that reads as soaked-in rather than as a
        /// blurred square - and small enough that a rebuild is a fraction of a millisecond.
        /// </summary>
        private const int RESOLUTION = 128;

        [Tooltip("Hit points in one grid cell that stain it to roughly two thirds of full " +
                 "strength. Saturating, so every punch after that darkens it a little less.")]
        [SerializeField] private float _damageForFullStain = 6f;

        [Tooltip("Opacity of the darkest stain. Below 1 so the canvas weave still reads " +
                 "through the worst corner of the ring.")]
        [Range(0f, 1f)]
        [SerializeField] private float _maxOpacity = 0.62f;

        [Tooltip("Colour of a light mark: sweat darkening the canvas, no hue.")]
        [SerializeField] private Color _sweatColor = new(0.1f, 0.09f, 0.08f, 1f);

        [Tooltip("Colour of a heavy mark.")]
        [SerializeField] private Color _bloodColor = new(0.36f, 0.03f, 0.04f, 1f);

        [Tooltip("Hit points in a cell at which the stain starts turning from sweat to blood.")]
        [SerializeField] private float _bloodFrom = 4f;

        [Tooltip("Hit points in a cell at which the stain is fully blood.")]
        [SerializeField] private float _bloodFull = 14f;

        [Tooltip("How strongly the splotch noise breaks up the stain's edges. 0 is a smooth " +
                 "blur of the grid, 1 is ragged.")]
        [Range(0f, 1f)]
        [SerializeField] private float _breakup = 0.6f;

        [Tooltip("Seconds between rebuilds while something new has landed.")]
        [SerializeField] private float _refreshSeconds = 0.4f;

        private DamageMapModel _map;
        private SpriteRenderer _renderer;
        private Texture2D _texture;
        private Sprite _sprite;
        private Color32[] _pixels;
        private float[] _noise;
        private Vector2 _spriteExtent;

        private float _refreshTimer;
        private float _builtTotal = -1f;

        [Inject]
        public void Construct(DamageMapModel map)
        {
            _map = map;
        }

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _renderer.enabled = false;
        }

        private void Start()
        {
            if (_map == null)
            {
                return;
            }

            _texture = new Texture2D(RESOLUTION, RESOLUTION, TextureFormat.RGBA32, false)
            {
                name = "CanvasStains",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            _pixels = new Color32[RESOLUTION * RESOLUTION];
            _noise = BuildNoise();
        }

        /// <summary>Unscaled, like the rest of the presentation layer: hitstop must not stall the canvas.</summary>
        private void Update()
        {
            if (_texture == null)
            {
                return;
            }

            _refreshTimer += Time.unscaledDeltaTime;

            if (_refreshTimer < _refreshSeconds)
            {
                return;
            }

            _refreshTimer = 0f;

            // Nothing new has landed, or the grid was cleared for a new match - the total is the
            // one number that moves on both, so it is the whole change check.
            if (Mathf.Approximately(_map.Total, _builtTotal))
            {
                return;
            }

            _builtTotal = _map.Total;

            if (_map.Total <= 0f)
            {
                _renderer.enabled = false;
                return;
            }

            Rebuild();
            _renderer.enabled = true;
        }

        /// <summary>
        /// Samples the damage grid bilinearly at each texel and turns it into a stain.
        ///
        /// Sampled rather than copied cell-for-cell so the stain is continuous across cells -
        /// the damage grid is 64 across a 40-unit ring, and nearest-cell sampling would draw
        /// every stain as a patchwork of squares.
        /// </summary>
        private void Rebuild()
        {
            int grid = DamageMapModel.RESOLUTION;
            float cellsPerTexel = grid / (float)RESOLUTION;

            for (int texelY = 0; texelY < RESOLUTION; texelY++)
            {
                float gridY = (texelY + 0.5f) * cellsPerTexel - 0.5f;

                for (int texelX = 0; texelX < RESOLUTION; texelX++)
                {
                    float gridX = (texelX + 0.5f) * cellsPerTexel - 0.5f;
                    float damage = SampleGrid(gridX, gridY, grid);
                    int index = texelY * RESOLUTION + texelX;

                    if (damage <= 0f)
                    {
                        _pixels[index] = default;
                        continue;
                    }

                    // The noise scales the damage rather than the opacity, so it moves where a
                    // stain's edge falls - soaking further along one thread than the next -
                    // instead of speckling the middle of a stain that is already saturated.
                    float broken = damage * Mathf.Lerp(1f, _noise[index], _breakup);
                    float opacity = CanvasStainMath.Opacity(broken, _damageForFullStain, _maxOpacity);
                    float blood = CanvasStainMath.BloodShare(damage, _bloodFrom, _bloodFull);
                    Color color = Color.Lerp(_sweatColor, _bloodColor, blood);

                    _pixels[index] = new Color32(
                        (byte)(color.r * 255f),
                        (byte)(color.g * 255f),
                        (byte)(color.b * 255f),
                        (byte)(opacity * 255f));
                }
            }

            _texture.SetPixels32(_pixels);
            _texture.Apply(false);

            FitSprite();
        }

        private float SampleGrid(float gridX, float gridY, int grid)
        {
            int x0 = Mathf.FloorToInt(gridX);
            int y0 = Mathf.FloorToInt(gridY);
            float tx = gridX - x0;
            float ty = gridY - y0;

            // At() returns zero off the grid, which is what lets the sample run off the edge.
            float bottom = Mathf.Lerp(_map.At(x0, y0), _map.At(x0 + 1, y0), tx);
            float top = Mathf.Lerp(_map.At(x0, y0 + 1), _map.At(x0 + 1, y0 + 1), tx);
            return Mathf.Lerp(bottom, top, ty);
        }

        /// <summary>
        /// Sizes the sprite to the ring the grid covers, exactly as the fight map does, so the
        /// two line up texel for cell and the recap lands on the stains it describes.
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

            float pixelsPerUnit = RESOLUTION / (2f * extent.x);

            _sprite = Sprite.Create(
                _texture,
                new Rect(0f, 0f, RESOLUTION, RESOLUTION),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit);

            _sprite.name = "CanvasStains";
            _spriteExtent = extent;
            _renderer.sprite = _sprite;
            transform.localScale = new Vector3(1f, extent.y / extent.x, 1f);
        }

        /// <summary>
        /// Two octaves of value noise in 0.35..1.35, built once. Deterministic, from a local
        /// xorshift rather than UnityEngine.Random, for the reason every presentation jitter
        /// here is: drawing a texture must not advance a sequence the scripted brains read.
        /// </summary>
        private static float[] BuildNoise()
        {
            const int COARSE = 16;
            const int FINE = 48;

            uint state = 0x7F4A7C15;
            float[] coarse = new float[(COARSE + 1) * (COARSE + 1)];
            float[] fine = new float[(FINE + 1) * (FINE + 1)];

            for (int index = 0; index < coarse.Length; index++)
            {
                coarse[index] = NextUnit(ref state);
            }

            for (int index = 0; index < fine.Length; index++)
            {
                fine[index] = NextUnit(ref state);
            }

            float[] noise = new float[RESOLUTION * RESOLUTION];

            for (int texelY = 0; texelY < RESOLUTION; texelY++)
            {
                for (int texelX = 0; texelX < RESOLUTION; texelX++)
                {
                    float u = texelX / (float)RESOLUTION;
                    float v = texelY / (float)RESOLUTION;

                    float value = 0.65f * Lattice(coarse, COARSE, u, v)
                                  + 0.35f * Lattice(fine, FINE, u, v);

                    noise[texelY * RESOLUTION + texelX] = 0.35f + value;
                }
            }

            return noise;
        }

        private static float Lattice(float[] lattice, int size, float u, float v)
        {
            float x = u * size;
            float y = v * size;
            int x0 = Mathf.Min(size - 1, (int)x);
            int y0 = Mathf.Min(size - 1, (int)y);
            float tx = Mathf.SmoothStep(0f, 1f, x - x0);
            float ty = Mathf.SmoothStep(0f, 1f, y - y0);
            int stride = size + 1;

            float bottom = Mathf.Lerp(lattice[y0 * stride + x0], lattice[y0 * stride + x0 + 1], tx);
            float top = Mathf.Lerp(lattice[(y0 + 1) * stride + x0], lattice[(y0 + 1) * stride + x0 + 1], tx);
            return Mathf.Lerp(bottom, top, ty);
        }

        /// <summary>A local xorshift, in 0..1.</summary>
        private static float NextUnit(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xFFFFFF) / (float)0xFFFFFF;
        }

        private void OnDestroy()
        {
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
