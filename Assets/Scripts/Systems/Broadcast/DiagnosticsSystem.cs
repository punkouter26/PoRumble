using PoRumble.Models;
using VContainer;

namespace PoRumble.Systems
{
    /// <summary>
    /// Owns the telemetry overlay's visibility.
    ///
    /// Small on purpose, and a System rather than a field on a view because two separate views
    /// ask for the overlay now - F3 and a
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

        /// <summary>
        /// Records how many problems the verdict found on its latest pass. Only a change is
        /// written, so the chrome bar's subscriber is not woken four times a second to be told
        /// the same number.
        /// </summary>
        public void ReportFindings(int count)
        {
            int clamped = count < 0 ? 0 : count;

            if (_diagnostics.FindingCount.Value != clamped)
            {
                _diagnostics.FindingCount.Value = clamped;
            }
        }
    }
}
