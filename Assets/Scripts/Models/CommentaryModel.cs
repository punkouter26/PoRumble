namespace PoRumble.Models
{
    /// <summary>
    /// What the commentator reacts to.
    ///
    /// Every one of these is read off a message the fight was already publishing. Nothing here
    /// asks the simulation for anything it was not already saying, which is what keeps the
    /// commentary a pure observer of a ring the shipped policy is calibrated against.
    /// </summary>
    public enum CommentaryEvent
    {
        /// <summary>The opening bell.</summary>
        Bell,

        /// <summary>A punch landed inside a counter window.</summary>
        Counter,

        /// <summary>A wound-up haymaker landed.</summary>
        Haymaker,

        /// <summary>One fighter landed several punches in quick succession.</summary>
        Flurry,

        /// <summary>A fighter has dropped to the point of being nearly out.</summary>
        Hurt,

        /// <summary>A fighter has blocked or slipped several punches in a row.</summary>
        Defence,

        /// <summary>Somebody has been knocked out.</summary>
        Elimination,

        /// <summary>The field is down to two.</summary>
        FinalTwo,

        /// <summary>The match ended with a winner.</summary>
        Winner,

        /// <summary>The match ended with a winner the ratings did not favour.</summary>
        Upset,

        /// <summary>The match ended with nobody ahead.</summary>
        Draw
    }

    /// <summary>
    /// What the commentator is saying right now.
    ///
    /// Exists as a model rather than as state inside the view so the subtitle and the voice
    /// are one decision rather than two. They drift apart the moment they are chosen
    /// separately, and a subtitle that does not match the audio is worse than no subtitle.
    /// </summary>
    public sealed class CommentaryModel
    {
        /// <summary>
        /// The line to speak and print, or null when there is nothing to say.
        ///
        /// Reactive because a line is a discrete event the view has to act on exactly once -
        /// polling it would mean comparing strings every frame to discover that nothing had
        /// changed, and would miss a line that repeated.
        /// </summary>
        public ReactiveProperty<CommentaryCue> Cue { get; } = new(CommentaryCue.None);

        /// <summary>Clears the cue for a fresh match, so no line is inherited across the bell.</summary>
        public void Reset()
        {
            Cue.Value = CommentaryCue.None;
        }
    }

    /// <summary>
    /// One thing the commentator says: which line, and whose name goes in front of it.
    ///
    /// A struct carrying an id rather than a resolved clip, because Models depends on nothing
    /// and an AudioClip is a UnityEngine.Object. The view resolves both halves against the
    /// bank.
    /// </summary>
    public readonly struct CommentaryCue : System.IEquatable<CommentaryCue>
    {
        /// <summary>Which line, e.g. "hurt_2". Empty when there is nothing to say.</summary>
        public readonly string LineId;

        /// <summary>
        /// The fighter the line is about, or <see cref="DirectorModel.NOBODY"/> when it names
        /// nobody. A line whose text expects a name still plays without one if the seat turns
        /// out to be empty - the body reads as a sentence about "he" either way, which is why
        /// they are phrased that way.
        /// </summary>
        public readonly int SubjectId;

        /// <summary>
        /// Counts up on every cue. Two identical lines in a row are a real thing that happens
        /// - the same fighter gets hurt twice - and without this a ReactiveProperty comparing
        /// values would swallow the second one.
        /// </summary>
        public readonly int Sequence;

        public CommentaryCue(string lineId, int subjectId, int sequence)
        {
            LineId = lineId;
            SubjectId = subjectId;
            Sequence = sequence;
        }

        public bool HasLine => !string.IsNullOrEmpty(LineId);

        public static CommentaryCue None => new(null, DirectorModel.NOBODY, 0);

        /// <summary>
        /// Implemented explicitly, not for correctness but for cost. ReactiveProperty compares
        /// through EqualityComparer&lt;T&gt;.Default on every assignment, and the default for a
        /// struct carrying a reference field is ValueType.Equals - which compares by
        /// reflection and boxes to do it. This is on the path of every line the commentator
        /// says.
        /// </summary>
        public bool Equals(CommentaryCue other)
        {
            return Sequence == other.Sequence
                   && SubjectId == other.SubjectId
                   && LineId == other.LineId;
        }

        public override bool Equals(object obj)
        {
            return obj is CommentaryCue other && Equals(other);
        }

        public override int GetHashCode()
        {
            int hash = Sequence;
            hash = (hash * 397) ^ SubjectId;
            return (hash * 397) ^ (LineId != null ? LineId.GetHashCode() : 0);
        }
    }
}
