using System;
using System.Collections.Generic;

namespace PoRumble.Models
{
    /// <summary>
    /// One finished match, as a line of the match log. Public lower-case fields because this is
    /// the on-disk shape: JsonUtility writes fields, and the names are the column names any
    /// later report reads.
    /// </summary>
    [Serializable]
    public sealed class MatchLogRecord
    {
        public string utc;
        public int entrants;
        public int winnerSeat;
        public bool knockout;
        public float seconds;
        public List<BoxerLogRow> boxers = new();
    }

    /// <summary>What one seat did in a match: its result, its output and how it moved.</summary>
    [Serializable]
    public sealed class BoxerLogRow
    {
        public int seat;
        public string fighter;
        public string control;

        /// <summary>1 for the winner. Survivors of a bell decision share 2.</summary>
        public int placement;
        public float survivedSeconds;

        public int thrown;
        public int landed;
        public int blocked;
        public int evaded;
        public int haymakers;
        public int counters;
        public int slips;
        public int damageDealt;
        public int damageTaken;
        public int knockouts;

        /// <summary>World units travelled while standing.</summary>
        public float distance;

        /// <summary>Share of standing time spent within reach of the ropes.</summary>
        public float ropesShare;

        /// <summary>
        /// Mean position as a fraction of the way to each wall. A symmetric task should average
        /// near zero; a policy that drifts to one corner shows up here.
        /// </summary>
        public float meanX;
        public float meanY;
    }
}
