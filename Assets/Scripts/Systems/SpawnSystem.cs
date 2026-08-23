using PoRumble.Models;
using UnityEngine;
using VContainer;

namespace PoRumble.Systems
{
    /// <summary>Builds the roster and places boxers evenly around the ring.</summary>
    public sealed class SpawnSystem
    {
        private readonly MatchModel _match;
        private readonly BoxerConfig _config;

        [Inject]
        public SpawnSystem(MatchModel match, BoxerConfig config)
        {
            _match = match;
            _config = config;
        }

        /// <summary>
        /// Spawns boxers on a circle of the given radius, each facing the centre.
        /// Even spacing guarantees the minimum separation the spec requires.
        /// </summary>
        public void SpawnRoster(int boxerCount, float spawnRadius)
        {
            for (int boxerIndex = 0; boxerIndex < boxerCount; boxerIndex++)
            {
                GetSpawnPose(boxerIndex, boxerCount, spawnRadius, out Vector2 position, out Vector2 facing);

                BoxerModel boxer = new(boxerIndex, _config.MaxHealth)
                {
                    Position = position,
                    Facing = facing
                };

                _match.AddBoxer(boxer);
            }
        }

        /// <summary>Returns the existing roster to full health at their spawn poses.</summary>
        public void ResetRoster(int boxerCount, float spawnRadius)
        {
            for (int boxerIndex = 0; boxerIndex < _match.Boxers.Count; boxerIndex++)
            {
                GetSpawnPose(boxerIndex, boxerCount, spawnRadius, out Vector2 position, out Vector2 facing);
                _match.Boxers[boxerIndex].ResetTo(position, facing, _config.MaxHealth);
            }
        }

        private static void GetSpawnPose(
            int boxerIndex, int boxerCount, float spawnRadius, out Vector2 position, out Vector2 facing)
        {
            float angleRadians = boxerIndex * (2f * Mathf.PI / Mathf.Max(1, boxerCount));
            Vector2 offset = new(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians));

            position = offset * spawnRadius;
            facing = (-offset).normalized;
        }
    }
}
