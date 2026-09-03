using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;

namespace PoRumble.Tests
{
    /// <summary>
    /// Six fighters share one set of weights. The modulator is what makes them fight
    /// differently: it bends the actions the network produced on the way to the boxer, and
    /// reaches the charge and slip side channels that were never ML actions in the first place.
    ///
    /// The action vector itself is untouched throughout - that is the whole constraint the
    /// class exists to satisfy - so these tests are about the transform, not about the policy.
    /// </summary>
    public sealed class StyleModulatorTests
    {
        private BoxerConfig _config;
        private MatchModel _match;

        /// <summary>Seconds between decisions at DecisionPeriod 5 on a 50Hz physics clock.</summary>
        private const float DECISION_DELTA = 0.1f;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<BoxerConfig>();
            _match = new MatchModel { ArenaHalfExtent = new Vector2(20f, 20f) };
            _match.AddBoxer(new BoxerModel(0, _config.MaxHealth));
            _match.AddBoxer(new BoxerModel(1, _config.MaxHealth));

            // Square, and inside the range where a punch can reach a head.
            _match.Boxers[0].Position = Vector2.zero;
            _match.Boxers[0].Facing = Vector2.up;
            _match.Boxers[1].Position = new Vector2(0f, _config.ArmReach + _config.HeadOffset);
            _match.Boxers[1].Facing = Vector2.down;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
        }

        private BoxerIntent Modulate(FighterStyle style, BoxerIntent policy)
        {
            StyleModulator modulator = new(_config, style, 1);
            return modulator.Modulate(policy, _match, 0, DECISION_DELTA);
        }

        [Test]
        public void ANeutralStyleChangesNothing()
        {
            BoxerIntent policy = new(Vector2.right, Vector2.up, true, false);
            BoxerIntent result = Modulate(FighterStyle.Neutral, policy);

            Assert.That(result.Move, Is.EqualTo(policy.Move));
            Assert.That(result.Aim, Is.EqualTo(policy.Aim));
            Assert.That(result.PunchLeft, Is.True);
            Assert.That(result.Charge, Is.False);
            Assert.That(result.Dodge, Is.False,
                "Standard RL is the policy driven straight through - it must stay that way");
        }

        [Test]
        public void PressureLeansTheMovementForward()
        {
            FighterStyle walkDown = new(0.8f, 0f, 1f, 0f, 0f, 0f);
            BoxerIntent result = Modulate(walkDown, new BoxerIntent(Vector2.zero, Vector2.up, false, false));

            // Facing is up, so forward pressure has to show up as travel up the ring.
            Assert.That(Vector2.Dot(result.Move, _match.Boxers[0].Facing), Is.GreaterThan(0.5f));
        }

        [Test]
        public void NegativePressureFightsOffTheBackFoot()
        {
            FighterStyle outFighter = new(-0.8f, 0f, 1f, 0f, 0f, 0f);
            BoxerIntent result = Modulate(outFighter, new BoxerIntent(Vector2.zero, Vector2.up, false, false));

            Assert.That(Vector2.Dot(result.Move, _match.Boxers[0].Facing), Is.LessThan(-0.5f));
        }

        [Test]
        public void TheAimIsNeverBent()
        {
            FighterStyle busy = new(0.9f, 0.9f, 0.5f, 0.5f, 0.5f, 0.5f);
            Vector2 aim = new Vector2(0.3f, 0.9f).normalized;
            BoxerIntent result = Modulate(busy, new BoxerIntent(Vector2.zero, aim, false, false));

            Assert.That(result.Aim, Is.EqualTo(aim),
                "pointing at an opponent is the one thing the network is good at; " +
                "rotating it makes a worse fighter, not a different one");
        }

        [Test]
        public void AClosedGateSwallowsEveryPunch()
        {
            FighterStyle pacifist = new(0f, 0f, 0f, 0f, 0f, 0f);
            BoxerIntent result = Modulate(pacifist, new BoxerIntent(Vector2.zero, Vector2.up, true, true));

            Assert.That(result.PunchLeft, Is.False);
            Assert.That(result.PunchRight, Is.False);
        }

