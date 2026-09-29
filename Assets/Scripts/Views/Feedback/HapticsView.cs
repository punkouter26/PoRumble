using MessagePipe;
using PoRumble.Models;
using UnityEngine;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// The phone in your hand feels the big hits.
    ///
    /// Only the ones on camera, and only the heavy ones. The shipped build is an all-AI
    /// exhibition with ten fighters, and a phone that buzzed for every counter anywhere in the
    /// ring would be buzzing for punches the viewer never saw - which is noise, not feedback.
    /// So a counter or a landed haymaker vibrates only when it involves the pair the director
    /// has on screen, and a knockout, which the camera always cuts to, vibrates wherever it is.
    ///
    /// Silent on every platform without a vibrator, including the Editor, so the desktop build
    /// carries this component at no cost.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HapticsView : MonoBehaviour
    {
        [SerializeField] private bool _enabled = true;

        [Tooltip("A landed haymaker or counter on the pair the camera is watching.")]
        [SerializeField] private int _heavyMilliseconds = 22;
        [Range(0f, 1f)]
        [SerializeField] private float _heavyStrength = 0.55f;

        [Tooltip("A fighter going down.")]
        [SerializeField] private int _knockoutMilliseconds = 70;
        [Range(0f, 1f)]
        [SerializeField] private float _knockoutStrength = 1f;

        [Tooltip("Seconds before another pulse can fire. A flurry of counters should be felt " +
                 "as a few distinct taps, not one long buzz.")]
        [SerializeField] private float _minInterval = 0.12f;

        private readonly CompositeDisposable _disposables = new();

        private DirectorModel _director;
        private AndroidHaptics _haptics;
        private float _lastPulse = float.NegativeInfinity;

        [Inject]
        public void Construct(
            DirectorModel director,
            ISubscriber<PunchLandedMessage> landedSubscriber,
            ISubscriber<BoxerEliminatedMessage> eliminatedSubscriber)
        {
            _director = director;

            landedSubscriber.Subscribe(OnPunchLanded).AddTo(_disposables);
            eliminatedSubscriber.Subscribe(_ => Pulse(_knockoutMilliseconds, _knockoutStrength)).AddTo(_disposables);
        }

        private void Awake()
        {
            _haptics = new AndroidHaptics();
        }

        private void OnPunchLanded(PunchLandedMessage message)
        {
            bool heavy = message.IsCounter || message.ChargeLevel > 0.5f;

            if (!heavy || !OnCamera(message.AttackerId, message.TargetId))
            {
                return;
            }

            Pulse(_heavyMilliseconds, _heavyStrength);
        }

        private bool OnCamera(int attackerId, int targetId)
        {
            if (_director == null || !_director.HasPair)
            {
                return false;
            }

            return IsInPair(attackerId) || IsInPair(targetId);
        }

        private bool IsInPair(int boxerId)
        {
            return boxerId == _director.FocusId || boxerId == _director.RivalId;
        }

        /// <summary>Unscaled time: hitstop slows the world at the exact moment a pulse is due.</summary>
        private void Pulse(int milliseconds, float strength)
        {
            if (!_enabled || _haptics == null || !_haptics.IsAvailable)
            {
                return;
            }

            float now = Time.unscaledTime;

            if (now - _lastPulse < _minInterval)
            {
                return;
            }

            _lastPulse = now;
            _haptics.Pulse(milliseconds, strength);
        }

        private void OnDestroy()
        {
            _disposables.Dispose();

            if (_haptics != null)
            {
                _haptics.Dispose();
            }
        }
    }
}
