using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;

namespace PoRumble.Tests
{
    /// <summary>
    /// The scripted sparring partner has to be a genuinely useful opponent, otherwise the
    /// learners get no more signal from it than they would from self-play against noise.
    /// </summary>
    public sealed class ScriptedBoxerBrainTests
    {
        private BoxerConfig _config;
        private MatchModel _match;
        private ScriptedBoxerBrain _brain;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<BoxerConfig>();
            _match = new MatchModel { ArenaHalfExtent = new Vector2(20f, 20f) };
            _match.AddBoxer(new BoxerModel(0, _config.MaxHealth));
            _match.AddBoxer(new BoxerModel(1, _config.MaxHealth));
            _brain = new ScriptedBoxerBrain(_config);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        private void Place(Vector2 selfPosition, Vector2 selfFacing, Vector2 opponentPosition)
        {
            _match.Boxers[0].Position = selfPosition;
            _match.Boxers[0].Facing = selfFacing;
            _match.Boxers[1].Position = opponentPosition;
        }

        [Test]
        public void ClosesTheDistanceWhenFarAway()
        {
            Place(Vector2.zero, Vector2.right, new Vector2(12f, 0f));

            BoxerIntent intent = _brain.Decide(_match, 0);

            Assert.That(Vector2.Dot(intent.Move.normalized, Vector2.right), Is.GreaterThan(0.5f),
                "should move toward a distant opponent");
        }

        [Test]
        public void AimsAtTheOpponent()
        {
            Place(Vector2.zero, Vector2.up, new Vector2(0f, 6f));

            BoxerIntent intent = _brain.Decide(_match, 0);

            Assert.That(Vector2.Dot(intent.Aim.normalized, Vector2.up), Is.GreaterThan(0.9f));
        }

        [Test]
        public void PunchesOnlyWhenSquareAndInRange()
        {
            float range = _config.ArmReach + _config.HeadOffset;

            // In range and facing the opponent.
            Place(Vector2.zero, Vector2.right, new Vector2(range, 0f));
            Assert.That(_brain.Decide(_match, 0).PunchLeft, Is.True, "should throw when squared up in range");

            // In range but looking the other way.
            Place(Vector2.zero, Vector2.left, new Vector2(range, 0f));
            Assert.That(_brain.Decide(_match, 0).PunchLeft, Is.False, "should not throw while facing away");

            // Squared up but far out of reach.
            Place(Vector2.zero, Vector2.right, new Vector2(14f, 0f));
            Assert.That(_brain.Decide(_match, 0).PunchLeft, Is.False, "should not flail at thin air");
        }

        [Test]
        public void BacksOffToBreatheWhenSpent()
        {
            float range = _config.ArmReach + _config.HeadOffset;
            Place(Vector2.zero, Vector2.right, new Vector2(range, 0f));
            _match.Boxers[0].Stamina.Value = 0.1f;

            BoxerIntent intent = _brain.Decide(_match, 0);

            Assert.That(intent.PunchLeft, Is.False, "an exhausted bot should stop trading");
            Assert.That(Vector2.Dot(intent.Move.normalized, Vector2.right), Is.LessThan(0.5f),
                "and give ground rather than press forward");
        }

        [Test]
        public void RecoveryUsesHysteresisRatherThanFlickering()
        {
            float range = _config.ArmReach + _config.HeadOffset;
            Place(Vector2.zero, Vector2.right, new Vector2(range, 0f));

            _match.Boxers[0].Stamina.Value = 0.1f;
            _brain.Decide(_match, 0);

            // Just above the trigger, but well below the resume threshold: stay in recovery.
            _match.Boxers[0].Stamina.Value = 0.3f;
            Assert.That(_brain.Decide(_match, 0).PunchLeft, Is.False,
                "should keep breathing rather than re-engaging the instant stamina ticks up");

            _match.Boxers[0].Stamina.Value = 0.8f;
            Assert.That(_brain.Decide(_match, 0).PunchLeft, Is.True, "and resume once actually recovered");
        }

        [Test]
        public void IdlesWhenNoOpponentIsLeft()
        {
            _match.Boxers[1].Eliminate();
            Place(Vector2.zero, Vector2.right, new Vector2(3f, 0f));

            BoxerIntent intent = _brain.Decide(_match, 0);

            Assert.That(intent.PunchLeft, Is.False);
            Assert.That(intent.Move, Is.EqualTo(Vector2.zero));
        }
    }
}
