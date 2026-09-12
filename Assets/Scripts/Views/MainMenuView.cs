using System.Text;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// The title screen: what the player sees while <see cref="MatchFlowPhase.Title"/> is up.
    ///
    /// The phase and the loop that returns to it both already existed. What did not was
    /// anything to press: the match HUD drew "PO RUMBLE" onto the centre stage and a line of
    /// text naming a key, which on a phone named a key that is not there. Starting a fight and
    /// opening the card are the only two things a player can do in this game that are not a
    /// punch, and neither had a button.
    ///
    /// A View, and a thin one. It shows and hides on a phase it does not own, and its two
    /// buttons call Systems that decide for themselves whether the request is legal right now -
    /// the same division <see cref="MatchInputView"/> already uses, which is what makes it safe
    /// for both to be live at once.
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

        [Tooltip("The shared HUD stylesheet. Without it the menu renders unstyled.")]
        [SerializeField] private StyleSheet _styleSheet;

        private readonly CompositeDisposable _disposables = new();
        private readonly StringBuilder _builder = new(96);

        private MatchFlowModel _flow;
        private MatchFlowSystem _flowSystem;
        private RosterModel _roster;
        private RosterSystem _rosterSystem;

        private VisualElement _screen;
        private Label _hint;

        [Inject]
        public void Construct(
            MatchFlowModel flow,
            MatchFlowSystem flowSystem,
            RosterModel roster,
            RosterSystem rosterSystem)
        {
            _flow = flow;
            _flowSystem = flowSystem;
            _roster = roster;
            _rosterSystem = rosterSystem;
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

            _screen = root.Q<VisualElement>("screen");
            _hint = root.Q<Label>("hint");

            Button fight = root.Q<Button>("fight");
            Button card = root.Q<Button>("card");

            if (fight != null)
            {
                fight.clicked += () => _flowSystem.TryStartFight();
            }

            if (card != null)
            {
                card.clicked += () => _rosterSystem.Toggle();
            }

            // The root itself is left pickable only while the menu is up - see Refresh. A
            // full-screen scrim that stayed pickable would swallow every tap meant for the
            // panels underneath it for the whole match.
            _flow.Phase.Subscribe(_ => Refresh()).AddTo(_disposables);
            _roster.IsOpen.Subscribe(_ => Refresh()).AddTo(_disposables);
            _roster.Revision.Subscribe(_ => RefreshHint()).AddTo(_disposables);

            Refresh();
        }

        /// <summary>
        /// Shows the menu on the title phase and hides it everywhere else.
        ///
        /// Also hidden while the fight card is open, even though both belong to the same phase.
        /// The card is a full-screen modal of its own and is sorted above this one, so leaving
        /// the menu up behind it would put two scrims and two sets of buttons on the same
        /// screen - and the card's own tiles would be competing with a FIGHT button that is
        /// still technically live underneath them.
        /// </summary>
        private void Refresh()
        {
            if (_screen == null)
            {
                return;
            }

            bool visible = _flow.Phase.Value == MatchFlowPhase.Title && !_roster.IsOpen.Value;

            _screen.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            // display:none already stops hit-testing, but the document root is a separate
            // element that UXML never declared, and it spans the screen whatever this one
            // does. Setting it here keeps the menu from eating clicks aimed at the HUD below.
            _screen.pickingMode = visible ? PickingMode.Position : PickingMode.Ignore;

            if (visible)
            {
                RefreshHint();
            }
        }

        /// <summary>
        /// The line under the buttons: how many contestants are on the card, and the keyboard
        /// shortcuts where there is a keyboard to use them.
        ///
        /// The count is the useful half. The card seats the ring cyclically, so picking three
        /// fighters for a ten-boxer ring is legal and means several of them fight twice - which
        /// is worth knowing before the bell rather than discovering from the health rows.
        /// </summary>
        private void RefreshHint()
        {
            if (_hint == null)
            {
                return;
            }

            _builder.Clear();

            int entrants = _roster.Entrants.Count;

            if (entrants > 0)
            {
                _builder.Append(entrants).Append(entrants == 1 ? " CONTESTANT" : " CONTESTANTS")
                        .Append(" ON THE CARD");
            }
            else
            {
                _builder.Append("NO CARD - THE HOUSE FIGHTERS TAKE THE RING");
            }

            // Named from the devices actually present rather than from a platform define, for
            // the reason the match HUD's prompts already are: a phone has no Enter key, and a
            // desktop that happens to have a touchscreen still has a keyboard sitting there.
            //
            // Asked through InputPresence rather than of Keyboard.current directly, because
            // Android answers that question yes on every phone - the hardware buttons arrive as
            // key events and the Input System builds a Keyboard to carry them. This line shipped
            // to a device, under the two buttons that were the only way to do either thing.
            if (InputPresence.HasUsableKeyboard())
            {
                _builder.Append("\nENTER TO FIGHT      TAB FOR THE CARD");
            }

            _hint.text = _builder.ToString();
        }

        private void OnDestroy() => _disposables.Dispose();
    }
}
