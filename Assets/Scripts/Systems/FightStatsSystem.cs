using System;
using System.Collections.Generic;
using MessagePipe;
using PoRumble.Models;
using UnityEngine;
using VContainer;

namespace PoRumble.Systems
{
    /// <summary>
    /// Keeps the fight's telemetry: what each fighter threw, what landed, what was stopped,
    /// and which way the exchange is currently running.
    ///
    /// Entirely a subscriber. It publishes nothing, mutates no boxer and is never consulted by
    /// combat, so the ring behaves identically whether it exists or not - which is what makes
    /// it safe to add to a project whose shipped policy is calibrated against that ring.
    ///
    /// Resolved eagerly in GameLifetimeScope for the same reason CombatSystem and MatchSystem
    /// are: nothing injects it, so VContainer would never construct it and every match would
    /// report a blank board.
    /// </summary>
    public sealed class FightStatsSystem : IDisposable
    {
        /// <summary>
        /// Seconds for the momentum reading to decay to roughly a third of its value with no
        /// further punches. Short enough that a fighter who has stopped working visibly loses
        /// the exchange, long enough that the bar does not empty between the two halves of a
        /// combination.
        /// </summary>
        private const float MOMENTUM_TIME_CONSTANT = 2.5f;

        /// <summary>
        /// Seconds between history samples. Chosen against FightStatsModel.HISTORY_LENGTH so
        /// the sparkline covers about eight seconds - one exchange and its aftermath.
        /// </summary>
        private const float SAMPLE_INTERVAL = 8f / FightStatsModel.HISTORY_LENGTH;

        private readonly MatchModel _match;
        private readonly FightStatsModel _stats;
        private readonly CompositeDisposable _disposables = new();

        private float _sampleTimer;

        [Inject]
        public FightStatsSystem(
            MatchModel match,
            FightStatsModel stats,
            ISubscriber<PunchThrownMessage> thrownSubscriber,
            ISubscriber<PunchLandedMessage> landedSubscriber,
            ISubscriber<PunchBlockedMessage> blockedSubscriber,
            ISubscriber<PunchEvadedMessage> evadedSubscriber,
            ISubscriber<BoxerDodgedMessage> dodgedSubscriber,
            ISubscriber<HaymakerThrownMessage> haymakerSubscriber)
        {
            _match = match;
            _stats = stats;

            thrownSubscriber.Subscribe(OnPunchThrown).AddTo(_disposables);
            landedSubscriber.Subscribe(OnPunchLanded).AddTo(_disposables);
            blockedSubscriber.Subscribe(OnPunchBlocked).AddTo(_disposables);
            evadedSubscriber.Subscribe(OnPunchEvaded).AddTo(_disposables);
            dodgedSubscriber.Subscribe(OnBoxerDodged).AddTo(_disposables);
            haymakerSubscriber.Subscribe(OnHaymakerThrown).AddTo(_disposables);

            // A fresh match starts from a blank board. Driven off the phase rather than off
            // whichever path re-racked the ring, because the game restarts through
            // MatchFlowSystem and training through MatchDirector, and both have to clear it.
            _match.Phase.Subscribe(OnMatchPhaseChanged).AddTo(_disposables);
        }

        /// <summary>
        /// Decays every fighter's momentum and appends a history column at a fixed interval.
        ///
        /// Ticked on unscaled time by MatchDirector. Scaled time would stall the decay through
        /// hitstop and the knockout hold - which is exactly when the board is on screen and
        /// being read.
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
            {
                return;
            }

            // The roster does not exist when this system is built. MatchModel starts in
            // InProgress, so the phase subscription fires immediately on subscribe - before
            // SpawnSystem has put a single boxer in the ring - and sizes the tables to zero.
            // Every tally then silently goes nowhere and the board draws a blank column for
            // the whole match, which is exactly how it first ran.
            //
            // Reconciled here rather than by deferring the subscription, because the same
            // mismatch appears legitimately whenever a scene changes its roster size, and one
            // count comparison a frame is cheaper than being clever about construction order.
            if (_stats.Stats.Count != _match.Boxers.Count)
            {
                _stats.Configure(_match.Boxers.Count);
            }

            IReadOnlyList<FighterStats> stats = _stats.Stats;

            // Exponential rather than linear, so a big flurry decays quickly at first and then
            // lingers. A linear bleed makes one heavy punch read the same as four light ones
            // for as long as it takes to run out, which is not what happened.
            float retained = Mathf.Exp(-deltaSeconds / MOMENTUM_TIME_CONSTANT);

