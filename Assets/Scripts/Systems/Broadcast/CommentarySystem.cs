using System;
using System.Collections.Generic;
using MessagePipe;
using PoRumble.Models;
using UnityEngine;
using VContainer;

namespace PoRumble.Systems
{
    /// <summary>
    /// Decides what the commentator says.
    ///
    /// Silence is the biggest tell that a fight is a simulation rather than a sport, and
    /// commentary is also the only thing in the build that explains emergent behaviour to
    /// somebody who cannot read the policy. This is a pure observer of messages the ring was
    /// already publishing: it mutates no boxer, publishes nothing, and is never consulted by
    /// combat.
    ///
    /// Almost all of the work here is deciding when *not* to speak. A ten-way publishes
    /// several landed punches a second, and a commentator that answered each of them would be
    /// an unlistenable stutter. Three rules do that job:
    ///
    ///   - a priority, so a knockout can cut across a flurry but never the reverse;
    ///   - a hold, so a line that has started gets to finish;
    ///   - a cooldown after it, so there is air between sentences.
    /// </summary>
    public sealed class CommentarySystem : IDisposable
    {
        /// <summary>
        /// Seconds of quiet after a line before another may start. Roughly a breath: short
        /// enough that a knockdown and the reaction to it are one thought, long enough that
        /// the commentator is not talking over himself.
        /// </summary>
        private const float COOLDOWN_SECONDS = 0.65f;

        /// <summary>
        /// Assumed length of a line, in seconds, for the purpose of holding the floor. The
        /// system never sees the clip - that is the view's business and Models may not
        /// reference an AudioClip - so it reserves a plausible sentence and the view simply
        /// stops early if the audio was shorter. Overestimating costs a little silence;
        /// underestimating costs the end of every sentence.
        /// </summary>
        private const float ASSUMED_LINE_SECONDS = 1.9f;

        /// <summary>Landed punches by one fighter inside <see cref="FLURRY_WINDOW"/> that make a flurry.</summary>
        private const int FLURRY_PUNCHES = 3;

        private const float FLURRY_WINDOW = 1.6f;

        /// <summary>Blocks and slips in a row by one fighter worth remarking on.</summary>
        private const int DEFENCE_RUN = 3;

        private const float DEFENCE_WINDOW = 4f;

        /// <summary>
        /// Health fraction below which a fighter is "in trouble". Said once per fighter per
        /// match — a commentator who announces it every time a hurt man is touched sounds
        /// broken rather than concerned.
        /// </summary>
        private const float HURT_FRACTION = 0.25f;

        /// <summary>
        /// Rating gap that makes a win an upset. About the point where the table genuinely
        /// did not expect it, rather than the point where two fighters are merely unequal.
        /// </summary>
        private const float UPSET_RATING_GAP = 120f;

        private readonly MatchModel _match;
        private readonly CommentaryModel _commentary;
        private readonly CommentaryBank _bank;
        private readonly MatchFlowModel _flow;
        private readonly RosterModel _roster;
        private readonly RatingModel _ratings;
        private readonly BoxerConfig _config;
        private readonly CompositeDisposable _disposables = new();

        /// <summary>Per-fighter running tallies for the flurry and defence triggers.</summary>
        private readonly List<int> _recentLanded = new(10);
        private readonly List<float> _recentLandedAt = new(10);
        private readonly List<int> _recentStops = new(10);
        private readonly List<float> _recentStoppedAt = new(10);
        private readonly List<bool> _hurtCalled = new(10);

        /// <summary>
        /// Ids of the last two lines said, so the same variant does not come round twice.
        ///
        /// A fixed pair rather than a Queue: two is all the memory this wants (see
        /// <see cref="Remember"/>), and a plain array keeps the scan out of the project's ban
        /// on iterating non-List collections where it can be avoided.
        /// </summary>
        private readonly string[] _spoken = new string[2];

        private int _spokenIndex;

        /// <summary>
        /// A tiny xorshift rather than UnityEngine.Random, matching ScriptedBoxerBrain. The
        /// commentary is not part of training, but sharing the global generator with anything
        /// that is would make a training run depend on how much somebody talked.
        /// </summary>
        private uint _rng = 0x9E3779B9;

        private float _clock;
        private float _floorHeldUntil;
        private int _floorPriority;
        private int _sequence;

