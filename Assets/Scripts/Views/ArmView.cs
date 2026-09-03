using System.Collections.Generic;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using VContainer;

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

        [Tooltip("Peak extra shoulder rotation through the middle of the swing, added on top " +
                 "of the guard-to-punch angles above. Power in a punch comes from the " +
                 "shoulder driving through, not from the elbow straightening: without this " +
                 "the shoulder travelled 15 degrees to the elbow's 117 and every punch was an " +
                 "elbow flick down the centreline, identical on both arms.\n\n" +
                 "Applied on a sine envelope that is zero at both ends of the swing, so the " +
                 "guard pose and the fully extended pose are byte-identical to what they were " +
                 "and the drawn glove still arrives exactly where CombatMath resolves the " +
                 "hit. All of the motion is in between, which is where an arc reads.")]
        [Range(0f, 90f)]
        [SerializeField] private float _shoulderDriveAngle = 26f;

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

        [Tooltip("Ceiling on the speed the servo will ask a joint for, in degrees per second. " +
                 "Not a nicety - it is what stops a runaway. HingeJoint2D.jointAngle is " +
                 "cumulative and unbounded: it does not wrap at 180, it keeps counting. If a " +
                 "joint is ever driven past its limit hard enough to get round, the raw " +
                 "difference between target and jointAngle grows without bound, the servo asks " +
                 "for a proportionally larger speed, and the arm spins itself off the body. " +
                 "Measured mid-fight with joints at -6543 degrees asking for 300,000 deg/s.")]
        [SerializeField] private float _maxMotorSpeed = 1800f;

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
        private BoxerModel _boxer;
        private BoxerSystem _boxerSystem;

        /// <summary>
        /// True when the limb is posed straight from the model instead of being servoed by
        /// the physics solver. See <see cref="SetKinematicDrive"/>.
        /// </summary>
        private bool _kinematic;

        [Inject]
        public void Construct(BoxerSystem boxerSystem)
        {
            _boxerSystem = boxerSystem;
        }

        public void Bind(BoxerModel boxer, ArmModel model)
        {
            _boxer = boxer;
            _model = model;
        }

        /// <summary>
        /// Swaps the arm between being servoed by the physics solver and being posed directly
        /// from the model.
        ///
        /// The arms are cosmetic: every hit is resolved by CombatMath against the model's own
        /// extension, and the segments carry no colliders. In a training scene that makes six
        /// dynamic bodies and six hinge joints per fighter - sixty of each in a ten-way -
        /// solved fifty times a second for a picture nobody is looking at.
        ///
        /// Turning it off drives the glove transform straight to the position CombatMath
        /// already believes it occupies. That is the one part of the arm that has to stay
        /// truthful, because a glove collider still occludes other fighters' rays; posing it
        /// from the model means perception is not merely close to the game's but identical to
        /// it, so nothing about this trades a sim-to-real gap for the speed.
        /// </summary>
        public void SetKinematicDrive(bool kinematic)
        {
            _kinematic = kinematic;
            ApplyJoint(_shoulderJoint, kinematic);
            ApplyJoint(_elbowJoint, kinematic);
            ApplyJoint(_wristJoint, kinematic);
        }

        /// <summary>
        /// Adds this arm's own colliders to the list.
        ///
        /// Read off the joints rather than from serialized fields, so the arm cannot end up
        /// describing a set of segments it is not actually driving. Used by the spawner to
        /// decide which self-collisions to keep: a fighter's two arms must stop each other,
        /// while every other pair of its own parts must not.
        /// </summary>
        public void CollectColliders(List<Collider2D> results)
        {
            AddColliders(_shoulderJoint, results);
            AddColliders(_elbowJoint, results);
            AddColliders(_wristJoint, results);
        }

        private static void AddColliders(HingeJoint2D joint, List<Collider2D> results)
        {
            if (joint == null)
            {
                return;
            }

            Rigidbody2D body = joint.attachedRigidbody;

            if (body == null)
            {
                return;
            }

            Collider2D[] colliders = body.GetComponents<Collider2D>();

            for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
            {
                results.Add(colliders[colliderIndex]);
            }
        }

        /// <summary>
        /// Stops one joint and the body it drives. Both halves are needed: disabling the
        /// joint alone leaves a free dynamic body that the solver still integrates, and it
        /// would drift away from the arm it is supposed to be part of.
        /// </summary>
        private static void ApplyJoint(HingeJoint2D joint, bool kinematic)
        {
            if (joint == null)
            {
                return;
            }

            joint.enabled = !kinematic;

            Rigidbody2D body = joint.attachedRigidbody;

            if (body != null)
            {
                body.simulated = !kinematic;
            }
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
            if (_kinematic)
            {
                PoseGloveFromModel();
                return;
            }

            float extension = ShapeStrike(_model.Extension) - _model.Windup * _windupPullback;
            float sign = _mirror ? -1f : 1f;

            // The shoulder's own contribution, peaking mid-swing and vanishing at both ends.
            // Keyed to the model's linear extension rather than to the shaped one, so the
            // envelope is exactly zero at the guard pose and exactly zero at full reach
            // whatever ShapeStrike does in between.
            float drive = _shoulderDriveAngle * Mathf.Sin(Mathf.PI * Mathf.Clamp01(_model.Extension));

            ServoTo(
                _shoulderJoint,
                sign * (Mathf.LerpUnclamped(_shoulderGuardAngle, _shoulderPunchAngle, extension) + drive),
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
        /// Puts the glove exactly where the combat maths says it is. Used only while the
        /// solver is switched off; the upper arm and forearm are left where they were, which
        /// is why this is for training scenes and not for anything anybody looks at.
        /// </summary>
        private void PoseGloveFromModel()
        {
            if (_wristJoint == null || _boxer == null || _boxerSystem == null)
            {
                return;
            }

            Transform glove = _wristJoint.transform;
            Vector2 target = _boxerSystem.GetGlovePosition(_boxer, _model);

            // Z is preserved rather than zeroed: sorting order in a 2D scene rides on it.
            Vector3 position = glove.position;
            glove.position = new Vector3(target.x, target.y, position.z);
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

            // DeltaAngle, not subtraction. jointAngle accumulates without wrapping, so a raw
            // difference is only correct while the joint has stayed inside one revolution -
            // and the moment it has not, the error is enormous and the servo drives the arm
            // further out rather than back.
            float error = Mathf.DeltaAngle(joint.jointAngle, targetAngle);

            JointMotor2D motor = joint.motor;
            motor.motorSpeed = Mathf.Clamp(error * _servoGain, -_maxMotorSpeed, _maxMotorSpeed);
            motor.maxMotorTorque = maxTorque;
            joint.motor = motor;
        }
    }
}
