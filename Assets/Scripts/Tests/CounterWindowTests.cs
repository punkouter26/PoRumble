using System.Collections.Generic;
using MessagePipe;
using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using VContainer;

namespace PoRumble.Tests
{
    /// <summary>
    /// Blocking opens a short window in which your own punch counts for more. This is what
    /// makes holding a guard up a way to win rather than only a way to lose slowly.
    /// </summary>
    public sealed class CounterWindowTests
    {
        private IObjectResolver _container;
        private MatchModel _match;
        private BoxerSystem _boxerSystem;
        private BoxerConfig _config;
        private readonly List<PunchLandedMessage> _landed = new();
        private readonly List<PunchBlockedMessage> _blocked = new();

        /// <summary>
        /// Range at which a glove reaches the head, for a guaranteed clean landing: dead
        /// centre of the band a fully extended punch can reach.
        ///
        /// Derived from the config rather than written as a number. These used to be literals
        /// tuned to a geometry the game had long since moved away from, so they silently
        /// stopped describing the ranges they are named after.
        /// </summary>
        private float LandingRange => _config.ArmReach + _config.HeadOffset;

        /// <summary>
        /// Range at which two boxers punching at once meet glove-to-glove: far enough that
        /// neither reaches the other's head, close enough that the gloves collide. Two fully
        /// extended arms touch at exactly twice the reach.
        /// </summary>
        /// <summary>
        /// A separation where two thrown gloves meet but neither reaches a head.
        ///
        /// The window is narrow and it moved. Gloves meet at twice the reach, so the margin
        /// above that is what keeps the heads out of range - and punches now converge on the
        /// centreline as they extend, which means a glove arrives 0.45 nearer the opponent's
        /// spine than it used to and the range at which one can land went from 3.09 to 3.28.
        /// Twice the reach on the nose is now inside that; it was outside it before.
        /// </summary>
        private float GuardRange => _config.ArmReach * 2f + 0.2f;

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

            _landed.Clear();
            _blocked.Clear();
            _container.Resolve<ISubscriber<PunchLandedMessage>>().Subscribe(m => _landed.Add(m));
            _container.Resolve<ISubscriber<PunchBlockedMessage>>().Subscribe(m => _blocked.Add(m));
        }

        [TearDown]
        public void TearDown()
        {
            _boxerSystem?.Dispose();
            _container?.Dispose();
            Object.DestroyImmediate(_config);
        }

        private void FaceOff(float separation)
        {
            BoxerModel attacker = _match.Boxers[0];
            BoxerModel target = _match.Boxers[1];

            attacker.Position = Vector2.zero;
            attacker.Facing = Vector2.up;
            target.Position = new Vector2(0f, separation);
            target.Facing = Vector2.down;
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
        public void BlockingAPunchOpensACounterWindow()
        {
            FaceOff(GuardRange);

            // Both throw at once, so each one's glove runs into the other's.
            _boxerSystem.Punch(0, ArmSide.Right);
            _boxerSystem.Punch(1, ArmSide.Left);
            Run(0.5f);

            Assert.That(_blocked, Is.Not.Empty, "the gloves never met - geometry is wrong");
            Assert.That(_landed, Is.Empty, "at this range neither punch should reach a head");

            BoxerModel blocker = _match.Boxers[_blocked[0].BlockerId];
            Assert.That(blocker.CounterWindow, Is.GreaterThan(0f),
                "stopping a punch has to be worth something");
        }

        [Test]
        public void LandingInsideTheWindowScoresACounter()
        {
            FaceOff(LandingRange);

            // Baseline damage for this exact punch with no window open.
            _boxerSystem.Punch(0, ArmSide.Left);
            Run(0.5f);
            Assert.That(_landed, Is.Not.Empty, "the baseline punch never landed");
            int plainDamage = _landed[0].Damage;
            Assert.That(_landed[0].IsCounter, Is.False);

            _landed.Clear();
            FaceOff(LandingRange);
            _match.Boxers[0].Stamina.Value = 1f;
            _match.Boxers[0].CounterWindow = _config.CounterWindowDuration;

            _boxerSystem.Punch(0, ArmSide.Left);
            Run(0.5f);

            Assert.That(_landed, Is.Not.Empty, "the counter punch never landed");
            Assert.That(_landed[0].IsCounter, Is.True);
            Assert.That(_landed[0].Damage, Is.EqualTo(plainDamage + _config.CounterDamageBonus));
        }

        [Test]
        public void OneBlockBuysExactlyOneCounter()
        {
            FaceOff(LandingRange);
            BoxerModel attacker = _match.Boxers[0];
            attacker.CounterWindow = _config.CounterWindowDuration;

            _boxerSystem.Punch(0, ArmSide.Left);
            Run(0.5f);

            Assert.That(_landed, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(_landed[0].IsCounter, Is.True);
            Assert.That(attacker.CounterWindow, Is.EqualTo(0f),
                "the window must be spent on the punch that used it");

            // Anything landing afterwards is an ordinary punch again.
            for (int index = 1; index < _landed.Count; index++)
            {
                Assert.That(_landed[index].IsCounter, Is.False,
                    "a single block must not buy a damage bonus for the rest of the window");
            }
        }

        [Test]
        public void TheWindowExpires()
        {
            BoxerModel boxer = _match.Boxers[0];
            FaceOff(LandingRange);
            boxer.CounterWindow = _config.CounterWindowDuration;

            Run(_config.CounterWindowDuration + 0.1f);

            Assert.That(boxer.CounterWindow, Is.EqualTo(0f),
                "a counter has to be taken promptly or not at all");
            Assert.That(boxer.HasCounterWindow, Is.False);
        }

        [Test]
        public void RespawningClearsTheWindow()
        {
            BoxerModel boxer = _match.Boxers[0];
            boxer.CounterWindow = _config.CounterWindowDuration;

            boxer.ResetTo(Vector2.zero, Vector2.up, _config.MaxHealth);

            Assert.That(boxer.CounterWindow, Is.EqualTo(0f),
                "a counter window must not survive into the next episode");
        }
    }
}
