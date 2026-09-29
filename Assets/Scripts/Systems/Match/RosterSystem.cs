using PoRumble.Models;
using VContainer;

namespace PoRumble.Systems
{
    /// <summary>
    /// Owns the roster screen: when it may open, and what changing the card is allowed to do.
    ///
    /// Exists so the rule lives somewhere other than a view. Re-dealing the ring swaps every
    /// fighter's face, controller and physical attributes, and doing that to boxers who are
    /// mid-exchange would be incoherent - so the card can only be changed between matches,
    /// which is checked here rather than trusted to whatever opened the panel.
    /// </summary>
    public sealed class RosterSystem
    {
        private readonly RosterModel _roster;
        private readonly MatchFlowModel _flow;

        [Inject]
        public RosterSystem(RosterModel roster, MatchFlowModel flow)
        {
            _roster = roster;
            _flow = flow;
        }

        /// <summary>
        /// Shows or hides the card. Refused while a fight is live, so the returned bool is
        /// worth reading: it says whether anything actually happened.
        /// </summary>
        public bool Toggle()
        {
            if (_roster.Available.Count == 0)
            {
                return false;
            }

            if (!_roster.IsOpen.Value && _flow.IsFightLive)
            {
                return false;
            }

            _roster.IsOpen.Value = !_roster.IsOpen.Value;
            return true;
        }

        public void Close()
        {
            _roster.IsOpen.Value = false;
        }

        /// <summary>
        /// Adds or drops a contestant. Returns false when the change was refused — dropping
        /// below two entrants would seat one fighter ten times and the match could never
        /// resolve.
        /// </summary>
        public bool ToggleEntrant(FighterProfile profile)
        {
            return _roster.Toggle(profile);
        }
    }
}
