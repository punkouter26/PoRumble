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
    public enum MatchFlowPhase
    {
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
        public ReactiveProperty<MatchFlowPhase> Phase { get; } = new(MatchFlowPhase.Introducing);

        /// <summary>Whole seconds left on the countdown, for the "3 / 2 / 1 / FIGHT" caption.</summary>
        public ReactiveProperty<int> CountdownSeconds { get; } = new(0);

        /// <summary>Matches fought since the scene loaded, so the HUD can show a run count.</summary>
        public ReactiveProperty<int> MatchNumber { get; } = new(1);

        /// <summary>True while the fight is live and boxers should accept input.</summary>
        public bool IsFightLive => Phase.Value == MatchFlowPhase.Fighting;

        /// <summary>True once a restart is available to the player.</summary>
        public bool CanRestart => Phase.Value == MatchFlowPhase.Results;
    }
}
