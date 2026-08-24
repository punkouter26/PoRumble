using System.Collections.Generic;
using MessagePipe;
using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using VContainer;

namespace PoRumble.Tests
{
    /// <summary>
    /// End-to-end message flow: punch -> damage -> elimination -> match end.
    /// Covers criteria 7, 8, 9 and 10 across real system instances.
    /// </summary>
    public sealed class CombatFlowTests
    {
        private IObjectResolver _container;
        private MatchModel _match;
        private CombatSystem _combatSystem;
        private MatchSystem _matchSystem;
        private readonly List<MatchEndedMessage> _endedMessages = new();
        private readonly List<BoxerEliminatedMessage> _eliminatedMessages = new();
        private IPublisher<PunchLandedMessage> _punchPublisher;

        [SetUp]
        public void SetUp()
        {
            ContainerBuilder builder = new();
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<PunchLandedMessage>(options);
            builder.RegisterMessageBroker<PunchEvadedMessage>(options);
            builder.RegisterMessageBroker<PunchBlockedMessage>(options);
            builder.RegisterMessageBroker<BoxerDamagedMessage>(options);
            builder.RegisterMessageBroker<BoxerEliminatedMessage>(options);
            builder.RegisterMessageBroker<MatchEndedMessage>(options);

            builder.Register<MatchModel>(Lifetime.Singleton);
            builder.Register<CombatSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<MatchSystem>(Lifetime.Singleton).AsSelf();

            _container = builder.Build();
            GlobalMessagePipe.SetProvider(_container.AsServiceProvider());

            _match = _container.Resolve<MatchModel>();
            _match.AddBoxer(new BoxerModel(0, 4));
            _match.AddBoxer(new BoxerModel(1, 4));

            // Resolving constructs the systems and wires their subscriptions.
            _combatSystem = _container.Resolve<CombatSystem>();
            _matchSystem = _container.Resolve<MatchSystem>();

            _endedMessages.Clear();
            _eliminatedMessages.Clear();
            _container.Resolve<ISubscriber<MatchEndedMessage>>().Subscribe(m => _endedMessages.Add(m));
            _container.Resolve<ISubscriber<BoxerEliminatedMessage>>().Subscribe(m => _eliminatedMessages.Add(m));

            _punchPublisher = _container.Resolve<IPublisher<PunchLandedMessage>>();
        }

        [TearDown]
        public void TearDown()
        {
            _combatSystem?.Dispose();
            _matchSystem?.Dispose();
            _container?.Dispose();
        }

        private void Punch(int attackerId, int targetId, int damage)
        {
            _punchPublisher.Publish(new PunchLandedMessage(attackerId, targetId, damage, false, Vector2.zero));
        }

        [Test]
        public void PunchesReduceHealthAndEliminateExactlyOnce()
        {
            Punch(0, 1, 2);
            Assert.That(_match.Boxers[1].Health.Value, Is.EqualTo(2));

            Punch(0, 1, 2);
            Assert.That(_match.Boxers[1].Health.Value, Is.Zero);
            Assert.That(_eliminatedMessages.Count, Is.EqualTo(1));
        }

        [Test]
        public void DamageToEliminatedBoxerIsIgnored()
        {
            Punch(0, 1, 4);
            Assert.That(_eliminatedMessages.Count, Is.EqualTo(1));

            Punch(0, 1, 4);
            Assert.That(_eliminatedMessages.Count, Is.EqualTo(1), "no second elimination message");
        }

        [Test]
        public void LastSurvivorEndsTheMatchExactlyOnce()
        {
            Punch(0, 1, 4);
            _matchSystem.EvaluateMatchState();
            _matchSystem.EvaluateMatchState();

            Assert.That(_endedMessages.Count, Is.EqualTo(1), "repeated evaluation must not re-end the match");
            Assert.That(_endedMessages[0].WinnerId, Is.EqualTo(0));
        }

