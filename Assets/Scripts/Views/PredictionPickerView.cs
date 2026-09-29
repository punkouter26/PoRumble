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
    /// The pick strip on the title screen: tap a contestant to back them for the next bell.
    ///
    /// A View. Whether a pick is legal and what it pays are <see cref="PredictionSystem"/>'s
    /// business; this lists the card, shows the price and forwards taps.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class PredictionPickerView : MonoBehaviour
    {
        [Tooltip("The strip's structure. Without it nothing renders.")]
        [SerializeField] private VisualTreeAsset _layout;

        [Tooltip("One contestant's chip, cloned once per fighter on the full card.")]
        [SerializeField] private VisualTreeAsset _chipTemplate;

        [Tooltip("The shared HUD stylesheet. Without it the strip renders unstyled.")]
        [SerializeField] private StyleSheet _styleSheet;

        private readonly CompositeDisposable _disposables = new();
        private readonly StringBuilder _builder = new(64);
        private readonly List<VisualElement> _chips = new();
        private readonly List<Label> _prices = new();

        /// <summary>The price each chip last showed, in tenths, so an unchanged price is not rewritten.</summary>
        private readonly List<int> _shownPrices = new();

        private RosterModel _roster;
        private MatchFlowModel _flow;
        private PredictionModel _predictions;
        private PredictionSystem _predictionSystem;
        private WinOddsModel _odds;

        private VisualElement _panel;
        private Label _bank;
        private bool _visible;

        [Inject]
        public void Construct(
            RosterModel roster,
            MatchFlowModel flow,
            PredictionModel predictions,
            PredictionSystem predictionSystem,
            WinOddsModel odds)
        {
            _roster = roster;
            _flow = flow;
            _predictions = predictions;
            _predictionSystem = predictionSystem;
            _odds = odds;
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

            root.pickingMode = PickingMode.Ignore;

            if (_layout == null || _chipTemplate == null)
            {
                Debug.LogError(
                    $"{nameof(PredictionPickerView)} is missing its layout or chip template; the " +
                    "pick strip will not render. Assign Assets/UI/Layouts/PredictionPicker.uxml " +
                    "and Templates/PickChip.uxml.", this);
                return;
            }

            _layout.CloneTree(root);
            _panel = root.Q<VisualElement>("panel");
            _bank = root.Q<Label>("bank");

            BuildChips(root.Q<VisualElement>("chips"));

            _flow.Phase.Subscribe(_ => RefreshVisibility()).AddTo(_disposables);
            _roster.IsOpen.Subscribe(_ => RefreshVisibility()).AddTo(_disposables);
            _roster.Revision.Subscribe(_ => RefreshChips()).AddTo(_disposables);
            _predictions.Pick.Subscribe(_ => RefreshChips()).AddTo(_disposables);
            _predictions.Bank.Subscribe(_ => RefreshBank()).AddTo(_disposables);
            _odds.Revision.Subscribe(_ => RefreshPrices()).AddTo(_disposables);
        }

        /// <summary>
        /// One chip per contestant on the whole card, built once. Entering or dropping a
        /// fighter only shows or hides a chip, so the card screen can be toggled freely
        /// without the strip rebuilding under it.
        /// </summary>
        private void BuildChips(VisualElement container)
        {
            if (container == null)
            {
                return;
            }

            IReadOnlyList<FighterProfile> available = _roster.Available;

            for (int index = 0; index < available.Count; index++)
            {
                FighterProfile profile = available[index];

                _chipTemplate.CloneTree(container);
                VisualElement chip = container[container.childCount - 1];

                Label name = chip.Q<Label>("name");

                if (name != null)
                {
                    name.text = profile.DisplayName;
                }

                FighterProfile captured = profile;

                if (chip is Button button)
                {
                    button.clicked += () => _predictionSystem.TogglePick(captured);
                }

                _chips.Add(chip);
                _prices.Add(chip.Q<Label>("price"));
                _shownPrices.Add(-1);
            }
        }

        /// <summary>
        /// Up on the title screen only, and not while the fight card is open over it - the
        /// card is its own full-screen modal, and chips for a card that is mid-edit would
        /// offer prices on fighters about to be dropped.
        /// </summary>
        private void RefreshVisibility()
        {
            if (_panel == null)
            {
                return;
            }

            bool visible = _flow.Phase.Value == MatchFlowPhase.Title
                           && !_roster.IsOpen.Value
                           && _roster.Available.Count > 0;

            _visible = visible;
            _panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            if (visible)
            {
                RefreshChips();
                RefreshBank();
            }
        }

        private void RefreshChips()
        {
            IReadOnlyList<FighterProfile> available = _roster.Available;
            FighterProfile pick = _predictions.Pick.Value;

            for (int index = 0; index < _chips.Count && index < available.Count; index++)
            {
                FighterProfile profile = available[index];
                VisualElement chip = _chips[index];

                chip.style.display = _roster.IsEntrant(profile) ? DisplayStyle.Flex : DisplayStyle.None;
                chip.EnableInClassList("picker__chip--on", profile == pick);
            }

            RefreshPrices();
        }

        /// <summary>
        /// Rewrites each price from the live book. Four times a second while the odds tick, so
        /// only a chip whose price actually moved is touched.
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

                float multiplier = _predictionSystem.MultiplierFor(available[index]);
                int tenths = Mathf.RoundToInt(multiplier * 10f);

                if (tenths == _shownPrices[index])
                {
                    continue;
                }

                _shownPrices[index] = tenths;
                _builder.Clear();
                _builder.Append("PAYS ").Append(multiplier.ToString("0.0")).Append('x');
                price.text = _builder.ToString();
            }
        }

        private void RefreshBank()
        {
            if (_bank == null)
            {
                return;
            }

            _builder.Clear();
            _builder.Append("BANK ").Append(_predictions.Bank.Value)
                    .Append("   STAKE ").Append(PredictionSystem.STAKE);

            if (_predictions.Placed > 0)
            {
                _builder.Append("   ").Append(_predictions.Correct).Append(" OF ")
                        .Append(_predictions.Placed).Append(" CALLED");
            }

            _bank.text = _builder.ToString();
        }

        private void OnDestroy() => _disposables.Dispose();
    }
}
