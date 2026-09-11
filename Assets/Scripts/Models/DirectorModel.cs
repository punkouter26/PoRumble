namespace PoRumble.Models
{
    /// <summary>
    /// The shot the camera is currently on. Ordered loosely from widest to tightest, which is
    /// also how much the director is willing to interrupt: a cut to <see cref="Impact"/> takes
    /// precedence over everything, a drop back to <see cref="Wide"/> happens only when there
    /// is nothing to watch.
    /// </summary>
    public enum ShotType
    {
        /// <summary>The whole ring. Between matches, and while the field is still spread out.</summary>
        Wide,

        /// <summary>Following one exchange at a readable distance. The default.</summary>
        Tracking,

        /// <summary>Tight two-shot: the pair are trading and nothing else matters.</summary>
        Duel,

        /// <summary>A hard cut to a second camera on a knockout or a landed haymaker.</summary>
        Impact
    }

    /// <summary>
    /// What the camera director has decided: which two fighters are the fight, how good a
    /// fight it is, and which shot is on.
    ///
    /// Owned by DirectorSystem and read by both camera views. It exists as a model rather
    /// than as state inside the camera so that two views - the framing camera and the cut
    /// camera - can act on one decision instead of each making their own and disagreeing.
    /// </summary>
    public sealed class DirectorModel
    {
        /// <summary>
        /// Used where a fighter id is expected and there is nobody. Matches the convention
        /// MatchModel already uses for "no winner".
        /// </summary>
        public const int NOBODY = -1;

        /// <summary>
        /// The shot currently held. Reactive because a cut is a discrete event that two views
        /// and, in time, the commentary need to hear about; polling it would mean the cut
        /// camera checking a float every frame to find out nothing had changed.
        /// </summary>
        public ReactiveProperty<ShotType> Shot { get; } = new(ShotType.Wide);

        /// <summary>The fighter the camera is on. <see cref="NOBODY"/> when the ring is empty.</summary>
        public int FocusId { get; set; } = NOBODY;

        /// <summary>Who the focus is fighting. <see cref="NOBODY"/> when they are alone.</summary>
        public int RivalId { get; set; } = NOBODY;

        /// <summary>
        /// How good the current pair is, 0..1, straight off <see cref="TensionMath.ScorePair"/>.
        ///
        /// A plain float rather than a ReactiveProperty: it moves every single tick, and
        /// waking a subscriber sixty times a second so it can redraw nothing is the same
        /// waste BoxerModel.CounterWindow avoids for the same reason.
        /// </summary>
        public float Tension { get; set; }

        /// <summary>
        /// Seconds the current shot has been held. The director's minimum shot length is
        /// enforced against this, and it is the single thing that separates a camera that
        /// cuts from one that twitches.
        /// </summary>
        public float ShotElapsed { get; set; }

        /// <summary>True when the director has picked out a genuine pair rather than a lone fighter.</summary>
        public bool HasPair => FocusId != NOBODY && RivalId != NOBODY;

        /// <summary>Clears the decision for a fresh match, so no shot is inherited across the bell.</summary>
        public void Reset()
        {
            FocusId = NOBODY;
            RivalId = NOBODY;
            Tension = 0f;
            ShotElapsed = 0f;
            Shot.Value = ShotType.Wide;
        }
    }
}