        [Test]
        public void AnOpportunistThrowsPunchesThePolicyPassedOn()
        {
            FighterStyle swarmer = new(0f, 0f, 1f, 1f, 0f, 0f);
            BoxerIntent result = Modulate(swarmer, new BoxerIntent(Vector2.zero, Vector2.up, false, false));

            Assert.That(result.PunchLeft, Is.True,
                "square and inside range is exactly the opening a volume puncher takes");
        }

        [Test]
        public void AnOpportunistDoesNotSwingAtNothing()
        {
            // Turned away: no opening, so the extra punch must not be thrown.
            _match.Boxers[0].Facing = Vector2.down;

            FighterStyle swarmer = new(0f, 0f, 1f, 1f, 0f, 0f);
            BoxerIntent result = Modulate(swarmer, new BoxerIntent(Vector2.zero, Vector2.down, false, false));

            Assert.That(result.PunchLeft, Is.False);
            Assert.That(result.PunchRight, Is.False);
        }

        [Test]
        public void ASlipperGetsOutOfTheWayOfAPunchItCanSee()
        {
            // The opponent commits a fist.
            _match.Boxers[1].RightArm.TryPunch();
            _match.Boxers[1].RightArm.Tick(0.02f, _config.ArmExtendDuration, _config.ArmRetractDuration, 0.1f);

            FighterStyle slippery = new(0f, 0f, 1f, 0f, 0f, 1f);
            BoxerIntent result = Modulate(slippery, new BoxerIntent(Vector2.zero, Vector2.up, false, false));

            Assert.That(result.Dodge, Is.True);
        }

        [Test]
        public void ASlipperStandsStillWhenNothingIsComing()
        {
            FighterStyle slippery = new(0f, 0f, 1f, 0f, 0f, 1f);
            BoxerIntent result = Modulate(slippery, new BoxerIntent(Vector2.zero, Vector2.up, false, false));

            Assert.That(result.Dodge, Is.False,
                "a slip has a cooldown; spending it on nothing is how a fighter gets hit");
        }

        [Test]
        public void ChargingNeverCoexistsWithAJab()
        {
            // Certain to charge: BoxerSystem refuses ordinary punches while a wind-up is held,
            // so a style that asked for both would silently throw the punch away.
            FighterStyle bomber = new(0f, 0f, 1f, 1f, 1f, 0f);
            StyleModulator modulator = new(_config, bomber, 1);

            for (int decision = 0; decision < 40; decision++)
            {
                BoxerIntent result = modulator.Modulate(
                    new BoxerIntent(Vector2.zero, Vector2.up, true, true), _match, 0, DECISION_DELTA);

                if (result.Charge)
                {
                    Assert.That(result.PunchLeft, Is.False);
                    Assert.That(result.PunchRight, Is.False);
                    return;
                }
            }

            Assert.Fail("a style with a full charge chance never wound one up in forty decisions");
        }

        [Test]
        public void TwoFightersOnTheSameStyleStillDifferBySeed()
        {
            FighterStyle style = new(0f, 0f, 0.5f, 0f, 0f, 0f);
            StyleModulator first = new(_config, style, 1);
            StyleModulator second = new(_config, style, 2);
            BoxerIntent policy = new(Vector2.zero, Vector2.up, true, false);

            bool diverged = false;

            for (int decision = 0; decision < 32 && !diverged; decision++)
            {
                bool a = first.Modulate(policy, _match, 0, DECISION_DELTA).PunchLeft;
                bool b = second.Modulate(policy, _match, 0, DECISION_DELTA).PunchLeft;
                diverged = a != b;
            }

            Assert.That(diverged, Is.True,
                "two boxers of the same tier must not make identical decisions on the same tick");
        }
    }
}
