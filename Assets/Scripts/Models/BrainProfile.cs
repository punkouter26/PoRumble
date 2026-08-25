using UnityEngine;

namespace PoRumble.Models
{
    /// <summary>
    /// A difficulty tier for the hand-written sparring brain.
    ///
    /// The brain's behaviour used to be a block of private constants, which meant every
    /// scripted opponent in every match fought identically. Lifting them into an asset lets a
    /// single roster field a spread of tiers, so the ring contains a mix of pressure fighters,
    /// nervous journeymen and patient counter-punchers rather than ten copies of one bot.
    ///
    /// Pure tuning data — the brain reads it and stays deterministic.
    /// </summary>
    [CreateAssetMenu(menuName = "PoRumble/Brain Profile", fileName = "BrainProfile")]
    public sealed class BrainProfile : ScriptableObject
    {
        [Tooltip("Shown on the HUD and in the roster inspector.")]
        [SerializeField] private string _displayName = "Journeyman";

        [Header("Engagement")]
        [Tooltip("How readily the bot presses forward and throws. At 0 it fights at arm's " +
                 "length and rarely commits; at 1 it walks through punches to land its own.")]
        [Range(0f, 1f)]
        [SerializeField] private float _aggression = 0.5f;

        [Tooltip("Range the bot tries to hold, as a fraction of its punching reach.")]
        [SerializeField] private float _engageRangeScale = 0.95f;
        [Tooltip("Beyond this fraction of reach the bot closes the distance down.")]
        [SerializeField] private float _breakRangeScale = 1.35f;

        [Header("Competence")]
        [Tooltip("Seconds before the bot reacts to a change in the fight. A weak opponent " +
                 "keeps swinging at where you were, which is what makes it beatable.")]
        [Range(0f, 1f)]
        [SerializeField] private float _reactionDelay = 0.18f;

        [Tooltip("1 aims true; lower values wander the aim, so punches drift off the face arc.")]
        [Range(0f, 1f)]
        [SerializeField] private float _accuracy = 0.75f;

        [Tooltip("How square to the target the bot insists on being before committing. " +
                 "Derived from accuracy when left at 0.")]
        [Range(0f, 1f)]
        [SerializeField] private float _punchAlignment;

        [Header("Stamina discipline")]
        [Tooltip("Below this the bot backs off to breathe.")]
        [Range(0f, 1f)]
        [SerializeField] private float _recoverStamina = 0.25f;
        [Tooltip("It resumes trading once breath comes back above this. The gap between the " +
                 "two is hysteresis, so the bot commits rather than flickering in and out.")]
        [Range(0f, 1f)]
        [SerializeField] private float _resumeStamina = 0.55f;

        [Header("Haymaker")]
        [Tooltip("Chance per opening that the bot winds up a haymaker instead of jabbing. " +
                 "0 leaves the tier throwing ordinary punches only.")]
        [Range(0f, 1f)]
        [SerializeField] private float _chargeChance;

        [Tooltip("How eagerly the bot cashes in a counter window after blocking. At 1 it " +
                 "always fires back immediately, which is the mark of a good fighter.")]
        [Range(0f, 1f)]
        [SerializeField] private float _counterDiscipline = 0.5f;

        public string DisplayName => _displayName;
        public float Aggression => _aggression;
        public float EngageRangeScale => _engageRangeScale;
        public float BreakRangeScale => _breakRangeScale;
        public float ReactionDelay => _reactionDelay;
        public float Accuracy => _accuracy;
        public float RecoverStamina => _recoverStamina;
        public float ResumeStamina => _resumeStamina;
        public float ChargeChance => _chargeChance;
        public float CounterDiscipline => _counterDiscipline;

        /// <summary>
        /// How aligned to the target the bot must be before it throws. Falls out of accuracy
        /// unless a tier overrides it: a sloppy bot swings at bad angles, which is exactly
        /// what makes its punches miss the face arc.
        /// </summary>
        public float PunchAlignment => _punchAlignment > 0f
            ? _punchAlignment
            : Mathf.Lerp(0.55f, 0.95f, _accuracy);

        /// <summary>Plain value copy, so the brain never holds a ScriptableObject reference.</summary>
        public BrainSettings ToSettings()
        {
            return new BrainSettings(
                _aggression,
                _engageRangeScale,
                _breakRangeScale,
                _reactionDelay,
                _accuracy,
                PunchAlignment,
                _recoverStamina,
                _resumeStamina,
                _chargeChance,
                _counterDiscipline);
        }
    }

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
            float counterDiscipline)
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
        }

        /// <summary>The tuning the brain used before profiles existed, kept as the fallback.</summary>
        public static BrainSettings Default => new(
            0.5f, 0.95f, 1.35f, 0f, 1f, 0.9f, 0.25f, 0.55f, 0f, 0.5f);
    }
}
