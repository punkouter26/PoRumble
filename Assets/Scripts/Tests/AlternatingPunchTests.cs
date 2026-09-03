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
    /// One fist at a time is no longer a rule in the system - it is a consequence of where the
    /// fists go.
    ///
    /// The gloves converge on the centreline as they extend, the way a straight punch actually
    /// travels, so two arms at full stretch occupy the same air and clash. A fighter that
    /// throws one at a time does so because throwing both costs it the punch, which is a thing
    /// a policy can learn rather than a thing it is told.
    /// </summary>
    public sealed class AlternatingPunchTests
    {
        private IObjectResolver _container;
        private MatchModel _match;
        private BoxerSystem _boxerSystem;
        private BoxerConfig _config;
        private readonly List<PunchClashedMessage> _clashes = new();
        private readonly List<PunchLandedMessage> _landed = new();

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

            // Square on, at a separation where a clean punch reaches the face.
            _match.Boxers[0].Position = Vector2.zero;
            _match.Boxers[0].Facing = Vector2.up;
            _match.Boxers[1].Position = new Vector2(0f, 2.4f);
            _match.Boxers[1].Facing = Vector2.down;

            _boxerSystem = new BoxerSystem(_match, _config,
                _container.Resolve<IPublisher<PunchLandedMessage>>(),
                _container.Resolve<IPublisher<PunchEvadedMessage>>(),
                _container.Resolve<IPublisher<PunchBlockedMessage>>(),
                _container.Resolve<IPublisher<PunchClashedMessage>>(),
                _container.Resolve<IPublisher<HaymakerThrownMessage>>(),
                _container.Resolve<IPublisher<BoxerDodgedMessage>>());

            _clashes.Clear();
            _landed.Clear();
            _container.Resolve<ISubscriber<PunchClashedMessage>>().Subscribe(m => _clashes.Add(m));
            _container.Resolve<ISubscriber<PunchLandedMessage>>().Subscribe(m => _landed.Add(m));
        }

        [TearDown]
        public void TearDown()
        {
            _boxerSystem?.Dispose();
            _container?.Dispose();
            Object.DestroyImmediate(_config);
        }

        private void Run(int ticks)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                _boxerSystem.Tick(0.02f);
            }
        }

        [Test]
        public void TheGlovesConvergeOnTheCentrelineAsTheArmExtends()
        {
            BoxerModel boxer = _match.Boxers[0];

            Vector2 guardLeft = _boxerSystem.GetGlovePosition(boxer, boxer.LeftArm);
            Vector2 guardRight = _boxerSystem.GetGlovePosition(boxer, boxer.RightArm);
            float guardGap = Vector2.Distance(guardLeft, guardRight);

            Assert.That(guardGap, Is.EqualTo(_config.GuardLateralOffset * 2f).Within(0.001f),
                "at rest the hands are carried at the chin, not out level with the shoulders");
            Assert.That(guardGap, Is.LessThan(_config.ArmLateralOffset * 2f),
                "a guard held as wide as the shoulders covers none of the centreline, which " +
                "is exactly where a converging punch arrives");

            _boxerSystem.Punch(0, ArmSide.Left);

            // Eight ticks is 0.16s into a 0.22s extension - mid-travel. Running longer takes
            // the arm through retraction and back to rest, where extension is zero again.
            Run(8);

            Assume.That(boxer.LeftArm.Extension, Is.GreaterThan(0.5f));

            Vector2 thrown = _boxerSystem.GetGlovePosition(boxer, boxer.LeftArm);
            float lateral = Mathf.Abs(Vector2.Dot(thrown - boxer.Position, Vector2.right));

            Assert.That(lateral, Is.LessThan(_config.ArmLateralOffset),
                "the thrown fist did not travel in toward the centreline; a punch on a rail " +
                "parallel to the spine can never meet the other fist, which is what makes " +
                "throwing both at once free");
        }

        [Test]
        public void ForwardReachIsUnchangedByTheConvergence()
        {
            BoxerModel boxer = _match.Boxers[0];
            boxer.LeftArm.TryPunch();

            // Drive the arm to full extension.
            Run(40);

            Vector2 glove = _boxerSystem.GetGlovePosition(boxer, boxer.LeftArm);
            float forward = Vector2.Dot(glove - boxer.Position, boxer.Facing.normalized);

            Assert.That(forward, Is.LessThanOrEqualTo(_config.ArmReach + 0.001f),
                "a punch reached further than ArmReach, which would move every damage band " +
                "the config is tuned against");
        }

        [Test]
        public void ThrowingBothFistsTogetherIsAllowed()
        {
            BoxerModel boxer = _match.Boxers[0];

            Assert.That(_boxerSystem.Punch(0, ArmSide.Left), Is.True);
            Assert.That(_boxerSystem.Punch(0, ArmSide.Right), Is.True,
                "the system must no longer refuse the second fist - the anatomy is what " +
                "punishes it now, not a rule");
            Assert.That(boxer.LeftArm.Phase, Is.EqualTo(ArmPhase.Extending));
            Assert.That(boxer.RightArm.Phase, Is.EqualTo(ArmPhase.Extending));
        }

        [Test]
        public void ThrowingBothFistsTogetherClashesAndLandsNothing()
        {
            _boxerSystem.Punch(0, ArmSide.Left);
            _boxerSystem.Punch(0, ArmSide.Right);
            Run(40);

            Assert.That(_clashes.Count, Is.GreaterThan(0),
                "both fists were thrown together and never ran into each other");
            Assert.That(_landed.Count, Is.Zero,
                "a clashed punch still did damage; the clash has to cost the punch or there " +
                "is nothing to learn from");
        }

        [Test]
        public void AClashCostsABeatOfOffence()
        {
            _boxerSystem.Punch(0, ArmSide.Left);
            _boxerSystem.Punch(0, ArmSide.Right);

            // Stopped on the clash rather than run past it. The recovery is short by design,
            // so a fixed run long enough to guarantee the clash also runs out the cooldown it
            // is supposed to be measuring.
            for (int tick = 0; tick < 40 && _clashes.Count == 0; tick++)
            {
                _boxerSystem.Tick(0.02f);
            }

            Assume.That(_clashes.Count, Is.GreaterThan(0));

            BoxerModel boxer = _match.Boxers[0];

            Assert.That(boxer.LeftArm.CanPunch, Is.False);
            Assert.That(boxer.RightArm.CanPunch, Is.False);
            Assert.That(_boxerSystem.Punch(0, ArmSide.Left), Is.False,
                "a clash left the fighter free to throw again immediately, so clashing costs " +
                "nothing and the fighter has no reason to stop");
        }

        [Test]
        public void AOneTwoThrownInSequenceDoesNotClash()
        {
            BoxerModel boxer = _match.Boxers[0];

            Assume.That(_boxerSystem.Punch(0, ArmSide.Left), Is.True);

            // Let the first punch resolve and start coming back before the second goes out.
            for (int tick = 0; tick < 60; tick++)
            {
                _boxerSystem.Tick(0.02f);

                if (boxer.LeftArm.Phase == ArmPhase.CoolingDown)
                {
                    break;
                }
            }

            Assume.That(boxer.LeftArm.Phase, Is.EqualTo(ArmPhase.CoolingDown));
            _clashes.Clear();

            Assert.That(_boxerSystem.Punch(0, ArmSide.Right), Is.True);
            Run(40);

            Assert.That(_clashes.Count, Is.Zero,
                "a properly sequenced one-two clashed; only genuinely simultaneous punches " +
                "should, or the combination is unthrowable");
        }

        [Test]
        public void OnceTheFirstArmIsBusyTheRequestFallsThroughToTheOther()
        {
            BoxerModel boxer = _match.Boxers[0];

            Assert.That(_boxerSystem.Punch(0, ArmSide.Left), Is.True);

            for (int tick = 0; tick < 60; tick++)
            {
                _boxerSystem.Tick(0.02f);

                if (boxer.LeftArm.Phase == ArmPhase.CoolingDown)
                {
                    break;
                }
            }

            Assume.That(boxer.LeftArm.Phase, Is.EqualTo(ArmPhase.CoolingDown));

            // Left cannot throw again yet, so the same request must reach the right arm.
            Assert.That(_boxerSystem.Punch(0, ArmSide.Left), Is.True);
            Assert.That(boxer.RightArm.Phase, Is.EqualTo(ArmPhase.Extending));
        }

        [Test]
        public void HeldInputKeepsBothArmsWorking()
        {
            BoxerModel boxer = _match.Boxers[0];
            int leftPunches = 0;
            int rightPunches = 0;
            ArmPhase previousLeft = boxer.LeftArm.Phase;
            ArmPhase previousRight = boxer.RightArm.Phase;

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
