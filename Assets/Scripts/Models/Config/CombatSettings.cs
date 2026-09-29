namespace PoRumble.Models
{
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
