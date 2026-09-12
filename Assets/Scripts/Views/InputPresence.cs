using UnityEngine;
using UnityEngine.InputSystem;

namespace PoRumble.Views
{
    /// <summary>
    /// Which input the player actually has, for the panels that name a control in their text.
    ///
    /// Telling somebody to press a key they do not have is a dead end, and it is the kind that
    /// looks like a bug in the game rather than in the prompt: the results screen said
    /// "PRESS R TO CONTINUE" on a phone, and the title screen said "ENTER TO FIGHT" under two
    /// buttons that were the only way to do either.
    ///
    /// The test used to be <c>Touchscreen.current != null &amp;&amp; Keyboard.current == null</c>,
    /// which is the right shape and the wrong reading. **Android reports a Keyboard device on
    /// every phone**, because the volume and back buttons arrive as key events and the Input
    /// System builds a Keyboard to carry them - so the second half of that test is false on
    /// hardware that has no keys to type on, and both prompts shipped naming keyboard controls
    /// to a touchscreen. Verified on a Pixel 9 Pro rather than reasoned about: the menu hint
    /// rendered its keyboard line on a device with nothing attached.
    ///
    /// So the mobile platform is what separates "a keyboard exists as a device" from "a keyboard
    /// exists as a thing you can type on". It is deliberately not a compile-time define, and the
    /// case it is still protecting is the original one: a desktop that happens to have a
    /// touchscreen has a keyboard sitting right there and must not be told to tap.
    /// </summary>
    internal static class InputPresence
    {
        /// <summary>
        /// True when tapping is the only thing the player can do - a touchscreen is present and
        /// there is no keyboard they could actually type on.
        /// </summary>
        internal static bool IsTouchOnly()
        {
            if (Touchscreen.current == null)
            {
                return false;
            }

            return Application.isMobilePlatform || Keyboard.current == null;
        }

        /// <summary>
        /// True when there is a keyboard worth naming in a prompt. The inverse of
        /// <see cref="IsTouchOnly"/> only where a touchscreen exists, so a plain desktop with no
        /// touchscreen at all still answers yes.
        /// </summary>
        internal static bool HasUsableKeyboard()
        {
            return Keyboard.current != null && !IsTouchOnly();
        }
    }
}
