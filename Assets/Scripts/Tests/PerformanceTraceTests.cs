using System.Text;
using NUnit.Framework;
using PoRumble.Models;

namespace PoRumble.Tests
{
    /// <summary>
    /// The per-second match trace behind the MATCH diagnostics page and the CSV each match
    /// leaves on the device.
    /// </summary>
    public sealed class PerformanceTraceTests
    {
        private static PerformanceSecond At(float time, float averageMs, int thermal = -1)
        {
            return new PerformanceSecond(time, averageMs, averageMs, 0f, 100, 40, thermal, float.NaN, 10);
        }

        [Test]
        public void TheRingKeepsTheNewestSamplesOldestFirst()
        {
            var trace = new PerformanceTraceModel(3);

            for (int second = 1; second <= 5; second++)
            {
                trace.Add(At(second, second));
            }

            Assert.That(trace.Count, Is.EqualTo(3));
            Assert.That(trace.Get(0).Time, Is.EqualTo(3f));
            Assert.That(trace.Get(2).Time, Is.EqualTo(5f));
        }

        [Test]
        public void ADeviceThatSlowsDownDriftsPositive()
        {
            var trace = new PerformanceTraceModel();

            for (int second = 0; second < 60; second++)
            {
                trace.Add(At(second, second < 30 ? 16f : 22f));
            }

            Assert.That(trace.DriftMs(30), Is.EqualTo(6f).Within(0.001f));
        }

        [Test]
        public void AnExpensiveButSteadySceneDoesNotDrift()
        {
            var trace = new PerformanceTraceModel();

            for (int second = 0; second < 60; second++)
            {
                trace.Add(At(second, 30f));
            }

            Assert.That(trace.DriftMs(30), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void DriftNeedsTwoFullWindows()
        {
            var trace = new PerformanceTraceModel();

            for (int second = 0; second < 40; second++)
            {
                trace.Add(At(second, second));
            }

            Assert.That(trace.DriftMs(30), Is.EqualTo(0f));
        }

        [Test]
        public void TheWorstThermalStatusIsReportedAndUnknownIsMinusOne()
        {
            var trace = new PerformanceTraceModel();
            Assert.That(trace.WorstThermalStatus(), Is.EqualTo(-1));

            trace.Add(At(1f, 16f, 0));
            trace.Add(At(2f, 16f, 3));
            trace.Add(At(3f, 16f, 1));

            Assert.That(trace.WorstThermalStatus(), Is.EqualTo(3));
        }

        [Test]
        public void TheCsvHasAHeaderAndOneRowPerSecondWithBlankUnknownTemperature()
        {
            var trace = new PerformanceTraceModel();
            trace.Add(At(1f, 16.5f));
            trace.Add(At(2f, 17.25f));

            var builder = new StringBuilder();
            trace.AppendCsv(builder);
            string[] lines = builder.ToString().TrimEnd('\n').Split('\n');

            Assert.That(lines.Length, Is.EqualTo(3));
            Assert.That(lines[0], Does.StartWith("time_s,avg_ms"));
            Assert.That(lines[2], Is.EqualTo("2.0,17.25,17.25,0.0,100,40,-1,,10"));
        }
    }
}
