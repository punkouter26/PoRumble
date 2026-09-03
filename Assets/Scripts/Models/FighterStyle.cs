namespace PoRumble.Models
{
    /// <summary>
    /// How a fighter bends the policy's output on its way to the boxer.
    ///
    /// <see cref="PoRumbleBoxer"/> — the compiled policy — has a frozen action vector: four
    /// continuous and two discrete, and growing it stops the model loading at all. So a
    /// fighter's personality cannot be a new action or a new observation. It is instead a
    /// transform applied to the actions the one shared network already produces, plus the two
    /// side channels (charge, dodge) that exist precisely because they are not ML actions.
    ///
    /// Everything here is therefore inference-only. Training scenes never build a modulator,
    /// so a run still learns against the unmodified policy.
    /// </summary>
    public readonly struct FighterStyle
    {
        /// <summary>
        /// Forward bias added to the policy's movement, along the fighter's own facing.
        /// Positive walks the opponent down; negative fights off the back foot.
        /// </summary>
        public readonly float Pressure;

        /// <summary>Sideways bias, 0..1. A high value circles rather than standing square.</summary>
        public readonly float Circling;

        /// <summary>
        /// Fraction of the policy's punches that are allowed through, 0..1. Below 1 this is a
        /// patient fighter that passes up openings the network would have taken.
        /// </summary>
        public readonly float PunchGate;

        /// <summary>
        /// Chance per decision of throwing a punch the policy did *not* ask for, when the
        /// fighter is square to a target and inside range. This is what makes a volume
        /// puncher: more output than the network alone would produce.
        /// </summary>
        public readonly float Opportunism;

        /// <summary>
        /// Chance per second of starting a haymaker wind-up when an opening presents itself.
        /// Rides <see cref="BoxerModel.ChargeInput"/>, not an action branch.
        /// </summary>
        public readonly float ChargeChance;

        /// <summary>
        /// Chance of slipping a punch this fighter can see coming, 0..1. Rides the dodge side
        /// channel; a fighter at 0 never dodges at all.
        /// </summary>
        public readonly float DodgeChance;

        public FighterStyle(
            float pressure,
            float circling,
            float punchGate,
            float opportunism,
            float chargeChance,
            float dodgeChance)
        {
            Pressure = pressure;
            Circling = circling;
            PunchGate = punchGate;
            Opportunism = opportunism;
            ChargeChance = chargeChance;
            DodgeChance = dodgeChance;
        }

        /// <summary>The policy driven straight through, unbent. This is "Standard RL".</summary>
        public static FighterStyle Neutral => new(0f, 0f, 1f, 0f, 0f, 0f);
    }
}
