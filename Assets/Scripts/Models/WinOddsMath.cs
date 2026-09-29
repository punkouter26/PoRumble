using UnityEngine;

namespace PoRumble.Models
{
    /// <summary>What the odds need to know about one fighter. A value copy, so the maths is testable without a scene.</summary>
    public readonly struct OddsInput
    {
        public readonly bool IsAlive;

        /// <summary>Health as a share of the maximum, 0..1.</summary>
        public readonly float HealthFraction;

        /// <summary>Breath, 0 spent and 1 fresh.</summary>
        public readonly float Stamina;

        /// <summary>The contestant's Elo, or <see cref="RatingModel.DEFAULT_RATING"/> with no card.</summary>
        public readonly float Rating;

        public readonly float Power;
        public readonly float Chin;

        /// <summary>The telemetry board's momentum, in hit points of recent differential.</summary>
        public readonly float Momentum;

        public OddsInput(
            bool isAlive,
            float healthFraction,
            float stamina,
            float rating,
            float power,
            float chin,
            float momentum)
        {
            IsAlive = isAlive;
            HealthFraction = healthFraction;
            Stamina = stamina;
            Rating = rating;
            Power = power;
            Chin = chin;
            Momentum = momentum;
        }
    }

    /// <summary>
    /// Turns the state of the ring into each fighter's chance of being the last one standing.
    ///
    /// A strength per fighter, normalised across the field: Plackett-Luce, the N-way form of
    /// the Bradley-Terry model Elo itself is. That choice is what makes the pre-fight number
    /// honest rather than invented - at the opening bell, with every fighter fresh and neutral,
    /// the rating term is the only one that differs, and two fighters' share of the total is
    /// exactly the Elo expected score between them. The in-fight terms then bend that prior by
    /// the things a watching person would weigh: who is hurt, who is gassed, and who is
    /// winning the exchange right now.
    ///
    /// The exponents are hand-set, not fitted. They were chosen so a fighter on half health is
    /// a quarter as likely to win as an identical fresh one, and so momentum can tip a close
    /// call without ever outweighing a health lead. Fitting them properly needs logged matches
    /// and their outcomes; until that exists this is a reasoned prior, and the panel says
    /// "probability" in the sense a broadcast does.
    ///
    /// Pure and static like <see cref="TensionMath"/>: no Unity components and no allocation.
    /// </summary>
    public static class WinOddsMath
    {
        /// <summary>
        /// How hard health drives the odds. Squared, because time-to-knockout scales with the
        /// health you have left *and* the fighter with more of it can afford to trade.
        /// </summary>
        private const float HEALTH_EXPONENT = 2f;

        /// <summary>Power reads straight through: twice the damage per punch, twice as dangerous.</summary>
        private const float POWER_EXPONENT = 1f;

        /// <summary>
        /// Breath counts for at most half. A gassed fighter still has punches in them; they
        /// just throw fewer and softer, which is what BoxerSystem already does with stamina.
        /// </summary>
        private const float STAMINA_FLOOR = 0.5f;

        /// <summary>Momentum, in hit points, at which its term saturates. Matches the telemetry board's full scale.</summary>
        private const float MOMENTUM_FULL_SCALE = 14f;

        /// <summary>
        /// How far a fully one-sided exchange can move a fighter's strength: e^0.35, about
        /// 1.4x. Enough to make the list move while punches land, never enough to put a fighter
        /// on a sliver of health ahead of a fresh one.
        /// </summary>
        private const float MOMENTUM_WEIGHT = 0.35f;

        /// <summary>The Elo scale. 400 points is a tenfold difference in strength.</summary>
        private const float ELO_SCALE = 400f;

        /// <summary>
        /// Floor under durability, so a fighter on their last hit point keeps a non-zero chance.
        /// They are still standing, and fighters on one hit point do win.
        /// </summary>
        private const float MIN_DURABILITY = 0.02f;

        /// <summary>Longest odds the book will quote. 50x is a two-percent shot.</summary>
        public const float MAX_MULTIPLIER = 50f;

        /// <summary>
        /// One fighter's strength. Zero for the eliminated, positive for anyone standing.
        ///
        /// Chin divides durability rather than multiplying anything: a chin of 1.3 takes 1.3x
        /// damage, so the same health bar lasts 1/1.3 as long.
        /// </summary>
        public static float Strength(in OddsInput input)
        {
            if (!input.IsAlive)
            {
                return 0f;
            }

            float durability = Mathf.Max(
                MIN_DURABILITY,
                Mathf.Clamp01(input.HealthFraction) / Mathf.Max(0.1f, input.Chin));

            float rating = Mathf.Pow(10f, (input.Rating - RatingModel.DEFAULT_RATING) / ELO_SCALE);
            float breath = Mathf.Lerp(STAMINA_FLOOR, 1f, Mathf.Clamp01(input.Stamina));
            float momentum = Mathf.Exp(
                MOMENTUM_WEIGHT * Mathf.Clamp(input.Momentum / MOMENTUM_FULL_SCALE, -1f, 1f));

            return rating
                   * Mathf.Pow(durability, HEALTH_EXPONENT)
                   * Mathf.Pow(Mathf.Max(0.1f, input.Power), POWER_EXPONENT)
                   * breath
                   * momentum;
        }

        /// <summary>
        /// Writes each strength's share of the total into <paramref name="odds"/>. A field with
        /// no strength at all - everyone eliminated - reads as zero across the board rather
        /// than dividing by nothing.
        /// </summary>
        public static void Normalise(float[] strengths, float[] odds, int count)
        {
            float total = 0f;

            for (int index = 0; index < count; index++)
            {
                total += strengths[index];
            }

            for (int index = 0; index < count; index++)
            {
                odds[index] = total > 0f ? strengths[index] / total : 0f;
            }
        }

        /// <summary>
        /// What a winning pick returns per point staked, stake included. Fair odds with no
        /// house margin - the bank is for fun, and a margin would only make every pick a slow
        /// loss. Capped so a two-percent outsider does not pay a hundred to one.
        /// </summary>
        public static float Multiplier(float probability)
        {
            return 1f / Mathf.Clamp(probability, 1f / MAX_MULTIPLIER, 1f);
        }
    }
}
