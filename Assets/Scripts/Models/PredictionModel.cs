namespace PoRumble.Models
{
    /// <summary>Where the viewer's pick stands for the current match.</summary>
    public enum PredictionOutcome
    {
        /// <summary>Nothing riding on this match.</summary>
        None = 0,

        /// <summary>Staked at the bell and waiting on the result.</summary>
        Pending = 1,

        Won = 2,
        Lost = 3,

        /// <summary>A draw, or a fight abandoned from the menu. The stake went back.</summary>
        Refunded = 4
    }

    /// <summary>
    /// The viewer's side of the fight: who they backed, what it pays and what is in the bank.
    ///
    /// Spectator state only. Nothing in the ring reads it, which is what lets a pick sit on
    /// top of a policy calibrated against a ring that never knew picks existed.
    /// </summary>
    public sealed class PredictionModel
    {
        /// <summary>What a new viewer starts with, and what a broke one is bailed out to.</summary>
        public const int STARTING_BANK = 1000;

        /// <summary>The contestant backed for the next bell. Sticky across matches until changed.</summary>
        public ReactiveProperty<FighterProfile> Pick { get; } = new(null);

        public ReactiveProperty<int> Bank { get; } = new(STARTING_BANK);

        /// <summary>Bumped whenever a pick is staked or settled, so the panels redraw once rather than per field.</summary>
        public ReactiveProperty<int> Revision { get; } = new(0);

        public PredictionOutcome Outcome { get; set; }

        /// <summary>The contestant the current stake is riding on. Held apart from Pick so changing Pick mid-fight cannot move the bet.</summary>
        public FighterProfile StakedOn { get; set; }

        /// <summary>Points riding on the current match.</summary>
        public int Stake { get; set; }

        /// <summary>Return per point staked, locked at the bell.</summary>
        public float LockedMultiplier { get; set; }

        /// <summary>What the last settled pick paid back, stake included. Zero for a loss.</summary>
        public int LastPayout { get; set; }

        /// <summary>Picks settled with a decision, win or lose. Refunds are not counted.</summary>
        public int Placed { get; set; }

        public int Correct { get; set; }

        /// <summary>Times the bank ran dry and was topped back up.</summary>
        public int Bailouts { get; set; }

        /// <summary>The biggest single return, stake included - the upset worth remembering.</summary>
        public int BestPayout { get; set; }
    }
}
