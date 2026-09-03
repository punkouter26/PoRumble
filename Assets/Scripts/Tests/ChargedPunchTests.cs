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
    /// The charged haymaker: winding up trades mobility and a long telegraph for a much
    /// heavier punch, and committing to it locks out the ordinary jab.
    /// </summary>
    public sealed class ChargedPunchTests
    {
        private IObjectResolver _container;
        private MatchModel _match;
        private BoxerSystem _boxerSystem;
        private BoxerConfig _config;
        private readonly List<PunchLandedMessage> _landed = new();
        private readonly List<HaymakerThrownMessage> _haymakers = new();

        /// <summary>
        /// Separation at which a fully extended glove reaches the head but the bodies are far
        /// enough apart to score as a long punch, so the base damage is a known 1.
        /// </summary>
        private const float LONG_RANGE = 1.5f;

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
            _haymakers.Clear();
            _container.Resolve<ISubscriber<PunchLandedMessage>>().Subscribe(m => _landed.Add(m));
            _container.Resolve<ISubscriber<HaymakerThrownMessage>>().Subscribe(m => _haymakers.Add(m));

            FaceOff(LONG_RANGE);
        }

        [TearDown]
        public void TearDown()
        {
            _boxerSystem?.Dispose();
            _container?.Dispose();
            Object.DestroyImmediate(_config);
        }

        /// <summary>Squares the two boxers up at the given separation, each facing the other.</summary>
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
        public void HoldingTheChargeBuildsPower()
        {
            BoxerModel boxer = _match.Boxers[0];

            _boxerSystem.SetCharge(0, true);
            Run(_config.ChargeDuration * 0.5f);

            Assert.That(boxer.Charge.Value, Is.GreaterThan(0.3f).And.LessThan(0.75f),
                "a half-length hold should be roughly half charged");

            Run(_config.ChargeDuration);

            Assert.That(boxer.Charge.Value, Is.EqualTo(1f).Within(0.001f),
                "charge must cap at full rather than growing without bound");
        }

        [Test]
        public void ChargingCocksTheArmBack()
        {
            BoxerModel boxer = _match.Boxers[0];

            _boxerSystem.SetCharge(0, true);
            Run(_config.ChargeDuration);

            Assert.That(boxer.RightArm.Windup, Is.EqualTo(1f).Within(0.001f),
                "the wind-up telegraph is what lets an opponent read the haymaker coming");
        }

        [Test]
        public void ReleasingAFullChargeLandsFarHarderThanAJab()
        {
            // Baseline: an ordinary punch at exactly the same range.
            _boxerSystem.Punch(0, ArmSide.Left);
            Run(0.5f);

            Assert.That(_landed, Is.Not.Empty, "the baseline jab never landed - geometry is wrong");
            int jabDamage = _landed[0].Damage;
            Assert.That(_landed[0].ChargeLevel, Is.EqualTo(0f), "a plain punch carries no charge");

            _landed.Clear();
            FaceOff(LONG_RANGE);
            _match.Boxers[0].Stamina.Value = 1f;

            _boxerSystem.SetCharge(0, true);
            Run(_config.ChargeDuration + 0.1f);
            _boxerSystem.SetCharge(0, false);
            Run(1f);

            Assert.That(_landed, Is.Not.Empty, "the haymaker never landed");
            PunchLandedMessage haymaker = _landed[0];

            Assert.That(haymaker.ChargeLevel, Is.GreaterThan(0.9f));
            Assert.That(haymaker.Damage, Is.GreaterThan(jabDamage),
                "a haymaker that hits no harder than a jab is not worth the wind-up");
            Assert.That(haymaker.Damage,
                Is.EqualTo(Mathf.RoundToInt(jabDamage * _config.ChargeDamageMultiplier)).Within(1));
        }

        [Test]
        public void ReleasingAFullChargeAnnouncesIt()
        {
            _boxerSystem.SetCharge(0, true);
            Run(_config.ChargeDuration + 0.1f);
            _boxerSystem.SetCharge(0, false);
            Run(0.1f);

            Assert.That(_haymakers, Has.Count.EqualTo(1),
                "the wind-up has to be announced so it can be heard coming");
            Assert.That(_haymakers[0].BoxerId, Is.EqualTo(0));
            Assert.That(_haymakers[0].ChargeLevel, Is.GreaterThan(0.9f));
        }

        [Test]
        public void ATapBelowTheMinimumThrowsAnOrdinaryPunch()
        {
            BoxerModel boxer = _match.Boxers[0];

            // Held for a fraction of the minimum: not enough to be a haymaker.
            _boxerSystem.SetCharge(0, true);
            Run(_config.ChargeDuration * _config.MinChargeToRelease * 0.4f);
            _boxerSystem.SetCharge(0, false);
            _boxerSystem.Tick(0.02f);

            Assert.That(boxer.RightArm.Phase, Is.Not.EqualTo(ArmPhase.Idle),
                "a brief hold must still throw something - tapping charge is never a wasted input");
            Assert.That(boxer.RightArm.ChargeLevel, Is.EqualTo(0f),
                "below the minimum it should be an ordinary punch, not a weak haymaker");
            Assert.That(_haymakers, Is.Empty);
        }

        [Test]
        public void ChargingLocksOutTheOrdinaryPunch()
        {
            _boxerSystem.SetCharge(0, true);
            Run(0.1f);

            Assert.That(_boxerSystem.Punch(0, ArmSide.Left), Is.False,
                "jabbing out of a cocked haymaker would make charging strictly better than not");
        }

        [Test]
        public void ChargingSlowsTheBoxerDown()
        {
            BoxerModel boxer = _match.Boxers[0];

            _boxerSystem.SetMoveInput(0, Vector2.right);
            Run(1f);
            float freeSpeed = boxer.Velocity.magnitude;

            boxer.Velocity = Vector2.zero;
            _boxerSystem.SetCharge(0, true);
            _boxerSystem.SetMoveInput(0, Vector2.right);
            Run(1f);
            float chargingSpeed = boxer.Velocity.magnitude;

            Assert.That(chargingSpeed, Is.LessThan(freeSpeed),
                "committing to a haymaker has to cost mobility, or there is no risk in it");
        }

        [Test]
        public void AChargedSwingTakesLongerToLand()
        {
            _boxerSystem.Punch(0, ArmSide.Left);
            int jabTicks = TicksUntilLanded();

            _landed.Clear();
            FaceOff(LONG_RANGE);
            _match.Boxers[0].Stamina.Value = 1f;

            _boxerSystem.SetCharge(0, true);
            Run(_config.ChargeDuration + 0.1f);
            _boxerSystem.SetCharge(0, false);
            int haymakerTicks = TicksUntilLanded();

            Assert.That(haymakerTicks, Is.GreaterThan(jabTicks),
                "the slow wind-up is the counterplay - without it the haymaker is free damage");
        }

        /// <summary>Ticks until something lands, returning how many ticks that took.</summary>
        private int TicksUntilLanded()
        {
            for (int tick = 1; tick <= 200; tick++)
            {
                _boxerSystem.Tick(0.02f);

                if (_landed.Count > 0)
                {
                    return tick;
                }
            }

            Assert.Fail("nothing landed within four seconds");
            return -1;
        }

        [Test]
        public void EliminationClearsAWindUpInProgress()
        {
            BoxerModel boxer = _match.Boxers[0];

            _boxerSystem.SetCharge(0, true);
            Run(_config.ChargeDuration);
            boxer.Eliminate();

            Assert.That(boxer.Charge.Value, Is.EqualTo(0f));
            Assert.That(boxer.RightArm.Windup, Is.EqualTo(0f),
                "a boxer on the canvas must not be left frozen mid-wind-up");
        }
    }
}
