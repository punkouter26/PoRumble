using System.Collections.Generic;
using UnityEngine;

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
            Momentum = 0f;
        }
    }

    /// <summary>
    /// The whole match's telemetry: a tally per fighter plus a short rolling history of each
    /// one's momentum, which is what the HUD draws as a sparkline.
    ///
    /// The history is a fixed ring buffer allocated once. An overlay that reports allocation
    /// rate must not allocate to do it, and the same applies to one that reports a fight:
    /// a growing List per fighter would put a GC spike into exactly the frames where punches
    /// are landing fastest.
    /// </summary>
    public sealed class FightStatsModel
    {
        /// <summary>
        /// Samples kept per fighter. At the sampling interval below this is about eight
        /// seconds of history, which is long enough to show a round turning and short enough
        /// that a sparkline forty pixels wide still has one pixel per sample on a phone.
        /// </summary>
        public const int HISTORY_LENGTH = 48;

        private readonly List<FighterStats> _stats = new(10);
        private readonly List<float[]> _momentumHistory = new(10);

        private int _writeIndex;
        private int _samplesWritten;

        /// <summary>Per-fighter tallies, indexed by seat rather than by boxer id.</summary>
        public IReadOnlyList<FighterStats> Stats => _stats;

        /// <summary>How many samples the ring buffer holds so far, capped at the length.</summary>
        public int SampleCount => Mathf.Min(_samplesWritten, HISTORY_LENGTH);

        /// <summary>
        /// Sizes the tables to the roster. Called once the boxers exist; calling it again with
        /// the same count keeps the buffers, since the ring is re-seated rather than rebuilt.
        /// </summary>
        public void Configure(int boxerCount)
        {
            while (_stats.Count < boxerCount)
            {
                _stats.Add(new FighterStats());
                _momentumHistory.Add(new float[HISTORY_LENGTH]);
            }

            while (_stats.Count > boxerCount)
            {
                _stats.RemoveAt(_stats.Count - 1);
                _momentumHistory.RemoveAt(_momentumHistory.Count - 1);
            }
        }

        /// <summary>The tally for one seat, or null when the index is outside the roster.</summary>
        public FighterStats For(int index)
        {
            return index >= 0 && index < _stats.Count ? _stats[index] : null;
        }

        /// <summary>
        /// Reads one fighter's momentum history oldest-first, so a caller can draw it left to
        /// right without knowing where the ring buffer's write head currently sits.
        ///
        /// Returns 0 for samples that have not been written yet, which draws as a flat line
        /// at the start of a match rather than as whatever the last match left behind.
        /// </summary>
        public float HistoryAt(int index, int sample)
        {
            if (index < 0 || index >= _momentumHistory.Count)
            {
                return 0f;
            }

            if (sample < 0 || sample >= HISTORY_LENGTH)
            {
                return 0f;
            }

            // The oldest sample sits at the write head once the buffer has wrapped, and at
            // zero before it has.
            int start = _samplesWritten >= HISTORY_LENGTH ? _writeIndex : 0;
            return _momentumHistory[index][(start + sample) % HISTORY_LENGTH];
        }

        /// <summary>Appends every fighter's current momentum to the history as one column.</summary>
        public void SampleHistory()
        {
            for (int index = 0; index < _stats.Count; index++)
            {
                _momentumHistory[index][_writeIndex] = _stats[index].Momentum;
            }

            _writeIndex = (_writeIndex + 1) % HISTORY_LENGTH;
            _samplesWritten++;
        }

        /// <summary>Wipes every tally and the whole history for a fresh match.</summary>
        public void Clear()
        {
            for (int index = 0; index < _stats.Count; index++)
            {
                _stats[index].Clear();

                float[] history = _momentumHistory[index];

                for (int sample = 0; sample < history.Length; sample++)
                {
                    history[sample] = 0f;
                }
            }

            _writeIndex = 0;
            _samplesWritten = 0;
        }
    }
}
