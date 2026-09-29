using UnityEngine;

namespace PoRumble.Views
{
    /// <summary>
    /// Builds the score's three stems in code at load time: one four-bar loop in A minor at
    /// 112bpm, split into pulse (kick and bass), drive (hats and snare) and stabs (chords and a
    /// turnaround fill).
    ///
    /// Synthesised for the same reason the impact sounds are - the project ships no music -
    /// and split into stems because that is what lets the score build without cutting: all
    /// three play from the first frame, locked to the same sample, and only their levels move.
    /// See <see cref="PoRumble.Models.MusicMath"/> for which comes in when.
    ///
    /// Every stem is the same length to the sample, and every event is written with wrap-
    /// around, so a snare tail that runs past the end of bar four lands at the start of bar one
    /// rather than being cut off - which is what makes the loop seamless.
    ///
    /// Deterministic: noise comes from a local xorshift seeded per stem, never
    /// UnityEngine.Random, so a build sounds the same every launch and generating the score
    /// cannot advance a sequence the scripted brains draw from.
    /// </summary>
    internal static class ProceduralMusic
    {
        /// <summary>Half the effects' rate. Nothing in a mix under a commentator needs more, and it halves the memory.</summary>
        private const int SAMPLE_RATE = 22050;

        private const float BPM = 112f;
        private const int BARS = 4;
        private const int BEATS_PER_BAR = 4;

        private const float TWO_PI = Mathf.PI * 2f;

        /// <summary>Bass roots per bar, in Hz: A, A, F, G. Minor, and it resolves back to A on the loop.</summary>
        private static readonly float[] BassRoots = { 55f, 55f, 43.65f, 49f };

        /// <summary>Chord tones per bar for the stabs: Am, Am, F, G, in close voicing around middle C.</summary>
        private static readonly float[][] Chords =
        {
            new[] { 220f, 261.63f, 329.63f },
            new[] { 220f, 261.63f, 329.63f },
            new[] { 174.61f, 220f, 261.63f },
            new[] { 196f, 246.94f, 293.66f }
        };

        /// <summary>Sixteenth-note positions within a bar where a stab lands. Syncopated on purpose: on the beat they would just double the kick.</summary>
        private static readonly int[] StabSteps = { 3, 6, 10, 14 };

        private static uint _randomState;

        private static float SecondsPerBeat => 60f / BPM;

        private static int LoopSamples => Mathf.RoundToInt(SecondsPerBeat * BEATS_PER_BAR * BARS * SAMPLE_RATE);

        internal static AudioClip CreatePulse()
        {
            float[] buffer = new float[LoopSamples];
            Seed(1);

            int beats = BEATS_PER_BAR * BARS;

            for (int beat = 0; beat < beats; beat++)
            {
                WriteKick(buffer, Offset(beat * 4));
            }

            // Eighth-note bass, ducked by the kick so the two pump together rather than fight
            // for the same low end.
            for (int eighth = 0; eighth < beats * 2; eighth++)
            {
                int bar = eighth / (BEATS_PER_BAR * 2);
                float root = BassRoots[bar];

                // The last eighth of every bar jumps the octave: a pickup into the next bar.
                bool pickup = eighth % (BEATS_PER_BAR * 2) == BEATS_PER_BAR * 2 - 1;
                WriteBass(buffer, Offset(eighth * 2), pickup ? root * 2f : root, SecondsPerBeat * 0.45f);
            }

            return ToClip("music_pulse", buffer);
        }

        internal static AudioClip CreateDrive()
        {
            float[] buffer = new float[LoopSamples];
            Seed(2);

            int sixteenths = BEATS_PER_BAR * BARS * 4;

            for (int step = 0; step < sixteenths; step++)
            {
                // Accent on the off-beat eighths, which is what makes it drive rather than tick.
                float accent = step % 4 == 2 ? 0.5f : 0.22f;
                WriteHat(buffer, Offset(step), accent);
            }

            for (int beat = 1; beat < BEATS_PER_BAR * BARS; beat += 2)
            {
                WriteSnare(buffer, Offset(beat * 4));
            }

            return ToClip("music_drive", buffer);
        }

        internal static AudioClip CreateStabs()
        {
            float[] buffer = new float[LoopSamples];
            Seed(3);

            for (int bar = 0; bar < BARS; bar++)
            {
                for (int index = 0; index < StabSteps.Length; index++)
                {
                    WriteStab(buffer, Offset(bar * 16 + StabSteps[index]), Chords[bar]);
                }
            }

            // A tom fill across the last two beats: the turnaround that tells the ear the loop
            // is about to come round, so it hears a phrase rather than a repeat.
            int fillStart = (BARS - 1) * 16 + 8;

            for (int step = 0; step < 8; step++)
            {
                WriteTom(buffer, Offset(fillStart + step), Mathf.Lerp(190f, 105f, step / 7f));
            }

            return ToClip("music_stabs", buffer);
        }

        private static int Offset(int sixteenth)
        {
            return Mathf.RoundToInt(sixteenth * SecondsPerBeat * 0.25f * SAMPLE_RATE);
        }

        private static void WriteKick(float[] buffer, int start)
        {
            int length = Mathf.RoundToInt(0.34f * SAMPLE_RATE);
            float phase = 0f;

            for (int index = 0; index < length; index++)
            {
                float t = index / (float)SAMPLE_RATE;

                // The pitch sweep is the punch of a kick; the tail is the weight.
                float frequency = Mathf.Lerp(48f, 150f, Mathf.Exp(-t * 28f));
                phase += TWO_PI * frequency / SAMPLE_RATE;

                float envelope = Mathf.Exp(-t * 9f);
                Add(buffer, start + index, Mathf.Sin(phase) * envelope * 0.85f);
            }
        }

