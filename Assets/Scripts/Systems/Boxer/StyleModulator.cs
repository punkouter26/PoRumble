using System.Collections.Generic;
using PoRumble.Models;
using UnityEngine;

namespace PoRumble.Systems
{
    /// <summary>
    /// Gives one fighter a personality without retraining anything.
    ///
    /// Every policy-driven boxer in the ring runs the same network — PoRumbleBoxer.onnx is a
    /// single set of weights — so left alone, ten of them fight identically. Training six
    /// separate policies is the honest answer and an enormous one; growing the action vector
    /// so a style could be an input stops the compiled model loading at all. What is left is
    /// this: take the actions the shared network produced and bend them on the way to the
    /// boxer, and reach the two mechanics that were already built as side channels.
    ///
    /// The aim is deliberately never touched. Pointing at an opponent is the one thing the
    /// network is genuinely good at, and rotating it produces a worse fighter rather than a
    /// different one. Pressure, punch volume, haymakers and slips are all decisions *about*
    /// an aim the policy has already found.
    ///
    /// Inference only. Training scenes never build a modulator, so a run still learns against
    /// the unmodified policy and checkpoints stay comparable across the curriculum.
    /// </summary>
    public sealed class StyleModulator
    {
        private readonly BoxerConfig _config;
        private readonly FighterStyle _style;

        /// <summary>
        /// Which way this fighter prefers to circle. Fixed per fighter rather than rolled each
        /// tick, because a boxer that reversed direction every decision reads as a twitch
        /// rather than as footwork.
        /// </summary>
        private readonly float _circleSign;

        /// <summary>
        /// Deterministic per-boxer noise, for the same reason the scripted brain owns one:
        /// UnityEngine.Random is global mutable state, and a fighter reading it would make
        /// every match depend on whatever else happened to draw a number that frame.
        /// </summary>
        private uint _randomState;

        private bool _charging;
        private float _chargeHoldRemaining;

        /// <summary>How long a fighter leans on a haymaker once it decides to throw one.</summary>
        private const float CHARGE_HOLD_SECONDS = 0.6f;

        /// <summary>How square a fighter must be for an opportunist punch to be worth throwing.</summary>
        private const float OPENING_ALIGNMENT = 0.9f;

        public StyleModulator(BoxerConfig config, FighterStyle style, int seed)
        {
            _config = config;
            _style = style;

            // Never zero: an xorshift seeded with zero only ever produces zero.
            _randomState = (uint)(seed * 747796405 + 2891336453) | 1u;
            _circleSign = (seed & 1) == 0 ? 1f : -1f;
        }

        /// <summary>
        /// Bends one decision's worth of policy output into what this fighter actually does.
        ///
        /// <paramref name="deltaTime"/> is the interval between decisions, not a physics step:
        /// the policy decides once every DecisionPeriod ticks, and rates scaled by the wrong
        /// one come out five times too fast.
        /// </summary>
        public BoxerIntent Modulate(in BoxerIntent policy, MatchModel match, int boxerId, float deltaTime)
        {
            BoxerModel self = FindBoxer(match, boxerId);

            if (self == null || !self.IsAlive.Value)
            {
                return policy;
            }

            BoxerModel target = FindNearestOpponent(match, self, out float distance);

            Vector2 move = ShapeMovement(policy.Move, self);
            float openingRange = _config.ArmReach + _config.HeadOffset + _config.BodyRadius;
            bool opening = target != null && distance <= openingRange && IsSquareTo(self, target);

            bool punchLeft = GatePunch(policy.PunchLeft);
            bool punchRight = GatePunch(policy.PunchRight);

            // Volume. An opportunist throws punches the network passed on, which is what
            // separates a pressure fighter from the policy's own measured rhythm.
            if (!punchLeft && !punchRight && opening && NextFloat() < _style.Opportunism)
            {
                punchLeft = true;
            }

            bool charge = DecideCharge(self, opening, deltaTime);

            // A charging boxer cannot jab - BoxerSystem refuses ordinary punches while the
            // wind-up is held - so asking for both would silently throw the punch away.
            if (charge)
            {
                punchLeft = false;
                punchRight = false;
            }

            return new BoxerIntent(move, policy.Aim, punchLeft, punchRight, charge, DecideDodge(match, self));
        }

