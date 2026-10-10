using PoRumble.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace PoRumble.Views
{
    /// <summary>The Painter2D half of the diagnostics sheet: the frame and decision traces.</summary>
    public sealed partial class DiagnosticsHudView
    {
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

            if (_page == DiagnosticsPage.Match)
            {
                DrawTrace(context.painter2D, bounds);
                DrawMidLine(context.painter2D, bounds);
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
            DrawMidLine(context.painter2D, bounds);
        }

        private static void DrawMidLine(Painter2D painter, Rect bounds)
        {
            painter.strokeColor = new Color(0.95f, 0.45f, 0.35f, 0.55f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, bounds.height * 0.5f));
            painter.LineTo(new Vector2(bounds.width, bounds.height * 0.5f));
            painter.Stroke();
        }

        /// <summary>
        /// The match trace across the whole graph: frame time in amber against the same
        /// twice-the-budget scale as the frame page, and thermal status as a stepped red line
        /// on its own 0..6 scale. Squeezed rather than scrolled, so the start of the match is
        /// always on screen to compare against - that comparison is the page's whole purpose.
        /// </summary>
        private void DrawTrace(Painter2D painter, Rect bounds)
        {
            int count = _trace == null ? 0 : _trace.Count;

            if (count < 2)
            {
                return;
            }

            // At most one point per pixel column. A thirty-minute trace is 1800 samples and the
            // graph is well under that wide, so every point beyond that is tessellation spent on
            // segments shorter than a pixel.
            int stride = Mathf.Max(1, Mathf.CeilToInt(count / Mathf.Max(1f, bounds.width)));
            float scale = _frameBudgetMs * 2f;
            float step = bounds.width / (count - 1);

            painter.strokeColor = new Color(0.98f, 0.74f, 0.3f, 0.9f);
            painter.lineWidth = 1.5f;
            painter.BeginPath();

            for (int index = 0; index < count; index += stride)
            {
                float y = bounds.height * (1f - Mathf.Clamp01(_trace.Get(index).AverageMs / scale));
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

            if (_trace.WorstThermalStatus() < 0)
            {
                return;
            }

            painter.strokeColor = new Color(0.95f, 0.3f, 0.3f, 0.8f);
            painter.lineWidth = 1f;
            painter.BeginPath();

            for (int index = 0; index < count; index += stride)
            {
                int status = Mathf.Max(0, _trace.Get(index).ThermalStatus);
                float y = bounds.height * (1f - status / 6f);
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
