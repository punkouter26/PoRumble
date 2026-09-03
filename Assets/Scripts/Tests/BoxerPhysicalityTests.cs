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
            builder.RegisterMessageBroker<PunchClashedMessage>(options);
            builder.RegisterMessageBroker<HaymakerThrownMessage>(options);
            builder.RegisterMessageBroker<BoxerDodgedMessage>(options);
            _container = builder.Build();

            _config = ScriptableObject.CreateInstance<BoxerConfig>();
            _match = new MatchModel { ArenaHalfExtent = new Vector2(20f, 20f) };
            _match.AddBoxer(new BoxerModel(0, _config.MaxHealth));
            _match.AddBoxer(new BoxerModel(1, _config.MaxHealth));
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
        public void LandedPunchDrivesTheTargetBack()
        {
            // Drive the exchange through the real systems so knockback comes from a genuine hit.
            ContainerBuilder builder = new();
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<PunchLandedMessage>(options);
            builder.RegisterMessageBroker<PunchEvadedMessage>(options);
            builder.RegisterMessageBroker<PunchBlockedMessage>(options);
            builder.RegisterMessageBroker<PunchClashedMessage>(options);
            builder.RegisterMessageBroker<HaymakerThrownMessage>(options);
            builder.RegisterMessageBroker<BoxerDodgedMessage>(options);
            builder.RegisterMessageBroker<BoxerDamagedMessage>(options);
            builder.RegisterMessageBroker<BoxerEliminatedMessage>(options);
            builder.Register<MatchModel>(Lifetime.Singleton);
            builder.RegisterInstance(_config);
            builder.Register<CombatSystem>(Lifetime.Singleton).AsSelf();

            using IObjectResolver container = builder.Build();
            MatchModel match = container.Resolve<MatchModel>();
            match.AddBoxer(new BoxerModel(0, 30));
            match.AddBoxer(new BoxerModel(1, 30));
            CombatSystem combat = container.Resolve<CombatSystem>();

            BoxerModel attacker = match.Boxers[0];
            BoxerModel target = match.Boxers[1];
            attacker.Position = Vector2.zero;
            target.Position = new Vector2(0f, 2f);
            target.Velocity = Vector2.zero;

            container.Resolve<IPublisher<PunchLandedMessage>>()
                     .Publish(new PunchLandedMessage(0, 1, 2, true, new Vector2(0f, 1.5f)));

            Assert.That(target.Velocity.y, Is.GreaterThan(0f),
                "a landed punch should drive the target away from the attacker");

            combat.Dispose();
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

        /// <summary>
        /// Throwing the second fist into the first is no longer refused by the system - the
        /// arms run into each other instead. A fighter that does it on every tick lands
        /// nothing and empties itself, which is the cost that makes one-at-a-time worth
        /// learning rather than worth being told.
        /// </summary>
        [Test]
        public void SpammingBothFistsIsPunished()
        {
            BoxerModel boxer = _match.Boxers[0];
            BoxerModel opponent = _match.Boxers[1];

            // Right in front, square on, at a range a clean punch would land at.
            boxer.Position = Vector2.zero;
            boxer.Facing = Vector2.up;
            opponent.Position = new Vector2(0f, 2.4f);
            opponent.Facing = Vector2.down;

            int landed = 0;
            _container.Resolve<ISubscriber<PunchLandedMessage>>().Subscribe(_ => landed++);

            for (int tick = 0; tick < 500; tick++)
            {
                _boxerSystem.Punch(0, ArmSide.Left);
                _boxerSystem.Punch(0, ArmSide.Right);
                _boxerSystem.Tick(0.02f);
            }

            Assert.That(landed, Is.Zero,
                "a fighter throwing both fists together landed something; the clash has to " +
                "cost the punch or there is nothing to learn from");
            Assert.That(boxer.Stamina.Value, Is.LessThan(0.15f),
                "throwing both fists together has to be expensive as well as useless");
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

            // Held input rather than a fixed cadence. Only one fist may be out at a time, so
            // a hand-picked interval can land while the other arm is still drawing back and
            // silently throw no punches at all.
            for (int tick = 0; tick < 90; tick++)
            {
                _boxerSystem.Punch(0, ArmSide.Left);
                _boxerSystem.Tick(0.02f);
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

            // Punching as a competent fighter does: the next one goes out once the last fist
            // is home. Asking on every single tick regardless is a different test - see
            // SpammingBothFistsIsPunished - and it is now punished rather than merely tiring.
            for (int tick = 0; tick < 1500; tick++)
            {
                if (boxer.LeftArm.Phase != ArmPhase.Extending
                    && boxer.LeftArm.Phase != ArmPhase.Retracting
                    && boxer.RightArm.Phase != ArmPhase.Extending
                    && boxer.RightArm.Phase != ArmPhase.Retracting)
                {
                    _boxerSystem.Punch(0, ArmSide.Left);
                }

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
        public void BackingUpIsSlowerThanAdvancing()
        {
            BoxerModel boxer = _match.Boxers[0];
            _match.Boxers[1].Position = new Vector2(100f, 100f);
            boxer.Facing = Vector2.up;

            _boxerSystem.SetMoveInput(0, Vector2.up);
            Run(2f);
            float advancing = boxer.Velocity.magnitude;

            boxer.Velocity = Vector2.zero;
            _boxerSystem.SetMoveInput(0, Vector2.down);
            Run(2f);
            float retreating = boxer.Velocity.magnitude;

            Assert.That(retreating, Is.LessThan(advancing),
                "a boxer on the back foot must not travel as fast as one walking forward");
        }

        [Test]
        public void SidesteppingIsSlowerThanAdvancing()
        {
            BoxerModel boxer = _match.Boxers[0];
            _match.Boxers[1].Position = new Vector2(100f, 100f);
            boxer.Facing = Vector2.up;

            _boxerSystem.SetMoveInput(0, Vector2.up);
            Run(2f);
            float advancing = boxer.Velocity.magnitude;

            boxer.Velocity = Vector2.zero;
            _boxerSystem.SetMoveInput(0, Vector2.right);
            Run(2f);
            float sidestepping = boxer.Velocity.magnitude;

            Assert.That(sidestepping, Is.LessThan(advancing),
                "a sidestep must not be as quick as walking someone down");
            Assert.That(sidestepping, Is.GreaterThan(0f), "but it must still move the boxer");
        }

        /// <summary>
        /// A thrown punch takes the shoulders with it. Without this the boxer can pivot at
        /// full rate mid-swing, which lets a whiffed punch track a target that has already
        /// stepped off the line.
        /// </summary>
        [Test]
        public void TurningIsSlowerWhileAPunchIsOnItsWay()
        {
            BoxerModel free = _match.Boxers[0];
            BoxerModel committed = _match.Boxers[1];
            free.Position = new Vector2(-50f, 0f);
            committed.Position = new Vector2(50f, 0f);
            free.Facing = Vector2.up;
            committed.Facing = Vector2.up;

            _boxerSystem.SetAim(0, Vector2.right);
            _boxerSystem.SetAim(1, Vector2.right);
            _boxerSystem.Punch(1, ArmSide.Left);

            _boxerSystem.Tick(0.02f);

            float freeTurn = Vector2.Angle(Vector2.up, free.Facing);
            float committedTurn = Vector2.Angle(Vector2.up, committed.Facing);

            Assert.That(committedTurn, Is.LessThan(freeTurn),
                "a boxer mid-punch must not pivot as fast as one with both hands back");
            Assert.That(committedTurn, Is.GreaterThan(0f),
                "but the feet are not nailed down either");
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
