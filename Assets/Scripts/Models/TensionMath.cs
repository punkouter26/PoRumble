using UnityEngine;

namespace PoRumble.Models
{
    /// <summary>
    /// Scores how worth watching a pair of fighters is right now.
    ///
    /// Pure and static for the same reason <see cref="CombatMath"/> and <see cref="ThreatMath"/>
    /// are: the camera director and its tests need the same answer to "where is the fight", and
    /// neither should have to agree on it separately. No Unity components, no physics query,
    /// no allocation.
    ///
    /// The terms are deliberately few. A camera that cuts on a score nobody can predict is
    /// indistinguishable from one that cuts at random, and the four things below are the four
    /// a person watching would actually name: they are close together, one of them is nearly
    /// out, someone has a haymaker cocked, and punches are landing.
    /// </summary>
    public static class TensionMath
    {
        /// <summary>
        /// Weight on how close the two are. Sits highest because it gates everything else:
        /// two fighters twenty units apart are not in a fight together no matter how hurt
        /// either of them is, and framing them as a pair would put the ring between them.
        /// </summary>
        private const float PROXIMITY_WEIGHT = 0.40f;

        /// <summary>Weight on how close the weaker of the two is to being knocked out.</summary>
        private const float FRAILTY_WEIGHT = 0.22f;

        /// <summary>Weight on a cocked haymaker, the one telegraph the game gives for free.</summary>
        private const float CHARGE_WEIGHT = 0.18f;

        /// <summary>Weight on punches that have actually landed in the last few seconds.</summary>
        private const float EXCHANGE_WEIGHT = 0.20f;

        /// <summary>
        /// Bonus for a punch already in flight and pointed at someone. Added rather than
        /// weighted into the average: it is short-lived and its whole job is to be the thing
        /// that tips a close call, not to be a quarter of every score.
        /// </summary>
        private const float THREAT_BONUS = 0.12f;

        /// <summary>
        /// How the proximity term falls away with distance, as a multiple of the range at
        /// which fighters are considered engaged. Beyond this the pair scores zero on
        /// proximity however dramatic they are individually.
        /// </summary>
        private const float PROXIMITY_FALLOFF = 3f;

        /// <summary>
        /// Recent damage, in hit points, at which the exchange term saturates. Roughly one
        /// good flurry: past that a scrap is already the most interesting thing on the canvas
        /// and scoring it higher only makes the ranking sensitive to noise.
        /// </summary>
        private const float EXCHANGE_SATURATION = 12f;

        /// <summary>
        /// How good a shot this pair would make, 0 for two fighters ignoring each other across
        /// the ring and 1 for a hurt fighter eating a cocked haymaker at touching range.
        ///
        /// Every input is passed in rather than read off a model, so the whole rule can be
        /// exercised from a test without a scene or a message bus behind it.
        /// </summary>
        public static float ScorePair(in PairTension pair)
        {
            float separation = Vector2.Distance(pair.PositionA, pair.PositionB);
            float engaged = Mathf.Max(0.01f, pair.EngagementRange);

            // Linear inside the engagement range and linearly decaying outside it, rather
            // than an inverse-square: the camera has to rank pairs against each other, and a
            // curve that spikes at contact makes every ranking a coin toss the instant two
            // fighters touch.
            float proximity = Mathf.Clamp01(
                1f - Mathf.Max(0f, separation - engaged) / (engaged * PROXIMITY_FALLOFF));

            int maxHealth = Mathf.Max(1, pair.MaxHealth);
            int weaker = Mathf.Min(pair.HealthA, pair.HealthB);
            float frailty = Mathf.Clamp01(1f - weaker / (float)maxHealth);

            float charge = Mathf.Clamp01(Mathf.Max(pair.ChargeA, pair.ChargeB));

            float exchange = Mathf.Clamp01(pair.RecentDamage / EXCHANGE_SATURATION);

            float score = proximity * PROXIMITY_WEIGHT
                          + frailty * FRAILTY_WEIGHT
                          + charge * CHARGE_WEIGHT
                          + exchange * EXCHANGE_WEIGHT;

            if (pair.IsThreatened)
            {
                score += THREAT_BONUS;
            }

            // Scaled by proximity rather than merely including it, so a pair that is not
            // together cannot reach a high score on frailty alone. Without this the camera
            // frames the most hurt fighter in the ring and whoever happens to be nearest
            // them, which is the failure the focus radius already exists to prevent.
            return Mathf.Clamp01(score * Mathf.Max(proximity, 0.05f) * 2f);
        }
    }

    /// <summary>
    /// Everything <see cref="TensionMath.ScorePair"/> needs about one pair of fighters.
    ///
    /// A struct passed by `in` rather than nine loose arguments: the director scores every
    /// living pair every frame — forty-five of them in a ten-way — and a long argument list
    /// at that call site is how the wrong fighter's charge ends up in the wrong slot.
    /// </summary>
    public readonly struct PairTension
    {
        public readonly Vector2 PositionA;
        public readonly Vector2 PositionB;
        public readonly int HealthA;
        public readonly int HealthB;
        public readonly float ChargeA;
        public readonly float ChargeB;
        public readonly int MaxHealth;

        /// <summary>Distance at which two fighters count as engaged; the punching range.</summary>
        public readonly float EngagementRange;

        /// <summary>Hit points traded between these two over the last few seconds.</summary>
        public readonly float RecentDamage;

        /// <summary>True when either has a punch already travelling toward the other.</summary>
        public readonly bool IsThreatened;

        public PairTension(
            Vector2 positionA,
            Vector2 positionB,
            int healthA,
            int healthB,
            float chargeA,
            float chargeB,
            int maxHealth,
            float engagementRange,
            float recentDamage,
            bool isThreatened)
        {
            PositionA = positionA;
            PositionB = positionB;
            HealthA = healthA;
            HealthB = healthB;
            ChargeA = chargeA;
            ChargeB = chargeB;
            MaxHealth = maxHealth;
            EngagementRange = engagementRange;
            RecentDamage = recentDamage;
            IsThreatened = isThreatened;
        }
    }
}
