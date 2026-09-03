using MessagePipe;
using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using VContainer;

namespace PoRumble.Tests
{
    /// <summary>
    /// A knockout is trauma arriving faster than it can be shed, not a health bar reaching
    /// zero. These pin the parts of that which are easy to tune into meaninglessness: a
    /// wobble has to be earned with a combination, it has to fade, and while it lasts it has
    /// to cost the feet more than the hands.
    /// </summary>
    public sealed class StunTests
    {
        private IObjectResolver _container;
        private MatchModel _match;
        private BoxerSystem _boxerSystem;
        private CombatSystem _combatSystem;
        private BoxerConfig _config;
        private IPublisher<PunchLandedMessage> _punchPublisher;

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
            builder.RegisterMessageBroker<BoxerDamagedMessage>(options);
            builder.RegisterMessageBroker<BoxerEliminatedMessage>(options);
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

            _combatSystem = new CombatSystem(_match, _config,
                _container.Resolve<ISubscriber<PunchLandedMessage>>(),
                _container.Resolve<ISubscriber<PunchBlockedMessage>>(),
                _container.Resolve<IPublisher<BoxerDamagedMessage>>(),
                _container.Resolve<IPublisher<BoxerEliminatedMessage>>());

            _punchPublisher = _container.Resolve<IPublisher<PunchLandedMessage>>();
        }

        [TearDown]
        public void TearDown()
        {
            _combatSystem?.Dispose();
            _boxerSystem?.Dispose();
            _container?.Dispose();
            Object.DestroyImmediate(_config);
        }

        private void Land(int damage)
        {
            _punchPublisher.Publish(new PunchLandedMessage(0, 1, damage, false, Vector2.zero, false, 0f));
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
        public void OnePunchDoesNotWobbleAnybody()
        {
            Land(2);

            Assert.That(_boxerSystem.IsStunned(_match.Boxers[1]), Is.False,
                "a single punch crossed the stun threshold; the wobble has to be earned with " +
                "a combination or it is just a second health bar");
        }

        [Test]
        public void ACombinationWobbles()
        {
            Land(2);
            Land(2);
            Land(2);

            Assert.That(_boxerSystem.IsStunned(_match.Boxers[1]), Is.True,
                "three landed punches inside a tick did not wobble the target");
        }

        [Test]
        public void AWobbleFadesWhenThePressureStops()
        {
            Land(2);
            Land(2);
            Land(2);
            Assume.That(_boxerSystem.IsStunned(_match.Boxers[1]), Is.True);

            Run(2f);

            Assert.That(_boxerSystem.IsStunned(_match.Boxers[1]), Is.False,
                "the wobble outlasted two seconds of no contact, which makes it a stun-lock " +
                "rather than a window");
        }

        [Test]
        public void StunIsCappedSoAHaymakerCannotBankOne()
        {
            for (int punch = 0; punch < 40; punch++)
            {
                _match.Boxers[1].Health.Value = _config.MaxHealth;
                Land(2);
            }

            Assert.That(_match.Boxers[1].Stun, Is.LessThanOrEqualTo(_config.MaxStun + 0.001f),
                "stun accumulated past its ceiling");
        }

        [Test]
        public void AWobbledBoxerTurnsSlowerThanItWalks()
        {
            Assert.That(_config.StunnedTurnScale, Is.LessThan(_config.StunnedMobilityScale),
                "a wobbled fighter must lose the ability to keep its guard pointed at the " +
                "attacker faster than it loses the ability to walk - that is what opens the " +
                "face arc and makes pressing an advantage the correct play");
        }

        [Test]
        public void AWobbledBoxerTurnsSlowerThanAFreshOne()
        {
            BoxerModel fresh = _match.Boxers[0];
            BoxerModel wobbled = _match.Boxers[1];

            foreach (BoxerModel boxer in new[] { fresh, wobbled })
            {
                boxer.Position = Vector2.zero;
                boxer.Facing = Vector2.up;
            }

            // Comfortably above the threshold, not exactly on it: TickStun sheds before
            // TickMovement reads, so a boxer seeded at the line is already under it by the
            // time the turn is computed.
            wobbled.Stun = _config.MaxStun;

            _boxerSystem.SetAim(0, Vector2.right);
            _boxerSystem.SetAim(1, Vector2.right);
            _boxerSystem.Tick(0.02f);

            float freshTurn = Vector2.Angle(Vector2.up, fresh.Facing);
            float wobbledTurn = Vector2.Angle(Vector2.up, wobbled.Facing);

            Assert.That(wobbledTurn, Is.LessThan(freshTurn),
                $"wobbled boxer turned {wobbledTurn} degrees against a fresh one's {freshTurn}");
        }

        [Test]
        public void EliminationClearsTheWobble()
        {
            _match.Boxers[1].Stun = _config.MaxStun;
            _match.Boxers[1].Eliminate();

            Assert.That(_match.Boxers[1].Stun, Is.Zero,
                "stun survived elimination and would carry into the next episode");
        }

        [Test]
        public void ANewEpisodeClearsTheWobble()
        {
            _match.Boxers[1].Stun = _config.MaxStun;
            _match.Boxers[1].ResetTo(Vector2.zero, Vector2.up, _config.MaxHealth);

            Assert.That(_match.Boxers[1].Stun, Is.Zero,
                "stun survived a reset and would poison the opening of the next episode");
        }

        [Test]
        public void ThrowingAPunchCarriesTheBodyForward()
        {
            BoxerModel boxer = _match.Boxers[0];
            boxer.Position = Vector2.zero;
            boxer.Facing = Vector2.up;
            boxer.Velocity = Vector2.zero;

            Assume.That(_boxerSystem.Punch(0, ArmSide.Left), Is.True);

            Assert.That(boxer.Velocity.y, Is.GreaterThan(0f),
                "a thrown punch left the body standing still; force comes from stepping into " +
                "it, and an arm extending off a static torso reads as a reach");
        }
    }
}
