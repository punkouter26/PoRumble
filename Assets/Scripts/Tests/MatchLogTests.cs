using MessagePipe;
using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using VContainer;

namespace PoRumble.Tests
{
    /// <summary>
    /// The match log: one record per finished match, carrying the result and how each seat
    /// moved, so results can be set against behaviour afterwards.
    /// </summary>
    public sealed class MatchLogTests
    {
        /// <summary>Keeps a copy of what was appended; the system reuses its record.</summary>
        private sealed class CapturingStore : IMatchLogStore
        {
            public int Appended { get; private set; }
            public MatchLogRecord Last { get; private set; }

            public void Append(MatchLogRecord record)
            {
                Appended++;
                Last = JsonUtility.FromJson<MatchLogRecord>(JsonUtility.ToJson(record));
            }
        }

        private const float STEP = 0.02f;

        private IObjectResolver _container;
        private MatchModel _match;
        private MatchFlowModel _flow;
        private FightStatsModel _stats;
        private CapturingStore _store;
        private MatchLogSystem _system;
        private BoxerConfig _config;

        [SetUp]
        public void SetUp()
        {
            ContainerBuilder builder = new();
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<BoxerEliminatedMessage>(options);
            builder.RegisterMessageBroker<MatchEndedMessage>(options);
            _container = builder.Build();

            _config = ScriptableObject.CreateInstance<BoxerConfig>();
            _match = new MatchModel { ArenaHalfExtent = new Vector2(8.5f, 8.5f) };
            _flow = new MatchFlowModel();
            _stats = new FightStatsModel();
            _store = new CapturingStore();

            new SpawnSystem(_match, _config).SpawnRoster(3, 4f);
            _stats.Configure(3);

            _system = new MatchLogSystem(
                _match,
                _flow,
                new RosterModel(),
                _stats,
                _store,
                _container.Resolve<ISubscriber<BoxerEliminatedMessage>>(),
                _container.Resolve<ISubscriber<MatchEndedMessage>>());
        }

        [TearDown]
        public void TearDown()
        {
            _system?.Dispose();
            _container?.Dispose();
            Object.DestroyImmediate(_config);
        }

        private void Eliminate(int boxerId, int byId)
        {
            _match.Boxers[boxerId].Eliminate();
            _container.Resolve<IPublisher<BoxerEliminatedMessage>>()
                .Publish(new BoxerEliminatedMessage(boxerId, byId));
        }

        private void End(int winnerId)
        {
            _match.End(winnerId);
            _container.Resolve<IPublisher<MatchEndedMessage>>().Publish(new MatchEndedMessage(winnerId));
        }

        [Test]
        public void AFinishedMatchIsLoggedOnceWithEverySeatPlaced()
        {
            _stats.For(0).Thrown = 10;
            _stats.For(0).Landed = 4;

            _system.Step(STEP);
            Eliminate(2, 0);
            _system.Step(STEP);
            Eliminate(1, 0);
            End(0);

            Assert.That(_store.Appended, Is.EqualTo(1));

            MatchLogRecord record = _store.Last;
            Assert.That(record.entrants, Is.EqualTo(3));
            Assert.That(record.winnerSeat, Is.EqualTo(0));
            Assert.That(record.knockout, Is.True);
            Assert.That(record.seconds, Is.EqualTo(2 * STEP).Within(0.0001f));

            Assert.That(record.boxers[0].placement, Is.EqualTo(1));
            Assert.That(record.boxers[1].placement, Is.EqualTo(2), "last out is the runner-up");
            Assert.That(record.boxers[2].placement, Is.EqualTo(3), "first out finishes last");
            Assert.That(record.boxers[0].thrown, Is.EqualTo(10));
            Assert.That(record.boxers[0].landed, Is.EqualTo(4));
            Assert.That(record.boxers[2].survivedSeconds, Is.EqualTo(STEP).Within(0.0001f));
        }

        [Test]
        public void TheMovementProfileMeasuresDistanceTheRopesAndWhereAFighterLives()
        {
            BoxerModel boxer = _match.Boxers[0];
            boxer.Position = new Vector2(8f, 0f);
            boxer.Velocity = new Vector2(2f, 0f);

            for (int step = 0; step < 50; step++)
            {
                _system.Step(STEP);
            }

            End(0);

            BoxerLogRow row = _store.Last.boxers[0];
            Assert.That(row.distance, Is.EqualTo(2f).Within(0.001f), "two units a second for a second");
            Assert.That(row.ropesShare, Is.EqualTo(1f), "half a unit off the east rope the whole time");
            Assert.That(row.meanX, Is.EqualTo(8f / 8.5f).Within(0.001f));
            Assert.That(_store.Last.knockout, Is.False, "three were standing, so the bell decided it");
        }

        [Test]
        public void AnAbandonedFightLeavesNothingBehindForTheNextOne()
        {
            _match.Boxers[0].Velocity = new Vector2(3f, 0f);

            for (int step = 0; step < 50; step++)
            {
                _system.Step(STEP);
            }

            // MENU re-racks without the match phase changing; the next introduction clears it.
            _flow.Phase.Value = MatchFlowPhase.Introducing;
            _system.Step(STEP);
            End(0);

            Assert.That(_store.Last.seconds, Is.EqualTo(STEP).Within(0.0001f));
            Assert.That(_store.Last.boxers[0].distance, Is.EqualTo(3f * STEP).Within(0.0001f));
        }
    }
}
