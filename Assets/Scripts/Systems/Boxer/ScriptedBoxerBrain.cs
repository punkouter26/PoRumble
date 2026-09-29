using System.Collections.Generic;
using PoRumble.Models;
using UnityEngine;

namespace PoRumble.Systems
{
    /// <summary>What a controller wants a boxer to do this tick.</summary>
    public readonly struct BoxerIntent
    {
        public readonly Vector2 Move;
        public readonly Vector2 Aim;
        public readonly bool PunchLeft;
        public readonly bool PunchRight;

        /// <summary>Hold the haymaker wind-up. Released the tick this goes back to false.</summary>
        public readonly bool Charge;

        /// <summary>
        /// Start a slip this tick. An edge rather than a held state: BoxerSystem.Dodge owns
        /// the window and the cooldown, so asking twice inside one slip changes nothing.
        /// </summary>
        public readonly bool Dodge;

        public BoxerIntent(Vector2 move, Vector2 aim, bool punchLeft, bool punchRight)
            : this(move, aim, punchLeft, punchRight, false, false)
        {
        }

        public BoxerIntent(Vector2 move, Vector2 aim, bool punchLeft, bool punchRight, bool charge)
            : this(move, aim, punchLeft, punchRight, charge, false)
        {
        }

        public BoxerIntent(
            Vector2 move,
            Vector2 aim,
            bool punchLeft,
            bool punchRight,
            bool charge,
            bool dodge)
        {
            Move = move;
            Aim = aim;
            PunchLeft = punchLeft;
            PunchRight = punchRight;
            Charge = charge;
            Dodge = dodge;
        }

        public static BoxerIntent Idle => new(Vector2.zero, Vector2.zero, false, false, false, false);
    }

    /// <summary>
    /// A hand-written boxer, used as a sparring partner for the learning agents and as the
    /// difficulty ladder the player fights.
    ///
    /// Self-play against a policy that starts out random teaches very little early on: two
    /// flailing boxers rarely produce a clean exchange, so there is almost nothing to learn
    /// from. A competent scripted opponent gives the learner a consistent target from the
    /// first episode.
    ///
    /// Behaviour comes from a <see cref="BrainSettings"/> tier rather than from constants, so
    /// one roster can field a spread of opponents instead of ten identical bots.
    ///
    /// Pure logic on purpose - no Unity components, and its only randomness is a seeded
    /// generator owned by this instance - so its behaviour is deterministic and can be
    /// unit-tested without a scene.
    /// </summary>
    public sealed class ScriptedBoxerBrain
    {
        private readonly BoxerConfig _config;
        private readonly BrainSettings _settings;

        /// <summary>
        /// Deterministic per-boxer noise. UnityEngine.Random is deliberately avoided: it is
        /// global mutable state, so a bot reading it would make every training episode depend
        /// on whatever else happened to draw a number that frame.
        /// </summary>
        private uint _randomState;

        private bool _recovering;

        /// <summary>Seconds until the bot is allowed to react to what it can currently see.</summary>
        private float _reactionTimer;

        private Vector2 _committedAim = Vector2.up;
        private bool _charging;
        private float _chargeHoldRemaining;

        /// <summary>How long a bot leans on a haymaker once it decides to throw one.</summary>
        private const float CHARGE_HOLD_SECONDS = 0.6f;

        public ScriptedBoxerBrain(BoxerConfig config)
            : this(config, BrainSettings.Default, 0)
        {
        }

        public ScriptedBoxerBrain(BoxerConfig config, BrainSettings settings, int seed)
        {
            _config = config;
            _settings = settings;

            // Never zero: an xorshift seeded with zero only ever produces zero.
            _randomState = (uint)(seed * 747796405 + 2891336453) | 1u;
        }

        public BoxerIntent Decide(MatchModel match, int boxerId)
        {
            return Decide(match, boxerId, 0f);
        }

