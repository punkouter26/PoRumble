using System.Collections.Generic;
using System.Text;
using PoRumble.Models;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// The league table, top three only.
    ///
    /// Watching one match tells you who won it. The point of a standing is the run of matches
    /// behind it, and three lines is as much of that as can sit on screen without competing
    /// with the fight. The rest of the table is on the roster screen.
    ///
    /// A pure subscriber, like every other HUD here: it redraws when
    /// <see cref="RatingModel.Revision"/> moves and never polls.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class StandingsHudView : MonoBehaviour
    {
        [Tooltip("The shared HUD stylesheet. Without it the panel renders unstyled.")]
        [SerializeField] private StyleSheet _styleSheet;

        [Tooltip("How many places to show. Three fits beside the ring without crowding it.")]
        [Min(1)]
        [SerializeField] private int _places = 3;

        private static readonly string[] RankMarks = { "1", "2", "3" };

        private readonly CompositeDisposable _disposables = new();
        private readonly StringBuilder _builder = new(64);
        private readonly List<RatingRecord> _top = new(8);
        private readonly List<Label> _rows = new();

        private RatingModel _ratings;
        private MatchFlowModel _flow;
        private VisualElement _panel;

        [Inject]
        public void Construct(RatingModel ratings, MatchFlowModel flow)
        {
            _ratings = ratings;
            _flow = flow;
        }

        private void Start()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            if (root == null || _ratings == null)
            {
                return;
            }

            if (_styleSheet != null)
            {
                root.styleSheets.Add(_styleSheet);
            }

            // The document's root fills the screen. Left pickable it would swallow every
            // click aimed at a panel sorted below this one; children still hit-test normally
            // with the root ignored, so the tiles keep working.
            root.pickingMode = PickingMode.Ignore;

            _panel = new VisualElement();
            _panel.AddToClassList("standings");
            _panel.pickingMode = PickingMode.Ignore;
            root.Add(_panel);

            Label title = new("STANDINGS");
            title.AddToClassList("text");
            title.AddToClassList("text--xs");
            title.AddToClassList("text--bold");
            title.AddToClassList("standings__title");
            title.pickingMode = PickingMode.Ignore;
            _panel.Add(title);

            for (int place = 0; place < _places; place++)
            {
                Label row = new(string.Empty);
                row.AddToClassList("text");
                row.AddToClassList("text--sm");
                row.AddToClassList("standings__row");
                row.pickingMode = PickingMode.Ignore;
                _panel.Add(row);
                _rows.Add(row);
            }

            _ratings.Revision.Subscribe(_ => Refresh()).AddTo(_disposables);
            Refresh();

            if (_flow != null)
            {
                _flow.Phase.Subscribe(OnFlowPhaseChanged).AddTo(_disposables);
            }
        }

        /// <summary>
        /// Shows the table only while there is a fight for it to sit beside.
        ///
        /// This panel had no phase awareness at all, so it stayed up through the title screen
        /// and drew underneath the menu - the league table bleeding through the FIGHT button.
        /// The menu owns that phase outright, and a standing means nothing before the first
        /// bout of a session anyway.
        ///
        /// Hidden through the intro and countdown as well: those exist to put the fight in
        /// front of the player, and the table is the one thing on screen looking backwards.
        /// </summary>
        private void OnFlowPhaseChanged(MatchFlowPhase phase)
        {
            if (_panel == null)
            {
                return;
            }

            bool visible = phase == MatchFlowPhase.Fighting
                           || phase == MatchFlowPhase.KnockoutHold
                           || phase == MatchFlowPhase.Results;

            _panel.EnableInClassList("standings--hidden", !visible);
        }

        private void Refresh()
        {
            _ratings.FillTop(_top, _rows.Count);

            for (int place = 0; place < _rows.Count; place++)
            {
                Label row = _rows[place];

                if (place >= _top.Count)
                {
                    row.text = string.Empty;
                    row.EnableInClassList("standings__row--gain", false);
                    row.EnableInClassList("standings__row--loss", false);
                    continue;
                }

                RatingRecord record = _top[place];

                _builder.Clear();
                _builder.Append(place < RankMarks.Length ? RankMarks[place] : (place + 1).ToString())
                        .Append("  ")
                        .Append(record.DisplayName)
                        .Append("   ")
                        .Append(Mathf.RoundToInt(record.Rating));

                // The change from the last match, which is the only part of a rating anyone
                // watches move. Suppressed below half a point, where the sign is noise.
                if (Mathf.Abs(record.LastDelta) >= 0.5f)
                {
                    _builder.Append(record.LastDelta > 0f ? "  +" : "  ")
                            .Append(Mathf.RoundToInt(record.LastDelta));
                }

                row.text = _builder.ToString();
                row.EnableInClassList("standings__row--gain", record.LastDelta >= 0.5f);
                row.EnableInClassList("standings__row--loss", record.LastDelta <= -0.5f);
            }
        }

        private void OnDestroy() => _disposables.Dispose();
    }
}
