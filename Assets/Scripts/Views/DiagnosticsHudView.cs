using System.Text;
using PoRumble.Models;
using PoRumble.Systems;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Profiling;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// A real-time diagnostic overlay, toggled with F3.
    ///
    /// The project's performance rules set hard budgets - zero allocation in the update loops,
    /// the lowest draw-call count reachable - but nothing in the game ever reported whether
    /// those budgets were being met. Profiling in the Editor window tells you about the Editor;
    /// this reports the numbers as the game actually runs, including in a build.
    ///
    /// Sampled once every refresh rather than every frame, and the text is built through a
    /// pooled StringBuilder, so the overlay does not itself become the allocation it exists to
    /// measure.
    ///
    /// Two pages, because there are two different questions to ask and the answers do not
    /// share a column. <see cref="DiagnosticsPage.Frame"/> is the renderer and the allocator -
    /// what the performance rules budget. <see cref="DiagnosticsPage.Combat"/> is the
    /// simulation: how hard the policy is being driven, what is actually being thrown, and
    /// where the director is pointing. Stacking both would make a sheet tall enough to cover
    /// the fight it is reporting on, and a developer is only ever reading one of them.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class DiagnosticsHudView : MonoBehaviour
    {
        private const int HISTORY = 120;

        /// <summary>Which page of the overlay is up.</summary>
        private enum DiagnosticsPage
        {
            Frame = 0,
            Combat = 1
        }

        [Tooltip("Start with the overlay visible. Off by default: it is a developer tool.")]
        [SerializeField] private bool _visibleOnStart;

        [Tooltip("Seconds between refreshes. Sampling every frame makes the numbers unreadable " +
                 "and costs more than it measures.")]
        [SerializeField] private float _refreshSeconds = 0.25f;

        [Tooltip("Frame time above which the graph turns red, in milliseconds. 16.7 is 60fps.")]
        [SerializeField] private float _frameBudgetMs = 16.7f;

        [Tooltip("The overlay's structure. Without it nothing is drawn.")]
        [SerializeField] private VisualTreeAsset _layout;

        [SerializeField] private StyleSheet _styleSheet;

        private readonly StringBuilder _builder = new(512);
        private readonly float[] _frameHistory = new float[HISTORY];

        // Decisions per second, on the same ring buffer shape as the frame history so the two
        // pages can share one graph painter. This is the number that moves when inference
        // stalls, and unlike frame time it says whether the *policy* is keeping up rather than
        // whether the renderer is.
        private readonly float[] _decisionHistory = new float[HISTORY];

        // Scratch for the percentile sort. Pre-allocated because this overlay exists to report
        // allocation rate and would be lying if it allocated an array to do it.
        private readonly float[] _sortedFrames = new float[HISTORY];

        private readonly CompositeDisposable _disposables = new();

        private MatchModel _match;
        private MatchFlowModel _flow;
        private FightStatsModel _stats;
        private DirectorModel _director;
        private RosterModel _roster;
        private DiagnosticsModel _diagnostics;
        private DiagnosticsSystem _diagnosticsSystem;

        private VisualElement _panel;
        private Label _readout;
        private VisualElement _graph;
        private Button _frameTab;
        private Button _combatTab;

        private DiagnosticsPage _page = DiagnosticsPage.Frame;

        private ProfilerRecorder _srpBatcherDraws;
        private ProfilerRecorder _standardDraws;
        private ProfilerRecorder _dynamicDraws;
        private ProfilerRecorder _setPassRecorder;
        private ProfilerRecorder _trianglesRecorder;
        private ProfilerRecorder _renderTexturesRecorder;
        private ProfilerRecorder _textureMemoryRecorder;
        private ProfilerRecorder _textureCountRecorder;
        private ProfilerRecorder _meshMemoryRecorder;

        // Counted once. Neither the light rig nor the caster set changes during a session -
        // RingAtmosphereView moves and dims lights but never adds or removes one - so polling
        // for these every refresh would be a scene-wide search for a constant.
        private int _light2DCount;
        private int _shadowCasterCount;

        // The pooled combat voices, cached at Start. There is no profiler counter for playing
        // audio sources on this platform: the Audio category publishes timing markers only,
        // which was read back from ProfilerRecorderHandle.GetAvailable rather than assumed.
        private AudioSource[] _audioSources;

        private int _historyHead;
        private float _refreshTimer;
        private float _accumulatedMs;
        private int _accumulatedFrames;
        private float _worstMs;
        private long _lastGcBytes;
        private float _gcPerSecond;

        /// <summary>How many <see cref="BoxerAgentView"/> seats exist. Counted once at Start.</summary>
        private int _agentCount;

        private int _lastAcademyStep;
        private float _decisionsPerSecond;

        /// <summary>
        /// Write head for <see cref="_decisionHistory"/>, kept apart from
        /// <see cref="_historyHead"/> because that one advances every frame and this series is
        /// only sampled once per refresh.
        /// </summary>
        private int _decisionHead;

        [Inject]
        public void Construct(
            MatchModel match,
            MatchFlowModel flow,
            FightStatsModel stats,
            DirectorModel director,
            RosterModel roster,
            DiagnosticsModel diagnostics,
            DiagnosticsSystem diagnosticsSystem)
        {
            _match = match;
            _flow = flow;
            _stats = stats;
            _director = director;
            _roster = roster;
            _diagnostics = diagnostics;
            _diagnosticsSystem = diagnosticsSystem;
        }

        private void Awake()
        {
            // Counter names were read back from ProfilerRecorderHandle.GetAvailable rather than
            // assumed: this Unity version publishes no plain "Draw Calls Count" at all, and a
            // recorder asking for one silently reports zero forever.
            //
            // Draw calls are split across three counters by how each batch was submitted, so
            // the total is the sum of all three.
            _srpBatcherDraws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SRP Batcher Draw Calls Count");
            _standardDraws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Standard Draw Calls Count");
            _dynamicDraws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Dynamic Batched Draw Calls Count");
            _setPassRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _trianglesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            // Render-texture count rather than shadow-caster or VRAM counts: "Shadow Casters
            // Count" only tracks 3D casters and reads zero for ShadowCaster2D, "Video Memory
            // Bytes" reports the adapter total, and "Used Textures Count" is gated behind a
            // profiler flag that is off here. All three would be confidently wrong numbers on
            // a screen whose whole job is being right. Render textures are real and, with 2D
            // lights and post-processing both on, the number worth watching.
            _renderTexturesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Render Textures Count");

            // Texture memory is the number that moves when art changes, and this project has
            // just taken on a normal map per sprite and an SDF atlas per font weight. Without
            // it, a doubling of VRAM footprint is invisible until a device runs out.
            _textureMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Texture Memory");
            _textureCountRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Texture Count");
            _meshMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Mesh Memory");
        }

        private void Start()
        {
            // One scene-wide search each, at startup, for values that never change afterwards.
            _light2DCount = FindObjectsByType<Light2D>(FindObjectsSortMode.None).Length;
            _shadowCasterCount = FindObjectsByType<ShadowCaster2D>(FindObjectsSortMode.None).Length;

            // The combat voice pool builds its sources in Awake, so by Start they all exist.
            _audioSources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);

            // Counted once for the same reason the lights are: the ring always seats ten and
            // re-dealing the card reconfigures those seats rather than creating new ones, so
            // this is a constant for the session.
            //
            // Inactive objects are excluded, unlike everywhere else in this project that
            // searches the scene. The boxers are clones of Boxer_Template, which stays in the
            // hierarchy switched off - counting it reported eleven agents in a ten-boxer ring.
            _agentCount = FindObjectsByType<BoxerAgentView>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;

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
                    $"{nameof(DiagnosticsHudView)} has no layout assigned; the overlay will " +
                    "not render. Assign Assets/UI/Layouts/Diagnostics.uxml.", this);
                return;
            }

            _layout.CloneTree(root);

            _panel = root.Q<VisualElement>("panel");
            _graph = root.Q<VisualElement>("graph");
            _readout = root.Q<Label>("readout");
            _frameTab = root.Q<Button>("tab-frame");
            _combatTab = root.Q<Button>("tab-combat");

            if (_panel == null || _graph == null || _readout == null)
            {
                return;
            }

            // Tabs are optional so an older layout asset still renders the frame page rather
            // than throwing. The overlay is a developer tool and must not be the thing that
            // stops the game running.
            if (_frameTab != null)
            {
                _frameTab.clicked += () => SelectPage(DiagnosticsPage.Frame);
            }

            if (_combatTab != null)
            {
                _combatTab.clicked += () => SelectPage(DiagnosticsPage.Combat);
            }

            // The graph has no children: it is painted directly with Painter2D, so the
            // callback is what gives the element its contents.
            _graph.generateVisualContent += DrawGraph;

            // The serialized default seeds the model rather than the element directly, so the
            // chrome bar's DEBUG button reads the right state on the first frame too.
            if (_diagnostics != null)
            {
                _diagnostics.IsVisible.Value = _visibleOnStart;
                _diagnostics.IsVisible
                    .Subscribe(visible => _panel.style.display =
                        visible ? DisplayStyle.Flex : DisplayStyle.None)
                    .AddTo(_disposables);
            }

            _panel.style.display = _visibleOnStart ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Switches page and refreshes immediately rather than waiting for the next tick. At
        /// the default quarter-second refresh a tab that stayed on the old page for up to 250ms
        /// reads as a click that did not register.
        /// </summary>
        private void SelectPage(DiagnosticsPage page)
        {
            _page = page;

            if (_frameTab != null)
            {
                _frameTab.EnableInClassList("diag__tab--on", page == DiagnosticsPage.Frame);
            }

            if (_combatTab != null)
            {
                _combatTab.EnableInClassList("diag__tab--on", page == DiagnosticsPage.Combat);
            }

            Refresh();
            _refreshTimer = 0f;
        }

        private void Update()
        {
            // The key and the gesture ask; DiagnosticsSystem decides and the model carries the
            // answer back to the panel through the subscription in Start. A view that flipped
            // its own display here would leave the chrome bar's DEBUG button labelled for a
            // state the sheet is no longer in.
            if (_diagnosticsSystem != null && TogglePressed())
            {
                _diagnosticsSystem.Toggle();
            }

            // Unscaled: hitstop and the knockout hold both change timeScale, and a frame-time
            // readout that moved with them would be measuring the wrong thing entirely.
            float frameMs = Time.unscaledDeltaTime * 1000f;
            _accumulatedMs += frameMs;
            _accumulatedFrames++;
            _worstMs = Mathf.Max(_worstMs, frameMs);

            _frameHistory[_historyHead] = frameMs;
            _historyHead = (_historyHead + 1) % HISTORY;

            if (_panel == null || _panel.style.display == DisplayStyle.None)
            {
                return;
            }

            _refreshTimer += Time.unscaledDeltaTime;

            if (_refreshTimer < _refreshSeconds)
            {
                return;
            }

            Refresh();
            _refreshTimer = 0f;
        }

        private void Refresh()
        {
            float averageMs = _accumulatedFrames > 0 ? _accumulatedMs / _accumulatedFrames : 0f;
            float fps = averageMs > 0f ? 1000f / averageMs : 0f;

            long gcNow = System.GC.GetTotalMemory(false);
            long delta = gcNow - _lastGcBytes;
            _lastGcBytes = gcNow;

            // Only count growth; a collection between samples shows up as a negative delta and
            // would otherwise read as though the game had freed memory it never allocated.
            if (delta > 0)
            {
                _gcPerSecond = delta / Mathf.Max(0.001f, _refreshTimer);
            }

            // Sampled on both pages, not only the combat one. The history is a ring buffer, and
            // one that only advanced while its own tab was up would show a flat line for
            // however long the developer had been reading the other page - which looks exactly
            // like a stalled policy.
            SampleDecisionRate();

            _builder.Clear();

            if (_page == DiagnosticsPage.Combat)
            {
                AppendCombatPage();
            }
            else
            {
                AppendFramePage(fps, averageMs, gcNow);
            }

            _readout.text = _builder.ToString();
            _readout.EnableInClassList("diag__readout--over", averageMs > _frameBudgetMs);

            // Reset regardless of page. These accumulate every frame, so leaving them running
            // while the combat page was up would make the frame page's first sample after a
            // tab switch an average over however long the other tab had been open.
            _accumulatedMs = 0f;
            _accumulatedFrames = 0;
            _worstMs = 0f;

            _graph.MarkDirtyRepaint();
        }

        /// <summary>
        /// The renderer and the allocator: what the performance rules actually set budgets for.
        /// </summary>
        private void AppendFramePage(float fps, float averageMs, long gcNow)
        {
            _builder.Append("fps      ").Append(fps.ToString("F0"))
                    .Append("   avg ").Append(averageMs.ToString("F2")).Append("ms")
                    .Append("   p95 ").Append(Percentile(0.95f).ToString("F2")).Append("ms")
                    .Append("   peak ").Append(_worstMs.ToString("F2")).Append("ms\n");

            long draws = Read(_srpBatcherDraws) + Read(_standardDraws) + Read(_dynamicDraws);

            _builder.Append("draw     ").Append(draws)
                    .Append("   setpass ").Append(Read(_setPassRecorder))
                    .Append("   tris ").Append(Read(_trianglesRecorder))
                    .Append("   rendertex ").Append(Read(_renderTexturesRecorder)).Append('\n');

            _builder.Append("vram     tex ").Append((Read(_textureMemoryRecorder) / 1048576f).ToString("F1"))
                    .Append(" MB / ").Append(Read(_textureCountRecorder))
                    .Append("   mesh ").Append((Read(_meshMemoryRecorder) / 1048576f).ToString("F1"))
                    .Append(" MB\n");

            // Shadow casters get their own line because they are the most expensive thing in
            // the scene - measured, not assumed: switching the key light's shadows off took
            // SetPass calls from 69 to 37. Unity's own "Shadow Casters Count" counts 3D casters
            // only and reads zero for every ShadowCaster2D here, so this is counted directly.
            _builder.Append("lights   ").Append(_light2DCount).Append(" light2d")
                    .Append("   ").Append(_shadowCasterCount).Append(" casters\n");

            _builder.Append("audio    ").Append(CountPlayingVoices())
                    .Append('/').Append(_audioSources == null ? 0 : _audioSources.Length)
                    .Append(" voices\n");


            _builder.Append("mono     ").Append((gcNow / 1048576f).ToString("F1")).Append(" MB")
                    .Append("   alloc ").Append((_gcPerSecond / 1024f).ToString("F0")).Append(" KB/s\n");

            _builder.Append("physics  ").Append((Time.fixedDeltaTime * 1000f).ToString("F1"))
                    .Append("ms step   timescale ").Append(Time.timeScale.ToString("F2")).Append('\n');

            if (_match != null)
            {
                _builder.Append("match    ").Append(_match.CountAlive())
                        .Append('/').Append(_match.Boxers.Count).Append(" alive");
            }

            if (_flow != null)
            {
                _builder.Append("   ").Append(_flow.Phase.Value)
                        .Append("   #").Append(_flow.MatchNumber.Value);
            }
        }

        /// <summary>
        /// The simulation rather than the renderer: how hard the policy is being driven, what
        /// the field is actually throwing, and where the director has pointed the camera.
        ///
        /// Totals across the whole ring rather than the director's pair. The telemetry board
        /// already reports the pair, and it reports it as a broadcast graphic; what is missing
        /// from the game entirely is a number for the field as a whole, which is the one that
        /// says whether a policy has stopped punching.
        /// </summary>
        private void AppendCombatPage()
        {
            _builder.Append("policy   ").Append(_agentCount).Append(" agents");

            if (Academy.IsInitialized)
            {
                _builder.Append("   academy ").Append(Academy.Instance.StepCount).Append(" steps")
                        .Append("   ").Append(_decisionsPerSecond.ToString("F1")).Append(" steps/s");
            }
            else
            {
                // Not an error: a scene with no agents never initialises the Academy, and
                // touching Academy.Instance would construct one as a side effect of looking.
                _builder.Append("   academy not initialised");
            }

            _builder.Append('\n');

            AppendCombatTotals();
            AppendDirectorLine();

            if (_match != null)
            {
                _builder.Append("match    ").Append(_match.CountAlive())
                        .Append('/').Append(_match.Boxers.Count).Append(" alive");
            }

            if (_flow != null)
            {
                _builder.Append("   ").Append(_flow.Phase.Value)
                        .Append("   #").Append(_flow.MatchNumber.Value);
            }
        }

        /// <summary>
        /// Sums the per-fighter tallies the telemetry board already keeps.
        ///
        /// Connect rate is landed over thrown, never over the punches that reached somebody -
        /// the same reason PunchThrownMessage exists at all. Computed over blocked, evaded and
        /// landed it would count only punches that hit something and report close to 100%.
        /// </summary>
        private void AppendCombatTotals()
        {
            if (_stats == null || _stats.Stats.Count == 0)
            {
                return;
            }

            int thrown = 0;
            int landed = 0;
            int blocked = 0;
            int evaded = 0;
            int slips = 0;
            int counters = 0;
            int haymakers = 0;
            int damage = 0;

            for (int index = 0; index < _stats.Stats.Count; index++)
            {
                FighterStats fighter = _stats.Stats[index];
                thrown += fighter.Thrown;
                landed += fighter.Landed;
                blocked += fighter.Blocked;
                evaded += fighter.Evaded;
                slips += fighter.Slips;
                counters += fighter.Counters;
                haymakers += fighter.Haymakers;
                damage += fighter.DamageDealt;
            }

            float connect = thrown > 0 ? landed / (float)thrown * 100f : 0f;

            _builder.Append("punches  ").Append(thrown).Append(" thrown")
                    .Append("   ").Append(landed).Append(" landed")
                    .Append("   ").Append(connect.ToString("F1")).Append("% connect\n");

            _builder.Append("stopped  ").Append(blocked).Append(" blocked")
                    .Append("   ").Append(evaded).Append(" evaded")
                    .Append("   ").Append(slips).Append(" slips\n");

            _builder.Append("heavy    ").Append(counters).Append(" counters")
                    .Append("   ").Append(haymakers).Append(" haymakers")
                    .Append("   ").Append(damage).Append(" damage\n");
        }

        /// <summary>Which pair the camera director picked, how tight, and how hard it scored.</summary>
        private void AppendDirectorLine()
        {
            if (_director == null)
            {
                return;
            }

            _builder.Append("director ").Append(_director.Shot.Value);

            if (_director.HasPair)
            {
                _builder.Append("   ").Append(NameOf(_director.FocusId))
                        .Append(" vs ").Append(NameOf(_director.RivalId))
                        .Append("   tension ").Append(_director.Tension.ToString("F2"));
            }
            else
            {
                _builder.Append("   no pair");
            }

            _builder.Append('\n');
        }

        /// <summary>The seated contestant's name, or the slot number when there is no card.</summary>
        private string NameOf(int boxerId)
        {
            FighterProfile profile = _roster == null ? null : _roster.SeatOf(boxerId);
            return profile != null ? profile.DisplayName : $"#{boxerId:00}";
        }

        /// <summary>
        /// Academy steps per second, pushed onto the ring buffer the combat graph paints.
        ///
        /// A rate rather than the raw counter, because the counter only ever climbs and a graph
        /// of it is a straight line. The rate is what falls when inference starts costing more
        /// than the frame can afford, which is the failure this page exists to catch.
        /// </summary>
        private void SampleDecisionRate()
        {
            if (!Academy.IsInitialized)
            {
                _decisionsPerSecond = 0f;
                _decisionHistory[_decisionHead] = 0f;
                _decisionHead = (_decisionHead + 1) % HISTORY;
                return;
            }

            int step = Academy.Instance.StepCount;
            int stepped = step - _lastAcademyStep;
            _lastAcademyStep = step;

            // A new episode resets the academy's counter, which would otherwise read as a
            // large negative rate for one sample and drag the graph's scale with it.
            if (stepped < 0)
            {
                stepped = 0;
            }

            _decisionsPerSecond = stepped / Mathf.Max(0.001f, _refreshTimer);

            _decisionHistory[_decisionHead] = _decisionsPerSecond;
            _decisionHead = (_decisionHead + 1) % HISTORY;
        }

        /// <summary>
        /// F3, or a three-finger tap where there is no keyboard.
        ///
        /// Three fingers rather than a screen-corner hit box: the overlay is a developer tool
        /// and a corner tap would collide with the tap-anywhere restart.
        /// </summary>
        private static bool TogglePressed()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame)
            {
                return true;
            }

            Touchscreen touchscreen = Touchscreen.current;

            if (touchscreen == null)
            {
                return false;
            }

            int pressed = 0;
            bool startedThisFrame = false;

            for (int index = 0; index < touchscreen.touches.Count; index++)
            {
                var touch = touchscreen.touches[index];

                if (!touch.press.isPressed)
                {
                    continue;
                }

                pressed++;
                startedThisFrame |= touch.press.wasPressedThisFrame;
            }

            // Fires once, on the frame the third finger lands.
            return pressed >= 3 && startedThisFrame;
        }

        /// <summary>Reads a recorder, or 0 when the counter is unavailable on this platform.</summary>
        private static long Read(ProfilerRecorder recorder)
        {
            return recorder.Valid ? recorder.LastValue : 0L;
        }

        /// <summary>
        /// A percentile over the frame history.
        ///
        /// p95 rather than the mean, and alongside the peak rather than instead of it, because
        /// the three answer different questions. The mean hides hitches entirely - a single
        /// 90ms frame in a 120-frame window moves a 16ms average by less than a millisecond.
        /// The peak catches that frame but cannot tell a one-off domain reload from a stutter
        /// happening several times a second. p95 is the one that says "this is how bad it
        /// regularly gets", which is what a player actually feels.
        /// </summary>
        private float Percentile(float fraction)
        {
            System.Array.Copy(_frameHistory, _sortedFrames, HISTORY);
            System.Array.Sort(_sortedFrames);

            int index = Mathf.Clamp(
                Mathf.RoundToInt(fraction * (HISTORY - 1)), 0, HISTORY - 1);

            return _sortedFrames[index];
        }

        /// <summary>
        /// How many pooled voices are mid-playback. Sixteen simultaneous punches on a
        /// fourteen-voice pool means the pool is stealing from itself and hits are being cut
        /// short, which is audible long before it is obvious why.
        /// </summary>
        private int CountPlayingVoices()
        {
            if (_audioSources == null)
            {
                return 0;
            }

            int playing = 0;

            for (int index = 0; index < _audioSources.Length; index++)
            {
                AudioSource source = _audioSources[index];

                if (source != null && source.isPlaying)
                {
                    playing++;
                }
            }

            return playing;
        }

        /// <summary>
        /// Recorders hold native handles. Leaving them undisposed leaks across a domain reload,
        /// which in the Editor means every entry into Play mode adds another.
        /// </summary>
        private void OnDestroy()
        {
            _disposables.Dispose();
            _srpBatcherDraws.Dispose();
            _standardDraws.Dispose();
            _dynamicDraws.Dispose();
            _setPassRecorder.Dispose();
            _trianglesRecorder.Dispose();
            _renderTexturesRecorder.Dispose();
            _textureMemoryRecorder.Dispose();
            _textureCountRecorder.Dispose();
            _meshMemoryRecorder.Dispose();
        }

        /// <summary>
        /// Paints whichever series belongs to the page that is up, with a reference line drawn
        /// across it so a reading is legible as "over budget" rather than merely "tall".
        ///
        /// One painter for both series rather than two callbacks on two elements. The shapes
        /// are identical - a fixed-length ring buffer of floats against a scale - and the only
        /// thing that actually differs is what the mid-line means, so forking it would be two
        /// copies of the same walk kept in step by hand.
        /// </summary>
        private void DrawGraph(MeshGenerationContext context)
        {
            Rect bounds = context.visualElement.contentRect;

            if (bounds.width <= 0f || bounds.height <= 0f)
            {
                return;
            }

            bool combat = _page == DiagnosticsPage.Combat;
            float[] series = combat ? _decisionHistory : _frameHistory;

            // Frame time scales to twice the budget, so a frame at exactly 60fps sits at half
            // height and there is headroom above it to see how bad a spike really is. The
            // decision rate scales to twice the physics rate for the same reason: a policy
            // keeping up sits on the mid-line and a stall falls visibly off it.
            float reference = combat ? 1f / Mathf.Max(0.0001f, Time.fixedDeltaTime) : _frameBudgetMs;
            float scale = reference * 2f;
            float step = bounds.width / (HISTORY - 1);

            // Each series has its own head: the frame history is written every frame, the
            // decision history once per refresh, so a shared head would leave the slower of
            // the two mostly holding stale samples from whenever it last happened to line up.
            int head = combat ? _decisionHead : _historyHead;

            StrokeSeries(context.painter2D, series, head, bounds, scale, step, combat);

            context.painter2D.strokeColor = new Color(0.95f, 0.45f, 0.35f, 0.55f);
            context.painter2D.lineWidth = 1f;
            context.painter2D.BeginPath();
            context.painter2D.MoveTo(new Vector2(0f, bounds.height * 0.5f));
            context.painter2D.LineTo(new Vector2(bounds.width, bounds.height * 0.5f));
            context.painter2D.Stroke();
        }

        /// <summary>Walks one ring-buffer series across the graph's box.</summary>
        private static void StrokeSeries(
            Painter2D painter,
            float[] series,
            int head,
            Rect bounds,
            float scale,
            float step,
            bool combat)
        {
            // Blue for the policy, green for the renderer: the two pages report different
            // things and the graph should not look identical on both.
            painter.strokeColor = combat
                ? new Color(0.45f, 0.72f, 0.95f, 0.9f)
                : new Color(0.45f, 0.85f, 0.55f, 0.9f);

            painter.lineWidth = 1.5f;
            painter.BeginPath();

            for (int index = 0; index < HISTORY; index++)
            {
                float value = series[(head + index) % HISTORY];
                float y = bounds.height * (1f - Mathf.Clamp01(value / scale));
                Vector2 point = new(index * step, y);

                if (index == 0)
                {
                    painter.MoveTo(point);
                }
                else
                {
                    painter.LineTo(point);
                }
            }

            painter.Stroke();
        }
    }
}
