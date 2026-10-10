using System;
using MessagePipe;
using PoRumble.Models;
using UnityEngine;
using VContainer;

namespace PoRumble.Systems
{
    /// <summary>
    /// Writes one record per finished match: who won and how, what every seat threw and
    /// landed, and how it moved.
    ///
    /// TensorBoard averages a run into curves, which says whether training is improving and
    /// nothing about why a fighter wins. This keeps the matches themselves, in the game and in
    /// training alike, so results can be aggregated afterwards against behaviour - accuracy,
    /// time on the ropes, distance covered, where in the ring a fighter lives.
    ///
    /// The punch counts are read off <see cref="FightStatsModel"/> rather than counted again;
    /// only the movement profile, which nothing else measures, is accumulated here.
    /// </summary>
    public sealed class MatchLogSystem : IDisposable
    {
        /// <summary>Distance from a rope that counts as on it. The agents' tally uses the same.</summary>
        private const float ROPES_MARGIN = 1.25f;

        private const int NOT_ELIMINATED = -1;

        private readonly MatchModel _match;
        private readonly RosterModel _roster;
        private readonly FightStatsModel _stats;
        private readonly IMatchLogStore _store;
        private readonly MatchLogRecord _record = new();
        private readonly CompositeDisposable _disposables = new();

        private float[] _distance = Array.Empty<float>();
        private float[] _sumX = Array.Empty<float>();
        private float[] _sumY = Array.Empty<float>();
        private float[] _standingSeconds = Array.Empty<float>();
        private int[] _standingSteps = Array.Empty<int>();
        private int[] _ropesSteps = Array.Empty<int>();
        private int[] _eliminationOrder = Array.Empty<int>();

        private float _seconds;
        private int _eliminated;

        [Inject]
        public MatchLogSystem(
            MatchModel match,
            MatchFlowModel flow,
            RosterModel roster,
            FightStatsModel stats,
            IMatchLogStore store,
            ISubscriber<BoxerEliminatedMessage> eliminatedSubscriber,
            ISubscriber<MatchEndedMessage> endedSubscriber)
        {
            _match = match;
            _roster = roster;
            _stats = stats;
            _store = store;

            eliminatedSubscriber.Subscribe(OnBoxerEliminated).AddTo(_disposables);
            endedSubscriber.Subscribe(OnMatchEnded).AddTo(_disposables);

            // Both, as the round clock does: MENU abandons a fight without the match phase
            // changing, and the next bout would otherwise inherit its mileage.
            _match.Phase.Subscribe(OnMatchPhaseChanged).AddTo(_disposables);
            flow.Phase.Subscribe(OnFlowPhaseChanged).AddTo(_disposables);
        }

        /// <summary>Accumulates the movement profile. Called once per physics step of a live fight.</summary>
        public void Step(float deltaSeconds)
        {
            int count = _match.Boxers.Count;

            if (_distance.Length != count)
            {
                Resize(count);
            }

            _seconds += deltaSeconds;

            Vector2 half = _match.ArenaHalfExtent;
            Vector2 ropes = _match.PlayableHalfExtent - new Vector2(ROPES_MARGIN, ROPES_MARGIN);

            for (int boxerIndex = 0; boxerIndex < count; boxerIndex++)
            {
                BoxerModel boxer = _match.Boxers[boxerIndex];

                if (!boxer.IsAlive.Value)
                {
                    continue;
                }

                Vector2 position = boxer.Position;

                _distance[boxerIndex] += boxer.Velocity.magnitude * deltaSeconds;
                _sumX[boxerIndex] += position.x / Mathf.Max(0.01f, half.x);
                _sumY[boxerIndex] += position.y / Mathf.Max(0.01f, half.y);
                _standingSeconds[boxerIndex] += deltaSeconds;
                _standingSteps[boxerIndex]++;

                if (Mathf.Abs(position.x) > ropes.x || Mathf.Abs(position.y) > ropes.y)
                {
                    _ropesSteps[boxerIndex]++;
                }
            }
        }

        private void OnBoxerEliminated(BoxerEliminatedMessage message)
        {
            if (message.BoxerId >= 0 && message.BoxerId < _eliminationOrder.Length)
            {
                _eliminationOrder[message.BoxerId] = _eliminated++;
            }
        }

        private void OnMatchEnded(MatchEndedMessage message)
        {
            int count = _distance.Length;

            if (count == 0 || _seconds <= 0f)
            {
                return;
            }

            _record.utc = DateTime.UtcNow.ToString("o");
            _record.entrants = count;
            _record.winnerSeat = message.WinnerId;
            _record.knockout = _match.CountAlive() <= 1;
            _record.seconds = _seconds;
            _record.boxers.Clear();

            for (int boxerIndex = 0; boxerIndex < count; boxerIndex++)
            {
                _record.boxers.Add(BuildRow(boxerIndex, message.WinnerId, count));
            }

            _store.Append(_record);
        }

        private BoxerLogRow BuildRow(int boxerIndex, int winnerId, int count)
        {
            FighterProfile profile = _roster.SeatOf(boxerIndex);
            FighterStats stats = _stats.For(boxerIndex);
            int steps = Mathf.Max(1, _standingSteps[boxerIndex]);
            int order = _eliminationOrder[boxerIndex];

            var row = new BoxerLogRow
            {
                seat = boxerIndex,
                fighter = _roster.SeatLabel(boxerIndex) ?? string.Empty,
                control = profile != null ? profile.Control.ToString() : string.Empty,
                placement = boxerIndex == winnerId ? 1 : order == NOT_ELIMINATED ? 2 : count - order,
                survivedSeconds = _standingSeconds[boxerIndex],
                distance = _distance[boxerIndex],
                ropesShare = _ropesSteps[boxerIndex] / (float)steps,
                meanX = _sumX[boxerIndex] / steps,
                meanY = _sumY[boxerIndex] / steps
            };

            if (stats != null)
            {
                row.thrown = stats.Thrown;
                row.landed = stats.Landed;
                row.blocked = stats.Blocked;
                row.evaded = stats.Evaded;
                row.haymakers = stats.Haymakers;
                row.counters = stats.Counters;
                row.slips = stats.Slips;
                row.damageDealt = stats.DamageDealt;
                row.damageTaken = stats.DamageTaken;
                row.knockouts = stats.Knockouts;
            }

            return row;
        }

        private void OnMatchPhaseChanged(MatchPhase phase)
        {
            if (phase == MatchPhase.InProgress)
            {
                Reset();
            }
        }

        private void OnFlowPhaseChanged(MatchFlowPhase phase)
        {
            if (phase == MatchFlowPhase.Introducing)
            {
                Reset();
            }
        }

        private void Resize(int count)
        {
            _distance = new float[count];
            _sumX = new float[count];
            _sumY = new float[count];
            _standingSeconds = new float[count];
            _standingSteps = new int[count];
            _ropesSteps = new int[count];
            _eliminationOrder = new int[count];
            Reset();
        }

        private void Reset()
        {
            _seconds = 0f;
            _eliminated = 0;

            for (int boxerIndex = 0; boxerIndex < _distance.Length; boxerIndex++)
            {
                _distance[boxerIndex] = 0f;
                _sumX[boxerIndex] = 0f;
                _sumY[boxerIndex] = 0f;
                _standingSeconds[boxerIndex] = 0f;
                _standingSteps[boxerIndex] = 0;
                _ropesSteps[boxerIndex] = 0;
                _eliminationOrder[boxerIndex] = NOT_ELIMINATED;
            }
        }

        public void Dispose() => _disposables.Dispose();
    }
}
