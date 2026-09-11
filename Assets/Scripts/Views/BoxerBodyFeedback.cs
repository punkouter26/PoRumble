using System.Collections.Generic;
using PoRumble.Models;
using UnityEngine;

namespace PoRumble.Views
{
    /// <summary>
    /// Everything a fighter's own body produces while it moves: dust off the canvas,
    /// footsteps, breath once they are spent, and the thud of hitting the ropes.
    ///
    /// Split out of <see cref="CombatFeedbackView"/>, which had grown to own two unrelated
    /// jobs. The rest of that class reacts to punches - one landed punch is a single artistic
    /// decision expressed as audio, particles, a light flash, a shake and hitstop together,
    /// and it belongs in one place. This is the other job: a per-frame pass over the roster
    /// driven by position and velocity, which has nothing to do with impacts and was only
    /// living there because it needed the same voice pool.
    ///
    /// A plain class rather than a second MonoBehaviour on purpose. The tuning is authored on
    /// the CombatFeedback object in the scene, and moving serialized fields onto a new
    /// component means re-entering every value by hand - the exact failure this project
    /// documents as silent. The fields stay where they are and are handed over here.
    /// </summary>
    internal sealed class BoxerBodyFeedback
    {
        /// <summary>
        /// The rates and thresholds, passed in from the serialized fields that already carry
        /// them so the scene stays the one place they are authored.
        /// </summary>
        internal readonly struct Tuning
        {
            internal readonly float DustSpeedThreshold;
            internal readonly float DustInterval;
            internal readonly float StepInterval;
            internal readonly float StepVolume;
            internal readonly float BreathStaminaThreshold;
            internal readonly float BreathInterval;
            internal readonly float BreathVolume;
            internal readonly float RopeContactMargin;
            internal readonly float RopeSpeedThreshold;
            internal readonly float RopeInterval;
            internal readonly float RopeVolume;

            /// <summary>How far a body sound carries, already resolved to world units.</summary>
            internal readonly float Earshot;

            internal Tuning(
                float dustSpeedThreshold,
                float dustInterval,
                float stepInterval,
                float stepVolume,
                float breathStaminaThreshold,
                float breathInterval,
                float breathVolume,
                float ropeContactMargin,
                float ropeSpeedThreshold,
                float ropeInterval,
                float ropeVolume,
                float earshot)
            {
                DustSpeedThreshold = dustSpeedThreshold;
                DustInterval = dustInterval;
                StepInterval = stepInterval;
                StepVolume = stepVolume;
                BreathStaminaThreshold = breathStaminaThreshold;
                BreathInterval = breathInterval;
                BreathVolume = breathVolume;
                RopeContactMargin = ropeContactMargin;
                RopeSpeedThreshold = ropeSpeedThreshold;
                RopeInterval = ropeInterval;
                RopeVolume = ropeVolume;
                Earshot = earshot;
            }
        }

        private readonly MatchModel _match;
        private readonly SpatialVoicePool _voices;
        private readonly ParticleSystem _footDust;
        private readonly AudioClip[] _stepClips;
        private readonly AudioClip[] _breathClips;
        private readonly AudioClip[] _ropeClips;
        private readonly Tuning _tuning;

        private Transform _listener;

        private float[] _dustTimers;
        private float[] _stepTimers;
        private float[] _breathTimers;
        private float[] _ropeTimers;

        /// <summary>
        /// A local xorshift. Kept off UnityEngine.Random for the same reason the scripted
        /// brains are: presentation must not advance a sequence the simulation draws from, or
        /// a training run stops being reproducible because something made a noise.
        /// </summary>
        private uint _randomState = 0x6C078965;

        internal BoxerBodyFeedback(
            MatchModel match,
            SpatialVoicePool voices,
            ParticleSystem footDust,
            AudioClip[] stepClips,
            AudioClip[] breathClips,
            AudioClip[] ropeClips,
            Tuning tuning)
        {
            _match = match;
            _voices = voices;
            _footDust = footDust;
            _stepClips = stepClips;
            _breathClips = breathClips;
            _ropeClips = ropeClips;
            _tuning = tuning;
        }

        /// <summary>The ear every body sound is judged against for audibility.</summary>
        internal void SetListener(Transform listener) => _listener = listener;

