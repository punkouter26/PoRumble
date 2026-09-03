using MessagePipe;
using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using VContainer;

namespace PoRumble.Tests
{
    /// <summary>
    /// A ring does not have to sit on the origin any more, because a training scene can hold
    /// several at once.
    ///
    /// The offset lives in the model rather than in the transform hierarchy, and that is
    /// forced: the torso is moved with Rigidbody2D.MovePosition, which is world-space and
    /// ignores its parents, so an arena offset by re-parenting would move the drawn ring and
    /// leave every fighter simulating on top of the arena next door. These pin the two places
    /// that would otherwise still assume zero.
    /// </summary>
    public sealed class ArenaOffsetTests
    {
        private static readonly Vector2 ArenaCenter = new(80f, 160f);

        private IObjectResolver _container;
        private MatchModel _match;
        private BoxerSystem _boxerSystem;
        private SpawnSystem _spawnSystem;
        private BoxerConfig _config;

        [SetUp]
        public void SetUp()
        {
            ContainerBuilder builder = new();
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<PunchLandedMessage>(options);
            builder.RegisterMessageBroker<PunchEvadedMessage>(options);
            builder.RegisterMessageBroker<PunchBlockedMessage>(options);
            builder.RegisterMessageBroker<PunchClashedMessage>(options);
            builder.RegisterMessageBroker<HaymakerThrownMessage>(options);
            builder.RegisterMessageBroker<BoxerDodgedMessage>(options);
            _container = builder.Build();

            _config = ScriptableObject.CreateInstance<BoxerConfig>();
            _match = new MatchModel
            {
                ArenaHalfExtent = new Vector2(20f, 20f),
                ArenaCenter = ArenaCenter,
            };

            _spawnSystem = new SpawnSystem(_match, _config);
            _boxerSystem = new BoxerSystem(_match, _config,
                _container.Resolve<IPublisher<PunchLandedMessage>>(),
                _container.Resolve<IPublisher<PunchEvadedMessage>>(),
                _container.Resolve<IPublisher<PunchBlockedMessage>>(),
                _container.Resolve<IPublisher<PunchClashedMessage>>(),
                _container.Resolve<IPublisher<HaymakerThrownMessage>>(),
                _container.Resolve<IPublisher<BoxerDodgedMessage>>());
        }

        [TearDown]
        public void TearDown()
        {
            _boxerSystem?.Dispose();
            _container?.Dispose();
            Object.DestroyImmediate(_config);
        }

        [Test]
        public void FightersSpawnAroundTheirOwnRing()
        {
            _spawnSystem.SpawnRoster(10, 15f);

            for (int boxerIndex = 0; boxerIndex < _match.Boxers.Count; boxerIndex++)
            {
                float distance = Vector2.Distance(_match.Boxers[boxerIndex].Position, ArenaCenter);

                Assert.That(distance, Is.LessThan(20f),
                    $"boxer {boxerIndex} spawned {distance} from its own ring centre - an " +
                    "arena that spawns on the world origin drops ten fighters into whichever " +
                    "arena happens to be there");
            }
        }

        [Test]
        public void FightersAreHeldInsideTheirOwnRopes()
        {
            _spawnSystem.SpawnRoster(2, 4f);

            // Walk both of them hard at the world origin, which is a long way outside this ring.
            _boxerSystem.SetMoveInput(0, Vector2.left);
            _boxerSystem.SetMoveInput(1, Vector2.down);

            for (int tick = 0; tick < 600; tick++)
            {
                _boxerSystem.Tick(0.02f);
            }

            Vector2 limit = _match.ArenaHalfExtent - Vector2.one * _config.BodyRadius;

            for (int boxerIndex = 0; boxerIndex < _match.Boxers.Count; boxerIndex++)
            {
                Vector2 local = _match.Boxers[boxerIndex].Position - ArenaCenter;

                Assert.That(Mathf.Abs(local.x), Is.LessThanOrEqualTo(limit.x + 0.01f),
                    $"boxer {boxerIndex} walked out of its ring on x");
                Assert.That(Mathf.Abs(local.y), Is.LessThanOrEqualTo(limit.y + 0.01f),
                    $"boxer {boxerIndex} walked out of its ring on y");
            }
        }

        [Test]
        public void ARingOnTheOriginIsUnchanged()
        {
            MatchModel plain = new() { ArenaHalfExtent = new Vector2(20f, 20f) };

            Assert.That(plain.ArenaCenter, Is.EqualTo(Vector2.zero),
                "the default arena centre moved, which would shift both shipped scenes");
        }
    }
}
