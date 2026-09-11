using MessagePipe;
using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using VContainer;

namespace PoRumble.Tests
{
    /// <summary>
    /// Pins the telemetry board's arithmetic, and one thing about it that is easy to get
    /// backwards: the connect rate is landed over <em>thrown</em>, and thrown is the only one
    /// of the five counters that nothing else on the message bus reports.
    /// </summary>
    public sealed class FightStatsTests
    {
        private IObjectResolver _container;
        private MatchModel _match;
        private FightStatsModel _stats;
        private FightStatsSystem _system;

        [SetUp]
        public void SetUp()
        {
            ContainerBuilder builder = new();
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<PunchThrownMessage>(options);
            builder.RegisterMessageBroker<PunchLandedMessage>(options);
            builder.RegisterMessageBroker<PunchBlockedMessage>(options);
            builder.RegisterMessageBroker<PunchEvadedMessage>(options);
            builder.RegisterMessageBroker<BoxerDodgedMessage>(options);
            builder.RegisterMessageBroker<HaymakerThrownMessage>(options);
            _container = builder.Build();

            _match = new MatchModel();
            _match.AddBoxer(new BoxerModel(0, 30));
            _match.AddBoxer(new BoxerModel(1, 30));

            _stats = new FightStatsModel();
            _stats.Configure(2);

            _system = new FightStatsSystem(
                _match,
                _stats,
                _container.Resolve<ISubscriber<PunchThrownMessage>>(),
                _container.Resolve<ISubscriber<PunchLandedMessage>>(),
                _container.Resolve<ISubscriber<PunchBlockedMessage>>(),
                _container.Resolve<ISubscriber<PunchEvadedMessage>>(),
                _container.Resolve<ISubscriber<BoxerDodgedMessage>>(),
                _container.Resolve<ISubscriber<HaymakerThrownMessage>>());
        }

        [TearDown]
        public void TearDown()
        {
            _system?.Dispose();
            _container?.Dispose();
        }

        private void Throw(int boxerId)
        {
            _container.Resolve<IPublisher<PunchThrownMessage>>()
                .Publish(new PunchThrownMessage(boxerId, Vector2.zero, 0f));
        }

        private void Land(int attackerId, int targetId, int damage)
        {
            _container.Resolve<IPublisher<PunchLandedMessage>>()
                .Publish(new PunchLandedMessage(attackerId, targetId, damage, true, Vector2.zero));
        }

        [Test]
        public void ConnectRateIsLandedOverThrownRatherThanOverPunchesThatReachedSomebody()
        {
            for (int punchIndex = 0; punchIndex < 4; punchIndex++)
            {
                Throw(0);
            }

            Land(0, 1, 3);

            // Four thrown, one landed. Counting only the punches that ran into something
            // would report 100% here, which is the tautology PunchThrownMessage exists to
            // break.
            Assert.That(_stats.For(0).Accuracy, Is.EqualTo(0.25f).Within(0.001f));
        }

        [Test]
        public void ALandedPunchMovesBothFightersMomentumInOppositeDirections()
        {
            Land(0, 1, 5);

            Assert.That(_stats.For(0).Momentum, Is.EqualTo(5f).Within(0.001f));
            Assert.That(_stats.For(1).Momentum, Is.EqualTo(-5f).Within(0.001f));
        }

        [Test]
        public void MomentumDecaysTowardZeroWhenNothingIsLanding()
        {
            Land(0, 1, 8);
            float immediately = _stats.For(0).Momentum;

            _system.Tick(2f);

            Assert.That(_stats.For(0).Momentum, Is.LessThan(immediately));
            Assert.That(_stats.For(0).Momentum, Is.GreaterThan(0f));
        }

        [Test]
        public void DamageDealtAndTakenAreCountedSeparately()
        {
            Land(0, 1, 4);
            Land(1, 0, 6);

            Assert.That(_stats.For(0).DamageDealt, Is.EqualTo(4));
            Assert.That(_stats.For(0).DamageTaken, Is.EqualTo(6));
            Assert.That(_stats.For(1).DamageDealt, Is.EqualTo(6));
            Assert.That(_stats.For(1).DamageTaken, Is.EqualTo(4));
        }

        /// <summary>
        /// The ring buffer must read back oldest-first regardless of where its write head is,
        /// or the sparkline draws the match in the wrong order once the buffer has wrapped -
        /// which is a bug that cannot appear until eight seconds into a fight.
        /// </summary>
        [Test]
        public void HistoryReadsBackOldestFirstAfterTheBufferHasWrapped()
        {
            for (int sample = 0; sample < FightStatsModel.HISTORY_LENGTH + 5; sample++)
            {
                _stats.For(0).Momentum = sample;
                _stats.SampleHistory();
            }

            Assert.That(_stats.SampleCount, Is.EqualTo(FightStatsModel.HISTORY_LENGTH));

            float oldest = _stats.HistoryAt(0, 0);
            float newest = _stats.HistoryAt(0, FightStatsModel.HISTORY_LENGTH - 1);

            Assert.That(newest, Is.GreaterThan(oldest));
            Assert.That(newest, Is.EqualTo(FightStatsModel.HISTORY_LENGTH + 4f).Within(0.001f));
        }

        [Test]
        public void ABlockIsCreditedToTheBlockerAndChargedToTheAttacker()
        {
            _container.Resolve<IPublisher<PunchBlockedMessage>>()
                .Publish(new PunchBlockedMessage(0, 1, Vector2.zero));

            Assert.That(_stats.For(0).Blocked, Is.EqualTo(1));
            Assert.That(_stats.For(1).BlocksMade, Is.EqualTo(1));
        }
    }
}
