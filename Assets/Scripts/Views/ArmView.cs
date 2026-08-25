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

        [Header("Punch shape")]
        [Tooltip("How far the fist draws back before it fires, as a fraction of the " +
                 "guard-to-punch swing. A punch that starts from the guard and only ever " +
                 "travels forward has no coil in it and lands looking like a push.")]
        [Range(0f, 1f)]
        [SerializeField] private float _cockDepth = 0.35f;

        [Tooltip("Share of the extension window spent drawing back. The rest is the strike, " +
                 "so a small number here makes the arm snap out over a longer travel in less " +
                 "time - which is where the speed on the follow-through comes from.")]
        [Range(0.05f, 0.6f)]
        [SerializeField] private float _cockFraction = 0.22f;

        [Header("Servo")]
        [SerializeField] private float _servoGain = 90f;

        [Tooltip("Torque available at the shoulder, the strongest joint in the arm.")]
        [SerializeField] private float _maxMotorTorque = 4000f;

        [Tooltip("Elbow torque as a fraction of the shoulder's. The arm tapers in muscle as " +
                 "it tapers in mass, so one flat figure for all three joints gives a limb " +
                 "that is equally rigid at the wrist as at the shoulder and absorbs nothing " +
                 "on contact.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float _elbowTorqueScale = 0.55f;

        [Tooltip("Wrist torque as a fraction of the shoulder's. Lowest of the three: a wrist " +
                 "gives on impact, which is what makes a landed punch look like it hit " +
                 "something rather than passing through it.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float _wristTorqueScale = 0.2f;

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
            float extension = ShapeStrike(_model.Extension) - _model.Windup * _windupPullback;
            float sign = _mirror ? -1f : 1f;

            ServoTo(
                _shoulderJoint,
                sign * Mathf.LerpUnclamped(_shoulderGuardAngle, _shoulderPunchAngle, extension),
                _maxMotorTorque);
            ServoTo(
                _elbowJoint,
                sign * Mathf.LerpUnclamped(_elbowGuardAngle, _elbowPunchAngle, extension),
                _maxMotorTorque * _elbowTorqueScale);
            ServoTo(
                _wristJoint,
                sign * Mathf.LerpUnclamped(_wristGuardAngle, _wristPunchAngle, extension),
                _maxMotorTorque * _wristTorqueScale);

            if (_gloveTrail != null)
            {
                // Driven off the model's extension rather than the rendered joint angle: the
                // joints are servoed and lag behind, so a trail keyed to them would start late
                // and outlive the punch.
                _gloveTrail.emitting = _model.Extension >= _trailThreshold;
            }
        }

        /// <summary>
        /// Reshapes the model's linear 0..1 extension into a punch that coils before it
        /// strikes: the fist pulls back behind the guard, then drives forward and straight.
        ///
        /// Purely how the arm is drawn. The model still reaches full extension on its own
        /// schedule and the hit still resolves there, so the two ends are pinned exactly -
        /// 0 returns the guard pose and 1 returns the punch pose, whatever the shaping does
        /// in between. Get that wrong and the drawn fist stops agreeing with the hit test.
        /// </summary>
        private float ShapeStrike(float extension)
        {
            if (extension <= 0f || extension >= 1f)
            {
                return extension;
            }

            if (extension < _cockFraction)
            {
                // Drawing back. Negative extension extrapolates past the guard pose, which is
                // what folds the elbow and pulls the shoulder behind the body.
                return -_cockDepth * (extension / _cockFraction);
            }

            // The strike: from fully cocked out to full reach, covering more travel in less
            // time than a plain ramp would.
            float strike = (extension - _cockFraction) / (1f - _cockFraction);
            return Mathf.Lerp(-_cockDepth, 1f, strike);
        }

        /// <summary>
        /// Drives a hinge toward a target angle with a proportional motor, capped at the
        /// torque that joint can actually produce.
        /// </summary>
        private void ServoTo(HingeJoint2D joint, float targetAngle, float maxTorque)
        {
            if (joint == null)
            {
                return;
            }

            float error = targetAngle - joint.jointAngle;

            JointMotor2D motor = joint.motor;
            motor.motorSpeed = error * _servoGain;
            motor.maxMotorTorque = maxTorque;
            joint.motor = motor;
        }
    }
}
