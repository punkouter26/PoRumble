using NUnit.Framework;
using PoRumble.Models;

namespace PoRumble.Tests
{
    /// <summary>
    /// The rules under the live canvas stains, the adaptive score and the portrait carousel.
    /// Each is only ever checked otherwise by watching a match, which is how a rule that is
    /// wrong in one phase goes unnoticed for weeks.
    /// </summary>
    public sealed class PresentationMathTests
    {
        [Test]
        public void AStainDarkensWithEveryPunchButEachAddsLessThanTheLast()
        {
            float one = CanvasStainMath.Opacity(3f, 6f, 1f);
            float two = CanvasStainMath.Opacity(6f, 6f, 1f);
            float three = CanvasStainMath.Opacity(9f, 6f, 1f);

            Assert.That(two, Is.GreaterThan(one));
            Assert.That(three, Is.GreaterThan(two));
            Assert.That(three - two, Is.LessThan(two - one));
        }

        [Test]
        public void AStainNeverExceedsItsMaximumOpacity()
        {
            Assert.That(CanvasStainMath.Opacity(10_000f, 6f, 0.62f), Is.LessThanOrEqualTo(0.62f));
        }

        [Test]
        public void UntouchedCanvasCarriesNoStain()
        {
            Assert.That(CanvasStainMath.Opacity(0f, 6f, 1f), Is.EqualTo(0f));
        }

        [Test]
        public void LightDamageIsSweatAndHeavyDamageIsBlood()
        {
            Assert.That(CanvasStainMath.BloodShare(2f, 4f, 14f), Is.EqualTo(0f));
            Assert.That(CanvasStainMath.BloodShare(9f, 4f, 14f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(CanvasStainMath.BloodShare(40f, 4f, 14f), Is.EqualTo(1f));
        }

        [Test]
        public void TheTitleScreenPlaysThePulseAlone()
        {
            float intensity = MusicMath.TargetIntensity(MatchFlowPhase.Title, 0f, 0f, 0f);

            Assert.That(MusicMath.LayerGain(0, intensity), Is.GreaterThan(0f));
            Assert.That(MusicMath.LayerGain(1, intensity), Is.EqualTo(0f));
            Assert.That(MusicMath.LayerGain(2, intensity), Is.EqualTo(0f));
        }

        [Test]
        public void ALiveFightAlwaysHasTheDriveInEvenWhenNothingIsHappening()
        {
            float quiet = MusicMath.TargetIntensity(MatchFlowPhase.Fighting, 0f, 0f, 0f);

            Assert.That(MusicMath.LayerGain(1, quiet), Is.GreaterThan(0f));
            Assert.That(MusicMath.LayerGain(2, quiet), Is.EqualTo(0f));
        }

        [Test]
        public void AHeatedFightBringsInEveryLayer()
        {
            float heated = MusicMath.TargetIntensity(MatchFlowPhase.Fighting, 1f, 0f, 0f);

            for (int layer = 0; layer < MusicMath.LAYER_COUNT; layer++)
            {
                Assert.That(MusicMath.LayerGain(layer, heated), Is.EqualTo(1f).Within(0.0001f));
            }
        }

        [Test]
        public void TheKnockoutHoldDropsTheScoreBelowAnyLiveFight()
        {
            float hold = MusicMath.TargetIntensity(MatchFlowPhase.KnockoutHold, 1f, 1f, 1f);
            float quietest = MusicMath.TargetIntensity(MatchFlowPhase.Fighting, 0f, 0f, 0f);

            Assert.That(hold, Is.LessThan(quietest));
        }

        [Test]
        public void AContestantAlwaysGetsTheSameSting()
        {
            int first = MusicMath.StableIndex("BIGGIE", 17);

            Assert.That(MusicMath.StableIndex("BIGGIE", 17), Is.EqualTo(first));
            Assert.That(first, Is.InRange(0, 16));
        }

        [Test]
        public void TheCarouselSkipsPanelsThatAreHidden()
        {
            bool[] eligible = { true, false, true };

            Assert.That(HudCarouselMath.NextEligible(eligible, 0), Is.EqualTo(2));
            Assert.That(HudCarouselMath.NextEligible(eligible, 2), Is.EqualTo(0));
        }

        [Test]
        public void ALonePanelHoldsTheSlot()
        {
            bool[] eligible = { false, true, false };

            Assert.That(HudCarouselMath.NextEligible(eligible, 1), Is.EqualTo(1));
            Assert.That(HudCarouselMath.Resolve(eligible, 0), Is.EqualTo(1));
        }

        [Test]
        public void NothingEligibleShowsNothing()
        {
            Assert.That(HudCarouselMath.NextEligible(new bool[3], 0), Is.EqualTo(-1));
            Assert.That(HudCarouselMath.Resolve(new bool[3], -1), Is.EqualTo(-1));
        }

        [Test]
        public void APanelBeingReadIsNotYankedWhenAnotherComesUp()
        {
            bool[] eligible = { true, true, true };

            Assert.That(HudCarouselMath.Resolve(eligible, 1), Is.EqualTo(1));
        }
    }
}
