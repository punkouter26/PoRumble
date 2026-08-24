using MessagePipe;
using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using VContainer;

namespace PoRumble.Tests
{
    /// <summary>Boxers occupy space, carry momentum, and tire.</summary>
    public sealed class BoxerPhysicalityTests
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
            _container = builder.Build();

            _config = ScriptableObject.CreateInstance<BoxerConfig>();
            _match = new MatchModel { ArenaHalfExtent = new Vector2(20f, 20f) };
            _match.AddBoxer(new BoxerModel(0, _config.MaxHealth));
            _match.AddBoxer(new BoxerModel(1, _config.MaxHealth));
            _boxerSystem = new BoxerSystem(_match, _config,
                _container.Resolve<IPublisher<PunchLandedMessage>>(),
                _container.Resolve<IPublisher<PunchEvadedMessage>>(),
                _container.Resolve<IPublisher<PunchBlockedMessage>>());
        }

        [TearDown]
        public void TearDown()
        {
            _boxerSystem?.Dispose();
            _container?.Dispose();
            Object.DestroyImmediate(_config);
        }

        private void Run(float seconds)
        {
            int ticks = Mathf.RoundToInt(seconds / 0.02f);

            for (int tick = 0; tick < ticks; tick++)
            {
                _boxerSystem.Tick(0.02f);
            }
        }

        [Test]
        public void BoxersCannotOverlap()
        {
            BoxerModel a = _match.Boxers[0];
            BoxerModel b = _match.Boxers[1];
            a.Position = new Vector2(-3f, 0f);
            b.Position = new Vector2(3f, 0f);

            // Walk them straight into each other.
            _boxerSystem.SetMoveInput(0, Vector2.right);
            _boxerSystem.SetMoveInput(1, Vector2.left);
            Run(6f);

            float separation = Vector2.Distance(a.Position, b.Position);
            Assert.That(separation, Is.GreaterThanOrEqualTo(_config.BodyRadius * 2f - 0.01f),
                $"boxers overlapped: {separation} apart, bodies are {_config.BodyRadius * 2f} wide");
        }

        [Test]
        public void CoincidentBoxersArePushedApart()
        {
            _match.Boxers[0].Position = Vector2.zero;
            _match.Boxers[1].Position = Vector2.zero;

            Run(0.1f);

            Assert.That(Vector2.Distance(_match.Boxers[0].Position, _match.Boxers[1].Position),
                Is.GreaterThan(0f), "exactly coincident boxers must still separate");
        }

        [Test]
        public void MovementAcceleratesRatherThanSnapping()
        {
            _boxerSystem.SetMoveInput(0, Vector2.right);
            _boxerSystem.Tick(0.02f);

            float firstTick = _match.Boxers[0].Velocity.magnitude;
            Assert.That(firstTick, Is.GreaterThan(0f), "should start moving");
            Assert.That(firstTick, Is.LessThan(_config.MoveSpeed),
                "should not reach full speed in a single tick");

            Run(2f);
            Assert.That(_match.Boxers[0].Velocity.magnitude, Is.GreaterThan(firstTick),
                "should build up speed over time");
        }

        [Test]
        public void TurningIsNotInstant()
        {
            BoxerModel boxer = _match.Boxers[0];
            boxer.Facing = Vector2.up;

            _boxerSystem.SetAim(0, Vector2.down);
            _boxerSystem.Tick(0.02f);

            Assert.That(Vector2.Angle(boxer.Facing, Vector2.down), Is.GreaterThan(1f),
                "a boxer must not pivot 180 degrees in one tick");

            Run(2f);
            Assert.That(Vector2.Angle(boxer.Facing, Vector2.down), Is.LessThan(5f),
                "but it should get there");
        }

        [Test]
        public void PunchingDrainsStaminaAndRestingRestoresIt()
        {
            BoxerModel boxer = _match.Boxers[0];
            Assert.That(boxer.Stamina.Value, Is.EqualTo(1f));

            for (int punch = 0; punch < 6; punch++)
            {
                _boxerSystem.Punch(0, ArmSide.Left);
                Run(0.3f);
            }

            float spent = boxer.Stamina.Value;
            Assert.That(spent, Is.LessThan(1f), "throwing punches must cost stamina");

            Run(4f);
            Assert.That(boxer.Stamina.Value, Is.GreaterThan(spent), "standing off must recover it");
        }

        [Test]
        public void SustainedPunchingSettlesAboveEmpty()
        {
            // Spamming must tire a boxer without pinning it at zero: as stamina falls the arms
            // slow down, so drain and recovery meet at an equilibrium.
            BoxerModel boxer = _match.Boxers[0];
            _match.Boxers[1].Position = new Vector2(100f, 100f);

            for (int tick = 0; tick < 1500; tick++)
            {
                _boxerSystem.Punch(0, ArmSide.Left);
                _boxerSystem.Tick(0.02f);
            }

            Assert.That(boxer.Stamina.Value, Is.LessThan(0.95f), "constant punching must tire a boxer");
            Assert.That(boxer.Stamina.Value, Is.GreaterThan(0.15f),
                $"stamina settled at {boxer.Stamina.Value}; exhaustion should throttle output "
                + "into an equilibrium rather than scrape the floor");
        }

        [Test]
        public void ExhaustedBoxersPunchSlower()
        {
            BoxerModel fresh = _match.Boxers[0];
            _match.Boxers[1].Position = new Vector2(100f, 100f);

            _boxerSystem.Punch(0, ArmSide.Left);
            int freshTicks = 0;
            while (fresh.LeftArm.Phase != ArmPhase.Idle && freshTicks < 500) { _boxerSystem.Tick(0.02f); freshTicks++; }

            fresh.Stamina.Value = 0f;
            _boxerSystem.Punch(0, ArmSide.Left);
            int tiredTicks = 0;
            while (fresh.LeftArm.Phase != ArmPhase.Idle && tiredTicks < 500)
            {
                fresh.Stamina.Value = 0f;   // hold it empty
                _boxerSystem.Tick(0.02f);
                tiredTicks++;
            }

            Assert.That(tiredTicks, Is.GreaterThan(freshTicks),
                "a spent boxer's punch cycle should take longer than a fresh one's");
        }

        [Test]
        public void ExhaustedBoxersMoveSlower()
        {
            BoxerModel boxer = _match.Boxers[0];
            _match.Boxers[1].Position = new Vector2(100f, 100f);

            _boxerSystem.SetMoveInput(0, Vector2.right);
            Run(3f);
            float freshSpeed = boxer.Velocity.magnitude;

            boxer.Stamina.Value = 0f;
            Run(3f);
            float tiredSpeed = boxer.Velocity.magnitude;

            Assert.That(tiredSpeed, Is.LessThan(freshSpeed),
                "a spent boxer should not move as fast as a fresh one");
        }
    }
}
