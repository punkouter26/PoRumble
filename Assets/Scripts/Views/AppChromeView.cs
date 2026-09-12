using System.Text;
using PoRumble.Models;
using PoRumble.Systems;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// The five fixed points of the HUD: what this is, how fast it is running, the way back to
    /// the menu, the way into the telemetry sheet, and which build is installed.
    ///
    /// Every other panel in this project is tied to a phase and disappears with it. That is
    /// right for the fight and wrong for these: a frame rate that is only visible behind a
    /// developer key is not a frame rate anyone checks, a version that is only in the APK
    /// filename is not one anyone can read off a device, and on a phone the overlay and the
    /// exit both had gestures rather than buttons - three fingers and a key that is not there.
    ///
    /// A View, and a thin one throughout. The frame average is the only number it computes, and
    /// it computes it because a frames-per-second readout of the last single frame is noise.
    /// Both buttons hand straight to a System, which decides for itself whether the request is
    /// legal right now.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class AppChromeView : MonoBehaviour
    {
        [Tooltip("The bar's structure. Without it nothing is drawn.")]
        [SerializeField] private VisualTreeAsset _layout;

        [Tooltip("The shared HUD stylesheet. Without it the bar renders unstyled.")]
        [SerializeField] private StyleSheet _styleSheet;

        [Tooltip("Seconds between frame-rate refreshes. Sampling every frame makes the number " +
                 "unreadable and turns a diagnostic into a flicker.")]
        [SerializeField] private float _fpsRefreshSeconds = 0.25f;

        private readonly CompositeDisposable _disposables = new();
        private readonly StringBuilder _builder = new(32);

        private MatchFlowModel _flow;
        private MatchFlowSystem _flowSystem;
        private DiagnosticsModel _diagnostics;
        private DiagnosticsSystem _diagnosticsSystem;

        private Label _fps;
        private Button _menu;
        private Button _debug;

        private float _accumulatedMs;
        private int _accumulatedFrames;
        private float _refreshTimer;

        /// <summary>
        /// The last value actually written to the label. Compared before every write so a
        /// steady 60 does not rebuild the same string four times a second.
        /// </summary>
        private int _shownFps = -1;

        [Inject]
        public void Construct(
            MatchFlowModel flow,
            MatchFlowSystem flowSystem,
            DiagnosticsModel diagnostics,
            DiagnosticsSystem diagnosticsSystem)
        {
            _flow = flow;
            _flowSystem = flowSystem;
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
                Debug.LogError(
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
                Debug.LogError(
                    $"{nameof(AppChromeView)} has no layout assigned; the chrome bar will not " +
                    "render. Assign Assets/UI/Layouts/AppChrome.uxml.", this);
                return;
            }

            _layout.CloneTree(root);

            // The root is an element UXML never declared, and it spans the screen. This document
            // sorts above every other one, so leaving it pickable would swallow every tap meant
            // for the fight card, the menu, or the tap-anywhere restart.
            root.pickingMode = PickingMode.Ignore;

            _fps = root.Q<Label>("fps");
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
            }
        }

        /// <summary>
        /// Unscaled throughout: the knockout hold runs the world at quarter speed and a frame
        /// counter that moved with it would report 15fps on a machine rendering 60.
        /// </summary>
        private void Update()
        {
            _accumulatedMs += Time.unscaledDeltaTime * 1000f;
            _accumulatedFrames++;
            _refreshTimer += Time.unscaledDeltaTime;

            if (_fps == null || _refreshTimer < _fpsRefreshSeconds)
            {
                return;
            }

            float averageMs = _accumulatedFrames > 0 ? _accumulatedMs / _accumulatedFrames : 0f;
            int fps = averageMs > 0f ? Mathf.RoundToInt(1000f / averageMs) : 0;

            _accumulatedMs = 0f;
            _accumulatedFrames = 0;
            _refreshTimer = 0f;

            if (fps == _shownFps)
            {
                return;
            }

            _shownFps = fps;

            _builder.Clear();
            _builder.Append(fps).Append(" FPS");
            _fps.text = _builder.ToString();
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
                Debug.LogWarning(
                    $"{nameof(AppChromeView)} could not read the installed version code: " +
                    $"{exception.Message}. The chrome bar will show the version name only.");
                return 0;
            }
        }
#endif

        private void OnDestroy() => _disposables.Dispose();
    }
}
