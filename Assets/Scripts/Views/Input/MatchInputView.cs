using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// Match-level keys: Enter to fight or rematch, R back to the menu.
    ///
    /// Separate from the per-boxer controls in <see cref="BoxerAgentView"/> because this is
    /// not a boxer's input - it belongs to the match, works while the player's boxer is lying
    /// on the canvas, and must keep working when there is no human boxer at all.
    ///
    /// Keyboard only. It used to take a tap anywhere on a touchscreen as well, read straight off
    /// the device rather than through the UI, and that was a bug on every screen that has buttons:
    /// the press that landed on a pick chip or the FIGHT CARD button also started the fight, and a
    /// press on a results-screen button changed phase before the button ever saw its release. On
    /// a phone every one of these actions is now a button, which is the only thing a touch should
    /// ever mean.
    ///
    /// A View, per the input rules: it reads a key and calls a System. Whether any request is
    /// legal right now is entirely the System's decision; each is refused outside its phase.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchInputView : MonoBehaviour
    {
        private MatchFlowSystem _flowSystem;

        [Inject]
        public void Construct(MatchFlowSystem flowSystem)
        {
            _flowSystem = flowSystem;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (_flowSystem == null || keyboard == null)
            {
                return;
            }

            // wasPressedThisFrame throughout, not isPressed: a held key would otherwise restart
            // the match again on every frame of the results screen.
            if (keyboard.enterKey.wasPressedThisFrame)
            {
                // Rematch at the results, fight at the title. Each is refused outside its own
                // phase, so one press can never both dismiss the results and start a bout it was
                // not meant to.
                if (_flowSystem.TryRestart())
                {
                    _flowSystem.TryStartFight();
                    return;
                }

                _flowSystem.TryStartFight();
                return;
            }

            if (keyboard.rKey.wasPressedThisFrame)
            {
                _flowSystem.TryRestart();
            }
        }
    }
}
