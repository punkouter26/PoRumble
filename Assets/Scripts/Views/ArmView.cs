using PoRumble.Models;
using UnityEngine;

namespace PoRumble.Views
{
    /// <summary>
    /// Drives one anatomically jointed arm: torso -> shoulder -> upper arm -> elbow -> forearm
    /// -> wrist -> glove. Each segment is a fixed-length rigid body held by a
    /// <see cref="HingeJoint2D"/> with human-like angle limits, so the arm folds and swings
    /// rather than telescoping.
    ///
    /// The joints are servoed toward the extension the model has already decided on, rather than
    /// physics deciding how far a punch reached. Combat stays deterministic - which the
    /// reinforcement learning depends on - while the limb itself is real 2D physics.
    ///
    /// Segment lengths sum to BoxerConfig.ArmReach, so at full extension the glove sits where
    /// CombatMath expects it. Hits only resolve at peak extension, so the drawn arm and the hit
    /// test agree at the one moment that matters.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArmView : MonoBehaviour
    {
        [Header("Joints")]
        [SerializeField] private HingeJoint2D _shoulderJoint;
        [SerializeField] private HingeJoint2D _elbowJoint;
        [SerializeField] private HingeJoint2D _wristJoint;

        [Header("Shoulder (degrees, relative to torso)")]
        [SerializeField] private float _shoulderGuardAngle = 0f;
        [SerializeField] private float _shoulderPunchAngle = 0f;

        [Header("Elbow (0 = straight, positive = flexed)")]
        [Tooltip("Resting bend. Kept shallow so the arms sit extended toward the opponent; a " +
                 "human elbow can flex to roughly 145 degrees, which the joint limit allows.")]
        [SerializeField] private float _elbowGuardAngle = 45f;
        [Tooltip("Never zero: elbows do not hyperextend.")]
        [SerializeField] private float _elbowPunchAngle = 0f;

        [Header("Wrist")]
        [SerializeField] private float _wristGuardAngle = 0f;
        [SerializeField] private float _wristPunchAngle = 0f;

        [Tooltip("Mirrors every target angle. The two arms sit on opposite sides of the body, " +
                 "so the same angle bends one inward and the other outward; the right arm needs " +
                 "the sign flipped to fold symmetrically.")]
        [SerializeField] private bool _mirror;

        [Tooltip("How far past the guard pose the arm cocks back at full haymaker charge, as " +
                 "a fraction of the guard-to-punch swing. This is the telegraph an opponent " +
                 "reads: purely visual, since hits still resolve at full extension.")]
        [Range(0f, 1.5f)]
        [SerializeField] private float _windupPullback = 0.55f;

        [Tooltip("Optional. Trail streaming off the glove while the punch is travelling, so a " +
                 "fast exchange leaves a readable arc rather than a blur of fists.")]
        [SerializeField] private TrailRenderer _gloveTrail;

        [Tooltip("Extension above which the trail emits. Kept off the very start of the swing " +
                 "so a cocked haymaker does not smear before it has been thrown.")]
        [Range(0f, 1f)]
        [SerializeField] private float _trailThreshold = 0.35f;

        [Header("Servo")]
        [SerializeField] private float _servoGain = 90f;
        [SerializeField] private float _maxMotorTorque = 4000f;

        private ArmModel _model;

        public void Bind(ArmModel model)
        {
            _model = model;
        }

        private void FixedUpdate()
        {
            if (_model == null)
            {
                return;
            }

            // Winding up drives extension negative, which cocks the arm back behind its
            // guard pose. LerpUnclamped rather than Lerp: the clamped form would pin the
            // wind-up at the guard angle and the telegraph would be invisible.
            float extension = _model.Extension - _model.Windup * _windupPullback;
            float sign = _mirror ? -1f : 1f;

            ServoTo(_shoulderJoint, sign * Mathf.LerpUnclamped(_shoulderGuardAngle, _shoulderPunchAngle, extension));
            ServoTo(_elbowJoint, sign * Mathf.LerpUnclamped(_elbowGuardAngle, _elbowPunchAngle, extension));
            ServoTo(_wristJoint, sign * Mathf.LerpUnclamped(_wristGuardAngle, _wristPunchAngle, extension));

            if (_gloveTrail != null)
            {
                // Driven off the model's extension rather than the rendered joint angle: the
                // joints are servoed and lag behind, so a trail keyed to them would start late
                // and outlive the punch.
                _gloveTrail.emitting = _model.Extension >= _trailThreshold;
            }
        }

        /// <summary>Drives a hinge toward a target angle with a proportional motor.</summary>
        private void ServoTo(HingeJoint2D joint, float targetAngle)
        {
            if (joint == null)
            {
                return;
            }

            float error = targetAngle - joint.jointAngle;

            JointMotor2D motor = joint.motor;
            motor.motorSpeed = error * _servoGain;
            motor.maxMotorTorque = _maxMotorTorque;
            joint.motor = motor;
        }
    }
}
