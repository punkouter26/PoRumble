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

        [Header("Follow spot")]
        [Tooltip("Optional. A single light that tracks whoever the camera director is watching, " +
                 "so the fight in frame is the fight that is lit.")]
        [SerializeField] private Light2D _followSpot;
        [SerializeField] private float _followSpotIntensity = 1.5f;
        [SerializeField] private float _followSpotRadius = 6.5f;
        [Tooltip("How quickly the spot crosses the ring to a new focus. Faster than the rest " +
                 "of the rig because the camera has already cut - a spot still drifting over " +
                 "from the last pair reads as a lighting fault rather than as a follow.")]
        [SerializeField] private float _followSpotDamping = 6f;

        [Header("Knockout")]
        [Tooltip("What the house light falls to while the knockout is held. The hold is the " +
                 "one moment the game deliberately stops, and dropping the room around the " +
                 "fighter who just went down is what makes it read as an ending rather than " +
                 "as a pause.")]
        [Range(0f, 1f)]
        [SerializeField] private float _knockoutGlobalIntensity = 0.06f;
        [Tooltip("How hard the key light drives during the hold, against its ordinary final " +
                 "intensity.")]
        [SerializeField] private float _knockoutKeyMultiplier = 1.6f;
        [Tooltip("How quickly the blackout closes in. Deliberately quick: the hold only lasts " +
                 "1.6 seconds and a slow fade would still be arriving when it ended.")]
        [SerializeField] private float _knockoutDamping = 7f;

        private MatchModel _match;
        private MatchFlowModel _flow;
        private DirectorModel _director;

        private float _tension;

        /// <summary>
        /// 0 normally, 1 while the knockout is being held. Eased rather than switched so the
        /// lights fall and come back rather than cutting, which at this speed is the difference
        /// between a blackout and a dropped frame.
        /// </summary>
        private float _blackout;

        [Inject]
        public void Construct(MatchModel match, MatchFlowModel flow, DirectorModel director)
        {
            _match = match;
            _flow = flow;
            _director = director;
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

            float delta = Time.unscaledDeltaTime;

            // Unscaled: the knockout hold slows the world right down, and the lights should
            // keep resolving through the moment they exist to sell.
            _tension = Mathf.Lerp(_tension, target, 1f - Mathf.Exp(-_damping * delta));

            bool holding = _flow != null && _flow.Phase.Value == MatchFlowPhase.KnockoutHold;

            _blackout = Mathf.Lerp(
                _blackout, holding ? 1f : 0f, 1f - Mathf.Exp(-_knockoutDamping * delta));

            Apply(_tension);

            if (_keyLight != null)
            {
                _keyLight.transform.position = FollowSurvivors(_keyLight.transform.position);
            }

            TickFollowSpot(delta);
        }

        /// <summary>
        /// Puts the follow spot on whoever the camera director has picked out.
        ///
        /// The director's focus rather than the key light's centre-of-survivors, and the two
        /// deliberately disagree. The key light averages the field, which is right for a
        /// ten-way and says nothing about where the fight is; the director has already decided
        /// which exchange is worth watching and the camera has already cut to it, so lighting
        /// anything else would light the half of the ring nobody is looking at.
        ///
        /// Faded out rather than switched off when there is no focus. A spot that vanished
        /// between pairs would strobe the ring every time the director changed its mind.
        /// </summary>
        private void TickFollowSpot(float delta)
        {
            if (_followSpot == null)
            {
                return;
            }

            BoxerModel focus = _director == null ? null : FindBoxer(_director.FocusId);
            bool lit = focus != null && focus.IsAlive.Value;

            // Blackout drives the spot the other way from the house lights: with the room down
            // it is the only thing still lighting the fighter who just went over.
            float target = lit
                ? _followSpotIntensity * Mathf.Lerp(1f, _knockoutKeyMultiplier, _blackout)
                : 0f;

            _followSpot.intensity = Mathf.Lerp(
                _followSpot.intensity, target, 1f - Mathf.Exp(-_followSpotDamping * delta));

            _followSpot.pointLightOuterRadius = _followSpotRadius;
            _followSpot.pointLightInnerRadius = _followSpotRadius * 0.2f;

            if (!lit)
            {
                return;
            }

            Vector3 current = _followSpot.transform.position;
            Vector3 destination = new(focus.Position.x, focus.Position.y, current.z);

            _followSpot.transform.position = Vector3.Lerp(
                current, destination, 1f - Mathf.Exp(-_followSpotDamping * delta));
        }

        private BoxerModel FindBoxer(int boxerId)
        {
            if (_match == null || boxerId == DirectorModel.NOBODY)
            {
                return null;
            }

            for (int boxerIndex = 0; boxerIndex < _match.Boxers.Count; boxerIndex++)
            {
                if (_match.Boxers[boxerIndex].Id == boxerId)
                {
                    return _match.Boxers[boxerIndex];
                }
            }

            return null;
        }

        /// <summary>
        /// Writes the whole rig for one frame.
        ///
        /// The blackout is applied on top of the tension blend rather than replacing it, so the
        /// hold always darkens from wherever the match had got to. A knockout at the opening
        /// bell and a knockout in the final both drop the room; they do not both drop it to the
        /// same place, because the fight they are ending does not look the same.
        /// </summary>
        private void Apply(float tension)
        {
            if (_globalLight != null)
            {
                float house = Mathf.Lerp(
                    _openingGlobalIntensity, _finalGlobalIntensity, tension);

                _globalLight.intensity = Mathf.Lerp(house, _knockoutGlobalIntensity, _blackout);
            }

            if (_keyLight != null)
            {
                float key = Mathf.Lerp(_openingKeyIntensity, _finalKeyIntensity, tension);

                // The key drives harder as the room falls away. Dropping everything together
                // reads as the power failing; lifting the key while the house goes is what
                // reads as a deliberate cue.
                _keyLight.intensity = key * Mathf.Lerp(1f, _knockoutKeyMultiplier, _blackout);

                float radius = Mathf.Lerp(_openingKeyRadius, _finalKeyRadius, tension);
                _keyLight.pointLightOuterRadius = Mathf.Lerp(radius, _finalKeyRadius, _blackout);
                _keyLight.pointLightInnerRadius = _keyLight.pointLightOuterRadius * 0.25f;
            }

            if (_rimLights == null)
            {
                return;
            }

            // The corners go out entirely with the house. They exist to separate fighters from
            // the backdrop across a busy ring, and during the hold there is one fighter worth
            // separating and the follow spot is already doing it.
            float rim = Mathf.Lerp(_openingRimIntensity, _finalRimIntensity, tension)
                        * (1f - _blackout);

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
