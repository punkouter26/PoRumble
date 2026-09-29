using NUnit.Framework;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;

namespace PoRumble.Tests
{
    /// <summary>
    /// The pure rules under the broadcast additions: when the ropes close, who headlines the
    /// tale of the tape, and which way a swipe turns the carousel.
    /// </summary>
    public sealed class BroadcastAdditionsTests
    {
        [Test]
        public void TheRopesDoNotMoveBeforeTheStart()
        {
            Assert.That(SuddenDeathMath.RingScaleAt(0f), Is.EqualTo(1f));
            Assert.That(SuddenDeathMath.RingScaleAt(SuddenDeathMath.START_SECONDS - 0.01f), Is.EqualTo(1f));
            Assert.That(SuddenDeathMath.IsClosing(SuddenDeathMath.START_SECONDS - 0.01f), Is.False);
        }

        [Test]
        public void TheRopesCloseSteadilyToTheSmallestRingAndStop()
        {
            float start = SuddenDeathMath.START_SECONDS;
            float end = start + SuddenDeathMath.CLOSE_SECONDS;

            float previous = 1f;

            for (float seconds = start; seconds <= end; seconds += 1f)
            {
                float scale = SuddenDeathMath.RingScaleAt(seconds);
                Assert.That(scale, Is.LessThanOrEqualTo(previous), $"the ring grew back at {seconds}s");
                previous = scale;
            }

            Assert.That(SuddenDeathMath.RingScaleAt(end), Is.EqualTo(SuddenDeathMath.MIN_SCALE).Within(1e-5f));
            Assert.That(SuddenDeathMath.RingScaleAt(end + 600f), Is.EqualTo(SuddenDeathMath.MIN_SCALE).Within(1e-5f));
            Assert.That(SuddenDeathMath.IsClosing(start), Is.True);
        }

        [Test]
        public void TheBellOnlyRingsAfterTheRopesHaveFinishedClosing()
        {
            float closed = SuddenDeathMath.START_SECONDS + SuddenDeathMath.CLOSE_SECONDS;

            Assert.That(SuddenDeathMath.BELL_SECONDS, Is.GreaterThan(closed));
            Assert.That(SuddenDeathMath.IsBell(closed), Is.False);
            Assert.That(SuddenDeathMath.IsBell(SuddenDeathMath.BELL_SECONDS), Is.True);
        }

        [Test]
        public void AFightThatNobodyFinishesIsStillBelled()
        {
            var match = new MatchModel();
            var flow = new MatchFlowModel();
            var system = new SuddenDeathSystem(match, flow, new SuddenDeathModel());
            flow.Phase.Value = MatchFlowPhase.Fighting;

            const float STEP = 0.02f;
            int steps = Mathf.CeilToInt(SuddenDeathMath.BELL_SECONDS / STEP) + 1;
            bool belled = false;

            for (int stepIndex = 0; stepIndex < steps && !belled; stepIndex++)
            {
                belled = system.Step(STEP);
            }

            Assert.That(belled, Is.True, "a stalled fight must still reach the bell");
            Assert.That(match.RingScale, Is.EqualTo(SuddenDeathMath.MIN_SCALE).Within(1e-5f));
        }

        [Test]
        public void TheCountdownToTheRopesNeverGoesNegative()
        {
            Assert.That(SuddenDeathMath.SecondsUntilClose(0f), Is.EqualTo(Mathf.CeilToInt(SuddenDeathMath.START_SECONDS)));
            Assert.That(SuddenDeathMath.SecondsUntilClose(SuddenDeathMath.START_SECONDS + 5f), Is.EqualTo(0));
        }

        [Test]
        public void ThePlayableRingFollowsTheScaleButTheBuiltRingDoesNot()
        {
            var match = new MatchModel { ArenaHalfExtent = new Vector2(8.5f, 8.5f), RingScale = 0.5f };

            Assert.That(match.PlayableHalfExtent, Is.EqualTo(new Vector2(4.25f, 4.25f)));
            Assert.That(match.ArenaHalfExtent, Is.EqualTo(new Vector2(8.5f, 8.5f)),
                "the agents' positional observation is normalised against the built ring");
        }

        [Test]
        public void TheViewersPickHeadlinesAgainstTheFavourite()
        {
            float[] odds = { 0.10f, 0.40f, 0.05f, 0.25f };

            Assert.That(HeadlineMath.Choose(odds, 4, 2, out int first, out int second), Is.True);
            Assert.That(first, Is.EqualTo(2));
            Assert.That(second, Is.EqualTo(1));
        }

        [Test]
        public void ThePickThatIsTheFavouriteFacesTheSecondFavourite()
        {
            float[] odds = { 0.10f, 0.40f, 0.05f, 0.25f };

            HeadlineMath.Choose(odds, 4, 1, out int first, out int second);

            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(3));
        }

        [Test]
        public void WithNoPickTheTwoFavouritesHeadline()
        {
            float[] odds = { 0.10f, 0.40f, 0.05f, 0.25f };

            HeadlineMath.Choose(odds, 4, HeadlineMath.NONE, out int first, out int second);

            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(3));
        }

        [Test]
        public void ACardOfOneHasNoHeadline()
        {
            Assert.That(HeadlineMath.Choose(new[] { 1f }, 1, HeadlineMath.NONE, out _, out _), Is.False);
        }

        [Test]
        public void ASwipeBackWrapsAndSkipsIneligiblePanels()
        {
            bool[] eligible = { true, false, true };

            Assert.That(HudCarouselMath.PreviousEligible(eligible, 0), Is.EqualTo(2));
            Assert.That(HudCarouselMath.PreviousEligible(eligible, 2), Is.EqualTo(0));
            Assert.That(HudCarouselMath.PreviousEligible(new[] { false, false }, 0), Is.EqualTo(-1));
            Assert.That(HudCarouselMath.PreviousEligible(new[] { true }, 0), Is.EqualTo(0));
        }
    }
}
