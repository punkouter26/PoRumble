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

        // Hits are buffered for the whole tick so that punches thrown on the same tick all
        // resolve against the state at the start of that tick. Without this, whichever boxer
        // happened to be ticked first could kill the other before their punch was counted,
        // making simultaneous knockouts impossible. Pre-allocated to keep FixedUpdate GC-free.
        private readonly List<PunchLandedMessage> _pendingHits = new(16);

        [Inject]
        public BoxerSystem(
            MatchModel match,
            BoxerConfig config,
            IPublisher<PunchLandedMessage> punchPublisher)
        {
            _match = match;
            _config = config;
            _punchPublisher = punchPublisher;
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
                boxer.Facing = aimDirection.normalized;
            }
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

            ArmModel requested = side == ArmSide.Left ? boxer.LeftArm : boxer.RightArm;

            if (requested.CanPunch)
            {
                requested.TryPunch();
                return true;
            }

            ArmModel other = side == ArmSide.Left ? boxer.RightArm : boxer.LeftArm;

            if (other.CanPunch)
            {
                other.TryPunch();
                return true;
            }

            return false;
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

                boxer.Position = ClampToArena(
                    boxer.Position + boxer.MoveInput * (_config.MoveSpeed * deltaTime));

                TickArm(boxer, boxer.LeftArm, deltaTime);
                TickArm(boxer, boxer.RightArm, deltaTime);
            }

            for (int hitIndex = 0; hitIndex < _pendingHits.Count; hitIndex++)
            {
                _punchPublisher.Publish(_pendingHits[hitIndex]);
            }

            _pendingHits.Clear();
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
            arm.Tick(
                deltaTime,
                _config.ArmExtendDuration,
                _config.ArmRetractDuration,
                _config.ArmCooldownDuration);

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

                _pendingHits.Add(new PunchLandedMessage(
                    attacker.Id,
                    target.Id,
                    result.Damage,
                    result.IsCloseRange,
                    glovePosition));

                return;
            }
        }

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
