namespace PoRumble.Models
{
    /// <summary>
    /// Which frame, if any, a HUD control has already claimed the pointer on.
    ///
    /// Exists because <see cref="PoRumble.Views.MatchInputView"/> deliberately does not
    /// hit-test: a tap anywhere restarts at the results screen, which is the right rule on a
    /// phone and the reason it needs no button. The moment the HUD grew buttons of its own
    /// that stopped being free - a tap on the menu button at the results screen would open
    /// the card *and* start the next match, because both are listening to the same press.
    ///
    /// A frame number rather than a bool, so nothing has to remember to clear it. The claim is
    /// only true for the frame it was made on and expires by itself; a consumer that never
    /// runs cannot leave input dead.
    /// </summary>
    public sealed class HudPointerModel
    {
        private const int NEVER = -1;

        private int _claimedFrame = NEVER;

        /// <summary>Called by a HUD control that has just handled a press.</summary>
        public void Claim(int frame) => _claimedFrame = frame;

        /// <summary>True when a HUD control already handled the press on this frame.</summary>
        public bool IsClaimed(int frame) => _claimedFrame == frame;
    }
}
