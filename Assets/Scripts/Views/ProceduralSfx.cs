using UnityEngine;

namespace PoRumble.Views
{
    /// <summary>
    /// Builds the match's sound effects in code at load time.
    ///
    /// The project ships no audio assets at all, and a boxing game where a landed punch and a
    /// blocked one sound identical - because neither makes a sound - loses most of what tells
    /// the player what just happened. Synthesising the bank keeps the feedback layer working
    /// without blocking on recorded audio, and each clip is cheap: a few thousand samples of
    /// shaped noise built once and reused.
    ///
    /// Replace these with recorded one-shots when real audio exists; nothing else has to
    /// change, since the feedback view only ever asks this class for a clip.
    /// </summary>
    internal static class ProceduralSfx
    {
        private const int SAMPLE_RATE = 44100;

        /// <summary>
        /// Deterministic noise. UnityEngine.Random is global mutable state, so drawing from it
        /// here would make the generated clips depend on whatever else happened to draw a
        /// number first.
        /// </summary>
        private static uint _randomState = 0x9E3779B9;

        private static float NextNoise()
        {
            _randomState ^= _randomState << 13;
            _randomState ^= _randomState >> 17;
            _randomState ^= _randomState << 5;
            return (_randomState & 0xFFFFFF) / (float)0x800000 - 1f;
        }

        /// <summary>A dull thud: low body plus a short noise transient. An ordinary punch.</summary>
        internal static AudioClip CreateJab()
        {
            return Build("sfx_jab", 0.14f, (t, duration) =>
            {
                float envelope = Decay(t, duration, 18f);
                float body = Mathf.Sin(2f * Mathf.PI * 165f * t);
                float transient = NextNoise() * Decay(t, duration, 90f);
                return (body * 0.55f + transient * 0.45f) * envelope;
            });
        }

        /// <summary>Heavier, lower and longer. A close-range punch.</summary>
        internal static AudioClip CreateHook()
        {
            return Build("sfx_hook", 0.24f, (t, duration) =>
            {
                float envelope = Decay(t, duration, 11f);

                // Pitch drops through the hit, which is what makes it read as heavier.
                float frequency = Mathf.Lerp(150f, 70f, t / duration);
                float body = Mathf.Sin(2f * Mathf.PI * frequency * t);
                float transient = NextNoise() * Decay(t, duration, 55f);
                return (body * 0.7f + transient * 0.4f) * envelope;
            });
        }

        /// <summary>Full haymaker: everything the hook has, deeper and with more crack.</summary>
        internal static AudioClip CreateHaymakerImpact()
        {
            return Build("sfx_haymaker_impact", 0.42f, (t, duration) =>
            {
                float envelope = Decay(t, duration, 7f);
                float frequency = Mathf.Lerp(130f, 45f, t / duration);
                float body = Mathf.Sin(2f * Mathf.PI * frequency * t);
                float sub = Mathf.Sin(2f * Mathf.PI * frequency * 0.5f * t) * 0.6f;
                float crack = NextNoise() * Decay(t, duration, 60f);
                return (body * 0.6f + sub * 0.4f + crack * 0.5f) * envelope;
            });
        }

        /// <summary>The wind-up: rising filtered noise, so a haymaker can be heard coming.</summary>
        internal static AudioClip CreateWhoosh()
        {
            float previous = 0f;

            return Build("sfx_whoosh", 0.30f, (t, duration) =>
            {
                float progress = t / duration;

                // A one-pole low-pass that opens up over the swing: the filter sweeping
                // upward is what makes this read as movement rather than static.
                float cutoff = Mathf.Lerp(0.04f, 0.55f, progress);
                previous += (NextNoise() - previous) * cutoff;

                // Swells in and falls away, rather than starting at full volume.
                float envelope = Mathf.Sin(progress * Mathf.PI);
                return previous * envelope * 0.85f;
            });
        }

        /// <summary>A hard leather-on-leather click. Guard held.</summary>
        internal static AudioClip CreateBlock()
        {
            return Build("sfx_block", 0.10f, (t, duration) =>
            {
                float envelope = Decay(t, duration, 45f);
                float click = Mathf.Sin(2f * Mathf.PI * 900f * t) * 0.4f;
                float slap = NextNoise() * 0.7f;
                return (click + slap) * envelope;
            });
        }

        /// <summary>A soft airy swish. A punch slipped.</summary>
        internal static AudioClip CreateEvade()
        {
            float previous = 0f;

            return Build("sfx_evade", 0.18f, (t, duration) =>
            {
                previous += (NextNoise() - previous) * 0.35f;
                float envelope = Mathf.Sin(t / duration * Mathf.PI);
                return previous * envelope * 0.35f;
            });
        }

        /// <summary>A falling tone. Somebody went down.</summary>
        internal static AudioClip CreateKnockout()
        {
            return Build("sfx_knockout", 0.75f, (t, duration) =>
            {
                float progress = t / duration;
                float frequency = Mathf.Lerp(420f, 90f, progress * progress);
                float tone = Mathf.Sin(2f * Mathf.PI * frequency * t);
                float envelope = Decay(t, duration, 4.5f);
                return tone * envelope * 0.7f;
            });
        }

        /// <summary>The ring bell. Two partials and a long decay, struck at the start.</summary>
        internal static AudioClip CreateBell()
        {
            return Build("sfx_bell", 1.4f, (t, duration) =>
            {
                float envelope = Decay(t, duration, 3.2f);

                // An inharmonic upper partial is what separates a bell from a sine beep.
                float fundamental = Mathf.Sin(2f * Mathf.PI * 620f * t);
                float partial = Mathf.Sin(2f * Mathf.PI * 1_483f * t) * 0.5f;
                float strike = NextNoise() * Decay(t, duration, 120f) * 0.3f;
                return (fundamental + partial + strike) * envelope * 0.5f;
            });
        }

        /// <summary>A short countdown blip, pitched up on the final beat.</summary>
        internal static AudioClip CreateCountdownBeep(bool final)
        {
            float frequency = final ? 880f : 440f;

            return Build(final ? "sfx_beep_final" : "sfx_beep", 0.18f, (t, duration) =>
            {
                float envelope = Decay(t, duration, 12f);
                return Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.5f;
            });
        }

        /// <summary>Exponential fall-off, normalised so every clip starts at full amplitude.</summary>
        private static float Decay(float t, float duration, float rate)
        {
            return Mathf.Exp(-rate * (t / duration));
        }

        private static AudioClip Build(string name, float duration, System.Func<float, float, float> shape)
        {
            int sampleCount = Mathf.CeilToInt(SAMPLE_RATE * duration);
            float[] samples = new float[sampleCount];

            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                float t = sampleIndex / (float)SAMPLE_RATE;
                samples[sampleIndex] = Mathf.Clamp(shape(t, duration), -1f, 1f);
            }

            // A couple of milliseconds of fade at each end. Without it the waveform starts and
            // stops on a non-zero sample, which clicks audibly on every single playback.
            int fade = Mathf.Min(96, sampleCount / 2);

            for (int sampleIndex = 0; sampleIndex < fade; sampleIndex++)
            {
                float gain = sampleIndex / (float)fade;
                samples[sampleIndex] *= gain;
                samples[sampleCount - 1 - sampleIndex] *= gain;
            }

            AudioClip clip = AudioClip.Create(name, sampleCount, 1, SAMPLE_RATE, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
