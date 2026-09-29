using PoRumble.Models;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// Speaks the commentary and prints it.
    ///
    /// A line is up to two clips: the fighter's name, then the body. Baking every name into
    /// every line would be eight contestants times forty-four lines of audio to regenerate
    /// whenever a word changed; concatenating at playback costs two AudioSources and keeps the
    /// bank at fifty-two clips.
    ///
    /// The join is made with <see cref="AudioSource.PlayScheduled"/> on the DSP clock rather
    /// than by waiting out the first clip and then starting the second. A frame-timed join
    /// lands a whole frame late at 60fps and audibly later under hitstop — which is a gap in
    /// the middle of a sentence, and the one artefact that would give away that the name and
    /// the line were recorded apart. The DSP clock is sample-accurate and does not care what
    /// the frame rate or the time scale is doing.
    ///
    /// Optional like every other presentation component: with no bank assigned the
    /// commentator is simply silent, which is what the training arenas get.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CommentaryView : MonoBehaviour
    {
        [Tooltip("Everything the commentator can say. Leave empty to disable commentary.")]
        [SerializeField] private CommentaryBank _bank;

        [Tooltip("Optional. The subtitle document; without one the commentary is audio only.")]
        [SerializeField] private UIDocument _subtitleDocument;

        [SerializeField] private VisualTreeAsset _subtitleLayout;
        [SerializeField] private StyleSheet _styleSheet;

        [Header("Voice")]
        [Tooltip("Plays the fighter's name. A plain 2D source on the Commentary group, not a " +
                 "pooled positional voice: the commentator is not in the ring, and placing him " +
                 "there would make him quieter whenever the camera cut away.")]
        [SerializeField] private AudioSource _nameSource;

        [Tooltip("Plays the body of the line, scheduled to follow the name.")]
        [SerializeField] private AudioSource _bodySource;

        [Tooltip("Gap between the name clip and the body, in seconds. A touch of air reads " +
                 "as a speaker drawing breath; none at all reads as two clips jammed together.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float _joinGap = 0.04f;

        [Tooltip("Seconds the subtitle stays up after the voice has finished.")]
        [SerializeField] private float _subtitleHold = 1.1f;

        private readonly CompositeDisposable _disposables = new();

        private CommentaryModel _commentary;
        private RosterModel _roster;

        private Label _subtitle;
        private float _subtitleUntil;
        private bool _subtitleShown;

        [Inject]
        public void Construct(CommentaryModel commentary, RosterModel roster)
        {
            _commentary = commentary;
            _roster = roster;
        }

        private void Start()
        {
            BuildSubtitle();

            if (_commentary != null)
            {
                _commentary.Cue.Subscribe(OnCue).AddTo(_disposables);
            }
        }

        private void BuildSubtitle()
        {
            if (_subtitleDocument == null || _subtitleLayout == null)
            {
                return;
            }

            VisualElement root = _subtitleDocument.rootVisualElement;

            if (root == null)
            {
                return;
            }

            if (_styleSheet != null)
            {
                root.styleSheets.Add(_styleSheet);
            }

            _subtitleLayout.CloneTree(root);

            // The caption sits in the chrome row beside DEBUG; its full-screen root must not
            // take a tap meant for anything else.
            root.pickingMode = PickingMode.Ignore;
            _subtitle = root.Q<Label>("commentary");

            HideSubtitle();
        }

        private void OnCue(CommentaryCue cue)
        {
            if (!cue.HasLine || _bank == null)
            {
                return;
            }

            CommentaryEntry entry = _bank.Find(cue.LineId);

            if (entry == null || entry.Clip == null)
            {
                return;
            }

            AudioClip nameClip = ResolveNameClip(entry, cue.SubjectId);

            Speak(nameClip, entry.Clip);
            ShowSubtitle(entry, cue.SubjectId, nameClip);
        }

        /// <summary>
        /// The subject's name clip, or null when the line names nobody, the seat is empty, or
        /// nobody baked a clip for that contestant.
        ///
        /// A missing clip is not an error and must not silence the line. The bodies are all
        /// phrased to stand as a sentence about "he", so a line that loses its name still
        /// reads — which is exactly the case in a scene with no fight card, and therefore in
        /// every training arena.
        /// </summary>
        private AudioClip ResolveNameClip(CommentaryEntry entry, int subjectId)
        {
            if (!entry.UsesName || subjectId == DirectorModel.NOBODY || _roster == null)
            {
                return null;
            }

            FighterProfile profile = _roster.SeatOf(subjectId);

            return profile == null ? null : _bank.NameClipFor(profile.DisplayName);
        }

        /// <summary>
        /// Plays the name and the body back to back, joined on the DSP clock.
        ///
        /// Both sources are stopped first. A new line always interrupts the old one outright:
        /// CommentarySystem has already decided this line outranks whatever was speaking, and
        /// letting two overlap would be worse than either.
        /// </summary>
        private void Speak(AudioClip nameClip, AudioClip bodyClip)
        {
            if (_nameSource == null || _bodySource == null)
            {
                return;
            }

            _nameSource.Stop();
            _bodySource.Stop();

            double now = AudioSettings.dspTime;

            // A small lead-in, because scheduling for the current instant means the mixer may
            // already be past it by the time the buffer is filled - which drops the clip
            // entirely rather than playing it late.
            double start = now + 0.05;
            double bodyAt = start;

            if (nameClip != null)
            {
                _nameSource.clip = nameClip;
                _nameSource.PlayScheduled(start);
                bodyAt = start + nameClip.length + _joinGap;
            }

            _bodySource.clip = bodyClip;
            _bodySource.PlayScheduled(bodyAt);

            float total = (float)(bodyAt - now) + bodyClip.length;
            _subtitleUntil = Time.unscaledTime + total + _subtitleHold;
        }

        /// <summary>
        /// Prints the line, with the fighter's name in front of it when the voice is saying
        /// one — matched to the audio rather than decided separately, which is the whole
        /// reason the cue carries a line id instead of a finished string.
        /// </summary>
        private void ShowSubtitle(CommentaryEntry entry, int subjectId, AudioClip nameClip)
        {
            if (_subtitle == null)
            {
                return;
            }

            string text = entry.Text;

            if (nameClip != null && _roster != null)
            {
                // The caption names the seat, numbered when a contestant fights twice; the spoken
                // clip is the contestant's name either way.
                string label = _roster.SeatLabel(subjectId);

                if (label != null)
                {
                    text = string.Concat(label, " ", text);
                }
            }

            _subtitle.text = text;
            _subtitle.EnableInClassList("commentary--on", true);
            _subtitleShown = true;
        }

        /// <summary>
        /// Unscaled, because the knockout hold slows the world right down and the line called
        /// over it is the one most worth reading.
        /// </summary>
        private void Update()
        {
            if (!_subtitleShown || Time.unscaledTime < _subtitleUntil)
            {
                return;
            }

            HideSubtitle();
        }

        private void HideSubtitle()
        {
            _subtitleShown = false;

            if (_subtitle != null)
            {
                _subtitle.EnableInClassList("commentary--on", false);
            }
        }

        private void OnDestroy()
        {
            _disposables.Dispose();
        }
    }
}
