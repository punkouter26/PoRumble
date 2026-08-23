using System;
using MessagePipe;
using PoRumble.Models;
using VContainer;

namespace PoRumble.Systems
{
    /// <summary>Watches eliminations and decides when the match is over.</summary>
    public sealed class MatchSystem : IDisposable
    {
        private readonly MatchModel _match;
        private readonly IPublisher<MatchEndedMessage> _endedPublisher;
        private readonly IDisposable _subscription;

        [Inject]
        public MatchSystem(
            MatchModel match,
            ISubscriber<BoxerEliminatedMessage> eliminatedSubscriber,
            IPublisher<MatchEndedMessage> endedPublisher)
        {
            _match = match;
            _endedPublisher = endedPublisher;
            _subscription = eliminatedSubscriber.Subscribe(OnBoxerEliminated);
        }

        private void OnBoxerEliminated(BoxerEliminatedMessage message)
        {
            // Deliberately does not end the match here. Resolution happens once per tick via
            // EvaluateMatchState, so both halves of a simultaneous knockout are counted.
        }

        /// <summary>
        /// Ends the match once fewer than two boxers remain. Zero survivors — every boxer
        /// eliminated on the same tick — is a draw, not a hang.
        /// </summary>
        public void EvaluateMatchState()
        {
            if (_match.Phase.Value == MatchPhase.Ended)
            {
                return;
            }

            int aliveCount = _match.CountAlive();

            if (aliveCount > 1)
            {
                return;
            }

            int winnerId = aliveCount == 1 ? _match.ResolveLeaderId() : MatchModel.NO_WINNER;
            EndMatch(winnerId);
        }

        /// <summary>Time-limit resolution: the highest-health survivor wins, an exact tie draws.</summary>
        public void EndByTimeout()
        {
            if (_match.Phase.Value == MatchPhase.Ended)
            {
                return;
            }

            EndMatch(_match.ResolveLeaderId());
        }

        private void EndMatch(int winnerId)
        {
            _match.End(winnerId);
            _endedPublisher.Publish(new MatchEndedMessage(winnerId));
        }

        public void Dispose()
        {
            _subscription.Dispose();
        }
    }
}
