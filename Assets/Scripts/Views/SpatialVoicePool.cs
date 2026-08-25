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
        private readonly AudioSource[] _voices;
        private int _next;

        internal SpatialVoicePool(
            Transform parent,
            int voiceCount,
            AudioMixerGroup mixerGroup,
            float minDistance,
            float maxDistance)
        {
            _voices = new AudioSource[Mathf.Max(1, voiceCount)];

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
            voice.pitch = pitch;

            // PlayOneShot rather than Play, so a stolen voice layers the new hit over the tail
            // of the old one instead of cutting it dead.
            voice.PlayOneShot(clip, volume);
        }

        internal void SetMixerGroup(AudioMixerGroup group)
        {
            for (int index = 0; index < _voices.Length; index++)
            {
                _voices[index].outputAudioMixerGroup = group;
            }
        }
    }
}
