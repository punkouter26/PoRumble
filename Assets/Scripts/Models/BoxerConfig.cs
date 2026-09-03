using UnityEngine;

namespace PoRumble.Models
{
    /// <summary>Designer-tunable combat and movement values.</summary>
    [CreateAssetMenu(menuName = "PoRumble/Boxer Config", fileName = "BoxerConfig")]
    public sealed class BoxerConfig : ScriptableObject
    {
        [Header("Health")]
        [Tooltip("Deliberately far below the Atari original's 100. With a 120-degree face arc " +
                 "rejecting most punches, 100 HP means 50-100 landed hits per KO, which makes " +
                 "matches very long and reinforcement learning episodes impractical.")]
        [SerializeField] private int _maxHealth = 30;

        [Header("Damage (mirrors Boxing 1980 scoring)")]
        [SerializeField] private int _longPunchDamage = 1;
        [SerializeField] private int _closePunchDamage = 2;
        [Tooltip("Must sit inside the band of body distances at which a punch can physically " +
                 "land (see BoxerConfigTuningTests). Set below that band, the close-range bonus " +
                 "becomes unreachable and every punch scores 1.")]
        [SerializeField] private float _closeRangeThreshold = 2.5f;

        [Header("Hitbox")]
        [SerializeField] private float _headOffset = 0.89f;
        [SerializeField] private float _headRadius = 0.8f;
        [Range(0f, 180f)]
        [SerializeField] private float _faceArcHalfAngleDegrees = 60f;
        [Tooltip("Radius of a glove. Two gloves within twice this counts as a block.")]
        [SerializeField] private float _gloveRadius = 0.30f;

        [Header("Arms")]
        [SerializeField] private float _armExtendDuration = 0.22f;
        [SerializeField] private float _armRetractDuration = 0.2f;
        [SerializeField] private float _armCooldownDuration = 0.12f;
        [SerializeField] private float _armReach = 1.6f;
        [Tooltip("Sideways distance from the body centre to each shoulder. Must match the " +
                 "prefab, or punches will land where no fist is drawn.")]
        [SerializeField] private float _armLateralOffset = 0.53f;

        [Header("Movement")]
        [SerializeField] private float _moveSpeed = 5f;
        [Tooltip("How quickly a boxer reaches full speed. Lower feels heavier.")]
        [SerializeField] private float _acceleration = 18f;
        [Tooltip("How quickly a boxer coasts to a stop.")]
        [SerializeField] private float _deceleration = 12f;
        [Tooltip("Degrees per second the boxer can turn. Humans cannot pivot instantly.")]
        [SerializeField] private float _turnSpeedDegrees = 360f;

