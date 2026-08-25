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

        internal ImpactLightPool(Transform parent, int count, float falloff)
        {
            int size = Mathf.Max(1, count);
            _lights = new Light2D[size];
            _remaining = new float[size];
            _duration = new float[size];
            _peak = new float[size];

            for (int index = 0; index < size; index++)
            {
                GameObject host = new($"ImpactLight_{index:00}");
                host.transform.SetParent(parent, false);

                Light2D light = host.AddComponent<Light2D>();
                light.lightType = Light2D.LightType.Point;
                light.pointLightInnerRadius = 0.1f;
                light.pointLightOuterRadius = 3f;
                light.falloffIntensity = falloff;
                light.intensity = 0f;
                light.shadowsEnabled = false;

                _lights[index] = light;
                host.SetActive(false);
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
