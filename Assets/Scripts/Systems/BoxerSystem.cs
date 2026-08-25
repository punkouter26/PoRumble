using System;
using System.Collections.Generic;
using MessagePipe;
using PoRumble.Models;
using UnityEngine;
using VContainer;

namespace PoRumble.Systems
{
    /// <summary>
    /// Owns every BoxerModel. Applies input, ticks arm state machines, and resolves punches
    /// against the roster directly — no physics query, so results are deterministic and
    /// allocation-free.
    /// </summary>
    public sealed class BoxerSystem : IDisposable
    {
        private readonly MatchModel _match;
        private readonly BoxerConfig _config;
        private readonly IPublisher<PunchLandedMessage> _punchPublisher;
        private readonly IPublisher<PunchEvadedMessage> _evadedPublisher;
        private readonly IPublisher<PunchBlockedMessage> _blockedPublisher;
        private readonly IPublisher<HaymakerThrownMessage> _haymakerPublisher;

        // Hits are buffered for the whole tick so that punches thrown on the same tick all
        // resolve against the state at the start of that tick. Without this, whichever boxer
        // happened to be ticked first could kill the other before their punch was counted,
        // making simultaneous knockouts impossible. Pre-allocated to keep FixedUpdate GC-free.
        private readonly List<PunchLandedMessage> _pendingHits = new(16);

        [Inject]
        public BoxerSystem(
            MatchModel match,
            BoxerConfig config,
            IPublisher<PunchLandedMessage> punchPublisher,
            IPublisher<PunchEvadedMessage> evadedPublisher,
            IPublisher<PunchBlockedMessage> blockedPublisher,
            IPublisher<HaymakerThrownMessage> haymakerPublisher)
        {
            _match = match;
            _config = config;
            _punchPublisher = punchPublisher;
            _evadedPublisher = evadedPublisher;
            _blockedPublisher = blockedPublisher;
            _haymakerPublisher = haymakerPublisher;
        }

        public void SetMoveInput(int boxerId, Vector2 moveInput)
        {
            BoxerModel boxer = FindBoxer(boxerId);

            if (boxer == null || !boxer.IsAlive.Value)
            {
                return;
            }

            boxer.MoveInput = Vector2.ClampMagnitude(moveInput, 1f);
        }

        public void SetAim(int boxerId, Vector2 aimDirection)
        {
            BoxerModel boxer = FindBoxer(boxerId);

            if (boxer == null || !boxer.IsAlive.Value)
            {
                return;
            }

            if (aimDirection.sqrMagnitude > Mathf.Epsilon)
            {
                boxer.DesiredFacing = aimDirection.normalized;
            }
        }

        /// <summary>
        /// Holds or releases the haymaker wind-up.
        ///
        /// Deliberately a method of its own rather than a third discrete action branch: the
        /// trained policy in PoRumbleBoxer.onnx is compiled against exactly four continuous
        /// and two discrete actions, and growing that vector would stop the model loading at
        /// all. Charging therefore rides a side channel that human and scripted controllers
        /// use, and the ML action space is left byte-identical.
        /// </summary>
        public void SetCharge(int boxerId, bool held)
        {
            BoxerModel boxer = FindBoxer(boxerId);

            if (boxer == null || !boxer.IsAlive.Value)
            {
                return;
            }

            boxer.ChargeInput = held;
        }

        /// <summary>
        /// Throws a punch. Uses the requested arm when it is ready; if that arm is still
        /// swinging or recovering, the other one throws instead. Held punch input therefore
        /// alternates left and right, which is the juggling rhythm of the original.
        ///
        /// Returns false only when neither arm can throw, so callers can tell a real punch
        /// from a wasted input.
        /// </summary>
        public bool Punch(int boxerId, ArmSide side)
        {
            BoxerModel boxer = FindBoxer(boxerId);

            if (boxer == null || !boxer.IsAlive.Value)
            {
                return false;
            }

            // Winding up is a commitment: you cannot keep jabbing out of a cocked haymaker,
            // otherwise charging would be strictly better than not charging.
            if (boxer.ChargeInput)
            {
                return false;
            }

            return ThrowPunch(boxer, side, 0f);
        }

