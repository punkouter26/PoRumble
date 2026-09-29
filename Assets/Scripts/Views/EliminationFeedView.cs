using System.Text;
using MessagePipe;
using PoRumble.Models;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// The knockout feed under the fight strip: "BIGGIE KO'D BY ALAN   7 LEFT".
    ///
    /// Before this a knockout showed up only as a row on the strip going grey - easy to miss in a
    /// ten-way, and silent about who did it. The feed names both, counts down the field, and
    /// colours the line when the viewer's pick is on either end of it. It also carries the one
    /// announcement sudden death needs, the moment the ropes start to move.
    ///
    /// Three toasts, declared in the layout and reused: the newest takes the top slot and the
    /// others shift down, and each fades on its own timer. Nothing is instantiated per knockout.
    ///
    /// A View: observes a message and models, writes text and classes.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class EliminationFeedView : MonoBehaviour
    {
        private const string ON_CLASS = "feed__toast--on";
        private const string GOOD_CLASS = "feed__toast--good";
        private const string BAD_CLASS = "feed__toast--bad";
        private const string ALERT_CLASS = "feed__toast--alert";

        [Tooltip("The feed's structure: a panel holding the toast labels.")]
        [SerializeField] private VisualTreeAsset _layout;

        [Tooltip("The shared HUD stylesheet.")]
        [SerializeField] private StyleSheet _styleSheet;

        [Tooltip("Seconds a line stays up, on unscaled time so the knockout hold does not stretch it.")]
        [SerializeField] private float _holdSeconds = 4f;

        private readonly CompositeDisposable _disposables = new();
        private readonly StringBuilder _builder = new(64);

        private MatchModel _match;
        private MatchFlowModel _flow;
        private RosterModel _roster;
        private PredictionModel _predictions;
        private SuddenDeathModel _suddenDeath;

        private Label[] _toasts = System.Array.Empty<Label>();
        private float[] _expiries = System.Array.Empty<float>();

        [Inject]
        public void Construct(
            MatchModel match,
            MatchFlowModel flow,
            RosterModel roster,
            PredictionModel predictions,
            SuddenDeathModel suddenDeath,
            ISubscriber<BoxerEliminatedMessage> eliminatedSubscriber)
        {
            _match = match;
            _flow = flow;
            _roster = roster;
            _predictions = predictions;
            _suddenDeath = suddenDeath;
            eliminatedSubscriber.Subscribe(OnEliminated).AddTo(_disposables);
        }

        private void Start()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            if (root == null || _layout == null || _match == null)
            {
                return;
            }

            if (_styleSheet != null)
            {
                root.styleSheets.Add(_styleSheet);
            }

            _layout.CloneTree(root);
            root.pickingMode = PickingMode.Ignore;

            UQueryBuilder<Label> query = root.Query<Label>(className: "feed__toast");
            _toasts = query.ToList().ToArray();
            _expiries = new float[_toasts.Length];

            _flow.Phase.Subscribe(OnPhaseChanged).AddTo(_disposables);
            _suddenDeath.Closing.Subscribe(OnClosingChanged).AddTo(_disposables);
        }

        private void Update()
        {
            float now = Time.unscaledTime;

            for (int index = 0; index < _toasts.Length; index++)
            {
                if (_expiries[index] > 0f && now >= _expiries[index])
                {
                    _expiries[index] = 0f;
                    _toasts[index].RemoveFromClassList(ON_CLASS);
                }
            }
        }

        private void OnEliminated(BoxerEliminatedMessage message)
        {
            if (_flow == null || !_flow.IsFightLive && _flow.Phase.Value != MatchFlowPhase.KnockoutHold)
            {
                return;
            }

            int left = _match.CountAlive();

            _builder.Clear();
            _builder.Append(NameOf(message.BoxerId));

            if (message.EliminatedById >= 0 && message.EliminatedById != message.BoxerId)
            {
                _builder.Append(" KO'D BY ").Append(NameOf(message.EliminatedById));
            }
            else
            {
                _builder.Append(" IS OUT");
            }

            _builder.Append("    ");

            if (left == 2)
            {
                _builder.Append("FINAL TWO");
            }
            else
            {
                _builder.Append(left).Append(" LEFT");
            }

            FighterProfile pick = _predictions.Outcome == PredictionOutcome.Pending ? _predictions.StakedOn : null;
            bool pickScored = pick != null && _roster.SeatOf(message.EliminatedById) == pick;
            bool pickFell = pick != null && _roster.SeatOf(message.BoxerId) == pick;

            Push(_builder.ToString(), pickScored ? GOOD_CLASS : pickFell ? BAD_CLASS : null);
        }

        private void OnClosingChanged(bool closing)
        {
            if (closing && _flow != null && _flow.IsFightLive)
            {
                Push("SUDDEN DEATH    THE ROPES ARE CLOSING IN", ALERT_CLASS);
            }
        }

        /// <summary>Clears the feed for a fresh fight, so the last bout's knockouts are not on screen at the bell.</summary>
        private void OnPhaseChanged(MatchFlowPhase phase)
        {
            if (phase != MatchFlowPhase.Introducing && phase != MatchFlowPhase.Title)
            {
                return;
            }

            for (int index = 0; index < _toasts.Length; index++)
            {
                _expiries[index] = 0f;
                _toasts[index].RemoveFromClassList(ON_CLASS);
            }
        }

        /// <summary>Newest on top: every line moves down one slot and the oldest falls off.</summary>
        private void Push(string text, string tone)
        {
            if (_toasts.Length == 0)
            {
                return;
            }

            for (int index = _toasts.Length - 1; index > 0; index--)
            {
                Label from = _toasts[index - 1];
                Label to = _toasts[index];

                to.text = from.text;
                _expiries[index] = _expiries[index - 1];
                CopyTone(from, to);
                to.EnableInClassList(ON_CLASS, _expiries[index] > 0f);
            }

            Label top = _toasts[0];
            top.text = text;
            top.EnableInClassList(GOOD_CLASS, tone == GOOD_CLASS);
            top.EnableInClassList(BAD_CLASS, tone == BAD_CLASS);
            top.EnableInClassList(ALERT_CLASS, tone == ALERT_CLASS);
            top.AddToClassList(ON_CLASS);
            _expiries[0] = Time.unscaledTime + _holdSeconds;
        }

        private static void CopyTone(Label from, Label to)
        {
            to.EnableInClassList(GOOD_CLASS, from.ClassListContains(GOOD_CLASS));
            to.EnableInClassList(BAD_CLASS, from.ClassListContains(BAD_CLASS));
            to.EnableInClassList(ALERT_CLASS, from.ClassListContains(ALERT_CLASS));
        }

        private string NameOf(int boxerId)
        {
            string label = _roster.SeatLabel(boxerId);
            return label ?? $"#{boxerId:00}";
        }

        private void OnDestroy() => _disposables.Dispose();
    }
}