        /// <summary>
        /// Decides this tick's intent. <paramref name="deltaTime"/> drives reaction delay and
        /// haymaker hold; passing zero keeps the old instant-reaction behaviour, which is what
        /// the timing-free unit tests want.
        /// </summary>
        public BoxerIntent Decide(MatchModel match, int boxerId, float deltaTime)
        {
            BoxerModel self = FindBoxer(match, boxerId);

            if (self == null || !self.IsAlive.Value)
            {
                return BoxerIntent.Idle;
            }

            BoxerModel target = FindNearestOpponent(match, self, out float distance);

            if (target == null)
            {
                return BoxerIntent.Idle;
            }

            Vector2 toTarget = target.Position - self.Position;
            Vector2 trueAim = toTarget.sqrMagnitude > Mathf.Epsilon ? toTarget.normalized : self.Facing;

            // Read before anything else this tick: a bot that has spotted an incoming punch
            // should slip it rather than continue with whatever it had planned. Cheap enough
            // to evaluate every decision - it is a loop over a roster of ten.
            bool slip = DecideDodge(match, self);

            // Reaction delay: a weaker tier keeps aiming where the opponent used to be, which
            // is most of what makes it beatable without making it look broken.
            _reactionTimer -= deltaTime;

            if (_reactionTimer <= 0f)
            {
                _committedAim = ApplyAimError(trueAim);
                _reactionTimer = _settings.ReactionDelay;
            }

            if (_committedAim.sqrMagnitude <= Mathf.Epsilon)
            {
                _committedAim = trueAim;
            }

            Vector2 aim = _committedAim;

            // Hysteresis, so the bot commits to breathing rather than flickering in and out.
            if (_recovering && self.Stamina.Value >= _settings.ResumeStamina)
            {
                _recovering = false;
            }
            else if (!_recovering && self.Stamina.Value <= _settings.RecoverStamina)
            {
                _recovering = true;
            }

            float idealRange = _config.ArmReach + _config.HeadOffset;

            if (_recovering)
            {
                _charging = false;
                _chargeHoldRemaining = 0f;

                // Circle away and keep the guard toward the opponent while catching breath.
                Vector2 retreat = -aim;
                Vector2 strafe = new(-aim.y, aim.x);
                return new BoxerIntent((retreat + strafe * 0.6f).normalized, aim, false, false, false, slip);
            }

            // Hold the range where a punch can actually reach the head. An aggressive tier
            // stands closer and is happier to trade.
            float aggressionBias = Mathf.Lerp(1.15f, 0.85f, _settings.Aggression);
            float engage = idealRange * _settings.EngageRangeScale * aggressionBias;
            float breakOff = idealRange * _settings.BreakRangeScale * aggressionBias;
            Vector2 move;

            if (distance > breakOff)
            {
                move = aim;                       // too far: close it down
            }
            else if (distance < engage * 0.75f)
            {
                move = -aim;                      // too close to extend: give ground
            }
            else
            {
                // In the pocket: circle rather than stand still, so it is not a stationary target.
                move = new Vector2(-aim.y, aim.x) * 0.5f;
            }

            // Only throw when square to the target and actually in range; flailing at the air
            // just burns stamina.
            bool aligned = Vector2.Dot(self.Facing.normalized, aim) >= _settings.PunchAlignment;
            bool inRange = distance <= breakOff;
            bool opening = aligned && inRange;

            bool charge = DecideCharge(self, opening, deltaTime);

            // A charging boxer cannot jab: BoxerSystem refuses ordinary punches while the
            // wind-up is held, so asking for both would silently throw the punch away.
            bool throwPunch = opening && !charge;

            return new BoxerIntent(move, aim, throwPunch, throwPunch, charge, slip);
        }

