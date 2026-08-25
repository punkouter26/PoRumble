using PoRumble.Models;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// Drives the ring's lighting across a match: the house lights come down and the key light
    /// tightens as fighters are knocked out, so the last exchange is lit like a title fight
    /// rather than like the opening free-for-all.
    ///
    /// The scene previously had exactly one global Light2D at full intensity and nothing ever
    /// changed it, which made the 2D lighting system an expensive way to draw flat sprites.
    ///
    /// A View: it reads the roster and writes only to lights.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RingAtmosphereView : MonoBehaviour
    {
        [Tooltip("The flat house light. Dimmed as the field thins.")]
        [SerializeField] private Light2D _globalLight;
        [Tooltip("Overhead light over the middle of the ring. Tightens onto the survivors.")]
        [SerializeField] private Light2D _keyLight;
        [Tooltip("Corner lights. Brought up as the crowd closes in on a decision.")]
        [SerializeField] private Light2D[] _rimLights;

        [Header("House light")]
        [SerializeField] private float _openingGlobalIntensity = 0.85f;
        [SerializeField] private float _finalGlobalIntensity = 0.35f;

        [Header("Key light")]
        [SerializeField] private float _openingKeyIntensity = 0.9f;
        [SerializeField] private float _finalKeyIntensity = 2.1f;
        [SerializeField] private float _openingKeyRadius = 26f;
        [SerializeField] private float _finalKeyRadius = 11f;

        [Header("Rim lights")]
        [SerializeField] private float _openingRimIntensity = 0.35f;
        [SerializeField] private float _finalRimIntensity = 1.3f;

        [Tooltip("How quickly the rig follows the drama. Low values drift between knockouts.")]
        [SerializeField] private float _damping = 1.4f;

        private MatchModel _match;
        private float _tension;

        [Inject]
        public void Construct(MatchModel match)
        {
            _match = match;
        }

        private void Start()
        {
            // Snap to the opening state rather than fading up from whatever the scene was
            // authored at, so the first frame of a match already looks right.
            _tension = 0f;
            Apply(0f);
        }

        private void LateUpdate()
        {
            if (_match == null || _match.Boxers.Count == 0)
            {
                return;
            }

            // 0 with a full field, 1 once it is down to the last two.
            int total = Mathf.Max(1, _match.Boxers.Count);
            int alive = Mathf.Max(1, _match.CountAlive());
            float target = 1f - Mathf.InverseLerp(2f, total, alive);

            // Unscaled: the knockout hold slows the world right down, and the lights should
            // keep resolving through the moment they exist to sell.
            _tension = Mathf.Lerp(
                _tension, target, 1f - Mathf.Exp(-_damping * Time.unscaledDeltaTime));

            Apply(_tension);

            if (_keyLight != null)
            {
                _keyLight.transform.position = FollowSurvivors(_keyLight.transform.position);
            }
        }

        private void Apply(float tension)
        {
            if (_globalLight != null)
            {
                _globalLight.intensity = Mathf.Lerp(
                    _openingGlobalIntensity, _finalGlobalIntensity, tension);
            }

            if (_keyLight != null)
            {
                _keyLight.intensity = Mathf.Lerp(_openingKeyIntensity, _finalKeyIntensity, tension);
                _keyLight.pointLightOuterRadius = Mathf.Lerp(
                    _openingKeyRadius, _finalKeyRadius, tension);
                _keyLight.pointLightInnerRadius = _keyLight.pointLightOuterRadius * 0.25f;
            }

            if (_rimLights == null)
            {
                return;
            }

            float rim = Mathf.Lerp(_openingRimIntensity, _finalRimIntensity, tension);

            for (int index = 0; index < _rimLights.Length; index++)
            {
                if (_rimLights[index] != null)
                {
                    _rimLights[index].intensity = rim;
                }
            }
        }

        /// <summary>Drifts the key light toward the middle of whoever is still standing.</summary>
        private Vector3 FollowSurvivors(Vector3 current)
        {
            Vector2 sum = Vector2.zero;
            int count = 0;

            for (int boxerIndex = 0; boxerIndex < _match.Boxers.Count; boxerIndex++)
            {
                BoxerModel boxer = _match.Boxers[boxerIndex];

                if (!boxer.IsAlive.Value)
                {
                    continue;
                }

                sum += boxer.Position;
                count++;
            }

            if (count == 0)
            {
                return current;
            }

            Vector2 centre = sum / count;
            Vector3 target = new(centre.x, centre.y, current.z);

            return Vector3.Lerp(current, target, 1f - Mathf.Exp(-_damping * Time.unscaledDeltaTime));
        }
    }
}
