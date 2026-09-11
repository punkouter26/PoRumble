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
        /// <summary>
        /// How much swelling a punch that took a fighter's entire health bar would leave.
        /// Above 1 so that a match of punishment shuts an eye before the fighter is out — a
        /// mark that only appears at the moment somebody is knocked down arrives too late to
        /// have been worth drawing.
        ///
        /// Measured rather than guessed. At 2.4 a fighter taking everything on one side was
        /// pinned at fully swollen after roughly 40% of their health, which is first blood
        /// rather than a story; 1.6 puts saturation near two-thirds, so the face still has
        /// somewhere to go for most of a fight.
        /// </summary>
        private const float SWELL_PER_HEALTH_SHARE = 1.6f;

        /// <summary>
        /// Share of a full health bar a single punch must take before it can open a cut. Set
        /// where a haymaker or a solid counter clears it and an ordinary punch does not, so a
        /// cut stays the mark of one specific punch rather than of attrition.
        ///
        /// Calibrated against measured matches rather than chosen: an average landed punch in
        /// a ten-way takes about 1.9 of 30 health, so 0.12 opened cuts on half the field
        /// inside a minute and 0.18 produced none at all across a whole match. At 0.15 it
        /// takes a punch of 4.5 or more — a haymaker, or a counter on top of a close-range
        /// punch — which is the small, identifiable set of punches this is meant to mark.
        /// </summary>
        private const float CUT_DAMAGE_SHARE = 0.15f;

        private const float CUT_PER_HEALTH_SHARE = 2f;

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
            ApplyKnockback(target, message);
            MarkFace(target, message);
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
        /// Writes the punch onto the target's face: swelling on the side it came in on, and a
        /// cut if it was heavy enough to open one.
        ///
        /// Swelling and cuts accumulate differently on purpose, because they are different
        /// injuries. Swelling is volume - it rises with every punch, however light, which is
        /// why it is driven by damage as a share of the fighter's whole health bar and why it
        /// only ever goes up during a match. A cut is a single event: it needs one heavy punch
        /// rather than twenty jabs, so it is gated on the punch being a real one first and
        /// scaled afterwards.
        ///
        /// Deliberately presentational. Nothing reads these back into combat, so a boxer with
        /// a shut eye is exactly as effective as one without and the shipped policy - which
        /// has never seen either - is not being quietly recalibrated underneath.
        /// </summary>
        private void MarkFace(BoxerModel target, PunchLandedMessage message)
        {
            float maxHealth = Mathf.Max(1, _config.MaxHealth);
            float share = message.Damage / maxHealth;

            // Split across the two sides by where the attacker was standing, so a punch square
            // down the middle marks both cheeks a little and a hook marks one. Halved, because
            // a centred punch would otherwise deposit twice as much total swelling as an
            // angled one carrying the same damage.
            float right = Mathf.Clamp01(message.ApproachLateral * 0.5f + 0.5f);

            target.SwellRight = Mathf.Clamp01(
                target.SwellRight + share * right * SWELL_PER_HEALTH_SHARE);
            target.SwellLeft = Mathf.Clamp01(
                target.SwellLeft + share * (1f - right) * SWELL_PER_HEALTH_SHARE);

            if (share < CUT_DAMAGE_SHARE)
            {
                return;
            }

            target.Cut = Mathf.Clamp01(target.Cut + share * CUT_PER_HEALTH_SHARE);
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
