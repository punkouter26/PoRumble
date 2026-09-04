using System.Text;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// The application chrome: title, frame rate, menu, telemetry toggle and version.
    ///
    /// One view owns all five because their contract is positional - each is pinned to a
    /// named corner - and a positional contract split across the match HUD, the diagnostics
    /// overlay and the fight card is one that nothing can check. Here the five are declared
    /// together in AppChrome.uxml and every other panel in the scene reflows around them.
    ///
    /// A View throughout: it reads models, writes text, and forwards two clicks. Whether the
    /// card may open is <see cref="RosterSystem"/> decision, and the overlay contents are
    /// entirely <see cref="DiagnosticsHudView"/> business.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class AppChromeView : MonoBehaviour
    {
        [Tooltip("The chrome structure. Without it nothing is drawn. " +
                 "Assign Assets/UI/Layouts/AppChrome.uxml.")]
        [SerializeField] private VisualTreeAsset _layout;

        [Tooltip("The shared HUD stylesheet. Without it the chrome renders unstyled.")]
        [SerializeField] private StyleSheet _styleSheet;

        [Tooltip("Shown top-left. Blank falls back to the product name in Player Settings, " +
                 "so the two cannot drift apart by accident.")]
        [SerializeField] private string _titleOverride;

        [Tooltip("The overlay the bottom-left button toggles. Leave empty in a scene that has " +
                 "no diagnostics overlay and the button hides itself.")]
        [SerializeField] private DiagnosticsHudView _diagnostics;

        [Tooltip("Seconds between frame-rate refreshes. Rewriting the label every frame costs " +
                 "more than the number is worth and makes it unreadable besides.")]
        [SerializeField] private float _fpsRefreshSeconds = 0.5f;

        [Tooltip("Frame rate below which the counter turns amber, then red at half of it.")]
        [SerializeField] private int _fpsTarget = 60;

        private readonly CompositeDisposable _disposables = new();
        private readonly StringBuilder _builder = new(16);

        private RosterSystem _rosterSystem;
        private RosterModel _roster;
        private MatchFlowModel _flow;
        private HudPointerModel _pointer;

        private Label _fpsLabel;
        private Button _menuButton;
        private Button _telemetryButton;

        private float _accumulatedSeconds;
        private int _accumulatedFrames;

        // The last value actually written. Assigning Label.text builds a string, so the
        // counter only writes when the integer it reports has changed - which at a steady
        // frame rate is almost never.
        private int _shownFps = -1;

        [Inject]
        public void Construct(
            RosterSystem rosterSystem,
            RosterModel roster,
            MatchFlowModel flow,
            HudPointerModel pointer)
        {
            _rosterSystem = rosterSystem;
            _roster = roster;
            _flow = flow;
            _pointer = pointer;
        }

        private void Start()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            if (root == null)
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
                    $"{nameof(AppChromeView)} has no layout assigned; the chrome will not " +
                    "render. Assign Assets/UI/Layouts/AppChrome.uxml.", this);
                return;
            }

            // The root fills the screen. Left pickable it would swallow every tap aimed at the
            // fight card sorted beneath the chrome; the two buttons still hit-test normally
            // with the root ignored.
            root.pickingMode = PickingMode.Ignore;

            _layout.CloneTree(root);

            Label title = root.Q<Label>("title");

            if (title != null)
            {
                title.text = string.IsNullOrWhiteSpace(_titleOverride)
                    ? Application.productName.ToUpperInvariant()
                    : _titleOverride;
            }

            Label version = root.Q<Label>("version");

            if (version != null)
            {
                // Read from the build rather than typed in, so a label claiming 1.0.3 cannot
                // survive on a 1.0.4 APK.
                _builder.Clear();
                _builder.Append('v').Append(Application.version);
                version.text = _builder.ToString();
            }

            _fpsLabel = root.Q<Label>("fps");
            _menuButton = root.Q<Button>("menu");
            _telemetryButton = root.Q<Button>("telemetry");

            if (_menuButton != null)
            {
                _menuButton.clicked += OnMenuClicked;
            }

            if (_telemetryButton != null)
            {
                // No overlay in this scene means no button. Hidden rather than left inert:
                // a control that does nothing when pressed is worse than an absent one.
                if (_diagnostics == null)
                {
                    _telemetryButton.style.display = DisplayStyle.None;
                }
                else
                {
                    _telemetryButton.clicked += OnTelemetryClicked;
                }
            }

            if (_flow != null)
            {
                _flow.Phase.Subscribe(_ => RefreshMenuButton()).AddTo(_disposables);
            }

            if (_roster != null)
            {
                _roster.IsOpen.Subscribe(_ => RefreshMenuButton()).AddTo(_disposables);
            }

            RefreshMenuButton();
        }

        private void Update()
        {
            // Unscaled throughout: hitstop and the knockout hold both drive Time.timeScale,
            // and a frame-rate readout that moved with them would be reporting the slow
            // motion rather than the machine.
            _accumulatedSeconds += Time.unscaledDeltaTime;
            _accumulatedFrames++;

            if (_fpsLabel == null || _accumulatedSeconds < _fpsRefreshSeconds)
            {
                return;
            }

            int fps = Mathf.RoundToInt(_accumulatedFrames / _accumulatedSeconds);

            _accumulatedSeconds = 0f;
            _accumulatedFrames = 0;

            if (fps == _shownFps)
            {
                return;
            }

            _shownFps = fps;

            _builder.Clear();
            _builder.Append(fps).Append(" FPS");
            _fpsLabel.text = _builder.ToString();

            _fpsLabel.EnableInClassList("chrome__fps--warn", fps < _fpsTarget && fps >= _fpsTarget / 2);
            _fpsLabel.EnableInClassList("chrome__fps--bad", fps < _fpsTarget / 2);
        }

        /// <summary>
        /// Dims the menu while the card cannot be opened, rather than hiding it.
        ///
        /// The chrome is a fixed contract - five elements, five corners - so an element that
        /// came and went would make the top row jump between phases. RosterSystem refuses the
        /// call anyway; this only says so in advance.
        /// </summary>
        private void RefreshMenuButton()
        {
            if (_menuButton == null || _flow == null)
            {
                return;
            }

            _menuButton.EnableInClassList("chrome__button--disabled", !_flow.CanOpenCard);
        }

        private void OnMenuClicked()
        {
            ClaimPointer();
            _rosterSystem.Toggle();
        }

        private void OnTelemetryClicked()
        {
            ClaimPointer();

            if (_diagnostics != null)
            {
                _diagnostics.Toggle();
            }
        }

        /// <summary>
        /// Tells <see cref="MatchInputView"/> that this frame press is already spoken for.
        ///
        /// That view reads a tap anywhere as a confirmation and does not hit-test, which is
        /// correct on a phone and is why the results screen needs no button of its own. Once
        /// the chrome grew buttons, though, a tap on MENU at the results screen would open the
        /// card and start the next match on the same press.
        /// </summary>
        private void ClaimPointer()
        {
            if (_pointer != null)
            {
                _pointer.Claim(Time.frameCount);
            }
        }

        private void OnDestroy()
        {
            if (_menuButton != null)
            {
                _menuButton.clicked -= OnMenuClicked;
            }

            if (_telemetryButton != null)
            {
                _telemetryButton.clicked -= OnTelemetryClicked;
            }

            _disposables.Dispose();
        }
    }
}
