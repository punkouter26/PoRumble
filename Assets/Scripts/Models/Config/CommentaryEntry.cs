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
}
