namespace PoRumble.Models
{
    public readonly struct HitResult
    {
        public readonly bool IsHit;
        public readonly int Damage;
        public readonly bool IsCloseRange;

        /// <summary>
        /// Which side of the defender's face the attacker was standing on: -1 hard to the
        /// defender's left, +1 hard to their right, 0 straight down the middle.
        ///
        /// Computed here because the face-arc test already has both the facing and the
        /// approach vector in hand; working it out again anywhere else would mean repeating
        /// the one piece of geometry that decided whether this was a hit at all.
        /// </summary>
        public readonly float ApproachLateral;

        public HitResult(bool isHit, int damage, bool isCloseRange)
            : this(isHit, damage, isCloseRange, 0f)
        {
        }

        public HitResult(bool isHit, int damage, bool isCloseRange, float approachLateral)
        {
            IsHit = isHit;
            Damage = damage;
            IsCloseRange = isCloseRange;
            ApproachLateral = approachLateral;
        }

        internal static HitResult Miss => new(false, 0, false);
    }
}
