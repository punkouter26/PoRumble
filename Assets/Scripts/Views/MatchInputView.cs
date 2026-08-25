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

        [Inject]
        public void Construct(MatchFlowSystem flowSystem)
        {
            _flowSystem = flowSystem;
        }

        private void Update()
        {
            if (_flowSystem == null)
            {
                return;
            }

            // wasPressedThisFrame throughout, not isPressed: a held key or finger would
            // otherwise restart the match again on every frame of the results screen.
            if (RestartRequested())
            {
                _flowSystem.TryRestart();
            }
        }

        private static bool RestartRequested()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null &&
                (keyboard.rKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame))
            {
                return true;
            }

            // Touch is the only way to restart on a phone, where there is no keyboard at all.
            Touchscreen touchscreen = Touchscreen.current;

            return touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame;
        }
    }
}
