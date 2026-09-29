using System.Collections.Generic;
using UnityEngine;

namespace PoRumble.Models
{
    /// <summary>
    /// Reads incoming danger off the roster.
    ///
    /// Pure and static for the same reason <see cref="CombatMath"/> is: the scripted brain,
    /// the style modulator and the tests all need the same answer to "am I about to be hit",
    /// and none of them should have to agree on it separately. No physics query, no Unity
    /// components, no allocation.
    /// </summary>
    public static class ThreatMath
    {
        /// <summary>
        /// How square an attacker must be to a boxer for the punch to be worth slipping.
        /// Wider than the face arc: a punch thrown slightly off-line still arrives, and a
        /// fighter that only dodged perfectly-aimed punches would look asleep.
        /// </summary>
        private const float THREAT_CONE_COSINE = 0.72f;

        /// <summary>
        /// True when some living opponent is committed to a punch that is going to reach this
        /// boxer: a fist already on its way out, or a haymaker cocked and pointed this way.
        ///
        /// The haymaker counts precisely because it has not been thrown yet. Its wind-up is
        /// the telegraph the whole mechanic is built around, and a slip is what that telegraph
        /// is supposed to buy the defender.
        /// </summary>
        public static bool IsPunchIncoming(
            IReadOnlyList<BoxerModel> boxers,
            BoxerModel self,
            float threatRange,
            float minChargeToRelease)
        {
            if (boxers == null || self == null || !self.IsAlive.Value)
            {
                return false;
            }

            float rangeSqr = threatRange * threatRange;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel other = boxers[boxerIndex];

                if (other == null || other.Id == self.Id || !other.IsAlive.Value)
                {
                    continue;
                }

                bool swinging = other.LeftArm.Phase == ArmPhase.Extending
                                || other.RightArm.Phase == ArmPhase.Extending;
                bool cocked = other.ChargeInput && other.Charge.Value >= minChargeToRelease;

                if (!swinging && !cocked)
                {
                    continue;
                }

                Vector2 toSelf = self.Position - other.Position;

                if (toSelf.sqrMagnitude > rangeSqr || toSelf.sqrMagnitude <= Mathf.Epsilon)
                {
                    continue;
                }

                if (Vector2.Dot(other.Facing.normalized, toSelf.normalized) >= THREAT_CONE_COSINE)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
