using System.Collections.Generic;

namespace PoRumble.Models
{
    /// <summary>
    /// Who is fighting tonight.
    ///
    /// Two lists on purpose. <see cref="Available"/> is the full card of contestants the
    /// project ships; <see cref="Entrants"/> is the subset the player has picked. The ring
    /// always seats the same number of boxers — the roster is usually shorter than that, so
    /// <see cref="AssignSeats"/> deals the entrants round the seats cyclically rather than
    /// leaving empty corners or rebuilding the arena every time the selection changes.
    /// </summary>
    public sealed class RosterModel
    {
        /// <summary>A fight needs two people in it.</summary>
        public const int MIN_ENTRANTS = 2;

        private readonly List<FighterProfile> _available = new();
        private readonly List<FighterProfile> _entrants = new();
        private FighterProfile[] _seats = System.Array.Empty<FighterProfile>();

        public IReadOnlyList<FighterProfile> Available => _available;
        public IReadOnlyList<FighterProfile> Entrants => _entrants;

        /// <summary>
        /// Bumped whenever the seating changes. Views watch this rather than polling, and it
        /// is a counter rather than a bool so a second change while a view is mid-rebuild
        /// still registers.
        /// </summary>
        public ReactiveProperty<int> Revision { get; } = new(0);

        /// <summary>True while the roster screen is up. Only ever set by RosterSystem.</summary>
        public ReactiveProperty<bool> IsOpen { get; } = new(false);

        /// <summary>
        /// Publishes the full card. Entrants that are no longer on it are dropped, so an
        /// asset removed from the scene cannot leave a dangling selection behind.
        /// </summary>
        public void SetAvailable(IReadOnlyList<FighterProfile> profiles)
        {
            _available.Clear();

            if (profiles != null)
            {
                for (int index = 0; index < profiles.Count; index++)
                {
                    if (profiles[index] != null)
                    {
                        _available.Add(profiles[index]);
                    }
                }
            }

            for (int index = _entrants.Count - 1; index >= 0; index--)
            {
                if (!_available.Contains(_entrants[index]))
                {
                    _entrants.RemoveAt(index);
                }
            }

            if (_entrants.Count < MIN_ENTRANTS)
            {
                SelectAll();
            }
        }

        public void SelectAll()
        {
            _entrants.Clear();

            for (int index = 0; index < _available.Count; index++)
            {
                _entrants.Add(_available[index]);
            }
        }

        public bool IsEntrant(FighterProfile profile)
        {
            return profile != null && _entrants.Contains(profile);
        }

        /// <summary>
        /// Adds or removes a contestant. Refuses to drop below two entrants — a one-fighter
        /// card would seat the same boxer ten times and the match could never resolve.
        /// </summary>
        public bool Toggle(FighterProfile profile)
        {
            if (profile == null || !_available.Contains(profile))
            {
                return false;
            }

            if (_entrants.Contains(profile))
            {
                if (_entrants.Count <= MIN_ENTRANTS)
                {
                    return false;
                }

                _entrants.Remove(profile);
                return true;
            }

            _entrants.Add(profile);
            return true;
        }

        /// <summary>
        /// Deals the entrants round the ring. With eight entrants and ten seats the first two
        /// fight twice; that is deliberate, and the ratings skip same-contestant pairings so
        /// it costs nobody anything.
        /// </summary>
        public void AssignSeats(int seatCount)
        {
            if (seatCount < 0)
            {
                seatCount = 0;
            }

            if (_seats.Length != seatCount)
            {
                _seats = new FighterProfile[seatCount];
            }

            for (int seat = 0; seat < seatCount; seat++)
            {
                _seats[seat] = _entrants.Count == 0 ? null : _entrants[seat % _entrants.Count];
            }

            Revision.Value++;
        }

        /// <summary>The contestant in a given boxer slot, or null when nothing is seated.</summary>
        public FighterProfile SeatOf(int boxerId)
        {
            return boxerId >= 0 && boxerId < _seats.Length ? _seats[boxerId] : null;
        }
    }
}
