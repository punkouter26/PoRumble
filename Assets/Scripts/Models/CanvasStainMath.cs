using UnityEngine;

namespace PoRumble.Models
{
    /// <summary>
    /// How much of a mark a patch of canvas carries for the damage done on it.
    ///
    /// Absolute rather than normalised to the hottest cell, and that is the difference between
    /// this and the results-screen fight map. The map answers "where, relative to the rest of
    /// the match", so it is scaled to its own peak. A stain answers "what has happened to this
    /// patch of canvas", which has to accumulate: normalising it would make the first jab of
    /// the match paint a full-strength stain, then fade that stain back out as heavier
    /// exchanges moved the peak elsewhere - a canvas that cleans itself mid-fight.
    ///
    /// Pure and static for the reason <see cref="FramingMath"/> is: the view is only ever
    /// checked by watching a match, and these rules are cheap to pin in a test.
    /// </summary>
    public static class CanvasStainMath
    {
        /// <summary>
        /// Opacity of the stain for a cell holding <paramref name="damage"/> hit points.
        ///
        /// Saturating, so a corner where three fighters were finished does not simply go
        /// opaque black: the first few punches mark the canvas clearly and each one after adds
        /// less, which is how a real stain darkens.
        /// </summary>
        public static float Opacity(float damage, float damageForFullStain, float maxOpacity)
        {
            if (damage <= 0f || damageForFullStain <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(maxOpacity) * (1f - Mathf.Exp(-damage / damageForFullStain));
        }

        /// <summary>
        /// How far a stain has turned from sweat to blood, 0 to 1.
        ///
        /// Light damage marks the canvas with sweat - a darkening, no colour. Only a patch that
        /// has taken real punishment reads red. Everything reading red from the first exchange
        /// would be the same mistake the blood spray avoids by firing only on heavy hits.
        /// </summary>
        public static float BloodShare(float damage, float bloodFrom, float bloodFull)
        {
            if (bloodFull <= bloodFrom)
            {
                return damage >= bloodFrom ? 1f : 0f;
            }

            return Mathf.Clamp01((damage - bloodFrom) / (bloodFull - bloodFrom));
        }
    }
}
