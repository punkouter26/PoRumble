using MessagePipe;
using PoRumble.Models;
using UnityEngine;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// The score: a loop that builds with the fight, and a sting for whoever wins it.
    ///
    /// Three stems of one loop play from the moment the scene loads, locked to the same
    /// sample; only their levels move, so the score builds and falls back without a cut. How
    /// hard it plays follows the same measures the crowd and the lights already follow - the
    /// field thinning, the director's tension on the pair it is watching, the momentum on the
    /// card - so the music, the room and the rig agree about how big the moment is.
    ///
    /// Non-positional and routed to its own mixer group. It ducks under the commentator on the
    /// same cue the crowd does, and it drops to almost nothing for the knockout hold, where the
    /// Knockout snapshot is muffling the whole mix and a band playing through it reads as a
    /// fault rather than as an ending.
    ///
    /// Each contestant has a signature winner's sting, picked from the bank by a stable hash of
    /// their name, so the same fighter always wins to the same few notes.
    ///
    /// A View and a pure observer: it reads models and messages and writes only its own sources.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MusicView : MonoBehaviour
    {
        [Tooltip("The looping stems, in layer order: pulse, drive, stabs. 2D sources on the Music " +
                 "group - without it the score plays dry through the master bus and the " +
                 "Knockout snapshot cannot muffle it.")]
        [SerializeField] private AudioSource[] _stems = new AudioSource[MusicMath.LAYER_COUNT];

        [Tooltip("Plays the intro and winner stings over the stems, on the same group.")]
        [SerializeField] private AudioSource _sting;

        [Tooltip("Level of the loop at full intensity. Low: this is a bed under a commentator " +
                 "and a ring full of punches, not a soundtrack.")]
        [Range(0f, 1f)]
        [SerializeField] private float _loopVolume = 0.3f;

        [Tooltip("How quickly the score follows the fight. Slow, so a layer arrives over a bar " +
                 "or two rather than on the punch that tipped the measure.")]
        [SerializeField] private float _damping = 0.6f;

        [Tooltip("Momentum, in hit points, that counts as the fight at full heat - the same " +
                 "scale the crowd uses, for the same reason.")]
        [SerializeField] private float _momentumForPeak = 8f;

        [Header("Stings")]
        [Tooltip("Optional. Played as the fighters are introduced.")]
        [SerializeField] private AudioClip _introSting;
        [Tooltip("Optional. Winner's stings. Each contestant is assigned one by name, so the " +
                 "bank does not need to be as long as the card.")]
        [SerializeField] private AudioClip[] _winnerStings;
        [Range(0f, 1f)]
        [SerializeField] private float _stingVolume = 0.55f;
        [Tooltip("What the loop falls to while a sting plays over it.")]
        [Range(0f, 1f)]
        [SerializeField] private float _stingDuck = 0.3f;

        [Header("Commentary duck")]
        [Range(0f, 1f)]
        [SerializeField] private float _duckLevel = 0.45f;
        [Tooltip("Seconds a line is assumed to last - the crowd's figure, and for its reason.")]
        [SerializeField] private float _duckSeconds = 1.9f;
        [SerializeField] private float _duckAttack = 9f;
        [SerializeField] private float _duckRelease = 2.2f;

        private readonly CompositeDisposable _disposables = new();

        private MatchModel _match;
        private MatchFlowModel _flow;
        private FightStatsModel _stats;
        private DirectorModel _director;
        private RosterModel _roster;

        /// <summary>True once every stem is assigned and loaded; nothing plays without them.</summary>
        private bool _hasStems;

        private float _intensity;
        private float _duckRemaining;
        private float _duckGain = 1f;
        private float _stingRemaining;
        private float _stingGain = 1f;

        /// <summary>Who won, held from the final bell until the results are up. The knockout hold between the two is muffled.</summary>
        private int _pendingWinnerId = MatchModel.NO_WINNER;

        [Inject]
        public void Construct(
            MatchModel match,
            MatchFlowModel flow,
            FightStatsModel stats,
            DirectorModel director,
            RosterModel roster,
            CommentaryModel commentary,
            ISubscriber<MatchEndedMessage> matchEndedSubscriber)
        {
            _match = match;
            _flow = flow;
            _stats = stats;
            _director = director;
            _roster = roster;

            commentary.Cue.Subscribe(OnCommentaryCue).AddTo(_disposables);
            matchEndedSubscriber.Subscribe(message => _pendingWinnerId = message.WinnerId).AddTo(_disposables);
        }

        private void Awake()
        {
            _hasStems = HasEveryStem();

            if (!_hasStems)
            {
                return;
            }

            AudioClip[] clips =
            {
                ProceduralMusic.CreatePulse(),
                ProceduralMusic.CreateDrive(),
                ProceduralMusic.CreateStabs()
            };

            for (int layer = 0; layer < MusicMath.LAYER_COUNT; layer++)
            {
                AudioSource stem = _stems[layer];
                stem.clip = clips[layer];
                stem.loop = true;
                stem.volume = 0f;
            }
        }

        private bool HasEveryStem()
        {
            if (_stems == null || _stems.Length != MusicMath.LAYER_COUNT)
            {
                return false;
            }

            for (int layer = 0; layer < _stems.Length; layer++)
            {
                if (_stems[layer] == null)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Starts the stems - here rather than in Awake, because disabling this object stops
        /// every AudioSource under it and nothing restarts a looping source on its own. Started
        /// in Awake, a score that was switched off and on again stayed silent for the session.
        ///
        /// Scheduled against the DSP clock rather than started with Play: three Play calls in a
        /// row can land in different audio buffers, and stems a buffer apart flam on every kick.
        /// </summary>
        private void OnEnable()
        {
            if (!_hasStems)
            {
                return;
            }

            double start = AudioSettings.dspTime + 0.2;

            for (int layer = 0; layer < MusicMath.LAYER_COUNT; layer++)
            {
                _stems[layer].PlayScheduled(start);
            }
        }

        private void Start()
        {
            if (_flow != null)
            {
                _flow.Phase.Subscribe(OnFlowPhaseChanged).AddTo(_disposables);
            }
        }

        /// <summary>Unscaled: the knockout hold slows the world, and the score must keep resolving through it.</summary>
        private void Update()
        {
            float delta = Time.unscaledDeltaTime;

            _intensity = Mathf.Lerp(_intensity, TargetIntensity(), 1f - Mathf.Exp(-_damping * delta));

            if (_duckRemaining > 0f)
            {
                _duckRemaining -= delta;
            }

            bool ducking = _duckRemaining > 0f;
            _duckGain = Mathf.Lerp(
                _duckGain,
                ducking ? _duckLevel : 1f,
                1f - Mathf.Exp(-(ducking ? _duckAttack : _duckRelease) * delta));

            if (_stingRemaining > 0f)
            {
                _stingRemaining -= delta;
            }

            bool stinging = _stingRemaining > 0f;
            _stingGain = Mathf.Lerp(
                _stingGain,
                stinging ? _stingDuck : 1f,
                1f - Mathf.Exp(-(stinging ? _duckAttack : _duckRelease) * delta));

            // A quieter floor under the whole score at low intensity, so the title screen is a
            // pulse in the room rather than the loop at full level with two stems muted.
            float master = _loopVolume * Mathf.Lerp(0.6f, 1f, _intensity) * _duckGain * _stingGain;

            if (!_hasStems)
            {
                return;
            }

            for (int layer = 0; layer < MusicMath.LAYER_COUNT; layer++)
            {
                _stems[layer].volume = master * MusicMath.LayerGain(layer, _intensity);
            }
        }

        private float TargetIntensity()
        {
            if (_flow == null)
            {
                return 0f;
            }

            float thinning = 0f;

            if (_match != null && _match.Boxers.Count > 0)
            {
                int total = Mathf.Max(1, _match.Boxers.Count);
                int alive = Mathf.Max(1, _match.CountAlive());
                thinning = 1f - Mathf.InverseLerp(2f, total, alive);
            }

            float tension = _director != null && _director.HasPair ? _director.Tension : 0f;

            float momentum = 0f;

            if (_stats != null)
            {
                for (int index = 0; index < _stats.Stats.Count; index++)
                {
                    momentum = Mathf.Max(momentum, Mathf.Abs(_stats.Stats[index].Momentum));
                }

                momentum /= Mathf.Max(0.01f, _momentumForPeak);
            }

            return MusicMath.TargetIntensity(_flow.Phase.Value, thinning, tension, momentum);
        }

        private void OnFlowPhaseChanged(MatchFlowPhase phase)
        {
            switch (phase)
            {
                case MatchFlowPhase.Introducing:
                    _pendingWinnerId = MatchModel.NO_WINNER;
                    PlaySting(_introSting);
                    break;

                case MatchFlowPhase.Results:
                    PlaySting(WinnerSting(_pendingWinnerId));
                    _pendingWinnerId = MatchModel.NO_WINNER;
                    break;
            }
        }

        /// <summary>The winner's signature sting, or nothing for a draw or an empty bank.</summary>
        private AudioClip WinnerSting(int winnerId)
        {
            if (winnerId == MatchModel.NO_WINNER || _winnerStings == null || _winnerStings.Length == 0)
            {
                return null;
            }

            FighterProfile profile = _roster == null ? null : _roster.SeatOf(winnerId);
            string name = profile != null ? profile.DisplayName : winnerId.ToString();

            return _winnerStings[MusicMath.StableIndex(name, _winnerStings.Length)];
        }

        private void PlaySting(AudioClip clip)
        {
            if (clip == null || _sting == null)
            {
                return;
            }

            _sting.PlayOneShot(clip, _stingVolume);
            _stingRemaining = clip.length;
        }

        private void OnCommentaryCue(CommentaryCue cue)
        {
            if (cue.HasLine)
            {
                _duckRemaining = _duckSeconds;
            }
        }

        private void OnDestroy() => _disposables.Dispose();
    }
}