            for (int index = 0; index < stats.Count; index++)
            {
                stats[index].Momentum *= retained;
            }

            _sampleTimer += deltaSeconds;

            while (_sampleTimer >= SAMPLE_INTERVAL)
            {
                _sampleTimer -= SAMPLE_INTERVAL;
                _stats.SampleHistory();
            }
        }

        /// <summary>
        /// Hit points traded between two fighters over the last few seconds, as the camera
        /// director's tension score wants it.
        ///
        /// An approximation, and deliberately so: momentum is per fighter rather than per
        /// pair, so this counts damage either of them has recently been involved in with
        /// anyone. In a ten-way that occasionally flatters a pair who both just came out of
        /// separate scraps - but they are then two hurt fighters standing next to each other,
        /// which is a shot worth taking anyway. A true per-pair ledger would be a ten-by-ten
        /// matrix decayed every tick to sharpen a camera cut, and that is not a trade worth
        /// making.
        /// </summary>
        public float RecentDamageBetween(int indexA, int indexB)
        {
            FighterStats a = _stats.For(indexA);
            FighterStats b = _stats.For(indexB);

            float total = 0f;

            if (a != null)
            {
                total += Mathf.Abs(a.Momentum);
            }

            if (b != null)
            {
                total += Mathf.Abs(b.Momentum);
            }

            return total;
        }

        private void OnMatchPhaseChanged(MatchPhase phase)
        {
            if (phase != MatchPhase.InProgress)
            {
                return;
            }

            _stats.Configure(_match.Boxers.Count);
            _stats.Clear();
            _sampleTimer = 0f;
        }

        private void OnPunchThrown(PunchThrownMessage message)
        {
            FighterStats thrower = StatsFor(message.BoxerId);

            if (thrower == null)
            {
                return;
            }

            thrower.Thrown++;
        }

        private void OnPunchLanded(PunchLandedMessage message)
        {
            FighterStats attacker = StatsFor(message.AttackerId);
            FighterStats target = StatsFor(message.TargetId);

            if (attacker != null)
            {
                attacker.Landed++;
                attacker.DamageDealt += message.Damage;
                attacker.Momentum += message.Damage;

                if (message.IsCounter)
                {
                    attacker.Counters++;
                }
            }

            if (target != null)
            {
                target.DamageTaken += message.Damage;
                target.Momentum -= message.Damage;
            }
        }

        private void OnPunchBlocked(PunchBlockedMessage message)
        {
            FighterStats attacker = StatsFor(message.AttackerId);
            FighterStats blocker = StatsFor(message.BlockerId);

            if (attacker != null)
            {
                attacker.Blocked++;
            }

            if (blocker != null)
            {
                blocker.BlocksMade++;
            }
        }

        private void OnPunchEvaded(PunchEvadedMessage message)
        {
            FighterStats attacker = StatsFor(message.AttackerId);

            if (attacker != null)
            {
                attacker.Evaded++;
            }
        }

        /// <summary>
        /// A slip counts where it was started, not where it happened to work. The evade
        /// message fires for any punch that went past a face, including ones the defender
        /// never reacted to, so counting slips off that would credit standing still.
        /// </summary>
        private void OnBoxerDodged(BoxerDodgedMessage message)
        {
            FighterStats dodger = StatsFor(message.BoxerId);

            if (dodger != null)
            {
                dodger.Slips++;
            }
        }

        private void OnHaymakerThrown(HaymakerThrownMessage message)
        {
            FighterStats thrower = StatsFor(message.BoxerId);

            if (thrower != null)
            {
                thrower.Haymakers++;
            }
        }

        /// <summary>
        /// The tally for a boxer id. Scanned rather than indexed because a boxer's id is its
        /// own identity and nothing guarantees it equals its seat - the same reason
        /// CombatSystem looks its targets up instead of subscripting the roster.
        /// </summary>
        private FighterStats StatsFor(int boxerId)
        {
            return _stats.For(IndexOf(boxerId));
        }

        private int IndexOf(int boxerId)
        {
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int index = 0; index < boxers.Count; index++)
            {
                if (boxers[index].Id == boxerId)
                {
                    return index;
                }
            }

            return -1;
        }

        public void Dispose()
        {
            _disposables.Dispose();
        }
    }
}
