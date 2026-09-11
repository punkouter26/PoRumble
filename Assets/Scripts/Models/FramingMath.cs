using UnityEngine;

namespace PoRumble.Models
{
    /// <summary>
    /// How wide the spectator camera may open.
    ///
    /// Pure and static for the same reason <see cref="CombatMath"/> and <see cref="TensionMath"/>
    /// are: the camera and its tests have to agree about what fits on screen, and a rule that
    /// lives only inside a MonoBehaviour's LateUpdate can be checked by nothing but looking at
    /// it. This one had a defect that survived exactly that way - the ring was cropped on tall
    /// phones and the only way to find out was to run the game and measure the camera.
    /// </summary>
    public static class FramingMath
    {
        /// <summary>
        /// The portrait screen the framing minimum is quoted against: 1080x1920, the same
        /// reference the UI panel uses. Every other portrait aspect scales from here so the
        /// tightest shot frames the same width rather than the same height.
        /// </summary>
        public const float PORTRAIT_REFERENCE_ASPECT = 0.5625f;

        /// <summary>
        /// The orthographic size - half the view's HEIGHT - that frames a fight of the given
        /// extent without leaving the ring.
        /// </summary>
        /// <param name="extent">Half the spread of the fighters being framed, already divided
        /// by the aspect on the horizontal axis.</param>
        /// <param name="padding">World units of air to leave around them.</param>
        /// <param name="aspect">Viewport width over height.</param>
        /// <param name="arenaHalfExtent">Half the ring, in world units.</param>
        /// <param name="outsideRingMargin">How far past the ropes the camera may look, so the
        /// corner posts and stools stay visible.</param>
        /// <param name="minLandscapeSize">Closest the camera may pull in on a wide screen.</param>
        /// <param name="minPortraitSize">Closest it may pull in at the portrait reference
        /// aspect; narrower screens scale it up to hold the same visible width.</param>
        /// <param name="maxSize">Flat backstop on how far it may pull out.</param>
        public static float ResolveOrthographicSize(
            float extent,
            float padding,
            float aspect,
            Vector2 arenaHalfExtent,
            float outsideRingMargin,
            float minLandscapeSize,
            float minPortraitSize,
            float maxSize)
        {
            aspect = Mathf.Max(0.01f, aspect);

            Vector2 bounds = arenaHalfExtent + Vector2.one * outsideRingMargin;

            // The ring is square and no screen is, so one of two framings has to be chosen.
            //
            // Landscape crops: pull out only until the view is as wide as the ring, which fills
            // the screen and keeps the fighters large, with some of the ring's height off-frame
            // for the camera to pan over.
            //
            // Portrait letterboxes instead. Cropping a 0.56 aspect to fill would show barely
            // half the ring's width, so most of a ten-way would be off-screen while the HUD
            // still claimed ten fighters were alive.
            float cropToFill = Mathf.Min(bounds.y, bounds.x / aspect);
            float fitWhole = Mathf.Max(bounds.y, bounds.x / aspect);
            float maxByRing = aspect < 1f ? fitWhole : cropToFill;

            float upper = Mathf.Min(maxSize, maxByRing);

            if (aspect < 1f)
            {
                // Portrait fits the whole ring by design, so the cap may never sit below the
                // size that actually achieves it. maxSize is a flat number while the size a fit
                // needs grows as 1/aspect, so on a tall enough screen the backstop bound first
                // and cropped the very thing the letterbox rule exists to show. Measured at
                // aspect 0.36: half-width 16.25 against a ring half-width of 20.
                //
                // The floor is the ring proper rather than `bounds`, because losing the dressing
                // margin is a blemish and losing the fighters is a broken build.
                upper = Mathf.Max(upper, arenaHalfExtent.x / aspect);
            }

            // The minimum can climb past the maximum on a very narrow screen, and Mathf.Clamp
            // returns the minimum when the two cross - so the cap has to win explicitly, or the
            // camera would pull out past the ring at the exact aspect this all exists to guard.
            float lower = Mathf.Min(
                ResolveMinimumSize(aspect, minLandscapeSize, minPortraitSize),
                upper);

            return Mathf.Clamp(extent + padding, lower, upper);
        }

        /// <summary>
        /// The closest the camera may pull in, which is not the same number in both
        /// orientations, and on a portrait screen is not a single number at all.
        ///
        /// Orthographic size is half-HEIGHT, so a flat portrait minimum silently tightens as the
        /// screen narrows: 6 shows 6.75 world units across at the 9:16 reference and 4.3 at
        /// 0.36, which is narrower than two fighters standing at punching range. Scaling against
        /// the reference aspect makes it the same framing on every phone.
        /// </summary>
        public static float ResolveMinimumSize(
            float aspect,
            float minLandscapeSize,
            float minPortraitSize)
        {
            if (aspect >= 1f)
            {
                return minLandscapeSize;
            }

            return minPortraitSize * (PORTRAIT_REFERENCE_ASPECT / Mathf.Max(0.01f, aspect));
        }
    }
}
