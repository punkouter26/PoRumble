namespace PoRumble.Models
{
    /// <summary>
    /// One fighter's tally for the current match.
    ///
    /// Every field here is derived: nothing in it changes the simulation, and removing the
    /// whole model would leave the fight identical. That is deliberate — a telemetry board
    /// that can alter the thing it is reporting is a liability, and the policy is already
    /// calibrated against a ring this does not touch.
    /// </summary>
    public sealed class FighterStats
    {
        /// <summary>Punches started. Only <see cref="PunchThrownMessage"/> can supply this.</summary>
        public int Thrown { get; set; }

        /// <summary>Punches that reached a face.</summary>
        public int Landed { get; set; }

        /// <summary>Punches of theirs that ran into somebody's guard.</summary>
        public int Blocked { get; set; }

        /// <summary>Punches of theirs that went past a face without touching it.</summary>
        public int Evaded { get; set; }

        /// <summary>Punches this fighter stopped with their own arms.</summary>
        public int BlocksMade { get; set; }

        /// <summary>Punches this fighter slipped.</summary>
        public int Slips { get; set; }

        /// <summary>Punches landed inside a counter window.</summary>
        public int Counters { get; set; }

        /// <summary>Haymakers committed to, landed or not.</summary>
        public int Haymakers { get; set; }

        public int DamageDealt { get; set; }
        public int DamageTaken { get; set; }

        /// <summary>Opponents this fighter put out of the match.</summary>
        public int Knockouts { get; set; }

        /// <summary>
        /// Exponentially decayed damage differential — dealt minus taken, in hit points, over
        /// roughly the last few seconds. Positive means this fighter is winning the exchange
        /// happening right now, which is not the same thing as winning the match and is the
        /// number a broadcast actually shows.
        /// </summary>
        public float Momentum { get; set; }

        /// <summary>
        /// Share of thrown punches that landed, 0..1.
        ///
        /// Zero rather than undefined before the first punch, so the HUD never has to decide
        /// what to print for a fighter who has not moved yet.
        /// </summary>
        public float Accuracy => Thrown > 0 ? Landed / (float)Thrown : 0f;

        public void Clear()
        {
            Thrown = 0;
            Landed = 0;
            Blocked = 0;
            Evaded = 0;
            BlocksMade = 0;
            Slips = 0;
            Counters = 0;
            Haymakers = 0;
            DamageDealt = 0;
            DamageTaken = 0;
            Knockouts = 0;
            Momentum = 0f;
        }
    }
}
