using System.Text;
using UnityEngine;

namespace PoRumble.Models
{
    /// <summary>
    /// What is wrong right now, in the order it is worth fixing.
    ///
    /// The overlay below this reports twenty numbers and every one of them is true. That is
    /// the problem it solves and the problem it creates: on a phone, held at arm's length,
    /// nobody reads twenty tabular figures and works out which one is the cause and which
    /// three are its symptoms. A frame at 40ms with 6MB/s of allocation and a peak of 90ms is
    /// one fault with three readouts, and the actionable one is the allocation.
    ///
    /// So the numbers are ranked. Each check scores how far past its own threshold the
    /// measurement is, the findings sort on that score, and the sheet prints the worst one
    /// first in a sentence that says what to go and look at.
    ///
    /// Pure and static for the same reason <see cref="FramingMath"/> is: the alternative is a
    /// rule that can only be checked by getting a device into a bad state and reading the
    /// screen, and the states worth ranking correctly are exactly the ones that are hard to
    /// reproduce on purpose.
    /// </summary>
    public static class DiagnosticsVerdict
    {
        /// <summary>
        /// How many findings the sheet will print. Four is the whole of the band the verdict
        /// is given; past that it is a second wall of text competing with the first.
        /// </summary>
        public const int MAX_FINDINGS = 4;

        // Thresholds. Each is the point at which the number stops being noise and starts being
        // worth a sentence, and each is divided into the measurement to get a severity, so 1.0
        // means "exactly at the line" and 4.0 means "four times past it".
        private const float ALLOC_KB_PER_SECOND = 64f;
        private const int DRAW_CALLS = 200;
        private const int SETPASS_CALLS = 80;
        private const float TEXTURE_MEGABYTES = 256f;

        /// <summary>
        /// A stall is not a slow frame, it is a broken one, so the functional checks are scored
        /// above the budget checks rather than sharing their scale. A frame four times over
        /// budget still outranks them, which is correct: at 67ms nothing else matters.
        /// </summary>
        private const float BREAKAGE_SEVERITY = 3.2f;

        /// <summary>Seconds of live fight before silence counts as a fault rather than a gap.</summary>
        private const float SILENCE_SECONDS = 10f;

        /// <summary>
        /// Scores every check against <paramref name="sample"/>, writes the ones that are over
        /// their threshold into <paramref name="into"/> worst-first, and returns how many.
        ///
        /// Takes the buffer rather than returning one because this runs on the overlay's
        /// refresh tick, and an allocation here would be measured by the very row it reports.
        /// </summary>
        public static int Rank(in DiagnosticsSample sample, DiagnosticsFinding[] into)
        {
            if (into == null)
            {
                return 0;
            }

            int count = 0;

            // --- broken, not slow ---------------------------------------------------------

            if (sample.AgentCount <= 0)
            {
                Add(into, ref count, DiagnosticsIssue.NoFighters, BREAKAGE_SEVERITY + 1f, 0f);
            }
            else if (sample.AcademyInitialised && sample.DecisionsPerSecond < 1f)
            {
                Add(into, ref count, DiagnosticsIssue.PolicyStalled, BREAKAGE_SEVERITY + 0.5f,
                    sample.DecisionsPerSecond);
            }

            if (sample.AgentCount > 0
                && sample.SecondsFighting > SILENCE_SECONDS
                && sample.PunchesThrown == 0)
            {
                Add(into, ref count, DiagnosticsIssue.NobodyPunching, BREAKAGE_SEVERITY,
                    sample.SecondsFighting);
            }

            // Only judged away from a live fight. Hitstop and the knockout hold both drive
            // timeScale down on purpose, and a warning that fires every time somebody lands a
            // punch is a warning nobody reads.
            if (!sample.FightLive && sample.TimeScale < 0.99f)
            {
                Add(into, ref count, DiagnosticsIssue.TimeStuck, BREAKAGE_SEVERITY - 0.7f,
                    sample.TimeScale);
            }

            // --- over budget --------------------------------------------------------------

            if (sample.BudgetMs > 0f && sample.AverageMs > sample.BudgetMs)
            {
                Add(into, ref count, DiagnosticsIssue.FrameTimeOverBudget,
                    sample.AverageMs / sample.BudgetMs, sample.AverageMs);
            }

            // A peak is only a stutter if it stands out from its own average. On a device that
            // is uniformly slow every frame is the peak, and that is the frame-time finding
            // above saying the same thing twice.
            if (sample.BudgetMs > 0f
                && sample.PeakMs > sample.BudgetMs * 2f
                && sample.PeakMs > sample.AverageMs * 2f)
            {
                Add(into, ref count, DiagnosticsIssue.FrameStutter,
                    sample.PeakMs / (sample.BudgetMs * 2f), sample.PeakMs);
            }

            float allocKb = sample.AllocatedBytesPerSecond / 1024f;

            if (allocKb > ALLOC_KB_PER_SECOND)
            {
                Add(into, ref count, DiagnosticsIssue.ManagedAllocation,
                    allocKb / ALLOC_KB_PER_SECOND, allocKb);
            }

            if (sample.DrawCalls > DRAW_CALLS)
            {
                Add(into, ref count, DiagnosticsIssue.DrawCallsHigh,
                    sample.DrawCalls / (float)DRAW_CALLS, sample.DrawCalls);
            }

            if (sample.SetPassCalls > SETPASS_CALLS)
            {
                Add(into, ref count, DiagnosticsIssue.SetPassHigh,
                    sample.SetPassCalls / (float)SETPASS_CALLS, sample.SetPassCalls);
            }

            if (sample.TextureMegabytes > TEXTURE_MEGABYTES)
            {
                Add(into, ref count, DiagnosticsIssue.TextureMemoryHigh,
                    sample.TextureMegabytes / TEXTURE_MEGABYTES, sample.TextureMegabytes);
            }

            Sort(into, count);

            return Mathf.Min(count, MAX_FINDINGS);
        }

