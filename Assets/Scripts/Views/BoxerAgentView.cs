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
    // No RequireComponent on BoxerView: the view lives on the boxer root while this agent
    // sits on the Torso, so requiring it here made Unity keep adding a second, unbound view.
    [DisallowMultipleComponent]
    public sealed class BoxerAgentView : Agent
    {
        [Header("Reward shaping")]
        [Tooltip("Per point of damage landed on an opponent's face. The face arc is the only " +
                 "way to score, so this is the core objective.")]
        [SerializeField] private float _damageDealtReward = 0.35f;
        [Tooltip("For slipping a punch that nearly landed.")]
        [SerializeField] private float _evadeReward = 0.03f;
        [Tooltip("For stopping a punch on the gloves. Less than slipping it: the punch still " +
                 "arrived, it just did not get through.")]
        [SerializeField] private float _blockReward = 0.015f;
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

        [Tooltip("Drive this boxer with the hand-written sparring brain instead of a policy. " +
                 "Requires BehaviorType HeuristicOnly, which SetScriptedBot enforces.")]
        [SerializeField] private bool _scriptedBot;

        private BoxerSystem _boxerSystem;
        private MatchModel _match;
        private MatchFlowModel _flow;
        private TouchInputModel _touch;
        private BoxerConfig _config;
        private BoxerModel _model;
        private ScriptedBoxerBrain _brain;
        private BrainProfile _profile;

        /// <summary>Physics ticks between decisions, read off the DecisionRequester.</summary>
        private int _decisionPeriod = 1;

        // Kept so the handlers can be put back on re-enable. Disabling an agent and enabling
        // it again used to drop every reward message permanently: OnDisable disposed the
        // subscriptions and nothing ever resubscribed, so the boxer went on fighting while
        // silently scoring nothing.
        private ISubscriber<PunchLandedMessage> _punchSubscriber;
        private ISubscriber<PunchEvadedMessage> _evadedSubscriber;
        private ISubscriber<PunchBlockedMessage> _blockedSubscriber;
        private ISubscriber<BoxerEliminatedMessage> _eliminatedSubscriber;

        private IDisposable _punchSubscription;
        private IDisposable _evadedSubscription;
        private IDisposable _blockedSubscription;
        private IDisposable _eliminatedSubscription;

        private int _boxerId = -1;

        [Inject]
        public void Construct(
            BoxerSystem boxerSystem,
            MatchModel match,
            MatchFlowModel flow,
            TouchInputModel touch,
            BoxerConfig config,
            ISubscriber<PunchLandedMessage> punchSubscriber,
            ISubscriber<PunchEvadedMessage> evadedSubscriber,
            ISubscriber<PunchBlockedMessage> blockedSubscriber,
            ISubscriber<BoxerEliminatedMessage> eliminatedSubscriber)
        {
            _boxerSystem = boxerSystem;
            _match = match;
            _flow = flow;
            _touch = touch;
            _config = config;
            _brain = BuildBrain();
            _punchSubscriber = punchSubscriber;
            _evadedSubscriber = evadedSubscriber;
            _blockedSubscriber = blockedSubscriber;
            _eliminatedSubscriber = eliminatedSubscriber;

            // Injection happens after the object is already enabled, so OnEnable cannot be
            // the only place this runs.
            SubscribeToCombat();
        }

        /// <summary>
        /// Attaches the reward handlers. Idempotent, and a no-op before injection has
        /// supplied the subscribers.
        /// </summary>
        private void SubscribeToCombat()
        {
            if (_punchSubscriber == null || _punchSubscription != null)
            {
                return;
            }

            _punchSubscription = _punchSubscriber.Subscribe(OnPunchLanded);
            _evadedSubscription = _evadedSubscriber.Subscribe(OnPunchEvaded);
            _blockedSubscription = _blockedSubscriber.Subscribe(OnPunchBlocked);
            _eliminatedSubscription = _eliminatedSubscriber.Subscribe(OnBoxerEliminated);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            SubscribeToCombat();
        }

        /// <summary>
        /// Sets the difficulty tier this boxer fights at. Null keeps the brain's original
        /// built-in tuning, which is what the training scenes expect.
        /// </summary>
        public void SetBrainProfile(BrainProfile profile)
        {
            _profile = profile;

            // Only rebuildable once the config has been injected; otherwise the brain is
            // built on first use instead.
            if (_config != null)
            {
                _brain = BuildBrain();
            }
        }

        /// <summary>
        /// Builds the sparring brain, seeded per boxer so two bots on the same tier do not
        /// make identical decisions on the same tick.
        /// </summary>
        private ScriptedBoxerBrain BuildBrain()
        {
            BrainSettings settings = _profile != null ? _profile.ToSettings() : BrainSettings.Default;
            return new ScriptedBoxerBrain(_config, settings, _boxerId + 1);
        }

        /// <summary>
        /// Polls the haymaker key every frame for the human boxer.
        ///
        /// Read here rather than in Heuristic because Heuristic only runs on decision steps -
        /// once every DecisionPeriod physics ticks - and a release latency that coarse makes
        /// the charge feel like it fires late.
        /// </summary>
        private void Update()
        {
            if (!_humanControlled || _boxerSystem == null || _boxerId < 0)
            {
                return;
            }

            bool live = _flow == null || _flow.IsFightLive;
            bool charging = false;

            if (_touch != null && _touch.IsActive)
            {
                charging = _touch.ChargeHeld;
            }

            Keyboard keyboard = Keyboard.current;

            if (keyboard != null)
            {
                charging |= keyboard.spaceKey.isPressed;
            }

            _boxerSystem.SetCharge(_boxerId, live && charging);
        }

        /// <summary>
        /// Turns this boxer into the hand-written sparring partner. Forces heuristic control,
        /// since a policy would otherwise override the script.
        /// </summary>
        public void SetScriptedBot(bool scripted)
        {
            _scriptedBot = scripted;

            if (!scripted)
            {
                return;
            }

            if (TryGetComponent(out BehaviorParameters parameters))
            {
                parameters.BehaviorType = BehaviorType.HeuristicOnly;
            }
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

            // Heuristic runs once per decision period, not once per physics tick, so the
            // brain needs to know the interval it is really deciding over.
            if (TryGetComponent(out DecisionRequester requester))
            {
                _decisionPeriod = Mathf.Max(1, requester.DecisionPeriod);
            }

            // Rebuilt now that the id is known: the brain is seeded from it, so two bots on
            // the same tier do not make identical decisions on the same tick.
            if (_config != null)
            {
                _brain = BuildBrain();
            }
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            // Opponents and walls are perceived by RayPerceptionSensorComponent2D, which handles
            // the roster shrinking from nine opponents to zero. Only self-state goes here.
            //
            // Fifteen floats. Changing this count means changing VectorObservationSize on the
            // prefab to match and retraining: a policy compiled against the old width will not
            // load at all.
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
                sensor.AddObservation(Vector2.zero);        // position in ring
                sensor.AddObservation(Vector2.zero);        // velocity
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

            // Where in the ring, as a fraction of the way to each wall. The rays report a wall
            // as a distance in some direction; they do not say which corner the boxer is in,
            // and being cornered is the single most important positional fact in boxing.
            Vector2 half = _match.ArenaHalfExtent;
            sensor.AddObservation(new Vector2(
                _model.Position.x / Mathf.Max(0.01f, half.x),
                _model.Position.y / Mathf.Max(0.01f, half.y)));

            // Momentum. Movement accelerates and coasts, so intent and actual travel come
            // apart; without this the policy cannot tell that it is still sliding into a
            // punch it meant to step away from.
            sensor.AddObservation(_model.Velocity / Mathf.Max(0.01f, _config.MoveSpeed));
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            if (_model == null || !_model.IsAlive.Value)
            {
                return;
            }

            // Nobody throws before the bell. Without this, a punch started during the
            // countdown sits half-extended until the fight starts and then lands for free.
            if (_flow != null && !_flow.IsFightLive)
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

            if (_scriptedBot)
            {
                _brain ??= BuildBrain();

                // Heuristic runs once per decision period, so that is the interval the brain
                // is actually deciding over - not a single physics tick.
                float decisionDelta = Time.fixedDeltaTime * _decisionPeriod;
                BoxerIntent intent = _brain.Decide(_match, _boxerId, decisionDelta);

                continuous[0] = intent.Move.x;
                continuous[1] = intent.Move.y;
                continuous[2] = intent.Aim.x;
                continuous[3] = intent.Aim.y;
                discrete[0] = intent.PunchLeft ? 1 : 0;
                discrete[1] = intent.PunchRight ? 1 : 0;

                // Charging rides its own channel rather than a third action branch, so the
                // trained policy's action vector is left untouched.
                _boxerSystem.SetCharge(_boxerId, intent.Charge);
                return;
            }

            if (!_humanControlled)
            {
                return;
            }

            // The on-screen stick and the keyboard feed the same four continuous actions, so
            // a phone and a desk drive the boxer through identical code.
            float horizontal = 0f;
            float vertical = 0f;
            bool punch = false;

            if (_touch != null && _touch.IsActive)
            {
                horizontal = _touch.Move.x;
                vertical = _touch.Move.y;
                punch = _touch.PunchHeld;
            }

            Keyboard keyboard = Keyboard.current;

            if (keyboard != null)
            {
                horizontal += (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                vertical += (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                punch |= keyboard.jKey.isPressed || keyboard.kKey.isPressed;
            }

            horizontal = Mathf.Clamp(horizontal, -1f, 1f);
            vertical = Mathf.Clamp(vertical, -1f, 1f);

            continuous[0] = horizontal;
            continuous[1] = vertical;
            // Aim follows movement: there is no second stick, and a boxer that walks one way
            // while facing another cannot land anything through the face arc anyway.
            continuous[2] = horizontal;
            continuous[3] = vertical;

            // One request is enough. BoxerSystem falls through to whichever arm is free, and
            // only one fist may be out at a time, so asking for both would change nothing.
            discrete[0] = punch ? 1 : 0;
            discrete[1] = 0;
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
            // across the arena. It stops paying once inside punching range: otherwise the
            // cheapest way to farm it is to huddle against a wall with everyone else, which is
            // exactly what an earlier policy learned to do.
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

        /// <summary>Slipping a punch is worth something: defence, not just aggression.</summary>
        private void OnPunchEvaded(PunchEvadedMessage message)
        {
            if (message.EvaderId == _boxerId)
            {
                AddReward(_evadeReward);
            }
        }

        /// <summary>Keeping the guard up counts, just for less than slipping the punch.</summary>
        private void OnPunchBlocked(PunchBlockedMessage message)
        {
            if (message.BlockerId == _boxerId)
            {
                AddReward(_blockReward);
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
            _evadedSubscription?.Dispose();
            _blockedSubscription?.Dispose();
            _eliminatedSubscription?.Dispose();
            _punchSubscription = null;
            _evadedSubscription = null;
            _blockedSubscription = null;
            _eliminatedSubscription = null;
        }
    }
}
