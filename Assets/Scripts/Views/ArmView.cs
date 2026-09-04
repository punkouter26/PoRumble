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

        [Tooltip("Distance from the body centre to the DRAWN head, along the facing - the Head " +
                 "sprite sits at local y 0.36 on the torso. Deliberately not " +
                 "BoxerConfig.HeadOffset, which is 0.89: that is where the hit maths puts the " +
                 "head, and this is where the picture puts it. The elbow is bent away from " +
                 "whichever side this lands on, so it is the drawn head that has to be used " +
                 "or the arm is routed around empty canvas half a unit past the face.")]
        [SerializeField] private float _headOffset = 0.36f;

        [Tooltip("How far forward the drawn hand is carried at rest, from the body centre. " +
                 "A guard is held up at the chin, in front of the face - not down at the ribs.")]
        [SerializeField] private float _guardHandForward = 0.62f;

        [Tooltip("How far to the side the drawn hand is carried at rest. Has to keep the hand " +
                 "and the forearm behind it clear of the head circle: at 0.42 against a head " +
                 "of radius 0.30 sitting 0.36 forward, the hand clears it by 0.05 and the " +
                 "elbow is pushed further out still. Bring it in much below 0.40 and the " +
                 "forearm starts crossing the face again.")]
        [SerializeField] private float _guardHandLateral = 0.42f;

        [Tooltip("How close the drawn hand may come to the head centre. The head sprite is " +
                 "0.30 across the radius and an arm capsule is 0.14 across the half-width, so " +
                 "0.46 keeps the bone clear of the face with a little to spare. Raising it " +
                 "holds the guard wider; lowering it below 0.44 lets the forearm clip the head.")]
        [SerializeField] private float _headKeepOut = 0.46f;

        [Header("Segment lengths - must match the prefab or the drawn arm misses the hitbox")]
        [Tooltip("Shoulder to elbow. Read off the prefab: UpperArm sits at y 0.09 and Forearm " +
                 "at y 0.82 on the boxer root, so the bone between them is 0.73.")]
        [SerializeField] private float _upperArmLength = 0.73f;

        [Tooltip("Elbow to glove. Forearm at y 0.82, Glove at y 1.60, so 0.78.")]
        [SerializeField] private float _forearmLength = 0.78f;

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
                PoseArmFromModel();
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
        /// Places the whole arm - upper arm, forearm and glove - on the line the combat maths
        /// already believes in, by two-link inverse kinematics from the shoulder to the glove.
        ///
        /// This replaces the servo rather than assisting it. A HingeJoint2D driven by a
        /// proportional motor could not be kept inside its own limits here: measured
        /// mid-fight, shoulders sat at 1731 degrees and elbows at 6479 against limits of 45
        /// and 92, and no combination of masses, torque, speed clamp or error formulation
        /// stopped them winding. The arm decides nothing - CombatMath resolves every hit from
        /// the model's own extension - so simulating it bought nothing and cost a limb that
        /// span up, swung through the fighter's own head and overlapped its body.
        ///
        /// Posed this way the arm is deterministic, it lands exactly where the hit test says
        /// it does, and the elbow is put on the outward side of the shoulder-to-glove line by
        /// construction, so it cannot fold across the face.
        /// </summary>
        private void PoseArmFromModel()
        {
            if (_boxer == null || _boxerSystem == null)
            {
                return;
            }

            Transform upper = _shoulderJoint != null ? _shoulderJoint.transform : null;
            Transform fore = _elbowJoint != null ? _elbowJoint.transform : null;
            Transform glove = _wristJoint != null ? _wristJoint.transform : null;

            if (upper == null || fore == null || glove == null)
            {
                return;
            }

            Vector2 shoulder = _boxerSystem.GetShoulderPosition(_boxer, _model);
            Vector2 facing = _boxer.Facing.normalized;
            Vector2 lateral = new(-facing.y, facing.x);

            // Where the hand is drawn at rest.
            //
            // Not GetGlovePosition at extension 0, which returns the shoulder: the model
            // treats a retracted arm as having no reach at all, deliberately, so that a
            // tucked arm does not guard the whole span it would have had if thrown. That is
            // the right abstraction for the hit maths and a hopeless one to draw from - an
            // arm 1.51 long folded into nothing doubles back on itself and sweeps the elbow
            // and forearm straight across the fighter's own face.
            //
            // So the picture gets its own guard, carried at the chin and outside the head,
            // and blends out to the model's glove as the punch travels. Only the far end has
            // to agree with the maths, because hits resolve at full extension and nowhere
            // else.
            float side = _mirror ? -1f : 1f;
            Vector2 guardHand = _boxer.Position
                                + facing * _guardHandForward
                                + lateral * (side * _guardHandLateral);

            Vector2 thrown = _boxerSystem.GetGlovePosition(_boxer, _model);
            Vector2 target = Vector2.Lerp(guardHand, thrown, Mathf.Clamp01(_model.Extension));

            // The hand is pushed out of the head before anything is solved.
            //
            // Choosing which way the elbow bends keeps the *elbow* off the face, but the
            // forearm's other end is the hand, and on the way out of a tight guard that is
            // the point that grazes the head. Displacing the target radially is the only
            // step that makes the whole segment clear by construction: with both of its
            // endpoints outside the circle and on the same side of it, the bone between them
            // cannot cross it.
            Vector2 head = _boxer.Position + facing * _headOffset;
            target = PushOutOfHead(target, head);

            Vector2 delta = target - shoulder;
            float reach = _upperArmLength + _forearmLength;

            // Clamped just inside full reach. At full extension the model's glove sits a
            // fraction further out than the arm is long - the punch converges toward the
            // centreline while the shoulder stays wide - and an unclamped solve would take
            // the square root of a negative number.
            float distance = Mathf.Min(delta.magnitude, reach * 0.999f);

            if (distance <= 0.0001f)
            {
                return;
            }

            Vector2 direction = delta / delta.magnitude;

            // Standard two-link solve: how far along the line the elbow sits, and how far off.
            float alongDistance = (distance * distance
                                   + _upperArmLength * _upperArmLength
                                   - _forearmLength * _forearmLength) / (2f * distance);
            float off = Mathf.Sqrt(Mathf.Max(
                0f, _upperArmLength * _upperArmLength - alongDistance * alongDistance));

            // Whichever of the two solutions puts the elbow further from the head.
            //
            // Measured rather than read off the mirror flag: which sign is "out" depends on
            // the side of the body, the facing and the winding of the perpendicular, and
            // getting any one of those backwards reintroduces the fault silently.
            //
            // Keeping the elbow outboard of the shoulder instead was tried and measured
            // worse - 19-33 segments touching the head against 13-19 for this. Neither is
            // clean, and the reason is geometric rather than a choice of rule: see the note
            // on PoseArmFromModel about an arm too long to fold this tightly.
            Vector2 perpendicular = new(-direction.y, direction.x);
            Vector2 along = direction * alongDistance;
            Vector2 first = shoulder + along + perpendicular * off;
            Vector2 second = shoulder + along - perpendicular * off;
            Vector2 elbow = (first - head).sqrMagnitude >= (second - head).sqrMagnitude
                ? first
                : second;

            Place(upper, shoulder, elbow);
            Place(fore, elbow, target);
            glove.position = new Vector3(target.x, target.y, glove.position.z);
            glove.rotation = fore.rotation;
        }

        /// <summary>
        /// Moves a point radially out of the head's keep-out circle, leaving it alone if it
        /// was already clear.
        ///
        /// A hand exactly on the head centre has no direction to be pushed in, so that one
        /// degenerate case is sent sideways rather than left where it is.
        /// </summary>
        private Vector2 PushOutOfHead(Vector2 point, Vector2 head)
        {
            Vector2 offset = point - head;
            float distance = offset.magnitude;

            if (distance >= _headKeepOut)
            {
                return point;
            }

            if (distance <= Mathf.Epsilon)
            {
                return head + new Vector2(_headKeepOut, 0f);
            }

            return head + offset * (_headKeepOut / distance);
        }

        /// <summary>
        /// Puts a segment's origin at <paramref name="from"/> and points its local +Y at
        /// <paramref name="to"/>. Z is preserved: sorting order in a 2D scene rides on it.
        /// </summary>
        private static void Place(Transform segment, Vector2 from, Vector2 to)
        {
            segment.position = new Vector3(from.x, from.y, segment.position.z);

            Vector2 axis = to - from;

            if (axis.sqrMagnitude > Mathf.Epsilon)
            {
                segment.rotation = Quaternion.Euler(
                    0f, 0f, Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg - 90f);
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

            // Raw difference, deliberately NOT Mathf.DeltaAngle, and the clamp below is what
            // makes it safe.
            //
            // HingeJoint2D.jointAngle accumulates without wrapping. DeltaAngle takes the
            // shortest way round to a coterminal angle, which sounds like the fix and is a
            // trap: a joint sitting at 1731 degrees is "already there" as far as the short
            // path is concerned, so the servo keeps winding it the same direction for ever.
            // Measured mid-fight at -4255 degrees on an elbow whose limit is 92.
            //
            // The raw error is what unwinds it: at 1731 against a target of -38 it reads
            // -1769 and drives the joint back down through every turn until it arrives. The
            // original code had exactly this and blew up only because nothing bounded the
            // speed it asked for - 1769 x gain 60 is 106,000 deg/s.
            float error = targetAngle - joint.jointAngle;

            JointMotor2D motor = joint.motor;
            motor.motorSpeed = Mathf.Clamp(error * _servoGain, -_maxMotorSpeed, _maxMotorSpeed);
            motor.maxMotorTorque = maxTorque;
            joint.motor = motor;
        }
    }
}