        /// <summary>
        /// Writes one finding as the sentence the sheet shows: what is wrong, the number that
        /// says so, and the place to go and look. Appends rather than returns so the overlay
        /// can build the whole verdict in its pooled builder.
        /// </summary>
        public static void Append(StringBuilder builder, in DiagnosticsFinding finding)
        {
            if (builder == null)
            {
                return;
            }

            switch (finding.Issue)
            {
                case DiagnosticsIssue.NoFighters:
                    builder.Append("NO FIGHTERS - the ring is empty. No boxer agents were ")
                           .Append("spawned; check BoxerSpawnPoints and the Boxer prefab.");
                    break;

                case DiagnosticsIssue.PolicyStalled:
                    builder.Append("AI HAS STOPPED - the boxers are not asking the policy for ")
                           .Append("decisions. PoRumbleBoxer.onnx most likely failed to load; ")
                           .Append("the log will say so.");
                    break;

                case DiagnosticsIssue.NobodyPunching:
                    builder.Append("NO PUNCHES THROWN in ").Append(finding.Value.ToString("F0"))
                           .Append("s of fight. The boxers cannot see each other - check that ")
                           .Append("perception isolation put each fighter on its own layer.");
                    break;

                case DiagnosticsIssue.TimeStuck:
                    builder.Append("TIME IS STUCK at ").Append(finding.Value.ToString("F2"))
                           .Append("x outside a fight. Something set Time.timeScale and never ")
                           .Append("put it back.");
                    break;

                case DiagnosticsIssue.FrameTimeOverBudget:
                    builder.Append("RUNNING SLOW - ").Append(finding.Value.ToString("F0"))
                           .Append("ms a frame. Read the draw and setpass rows below: high ")
                           .Append("means the renderer, low means the simulation.");
                    break;

                case DiagnosticsIssue.FrameStutter:
                    builder.Append("STUTTERING - one frame took ")
                           .Append(finding.Value.ToString("F0"))
                           .Append("ms while the rest were fine. Almost always a garbage ")
                           .Append("collection; the alloc row names the culprit.");
                    break;

                case DiagnosticsIssue.ManagedAllocation:
                    builder.Append("ALLOCATING - ").Append(finding.Value.ToString("F0"))
                           .Append(" KB/s of C# garbage. Something in an Update loop is ")
                           .Append("allocating, and that is what the stutter is.");
                    break;

                case DiagnosticsIssue.DrawCallsHigh:
                    builder.Append("TOO MANY DRAW CALLS - ").Append(finding.Value.ToString("F0"))
                           .Append(". A sprite outside BoxerAtlas, or a script touching ")
                           .Append("renderer.material, has broken batching.");
                    break;

                case DiagnosticsIssue.SetPassHigh:
                    builder.Append("TOO MANY MATERIAL SWITCHES - ")
                           .Append(finding.Value.ToString("F0")).Append(" setpass calls. The 2D ")
                           .Append("shadow casters are the usual cost; the key light's shadows ")
                           .Append("are the switch worth trying.");
                    break;

                case DiagnosticsIssue.TextureMemoryHigh:
                    builder.Append("TEXTURES ARE LARGE - ").Append(finding.Value.ToString("F0"))
                           .Append(" MB resident. Check import settings for uncompressed ")
                           .Append("sprites, or mipmaps left on a UI texture.");
                    break;

                default:
                    AppendAllClear(builder);
                    break;
            }
        }

        /// <summary>The line shown when nothing is over its threshold.</summary>
        public static void AppendAllClear(StringBuilder builder)
        {
            if (builder != null)
            {
                builder.Append("ALL CLEAR - frame time, memory and the AI are inside budget.");
            }
        }

        private static void Add(
            DiagnosticsFinding[] into,
            ref int count,
            DiagnosticsIssue issue,
            float severity,
            float value)
        {
            if (count >= into.Length)
            {
                return;
            }

            into[count] = new DiagnosticsFinding(issue, severity, value);
            count++;
        }

        /// <summary>
        /// Insertion sort, descending by severity. There are never more than a handful of
        /// findings and the array is the caller's, so this is the sort that does not allocate a
        /// comparer to order ten items.
        /// </summary>
        private static void Sort(DiagnosticsFinding[] findings, int count)
        {
            for (int index = 1; index < count; index++)
            {
                DiagnosticsFinding held = findings[index];
                int scan = index - 1;

                while (scan >= 0 && findings[scan].Severity < held.Severity)
                {
                    findings[scan + 1] = findings[scan];
                    scan--;
                }

                findings[scan + 1] = held;
            }
        }
    }
}