        [Inject]
        public CommentarySystem(
            MatchModel match,
            CommentaryModel commentary,
            CommentaryBank bank,
            MatchFlowModel flow,
            RosterModel roster,
            RatingModel ratings,
            BoxerConfig config,
            ISubscriber<PunchLandedMessage> landedSubscriber,
            ISubscriber<PunchBlockedMessage> blockedSubscriber,
            ISubscriber<BoxerDodgedMessage> dodgedSubscriber,
            ISubscriber<BoxerEliminatedMessage> eliminatedSubscriber,
            ISubscriber<MatchEndedMessage> endedSubscriber)
        {
            _match = match;
            _commentary = commentary;
            _bank = bank;
            _flow = flow;
            _roster = roster;
            _ratings = ratings;
            _config = config;

            landedSubscriber.Subscribe(OnPunchLanded).AddTo(_disposables);
            blockedSubscriber.Subscribe(OnPunchBlocked).AddTo(_disposables);
            dodgedSubscriber.Subscribe(OnBoxerDodged).AddTo(_disposables);
            eliminatedSubscriber.Subscribe(OnBoxerEliminated).AddTo(_disposables);
            endedSubscriber.Subscribe(OnMatchEnded).AddTo(_disposables);
            _match.Phase.Subscribe(OnMatchPhaseChanged).AddTo(_disposables);

            // The bell is the flow loop entering Fighting. Read off the phase rather than
            // through a hook on MatchFlowSystem, because that is already how the feedback
            // layer hears the bell and adding a second path would be one more thing that can
            // disagree about when a fight started.
            _flow.Phase.Subscribe(OnFlowPhaseChanged).AddTo(_disposables);
        }

        /// <summary>
        /// Advances the commentator's own clock. Ticked on unscaled time by MatchDirector,
        /// because the knockout hold slows the world down and a commentator timed on scaled
        /// time would hold the floor four times as long over exactly the moment he is most
        /// worth listening to.
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
            {
                return;
            }

            EnsureSized();
            _clock += deltaSeconds;
        }

        private void OnFlowPhaseChanged(MatchFlowPhase phase)
        {
            if (phase == MatchFlowPhase.Fighting)
            {
                Say(CommentaryEvent.Bell, DirectorModel.NOBODY);
            }
        }

        private void OnPunchLanded(PunchLandedMessage message)
        {
            EnsureSized();

            int attacker = IndexOf(message.AttackerId);
            int target = IndexOf(message.TargetId);

            // A counter and a haymaker are both specific enough to call on their own, and
            // both beat the generic flurry line - which is why they are tested first.
            if (message.IsCounter)
            {
                Say(CommentaryEvent.Counter, message.AttackerId);
            }
            else if (message.ChargeLevel >= _config.MinChargeToRelease)
            {
                Say(CommentaryEvent.Haymaker, message.AttackerId);
            }

            if (attacker >= 0)
            {
                // The run resets when the punches stop coming, so three landed over half a
                // minute is not a flurry. It has to be three inside the window.
                if (_clock - _recentLandedAt[attacker] > FLURRY_WINDOW)
                {
                    _recentLanded[attacker] = 0;
                }

                _recentLanded[attacker]++;
                _recentLandedAt[attacker] = _clock;

                if (_recentLanded[attacker] >= FLURRY_PUNCHES)
                {
                    _recentLanded[attacker] = 0;
                    Say(CommentaryEvent.Flurry, message.AttackerId);
                }
            }

            // Taking a punch ends a defensive run whatever else happens.
            if (target >= 0)
            {
                _recentStops[target] = 0;
                CheckHurt(target, message.TargetId);
            }
        }

        private void OnPunchBlocked(PunchBlockedMessage message)
        {
            RegisterStop(message.BlockerId);
        }

        private void OnBoxerDodged(BoxerDodgedMessage message)
        {
            RegisterStop(message.BoxerId);
        }

        /// <summary>A punch stopped, by guard or by slip. Enough of them in a row is a line.</summary>
        private void RegisterStop(int boxerId)
        {
            EnsureSized();

            int index = IndexOf(boxerId);

            if (index < 0)
            {
                return;
            }

            if (_clock - _recentStoppedAt[index] > DEFENCE_WINDOW)
            {
                _recentStops[index] = 0;
            }

            _recentStops[index]++;
            _recentStoppedAt[index] = _clock;

            if (_recentStops[index] >= DEFENCE_RUN)
            {
                _recentStops[index] = 0;
                Say(CommentaryEvent.Defence, boxerId);
            }
        }