        [Tooltip("Top sidestep speed as a fraction of the forward shuffle. A boxer sidesteps " +
                 "without crossing the feet, which is slower than travelling front-on.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float _lateralSpeedScale = 0.75f;

        [Tooltip("Top backward speed as a fraction of the forward shuffle. Retreating on the " +
                 "back foot is the slowest direction a boxer travels, which is what makes " +
                 "being walked down dangerous.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float _retreatSpeedScale = 0.6f;

        [Tooltip("Turn rate multiplier while a punch is on its way out or a haymaker is " +
                 "cocked. A thrown punch takes the shoulders with it, so a boxer cannot " +
                 "re-aim mid-swing.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float _committedTurnScale = 0.4f;

        [Header("Stamina")]
        [Tooltip("Stamina spent per punch thrown. Stays at 0.07 even though one fist at a " +
                 "time is no longer enforced: a held button still alternates, because the " +
                 "fall-through to the other arm is refused while either fist is out. " +
                 "Throughput under ordinary play is therefore the ~2.2 punches/sec this was " +
                 "tuned against. A fighter that deliberately names both arms doubles the " +
                 "drain, and clashes for its trouble.")]
        [SerializeField] private float _punchStaminaCost = 0.07f;
        [Tooltip("Stamina spent per second at full sprint.")]
        [SerializeField] private float _moveStaminaCost = 0.05f;
        [Tooltip("Speed a landed punch drives the target backwards, per point of damage. " +
                 "Momentum carries it, so the shove decays rather than teleporting anyone.")]
        [SerializeField] private float _knockbackPerDamage = 1.6f;

        [Tooltip("Stamina spent absorbing a punch on the gloves.")]
        [SerializeField] private float _blockStaminaCost = 0.012f;
        [Tooltip("Stamina recovered per second while not throwing.")]
        [SerializeField] private float _staminaRecovery = 0.18f;
        [Tooltip("Speed and damage multiplier when completely spent.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float _exhaustedPenalty = 0.45f;

        [Tooltip("Half the width of a body. Two boxers cannot come closer than twice this, so " +
                 "it must stay above the nearest separation at which a punch can reach a face " +
                 "(see BoxerConfigTuningTests) - otherwise fighters can bulldoze into a clinch " +
                 "where neither can land and the exchange deadlocks.")]
        [SerializeField] private float _bodyRadius = 0.98f;

        [Header("Charged punch")]
        [Tooltip("Seconds of holding the charge button to reach a full-power haymaker.")]
        [SerializeField] private float _chargeDuration = 0.75f;
        [Tooltip("Releasing below this charge throws an ordinary punch instead, so tapping " +
                 "the charge button is never worse than tapping punch.")]
        [Range(0f, 1f)]
        [SerializeField] private float _minChargeToRelease = 0.25f;
        [Tooltip("Damage multiplier at full charge. The whole risk/reward of the mechanic: " +
                 "big enough to be worth the wind-up, small enough that whiffing hurts.")]
        [SerializeField] private float _chargeDamageMultiplier = 3f;
        [Tooltip("How much longer the swing takes at full charge. This is the telegraph — " +
                 "the window in which an opponent can see it coming and get out of the way.")]
        [SerializeField] private float _chargeWindupScale = 2.2f;
        [Tooltip("Extra knockback multiplier at full charge, so a haymaker visibly throws " +
                 "the target across the ring.")]
        [SerializeField] private float _chargeKnockbackMultiplier = 2.5f;
        [Tooltip("Stamina spent on a full-charge haymaker, on top of the ordinary punch cost.")]
        [SerializeField] private float _chargeStaminaCost = 0.10f;
        [Tooltip("Movement multiplier while winding up. Committing to a haymaker should cost " +
                 "mobility, otherwise there is no reason ever to throw a jab.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float _chargeMoveScale = 0.45f;

        [Header("Slip")]
        [Tooltip("Seconds of invulnerability a slip buys. Must stay comfortably above " +
                 "Arm Extend Duration: a fighter slips on seeing the arm start to travel, so " +
                 "a window shorter than the punch's flight time closes before the punch " +
                 "arrives and the slip does nothing at all. DodgeTests pins this.")]
        [SerializeField] private float _dodgeDuration = 0.3f;
        [Tooltip("Seconds before another slip is allowed, measured from the end of the last " +
                 "one. This is the whole cost of the mechanic being readable: a fighter that " +
                 "could slip on demand would never need to block.")]
        [SerializeField] private float _dodgeCooldown = 0.85f;
        [Tooltip("Stamina spent on a slip. Comfortably more than a punch — evasion should be " +
                 "the expensive option, or nobody would ever trade.")]
        [SerializeField] private float _dodgeStaminaCost = 0.16f;
        [Tooltip("Sideways speed the slip carries, as a multiple of the walking speed. This " +
                 "is what moves the head off the line and makes the dodge visible.")]
        [SerializeField] private float _dodgeSpeedScale = 2.4f;
        [Tooltip("How far out an incoming punch is worth slipping, as a multiple of the reach " +
                 "at which one can actually land.")]
        [SerializeField] private float _dodgeThreatRangeScale = 1.35f;

        [Header("Punch mechanics")]
        [Tooltip("How far the glove travels toward the centreline as the arm extends, as a " +
                 "fraction of the shoulder's lateral offset. 0 punches on two parallel rails " +
                 "0.53 either side of the spine, which is what a boxer's arms emphatically do " +
                 "not do: a straight punch crosses the middle, which is why you cannot throw " +
                 "both at once.\n\n" +
                 "At 0.85 the two gloves finish 0.16 apart at full extension - inside a glove " +
                 "diameter, so simultaneous punches physically clash - without ever being " +
                 "exactly coincident, which would give the block maths a zero-length vector.")]
        [Range(0f, 1f)]
        [SerializeField] private float _punchConvergence = 0.85f;

        [Tooltip("Sideways distance from the spine to a glove at rest. The shoulder stays out " +
                 "at Arm Lateral Offset; the hand does not, because a guard is carried at the " +
                 "chin. The model used to put the resting glove level with its own shoulder, " +
                 "which made the guard a point 0.53 out to the side - so once punches began " +
                 "converging on the centreline they arrived nowhere near it and almost nothing " +
                 "could be blocked. Shoulder-to-glove is now a diagonal across the chest, " +
                 "which is both what a guard looks like and what it covers.")]
        [SerializeField] private float _guardLateralOffset = 0.18f;

        [Tooltip("Forward speed a thrown punch carries the body into, before stamina. A punch " +
                 "is delivered by stepping into it - the arm alone is the weakest way a human " +
                 "can generate force - so throwing one commits the feet as well as the " +
                 "shoulders. Small: this is a step, not a lunge, and it decays under " +
                 "Deceleration within about a tenth of a second.")]
        [SerializeField] private float _punchLungeSpeed = 1.2f;

        [Header("Stun")]
        [Tooltip("Stun accumulated per point of damage taken. A knockout in a real fight is " +
                 "an accumulation of trauma inside a short window, not a health bar reaching " +
                 "zero, and without this a boxer on 1 HP fights exactly as well as one on 30.")]
        [SerializeField] private float _stunPerDamage = 0.16f;

        [Tooltip("Stun shed per second. Sets how long a window has to be exploited before it " +
                 "closes; at 0.5 a two-damage punch is fully cleared in under a second, so " +
                 "wobbling somebody takes a combination rather than one lucky shot.")]
        [SerializeField] private float _stunDecayPerSecond = 0.5f;

        [Tooltip("Accumulated stun above which the boxer is visibly wobbled. Reachable in " +
                 "roughly three or four landed punches inside a second.")]
        [Range(0.1f, 2f)]
        [SerializeField] private float _stunThreshold = 0.6f;

        [Tooltip("Speed and acceleration multiplier while wobbled. This is what makes " +
                 "pressing an advantage the correct play rather than resetting to range.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float _stunnedMobilityScale = 0.55f;

        [Tooltip("Turn rate multiplier while wobbled. Lower than the mobility scale: losing " +
                 "the ability to keep the guard pointed at the attacker is what actually " +
                 "opens the face arc up.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float _stunnedTurnScale = 0.4f;

        [Tooltip("Ceiling on accumulated stun, so a haymaker cannot bank a wobble that " +
                 "outlasts the exchange that earned it.")]
        [SerializeField] private float _maxStun = 1.5f;

        [Header("Counter")]
        [Tooltip("Seconds after blocking a punch during which your next landed punch counts " +
                 "as a counter. Rewards reading the opponent rather than mashing.")]
        [SerializeField] private float _counterWindowDuration = 0.45f;
        [Tooltip("Flat extra damage on a countered punch.")]
        [SerializeField] private int _counterDamageBonus = 2;

        public int MaxHealth => _maxHealth;
        public float HeadOffset => _headOffset;
        public float GloveRadius => _gloveRadius;
        public float HeadRadius => _headRadius;
        public float CloseRangeThreshold => _closeRangeThreshold;
        public float ArmExtendDuration => _armExtendDuration;
        public float ArmRetractDuration => _armRetractDuration;
        public float ArmCooldownDuration => _armCooldownDuration;
        public float ArmReach => _armReach;
        public float ArmLateralOffset => _armLateralOffset;
        public float MoveSpeed => _moveSpeed;
        public float BodyRadius => _bodyRadius;
        public float Acceleration => _acceleration;
        public float Deceleration => _deceleration;
        public float TurnSpeedDegrees => _turnSpeedDegrees;
        public float LateralSpeedScale => _lateralSpeedScale;
        public float RetreatSpeedScale => _retreatSpeedScale;
        public float CommittedTurnScale => _committedTurnScale;
        public float PunchStaminaCost => _punchStaminaCost;
        public float MoveStaminaCost => _moveStaminaCost;
        public float StaminaRecovery => _staminaRecovery;
        public float BlockStaminaCost => _blockStaminaCost;
        public float KnockbackPerDamage => _knockbackPerDamage;
        public float ExhaustedPenalty => _exhaustedPenalty;
        public float ChargeDuration => _chargeDuration;
        public float MinChargeToRelease => _minChargeToRelease;
        public float ChargeDamageMultiplier => _chargeDamageMultiplier;
        public float ChargeWindupScale => _chargeWindupScale;
        public float ChargeKnockbackMultiplier => _chargeKnockbackMultiplier;
        public float ChargeStaminaCost => _chargeStaminaCost;
        public float ChargeMoveScale => _chargeMoveScale;
        public float DodgeDuration => _dodgeDuration;
        public float DodgeCooldown => _dodgeCooldown;
        public float DodgeStaminaCost => _dodgeStaminaCost;
        public float DodgeSpeedScale => _dodgeSpeedScale;
        public float DodgeThreatRangeScale => _dodgeThreatRangeScale;
        public float PunchConvergence => _punchConvergence;
        public float GuardLateralOffset => _guardLateralOffset;
        public float PunchLungeSpeed => _punchLungeSpeed;
        public float StunPerDamage => _stunPerDamage;
        public float StunDecayPerSecond => _stunDecayPerSecond;
        public float StunThreshold => _stunThreshold;
        public float StunnedMobilityScale => _stunnedMobilityScale;
        public float StunnedTurnScale => _stunnedTurnScale;
        public float MaxStun => _maxStun;
        public float CounterWindowDuration => _counterWindowDuration;
        public int CounterDamageBonus => _counterDamageBonus;

        public CombatSettings ToCombatSettings()
        {
            return new CombatSettings(
                _headOffset,
                _headRadius,
                _faceArcHalfAngleDegrees,
                _closeRangeThreshold,
                _longPunchDamage,
                _closePunchDamage);
        }
    }
}
