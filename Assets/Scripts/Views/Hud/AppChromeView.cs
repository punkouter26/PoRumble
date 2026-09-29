using System.Text;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// The five fixed points of the HUD: what this is, where the bout stands, the way back to
    /// the menu, the way into the telemetry sheet, and which build is installed.
    ///
    /// Every other panel in this project is tied to a phase and disappears with it. That is
    /// right for the fight and wrong for these: a version that is only in the APK filename is not
    /// one anyone can read off a device, and on a phone the overlay and the exit both had
    /// gestures rather than buttons - three fingers and a key that is not there.
    ///
    /// The top-centre slot carried a frame rate for a long time. That is a developer's number in
    /// the one spot every viewer looks at, so it now carries the bout - its number on the menu,
    /// the clock and the field while the fight is live, a warning as the ropes are about to
    /// close - and the frame rate lives at the head of the DEBUG sheet. The amber "something is
    /// wrong with this build" signal the counter used to carry moved onto the DEBUG button.
    ///
    /// A View, and a thin one throughout. Both buttons hand straight to a System, which decides
    /// for itself whether the request is legal right now.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class AppChromeView : MonoBehaviour
    {
        /// <summary>Seconds before the ropes move at which the status starts counting down to them.</summary>
        private const int ROPES_WARNING_SECONDS = 10;

        [Tooltip("The bar's structure. Without it nothing is drawn.")]
        [SerializeField] private VisualTreeAsset _layout;

        [Tooltip("The shared HUD stylesheet. Without it the bar renders unstyled.")]
        [SerializeField] private StyleSheet _styleSheet;

        private readonly CompositeDisposable _disposables = new();
        private readonly StringBuilder _builder = new(32);

        private MatchModel _match;
        private MatchFlowModel _flow;
        private MatchFlowSystem _flowSystem;
        private SuddenDeathModel _suddenDeath;
        private DiagnosticsModel _diagnostics;
        private DiagnosticsSystem _diagnosticsSystem;

        private Label _status;
        private Button _menu;
        private Button _debug;

        /// <summary>
        /// A digest of everything the status line shows. Compared before every write so the
        /// line is rebuilt only when a figure on it actually changed, not every frame.
        /// </summary>
        private int _shownKey = int.MinValue;

        [Inject]
        public void Construct(
            MatchModel match,
            MatchFlowModel flow,
            MatchFlowSystem flowSystem,
            SuddenDeathModel suddenDeath,
            DiagnosticsModel diagnostics,
            DiagnosticsSystem diagnosticsSystem)
        {
            _match = match;
            _flow = flow;
            _flowSystem = flowSystem;
            _suddenDeath = suddenDeath;
            _diagnostics = diagnostics;
            _diagnosticsSystem = diagnosticsSystem;
        }

        private void Start()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            // Reported rather than returned on quietly. Every other optional presentation
            // component in this scene may legitimately be absent, so they all fail silent - but
            // this one is wired up by hand in the scene and a silent bail reads on a device as
            // "the chrome bar was never written", which is a much longer hunt than the one line
            // it takes to say which half is missing.
            if (root == null || _flow == null)
            {
                GameLog.Error(
                    $"{nameof(AppChromeView)} cannot start: " +
                    $"rootVisualElement={(root == null ? "null" : "ok")}, " +
                    $"injected={(_flow == null ? "no" : "yes")}. The chrome bar will not render.",
                    this);
                return;
            }

            if (_styleSheet != null)
            {
                root.styleSheets.Add(_styleSheet);
            }

            if (_layout == null)
            {
                GameLog.Error(
                    $"{nameof(AppChromeView)} has no layout assigned; the chrome bar will not " +
                    "render. Assign Assets/UI/Layouts/AppChrome.uxml.", this);
                return;
            }

            _layout.CloneTree(root);

            // The root is an element UXML never declared, and it spans the screen. This document
            // sorts above every other one, so leaving it pickable would swallow every tap meant
            // for the title screen or the results card.
            root.pickingMode = PickingMode.Ignore;

            _status = root.Q<Label>("status");
            _menu = root.Q<Button>("menu");
            _debug = root.Q<Button>("debug");

            Label version = root.Q<Label>("version");

            if (version != null)
            {
                version.text = BuildStamp();
            }

            if (_menu != null)
            {
                _menu.clicked += () => _flowSystem.TryReturnToTitle();
                _flow.Phase.Subscribe(_ => RefreshMenu()).AddTo(_disposables);
                RefreshMenu();
            }

            if (_debug != null && _diagnostics != null)
            {
                _debug.clicked += () => _diagnosticsSystem.Toggle();
                _diagnostics.IsVisible
                    .Subscribe(visible => _debug.EnableInClassList("chrome__button--on", visible))
                    .AddTo(_disposables);

                // Amber whenever the diagnostics verdict has anything to report, open or not, so a
                // broken build shows on the one button that leads to the reason - including one
                // that is broken at a perfect 60fps.
                _diagnostics.FindingCount
                    .Subscribe(count => _debug.EnableInClassList("chrome__button--alert", count > 0))
                    .AddTo(_disposables);
            }
        }

        /// <summary>
        /// Polled rather than subscribed: the line depends on the phase, the clock, the alive
        /// count and the ropes, and folding them into one comparison is cheaper than four
        /// subscriptions that would each rebuild the same string.
        /// </summary>
        private void Update()
        {
            if (_status == null)
            {
                return;
            }

            MatchFlowPhase phase = _flow.Phase.Value;
            int seconds = _suddenDeath.FightSeconds.Value;
            int alive = _match.CountAlive();
            bool closing = _suddenDeath.Closing.Value;

            int key = ((((int)phase * 16 + alive) * 2 + (closing ? 1 : 0)) * 4096) + seconds;

            if (key == _shownKey)
            {
                return;
            }

            _shownKey = key;
            _builder.Clear();

            switch (phase)
            {
                case MatchFlowPhase.Fighting:
                    AppendClock(seconds);
                    _builder.Append("   ");

                    int untilRopes = SuddenDeathMath.SecondsUntilClose(seconds);

                    if (closing)
                    {
                        _builder.Append("ROPES CLOSING");
                    }
                    else if (untilRopes <= ROPES_WARNING_SECONDS)
                    {
                        _builder.Append("ROPES IN ").Append(untilRopes);
                    }
                    else
                    {
                        _builder.Append(alive).Append(" LEFT");
                    }

                    break;

                case MatchFlowPhase.KnockoutHold:
                case MatchFlowPhase.Results:
                    _builder.Append("FINAL   ");
                    AppendClock(seconds);
                    break;

                default:
                    _builder.Append("BOUT ").Append(_flow.MatchNumber.Value);
                    break;
            }

            _status.text = _builder.ToString();
            _status.EnableInClassList("chrome__status--alert",
                phase == MatchFlowPhase.Fighting && (closing || SuddenDeathMath.SecondsUntilClose(seconds) <= ROPES_WARNING_SECONDS));
        }

        private void AppendClock(int seconds)
        {
            _builder.Append(seconds / 60).Append(':');

            int remainder = seconds % 60;

            if (remainder < 10)
            {
                _builder.Append('0');
            }

            _builder.Append(remainder);
        }

        /// <summary>
        /// The menu button is the only way out of a live fight on a phone, and it is dead on
        /// the title screen because there is nothing there to leave. Disabled rather than
        /// hidden: a control that vanishes and reappears in the corner reads as a glitch, and
        /// the bar is meant to be the one part of the screen that never moves.
        /// </summary>
        private void RefreshMenu()
        {
            bool canLeave = _flow.Phase.Value != MatchFlowPhase.Title;

            _menu.SetEnabled(canLeave);
            _menu.pickingMode = canLeave ? PickingMode.Position : PickingMode.Ignore;
        }

        /// <summary>
        /// Version name and, on a device, the version code the package manager actually
        /// installed.
        ///
        /// Read back from the installed package rather than baked in at compile time, because
        /// the number worth showing is the one that answers "is this the APK I just pushed" -
        /// and a constant compiled into the build cannot be wrong in the interesting way. Off
        /// Android there is no code to report and the name stands alone.
        /// </summary>
        private string BuildStamp()
        {
            _builder.Clear();
            _builder.Append('v').Append(Application.version);

#if UNITY_ANDROID && !UNITY_EDITOR
            int versionCode = AndroidVersionCode();

            if (versionCode > 0)
            {
                _builder.Append(" (").Append(versionCode).Append(')');
            }
#endif

            return _builder.ToString();
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>
        /// PackageInfo.versionCode for the installed package, or 0 if the lookup fails.
        ///
        /// Wrapped rather than trusted: this is four JNI hops through classes a manufacturer
        /// skin is free to surprise us with, and a version label is not worth an exception on
        /// the first frame of the game.
        /// </summary>
        private static int AndroidVersionCode()
        {
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject manager = activity.Call<AndroidJavaObject>("getPackageManager"))
                using (AndroidJavaObject info = manager.Call<AndroidJavaObject>(
                    "getPackageInfo", activity.Call<string>("getPackageName"), 0))
                {
                    return info.Get<int>("versionCode");
                }
            }
            catch (System.Exception exception)
            {
                GameLog.Warning(
                    $"{nameof(AppChromeView)} could not read the installed version code: " +
                    $"{exception.Message}. The chrome bar will show the version name only.");
                return 0;
            }
        }
#endif

        private void OnDestroy() => _disposables.Dispose();
    }
}
