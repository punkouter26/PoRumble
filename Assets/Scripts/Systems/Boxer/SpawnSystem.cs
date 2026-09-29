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

        /// <summary>
        /// The slide is never more than this fraction of a slot. It is exactly the 12 degrees
        /// above at ten seats, so every scene up to ten opens as it always has; past that the
        /// slots narrow and a fixed 12 degrees would slide neighbours onto each other.
        /// </summary>
        private const float MAX_SLOT_JITTER_FRACTION = 1f / 3f;

        /// <summary>How far in or out of the spawn circle a fighter can start, as a fraction.</summary>
        private const float RADIUS_JITTER = 0.18f;

        /// <summary>
        /// Below this many body widths of arc per seat, one circle is too crowded to open on:
        /// twenty fighters on a circle that fits in the ring have under one and a half each, and
        /// four in five openings put two of them inside each other. The seats then alternate
        /// between the spawn circle and an inner one. Ten seats have just over two widths, so
        /// every training scene stays on a single circle.
        /// </summary>
        private const float SINGLE_CIRCLE_MIN_WIDTHS = 1.75f;

        /// <summary>Radius of the inner circle when staggered, as a fraction of the outer.</summary>
        private const float INNER_CIRCLE_FRACTION = 0.65f;

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
            float slotWidthDegrees = 360f / Mathf.Max(1, boxerCount);
            float slotDegrees = boxerIndex * slotWidthDegrees;
            float jitterDegrees = Mathf.Min(SLOT_JITTER_DEGREES, slotWidthDegrees * MAX_SLOT_JITTER_FRACTION);
            float degrees = slotDegrees + ringRotationDegrees + NextSigned() * jitterDegrees;

            float arcPerSeat = 2f * Mathf.PI * spawnRadius / Mathf.Max(1, boxerCount);
            bool staggered = arcPerSeat < SINGLE_CIRCLE_MIN_WIDTHS * _config.BodyRadius * 2f;

            // Staggered, the two circles are close enough that the full radial jitter would
            // carry an inner fighter out into the outer ring, so each gets half of it.
            float circle = staggered && boxerIndex % 2 == 1 ? spawnRadius * INNER_CIRCLE_FRACTION : spawnRadius;
            float radiusJitter = staggered ? RADIUS_JITTER * 0.5f : RADIUS_JITTER;
            float radius = circle * (1f + NextSigned() * radiusJitter);
            float radians = degrees * Mathf.Deg2Rad;

            position = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius;

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
