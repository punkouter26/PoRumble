using System.Text;
using UnityEngine;

namespace PoRumble.Models
{
    /// <summary>One second of a match, as the performance log records it.</summary>
    public readonly struct PerformanceSecond
    {
        /// <summary>Seconds since the log started, at the end of this sample.</summary>
        public readonly float Time;

        /// <summary>Mean unscaled frame time over the second, in milliseconds.</summary>
        public readonly float AverageMs;

        /// <summary>Worst single frame in the second, in milliseconds.</summary>
        public readonly float PeakMs;

        /// <summary>Managed heap growth over the second, in kilobytes. Collections read as zero, not negative.</summary>
        public readonly float AllocatedKb;

        public readonly int DrawCalls;
        public readonly int SetPassCalls;

        /// <summary>
        /// Android's PowerManager thermal status: 0 none, 1 light, 2 moderate, 3 severe,
        /// 4 critical, 5 emergency, 6 shutdown. -1 where the platform does not report one.
        /// </summary>
        public readonly int ThermalStatus;

        /// <summary>Battery temperature in Celsius, or NaN where it is not reported.</summary>
        public readonly float BatteryCelsius;

        /// <summary>Fighters still standing, so a frame-time rise can be read against how busy the ring was.</summary>
        public readonly int Alive;

        public PerformanceSecond(
            float time,
            float averageMs,
            float peakMs,
            float allocatedKb,
            int drawCalls,
            int setPassCalls,
            int thermalStatus,
            float batteryCelsius,
            int alive)
        {
            Time = time;
            AverageMs = averageMs;
            PeakMs = peakMs;
            AllocatedKb = allocatedKb;
            DrawCalls = drawCalls;
            SetPassCalls = setPassCalls;
            ThermalStatus = thermalStatus;
            BatteryCelsius = batteryCelsius;
            Alive = alive;
        }
    }

    /// <summary>
    /// A match's performance, one sample per second, for the log written at the final bell and
    /// the MATCH page of the diagnostics sheet.
    ///
    /// Exists because the overlay's own history is 120 frames - two seconds - which is the
    /// right window for a stutter and the wrong one for the failure a phone actually has: a
    /// build that holds 60fps for the first minute and 40 by the fifth because the device has
    /// heated up and throttled. That only shows as a trend over the whole match.
    ///
    /// A fixed ring allocated once, oldest overwritten, so a match that runs for an hour costs
    /// the same memory as one that runs for a minute and nothing is allocated while it runs.
    /// </summary>
    public sealed class PerformanceTraceModel
    {
        /// <summary>Thirty minutes of seconds. A ten-way with no bell can run long.</summary>
        public const int CAPACITY = 1800;

        private readonly PerformanceSecond[] _samples;

        private int _head;

        public PerformanceTraceModel() : this(CAPACITY)
        {
        }

        public PerformanceTraceModel(int capacity)
        {
            _samples = new PerformanceSecond[Mathf.Max(1, capacity)];
        }

        /// <summary>Samples held, up to the capacity.</summary>
        public int Count { get; private set; }

        public int Capacity => _samples.Length;

        /// <summary>The match number the trace belongs to, for naming the log file.</summary>
        public int MatchNumber { get; set; }

        /// <summary>Where the last log was written, or empty if none has been.</summary>
        public string LastLogPath { get; set; } = string.Empty;

        public void Clear()
        {
            _head = 0;
            Count = 0;
        }

        public void Add(in PerformanceSecond sample)
        {
            _samples[_head] = sample;
            _head = (_head + 1) % _samples.Length;

            if (Count < _samples.Length)
            {
                Count++;
            }
        }

        /// <summary>The sample at <paramref name="index"/>, 0 being the oldest held.</summary>
        public PerformanceSecond Get(int index)
        {
            int oldest = (_head - Count + _samples.Length) % _samples.Length;
            return _samples[(oldest + index) % _samples.Length];
        }

        /// <summary>Mean frame time over the samples held, in milliseconds.</summary>
        public float MeanMs()
        {
            return MeanMs(0, Count);
        }

        /// <summary>
        /// How much slower the end of the trace ran than its start: the mean frame time of the
        /// last <paramref name="window"/> samples minus that of the first. Positive is slower.
        ///
        /// This is the number that separates thermal throttling from a scene that is simply
        /// expensive - an expensive scene is slow from the first second, a throttling device
        /// gets slower. Zero until there are two full windows, because comparing a window with
        /// itself says nothing.
        /// </summary>
        public float DriftMs(int window)
        {
            if (window <= 0 || Count < window * 2)
            {
                return 0f;
            }

            return MeanMs(Count - window, window) - MeanMs(0, window);
        }

        /// <summary>The highest thermal status seen, or -1 when the platform reported none.</summary>
        public int WorstThermalStatus()
        {
            int worst = -1;

            for (int index = 0; index < Count; index++)
            {
                worst = Mathf.Max(worst, Get(index).ThermalStatus);
            }

            return worst;
        }

        /// <summary>Writes the trace as CSV, header first. The caller owns the builder, so nothing here allocates one.</summary>
        public void AppendCsv(StringBuilder builder)
        {
            builder.Append("time_s,avg_ms,peak_ms,alloc_kb,draw_calls,setpass,thermal_status,battery_c,alive\n");

            for (int index = 0; index < Count; index++)
            {
                PerformanceSecond sample = Get(index);

                builder.Append(sample.Time.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                       .Append(sample.AverageMs.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                       .Append(sample.PeakMs.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                       .Append(sample.AllocatedKb.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                       .Append(sample.DrawCalls).Append(',')
                       .Append(sample.SetPassCalls).Append(',')
                       .Append(sample.ThermalStatus).Append(',');

                // An empty field rather than "NaN", so a spreadsheet reads the column as numbers.
                if (!float.IsNaN(sample.BatteryCelsius))
                {
                    builder.Append(sample.BatteryCelsius.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                }

                builder.Append(',').Append(sample.Alive).Append('\n');
            }
        }

        private float MeanMs(int start, int length)
        {
            if (length <= 0)
            {
                return 0f;
            }

            float sum = 0f;

            for (int index = start; index < start + length; index++)
            {
                sum += Get(index).AverageMs;
            }

            return sum / length;
        }
    }
}