        /// <summary>
        /// One pass over the roster rather than four. All of it is rate-limited per fighter off
        /// the same position and velocity, and splitting it into a loop each would mean four
        /// walks of the same list recomputing the same speed.
        ///
        /// Footwork was the one thing a fighter did constantly that produced no feedback at
        /// all - a boxer crossing the ring looked and sounded exactly like a boxer standing
        /// still but translating.
        /// </summary>
        internal void Tick(float delta)
        {
            if (_match == null)
            {
                return;
            }

            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            EnsureTimers(boxers.Count);

            Vector2 ears = _listener == null ? Vector2.zero : (Vector2)_listener.position;
            float earshotSquared = _tuning.Earshot * _tuning.Earshot;

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
        /// Lazily from the tick rather than once at construction, for the reason the telemetry
        /// board already documents: the roster is not populated until SpawnSystem has run, and
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
            if (_footDust == null || speed < _tuning.DustSpeedThreshold)
            {
                return;
            }

            _dustTimers[index] -= delta;

            if (_dustTimers[index] > 0f)
            {
                return;
            }

            _dustTimers[index] = _tuning.DustInterval;

            // Behind the fighter rather than under them: dust is what the foot pushed
            // away, and emitting it at the centre just paints a halo around the sprite.
            Vector2 trail = boxer.Position - boxer.Velocity.normalized * 0.35f;

            ParticleSystem.EmitParams emit = new();
            emit.position = new Vector3(trail.x, trail.y, 0f);
            emit.startSize = Mathf.Lerp(
                0.12f, 0.30f, Mathf.InverseLerp(_tuning.DustSpeedThreshold, 6f, speed));
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
            if (!audible || speed < _tuning.DustSpeedThreshold)
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
            float pace = Mathf.InverseLerp(_tuning.DustSpeedThreshold, 6f, speed);
            _stepTimers[index] = Mathf.Lerp(_tuning.StepInterval, _tuning.StepInterval * 0.6f, pace);

            _voices?.PlayAt(
                PickFrom(_stepClips),
                boxer.Position,
                1f,
                _tuning.StepVolume * Mathf.Lerp(0.6f, 1f, pace));
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
            if (!audible || boxer.Stamina.Value > _tuning.BreathStaminaThreshold)
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
            float spent = 1f - Mathf.InverseLerp(0f, _tuning.BreathStaminaThreshold, boxer.Stamina.Value);
            _breathTimers[index] = Mathf.Lerp(_tuning.BreathInterval, _tuning.BreathInterval * 0.55f, spent);

            _voices?.PlayAt(
                PickFrom(_breathClips),
                boxer.Position,
                Mathf.Lerp(1.05f, 0.92f, spent),
                _tuning.BreathVolume * Mathf.Lerp(0.5f, 1f, spent));
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

            if (!audible || speed < _tuning.RopeSpeedThreshold)
            {
                return;
            }

            Vector2 extent = _match.ArenaHalfExtent;
            Vector2 position = boxer.Position;

            bool intoVertical = Mathf.Abs(position.x) >= extent.x - _tuning.RopeContactMargin
                                && Mathf.Sign(boxer.Velocity.x) == Mathf.Sign(position.x);

            bool intoHorizontal = Mathf.Abs(position.y) >= extent.y - _tuning.RopeContactMargin
                                  && Mathf.Sign(boxer.Velocity.y) == Mathf.Sign(position.y);

            // Moving *into* the boundary, not merely near it. A fighter working along the
            // ropes is touching them constantly and is not hitting them; what makes a thud is
            // the direction of travel, which is why the velocity sign is part of the test.
            if (!intoVertical && !intoHorizontal)
            {
                return;
            }

            _ropeTimers[index] = _tuning.RopeInterval;

            float force = Mathf.InverseLerp(_tuning.RopeSpeedThreshold, 7f, speed);

            _voices?.PlayAt(
                PickFrom(_ropeClips),
                position,
                Mathf.Lerp(1.08f, 0.9f, force),
                _tuning.RopeVolume * Mathf.Lerp(0.55f, 1f, force));

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

        private float NextUnit()
        {
            _randomState ^= _randomState << 13;
            _randomState ^= _randomState >> 17;
            _randomState ^= _randomState << 5;
            return (_randomState & 0xFFFFFF) / (float)0x800000 - 1f;
        }
    }
}
