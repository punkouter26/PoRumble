using MessagePipe;
using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using VContainer;

namespace PoRumble.Tests
{
    /// <summary>
    /// The fight map's bookkeeping and the second-fight rule. Both are derived state; what is
    /// pinned here is that they describe the fight correctly, not that they leave it alone -
    /// nothing they write is read by combat.
    /// </summary>
    public sealed class BroadcastLayerTests
    {
        [Test]
        public void APunchDepositsExactlyItsDamageAroundWhereItLanded()
        {
            ContainerBuilder builder = new();
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<PunchLandedMessage>(options);
            IObjectResolver container = builder.Build();

            MatchModel match = new() { ArenaHalfExtent = new Vector2(20f, 20f) };
            DamageMapModel map = new();
            DamageMapSystem system = new(match, map, container.Resolve<ISubscriber<PunchLandedMessage>>());

            container.Resolve<IPublisher<PunchLandedMessage>>()
                .Publish(new PunchLandedMessage(0, 1, 6, false, new Vector2(10f, -10f)));

            Assert.That(map.Total, Is.EqualTo(6f).Within(1e-4f));

            Assert.That(map.TryWorldToCell(new Vector2(10f, -10f), out int x, out int y), Is.True);
            Assert.That(map.At(x, y), Is.EqualTo(map.Peak).Within(1e-5f), "the hottest cell is the one it landed in");
            Assert.That(map.CellCentre(x, y).x, Is.EqualTo(10f).Within(0.7f));

            system.Dispose();
            container.Dispose();
        }

        [Test]
        public void AFreshMatchStartsWithAClearMap()
        {
            ContainerBuilder builder = new();
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<PunchLandedMessage>(options);
            IObjectResolver container = builder.Build();

            MatchModel match = new();
            DamageMapModel map = new();
            DamageMapSystem system = new(match, map, container.Resolve<ISubscriber<PunchLandedMessage>>());

            system.Deposit(Vector2.zero, 5f);
            match.End(0);
            match.BeginNewEpisode();

            Assert.That(map.Total, Is.Zero);
            Assert.That(map.Peak, Is.Zero);

            system.Dispose();
            container.Dispose();
        }

        [Test]
        public void PunchesOverTheRopesAreNotTheCanvassDamage()
        {
            DamageMapModel map = new();
            map.Configure(new Vector2(20f, 20f));

            Assert.That(map.TryWorldToCell(new Vector2(20.5f, 0f), out _, out _), Is.False);
            Assert.That(map.TryWorldToCell(new Vector2(20f, 20f), out int x, out int y), Is.True);
            Assert.That(x, Is.EqualTo(DamageMapModel.RESOLUTION - 1), "the far rope is the last cell, not one past it");
            Assert.That(y, Is.EqualTo(DamageMapModel.RESOLUTION - 1));
        }

