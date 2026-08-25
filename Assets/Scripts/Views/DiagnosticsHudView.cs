using System.Text;
using PoRumble.Models;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Profiling;
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

        [SerializeField] private StyleSheet _styleSheet;

        private readonly StringBuilder _builder = new(512);
        private readonly float[] _frameHistory = new float[HISTORY];

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

            _panel = new VisualElement();
            _panel.AddToClassList("diag");
            _panel.pickingMode = PickingMode.Ignore;
            root.Add(_panel);

            Label title = new("DIAGNOSTICS   F3");
            title.AddToClassList("diag__title");
            _panel.Add(title);

            _graph = new VisualElement();
            _graph.AddToClassList("diag__graph");
            _graph.generateVisualContent += DrawGraph;
            _panel.Add(_graph);

            _readout = new Label(string.Empty);
            _readout.AddToClassList("diag__readout");
            _panel.Add(_readout);

            _panel.style.display = _visibleOnStart ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame && _panel != null)
            {
                _panel.style.display = _panel.style.display == DisplayStyle.None
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
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
                    .Append("   peak ").Append(_worstMs.ToString("F2")).Append("ms\n");

            long draws = Read(_srpBatcherDraws) + Read(_standardDraws) + Read(_dynamicDraws);

            _builder.Append("draw     ").Append(draws)
                    .Append("   setpass ").Append(Read(_setPassRecorder))
                    .Append("   tris ").Append(Read(_trianglesRecorder))
                    .Append("   rendertex ").Append(Read(_renderTexturesRecorder)).Append('\n');


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

            _readout.text = _builder.ToString();
            _readout.EnableInClassList("diag__readout--over", averageMs > _frameBudgetMs);

            _accumulatedMs = 0f;
            _accumulatedFrames = 0;
            _worstMs = 0f;

            _graph.MarkDirtyRepaint();
        }

        /// <summary>Reads a recorder, or 0 when the counter is unavailable on this platform.</summary>
        private static long Read(ProfilerRecorder recorder)
        {
            return recorder.Valid ? recorder.LastValue : 0L;
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
