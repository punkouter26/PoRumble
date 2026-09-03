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
    /// The card: who is fighting tonight, and what each of them is.
    ///
    /// One tile per contestant, showing the face that will be on the head in the ring, the
    /// style in a line, and the standing so far. Clicking a tile adds or drops that fighter.
    ///
    /// A View throughout. Whether a change is allowed is <see cref="RosterSystem"/>'s
    /// decision; how the ring is re-dealt is <see cref="BoxerSpawnPoints"/>'s. This draws
    /// tiles and forwards clicks.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class RosterSelectionView : MonoBehaviour
    {
        [Tooltip("The card's structure. Without it the fight card renders nothing.")]
        [SerializeField] private VisualTreeAsset _layout;

        [Tooltip("One contestant's tile, cloned once per selectable fighter.")]
        [SerializeField] private VisualTreeAsset _tileTemplate;

        [Tooltip("The shared HUD stylesheet. Without it the panel renders unstyled.")]
        [SerializeField] private StyleSheet _styleSheet;

        private readonly CompositeDisposable _disposables = new();
        private readonly StringBuilder _builder = new(64);
        private readonly List<VisualElement> _tiles = new();

        /// <summary>
        /// The standing line on each tile, held by index rather than looked up as a child.
        /// Indexing into a tile's children ties the refresh to the order the tile happens to
        /// be built in, which is exactly the kind of thing a later layout tweak breaks
        /// silently.
        /// </summary>
        private readonly List<Label> _standingLabels = new();

        private RosterModel _roster;
        private RatingModel _ratings;
        private RosterSystem _rosterSystem;
        private BoxerSpawnPoints _spawnPoints;
        private MatchFlowModel _flow;

        private VisualElement _panel;
        private Label _footerLabel;
        private Button _openButton;

        [Inject]
        public void Construct(
            RosterModel roster,
            RatingModel ratings,
            RosterSystem rosterSystem,
            BoxerSpawnPoints spawnPoints,
            MatchFlowModel flow)
        {
            _roster = roster;
            _ratings = ratings;
            _rosterSystem = rosterSystem;
            _spawnPoints = spawnPoints;
            _flow = flow;
        }

        private void Start()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            if (root == null || _roster == null)
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

            if (_layout == null)
            {
                Debug.LogError(
                    $"{nameof(RosterSelectionView)} has no layout assigned; the fight card " +
                    "will not render. Assign Assets/UI/Layouts/RosterCard.uxml.", this);
                return;
            }

            _layout.CloneTree(root);

            _panel = root.Q<VisualElement>("panel");
            _footerLabel = root.Q<Label>("footer");
            _openButton = root.Q<Button>("open-card");

            if (_panel == null)
            {
                return;
            }

            // Closed until something opens it. Kept in C# rather than in the UXML because it
            // is state rather than structure, and OnOpenChanged writes the same property.
            _panel.style.display = DisplayStyle.None;

            BuildTiles(root.Q<VisualElement>("grid"));

            if (_openButton != null)
            {
                _openButton.clicked += () => _rosterSystem.Toggle();
            }

            _roster.IsOpen.Subscribe(OnOpenChanged).AddTo(_disposables);
            _roster.Revision.Subscribe(_ => RefreshTiles()).AddTo(_disposables);
            _ratings.Revision.Subscribe(_ => RefreshTiles()).AddTo(_disposables);
            _flow.Phase.Subscribe(_ => RefreshOpenButton()).AddTo(_disposables);
        }

        /// <summary>
        /// Shows the way into the card only when the card can actually be used: between
        /// matches, and never while it is already open on top of the button.
        /// </summary>
        private void RefreshOpenButton()
        {
            if (_openButton == null)
            {
                return;
            }

            bool available = _flow.CanOpenCard && !_roster.IsOpen.Value;
            _openButton.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Clones one tile per selectable contestant and fills in what comes from data.
        ///
        /// The face, the name, the tagline and the trunk colour all live on a FighterProfile
        /// asset, so none of them can be authored in the template - the layout guarantees the
        /// shape of a tile and this fills it with a particular fighter.
        /// </summary>
        private void BuildTiles(VisualElement grid)
        {
            if (grid == null || _tileTemplate == null)
            {
                Debug.LogError(
                    $"{nameof(RosterSelectionView)} is missing the tile template or its grid; " +
                    "the fight card will be empty.", this);
                return;
            }

            IReadOnlyList<FighterProfile> available = _roster.Available;

            for (int index = 0; index < available.Count; index++)
            {
                FighterProfile profile = available[index];

                _tileTemplate.CloneTree(grid);
                VisualElement tile = grid[grid.childCount - 1];

                VisualElement portrait = tile.Q<VisualElement>("portrait");

                // A fighter with no face keeps the tile's plain plate rather than an empty
                // hole, so the two generic entries still read as contestants.
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

                Label tagline = tile.Q<Label>("tagline");

                if (tagline != null)
                {
                    tagline.text = profile.Tagline;
                }

                _standingLabels.Add(tile.Q<Label>("standing"));

                // Captured per tile so the handler knows which contestant it belongs to
                // without hit-testing anything.
                FighterProfile captured = profile;
                tile.RegisterCallback<ClickEvent>(_ => OnTileClicked(captured));

                _tiles.Add(tile);
            }
        }

        private void OnTileClicked(FighterProfile profile)
        {
            if (!_rosterSystem.ToggleEntrant(profile))
            {
                // Refused: the card is already down to the two fighters a match needs.
                RefreshFooter();
                return;
            }

            RefreshTiles();
        }

        private void OnOpenChanged(bool isOpen)
        {
            RefreshOpenButton();

            if (_panel == null)
            {
                return;
            }

            _panel.style.display = isOpen ? DisplayStyle.Flex : DisplayStyle.None;

            if (isOpen)
            {
                RefreshTiles();
                return;
            }

            // Closing is what commits the card. Re-dealing seats every boxer again, which
            // swaps faces, controllers and attributes on objects that already exist - no
            // agent is destroyed and nothing is respawned.
            _spawnPoints.SeatRoster();
        }

        private void RefreshTiles()
        {
            IReadOnlyList<FighterProfile> available = _roster.Available;

            for (int index = 0; index < _tiles.Count && index < available.Count; index++)
            {
                FighterProfile profile = available[index];
                VisualElement tile = _tiles[index];

                tile.EnableInClassList("roster-tile--in", _roster.IsEntrant(profile));
                _standingLabels[index].text = DescribeStanding(profile);
            }

            RefreshFooter();
        }

        private string DescribeStanding(FighterProfile profile)
        {
            RatingRecord record = _ratings.GetOrCreate(profile.Id, profile.DisplayName);

            _builder.Clear();
            _builder.Append(Mathf.RoundToInt(record.Rating)).Append("  ELO");

            if (record.Matches > 0)
            {
                _builder.Append("   ").Append(record.Wins).Append('W').Append(" / ").Append(record.Matches);
            }

            return _builder.ToString();
        }

        private void RefreshFooter()
        {
            if (_footerLabel == null)
            {
                return;
            }

            _builder.Clear();
            _builder.Append(_roster.Entrants.Count)
                    .Append(" SELECTED — DEALT ROUND ")
                    .Append(_spawnPoints.BoxerCount)
                    .Append(" CORNERS.   TAB TO CLOSE");

            _footerLabel.text = _builder.ToString();
        }

        private void OnDestroy() => _disposables.Dispose();
    }
}
