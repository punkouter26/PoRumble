using UnityEngine;

namespace PoRumble.Models
{
    /// <summary>
    /// When the ropes start closing and how far in they are at any moment of a fight.
    ///
    /// The game has no bell - a ten-way runs until one fighter is left - and measured on this
    /// build that took minutes, with long plateaus where the last three circled and blocked each
    /// other. Closing the ropes pushes the fight towards an ending: it still has to be won with
    /// punches, there is just less and less room to avoid throwing them. <see cref="BELL_SECONDS"/>
    /// is the backstop for survivors who stop throwing them anyway.
    ///
    /// Pure and static, like FramingMath and TensionMath, so the schedule is testable without a
    /// scene.
    /// </summary>
    public static class SuddenDeathMath
    {
        /// <summary>Seconds of fight before the ropes start to move.</summary>
        public const float START_SECONDS = 60f;

        /// <summary>Seconds the ropes take to close from the full ring to the smallest one.</summary>
        public const float CLOSE_SECONDS = 60f;

        /// <summary>
        /// The smallest ring, as a fraction of the full one. At the shipped 8.5 half extent this
        /// is a 5-unit square: room for a pair to trade, none for anybody to hide.
        /// </summary>
        public const float MIN_SCALE = 0.3f;

        /// <summary>
        /// Seconds of fight at which the bell finally decides it on health.
        ///
        /// The ropes were meant to guarantee the ending on their own, and measured against the
        /// shipped policy they do not: three survivors on 1, 14 and 12 health stood apart in
        /// the smallest ring for over nine minutes without landing a punch. A full minute at
        /// the smallest ring is the fighters' last chance to settle it themselves.
        /// </summary>
        public const float BELL_SECONDS = START_SECONDS + CLOSE_SECONDS + 60f;

        /// <summary>The ring scale after <paramref name="fightSeconds"/> of fighting.</summary>
        public static float RingScaleAt(float fightSeconds)
        {
            float progress = Mathf.Clamp01((fightSeconds - START_SECONDS) / CLOSE_SECONDS);

            // Eased in, so the first few seconds of movement are slow enough to read as the
            // ropes starting to move rather than as the camera jumping.
            float eased = progress * progress * (3f - 2f * progress);
            return Mathf.Lerp(1f, MIN_SCALE, eased);
        }

        /// <summary>True once the ropes have started to move.</summary>
        public static bool IsClosing(float fightSeconds)
        {
            return fightSeconds >= START_SECONDS;
        }

        /// <summary>True once the fight has run long enough for the bell to decide it.</summary>
        public static bool IsBell(float fightSeconds)
        {
            return fightSeconds >= BELL_SECONDS;
        }

        /// <summary>Whole seconds until the ropes start moving, never below zero.</summary>
        public static int SecondsUntilClose(float fightSeconds)
        {
            return Mathf.Max(0, Mathf.CeilToInt(START_SECONDS - fightSeconds));
        }
    }
}