        /// <summary>
        /// Adds this fighter's forward pressure and circling to the policy's own movement.
        ///
        /// Added rather than replaced: the network still decides where to go and the style
        /// leans on that decision. Replacing it gives fighters that walk into the ropes,
        /// because nothing is reading the ring any more.
        /// </summary>
        private Vector2 ShapeMovement(Vector2 policyMove, BoxerModel self)
        {
            if (_style.Pressure == 0f && _style.Circling == 0f)
            {
                return policyMove;
            }

            Vector2 facing = self.Facing.normalized;
            Vector2 lateral = new(-facing.y, facing.x);
            Vector2 shaped = policyMove
                             + facing * _style.Pressure
                             + lateral * (_style.Circling * _circleSign);

            return Vector2.ClampMagnitude(shaped, 1f);
        }

        /// <summary>Drops a fraction of the policy's punches, which is what patience looks like.</summary>
        private bool GatePunch(bool requested)
        {
            if (!requested || _style.PunchGate >= 1f)
            {
                return requested;
            }

            return NextFloat() < _style.PunchGate;
        }

        /// <summary>
        /// Decides whether to hold a haymaker. Mirrors the scripted brain deliberately: once
        /// started it is held for a fixed spell so the wind-up reaches useful power, then
        /// released by returning false.
        /// </summary>
        private bool DecideCharge(BoxerModel self, bool opening, float deltaTime)
        {
            if (_charging)
            {
                _chargeHoldRemaining -= deltaTime;

                if (_chargeHoldRemaining <= 0f || self.Stamina.Value <= _config.ChargeStaminaCost * 2f)
                {
                    _charging = false;
                    return false;
                }

                return true;
            }

            if (_style.ChargeChance <= 0f || !opening || deltaTime <= 0f)
            {
                return false;
            }

            // Spending a counter window on a slow haymaker wastes it: the window closes long
            // before the swing arrives.
            if (self.HasCounterWindow)
            {
                return false;
            }

            // Scaled by deltaTime so the decision rate does not depend on the tick rate.
            if (NextFloat() >= _style.ChargeChance * deltaTime * 10f)
            {
                return false;
            }

            _charging = true;
            _chargeHoldRemaining = CHARGE_HOLD_SECONDS;
            return true;
        }

        /// <summary>
        /// Decides whether to slip a punch this fighter can see coming.
        ///
        /// Rolled fresh every decision rather than latched, because BoxerSystem.Dodge owns the
        /// cooldown: asking again mid-slip is simply refused, and asking repeatedly while a
        /// haymaker is cocked eventually gets the fighter out of the way, which is exactly the
        /// reaction the telegraph exists to provoke.
        /// </summary>
        private bool DecideDodge(MatchModel match, BoxerModel self)
        {
            if (_style.DodgeChance <= 0f || !self.CanDodge)
            {
                return false;
            }

            float threatRange =
                (_config.ArmReach + _config.HeadOffset + _config.BodyRadius) * _config.DodgeThreatRangeScale;

            if (!ThreatMath.IsPunchIncoming(match.Boxers, self, threatRange, _config.MinChargeToRelease))
            {
                return false;
            }

            return NextFloat() < _style.DodgeChance;
        }

        /// <summary>True when the fighter is square enough for a punch to reach the face arc.</summary>
        private static bool IsSquareTo(BoxerModel self, BoxerModel target)
        {
            Vector2 toTarget = target.Position - self.Position;

            if (toTarget.sqrMagnitude <= Mathf.Epsilon)
            {
                return false;
            }

            return Vector2.Dot(self.Facing.normalized, toTarget.normalized) >= OPENING_ALIGNMENT;
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
