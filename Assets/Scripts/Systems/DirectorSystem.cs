using System;
using System.Collections.Generic;
using MessagePipe;
using PoRumble.Models;
using UnityEngine;
using VContainer;

namespace PoRumble.Systems
{
    /// <summary>
    /// Decides what the camera is looking at and which shot it is on.
    ///
    /// A single continuous tracking shot reads as a debug view rather than as coverage of a
    /// sport: a broadcast establishes wide, tightens onto the exchange that matters, and cuts
    /// on the knockout. This picks the pair and the shot; two views act on the decision, and
    /// neither of them makes one of its own.
    ///
    /// Three things keep it watchable rather than merely correct, and all three are the
    /// difference between a director and a twitch:
    ///
    ///   - a minimum shot length, so a shot is always on screen long enough to be read;
    ///   - hysteresis on the pair, because tension moves several times a second across
    ///     forty-five pairs and the best one by a hair changes constantly;
    ///   - a hard preemption for impacts, which is the one case where cutting instantly is
    ///     the right answer.
    ///
    /// Ticked on unscaled time. The knockout hold slows the world down, and a director timed
    /// on scaled time would hold its impact cut for four times as long as it asked for -
    /// exactly the trap MatchFlowSystem's loop already documents.
    /// </summary>
    public sealed class DirectorSystem : IDisposable
    {
        /// <summary>
        /// Shortest a shot may stay on screen. Below about a second a cut reads as a glitch
        /// rather than as an edit, and the viewer never finds the fighters before the frame
        /// changes again.
        /// </summary>
        private const float MIN_SHOT_SECONDS = 1.6f;

        /// <summary>How long an impact cut holds before the director goes back to work.</summary>
        private const float IMPACT_SHOT_SECONDS = 1.15f;

        /// <summary>
        /// How much better a rival pair must score before the camera abandons the one it is
        /// watching. The same idea as SpectatorCameraView's focus-switch margin and for the
        /// same reason: without it the framing chases the leader of a race that changes hands
        /// on every landed punch.
        /// </summary>
        private const float PAIR_SWITCH_MARGIN = 0.12f;

        /// <summary>Tension at or above which the pair earns a tight two-shot.</summary>
        private const float DUEL_TENSION = 0.55f;

        /// <summary>
        /// Tension below which the director gives up on the pair entirely and goes wide.
        /// Distinct from simply being under the duel threshold: this is "there is no fight",
        /// not "the fight is quiet".
        /// </summary>
        private const float WIDE_TENSION = 0.14f;

        /// <summary>
        /// Charge at or above which a landed punch counts as a haymaker worth cutting to.
        /// Read off the message rather than from the config's release minimum, because what
        /// earns a cut is the punch having been visibly wound up, not merely having been
        /// legal to throw.
        /// </summary>
        private const float IMPACT_CHARGE = 0.6f;

        /// <summary>
        /// Tension the second fight must reach before it earns a feed. Above <see cref="WIDE_TENSION"/>
        /// on purpose: the main camera settles for a quiet pair because it has to show
        /// something, and the corner feed does not - an empty inset is better than two fighters
        /// circling at range in a box that pulls the eye off the real fight.
        /// </summary>
        private const float SECOND_SHOW_TENSION = 0.24f;

        /// <summary>Below this the feed closes. The gap to the show threshold is hysteresis, so it does not blink.</summary>
        private const float SECOND_HIDE_TENSION = 0.12f;

        private readonly MatchModel _match;
        private readonly DirectorModel _director;
        private readonly MatchFlowModel _flow;
        private readonly FightStatsSystem _stats;
        private readonly BoxerConfig _config;
        private readonly CompositeDisposable _disposables = new();

        /// <summary>Seconds left on an impact cut. Zero when the director is free to choose.</summary>
        private float _impactHold;

        /// <summary>
        /// Every living pair scored on the last pass, kept so the second fight can be chosen
        /// from the same scores the main one was. Grown only when the roster outgrows it, so a
        /// ten-way allocates it once.
        /// </summary>
        private ScoredPair[] _pairs = Array.Empty<ScoredPair>();
        private int _pairCount;

        /// <summary>Seconds since the feed last opened or closed, so it holds for a readable minimum.</summary>
        private float _secondElapsed;

