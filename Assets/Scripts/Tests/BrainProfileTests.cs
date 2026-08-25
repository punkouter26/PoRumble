using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;

namespace PoRumble.Tests
{
    /// <summary>
    /// Difficulty tiers. The brain used to be a block of constants, which meant every scripted
    /// opponent in every match fought identically; a profile makes one roster able to field a
    /// spread of them.
    /// </summary>
    public sealed class BrainProfileTests
    {
        private BoxerConfig _config;
        private MatchModel _match;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<BoxerConfig>();
            _match = new MatchModel { ArenaHalfExtent = new Vector2(20f, 20f) };
            _match.AddBoxer(new BoxerModel(0, _config.MaxHealth));
            _match.AddBoxer(new BoxerModel(1, _config.MaxHealth));

            // Squared up just inside punching range.
            _match.Boxers[0].Position = Vector2.zero;
            _match.Boxers[0].Facing = Vector2.up;
            _match.Boxers[1].Position = new Vector2(0f, 1.3f);
            _match.Boxers[1].Facing = Vector2.down;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
        }

        private static BrainSettings Settings(
            float aggression = 0.5f,
            float reactionDelay = 0f,
            float accuracy = 1f,
            float chargeChance = 0f,
            float counterDiscipline = 0.5f)
        {
            return new BrainSettings(
                aggression, 0.95f, 1.35f, reactionDelay, accuracy,
                Mathf.Lerp(0.55f, 0.95f, accuracy), 0.25f, 0.55f, chargeChance, counterDiscipline);
        }

        [Test]
        public void TheSameSeedProducesTheSameFight()
        {
            ScriptedBoxerBrain first = new(_config, Settings(accuracy: 0.4f), 7);
            ScriptedBoxerBrain second = new(_config, Settings(accuracy: 0.4f), 7);

            for (int tick = 0; tick < 40; tick++)
            {
                BoxerIntent a = first.Decide(_match, 0, 0.02f);
                BoxerIntent b = second.Decide(_match, 0, 0.02f);

                Assert.That(a.Aim, Is.EqualTo(b.Aim),
                    "training depends on the sparring partner being reproducible");
                Assert.That(a.PunchLeft, Is.EqualTo(b.PunchLeft));
            }
        }

        [Test]
        public void DifferentSeedsProduceDifferentFights()
        {
            ScriptedBoxerBrain first = new(_config, Settings(accuracy: 0.4f), 1);
            ScriptedBoxerBrain second = new(_config, Settings(accuracy: 0.4f), 2);

            bool diverged = false;

            for (int tick = 0; tick < 40 && !diverged; tick++)
            {
                BoxerIntent a = first.Decide(_match, 0, 0.02f);
                BoxerIntent b = second.Decide(_match, 0, 0.02f);
                diverged = a.Aim != b.Aim;
            }

            Assert.That(diverged, Is.True,
                "two bots on the same tier must not move in lockstep, or ten of them read as one");
        }

        [Test]
        public void APerfectlyAccurateTierAimsStraightAtTheOpponent()
        {
            ScriptedBoxerBrain brain = new(_config, Settings(accuracy: 1f), 3);
            BoxerIntent intent = brain.Decide(_match, 0, 0.02f);

            Assert.That(Vector2.Dot(intent.Aim, Vector2.up), Is.GreaterThan(0.999f));
        }

        [Test]
        public void ASloppyTierAimsOffTarget()
        {
            ScriptedBoxerBrain brain = new(_config, Settings(accuracy: 0f), 3);
            bool wandered = false;

            for (int tick = 0; tick < 40 && !wandered; tick++)
            {
                BoxerIntent intent = brain.Decide(_match, 0, 0.02f);
                wandered = Vector2.Dot(intent.Aim, Vector2.up) < 0.995f;
            }

            Assert.That(wandered, Is.True,
                "an inaccurate tier has to actually miss, or the difficulty ladder is cosmetic");
        }

        [Test]
        public void ReactionDelayHoldsAnAimAcrossTicks()
        {
            ScriptedBoxerBrain brain = new(_config, Settings(reactionDelay: 0.5f, accuracy: 1f), 4);

            BoxerIntent first = brain.Decide(_match, 0, 0.02f);

            // Teleport the opponent. A delayed tier keeps swinging at where they used to be.
            _match.Boxers[1].Position = new Vector2(1.3f, 0f);

            BoxerIntent second = brain.Decide(_match, 0, 0.02f);

            Assert.That(second.Aim, Is.EqualTo(first.Aim),
                "reaction delay is most of what makes a weaker tier beatable");
        }

