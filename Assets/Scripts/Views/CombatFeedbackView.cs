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
        [SerializeField] private int _impactParticles = 12;
        [SerializeField] private int _blockParticles = 6;

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

        private AudioClip _jabClip;
        private AudioClip _hookClip;
        private AudioClip _haymakerClip;
        private AudioClip _whooshClip;
        private AudioClip _blockClip;
        private AudioClip _evadeClip;
        private AudioClip _knockoutClip;
        private AudioClip _bellClip;
        private AudioClip _beepClip;
        private AudioClip _beepFinalClip;

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
            ISubscriber<BoxerEliminatedMessage> eliminatedSubscriber)
        {
            _flow = flow;
            _match = match;
            _config = config;

            landedSubscriber.Subscribe(OnPunchLanded).AddTo(_disposables);
            blockedSubscriber.Subscribe(OnPunchBlocked).AddTo(_disposables);
            evadedSubscriber.Subscribe(OnPunchEvaded).AddTo(_disposables);
            haymakerSubscriber.Subscribe(OnHaymakerThrown).AddTo(_disposables);
            eliminatedSubscriber.Subscribe(OnBoxerEliminated).AddTo(_disposables);
        }

        private void Awake()
        {
            _jabClip = ProceduralSfx.CreateJab();
            _hookClip = ProceduralSfx.CreateHook();
            _haymakerClip = ProceduralSfx.CreateHaymakerImpact();
            _whooshClip = ProceduralSfx.CreateWhoosh();
            _blockClip = ProceduralSfx.CreateBlock();
            _evadeClip = ProceduralSfx.CreateEvade();
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
                ? _haymakerClip
                : message.IsCloseRange || message.IsCounter
                    ? _hookClip
                    : _jabClip;

            // Counters ring a little higher, so the moment is audible as well as visible.
            PlayAt(clip, message.Position, message.IsCounter ? 1.18f : 1f);

            Burst(_impactBurst, message.Position, _impactParticles + Mathf.RoundToInt(message.Damage * 2f));
            Burst(_sweatBurst, message.Position, 3 + message.Damage);

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
            PlayAt(_blockClip, message.Position, 1f);
            Burst(_blockBurst, message.Position, _blockParticles);
            _lights.Flash(message.Position, new Color(0.72f, 0.85f, 1f), 1.1f, 2.2f, 0.14f);
            Shake(_jabImpulse * 0.5f);
        }

        private void OnPunchEvaded(PunchEvadedMessage message)
        {
            PlayAt(_evadeClip, message.Position, 1f);
        }

        private void OnHaymakerThrown(HaymakerThrownMessage message)
        {
            // Heard at the moment of commitment, before anyone knows whether it lands. That
            // warning is the counterplay to a punch this heavy.
            PlayAt(_whooshClip, message.Position, Mathf.Lerp(1.15f, 0.85f, message.ChargeLevel));
        }

        private void OnBoxerEliminated(BoxerEliminatedMessage message)
        {
            BoxerModel boxer = FindBoxer(message.BoxerId);
            Vector2 position = boxer != null ? boxer.Position : Vector2.zero;

            PlayAt(_knockoutClip, position, 1f);
            _lights.Flash(position, new Color(1f, 0.45f, 0.35f), 4f, 6f, 0.55f);
            Shake(_knockoutImpulse);
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
