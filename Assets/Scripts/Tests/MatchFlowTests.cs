using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;

namespace PoRumble.Tests
{
    /// <summary>
    /// The round loop that wraps a fight. Before it existed the game scene had no way back:
    /// a match resolved, the banner appeared, and nothing further could happen without
    /// leaving Play mode.
    /// </summary>
    public sealed class MatchFlowTests
    {
        private MatchFlowModel _flow;
        private MatchModel _match;
        private MatchFlowSystem _flowSystem;
        private BoxerConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<BoxerConfig>();
            _flow = new MatchFlowModel();
            _match = new MatchModel();

            SpawnSystem spawnSystem = new(_match, _config);
            spawnSystem.SpawnRoster(2, 5f);

            _flowSystem = new MatchFlowSystem(_flow, _match, spawnSystem);
            _flowSystem.Configure(2, 5f);
        }

        [TearDown]
        public void TearDown()
        {
            // Time.timeScale is global and survives the test run, so a test that ended during
            // a knockout hold would leave the whole Editor at quarter speed.
            _flowSystem?.ResetTimeScale();
            Object.DestroyImmediate(_config);
        }

        /// <summary>Advances the flow in realistic frame-sized steps.</summary>
        private void Run(float seconds)
        {
            int frames = Mathf.RoundToInt(seconds / 0.02f);

            for (int frame = 0; frame < frames; frame++)
            {
                _flowSystem.Tick(0.02f);
            }
        }

        [Test]
        public void TheFightDoesNotStartUntilTheBell()
        {
            Assert.That(_flow.Phase.Value, Is.EqualTo(MatchFlowPhase.Introducing));
            Assert.That(_flow.IsFightLive, Is.False);

            Run(1f);
            Assert.That(_flow.Phase.Value, Is.EqualTo(MatchFlowPhase.Countdown));
            Assert.That(_flow.IsFightLive, Is.False,
                "ten bots sprinting at the player before they have touched a key is the "
                + "problem the countdown exists to solve");
        }

        [Test]
        public void TheCountdownCountsDown()
        {
            Run(1f);
            Assert.That(_flow.CountdownSeconds.Value, Is.EqualTo(3));

            Run(1.1f);
            Assert.That(_flow.CountdownSeconds.Value, Is.EqualTo(2));

            Run(1f);
            Assert.That(_flow.CountdownSeconds.Value, Is.EqualTo(1));
        }

        [Test]
        public void TheBellStartsTheFight()
        {
            Run(4.2f);

            Assert.That(_flow.Phase.Value, Is.EqualTo(MatchFlowPhase.Fighting));
            Assert.That(_flow.IsFightLive, Is.True);
        }

        [Test]
        public void AResolvedMatchHoldsOnTheKnockoutThenShowsResults()
        {
            Run(4.2f);
            Assert.That(_flow.Phase.Value, Is.EqualTo(MatchFlowPhase.Fighting));

            _match.End(0);
            _flowSystem.Tick(0.02f);

            Assert.That(_flow.Phase.Value, Is.EqualTo(MatchFlowPhase.KnockoutHold));
            Assert.That(Time.timeScale, Is.LessThan(1f),
                "the final blow should read in slow motion rather than cutting to a banner");

            Run(2f);

            Assert.That(_flow.Phase.Value, Is.EqualTo(MatchFlowPhase.Results));
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(0.001f),
                "normal speed has to come back before the next fight");
        }

        [Test]
        public void RestartIsRefusedUntilTheResultsAreUp()
        {
            Assert.That(_flowSystem.TryRestart(), Is.False, "cannot restart during the intro");

            Run(4.2f);
            Assert.That(_flowSystem.TryRestart(), Is.False,
                "a mashed restart key must not be able to cut a live fight short");
        }

        [Test]
        public void RestartRacksTheFightersAgain()
        {
            Run(4.2f);

            BoxerModel boxer = _match.Boxers[0];
            boxer.ApplyDamage(_config.MaxHealth);
            boxer.Eliminate();
            _match.End(1);

            _flowSystem.Tick(0.02f);
            Run(2f);
            Assert.That(_flow.Phase.Value, Is.EqualTo(MatchFlowPhase.Results));

            Assert.That(_flowSystem.TryRestart(), Is.True);

            Assert.That(_flow.Phase.Value, Is.EqualTo(MatchFlowPhase.Introducing));
            Assert.That(_flow.MatchNumber.Value, Is.EqualTo(2));
            Assert.That(_match.Phase.Value, Is.EqualTo(MatchPhase.InProgress));
            Assert.That(boxer.IsAlive.Value, Is.True, "the fighters must come back up");
            Assert.That(boxer.Health.Value, Is.EqualTo(_config.MaxHealth));
        }

        [Test]
        public void ARestartedMatchRunsTheWholeLoopAgain()
        {
            Run(4.2f);
            _match.End(0);
            _flowSystem.Tick(0.02f);
            Run(2f);
            _flowSystem.TryRestart();

            // The second match must count down exactly like the first, not drop straight in.
            Assert.That(_flow.IsFightLive, Is.False);
            Run(4.2f);
            Assert.That(_flow.Phase.Value, Is.EqualTo(MatchFlowPhase.Fighting));
        }
    }
}
