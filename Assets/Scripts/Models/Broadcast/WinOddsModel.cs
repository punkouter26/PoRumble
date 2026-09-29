using System.Collections.Generic;

namespace PoRumble.Models
{
    /// <summary>
    /// Every fighter's live chance of winning, per seat and per contestant.
    ///
    /// Derived state and nothing else, like <see cref="FightStatsModel"/>: removing it would
    /// leave the ring identical. Written by WinOddsSystem a few times a second; views redraw on
    /// <see cref="Revision"/> rather than polling.
    /// </summary>
    public sealed class WinOddsModel
    {
        private readonly List<OddsEntry> _entries = new(10);
        private float[] _seatOdds = System.Array.Empty<float>();

        /// <summary>The board, best chance first. Rebuilt when the seating changes, re-sorted on every update.</summary>
        public IReadOnlyList<OddsEntry> Entries => _entries;

        /// <summary>Bumped after every recompute.</summary>
        public ReactiveProperty<int> Revision { get; } = new(0);

        public int SeatCount => _seatOdds.Length;

        /// <summary>One seat's chance, by index into MatchModel.Boxers.</summary>
        public float SeatOdds(int seatIndex)
        {
            return seatIndex >= 0 && seatIndex < _seatOdds.Length ? _seatOdds[seatIndex] : 0f;
        }

        /// <summary>Sizes the per-seat table. Keeps the buffer when the count has not changed.</summary>
        public void ConfigureSeats(int seatCount)
        {
            if (_seatOdds.Length != seatCount)
            {
                _seatOdds = new float[seatCount];
            }
        }

        public void SetSeatOdds(int seatIndex, float odds)
        {
            if (seatIndex >= 0 && seatIndex < _seatOdds.Length)
            {
                _seatOdds[seatIndex] = odds;
            }
        }

        /// <summary>Drops every entry, for a board about to be rebuilt against a new seating.</summary>
        public void ClearEntries()
        {
            _entries.Clear();
        }

        public void AddEntry(OddsEntry entry)
        {
            _entries.Add(entry);
        }

        /// <summary>The entry for a contestant, or null when they are not on tonight's board.</summary>
        public OddsEntry EntryFor(FighterProfile profile)
        {
            if (profile == null)
            {
                return null;
            }

            for (int index = 0; index < _entries.Count; index++)
            {
                if (_entries[index].Profile == profile)
                {
                    return _entries[index];
                }
            }

            return null;
        }

        /// <summary>
        /// Puts the board in order, best chance first.
        ///
        /// An insertion sort: ten entries at most, the list is already nearly sorted from the
        /// last update, and List.Sort with a comparison allocates a delegate every call.
        /// </summary>
        public void SortByOdds()
        {
            for (int index = 1; index < _entries.Count; index++)
            {
                OddsEntry moving = _entries[index];
                int slot = index - 1;

                while (slot >= 0 && _entries[slot].Odds < moving.Odds)
                {
                    _entries[slot + 1] = _entries[slot];
                    slot--;
                }

                _entries[slot + 1] = moving;
            }
        }

        /// <summary>Rolls every entry's trend window over, so the next trend is measured from now.</summary>
        public void CaptureBaselines()
        {
            for (int index = 0; index < _entries.Count; index++)
            {
                _entries[index].Baseline = _entries[index].Odds;
            }
        }
    }
}
