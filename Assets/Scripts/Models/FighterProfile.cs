using UnityEngine;

namespace PoRumble.Models
{
    /// <summary>What drives a fighter.</summary>
    public enum FighterControl
    {
        /// <summary>The hand-written sparring brain, tuned by a <see cref="BrainProfile"/>.</summary>
        Scripted = 0,

        /// <summary>The trained policy, bent by this profile's <see cref="FighterStyle"/>.</summary>
        Policy = 1
    }

    /// <summary>
    /// One selectable contestant: a name, a face, a way of fighting and a rating identity.
    ///
    /// Distinct from <see cref="BrainProfile"/>, which tunes only the scripted brain. A
    /// fighter profile sits a level up and answers "who is in the ring": it can put a policy
    /// fighter and a scripted one on the same roster screen, and it is the thing the Elo
    /// table rates. Several boxers in one match may share a profile — the ring seats ten and
    /// the roster is shorter than that — so the ratings deliberately ignore pairs of the same
    /// contestant.
    /// </summary>
    [CreateAssetMenu(menuName = "PoRumble/Fighter Profile", fileName = "Fighter")]
    public sealed class FighterProfile : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable key for the saved ratings. Renaming the asset is safe; changing this " +
                 "orphans the fighter's record and starts it back at the default rating.")]
        [SerializeField] private string _id = "fighter";

        [SerializeField] private string _displayName = "FIGHTER";

        [Tooltip("One line describing how this fighter fights, shown on the roster card.")]
        [SerializeField] private string _tagline = "";

        [Tooltip("Drawn on the head in place of the generic one. Leave empty for a plain boxer.")]
        [SerializeField] private Sprite _face;

        [Tooltip("Trunk and body colour. Ignored for the parts a face covers.")]
        [SerializeField] private Color _tint = Color.white;

        [Header("Control")]
        [SerializeField] private FighterControl _control = FighterControl.Policy;

        [Tooltip("Tier for the scripted brain. Only read when Control is Scripted.")]
        [SerializeField] private BrainProfile _brainProfile;

        [Header("Style — how this fighter bends the policy")]
        [Tooltip("Forward bias on movement. Positive walks the opponent down, negative " +
                 "fights off the back foot.")]
        [Range(-1f, 1f)]
        [SerializeField] private float _pressure;

        [Tooltip("Sideways drift. High values circle rather than stand square.")]
        [Range(0f, 1f)]
        [SerializeField] private float _circling;

        [Tooltip("Fraction of the policy's punches let through. Below 1 is a patient fighter " +
                 "that passes up openings the network would have taken.")]
        [Range(0f, 1f)]
        [SerializeField] private float _punchGate = 1f;

        [Tooltip("Chance per decision of throwing a punch the policy did not ask for, when " +
                 "square to a target and in range. This is what makes a volume puncher.")]
        [Range(0f, 1f)]
        [SerializeField] private float _opportunism;

        [Tooltip("Chance per second of winding up a haymaker when an opening presents itself.")]
        [Range(0f, 1f)]
        [SerializeField] private float _chargeChance;

        [Tooltip("Chance of slipping a punch this fighter can see coming.")]
        [Range(0f, 1f)]
        [SerializeField] private float _dodgeChance;

        [Header("Attributes")]
        [Tooltip("Damage multiplier on punches landed.")]
        [Range(0.5f, 2f)]
        [SerializeField] private float _power = 1f;

        [Tooltip("Damage multiplier on punches taken. Below 1 is a granite chin, above 1 glass.")]
        [Range(0.5f, 2f)]
        [SerializeField] private float _chin = 1f;

        [Tooltip("Movement speed multiplier.")]
        [Range(0.5f, 1.6f)]
        [SerializeField] private float _speed = 1f;

        [Tooltip("Stamina recovery multiplier.")]
        [Range(0.5f, 2f)]
        [SerializeField] private float _recovery = 1f;

        public string Id => string.IsNullOrEmpty(_id) ? name : _id;
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
        public string Tagline => _tagline;
        public Sprite Face => _face;
        public Color Tint => _tint;
        public FighterControl Control => _control;
        public BrainProfile Brain => _brainProfile;

        public FighterStyle ToStyle()
        {
            return new FighterStyle(_pressure, _circling, _punchGate, _opportunism, _chargeChance, _dodgeChance);
        }

        public FighterAttributes ToAttributes()
        {
            return new FighterAttributes(_power, _chin, _speed, _recovery);
        }
    }
}
