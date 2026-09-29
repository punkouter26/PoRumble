namespace PoRumble.Models
{
    /// <summary>
    /// Which two contestants headline the tale of the tape before the bell.
    ///
    /// A ten-way has no main event, so the intro has to choose one worth showing: the
    /// viewer's pick against the favourite they are betting against, or with no pick the two
    /// favourites. Pure so the choice is testable; the view only draws it.
    /// </summary>
    public static class HeadlineMath
    {
        public const int NONE = -1;

        /// <summary>
        /// Picks two distinct indices into <paramref name="odds"/>. The viewer's pick, when there
        /// is one, is always first; the other side is the highest-odds contestant who is not the
        /// pick. Returns false when fewer than two contestants are on the card.
        /// </summary>
        public static bool Choose(float[] odds, int count, int pickIndex, out int first, out int second)
        {
            first = NONE;
            second = NONE;

            if (odds == null || count < 2)
            {
                return false;
            }

            if (pickIndex >= 0 && pickIndex < count)
            {
                first = pickIndex;
                second = Best(odds, count, pickIndex);
                return second != NONE;
            }

            first = Best(odds, count, NONE);
            second = Best(odds, count, first);
            return first != NONE && second != NONE;
        }

        private static int Best(float[] odds, int count, int exclude)
        {
            int best = NONE;

            for (int index = 0; index < count; index++)
            {
                if (index == exclude)
                {
                    continue;
                }

                if (best == NONE || odds[index] > odds[best])
                {
                    best = index;
                }
            }

            return best;
        }
    }
}
