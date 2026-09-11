using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PoRumble.Views
{
    /// <summary>
    /// A pool of short-lived <see cref="Light2D"/> flashes fired at impact points.
    ///
    /// Every sprite in the game uses Sprite-Lit-Default, so they already respond to 2D lights -
    /// the scene simply had one flat global light and nothing ever changed. A punch that briefly
    /// lights the fighters around it is the cheapest way to make a hit feel like it happened in
    /// the world rather than on top of it.
    ///
    /// Lights are created once and re-aimed. Creating one per punch would allocate, and URP
    /// caps the number of 2D light render textures anyway.
    /// </summary>
    internal sealed class ImpactLightPool
    {
        private readonly Light2D[] _lights;
        private readonly float[] _remaining;
        private readonly float[] _duration;
        private readonly float[] _peak;

        private int _next;

        /// <summary>
        /// Builds the pool, adopting lights already authored in the scene when there are any.
        ///
        /// Adoption is the preferred path: a light that exists in the scene can be selected,
        /// re-coloured and re-ranged without entering Play mode, and its sorting-layer list is
        /// visible rather than whatever AddComponent happened to default to - which matters,
        /// because a Light2D silently fails to light any sorting layer missing from that list.
        ///
        /// When lights are adopted the pool size comes from the array rather than from the
        /// count field, so the two cannot disagree. A count of twelve against eight authored
        /// lights would otherwise leave four null entries that only fail on the ninth punch.
        /// </summary>
        internal ImpactLightPool(Transform parent, int count, float falloff, Light2D[] preplaced)
        {
            bool adopt = preplaced != null && preplaced.Length > 0;
            int size = adopt ? preplaced.Length : Mathf.Max(1, count);
            _lights = new Light2D[size];
            _remaining = new float[size];
            _duration = new float[size];
            _peak = new float[size];

            for (int index = 0; index < size; index++)
            {
                Light2D light;

                if (adopt)
                {
                    light = preplaced[index];

                    if (light == null)
                    {
                        continue;
                    }
                }
                else
                {
                    GameObject host = new($"ImpactLight_{index:00}");
                    host.transform.SetParent(parent, false);

                    light = host.AddComponent<Light2D>();
                    light.lightType = Light2D.LightType.Point;
                    light.pointLightInnerRadius = 0.1f;
                    light.pointLightOuterRadius = 3f;
                    light.falloffIntensity = falloff;
                }

                light.intensity = 0f;
                light.shadowsEnabled = false;

                _lights[index] = light;
                light.gameObject.SetActive(false);
            }
        }

        /// <summary>Fires a flash at a world position.</summary>
        internal void Flash(Vector2 position, Color color, float intensity, float radius, float seconds)
        {
            int index = _next;
            _next = (_next + 1) % _lights.Length;

            Light2D light = _lights[index];
            light.transform.position = new Vector3(position.x, position.y, 0f);
            light.color = color;
            light.pointLightOuterRadius = radius;
            light.intensity = intensity;
            light.gameObject.SetActive(true);

            _peak[index] = intensity;
            _duration[index] = Mathf.Max(0.01f, seconds);
            _remaining[index] = _duration[index];
        }

        /// <summary>
        /// Fades every live flash. Driven on unscaled time so a flash still resolves at a
        /// sensible rate during hitstop and the knockout hold, both of which slow the world
        /// right down at exactly the moment a punch has just landed.
        /// </summary>
        internal void Tick(float unscaledDeltaTime)
        {
            for (int index = 0; index < _lights.Length; index++)
            {
                if (_remaining[index] <= 0f)
                {
                    continue;
                }

                _remaining[index] -= unscaledDeltaTime;

                if (_remaining[index] <= 0f)
                {
                    _lights[index].intensity = 0f;
                    _lights[index].gameObject.SetActive(false);
                    continue;
                }

                // Square the fall-off so the flash reads as a spark rather than a fade-out.
                float t = _remaining[index] / _duration[index];
                _lights[index].intensity = _peak[index] * t * t;
            }
        }
    }
}
