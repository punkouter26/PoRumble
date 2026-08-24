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
        [SerializeField] private float _closeRangeThreshold = 1.4f;

        [Header("Hitbox")]
        [SerializeField] private float _headOffset = 0.5f;
        [SerializeField] private float _headRadius = 0.45f;
        [Range(0f, 180f)]
        [SerializeField] private float _faceArcHalfAngleDegrees = 60f;
        [Tooltip("Radius of a glove. Two gloves within twice this counts as a block.")]
        [SerializeField] private float _gloveRadius = 0.30f;

        [Header("Arms")]
        [SerializeField] private float _armExtendDuration = 0.12f;
        [SerializeField] private float _armRetractDuration = 0.18f;
        [SerializeField] private float _armCooldownDuration = 0.15f;
        [SerializeField] private float _armReach = 0.9f;
        [Tooltip("Sideways distance from the body centre to each shoulder. Must match the " +
                 "prefab, or punches will land where no fist is drawn.")]
        [SerializeField] private float _armLateralOffset = 0.3f;

        [Header("Movement")]
        [SerializeField] private float _moveSpeed = 5f;
        [Tooltip("How quickly a boxer reaches full speed. Lower feels heavier.")]
        [SerializeField] private float _acceleration = 18f;
        [Tooltip("How quickly a boxer coasts to a stop.")]
        [SerializeField] private float _deceleration = 12f;
        [Tooltip("Degrees per second the boxer can turn. Humans cannot pivot instantly.")]
        [SerializeField] private float _turnSpeedDegrees = 540f;

        [Header("Stamina")]
        [Tooltip("Stamina spent per punch thrown.")]
        [SerializeField] private float _punchStaminaCost = 0.035f;
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
        [SerializeField] private float _bodyRadius = 0.4f;

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
        public float PunchStaminaCost => _punchStaminaCost;
        public float MoveStaminaCost => _moveStaminaCost;
        public float StaminaRecovery => _staminaRecovery;
        public float BlockStaminaCost => _blockStaminaCost;
        public float KnockbackPerDamage => _knockbackPerDamage;
        public float ExhaustedPenalty => _exhaustedPenalty;

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
