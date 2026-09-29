namespace PoRumble.Models
{
    /// <summary>
    /// Which panel the portrait carousel shows next.
    ///
    /// The carousel only ever rotates between panels that want to be up: a panel its own
    /// view has hidden for the phase, or whose document the fight has cleared, is skipped
    /// rather than given a blank turn.
    /// </summary>
    public static class HudCarouselMath
    {
        /// <summary>
        /// The next eligible slot after <paramref name="current"/>, wrapping. Returns
        /// <paramref name="current"/> when it is the only eligible one, and -1 when nothing is.
        /// A current of -1 starts the search from the first slot.
        /// </summary>
        public static int NextEligible(bool[] eligible, int current)
        {
            if (eligible == null || eligible.Length == 0)
            {
                return -1;
            }

            int count = eligible.Length;
            int start = current < 0 || current >= count ? count - 1 : current;

            for (int offset = 1; offset <= count; offset++)
            {
                int candidate = (start + offset) % count;

                if (eligible[candidate])
                {
                    return candidate;
                }
            }

            return -1;
        }

        /// <summary>
        /// The eligible slot before <paramref name="current"/>, wrapping - a swipe the other way.
        /// Same contract as <see cref="NextEligible"/>.
        /// </summary>
        public static int PreviousEligible(bool[] eligible, int current)
        {
            if (eligible == null || eligible.Length == 0)
            {
                return -1;
            }

            int count = eligible.Length;
            int start = current < 0 || current >= count ? 0 : current;

            for (int offset = 1; offset <= count; offset++)
            {
                int candidate = ((start - offset) % count + count) % count;

                if (eligible[candidate])
                {
                    return candidate;
                }
            }

            return -1;
        }

        /// <summary>
        /// The slot to show when the set of eligible panels has changed under the current one.
        /// Keeps the current slot if it is still eligible - a panel should not be yanked off
        /// screen mid-read because a different panel came up - and otherwise moves on.
        /// </summary>
        public static int Resolve(bool[] eligible, int current)
        {
            if (eligible != null && current >= 0 && current < eligible.Length && eligible[current])
            {
                return current;
            }

            return NextEligible(eligible, current);
        }
    }
}
