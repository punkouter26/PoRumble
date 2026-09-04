using UnityEngine;

namespace PoRumble.Views
{
    /// <summary>
    /// Runtime settings that have no scene to live in.
    ///
    /// The frame rate is the reason this exists. Unity caps a mobile player at 30fps unless
    /// something sets <see cref="Application.targetFrameRate"/>, and nothing in this project
    /// ever did - so the whole game ran at half rate on a phone while the Editor, which has no
    /// such cap, ran at 60 and hid it completely. The quality tiers look like they say
    /// otherwise, but their vSyncCount is ignored on Android: only targetFrameRate governs
    /// there, which is why it is set explicitly and vSync is taken out of the argument.
    ///
    /// Static and driven by RuntimeInitializeOnLoadMethod rather than a component, because
    /// this has to hold for every scene including the training arenas, and a component in
    /// SampleScene would not.
    /// </summary>
    internal static class PlatformBootstrap
    {
        /// <summary>
        /// 60 rather than the panel's own rate. A 120Hz phone can drive this game, but the
        /// ring is lit by five 2D lights with a shadow pass and a full post stack, and the
        /// battery cost of doubling that is not repaid by a boxing match the player watches
        /// as much as plays.
        /// </summary>
        private const int TARGET_FRAME_RATE = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Configure()
        {
            // Order matters: vSync takes precedence over targetFrameRate wherever it is
            // honoured, so leaving it at the quality tier's 1 would silently discard the
            // line below on every platform that is not Android.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TARGET_FRAME_RATE;

            // The shipping build is an all-AI exhibition that can be watched without a single
            // touch. On the default timeout the screen dims mid-match.
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }
    }
}
