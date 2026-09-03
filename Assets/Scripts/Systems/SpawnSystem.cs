using PoRumble.Models;
using UnityEngine;
using VContainer;

namespace PoRumble.Systems
{
    /// <summary>Builds the roster and places boxers around the ring.</summary>
    public sealed class SpawnSystem
    {
        private readonly MatchModel _match;
        private readonly BoxerConfig _config;

        /// <summary>
        /// Seeded per system rather than drawn from UnityEngine.Random, for the same reason
        /// the sparring brain is: a training run has to be reproducible, and a global
        /// generator makes every episode depend on whatever else happened to draw a number.
        /// </summary>
        private uint _randomState = 0x9E3779B9u;

        /// <summary>How far each fighter slides around the ring from its allotted slot.</summary>
        private const float SLOT_JITTER_DEGREES = 12f;

        /// <summary>How far in or out of the spawn circle a fighter can start, as a fraction.</summary>
        private const float RADIUS_JITTER = 0.18f;

        /// <summary>
        /// How far off dead-centre a fighter can be looking at the bell. Without this the
        /// opponent is always exactly ahead on step one and the policy never has to learn to
        /// find one.
        /// </summary>
        private const float FACING_JITTER_DEGREES = 35f;

        [Inject]
        public SpawnSystem(MatchModel match, BoxerConfig config)
        {
            _match = match;
            _config = config;
        }

        /// <summary>
        /// Spawns boxers around a circle of the given radius, each roughly facing the centre.
        /// Slots are evenly spaced before jitter, which is what guarantees the separation.
        /// </summary>
        public void SpawnRoster(int boxerCount, float spawnRadius)
        {
            float ringRotation = NextFloat() * 360f;

            for (int boxerIndex = 0; boxerIndex < boxerCount; boxerIndex++)
            {
                GetSpawnPose(
                    boxerIndex, boxerCount, spawnRadius, ringRotation,
                    out Vector2 position, out Vector2 facing);

                BoxerModel boxer = new(boxerIndex, _config.MaxHealth)
                {
                    Position = position,
                    Facing = facing
                };

                _match.AddBoxer(boxer);
            }
        }

        /// <summary>
        /// Returns the existing roster to full health at fresh spawn poses.
        ///
        /// Fresh, not identical: the whole ring is rotated and every fighter jittered again,
        /// so no two episodes open from the same position. Replaying one fixed opening for
        /// millions of steps teaches a policy that opening rather than the game.
        /// </summary>
        public void ResetRoster(int boxerCount, float spawnRadius)
        {
            float ringRotation = NextFloat() * 360f;

            for (int boxerIndex = 0; boxerIndex < _match.Boxers.Count; boxerIndex++)
            {
                GetSpawnPose(
                    boxerIndex, boxerCount, spawnRadius, ringRotation,
                    out Vector2 position, out Vector2 facing);

                _match.Boxers[boxerIndex].ResetTo(position, facing, _config.MaxHealth);
            }
        }

        private void GetSpawnPose(
            int boxerIndex,
            int boxerCount,
            float spawnRadius,
            float ringRotationDegrees,
            out Vector2 position,
            out Vector2 facing)
        {
            float slotDegrees = boxerIndex * (360f / Mathf.Max(1, boxerCount));
            float degrees = slotDegrees + ringRotationDegrees + NextSigned() * SLOT_JITTER_DEGREES;
            float radius = spawnRadius * (1f + NextSigned() * RADIUS_JITTER);
            float radians = degrees * Mathf.Deg2Rad;

            position = _match.ArenaCenter
                       + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius;

            // Inward, give or take. Built from the angle rather than by negating the offset,
            // so the result is already a unit vector.
            float facingRadians = (degrees + 180f + NextSigned() * FACING_JITTER_DEGREES) * Mathf.Deg2Rad;
            facing = new Vector2(Mathf.Cos(facingRadians), Mathf.Sin(facingRadians));
        }

        /// <summary>Deterministic xorshift32, in 0..1.</summary>
        private float NextFloat()
        {
            _randomState ^= _randomState << 13;
            _randomState ^= _randomState >> 17;
            _randomState ^= _randomState << 5;
            return (_randomState & 0xFFFFFF) / (float)0x1000000;
        }

        /// <summary>Deterministic xorshift32, in -1..1.</summary>
        private float NextSigned()
        {
            return NextFloat() * 2f - 1f;
        }
    }
}