        [Inject]
        public DirectorSystem(
            MatchModel match,
            DirectorModel director,
            MatchFlowModel flow,
            FightStatsSystem stats,
            BoxerConfig config,
            ISubscriber<PunchLandedMessage> landedSubscriber,
            ISubscriber<BoxerEliminatedMessage> eliminatedSubscriber)
        {
            _match = match;
            _director = director;
            _flow = flow;
            _stats = stats;
            _config = config;

            landedSubscriber.Subscribe(OnPunchLanded).AddTo(_disposables);
            eliminatedSubscriber.Subscribe(OnBoxerEliminated).AddTo(_disposables);
            _match.Phase.Subscribe(OnMatchPhaseChanged).AddTo(_disposables);
        }

        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
            {
                return;
            }

            _director.ShotElapsed += deltaSeconds;
            _secondElapsed += deltaSeconds;

            // An impact cut owns the camera outright while it runs. Nothing is re-scored
            // underneath it: the point of the shot is that it does not move.
            if (_impactHold > 0f)
            {
                _impactHold -= deltaSeconds;
                return;
            }

            ChooseFocus();
            ChooseShot();
            ChooseSecond();
        }

        /// <summary>
        /// Follows a fighter the viewer tapped, or lets go if they tap the one already followed.
        /// Refused for a fighter who is out - there is nothing to follow - and between fights,
        /// when the camera holds the wide shot anyway. Returns whether anything changed.
        /// </summary>
        public bool TogglePin(int boxerId)
        {
            if (_director.PinnedId.Value == boxerId)
            {
                _director.PinnedId.Value = DirectorModel.NOBODY;
                return true;
            }

            if (!_flow.IsFightLive || !IsAlive(boxerId))
            {
                return false;
            }

            _director.PinnedId.Value = boxerId;

            // Cut straight to them. Waiting out the minimum shot length reads as the tap not
            // having registered.
            ChooseFocus();
            SetShot(PinnedShot(), 0f);
            return true;
        }

        /// <summary>
        /// Scores every living pair and keeps the best, unless the pair already on screen is
        /// within <see cref="PAIR_SWITCH_MARGIN"/> of it.
        ///
        /// Quadratic in the roster, which is forty-five pairs in a ten-way and ninety scalar
        /// terms per pair - a few thousand multiplies a frame, none of them allocating. That
        /// is cheap enough not to warrant a spatial structure, and a spatial structure over
        /// ten fighters in a forty-unit ring would cost more to maintain than it saved.
        /// </summary>
        private void ChooseFocus()
        {
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;
            float engagementRange = _config.ArmReach + _config.HeadOffset + _config.BodyRadius;
            float threatRange = engagementRange * _config.DodgeThreatRangeScale;

            int bestA = DirectorModel.NOBODY;
            int bestB = DirectorModel.NOBODY;
            float bestScore = float.MinValue;
            float currentScore = float.MinValue;

            int pairCapacity = boxers.Count * (boxers.Count - 1) / 2;

            if (_pairs.Length < pairCapacity)
            {
                _pairs = new ScoredPair[pairCapacity];
            }

            _pairCount = 0;

            for (int indexA = 0; indexA < boxers.Count; indexA++)
            {
                BoxerModel a = boxers[indexA];

                if (!a.IsAlive.Value)
                {
                    continue;
                }

                for (int indexB = indexA + 1; indexB < boxers.Count; indexB++)
                {
                    BoxerModel b = boxers[indexB];

                    if (!b.IsAlive.Value)
                    {
                        continue;
                    }

                    bool threatened =
                        ThreatMath.IsPunchIncoming(boxers, a, threatRange, _config.MinChargeToRelease)
                        || ThreatMath.IsPunchIncoming(boxers, b, threatRange, _config.MinChargeToRelease);

                    float score = TensionMath.ScorePair(new PairTension(
                        a.Position,
                        b.Position,
                        a.Health.Value,
                        b.Health.Value,
                        a.Charge.Value,
                        b.Charge.Value,
                        _config.MaxHealth,
                        engagementRange,
                        _stats.RecentDamageBetween(indexA, indexB),
                        threatened));

                    _pairs[_pairCount++] = new ScoredPair(a.Id, b.Id, score);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestA = a.Id;
                        bestB = b.Id;
                    }

                    if (IsCurrentPair(a.Id, b.Id))
                    {
                        currentScore = score;
                    }
                }
            }

            if (TryFollowPinned())
            {
                return;
            }

