using System;
using System.Collections.Generic;
using PoRumble.Models;
using UnityEngine;
using VContainer;

namespace PoRumble.Systems
{
    /// <summary>
    /// Keeps every fighter's chance of winning current: per seat for the field board and the
    /// head-to-head share, and per contestant for the tile prices and the book.
    ///
    /// Entirely derived. It reads the ring and writes <see cref="WinOddsModel"/>, publishes
    /// nothing and is consulted by nothing in combat, so the fight is identical whether it
    /// runs or not - the same guarantee the telemetry board and the camera director give.
    ///
    /// Ticked by MatchDirector on unscaled time and skipped in training, like the rest of the
    /// broadcast layer.
    /// </summary>
    public sealed class WinOddsSystem : IDisposable
    {
        /// <summary>
        /// Seconds between recomputes. Four times a second is faster than anybody reads a
        /// percentage, and the odds only move when a punch lands anyway.
        /// </summary>
        private const float REFRESH_INTERVAL = 0.25f;

        /// <summary>
        /// How far back the trend on each line looks. Long enough that "+6" means an exchange
        /// went someone's way rather than one jab, short enough to still be about now.
        /// </summary>
        private const float TREND_WINDOW = 2.5f;

        private readonly MatchModel _match;
        private readonly RosterModel _roster;
        private readonly RatingModel _ratings;
        private readonly FightStatsModel _stats;
        private readonly WinOddsModel _odds;
        private readonly BoxerConfig _config;
        private readonly CompositeDisposable _disposables = new();

        /// <summary>Entries retained across rebuilds, so a re-seat reuses objects rather than allocating.</summary>
        private readonly List<OddsEntry> _pool = new(10);

        private float[] _strengths = Array.Empty<float>();
        private float[] _seatOdds = Array.Empty<float>();

        private float _refreshTimer;
        private float _trendTimer;
        private bool _entriesStale = true;

        [Inject]
        public WinOddsSystem(
            MatchModel match,
            RosterModel roster,
            RatingModel ratings,
            FightStatsModel stats,
            WinOddsModel odds,
            BoxerConfig config)
        {
            _match = match;
            _roster = roster;
            _ratings = ratings;
            _stats = stats;
            _odds = odds;
            _config = config;

            // A re-dealt card changes who sits where, and a board keyed on the old seating
            // would credit one contestant with another's chair.
            _roster.Revision.Subscribe(_ => _entriesStale = true).AddTo(_disposables);

            // A fresh match starts every trend from level, rather than showing the whole of
            // the last match's collapse as this one's opening move.
            _match.Phase.Subscribe(OnMatchPhaseChanged).AddTo(_disposables);
        }

        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
            {
                return;
            }

            _refreshTimer += deltaSeconds;
            _trendTimer += deltaSeconds;

            if (_refreshTimer < REFRESH_INTERVAL)
            {
                return;
            }

            _refreshTimer = 0f;
            Recompute();

            if (_trendTimer >= TREND_WINDOW)
            {
                _trendTimer = 0f;
                _odds.CaptureBaselines();
            }
        }

        /// <summary>
        /// A contestant's chance of winning, summed over every chair they sit in. Zero for
        /// anyone not on tonight's board.
        /// </summary>
        public float OddsFor(FighterProfile profile)
        {
            OddsEntry entry = _odds.EntryFor(profile);
            return entry != null ? entry.Odds : 0f;
        }