        private static void WriteBass(float[] buffer, int start, float frequency, float seconds)
        {
            int length = Mathf.RoundToInt(seconds * SAMPLE_RATE);
            float phase = 0f;
            float filtered = 0f;

            for (int index = 0; index < length; index++)
            {
                float t = index / (float)SAMPLE_RATE;
                phase += frequency / SAMPLE_RATE;
                phase -= Mathf.Floor(phase);

                // A sawtooth through a one-pole low-pass: warm, and still audible on a phone
                // speaker that cannot reproduce the fundamental, because the filter leaves some
                // of the upper harmonics in.
                float saw = phase * 2f - 1f;
                filtered += (saw - filtered) * 0.09f;

                float attack = Mathf.Clamp01(t / 0.005f);
                float release = Mathf.Clamp01((seconds - t) / 0.02f);

                // Sidechain from the kick, which lands at the start of every other eighth.
                float beatPhase = (start + index) % Mathf.RoundToInt(SecondsPerBeat * SAMPLE_RATE) / (float)SAMPLE_RATE;
                float pump = 1f - 0.55f * Mathf.Exp(-beatPhase * 12f);

                Add(buffer, start + index, filtered * attack * release * pump * 0.55f);
            }
        }

        private static void WriteHat(float[] buffer, int start, float level)
        {
            int length = Mathf.RoundToInt(0.06f * SAMPLE_RATE);
            float low = 0f;

            for (int index = 0; index < length; index++)
            {
                float t = index / (float)SAMPLE_RATE;
                float noise = NextNoise();

                // Noise minus its own low-pass is a high-pass: only the sizzle is left.
                low += (noise - low) * 0.35f;
                float envelope = Mathf.Exp(-t * 70f);
                Add(buffer, start + index, (noise - low) * envelope * level * 0.5f);
            }
        }

        private static void WriteSnare(float[] buffer, int start)
        {
            int length = Mathf.RoundToInt(0.24f * SAMPLE_RATE);
            float band = 0f;

            for (int index = 0; index < length; index++)
            {
                float t = index / (float)SAMPLE_RATE;
                band += (NextNoise() - band) * 0.45f;

                float body = Mathf.Sin(TWO_PI * 190f * t) * Mathf.Exp(-t * 30f) * 0.4f;
                float rattle = band * Mathf.Exp(-t * 17f) * 0.55f;
                Add(buffer, start + index, body + rattle);
            }
        }

        private static void WriteStab(float[] buffer, int start, float[] chord)
        {
            int length = Mathf.RoundToInt(0.3f * SAMPLE_RATE);
            float filtered = 0f;

            // Three voices per note, detuned apart. One saw per note sounds like a test tone;
            // a slightly out-of-tune stack is what reads as a section.
            float[] phases = new float[chord.Length * 3];

            for (int index = 0; index < length; index++)
            {
                float t = index / (float)SAMPLE_RATE;
                float sum = 0f;

                for (int note = 0; note < chord.Length; note++)
                {
                    for (int voice = 0; voice < 3; voice++)
                    {
                        int slot = note * 3 + voice;
                        float detune = 1f + (voice - 1) * 0.004f;
                        phases[slot] += chord[note] * detune / SAMPLE_RATE;
                        phases[slot] -= Mathf.Floor(phases[slot]);
                        sum += phases[slot] * 2f - 1f;
                    }
                }

                sum /= chord.Length * 3;

                // The filter closes over the stab, bright to dark: the brass "blat".
                float cutoff = Mathf.Lerp(0.05f, 0.4f, Mathf.Exp(-t * 14f));
                filtered += (sum - filtered) * cutoff;

                float attack = Mathf.Clamp01(t / 0.006f);
                float envelope = Mathf.Exp(-t * 8f);
                Add(buffer, start + index, filtered * attack * envelope * 0.9f);
            }
        }

        private static void WriteTom(float[] buffer, int start, float frequency)
        {
            int length = Mathf.RoundToInt(0.2f * SAMPLE_RATE);
            float phase = 0f;

            for (int index = 0; index < length; index++)
            {
                float t = index / (float)SAMPLE_RATE;
                phase += TWO_PI * frequency * (1f + 0.4f * Mathf.Exp(-t * 30f)) / SAMPLE_RATE;
                Add(buffer, start + index, Mathf.Sin(phase) * Mathf.Exp(-t * 14f) * 0.5f);
            }
        }

        /// <summary>Adds into the loop, wrapping past the end back to the start.</summary>
        private static void Add(float[] buffer, int index, float value)
        {
            buffer[index % buffer.Length] += value;
        }

        /// <summary>
        /// Soft-clips and wraps the buffer as a clip. tanh rather than a hard clamp, so where
        /// the kick and a bass note stack the peak rounds off instead of cracking.
        /// </summary>
        private static AudioClip ToClip(string name, float[] buffer)
        {
            for (int index = 0; index < buffer.Length; index++)
            {
                buffer[index] = (float)System.Math.Tanh(buffer[index]);
            }

            AudioClip clip = AudioClip.Create(name, buffer.Length, 1, SAMPLE_RATE, false);
            clip.SetData(buffer, 0);
            return clip;
        }

        private static void Seed(int stem)
        {
            _randomState = (uint)(0x9E3779B9 + stem * 0x85EBCA6B);

            if (_randomState == 0u)
            {
                _randomState = 0x9E3779B9;
            }
        }

        private static float NextNoise()
        {
            _randomState ^= _randomState << 13;
            _randomState ^= _randomState >> 17;
            _randomState ^= _randomState << 5;
            return (_randomState & 0xFFFFFF) / (float)0x800000 - 1f;
        }
    }
}
