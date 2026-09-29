using System;
using MessagePipe;
using PoRumble.Models;
using UnityEngine;
using VContainer;

namespace PoRumble.Systems
{
    /// <summary>
    /// The book: the viewer backs a contestant before the bell, the price locks when the fight
    /// is introduced, and the bank settles on the result.
    ///
    /// The rules live here rather than in the picker so the picker cannot bend them. A pick is
    /// only taken on the title screen, because once the fighters are introduced the viewer is
    /// watching the odds move and a price taken then is not a prediction. The stake locks at
    /// the introduction rather than at the tap, so changing your mind on the menu is free.
    ///
    /// Spectator state only. Nothing in combat reads it.
    /// </summary>
    public sealed class PredictionSystem : IDisposable
    {
        /// <summary>
        /// Points riding on every pick. Flat rather than chosen, because a stake slider is a
        /// second decision on a screen whose whole job is getting the viewer to the bell, and
        /// the price already carries the risk: backing the outsider is the big bet.
        /// </summary>
        public const int STAKE = 100;

        private readonly PredictionModel _predictions;
        private readonly WinOddsSystem _odds;
        private readonly RosterModel _roster;
        private readonly MatchFlowModel _flow;
        private readonly IPredictionStore _store;
        private readonly CompositeDisposable _disposables = new();

        [Inject]
        public PredictionSystem(
            PredictionModel predictions,
            WinOddsSystem odds,
            RosterModel roster,
            MatchFlowModel flow,
            IPredictionStore store,
            ISubscriber<MatchEndedMessage> endedSubscriber)
        {
            _predictions = predictions;
            _odds = odds;
            _roster = roster;
            _flow = flow;
            _store = store;

            _store?.Load(_predictions);

            endedSubscriber.Subscribe(OnMatchEnded).AddTo(_disposables);
            _flow.Phase.Subscribe(OnFlowPhaseChanged).AddTo(_disposables);

            // A contestant dropped from the card cannot be backed; leaving the pick on them
            // would lock a stake on somebody who is not in the ring.
            _roster.Revision.Subscribe(_ => DropPickIfNotEntrant()).AddTo(_disposables);
        }

        /// <summary>
        /// Backs a contestant, or clears the pick when they are already backed. Refused off the
        /// title screen and for anyone not on the card, so the bool says whether anything
        /// changed.
        /// </summary>
        public bool TogglePick(FighterProfile profile)
        {
            if (!_flow.CanStartFight || profile == null || !_roster.IsEntrant(profile))
            {
                return false;
            }

            _predictions.Pick.Value = _predictions.Pick.Value == profile ? null : profile;
            return true;
        }

        /// <summary>What a pick on this contestant would pay per point, at the current price.</summary>
        public float MultiplierFor(FighterProfile profile)
        {
            return WinOddsMath.Multiplier(_odds.OddsFor(profile));
        }

        private void OnFlowPhaseChanged(MatchFlowPhase phase)
        {
            switch (phase)
            {
                case MatchFlowPhase.Introducing:
                    Lock();
                    break;

                case MatchFlowPhase.Title:
                    // Back on the menu with a stake still riding means the fight was
                    // abandoned from the chrome bar: no result, so the stake goes back.
                    if (_predictions.Outcome == PredictionOutcome.Pending)
                    {
                        Settle(PredictionOutcome.Refunded, _predictions.Stake);
                    }

                    break;
            }
        }

        /// <summary>
        /// Takes the stake and fixes the price at the introduction. A viewer who has run the
        /// bank below one stake is bailed out first rather than shut out of the game.
        /// </summary>
        private void Lock()
        {
            FighterProfile pick = _predictions.Pick.Value;

            if (pick == null || !_roster.IsEntrant(pick))
            {
                _predictions.Outcome = PredictionOutcome.None;
                _predictions.StakedOn = null;
                _predictions.Stake = 0;
                _predictions.Revision.Value++;
                return;
            }

            if (_predictions.Bank.Value < STAKE)
            {
                _predictions.Bank.Value = PredictionModel.STARTING_BANK;
                _predictions.Bailouts++;
            }

            // Recomputed now rather than read from the last tick, so the price is the one for
            // the card that is actually about to fight.
            _odds.Recompute();

            _predictions.StakedOn = pick;
            _predictions.Stake = STAKE;
            _predictions.LockedMultiplier = MultiplierFor(pick);
            _predictions.LastPayout = 0;
            _predictions.Outcome = PredictionOutcome.Pending;
            _predictions.Bank.Value -= STAKE;
            _predictions.Revision.Value++;
        }

        private void OnMatchEnded(MatchEndedMessage message)
        {
            if (_predictions.Outcome != PredictionOutcome.Pending)
            {
                return;
            }

            // No winner is a draw on the bell. Nobody lost the bet, so nobody pays for it.
            if (message.WinnerId == MatchModel.NO_WINNER)
            {
                Settle(PredictionOutcome.Refunded, _predictions.Stake);
                return;
            }

            _predictions.Placed++;

            if (_roster.SeatOf(message.WinnerId) == _predictions.StakedOn)
            {
                int payout = Mathf.RoundToInt(_predictions.Stake * _predictions.LockedMultiplier);
                _predictions.Correct++;
                _predictions.BestPayout = Mathf.Max(_predictions.BestPayout, payout);
                Settle(PredictionOutcome.Won, payout);
                return;
            }

            Settle(PredictionOutcome.Lost, 0);
        }

        private void Settle(PredictionOutcome outcome, int payout)
        {
            _predictions.Outcome = outcome;
            _predictions.LastPayout = payout;
            _predictions.Bank.Value += payout;
            _predictions.Revision.Value++;
            _store?.Save(_predictions);
        }

        private void DropPickIfNotEntrant()
        {
            FighterProfile pick = _predictions.Pick.Value;

            if (pick != null && !_roster.IsEntrant(pick))
            {
                _predictions.Pick.Value = null;
            }
        }

        public void Dispose()
        {
            _disposables.Dispose();
        }
    }
}
