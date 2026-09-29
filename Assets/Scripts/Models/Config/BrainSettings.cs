namespace PoRumble.Models
{
    /// <summary>
    /// Value-type view of a <see cref="BrainProfile"/>. Keeps <see cref="BrainSettings"/>
    /// consumers testable without creating assets, and matches how CombatSettings already
    /// decouples CombatMath from the config asset.
    /// </summary>
    public readonly struct BrainSettings
    {
        public readonly float Aggression;
        public readonly float EngageRangeScale;
        public readonly float BreakRangeScale;
        public readonly float ReactionDelay;
        public readonly float Accuracy;
        public readonly float PunchAlignment;
        public readonly float RecoverStamina;
        public readonly float ResumeStamina;
        public readonly float ChargeChance;
        public readonly float CounterDiscipline;
        public readonly float DodgeDiscipline;

        public BrainSettings(
            float aggression,
            float engageRangeScale,
            float breakRangeScale,
            float reactionDelay,
            float accuracy,
            float punchAlignment,
            float recoverStamina,
            float resumeStamina,
            float chargeChance,
            float counterDiscipline,
            float dodgeDiscipline = 0f)
        {
            Aggression = aggression;
            EngageRangeScale = engageRangeScale;
            BreakRangeScale = breakRangeScale;
            ReactionDelay = reactionDelay;
            Accuracy = accuracy;
            PunchAlignment = punchAlignment;
            RecoverStamina = recoverStamina;
            ResumeStamina = resumeStamina;
            ChargeChance = chargeChance;
            CounterDiscipline = counterDiscipline;
            DodgeDiscipline = dodgeDiscipline;
        }

        /// <summary>The tuning the brain used before profiles existed, kept as the fallback.</summary>
        public static BrainSettings Default => new(
            0.5f, 0.95f, 1.35f, 0f, 1f, 0.9f, 0.25f, 0.55f, 0f, 0.5f);
    }
}
