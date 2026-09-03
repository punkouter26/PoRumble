using System;

namespace PoRumble.Models
{
    /// <summary>
    /// What a policy actually did over a run of matches.
    ///
    /// Deliberately not a reward figure. Reward and the objective pull apart in this project:
    /// finishing a match early truncates the episode, which caps how much damage-dealt reward
    /// can accumulate, so the reward function mildly punishes winning quickly and picking on
    /// it once shipped a policy that finished 21% of matches over one that finished 76%.
    /// <see cref="knockoutRate"/> is the number to select on.
    /// </summary>
    [Serializable]
    public sealed class EvaluationReport
    {
        public string label;
        public int matches;

        /// <summary>Matches a knockout finished, rather than the bell.</summary>
        public int knockouts;

        /// <summary>Matches the bell decided on health.</summary>
        public int timeouts;

        /// <summary>Matches nobody won - everybody down on the same tick, or an exact tie.</summary>
        public int draws;

        /// <summary>Share of matches a knockout finished. The selection criterion.</summary>
        public float knockoutRate;

        /// <summary>Mean physics steps a match lasted. Falls as the policy gets sharper.</summary>
        public float meanEpisodeSteps;

        /// <summary>
        /// Mean fighters still standing at the final bell. In a ten-way this is the clearest
        /// single number for how decisive the policy is; a value near the roster size means
        /// nobody is finishing anybody.
        /// </summary>
        public float meanSurvivors;

        /// <summary>Mean health the winner had left, as a fraction of a full bar.</summary>
        public float meanWinnerHealth;
    }
}
