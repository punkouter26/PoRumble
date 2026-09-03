using NUnit.Framework;
using PoRumble.Models;
using UnityEngine;

namespace PoRumble.Tests
{
    /// <summary>
    /// A guard is the whole arm, not the fist on the end of it.
    ///
    /// Blocking used to be a proximity test against the defender's glove alone, so a punch
    /// could pass through a forearm held across the body and land clean on the face behind it.
    /// The arms carried no colliders either, so nothing in maths or physics gave a limb any
    /// presence at all.
    /// </summary>
    public sealed class ArmBlockTests
    {
        private const float BLOCK_RADIUS = 0.6f;

        [Test]
        public void APunchIntoTheFistIsStillBlocked()
        {
            Vector2 shoulder = new(0f, 0f);
            Vector2 glove = new(1.6f, 0f);

            Assert.That(
                CombatMath.ArmBlocks(new Vector2(1.6f, 0.2f), shoulder, glove, BLOCK_RADIUS),
                Is.True,
                "The original glove-only block must keep working.");
        }

        [Test]
        public void APunchIntoTheForearmIsBlocked()
        {
            Vector2 shoulder = new(0f, 0f);
            Vector2 glove = new(1.6f, 0f);

            // Halfway along the limb - nowhere near the fist, and the case that used to pass
            // straight through.
            Assert.That(
                CombatMath.ArmBlocks(new Vector2(0.8f, 0.1f), shoulder, glove, BLOCK_RADIUS),
                Is.True);
        }

        [Test]
        public void APunchPastTheArmIsNotBlocked()
        {
            Vector2 shoulder = new(0f, 0f);
            Vector2 glove = new(1.6f, 0f);

            Assert.That(
                CombatMath.ArmBlocks(new Vector2(0.8f, 1.4f), shoulder, glove, BLOCK_RADIUS),
                Is.False,
                "Clearing the guard has to remain possible, or blocking becomes invulnerability.");
        }

        [Test]
        public void APunchBeyondTheReachOfAnExtendedArmIsNotBlocked()
        {
            Vector2 shoulder = new(0f, 0f);
            Vector2 glove = new(1.6f, 0f);

            // The segment ends at the glove: distance is measured to the endpoint, never to
            // the infinite line the arm happens to lie on.
            Assert.That(
                CombatMath.ArmBlocks(new Vector2(3f, 0f), shoulder, glove, BLOCK_RADIUS),
                Is.False);
        }

        [Test]
        public void ARetractedArmGuardsOnlyTheShoulder()
        {
            Vector2 shoulder = new(0f, 0f);

            // Extension 0 collapses the segment to a point. A tucked arm must not block the
            // whole reach it would have had if it were thrown.
            Assert.That(
                CombatMath.ArmBlocks(new Vector2(0.1f, 0f), shoulder, shoulder, BLOCK_RADIUS),
                Is.True);
            Assert.That(
                CombatMath.ArmBlocks(new Vector2(1.5f, 0f), shoulder, shoulder, BLOCK_RADIUS),
                Is.False);
        }

        [Test]
        public void TheArmBlocksAlongItsWholeLengthRatherThanAtOnePoint()
        {
            Vector2 shoulder = new(0f, 0f);
            Vector2 glove = new(1.6f, 0f);
            const int samples = 17;
            int blocked = 0;

            // Stepped by an integer index rather than by accumulating 0.1f. Accumulation puts
            // the final sample at 1.6000001, just past the glove, and silently drops it - the
            // loop then tests sixteen points and quietly passes a weaker assertion.
            for (int step = 0; step < samples; step++)
            {
                float along = 1.6f * step / (samples - 1);

                if (CombatMath.ArmBlocks(new Vector2(along, 0f), shoulder, glove, BLOCK_RADIUS))
                {
                    blocked++;
                }
            }

            Assert.That(blocked, Is.EqualTo(samples), "Every sample on the limb should stop a punch.");
        }

        [Test]
        public void DistanceToSegmentClampsToTheEndpoints()
        {
            Vector2 a = new(0f, 0f);
            Vector2 b = new(2f, 0f);

            Assert.That(CombatMath.DistanceToSegment(new Vector2(-1f, 0f), a, b), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(CombatMath.DistanceToSegment(new Vector2(3f, 0f), a, b), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(CombatMath.DistanceToSegment(new Vector2(1f, 0.5f), a, b), Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void DistanceToADegenerateSegmentIsDistanceToThePoint()
        {
            Vector2 a = new(1f, 1f);

            Assert.That(
                CombatMath.DistanceToSegment(new Vector2(1f, 3f), a, a),
                Is.EqualTo(2f).Within(1e-4f),
                "A zero-length arm must not divide by zero.");
        }
    }
}
