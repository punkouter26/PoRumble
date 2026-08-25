using NUnit.Framework;
using PoRumble.Models;
using UnityEngine;

namespace PoRumble.Tests
{
    /// <summary>
    /// Guards the tuning itself rather than the logic.
    ///
    /// The arm has a fixed reach, so a punch only lands inside a narrow band of body distances:
    /// too far and the glove falls short, too close and it overshoots the head entirely. If
    /// CloseRangeThreshold sits outside that band, one of the two damage tiers becomes
    /// unreachable and the long/close scoring inherited from Boxing (1980) silently dies.
    /// </summary>
    public sealed class BoxerConfigTuningTests
    {
        private BoxerConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<BoxerConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
        }

        /// <summary>
        /// Head-on punch at body distance <paramref name="distance"/>, attacker below facing up.
        /// Mirrors BoxerSystem.GetGlovePosition for a fully extended left arm.
        /// </summary>
        private HitResult PunchAtDistance(float distance)
        {
            // Read off the config rather than hard-coded: the offset had drifted to less than
            // half the configured shoulder width, so the test no longer described the geometry
            // it claims to mirror.
            float lateral = _config.ArmLateralOffset;

            Vector2 attackerPosition = Vector2.zero;
            Vector2 glovePosition = new(-lateral, _config.ArmReach);
            Vector2 targetPosition = new(-lateral, distance);

            return CombatMath.ResolveHit(
                0, attackerPosition,
                1, targetPosition, Vector2.down, true,
                glovePosition, _config.ToCombatSettings());
        }

        [Test]
        public void BothDamageTiersAreReachable()
        {
            bool sawLong = false;
            bool sawClose = false;

            // Sweep every plausible separation in 1cm steps.
            for (int step = 0; step <= 400; step++)
            {
                HitResult result = PunchAtDistance(step * 0.01f);

                if (!result.IsHit)
                {
                    continue;
                }

                if (result.IsCloseRange)
                {
                    sawClose = true;
                }
                else
                {
                    sawLong = true;
                }
            }

            Assert.That(sawLong, Is.True,
                "no separation produces a long punch - CloseRangeThreshold is above the landing band");
            Assert.That(sawClose, Is.True,
                "no separation produces a close punch - CloseRangeThreshold is below the landing band");
        }

        /// <summary>
        /// Bodies must not be able to push closer than the nearest range at which a punch can
        /// still reach a face. Below that, two fighters walking into each other reach a
        /// separation where neither can land anything, and the exchange deadlocks - a clinch
        /// nobody can break, which the approach shaping actively drives them into.
        /// </summary>
        [Test]
        public void BodiesCannotClinchInsideThePunchingRange()
        {
            float nearest = float.MaxValue;

            for (int step = 0; step <= 400; step++)
            {
                float distance = step * 0.01f;

                if (PunchAtDistance(distance).IsHit)
                {
                    nearest = Mathf.Min(nearest, distance);
                }
            }

            Assert.That(nearest, Is.LessThan(float.MaxValue), "a punch must land at some separation");

            float minimumSeparation = _config.BodyRadius * 2f;

            Assert.That(minimumSeparation, Is.GreaterThanOrEqualTo(nearest),
                $"bodies separate at {minimumSeparation} but a punch only reaches from " +
                $"{nearest} - everything between is a dead zone where neither boxer can land");
        }

        [Test]
        public void CloseRangeThresholdSitsInsideTheLandingBand()
        {
            float nearest = float.MaxValue;
            float farthest = float.MinValue;

            for (int step = 0; step <= 400; step++)
            {
                float distance = step * 0.01f;

                if (!PunchAtDistance(distance).IsHit)
                {
                    continue;
                }

                nearest = Mathf.Min(nearest, distance);
                farthest = Mathf.Max(farthest, distance);
            }

            Assert.That(nearest, Is.LessThan(farthest), "a punch must land at some separation");
            Assert.That(_config.CloseRangeThreshold, Is.GreaterThan(nearest).And.LessThan(farthest),
                $"threshold {_config.CloseRangeThreshold} must fall inside the landing band [{nearest}, {farthest}]");
        }
    }
}
