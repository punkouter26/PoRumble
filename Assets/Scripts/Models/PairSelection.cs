namespace PoRumble.Models
{
    /// <summary>Two fighters and how good a shot they make together.</summary>
    public readonly struct ScoredPair
    {
        public readonly int IdA;
        public readonly int IdB;
        public readonly float Score;

        public ScoredPair(int idA, int idB, float score)
        {
            IdA = idA;
            IdB = idB;
            Score = score;
        }

        public bool Involves(int boxerId)
        {
            return IdA == boxerId || IdB == boxerId;
        }

        public bool Is(int idA, int idB)
        {
            return (IdA == idA && IdB == idB) || (IdA == idB && IdB == idA);
        }
    }

    /// <summary>
    /// Picks the second fight for the picture-in-picture feed.
    ///
    /// Pure and static so the rule can be tested without a director or a scene. The rule is
    /// the one that makes a second feed worth having: it shows a fight that shares nobody with
    /// the main one. A corner feed on the same two fighters from a slightly different distance
    /// is a mirror, not coverage.
    /// </summary>
    public static class PairSelection
    {
        /// <summary>
        /// The best pair that involves neither main fighter, held against the pair already on
        /// the feed unless a rival beats it by <paramref name="switchMargin"/>.
        ///
        /// The margin is the same hysteresis the main pair gets, for the same reason: tension
        /// moves several times a second across dozens of pairs, and a feed that swapped on
        /// every hair's difference would flicker between fights too fast to follow either.
        /// </summary>
        public static bool TryPickSecondary(
            ScoredPair[] pairs,
            int count,
            int mainA,
            int mainB,
            int currentA,
            int currentB,
            float switchMargin,
            out ScoredPair chosen)
        {
            chosen = default;
            bool found = false;
            bool currentStillValid = false;
            ScoredPair current = default;

            for (int index = 0; index < count; index++)
            {
                ScoredPair pair = pairs[index];

                if (pair.Involves(mainA) || pair.Involves(mainB))
                {
                    continue;
                }

                if (pair.Is(currentA, currentB))
                {
                    current = pair;
                    currentStillValid = true;
                }

                if (!found || pair.Score > chosen.Score)
                {
                    chosen = pair;
                    found = true;
                }
            }

            if (!found)
            {
                return false;
            }

            if (currentStillValid && chosen.Score - current.Score < switchMargin)
            {
                chosen = current;
            }

            return true;
        }
    }
}
