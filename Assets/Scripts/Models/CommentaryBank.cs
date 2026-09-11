using System.Collections.Generic;
using UnityEngine;

namespace PoRumble.Models
{
    /// <summary>
    /// One line of commentary: the clip, the subtitle, and which event it answers.
    ///
    /// Text and audio live on the same record on purpose. They are generated from one file by
    /// `Tools/bake_commentary.sh`, and pairing them here is what stops a later edit leaving
    /// the printed line saying something the voice does not.
    /// </summary>
    [System.Serializable]
    public sealed class CommentaryEntry
    {
        [SerializeField] private string _id;
        [SerializeField] private CommentaryEvent _event;
        [SerializeField] private string _text;
        [SerializeField] private AudioClip _clip;

        [Tooltip("Play the subject's name clip before this line. The body is then phrased to " +
                 "follow one - \"is in real trouble\" rather than \"he is in real trouble\".")]
        [SerializeField] private bool _usesName;

        public string Id => _id;
        public CommentaryEvent Event => _event;
        public string Text => _text;
        public AudioClip Clip => _clip;
        public bool UsesName => _usesName;
    }

    /// <summary>A contestant's name, spoken.</summary>
    [System.Serializable]
    public sealed class CommentaryName
    {
        [SerializeField] private string _displayName;
        [SerializeField] private AudioClip _clip;

        public string DisplayName => _displayName;
        public AudioClip Clip => _clip;
    }

    /// <summary>
    /// Everything the commentator can say.
    ///
    /// A ScriptableObject because it is static configuration in the sense the project's rules
    /// mean: authored once, never mutated at runtime, and swappable without touching code. It
    /// is also the seam that makes the whole feature optional - a scene with no bank assigned
    /// simply has a silent commentator, which is what the training arenas get.
    ///
    /// Rebuilt from `Tools/commentary_lines.json` by `Temp/evals/build_commentary_bank.cs`
    /// rather than filled in by hand: fifty-two clips matched to fifty-two subtitles is not
    /// work a person should do twice.
    /// </summary>
    [CreateAssetMenu(menuName = "PoRumble/Commentary Bank", fileName = "CommentaryBank")]
    public sealed class CommentaryBank : ScriptableObject
    {
        [SerializeField] private List<CommentaryEntry> _entries = new();
        [SerializeField] private List<CommentaryName> _names = new();

        /// <summary>
        /// Scratch list reused by <see cref="CollectVariants"/>. The selection runs whenever
        /// anything happens in the ring, and a fresh List per line would put an allocation on
        /// the path of every landed punch.
        /// </summary>
        private readonly List<CommentaryEntry> _scratch = new(8);

        public IReadOnlyList<CommentaryEntry> Entries => _entries;

        /// <summary>
        /// Every line that answers an event. The returned list is reused between calls, so
        /// read it before asking again.
        /// </summary>
        public IReadOnlyList<CommentaryEntry> CollectVariants(CommentaryEvent trigger)
        {
            _scratch.Clear();

            for (int entryIndex = 0; entryIndex < _entries.Count; entryIndex++)
            {
                CommentaryEntry entry = _entries[entryIndex];

                if (entry != null && entry.Event == trigger)
                {
                    _scratch.Add(entry);
                }
            }

            return _scratch;
        }

        /// <summary>The entry with this id, or null. Used by the view to resolve a cue.</summary>
        public CommentaryEntry Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (int entryIndex = 0; entryIndex < _entries.Count; entryIndex++)
            {
                if (_entries[entryIndex] != null && _entries[entryIndex].Id == id)
                {
                    return _entries[entryIndex];
                }
            }

            return null;
        }

        /// <summary>
        /// The spoken form of a contestant's name, or null when nobody baked one.
        ///
        /// Matched on the profile's display name rather than on an index, so adding a
        /// contestant to the card is a matter of baking one more clip - not of keeping two
        /// orderings in step.
        /// </summary>
        public AudioClip NameClipFor(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                return null;
            }

            for (int nameIndex = 0; nameIndex < _names.Count; nameIndex++)
            {
                if (_names[nameIndex] != null && _names[nameIndex].DisplayName == displayName)
                {
                    return _names[nameIndex].Clip;
                }
            }

            return null;
        }
    }
}
