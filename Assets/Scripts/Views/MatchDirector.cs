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
    /// </summary>
    public sealed class MatchDirector : IStartable, IFixedTickable
    {
        private readonly SpawnSystem _spawnSystem;
        private readonly BoxerSystem _boxerSystem;
        private readonly MatchModel _match;
        private readonly MatchSystem _matchSystem;
        private readonly BoxerConfig _config;
        private readonly BoxerSpawnPoints _spawnPoints;

        [Inject]
        public MatchDirector(
            SpawnSystem spawnSystem,
            BoxerSystem boxerSystem,
            MatchModel match,
            MatchSystem matchSystem,
            BoxerConfig config,
            BoxerSpawnPoints spawnPoints)
        {
            _spawnSystem = spawnSystem;
            _boxerSystem = boxerSystem;
            _match = match;
            _matchSystem = matchSystem;
            _config = config;
            _spawnPoints = spawnPoints;
        }

        public void Start()
        {
            _match.ArenaHalfExtent = _spawnPoints.ArenaHalfExtent;
            _spawnSystem.SpawnRoster(_spawnPoints.BoxerCount, _spawnPoints.SpawnRadius);
            _spawnPoints.BuildViews(_match.Boxers);
        }

        public void FixedTick()
        {
            if (_match.Phase.Value == MatchPhase.Ended)
            {
                return;
            }

            _boxerSystem.Tick(Time.fixedDeltaTime);

            // Resolved after the tick so simultaneous knockouts both count.
            _matchSystem.EvaluateMatchState();

            if (_match.Phase.Value == MatchPhase.Ended && _spawnPoints.AutoRestart)
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
    }
}
