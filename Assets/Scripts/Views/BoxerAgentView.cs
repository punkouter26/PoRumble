using System;
using System.Collections.Generic;
using MessagePipe;
using PoRumble.Models;
using PoRumble.Systems;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
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
        [SerializeField] private float _damageDealtReward = 0.20f;
        [SerializeField] private float _damageTakenPenalty = 0.02f;
        [SerializeField] private float _eliminationReward = 0.5f;
        [SerializeField] private float _eliminatedPenalty = 1.0f;

        [Header("Dense shaping")]
        [Tooltip("Reward per step for pointing at the nearest opponent. Without this the agent " +
                 "must stumble onto move+aim+punch at once, and the terminal reward is far too " +
                 "sparse to teach any of them.")]
        [SerializeField] private float _aimShapingWeight = 0.6f;

        [Tooltip("Reward per step for closing on the nearest opponent. Scored across the whole " +
                 "ring, so there is a gradient to follow from anywhere.")]
        [SerializeField] private float _approachShapingWeight = 0.25f;

        [Tooltip("Extra reward per step for sitting at the distance a punch can actually land.")]
        [SerializeField] private float _rangeShapingWeight = 0.4f;

        [Tooltip("Penalty per punch thrown, so flailing is not free.")]
        [SerializeField] private float _punchCost = 0.002f;

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

        /// <summary>Hands this boxer to the keyboard. Inference only, never during training.</summary>
        public void SetHumanControlled(bool humanControlled)
        {
            _humanControlled = humanControlled;

            if (!humanControlled)
            {
                return;
            }

            // Force heuristic, otherwise the trained policy would drive the boxer the player
            // is supposed to be controlling.
            if (TryGetComponent(out BehaviorParameters parameters))
            {
                parameters.BehaviorType = BehaviorType.HeuristicOnly;
            }
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
                sensor.AddObservation(0f);                  // stamina
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
            sensor.AddObservation(_model.Stamina.Value);
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

            // Charged only when a punch actually starts. OnActionReceived runs every step, so
            // billing the intent would cost several points per episode just for holding the
            // button down while the arms were on cooldown.
            if (discrete[0] == 1 && _boxerSystem.Punch(_boxerId, ArmSide.Left))
            {
                AddReward(-_punchCost);
            }

            if (discrete[1] == 1 && _boxerSystem.Punch(_boxerId, ArmSide.Right))
            {
                AddReward(-_punchCost);
            }

            // Existential penalty. The ring does not shrink, so without a standing cost for
            // doing nothing, agents reliably learn to run away and stall the match out.
            AddReward(-1f / Mathf.Max(1, MaxStep));

            ApplyShapingRewards();
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

        /// <summary>
        /// Rewards the intermediate skills — facing the opponent and holding punching range —
        /// so the policy has a gradient to climb before it ever lands a first hit.
        /// </summary>
        private void ApplyShapingRewards()
        {
            BoxerModel nearest = FindNearestLivingOpponent(out float distance);

            if (nearest == null)
            {
                return;
            }

            float steps = Mathf.Max(1, MaxStep);
            Vector2 toOpponent = nearest.Position - _model.Position;

            if (toOpponent.sqrMagnitude > Mathf.Epsilon)
            {
                // 1 when looking straight at them, -1 when looking away.
                float alignment = Vector2.Dot(_model.Facing.normalized, toOpponent.normalized);
                AddReward(_aimShapingWeight * alignment / steps);
            }

            float idealRange = _config.ArmReach + _config.HeadOffset;

            // Closing reward, scored over the whole ring so there is a gradient to follow from
            // across the arena. It stops paying once the boxer is already within punching
            // range: otherwise the cheapest way to farm it is to huddle against a wall with
            // everyone else, which is exactly what the policy learned to do.
            if (distance > idealRange)
            {
                float reach = _match.ArenaHalfExtent.magnitude * 2f;
                float closeness = 1f - Mathf.Clamp01(distance / Mathf.Max(0.01f, reach));
                AddReward(_approachShapingWeight * closeness / steps);
            }

            // Peaks at the separation where a fully extended punch reaches the head.
            float rangeError = Mathf.Abs(distance - idealRange);
            float rangeScore = Mathf.Clamp01(1f - rangeError / idealRange);
            AddReward(_rangeShapingWeight * rangeScore / steps);
        }

        private BoxerModel FindNearestLivingOpponent(out float distance)
        {
            BoxerModel nearest = null;
            float bestSqr = float.MaxValue;
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel other = boxers[boxerIndex];

                if (other.Id == _boxerId || !other.IsAlive.Value)
                {
                    continue;
                }

                float sqr = (other.Position - _model.Position).sqrMagnitude;

                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    nearest = other;
                }
            }

            distance = nearest == null ? 0f : Mathf.Sqrt(bestSqr);
            return nearest;
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
