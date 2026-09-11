using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using PoRumble.Models;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering.Universal;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// Everything you see and hear when a punch resolves: hitstop, camera shake, a burst of
    /// sparks, a flash of light and a positioned sound picked to match what actually happened.
    ///
    /// Every one of these events was already being broadcast on MessagePipe and consumed only
    /// by the reward shaping, which meant a landed punch, a blocked punch and a whiff were
    /// completely indistinguishable to a person watching. This is a pure View: it subscribes,
    /// it reacts, and it never touches game state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatFeedbackView : MonoBehaviour
    {
        [Header("Shake")]
        [Tooltip("Optional. Cinemachine impulse fired on impact; leave empty for no shake.")]
        [SerializeField] private CinemachineImpulseSource _impulseSource;
        [SerializeField] private float _jabImpulse = 0.12f;
        [SerializeField] private float _knockoutImpulse = 0.9f;

        [Header("Particles")]
        [Tooltip("Optional. Burst emitted at the contact point of a landed punch.")]
        [SerializeField] private ParticleSystem _impactBurst;
        [Tooltip("Optional. Burst emitted where a punch was stopped on the gloves.")]
        [SerializeField] private ParticleSystem _blockBurst;
        [Tooltip("Optional. Sweat thrown off the target's head by a solid hit.")]
        [SerializeField] private ParticleSystem _sweatBurst;
        [Tooltip("Optional. Expanding ring on a full-charge landing.")]
        [SerializeField] private ParticleSystem _shockwave;
        [Tooltip("Optional. Embers gathering around a boxer winding up a haymaker.")]
        [SerializeField] private ParticleSystem _chargeAura;
        [Tooltip("Optional. Canvas dust kicked up by a fighter's footwork.")]
        [SerializeField] private ParticleSystem _footDust;
        [Tooltip("Optional. Motion streaks trailing a released haymaker.")]
        [SerializeField] private ParticleSystem _speedLines;
        [Tooltip("Optional. Spray thrown off a head by a punch heavy enough to open it up. " +
                 "Distinct from the sweat burst: sweat comes off any solid hit and drifts, " +
                 "this is fast, directional and only fires on the heavy ones.")]
        [SerializeField] private ParticleSystem _bloodSpray;
        [SerializeField] private int _impactParticles = 12;
        [SerializeField] private int _blockParticles = 6;

        [Tooltip("Damage in one punch at or above which the spray fires. A jab that grazes a " +
                 "cheek does not open anyone up, and spraying on every landed punch turns the " +
                 "ring red inside one exchange.")]
        [SerializeField] private int _bloodSprayDamage = 4;

        [Header("Footwork dust")]
        [Tooltip("Speed, in units per second, below which a fighter raises no dust at all.")]
        [SerializeField] private float _dustSpeedThreshold = 1.6f;
        [Tooltip("Seconds between dust puffs from one fighter at full speed.")]
        [SerializeField] private float _dustInterval = 0.11f;

        [Header("Body sounds")]
        [Tooltip("Seconds between footsteps from one moving fighter. Far longer than the dust " +
                 "interval on purpose: dust is cheap and a step is a voice out of the pool.")]
        [SerializeField] private float _stepInterval = 0.34f;
        [Tooltip("Quiet, and it has to be. This is the most frequently played sound in the " +
                 "game by a wide margin, and it is filtered noise - at any level where an " +
                 "individual step is noticeable, ten fighters produce a wash that buries the " +
                 "punches and the commentator alike.")]
        [Range(0f, 1f)]
        [SerializeField] private float _stepVolume = 0.16f;

        [Tooltip("How close to the ropes counts as hitting them, in world units.")]
        [SerializeField] private float _ropeContactMargin = 0.22f;
        [Tooltip("Speed into the ropes below which the contact is a lean rather than a jolt.")]
        [SerializeField] private float _ropeSpeedThreshold = 2.2f;
        [Tooltip("Seconds before the same fighter can jolt the ropes again. Without it a " +
                 "boxer pinned in a corner throws dust on every frame they hold into it, " +
                 "because the position clamp keeps them touching the boundary.")]
        [SerializeField] private float _ropeInterval = 0.6f;

        [Tooltip("Fraction of the audio max distance beyond which body sounds are not " +
                 "synthesised at all. Ten fighters shuffling would otherwise steal every voice " +
                 "in the pool from the punches, for sounds the distance rolloff has already " +
                 "made inaudible. Measured at 0.6 this had nine of fourteen voices live at " +
                 "once during an ordinary exchange, which is a wash rather than a ring.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float _bodySoundEarshot = 0.42f;

        [Header("Crowd")]
        [Tooltip("Optional. Press cameras firing from ringside on a knockout. Reuses the impact " +
                 "light pool, so this costs no extra lights and no extra particle system.")]
        [SerializeField] private int _crowdFlashCount = 5;
        [SerializeField] private float _crowdFlashRadius = 26f;

        [Header("Sound bank")]
        [Tooltip("How many timbre variants to synthesise per impact sound. One clip per event " +
                 "makes a flurry read as a loop within seconds; four is enough to hide it.")]
        [SerializeField] private int _sfxVariants = 4;

        [Header("Impact light")]
        [Tooltip("Lights authored in the scene. When set, these are used instead of creating " +
                 "them at Start, so the rig can be selected and retuned without entering Play " +
                 "mode - and the array length, not the count below, sizes the pool.")]
        [SerializeField] private Light2D[] _preplacedImpactLights;
        [Tooltip("How many punches can be lighting the ring at once before the oldest is " +
                 "reused. Ignored when lights are authored in the scene above.")]
        [SerializeField] private int _impactLightCount = 8;
        [SerializeField] private Color _impactLightColor = new(1f, 0.88f, 0.62f);
        [SerializeField] private float _impactLightIntensity = 2.2f;
        [SerializeField] private float _impactLightRadius = 3.4f;
        [SerializeField] private float _impactLightSeconds = 0.22f;

        [Header("Hitstop")]
        [Tooltip("Seconds of frozen time on an ordinary punch. Small on purpose: this is felt " +
                 "rather than seen, and anything longer starts to feel like frame drops.")]
        [SerializeField] private float _jabHitstop = 0.035f;
        [Tooltip("Seconds of frozen time on a full-charge haymaker or a counter.")]
        [SerializeField] private float _heavyHitstop = 0.09f;
        [Range(0f, 1f)]
        [SerializeField] private float _hitstopTimeScale = 0.05f;

        [Header("Audio")]
        [Tooltip("Optional mixer group for positioned combat sounds.")]
        [SerializeField] private AudioMixerGroup _sfxMixerGroup;
        [Tooltip("Voices authored in the scene. When set, these are used instead of creating " +
                 "them at Start, and the array length, not the count below, sizes the pool.")]
        [SerializeField] private AudioSource[] _preplacedVoices;
        [Tooltip("Simultaneous positioned sounds before the oldest voice is reused. Ignored " +
                 "when voices are authored in the scene above.")]
        [SerializeField] private int _spatialVoiceCount = 14;
        [Tooltip("Distance at which a punch is still at full volume.")]
        [SerializeField] private float _audioMinDistance = 4f;
        [Tooltip("Distance beyond which a punch is inaudible.")]
        [SerializeField] private float _audioMaxDistance = 34f;
        [Range(0f, 1f)]
        [SerializeField] private float _sfxVolume = 0.7f;

        private readonly CompositeDisposable _disposables = new();

        private MatchFlowModel _flow;
        private MatchModel _match;
        private BoxerConfig _config;

        private SpatialVoicePool _voices;
        private ImpactLightPool _lights;

        // Banks rather than single clips. Index chosen per playback - see PickFrom.
        private AudioClip[] _jabClips;
        private AudioClip[] _hookClips;
        private AudioClip[] _haymakerClips;
        private AudioClip[] _stepClips;

        /// <summary>Locomotion feedback - dust, steps, breath, ropes. See BoxerBodyFeedback.</summary>
        private BoxerBodyFeedback _bodyFeedback;

        /// <summary>
        /// Where the ears are. Body sounds are gated on distance to this rather than emitted
        /// for the whole roster: the pool holds fourteen voices and ten fighters shuffling
        /// would take all of them from the punches, to play sounds the distance rolloff had
        /// already faded to nothing.
        /// </summary>
        private Transform _listener;

        private uint _randomState = 0x6C078965;

        private CancellationTokenSource _hitstopCts;

        [Inject]
        public void Construct(
            MatchFlowModel flow,
            MatchModel match,
            BoxerConfig config,
            ISubscriber<PunchLandedMessage> landedSubscriber,
            ISubscriber<PunchBlockedMessage> blockedSubscriber,
            ISubscriber<PunchEvadedMessage> evadedSubscriber,
            ISubscriber<HaymakerThrownMessage> haymakerSubscriber,
            ISubscriber<BoxerDodgedMessage> dodgedSubscriber,
            ISubscriber<BoxerEliminatedMessage> eliminatedSubscriber)
        {
            _flow = flow;
            _match = match;
            _config = config;

            landedSubscriber.Subscribe(OnPunchLanded).AddTo(_disposables);
            blockedSubscriber.Subscribe(OnPunchBlocked).AddTo(_disposables);
            evadedSubscriber.Subscribe(OnPunchEvaded).AddTo(_disposables);
            haymakerSubscriber.Subscribe(OnHaymakerThrown).AddTo(_disposables);
            dodgedSubscriber.Subscribe(OnBoxerDodged).AddTo(_disposables);
            eliminatedSubscriber.Subscribe(OnBoxerEliminated).AddTo(_disposables);
        }

        private void Awake()
        {
            int variants = Mathf.Max(1, _sfxVariants);
            _jabClips = new AudioClip[variants];
            _hookClips = new AudioClip[variants];
            _haymakerClips = new AudioClip[variants];
            _stepClips = new AudioClip[variants];

            for (int variant = 0; variant < variants; variant++)
            {
                _jabClips[variant] = ProceduralSfx.CreateJab(variant);
                _hookClips[variant] = ProceduralSfx.CreateHook(variant);
                _haymakerClips[variant] = ProceduralSfx.CreateHaymakerImpact(variant);
                // Footsteps and breath repeat far more often than any punch does, so the
                // variant bank matters more here than anywhere else in the game: the ear locks
                // onto an identical waveform fastest when it hears it every third of a second.
                _stepClips[variant] = ProceduralSfx.CreateFootstep(variant);
            }


            _voices = new SpatialVoicePool(
                transform, _spatialVoiceCount, _sfxMixerGroup, _audioMinDistance, _audioMaxDistance,
                _preplacedVoices);

            _lights = new ImpactLightPool(transform, _impactLightCount, 0.6f, _preplacedImpactLights);

        }

        private void Start()
        {
            // Cached once. The listener rides the spectator camera, which moves constantly, so
            // it is the Transform that is cached and the position read from it each tick -
            // caching the position itself would gate every body sound on where the camera was
            // when the scene loaded.
            AudioListener listener = FindAnyObjectByType<AudioListener>();

            if (listener != null)
            {
                _listener = listener.transform;
            }

            // Built here rather than in Awake because it needs the listener resolved above and
            // the voice pool and clip banks built in Awake. The tuning comes straight off the
            // serialized fields, which stay on this component so the scene remains the single
            // place any of it is authored.
            _bodyFeedback = new BoxerBodyFeedback(
                _match,
                _voices,
                _footDust,
                _stepClips,
                new BoxerBodyFeedback.Tuning(
                    _dustSpeedThreshold,
                    _dustInterval,
                    _stepInterval,
                    _stepVolume,
                    _ropeContactMargin,
                    _ropeSpeedThreshold,
                    _ropeInterval,
                    _audioMaxDistance * _bodySoundEarshot));

            _bodyFeedback.SetListener(_listener);
        }

        /// <summary>
        /// Fades impact lights and feeds the charge aura.
        ///
        /// Unscaled throughout: hitstop and the knockout hold both slow the world right down at
        /// the exact moment a punch has landed, and a flash timed on scaled time would hang in
        /// the air for the whole hold.
        /// </summary>
        private void Update()
        {
            float delta = Time.unscaledDeltaTime;
            _lights.Tick(delta);
            TickChargeAura();
            _bodyFeedback?.Tick(delta);
        }

        /// <summary>
        /// Gathers embers around anyone winding up. Sampled from the models rather than driven
        /// by an event, because a wind-up is a state that persists over many frames rather than
        /// a moment - and there can be several at once in a ten-way brawl.
        /// </summary>
        private void TickChargeAura()
        {
            if (_chargeAura == null || _match == null)
            {
                return;
            }

            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel boxer = boxers[boxerIndex];

                if (!boxer.IsAlive.Value || boxer.Charge.Value < _config.MinChargeToRelease)
                {
                    continue;
                }

                ParticleSystem.EmitParams emit = new();
                emit.position = new Vector3(boxer.Position.x, boxer.Position.y, 0f);
                emit.startSize = Mathf.Lerp(0.10f, 0.28f, boxer.Charge.Value);
                emit.startColor = Color.Lerp(
                    new Color(1f, 0.8f, 0.3f, 0.7f),
                    new Color(1f, 0.42f, 0.2f, 1f),
                    boxer.Charge.Value);

                _chargeAura.Emit(emit, 1);
            }
        }

        private void OnPunchLanded(PunchLandedMessage message)
        {
            // A counter or a haymaker gets the full treatment; a jab gets a tap. The whole
            // point is that the player can tell those apart without reading a number.
            bool charged = message.ChargeLevel > 0.5f;
            bool heavy = message.IsCounter || charged;

            AudioClip clip = charged
                ? PickFrom(_haymakerClips)
                : message.IsCloseRange || message.IsCounter
                    ? PickFrom(_hookClips)
                    : PickFrom(_jabClips);

            // Counters ring a little higher, so the moment is audible as well as visible.
            PlayAt(clip, message.Position, message.IsCounter ? 1.18f : 1f);

            Burst(_impactBurst, message.Position, _impactParticles + Mathf.RoundToInt(message.Damage * 2f));
            Burst(_sweatBurst, message.Position, 3 + message.Damage);

            // Only the heavy ones. Blood is the strongest signal the feedback layer has, and a
            // signal that fires on every landed punch is not a signal - it is the background.
            if (message.Damage >= _bloodSprayDamage || message.IsCounter)
            {
                Burst(_bloodSpray, message.Position, 4 + message.Damage * 2);
            }

            if (charged)
            {
                Burst(_shockwave, message.Position, 1);
            }

            float scale = 1f + message.ChargeLevel * 1.4f + (message.IsCounter ? 0.4f : 0f);
            _lights.Flash(
                message.Position,
                message.IsCounter ? new Color(1f, 0.72f, 0.5f) : _impactLightColor,
                _impactLightIntensity * scale,
                _impactLightRadius * scale,
                _impactLightSeconds);

            float force = _jabImpulse * (1f + message.Damage * 0.35f + message.ChargeLevel * 2f);
            Shake(force);
            HitStop(heavy ? _heavyHitstop : _jabHitstop);
        }

        private void OnPunchBlocked(PunchBlockedMessage message)
        {
            Burst(_blockBurst, message.Position, _blockParticles);
            _lights.Flash(message.Position, new Color(0.72f, 0.85f, 1f), 1.1f, 2.2f, 0.14f);
            Shake(_jabImpulse * 0.5f);
        }

        private void OnPunchEvaded(PunchEvadedMessage message)
        {
        }

        /// <summary>
        /// The slip. Heard at the moment it starts rather than when something misses, so the
        /// duck reads as a decision the fighter made - a slip that only made a sound when it
        /// happened to work would be invisible most of the time it was used.
        /// </summary>
        private void OnBoxerDodged(BoxerDodgedMessage message)
        {
        }

        private void OnHaymakerThrown(HaymakerThrownMessage message)
        {
            // Heard at the moment of commitment, before anyone knows whether it lands. That
            // warning is the counterplay to a punch this heavy.
            // Seen at the same moment, and for the same reason. The haymaker's telegraph is
            // the entire counterplay to it, so it needs to be legible from across the ring,
            // where a wind-up on a small sprite is not.
            Burst(_speedLines, message.Position, 4 + Mathf.RoundToInt(message.ChargeLevel * 6f));
        }

        private void OnBoxerEliminated(BoxerEliminatedMessage message)
        {
            BoxerModel boxer = FindBoxer(message.BoxerId);
            Vector2 position = boxer != null ? boxer.Position : Vector2.zero;

            _lights.Flash(position, new Color(1f, 0.45f, 0.35f), 4f, 6f, 0.55f);
            Shake(_knockoutImpulse);
            CrowdFlashes();
        }

        /// <summary>
        /// Ringside press cameras going off when someone goes down.
        ///
        /// Built on the impact light pool rather than as its own system: these are exactly what
        /// that pool already does - a bright, short-lived, positioned flash - and routing them
        /// through it means a knockout cannot exceed the light budget the pool was sized for.
        /// It steals its own earlier flashes instead, which is the right thing to give up.
        /// </summary>
        private void CrowdFlashes()
        {
            for (int index = 0; index < _crowdFlashCount; index++)
            {
                // Around the outside of the ring, where a crowd would be - never over the
                // canvas, which would read as lightning rather than as cameras.
                float angle = NextUnit() * Mathf.PI;
                Vector2 at = new(
                    Mathf.Cos(angle) * _crowdFlashRadius,
                    Mathf.Sin(angle) * _crowdFlashRadius);

                _lights.Flash(at, new Color(0.85f, 0.92f, 1f), 3.2f, 4.5f, 0.09f);
            }
        }

        private BoxerModel FindBoxer(int boxerId)
        {
            if (_match == null)
            {
                return null;
            }

            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                if (boxers[boxerIndex].Id == boxerId)
                {
                    return boxers[boxerIndex];
                }
            }

            return null;
        }

        /// <summary>
        /// Draws a clip from a variant bank.
        ///
        /// Uniform rather than shuffled. A shuffle bag guarantees no immediate repeat, but it
        /// also guarantees every variant is heard before any of them repeats, which over a long
        /// exchange is its own audible pattern. A uniform draw never settles into a rhythm.
        /// </summary>
        private AudioClip PickFrom(AudioClip[] bank)
        {
            if (bank == null || bank.Length == 0)
            {
                return null;
            }

            int index = (int)(Mathf.Abs(NextUnit()) * bank.Length);
            return bank[Mathf.Clamp(index, 0, bank.Length - 1)];
        }

        /// <summary>
        /// A local xorshift, in -1..1. Kept off UnityEngine.Random for the same reason the
        /// scripted brains are: presentation must not advance a sequence the simulation draws
        /// from, or a training run stops being reproducible because something made a noise.
        /// </summary>
        private float NextUnit()
        {
            _randomState ^= _randomState << 13;
            _randomState ^= _randomState >> 17;
            _randomState ^= _randomState << 5;
            return (_randomState & 0xFFFFFF) / (float)0x800000 - 1f;
        }

        /// <summary>A sound that happened somewhere in the ring.</summary>
        private void PlayAt(AudioClip clip, Vector2 position, float pitch)
        {
            _voices?.PlayAt(clip, position, pitch, _sfxVolume);
        }

        private void Burst(ParticleSystem system, Vector2 position, int count)
        {
            if (system == null)
            {
                return;
            }

            system.transform.position = new Vector3(position.x, position.y, 0f);
            system.Emit(count);
        }

        private void Shake(float force)
        {
            if (_impulseSource == null)
            {
                return;
            }

            _impulseSource.GenerateImpulseWithForce(force);
        }

        /// <summary>
        /// Briefly all but stops time, which is what gives a punch its sense of weight.
        ///
        /// Restores the scale only while the fight is still live: the knockout hold sets its
        /// own slow motion, and a hitstop that resolved afterwards would snap the world back
        /// to full speed in the middle of it.
        /// </summary>
        private void HitStop(float seconds)
        {
            if (seconds <= 0f || _flow == null || !_flow.IsFightLive)
            {
                return;
            }

            _hitstopCts?.Cancel();
            _hitstopCts?.Dispose();
            _hitstopCts = CancellationTokenSource.CreateLinkedTokenSource(
                this.GetCancellationTokenOnDestroy());

            HitStopAsync(seconds, _hitstopCts.Token).Forget();
        }

        private async UniTaskVoid HitStopAsync(float seconds, CancellationToken token)
        {
            Time.timeScale = _hitstopTimeScale;

            try
            {
                // Unscaled, or the delay would be stretched by the very scale it just set.
                await UniTask.Delay(
                    TimeSpan.FromSeconds(seconds),
                    DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update,
                    token);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a later punch, or the view is going away. Either way the
                // restore below is the responsibility of whoever cancelled us.
                return;
            }

            if (_flow.IsFightLive)
            {
                Time.timeScale = 1f;
            }
        }

        private void OnDestroy()
        {
            _hitstopCts?.Cancel();
            _hitstopCts?.Dispose();
            _hitstopCts = null;
            _disposables.Dispose();

            // Time.timeScale is global and outlives this object. Being destroyed mid-hitstop
            // would otherwise leave the Editor running at a twentieth of normal speed.
            if (Mathf.Approximately(Time.timeScale, _hitstopTimeScale))
            {
                Time.timeScale = 1f;
            }
        }
    }
}