        /// <summary>
        /// True while either fist is away from the body - travelling out or drawing back.
        ///
        /// A boxer throws one punch at a time. Both arms firing together let a fighter double
        /// the damage it could put out for the same stamina, and read on screen as a shove
        /// rather than a punch.
        /// </summary>
        private static bool IsAnyArmOut(BoxerModel boxer)
        {
            return IsArmOut(boxer.LeftArm) || IsArmOut(boxer.RightArm);
        }

        private static bool IsArmOut(ArmModel arm)
        {
            // Cooling down does not count: the fist is already back at the guard by then, which
            // is what keeps held input alternating between the two arms.
            return arm.Phase == ArmPhase.Extending || arm.Phase == ArmPhase.Retracting;
        }

        /// <summary>Starts a swing on the requested arm, or the other one if it is busy.</summary>
        private bool ThrowPunch(BoxerModel boxer, ArmSide side, float chargeLevel)
        {
            // One fist out at a time, whoever is asking - human, scripted brain or policy.
            if (IsAnyArmOut(boxer))
            {
                return false;
            }

            ArmModel requested = side == ArmSide.Left ? boxer.LeftArm : boxer.RightArm;

            if (requested.CanPunch)
            {
                requested.TryPunch(chargeLevel);
                SpendPunchStamina(boxer);
                return true;
            }

            ArmModel other = side == ArmSide.Left ? boxer.RightArm : boxer.LeftArm;

            if (other.CanPunch)
            {
                other.TryPunch(chargeLevel);
                SpendPunchStamina(boxer);
                return true;
            }

            return false;
        }

        private const float WORKING_RECOVERY_SCALE = 0.5f;

        private void SpendPunchStamina(BoxerModel boxer)
        {
            boxer.Stamina.Value = Mathf.Clamp01(boxer.Stamina.Value - _config.PunchStaminaCost);
        }

        /// <summary>Advances movement and both arms for every living boxer.</summary>
        public void Tick(float deltaTime)
        {
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;
            _pendingHits.Clear();

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel boxer = boxers[boxerIndex];

                if (!boxer.IsAlive.Value)
                {
                    continue;
                }

                TickMovement(boxer, deltaTime);
                TickStamina(boxer, deltaTime);
                TickCounterWindow(boxer, deltaTime);
                TickCharge(boxer, deltaTime);

                TickArm(boxer, boxer.LeftArm, deltaTime);
                TickArm(boxer, boxer.RightArm, deltaTime);
            }

            ResolveOverlaps();

            for (int hitIndex = 0; hitIndex < _pendingHits.Count; hitIndex++)
            {
                _punchPublisher.Publish(_pendingHits[hitIndex]);
            }

            _pendingHits.Clear();
        }

        /// <summary>
        /// Moves a boxer with momentum and a finite turn rate, so it accelerates and pivots
        /// like a person rather than snapping to a new heading. A tired boxer is slower.
        /// </summary>
        private void TickMovement(BoxerModel boxer, float deltaTime)
        {
            float effort = StaminaScale(boxer);

            // A cocked haymaker plants the feet. Without this cost, charging would be free
            // and there would be no reason ever to throw an ordinary punch.
            if (boxer.ChargeInput && boxer.Charge.Value > 0f)
            {
                effort *= _config.ChargeMoveScale;
            }

            Vector2 desired = ScaleByStance(boxer, boxer.MoveInput) * (_config.MoveSpeed * effort);
            float rate = desired.sqrMagnitude > Mathf.Epsilon ? _config.Acceleration : _config.Deceleration;

            boxer.Velocity = Vector2.MoveTowards(boxer.Velocity, desired, rate * deltaTime);
            boxer.Position = ClampToArena(boxer.Position + boxer.Velocity * deltaTime);

            // Turn toward the aim heading at a finite rate; nobody spins on the spot.
            if (boxer.Facing.sqrMagnitude > Mathf.Epsilon && boxer.DesiredFacing.sqrMagnitude > Mathf.Epsilon)
            {
                float commitment = IsCommitted(boxer) ? _config.CommittedTurnScale : 1f;
                float maxTurn = _config.TurnSpeedDegrees * effort * commitment * deltaTime;
                float delta = Vector2.SignedAngle(boxer.Facing, boxer.DesiredFacing);
                float applied = Mathf.Clamp(delta, -maxTurn, maxTurn);
                boxer.ApplyTurn(Rotate(boxer.Facing.normalized, applied));
            }
        }

