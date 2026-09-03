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
    /// The slip: a short window of invulnerability bought with stamina and a cooldown.
    ///
    /// It rides a side channel rather than an ML action for the same reason the haymaker
    /// does - PoRumbleBoxer.onnx is compiled against exactly four continuous and two discrete
    /// actions - so these tests exercise BoxerSystem.Dodge directly, which is the one path
    /// every controller reaches it through.
    /// </summary>
    public sealed class DodgeTests
    {
        private IObjectResolver _container;
        private MatchModel _match;
        private BoxerSystem _boxerSystem;
        private BoxerConfig _config;
        private readonly List<PunchLandedMessage> _landed = new();
        private readonly List<PunchEvadedMessage> _evaded = new();
        private readonly List<BoxerDodgedMessage> _dodged = new();

        /// <summary>Separation at which a fully extended glove reaches the head.</summary>
        private float LandingRange => _config.ArmReach + _config.HeadOffset;

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
            _evaded.Clear();
            _dodged.Clear();
            _container.Resolve<ISubscriber<PunchLandedMessage>>().Subscribe(m => _landed.Add(m));
            _container.Resolve<ISubscriber<PunchEvadedMessage>>().Subscribe(m => _evaded.Add(m));
            _container.Resolve<ISubscriber<BoxerDodgedMessage>>().Subscribe(m => _dodged.Add(m));
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

            // Momentum is cleared as well as position. A slip leaves the body travelling
            // sideways at well over walking speed, and a test that only reset the position
            // would watch the target coast straight back out of range.
            attacker.Velocity = Vector2.zero;
            target.Velocity = Vector2.zero;
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
        public void APunchThatWouldLandMissesASlippingTarget()
        {
            FaceOff(LandingRange);

            // Baseline: the identical punch, with nobody slipping.
            _boxerSystem.Punch(0, ArmSide.Left);
            Run(0.5f);
            Assert.That(_landed, Is.Not.Empty, "the baseline punch never landed - geometry is wrong");

            _landed.Clear();
            _match.Boxers[1].ResetTo(new Vector2(0f, LandingRange), Vector2.down, _config.MaxHealth);
            _match.Boxers[0].ResetTo(Vector2.zero, Vector2.up, _config.MaxHealth);

            // The target holds still and slips instead; the position is restored each tick so
            // the miss cannot be explained away by the burst carrying it out of range.
            Vector2 held = _match.Boxers[1].Position;
            Assert.That(_boxerSystem.Dodge(1, Vector2.right), Is.True);
            _boxerSystem.Punch(0, ArmSide.Left);

            int ticks = Mathf.RoundToInt(_config.ArmExtendDuration / 0.02f) + 2;

            for (int tick = 0; tick < ticks; tick++)
            {
                _match.Boxers[1].Position = held;
                _boxerSystem.Tick(0.02f);
            }

            Assert.That(_landed, Is.Empty, "a punch must not land on a boxer mid-slip");
            Assert.That(_evaded, Is.Not.Empty, "a slipped punch should still report as an evade");
        }

        [Test]
        public void TheWindowClosesAndThePunchLandsAgain()
        {
            FaceOff(LandingRange);
            Assert.That(_boxerSystem.Dodge(1, Vector2.right), Is.True);

            // Let the window run out, then put the target back where it started.
            Run(_config.DodgeDuration + 0.1f);
            Assert.That(_match.Boxers[1].IsDodging, Is.False, "the slip should have expired");

            FaceOff(LandingRange);
            _landed.Clear();
            _boxerSystem.Punch(0, ArmSide.Left);
            Run(0.5f);

            Assert.That(_landed, Is.Not.Empty, "once the window closes the head is hittable again");
        }

        [Test]
        public void ASecondSlipIsRefusedUntilTheCooldownExpires()
        {
            FaceOff(LandingRange);

            Assert.That(_boxerSystem.Dodge(1, Vector2.right), Is.True);
            Run(_config.DodgeDuration + 0.05f);

            Assert.That(_boxerSystem.Dodge(1, Vector2.right), Is.False,
                "slipping again this soon would make blocking pointless");

            Run(_config.DodgeCooldown);

            Assert.That(_boxerSystem.Dodge(1, Vector2.right), Is.True,
                "the cooldown has run - a slip should be available again");
        }

        [Test]
        public void SlippingCostsStaminaAndPublishesTheEvent()
        {
            FaceOff(LandingRange);
            float before = _match.Boxers[1].Stamina.Value;

            Assert.That(_boxerSystem.Dodge(1, Vector2.up), Is.True);

            Assert.That(_match.Boxers[1].Stamina.Value,
                Is.EqualTo(before - _config.DodgeStaminaCost).Within(0.0001f),
                "evasion has to be the expensive option or nobody would ever trade");

            Assert.That(_dodged, Has.Count.EqualTo(1));
            Assert.That(_dodged[0].BoxerId, Is.EqualTo(1));
        }

        [Test]
        public void ABoxerCannotPunchOutOfASlip()
        {
            FaceOff(LandingRange);
            Assert.That(_boxerSystem.Dodge(0, Vector2.right), Is.True);

            Assert.That(_boxerSystem.Punch(0, ArmSide.Left), Is.False,
                "the invulnerability window must not double as a free attack");

            Run(_config.DodgeDuration + 0.05f);

            Assert.That(_boxerSystem.Punch(0, ArmSide.Left), Is.True,
                "once the slip ends the fighter should be able to throw again");
        }

        [Test]
        public void ASlipCannotBeStartedOutOfAPunchAlreadyThrown()
        {
            FaceOff(LandingRange);
            Assert.That(_boxerSystem.Punch(0, ArmSide.Left), Is.True);
            _boxerSystem.Tick(0.02f);

            Assert.That(_boxerSystem.Dodge(0, Vector2.right), Is.False,
                "committing the shoulders is what a punch costs; a slip must not cancel it");
        }

        [Test]
        public void ASpentBoxerCannotAffordToSlip()
        {
            FaceOff(LandingRange);
            _match.Boxers[1].Stamina.Value = _config.DodgeStaminaCost * 0.5f;

            Assert.That(_boxerSystem.Dodge(1, Vector2.right), Is.False,
                "a fighter with no breath left has to take the punch");
        }

        [Test]
        public void TheWindowOutlastsAPunchInFlight()
        {
            // The invariant the mechanic rests on. A fighter cannot slip earlier than the
            // moment it sees the arm start to travel, so a window shorter than the flight
            // time expires before the punch lands and buys nothing whatsoever.
            Assert.That(_config.DodgeDuration, Is.GreaterThan(_config.ArmExtendDuration),
                "a slip has to outlast the punch it is slipping");
        }

        [Test]
        public void AnIncomingPunchIsVisibleToTheDefender()
        {
            FaceOff(LandingRange);
            float threatRange = _boxerSystem.DodgeThreatRange;

            Assert.That(
                ThreatMath.IsPunchIncoming(_match.Boxers, _match.Boxers[1], threatRange, _config.MinChargeToRelease),
                Is.False,
                "nothing has been thrown yet");

            _boxerSystem.Punch(0, ArmSide.Left);
            _boxerSystem.Tick(0.02f);

            Assert.That(
                ThreatMath.IsPunchIncoming(_match.Boxers, _match.Boxers[1], threatRange, _config.MinChargeToRelease),
                Is.True,
                "a fist on its way out, square and in range, is exactly what a slip is for");
        }

        [Test]
        public void APunchThrownAtSomebodyElseIsNotAThreat()
        {
            FaceOff(LandingRange);

            // The attacker turns away and throws at thin air.
            _match.Boxers[0].Facing = Vector2.down;
            _boxerSystem.Punch(0, ArmSide.Left);
            _boxerSystem.Tick(0.02f);

            Assert.That(
                ThreatMath.IsPunchIncoming(
                    _match.Boxers, _match.Boxers[1], _boxerSystem.DodgeThreatRange, _config.MinChargeToRelease),
                Is.False,
                "a fighter should not burn a slip on a punch pointed the other way");
        }
    }
}
