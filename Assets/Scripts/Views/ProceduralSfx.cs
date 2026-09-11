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
    ///
    /// Every impact sound takes a <c>variant</c>. A boxing match is mostly the same four or
    /// five sounds fired hundreds of times, and a bank of one clip per event makes that
    /// obvious within seconds - the ear locks onto an identical waveform far faster than the
    /// eye locks onto a repeated sprite. Pitch-shifting one clip at playback does not fix it
    /// either, because the noise transient shifts with the body and the result still reads as
    /// the same sample. A variant reseeds the noise *and* moves the body frequency, so the
    /// clips differ in timbre rather than only in pitch.
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

        /// <summary>
        /// Restarts the noise sequence for a variant, so variant N is always the same clip.
        ///
        /// Determinism matters here for the same reason it matters in the scripted brains: a
        /// build and a training run should not differ because the clips were generated in a
        /// different order.
        /// </summary>
        private static void Seed(int variant)
        {
            // Golden-ratio odd constant, so consecutive variants land far apart in the state
            // space rather than producing near-identical sequences.
            _randomState = (uint)(0x9E3779B9 + variant * 0x85EBCA6B);

            if (_randomState == 0u)
            {
                _randomState = 0x9E3779B9;
            }

            // Xorshift correlates strongly for the first few draws from a fresh seed.
            for (int warmup = 0; warmup < 8; warmup++)
            {
                NextNoise();
            }
        }

        /// <summary>
        /// A deterministic multiplier around 1 for a variant, used to detune the tonal body.
        /// Variant 0 always returns exactly 1, so the first clip of every bank is the tuned
        /// one and the spread is a deviation from it rather than a drift away from it.
        /// </summary>
        private static float VariantScale(int variant, float spread)
        {
            if (variant == 0)
            {
                return 1f;
            }

            uint hash = (uint)(variant * 0x9E3779B9);
            hash ^= hash >> 15;
            float unit = (hash & 0xFFFF) / 65535f * 2f - 1f;
            return 1f + unit * spread;
        }

        /// <summary>A dull thud: low body plus a short noise transient. An ordinary punch.</summary>
        internal static AudioClip CreateJab(int variant = 0)
        {
            Seed(variant);
            float bodyHz = 165f * VariantScale(variant, 0.14f);

            return Build("sfx_jab_" + variant, 0.14f, (t, duration) =>
            {
                float envelope = Decay(t, duration, 18f);
                float body = Mathf.Sin(2f * Mathf.PI * bodyHz * t);
                float transient = NextNoise() * Decay(t, duration, 90f);
                return (body * 0.55f + transient * 0.45f) * envelope;
            });
        }

        /// <summary>Heavier, lower and longer. A close-range punch.</summary>
        internal static AudioClip CreateHook(int variant = 0)
        {
            Seed(variant);
            float scale = VariantScale(variant, 0.13f);

            return Build("sfx_hook_" + variant, 0.24f, (t, duration) =>
            {
                float envelope = Decay(t, duration, 11f);

                // Pitch drops through the hit, which is what makes it read as heavier.
                float frequency = Mathf.Lerp(150f * scale, 70f * scale, t / duration);
                float body = Mathf.Sin(2f * Mathf.PI * frequency * t);
                float transient = NextNoise() * Decay(t, duration, 55f);
                return (body * 0.7f + transient * 0.4f) * envelope;
            });
        }

        /// <summary>Full haymaker: everything the hook has, deeper and with more crack.</summary>
        internal static AudioClip CreateHaymakerImpact(int variant = 0)
        {
            Seed(variant);
            float scale = VariantScale(variant, 0.10f);

            return Build("sfx_haymaker_impact_" + variant, 0.42f, (t, duration) =>
            {
                float envelope = Decay(t, duration, 7f);
                float frequency = Mathf.Lerp(130f * scale, 45f * scale, t / duration);
                float body = Mathf.Sin(2f * Mathf.PI * frequency * t);
                float sub = Mathf.Sin(2f * Mathf.PI * frequency * 0.5f * t) * 0.6f;
                float crack = NextNoise() * Decay(t, duration, 60f);
                return (body * 0.6f + sub * 0.4f + crack * 0.5f) * envelope;
            });
        }

        /// <summary>The wind-up: rising filtered noise, so a haymaker can be heard coming.</summary>
        internal static AudioClip CreateWhoosh(int variant = 0)
        {
            Seed(variant);
            float previous = 0f;
            float openTo = 0.55f * VariantScale(variant, 0.18f);

            return Build("sfx_whoosh_" + variant, 0.30f, (t, duration) =>
            {
                float progress = t / duration;

                // A one-pole low-pass that opens up over the swing: the filter sweeping
                // upward is what makes this read as movement rather than static.
                float cutoff = Mathf.Lerp(0.04f, openTo, progress);
                previous += (NextNoise() - previous) * cutoff;

                // Swells in and falls away, rather than starting at full volume.
                float envelope = Mathf.Sin(progress * Mathf.PI);
                return previous * envelope * 0.85f;
            });
        }

        /// <summary>A hard leather-on-leather click. Guard held.</summary>
        internal static AudioClip CreateBlock(int variant = 0)
        {
            Seed(variant);
            float clickHz = 900f * VariantScale(variant, 0.16f);

            return Build("sfx_block_" + variant, 0.10f, (t, duration) =>
            {
                float envelope = Decay(t, duration, 45f);
                float click = Mathf.Sin(2f * Mathf.PI * clickHz * t) * 0.4f;
                float slap = NextNoise() * 0.7f;
                return (click + slap) * envelope;
            });
        }

        /// <summary>A soft airy swish. A punch slipped.</summary>
        internal static AudioClip CreateEvade(int variant = 0)
        {
            Seed(variant);
            float previous = 0f;
            float cutoff = 0.35f * VariantScale(variant, 0.20f);

            return Build("sfx_evade_" + variant, 0.18f, (t, duration) =>
            {
                previous += (NextNoise() - previous) * cutoff;
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

        /// <summary>
        /// A footstep on canvas: a soft, short thump with almost no tone to it.
        ///
        /// Quiet and dull on purpose. Ten fighters shuffling is the most frequent sound in the
        /// game by a wide margin, and anything with a transient sharp enough to notice turns a
        /// ten-way into a hailstorm. What it has to do is fill the silence under the footwork,
        /// not announce each step.
        /// </summary>
        internal static AudioClip CreateFootstep(int variant = 0)
        {
            Seed(variant);
            float previous = 0f;
            float bodyHz = 92f * VariantScale(variant, 0.20f);

            return Build("sfx_step_" + variant, 0.11f, (t, duration) =>
            {
                float envelope = Decay(t, duration, 26f);

                // Heavily low-passed noise rather than a click: canvas over board absorbs
                // almost everything above a few hundred hertz, and a bright step reads as
                // walking on tile.
                previous += (NextNoise() - previous) * 0.09f;

                float body = Mathf.Sin(2f * Mathf.PI * bodyHz * t) * 0.5f;
                return (previous * 2.2f + body) * envelope * 0.5f;
            });
        }

        /// <summary>
        /// A hard exhale. Played when a fighter is running out of breath.
        ///
        /// Two filtered noise bands rather than one: a single band reads as wind, and the
        /// thing that makes an exhale sound like a person is that it has a voiced floor under
        /// the air. Shaped to open and close rather than to decay, because a breath has a
        /// beginning and an end and a decay envelope only has an end.
        /// </summary>
        internal static AudioClip CreateBreath(int variant = 0)
        {
            Seed(variant);
            float air = 0f;
            float chest = 0f;
            float scale = VariantScale(variant, 0.18f);

            return Build("sfx_breath_" + variant, 0.38f, (t, duration) =>
            {
                float progress = t / duration;

                // Opens quickly and closes slowly, which is the shape of an exhale rather
                // than the symmetric swell of a whoosh.
                float envelope = Mathf.Sin(Mathf.Pow(progress, 0.6f) * Mathf.PI);

                air += (NextNoise() - air) * 0.30f * scale;
                chest += (NextNoise() - chest) * 0.035f * scale;

                return (air * 0.35f + chest * 1.5f) * envelope * 0.45f;
            });
        }

        /// <summary>
        /// A fighter hitting the ropes: a dull thud with a slack, detuned ring after it.
        ///
        /// The ring is what distinguishes it from a punch. Rope is under tension and springs
        /// back, so the tail bends in pitch; a punch's tail only falls away.
        /// </summary>
        internal static AudioClip CreateRopeThud(int variant = 0)
        {
            Seed(variant);
            float scale = VariantScale(variant, 0.15f);

            return Build("sfx_rope_" + variant, 0.34f, (t, duration) =>
            {
                float progress = t / duration;
                float envelope = Decay(t, duration, 9f);

                // Bends upward as the rope takes the weight and pulls back.
                float frequency = Mathf.Lerp(74f * scale, 108f * scale, progress);
                float body = Mathf.Sin(2f * Mathf.PI * frequency * t);
                float creak = Mathf.Sin(2f * Mathf.PI * frequency * 3.7f * t) * 0.22f;
                float thud = NextNoise() * Decay(t, duration, 48f) * 0.5f;

                return (body * 0.7f + creak + thud) * envelope * 0.6f;
            });
        }

        /// <summary>
        /// The crowd bed: a seamless loop of filtered noise with a slow swell under it.
        ///
        /// The mixer has routed a dedicated Ambience group since it was built and nothing has
        /// ever played into it, so the ring has been silent between punches for the whole life
        /// of the project. A crowd is what a room full of people sounds like from inside it,
        /// which is almost entirely broadband noise shaped by the room - so noise through a
        /// band-pass is not an approximation here, it is the thing itself.
        ///
        /// Built through <see cref="BuildLoop"/> rather than <see cref="Build"/>: the ordinary
        /// builder fades both ends to zero to stop the one-shots clicking, and a bed that
        /// faded to silence every four seconds would pulse rather than sustain.
        /// </summary>
        internal static AudioClip CreateCrowdBed()
        {
            Seed(97);

            float low = 0f;
            float mid = 0f;
            float high = 0f;

            return BuildLoop("sfx_crowd_bed", 4.2f, 0.5f, (t, duration) =>
            {
                // Three bands rather than one. A single low-pass reads as rain; what makes a
                // crowd is that the energy is spread with a hump in the middle where voices
                // live, and that the top is present but soft.
                float source = NextNoise();
                low += (source - low) * 0.020f;
                mid += (source - mid) * 0.140f;
                high += (source - high) * 0.480f;

                float band = low * 1.5f + (mid - low) * 1.9f + (high - mid) * 0.5f;

                // Two slow, mutually prime swells so the bed never settles into an obvious
                // period. Both complete a whole number of cycles over the clip, or the
                // crossfade would splice two different points of the swell together.
                float swell = 1f
                    + 0.14f * Mathf.Sin(2f * Mathf.PI * 3f * t / duration)
                    + 0.09f * Mathf.Sin(2f * Mathf.PI * 5f * t / duration);

                return band * swell * 0.5f;
            });
        }

        /// <summary>
        /// A crowd reaction: the room coming up and settling again. Fired on a knockout or a
        /// heavy landed punch.
        ///
        /// Deliberately slower to arrive than the punch that caused it. A crowd noticing a
        /// blow takes a beat, and a roar that starts on the same sample as the impact reads as
        /// part of the impact rather than as a reaction to it.
        /// </summary>
        internal static AudioClip CreateCrowdSwell(int variant = 0)
        {
            Seed(200 + variant);

            float low = 0f;
            float mid = 0f;
            float peak = 0.34f * VariantScale(variant, 0.22f);

            return Build("sfx_crowd_swell_" + variant, 1.9f, (t, duration) =>
            {
                float progress = t / duration;

                float source = NextNoise();
                low += (source - low) * 0.030f;
                mid += (source - mid) * 0.190f;

                // Rises over the first third and falls away over the rest. Raised to a power
                // so the attack is a swell rather than a step.
                float envelope = progress < peak
                    ? Mathf.Pow(progress / peak, 1.7f)
                    : Mathf.Pow(1f - (progress - peak) / (1f - peak), 1.4f);

                return (low * 1.4f + (mid - low) * 2.3f) * envelope * 0.8f;
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

        /// <summary>
        /// Builds a clip that loops without a seam, by crossfading its own tail back over its
        /// head and then discarding the tail.
        ///
        /// <see cref="Build"/> cannot be used for anything looping: it fades both ends to zero
        /// so a one-shot does not click, and a sustained bed built that way would drop to
        /// silence on every wrap - a pulse rather than a loop. Crossfading instead means the
        /// last <paramref name="crossfadeSeconds"/> of material is mixed into the first, so
        /// the sample after the end is the sample the loop point already played.
        ///
        /// The shaping function is still evaluated over the full <paramref name="duration"/>;
        /// the returned clip is shorter by the crossfade, which is why anything periodic in
        /// the shape has to complete a whole number of cycles over the full duration or the
        /// two ends will be at different points of it when they meet.
        /// </summary>
        private static AudioClip BuildLoop(
            string name,
            float duration,
            float crossfadeSeconds,
            System.Func<float, float, float> shape)
        {
            int sampleCount = Mathf.CeilToInt(SAMPLE_RATE * duration);
            int fade = Mathf.Clamp(
                Mathf.CeilToInt(SAMPLE_RATE * crossfadeSeconds), 1, sampleCount / 2);

            float[] samples = new float[sampleCount];

            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                float t = sampleIndex / (float)SAMPLE_RATE;
                samples[sampleIndex] = shape(t, duration);
            }

            int loopLength = sampleCount - fade;

            for (int offset = 0; offset < fade; offset++)
            {
                // Equal-power rather than linear. Two uncorrelated noise signals summed with
                // linear gains dip by about 3dB through the middle of the crossfade, which is
                // audible on a sustained bed as a dropout once per loop.
                float position = offset / (float)fade;
                float incomingGain = Mathf.Sqrt(position);
                float outgoingGain = Mathf.Sqrt(1f - position);

                samples[offset] = samples[offset] * incomingGain
                                  + samples[loopLength + offset] * outgoingGain;
            }

            float[] looped = new float[loopLength];
            System.Array.Copy(samples, looped, loopLength);

            for (int sampleIndex = 0; sampleIndex < loopLength; sampleIndex++)
            {
                looped[sampleIndex] = Mathf.Clamp(looped[sampleIndex], -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(name, loopLength, 1, SAMPLE_RATE, false);
            clip.SetData(looped, 0);
            return clip;
        }
    }
}