        /// <summary>
        /// Caps travel per direction relative to where the boxer is looking. A boxer is
        /// fastest going forward, slower sidestepping and slowest backing up; an unscaled
        /// input lets one sprint backwards as fast as it advances, which is the single
        /// least human thing a top-down fighter can do.
        /// </summary>
        private Vector2 ScaleByStance(BoxerModel boxer, Vector2 moveInput)
        {
            if (boxer.Facing.sqrMagnitude <= Mathf.Epsilon)
            {
                return moveInput;
            }

            Vector2 facing = boxer.Facing.normalized;
            Vector2 lateral = new(-facing.y, facing.x);

            float forward = Vector2.Dot(moveInput, facing);
            float sideways = Vector2.Dot(moveInput, lateral);
            float forwardScale = forward >= 0f ? 1f : _config.RetreatSpeedScale;

            return facing * (forward * forwardScale) + lateral * (sideways * _config.LateralSpeedScale);
        }

        /// <summary>
        /// True while the shoulders are already committed - a punch on its way out, or a
        /// haymaker held cocked. Retracting deliberately does not count: that is the recovery,
        /// and a boxer can start turning again as the arm comes back.
        /// </summary>
        private static bool IsCommitted(BoxerModel boxer)
        {
            return boxer.ChargeInput
                   || boxer.LeftArm.Phase == ArmPhase.Extending
                   || boxer.RightArm.Phase == ArmPhase.Extending;
        }

