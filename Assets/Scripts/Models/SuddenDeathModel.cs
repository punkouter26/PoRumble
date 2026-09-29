namespace PoRumble.Models
{
    /// <summary>
    /// The fight clock and the state of the ropes, for the HUD. The ropes' actual position is
    /// <see cref="MatchModel.RingScale"/>, because that is what the clamp reads; this carries
    /// what a viewer is told about it.
    /// </summary>
    public sealed class SuddenDeathModel
    {
        /// <summary>Whole seconds the current fight has been live. Zero before the bell.</summary>
        public ReactiveProperty<int> FightSeconds { get; } = new(0);

        /// <summary>True once the ropes have started closing in.</summary>
        public ReactiveProperty<bool> Closing { get; } = new(false);
    }
}
