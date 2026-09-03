using System;
using System.Collections.Generic;
using MessagePipe;
using PoRumble.Models;
using UnityEngine;
using VContainer;

namespace PoRumble.Systems
{
    /// <summary>Turns landed punches into health loss and elimination.</summary>
    public sealed class CombatSystem : IDisposable
    {
        private readonly MatchModel _match;
        private readonly IPublisher<BoxerDamagedMessage> _damagedPublisher;
        private readonly IPublisher<BoxerEliminatedMessage> _eliminatedPublisher;
        private readonly IDisposable _subscription;
        private readonly IDisposable _blockedSubscription;
        private readonly BoxerConfig _config;

        [Inject]
        public CombatSystem(
            MatchModel match,
            BoxerConfig config,
            ISubscriber<PunchLandedMessage> punchSubscriber,
            ISubscriber<PunchBlockedMessage> blockedSubscriber,
            IPublisher<BoxerDamagedMessage> damagedPublisher,
            IPublisher<BoxerEliminatedMessage> eliminatedPublisher)
        {
            _match = match;
            _config = config;
            _damagedPublisher = damagedPublisher;
            _eliminatedPublisher = eliminatedPublisher;
            _subscription = punchSubscriber.Subscribe(OnPunchLanded);
            _blockedSubscription = blockedSubscriber.Subscribe(OnPunchBlocked);
        }

        /// <summary>Taking a punch on the gloves still costs breath, so turtling is not free.</summary>
        private void OnPunchBlocked(PunchBlockedMessage message)
        {
            BoxerModel blocker = FindBoxer(message.BlockerId);

            if (blocker == null || !blocker.IsAlive.Value)
            {
                return;
            }

            blocker.Stamina.Value = Mathf.Clamp01(blocker.Stamina.Value - _config.BlockStaminaCost);
        }

        private void OnPunchLanded(PunchLandedMessage message)
        {
            // Once the match is decided, in-flight punches must not keep landing.
            if (_match.Phase.Value == MatchPhase.Ended)
            {
                return;
            }

            BoxerModel target = FindBoxer(message.TargetId);

            if (target == null || !target.IsAlive.Value)
            {
                return;
            }

            target.ApplyDamage(message.Damage);
            AccumulateStun(target, message.Damage);
            ApplyKnockback(target, message);
            _damagedPublisher.Publish(new BoxerDamagedMessage(target.Id, target.Health.Value));

            if (target.Health.Value > 0)
            {
                return;
            }

            // Eliminate() returns false if something already eliminated this boxer,
            // which keeps the elimination message strictly one-per-boxer.
            if (target.Eliminate())
            {
                _eliminatedPublisher.Publish(new BoxerEliminatedMessage(target.Id, message.AttackerId));
            }
        }

        /// <summary>
        /// Banks the trauma a landed punch did, on top of the health it took.
        ///
        /// Scored off the damage actually dealt, which already has the attacker's power and
        /// the target's chin folded into it - so a granite-chinned fighter is harder to wobble
        /// for exactly the same reason it is harder to hurt, without a second attribute
        /// saying so.
        ///
        /// Capped, so a haymaker cannot bank a wobble that outlasts the exchange that earned
        /// it: the point of the mechanic is a window to be exploited, not a stun-lock.
        /// </summary>
        private void AccumulateStun(BoxerModel target, int damage)
        {
            target.Stun = Mathf.Min(_config.MaxStun, target.Stun + _config.StunPerDamage * damage);
        }

        /// <summary>
        /// Drives the target backwards off a landed punch. Added to velocity rather than
        /// teleporting the position, so momentum and the ring clamp both still apply and the
        /// shove decays the way the original's "reel back slightly" reads.
        /// </summary>
        private void ApplyKnockback(BoxerModel target, PunchLandedMessage message)
        {
            BoxerModel attacker = FindBoxer(message.AttackerId);
            Vector2 direction = attacker != null
                ? target.Position - attacker.Position
                : target.Position - message.Position;

            if (direction.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            // A haymaker should visibly throw someone, not merely hurt more. The damage is
            // already scaled by charge, so this is the extra shove on top of that.
            float chargeScale = 1f + message.ChargeLevel * (_config.ChargeKnockbackMultiplier - 1f);

            target.Velocity += direction.normalized
                               * (_config.KnockbackPerDamage * message.Damage * chargeScale);
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
            _subscription.Dispose();
            _blockedSubscription.Dispose();
        }
    }
}
