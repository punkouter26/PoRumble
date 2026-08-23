using PoRumble.Models;
using UnityEngine;

namespace PoRumble.Views
{
    /// <summary>
    /// Renders one arm as a circular fist that slides out from the shoulder on a
    /// <see cref="SliderJoint2D"/>, with a limb drawn between the two.
    ///
    /// The joint motor is servoed toward the position the model has already decided on, rather
    /// than the physics deciding how far the punch reached. Combat stays deterministic (which
    /// reinforcement learning depends on) while the fist is still a real 2D body that can be
    /// pushed against and collided with.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArmView : MonoBehaviour
    {
        [SerializeField] private SliderJoint2D _fistJoint;
        [SerializeField] private Transform _fistTransform;
        [SerializeField] private Transform _limbTransform;

        [SerializeField] private float _reach = 1.4f;
        [SerializeField] private float _restLength = 0.38f;
        [SerializeField] private float _limbWidth = 0.12f;

        [Tooltip("How hard the motor chases the model's extension. Higher tracks tighter.")]
        [SerializeField] private float _servoGain = 30f;
        [SerializeField] private float _maxMotorForce = 200f;

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

            float target = _restLength + _reach * _model.Extension;

            // With a joint assigned the fist is a physics body servoed toward the model's
            // extension. Without one it is a plain child transform, which is the simpler and
            // currently the default setup.
            if (_fistJoint != null)
            {
                float error = target - _fistJoint.jointTranslation;
                JointMotor2D motor = _fistJoint.motor;
                motor.motorSpeed = error * _servoGain;
                motor.maxMotorTorque = _maxMotorForce;
                _fistJoint.motor = motor;
                return;
            }

            if (_fistTransform != null)
            {
                _fistTransform.localPosition = new Vector3(0f, target, 0f);
            }
        }

        private void LateUpdate()
        {
            if (_limbTransform == null || _fistTransform == null)
            {
                return;
            }

            // The fist is a jointed sibling, not a child, so measure in world space from the
            // shoulder (the limb's parent) out to wherever physics has actually put the fist.
            Transform shoulder = _limbTransform.parent;

            if (shoulder == null)
            {
                return;
            }

            float length = Mathf.Max(0.01f, Vector3.Distance(shoulder.position, _fistTransform.position));

            _limbTransform.localScale = new Vector3(_limbWidth, length, 1f);
            _limbTransform.localPosition = new Vector3(0f, length * 0.5f, 0f);
        }
    }
}
