using System;
using System.Collections.Generic;
using MessagePipe;
using PoRumble.Models;
using UnityEngine;
using VContainer;

namespace PoRumble.Systems
{
    /// <summary>
    /// Rates the contestants against each other, match after match.
    ///
    /// Elo is a two-player system and this is a ten-way, so the free-for-all is scored the
    /// standard way: the finishing order is turned into every pairwise result it implies, each
    /// pair is rated as its own little match, and the sum is divided by the number of
    /// opponents. Without that division a fighter in a ten-way would swing nine times as far
    /// as one in a 1v1, and the ratings would say more about how crowded the ring was than
    /// about who is any good.
    ///
    /// Ratings belong to the contestant, not to the boxer slot. The ring seats ten and the
    /// roster is usually shorter, so the same fighter can be in two chairs at once — those
    /// pairs are skipped rather than rated, because beating yourself proves nothing.
    /// </summary>
    public sealed class RatingSystem : IDisposable
    {
        private readonly RatingModel _ratings;
        private readonly RosterModel _roster;
        private readonly MatchModel _match;
        private readonly IRatingStore _store;
        private readonly IDisposable _eliminatedSubscription;
        private readonly IDisposable _endedSubscription;

        /// <summary>
        /// How far a single match can move a rating. The chess convention for provisional
        /// players; matches here are quick and plentiful, so a table that settles fast is
        /// worth more than one that is statistically pristine.
        /// </summary>
        private const float K_FACTOR = 32f;

        /// <summary>Boxer ids in the order they were knocked out, first out first.</summary>
        private readonly List<int> _eliminationOrder = new(16);

        /// <summary>Boxer ids in finishing order, winner first. Rebuilt at the final bell.</summary>
        private readonly List<int> _finishingOrder = new(16);

        /// <summary>Rating change per boxer for this match, applied only once every pair is scored.</summary>
        private readonly List<float> _deltas = new(16);

        [Inject]
        public RatingSystem(
            RatingModel ratings,
            RosterModel roster,
            MatchModel match,
            IRatingStore store,
            ISubscriber<BoxerEliminatedMessage> eliminatedSubscriber,
            ISubscriber<MatchEndedMessage> endedSubscriber)
        {
            _ratings = ratings;
            _roster = roster;
            _match = match;
            _store = store;

            if (_store != null)
            {
                _store.Load(_ratings);
            }

            _eliminatedSubscription = eliminatedSubscriber.Subscribe(OnBoxerEliminated);
            _endedSubscription = endedSubscriber.Subscribe(OnMatchEnded);
        }

        /// <summary>
        /// Records the order fighters went out in. That order is the whole result: a boxer who
        /// survived to third place did better than one eliminated in the first exchange, and
        /// nothing else in the match state remembers which of them fell first.
        /// </summary>
        private void OnBoxerEliminated(BoxerEliminatedMessage message)
        {
            if (!_eliminationOrder.Contains(message.BoxerId))
            {
                _eliminationOrder.Add(message.BoxerId);
            }

            RatingRecord killer = RecordFor(message.EliminatedById);

            if (killer != null && message.EliminatedById != message.BoxerId)
            {
                killer.Knockouts++;
            }
        }

        private void OnMatchEnded(MatchEndedMessage message)
        {
            // No seated contestants means no roster - a training scene, or the scene loaded
            // before anything assigned the seats. Nothing to rate.
            if (_roster.SeatOf(0) == null)
            {
                _eliminationOrder.Clear();
                return;
            }

            BuildFinishingOrder(message.WinnerId);
            ApplyPairwiseElo();
            _eliminationOrder.Clear();

            _ratings.Revision.Value++;
            _store?.Save(_ratings);
        }

