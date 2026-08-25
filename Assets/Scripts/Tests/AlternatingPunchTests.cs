using MessagePipe;
using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using VContainer;

namespace PoRumble.Tests
{
    /// <summary>
    /// Held punch input must alternate arms rather than stalling on one, which is the
    /// juggling rhythm of the original.
    /// </summary>
    public sealed class AlternatingPunchTests
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
            builder.RegisterMessageBroker<HaymakerThrownMessage>(options);
            _container = builder.Build();

            _config = ScriptableObject.CreateInstance<BoxerConfig>();
            _match = new MatchModel();
            _match.AddBoxer(new BoxerModel(0, _config.MaxHealth));
            _boxerSystem = new BoxerSystem(_match, _config,
                _container.Resolve<IPublisher<PunchLandedMessage>>(),
                _container.Resolve<IPublisher<PunchEvadedMessage>>(),
                _container.Resolve<IPublisher<PunchBlockedMessage>>(),
                _container.Resolve<IPublisher<HaymakerThrownMessage>>());
        }

        [TearDown]
        public void TearDown()
        {
            _boxerSystem?.Dispose();
            _container?.Dispose();
            Object.DestroyImmediate(_config);
        }

        [Test]
        public void RequestingTheSameArmTwiceThrowsWithTheOther()
        {
            BoxerModel boxer = _match.Boxers[0];

            Assert.That(_boxerSystem.Punch(0, ArmSide.Left), Is.True);
            Assert.That(boxer.LeftArm.Phase, Is.EqualTo(ArmPhase.Extending));

            // Left is busy, so the same request must fall through to the right arm.
            Assert.That(_boxerSystem.Punch(0, ArmSide.Left), Is.True);
            Assert.That(boxer.RightArm.Phase, Is.EqualTo(ArmPhase.Extending));
        }

        [Test]
        public void BothArmsBusy_PunchIsRefused()
        {
            _boxerSystem.Punch(0, ArmSide.Left);
            _boxerSystem.Punch(0, ArmSide.Left);

            Assert.That(_boxerSystem.Punch(0, ArmSide.Left), Is.False,
                "with both arms swinging there is nothing left to throw");
        }

        [Test]
        public void HeldInputKeepsBothArmsWorking()
        {
            BoxerModel boxer = _match.Boxers[0];
            int leftPunches = 0;
            int rightPunches = 0;
            ArmPhase previousLeft = boxer.LeftArm.Phase;
            ArmPhase previousRight = boxer.RightArm.Phase;

            // Hold the punch button for two seconds of physics ticks.
            for (int tick = 0; tick < 100; tick++)
            {
                _boxerSystem.Punch(0, ArmSide.Left);
                _boxerSystem.Tick(0.02f);

                if (previousLeft == ArmPhase.Idle && boxer.LeftArm.Phase == ArmPhase.Extending)
                {
                    leftPunches++;
                }

                if (previousRight == ArmPhase.Idle && boxer.RightArm.Phase == ArmPhase.Extending)
                {
                    rightPunches++;
                }

                previousLeft = boxer.LeftArm.Phase;
                previousRight = boxer.RightArm.Phase;
            }

            Assert.That(leftPunches, Is.GreaterThan(0), "left arm never threw");
            Assert.That(rightPunches, Is.GreaterThan(0), "right arm never threw - no alternation");
        }
    }
}
