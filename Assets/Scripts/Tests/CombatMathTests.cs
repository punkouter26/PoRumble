using NUnit.Framework;
using PoRumble.Models;
using UnityEngine;

namespace PoRumble.Tests
{
    /// <summary>Covers acceptance criteria 1, 2, 3, 4, 6 and 10.</summary>
    public sealed class CombatMathTests
    {
        private const float HEAD_OFFSET = 0.5f;
        private const float HEAD_RADIUS = 0.25f;
        private const float FACE_ARC_HALF_ANGLE = 60f;
        private const float CLOSE_RANGE = 1.2f;
        private const int LONG_DAMAGE = 1;
        private const int CLOSE_DAMAGE = 2;

        private static CombatSettings Settings => new(
            HEAD_OFFSET, HEAD_RADIUS, FACE_ARC_HALF_ANGLE, CLOSE_RANGE, LONG_DAMAGE, CLOSE_DAMAGE);

        /// <summary>Target sits at the origin facing +Y, so its head is at (0, 0.5).</summary>
        private static HitResult Resolve(Vector2 attackerPosition, Vector2 glovePosition,
            int attackerId = 1, int targetId = 2, bool targetAlive = true)
        {
            return CombatMath.ResolveHit(
                attackerId, attackerPosition,
                targetId, Vector2.zero, Vector2.up, targetAlive,
                glovePosition, Settings);
        }

        [Test]
        public void GloveInFaceArc_DealsDamage()
        {
            // Glove directly in front of the face, at long range.
            HitResult result = Resolve(new Vector2(0f, 3f), new Vector2(0f, 0.7f));

            Assert.That(result.IsHit, Is.True);
            Assert.That(result.Damage, Is.GreaterThan(0));
        }

        [Test]
        public void GloveBehindHead_DealsNoDamage()
        {
            // Directly behind: 180 degrees from facing.
            HitResult result = Resolve(new Vector2(0f, -3f), new Vector2(0f, 0.3f));

            Assert.That(result.IsHit, Is.False);
            Assert.That(result.Damage, Is.Zero);
        }

        [Test]
        public void GloveOnSideOfHead_DealsNoDamage()
        {
            // 90 degrees off the facing vector — outside the 60 degree half-arc.
            HitResult result = Resolve(new Vector2(3f, 0f), new Vector2(0.2f, 0.5f));

            Assert.That(result.IsHit, Is.False);
        }

        [Test]
        public void GloveMissingHeadEntirely_DealsNoDamage()
        {
            // In front, but far outside the head radius — a body-level whiff.
            HitResult result = Resolve(new Vector2(0f, 3f), new Vector2(0f, 2.0f));

            Assert.That(result.IsHit, Is.False);
        }

        [Test]
        public void LongRangeFaceHit_DealsOneDamage()
        {
            HitResult result = Resolve(new Vector2(0f, 3f), new Vector2(0f, 0.7f));

            Assert.That(result.IsHit, Is.True);
            Assert.That(result.IsCloseRange, Is.False);
            Assert.That(result.Damage, Is.EqualTo(LONG_DAMAGE));
        }

        [Test]
        public void CloseRangeFaceHit_DealsTwoDamage()
        {
            // Attacker inside CLOSE_RANGE of the target body.
            HitResult result = Resolve(new Vector2(0f, 1.0f), new Vector2(0f, 0.6f));

            Assert.That(result.IsHit, Is.True);
            Assert.That(result.IsCloseRange, Is.True);
            Assert.That(result.Damage, Is.EqualTo(CLOSE_DAMAGE));
        }

        [Test]
        public void SelfPunch_NeverDealsDamage()
        {
            HitResult result = Resolve(new Vector2(0f, 3f), new Vector2(0f, 0.5f),
                attackerId: 7, targetId: 7);

            Assert.That(result.IsHit, Is.False);
        }

        [Test]
        public void EliminatedTarget_TakesNoDamage()
        {
            HitResult result = Resolve(new Vector2(0f, 3f), new Vector2(0f, 0.5f),
                targetAlive: false);

            Assert.That(result.IsHit, Is.False);
        }

        [Test]
        public void ArcBoundary_AttackerJustInsideHits_JustOutsideMisses()
        {
            // Sweep the attacker around the target, keeping the glove on the head centre.
            Vector2 headCenter = new(0f, HEAD_OFFSET);

            Vector2 insideAttacker = headCenter + Rotate(Vector2.up, FACE_ARC_HALF_ANGLE - 2f) * 3f;
            Vector2 outsideAttacker = headCenter + Rotate(Vector2.up, FACE_ARC_HALF_ANGLE + 2f) * 3f;

            Assert.That(Resolve(insideAttacker, headCenter).IsHit, Is.True,
                "an attacker just inside the face arc should land");
            Assert.That(Resolve(outsideAttacker, headCenter).IsHit, Is.False,
                "an attacker just outside the face arc should whiff");
        }

        [Test]
        public void GloveDeadCentreFromBehind_StillMisses()
        {
            // Regression: a glove landing exactly on the head centre yields a zero-length offset
            // vector. Judging the arc from that vector let punches from behind score as face hits.
            Vector2 headCenter = new(0f, HEAD_OFFSET);
            HitResult result = Resolve(new Vector2(0f, -3f), headCenter);

            Assert.That(result.IsHit, Is.False);
            Assert.That(result.Damage, Is.Zero);
        }

        [Test]
        public void AttackerStandingOnTheHead_Misses()
        {
            // Degenerate case: no meaningful approach direction.
            Vector2 headCenter = new(0f, HEAD_OFFSET);

            Assert.That(Resolve(headCenter, headCenter).IsHit, Is.False);
        }

        private static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            return new Vector2(value.x * cos - value.y * sin, value.x * sin + value.y * cos);
        }
    }
}