        /// <summary>
        /// Turns the match into a finishing order, winner first.
        ///
        /// Survivors are ranked on health, then the eliminated in reverse order of going out.
        /// A timeout resolves with several fighters still standing, so ordering survivors on
        /// health is what stops a bell-decided match rating everyone who lasted as equal.
        /// </summary>
        private void BuildFinishingOrder(int winnerId)
        {
            _finishingOrder.Clear();
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            // Survivors, best health first. An insertion sort: the roster is ten long, and
            // List.Sort with a comparer allocates one on every match.
            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel boxer = boxers[boxerIndex];

                if (!boxer.IsAlive.Value)
                {
                    continue;
                }

                int insertAt = _finishingOrder.Count;

                for (int placed = 0; placed < _finishingOrder.Count; placed++)
                {
                    if (boxer.Health.Value > boxers[_finishingOrder[placed]].Health.Value)
                    {
                        insertAt = placed;
                        break;
                    }
                }

                _finishingOrder.Insert(insertAt, boxer.Id);
            }

            // The declared winner takes first outright. On a bell decision that is already the
            // healthiest survivor, but a knockout can leave two standing on equal health and
            // the match has already picked one.
            if (winnerId != MatchModel.NO_WINNER)
            {
                _finishingOrder.Remove(winnerId);
                _finishingOrder.Insert(0, winnerId);
            }

            // Then the fallen, last out placing highest.
            for (int index = _eliminationOrder.Count - 1; index >= 0; index--)
            {
                _finishingOrder.Add(_eliminationOrder[index]);
            }
        }

        private void ApplyPairwiseElo()
        {
            _deltas.Clear();

            for (int index = 0; index < _finishingOrder.Count; index++)
            {
                _deltas.Add(0f);
            }

            int rated = _finishingOrder.Count;

            if (rated < 2)
            {
                return;
            }

            // Every delta is computed against the ratings as they stood at the opening bell,
            // so the order the pairs happen to be visited in cannot change the outcome.
            for (int first = 0; first < rated; first++)
            {
                RatingRecord a = RecordFor(_finishingOrder[first]);

                if (a == null)
                {
                    continue;
                }

                for (int second = first + 1; second < rated; second++)
                {
                    RatingRecord b = RecordFor(_finishingOrder[second]);

                    // Same contestant in two chairs. Rating that pair would be self-play noise.
                    if (b == null || ReferenceEquals(a, b))
                    {
                        continue;
                    }

                    float expectedA = 1f / (1f + Mathf.Pow(10f, (b.Rating - a.Rating) / 400f));

                    // The list is already in finishing order, so the earlier index won.
                    _deltas[first] += K_FACTOR * (1f - expectedA);
                    _deltas[second] += K_FACTOR * (expectedA - 1f);
                }
            }

            float share = 1f / (rated - 1);

            // Cleared before anything is added, because a contestant seated in two chairs is
            // visited twice and its change for this match is the sum of both.
            for (int index = 0; index < rated; index++)
            {
                RatingRecord record = RecordFor(_finishingOrder[index]);

                if (record != null)
                {
                    record.LastDelta = 0f;
                }
            }

            for (int index = 0; index < rated; index++)
            {
                RatingRecord record = RecordFor(_finishingOrder[index]);

                if (record == null)
                {
                    continue;
                }

                float delta = _deltas[index] * share;

                // A contestant seated twice accumulates both chairs' results, which is right:
                // it fought twice as many pairings and the record should say so.
                record.Rating += delta;
                record.LastDelta += delta;
                record.Matches++;

                if (index == 0)
                {
                    record.Wins++;
                }
            }
        }

        /// <summary>The rating record for whoever is sitting in a boxer slot, or null.</summary>
        private RatingRecord RecordFor(int boxerId)
        {
            FighterProfile profile = _roster.SeatOf(boxerId);

            return profile == null ? null : _ratings.GetOrCreate(profile.Id, profile.DisplayName);
        }

        public void Dispose()
        {
            _eliminatedSubscription.Dispose();
            _endedSubscription.Dispose();
        }
    }
}
