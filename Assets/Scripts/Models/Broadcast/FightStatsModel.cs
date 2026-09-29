using System.Collections.Generic;
using UnityEngine;

namespace PoRumble.Models
{
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
