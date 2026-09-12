namespace PoRumble.Models
{
    /// <summary>
    /// Whether the telemetry overlay is up.
    ///
    /// The overlay used to own this as a private field on its own view, flipped in place by the
    /// F3 handler. That was fine while a key was the only way in; it stopped being fine once
    /// the chrome bar grew a DEBUG button, because two views would then each hold their own
    /// idea of whether the sheet was showing and the button's label would drift out of step
    /// with the sheet it names.
    ///
    /// One reactive bool instead, on the same shape as <see cref="RosterModel.IsOpen"/>: the
    /// overlay renders it, the chrome bar labels itself from it, and neither needs to know the
    /// other exists.
    /// </summary>
    public sealed class DiagnosticsModel
    {
        public ReactiveProperty<bool> IsVisible { get; } = new(false);
    }
}
