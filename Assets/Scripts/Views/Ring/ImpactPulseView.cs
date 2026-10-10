using MessagePipe;
using PoRumble.Models;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// Kicks a post-processing volume on a heavy hit - a haymaker or a counter - and lets it
    /// fall away again within a few frames.
    ///
    /// The volume's profile carries the look (lens distortion and chromatic aberration); this
    /// only drives its weight, the way <see cref="KnockoutMoodView"/> drives the knockout
    /// grade, so the pulse can be re-authored without touching code. Jabs do not fire it: a
    /// lens that flinches on every landed punch is the background, not a signal.
    ///
    /// Decays on unscaled time. The hit that fires it also starts a hitstop, and a pulse timed
    /// on scaled time would hang at full strength for the length of the freeze.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ImpactPulseView : MonoBehaviour
    {
        [Tooltip("A global Volume holding the pulse profile. Its weight is what this drives.")]
        [SerializeField] private Volume _pulseVolume;

        [Tooltip("Weight the volume jumps to on a fully charged haymaker. A counter or a " +
                 "half-charged punch lands proportionally lower.")]
        [Range(0f, 1f)]
        [SerializeField] private float _peakWeight = 0.85f;

        [Tooltip("Seconds for the pulse to die away.")]
        [SerializeField] private float _decaySeconds = 0.18f;

        private const float HEAVY_CHARGE = 0.5f;
        private const float COUNTER_STRENGTH = 0.6f;

        private readonly CompositeDisposable _disposables = new();

        private float _weight;

        [Inject]
        public void Construct(ISubscriber<PunchLandedMessage> landedSubscriber)
        {
            landedSubscriber.Subscribe(OnPunchLanded).AddTo(_disposables);
        }

        private void Start()
        {
            if (_pulseVolume != null)
            {
                _pulseVolume.weight = 0f;
            }
        }

        private void Update()
        {
            if (_pulseVolume == null || _weight <= 0f)
            {
                return;
            }

            _weight = Mathf.Max(0f, _weight - Time.unscaledDeltaTime / Mathf.Max(0.01f, _decaySeconds));
            _pulseVolume.weight = _weight;
        }

        private void OnPunchLanded(PunchLandedMessage message)
        {
            float strength = message.ChargeLevel > HEAVY_CHARGE ? message.ChargeLevel : 0f;

            if (message.IsCounter)
            {
                strength = Mathf.Max(strength, COUNTER_STRENGTH);
            }

            if (strength <= 0f)
            {
                return;
            }

            // Never lowered by a second hit landing inside the first one's decay.
            _weight = Mathf.Max(_weight, _peakWeight * strength);
        }

        private void OnDestroy() => _disposables.Dispose();
    }
}
