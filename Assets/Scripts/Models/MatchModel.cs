using System.Collections.Generic;
using UnityEngine;

namespace PoRumble.Models
{
    public enum MatchPhase
    {
        InProgress = 0,
        Ended = 1
    }

    /// <summary>Match-wide state: the roster and the outcome.</summary>
    public sealed class MatchModel
    {
        /// <summary>Winner id, or NO_WINNER for a draw.</summary>
        public const int NO_WINNER = -1;

        private readonly List<BoxerModel> _boxers = new();

        public IReadOnlyList<BoxerModel> Boxers => _boxers;

        /// <summary>Half width/height of the ring interior. Boxers are clamped inside it.</summary>
        public Vector2 ArenaHalfExtent { get; set; } = new(20f, 20f);
        public ReactiveProperty<MatchPhase> Phase { get; } = new(MatchPhase.InProgress);
        public int WinnerId { get; private set; } = NO_WINNER;

        public void AddBoxer(BoxerModel boxer)
        {
            _boxers.Add(boxer);
        }

        public int CountAlive()
        {
            int aliveCount = 0;

            for (int boxerIndex = 0; boxerIndex < _boxers.Count; boxerIndex++)
            {
                if (_boxers[boxerIndex].IsAlive.Value)
                {
                    aliveCount++;
                }
            }

            return aliveCount;
        }

        /// <summary>Highest-health survivor, or NO_WINNER when none or tied.</summary>
        public int ResolveLeaderId()
        {
            int leaderId = NO_WINNER;
            int bestHealth = int.MinValue;
            bool tied = false;

            for (int boxerIndex = 0; boxerIndex < _boxers.Count; boxerIndex++)
            {
                BoxerModel boxer = _boxers[boxerIndex];

                if (!boxer.IsAlive.Value)
                {
                    continue;
                }

                if (boxer.Health.Value > bestHealth)
                {
                    bestHealth = boxer.Health.Value;
                    leaderId = boxer.Id;
                    tied = false;
                }
                else if (boxer.Health.Value == bestHealth)
                {
                    tied = true;
                }
            }

            return tied ? NO_WINNER : leaderId;
        }

        /// <summary>Reopens the match so a new training episode can run in the same arena.</summary>
        public void BeginNewEpisode()
        {
            WinnerId = NO_WINNER;
            Phase.Value = MatchPhase.InProgress;
        }

        public void End(int winnerId)
        {
            WinnerId = winnerId;
            Phase.Value = MatchPhase.Ended;
        }
    }
}
