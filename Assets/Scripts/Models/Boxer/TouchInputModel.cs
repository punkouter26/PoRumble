using UnityEngine;

namespace PoRumble.Models
{
    /// <summary>
    /// What the on-screen controls are currently asking for.
    ///
    /// Exists because a boxer's movement and punches cannot be delivered by calling a system
    /// directly: they have to travel through the agent's action buffer, which is the single
    /// control path shared by the keyboard, the scripted brains and the trained policy. The
    /// touch view writes here and <see cref="PoRumble.Views.BoxerAgentView"/> reads it in the
    /// same place it reads the keyboard.
    ///
    /// Plain state with no logic, so it is trivially testable and the view stays a view.
    /// </summary>
    public sealed class TouchInputModel
    {
        /// <summary>Stick direction, magnitude 0..1. Drives both movement and facing.</summary>
        public Vector2 Move { get; set; }

        /// <summary>True while the punch button is held. Held input alternates arms.</summary>
        public bool PunchHeld { get; set; }

        /// <summary>True while the haymaker button is held. Releasing throws the wind-up.</summary>
        public bool ChargeHeld { get; set; }

        /// <summary>
        /// Set for one frame when the slip button is pressed, and cleared by whoever consumed
        /// it. An edge rather than a held flag: a slip is a single committed action with its
        /// own window and cooldown, so holding the button must not queue a stream of them.
        /// </summary>
        public bool DodgeRequested { get; set; }

        /// <summary>
        /// True once the on-screen controls exist and are driving input. Lets the agent ignore
        /// this model entirely on desktop rather than having touch state fight the keyboard.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>Clears everything, e.g. when the controls are hidden between matches.</summary>
        public void Clear()
        {
            Move = Vector2.zero;
            PunchHeld = false;
            ChargeHeld = false;
            DodgeRequested = false;
        }
    }
}
