using MessagePipe;
using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using VContainer;

namespace PoRumble.Tests
{
    /// <summary>
    /// A boxer throws one punch at a time, and held input alternates arms rather than
    /// stalling on one - the juggling rhythm of the original.
    ///
    /// The two rules work together: a request is refused while a fist is still out, and once
    /// that arm is drawing breath in cooldown the next request falls through to the other arm.
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
        public void APunchIsRefusedWhileTheOtherFistIsStillOut()
        {
            BoxerModel boxer = _match.Boxers[0];

            Assert.That(_boxerSystem.Punch(0, ArmSide.Left), Is.True);
            Assert.That(boxer.LeftArm.Phase, Is.EqualTo(ArmPhase.Extending));

            Assert.That(_boxerSystem.Punch(0, ArmSide.Right), Is.False,
                "the left fist is still travelling, so the right must not also fire");
            Assert.That(boxer.RightArm.Phase, Is.EqualTo(ArmPhase.Idle));
        }

        [Test]
        public void OnceTheFirstArmIsBackTheRequestFallsThroughToTheOther()
        {
            BoxerModel boxer = _match.Boxers[0];

            Assert.That(_boxerSystem.Punch(0, ArmSide.Left), Is.True);

            // Run out the extend and retract phases; the left arm is then cooling down with
            // its fist already back at the guard.
            // Generous cap: the extend and retract phases both stretch as stamina falls, so
            // the cycle runs a little past its nominal 0.30s after the very first punch.
            for (int tick = 0; tick < 60; tick++)
            {
                _boxerSystem.Tick(0.02f);

                if (boxer.LeftArm.Phase == ArmPhase.CoolingDown)
                {
                    break;
                }
            }

            Assert.That(boxer.LeftArm.Phase, Is.EqualTo(ArmPhase.CoolingDown),
                "left arm should have finished retracting");

            // Left cannot throw again yet, so the same request must reach the right arm.
            Assert.That(_boxerSystem.Punch(0, ArmSide.Left), Is.True);
            Assert.That(boxer.RightArm.Phase, Is.EqualTo(ArmPhase.Extending));
        }

        [Test]
        public void HeldInputNeverPutsBothFistsOutAtOnce()
        {
            BoxerModel boxer = _match.Boxers[0];

            for (int tick = 0; tick < 300; tick++)
            {
                // Ask for both arms every tick, the way a held two-button input or a policy
                // firing both discrete branches would.
                _boxerSystem.Punch(0, ArmSide.Left);
                _boxerSystem.Punch(0, ArmSide.Right);
                _boxerSystem.Tick(0.02f);

                bool leftOut = boxer.LeftArm.Extension > 0f;
                bool rightOut = boxer.RightArm.Extension > 0f;

                Assert.That(leftOut && rightOut, Is.False,
                    $"both fists were extended on tick {tick}");
            }
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
