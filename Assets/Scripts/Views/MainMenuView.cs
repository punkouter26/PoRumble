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
    /// The title screen: who is fighting, who you are backing, and the bell - on one page.
    ///
    /// This used to be three documents with three views. The menu had a FIGHT and a FIGHT CARD
    /// button; the card was a full-screen modal of its own with 300px tiles three to a row, which
    /// ran off the bottom of a phone once the card was full; and a pick strip along the bottom
    /// offered prices on the same contestants as a row of chips. Now there is one grid of compact
    /// tiles and a PICK / CARD switch deciding what a tap on a tile does.
    ///
    /// The switch is <see cref="RosterModel.IsOpen"/>, so Tab still flips it and
    /// <see cref="RosterSystem"/> still decides when it may. Leaving CARD is what commits the
    /// card to the ring, exactly as closing the old modal did: re-dealing seats every boxer
    /// again, which swaps faces, controllers and attributes on objects that already exist.
    ///
    /// A View throughout. Whether a pick is legal and what it pays is
    /// <see cref="PredictionSystem"/>'s business; whether the card may change is
    /// <see cref="RosterSystem"/>'s; whether a fight may start is <see cref="MatchFlowSystem"/>'s.
    /// This draws tiles and forwards taps.
    ///
    /// Optional, like every other presentation component: the training scenes have no menu,
    /// and <see cref="GameLifetimeScope"/> injects this only if it is in the scene.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuView : MonoBehaviour
    {
        [Tooltip("The screen's structure. Without it the menu renders nothing at all.")]
        [SerializeField] private VisualTreeAsset _layout;

        [Tooltip("One contestant's tile, cloned once per fighter on the full card.")]
        [SerializeField] private VisualTreeAsset _tileTemplate;

        [Tooltip("The shared HUD stylesheet. Without it the menu renders unstyled.")]
        [SerializeField] private StyleSheet _styleSheet;

        private readonly CompositeDisposable _disposables = new();
        private readonly StringBuilder _builder = new(96);
        private readonly List<VisualElement> _tiles = new();
        private readonly List<Label> _prices = new();
        private readonly List<Label> _standings = new();
        private readonly List<Label> _seats = new();

        /// <summary>The price each tile last showed, in tenths, so an unchanged price is not rewritten.</summary>
        private readonly List<int> _shownPrices = new();

        private MatchFlowModel _flow;
        private MatchFlowSystem _flowSystem;
        private RosterModel _roster;
        private RosterSystem _rosterSystem;
        private RatingModel _ratings;
        private PredictionModel _predictions;
        private PredictionSystem _predictionSystem;
        private WinOddsModel _odds;
        private BoxerSpawnPoints _spawnPoints;

        private VisualElement _screen;
        private Button _modePick;
        private Button _modeCard;
        private Label _bank;
        private Label _hint;

        private bool _visible;

        /// <summary>
        /// Whether the card was being edited, so the commit fires on the edge out of CARD
        /// rather than on every false the subscription pushes - including the one it pushes the
        /// moment it is made.
        /// </summary>
        private bool _wasEditing;

        [Inject]
        public void Construct(
            MatchFlowModel flow,
            MatchFlowSystem flowSystem,
            RosterModel roster,
            RosterSystem rosterSystem,
            RatingModel ratings,
            PredictionModel predictions,
            PredictionSystem predictionSystem,
            WinOddsModel odds,
            BoxerSpawnPoints spawnPoints)
        {
            _flow = flow;
            _flowSystem = flowSystem;
            _roster = roster;
            _rosterSystem = rosterSystem;
            _ratings = ratings;
            _predictions = predictions;
            _predictionSystem = predictionSystem;
            _odds = odds;
            _spawnPoints = spawnPoints;
        }

        private void Start()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            if (root == null || _flow == null)
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
                    $"{nameof(MainMenuView)} has no layout assigned; the title screen will " +
                    "not render. Assign Assets/UI/Layouts/MainMenu.uxml.", this);
                return;
            }

            _layout.CloneTree(root);

            // The document root is an element UXML never declared, and it spans the screen
            // whatever the scrim does. Ignored so it never eats clicks aimed at the HUD below;
            // the scrim sets its own picking in Refresh.
            root.pickingMode = PickingMode.Ignore;

            _screen = root.Q<VisualElement>("screen");
            _modePick = root.Q<Button>("mode-pick");
            _modeCard = root.Q<Button>("mode-card");
            _bank = root.Q<Label>("bank");
            _hint = root.Q<Label>("hint");

            Button fight = root.Q<Button>("fight");

            if (fight != null)
            {
                fight.clicked += OnFightClicked;
            }

            if (_modePick != null)
            {
                _modePick.clicked += () => SetEditing(false);
            }

            if (_modeCard != null)
            {
                _modeCard.clicked += () => SetEditing(true);
            }

            BuildTiles(root.Q<VisualElement>("grid"));

            _flow.Phase.Subscribe(OnPhaseChanged).AddTo(_disposables);
            _roster.IsOpen.Subscribe(OnEditingChanged).AddTo(_disposables);
            _roster.Revision.Subscribe(_ => RefreshTiles()).AddTo(_disposables);
            _ratings.Revision.Subscribe(_ => RefreshStandings()).AddTo(_disposables);
            _predictions.Pick.Subscribe(_ => RefreshTiles()).AddTo(_disposables);
            _predictions.Bank.Subscribe(_ => RefreshBank()).AddTo(_disposables);
            _odds.Revision.Subscribe(_ => RefreshPrices()).AddTo(_disposables);
        }

        /// <summary>
        /// Commits a card that is mid-edit before asking for the bell, so the fight that starts
        /// is the card on screen rather than the one dealt before the edit began.
        /// </summary>
        private void OnFightClicked()
        {
            if (_roster.IsOpen.Value)
            {
                _rosterSystem.Close();
            }

            _flowSystem.TryStartFight();
        }

        private void SetEditing(bool editing)
        {
            if (_roster.IsOpen.Value != editing)
            {
                _rosterSystem.Toggle();
            }
        }

        /// <summary>
        /// Clones one tile per selectable contestant and fills in what comes from data. Built
        /// once for the whole card: entering or dropping a fighter, or flipping the mode, only
        /// changes classes, so nothing rebuilds under a tap.
        /// </summary>
        private void BuildTiles(VisualElement grid)
        {
            if (grid == null || _tileTemplate == null)
            {
                Debug.LogError(
                    $"{nameof(MainMenuView)} is missing the tile template or its grid; the " +
                    "title screen will have no contestants. Assign Templates/RosterTile.uxml.", this);
                return;
            }

            IReadOnlyList<FighterProfile> available = _roster.Available;

            for (int index = 0; index < available.Count; index++)
            {
                FighterProfile profile = available[index];

                _tileTemplate.CloneTree(grid);
                VisualElement tile = grid[grid.childCount - 1];

                VisualElement portrait = tile.Q<VisualElement>("portrait");

                // A fighter with no face keeps a plain plate in their trunk colour rather than an
                // empty hole, so the generic entries still read as contestants.
                if (portrait != null)
                {
                    if (profile.Face != null)
                    {
                        portrait.style.backgroundImage = new StyleBackground(profile.Face);
                    }
                    else
                    {
                        portrait.style.backgroundColor = profile.Tint;
                    }
                }

                Label name = tile.Q<Label>("name");

                if (name != null)
                {
                    name.text = profile.DisplayName;
                }

                // The training generation is the whole point of an evolution exhibition - two
                // tiles that look alike are a fighter from early in the run and one from the end.
                // The style tagline that used to sit here as well did not survive the smaller tile.
                Label generation = tile.Q<Label>("generation");

                if (generation != null)
                {
                    string label = profile.GenerationLabel;
                    generation.text = label;
                    generation.style.display = label.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                }

                _prices.Add(tile.Q<Label>("price"));
                _standings.Add(tile.Q<Label>("standing"));
                _seats.Add(tile.Q<Label>("seats"));
                _shownPrices.Add(-1);

                // Captured per tile so the handler knows which contestant it belongs to without
                // hit-testing anything.
                FighterProfile captured = profile;
                tile.RegisterCallback<ClickEvent>(_ => OnTileClicked(captured));

                _tiles.Add(tile);
            }
        }

        /// <summary>
        /// PICK backs the fighter; CARD puts them on or off the card. A refusal in CARD mode is
        /// the card already being down to the two fighters a match needs, which the hint says.
        /// </summary>
        private void OnTileClicked(FighterProfile profile)
        {
            if (_roster.IsOpen.Value)
            {
                _rosterSystem.ToggleEntrant(profile);
                RefreshTiles();
                return;
            }

            _predictionSystem.TogglePick(profile);
        }

        /// <summary>
        /// Shows the screen on the title phase and nowhere else, and commits a card left
        /// mid-edit when the phase moves on - Enter on a keyboard can start the fight without
        /// passing through the FIGHT button.
        /// </summary>
        private void OnPhaseChanged(MatchFlowPhase phase)
        {
            bool title = phase == MatchFlowPhase.Title;

            if (!title && _roster.IsOpen.Value)
            {
                _rosterSystem.Close();
            }

            _visible = title;

            if (_screen == null)
            {
                return;
            }

            _screen.style.display = title ? DisplayStyle.Flex : DisplayStyle.None;

            // display:none already stops hit-testing, but a full-screen scrim left pickable
            // would swallow every tap meant for the panels underneath it for the whole match.
            _screen.pickingMode = title ? PickingMode.Position : PickingMode.Ignore;

            if (title)
            {
                RefreshTiles();
                RefreshBank();
            }
        }

        private void OnEditingChanged(bool editing)
        {
            if (_wasEditing && !editing)
            {
                // Leaving CARD is what commits the card. No agent is destroyed and nothing is
                // respawned: the ring's existing seats are reconfigured.
                _spawnPoints.SeatRoster();
            }

            _wasEditing = editing;

            if (_modePick != null)
            {
                _modePick.EnableInClassList("menu__mode--on", !editing);
            }

            if (_modeCard != null)
            {
                _modeCard.EnableInClassList("menu__mode--on", editing);
            }

            RefreshTiles();
        }

        private void RefreshTiles()
        {
            IReadOnlyList<FighterProfile> available = _roster.Available;
            bool editing = _roster.IsOpen.Value;
            FighterProfile pick = _predictions.Pick.Value;

            for (int index = 0; index < _tiles.Count && index < available.Count; index++)
            {
                FighterProfile profile = available[index];
                VisualElement tile = _tiles[index];
                bool entrant = _roster.IsEntrant(profile);

                tile.EnableInClassList("roster-tile--in", editing && entrant);
                tile.EnableInClassList("roster-tile--off", editing && !entrant);
                tile.EnableInClassList("roster-tile--hidden", !editing && !entrant);
                tile.EnableInClassList("roster-tile--pick", !editing && profile == pick);

                // A short card deals some contestants two corners. Said on the tile, before the
                // bell, rather than discovered as two rows with the same name on the board.
                Label seats = index < _seats.Count ? _seats[index] : null;

                if (seats != null)
                {
                    int corners = _roster.SeatsFor(profile, _spawnPoints.BoxerCount);
                    seats.EnableInClassList("roster-tile__seats--gone", corners < 2);

                    if (corners >= 2)
                    {
                        _builder.Clear();
                        _builder.Append('x').Append(corners);
                        seats.text = _builder.ToString();
                    }
                }
            }

            RefreshStandings();
            RefreshPrices();
            RefreshHint();
        }

        private void RefreshStandings()
        {
            IReadOnlyList<FighterProfile> available = _roster.Available;

            for (int index = 0; index < _standings.Count && index < available.Count; index++)
            {
                Label standing = _standings[index];

                if (standing == null)
                {
                    continue;
                }

                // Read rather than created: GetOrCreate would add a record for every tile the
                // first time the screen drew, as a side effect of a view looking at it.
                float rating = _ratings.RatingOf(available[index].Id);

                _builder.Clear();
                _builder.Append(Mathf.RoundToInt(rating));
                standing.text = _builder.ToString();
            }
        }

        /// <summary>
        /// Rewrites each price from the live book. Four times a second while the odds tick, so
        /// only a tile whose price actually moved is touched, and nothing while the screen is
        /// not up.
        /// </summary>
        private void RefreshPrices()
        {
            if (!_visible)
            {
                return;
            }

            IReadOnlyList<FighterProfile> available = _roster.Available;

            for (int index = 0; index < _prices.Count && index < available.Count; index++)
            {
                Label price = _prices[index];

                if (price == null)
                {
                    continue;
                }

                // What a bet returns, not the odds it is priced at: "WINS 440" needs no
                // arithmetic, "4.4x" needed the viewer to know the stake and multiply.
                int payout = PayoutFor(available[index]);

                if (payout == _shownPrices[index])
                {
                    continue;
                }

                _shownPrices[index] = payout;
                _builder.Clear();
                _builder.Append("WINS ").Append(payout);
                price.text = _builder.ToString();

                if (available[index] == _predictions.Pick.Value)
                {
                    RefreshHint();
                }
            }
        }

        /// <summary>What a stake on this contestant returns if they win, stake included.</summary>
        private int PayoutFor(FighterProfile profile)
        {
            return Mathf.RoundToInt(PredictionSystem.STAKE * _predictionSystem.MultiplierFor(profile));
        }

        private void RefreshBank()
        {
            if (_bank == null)
            {
                return;
            }

            _builder.Clear();
            _builder.Append("BANK ").Append(_predictions.Bank.Value);

            if (_predictions.Placed > 0)
            {
                _builder.Append("   RIGHT ").Append(_predictions.Correct).Append(" OF ")
                        .Append(_predictions.Placed);
            }

            _bank.text = _builder.ToString();
        }

        /// <summary>
        /// The line above FIGHT: what a tap on a tile does right now, how many are on the card,
        /// and the keys where there is a keyboard to press them.
        ///
        /// The count matters because the card seats the ring cyclically: three contestants in a
        /// ten-boxer ring is legal and means several of them fight more than once, which is
        /// worth knowing before the bell rather than discovering from the field board.
        /// </summary>
        private void RefreshHint()
        {
            if (_hint == null)
            {
                return;
            }

            _builder.Clear();

            int entrants = _roster.Entrants.Count;

            FighterProfile pick = _predictions.Pick.Value;

            if (_roster.IsOpen.Value)
            {
                _builder.Append("TAP TO ADD OR DROP   ").Append(entrants).Append(" ON THE CARD, ")
                        .Append(_spawnPoints.BoxerCount).Append(" CORNERS");
            }
            else if (pick != null)
            {
                _builder.Append("YOUR ").Append(PredictionSystem.STAKE).Append(" IS ON ")
                        .Append(pick.DisplayName).Append("   WINS ").Append(PayoutFor(pick))
                        .Append(" IF THEY WIN");
            }
            else if (entrants > 0)
            {
                _builder.Append("TAP A FIGHTER TO BET ").Append(PredictionSystem.STAKE).Append(" ON THEM");
            }
            else
            {
                _builder.Append("NO CARD - THE HOUSE FIGHTERS TAKE THE RING");
            }

            // Asked through InputPresence rather than of Keyboard.current directly, because
            // Android answers that question yes on every phone - the hardware buttons arrive as
            // key events and the Input System builds a Keyboard to carry them.
            if (InputPresence.HasUsableKeyboard())
            {
                _builder.Append("\nENTER TO FIGHT      TAB SWITCHES PICK / CARD");
            }

            _hint.text = _builder.ToString();
        }

        private void OnDestroy() => _disposables.Dispose();
    }
}
