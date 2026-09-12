using System.Text;
using NUnit.Framework;
using PoRumble.Models;

namespace PoRumble.Tests
{
    /// <summary>
    /// The ranking rule, pinned.
    ///
    /// Every state worth getting right here is a state that is hard to produce on purpose: a
    /// policy that stopped loading, a ring that never got seated, a timescale left behind by a
    /// knockout. The whole reason the rule is a pure static is that it can be driven into those
    /// states from a test rather than from a device.
    /// </summary>
    public sealed class DiagnosticsVerdictTests
    {
        private DiagnosticsFinding[] _findings;

        [SetUp]
        public void SetUp() => _findings = new DiagnosticsFinding[16];

        /// <summary>A ring fighting normally at 60fps with nothing over any threshold.</summary>
        private static DiagnosticsSample Healthy(
            float averageMs = 12f,
            float peakMs = 14f,
            float allocBytesPerSecond = 0f,
            long drawCalls = 60,
            long setPass = 30,
            float textureMb = 90f,
            int agents = 10,
            bool academy = true,
            float decisionsPerSecond = 40f,
            int punchesThrown = 25,
            float secondsFighting = 20f,
            bool fightLive = true,
            float timeScale = 1f)
        {
            return new DiagnosticsSample(
                averageMs, peakMs, 16.7f, allocBytesPerSecond, drawCalls, setPass, textureMb,
                agents, academy, decisionsPerSecond, punchesThrown, secondsFighting, fightLive,
                timeScale);
        }

        [Test]
        public void Rank_WithEverythingInsideBudget_FindsNothing()
        {
            Assert.That(DiagnosticsVerdict.Rank(Healthy(), _findings), Is.EqualTo(0));
        }

        [Test]
        public void Rank_WithNoFindings_ReadsAsAllClear()
        {
            var builder = new StringBuilder();
            DiagnosticsVerdict.AppendAllClear(builder);

            Assert.That(builder.ToString(), Does.StartWith("ALL CLEAR"));
        }

        [Test]
        public void Rank_WithEmptyRing_LeadsWithTheEmptyRing()
        {
            // An empty ring is the most broken the build can be and it costs nothing to render,
            // so it must outrank a frame-time finding it will never produce.
            DiagnosticsSample sample = Healthy(agents: 0, averageMs: 30f, peakMs: 40f);

            int found = DiagnosticsVerdict.Rank(sample, _findings);

            Assert.That(found, Is.GreaterThan(0));
            Assert.That(_findings[0].Issue, Is.EqualTo(DiagnosticsIssue.NoFighters));
        }

        [Test]
        public void Rank_WithAStalledPolicy_LeadsWithTheStall()
        {
            // The defining trap: a stalled policy runs at a perfect frame rate, so nothing in
            // the frame page moves and the sheet used to look entirely healthy.
            DiagnosticsSample sample = Healthy(decisionsPerSecond: 0f);

            int found = DiagnosticsVerdict.Rank(sample, _findings);

            Assert.That(found, Is.EqualTo(1));
            Assert.That(_findings[0].Issue, Is.EqualTo(DiagnosticsIssue.PolicyStalled));
        }

        [Test]
        public void Rank_WithNoAgentsAndAStalledAcademy_DoesNotReportBoth()
        {
            // An empty ring never asks for a decision, so the stall is a restatement of the
            // empty ring rather than a second fault.
            DiagnosticsSample sample = Healthy(agents: 0, decisionsPerSecond: 0f);

            int found = DiagnosticsVerdict.Rank(sample, _findings);

            Assert.That(found, Is.EqualTo(1));
            Assert.That(_findings[0].Issue, Is.EqualTo(DiagnosticsIssue.NoFighters));
        }

        [Test]
        public void Rank_WithAnUninitialisedAcademy_DoesNotCallItAStall()
        {
            // A scene with no ML agents never initialises the Academy, and zero decisions a
            // second is then the correct reading rather than a fault.
            DiagnosticsSample sample = Healthy(academy: false, decisionsPerSecond: 0f);

            Assert.That(DiagnosticsVerdict.Rank(sample, _findings), Is.EqualTo(0));
        }

        [Test]
        public void Rank_WithSilenceEarlyInTheFight_SaysNothing()
        {
            // Before the threshold this is the gap between the bell and the first exchange.
            DiagnosticsSample sample = Healthy(punchesThrown: 0, secondsFighting: 4f);

            Assert.That(DiagnosticsVerdict.Rank(sample, _findings), Is.EqualTo(0));
        }

        [Test]
        public void Rank_WithSilenceDeepIntoTheFight_NamesBlindPerception()
        {
            DiagnosticsSample sample = Healthy(punchesThrown: 0, secondsFighting: 30f);

            int found = DiagnosticsVerdict.Rank(sample, _findings);

            Assert.That(found, Is.EqualTo(1));
            Assert.That(_findings[0].Issue, Is.EqualTo(DiagnosticsIssue.NobodyPunching));

            var builder = new StringBuilder();
            DiagnosticsVerdict.Append(builder, _findings[0]);

            Assert.That(builder.ToString(), Does.Contain("perception"));
            Assert.That(builder.ToString(), Does.Contain("30s"));
        }

