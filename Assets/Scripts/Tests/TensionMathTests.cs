using NUnit.Framework;
using PoRumble.Models;
using UnityEngine;

namespace PoRumble.Tests
{
    /// <summary>
    /// Pins the camera director's ranking rule.
    ///
    /// These are all comparisons rather than absolute values, deliberately. The weights are
    /// tuning and are expected to move; what must not move is the ordering, because the
    /// ordering is the entire behaviour a viewer sees - the camera goes to the higher score,
    /// and a change that quietly inverts one of these comparisons is a camera that starts
    /// watching the wrong half of the ring.
    /// </summary>
    public sealed class TensionMathTests
    {
        private const int MAX_HEALTH = 30;
        private const float ENGAGED = 2.5f;

        private static PairTension Pair(
            float separation,
            int healthA = MAX_HEALTH,
            int healthB = MAX_HEALTH,
            float chargeA = 0f,
            float chargeB = 0f,
            float recentDamage = 0f,
            bool threatened = false)
        {
            return new PairTension(
                Vector2.zero,
                new Vector2(separation, 0f),
                healthA,
                healthB,
                chargeA,
                chargeB,
                MAX_HEALTH,
                ENGAGED,
                recentDamage,
                threatened);
        }

        [Test]
        public void FightersAtPunchingRangeOutscoreFightersAcrossTheRing()
        {
            float close = TensionMath.ScorePair(Pair(ENGAGED));
            float far = TensionMath.ScorePair(Pair(ENGAGED * 6f));

            Assert.That(close, Is.GreaterThan(far));
        }

        /// <summary>
        /// The failure the whole proximity scaling exists to prevent. Framing the most hurt
        /// fighter in the ring and whoever happens to be nearest them is what a health-only
        /// camera does, and it puts the ring between two people who are not fighting.
        /// </summary>
        [Test]
        public void ADyingFighterAloneAcrossTheRingLosesToAHealthyPairInClose()
        {
            float lonelyAndDying = TensionMath.ScorePair(Pair(ENGAGED * 8f, healthA: 1, healthB: 1));
            float healthyAndClose = TensionMath.ScorePair(Pair(ENGAGED));

            Assert.That(healthyAndClose, Is.GreaterThan(lonelyAndDying));
        }

        [Test]
        public void AHurtPairOutscoresAFreshPairAtTheSameRange()
        {
            float hurt = TensionMath.ScorePair(Pair(ENGAGED, healthA: 3));
            float fresh = TensionMath.ScorePair(Pair(ENGAGED));

            Assert.That(hurt, Is.GreaterThan(fresh));
        }

        [Test]
        public void ACockedHaymakerRaisesTheScore()
        {
            float cocked = TensionMath.ScorePair(Pair(ENGAGED, chargeA: 1f));
            float idle = TensionMath.ScorePair(Pair(ENGAGED));

            Assert.That(cocked, Is.GreaterThan(idle));
        }

        [Test]
        public void PunchesLandingRaiseTheScore()
        {
            float trading = TensionMath.ScorePair(Pair(ENGAGED, recentDamage: 10f));
            float circling = TensionMath.ScorePair(Pair(ENGAGED));

            Assert.That(trading, Is.GreaterThan(circling));
        }

        [Test]
        public void APunchAlreadyTravellingRaisesTheScore()
        {
            float incoming = TensionMath.ScorePair(Pair(ENGAGED, threatened: true));
            float quiet = TensionMath.ScorePair(Pair(ENGAGED));

            Assert.That(incoming, Is.GreaterThan(quiet));
        }

        /// <summary>
        /// The director compares scores against a fixed switch margin and against fixed shot
        /// thresholds, both of which assume a bounded range. A score that could exceed 1 would
        /// make the duel threshold unreachable in some matches and trivial in others.
        /// </summary>
        [Test]
        public void TheScoreStaysWithinZeroAndOneAtEveryExtreme()
        {
            float everything = TensionMath.ScorePair(Pair(
                0f, healthA: 1, healthB: 1, chargeA: 1f, chargeB: 1f,
                recentDamage: 999f, threatened: true));
            float nothing = TensionMath.ScorePair(Pair(1000f));

            Assert.That(everything, Is.InRange(0f, 1f));
            Assert.That(nothing, Is.InRange(0f, 1f));
        }
    }
}
