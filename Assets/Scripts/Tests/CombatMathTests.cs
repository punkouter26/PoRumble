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
        public void GloveBehindHead_DealsDamage()
        {
            // Directly behind: 180 degrees from facing.
            //
            // The back of the head is the weak spot, not a sanctuary. Rejecting a rear punch
            // outright made turning your back a perfect defence, and measured in a live 1v1 it
            // was exactly that - 1988 steps, both fighters on full health, the scripted brain
            // aimed dead on and throwing into a back that could not be hurt.
            HitResult result = Resolve(new Vector2(0f, -3f), new Vector2(0f, 0.3f));

            Assert.That(result.IsHit, Is.True);
            Assert.That(result.Damage, Is.GreaterThan(0));
        }

        [Test]
        public void GloveOnSideOfHead_DealsDamage()
        {
            // 90 degrees off the facing vector, so outside the guard's arc and unblockable -
            // but a punch that reaches the head still lands, whatever angle it came from.
            HitResult result = Resolve(new Vector2(3f, 0f), new Vector2(0.2f, 0.5f));

            Assert.That(result.IsHit, Is.True);
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
        public void ArcBoundary_DecidesWhatTheGuardCovers_NotWhatMayBeHit()
        {
            // Sweep the attacker around the target, keeping the glove on the head centre.
            //
            // The arc survived the change but changed job: it no longer says whether a punch
            // may land, it says whether the hands are between it and the face. Both of these
            // land; only the first one can be blocked.
            Vector2 headCenter = new(0f, HEAD_OFFSET);

            Vector2 insideAttacker = headCenter + Rotate(Vector2.up, FACE_ARC_HALF_ANGLE - 2f) * 3f;
            Vector2 outsideAttacker = headCenter + Rotate(Vector2.up, FACE_ARC_HALF_ANGLE + 2f) * 3f;

            Assert.That(Resolve(insideAttacker, headCenter).IsHit, Is.True,
                "a punch from inside the guard's arc still lands");
            Assert.That(Resolve(outsideAttacker, headCenter).IsHit, Is.True,
                "and so does one from outside it");

            Assert.That(
                CombatMath.IsInFaceArc(insideAttacker, Vector2.zero, Vector2.up, Settings),
                Is.True, "an attacker inside the arc meets the guard");
            Assert.That(
                CombatMath.IsInFaceArc(outsideAttacker, Vector2.zero, Vector2.up, Settings),
                Is.False, "an attacker outside it does not, so the punch is unblockable");
        }

        [Test]
        public void GloveDeadCentreFromBehind_IsNotMistakenForAFrontalPunch()
        {
            // Regression: a glove landing exactly on the head centre yields a zero-length
            // offset vector carrying no direction. Judging the arc from that vector instead of
            // from the attacker's position would read a punch thrown from directly behind as a
            // frontal one - which now matters for the opposite reason it used to. It would no
            // longer wrongly score a hit; it would wrongly let hands nowhere near the punch
            // block it.
            Vector2 headCenter = new(0f, HEAD_OFFSET);
            Vector2 behind = new(0f, -3f);

            Assert.That(Resolve(behind, headCenter).IsHit, Is.True, "a rear punch lands");
            Assert.That(
                CombatMath.IsInFaceArc(behind, Vector2.zero, Vector2.up, Settings),
                Is.False, "and cannot be blocked");
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
