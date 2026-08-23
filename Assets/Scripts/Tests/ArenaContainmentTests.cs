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
            _container = builder.Build();

            _config = ScriptableObject.CreateInstance<BoxerConfig>();
            _match = new MatchModel { ArenaHalfExtent = new Vector2(8f, 8f) };
            _match.AddBoxer(new BoxerModel(0, _config.MaxHealth));

            _boxerSystem = new BoxerSystem(_match, _config,
                _container.Resolve<IPublisher<PunchLandedMessage>>());
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