        /// <summary>
        /// Calls a fighter as being in trouble, once each per match.
        ///
        /// Latched rather than tested every punch, because health only goes one way inside a
        /// match: without the latch every subsequent punch on a hurt fighter re-announces that
        /// he is hurt, which is the single fastest way to make a commentator sound broken.
        /// </summary>
        private void CheckHurt(int index, int boxerId)
        {
            if (_hurtCalled[index])
            {
                return;
            }

            BoxerModel boxer = _match.Boxers[index];

            if (!boxer.IsAlive.Value)
            {
                return;
            }

            float fraction = boxer.Health.Value / (float)Mathf.Max(1, _config.MaxHealth);

            if (fraction > HURT_FRACTION)
            {
                return;
            }

            _hurtCalled[index] = true;
            Say(CommentaryEvent.Hurt, boxerId);
        }

        private void OnBoxerEliminated(BoxerEliminatedMessage message)
        {
            Say(CommentaryEvent.Elimination, message.BoxerId);

            // Said on the way down to two rather than when the match ends, so it lands while
            // there is still a fight to introduce.
            if (_match.CountAlive() == 2)
            {
                Say(CommentaryEvent.FinalTwo, DirectorModel.NOBODY);
            }
        }

        private void OnMatchEnded(MatchEndedMessage message)
        {
            if (message.WinnerId == MatchModel.NO_WINNER)
            {
                Say(CommentaryEvent.Draw, DirectorModel.NOBODY);
                return;
            }

            Say(IsUpset(message.WinnerId) ? CommentaryEvent.Upset : CommentaryEvent.Winner,
                message.WinnerId);
        }

        /// <summary>
        /// Whether the ratings expected the winner to lose, by a clear margin, to somebody who
        /// was actually in the ring.
        ///
        /// Measured against the best-rated opponent rather than the field average: beating one
        /// fighter far above you is the thing worth calling, and an average is dragged down by
        /// however many novices happened to be seated.
        /// </summary>
        private bool IsUpset(int winnerId)
        {
            FighterProfile winner = _roster.SeatOf(winnerId);

            // No card, no ratings, no upsets. The training arenas take this path.
            if (winner == null)
            {
                return false;
            }

            float winnerRating = _ratings.RatingOf(winner.Id);
            float best = winnerRating;

            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                if (boxers[boxerIndex].Id == winnerId)
                {
                    continue;
                }

                FighterProfile rival = _roster.SeatOf(boxers[boxerIndex].Id);

                // The cyclic deal seats a short card twice, so the winner can be their own
                // rival. Beating yourself is not an upset.
                if (rival == null || rival.Id == winner.Id)
                {
                    continue;
                }

                float rating = _ratings.RatingOf(rival.Id);

                if (rating > best)
                {
                    best = rating;
                }
            }

