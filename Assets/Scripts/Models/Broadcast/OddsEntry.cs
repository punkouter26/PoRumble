namespace PoRumble.Models
{
    /// <summary>
    /// One contestant in the book, or a bare seat when there is no card.
    ///
    /// A class rather than a struct because the board keeps the same entry objects for a
    /// whole match and only re-sorts them. The trend is measured against each entry's own
    /// baseline, and a struct copied in and out of a sorted list would lose track of whose
    /// baseline was whose.
    /// </summary>
    public sealed class OddsEntry
    {
        /// <summary>The contestant, or null when the scene has no card and this entry is a seat.</summary>
        public FighterProfile Profile { get; set; }

        /// <summary>The first boxer this entry sits in, for naming a seat when there is no card.</summary>
        public int BoxerId { get; set; }

        /// <summary>Chance of winning, 0..1. For a contestant seated twice, both chairs summed.</summary>
        public float Odds { get; set; }

        /// <summary>Odds when the trend window last rolled over, so the board can show which way each one is moving.</summary>
        public float Baseline { get; set; }

        /// <summary>True while any chair this entry sits in is still standing.</summary>
        public bool IsAlive { get; set; }

        public float Trend => Odds - Baseline;
    }
}
