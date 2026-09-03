using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// Match-level input: the restart key, or a tap on a touchscreen.
    ///
    /// Separate from the per-boxer controls in <see cref="BoxerAgentView"/> because this is
    /// not a boxer's input - it belongs to the match, works while the player's boxer is lying
    /// on the canvas, and must keep working when there is no human boxer at all.
    ///
    /// A View, per the input rules: it reads a key and calls a System. No logic of its own -
    /// whether a restart is legal right now is entirely MatchFlowSystem's decision, which is
    /// what makes it safe to accept a tap anywhere on screen rather than hit-testing a button:
    /// outside the results screen the call is simply refused.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchInputView : MonoBehaviour
    {
        private MatchFlowSystem _flowSystem;
        private RosterSystem _rosterSystem;
        private RosterModel _roster;
        private MatchFlowModel _flow;

        [Inject]
        public void Construct(
            MatchFlowSystem flowSystem,
            RosterSystem rosterSystem,
            RosterModel roster,
            MatchFlowModel flow)
        {
            _flowSystem = flowSystem;
            _rosterSystem = rosterSystem;
            _roster = roster;
            _flow = flow;
        }

        private void Update()
        {
            if (_flowSystem == null)
            {
                return;
            }

            // The card, before anything else: opening it must not also be read as a request to
            // start or restart a match.
            if (RosterToggleRequested())
            {
                _rosterSystem.Toggle();
                return;
            }

            // wasPressedThisFrame throughout, not isPressed: a held key or finger would
            // otherwise restart the match again on every frame of the results screen.
            if (!ConfirmRequested())
            {
                return;
            }

            // One gesture, two meanings, resolved by phase rather than by asking the player to
            // learn two. Each is refused outside its own phase, so a single tap can never both
            // dismiss the results and start the next bout.
            if (_flowSystem.TryRestart())
            {
                return;
            }

            _flowSystem.TryStartFight();
        }

        private bool RosterToggleRequested()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
            {
                return true;
            }

            // A phone has no Tab key, and for a long time that meant the fight card - the whole
            // contestant-selection screen - simply could not be opened in the shipping build.
            // The on-screen button in the card panel is the discoverable way in; this two-finger
            // tap is the shortcut for anyone who finds it.
            //
            // Only between matches: mid-fight the roster must not be re-seated underneath the
            // fighters currently swinging.
            Touchscreen touchscreen = Touchscreen.current;

            if (touchscreen == null || !_flow.CanOpenCard)
            {
                return false;
            }

            return touchscreen.touches.Count > 1
                   && touchscreen.touches[1].press.wasPressedThisFrame;
        }

        private bool ConfirmRequested()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null &&
                (keyboard.rKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame))
            {
                return true;
            }

            // Touch is the only confirmation on a phone, where there is no keyboard at all.
            // Ignored while the card is up, or the tap that picked a fighter would also start
            // the fight.
            Touchscreen touchscreen = Touchscreen.current;

            return touchscreen != null
                   && !_roster.IsOpen.Value
                   && touchscreen.primaryTouch.press.wasPressedThisFrame;
        }
    }
}
