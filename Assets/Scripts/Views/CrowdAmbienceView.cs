using MessagePipe;
using PoRumble.Models;
using UnityEngine;
using UnityEngine.Audio;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// The room the fight is happening in.
    ///
    /// PoRumbleMixer has routed Master into SFX, UI and a dedicated Ambience group since it was
    /// built, and nothing has ever played a sample into Ambience - so between punches the ring
    /// has been completely silent for the life of the project. That silence is what made the
    /// synthesised one-shots sound synthesised: a punch with nothing behind it is a waveform,
    /// and a punch over a room is an event in a place.
    ///
    /// Two voices, both non-positional. The bed is a seamless loop whose level tracks how much
    /// is happening; the swell is a one-shot fired when something happens worth reacting to.
    /// Neither is spatialised, and that is the point - a crowd is all around the listener, so
    /// giving it a position would put the entire audience in one seat.
    ///
    /// A View, and a pure observer: it reads the models and the message bus and writes only to
    /// its own two AudioSources.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrowdAmbienceView : MonoBehaviour
    {
        [Tooltip("The Ambience group on PoRumbleMixer. Without it the crowd plays dry through " +
                 "the master bus and the Knockout snapshot cannot duck it.")]
        [SerializeField] private AudioMixerGroup _ambienceMixerGroup;

        [Header("Bed")]
        [Tooltip("Level of the crowd bed while the ring is racked and nothing is happening.")]
        [Range(0f, 1f)]
        [SerializeField] private float _idleVolume = 0.05f;

        [Tooltip("Level of the crowd bed with the fight at its most heated. Low, and it has " +
                 "to be: this is a broadband noise bed sitting underneath a speaking voice, " +
                 "and it only has to be noticed when it stops.")]
        [Range(0f, 1f)]
        [SerializeField] private float _peakVolume = 0.22f;

        [Tooltip("How quickly the bed follows the fight. Low values drift between exchanges " +
                 "rather than tracking each punch, which is what a room actually does.")]
        [SerializeField] private float _damping = 0.9f;

        [Tooltip("Pitch at idle. A crowd waiting is lower and slower than a crowd on its feet.")]
        [SerializeField] private float _idlePitch = 0.92f;
        [SerializeField] private float _peakPitch = 1.06f;

        [Header("Reaction")]
        [Tooltip("How many timbre variants of the reaction swell to synthesise.")]
        [SerializeField] private int _swellVariants = 3;

        [Range(0f, 1f)]
        [SerializeField] private float _swellVolume = 0.34f;

        [Tooltip("Momentum, in hit points, that counts as the crowd being fully worked up. " +
                 "Momentum accumulates raw damage and decays, so it is NOT a 0..1 measure - " +
                 "clamping it directly pinned the bed at its peak within seconds of the bell " +
                 "and left the room at full volume for the whole fight. Eight is roughly one " +
                 "clean flurry, on the same reasoning as the telemetry board's full scale.")]
        [SerializeField] private float _momentumForPeak = 8f;

        [Tooltip("Damage in one punch at or above which the crowd reacts. Below this the bed " +
                 "rising is the whole response - a roar on every jab is a roar on nothing.")]
        [SerializeField] private int _reactionDamage = 4;

        [Tooltip("Seconds after a reaction before another can fire, so a flurry of heavy " +
                 "punches produces one sustained roar rather than four overlapping ones.")]
        [SerializeField] private float _reactionCooldown = 2.4f;

        [Header("Commentary duck")]
        [Tooltip("What the crowd falls to while the commentator is speaking, as a fraction of " +
                 "where it would otherwise be. Standard broadcast practice, and the reason it " +
                 "is needed here rather than optional: the bed is broadband noise and the " +
                 "commentator is one voice, so without it he is simply not audible over a " +
                 "busy exchange - which is exactly when he has most to say.")]
        [Range(0f, 1f)]
        [SerializeField] private float _duckLevel = 0.3f;

        [Tooltip("Seconds a line is assumed to last. Matches CommentarySystem's own " +
                 "ASSUMED_LINE_SECONDS, and that is not a coincidence: the duck has to cover " +
                 "the line and stop, not the line plus the cooldown after it. At 2.6 it was " +
                 "longer than the 2.55s minimum between line starts, so a busy ten-way " +
                 "re-armed it before it could ever release and the crowd sat permanently " +
                 "ducked - a level cut wearing a ducker's clothes. At 1.9 the room breathes " +
                 "back up through the gap between lines, which is the whole effect.\n\n" +
                 "Time-based rather than tied to the clip, because the crowd has no business " +
                 "knowing what CommentaryView is playing.")]
        [SerializeField] private float _duckSeconds = 1.9f;

        [Tooltip("How quickly the duck closes and releases. Fast in, slower out, which is " +
                 "what stops the room audibly pumping on every line.")]
        [SerializeField] private float _duckAttack = 9f;
        [SerializeField] private float _duckRelease = 2.2f;

        private readonly CompositeDisposable _disposables = new();

        private MatchModel _match;
        private MatchFlowModel _flow;
        private FightStatsModel _stats;

        private AudioSource _bed;
        private AudioSource _swell;
        private AudioClip[] _swellClips;

        private float _excitement;
        private float _reactionCooldownRemaining;
        private float _duckRemaining;

        /// <summary>
        /// 1 when the crowd is at full level, <c>_duckLevel</c> while the commentator is
        /// speaking. Eased rather than switched, so the room steps back rather than jumping.
        /// </summary>
        private float _duckGain = 1f;

        private uint _randomState = 0x1B873593;

        [Inject]
        public void Construct(
            MatchModel match,
            MatchFlowModel flow,
            FightStatsModel stats,
            CommentaryModel commentary,
            ISubscriber<PunchLandedMessage> landedSubscriber,
            ISubscriber<BoxerEliminatedMessage> eliminatedSubscriber,
            ISubscriber<HaymakerThrownMessage> haymakerSubscriber)
        {
            _match = match;
            _flow = flow;
            _stats = stats;

            landedSubscriber.Subscribe(OnPunchLanded).AddTo(_disposables);
            eliminatedSubscriber.Subscribe(_ => React(1f)).AddTo(_disposables);

            // The wind-up, not the landing. A crowd sees a haymaker coming - that is the whole
            // reason the telegraph exists - so the noise belongs to the moment of commitment.
            haymakerSubscriber.Subscribe(OnHaymakerThrown).AddTo(_disposables);

            // The cue rather than the clip. CommentarySystem publishes this the moment it
            // decides to speak, and the crowd has no business knowing what CommentaryView is
            // actually playing - so the duck is held for a fixed time, which is what a real
            // ducker does anyway.
            commentary.Cue.Subscribe(OnCommentaryCue).AddTo(_disposables);
        }

        private void OnCommentaryCue(CommentaryCue cue)
        {
            if (!cue.HasLine)
            {
                return;
            }

            _duckRemaining = _duckSeconds;
        }

        private void Awake()
        {
            _bed = BuildVoice("CrowdBed");
            _bed.clip = ProceduralSfx.CreateCrowdBed();
            _bed.loop = true;
            _bed.volume = _idleVolume;
            _bed.pitch = _idlePitch;
            _bed.Play();

            _swell = BuildVoice("CrowdSwell");

            int variants = Mathf.Max(1, _swellVariants);
            _swellClips = new AudioClip[variants];

            for (int variant = 0; variant < variants; variant++)
            {
                _swellClips[variant] = ProceduralSfx.CreateCrowdSwell(variant);
            }
        }

        /// <summary>
        /// One non-positional source, routed to Ambience.
        ///
        /// Built in code rather than authored in the scene for the same reason the combat voice
        /// pool is: these are runtime voices with nothing to adjust on them, not scenery. The
        /// mixer group is the one thing that is a decision, and that is serialized.
        /// </summary>
        private AudioSource BuildVoice(string name)
        {
            GameObject host = new(name);
            host.transform.SetParent(transform, false);

            AudioSource source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;

            // Fully 2D. A crowd surrounds the listener, so panning it anywhere would put the
            // whole audience in one seat - and the spectator camera moves, which would then
            // swing the entire room from side to side as it tracked the fight.
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = _ambienceMixerGroup;

            return source;
        }

        /// <summary>
        /// Tracks how heated the fight is and drives the bed from it.
        ///
        /// Unscaled, like everything else in the presentation layer: the knockout hold slows
        /// the world right down at the moment the crowd is loudest, and a bed easing on scaled
        /// time would take four times as long to settle as the hold it is settling under.
        /// </summary>
        private void Update()
        {
            if (_bed == null)
            {
                return;
            }

            float delta = Time.unscaledDeltaTime;

            if (_reactionCooldownRemaining > 0f)
            {
                _reactionCooldownRemaining -= delta;
            }

            _excitement = Mathf.Lerp(
                _excitement, TargetExcitement(), 1f - Mathf.Exp(-_damping * delta));

            if (_duckRemaining > 0f)
            {
                _duckRemaining -= delta;
            }

            // Asymmetric: in fast so the first word is already clear, out slowly so the room
            // does not audibly pump back up between one line and the next.
            bool ducking = _duckRemaining > 0f;
            float duckTarget = ducking ? _duckLevel : 1f;
            float duckRate = ducking ? _duckAttack : _duckRelease;

            _duckGain = Mathf.Lerp(_duckGain, duckTarget, 1f - Mathf.Exp(-duckRate * delta));

            _bed.volume = Mathf.Lerp(_idleVolume, _peakVolume, _excitement) * _duckGain;
            _bed.pitch = Mathf.Lerp(_idlePitch, _peakPitch, _excitement);
        }

        /// <summary>
        /// How worked up the room should be, in 0..1.
        ///
        /// Two independent halves, taken at their maximum rather than averaged. The field
        /// thinning is a slow build that holds even when nothing is happening this second - a
        /// crowd at the last two is loud because of where the fight is, not because of the last
        /// punch. Momentum is the fast half, and on its own it would drop the room to nothing
        /// between exchanges in a final that should never go quiet.
        /// </summary>
        private float TargetExcitement()
        {
            if (_flow != null && _flow.Phase.Value == MatchFlowPhase.Title)
            {
                // Racked and waiting. The room is in, but nothing has started.
                return 0f;
            }

            float thinning = 0f;

            if (_match != null && _match.Boxers.Count > 0)
            {
                int total = Mathf.Max(1, _match.Boxers.Count);
                int alive = Mathf.Max(1, _match.CountAlive());
                thinning = 1f - Mathf.InverseLerp(2f, total, alive);
            }

            float action = 0f;

            if (_stats != null)
            {
                // The largest momentum on the card rather than the sum. Momentum is already a
                // per-fighter measure of recent damage, and summing ten of them would report a
                // busy ring as louder than a decisive one - which is the wrong way round.
                for (int index = 0; index < _stats.Stats.Count; index++)
                {
                    action = Mathf.Max(action, Mathf.Abs(_stats.Stats[index].Momentum));
                }

                // Divided before clamping. Momentum is a running total of raw damage, not a
                // fraction - measured live it sits around 2 and peaks near 14 - so clamping it
                // straight to 0..1 saturated the bed within seconds of the bell and held the
                // room at full volume for the entire match, which is no dynamic range at all.
                action = Mathf.Clamp01(action / Mathf.Max(0.01f, _momentumForPeak));
            }

            return Mathf.Max(thinning, action);
        }

        private void OnPunchLanded(PunchLandedMessage message)
        {
            if (message.Damage < _reactionDamage && !message.IsCounter)
            {
                return;
            }

            React(Mathf.Clamp01(message.Damage / 8f + (message.IsCounter ? 0.3f : 0f)));
        }

        private void OnHaymakerThrown(HaymakerThrownMessage message)
        {
            if (message.ChargeLevel < 0.75f)
            {
                return;
            }

            React(message.ChargeLevel * 0.7f);
        }

        /// <summary>
        /// Fires a reaction swell, unless one is still ringing.
        ///
        /// Dropped rather than queued while cooling down, for the reason the commentator drops
        /// lines: by the time a queued roar reached the front, the thing it was reacting to
        /// would be several seconds gone and the room would be cheering an empty ring.
        /// </summary>
        private void React(float strength)
        {
            if (_swell == null || _reactionCooldownRemaining > 0f)
            {
                return;
            }

            _reactionCooldownRemaining = _reactionCooldown;

            // The reaction also lifts the bed directly. Without this the room would roar once
            // and immediately fall back to whatever the slow measures said, which reads as a
            // recording rather than as the same crowd getting louder.
            _excitement = Mathf.Clamp01(_excitement + strength * 0.45f);

            int index = (int)(Mathf.Abs(NextUnit()) * _swellClips.Length);
            AudioClip clip = _swellClips[Mathf.Clamp(index, 0, _swellClips.Length - 1)];

            _swell.pitch = 1f + NextUnit() * 0.05f;

            // Ducked like the bed. A roar is the loudest thing the crowd does and a knockout
            // is exactly when the commentator has a line worth hearing, so without this the
            // two arrive together and neither is legible.
            _swell.PlayOneShot(
                clip, Mathf.Clamp01(_swellVolume * (0.55f + strength * 0.45f) * _duckGain));
        }

        /// <summary>
        /// A local xorshift, in -1..1. Off UnityEngine.Random for the reason every other
        /// presentation jitter in this project is: making a noise must not advance a sequence
        /// the scripted brains draw from, or a training run stops being reproducible.
        /// </summary>
        private float NextUnit()
        {
            _randomState ^= _randomState << 13;
            _randomState ^= _randomState >> 17;
            _randomState ^= _randomState << 5;
            return (_randomState & 0xFFFFFF) / (float)0x800000 - 1f;
        }

        private void OnDestroy() => _disposables.Dispose();
    }
}
