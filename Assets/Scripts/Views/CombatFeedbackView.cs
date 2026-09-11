using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using PoRumble.Models;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Audio;
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
        [Range(0f, 1f)]
        [SerializeField] private float _stepVolume = 0.34f;

        [Tooltip("Stamina at or below which a fighter is audibly out of breath.")]
        [Range(0f, 1f)]
        [SerializeField] private float _breathStaminaThreshold = 0.35f;
        [Tooltip("Seconds between exhales from one spent fighter.")]
        [SerializeField] private float _breathInterval = 1.5f;
        [Range(0f, 1f)]
        [SerializeField] private float _breathVolume = 0.5f;

        [Tooltip("How close to the ropes counts as hitting them, in world units.")]
        [SerializeField] private float _ropeContactMargin = 0.22f;
        [Tooltip("Speed into the ropes below which the contact is a lean rather than a thud.")]
        [SerializeField] private float _ropeSpeedThreshold = 2.2f;
        [Tooltip("Seconds before the same fighter can thud into the ropes again. Without it a " +
                 "boxer pinned in a corner machine-guns the sound for as long as they hold " +
                 "into it, because the position clamp keeps them touching every frame.")]
        [SerializeField] private float _ropeInterval = 0.6f;
        [Range(0f, 1f)]
        [SerializeField] private float _ropeVolume = 0.66f;

        [Tooltip("Fraction of the audio max distance beyond which body sounds are not " +
                 "synthesised at all. Ten fighters shuffling would otherwise steal every voice " +
                 "in the pool from the punches, for sounds the distance rolloff has already " +
                 "made inaudible.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float _bodySoundEarshot = 0.6f;

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
        [Tooltip("How many punches can be lighting the ring at once before the oldest is reused.")]
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
        [Tooltip("Non-positional source for the bell, countdown and other match-wide cues.")]
        [SerializeField] private AudioSource _audioSource;
        [Tooltip("Optional mixer group for positioned combat sounds.")]
        [SerializeField] private AudioMixerGroup _sfxMixerGroup;
        [Tooltip("Optional mixer group for match-wide cues.")]
        [SerializeField] private AudioMixerGroup _uiMixerGroup;
        [Tooltip("Simultaneous positioned sounds before the oldest voice is reused.")]
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
        private AudioClip[] _whooshClips;
        private AudioClip[] _blockClips;
        private AudioClip[] _evadeClips;
        private AudioClip[] _stepClips;
        private AudioClip[] _breathClips;
        private AudioClip[] _ropeClips;

        // One-offs. A knockout, the bell and the countdown each happen at a moment the player
        // is already attending to, so repetition is not what stands out about them.
        private AudioClip _knockoutClip;
        private AudioClip _bellClip;
        private AudioClip _beepClip;
        private AudioClip _beepFinalClip;

        private float[] _dustTimers;
        private float[] _stepTimers;
        private float[] _breathTimers;
        private float[] _ropeTimers;

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
            _whooshClips = new AudioClip[variants];
            _blockClips = new AudioClip[variants];
            _evadeClips = new AudioClip[variants];
            _stepClips = new AudioClip[variants];
            _breathClips = new AudioClip[variants];
            _ropeClips = new AudioClip[variants];

            for (int variant = 0; variant < variants; variant++)
            {
                _jabClips[variant] = ProceduralSfx.CreateJab(variant);
                _hookClips[variant] = ProceduralSfx.CreateHook(variant);
                _haymakerClips[variant] = ProceduralSfx.CreateHaymakerImpact(variant);
                _whooshClips[variant] = ProceduralSfx.CreateWhoosh(variant);
                _blockClips[variant] = ProceduralSfx.CreateBlock(variant);
                _evadeClips[variant] = ProceduralSfx.CreateEvade(variant);

                // Footsteps and breath repeat far more often than any punch does, so the
                // variant bank matters more here than anywhere else in the game: the ear locks
                // onto an identical waveform fastest when it hears it every third of a second.
                _stepClips[variant] = ProceduralSfx.CreateFootstep(variant);
                _breathClips[variant] = ProceduralSfx.CreateBreath(variant);
                _ropeClips[variant] = ProceduralSfx.CreateRopeThud(variant);
            }

            _knockoutClip = ProceduralSfx.CreateKnockout();
            _bellClip = ProceduralSfx.CreateBell();
            _beepClip = ProceduralSfx.CreateCountdownBeep(false);
            _beepFinalClip = ProceduralSfx.CreateCountdownBeep(true);

            _voices = new SpatialVoicePool(
                transform, _spatialVoiceCount, _sfxMixerGroup, _audioMinDistance, _audioMaxDistance);

            _lights = new ImpactLightPool(transform, _impactLightCount, 0.6f);

            if (_audioSource != null && _uiMixerGroup != null)
            {
                _audioSource.outputAudioMixerGroup = _uiMixerGroup;
            }
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

            if (_flow == null)
            {
                return;
            }

            _flow.Phase.Subscribe(OnFlowPhaseChanged).AddTo(_disposables);
            _flow.CountdownSeconds.Subscribe(OnCountdownTick).AddTo(_disposables);
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
            TickBodyFeedback(delta);
        }

        /// <summary>
        /// Everything a fighter's own body produces: dust off the canvas, footsteps, breath
        /// once they are spent, and the thud of hitting the ropes.
        ///
        /// One pass over the roster rather than four. All of it is rate-limited per fighter off
        /// the same position and velocity, and splitting it into a loop each would mean four
        /// walks of the same list recomputing the same speed.
        ///
        /// Footwork was the one thing a fighter did constantly that produced no feedback at
        /// all - a boxer crossing the ring looked and sounded exactly like a boxer standing
        /// still but translating.
        /// </summary>
        private void TickBodyFeedback(float delta)
        {
            if (_match == null)
            {
                return;
            }

            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            EnsureTimers(boxers.Count);

            Vector2 ears = _listener == null ? Vector2.zero : (Vector2)_listener.position;
            float earshot = _audioMaxDistance * _bodySoundEarshot;
            float earshotSquared = earshot * earshot;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel boxer = boxers[boxerIndex];

                if (!boxer.IsAlive.Value)
                {
                    continue;
                }

                float speed = boxer.Velocity.magnitude;

                // Squared distance: this runs for every fighter every frame, and a square root
                // per fighter buys nothing when the only question is "nearer than".
                bool audible = _listener != null
                               && (boxer.Position - ears).sqrMagnitude <= earshotSquared;

                TickFootDust(boxerIndex, boxer, speed, delta);
                TickFootsteps(boxerIndex, boxer, speed, audible, delta);
                TickBreath(boxerIndex, boxer, audible, delta);
                TickRopeContact(boxerIndex, boxer, speed, audible, delta);
            }
        }

        /// <summary>
        /// Sizes the per-fighter cooldowns to the roster.
        ///
        /// Lazily from the tick rather than once at Start, for the reason the telemetry board
        /// already documents: the roster is not populated until SpawnSystem has run, and
        /// anything that sized itself earlier would size to zero and silently do nothing.
        /// </summary>
        private void EnsureTimers(int count)
        {
            if (_dustTimers != null && _dustTimers.Length == count)
            {
                return;
            }

            _dustTimers = new float[count];
            _stepTimers = new float[count];
            _breathTimers = new float[count];
            _ropeTimers = new float[count];
        }

        private void TickFootDust(int index, BoxerModel boxer, float speed, float delta)
        {
            if (_footDust == null || speed < _dustSpeedThreshold)
            {
                return;
            }

            _dustTimers[index] -= delta;

            if (_dustTimers[index] > 0f)
            {
                return;
            }

            _dustTimers[index] = _dustInterval;

            // Behind the fighter rather than under them: dust is what the foot pushed
            // away, and emitting it at the centre just paints a halo around the sprite.
            Vector2 trail = boxer.Position - boxer.Velocity.normalized * 0.35f;

            ParticleSystem.EmitParams emit = new();
            emit.position = new Vector3(trail.x, trail.y, 0f);
            emit.startSize = Mathf.Lerp(
                0.12f, 0.30f, Mathf.InverseLerp(_dustSpeedThreshold, 6f, speed));
            emit.velocity = new Vector3(-boxer.Velocity.x, -boxer.Velocity.y, 0f) * 0.18f;

            _footDust.Emit(emit, 1);
        }

        /// <summary>
        /// A step, on its own much slower cadence than the dust.
        ///
        /// The two are deliberately not in lockstep. Dust costs a particle and can run at nine
        /// puffs a second; a step costs a voice out of a pool of fourteen shared with every
        /// punch in the ring, so it runs at three - and only for fighters near enough to hear.
        /// Louder the faster they are moving, because a shuffle and a lunge are not the same
        /// thing and the sound is most of what tells them apart from behind.
        /// </summary>
        private void TickFootsteps(int index, BoxerModel boxer, float speed, bool audible, float delta)
        {
            if (!audible || speed < _dustSpeedThreshold)
            {
                return;
            }

            _stepTimers[index] -= delta;

            if (_stepTimers[index] > 0f)
            {
                return;
            }

            // Faster fighters step more often as well as louder. Scaled rather than fixed, or
            // a sprint and a shuffle would have identical rhythm and only differ in level.
            float pace = Mathf.InverseLerp(_dustSpeedThreshold, 6f, speed);
            _stepTimers[index] = Mathf.Lerp(_stepInterval, _stepInterval * 0.6f, pace);

            _voices?.PlayAt(
                PickFrom(_stepClips),
                boxer.Position,
                1f,
                _stepVolume * Mathf.Lerp(0.6f, 1f, pace));
        }

        /// <summary>
        /// A hard exhale once a fighter has punched themselves out.
        ///
        /// Stamina is already what drops the drawn guard through ArmView, so a fighter running
        /// out of breath is visible - but only if you happen to be looking at them, and in a
        /// ten-way you are looking at two of ten. Hearing it is what makes fatigue something
        /// you notice about the fighter you are not watching.
        /// </summary>
        private void TickBreath(int index, BoxerModel boxer, bool audible, float delta)
        {
            if (!audible || boxer.Stamina.Value > _breathStaminaThreshold)
            {
                return;
            }

            _breathTimers[index] -= delta;

            if (_breathTimers[index] > 0f)
            {
                return;
            }

            // The more spent they are the harder they are breathing, so the interval closes as
            // stamina falls rather than sitting at one rate for everything below the threshold.
            float spent = 1f - Mathf.InverseLerp(0f, _breathStaminaThreshold, boxer.Stamina.Value);
            _breathTimers[index] = Mathf.Lerp(_breathInterval, _breathInterval * 0.55f, spent);

            _voices?.PlayAt(
                PickFrom(_breathClips),
                boxer.Position,
                Mathf.Lerp(1.05f, 0.92f, spent),
                _breathVolume * Mathf.Lerp(0.5f, 1f, spent));
        }

        /// <summary>
        /// Hitting the ropes.
        ///
        /// Judged from the model's own position against the arena extent rather than from a
        /// physics contact, because that is where the containment actually happens: BoxerSystem
        /// clamps positions to the ring and the wall colliders hold nobody. A collision
        /// callback would fire for the drawn body drifting into a wall it was never really
        /// touching, and would miss the clamp entirely.
        ///
        /// The cooldown is load-bearing rather than a polish detail. A fighter held into a
        /// corner is *at* the boundary every single frame, because the clamp puts them back
        /// there, so without it this machine-guns the sound for as long as they lean.
        /// </summary>
        private void TickRopeContact(int index, BoxerModel boxer, float speed, bool audible, float delta)
        {
            if (_ropeTimers[index] > 0f)
            {
                _ropeTimers[index] -= delta;
                return;
            }

            if (!audible || speed < _ropeSpeedThreshold)
            {
                return;
            }

            Vector2 extent = _match.ArenaHalfExtent;
            Vector2 position = boxer.Position;

            bool intoVertical = Mathf.Abs(position.x) >= extent.x - _ropeContactMargin
                                && Mathf.Sign(boxer.Velocity.x) == Mathf.Sign(position.x);

            bool intoHorizontal = Mathf.Abs(position.y) >= extent.y - _ropeContactMargin
                                  && Mathf.Sign(boxer.Velocity.y) == Mathf.Sign(position.y);

            // Moving *into* the boundary, not merely near it. A fighter working along the
            // ropes is touching them constantly and is not hitting them; what makes a thud is
            // the direction of travel, which is why the velocity sign is part of the test.
            if (!intoVertical && !intoHorizontal)
            {
                return;
            }

            _ropeTimers[index] = _ropeInterval;

            float force = Mathf.InverseLerp(_ropeSpeedThreshold, 7f, speed);

            _voices?.PlayAt(
                PickFrom(_ropeClips),
                position,
                Mathf.Lerp(1.08f, 0.9f, force),
                _ropeVolume * Mathf.Lerp(0.55f, 1f, force));

            // The ropes take the fighter's weight and throw a little dust off the canvas with
            // them. Reusing the dust system rather than adding one: it is the same material
            // being disturbed, and a second particle system for it would be a second draw.
            if (_footDust != null)
            {
                ParticleSystem.EmitParams emit = new();
                emit.position = new Vector3(position.x, position.y, 0f);
                emit.startSize = Mathf.Lerp(0.18f, 0.38f, force);
                _footDust.Emit(emit, 2 + Mathf.RoundToInt(force * 3f));
            }
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
            PlayAt(PickFrom(_blockClips), message.Position, 1f);
            Burst(_blockBurst, message.Position, _blockParticles);
            _lights.Flash(message.Position, new Color(0.72f, 0.85f, 1f), 1.1f, 2.2f, 0.14f);
            Shake(_jabImpulse * 0.5f);
        }

        private void OnPunchEvaded(PunchEvadedMessage message)
        {
            PlayAt(PickFrom(_evadeClips), message.Position, 1f);
        }

        /// <summary>
        /// The slip. Heard at the moment it starts rather than when something misses, so the
        /// duck reads as a decision the fighter made - a slip that only made a sound when it
        /// happened to work would be invisible most of the time it was used.
        /// </summary>
        private void OnBoxerDodged(BoxerDodgedMessage message)
        {
            PlayAt(PickFrom(_whooshClips), message.Position, 1.5f);
        }

        private void OnHaymakerThrown(HaymakerThrownMessage message)
        {
            // Heard at the moment of commitment, before anyone knows whether it lands. That
            // warning is the counterplay to a punch this heavy.
            PlayAt(PickFrom(_whooshClips), message.Position,
                Mathf.Lerp(1.15f, 0.85f, message.ChargeLevel));

            // Seen at the same moment, and for the same reason. The haymaker's telegraph is
            // the entire counterplay to it, so it needs to be legible from across the ring,
            // where a wind-up on a small sprite is not.
            Burst(_speedLines, message.Position, 4 + Mathf.RoundToInt(message.ChargeLevel * 6f));
        }

        private void OnBoxerEliminated(BoxerEliminatedMessage message)
        {
            BoxerModel boxer = FindBoxer(message.BoxerId);
            Vector2 position = boxer != null ? boxer.Position : Vector2.zero;

            PlayAt(_knockoutClip, position, 1f);
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

        private void OnFlowPhaseChanged(MatchFlowPhase phase)
        {
            if (phase == MatchFlowPhase.Fighting)
            {
                PlayFlat(_bellClip, 1f);
            }
        }

        private void OnCountdownTick(int seconds)
        {
            if (seconds <= 0)
            {
                return;
            }

            PlayFlat(seconds == 1 ? _beepFinalClip : _beepClip, 1f);
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

        /// <summary>A match-wide cue: the bell, the countdown. These belong to nowhere.</summary>
        private void PlayFlat(AudioClip clip, float pitch)
        {
            if (_audioSource == null || clip == null)
            {
                return;
            }

            _audioSource.pitch = pitch;
            _audioSource.PlayOneShot(clip, _sfxVolume);
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
