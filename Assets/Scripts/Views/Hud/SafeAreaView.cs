using UnityEngine;
using UnityEngine.UIElements;

namespace PoRumble.Views
{
    /// <summary>
    /// Insets every UI panel by the device's safe area.
    ///
    /// Nothing did this before, and on the portrait build it showed: the survivor count sat at
    /// y=20 and the health panel at y=16 of a 1920-tall screen, both underneath the status bar
    /// on any phone that has one, and underneath the camera cutout on most modern ones.
    ///
    /// Applied as a margin on each document's root, which shrinks the root itself to the safe
    /// rectangle, rather than as anything on the panels. The HUD positions its panels
    /// absolutely against the root, so one write moves every corner-anchored panel at once and
    /// no individual layout has to know the notch exists.
    ///
    /// It was padding for a long time, and padding does nothing here. An absolutely positioned
    /// child's top and left are measured from its parent's padding edge, so a panel at
    /// <c>top: 80</c> sat at y=80 whatever the padding was - measured at 80 against a 116-unit
    /// inset, with the chrome row and its MENU button at y=0, under the status bar. Nothing
    /// errors and the padding reads back correctly, which is why it went unnoticed.
    ///
    /// It also marks each root with a portrait class while the screen is taller than it is wide,
    /// which the stylesheet uses to tighten the larger spacing steps on a phone. Same trigger,
    /// same set of roots - a rotation has to move both at once.
    ///
    /// One coordinator rather than a component per document: panels come and go with the
    /// roster and the diagnostics overlay, and a per-document script is one that gets forgotten
    /// on the next panel somebody adds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SafeAreaView : MonoBehaviour
    {
        private const string PORTRAIT_CLASS = "portrait";

        [Tooltip("Extra inset applied on every edge on top of the reported safe area, in " +
                 "reference pixels. Keeps the HUD off the exact boundary, which on a rounded " +
                 "display is still clipped by the corner radius.")]
        [SerializeField] private float _extraInset = 12f;

        private Rect _lastSafeArea;
        private int _lastWidth;
        private int _lastHeight;

        /// <summary>
        /// False until a pass has actually written padding to every document. Keeps Update
        /// retrying while panels are still resolving their first layout.
        /// </summary>
        private bool _applied;

        private void OnEnable()
        {
            Apply();
        }

        /// <summary>
        /// Re-applied whenever the screen changes rather than once at startup. A phone rotates,
        /// a desktop window resizes, and the Editor's Game view changes resolution constantly -
        /// and the safe area reported before the first frame is not always the final one.
        /// </summary>
        private void Update()
        {
            // _applied is part of the condition, not just the screen metrics. Panels resolve
            // their layout a frame or more after the first Apply, and an early pass that
            // skipped every one of them would otherwise cache the current screen size and
            // never run again - which is exactly how this shipped padding of zero.
            if (_applied &&
                Screen.safeArea == _lastSafeArea &&
                Screen.width == _lastWidth &&
                Screen.height == _lastHeight)
            {
                return;
            }

            Apply();
        }

        private void Apply()
        {
            Rect safe = Screen.safeArea;
            int width = Screen.width;
            int height = Screen.height;

            if (width <= 0 || height <= 0)
            {
                return;
            }

            _lastSafeArea = safe;
            _lastWidth = width;
            _lastHeight = height;

            // Screen.safeArea is in device pixels with its origin at the bottom left; UI
            // Toolkit lays out in panel points from the top left. Both insets are therefore
            // converted through the panel's own resolved height rather than through the screen,
            // or the inset would be wrong by the panel's scale factor on every device whose
            // reference resolution is not its native one.
            // Clamped, and not defensively: in the Editor Screen.safeArea reports the whole
            // display (1440x2999 here) while Screen.width/height report the Game view's
            // resolution (1080x1920), so the raw right and top insets come out negative and a
            // negative margin would push the HUD off the screen. A device can report a
            // safe area larger than the screen during a rotation for the same reason.
            //
            // Half the axis is the upper bound: an inset past that would leave nothing to lay
            // out in, and any value that large is a bad reading rather than a real notch.
            float leftFraction = Mathf.Clamp(safe.xMin / width, 0f, 0.5f);
            float rightFraction = Mathf.Clamp((width - safe.xMax) / width, 0f, 0.5f);
            float topFraction = Mathf.Clamp((height - safe.yMax) / height, 0f, 0.5f);
            float bottomFraction = Mathf.Clamp(safe.yMin / height, 0f, 0.5f);
            bool portrait = height > width;

            UIDocument[] documents = FindObjectsByType<UIDocument>(FindObjectsInactive.Include);

            bool everyDocumentReady = documents.Length > 0;

            for (int documentIndex = 0; documentIndex < documents.Length; documentIndex++)
            {
                UIDocument document = documents[documentIndex];

                if (document == null)
                {
                    continue;
                }

                VisualElement root = document.rootVisualElement;

                if (root == null || root.panel == null)
                {
                    continue;
                }

                // Measured on the panel, not on the root: the margins below shrink the root, so
                // its own size is already inset and would compound on every later pass.
                Rect panelRect = root.panel.visualTree.layout;
                float panelWidth = panelRect.width;
                float panelHeight = panelRect.height;

                // Before the first layout pass the panel has no resolved size. Skipping is
                // correct rather than falling back to the screen - a guessed inset would be
                // visibly wrong for a frame - but it means this pass was incomplete, and
                // everyDocumentReady is what brings Update back to finish the job.
                if (float.IsNaN(panelWidth) || float.IsNaN(panelHeight) ||
                    panelWidth <= 0f || panelHeight <= 0f)
                {
                    everyDocumentReady = false;
                    continue;
                }

                root.style.marginLeft = leftFraction * panelWidth + _extraInset;
                root.style.marginRight = rightFraction * panelWidth + _extraInset;
                root.style.marginTop = topFraction * panelHeight + _extraInset;
                root.style.marginBottom = bottomFraction * panelHeight + _extraInset;

                // The stylesheet tightens the larger spacing steps under this class. Set here
                // because this is already the one place that visits every document root whenever
                // the screen changes shape - a rotation has to move both at once.
                root.EnableInClassList(PORTRAIT_CLASS, portrait);
            }

            _applied = everyDocumentReady;
        }
    }
}
