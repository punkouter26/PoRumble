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

            // The loop now opens on the menu. These tests are about what happens once a fight
            // has been asked for, so they start it here and leave the menu itself to
            // TheLoopOpensAndClosesOnTheMenu below.
            _flowSystem.TryStartFight();
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
        public void TheLoopOpensAndClosesOnTheMenu()
        {
            MatchFlowModel fresh = new();
            Assert.That(fresh.Phase.Value, Is.EqualTo(MatchFlowPhase.Title),
                "the scene used to boot straight into a ten-way with no way to change the card");

            SpawnSystem spawnSystem = new(_match, _config);
            MatchFlowSystem system = new(fresh, _match, spawnSystem);
            system.Configure(2, 5f);

            // The menu waits for the player rather than a clock.
            for (int frame = 0; frame < 300; frame++)
            {
                system.Tick(0.02f);
            }

            Assert.That(fresh.Phase.Value, Is.EqualTo(MatchFlowPhase.Title));
            Assert.That(system.TryStartFight(), Is.True);
            Assert.That(fresh.Phase.Value, Is.EqualTo(MatchFlowPhase.Introducing));
            Assert.That(system.TryStartFight(), Is.False,
                "a second confirmation must not restart the intro");
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

            // Back to the menu rather than straight to the next bell: the fight card is the
            // only thing a player can change between matches and the menu is where it lives.
            Assert.That(_flow.Phase.Value, Is.EqualTo(MatchFlowPhase.Title));
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

            // A restart lands on the menu, and the menu waits: no amount of ticking may start
            // a fight the player has not asked for.
            Assert.That(_flow.Phase.Value, Is.EqualTo(MatchFlowPhase.Title));
            Run(4.2f);
            Assert.That(_flow.Phase.Value, Is.EqualTo(MatchFlowPhase.Title));
            Assert.That(_flow.IsFightLive, Is.False);

            // Once asked for, the second match must count down exactly like the first rather
            // than dropping straight in.
            Assert.That(_flowSystem.TryStartFight(), Is.True);
            Assert.That(_flow.IsFightLive, Is.False);
            Run(4.2f);
            Assert.That(_flow.Phase.Value, Is.EqualTo(MatchFlowPhase.Fighting));
        }

        /// <summary>
        /// The two phase machines are separate - MatchPhase says whether the fight is decided,
        /// MatchFlowPhase says what the player is looking at - and only the flow one is driven
        /// by the menu. Introducing an already-decided match fails silently and completely: the
        /// player sits through the intro and a three-second countdown, the bell rings, and
        /// TickFighting cuts straight to the knockout hold for a fight that never happened.
        /// </summary>
        [Test]
        public void ADecidedMatchCannotBeIntroduced()
        {
            // Back to the menu with a match that is over and has not been re-racked.
            _flow.Phase.Value = MatchFlowPhase.Title;
            _match.End(0);

            Assert.That(_flow.CanStartFight, Is.True, "The menu is up, so the seat is willing.");
            Assert.That(_flowSystem.TryStartFight(), Is.False);
            Assert.That(_flow.Phase.Value, Is.EqualTo(MatchFlowPhase.Title));

            // Re-racking the match is what makes the seat live again.
            _match.BeginNewEpisode();
            Assert.That(_flowSystem.TryStartFight(), Is.True);
            Assert.That(_flow.Phase.Value, Is.EqualTo(MatchFlowPhase.Introducing));
        }
    }
}
