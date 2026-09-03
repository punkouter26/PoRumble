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

        private VisualElement _panel;
        private Label _footerLabel;

        [Inject]
        public void Construct(
            RosterModel roster,
            RatingModel ratings,
            RosterSystem rosterSystem,
            BoxerSpawnPoints spawnPoints)
        {
            _roster = roster;
            _ratings = ratings;
            _rosterSystem = rosterSystem;
            _spawnPoints = spawnPoints;
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

            _panel = new VisualElement();
            _panel.AddToClassList("roster");
            _panel.style.display = DisplayStyle.None;
            root.Add(_panel);

            Label title = new("SELECT THE CARD");
            title.AddToClassList("text");
            title.AddToClassList("text--lg");
            title.AddToClassList("text--bold");
            title.AddToClassList("roster__title");
            _panel.Add(title);

            VisualElement grid = new();
            grid.AddToClassList("roster__grid");
            _panel.Add(grid);

            BuildTiles(grid);

            _footerLabel = new Label(string.Empty);
            _footerLabel.AddToClassList("text");
            _footerLabel.AddToClassList("text--sm");
            _footerLabel.AddToClassList("roster__footer");
            _panel.Add(_footerLabel);

            _roster.IsOpen.Subscribe(OnOpenChanged).AddTo(_disposables);
            _roster.Revision.Subscribe(_ => RefreshTiles()).AddTo(_disposables);
            _ratings.Revision.Subscribe(_ => RefreshTiles()).AddTo(_disposables);
        }

        private void BuildTiles(VisualElement grid)
        {
            IReadOnlyList<FighterProfile> available = _roster.Available;

            for (int index = 0; index < available.Count; index++)
            {
                FighterProfile profile = available[index];

                VisualElement tile = new();
                tile.AddToClassList("roster-tile");

                VisualElement portrait = new();
                portrait.AddToClassList("roster-tile__portrait");

                // A fighter with no face keeps the tile's plain plate rather than an empty
                // hole, so the two generic entries still read as contestants.
                if (profile.Face != null)
                {
                    portrait.style.backgroundImage = new StyleBackground(profile.Face);
                }
                else
                {
                    portrait.style.backgroundColor = profile.Tint;
                }

                tile.Add(portrait);

                Label name = MakeLabel("text", "text--md", "text--bold", "roster-tile__name");
                name.text = profile.DisplayName;
                tile.Add(name);

                Label tagline = MakeLabel("text", "text--xs", "text--muted", "roster-tile__tagline");
                tagline.text = profile.Tagline;
                tile.Add(tagline);

                Label standing = MakeLabel("text", "text--xs", "roster-tile__standing");
                tile.Add(standing);
                _standingLabels.Add(standing);

                // Captured per tile so the handler knows which contestant it belongs to
                // without hit-testing anything.
                FighterProfile captured = profile;
                tile.RegisterCallback<ClickEvent>(_ => OnTileClicked(captured));

                _tiles.Add(tile);
                grid.Add(tile);
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

        private static Label MakeLabel(params string[] classes)
        {
            Label label = new(string.Empty);

            for (int index = 0; index < classes.Length; index++)
            {
                label.AddToClassList(classes[index]);
            }

            return label;
        }

        private void OnDestroy() => _disposables.Dispose();
    }
}
