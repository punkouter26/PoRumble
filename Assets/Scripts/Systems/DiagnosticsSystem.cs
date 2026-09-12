using PoRumble.Models;
using VContainer;

namespace PoRumble.Systems
{
    /// <summary>
    /// Owns the telemetry overlay's visibility.
    ///
    /// Small on purpose, and a System rather than a field on a view for the reason
    /// <see cref="RosterSystem"/> is: two separate views ask for the overlay now - F3 and a
    /// three-finger tap from the overlay itself, the DEBUG button from the chrome bar - and
    /// the one that asks must not be the one that decides.
    /// </summary>
    public sealed class DiagnosticsSystem
    {
        private readonly DiagnosticsModel _diagnostics;

        [Inject]
        public DiagnosticsSystem(DiagnosticsModel diagnostics)
        {
            _diagnostics = diagnostics;
        }

        public void Toggle()
        {
            _diagnostics.IsVisible.Value = !_diagnostics.IsVisible.Value;
        }
    }
}
