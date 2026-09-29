namespace PoRumble.Models
{
    /// <summary>
    /// Where the match sits in the round loop that wraps a fight.
    ///
    /// <see cref="MatchPhase"/> answers "is the fight decided"; this answers "what is the
    /// player looking at". They are deliberately separate: the fight is over the instant the
    /// last opponent drops, but the results screen sits on top of that decided fight for a
    /// few seconds before anything restarts.
    /// </summary>
    /// <remarks>
    /// Title carries the value 5 rather than 0 so the four phases that existed first keep the
    /// numbers they already had. Nothing serializes this today, but renumbering an enum is the
    /// kind of change that is invisible until something does.
    /// </remarks>
    public enum MatchFlowPhase
    {
        /// <summary>
        /// The menu. Fighters are racked but nothing is running, the card can be changed, and
        /// the loop waits here for the player to ask for a fight.
        /// </summary>
        Title = 5,

        /// <summary>Fighters are racked and the countdown has not started.</summary>
        Introducing = 0,

        /// <summary>Counting down to the bell. Input is ignored.</summary>
        Countdown = 1,

        /// <summary>Live fight.</summary>
        Fighting = 2,

        /// <summary>Decided, holding on the knockout in slow motion.</summary>
        KnockoutHold = 3,

        /// <summary>Results are up and a restart is available.</summary>
        Results = 4
    }

    /// <summary>
    /// Round-loop state. Exists so the game scene has somewhere to go when a match ends:
    /// previously the match resolved, the banner appeared and nothing could ever happen
    /// again without leaving Play mode.
    /// </summary>
    public sealed class MatchFlowModel
    {
        public ReactiveProperty<MatchFlowPhase> Phase { get; } = new(MatchFlowPhase.Title);

        /// <summary>Whole seconds left on the countdown, for the "3 / 2 / 1 / FIGHT" caption.</summary>
        public ReactiveProperty<int> CountdownSeconds { get; } = new(0);

        /// <summary>Matches fought since the scene loaded, so the HUD can show a run count.</summary>
        public ReactiveProperty<int> MatchNumber { get; } = new(1);

        /// <summary>True while the fight is live and boxers should accept input.</summary>
        public bool IsFightLive => Phase.Value == MatchFlowPhase.Fighting;

        /// <summary>True once a restart is available to the player.</summary>
        public bool CanRestart => Phase.Value == MatchFlowPhase.Results;

        /// <summary>True while the menu is up and a fight can be started.</summary>
        public bool CanStartFight => Phase.Value == MatchFlowPhase.Title;

        /// <summary>
        /// True when the fight card may be opened: between matches only. Changing the roster
        /// mid-fight would re-seat contestants into chairs that are currently swinging.
        /// </summary>
        public bool CanOpenCard =>
            Phase.Value == MatchFlowPhase.Title || Phase.Value == MatchFlowPhase.Results;
    }
}