        /// <summary>
        /// The fighter backed on the title screen is the one the camera follows from the bell,
        /// and nobody is followed without a pick. The pick is a contestant and the pin is a seat,
        /// so this also holds that the lookup goes through the dealt seats.
        /// </summary>
        [Test]
        public void TheBackedFighterIsFollowedFromTheBell()
        {
            ContainerBuilder builder = new();
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<PunchThrownMessage>(options);
            builder.RegisterMessageBroker<PunchLandedMessage>(options);
            builder.RegisterMessageBroker<PunchBlockedMessage>(options);
            builder.RegisterMessageBroker<PunchEvadedMessage>(options);
            builder.RegisterMessageBroker<BoxerDodgedMessage>(options);
            builder.RegisterMessageBroker<HaymakerThrownMessage>(options);
            builder.RegisterMessageBroker<BoxerEliminatedMessage>(options);
            IObjectResolver container = builder.Build();

            BoxerConfig config = ScriptableObject.CreateInstance<BoxerConfig>();
            FighterProfile[] card =
            {
                ScriptableObject.CreateInstance<FighterProfile>(),
                ScriptableObject.CreateInstance<FighterProfile>(),
                ScriptableObject.CreateInstance<FighterProfile>()
            };

            RosterModel roster = new();
            roster.SetAvailable(card);
            roster.AssignSeats(card.Length);

            MatchModel match = new();

            for (int boxerId = 0; boxerId < card.Length; boxerId++)
            {
                match.AddBoxer(new BoxerModel(boxerId, config.MaxHealth));
            }

            PredictionModel predictions = new();
            DirectorModel director = new();
            FightStatsSystem stats = new(
                match, new FightStatsModel(),
                container.Resolve<ISubscriber<PunchThrownMessage>>(),
                container.Resolve<ISubscriber<PunchLandedMessage>>(),
                container.Resolve<ISubscriber<PunchBlockedMessage>>(),
                container.Resolve<ISubscriber<PunchEvadedMessage>>(),
                container.Resolve<ISubscriber<BoxerDodgedMessage>>(),
                container.Resolve<ISubscriber<HaymakerThrownMessage>>(),
                container.Resolve<ISubscriber<BoxerEliminatedMessage>>());
            DirectorSystem system = new(
                match, director, new MatchFlowModel(), stats, config, roster, predictions,
                container.Resolve<ISubscriber<PunchLandedMessage>>(),
                container.Resolve<ISubscriber<BoxerEliminatedMessage>>());

            predictions.Pick.Value = card[2];
            match.End(0);
            match.BeginNewEpisode();

            Assert.That(director.PinnedId.Value, Is.EqualTo(2), "the camera follows the seat the pick was dealt");

            predictions.Pick.Value = null;
            match.End(0);
            match.BeginNewEpisode();

            Assert.That(director.PinnedId.Value, Is.EqualTo(DirectorModel.NOBODY), "no pick, nobody pinned");

            system.Dispose();
            stats.Dispose();
            container.Dispose();
            Object.DestroyImmediate(config);

            for (int index = 0; index < card.Length; index++)
            {
                Object.DestroyImmediate(card[index]);
            }
        }

        [Test]
        public void TheSecondFightSharesNobodyWithTheMainOne()
        {
            ScoredPair[] pairs =
            {
                new(0, 1, 0.9f),
                new(0, 2, 0.8f),
                new(2, 3, 0.4f),
                new(4, 5, 0.3f)
            };

            bool found = PairSelection.TryPickSecondary(pairs, pairs.Length, 0, 1, -1, -1, 0.12f, out ScoredPair second);

            Assert.That(found, Is.True);
            Assert.That(second.Is(2, 3), Is.True, "0-2 scores higher but shares a fighter with the main pair");
        }

        [Test]
        public void TheSecondFightHoldsUntilARivalIsDecisivelyBetter()
        {
            ScoredPair[] pairs =
            {
                new(0, 1, 0.9f),
                new(2, 3, 0.40f),
                new(4, 5, 0.45f)
            };

            PairSelection.TryPickSecondary(pairs, pairs.Length, 0, 1, 2, 3, 0.12f, out ScoredPair held);
            Assert.That(held.Is(2, 3), Is.True, "0.45 against 0.40 is a hair, not a better fight");

            pairs[2] = new ScoredPair(4, 5, 0.6f);
            PairSelection.TryPickSecondary(pairs, pairs.Length, 0, 1, 2, 3, 0.12f, out ScoredPair switched);
            Assert.That(switched.Is(4, 5), Is.True);
        }

        [Test]
        public void ThreeFightersLeaveNoSecondFight()
        {
            ScoredPair[] pairs =
            {
                new(0, 1, 0.9f),
                new(0, 2, 0.5f),
                new(1, 2, 0.4f)
            };

            Assert.That(
                PairSelection.TryPickSecondary(pairs, pairs.Length, 0, 1, -1, -1, 0.12f, out _),
                Is.False);
        }
    }
}