            return best - winnerRating >= UPSET_RATING_GAP;
        }

        /// <summary>
        /// Puts a line up if the commentator is free, or if this one outranks what he is
        /// already saying.
        ///
        /// Returns quietly rather than queueing. A queue sounds wrong here: by the time a
        /// backed-up line about a flurry reached the front, the flurry would be ten seconds
        /// gone and the commentator would be describing a fight nobody is watching any more.
        /// Dropping the line is the honest answer.
        /// </summary>
        private void Say(CommentaryEvent trigger, int subjectId)
        {
            if (_bank == null)
            {
                return;
            }

            int priority = PriorityOf(trigger);

            if (_clock < _floorHeldUntil && priority <= _floorPriority)
            {
                return;
            }

            CommentaryEntry entry = PickVariant(trigger);

            if (entry == null)
            {
                return;
            }

            _floorPriority = priority;
            _floorHeldUntil = _clock + ASSUMED_LINE_SECONDS + COOLDOWN_SECONDS;
            _sequence++;

            Remember(entry.Id);
            _commentary.Cue.Value = new CommentaryCue(entry.Id, subjectId, _sequence);
        }

        /// <summary>
        /// Picks one of an event's variants, avoiding the handful most recently used.
        ///
        /// Drawn at random from what is left rather than from a shuffle bag. A shuffle
        /// guarantees no immediate repeat, but it also guarantees every variant is heard
        /// before any repeats - which over a long session is its own audible pattern, and is
        /// the same reasoning the impact sound banks already record.
        /// </summary>
        private CommentaryEntry PickVariant(CommentaryEvent trigger)
        {
            IReadOnlyList<CommentaryEntry> variants = _bank.CollectVariants(trigger);

            if (variants.Count == 0)
            {
                return null;
            }

            CommentaryEntry fallback = null;
            int eligible = 0;

            // Counted first so the pick is uniform over the eligible ones rather than biased
            // toward whichever happens to sit first in the list.
            for (int variantIndex = 0; variantIndex < variants.Count; variantIndex++)
            {
                CommentaryEntry entry = variants[variantIndex];

                if (entry.Clip == null)
                {
                    continue;
                }

                fallback = entry;

                if (!WasRecentlySpoken(entry.Id))
                {
                    eligible++;
                }
            }

            if (eligible == 0)
            {
                return fallback;
            }

            int chosen = (int)(NextFloat() * eligible);

            if (chosen >= eligible)
            {
                chosen = eligible - 1;
            }

            for (int variantIndex = 0; variantIndex < variants.Count; variantIndex++)
            {
                CommentaryEntry entry = variants[variantIndex];

                if (entry.Clip == null || WasRecentlySpoken(entry.Id))
                {
                    continue;
                }

                if (chosen == 0)
                {
                    return entry;
                }

                chosen--;
            }

            return fallback;
        }

        private bool WasRecentlySpoken(string id)
        {
            for (int index = 0; index < _spoken.Length; index++)
            {
                if (_spoken[index] == id)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Two is deliberately short. The bank holds four variants of each event, so
        /// remembering more than two would leave a frequently-fired event with a single legal
        /// choice — turning variation off exactly where it is most needed.
        /// </summary>
        private void Remember(string id)
        {
            _spoken[_spokenIndex] = id;
            _spokenIndex = (_spokenIndex + 1) % _spoken.Length;
        }

        /// <summary>
        /// How hard a line pushes for the floor. The ordering is the editorial judgement in
        /// the whole feature: a knockout is worth interrupting anything for, and a good bit of
        /// defensive work is worth interrupting nothing.
        /// </summary>
        private static int PriorityOf(CommentaryEvent trigger)
        {
            switch (trigger)
            {
                case CommentaryEvent.Winner:
                case CommentaryEvent.Upset:
                case CommentaryEvent.Draw:
                    return 5;
                case CommentaryEvent.Elimination:
                    return 4;
                case CommentaryEvent.FinalTwo:
                case CommentaryEvent.Bell:
                    return 3;
                case CommentaryEvent.Haymaker:
                case CommentaryEvent.Hurt:
                    return 2;
                case CommentaryEvent.Counter:
                    return 1;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Sizes the per-fighter tallies to the roster.
        ///
        /// Reconciled on demand rather than only on the phase change, for the reason
        /// FightStatsSystem documents at length: MatchModel starts in InProgress, so the phase
        /// subscription fires before a single boxer has been spawned.
        /// </summary>
        private void EnsureSized()
        {
            int count = _match.Boxers.Count;

            while (_recentLanded.Count < count)
            {
                _recentLanded.Add(0);
                _recentLandedAt.Add(0f);
                _recentStops.Add(0);
                _recentStoppedAt.Add(0f);
                _hurtCalled.Add(false);
            }
        }

        private void OnMatchPhaseChanged(MatchPhase phase)
        {
            if (phase != MatchPhase.InProgress)
            {
                return;
            }

            EnsureSized();

            for (int index = 0; index < _recentLanded.Count; index++)
            {
                _recentLanded[index] = 0;
                _recentLandedAt[index] = 0f;
                _recentStops[index] = 0;
                _recentStoppedAt[index] = 0f;
                _hurtCalled[index] = false;
            }

            for (int index = 0; index < _spoken.Length; index++)
            {
                _spoken[index] = null;
            }

            _floorHeldUntil = 0f;
            _floorPriority = 0;
            _commentary.Reset();
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

        private float NextFloat()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / (float)0x1000000;
        }

        public void Dispose()
        {
            _disposables.Dispose();
        }
    }
}
