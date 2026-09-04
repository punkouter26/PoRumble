using MessagePipe;
using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using VContainer;

namespace PoRumble.Tests
{
    /// <summary>
    /// Covers acceptance criterion 11. Boxer positions are driven by the model, not by physics,
    /// so the ring's wall colliders do not contain anyone on their own.
    /// </summary>
    public sealed class ArenaContainmentTests
    {
        private IObjectResolver _container;
        private MatchModel _match;
        private BoxerSystem _boxerSystem;
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
            _match = new MatchModel { ArenaHalfExtent = new Vector2(8f, 8f) };
            _match.AddBoxer(new BoxerModel(0, _config.MaxHealth));

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

        private void RunFor(float seconds)
        {
            int ticks = Mathf.RoundToInt(seconds / 0.02f);

            for (int tickIndex = 0; tickIndex < ticks; tickIndex++)
            {
                _boxerSystem.Tick(0.02f);
            }
        }

        [Test]
        public void BoxerCannotWalkThroughTheRopes()
        {
            _boxerSystem.SetMoveInput(0, new Vector2(1f, 1f));
            RunFor(20f);

            BoxerModel boxer = _match.Boxers[0];
            float limit = 8f - _config.BodyRadius;

            Assert.That(boxer.Position.x, Is.LessThanOrEqualTo(limit + 0.001f));
            Assert.That(boxer.Position.y, Is.LessThanOrEqualTo(limit + 0.001f));
        }

        [Test]
        public void TwoBoxersCrushedIntoTheRopesStillSeparate()
        {
            // Regression: the separation used to be split in half, with each half clamped on
            // its own, so the half that landed inside a wall was silently discarded. A pair
            // with one man on the ropes therefore stayed overlapped for ever - the correction
            // was reapplied and half-thrown-away on every tick, and the two settled into an
            // equilibrium where neither moved at all. Measured in a live 1v1 as byte-identical
            // positions across 1400+ steps at separation 1.92 against a required 1.96.
            _match.AddBoxer(new BoxerModel(1, _config.MaxHealth));

            float wall = 8f - _config.BodyRadius;

            // One against the ropes, the other pressed into it and still walking in.
            _match.Boxers[0].Position = new Vector2(wall, 0f);
            _match.Boxers[1].Position = new Vector2(wall - 0.05f, 0f);

            _boxerSystem.SetMoveInput(0, Vector2.right);
            _boxerSystem.SetMoveInput(1, Vector2.right);
            RunFor(2f);

            float separation =
                (_match.Boxers[0].Position - _match.Boxers[1].Position).magnitude;

            Assert.That(separation, Is.GreaterThanOrEqualTo(_config.BodyRadius * 2f - 0.001f),
                "a fighter on the ropes must still be pushed clear of the one leaning on him");
        }

        [Test]
        public void ACorneredPairDoesNotFreeze()
        {
            // The lock this exposes is not the overlap itself but the stillness: two fighters
            // holding position to the last decimal for the rest of the round, which makes the
            // match unwinnable and every measurement taken from it meaningless.
            _match.AddBoxer(new BoxerModel(1, _config.MaxHealth));

            float wall = 8f - _config.BodyRadius;
            _match.Boxers[0].Position = new Vector2(wall, wall);
            _match.Boxers[1].Position = new Vector2(wall - 0.05f, wall - 0.05f);

            _boxerSystem.SetMoveInput(0, new Vector2(1f, 1f));
            _boxerSystem.SetMoveInput(1, new Vector2(1f, 1f));
            RunFor(1f);

            float separation =
                (_match.Boxers[0].Position - _match.Boxers[1].Position).magnitude;

            Assert.That(separation, Is.GreaterThan(0.5f),
                "two fighters driven into the same corner must not end up stacked");
        }

        [Test]
        public void BoxerPinnedToTheRopesCanStillMoveAlongThem()
        {
            _boxerSystem.SetMoveInput(0, Vector2.right);
            RunFor(10f);
            float pinnedY = _match.Boxers[0].Position.y;

            // Now slide along the wall.
            _boxerSystem.SetMoveInput(0, Vector2.up);
            RunFor(1f);

            Assert.That(_match.Boxers[0].Position.y, Is.GreaterThan(pinnedY),
                "a cornered boxer must still be able to move laterally");
        }

        [Test]
        public void BoxerPinnedToTheRopesCanStillPunch()
        {
            _boxerSystem.SetMoveInput(0, Vector2.right);
            RunFor(10f);

            _boxerSystem.Punch(0, ArmSide.Left);
            RunFor(0.14f);

            Assert.That(_match.Boxers[0].LeftArm.Extension, Is.GreaterThan(0f),
                "being on the ropes must not lock out punching");
        }
    }
}
