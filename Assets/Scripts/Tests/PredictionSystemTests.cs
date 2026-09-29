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
    /// The book's rules: picks only before the bell, the price locks at the introduction, and
    /// a match with no result - a draw or an abandoned fight - never costs the viewer.
    /// </summary>
    public sealed class PredictionSystemTests
    {
        private sealed class MemoryStore : IPredictionStore
        {
            public int SaveCount { get; private set; }

            public void Load(PredictionModel predictions) { }

            public void Save(PredictionModel predictions) => SaveCount++;
        }

        private readonly List<Object> _created = new();

        private IObjectResolver _container;
        private IPublisher<MatchEndedMessage> _ended;
        private MatchModel _match;
        private RosterModel _roster;
        private MatchFlowModel _flow;
        private PredictionModel _predictions;
        private WinOddsSystem _odds;
        private PredictionSystem _system;
        private MemoryStore _store;
        private FighterProfile _alpha;
        private FighterProfile _beta;

        [SetUp]
        public void SetUp()
        {
            ContainerBuilder builder = new();
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<MatchEndedMessage>(options);
            _container = builder.Build();
            _ended = _container.Resolve<IPublisher<MatchEndedMessage>>();

            BoxerConfig config = ScriptableObject.CreateInstance<BoxerConfig>();
            _created.Add(config);

            _match = new MatchModel();
            _match.AddBoxer(new BoxerModel(0, config.MaxHealth));
            _match.AddBoxer(new BoxerModel(1, config.MaxHealth));

            FightStatsModel stats = new();
            stats.Configure(2);

            _alpha = MakeProfile("alpha");
            _beta = MakeProfile("beta");

            _roster = new RosterModel();
            _roster.SetAvailable(new[] { _alpha, _beta });
            _roster.AssignSeats(2);

            _flow = new MatchFlowModel();
            _predictions = new PredictionModel();
            _store = new MemoryStore();

            _odds = new WinOddsSystem(_match, _roster, new RatingModel(), stats, new WinOddsModel(), config);
            _system = new PredictionSystem(
                _predictions,
                _odds,
                _roster,
                _flow,
                _store,
                _container.Resolve<ISubscriber<MatchEndedMessage>>());
        }

        [TearDown]
        public void TearDown()
        {
            _system?.Dispose();
            _odds?.Dispose();
            _container?.Dispose();

            for (int index = 0; index < _created.Count; index++)
            {
                Object.DestroyImmediate(_created[index]);
            }

            _created.Clear();
        }

        private FighterProfile MakeProfile(string id)
        {
            FighterProfile profile = ScriptableObject.CreateInstance<FighterProfile>();
            JsonUtility.FromJsonOverwrite($"{{\"_id\":\"{id}\",\"_displayName\":\"{id.ToUpperInvariant()}\"}}", profile);
            _created.Add(profile);
            return profile;
        }

        private void Bell()
        {
            _flow.Phase.Value = MatchFlowPhase.Introducing;
        }

        [Test]
        public void TheStakeIsTakenAndThePriceLockedAtTheIntroduction()
        {
            Assert.That(_system.TogglePick(_alpha), Is.True);
            Assert.That(_predictions.Bank.Value, Is.EqualTo(PredictionModel.STARTING_BANK), "a pick alone costs nothing");

            Bell();

            Assert.That(_predictions.Outcome, Is.EqualTo(PredictionOutcome.Pending));
            Assert.That(_predictions.Bank.Value, Is.EqualTo(PredictionModel.STARTING_BANK - PredictionSystem.STAKE));
            Assert.That(_predictions.LockedMultiplier, Is.EqualTo(2f).Within(1e-3f), "two equal fighters pay evens");
        }

        [Test]
        public void AWinningPickPaysTheLockedPrice()
        {
            _system.TogglePick(_alpha);
            Bell();

            _ended.Publish(new MatchEndedMessage(0));

            Assert.That(_predictions.Outcome, Is.EqualTo(PredictionOutcome.Won));
            Assert.That(_predictions.Bank.Value, Is.EqualTo(PredictionModel.STARTING_BANK + PredictionSystem.STAKE));
            Assert.That(_predictions.Correct, Is.EqualTo(1));
            Assert.That(_store.SaveCount, Is.EqualTo(1));
        }

        [Test]
        public void ALosingPickLosesTheStake()
        {
            _system.TogglePick(_alpha);
            Bell();

            _ended.Publish(new MatchEndedMessage(1));

            Assert.That(_predictions.Outcome, Is.EqualTo(PredictionOutcome.Lost));
            Assert.That(_predictions.Bank.Value, Is.EqualTo(PredictionModel.STARTING_BANK - PredictionSystem.STAKE));
            Assert.That(_predictions.Placed, Is.EqualTo(1));
        }

        [Test]
        public void ADrawReturnsTheStake()
        {
            _system.TogglePick(_alpha);
            Bell();

            _ended.Publish(new MatchEndedMessage(MatchModel.NO_WINNER));

            Assert.That(_predictions.Outcome, Is.EqualTo(PredictionOutcome.Refunded));
            Assert.That(_predictions.Bank.Value, Is.EqualTo(PredictionModel.STARTING_BANK));
            Assert.That(_predictions.Placed, Is.Zero, "a refund is not a call");
        }

        [Test]
        public void AFightAbandonedToTheMenuReturnsTheStake()
        {
            _system.TogglePick(_alpha);
            Bell();
            _flow.Phase.Value = MatchFlowPhase.Fighting;

            _flow.Phase.Value = MatchFlowPhase.Title;

            Assert.That(_predictions.Outcome, Is.EqualTo(PredictionOutcome.Refunded));
            Assert.That(_predictions.Bank.Value, Is.EqualTo(PredictionModel.STARTING_BANK));
        }

        [Test]
        public void PicksAreRefusedOnceTheFightIsIntroduced()
        {
            Bell();

            Assert.That(_system.TogglePick(_alpha), Is.False);
            Assert.That(_predictions.Pick.Value, Is.Null);
        }

        [Test]
        public void ChangingThePickMidFightCannotMoveTheBet()
        {
            _system.TogglePick(_alpha);
            Bell();

            // Refused anyway off the title screen, but the settled bet must name the staked
            // contestant even if the pick field were somehow changed.
            _predictions.Pick.Value = _beta;
            _ended.Publish(new MatchEndedMessage(0));

            Assert.That(_predictions.Outcome, Is.EqualTo(PredictionOutcome.Won));
        }

        [Test]
        public void ABrokeViewerIsBailedOutRatherThanShutOut()
        {
            _predictions.Bank.Value = 40;
            _system.TogglePick(_alpha);

            Bell();

            Assert.That(_predictions.Bailouts, Is.EqualTo(1));
            Assert.That(_predictions.Bank.Value, Is.EqualTo(PredictionModel.STARTING_BANK - PredictionSystem.STAKE));
        }

        [Test]
        public void DroppingThePickedFighterFromTheCardClearsThePick()
        {
            FighterProfile gamma = MakeProfile("gamma");
            _roster.SetAvailable(new[] { _alpha, _beta, gamma });
            _roster.SelectAll();
            _system.TogglePick(gamma);

            _roster.Toggle(gamma);
            _roster.AssignSeats(2);

            Assert.That(_predictions.Pick.Value, Is.Null);
        }

        [Test]
        public void NoPickMeansNothingRidesOnTheMatch()
        {
            Bell();
            _ended.Publish(new MatchEndedMessage(0));

            Assert.That(_predictions.Outcome, Is.EqualTo(PredictionOutcome.None));
            Assert.That(_predictions.Bank.Value, Is.EqualTo(PredictionModel.STARTING_BANK));
        }
    }
}
