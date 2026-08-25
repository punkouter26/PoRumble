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

            // Training has no intro: the fight is live from the first step.
            if (IsTraining)
            {
                _flow.Phase.Value = MatchFlowPhase.Fighting;
            }
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
        /// </summary>
        private void StartNextEpisode()
        {
            IReadOnlyList<BoxerAgentView> agents = _spawnPoints.Agents;

            for (int agentIndex = 0; agentIndex < agents.Count; agentIndex++)
            {
                agents[agentIndex].AwardMatchResult(_match.WinnerId);
            }

            _spawnSystem.ResetRoster(_spawnPoints.BoxerCount, _spawnPoints.SpawnRadius);
            _match.BeginNewEpisode();

            for (int agentIndex = 0; agentIndex < agents.Count; agentIndex++)
            {
                agents[agentIndex].EndEpisode();
            }
        }

        /// <summary>
        /// Time.timeScale is global and outlives Play mode. Leaving the scene during a
        /// knockout hold would otherwise leave the whole Editor running at quarter speed.
        /// </summary>
        public void Dispose()
        {
            _flowSystem.ResetTimeScale();
        }
    }
}
