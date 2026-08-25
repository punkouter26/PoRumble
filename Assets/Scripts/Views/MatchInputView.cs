using PoRumble.Systems;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// Match-level input: the restart key.
    ///
    /// Separate from the per-boxer controls in <see cref="BoxerAgentView"/> because this is
    /// not a boxer's input - it belongs to the match, works while the player's boxer is lying
    /// on the canvas, and must keep working when there is no human boxer at all.
    ///
    /// A View, per the input rules: it reads a key and calls a System. No logic of its own -
    /// whether a restart is legal right now is entirely MatchFlowSystem's decision.
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

            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
            {
                return;
            }

            // wasPressedThisFrame, not isPressed: a held key would otherwise restart the match
            // again on every frame of the results screen.
            if (keyboard.rKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame)
            {
                _flowSystem.TryRestart();
            }
        }
    }
}