        [Test]
        public void PunchAfterMatchEnded_IsIgnored()
        {
            Punch(0, 1, 4);
            _matchSystem.EvaluateMatchState();
            Assert.That(_endedMessages.Count, Is.EqualTo(1));

            // A punch thrown on a later tick by the loser must not land after the bell.
            Punch(1, 0, 4);

            Assert.That(_match.Boxers[0].IsAlive.Value, Is.True, "the winner survives the bell");
            Assert.That(_endedMessages.Count, Is.EqualTo(1), "still exactly one match-ended message");
        }

        [Test]
        public void SimultaneousKnockouts_BothLand_AndMatchIsADraw()
        {
            // Exercises the real BoxerSystem path: two boxers whose punches peak on the same
            // tick must both resolve, even though the first one is already lethal.
            ContainerBuilder builder = new();
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<PunchLandedMessage>(options);
            builder.RegisterMessageBroker<PunchEvadedMessage>(options);
            builder.RegisterMessageBroker<PunchBlockedMessage>(options);
            builder.RegisterMessageBroker<BoxerDamagedMessage>(options);
            builder.RegisterMessageBroker<BoxerEliminatedMessage>(options);
            builder.RegisterMessageBroker<MatchEndedMessage>(options);
            builder.Register<MatchModel>(Lifetime.Singleton);
            builder.Register<CombatSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<MatchSystem>(Lifetime.Singleton).AsSelf();

            using IObjectResolver container = builder.Build();
            MatchModel match = container.Resolve<MatchModel>();

            // Derived from the config rather than hardcoded, so retuning arm reach cannot
            // silently move the boxers out of range and turn this into a false failure.
            BoxerConfig geometry = ScriptableObject.CreateInstance<BoxerConfig>();
            float lateral = geometry.ArmLateralOffset;
            float separation = geometry.ArmReach + geometry.HeadOffset;
            Object.DestroyImmediate(geometry);

            // One hit point each, so a single long punch is lethal.
            BoxerModel first = new(0, 1) { Position = new Vector2(0f, 0f), Facing = Vector2.up };
            BoxerModel second = new(1, 1) { Position = new Vector2(-lateral, separation), Facing = Vector2.down };
            match.AddBoxer(first);
            match.AddBoxer(second);

            CombatSystem combat = container.Resolve<CombatSystem>();
            MatchSystem matchSystem = container.Resolve<MatchSystem>();

            List<MatchEndedMessage> ended = new();
            container.Resolve<ISubscriber<MatchEndedMessage>>().Subscribe(m => ended.Add(m));

            BoxerConfig config = ScriptableObject.CreateInstance<BoxerConfig>();
            BoxerSystem boxerSystem = new(
                match,
                config,
                container.Resolve<IPublisher<PunchLandedMessage>>(),
                container.Resolve<IPublisher<PunchEvadedMessage>>(),
                container.Resolve<IPublisher<PunchBlockedMessage>>());

            // Placed so each glove lands on the other's head centre at full reach.
            boxerSystem.Punch(0, ArmSide.Left);
            boxerSystem.Punch(1, ArmSide.Left);

            for (int tickIndex = 0; tickIndex < 20 && match.Phase.Value == MatchPhase.InProgress; tickIndex++)
            {
                boxerSystem.Tick(0.02f);
                matchSystem.EvaluateMatchState();
            }

            combat.Dispose();
            matchSystem.Dispose();
            boxerSystem.Dispose();
            Object.DestroyImmediate(config);

            Assert.That(first.IsAlive.Value, Is.False, "boxer 0 was knocked out");
            Assert.That(second.IsAlive.Value, Is.False, "boxer 1 was knocked out on the same tick");
            Assert.That(ended.Count, Is.EqualTo(1), "exactly one match-ended message");
            Assert.That(ended[0].WinnerId, Is.EqualTo(MatchModel.NO_WINNER), "a mutual knockout is a draw");
        }
    }
}
