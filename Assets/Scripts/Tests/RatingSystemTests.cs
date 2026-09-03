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
    /// Elo over a free-for-all. The finishing order is turned into every pairwise result it
    /// implies and the sum divided by the number of opponents, so a ten-way and a 1v1 move a
    /// rating by comparable amounts.
    /// </summary>
    public sealed class RatingSystemTests
    {
        private IObjectResolver _container;
        private MatchModel _match;
        private RosterModel _roster;
        private RatingModel _ratings;
        private RatingSystem _system;
        private readonly List<FighterProfile> _profiles = new();

        [SetUp]
        public void SetUp()
        {
            ContainerBuilder builder = new();
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<BoxerEliminatedMessage>(options);
            builder.RegisterMessageBroker<MatchEndedMessage>(options);
            _container = builder.Build();

            _match = new MatchModel();
            _roster = new RosterModel();
            _ratings = new RatingModel();
            _profiles.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _system?.Dispose();
            _container?.Dispose();

            for (int index = 0; index < _profiles.Count; index++)
            {
                Object.DestroyImmediate(_profiles[index]);
            }
        }

        /// <summary>
        /// Builds a contestant with a given id.
        ///
        /// FromJsonOverwrite rather than a public setter: the id is a serialized field with no
        /// business being writable at runtime, and this is exactly the shape Unity itself
        /// deserializes the asset with.
        /// </summary>
        private FighterProfile MakeProfile(string id)
        {
            FighterProfile profile = ScriptableObject.CreateInstance<FighterProfile>();
            JsonUtility.FromJsonOverwrite($"{{\"_id\":\"{id}\",\"_displayName\":\"{id.ToUpperInvariant()}\"}}", profile);
            _profiles.Add(profile);
            return profile;
        }

        /// <summary>Seats one contestant per corner and starts the rating system watching.</summary>
        private void Rack(int fighterCount, int seatCount)
        {
            List<FighterProfile> card = new();

            for (int index = 0; index < fighterCount; index++)
            {
                card.Add(MakeProfile("f" + index));
            }

            _roster.SetAvailable(card);
            _roster.AssignSeats(seatCount);

            for (int seat = 0; seat < seatCount; seat++)
            {
                _match.AddBoxer(new BoxerModel(seat, 30));
            }

            _system = new RatingSystem(
                _ratings,
                _roster,
                _match,
                null,
                _container.Resolve<ISubscriber<BoxerEliminatedMessage>>(),
                _container.Resolve<ISubscriber<MatchEndedMessage>>());
        }

        /// <summary>Knocks boxers out in the given order, then rings the final bell.</summary>
        private void ResolveMatch(int winnerId, params int[] eliminationOrder)
        {
            IPublisher<BoxerEliminatedMessage> eliminated =
                _container.Resolve<IPublisher<BoxerEliminatedMessage>>();

            for (int index = 0; index < eliminationOrder.Length; index++)
            {
                BoxerModel boxer = _match.Boxers[eliminationOrder[index]];
                boxer.Eliminate();
                eliminated.Publish(new BoxerEliminatedMessage(boxer.Id, winnerId));
            }

            _match.End(winnerId);
            _container.Resolve<IPublisher<MatchEndedMessage>>().Publish(new MatchEndedMessage(winnerId));
        }

        private float RatingOf(int seat)
        {
            return _ratings.GetOrCreate(_roster.SeatOf(seat).Id, null).Rating;
        }

        [Test]
        public void TheWinnerGainsAndTheFirstOutLosesMost()
        {
            Rack(4, 4);

            // Boxer 3 goes out first, then 2, then 1. Boxer 0 is left standing.
            ResolveMatch(0, 3, 2, 1);

            Assert.That(RatingOf(0), Is.GreaterThan(RatingModel.DEFAULT_RATING));
            Assert.That(RatingOf(3), Is.LessThan(RatingModel.DEFAULT_RATING));

            Assert.That(RatingOf(0), Is.GreaterThan(RatingOf(1)));
            Assert.That(RatingOf(1), Is.GreaterThan(RatingOf(2)));
            Assert.That(RatingOf(2), Is.GreaterThan(RatingOf(3)),
                "lasting longer has to be worth more than going out first");
        }

        [Test]
        public void RatingsAreZeroSumBetweenEquals()
        {
            Rack(4, 4);
            ResolveMatch(0, 3, 2, 1);

            float total = 0f;

            for (int seat = 0; seat < 4; seat++)
            {
                total += RatingOf(seat) - RatingModel.DEFAULT_RATING;
            }

            Assert.That(total, Is.EqualTo(0f).Within(0.001f),
                "points taken off the field have to be points handed to the winners");
        }

        [TestCase(2)]
        [TestCase(10)]
        public void RingSizeDoesNotChangeHowFarAWinnerMoves(int fighterCount)
        {
            Rack(fighterCount, fighterCount);

            int[] order = new int[fighterCount - 1];

            for (int index = 0; index < order.Length; index++)
            {
                order[index] = fighterCount - 1 - index;
            }

            ResolveMatch(0, order);

            // Winning outright against an equal field is worth half the K factor whatever the
            // field size, because the pairwise sum is divided by the opponent count. Without
            // that division a ten-way would swing nine times as far as a 1v1, and the table
            // would describe how crowded the ring was rather than who is any good.
            Assert.That(RatingOf(0) - RatingModel.DEFAULT_RATING, Is.EqualTo(16f).Within(0.01f));
        }

        [Test]
        public void TheSameContestantInTwoChairsIsNotRatedAgainstItself()
        {
            // Two contestants over four seats: f0 sits in 0 and 2, f1 in 1 and 3.
            Rack(2, 4);
            Assert.That(_roster.SeatOf(0).Id, Is.EqualTo(_roster.SeatOf(2).Id),
                "the deal should have wrapped round");

            ResolveMatch(0, 3, 2, 1);

            float first = _ratings.GetOrCreate("f0", null).Rating;
            float second = _ratings.GetOrCreate("f1", null).Rating;

            Assert.That(first, Is.Not.EqualTo(second), "the two contestants did not finish level");
            Assert.That(first + second,
                Is.EqualTo(RatingModel.DEFAULT_RATING * 2f).Within(0.001f),
                "self-pairings must contribute nothing at all");
        }

        [Test]
        public void MatchesAndWinsAreCounted()
        {
            Rack(4, 4);
            ResolveMatch(0, 3, 2, 1);

            RatingRecord winner = _ratings.GetOrCreate(_roster.SeatOf(0).Id, null);
            RatingRecord loser = _ratings.GetOrCreate(_roster.SeatOf(3).Id, null);

            Assert.That(winner.Matches, Is.EqualTo(1));
            Assert.That(winner.Wins, Is.EqualTo(1));
            Assert.That(winner.Knockouts, Is.EqualTo(3), "boxer 0 was credited with all three");
            Assert.That(loser.Matches, Is.EqualTo(1));
            Assert.That(loser.Wins, Is.Zero);
        }

        [Test]
        public void TheTableRanksByRating()
        {
            Rack(4, 4);
            ResolveMatch(0, 3, 2, 1);

            List<RatingRecord> top = new();
            _ratings.FillTop(top, 3);

            Assert.That(top, Has.Count.EqualTo(3));
            Assert.That(top[0].Rating, Is.GreaterThanOrEqualTo(top[1].Rating));
            Assert.That(top[1].Rating, Is.GreaterThanOrEqualTo(top[2].Rating));
            Assert.That(top[0].Id, Is.EqualTo(_roster.SeatOf(0).Id));
        }

        [Test]
        public void ASceneWithNoCardRatesNothing()
        {
            // A training scene: boxers, but nobody seated in them.
            for (int seat = 0; seat < 4; seat++)
            {
                _match.AddBoxer(new BoxerModel(seat, 30));
            }

            _system = new RatingSystem(
                _ratings,
                _roster,
                _match,
                null,
                _container.Resolve<ISubscriber<BoxerEliminatedMessage>>(),
                _container.Resolve<ISubscriber<MatchEndedMessage>>());

            ResolveMatch(0, 3, 2, 1);

            Assert.That(_ratings.Records, Is.Empty,
                "a run must not write a league table nobody asked for");
        }
    }
}
