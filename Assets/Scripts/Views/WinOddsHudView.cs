using System.Collections.Generic;
using System.Text;
using PoRumble.Models;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// The odds board: each fighter's live chance of being the last one standing, the
    /// head-to-head split for the pair on camera, and the viewer's own stake.
    ///
    /// A View throughout. The numbers are <see cref="WinOddsModel"/>'s and the stake is
    /// <see cref="PredictionModel"/>'s; this draws them when either model says it moved and
    /// never polls.
    ///
    /// Stays up during the live fight, which almost nothing else does - see
    /// <see cref="HudVisibilityView"/>. A win probability is the one figure worth reading
    /// while the fight is happening, because it is the one that is moving.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class WinOddsHudView : MonoBehaviour
    {
        [Tooltip("The board's structure. Without it the panel renders nothing.")]
        [SerializeField] private VisualTreeAsset _layout;

        [Tooltip("One line of the board, cloned once per shown place.")]
        [SerializeField] private VisualTreeAsset _rowTemplate;

        [Tooltip("The shared HUD stylesheet. Without it the panel renders unstyled.")]
        [SerializeField] private StyleSheet _styleSheet;

        [Tooltip("How many fighters the board lists. Four fits beside the ring on a phone; the " +
                 "rest of a ten-way is below a few percent for most of the fight anyway.")]
        [Min(1)]
        [SerializeField] private int _places = 4;

        [Tooltip("Smallest odds movement, in percentage points, that shows as a trend. Below " +
                 "this the sign is noise from the momentum term decaying.")]
        [SerializeField] private float _trendThreshold = 1.5f;

        private readonly CompositeDisposable _disposables = new();
        private readonly StringBuilder _builder = new(64);

        /// <summary>
        /// For the small formatters. Separate from <see cref="_builder"/> because the pick line and
        /// the duel labels are composed out of their results, and a formatter that cleared the
        /// shared builder mid-composition would wipe the line it was being appended to.
        /// </summary>
        private readonly StringBuilder _scratch = new(16);
        private readonly List<VisualElement> _rows = new();
        private readonly List<Label> _names = new();
        private readonly List<Label> _values = new();
        private readonly List<Label> _trends = new();
        private readonly List<VisualElement> _fills = new();

        private MatchModel _match;
        private RosterModel _roster;
        private WinOddsModel _odds;
        private PredictionModel _predictions;
        private DirectorModel _director;
        private MatchFlowModel _flow;

        private VisualElement _panel;
        private VisualElement _duel;
        private VisualElement _duelFill;
        private Label _duelA;
        private Label _duelB;
        private Label _pick;

        [Inject]
        public void Construct(
            MatchModel match,
            RosterModel roster,
            WinOddsModel odds,
            PredictionModel predictions,
            DirectorModel director,
            MatchFlowModel flow)
        {
            _match = match;
            _roster = roster;
            _odds = odds;
            _predictions = predictions;
            _director = director;
            _flow = flow;
        }

        private void Start()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            if (root == null || _odds == null)
            {
                return;
            }

            if (_styleSheet != null)
            {
                root.styleSheets.Add(_styleSheet);
            }

            root.pickingMode = PickingMode.Ignore;

            if (_layout == null || _rowTemplate == null)
            {
                Debug.LogError(
                    $"{nameof(WinOddsHudView)} is missing its layout or row template; the odds " +
                    "board will not render. Assign Assets/UI/Layouts/WinOdds.uxml and " +
                    "Templates/OddsRow.uxml.", this);
                return;
            }

            _layout.CloneTree(root);

            _panel = root.Q<VisualElement>("panel");
            _duel = root.Q<VisualElement>("duel");
            _duelFill = root.Q<VisualElement>("duel-fill");
            _duelA = root.Q<Label>("duel-a");
            _duelB = root.Q<Label>("duel-b");
            _pick = root.Q<Label>("pick");

            BuildRows(root.Q<VisualElement>("rows"));

            _odds.Revision.Subscribe(_ => RefreshOdds()).AddTo(_disposables);
            _predictions.Revision.Subscribe(_ => RefreshPick()).AddTo(_disposables);
            _predictions.Pick.Subscribe(_ => RefreshPick()).AddTo(_disposables);
            _flow.Phase.Subscribe(OnFlowPhaseChanged).AddTo(_disposables);
        }

        private void BuildRows(VisualElement container)
        {
            if (container == null)
            {
                return;
            }

            for (int place = 0; place < _places; place++)
            {
                _rowTemplate.CloneTree(container);
                VisualElement row = container[container.childCount - 1];

                _rows.Add(row);
                _names.Add(row.Q<Label>("name"));
                _values.Add(row.Q<Label>("value"));
                _trends.Add(row.Q<Label>("trend"));
                _fills.Add(row.Q<VisualElement>("fill"));
            }
        }

        /// <summary>
        /// Up from the introduction to the results. Not on the title screen, which the menu and
        /// the pick strip own; from the introduction on it shows the prices the fight opened at,
        /// which is the tale of the tape a viewer wants before the bell.
        /// </summary>
        private void OnFlowPhaseChanged(MatchFlowPhase phase)
        {
            if (_panel == null)
            {
                return;
            }

            _panel.EnableInClassList("odds--hidden", phase == MatchFlowPhase.Title);
            RefreshOdds();
            RefreshPick();
        }

        private void RefreshOdds()
        {
            if (_panel == null)
            {
                return;
            }

            IReadOnlyList<OddsEntry> entries = _odds.Entries;
            FighterProfile pick = CurrentStake();

            for (int place = 0; place < _rows.Count; place++)
            {
                VisualElement row = _rows[place];

                if (place >= entries.Count)
                {
                    row.style.display = DisplayStyle.None;
                    continue;
                }

                OddsEntry entry = entries[place];
                row.style.display = DisplayStyle.Flex;

                SetText(_names[place], NameOf(entry));
                SetText(_values[place], FormatPercent(entry.Odds));
                SetText(_trends[place], FormatTrend(entry.Trend));

                _fills[place].style.width = Length.Percent(entry.Odds * 100f);

                float trendPoints = entry.Trend * 100f;
                row.EnableInClassList("odds__row--pick", pick != null && entry.Profile == pick);
                row.EnableInClassList("odds__row--out", !entry.IsAlive);
                _trends[place].EnableInClassList("odds__trend--up", trendPoints >= _trendThreshold);
                _trends[place].EnableInClassList("odds__trend--down", trendPoints <= -_trendThreshold);
            }

            RefreshDuel();

            if (_predictions.Outcome == PredictionOutcome.Pending)
            {
                RefreshPick();
            }
        }

        /// <summary>
        /// The pair on camera, split by their share of each other's chances. Normalised to the
        /// two of them rather than showing their field odds, because 9% against 6% in a ten-way
        /// is two small numbers; 60/40 is a fight.
        /// </summary>
        private void RefreshDuel()
        {
            if (_duel == null)
            {
                return;
            }

            int indexA = IndexOf(_director.FocusId);
            int indexB = IndexOf(_director.RivalId);

            bool show = indexA >= 0 && indexB >= 0 && _flow.Phase.Value != MatchFlowPhase.Results;
            _duel.EnableInClassList("odds__duel--hidden", !show);

            if (!show)
            {
                return;
            }

            float oddsA = _odds.SeatOdds(indexA);
            float oddsB = _odds.SeatOdds(indexB);
            float total = oddsA + oddsB;
            float shareA = total > 0f ? oddsA / total : 0.5f;

            SetText(_duelA, DuelLabel(indexA, shareA));
            SetText(_duelB, DuelLabel(indexB, 1f - shareA));

            if (_duelFill != null)
            {
                _duelFill.style.width = Length.Percent(shareA * 100f);
            }
        }

        private void RefreshPick()
        {
            if (_pick == null)
            {
                return;
            }

            _builder.Clear();

            switch (_predictions.Outcome)
            {
                case PredictionOutcome.Pending:
                    _builder.Append("YOUR PICK  ").Append(ProfileName(_predictions.StakedOn))
                            .Append("   PAYS ").Append(FormatMultiplier(_predictions.LockedMultiplier))
                            .Append("   NOW ").Append(FormatPercent(OddsOfStake()));
                    break;

                case PredictionOutcome.Won:
                    _builder.Append(ProfileName(_predictions.StakedOn)).Append(" WON  +")
                            .Append(_predictions.LastPayout - _predictions.Stake)
                            .Append("   BANK ").Append(_predictions.Bank.Value);
                    break;

                case PredictionOutcome.Lost:
                    _builder.Append(ProfileName(_predictions.StakedOn)).Append(" LOST  -")
                            .Append(_predictions.Stake)
                            .Append("   BANK ").Append(_predictions.Bank.Value);
                    break;

                case PredictionOutcome.Refunded:
                    _builder.Append("NO DECISION - STAKE RETURNED   BANK ").Append(_predictions.Bank.Value);
                    break;

                default:
                    _builder.Append("BANK ").Append(_predictions.Bank.Value)
                            .Append("   PICK A WINNER ON THE MENU");
                    break;
            }

            SetText(_pick, _builder.ToString());
            _pick.EnableInClassList("odds__pick--won", _predictions.Outcome == PredictionOutcome.Won);
            _pick.EnableInClassList("odds__pick--lost", _predictions.Outcome == PredictionOutcome.Lost);
        }

        /// <summary>The contestant a live stake rides on, for highlighting their row. Null with nothing riding.</summary>
        private FighterProfile CurrentStake()
        {
            return _predictions.Outcome == PredictionOutcome.Pending ? _predictions.StakedOn : null;
        }

        private float OddsOfStake()
        {
            OddsEntry entry = _odds.EntryFor(_predictions.StakedOn);
            return entry != null ? entry.Odds : 0f;
        }

        private string DuelLabel(int index, float share)
        {
            string name = SeatName(index);
            _builder.Clear();
            _builder.Append(name).Append(' ').Append(Mathf.RoundToInt(share * 100f)).Append('%');
            return _builder.ToString();
        }

        private string NameOf(OddsEntry entry)
        {
            if (entry.Profile != null)
            {
                return entry.Profile.DisplayName;
            }

            _scratch.Clear();
            _scratch.Append('#').Append(entry.BoxerId.ToString("00"));
            return _scratch.ToString();
        }

        private string SeatName(int index)
        {
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;
            FighterProfile profile = _roster.SeatOf(boxers[index].Id);

            if (profile != null)
            {
                return profile.DisplayName;
            }

            _scratch.Clear();
            _scratch.Append('#').Append(boxers[index].Id.ToString("00"));
            return _scratch.ToString();
        }

        private static string ProfileName(FighterProfile profile)
        {
            return profile != null ? profile.DisplayName : "?";
        }

        private string FormatPercent(float odds)
        {
            _scratch.Clear();
            float percent = odds * 100f;

            // Under one percent reads as "<1%" rather than "0%": a fighter still standing is
            // never a certain loser, and a zero next to their name says they are.
            if (percent > 0f && percent < 1f)
            {
                _scratch.Append("<1%");
            }
            else
            {
                _scratch.Append(Mathf.RoundToInt(percent)).Append('%');
            }

            return _scratch.ToString();
        }

        private string FormatTrend(float trend)
        {
            float points = trend * 100f;

            if (Mathf.Abs(points) < _trendThreshold)
            {
                return string.Empty;
            }

            _scratch.Clear();
            _scratch.Append(points > 0f ? "+" : "-").Append(Mathf.RoundToInt(Mathf.Abs(points)));
            return _scratch.ToString();
        }

        private string FormatMultiplier(float multiplier)
        {
            _scratch.Clear();
            _scratch.Append(multiplier.ToString("0.0")).Append('x');
            return _scratch.ToString();
        }

        /// <summary>Writes a label only when it changed, so an unchanged board does not dirty its layout four times a second.</summary>
        private static void SetText(Label label, string text)
        {
            if (label != null && label.text != text)
            {
                label.text = text;
            }
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

        private void OnDestroy() => _disposables.Dispose();
    }
}
