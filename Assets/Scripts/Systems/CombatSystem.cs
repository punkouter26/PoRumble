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
