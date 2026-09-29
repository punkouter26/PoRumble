using System.Collections.Generic;
using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;

namespace PoRumble.Tests
{
    /// <summary>
    /// The live win probability. The property that matters most is the one that makes the
    /// number honest before a punch is thrown: with everyone fresh and neutral, two fighters'
    /// share of the odds is exactly their Elo expected score.
    /// </summary>
    public sealed class WinOddsTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            for (int index = 0; index < _created.Count; index++)
            {
                Object.DestroyImmediate(_created[index]);
            }

            _created.Clear();
        }

        private static OddsInput Fresh(float rating = RatingModel.DEFAULT_RATING)
        {
            return new OddsInput(true, 1f, 1f, rating, 1f, 1f, 0f);
        }

        private static float[] Odds(params OddsInput[] inputs)
        {
            float[] strengths = new float[inputs.Length];
            float[] odds = new float[inputs.Length];

            for (int index = 0; index < inputs.Length; index++)
            {
                strengths[index] = WinOddsMath.Strength(inputs[index]);
            }

            WinOddsMath.Normalise(strengths, odds, inputs.Length);
            return odds;
        }

        [Test]
        public void IdenticalFreshFightersSplitTheFieldEvenly()
        {
            float[] odds = Odds(Fresh(), Fresh(), Fresh(), Fresh());

            for (int index = 0; index < odds.Length; index++)
            {
                Assert.That(odds[index], Is.EqualTo(0.25f).Within(1e-5f));
            }
        }

        [Test]
        public void BeforeTheBellAOneOnOneIsTheEloExpectedScore()
        {
            float[] odds = Odds(Fresh(1400f), Fresh(1200f));
            float expected = 1f / (1f + Mathf.Pow(10f, (1200f - 1400f) / 400f));

            Assert.That(odds[0], Is.EqualTo(expected).Within(1e-4f));
        }

        [Test]
        public void TheEliminatedHaveNoChanceAndTheLastStandingHasAll()
        {
            OddsInput down = new(false, 0f, 1f, RatingModel.DEFAULT_RATING, 1f, 1f, 0f);
            float[] odds = Odds(down, Fresh(), down);

            Assert.That(odds[0], Is.Zero);
            Assert.That(odds[1], Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void HalfHealthIsAQuarterOfTheStrength()
        {
            OddsInput hurt = new(true, 0.5f, 1f, RatingModel.DEFAULT_RATING, 1f, 1f, 0f);

            Assert.That(
                WinOddsMath.Strength(hurt) / WinOddsMath.Strength(Fresh()),
                Is.EqualTo(0.25f).Within(1e-5f));
        }

        [Test]
        public void AGlassChinShortensTheOdds()
        {
            OddsInput glass = new(true, 1f, 1f, RatingModel.DEFAULT_RATING, 1f, 1.3f, 0f);
            float[] odds = Odds(glass, Fresh());

            Assert.That(odds[0], Is.LessThan(odds[1]));
        }

        [Test]
        public void MomentumTipsACloseCallButNeverBeatsAHealthLead()
        {
            OddsInput rolling = new(true, 0.5f, 1f, RatingModel.DEFAULT_RATING, 1f, 1f, 100f);
            OddsInput fresh = Fresh();

            Assert.That(WinOddsMath.Strength(rolling), Is.LessThan(WinOddsMath.Strength(fresh)));

            OddsInput level = new(true, 1f, 1f, RatingModel.DEFAULT_RATING, 1f, 1f, 8f);
            Assert.That(WinOddsMath.Strength(level), Is.GreaterThan(WinOddsMath.Strength(fresh)));
        }

        [Test]
        public void TheMultiplierIsFairOddsCappedForLongShots()
        {
            Assert.That(WinOddsMath.Multiplier(0.25f), Is.EqualTo(4f).Within(1e-5f));
            Assert.That(WinOddsMath.Multiplier(0f), Is.EqualTo(WinOddsMath.MAX_MULTIPLIER));
        }

        [Test]
        public void AContestantSeatedTwiceIsOneLineWithBothChairsSummed()
        {
            MatchModel match = new();
            RosterModel roster = new();
            RatingModel ratings = new();
            FightStatsModel stats = new();
            WinOddsModel odds = new();
            BoxerConfig config = ScriptableObject.CreateInstance<BoxerConfig>();
            _created.Add(config);

            for (int id = 0; id < 3; id++)
            {
                match.AddBoxer(new BoxerModel(id, config.MaxHealth));
            }

            stats.Configure(3);

            FighterProfile alpha = MakeProfile("alpha");
            FighterProfile beta = MakeProfile("beta");
            roster.SetAvailable(new[] { alpha, beta });
            roster.AssignSeats(3);

            WinOddsSystem system = new(match, roster, ratings, stats, odds, config);
            system.Recompute();

            Assert.That(odds.Entries.Count, Is.EqualTo(2), "one line per contestant, not per chair");
            Assert.That(system.OddsFor(alpha), Is.EqualTo(2f / 3f).Within(1e-4f));
            Assert.That(system.OddsFor(beta), Is.EqualTo(1f / 3f).Within(1e-4f));
            Assert.That(odds.Entries[0].Profile, Is.SameAs(alpha), "best chance first");

            system.Dispose();
        }

        [Test]
        public void StepCountsReadTheWayARunIsTalkedAbout()
        {
            Assert.That(StepCountFormat.Label(0), Is.Empty);
            Assert.That(StepCountFormat.Label(520_000), Is.EqualTo("520K STEPS"));
            Assert.That(StepCountFormat.Label(3_900_000), Is.EqualTo("3.9M STEPS"));
            Assert.That(StepCountFormat.Label(21_400_000), Is.EqualTo("21M STEPS"));
        }

        private FighterProfile MakeProfile(string id)
        {
            FighterProfile profile = ScriptableObject.CreateInstance<FighterProfile>();
            JsonUtility.FromJsonOverwrite($"{{\"_id\":\"{id}\",\"_displayName\":\"{id.ToUpperInvariant()}\"}}", profile);
            _created.Add(profile);
            return profile;
        }
    }
}
