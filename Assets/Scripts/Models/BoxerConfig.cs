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
        [SerializeField] private float _bodyRadius = 0.4f;

        public int MaxHealth => _maxHealth;
        public float HeadOffset => _headOffset;
        public float CloseRangeThreshold => _closeRangeThreshold;
        public float ArmExtendDuration => _armExtendDuration;
        public float ArmRetractDuration => _armRetractDuration;
        public float ArmCooldownDuration => _armCooldownDuration;
        public float ArmReach => _armReach;
        public float ArmLateralOffset => _armLateralOffset;
        public float MoveSpeed => _moveSpeed;
        public float BodyRadius => _bodyRadius;

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