        /// <summary>
        /// Scores the whole field and rewrites the board.
        ///
        /// Public so a caller that needs the answer now - the book locking a price at the bell
        /// - does not have to wait up to a quarter of a second for the next tick.
        /// </summary>
        public void Recompute()
        {
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;
            int count = boxers.Count;

            if (_strengths.Length != count)
            {
                _strengths = new float[count];
                _seatOdds = new float[count];
                _entriesStale = true;
            }

            _odds.ConfigureSeats(count);

            bool rebuilt = _entriesStale;

            if (rebuilt)
            {
                RebuildEntries();
            }

            float maxHealth = Mathf.Max(1, _config.MaxHealth);

            for (int seat = 0; seat < count; seat++)
            {
                BoxerModel boxer = boxers[seat];
                FighterStats stats = _stats.For(seat);

                _strengths[seat] = WinOddsMath.Strength(new OddsInput(
                    boxer.IsAlive.Value,
                    boxer.Health.Value / maxHealth,
                    boxer.Stamina.Value,
                    RatingFor(boxer.Id),
                    boxer.Attributes.Power,
                    boxer.Attributes.Chin,
                    stats != null ? stats.Momentum : 0f));
            }

            WinOddsMath.Normalise(_strengths, _seatOdds, count);

            IReadOnlyList<OddsEntry> entries = _odds.Entries;

            for (int index = 0; index < entries.Count; index++)
            {
                entries[index].Odds = 0f;
                entries[index].IsAlive = false;
            }

            for (int seat = 0; seat < count; seat++)
            {
                _odds.SetSeatOdds(seat, _seatOdds[seat]);

                OddsEntry entry = EntryForSeat(seat);

                if (entry == null)
                {
                    continue;
                }

                entry.Odds += _seatOdds[seat];
                entry.IsAlive |= boxers[seat].IsAlive.Value;
            }

            _odds.SortByOdds();

            // A freshly dealt board opens with every trend at zero, rather than reading its
            // first real numbers as a surge from nothing.
            if (rebuilt)
            {
                _odds.CaptureBaselines();
                _trendTimer = 0f;
            }

            _odds.Revision.Value++;
        }

        /// <summary>
        /// One entry per contestant when there is a card, one per seat when there is not.
        ///
        /// Per contestant because a card shorter than the ring seats some fighters twice, and a
        /// board listing BIGGIE on two lines at 12% each is telling the viewer something false
        /// about who they would be backing.
        /// </summary>
        private void RebuildEntries()
        {
            _entriesStale = false;
            _odds.ClearEntries();

            IReadOnlyList<BoxerModel> boxers = _match.Boxers;
            int used = 0;

            for (int seat = 0; seat < boxers.Count; seat++)
            {
                FighterProfile profile = _roster.SeatOf(boxers[seat].Id);

                if (profile != null && _odds.EntryFor(profile) != null)
                {
                    continue;
                }

                if (used == _pool.Count)
                {
                    _pool.Add(new OddsEntry());
                }

                OddsEntry entry = _pool[used++];
                entry.Profile = profile;
                entry.BoxerId = boxers[seat].Id;
                entry.Odds = 0f;
                entry.Baseline = 0f;
                entry.IsAlive = true;
                _odds.AddEntry(entry);
            }
        }

        private OddsEntry EntryForSeat(int seat)
        {
            int boxerId = _match.Boxers[seat].Id;
            FighterProfile profile = _roster.SeatOf(boxerId);

            if (profile != null)
            {
                return _odds.EntryFor(profile);
            }

            IReadOnlyList<OddsEntry> entries = _odds.Entries;

            for (int index = 0; index < entries.Count; index++)
            {
                if (entries[index].Profile == null && entries[index].BoxerId == boxerId)
                {
                    return entries[index];
                }
            }

            return null;
        }

        private float RatingFor(int boxerId)
        {
            FighterProfile profile = _roster.SeatOf(boxerId);
            return profile != null ? _ratings.RatingOf(profile.Id) : RatingModel.DEFAULT_RATING;
        }

        private void OnMatchPhaseChanged(MatchPhase phase)
        {
            if (phase != MatchPhase.InProgress)
            {
                return;
            }

            _entriesStale = true;
            _refreshTimer = REFRESH_INTERVAL;
        }

        public void Dispose()
        {
            _disposables.Dispose();
        }
    }
}
