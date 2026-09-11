using NUnit.Framework;
using PoRumble.Models;
using UnityEngine;

namespace PoRumble.Tests
{
    /// <summary>
    /// Pins which cheek a punch marks.
    ///
    /// The sign convention is the whole of it: get it backwards and every fighter is drawn
    /// swollen on the side nobody has been hitting, which is exactly as wrong as no swelling
    /// at all and considerably harder to notice.
    /// </summary>
    public sealed class FaceDamageTests
    {
        private static CombatSettings Settings()
        {
            return new CombatSettings(
                headOffset: 0.9f,
                headRadius: 0.6f,
                faceArcHalfAngleDegrees: 80f,
                closeRangeThreshold: 2f,
                longPunchDamage: 1,
                closePunchDamage: 2);
        }

        /// <summary>
        /// Places an attacker on one side of a defender who is facing up the screen, and lands
        /// a glove on the defender's head centre.
        /// </summary>
        private static HitResult HitFrom(Vector2 attackerPosition)
        {
            CombatSettings settings = Settings();
            Vector2 defender = Vector2.zero;
            Vector2 facing = Vector2.up;
            Vector2 headCentre = defender + facing * settings.HeadOffset;

            return CombatMath.ResolveHit(
                attackerId: 1,
                attackerPosition: attackerPosition,
                targetId: 0,
                targetPosition: defender,
                targetFacing: facing,
                targetIsAlive: true,
                glovePosition: headCentre,
                settings: settings);
        }

        [Test]
        public void APunchFromTheDefendersRightReportsPositiveLateral()
        {
            // Facing up the screen, so the defender's own right hand is to the east.
            HitResult result = HitFrom(new Vector2(1.4f, 1.2f));

            Assert.That(result.IsHit, Is.True);
            Assert.That(result.ApproachLateral, Is.GreaterThan(0.2f));
        }

        [Test]
        public void APunchFromTheDefendersLeftReportsNegativeLateral()
        {
            HitResult result = HitFrom(new Vector2(-1.4f, 1.2f));

            Assert.That(result.IsHit, Is.True);
            Assert.That(result.ApproachLateral, Is.LessThan(-0.2f));
        }

        [Test]
        public void APunchStraightDownTheMiddleReportsNoSide()
        {
            HitResult result = HitFrom(new Vector2(0f, 2.4f));

            Assert.That(result.IsHit, Is.True);
            Assert.That(result.ApproachLateral, Is.EqualTo(0f).Within(0.02f));
        }

        /// <summary>
        /// The lateral reading is measured against the defender's facing, not against the
        /// world, so it has to follow them round as they turn. Without this a fighter who
        /// pivots mid-match would start accumulating swelling on the wrong cheek.
        /// </summary>
        [Test]
        public void TheSideIsMeasuredAgainstTheDefendersFacingRatherThanTheWorld()
        {
            CombatSettings settings = Settings();

            // Defender facing east; the attacker is to the south, which is the defender's
            // right-hand side once they have turned.
            Vector2 facing = Vector2.right;
            Vector2 headCentre = facing * settings.HeadOffset;

            HitResult result = CombatMath.ResolveHit(
                attackerId: 1,
                attackerPosition: new Vector2(1.6f, -1.2f),
                targetId: 0,
                targetPosition: Vector2.zero,
                targetFacing: facing,
                targetIsAlive: true,
                glovePosition: headCentre,
                settings: settings);

            Assert.That(result.IsHit, Is.True);
            Assert.That(result.ApproachLateral, Is.GreaterThan(0.2f));
        }

        [Test]
        public void ResettingABoxerClearsEveryMark()
        {
            BoxerModel boxer = new(0, 30)
            {
                SwellLeft = 0.8f,
                SwellRight = 0.4f,
                Cut = 0.6f
            };

            boxer.ResetTo(Vector2.zero, Vector2.up, 30);

            Assert.That(boxer.SwellLeft, Is.EqualTo(0f));
            Assert.That(boxer.SwellRight, Is.EqualTo(0f));
            Assert.That(boxer.Cut, Is.EqualTo(0f));
        }

        [Test]
        public void SwellReportsTheWorseOfTheTwoSides()
        {
            BoxerModel boxer = new(0, 30) { SwellLeft = 0.2f, SwellRight = 0.7f };

            Assert.That(boxer.Swell, Is.EqualTo(0.7f).Within(0.001f));
        }
    }
}