            // Nobody is paired up: either one fighter is left or the ring is empty. Hold on
            // whoever is still standing rather than snapping to the origin, which is the same
            // choice SpectatorCameraView makes when it cannot measure a fight.
            if (bestA == DirectorModel.NOBODY)
            {
                _director.FocusId = FirstAlive();
                _director.RivalId = DirectorModel.NOBODY;
                _director.Tension = 0f;
                return;
            }

            // Hold the pair already on screen unless something is decisively better, so a
            // switch is an event rather than a tie-break.
            if (currentScore > float.MinValue && bestScore - currentScore < PAIR_SWITCH_MARGIN)
            {
                _director.Tension = currentScore;
                return;
            }

            _director.FocusId = bestA;
            _director.RivalId = bestB;
            _director.Tension = bestScore;
        }

        /// <summary>
        /// Turns the tension reading into a shot, subject to the minimum shot length.
        ///
        /// The flow phase overrides it outright: between matches there is no exchange to
        /// tighten onto, and a tight shot over a results banner frames two fighters standing
        /// still. Training has no flow loop and sits in Fighting from the first step, so this
        /// costs it nothing.
        /// </summary>
        private void ChooseShot()
        {
            ShotType wanted;

            if (!_flow.IsFightLive && _flow.Phase.Value != MatchFlowPhase.KnockoutHold)
            {
                wanted = ShotType.Wide;
            }
            else if (_director.PinnedId.Value != DirectorModel.NOBODY)
            {
                wanted = PinnedShot();
            }
            else if (!_director.HasPair || _director.Tension < WIDE_TENSION)
            {
                wanted = ShotType.Wide;
            }
            else if (_director.Tension >= DUEL_TENSION)
            {
                wanted = ShotType.Duel;
            }
            else
            {
                wanted = ShotType.Tracking;
            }

            SetShot(wanted, MIN_SHOT_SECONDS);
        }

        /// <summary>
        /// Picks the fight for the corner feed and decides whether it is worth showing.
        ///
        /// Chosen after the main pair, from the same scores, and never sharing a fighter with
        /// it. That is also what makes the swap happen by itself: when the corner fight heats
        /// up past the main one by the switch margin, <see cref="ChooseFocus"/> takes it for
        /// the main camera, and the fight it displaced is now the best disjoint pair and moves
        /// into the corner.
        ///
        /// Only during a live fight. The knockout hold is one moment on one fighter, and a
        /// second feed over it would split the eye exactly when it should not be split.
        /// </summary>
        private void ChooseSecond()
        {
            ScoredPair second = default;
            bool found = false;

            if (_flow.IsFightLive && _director.HasPair)
            {
                found = PairSelection.TryPickSecondary(
                    _pairs,
                    _pairCount,
                    _director.FocusId,
                    _director.RivalId,
                    _director.SecondFocusId,
                    _director.SecondRivalId,
                    PAIR_SWITCH_MARGIN,
                    out second);
            }

            if (!found)
            {
                _director.SecondFocusId = DirectorModel.NOBODY;
                _director.SecondRivalId = DirectorModel.NOBODY;
                _director.SecondTension = 0f;
                SetSecondVisible(false, force: true);
                return;
            }

            _director.SecondFocusId = second.IdA;
            _director.SecondRivalId = second.IdB;
            _director.SecondTension = second.Score;

            if (_director.ShowSecond.Value)
            {
                SetSecondVisible(second.Score >= SECOND_HIDE_TENSION, force: false);
            }
            else
            {
                SetSecondVisible(second.Score >= SECOND_SHOW_TENSION, force: false);
            }
        }

        /// <summary>
        /// Opens or closes the feed, holding each state for the minimum shot length unless
        /// forced. Forced when the fight it was showing no longer exists - a feed held open on
        /// a pair that has been knocked apart shows nothing.
        /// </summary>
        private void SetSecondVisible(bool visible, bool force)
        {
            if (_director.ShowSecond.Value == visible)
            {
                return;
            }

            if (!force && _secondElapsed < MIN_SHOT_SECONDS)
            {
                return;
            }

            _director.ShowSecond.Value = visible;
            _secondElapsed = 0f;
        }

        /// <summary>
        /// A landed haymaker is worth cutting to, and an ordinary jab is not. Without the
        /// charge test the director would cut on every punch in a ten-way, which is several a
        /// second and would make the camera unwatchable in exactly the moments it is meant to
        /// be serving.
        /// </summary>
        private void OnPunchLanded(PunchLandedMessage message)
        {
            if (message.ChargeLevel < IMPACT_CHARGE)
            {
                return;
            }

            CutToImpact(message.TargetId, message.AttackerId);
        }

        private void OnBoxerEliminated(BoxerEliminatedMessage message)
        {
            CutToImpact(message.BoxerId, message.EliminatedById);
        }

        /// <summary>
        /// Takes the camera to the tight shot immediately, centred on whoever just took it.
        /// Preempts the minimum shot length deliberately: a knockout that the camera reaches
        /// a second and a half late is a knockout the camera missed.
        /// </summary>
        private void CutToImpact(int subjectId, int otherId)
        {
            // A viewer following one fighter asked not to be taken elsewhere. Their fighter's
            // own knockouts and haymakers still cut; everybody else's do not.
            int pinned = _director.PinnedId.Value;

            if (pinned != DirectorModel.NOBODY && pinned != subjectId && pinned != otherId)
            {
                return;
            }

            _director.FocusId = subjectId;
            _director.RivalId = otherId;
            _impactHold = IMPACT_SHOT_SECONDS;
            SetShot(ShotType.Impact, 0f);
        }

        /// <summary>
        /// Writes a shot if it has changed and the one on screen has had its time. Resets the
        /// elapsed clock only on a genuine change, so holding the same shot keeps accumulating
        /// rather than restarting every frame.
        /// </summary>
        private void SetShot(ShotType shot, float minimumHold)
        {
            if (_director.Shot.Value == shot)
            {
                return;
            }

            if (_director.ShotElapsed < minimumHold)
            {
                return;
            }

            _director.Shot.Value = shot;
            _director.ShotElapsed = 0f;
        }

        /// <summary>
        /// Frames the pinned fighter's best exchange from the pairs just scored. Lets go of a
        /// pin whose fighter has gone down, so the camera goes back to the ring's best fight
        /// rather than holding on a body on the canvas.
        /// </summary>
        private bool TryFollowPinned()
        {
            int pinned = _director.PinnedId.Value;

            if (pinned == DirectorModel.NOBODY)
            {
                return false;
            }

            if (!IsAlive(pinned))
            {
                _director.PinnedId.Value = DirectorModel.NOBODY;
                return false;
            }

            int rival = DirectorModel.NOBODY;
            float best = float.MinValue;

            for (int index = 0; index < _pairCount; index++)
            {
                ScoredPair pair = _pairs[index];

                if (pair.IdA != pinned && pair.IdB != pinned)
                {
                    continue;
                }

                if (pair.Score > best)
                {
                    best = pair.Score;
                    rival = pair.IdA == pinned ? pair.IdB : pair.IdA;
                }
            }

            _director.FocusId = pinned;
            _director.RivalId = rival;
            _director.Tension = rival == DirectorModel.NOBODY ? 0f : best;
            return true;
        }

        /// <summary>
        /// Never wide while a fighter is pinned: the camera only follows a focus on a tracking
        /// or tighter shot, so a wide pin would follow nobody.
        /// </summary>
        private ShotType PinnedShot()
        {
            return _director.HasPair && _director.Tension >= DUEL_TENSION ? ShotType.Duel : ShotType.Tracking;
        }

        private bool IsAlive(int boxerId)
        {
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int index = 0; index < boxers.Count; index++)
            {
                if (boxers[index].Id == boxerId)
                {
                    return boxers[index].IsAlive.Value;
                }
            }

            return false;
        }

        private bool IsCurrentPair(int idA, int idB)
        {
            return (_director.FocusId == idA && _director.RivalId == idB)
                   || (_director.FocusId == idB && _director.RivalId == idA);
        }

        private int FirstAlive()
        {
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int index = 0; index < boxers.Count; index++)
            {
                if (boxers[index].IsAlive.Value)
                {
                    return boxers[index].Id;
                }
            }

            return DirectorModel.NOBODY;
        }

        private void OnMatchPhaseChanged(MatchPhase phase)
        {
            if (phase != MatchPhase.InProgress)
            {
                return;
            }

            _director.Reset();
            _impactHold = 0f;
            _pairCount = 0;
            _secondElapsed = 0f;
        }

        public void Dispose()
        {
            _disposables.Dispose();
        }
    }
}