        private static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            return new Vector2(value.x * cos - value.y * sin, value.x * sin + value.y * cos);
        }

        /// <summary>Drains stamina for effort thrown, and pays it back while standing off.</summary>
        private void TickStamina(BoxerModel boxer, float deltaTime)
        {
            bool working = boxer.LeftArm.Phase != ArmPhase.Idle || boxer.RightArm.Phase != ArmPhase.Idle;
            float moveDrain = boxer.Velocity.magnitude / Mathf.Max(0.01f, _config.MoveSpeed) * _config.MoveStaminaCost;

            // Breath comes back even mid-exchange, just far slower than when standing off.
            float recovery = _config.StaminaRecovery * (working ? WORKING_RECOVERY_SCALE : 1f);
            float delta = recovery - moveDrain;

            boxer.Stamina.Value = Mathf.Clamp01(boxer.Stamina.Value + delta * deltaTime);
        }

        /// <summary>Scales effort by how fresh the boxer is: 1 when rested, less when spent.</summary>
        private float StaminaScale(BoxerModel boxer)
        {
            return Mathf.Lerp(_config.ExhaustedPenalty, 1f, boxer.Stamina.Value);
        }

        /// <summary>
        /// Builds the haymaker while the button is held and throws it on release.
        ///
        /// A charge that never reached the minimum is released as an ordinary punch rather
        /// than being thrown away, so holding the button briefly is never worse than tapping
        /// the punch key.
        /// </summary>
        private void TickCharge(BoxerModel boxer, float deltaTime)
        {
            if (boxer.ChargeInput)
            {
                // Only build while an arm is actually free to deliver it. Charging against
                // two busy arms would bank power the boxer cannot spend.
                if (boxer.LeftArm.CanPunch || boxer.RightArm.CanPunch)
                {
                    float rate = _config.ChargeDuration <= 0f
                        ? 1f
                        : deltaTime / _config.ChargeDuration;

                    boxer.Charge.Value = Mathf.Clamp01(boxer.Charge.Value + rate);
                }

                boxer.LeftArm.SetWindup(boxer.Charge.Value);
                boxer.RightArm.SetWindup(boxer.Charge.Value);
                return;
            }

            if (boxer.Charge.Value <= 0f)
            {
                return;
            }

            ReleaseCharge(boxer);
        }

        private void ReleaseCharge(BoxerModel boxer)
        {
            float charge = boxer.Charge.Value;
            float level = charge >= _config.MinChargeToRelease ? charge : 0f;

            // Attempted before the charge is cleared. With only one fist allowed out at a
            // time a release can land on a tick where the other arm is still travelling, and
            // banking the wind-up until an arm frees up is far better than silently eating a
            // haymaker the player already paid stamina and mobility for.
            if (!ThrowPunch(boxer, ArmSide.Right, level))
            {
                return;
            }

            boxer.Charge.Value = 0f;
            boxer.LeftArm.SetWindup(0f);
            boxer.RightArm.SetWindup(0f);

            if (level <= 0f)
            {
                return;
            }

            boxer.Stamina.Value = Mathf.Clamp01(boxer.Stamina.Value - _config.ChargeStaminaCost * level);
            _haymakerPublisher.Publish(new HaymakerThrownMessage(boxer.Id, boxer.Position, level));
        }

        /// <summary>Runs down the window opened by a block.</summary>
        private static void TickCounterWindow(BoxerModel boxer, float deltaTime)
        {
            if (boxer.CounterWindow <= 0f)
            {
                return;
            }

            boxer.CounterWindow = Mathf.Max(0f, boxer.CounterWindow - deltaTime);
        }

        /// <summary>
        /// Pushes overlapping boxers apart. Bodies are circles of BodyRadius; positions are
        /// model-driven, so nothing else stops two fighters occupying the same spot.
        /// </summary>
        private void ResolveOverlaps()
        {
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;
            float minDistance = _config.BodyRadius * 2f;
            float minDistanceSqr = minDistance * minDistance;

            for (int firstIndex = 0; firstIndex < boxers.Count; firstIndex++)
            {
                BoxerModel first = boxers[firstIndex];

                if (!first.IsAlive.Value)
                {
                    continue;
                }

                for (int secondIndex = firstIndex + 1; secondIndex < boxers.Count; secondIndex++)
                {
                    BoxerModel second = boxers[secondIndex];

                    if (!second.IsAlive.Value)
                    {
                        continue;
                    }

                    Vector2 offset = second.Position - first.Position;
                    float distanceSqr = offset.sqrMagnitude;

                    if (distanceSqr >= minDistanceSqr)
                    {
                        continue;
                    }

                    // Exactly coincident: nudge along a fixed axis so the split is deterministic.
                    Vector2 direction = distanceSqr > Mathf.Epsilon
                        ? offset / Mathf.Sqrt(distanceSqr)
                        : Vector2.right;

                    float overlap = minDistance - Mathf.Sqrt(Mathf.Max(distanceSqr, 0f));
                    Vector2 push = direction * (overlap * 0.5f);

                    first.Position = ClampToArena(first.Position - push);
                    second.Position = ClampToArena(second.Position + push);
                }
            }
        }

        /// <summary>
        /// Keeps a boxer inside the ropes. Positions are driven by the model rather than by
        /// physics, so the wall colliders alone would not contain anyone.
        /// </summary>
        private Vector2 ClampToArena(Vector2 position)
        {
            Vector2 limit = _match.ArenaHalfExtent - new Vector2(_config.BodyRadius, _config.BodyRadius);

            return new Vector2(
                Mathf.Clamp(position.x, -limit.x, limit.x),
                Mathf.Clamp(position.y, -limit.y, limit.y));
        }

        private void TickArm(BoxerModel attacker, ArmModel arm, float deltaTime)
        {
            // A spent boxer's arms are heavier: every phase stretches out, so the punch rate
            // falls as stamina does and the two settle at an equilibrium.
            float slow = 1f / Mathf.Max(0.01f, StaminaScale(attacker));

            // A charged swing winds up visibly slower. That delay is the counterplay: it is
            // the window in which an opponent can read the haymaker and step out of it.
            float windup = 1f + arm.ChargeLevel * (_config.ChargeWindupScale - 1f);

            arm.Tick(
                deltaTime,
                _config.ArmExtendDuration * slow * windup,
                _config.ArmRetractDuration * slow,
                _config.ArmCooldownDuration * slow);

            if (arm.ReachedPeakThisTick)
            {
                ResolvePunch(attacker, arm);
            }
        }

        /// <summary>
        /// Tests the glove against every other boxer and applies the first hit found.
        /// One glove damages at most one target per extension.
        /// </summary>
        private void ResolvePunch(BoxerModel attacker, ArmModel arm)
        {
            Vector2 glovePosition = GetGlovePosition(attacker, arm);
            CombatSettings settings = _config.ToCombatSettings();
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel target = boxers[boxerIndex];

                HitResult result = CombatMath.ResolveHit(
                    attacker.Id,
                    attacker.Position,
                    target.Id,
                    target.Position,
                    target.Facing,
                    target.IsAlive.Value,
                    glovePosition,
                    settings);

                if (!result.IsHit)
                {
                    continue;
                }

                // A spent boxer lands softer punches, but never for zero.
                float chargeScale = 1f + arm.ChargeLevel * (_config.ChargeDamageMultiplier - 1f);
                int damage = Mathf.Max(
                    1,
                    Mathf.RoundToInt(result.Damage * StaminaScale(attacker) * chargeScale));

                // Cashing in a block. Consumed on the way out so one block buys exactly one
                // counter, not a free damage bonus for the rest of the window.
                bool isCounter = attacker.HasCounterWindow;

                if (isCounter)
                {
                    damage += _config.CounterDamageBonus;
                    attacker.CounterWindow = 0f;
                }

                _pendingHits.Add(new PunchLandedMessage(
                    attacker.Id,
                    target.Id,
                    damage,
                    result.IsCloseRange,
                    glovePosition,
                    isCounter,
                    arm.ChargeLevel));

                return;
            }

            ReportBlockOrNearMiss(attacker, glovePosition);
        }

        /// <summary>
        /// The punch did not reach a face. Work out whether the defender's gloves stopped it
        /// (a block) or it simply missed a nearby opponent (an evade), so defence is worth
        /// learning and not just aggression. A block outranks an evade: the glove is in front
        /// of the head, so it is checked first.
        /// </summary>
        private void ReportBlockOrNearMiss(BoxerModel attacker, Vector2 glovePosition)
        {
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;
            float blockRange = _config.GloveRadius * 2f;
            float blockRangeSqr = blockRange * blockRange;
            float nearMiss = _config.HeadRadius * NEAR_MISS_SCALE;
            float nearMissSqr = nearMiss * nearMiss;

            BoxerModel nearest = null;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel target = boxers[boxerIndex];

                if (target.Id == attacker.Id || !target.IsAlive.Value)
                {
                    continue;
                }

                // Blocked: the incoming glove ran into one of the defender's own gloves.
                Vector2 leftGlove = GetGlovePosition(target, target.LeftArm);
                Vector2 rightGlove = GetGlovePosition(target, target.RightArm);

                if ((glovePosition - leftGlove).sqrMagnitude <= blockRangeSqr ||
                    (glovePosition - rightGlove).sqrMagnitude <= blockRangeSqr)
                {
                    // Stopping a punch buys a moment in which your own counts for more. This
                    // is what turns holding a guard up from a way to lose slowly into a way
                    // to win: block, then fire back before the window closes.
                    target.CounterWindow = _config.CounterWindowDuration;

                    _blockedPublisher.Publish(new PunchBlockedMessage(attacker.Id, target.Id, glovePosition));
                    return;
                }

                if (nearest == null)
                {
                    Vector2 headCenter = target.Position + target.Facing.normalized * _config.HeadOffset;

                    if ((glovePosition - headCenter).sqrMagnitude <= nearMissSqr)
                    {
                        nearest = target;
                    }
                }
            }

            if (nearest != null)
            {
                _evadedPublisher.Publish(new PunchEvadedMessage(attacker.Id, nearest.Id, glovePosition));
            }
        }

        private const float NEAR_MISS_SCALE = 2.2f;

        /// <summary>Glove tip position for an arm at its current extension.</summary>
        public Vector2 GetGlovePosition(BoxerModel boxer, ArmModel arm)
        {
            Vector2 facing = boxer.Facing.normalized;
            Vector2 lateral = new(-facing.y, facing.x);
            float lateralSign = arm.Side == ArmSide.Left ? 1f : -1f;

            return boxer.Position
                   + facing * (_config.ArmReach * arm.Extension)
                   + lateral * (lateralSign * _config.ArmLateralOffset);
        }

        private BoxerModel FindBoxer(int boxerId)
        {
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

        public void Dispose()
        {
        }
    }
}
