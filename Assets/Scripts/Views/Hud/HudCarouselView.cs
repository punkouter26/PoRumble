using System;
using PoRumble.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace PoRumble.Views
{
    /// <summary>
    /// On a portrait screen, shows the broadcast panels one at a time in a single slot instead
    /// of stacking them, under a row of tabs naming them.
    ///
    /// The HUD is laid out as two columns - the field board on the left, the telemetry board and
    /// the standings on the right - and on a 1080x1920 phone a second row of panels starts over
    /// a third of the way down, leaving the ring to be watched through the gaps. Landscape has
    /// the width for all of them; portrait does not. So in portrait the right-hand panels share
    /// one slot and take turns, and nothing ever has to scroll, because only one is ever up.
    ///
    /// The turns used to run on a timer alone, so a panel could change halfway through being
    /// read and there was no way back to the one that had just gone. The tabs fix both: they say
    /// what is in the rotation, and a tap on one holds it in place for
    /// <see cref="_manualHoldSeconds"/> before the rotation resumes.
    ///
    /// Only panels that want to be up take a turn or get a tab. A panel its own view has hidden
    /// for the phase, or whose document the live fight has cleared, is skipped rather than given
    /// a blank few seconds.
    ///
    /// Works entirely through USS classes on each panel, so every panel's own view keeps owning
    /// its content and its visibility and none of them knows this exists. A View: it reads the
    /// documents, writes only classes, and draws its own tab row.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudCarouselView : MonoBehaviour
    {
        private const string SLOTTED_CLASS = "hud-carousel--slotted";
        private const string OFF_CLASS = "hud-carousel--off";
        private const string PANEL_NAME = "panel";

        [Serializable]
        private struct Slot
        {
            [Tooltip("The panel's document.")]
            [SerializeField] private UIDocument _document;

            [Tooltip("The class the panel's own view adds when it hides the panel, e.g. " +
                     "stats--hidden. A panel carrying it does not take a turn.")]
            [SerializeField] private string _hiddenClass;

            [Tooltip("The panel's tab. One short word: the tab row is half a phone wide.")]
            [SerializeField] private string _label;

            public UIDocument Document => _document;
            public string HiddenClass => _hiddenClass;
            public string Label => _label;
        }

        [Tooltip("The panels that share the slot in portrait, in the order they take turns.")]
        [SerializeField] private Slot[] _slots;

        [Tooltip("Seconds each panel holds the slot.")]
        [SerializeField] private float _dwellSeconds = 6f;

        [Tooltip("Seconds a tapped tab holds the slot before the rotation resumes. Long enough " +
                 "to read a board through without it changing under you.")]
        [SerializeField] private float _manualHoldSeconds = 30f;

        [Tooltip("Width over height below which the screen counts as portrait.")]
        [SerializeField] private float _portraitAspect = 0.9f;

        [Tooltip("The tab row's structure. Without it the slot still rotates, with no tabs.")]
        [SerializeField] private VisualTreeAsset _tabsLayout;

        [Tooltip("The shared HUD stylesheet.")]
        [SerializeField] private StyleSheet _styleSheet;

        [Tooltip("Horizontal travel, in panel pixels, that counts as a swipe rather than a tap.")]
        [SerializeField] private float _swipeDistance = 80f;

        private VisualElement[] _panels;
        private Button[] _tabs;
        private bool[] _eligible;
        private VisualElement _tabRow;
        private VisualElement _progress;

        /// <summary>The last width written to the progress bar, in whole percent, so it is not rewritten every frame.</summary>
        private int _shownProgress = -1;

        /// <summary>Where the current press started, or NaN with no press down.</summary>
        private float _pressX = float.NaN;

        private int _current = -1;
        private int _applied = -2;
        private bool _portrait;
        private bool _slotted;
        private float _dwell;
        private float _holdRemaining;

        private void Awake()
        {
            int count = _slots == null ? 0 : _slots.Length;
            _panels = new VisualElement[count];
            _tabs = new Button[count];
            _eligible = new bool[count];
        }

        private void Start()
        {
            BuildTabs();
        }

        /// <summary>Unscaled: the rotation should not stall through hitstop or the knockout hold.</summary>
        private void Update()
        {
            if (_panels.Length == 0)
            {
                return;
            }

            ResolvePanels();

            _portrait = Screen.width < Screen.height * _portraitAspect;

            if (_portrait != _slotted)
            {
                SetSlotted(_portrait);
            }

            if (!_portrait)
            {
                return;
            }

            for (int index = 0; index < _panels.Length; index++)
            {
                _eligible[index] = IsEligible(index);
            }

            _current = HudCarouselMath.Resolve(_eligible, _current);

            if (_holdRemaining > 0f)
            {
                _holdRemaining -= Time.unscaledDeltaTime;
            }
            else
            {
                _dwell += Time.unscaledDeltaTime;

                if (_dwell >= _dwellSeconds)
                {
                    _dwell = 0f;
                    _current = HudCarouselMath.NextEligible(_eligible, _current);
                }
            }

            RefreshTabs();

            if (_current != _applied)
            {
                ApplyTurn();
            }

            RefreshProgress();
        }

        /// <summary>
        /// The bar under the tabs: how much of this panel's turn is left. Full after a tap or a
        /// swipe and draining through the long hold; otherwise draining through the dwell.
        /// </summary>
        private void RefreshProgress()
        {
            if (_progress == null)
            {
                return;
            }

            float left = _holdRemaining > 0f
                ? _holdRemaining / Mathf.Max(0.01f, _manualHoldSeconds)
                : 1f - _dwell / Mathf.Max(0.01f, _dwellSeconds);

            int percent = Mathf.Clamp(Mathf.RoundToInt(left * 100f), 0, 100);

            if (percent == _shownProgress)
            {
                return;
            }

            _shownProgress = percent;
            _progress.style.width = Length.Percent(percent);
        }

        private void OnPanelPointerDown(PointerDownEvent evt)
        {
            _pressX = _slotted ? evt.position.x : float.NaN;
        }

        /// <summary>
        /// A swipe left shows the next panel, right the previous one, and holds it like a tapped
        /// tab. A short press is not a swipe and does nothing: the panels are read, not pressed.
        /// </summary>
        private void OnPanelPointerUp(PointerUpEvent evt)
        {
            if (float.IsNaN(_pressX))
            {
                return;
            }

            float travel = evt.position.x - _pressX;
            _pressX = float.NaN;

            if (Mathf.Abs(travel) < _swipeDistance)
            {
                return;
            }

            int target = travel < 0f
                ? HudCarouselMath.NextEligible(_eligible, _current)
                : HudCarouselMath.PreviousEligible(_eligible, _current);

            OnTabClicked(target);
        }

        /// <summary>
        /// One tab per slot, built once. A tab whose panel is not eligible is hidden by class
        /// rather than removed, so the row never rebuilds while a finger is on it.
        /// </summary>
        private void BuildTabs()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            if (root == null || _tabsLayout == null)
            {
                return;
            }

            if (_styleSheet != null)
            {
                root.styleSheets.Add(_styleSheet);
            }

            // The root spans the screen; only the tabs themselves may take a tap.
            root.pickingMode = PickingMode.Ignore;

            _tabsLayout.CloneTree(root);
            _tabRow = root.Q<VisualElement>("tabs");

            if (_tabRow == null)
            {
                return;
            }

            _progress = _tabRow.Q<VisualElement>("progress");

            for (int index = 0; index < _tabs.Length; index++)
            {
                var tab = new Button { text = _slots[index].Label };
                tab.AddToClassList("text");
                tab.AddToClassList("text--xs");
                tab.AddToClassList("text--bold");
                tab.AddToClassList("carousel-tab");

                int slot = index;
                tab.clicked += () => OnTabClicked(slot);

                _tabRow.Add(tab);
                _tabs[index] = tab;
            }
        }

        private void OnTabClicked(int slot)
        {
            if (slot < 0 || slot >= _eligible.Length || !_eligible[slot])
            {
                return;
            }

            _current = slot;
            _holdRemaining = _manualHoldSeconds;
            _dwell = 0f;
        }

        /// <summary>
        /// Shows the row only when there is a choice to make: in portrait, with at least two
        /// panels wanting the slot. One panel under a single tab is a heading, which is exactly
        /// what the tabs replaced.
        /// </summary>
        private void RefreshTabs()
        {
            if (_tabRow == null)
            {
                return;
            }

            int eligibleCount = 0;

            for (int index = 0; index < _tabs.Length; index++)
            {
                Button tab = _tabs[index];

                if (_eligible[index])
                {
                    eligibleCount++;
                }

                if (tab == null)
                {
                    continue;
                }

                tab.EnableInClassList("carousel-tab--off", !_eligible[index]);
                tab.EnableInClassList("carousel-tab--on", index == _current);
            }

            _tabRow.EnableInClassList("carousel-tabs--hidden", !_slotted || eligibleCount < 2);
        }

        /// <summary>
        /// Finds each panel once its view has built it. The views clone their layouts in Start,
        /// in no particular order relative to this, so a panel not found yet is looked for
        /// again next frame rather than given up on.
        /// </summary>
        private void ResolvePanels()
        {
            for (int index = 0; index < _panels.Length; index++)
            {
                if (_panels[index] != null)
                {
                    continue;
                }

                UIDocument document = _slots[index].Document;

                if (document == null || document.rootVisualElement == null)
                {
                    continue;
                }

                _panels[index] = document.rootVisualElement.Q<VisualElement>(PANEL_NAME);

                if (_panels[index] != null)
                {
                    // Force the new panel into the current mode on the next pass.
                    _panels[index].EnableInClassList(SLOTTED_CLASS, _slotted);
                    _panels[index].pickingMode = _slotted ? PickingMode.Position : PickingMode.Ignore;
                    _panels[index].RegisterCallback<PointerDownEvent>(OnPanelPointerDown);
                    _panels[index].RegisterCallback<PointerUpEvent>(OnPanelPointerUp);
                    _applied = -2;
                }
            }
        }

        private bool IsEligible(int index)
        {
            VisualElement panel = _panels[index];

            if (panel == null)
            {
                return false;
            }

            // HudVisibilityView clears a whole document during the fight through the root's
            // visibility, which the panel's own classes know nothing about.
            UIDocument document = _slots[index].Document;

            if (document == null || document.rootVisualElement == null || !document.rootVisualElement.visible)
            {
                return false;
            }

            string hidden = _slots[index].HiddenClass;
            return string.IsNullOrEmpty(hidden) || !panel.ClassListContains(hidden);
        }

        private void SetSlotted(bool slotted)
        {
            _slotted = slotted;

            for (int index = 0; index < _panels.Length; index++)
            {
                VisualElement panel = _panels[index];

                if (panel == null)
                {
                    continue;
                }

                panel.EnableInClassList(SLOTTED_CLASS, slotted);

                // Swipeable only while it is sharing the slot. In landscape the panels sit side
                // by side over the ring and have nothing to swipe to.
                panel.pickingMode = slotted ? PickingMode.Position : PickingMode.Ignore;

                if (!slotted)
                {
                    panel.EnableInClassList(OFF_CLASS, false);
                }
            }

            _applied = -2;
            _dwell = 0f;

            if (!slotted && _tabRow != null)
            {
                _tabRow.EnableInClassList("carousel-tabs--hidden", true);
            }
        }

        private void ApplyTurn()
        {
            for (int index = 0; index < _panels.Length; index++)
            {
                VisualElement panel = _panels[index];

                if (panel != null)
                {
                    panel.EnableInClassList(OFF_CLASS, index != _current);

                    // Only the panel on show takes a swipe. The others share its rectangle at
                    // zero opacity, and a transparent panel on top would catch the press.
                    panel.pickingMode = _slotted && index == _current ? PickingMode.Position : PickingMode.Ignore;
                }
            }

            _applied = _current;
            _dwell = 0f;
        }
    }
}
