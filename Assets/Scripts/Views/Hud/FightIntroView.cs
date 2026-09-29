using System.Collections.Generic;
using System.Text;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// The tale of the tape, for the 3.8 seconds between FIGHT and the bell: two contestants
    /// side by side with their faces, rating, record and price.
    ///
    /// That stretch used to be a caption over a still ring - GET READY, 3, 2, 1 - which is dead
    /// air. A ten-way has no main event, so <see cref="HeadlineMath"/> picks one worth billing:
    /// the viewer's pick against the favourite they are betting against, or the two favourites
    /// when there is no pick.
    ///
    /// Written once, when the fight is introduced, and hidden at the bell. Sits in the bottom
    /// band, clear of the field board above and the countdown number in the middle.
    ///
    /// A View: reads models on a phase edge and writes text, images and one class.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class FightIntroView : MonoBehaviour
    {
        private const string HIDDEN_CLASS = "tape--hidden";

        [Tooltip("The tale of the tape's structure.")]
        [SerializeField] private VisualTreeAsset _layout;

        [Tooltip("The shared HUD stylesheet.")]
        [SerializeField] private StyleSheet _styleSheet;

        private readonly CompositeDisposable _disposables = new();
        private readonly StringBuilder _builder = new(48);
        private readonly List<FighterProfile> _contestants = new();

        private float[] _odds = System.Array.Empty<float>();

        private MatchFlowModel _flow;
        private RosterModel _roster;
        private RatingModel _ratings;
        private WinOddsSystem _winOdds;
        private PredictionModel _predictions;
        private PredictionSystem _predictionSystem;

        private VisualElement _panel;
        private Label _headline;

        [Inject]
        public void Construct(
            MatchFlowModel flow,
            RosterModel roster,
            RatingModel ratings,
            WinOddsSystem winOdds,
            PredictionModel predictions,
            PredictionSystem predictionSystem)
        {
            _flow = flow;
            _roster = roster;
            _ratings = ratings;
            _winOdds = winOdds;
            _predictions = predictions;
            _predictionSystem = predictionSystem;
        }

        private void Start()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            if (root == null || _layout == null || _flow == null)
            {
                return;
            }

            if (_styleSheet != null)
            {
                root.styleSheets.Add(_styleSheet);
            }

            _layout.CloneTree(root);
            root.pickingMode = PickingMode.Ignore;

            _panel = root.Q<VisualElement>("panel");
            _headline = root.Q<Label>("headline");

            _flow.Phase.Subscribe(OnPhaseChanged).AddTo(_disposables);
        }

        private void OnPhaseChanged(MatchFlowPhase phase)
        {
            if (_panel == null)
            {
                return;
            }

            bool up = phase == MatchFlowPhase.Introducing || phase == MatchFlowPhase.Countdown;

            // Written on the way in only. The countdown is the same bill, and rewriting it there
            // would let a price that ticked during the intro change under the viewer's eyes.
            if (phase == MatchFlowPhase.Introducing)
            {
                up = Build();
            }

            _panel.EnableInClassList(HIDDEN_CLASS, !up);
        }

        /// <summary>Fills both sides. Returns false when the card cannot make a bill of two.</summary>
        private bool Build()
        {
            _contestants.Clear();
            IReadOnlyList<FighterProfile> entrants = _roster.Entrants;

            for (int index = 0; index < entrants.Count; index++)
            {
                _contestants.Add(entrants[index]);
            }

            if (_odds.Length < _contestants.Count)
            {
                _odds = new float[_contestants.Count];
            }

            FighterProfile pick = _predictions.StakedOn != null ? _predictions.StakedOn : _predictions.Pick.Value;
            int pickIndex = HeadlineMath.NONE;

            for (int index = 0; index < _contestants.Count; index++)
            {
                _odds[index] = _winOdds.OddsFor(_contestants[index]);

                if (pick != null && _contestants[index] == pick)
                {
                    pickIndex = index;
                }
            }

            if (!HeadlineMath.Choose(_odds, _contestants.Count, pickIndex, out int first, out int second))
            {
                return false;
            }

            if (_headline != null)
            {
                _headline.text = pickIndex != HeadlineMath.NONE
                    ? "YOUR PICK  VS  THE FAVOURITE"
                    : "THE TWO FAVOURITES";
            }

            WriteSide("a", _contestants[first], _odds[first]);
            WriteSide("b", _contestants[second], _odds[second]);
            return true;
        }

        private void WriteSide(string side, FighterProfile profile, float odds)
        {
            VisualElement root = _panel;

            VisualElement portrait = root.Q<VisualElement>("portrait-" + side);

            if (portrait != null)
            {
                if (profile.Face != null)
                {
                    portrait.style.backgroundImage = new StyleBackground(profile.Face);
                    portrait.style.backgroundColor = StyleKeyword.Null;
                }
                else
                {
                    portrait.style.backgroundImage = StyleKeyword.None;
                    portrait.style.backgroundColor = profile.Tint;
                }
            }

            SetText(root, "name-" + side, profile.DisplayName);

            RatingRecord record = RecordOf(profile);

            _builder.Clear();
            _builder.Append(Mathf.RoundToInt(_ratings.RatingOf(profile.Id))).Append(" ELO");
            SetText(root, "rating-" + side, _builder.ToString());

            _builder.Clear();

            if (record != null && record.Matches > 0)
            {
                _builder.Append(record.Wins).Append(record.Wins == 1 ? " WIN IN " : " WINS IN ").Append(record.Matches);

                if (record.Knockouts > 0)
                {
                    _builder.Append("   ").Append(record.Knockouts).Append(" KO");
                }
            }
            else
            {
                _builder.Append("DEBUT");
            }

            SetText(root, "record-" + side, _builder.ToString());

            _builder.Clear();
            // The same words as the title tiles and the stake line: what a bet returns, not the
            // multiplier it is priced at.
            _builder.Append(Mathf.RoundToInt(odds * 100f)).Append("% TO WIN   ")
                    .Append(PredictionSystem.STAKE).Append(" WINS ")
                    .Append(Mathf.RoundToInt(PredictionSystem.STAKE * _predictionSystem.MultiplierFor(profile)));
            SetText(root, "price-" + side, _builder.ToString());
        }

        /// <summary>Looked up, not created: GetOrCreate would add a record as a side effect of a view reading it.</summary>
        private RatingRecord RecordOf(FighterProfile profile)
        {
            IReadOnlyList<RatingRecord> records = _ratings.Records;

            for (int index = 0; index < records.Count; index++)
            {
                if (records[index].Id == profile.Id)
                {
                    return records[index];
                }
            }

            return null;
        }

        private static void SetText(VisualElement root, string name, string text)
        {
            Label label = root.Q<Label>(name);

            if (label != null)
            {
                label.text = text;
            }
        }

        private void OnDestroy() => _disposables.Dispose();
    }
}
