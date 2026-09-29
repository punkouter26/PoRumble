namespace PoRumble.Models
{
    /// <summary>One contestant's standing. Mutable: the rating system updates it in place.</summary>
    public sealed class RatingRecord
    {
        public string Id { get; }
        public string DisplayName { get; set; }
        public float Rating { get; set; } = RatingModel.DEFAULT_RATING;
        public int Matches { get; set; }
        public int Wins { get; set; }

        /// <summary>Opponents this fighter has personally knocked out, across all matches.</summary>
        public int Knockouts { get; set; }

        /// <summary>Rating change from the most recent match, for the "+18" on the standings.</summary>
        public float LastDelta { get; set; }

        public RatingRecord(string id)
        {
            Id = id;
        }
    }
}
