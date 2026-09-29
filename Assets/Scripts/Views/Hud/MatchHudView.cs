using System.Collections.Generic;
using System.Text;
using MessagePipe;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// The field board, the countdown and the results card.
    ///
    /// The field board is the one list of the fighters: each row carries a name, a health bar
    /// and a live chance of winning, with the viewer's stake marked on it. It used to be two
    /// panels - a health column here and an odds board under it that listed the same fighters
    /// again with a percentage and carried the stake line. Between bouts the board is a column
    /// of full rows; while the fight is live it collapses into a single strip of ten cells under
    /// the chrome bar, and it is the only part of the HUD that stays over the ring.
    ///
    /// The results card is the whole results screen: winner, what it did to their rating and to
    /// the viewer's stake, and REMATCH / NEW PICK. It replaced a banner, a tap-anywhere prompt
    /// and a floating fight-card button - and the tap-anywhere half of that could not survive
    /// buttons on the same screen, because it fired on the press and changed phase before the
    /// button under the finger ever saw its release.
    ///
    /// Structure comes from UXML and styling from the shared stylesheet. This class looks
    /// elements up by name and writes only what is genuinely dynamic - a bar's width, a label's
    /// text, a state class. No colours, paddings or font sizes.
    ///
    /// A View: it observes models and messages, forwards two button presses to Systems that
    /// decide for themselves whether they are legal, and never mutates game state.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class MatchHudView : MonoBehaviour
    {
        /// <summary>Written into a row's odds cell for a fighter who is out.</summary>
        private const int SHOWN_OUT = -2;

        /// <summary>Written for a fighter still standing on under one percent.</summary>
        private const int SHOWN_UNDER_ONE = -1;

        /// <summary>Nothing has been written to the cell yet.</summary>
        private const int SHOWN_NOTHING = int.MinValue;

        [Tooltip("The panel's structure. Without it the HUD renders nothing at all.")]
        [SerializeField] private VisualTreeAsset _layout;

        [Tooltip("One fighter's row on the field board, cloned once per boxer slot.")]
        [SerializeField] private VisualTreeAsset _healthRowTemplate;

        [Tooltip("The shared HUD stylesheet. Without it the panel renders unstyled.")]
        [SerializeField] private StyleSheet _styleSheet;

        [Tooltip("Smallest odds movement, in percentage points, that colours a fighter's " +
                 "percentage. Below this the sign is noise from the momentum term decaying.")]
        [SerializeField] private float _trendThreshold = 1.5f;

        private readonly CompositeDisposable _disposables = new();
        private readonly StringBuilder _builder = new(64);

        /// <summary>
        /// For the small formatters, kept apart from <see cref="_builder"/> because the stake
        /// line is composed out of their results and a formatter that cleared the shared
        /// builder mid-composition would wipe the line it was being appended to.
        /// </summary>
        private readonly StringBuilder _scratch = new(16);

        private readonly List<VisualElement> _rows = new();
        private readonly List<VisualElement> _healthFills = new();
        private readonly List<Label> _oddsLabels = new();

        /// <summary>
        /// The percentage each row last showed, so an unchanged figure is not rebuilt four
        /// times a second. Whole points, or one of the SHOWN_ markers.
        /// </summary>
        private readonly List<int> _shownOdds = new();

        /// <summary>
        /// The name beside each health bar, kept so a re-dealt card can rewrite them. Boxer
        /// slots are permanent; who is sitting in one is not.
        /// </summary>
        private readonly List<Label> _nameLabels = new();

        private MatchModel _match;
        private MatchFlowModel _flow;
        private MatchFlowSystem _flowSystem;
        private RosterModel _roster;
        private RatingModel _ratings;
        private WinOddsModel _odds;
        private PredictionModel _predictions;
        private BoxerSpawnPoints _spawnPoints;
        private BoxerConfig _config;
        private DirectorModel _director;
        private DirectorSystem _directorSystem;
        private FightStatsModel _stats;

        private VisualElement _panel;
        private VisualElement _resultPortrait;
        private Label _resultStats;
        private Label _survivorsLabel;
        private Label _boutLabel;
        private Label _pickLabel;
        private Label _captionLabel;
        private VisualElement _results;
        private Label _resultLabel;
        private Label _resultRating;
        private Label _resultPick;
        private Label _promptLabel;

        private int _winnerId = MatchModel.NO_WINNER;

        [Inject]
        public void Construct(
            MatchModel match,
            MatchFlowModel flow,
            MatchFlowSystem flowSystem,
            RosterModel roster,
            RatingModel ratings,
            WinOddsModel odds,
            PredictionModel predictions,
            BoxerSpawnPoints spawnPoints,
            BoxerConfig config,
            DirectorModel director,
            DirectorSystem directorSystem,
            FightStatsModel stats,
            ISubscriber<MatchEndedMessage> endedSubscriber)
        {
            _director = director;
            _directorSystem = directorSystem;
            _stats = stats;
            _match = match;
            _flow = flow;
            _flowSystem = flowSystem;
            _roster = roster;
            _ratings = ratings;
            _odds = odds;
            _predictions = predictions;
            _spawnPoints = spawnPoints;
            _config = config;
            endedSubscriber.Subscribe(OnMatchEnded).AddTo(_disposables);
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
                GameLog.Error(
                    $"{nameof(MatchHudView)} has no layout assigned; the match HUD will not " +
                    "render. Assign Assets/UI/Layouts/MatchHud.uxml.", this);
                return;
            }

            _layout.CloneTree(root);

            // The root spans the screen and this document stays up through the fight now. Left
            // pickable it would swallow taps meant for any panel sorted beneath it; the two
            // results buttons still hit-test with the root ignored.
            root.pickingMode = PickingMode.Ignore;

            _panel = root.Q<VisualElement>("panel");
            _survivorsLabel = root.Q<Label>("survivors");
            _boutLabel = root.Q<Label>("bout");
            _pickLabel = root.Q<Label>("pick");
            _captionLabel = root.Q<Label>("caption");
            _results = root.Q<VisualElement>("results");
            _resultLabel = root.Q<Label>("result");
            _resultRating = root.Q<Label>("result-rating");
            _resultPick = root.Q<Label>("result-pick");
            _promptLabel = root.Q<Label>("prompt");
            _resultPortrait = root.Q<VisualElement>("result-portrait");
            _resultStats = root.Q<Label>("result-stats");

            BindResultButtons(root);
            BuildHealthBars(root.Q<VisualElement>("roster"));
            RefreshNames();
            RefreshSurvivors();

            if (_flow != null)
            {
                _flow.Phase.Subscribe(OnFlowPhaseChanged).AddTo(_disposables);
                _flow.CountdownSeconds.Subscribe(OnCountdownChanged).AddTo(_disposables);
            }

            // Names follow the card, so re-dealing the roster rewrites the whole column.
            _roster.Revision.Subscribe(_ => RefreshNames()).AddTo(_disposables);
            _odds.Revision.Subscribe(_ => RefreshOdds()).AddTo(_disposables);
            _predictions.Revision.Subscribe(_ => RefreshStake()).AddTo(_disposables);
            _predictions.Pick.Subscribe(_ => RefreshStake()).AddTo(_disposables);
            _ratings.Revision.Subscribe(_ => RefreshResultBadges()).AddTo(_disposables);
            _director.PinnedId.Subscribe(OnPinnedChanged).AddTo(_disposables);
        }

        /// <summary>Marks the row of the fighter the camera has been told to follow.</summary>
        private void OnPinnedChanged(int pinnedId)
        {
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int index = 0; index < _rows.Count && index < boxers.Count; index++)
            {
                _rows[index].EnableInClassList("match-hud__row--pinned", boxers[index].Id == pinnedId);
            }
        }

        /// <summary>
        /// REMATCH runs the same card again straight away; NEW PICK goes back to the title
        /// screen to back someone else. Both are asked of the flow system, which refuses either
        /// outside the results phase - so a stale click cannot restart a live fight.
        /// </summary>
        private void BindResultButtons(VisualElement root)
        {
            Button rematch = root.Q<Button>("rematch");
            Button newPick = root.Q<Button>("new-pick");

            if (rematch != null)
            {
                rematch.clicked += () =>
                {
                    if (_flowSystem.TryRestart())
                    {
                        _flowSystem.TryStartFight();
                    }
                };
            }

            if (newPick != null)
            {
                newPick.clicked += () => _flowSystem.TryRestart();
            }
        }

        /// <summary>
        /// Clones one row per boxer slot and keeps the elements that get written later.
        ///
        /// CloneTree(target) adds the template's own children straight into the target, with no
        /// TemplateContainer in between. That matters here: an extra wrapper element would sit
        /// in the middle of the board's flex layout - and in the fight strip, where the rows
        /// are the flex items dividing the width, it would be the wrapper that got the share.
        /// </summary>
        private void BuildHealthBars(VisualElement roster)
        {
            if (roster == null || _healthRowTemplate == null)
            {
                GameLog.Error(
                    $"{nameof(MatchHudView)} is missing the health-row template or its " +
                    "container; per-fighter health will not be shown.", this);
                return;
            }

            IReadOnlyList<BoxerModel> boxers = _match.Boxers;
            int humanId = _spawnPoints != null ? _spawnPoints.HumanBoxerId : -1;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel boxer = boxers[boxerIndex];

                _healthRowTemplate.CloneTree(roster);
                VisualElement row = roster[roster.childCount - 1];

                row.EnableInClassList("match-hud__row--you", boxer.Id == humanId);

                _rows.Add(row);
                _nameLabels.Add(row.Q<Label>("name"));
                _healthFills.Add(row.Q<VisualElement>("fill"));
                _oddsLabels.Add(row.Q<Label>("odds"));
                _shownOdds.Add(SHOWN_NOTHING);

                // A tap on a fighter's cell tells the camera to follow them, and a second tap
                // lets go. The row is the one element of the board that takes a tap; its
                // children stay ignored so the whole cell is the target.
                int boxerId = boxer.Id;
                row.pickingMode = PickingMode.Position;
                row.RegisterCallback<ClickEvent>(_ => _directorSystem.TogglePin(boxerId));

                int index = boxerIndex;
                boxer.Health.Subscribe(hp => OnHealthChanged(index, hp)).AddTo(_disposables);
                boxer.IsAlive.Subscribe(alive => OnAliveChanged(index, alive)).AddTo(_disposables);
            }
        }

        /// <summary>
        /// Writes whoever is currently seated beside each bar, falling back to the slot number
        /// when no card is in play - which is what the training scenes and any scene without
        /// fighter profiles get.
        /// </summary>
        private void RefreshNames()
        {
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int index = 0; index < _nameLabels.Count && index < boxers.Count; index++)
            {
                string label = _roster.SeatLabel(boxers[index].Id);
                _nameLabels[index].text = label ?? $"#{boxers[index].Id:00}";
            }

            RefreshStake();
        }

        /// <summary>
        /// The seat's printed name - numbered when a short card seats a contestant twice - or the
        /// slot number when there is no card.
        /// </summary>
        private string NameOf(int boxerId)
        {
            string label = _roster.SeatLabel(boxerId);
            return label ?? $"BOXER #{boxerId:00}";
        }

        private void OnHealthChanged(int index, int health)
        {
            if (index >= _healthFills.Count)
            {
                return;
            }

            // Max health comes from the config, not from the first observed value, which would
            // rebase the bar after the boxer had already taken damage.
            float ratio = Mathf.Clamp01(health / (float)Mathf.Max(1, _config.MaxHealth));
            VisualElement fill = _healthFills[index];

            fill.style.width = Length.Percent(ratio * 100f);
            fill.EnableInClassList("bar__fill--healthy", ratio > 0.5f);
            fill.EnableInClassList("bar__fill--hurt", ratio <= 0.5f && ratio > 0.2f);
            fill.EnableInClassList("bar__fill--critical", ratio <= 0.2f);
        }

        private void OnAliveChanged(int index, bool isAlive)
        {
            if (index < _rows.Count)
            {
                _rows[index].EnableInClassList("match-hud__row--out", !isAlive);
            }

            RefreshSurvivors();
            RefreshOdds();
        }

        private void RefreshSurvivors()
        {
            if (_survivorsLabel == null)
            {
                return;
            }

            _builder.Clear();
            _builder.Append(_match.CountAlive()).Append(" / ").Append(_match.Boxers.Count).Append(" LEFT");
            _survivorsLabel.text = _builder.ToString();

            if (_boutLabel != null && _flow != null)
            {
                _builder.Clear();
                _builder.Append("BOUT ").Append(_flow.MatchNumber.Value);
                _boutLabel.text = _builder.ToString();
            }
        }

        /// <summary>
        /// Each row's live chance of being the last one standing, coloured by which way it has
        /// moved over the last couple of seconds. Four times a second while the book ticks, so
        /// only a row whose whole-point figure actually changed is rewritten.
        /// </summary>
        private void RefreshOdds()
        {
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int index = 0; index < _oddsLabels.Count && index < boxers.Count; index++)
            {
                Label label = _oddsLabels[index];

                if (label == null)
                {
                    continue;
                }

                BoxerModel boxer = boxers[index];
                int shown = ShownOddsFor(index, boxer);

                if (shown != _shownOdds[index])
                {
                    _shownOdds[index] = shown;
                    label.text = FormatShownOdds(shown);
                }

                OddsEntry entry = _odds.EntryFor(_roster.SeatOf(boxer.Id));
                float trendPoints = entry != null && boxer.IsAlive.Value ? entry.Trend * 100f : 0f;

                label.EnableInClassList("match-hud__odds--up", trendPoints >= _trendThreshold);
                label.EnableInClassList("match-hud__odds--down", trendPoints <= -_trendThreshold);
            }

            if (_predictions.Outcome == PredictionOutcome.Pending)
            {
                RefreshStake();
            }
        }

        /// <summary>
        /// The figure a row should show: whole percentage points, or a marker for "out" and for
        /// "still standing on under one percent". A fighter still up is never shown at 0% -
        /// a zero beside a name says certain loser, and nobody standing is that.
        /// </summary>
        private int ShownOddsFor(int seatIndex, BoxerModel boxer)
        {
            if (!boxer.IsAlive.Value)
            {
                return SHOWN_OUT;
            }

            if (seatIndex >= _odds.SeatCount)
            {
                return SHOWN_NOTHING;
            }

            float percent = _odds.SeatOdds(seatIndex) * 100f;
            return percent < 1f ? SHOWN_UNDER_ONE : Mathf.RoundToInt(percent);
        }

        private string FormatShownOdds(int shown)
        {
            switch (shown)
            {
                case SHOWN_NOTHING:
                    return string.Empty;

                case SHOWN_OUT:
                    return "OUT";

                case SHOWN_UNDER_ONE:
                    return "<1%";
            }

            _scratch.Clear();
            _scratch.Append(shown).Append('%');
            return _scratch.ToString();
        }

        /// <summary>
        /// Marks the backed fighter's rows and writes the stake line under the board. Every seat
        /// a contestant fills is marked: the card deals the ring cyclically, so a short card
        /// seats the same fighter more than once and the stake rides on all of them.
        /// </summary>
        private void RefreshStake()
        {
            FighterProfile stake = CurrentStake();
            IReadOnlyList<BoxerModel> boxers = _match.Boxers;

            for (int index = 0; index < _rows.Count && index < boxers.Count; index++)
            {
                bool backed = stake != null && _roster.SeatOf(boxers[index].Id) == stake;
                _rows[index].EnableInClassList("match-hud__row--pick", backed);
            }

            if (_pickLabel == null)
            {
                return;
            }

            _builder.Clear();

            // In words, not in odds notation: "PAYS 4.4x" asked the viewer to multiply, and
            // "NOW 14%" did not say fourteen percent of what.
            if (_predictions.Outcome == PredictionOutcome.Pending && _predictions.StakedOn != null)
            {
                _builder.Append("YOUR ").Append(_predictions.Stake).Append(" ON ")
                        .Append(_predictions.StakedOn.DisplayName)
                        .Append("   WINS ").Append(PotentialPayout())
                        .Append("   CHANCE ").Append(FormatPercent(OddsOfStake()));
            }
            else
            {
                _builder.Append("NO BET   BANK ").Append(_predictions.Bank.Value);
            }

            _pickLabel.text = _builder.ToString();

            RefreshResultBadges();
        }

        /// <summary>The contestant a live stake rides on, for highlighting their rows. Null with nothing riding.</summary>
        private FighterProfile CurrentStake()
        {
            return _predictions.Outcome == PredictionOutcome.Pending ? _predictions.StakedOn : null;
        }

        private float OddsOfStake()
        {
            OddsEntry entry = _odds.EntryFor(_predictions.StakedOn);
            return entry != null ? entry.Odds : 0f;
        }

        private string FormatPercent(float odds)
        {
            _scratch.Clear();
            float percent = odds * 100f;

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

        /// <summary>What the stake returns if it wins, stake included - the same rounding PredictionSystem settles with.</summary>
        private int PotentialPayout()
        {
            return Mathf.RoundToInt(_predictions.Stake * _predictions.LockedMultiplier);
        }

        private void OnCountdownChanged(int seconds)
        {
            if (_captionLabel == null || _flow.Phase.Value != MatchFlowPhase.Countdown)
            {
                return;
            }

            _captionLabel.text = seconds > 0 ? seconds.ToString() : string.Empty;
        }

        private void OnFlowPhaseChanged(MatchFlowPhase phase)
        {
            if (_captionLabel == null)
            {
                return;
            }

            bool live = phase == MatchFlowPhase.Fighting || phase == MatchFlowPhase.KnockoutHold;

            // The survivor count and the rows describe a fight. On the title screen there is
            // none, and the board sat there claiming ten alive at full health before a single
            // punch - beside a menu that owns the phase outright.
            if (_panel != null)
            {
                _panel.EnableInClassList("match-hud--hidden", phase == MatchFlowPhase.Title);
                _panel.EnableInClassList("match-hud--compact", live);
            }

            // The stake line is the results card's to report once the fight is decided. A class
            // rather than an inline display, which would outrank the fight strip's own rule
            // hiding the line and put it back under the ten cells.
            if (_pickLabel != null)
            {
                _pickLabel.EnableInClassList("match-hud__pick--gone", phase == MatchFlowPhase.Results);
            }

            SetResultsVisible(phase == MatchFlowPhase.Results);

            switch (phase)
            {
                case MatchFlowPhase.Title:
                    // Deliberately blank. MainMenuView owns this phase - it draws the title, the
                    // card and the button that starts the fight.
                    _captionLabel.text = string.Empty;
                    break;

                case MatchFlowPhase.Introducing:
                    _captionLabel.text = "GET READY";
                    _winnerId = MatchModel.NO_WINNER;
                    RefreshSurvivors();
                    RefreshOdds();
                    break;

                case MatchFlowPhase.Countdown:
                    _captionLabel.text = _flow.CountdownSeconds.Value.ToString();
                    break;

                case MatchFlowPhase.Fighting:
                    _captionLabel.text = "FIGHT!";
                    ClearCaptionAfterBell();
                    break;

                case MatchFlowPhase.KnockoutHold:
                    _captionLabel.text = string.Empty;
                    break;

                case MatchFlowPhase.Results:
                    _captionLabel.text = string.Empty;
                    RefreshResultBadges();
                    break;
            }
        }

        private void SetResultsVisible(bool visible)
        {
            if (_results == null)
            {
                return;
            }

            _results.EnableInClassList("results--hidden", !visible);

            if (visible && _promptLabel != null)
            {
                _promptLabel.text = KeyboardHint();
            }
        }

        /// <summary>
        /// The keys, where there is a keyboard to press them. A phone gets nothing here - the
        /// two buttons are the prompt - and Android reports a Keyboard device on every phone,
        /// which is why this asks <see cref="InputPresence"/> rather than the device list.
        /// </summary>
        private static string KeyboardHint()
        {
            return InputPresence.HasUsableKeyboard() ? "ENTER  REMATCH      R  MENU" : string.Empty;
        }

        /// <summary>
        /// Wipes "FIGHT!" a beat after the bell. Scheduled on the panel rather than timed in
        /// Update so it costs nothing on the frames in between.
        /// </summary>
        private void ClearCaptionAfterBell()
        {
            _captionLabel.schedule.Execute(() =>
            {
                if (_flow.Phase.Value == MatchFlowPhase.Fighting)
                {
                    _captionLabel.text = string.Empty;
                }
            }).StartingIn(700);
        }

        private void OnMatchEnded(MatchEndedMessage message)
        {
            _winnerId = message.WinnerId;

            if (_resultLabel != null)
            {
                _resultLabel.text = message.WinnerId == MatchModel.NO_WINNER
                    ? "DRAW"
                    : $"{NameOf(message.WinnerId)} WINS";
            }

            RefreshResultBadges();
        }

        /// <summary>
        /// The two facts under the winner's name: their rating after this bout, and what the
        /// viewer's stake came to. Both models settle on the same MatchEndedMessage this view
        /// hears, in no guaranteed order, so this is rewritten whenever either revises.
        /// </summary>
        private void RefreshResultBadges()
        {
            if (_resultRating == null || _resultPick == null || _flow == null)
            {
                return;
            }

            if (_flow.Phase.Value != MatchFlowPhase.Results && _winnerId == MatchModel.NO_WINNER)
            {
                return;
            }

            WriteRatingBadge();
            WritePickBadge();
            WriteWinnerCard();
        }

        /// <summary>
        /// The winner's face and what they did to get there. A draw has neither: the card is the
        /// word and the two badges, and a blank portrait would read as a fighter with no face.
        /// </summary>
        private void WriteWinnerCard()
        {
            FighterProfile winner = _winnerId == MatchModel.NO_WINNER ? null : _roster.SeatOf(_winnerId);

            if (_resultPortrait != null)
            {
                _resultPortrait.EnableInClassList("results__portrait--gone", winner == null);

                if (winner != null && winner.Face != null)
                {
                    _resultPortrait.style.backgroundImage = new StyleBackground(winner.Face);
                    _resultPortrait.style.backgroundColor = StyleKeyword.Null;
                }
                else if (winner != null)
                {
                    _resultPortrait.style.backgroundImage = StyleKeyword.None;
                    _resultPortrait.style.backgroundColor = winner.Tint;
                }
            }

            if (_resultStats == null)
            {
                return;
            }

            FighterStats stats = _winnerId == MatchModel.NO_WINNER ? null : _stats.For(IndexOf(_winnerId));
            _resultStats.EnableInClassList("results__stats--gone", stats == null);

            if (stats == null)
            {
                return;
            }

            _builder.Clear();
            _builder.Append(stats.Knockouts).Append(stats.Knockouts == 1 ? " KNOCKOUT   " : " KNOCKOUTS   ")
                    .Append(stats.DamageDealt).Append(" DAMAGE   ")
                    .Append(Mathf.RoundToInt(stats.Accuracy * 100f)).Append("% LANDED");
            _resultStats.text = _builder.ToString();
        }

        private int IndexOf(int boxerId)
        {
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

        private void WriteRatingBadge()
        {
            RatingRecord record = WinnerRecord();

            _resultRating.EnableInClassList("badge--empty", record == null);

            if (record == null)
            {
                return;
            }

            _builder.Clear();
            _builder.Append(Mathf.RoundToInt(record.Rating)).Append(" ELO");

            int delta = Mathf.RoundToInt(record.LastDelta);

            if (delta != 0)
            {
                _builder.Append(delta > 0 ? "  +" : "  ").Append(delta);
            }

            _resultRating.text = _builder.ToString();
            _resultRating.EnableInClassList("badge--good", delta > 0);
            _resultRating.EnableInClassList("badge--bad", delta < 0);
        }

        private void WritePickBadge()
        {
            _builder.Clear();

            // A sentence about the viewer's money rather than "+1150": what they put on whom,
            // and what came back.
            string backed = _predictions.StakedOn != null ? _predictions.StakedOn.DisplayName : string.Empty;

            switch (_predictions.Outcome)
            {
                case PredictionOutcome.Won:
                    _builder.Append("YOUR ").Append(_predictions.Stake).Append(" ON ").Append(backed)
                            .Append(" WON ").Append(_predictions.LastPayout);
                    break;

                case PredictionOutcome.Lost:
                    _builder.Append("YOUR ").Append(_predictions.Stake).Append(" ON ").Append(backed)
                            .Append(" LOST");
                    break;

                case PredictionOutcome.Refunded:
                    _builder.Append("NO RESULT   YOUR ").Append(_predictions.Stake).Append(" IS BACK");
                    break;

                default:
                    _builder.Append("NO BET");
                    break;
            }

            _builder.Append("   BANK ").Append(_predictions.Bank.Value);

            _resultPick.text = _builder.ToString();
            _resultPick.EnableInClassList("badge--good", _predictions.Outcome == PredictionOutcome.Won);
            _resultPick.EnableInClassList("badge--bad", _predictions.Outcome == PredictionOutcome.Lost);
            _resultPick.EnableInClassList("badge--muted", _predictions.Outcome == PredictionOutcome.None);
        }

        /// <summary>
        /// The winner's standing, looked up rather than created: <see cref="RatingModel.GetOrCreate"/>
        /// would add a record as a side effect of a view reading it.
        /// </summary>
        private RatingRecord WinnerRecord()
        {
            if (_winnerId == MatchModel.NO_WINNER)
            {
                return null;
            }

            FighterProfile profile = _roster.SeatOf(_winnerId);

            if (profile == null)
            {
                return null;
            }

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

        private void OnDestroy() => _disposables.Dispose();
    }
}
