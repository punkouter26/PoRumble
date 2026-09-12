namespace PoRumble.Models
{
    /// <summary>
    /// The faults the telemetry sheet knows how to name, in no particular order - the order is
    /// computed from the measurements by <see cref="DiagnosticsVerdict"/>, not declared here.
    /// </summary>
    public enum DiagnosticsIssue
    {
        None = 0,
        NoFighters = 1,
        PolicyStalled = 2,
        NobodyPunching = 3,
        TimeStuck = 4,
        FrameTimeOverBudget = 5,
        FrameStutter = 6,
        ManagedAllocation = 7,
        DrawCallsHigh = 8,
        SetPassHigh = 9,
        TextureMemoryHigh = 10
    }

    /// <summary>
    /// One reading of everything the verdict is computed from.
    ///
    /// A struct passed by <c>in</c> rather than a dozen parameters: the check list grows and a
    /// twelve-argument call is where the argument in position nine quietly becomes the one in
    /// position ten.
    /// </summary>
    public readonly struct DiagnosticsSample
    {
        /// <summary>Mean unscaled frame time over the last refresh, in milliseconds.</summary>
        public readonly float AverageMs;

        /// <summary>Worst single frame since the last refresh, in milliseconds.</summary>
        public readonly float PeakMs;

        /// <summary>The frame budget being judged against. 16.7 is 60fps.</summary>
        public readonly float BudgetMs;

        /// <summary>Managed heap growth per second, in bytes.</summary>
        public readonly float AllocatedBytesPerSecond;

        public readonly long DrawCalls;
        public readonly long SetPassCalls;
        public readonly float TextureMegabytes;

        /// <summary>Boxer agents seated in the ring. Zero means nothing is fighting.</summary>
        public readonly int AgentCount;

        public readonly bool AcademyInitialised;
        public readonly float DecisionsPerSecond;

        /// <summary>Punches thrown by the whole field this match, landed or not.</summary>
        public readonly int PunchesThrown;

        /// <summary>How long the current fight has been live. Zero outside one.</summary>
        public readonly float SecondsFighting;

        /// <summary>
        /// Whether a fight is running right now. The timescale check is suspended while one is,
        /// because hitstop and the knockout hold both lower it deliberately.
        /// </summary>
        public readonly bool FightLive;

        public readonly float TimeScale;

        public DiagnosticsSample(
            float averageMs,
            float peakMs,
            float budgetMs,
            float allocatedBytesPerSecond,
            long drawCalls,
            long setPassCalls,
            float textureMegabytes,
            int agentCount,
            bool academyInitialised,
            float decisionsPerSecond,
            int punchesThrown,
            float secondsFighting,
            bool fightLive,
            float timeScale)
        {
            AverageMs = averageMs;
            PeakMs = peakMs;
            BudgetMs = budgetMs;
            AllocatedBytesPerSecond = allocatedBytesPerSecond;
            DrawCalls = drawCalls;
            SetPassCalls = setPassCalls;
            TextureMegabytes = textureMegabytes;
            AgentCount = agentCount;
            AcademyInitialised = academyInitialised;
            DecisionsPerSecond = decisionsPerSecond;
            PunchesThrown = punchesThrown;
            SecondsFighting = secondsFighting;
            FightLive = fightLive;
            TimeScale = timeScale;
        }
    }

    /// <summary>
    /// One ranked fault: what it is, how far past its threshold the measurement was, and the
    /// number the sentence quotes.
    /// </summary>
    public readonly struct DiagnosticsFinding
    {
        public readonly DiagnosticsIssue Issue;

        /// <summary>1.0 is exactly at the threshold; the sheet sorts on this, descending.</summary>
        public readonly float Severity;

        /// <summary>The measurement itself, in whatever unit the sentence reads out.</summary>
        public readonly float Value;

        public DiagnosticsFinding(DiagnosticsIssue issue, float severity, float value)
        {
            Issue = issue;
            Severity = severity;
            Value = value;
        }
    }
}
