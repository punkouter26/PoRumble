using System.Text;
using PoRumble.Models;
using Unity.InferenceEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
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
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class DiagnosticsHudView : MonoBehaviour
    {
        private const int HISTORY = 120;

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

        // Scratch for the percentile sort. Pre-allocated because this overlay exists to report
        // allocation rate and would be lying if it allocated an array to do it.
        private readonly float[] _sortedFrames = new float[HISTORY];

        private MatchModel _match;
        private MatchFlowModel _flow;

        private VisualElement _panel;
        private Label _readout;
        private VisualElement _graph;

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

        // Every agent in the scene, and the fixed half of what its policy is. Cached because
        // the roster re-seats agents rather than destroying them - the set never changes
        // during a session - and because the observation and action shapes are compiled into
        // the model and cannot change at all.
        private BehaviorParameters[] _agentPolicies;
        private string _policyShape;

        // The compiled model, and its name held against it. ModelAsset.name allocates a string
        // on every read, and this readout refreshes four times a second; the reference is
        // compared instead and the name rebuilt only when the asset actually changes, which in
        // the game is never and in the checkpoint evaluator is once per run.
        private ModelAsset _lastModel;
        private string _modelName = "none";

        private int _historyHead;
        private float _refreshTimer;
        private float _accumulatedMs;
        private int _accumulatedFrames;
        private float _worstMs;
        private long _lastGcBytes;
        private float _gcPerSecond;

        [Inject]
        public void Construct(MatchModel match, MatchFlowModel flow)
        {
            _match = match;
            _flow = flow;
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

            _agentPolicies = FindObjectsByType<BehaviorParameters>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            _policyShape = DescribePolicyShape();

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

            if (_panel == null || _graph == null || _readout == null)
            {
                return;
            }

            // The graph has no children: it is painted directly with Painter2D, so the
            // callback is what gives the element its contents.
            _graph.generateVisualContent += DrawGraph;
            _panel.style.display = _visibleOnStart ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Shows or hides the overlay.
        ///
        /// Public because the chrome carries a bottom-left button for it. F3 and the
        /// three-finger tap are developer gestures that nobody discovers; the button is how
        /// the overlay is actually reachable on a phone.
        /// </summary>
        public void Toggle()
        {
            if (_panel == null)
            {
                return;
            }

            _panel.style.display = _panel.style.display == DisplayStyle.None
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private void Update()
        {
            if (TogglePressed())
            {
                Toggle();
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

            _builder.Clear();
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

            AppendPolicyStats();

            _readout.text = _builder.ToString();
            _readout.EnableInClassList("diag__readout--over", averageMs > _frameBudgetMs);

            _accumulatedMs = 0f;
            _accumulatedFrames = 0;
            _worstMs = 0f;

            _graph.MarkDirtyRepaint();
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
        /// The half of the policy description that is compiled into the model and cannot move
        /// at runtime: observation and action shapes, and how often a decision is asked for.
        ///
        /// Worth showing because the action vector being frozen is the single constraint that
        /// most often bites here - CLAUDE.md records that growing it stops the model loading
        /// altogether, and until now nothing in a build reported what shape was actually
        /// bound. A model whose observation size disagrees with what CollectObservations
        /// writes does not fail loudly; it just refuses to load, and this is where that shows.
        /// </summary>
        private string DescribePolicyShape()
        {
            if (_agentPolicies == null || _agentPolicies.Length == 0)
            {
                return "no agents";
            }

            BehaviorParameters parameters = _agentPolicies[0];

            if (parameters == null)
            {
                return "no agents";
            }

            BrainParameters brain = parameters.BrainParameters;

            var shape = new StringBuilder(96);

            shape.Append("obs ").Append(brain.VectorObservationSize)
                 .Append('x').Append(brain.NumStackedVectorObservations);

            // The ray fan is a separate sensor component and carries most of the observation
            // budget, so a vector size alone would understate what the network is fed.
            if (parameters.TryGetComponent(out RayPerceptionSensorComponent2D rays))
            {
                shape.Append(" + ").Append(rays.RaysPerDirection * 2 + 1)
                     .Append(" rays x").Append(rays.ObservationStacks);
            }

            shape.Append("   act ").Append(brain.ActionSpec.NumContinuousActions)
                 .Append("c/").Append(brain.ActionSpec.NumDiscreteActions).Append('d');

            if (parameters.TryGetComponent(out DecisionRequester requester))
            {
                shape.Append("   period ").Append(requester.DecisionPeriod);
            }

            return shape.ToString();
        }

        /// <summary>
        /// What the agents are actually running: the behaviour name the trainer must match,
        /// the compiled model bound to them, where inference executes, and how the ring is
        /// split between the policy and the scripted brains.
        ///
        /// The split is counted every refresh rather than cached, because re-dealing the fight
        /// card swaps controllers on boxers that already exist - the agents are never
        /// destroyed, so a count taken at Start would go quietly stale the first time the card
        /// is changed.
        /// </summary>
        private void AppendPolicyStats()
        {
            if (_agentPolicies == null || _agentPolicies.Length == 0)
            {
                return;
            }

            BehaviorParameters first = null;
            int policyDriven = 0;
            int scripted = 0;

            for (int index = 0; index < _agentPolicies.Length; index++)
            {
                BehaviorParameters parameters = _agentPolicies[index];

                if (parameters == null)
                {
                    continue;
                }

                // Explicit rather than ??=, which is C# null coalescing and would not see a
                // destroyed object as null the way Unity's own == does.
                if (first == null)
                {
                    first = parameters;
                }

                if (parameters.BehaviorType == BehaviorType.HeuristicOnly)
                {
                    scripted++;
                }
                else
                {
                    policyDriven++;
                }
            }

            if (first == null)
            {
                return;
            }

            ModelAsset model = first.Model;

            if (model != _lastModel)
            {
                _lastModel = model;
                _modelName = model == null ? "none" : model.name;
            }

            _builder.Append("\npolicy   ").Append(first.BehaviorName)
                    .Append("   ").Append(_modelName)
                    .Append("   ").Append(first.InferenceDevice).Append('\n');

            _builder.Append("shape    ").Append(_policyShape).Append('\n');

            _builder.Append("brains   ").Append(policyDriven).Append(" policy / ")
                    .Append(scripted).Append(" scripted");
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
        /// Paints the frame-time history as a filled graph, with the budget drawn across it so
        /// a spike is legible as "over budget" rather than merely "tall".
        /// </summary>
        private void DrawGraph(MeshGenerationContext context)
        {
            Rect bounds = context.visualElement.contentRect;

            if (bounds.width <= 0f || bounds.height <= 0f)
            {
                return;
            }

            Painter2D painter = context.painter2D;

            // Scaled to twice the budget, so a frame at exactly 60fps sits at half height and
            // there is headroom above it to see how bad a spike really is.
            float scale = _frameBudgetMs * 2f;
            float step = bounds.width / (HISTORY - 1);

            painter.strokeColor = new Color(0.45f, 0.85f, 0.55f, 0.9f);
            painter.lineWidth = 1.5f;
            painter.BeginPath();

            for (int index = 0; index < HISTORY; index++)
            {
                float value = _frameHistory[(_historyHead + index) % HISTORY];
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

            painter.strokeColor = new Color(0.95f, 0.45f, 0.35f, 0.55f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, bounds.height * 0.5f));
            painter.LineTo(new Vector2(bounds.width, bounds.height * 0.5f));
            painter.Stroke();
        }
    }
}
