using NUnit.Framework;
using PoRumble.Models;
using UnityEngine;

namespace PoRumble.Tests
{
    /// <summary>
    /// What the spectator camera is allowed to show.
    ///
    /// The rule used to live inside SpectatorCameraView.LateUpdate, where the only way to check
    /// it was to run the game and measure the camera - which is how it shipped cropping the ring
    /// on tall phones without anything noticing.
    /// </summary>
    public sealed class FramingMathTests
    {
        /// <summary>The shipped ring: 40x40, so half of it is 20 on each axis.</summary>
        private static readonly Vector2 Arena = new(20f, 20f);

        private const float OUTSIDE_MARGIN = 4f;
        private const float MIN_LANDSCAPE = 6f;
        private const float MIN_PORTRAIT = 6f;
        private const float MAX_SIZE = 45f;

        /// <summary>Half the view's width at a given orthographic size, which is half-HEIGHT.</summary>
        private static float HalfWidth(float size, float aspect) => size * aspect;

        private static float Resolve(float extent, float padding, float aspect)
        {
            return FramingMath.ResolveOrthographicSize(
                extent, padding, aspect, Arena, OUTSIDE_MARGIN,
                MIN_LANDSCAPE, MIN_PORTRAIT, MAX_SIZE);
        }

        /// <summary>
        /// The defect this file was written for. A flat maximum cannot fit a ring whose required
        /// size grows as 1/aspect, so on a tall enough phone the backstop bound first and cut
        /// the fighters off the sides - while the HUD went on counting ten of them alive.
        ///
        /// Measured in the running game at 0.36 before the fix: half-width 16.25 against a ring
        /// half-width of 20, four units gone off each side.
        /// </summary>
        [Test]
        public void TheWholeRingFitsAcrossEveryPortraitScreen()
        {
            // 9:16 reference, 20:9, 21:9, and the extreme editor viewport that exposed it.
            foreach (float aspect in new[] { 0.5625f, 0.45f, 0.4286f, 0.361f })
            {
                // A ten-way asking for more than the ring can give, so the cap is what answers.
                float size = Resolve(extent: 60f, padding: 4.5f, aspect: aspect);

                Assert.That(
                    HalfWidth(size, aspect),
                    Is.GreaterThanOrEqualTo(Arena.x - 0.001f),
                    $"The ring is cropped at aspect {aspect}: a fighter can stand somewhere the " +
                    "camera cannot look, which is the failure the letterbox rule exists to stop.");
            }
        }

        /// <summary>
        /// Landscape crops to fill on purpose - it pulls out only until the view is as wide as
        /// the ring and pans over the height. The portrait floor must not leak into it, or the
        /// fighters would shrink on the one orientation that was already correct.
        /// </summary>
        [Test]
        public void LandscapeStillCropsToFillRatherThanFittingTheWholeRing()
        {
            float size = Resolve(extent: 60f, padding: 4.5f, aspect: 16f / 9f);

            Assert.That(size, Is.LessThan(Arena.y),
                "A landscape camera that fits the ring's height has stopped cropping to fill.");
        }

        /// <summary>
        /// Orthographic size is half-HEIGHT, so a single portrait minimum frames a different
        /// WIDTH on every phone: 6 showed 6.75 world units across at the 9:16 reference and 4.3
        /// at 0.36 - narrower than two fighters standing at punching range.
        /// </summary>
        [Test]
        public void TheTightestShotFramesTheSameWidthOnEveryPortraitScreen()
        {
            float reference = FramingMath.ResolveMinimumSize(
                FramingMath.PORTRAIT_REFERENCE_ASPECT, MIN_LANDSCAPE, MIN_PORTRAIT);

            float referenceWidth = HalfWidth(reference, FramingMath.PORTRAIT_REFERENCE_ASPECT);

            foreach (float aspect in new[] { 0.5625f, 0.45f, 0.4286f, 0.361f })
            {
                float size = FramingMath.ResolveMinimumSize(aspect, MIN_LANDSCAPE, MIN_PORTRAIT);

                Assert.That(HalfWidth(size, aspect), Is.EqualTo(referenceWidth).Within(0.001f),
                    $"The tightest shot is a different width at aspect {aspect}.");
            }
        }

        /// <summary>
        /// The serialized value keeps meaning what it always meant, so the number sitting in the
        /// scene is not silently retuned by the change that made it aspect-aware.
        /// </summary>
        [Test]
        public void ThePortraitMinimumIsUnchangedAtTheReferenceAspect()
        {
            Assert.That(
                FramingMath.ResolveMinimumSize(
                    FramingMath.PORTRAIT_REFERENCE_ASPECT, MIN_LANDSCAPE, MIN_PORTRAIT),
                Is.EqualTo(MIN_PORTRAIT).Within(0.0001f));
        }

        /// <summary>
        /// On a narrow enough screen the width-scaled minimum climbs past the ring-fit cap.
        /// Mathf.Clamp returns the MINIMUM when the two cross, so without an explicit guard the
        /// camera would pull out past the ring at exactly the aspect the cap exists to protect.
        /// </summary>
        [Test]
        public void TheCapWinsWhenTheMinimumClimbsPastIt()
        {
            // A duel asking for the tightest shot available on a very narrow screen.
            float size = Resolve(extent: 0f, padding: 0f, aspect: 0.361f);

            float maxAllowed = FramingMath.ResolveOrthographicSize(
                60f, 4.5f, 0.361f, Arena, OUTSIDE_MARGIN, MIN_LANDSCAPE, MIN_PORTRAIT, MAX_SIZE);

            Assert.That(size, Is.LessThanOrEqualTo(maxAllowed + 0.001f),
                "The tightest shot is wider than the widest one.");
        }

        /// <summary>
        /// A duel must still be a duel. The fix raises the floor on narrow screens, and it would
        /// be self-defeating if it also pushed a two-shot out to the whole ring.
        /// </summary>
        [Test]
        public void ADuelIsStillFramedTightly()
        {
            float size = Resolve(extent: 2f, padding: 2.5f, aspect: FramingMath.PORTRAIT_REFERENCE_ASPECT);

            Assert.That(HalfWidth(size, FramingMath.PORTRAIT_REFERENCE_ASPECT),
                Is.LessThan(Arena.x * 0.5f),
                "A two-shot that shows half the ring is not a two-shot.");
        }
    }
}
