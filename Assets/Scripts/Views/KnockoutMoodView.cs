using PoRumble.Models;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// Pushes the whole presentation - picture and sound together - into a different register
    /// for the knockout hold, and lets it back out again.
    ///
    /// This exists because the project was already paying for a full post-processing stack and
    /// a mixer that could hold more than one snapshot, and using neither to mark the single
    /// most important moment in a match. The knockout already had hitstop and a light flash;
    /// what it did not have was any change in how the ring itself felt.
    ///
    /// Both halves are deliberately driven from one place. A picture that desaturates while the
    /// sound stays bright reads as a bug rather than as a moment, and keeping the two blends on
    /// the same curve is the only way to guarantee they arrive together.
    ///
    /// Entirely optional, like every other presentation component: the training scenes carry
    /// neither a Volume nor a mixer and construct nothing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KnockoutMoodView : MonoBehaviour
    {
        [Tooltip("Optional. A local Volume holding the knockout grade. Its weight is what this " +
                 "drives, so the profile can be authored freely without touching code.")]
        [SerializeField] private Volume _knockoutVolume;

        [Tooltip("Optional. The mixer carrying the 'Knockout' snapshot.")]
        [SerializeField] private AudioMixer _mixer;

        [Tooltip("Snapshot entered on a knockout. Must exist on the mixer above.")]
        [SerializeField] private string _knockoutSnapshot = "Knockout";

        [Tooltip("Snapshot returned to for everything else.")]
        [SerializeField] private string _defaultSnapshot = "Default";

        [Tooltip("Seconds to reach full knockout weight. Fast: the moment has already happened.")]
        [SerializeField] private float _enterSeconds = 0.12f;

        [Tooltip("Seconds to come back out. Slower, so the ring settles rather than snaps.")]
        [SerializeField] private float _exitSeconds = 0.9f;

        private readonly CompositeDisposable _disposables = new();

        private MatchFlowModel _flow;
        private AudioMixerSnapshot _knockout;
        private AudioMixerSnapshot _default;
        private float _weight;
        private bool _held;

        [Inject]
        public void Construct(MatchFlowModel flow)
        {
            _flow = flow;
        }

        private void Awake()
        {
            if (_mixer == null)
            {
                return;
            }

            // Resolved once. FindSnapshot is a string lookup into the mixer's table and has no
            // business running on the frame a knockout lands.
            _knockout = _mixer.FindSnapshot(_knockoutSnapshot);
            _default = _mixer.FindSnapshot(_defaultSnapshot);

            if (_knockout == null || _default == null)
            {
                Debug.LogWarning(
                    $"{nameof(KnockoutMoodView)}: mixer '{_mixer.name}' has no snapshot named " +
                    $"'{_knockoutSnapshot}' or '{_defaultSnapshot}'. The audio half is disabled.",
                    this);
            }
        }

        private void Start()
        {
            if (_knockoutVolume != null)
            {
                _knockoutVolume.weight = 0f;
            }

            if (_flow == null)
            {
                return;
            }

            _flow.Phase.Subscribe(OnPhaseChanged).AddTo(_disposables);
        }

        private void OnPhaseChanged(MatchFlowPhase phase)
        {
            bool held = phase == MatchFlowPhase.KnockoutHold;

            if (held == _held)
            {
                return;
            }

            _held = held;

            if (_knockout == null || _default == null)
            {
                return;
            }

            // The mixer runs its own transition on real time, so it is handed a duration rather
            // than stepped frame by frame - and it therefore stays correct through the hold's
            // slow motion without any of the unscaled-time care the visual half needs.
            AudioMixerSnapshot target = held ? _knockout : _default;
            target.TransitionTo(held ? _enterSeconds : _exitSeconds);
        }

        /// <summary>
        /// Unscaled, and that is the whole reason this is a per-frame blend rather than a
        /// coroutine-shaped fade. The knockout hold sets <c>Time.timeScale</c> to a fraction,
        /// so a blend timed on scaled time would stretch by exactly the factor the moment it
        /// is illustrating just applied - the grade would still be arriving after the hold
        /// ended.
        /// </summary>
        private void LateUpdate()
        {
            if (_knockoutVolume == null)
            {
                return;
            }

            float target = _held ? 1f : 0f;
            float seconds = _held ? _enterSeconds : _exitSeconds;

            if (seconds <= 0f)
            {
                _weight = target;
            }
            else
            {
                _weight = Mathf.MoveTowards(
                    _weight, target, Time.unscaledDeltaTime / seconds);
            }

            _knockoutVolume.weight = _weight;
        }

        private void OnDestroy()
        {
            _disposables.Dispose();

            // The mixer's snapshot state is an asset-level setting and outlives Play mode, in
            // exactly the way Time.timeScale does. Leaving on the knockout snapshot means the
            // next session starts muffled with nothing on screen to explain why.
            if (_default != null)
            {
                _default.TransitionTo(0f);
            }
        }
    }
}
