using MessagePipe;
using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using VContainer;

namespace PoRumble.Tests
{
    /// <summary>
    /// Every episode used to open from byte-identical poses, because the spawn pose was a pure
    /// function of roster index and count. Millions of steps of one fixed opening teaches a
    /// policy that opening rather than the game.
    /// </summary>
    public sealed class SpawnRandomisationTests
    {
        private BoxerConfig _config;
        private MatchModel _match;
        private SpawnSystem _spawnSystem;

        private const int BOXER_COUNT = 4;
        private const float SPAWN_RADIUS = 8f;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<BoxerConfig>();
            _match = new MatchModel { ArenaHalfExtent = new Vector2(20f, 20f) };
            _spawnSystem = new SpawnSystem(_match, _config);
            _spawnSystem.SpawnRoster(BOXER_COUNT, SPAWN_RADIUS);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        [Test]
        public void ConsecutiveEpisodesOpenFromDifferentPoses()
        {
            Vector2 firstPosition = _match.Boxers[0].Position;
            Vector2 firstFacing = _match.Boxers[0].Facing;

            _spawnSystem.ResetRoster(BOXER_COUNT, SPAWN_RADIUS);

            Assert.That(Vector2.Distance(firstPosition, _match.Boxers[0].Position), Is.GreaterThan(0.01f),
                "a fresh episode must not re-rack the fighters on the exact same spot");
            Assert.That(Vector2.Angle(firstFacing, _match.Boxers[0].Facing), Is.GreaterThan(0.01f),
                "nor face them in the exact same direction");
        }

        /// <summary>
        /// Jitter must not undo the even spacing: fighters that start already touching are
        /// resolved apart on tick one, which is not an opening anyone meant to train on.
        /// </summary>
        [Test]
        public void FightersStillStartClearOfEachOther()
        {
            float minimumSeparation = _config.BodyRadius * 2f;

            for (int episode = 0; episode < 200; episode++)
            {
                _spawnSystem.ResetRoster(BOXER_COUNT, SPAWN_RADIUS);

                for (int first = 0; first < _match.Boxers.Count; first++)
                {
                    for (int second = first + 1; second < _match.Boxers.Count; second++)
                    {
                        float separation = Vector2.Distance(
                            _match.Boxers[first].Position, _match.Boxers[second].Position);

                        Assert.That(separation, Is.GreaterThan(minimumSeparation),
                            $"episode {episode}: boxers {first} and {second} spawned {separation} apart");
                    }
                }
            }
        }

        /// <summary>
        /// Seeded rather than drawn from UnityEngine.Random, so a training run can be replayed.
        /// </summary>
        [Test]
        public void TheSameSeedProducesTheSameSequenceOfOpenings()
        {
            MatchModel other = new() { ArenaHalfExtent = new Vector2(20f, 20f) };
            SpawnSystem twin = new(other, _config);
            twin.SpawnRoster(BOXER_COUNT, SPAWN_RADIUS);

            for (int episode = 0; episode < 5; episode++)
            {
                _spawnSystem.ResetRoster(BOXER_COUNT, SPAWN_RADIUS);
                twin.ResetRoster(BOXER_COUNT, SPAWN_RADIUS);

                for (int index = 0; index < BOXER_COUNT; index++)
                {
                    Assert.That(other.Boxers[index].Position,
                        Is.EqualTo(_match.Boxers[index].Position),
                        $"episode {episode}, boxer {index} diverged between identical seeds");
                }
            }
        }
    }
}