        [Test]
        public void ATierWithNoChargeChanceNeverWindsUp()
        {
            ScriptedBoxerBrain brain = new(_config, Settings(chargeChance: 0f), 5);

            for (int tick = 0; tick < 200; tick++)
            {
                Assert.That(brain.Decide(_match, 0, 0.02f).Charge, Is.False);
            }
        }

        [Test]
        public void ATierThatAlwaysChargesEventuallyWindsUp()
        {
            ScriptedBoxerBrain brain = new(_config, Settings(chargeChance: 1f, counterDiscipline: 0f), 5);
            bool charged = false;

            for (int tick = 0; tick < 200 && !charged; tick++)
            {
                charged = brain.Decide(_match, 0, 0.02f).Charge;
            }

            Assert.That(charged, Is.True);
        }

        [Test]
        public void AChargingBotDoesNotAlsoJab()
        {
            ScriptedBoxerBrain brain = new(_config, Settings(chargeChance: 1f, counterDiscipline: 0f), 6);

            for (int tick = 0; tick < 200; tick++)
            {
                BoxerIntent intent = brain.Decide(_match, 0, 0.02f);

                if (intent.Charge)
                {
                    Assert.That(intent.PunchLeft, Is.False,
                        "the system refuses ordinary punches mid-wind-up, so asking for both "
                        + "would silently throw the punch away");
                    Assert.That(intent.PunchRight, Is.False);
                    return;
                }
            }

            Assert.Fail("the bot never charged, so the interaction was never exercised");
        }

        [Test]
        public void AnAggressiveTierFightsCloserThanACautiousOne()
        {
            // Placed well outside anyone's range, so both tiers are closing the distance.
            float idealRange = _config.ArmReach + _config.HeadOffset;
            _match.Boxers[1].Position = new Vector2(0f, idealRange * 3f);

            ScriptedBoxerBrain aggressive = new(_config, Settings(aggression: 1f), 8);
            ScriptedBoxerBrain cautious = new(_config, Settings(aggression: 0f), 8);

            BoxerIntent aggressiveIntent = aggressive.Decide(_match, 0, 0.02f);
            BoxerIntent cautiousIntent = cautious.Decide(_match, 0, 0.02f);

            // Both close from here; the difference shows up once inside the pocket.
            Assert.That(Vector2.Dot(aggressiveIntent.Move, Vector2.up), Is.GreaterThan(0.5f));
            Assert.That(Vector2.Dot(cautiousIntent.Move, Vector2.up), Is.GreaterThan(0.5f));

            // The tiers differ in where they break off, not in how fast they walk. At three
            // quarters of the ideal range the cautious tier is already backing out of a
            // pocket the aggressive one is still happy to work in, so this is the distance
            // that separates them. Expressed against the config rather than as a literal:
            // the number that used to sit here was three quarters of a reach the game no
            // longer uses.
            _match.Boxers[1].Position = new Vector2(0f, idealRange * 0.75f);

            float aggressiveApproach = Vector2.Dot(aggressive.Decide(_match, 0, 0.02f).Move, Vector2.up);
            float cautiousApproach = Vector2.Dot(cautious.Decide(_match, 0, 0.02f).Move, Vector2.up);

            Assert.That(cautiousApproach, Is.LessThan(0f),
                "a cautious tier should be giving ground at this range");
            Assert.That(aggressiveApproach, Is.GreaterThan(cautiousApproach),
                "an aggressive tier should be the one willing to stand in the pocket");
        }

        [Test]
        public void ADefaultProfileMatchesTheOriginalTuning()
        {
            BrainProfile profile = ScriptableObject.CreateInstance<BrainProfile>();

            try
            {
                BrainSettings settings = profile.ToSettings();

                Assert.That(settings.EngageRangeScale, Is.EqualTo(0.95f).Within(0.001f));
                Assert.That(settings.BreakRangeScale, Is.EqualTo(1.35f).Within(0.001f));
                Assert.That(settings.RecoverStamina, Is.EqualTo(0.25f).Within(0.001f));
                Assert.That(settings.ResumeStamina, Is.EqualTo(0.55f).Within(0.001f));
                Assert.That(settings.PunchAlignment, Is.GreaterThan(0f),
                    "alignment has to fall out of accuracy when a tier does not set it");
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }
    }
}
