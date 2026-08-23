using NUnit.Framework;
using PoRumble.Models;

namespace PoRumble.Tests
{
    /// <summary>Covers acceptance criterion 5 — independent arm extension and cooldown.</summary>
    public sealed class ArmModelTests
    {
        private const float EXTEND = 0.12f;
        private const float RETRACT = 0.18f;
        private const float COOLDOWN = 0.15f;

        private static void Tick(ArmModel arm, float seconds)
        {
            arm.Tick(seconds, EXTEND, RETRACT, COOLDOWN);
        }

        [Test]
        public void Punch_ReachesPeakExactlyOnce()
        {
            ArmModel arm = new(ArmSide.Left);
            arm.TryPunch();

            int peakCount = 0;

            for (int tickIndex = 0; tickIndex < 40; tickIndex++)
            {
                Tick(arm, 0.02f);

                if (arm.ReachedPeakThisTick)
                {
                    peakCount++;
                }
            }

            Assert.That(peakCount, Is.EqualTo(1));
        }

        [Test]
        public void ArmCannotPunchAgainUntilCooldownCompletes()
        {
            ArmModel arm = new(ArmSide.Right);
            arm.TryPunch();

            Tick(arm, EXTEND);
            Assert.That(arm.CanPunch, Is.False, "still retracting");

            Tick(arm, RETRACT);
            Assert.That(arm.CanPunch, Is.False, "still cooling down");

            Tick(arm, COOLDOWN);
            Assert.That(arm.CanPunch, Is.True, "cooldown finished");
        }

        [Test]
        public void BothArmsExtendIndependently()
        {
            BoxerModel boxer = new(0, 30);

            boxer.LeftArm.TryPunch();
            boxer.LeftArm.Tick(0.06f, EXTEND, RETRACT, COOLDOWN);

            // Right arm starts while the left is still mid-extension.
            boxer.RightArm.TryPunch();

            Assert.That(boxer.LeftArm.Phase, Is.EqualTo(ArmPhase.Extending));
            Assert.That(boxer.RightArm.Phase, Is.EqualTo(ArmPhase.Extending));
            Assert.That(boxer.LeftArm.Extension, Is.GreaterThan(boxer.RightArm.Extension),
                "the left arm started earlier so it is further extended");
        }

        [Test]
        public void EliminationForcesBothArmsToRest()
        {
            BoxerModel boxer = new(0, 30);
            boxer.LeftArm.TryPunch();
            boxer.RightArm.TryPunch();

            boxer.Eliminate();

            Assert.That(boxer.LeftArm.Phase, Is.EqualTo(ArmPhase.Idle));
            Assert.That(boxer.RightArm.Phase, Is.EqualTo(ArmPhase.Idle));
            Assert.That(boxer.LeftArm.Extension, Is.Zero);
            Assert.That(boxer.RightArm.Extension, Is.Zero);
        }
    }
}
