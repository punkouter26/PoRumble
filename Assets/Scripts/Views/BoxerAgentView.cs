using System;
using MessagePipe;
using PoRumble.Models;
using PoRumble.Systems;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// The single control path for a boxer. An ML-Agents policy drives it through
    /// OnActionReceived; a human drives the exact same code through Heuristic.
    ///
    /// This is a View: it translates actions into system calls and observations out of the
    /// model. It holds no combat logic.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxerView))]
    public sealed class BoxerAgentView : Agent
    {
        [Header("Reward shaping")]
        [SerializeField] private float _damageDealtReward = 0.05f;
        [SerializeField] private float _damageTakenPenalty = 0.02f;
        [SerializeField] private float _eliminationReward = 0.5f;
        [SerializeField] private float _eliminatedPenalty = 1.0f;

        [Tooltip("Keyboard control for this boxer instead of a policy. Inference only — " +
                 "never enable during a training run.")]
        [SerializeField] private bool _humanControlled;

        private BoxerSystem _boxerSystem;
        private MatchModel _match;
        private BoxerConfig _config;
        private BoxerModel _model;

        private IDisposable _punchSubscription;
        private IDisposable _eliminatedSubscription;

        private int _boxerId = -1;

        [Inject]
        public void Construct(
            BoxerSystem boxerSystem,
            MatchModel match,
            BoxerConfig config,
            ISubscriber<PunchLandedMessage> punchSubscriber,
            ISubscriber<BoxerEliminatedMessage> eliminatedSubscriber)
        {
            _boxerSystem = boxerSystem;
            _match = match;
            _config = config;
            _punchSubscription = punchSubscriber.Subscribe(OnPunchLanded);
            _eliminatedSubscription = eliminatedSubscriber.Subscribe(OnBoxerEliminated);
        }

        /// <summary>Called by the spawner once this agent's model exists.</summary>
        public void Bind(BoxerModel model)
        {
            _model = model;
            _boxerId = model.Id;
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            // Opponents and walls are perceived by RayPerceptionSensorComponent2D, which handles
            // the roster shrinking from nine opponents to zero. Only self-state goes here.
            if (_model == null)
            {
                sensor.AddObservation(0f);                  // health
                sensor.AddObservation(Vector2.zero);        // facing
                sensor.AddObservation(Vector2.zero);        // move input
                sensor.AddObservation(0f);                  // left arm extension
                sensor.AddObservation(0f);                  // left arm ready
                sensor.AddObservation(0f);                  // right arm extension
                sensor.AddObservation(0f);                  // right arm ready
                sensor.AddObservation(0f);                  // boxers remaining
                return;
            }

            sensor.AddObservation(_model.Health.Value / (float)Mathf.Max(1, _config.MaxHealth));
            sensor.AddObservation(_model.Facing);
            sensor.AddObservation(_model.MoveInput);
            sensor.AddObservation(_model.LeftArm.Extension);
            sensor.AddObservation(_model.LeftArm.CanPunch ? 1f : 0f);
            sensor.AddObservation(_model.RightArm.Extension);
            sensor.AddObservation(_model.RightArm.CanPunch ? 1f : 0f);
            sensor.AddObservation(_match.CountAlive() / (float)Mathf.Max(1, _match.Boxers.Count));
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            if (_model == null || !_model.IsAlive.Value)
            {
                return;
            }

            var continuous = actions.ContinuousActions;
            Vector2 move = new(Mathf.Clamp(continuous[0], -1f, 1f), Mathf.Clamp(continuous[1], -1f, 1f));
            Vector2 aim = new(Mathf.Clamp(continuous[2], -1f, 1f), Mathf.Clamp(continuous[3], -1f, 1f));

            _boxerSystem.SetMoveInput(_boxerId, move);

            if (aim.sqrMagnitude > 0.01f)
            {
                _boxerSystem.SetAim(_boxerId, aim);
            }

            var discrete = actions.DiscreteActions;

            if (discrete[0] == 1)
            {
                _boxerSystem.Punch(_boxerId, ArmSide.Left);
            }

            if (discrete[1] == 1)
            {
                _boxerSystem.Punch(_boxerId, ArmSide.Right);
            }

            // Existential penalty. The ring does not shrink, so without a standing cost for
            // doing nothing, agents reliably learn to run away and stall the match out.
            AddReward(-1f / Mathf.Max(1, MaxStep));
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var continuous = actionsOut.ContinuousActions;
            var discrete = actionsOut.DiscreteActions;

            continuous[0] = 0f;
            continuous[1] = 0f;
            continuous[2] = 0f;
            continuous[3] = 0f;
            discrete[0] = 0;
            discrete[1] = 0;

            if (!_humanControlled)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
            {
                return;
            }

            float horizontal = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float vertical = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);

            continuous[0] = horizontal;
            continuous[1] = vertical;
            continuous[2] = horizontal;
            continuous[3] = vertical;

            discrete[0] = keyboard.jKey.isPressed ? 1 : 0;
            discrete[1] = keyboard.kKey.isPressed ? 1 : 0;
        }

        private void OnPunchLanded(PunchLandedMessage message)
        {
            if (message.AttackerId == _boxerId)
            {
                AddReward(_damageDealtReward * message.Damage);
            }
            else if (message.TargetId == _boxerId)
            {
                AddReward(-_damageTakenPenalty * message.Damage);
            }
        }

        private void OnBoxerEliminated(BoxerEliminatedMessage message)
        {
            if (message.BoxerId == _boxerId)
            {
                AddReward(-_eliminatedPenalty);
            }
            else if (message.EliminatedById == _boxerId)
            {
                AddReward(_eliminationReward);
            }
        }

        /// <summary>Called by the arena when the match resolves, before episodes are ended.</summary>
        public void AwardMatchResult(int winnerId)
        {
            if (winnerId == _boxerId)
            {
                AddReward(2f);
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            _punchSubscription?.Dispose();
            _eliminatedSubscription?.Dispose();
            _punchSubscription = null;
            _eliminatedSubscription = null;
        }
    }
}