        /// <summary>
        /// Decides whether to slip a punch the bot can see coming.
        ///
        /// Rolled fresh every decision rather than held, because BoxerSystem.Dodge owns the
        /// cooldown: a bot that keeps asking while it is still slipping simply gets refused,
        /// and one that keeps asking while a haymaker is cocked at it eventually gets out of
        /// the way - which is exactly the reaction the telegraph is meant to provoke.
        /// </summary>
        private bool DecideDodge(MatchModel match, BoxerModel self)
        {
            if (_settings.DodgeDiscipline <= 0f || !self.CanDodge)
            {
                return false;
            }

            float threatRange =
                (_config.ArmReach + _config.HeadOffset + _config.BodyRadius) * _config.DodgeThreatRangeScale;

            if (!ThreatMath.IsPunchIncoming(match.Boxers, self, threatRange, _config.MinChargeToRelease))
            {
                return false;
            }

            return NextFloat() < _settings.DodgeDiscipline;
        }

        /// <summary>
        /// Decides whether to hold a haymaker. Once started it is held for a fixed spell so
        /// the wind-up actually reaches useful power, then released by returning false.
        /// </summary>
        private bool DecideCharge(BoxerModel self, bool opening, float deltaTime)
        {
            if (_charging)
            {
                _chargeHoldRemaining -= deltaTime;

                // Let go if the moment passed, the spell elapsed, or breath ran out.
                if (_chargeHoldRemaining <= 0f || self.Stamina.Value <= _settings.RecoverStamina)
                {
                    _charging = false;
                    return false;
                }

                return true;
            }

            if (_settings.ChargeChance <= 0f || !opening || deltaTime <= 0f)
            {
                return false;
            }

            // A bot with counter discipline prefers to spend its counter window on a fast
            // punch rather than a slow haymaker it would never land in time.
            if (self.HasCounterWindow && NextFloat() < _settings.CounterDiscipline)
            {
                return false;
            }

            // Scaled by deltaTime so the decision rate does not depend on the tick rate.
            if (NextFloat() >= _settings.ChargeChance * deltaTime * 10f)
            {
                return false;
            }

            _charging = true;
            _chargeHoldRemaining = CHARGE_HOLD_SECONDS;
            return true;
        }

        /// <summary>Wanders the aim by however inaccurate this tier is.</summary>
        private Vector2 ApplyAimError(Vector2 trueAim)
        {
            float error = 1f - _settings.Accuracy;

            if (error <= 0f)
            {
                return trueAim;
            }

            // Up to 35 degrees off at zero accuracy: enough to miss the face arc regularly
            // without the bot appearing to fight someone who is not there.
            float degrees = (NextFloat() * 2f - 1f) * error * 35f;
            float radians = degrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);

            return new Vector2(
                trueAim.x * cos - trueAim.y * sin,
                trueAim.x * sin + trueAim.y * cos);
        }

        /// <summary>Deterministic xorshift32, in 0..1.</summary>
        private float NextFloat()
        {
            _randomState ^= _randomState << 13;
            _randomState ^= _randomState >> 17;
            _randomState ^= _randomState << 5;
            return (_randomState & 0xFFFFFF) / (float)0x1000000;
        }

        private static BoxerModel FindBoxer(MatchModel match, int boxerId)
        {
            IReadOnlyList<BoxerModel> boxers = match.Boxers;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                if (boxers[boxerIndex].Id == boxerId)
                {
                    return boxers[boxerIndex];
                }
            }

            return null;
        }

        private static BoxerModel FindNearestOpponent(MatchModel match, BoxerModel self, out float distance)
        {
            BoxerModel nearest = null;
            float bestSqr = float.MaxValue;
            IReadOnlyList<BoxerModel> boxers = match.Boxers;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel other = boxers[boxerIndex];

                if (other.Id == self.Id || !other.IsAlive.Value)
                {
                    continue;
                }

                float sqr = (other.Position - self.Position).sqrMagnitude;

                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    nearest = other;
                }
            }

            distance = nearest == null ? 0f : Mathf.Sqrt(bestSqr);
            return nearest;
        }
    }
}
