namespace PoRumble.Models
{
    /// <summary>
    /// Physical differences between two fighters running the same policy.
    ///
    /// The trained policy is one network shared by every learning agent, so on its own it
    /// produces ten boxers who fight identically. These multipliers are applied by the
    /// systems rather than by the network, which is what lets a heavy-handed slugger and a
    /// quick counter-puncher come out of the same weights.
    ///
    /// A value copy rather than the ScriptableObject, so Models depends on nothing and the
    /// combat systems can be tested without an asset.
    /// </summary>
    public readonly struct FighterAttributes
    {
        /// <summary>Damage multiplier on punches this fighter lands.</summary>
        public readonly float Power;

        /// <summary>
        /// Damage multiplier on punches this fighter takes. Below 1 is a granite chin; above
        /// 1 is glass. Named for the thing it describes rather than for the direction of the
        /// number, because "toughness 1.3" reading as *more* damage taken is a trap.
        /// </summary>
        public readonly float Chin;

        /// <summary>Movement speed multiplier.</summary>
        public readonly float Speed;

        /// <summary>Stamina recovery multiplier — how quickly the fighter gets its breath back.</summary>
        public readonly float Recovery;

        public FighterAttributes(float power, float chin, float speed, float recovery)
        {
            Power = power;
            Chin = chin;
            Speed = speed;
            Recovery = recovery;
        }

        /// <summary>Everything at 1: the shipped tuning, unmodified.</summary>
        public static FighterAttributes Neutral => new(1f, 1f, 1f, 1f);
    }
}
