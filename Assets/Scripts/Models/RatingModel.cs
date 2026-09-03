using System.Collections.Generic;

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

    /// <summary>
    /// The Elo table: every contestant's rating, carried between matches and across sessions.
    ///
    /// Keyed by <see cref="FighterProfile.Id"/> rather than by boxer slot, because a boxer
    /// slot is just a chair — the same contestant sits in a different one every match, and
    /// may sit in two at once when the roster is shorter than the ring.
    /// </summary>
    public sealed class RatingModel
    {
        /// <summary>Where an unrated fighter starts. The usual chess convention.</summary>
        public const float DEFAULT_RATING = 1200f;

        private readonly Dictionary<string, RatingRecord> _records = new();
        private readonly List<RatingRecord> _ordered = new();

        /// <summary>Every known record, in no particular order.</summary>
        public IReadOnlyList<RatingRecord> Records => _ordered;

        /// <summary>Bumped after each match so the standings redraw without polling.</summary>
        public ReactiveProperty<int> Revision { get; } = new(0);

        public RatingRecord GetOrCreate(string id, string displayName)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            if (_records.TryGetValue(id, out RatingRecord existing))
            {
                // Display names live in the profile asset, so a rename should follow through
                // to a table that was loaded from disk under the old one.
                if (!string.IsNullOrEmpty(displayName))
                {
                    existing.DisplayName = displayName;
                }

                return existing;
            }

            RatingRecord record = new(id)
            {
                DisplayName = string.IsNullOrEmpty(displayName) ? id : displayName
            };

            _records[id] = record;
            _ordered.Add(record);
            return record;
        }

        /// <summary>
        /// Fills a caller-owned buffer with the highest-rated fighters, best first.
        ///
        /// A selection sort over the buffer rather than List.Sort or LINQ: the table has
        /// single digits of entries, the caller only ever wants the top three, and this
        /// allocates nothing at all.
        /// </summary>
        public void FillTop(List<RatingRecord> buffer, int count)
        {
            buffer.Clear();

            if (count <= 0)
            {
                return;
            }

            for (int taken = 0; taken < count; taken++)
            {
                RatingRecord best = null;

                for (int index = 0; index < _ordered.Count; index++)
                {
                    RatingRecord candidate = _ordered[index];

                    if (buffer.Contains(candidate))
                    {
                        continue;
                    }

                    if (best == null || candidate.Rating > best.Rating)
                    {
                        best = candidate;
                    }
                }

                if (best == null)
                {
                    return;
                }

                buffer.Add(best);
            }
        }

        /// <summary>Wipes every standing. Only the "reset ratings" control calls this.</summary>
        public void Clear()
        {
            _records.Clear();
            _ordered.Clear();
            Revision.Value++;
        }
    }
}
