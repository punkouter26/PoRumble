using System.Collections.Generic;
using System.Text;
using PoRumble.Models;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// The telemetry board: punch counts, connect rate and momentum for the two fighters the
    /// camera director has picked out.
    ///
    /// Reports the director's pair rather than a fixed pair or the whole field, so the board
    /// and the camera are always talking about the same fight. That is the one thing that
    /// makes a stat panel feel like broadcast rather than like a debug readout: the figures
    /// belong to what is on screen.
    ///
    /// A View throughout. It observes <see cref="FightStatsModel"/> and <see cref="DirectorModel"/>
    /// and mutates neither, and the model it reads is itself derived - so the whole feature
    /// can be deleted without the ring behaving any differently.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class FightStatsHudView : MonoBehaviour
    {
        /// <summary>
        /// Hit points of momentum differential at which the bar is pinned to one end. Roughly
        /// one clean flurry: past that the exchange is already decisively one-sided and
        /// scaling further only makes the bar twitch on noise.
        /// </summary>
        private const float MOMENTUM_FULL_SCALE = 14f;

        /// <summary>
        /// Seconds between refreshes. The figures are read, not watched, and rewriting eight
        /// labels every frame builds eight strings a frame for a panel nobody can read that
        /// fast. Six times a second is past the point where a counter looks live.
        /// </summary>
        private const float REFRESH_INTERVAL = 1f / 6f;

        /// <summary>Names of the rows, in the order they are stacked.</summary>
        private static readonly string[] RowLabels =
        {
            "THROWN",
            "LANDED",
            "CONNECT",
            "BLOCKED",
            "SLIPS",
            "DAMAGE"
        };

        [Tooltip("The board's structure. Without it the panel renders nothing at all.")]
        [SerializeField] private VisualTreeAsset _layout;

        [Tooltip("One stat line, cloned once per row.")]
        [SerializeField] private VisualTreeAsset _statRowTemplate;

        [Tooltip("The shared HUD stylesheet. Without it the panel renders unstyled.")]
        [SerializeField] private StyleSheet _styleSheet;

        [Header("Sparkline")]
        [Tooltip("Line colour for the fighter on the left of the board.")]
        [SerializeField] private Color _sparkColorA = new(0.40f, 0.85f, 0.44f);

        [Tooltip("Line colour for the fighter on the right of the board.")]
        [SerializeField] private Color _sparkColorB = new(0.92f, 0.55f, 0.28f);

        [Tooltip("Colour of the zero line the two traces are measured against.")]
        [SerializeField] private Color _sparkBaselineColor = new(1f, 1f, 1f, 0.22f);

        [SerializeField] private float _sparkLineWidth = 2f;

        private readonly StringBuilder _builder = new(24);
        private readonly List<Label> _valuesA = new(RowCount);
        private readonly List<Label> _valuesB = new(RowCount);

        private MatchModel _match;
        private RosterModel _roster;
        private FightStatsModel _stats;
        private DirectorModel _director;

        private VisualElement _panel;
        private VisualElement _spark;
        private VisualElement _momentumA;
        private VisualElement _momentumB;
        private Label _nameA;
        private Label _nameB;

        private float _refreshTimer;

        /// <summary>Seat indices of the pair currently on the board, or -1 for neither.</summary>
        private int _indexA = -1;
        private int _indexB = -1;

        private static int RowCount => RowLabels.Length;

        [Inject]
        public void Construct(
            MatchModel match,
            RosterModel roster,
            FightStatsModel stats,
            DirectorModel director)
        {
            _match = match;
            _roster = roster;
            _stats = stats;
            _director = director;
        }

        private void Start()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            if (root == null || _match == null)
            {
                return;
            }

            if (_styleSheet != null)
            {
                root.styleSheets.Add(_styleSheet);
            }

            if (_layout == null)
            {
                Debug.LogError(
                    $"{nameof(FightStatsHudView)} has no layout assigned; the telemetry board " +
                    "will not render. Assign Assets/UI/Layouts/FightStats.uxml.", this);
                return;
            }

            _layout.CloneTree(root);

            _panel = root.Q<VisualElement>("panel");
            _spark = root.Q<VisualElement>("spark");
            _momentumA = root.Q<VisualElement>("momentum-a");
            _momentumB = root.Q<VisualElement>("momentum-b");
            _nameA = root.Q<Label>("name-a");
            _nameB = root.Q<Label>("name-b");

            BuildRows(root.Q<VisualElement>("rows"));

            if (_spark != null)
            {
                _spark.generateVisualContent += DrawSparkline;
            }

            SetVisible(false);
        }

        /// <summary>
        /// Clones one line per stat and keeps the two figures on it.
        ///
        /// CloneTree(target) puts the template's own children straight into the target with no
        /// TemplateContainer between, so the freshly cloned row is the last child - the same
        /// mechanic the match HUD's health rows rely on, and for the same reason: a wrapper
        /// element would give every row a second box to inherit its sizing from.
        /// </summary>
        private void BuildRows(VisualElement rows)
        {
            if (rows == null || _statRowTemplate == null)
            {
                Debug.LogError(
                    $"{nameof(FightStatsHudView)} is missing the stat-row template or its " +
                    "container; the board will show no figures.", this);
                return;
            }

            for (int rowIndex = 0; rowIndex < RowCount; rowIndex++)
            {
                _statRowTemplate.CloneTree(rows);
                VisualElement row = rows[rows.childCount - 1];

                row.Q<Label>("label").text = RowLabels[rowIndex];
                _valuesA.Add(row.Q<Label>("value-a"));
                _valuesB.Add(row.Q<Label>("value-b"));
            }
        }

        /// <summary>
        /// Unscaled, because the board stays up through the knockout hold - which is exactly
        /// when somebody looks at it to see how the fight was won, and scaled time would have
        /// it refreshing at a quarter speed for the whole of that.
        /// </summary>
        private void Update()
        {
            if (_panel == null || _director == null)
            {
                return;
            }

            _refreshTimer += Time.unscaledDeltaTime;

            if (_refreshTimer < REFRESH_INTERVAL)
            {
                return;
            }

            _refreshTimer = 0f;
            Refresh();
        }

        private void Refresh()
        {
            int indexA = IndexOf(_director.FocusId);
            int indexB = IndexOf(_director.RivalId);

            // Nothing to report until the director has two fighters. A panel full of dashes is
            // worse than no panel, and between matches there is no exchange to describe.
            if (indexA < 0 || indexB < 0)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);

            if (indexA != _indexA || indexB != _indexB)
            {
                _indexA = indexA;
                _indexB = indexB;
                RefreshNames();
            }

            FighterStats a = _stats.For(indexA);
            FighterStats b = _stats.For(indexB);

            if (a == null || b == null)
            {
                return;
            }

            WriteRow(0, a.Thrown, b.Thrown);
            WriteRow(1, a.Landed, b.Landed);
            WritePercentRow(2, a.Accuracy, b.Accuracy);
            WriteRow(3, a.BlocksMade, b.BlocksMade);
            WriteRow(4, a.Slips, b.Slips);
            WriteRow(5, a.DamageDealt, b.DamageDealt);

            RefreshMomentum(a.Momentum, b.Momentum);

            if (_spark != null)
            {
                _spark.MarkDirtyRepaint();
            }
        }

        /// <summary>
        /// Drives the two-sided momentum bar off the difference between the pair rather than
        /// off either fighter's own reading.
        ///
        /// The difference, because the bar has one midpoint and has to say which way the
        /// exchange is running. Two fighters both being battered by the rest of the ring would
        /// otherwise fill the bar from both ends and read as a furious exchange between the
        /// two of them, which is the opposite of what happened.
        /// </summary>
        private void RefreshMomentum(float momentumA, float momentumB)
        {
            if (_momentumA == null || _momentumB == null)
            {
                return;
            }

            float differential = Mathf.Clamp(
                (momentumA - momentumB) / MOMENTUM_FULL_SCALE, -1f, 1f);

            // Half the track each, so a pinned bar reaches the panel edge rather than
            // overrunning it.
            float share = Mathf.Abs(differential) * 50f;

            _momentumA.style.width = Length.Percent(differential > 0f ? share : 0f);
            _momentumB.style.width = Length.Percent(differential < 0f ? share : 0f);
        }

        /// <summary>
        /// Draws both fighters' momentum history as two traces around a zero line.
        ///
        /// Painter2D rather than a charting package, and not only to avoid a dependency: the
        /// HUD is UI Toolkit throughout, and every third-party 2D chart for Unity is built on
        /// UGUI. Pulling one in would mean a second canvas, a second event system and a second
        /// set of scaling rules over the same screen, to draw forty-eight line segments.
        /// </summary>
        private void DrawSparkline(MeshGenerationContext context)
        {
            if (_indexA < 0 || _indexB < 0)
            {
                return;
            }

            Rect bounds = context.visualElement.contentRect;

            if (bounds.width <= 1f || bounds.height <= 1f)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            float midY = bounds.height * 0.5f;

            painter.lineWidth = 1f;
            painter.strokeColor = _sparkBaselineColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, midY));
            painter.LineTo(new Vector2(bounds.width, midY));
            painter.Stroke();

            painter.lineWidth = _sparkLineWidth;
            StrokeTrace(painter, _indexA, bounds, _sparkColorA);
            StrokeTrace(painter, _indexB, bounds, _sparkColorB);
        }

        private void StrokeTrace(Painter2D painter, int index, Rect bounds, Color color)
        {
            int samples = _stats.SampleCount;

            // One point is not a line, and asking Painter2D to stroke a zero-length path
            // leaves the path open for whatever is stroked next.
            if (samples < 2)
            {
                return;
            }

            float midY = bounds.height * 0.5f;
            float step = bounds.width / (FightStatsModel.HISTORY_LENGTH - 1);

            // A part-filled buffer is drawn against the right-hand edge, not stretched across
            // the whole panel. The newest sample must always sit in the same place or the
            // trace appears to slide leftwards for the first eight seconds of every match,
            // which reads as the graph scrolling when nothing has scrolled.
            int firstSample = FightStatsModel.HISTORY_LENGTH - samples;

            painter.strokeColor = color;
            painter.BeginPath();

            for (int sample = 0; sample < samples; sample++)
            {
                float value = _stats.HistoryAt(index, sample);
                float normalised = Mathf.Clamp(value / MOMENTUM_FULL_SCALE, -1f, 1f);

                // Negated because UI Toolkit's y axis runs downward and momentum is being read
                // as a graph, where positive is up.
                Vector2 point = new(
                    (firstSample + sample) * step,
                    midY - normalised * midY);

                if (sample == 0)
                {
                    painter.MoveTo(point);
                    continue;
                }

                painter.LineTo(point);
            }

            painter.Stroke();
        }

        private void RefreshNames()
        {
            if (_nameA != null)
            {
                _nameA.text = NameOf(_indexA);
            }

            if (_nameB != null)
            {
                _nameB.text = NameOf(_indexB);
            }
        }

        /// <summary>The seated contestant's name, or the slot number when there is no card.</summary>
        private string NameOf(int index)
        {
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            if (index < 0 || index >= boxers.Count)
            {
                return string.Empty;
            }

            FighterProfile profile = _roster.SeatOf(boxers[index].Id);

            if (profile != null)
            {
                return profile.DisplayName;
            }

            _builder.Clear();
            _builder.Append('#').Append(boxers[index].Id.ToString("00"));
            return _builder.ToString();
        }

        private void WriteRow(int rowIndex, int valueA, int valueB)
        {
            if (rowIndex >= _valuesA.Count)
            {
                return;
            }

            _valuesA[rowIndex].text = valueA.ToString();
            _valuesB[rowIndex].text = valueB.ToString();
        }

        private void WritePercentRow(int rowIndex, float valueA, float valueB)
        {
            if (rowIndex >= _valuesA.Count)
            {
                return;
            }

            _valuesA[rowIndex].text = FormatPercent(valueA);
            _valuesB[rowIndex].text = FormatPercent(valueB);
        }

        private string FormatPercent(float ratio)
        {
            _builder.Clear();
            _builder.Append(Mathf.RoundToInt(ratio * 100f)).Append('%');
            return _builder.ToString();
        }

        /// <summary>
        /// Fades the panel rather than disabling it. A hidden element stops generating visual
        /// content, so the sparkline would have to be rebuilt from scratch on the frame the
        /// board came back - and it comes back every time the director changes its pair.
        /// </summary>
        private void SetVisible(bool visible)
        {
            if (_panel == null)
            {
                return;
            }

            _panel.EnableInClassList("stats--hidden", !visible);
        }

        private int IndexOf(int boxerId)
        {
            if (boxerId == DirectorModel.NOBODY)
            {
                return -1;
            }

            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int index = 0; index < boxers.Count; index++)
            {
                if (boxers[index].Id == boxerId)
                {
                    return index;
                }
            }

            return -1;
        }

        private void OnDestroy()
        {
            if (_spark != null)
            {
                _spark.generateVisualContent -= DrawSparkline;
            }
        }
    }
}
