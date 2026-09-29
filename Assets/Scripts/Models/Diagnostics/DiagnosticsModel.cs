namespace PoRumble.Models
{
    /// <summary>
    /// Whether the telemetry overlay is up, and whether it has anything to report.
    ///
    /// The overlay used to own its visibility as a private field on its own view, flipped in
    /// place by the F3 handler. That was fine while a key was the only way in; it stopped being
    /// fine once the chrome bar grew a DEBUG button, because two views would then each hold their
    /// own idea of whether the sheet was showing and the button's label would drift out of step
    /// with the sheet it names.
    ///
    /// One reactive bool instead: the overlay renders it, the chrome bar labels itself from it, and neither needs to know the
    /// other exists.
    ///
    /// <see cref="FindingCount"/> is the same idea for the verdict. The overlay ranks what is
    /// wrong whether or not it is open, and the chrome bar colours the frame counter from the
    /// count - so a broken build shows on the one number that is always on screen.
    /// </summary>
    public sealed class DiagnosticsModel
    {
        public ReactiveProperty<bool> IsVisible { get; } = new(false);

        /// <summary>How many problems the diagnostics verdict currently ranks. Zero is all clear.</summary>
        public ReactiveProperty<int> FindingCount { get; } = new(0);
    }
}
