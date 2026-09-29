using UnityEngine;

namespace PoRumble.Models
{
    /// <summary>
    /// How hard the score should be playing, and which of its layers that brings in.
    ///
    /// The music is three stems of the same loop, started together and never stopped - only
    /// their levels move. That is what lets it build without ever cutting: a new layer fades
    /// in over the bar that is already playing rather than starting a new cue on top of it.
    ///
    /// Pure and static, like the other *Math classes, so the rule the ear hears and the rule
    /// the tests pin are the same code.
    /// </summary>
    public static class MusicMath
    {
        /// <summary>The number of stems. Pulse, drive, stabs.</summary>
        public const int LAYER_COUNT = 3;

        /// <summary>Intensity on the title screen: the pulse alone, low.</summary>
        public const float TITLE_INTENSITY = 0.12f;

        /// <summary>Intensity while the fighters are introduced and counted in.</summary>
        public const float BUILD_INTENSITY = 0.3f;

        /// <summary>Intensity on the results screen. Back down to the pulse, the fight is decided.</summary>
        public const float RESULTS_INTENSITY = 0.2f;

        /// <summary>
        /// Where each layer starts to come in, and where it is fully in. The pulse is present
        /// from the first moment the score plays at all; the drive arrives once the fight is
        /// genuinely live; the stabs only when it is heated.
        /// </summary>
        private static readonly float[] LayerStart = { 0f, 0.28f, 0.62f };
        private static readonly float[] LayerFull = { 0.1f, 0.5f, 0.85f };

        /// <summary>
        /// Target intensity, 0..1, for where the match is.
        ///
        /// During the fight it is the largest of three measures rather than their mean, for the
        /// reason the crowd takes the largest: the field thinning is a slow build that should
        /// hold between exchanges, while tension and momentum are fast and would otherwise drop
        /// the score to nothing every time the pair on camera stepped apart. A floor keeps the
        /// drive in once the bell has gone - a live fight never sounds like a menu.
        ///
        /// The knockout hold is the one moment that goes nearly silent on purpose: the mixer's
        /// Knockout snapshot muffles everything, and a full band playing through a muffled
        /// room reads as a fault rather than as the ending.
        /// </summary>
        public static float TargetIntensity(
            MatchFlowPhase phase,
            float thinning,
            float tension,
            float momentum)
        {
            switch (phase)
            {
                case MatchFlowPhase.Title:
                    return TITLE_INTENSITY;

                case MatchFlowPhase.Introducing:
                case MatchFlowPhase.Countdown:
                    return BUILD_INTENSITY;

                case MatchFlowPhase.KnockoutHold:
                    return 0.05f;

                case MatchFlowPhase.Results:
                    return RESULTS_INTENSITY;

                default:
                    float heat = Mathf.Max(
                        Mathf.Clamp01(thinning),
                        Mathf.Max(Mathf.Clamp01(tension), Mathf.Clamp01(momentum)));

                    return Mathf.Lerp(0.34f, 1f, heat);
            }
        }

        /// <summary>
        /// Level of one stem at an intensity, 0..1. Smoothstepped between its start and full
        /// points, so a layer eases in rather than arriving at a fixed fraction of its level.
        /// </summary>
        public static float LayerGain(int layer, float intensity)
        {
            if (layer < 0 || layer >= LAYER_COUNT)
            {
                return 0f;
            }

            float start = LayerStart[layer];
            float full = LayerFull[layer];

            if (intensity <= start)
            {
                return 0f;
            }

            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, full, intensity));
        }

        /// <summary>
        /// A stable index for a name, for picking a contestant's signature sting from a bank.
        ///
        /// FNV-1a rather than <see cref="string.GetHashCode"/>, which is allowed to differ
        /// between runtimes and would hand a fighter a different sting on the phone than in
        /// the Editor.
        /// </summary>
        public static int StableIndex(string name, int count)
        {
            if (count <= 0)
            {
                return -1;
            }

            if (string.IsNullOrEmpty(name))
            {
                return 0;
            }

            uint hash = 2166136261u;

            for (int index = 0; index < name.Length; index++)
            {
                hash ^= name[index];
                hash *= 16777619u;
            }

            return (int)(hash % (uint)count);
        }
    }
}
