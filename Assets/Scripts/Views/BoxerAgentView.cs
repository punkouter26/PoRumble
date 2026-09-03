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
using UnityEngine.Serialization;
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
        [Tooltip("Paid for taking a full health bar off one opponent, spread over the punches " +
                 "that did it. Denominated per knockout rather than per point of damage on " +
                 "purpose: a per-point figure silently rescales the whole reward function " +
                 "whenever Max Health moves, and at 30 HP the old 0.2/point made a single " +
                 "knockout worth 6.0 against a win bonus of 2. The objective was outscored " +
                 "three to one by its own scaffolding, which is why the policy preferred long " +
                 "matches: finishing one early truncates the damage it could still farm.")]
        [FormerlySerializedAs("_damageDealtReward")]
        [SerializeField] private float _knockoutDamageReward = 0.6f;
        [Tooltip("For slipping a punch that nearly landed.")]
        [SerializeField] private float _evadeReward = 0.03f;
        [Tooltip("For stopping a punch on the gloves. Less than slipping it: the punch still " +
                 "arrived, it just did not get through.")]
        [SerializeField] private float _blockReward = 0.015f;
        [Tooltip("Charged for losing a full health bar, on the same per-knockout scale as the " +
                 "damage reward. Deliberately below it: a fighter that valued its own health " +
                 "as highly as an opponent's would rather run out the clock than trade.")]
        [FormerlySerializedAs("_damageTakenPenalty")]
        [SerializeField] private float _knockoutDamagePenalty = 0.4f;
        [SerializeField] private float _eliminationReward = 1.5f;
        [SerializeField] private float _eliminatedPenalty = 1.0f;

        [Tooltip("Paid to the last fighter standing. With the damage terms denominated per " +
                 "knockout, a nine-kill sweep is worth about 19 from eliminations and 5 from " +
                 "damage, so this has to be large enough that the outcome - not the damage " +
                 "farmed on the way - is what the return is mostly made of.")]
        [SerializeField] private float _winReward = 6f;

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

        [Tooltip("Environment parameter that scales all three dense shaping terms, so a " +
                 "curriculum can fade the scaffolding out. It was written for a policy that " +
                 "could not see an opponent at all; once one can, paying per step for " +
                 "*standing* at punching range rewards hovering there without throwing " +
                 "anything. Defaults to 1 when no trainer is connected, so the game is " +
                 "unaffected.")]
        [SerializeField] private string _shapingScaleParameter = "shaping_scale";

        [Tooltip("Penalty per punch thrown, so flailing is not free.")]
        [SerializeField] private float _punchCost = 0.002f;

        [Tooltip("Penalty for running your own two fists into each other. Throwing both at " +
                 "once is no longer refused by BoxerSystem - the gloves converge on the " +
                 "centreline as they extend, so simultaneous punches physically clash and are " +
                 "both lost. The lost punch is most of the cost; this is the immediate signal " +
                 "that makes the lesson learnable in far fewer episodes than waiting for the " +
                 "damage that never arrived to show up in the return.")]
        [SerializeField] private float _clashPenalty = 0.05f;

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

        /// <summary>
        /// Bends this fighter's share of the shared policy into a style of its own. Null for
        /// a scripted bot, for the human, and throughout training - a run must learn against
        /// the unmodified network.
        /// </summary>
        private StyleModulator _modulator;

        /// <summary>
        /// The modulator's last answer, held between decisions.
        ///
        /// OnActionReceived fires on every physics step, not only on decision steps, so
        /// re-rolling the stochastic parts here would run them five times per decision and
        /// make every probability in a FighterStyle mean five times what it says.
        /// </summary>
        private BoxerIntent _modulatedIntent;

        /// <summary>Physics steps since binding, used to find the decision boundary.</summary>
        private int _actionStep;

        /// <summary>
        /// The behaviour type the prefab authored, so forcing heuristic control can be undone.
        /// A seat that held a scripted bot and now holds a policy fighter has to get its
        /// policy back; without this it would stay on HeuristicOnly and stand there.
        /// </summary>
        private BehaviorType _authoredBehaviorType = BehaviorType.Default;
        private bool _behaviorTypeCaptured;

        /// <summary>Physics ticks between decisions, read off the DecisionRequester.</summary>
        private int _decisionPeriod = 1;

        /// <summary>
        /// Current multiplier on the dense shaping terms, resolved once per episode from the
        /// trainer's environment parameters. Read at the episode boundary rather than per
        /// step: it cannot change mid-episode, and a dictionary lookup on every physics tick
        /// of every agent is pure waste.
        /// </summary>
        private float _shapingScale = 1f;

        // Kept so the handlers can be put back on re-enable. Disabling an agent and enabling
        // it again used to drop every reward message permanently: OnDisable disposed the
        // subscriptions and nothing ever resubscribed, so the boxer went on fighting while
        // silently scoring nothing.
        private ISubscriber<PunchLandedMessage> _punchSubscriber;
        private ISubscriber<PunchEvadedMessage> _evadedSubscriber;
        private ISubscriber<PunchBlockedMessage> _blockedSubscriber;
        private ISubscriber<PunchClashedMessage> _clashedSubscriber;
        private ISubscriber<BoxerEliminatedMessage> _eliminatedSubscriber;

        private IDisposable _punchSubscription;
        private IDisposable _evadedSubscription;
        private IDisposable _blockedSubscription;
        private IDisposable _clashedSubscription;
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
            ISubscriber<PunchClashedMessage> clashedSubscriber,
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
            _clashedSubscriber = clashedSubscriber;
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
            _clashedSubscription = _clashedSubscriber.Subscribe(OnPunchClashed);
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

            // The slip is polled here for the same reason the haymaker is: Heuristic runs
            // once every DecisionPeriod physics ticks, and a defensive input that coarse
            // arrives after the punch it was meant to avoid.
            bool slip = _touch != null && _touch.IsActive && _touch.DodgeRequested;

            // Consumed on read. The touch view raises the flag on press and never lowers it,
            // so leaving it set would slip again on every frame the thumb stayed down.
            if (_touch != null)
            {
                _touch.DodgeRequested = false;
            }

            if (keyboard != null)
            {
                slip |= keyboard.lKey.wasPressedThisFrame;
            }

            if (live && slip)
            {
                _boxerSystem.Dodge(_boxerId, Vector2.zero);
            }
        }

        /// <summary>
        /// Seats a contestant in this boxer: who drives it, how it fights and what it is
        /// physically made of.
        ///
        /// Idempotent and reversible, because the roster can be re-dealt between matches and
        /// the same chair may go from a scripted bot to a policy fighter and back.
        /// </summary>
        public void ApplyFighter(FighterProfile profile)
        {
            _modulator = null;

            if (profile == null)
            {
                return;
            }

            // A human seat keeps the keyboard whatever the profile says it is driven by.
            // The contestant still supplies the face, the colour and the attributes, so
            // "play as Biggie" gets Biggie's chin and Biggie's power.
            bool scripted = profile.Control == FighterControl.Scripted && !_humanControlled;

            _scriptedBot = scripted;
            SetBrainProfile(scripted ? profile.Brain : null);
            ApplyBehaviorType(scripted || _humanControlled);

            // Only a policy fighter needs a modulator. A scripted one already has a whole
            // brain of its own, tuned by its BrainProfile tier.
            if (!scripted && _config != null)
            {
                _modulator = new StyleModulator(_config, profile.ToStyle(), _boxerId + 1);
            }

            if (_model != null)
            {
                _model.Attributes = profile.ToAttributes();
            }
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

            ApplyBehaviorType(true);
        }

        /// <summary>
        /// Forces heuristic control, or puts back whatever the prefab authored.
        ///
        /// The authored value is captured on first use rather than in Awake, because an agent
        /// can be configured before it has ever been enabled.
        /// </summary>
        private void ApplyBehaviorType(bool heuristic)
        {
            if (!TryGetComponent(out BehaviorParameters parameters))
            {
                return;
            }

            if (!_behaviorTypeCaptured)
            {
                _authoredBehaviorType = parameters.BehaviorType;
                _behaviorTypeCaptured = true;
            }

            parameters.BehaviorType = heuristic ? BehaviorType.HeuristicOnly : _authoredBehaviorType;
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
            ApplyBehaviorType(true);
        }

        /// <summary>Called by the spawner once this agent's model exists.</summary>
        public void Bind(BoxerModel model)
        {
            _model = model;
            _boxerId = model.Id;
            _actionStep = 0;
            _modulatedIntent = BoxerIntent.Idle;

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

        /// <summary>
        /// Picks up the trainer's current shaping scale for the episode about to run.
        ///
        /// Read here rather than per step because it cannot change mid-episode, and because
        /// EnvironmentParameters is a dictionary lookup that would otherwise run on every
        /// physics tick of every agent. Falls back to 1 whenever no Academy is initialised or
        /// no trainer is connected, which is every case outside a training run - so the game
        /// keeps the shaping exactly as authored.
        /// </summary>
        public override void OnEpisodeBegin()
        {
            _shapingScale = Academy.IsInitialized
                ? Academy.Instance.EnvironmentParameters.GetWithDefault(_shapingScaleParameter, 1f)
                : 1f;
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            // Walls and the shape of the field are perceived by RayPerceptionSensorComponent2D,
            // which handles the roster shrinking from nine opponents to zero. Self-state, and
            // the few facts about the nearest opponent that a ray fan cannot express, go here.
            //
            // Twenty floats. Changing this count means changing VectorObservationSize on the
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
                sensor.AddObservation(0f);                  // stun
                sensor.AddObservation(Vector2.zero);        // bearing to nearest opponent
                sensor.AddObservation(0f);                  // range to nearest opponent
                sensor.AddObservation(0f);                  // incoming punch
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
            Vector2 local = _model.Position - _match.ArenaCenter;
            sensor.AddObservation(new Vector2(
                local.x / Mathf.Max(0.01f, half.x),
                local.y / Mathf.Max(0.01f, half.y)));

            // Momentum. Movement accelerates and coasts, so intent and actual travel come
            // apart; without this the policy cannot tell that it is still sliding into a
            // punch it meant to step away from.
            sensor.AddObservation(_model.Velocity / Mathf.Max(0.01f, _config.MoveSpeed));

            // Proprioception of trauma. A boxer knows when its own legs have gone, and the
            // fact changes which move is correct - press, or cover up - so it cannot be left
            // to be inferred from a health bar that says nothing about how fast it emptied.
            sensor.AddObservation(_boxerSystem.StunFraction(_model));

            ScanOpponents(out BoxerModel nearest, out float distance, out float threat);

            if (nearest == null)
            {
                sensor.AddObservation(Vector2.zero);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                return;
            }

            // Bearing to the nearest opponent, as the cosine and sine of the angle off the
            // facing. The rays already say something is out there, but only inside the
            // forward hemisphere they sweep and only at the resolution of seventeen samples.
            // This is exact, it is signed - so left and right are distinguishable rather than
            // merely off-centre - and it survives the opponent being directly behind, which
            // a 180-degree ray fan cannot represent at all.
            Vector2 facing = _model.Facing.normalized;
            Vector2 toOpponent = (nearest.Position - _model.Position).normalized;
            sensor.AddObservation(new Vector2(
                Vector2.Dot(facing, toOpponent),
                facing.x * toOpponent.y - facing.y * toOpponent.x));

            float reach = Mathf.Max(0.01f, _match.ArenaHalfExtent.magnitude * 2f);
            sensor.AddObservation(Mathf.Clamp01(distance / reach));

            // A punch already in flight and pointed this way. Without it the slip is
            // unusable to a policy: DodgeDuration is 0.3s against a 0.22s punch, so the
            // window only exists for something that can see an arm start to travel - and
            // nothing in the observation vector said an arm was travelling.
            sensor.AddObservation(threat);
        }

        /// <summary>
        /// One pass over the roster for the two things that need it: the nearest living
        /// opponent, and whether anybody has a punch on its way to this boxer.
        ///
        /// Combined because both run on every physics tick of every agent - ten fighters
        /// scanning ten models, fifty times a second - and walking the same list twice for
        /// the same data is exactly the waste that ends up on a profiler.
        /// </summary>
        private void ScanOpponents(out BoxerModel nearest, out float distance, out float threat)
        {
            nearest = null;
            threat = 0f;
            float bestSqr = float.MaxValue;
            float threatRange = _boxerSystem.DodgeThreatRange;
            float threatRangeSqr = threatRange * threatRange;
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel other = boxers[boxerIndex];

                if (other.Id == _boxerId || !other.IsAlive.Value)
                {
                    continue;
                }

                Vector2 offset = other.Position - _model.Position;
                float sqr = offset.sqrMagnitude;

                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    nearest = other;
                }

                if (sqr > threatRangeSqr)
                {
                    continue;
                }

                threat = Mathf.Max(threat, IncomingPunch(other, -offset));
            }

            distance = nearest == null ? 0f : Mathf.Sqrt(bestSqr);
        }

        /// <summary>
        /// How far through its travel an opponent's punch is, provided that punch is coming
        /// this way. Zero when neither arm is extending, or when the attacker is facing
        /// somewhere else entirely.
        ///
        /// Only the Extending phase counts. A retracting arm is a punch that has already
        /// resolved, and treating it as a threat would teach the policy to slip after the
        /// damage had been taken.
        /// </summary>
        private static float IncomingPunch(BoxerModel attacker, Vector2 towardSelf)
        {
            if (towardSelf.sqrMagnitude <= Mathf.Epsilon)
            {
                return 0f;
            }

            // A punch travels down the attacker's facing, so one aimed at somebody else is
            // not a threat however close it happens to land.
            if (Vector2.Dot(attacker.Facing.normalized, towardSelf.normalized) < 0.5f)
            {
                return 0f;
            }

            float extension = 0f;

            if (attacker.LeftArm.Phase == ArmPhase.Extending)
            {
                extension = attacker.LeftArm.Extension;
            }

            if (attacker.RightArm.Phase == ArmPhase.Extending)
            {
                extension = Mathf.Max(extension, attacker.RightArm.Extension);
            }

            return extension;
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
            var discrete = actions.DiscreteActions;

            BoxerIntent intent = new(
                new Vector2(Mathf.Clamp(continuous[0], -1f, 1f), Mathf.Clamp(continuous[1], -1f, 1f)),
                new Vector2(Mathf.Clamp(continuous[2], -1f, 1f), Mathf.Clamp(continuous[3], -1f, 1f)),
                discrete[0] == 1,
                discrete[1] == 1);

            if (_modulator != null)
            {
                // Re-rolled only on decision steps. OnActionReceived fires every physics tick
                // - the DecisionRequester repeats the last decision in between - so rolling
                // here would run every probability in a FighterStyle five times per decision.
                if (_actionStep % _decisionPeriod == 0)
                {
                    _modulatedIntent = _modulator.Modulate(
                        intent, _match, _boxerId, Time.fixedDeltaTime * _decisionPeriod);
                }

                intent = _modulatedIntent;
                _boxerSystem.SetCharge(_boxerId, intent.Charge);

                if (intent.Dodge)
                {
                    _boxerSystem.Dodge(_boxerId, Vector2.zero);
                }
            }

            _actionStep++;
            _boxerSystem.SetMoveInput(_boxerId, intent.Move);

            if (intent.Aim.sqrMagnitude > 0.01f)
            {
                _boxerSystem.SetAim(_boxerId, intent.Aim);
            }

            // Charged only when a punch actually starts. OnActionReceived runs every step, so
            // billing the intent would cost several points per episode just for holding the
            // button down while the arms were on cooldown.
            if (intent.PunchLeft && _boxerSystem.Punch(_boxerId, ArmSide.Left))
            {
                AddReward(-_punchCost);
            }

            if (intent.PunchRight && _boxerSystem.Punch(_boxerId, ArmSide.Right))
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

                // Same discipline as the human path below: a scripted brain asks for a punch
                // without knowing whether the other fist is still out, and would otherwise
                // clash its way through every round.
                bool armFree = !IsAnyArmTravelling();
                discrete[0] = intent.PunchLeft && armFree ? 1 : 0;
                discrete[1] = intent.PunchRight && armFree ? 1 : 0;

                // Charging and slipping ride their own channels rather than extra action
                // branches, so the trained policy's action vector is left untouched.
                _boxerSystem.SetCharge(_boxerId, intent.Charge);

                if (intent.Dodge)
                {
                    _boxerSystem.Dodge(_boxerId, Vector2.zero);
                }

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

            // One request is enough, and it is withheld while a fist is already travelling.
            //
            // BoxerSystem no longer refuses a second punch - the arms clash instead, which is
            // what a policy has to learn to avoid. A held button is not a policy: it would
            // reissue the request the instant the first arm came home and knock the fighter's
            // own fists together for the whole round. The discipline belongs here, in the
            // mapping from an input to an intent, not in the physics.
            discrete[0] = punch && !IsAnyArmTravelling() ? 1 : 0;
            discrete[1] = 0;
        }

        /// <summary>
        /// True while either fist is away from the guard.
        ///
        /// Used only by the hand-driven paths. The policy is deliberately free to throw into
        /// its own arm and be punished for it; a person holding a button is not making that
        /// choice and should not be charged for it.
        /// </summary>
        private bool IsAnyArmTravelling()
        {
            if (_model == null)
            {
                return false;
            }

            return _model.LeftArm.Phase == ArmPhase.Extending
                   || _model.LeftArm.Phase == ArmPhase.Retracting
                   || _model.RightArm.Phase == ArmPhase.Extending
                   || _model.RightArm.Phase == ArmPhase.Retracting;
        }

        /// <summary>
        /// Rewards the intermediate skills — facing the opponent and holding punching range —
        /// so the policy has a gradient to climb before it ever lands a first hit.
        /// </summary>
        private void ApplyShapingRewards()
        {
            // Zero once the curriculum has faded the scaffolding out, at which point none of
            // the work below is worth doing at all.
            if (_shapingScale <= 0f)
            {
                return;
            }

            ScanOpponents(out BoxerModel nearest, out float distance, out _);

            if (nearest == null)
            {
                return;
            }

            float steps = Mathf.Max(1, MaxStep) / _shapingScale;
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

        private void OnPunchLanded(PunchLandedMessage message)
        {
            // Per point of a health bar rather than per point of damage, so changing
            // MaxHealth retunes the fight without silently rescaling the reward function.
            float perPoint = 1f / Mathf.Max(1, _config.MaxHealth);

            if (message.AttackerId == _boxerId)
            {
                AddReward(_knockoutDamageReward * perPoint * message.Damage);
            }
            else if (message.TargetId == _boxerId)
            {
                AddReward(-_knockoutDamagePenalty * perPoint * message.Damage);
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

        /// <summary>Running your own fists together costs the punch, and a little besides.</summary>
        private void OnPunchClashed(PunchClashedMessage message)
        {
            if (message.BoxerId == _boxerId)
            {
                AddReward(-_clashPenalty);
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
                AddReward(_winReward);
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            _punchSubscription?.Dispose();
            _evadedSubscription?.Dispose();
            _blockedSubscription?.Dispose();
            _clashedSubscription?.Dispose();
            _eliminatedSubscription?.Dispose();
            _punchSubscription = null;
            _evadedSubscription = null;
            _blockedSubscription = null;
            _clashedSubscription = null;
            _eliminatedSubscription = null;
        }
    }
}