        [Test]
        public void Rank_WithALoweredTimescaleDuringAFight_StaysQuiet()
        {
            // Hitstop lowers it on every landed punch. A warning that fires there is a warning
            // nobody reads.
            DiagnosticsSample sample = Healthy(fightLive: true, timeScale: 0.25f);

            Assert.That(DiagnosticsVerdict.Rank(sample, _findings), Is.EqualTo(0));
        }

        [Test]
        public void Rank_WithALoweredTimescaleOutsideAFight_ReportsIt()
        {
            DiagnosticsSample sample = Healthy(fightLive: false, timeScale: 0.25f);

            int found = DiagnosticsVerdict.Rank(sample, _findings);

            Assert.That(found, Is.EqualTo(1));
            Assert.That(_findings[0].Issue, Is.EqualTo(DiagnosticsIssue.TimeStuck));
        }

        [Test]
        public void Rank_WithAUniformlySlowFrame_DoesNotAlsoCallItAStutter()
        {
            // Every frame is the peak on a device that is simply slow, and a stutter line there
            // is the frame-time line said twice.
            DiagnosticsSample sample = Healthy(averageMs: 40f, peakMs: 44f);

            int found = DiagnosticsVerdict.Rank(sample, _findings);

            Assert.That(found, Is.EqualTo(1));
            Assert.That(_findings[0].Issue, Is.EqualTo(DiagnosticsIssue.FrameTimeOverBudget));
        }

        [Test]
        public void Rank_WithOneLongFrameAmongGoodOnes_CallsItAStutter()
        {
            DiagnosticsSample sample = Healthy(averageMs: 12f, peakMs: 90f);

            int found = DiagnosticsVerdict.Rank(sample, _findings);

            Assert.That(found, Is.EqualTo(1));
            Assert.That(_findings[0].Issue, Is.EqualTo(DiagnosticsIssue.FrameStutter));
        }

        [Test]
        public void Rank_WithAllocationDrivenStutter_PutsTheAllocationFirst()
        {
            // The cause outranks the symptom by being further past its own threshold: 6MB/s is
            // ninety-six times the allocation budget, a 90ms peak is under three times the
            // stutter one.
            DiagnosticsSample sample = Healthy(
                averageMs: 12f, peakMs: 90f, allocBytesPerSecond: 6f * 1024f * 1024f);

            int found = DiagnosticsVerdict.Rank(sample, _findings);

            Assert.That(found, Is.EqualTo(2));
            Assert.That(_findings[0].Issue, Is.EqualTo(DiagnosticsIssue.ManagedAllocation));
            Assert.That(_findings[1].Issue, Is.EqualTo(DiagnosticsIssue.FrameStutter));
        }

        [Test]
        public void Rank_SortsStrictlyBySeverity()
        {
            DiagnosticsSample sample = Healthy(
                drawCalls: 220,          // 1.1x
                setPass: 240,            // 3.0x
                textureMb: 512f);        // 2.0x

            int found = DiagnosticsVerdict.Rank(sample, _findings);

            Assert.That(found, Is.EqualTo(3));
            Assert.That(_findings[0].Issue, Is.EqualTo(DiagnosticsIssue.SetPassHigh));
            Assert.That(_findings[1].Issue, Is.EqualTo(DiagnosticsIssue.TextureMemoryHigh));
            Assert.That(_findings[2].Issue, Is.EqualTo(DiagnosticsIssue.DrawCallsHigh));

            Assert.That(_findings[0].Severity, Is.GreaterThan(_findings[1].Severity));
            Assert.That(_findings[1].Severity, Is.GreaterThan(_findings[2].Severity));
        }

        [Test]
        public void Rank_NeverReportsMoreThanTheSheetCanShow()
        {
            DiagnosticsSample sample = Healthy(
                averageMs: 60f,
                peakMs: 400f,
                allocBytesPerSecond: 8f * 1024f * 1024f,
                drawCalls: 900,
                setPass: 400,
                textureMb: 900f,
                punchesThrown: 0,
                secondsFighting: 40f,
                decisionsPerSecond: 0f);

            Assert.That(
                DiagnosticsVerdict.Rank(sample, _findings),
                Is.EqualTo(DiagnosticsVerdict.MAX_FINDINGS));
        }

        [Test]
        public void Rank_WithANullBuffer_ReturnsNothingRatherThanThrowing()
        {
            // The overlay is a developer tool and must never be the thing that stops the game.
            Assert.That(DiagnosticsVerdict.Rank(Healthy(), null), Is.EqualTo(0));
        }

        [Test]
        public void Append_WritesAPlainEnglishSentenceForEveryIssue()
        {
            foreach (DiagnosticsIssue issue in System.Enum.GetValues(typeof(DiagnosticsIssue)))
            {
                var builder = new StringBuilder();
                DiagnosticsVerdict.Append(builder, new DiagnosticsFinding(issue, 1f, 42f));

                string text = builder.ToString();

                Assert.That(text, Is.Not.Empty, $"{issue} has no sentence.");
                Assert.That(text.Length, Is.GreaterThan(30), $"{issue} is not a sentence.");
            }
        }
    }
}
