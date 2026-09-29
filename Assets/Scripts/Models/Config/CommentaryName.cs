using UnityEngine;

namespace PoRumble.Models
{
    /// <summary>A contestant's name, spoken.</summary>
    [System.Serializable]
    public sealed class CommentaryName
    {
        [SerializeField] private string _displayName;
        [SerializeField] private AudioClip _clip;

        public string DisplayName => _displayName;
        public AudioClip Clip => _clip;
    }
}
