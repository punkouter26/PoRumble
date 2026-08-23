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
        [SerializeField] private float _shoulderGuardAngle = 18f;
        [SerializeField] private float _shoulderPunchAngle = 5f;

        [Header("Elbow (0 = straight, positive = flexed)")]
        [Tooltip("A human elbow flexes to roughly 145 degrees.")]
        [SerializeField] private float _elbowGuardAngle = 110f;
        [Tooltip("Never zero: elbows do not hyperextend.")]
        [SerializeField] private float _elbowPunchAngle = 8f;

        [Header("Wrist")]
        [SerializeField] private float _wristGuardAngle = 12f;
        [SerializeField] private float _wristPunchAngle = 0f;

        [Header("Servo")]
        [SerializeField] private float _servoGain = 18f;
        [SerializeField] private float _maxMotorTorque = 400f;

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

            float extension = _model.Extension;

            ServoTo(_shoulderJoint, Mathf.Lerp(_shoulderGuardAngle, _shoulderPunchAngle, extension));
            ServoTo(_elbowJoint, Mathf.Lerp(_elbowGuardAngle, _elbowPunchAngle, extension));
            ServoTo(_wristJoint, Mathf.Lerp(_wristGuardAngle, _wristPunchAngle, extension));
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
