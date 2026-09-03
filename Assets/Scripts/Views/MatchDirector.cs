using System;
using System.Collections.Generic;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace PoRumble.Views
{
    /// <summary>
    /// Drives the match: spawns the roster, binds views, ticks BoxerSystem on the physics
    /// clock, and — when training — resets the arena for the next episode.
    ///
    /// Two loops on purpose. A training scene auto-restarts the instant a match resolves, with
    /// no countdown and no knockout hold, because both would burn episode steps on animation.
    /// The game scene runs the presentation loop in <see cref="MatchFlowSystem"/> instead.
    /// </summary>
    public sealed class MatchDirector : IStartable, ITickable, IFixedTickable, IDisposable
    {
        private readonly SpawnSystem _spawnSystem;
        private readonly BoxerSystem _boxerSystem;
        private readonly MatchModel _match;
        private readonly MatchSystem _matchSystem;
        private readonly MatchFlowSystem _flowSystem;
        private readonly MatchFlowModel _flow;
        private readonly BoxerConfig _config;
        private readonly BoxerSpawnPoints _spawnPoints;

        /// <summary>
        /// Physics steps of margin between resolving a match on health and the point
        /// ML-Agents cuts the trajectory at MaxStep. EndByTimeout has to publish, the
        /// director has to notice, and the win has to be awarded, all before the cut.
        /// </summary>
        private const int TIMEOUT_MARGIN_STEPS = 5;

        private readonly CompositeDisposable _disposables = new();

        /// <summary>Physics steps the current match has run.</summary>
        private int _episodeSteps;

        /// <summary>
        /// Step count at which an unresolved match is decided on health instead - the bell.
        ///
        /// This applies to the game as much as to training. EvaluateMatchState only ends a
        /// match when one fighter is left standing, and the last two rarely oblige: measured
        /// against a trained policy, a ten-way reaches the cap with three alive on 7, 4 and
        /// 22 health, still circling. Without a bell that match has no ending at all.
        ///
        /// Worse, the better the policy gets the longer it lasts - fighters learn to survive
        /// - so raising the cap chases a moving target. A decision on health is the answer,
        /// which is what EndByTimeout was written for.
        ///
        /// Derived from the agents' own MaxStep so the two cannot drift apart. In training
        /// that also keeps the result inside the trajectory ML-Agents is about to close.
        /// </summary>
        private int _timeoutSteps = int.MaxValue;

        [Inject]
        public MatchDirector(
            SpawnSystem spawnSystem,
            BoxerSystem boxerSystem,
            MatchModel match,
            MatchSystem matchSystem,
            MatchFlowSystem flowSystem,
            MatchFlowModel flow,
            BoxerConfig config,
            BoxerSpawnPoints spawnPoints)
        {
            _spawnSystem = spawnSystem;
            _boxerSystem = boxerSystem;
            _match = match;
            _matchSystem = matchSystem;
            _flowSystem = flowSystem;
            _flow = flow;
            _config = config;
            _spawnPoints = spawnPoints;
        }

        /// <summary>True in a training scene, where the presentation loop is skipped.</summary>
        private bool IsTraining => _spawnPoints.AutoRestart;

        public void Start()
        {
            _match.ArenaHalfExtent = _spawnPoints.ArenaHalfExtent;
            _spawnSystem.SpawnRoster(_spawnPoints.BoxerCount, _spawnPoints.SpawnRadius);
            _spawnPoints.BuildViews(_match.Boxers);
            _flowSystem.Configure(_spawnPoints.BoxerCount, _spawnPoints.SpawnRadius);

            _timeoutSteps = ResolveTimeoutSteps();

            // Restarts the round clock on every fresh match, whichever path re-racked it.
            _match.Phase
                .Subscribe(OnMatchPhaseChanged)
                .AddTo(_disposables);

            // Training has no menu and no intro: the fight is live from the first step. The
            // game scene starts on the title instead, which is where the fight card lives and
            // the only place a phone can reach it.
            if (IsTraining)
            {
                _flow.Phase.Value = MatchFlowPhase.Fighting;
            }
        }

        /// <summary>
        /// Reads the episode cap off the agents themselves. Returns int.MaxValue when no cap
        /// is configured, which leaves the match running until it resolves on its own.
        /// </summary>
        private int ResolveTimeoutSteps()
        {
            IReadOnlyList<BoxerAgentView> agents = _spawnPoints.Agents;

            if (agents.Count == 0 || agents[0].MaxStep <= 0)
            {
                return int.MaxValue;
            }

            return Mathf.Max(1, agents[0].MaxStep - TIMEOUT_MARGIN_STEPS);
        }

        /// <summary>
        /// Advances the presentation loop on unscaled time. It has to be unscaled because the
        /// knockout hold slows the world down, and a hold timed on scaled time would stretch
        /// itself out by exactly the factor it just applied.
        /// </summary>
        public void Tick()
        {
            if (IsTraining)
            {
                return;
            }

            _flowSystem.Tick(Time.unscaledDeltaTime);
        }

        public void FixedTick()
        {
            if (_match.Phase.Value == MatchPhase.Ended)
            {
                return;
            }

            // Fighters stand still through the intro and countdown; the bell starts the fight.
            if (!IsTraining && !_flow.IsFightLive)
            {
                return;
            }

            _boxerSystem.Tick(Time.fixedDeltaTime);

            // Resolved after the tick so simultaneous knockouts both count.
            _matchSystem.EvaluateMatchState();

            _episodeSteps++;

            // Nobody knocked the last opponent out in time, so the bell decides it on health.
            // In the game this is the difference between a match that ends and one that runs
            // for ever; in training it also keeps the result inside the trajectory rather
            // than letting the episode lapse with no result in it at all.
            if (_match.Phase.Value != MatchPhase.Ended && _episodeSteps >= _timeoutSteps)
            {
                _matchSystem.EndByTimeout();
            }

            if (_match.Phase.Value == MatchPhase.Ended && IsTraining)
            {
                StartNextEpisode();
            }
        }

        /// <summary>
        /// Ends every agent's episode together and re-racks the fighters.
        ///
        /// Episodes are ended for all agents at once rather than per elimination: ML-Agents
        /// calls OnEpisodeBegin straight after EndEpisode, so ending one agent's episode
        /// mid-match would respawn a boxer that had just been knocked out.
        ///
        /// The order matters. EndEpisode collects each agent's final observation on the spot,
        /// so the roster has to still be standing in the state that ended the match — reset
        /// first and every trajectory closes on a snapshot of the *next* episode's opening
        /// instead. It is survivable while these are true terminals, because the trainer
        /// forces the terminal value to zero and never reads that observation; it stops being
        /// survivable the moment anything here reports an interrupted episode.
        /// </summary>
        private void StartNextEpisode()
        {
            IReadOnlyList<BoxerAgentView> agents = _spawnPoints.Agents;

            for (int agentIndex = 0; agentIndex < agents.Count; agentIndex++)
            {
                agents[agentIndex].AwardMatchResult(_match.WinnerId);
            }

            for (int agentIndex = 0; agentIndex < agents.Count; agentIndex++)
            {
                agents[agentIndex].EndEpisode();
            }

            _spawnSystem.ResetRoster(_spawnPoints.BoxerCount, _spawnPoints.SpawnRadius);
            _match.BeginNewEpisode();
            _episodeSteps = 0;
        }

        /// <summary>
        /// Restarts the round clock whenever a fresh match begins.
        ///
        /// The game re-racks through MatchFlowSystem.TryRestart rather than through
        /// StartNextEpisode, so without this the second match of a session would inherit a
        /// spent clock and hit the bell immediately.
        /// </summary>
        private void OnMatchPhaseChanged(MatchPhase phase)
        {
            if (phase == MatchPhase.InProgress)
            {
                _episodeSteps = 0;
            }
        }

        /// <summary>
        /// Time.timeScale is global and outlives Play mode. Leaving the scene during a
        /// knockout hold would otherwise leave the whole Editor running at quarter speed.
        /// </summary>
        public void Dispose()
        {
            _disposables.Dispose();
            _flowSystem.ResetTimeScale();
        }
    }
}
