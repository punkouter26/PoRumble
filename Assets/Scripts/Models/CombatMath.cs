using UnityEngine;

namespace PoRumble.Models
{
    public readonly struct HitResult
    {
        public readonly bool IsHit;
        public readonly int Damage;
        public readonly bool IsCloseRange;

        public HitResult(bool isHit, int damage, bool isCloseRange)
        {
            IsHit = isHit;
            Damage = damage;
            IsCloseRange = isCloseRange;
        }

        public static HitResult Miss => new(false, 0, false);
    }

    /// <summary>
    /// Pure, allocation-free punch resolution. Deliberately free of Unity components and
    /// physics queries so the face-arc rules are unit-testable without a scene.
    /// </summary>
    public static class CombatMath
    {
        /// <summary>World-space centre of a boxer's head, offset forward along its facing.</summary>
        private static Vector2 GetHeadCenter(Vector2 bodyPosition, Vector2 facing, float headOffset)
        {
            return bodyPosition + facing.normalized * headOffset;
        }

        /// <summary>
        /// Resolves one glove against one target.
        /// A hit requires: a different boxer, a living target that is not mid-slip, the glove
        /// touching the head, and the contact falling inside the target's forward face arc.
        /// </summary>
        public static HitResult ResolveHit(
            int attackerId,
            Vector2 attackerPosition,
            int targetId,
            Vector2 targetPosition,
            Vector2 targetFacing,
            bool targetIsAlive,
            Vector2 glovePosition,
            in CombatSettings settings)
        {
            return ResolveHit(
                attackerId,
                attackerPosition,
                targetId,
                targetPosition,
                targetFacing,
                targetIsAlive,
                false,
                glovePosition,
                settings);
        }

        /// <summary>
        /// Resolves one glove against one target that may be slipping the punch.
        ///
        /// The slip is checked here rather than in the caller so that a dodged punch falls
        /// through the same path as any other miss - which is what makes it report as an
        /// evade, and so pay the defender the evade reward it has always paid.
        /// </summary>
        public static HitResult ResolveHit(
            int attackerId,
            Vector2 attackerPosition,
            int targetId,
            Vector2 targetPosition,
            Vector2 targetFacing,
            bool targetIsAlive,
            bool targetIsDodging,
            Vector2 glovePosition,
            in CombatSettings settings)
        {
            // A boxer can never punch itself.
            if (attackerId == targetId)
            {
                return HitResult.Miss;
            }

            // Already-eliminated boxers absorb nothing.
            if (!targetIsAlive)
            {
                return HitResult.Miss;
            }

            // Mid-slip: the head is not where it looks. Deliberately unconditional rather
            // than directional - a slip beats a punch from any angle, which is what pays for
            // its stamina and its cooldown.
            if (targetIsDodging)
            {
                return HitResult.Miss;
            }

            Vector2 facing = targetFacing.normalized;
            Vector2 headCenter = GetHeadCenter(targetPosition, facing, settings.HeadOffset);
            Vector2 headToGlove = glovePosition - headCenter;

            // The glove must actually be touching the head.
            if (headToGlove.sqrMagnitude > settings.HeadRadius * settings.HeadRadius)
            {
                return HitResult.Miss;
            }

            // The attacker must be standing inside the target's forward face arc.
            //
            // This is measured from the attacker's position rather than the glove's offset from
            // the head centre: a glove that lands dead-centre on the head produces a zero-length
            // offset vector, which carries no direction at all. Using it would let a punch thrown
            // from directly behind register as a clean face hit.
            Vector2 headToAttacker = attackerPosition - headCenter;

            if (headToAttacker.sqrMagnitude <= Mathf.Epsilon)
            {
                return HitResult.Miss;
            }

            float approachAngle = Vector2.Angle(facing, headToAttacker.normalized);

            if (approachAngle > settings.FaceArcHalfAngleDegrees)
            {
                return HitResult.Miss;
            }

            // Range is measured body-to-body, mirroring the original's long/close scoring.
            float range = Vector2.Distance(attackerPosition, targetPosition);
            bool isCloseRange = range <= settings.CloseRangeThreshold;
            int damage = isCloseRange ? settings.ClosePunchDamage : settings.LongPunchDamage;

            return new HitResult(true, damage, isCloseRange);
        }


        /// <summary>
        /// Shortest distance from a point to the line segment ab.
        ///
        /// Lives here rather than in BoxerSystem because it is the whole of what makes an arm
        /// block work, and blocking is combat resolution: it has to be testable without a scene
        /// for the same reason the face arc is.
        /// </summary>
        public static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSqr = ab.sqrMagnitude;

            // A retracted arm is a zero-length segment sitting at the shoulder. Projecting onto it
            // would divide by zero, and the honest answer is simply the distance to that point.
            if (lengthSqr < 1e-6f)
            {
                return (point - a).magnitude;
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSqr);
            return (point - (a + ab * t)).magnitude;
        }

        /// <summary>
        /// Whether an incoming glove is stopped by a defender's arm.
        ///
        /// The arm is treated as the whole limb from shoulder to glove, not just the fist on the
        /// end of it. Testing the glove alone - which is what this replaced - meant a punch could
        /// pass straight through a forearm held across the body and land clean on the face behind
        /// it, which is the one thing a guard is for.
        ///
        /// The defender's arm is a straight segment in the model even though it is drawn with a
        /// bent elbow: BoxerSystem.GetGlovePosition places the glove along `facing` at a lateral
        /// offset, and the elbow exists only in ArmView's servo. Blocking therefore has to be
        /// judged against the straight line the model believes in, or the maths and the picture
        /// would disagree about where the arm is.
        /// </summary>
        public static bool ArmBlocks(Vector2 incomingGlove, Vector2 shoulder, Vector2 glove, float blockRadius)
        {
            return DistanceToSegment(incomingGlove, shoulder, glove) <= blockRadius;
        }
    }

    /// <summary>Plain value copy of the tuning data, so CombatMath stays free of ScriptableObject.</summary>
    public readonly struct CombatSettings
    {
        public readonly float HeadOffset;
        public readonly float HeadRadius;
        public readonly float FaceArcHalfAngleDegrees;
        public readonly float CloseRangeThreshold;
        public readonly int LongPunchDamage;
        public readonly int ClosePunchDamage;

        public CombatSettings(
            float headOffset,
            float headRadius,
            float faceArcHalfAngleDegrees,
            float closeRangeThreshold,
            int longPunchDamage,
            int closePunchDamage)
        {
            HeadOffset = headOffset;
            HeadRadius = headRadius;
            FaceArcHalfAngleDegrees = faceArcHalfAngleDegrees;
            CloseRangeThreshold = closeRangeThreshold;
            LongPunchDamage = longPunchDamage;
            ClosePunchDamage = closePunchDamage;
        }
    }
}
