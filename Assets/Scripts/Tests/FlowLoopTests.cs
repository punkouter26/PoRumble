using NUnit.Framework;
using PoRumble.Models;

namespace PoRumble.Tests
{
    /// <summary>
    /// The round loop as a player walks it: menu, fight, decision, back to the menu.
    ///
    /// The loop had no menu at all until recently - the scene booted straight into a ten-way
    /// and the only thing a restart could do was start another one. The fight card is the one
    /// thing a player can change between matches, so where the loop returns to is a gameplay
    /// decision and not a presentation detail.
    /// </summary>
    public sealed class FlowLoopTests
    {
        [Test]
        public void TheGameOpensOnTheMenu()
        {
            MatchFlowModel flow = new();

            Assert.That(flow.Phase.Value, Is.EqualTo(MatchFlowPhase.Title));
            Assert.That(flow.CanStartFight, Is.True);
            Assert.That(flow.IsFightLive, Is.False, "Nothing should be swinging on the menu.");
        }

        [Test]
        public void TheCardOpensBetweenMatchesAndNeverDuringOne()
        {
            MatchFlowModel flow = new();

            flow.Phase.Value = MatchFlowPhase.Title;
            Assert.That(flow.CanOpenCard, Is.True, "The menu is where the card lives.");

            flow.Phase.Value = MatchFlowPhase.Results;
            Assert.That(flow.CanOpenCard, Is.True);

            // Re-seating the roster mid-fight would swap contestants into chairs that are
            // currently mid-punch.
            flow.Phase.Value = MatchFlowPhase.Fighting;
            Assert.That(flow.CanOpenCard, Is.False);

            flow.Phase.Value = MatchFlowPhase.Countdown;
            Assert.That(flow.CanOpenCard, Is.False);

            flow.Phase.Value = MatchFlowPhase.KnockoutHold;
            Assert.That(flow.CanOpenCard, Is.False);
        }

        [Test]
        public void StartingAFightIsRefusedAnywhereButTheMenu()
        {
            MatchFlowModel flow = new();

            foreach (MatchFlowPhase phase in new[]
                     {
                         MatchFlowPhase.Introducing, MatchFlowPhase.Countdown,
                         MatchFlowPhase.Fighting, MatchFlowPhase.KnockoutHold,
                         MatchFlowPhase.Results
                     })
            {
                flow.Phase.Value = phase;
                Assert.That(flow.CanStartFight, Is.False,
                    $"A fight must not be startable from {phase}.");
            }
        }

        [Test]
        public void RestartAndStartNeverBothAcceptTheSameInput()
        {
            MatchFlowModel flow = new();

            // One tap serves both, so the two gates must never be open together or a single
            // press would dismiss the results and start the next bout in the same frame.
            foreach (MatchFlowPhase phase in new[]
                     {
                         MatchFlowPhase.Title, MatchFlowPhase.Introducing,
                         MatchFlowPhase.Countdown, MatchFlowPhase.Fighting,
                         MatchFlowPhase.KnockoutHold, MatchFlowPhase.Results
                     })
            {
                flow.Phase.Value = phase;
                Assert.That(flow.CanRestart && flow.CanStartFight, Is.False,
                    $"Both gates were open in {phase}.");
            }
        }

        [Test]
        public void OnlyTheFightingPhaseIsLive()
        {
            MatchFlowModel flow = new();

            foreach (MatchFlowPhase phase in new[]
                     {
                         MatchFlowPhase.Title, MatchFlowPhase.Introducing,
                         MatchFlowPhase.Countdown, MatchFlowPhase.KnockoutHold,
                         MatchFlowPhase.Results
                     })
            {
                flow.Phase.Value = phase;
                Assert.That(flow.IsFightLive, Is.False);
            }

            flow.Phase.Value = MatchFlowPhase.Fighting;
            Assert.That(flow.IsFightLive, Is.True);
        }

        /// <summary>
        /// Hitstop and the knockout hold both write <c>Time.timeScale</c>, and the hitstop's
        /// restore is guarded on the fight still being live. This pins the guard rather than
        /// the timing: if the phase is no longer Fighting, hitstop must not put time back to
        /// normal, because the knockout hold is deliberately holding it slow.
        /// </summary>
        [Test]
        public void HitstopMustNotRestoreTimeOutsideAFight()
        {
            MatchFlowModel flow = new();

            flow.Phase.Value = MatchFlowPhase.Fighting;
            Assert.That(flow.IsFightLive, Is.True, "Hitstop restores time during a fight.");

            flow.Phase.Value = MatchFlowPhase.KnockoutHold;
            Assert.That(flow.IsFightLive, Is.False,
                "The knockout hold owns the time scale; hitstop must keep its hands off it.");
        }
    }
}
