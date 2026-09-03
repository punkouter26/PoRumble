using UnityEngine;
using UnityEngine.Audio;

namespace PoRumble.Views
{
    /// <summary>
    /// A round-robin pool of positioned <see cref="AudioSource"/> voices.
    ///
    /// The feedback layer previously played every sound through a single 2D source, so a punch
    /// thrown across the ring and one landing on your own face were indistinguishable and both
    /// arrived dead centre. In a ten-fighter brawl that is most of the spatial information the
    /// player has to work with.
    ///
    /// Voices are built once and reused. Round-robin rather than "find a free one" on purpose:
    /// a busy exchange should steal the oldest voice and keep going rather than silently drop
    /// the newest hit, which is the one the player is actually looking at.
    /// </summary>
    internal sealed class SpatialVoicePool
    {
        /// <summary>Cutoff at the listener's ear. Effectively open - nothing is filtered.</summary>
        private const float NEAR_CUTOFF_HZ = 22_000f;

        /// <summary>Cutoff at <c>maxDistance</c>. Dull, but still clearly a punch.</summary>
        private const float FAR_CUTOFF_HZ = 1_100f;

        private readonly AudioSource[] _voices;

        private uint _randomState = 0x2545F491;
        private int _next;

        internal SpatialVoicePool(
            Transform parent,
            int voiceCount,
            AudioMixerGroup mixerGroup,
            float minDistance,
            float maxDistance)
        {
            _voices = new AudioSource[Mathf.Max(1, voiceCount)];

            // Air absorbs high frequencies faster than low ones, so a hit across a 40-unit ring
            // should arrive dull as well as quiet. Volume rolloff alone reads as someone turning
            // a knob down; losing the crack of the transient is what actually reads as distance.
            AnimationCurve cutoffByDistance = new(
                new Keyframe(0f, NEAR_CUTOFF_HZ),
                new Keyframe(0.35f, NEAR_CUTOFF_HZ * 0.45f),
                new Keyframe(1f, FAR_CUTOFF_HZ));

            for (int index = 0; index < _voices.Length; index++)
            {
                GameObject host = new($"Voice_{index:00}");
                host.transform.SetParent(parent, false);

                AudioSource source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = minDistance;
                source.maxDistance = maxDistance;
                source.dopplerLevel = 0f;
                source.outputAudioMixerGroup = mixerGroup;

                // Unity evaluates this curve against the source's own distance to the listener,
                // normalised over min..max. That is why the filter needs no per-frame update
                // from us and why nothing here has to know where the camera is.
                AudioLowPassFilter filter = host.AddComponent<AudioLowPassFilter>();
                filter.customCutoffCurve = cutoffByDistance;
                filter.lowpassResonanceQ = 1f;

                _voices[index] = source;
            }
        }

        /// <summary>Plays a clip at a world position on the next voice in the ring.</summary>
        internal void PlayAt(AudioClip clip, Vector2 position, float pitch, float volume)
        {
            if (clip == null)
            {
                return;
            }

            AudioSource voice = _voices[_next];
            _next = (_next + 1) % _voices.Length;

            voice.transform.position = new Vector3(position.x, position.y, 0f);

            // Jitter on top of whatever pitch the caller asked for, never instead of it: a
            // counter is deliberately pitched up and that has to survive. +/-6% is under a
            // semitone, which colours a repeat without making it a different sound.
            voice.pitch = pitch * (1f + NextUnit() * 0.06f);

            // A touch of level variation for the same reason. Kept smaller than the pitch
            // spread because volume is also carrying distance, and this must not fight it.
            float gain = volume * (1f + NextUnit() * 0.08f);

            // PlayOneShot rather than Play, so a stolen voice layers the new hit over the tail
            // of the old one instead of cutting it dead.
            voice.PlayOneShot(clip, Mathf.Clamp01(gain));
        }

        internal void SetMixerGroup(AudioMixerGroup group)
        {
            for (int index = 0; index < _voices.Length; index++)
            {
                _voices[index].outputAudioMixerGroup = group;
            }
        }

        /// <summary>How many pooled voices are mid-playback. Reported by the F3 overlay.</summary>
        internal int CountPlaying()
        {
            int playing = 0;

            for (int index = 0; index < _voices.Length; index++)
            {
                if (_voices[index].isPlaying)
                {
                    playing++;
                }
            }

            return playing;
        }

        internal int VoiceCount => _voices.Length;

        /// <summary>
        /// A local xorshift rather than <see cref="UnityEngine.Random"/>. Presentation jitter
        /// has no business advancing the global sequence the scripted brains and spawn
        /// randomisation draw from.
        /// </summary>
        private float NextUnit()
        {
            _randomState ^= _randomState << 13;
            _randomState ^= _randomState >> 17;
            _randomState ^= _randomState << 5;
            return (_randomState & 0xFFFFFF) / (float)0x800000 - 1f;
        }
    }
}
