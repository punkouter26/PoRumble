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
    /// Applied as padding on each document's root rather than as a margin on the panels
    /// themselves. The HUD positions its panels absolutely against the root, and an absolutely
    /// positioned child resolves against its parent's padding box, so one padding write moves
    /// every corner-anchored panel at once and no individual layout has to know the notch
    /// exists.
    ///
    /// One coordinator rather than a component per document: panels come and go with the
    /// roster and the diagnostics overlay, and a per-document script is one that gets forgotten
    /// on the next panel somebody adds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SafeAreaView : MonoBehaviour
    {
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
            // or the padding would be wrong by the panel's scale factor on every device whose
            // reference resolution is not its native one.
            // Clamped, and not defensively: in the Editor Screen.safeArea reports the whole
            // display (1440x2999 here) while Screen.width/height report the Game view's
            // resolution (1080x1920), so the raw right and top insets come out negative and a
            // negative padding silently loses the base inset as well. A device can report a
            // safe area larger than the screen during a rotation for the same reason.
            //
            // Half the axis is the upper bound: an inset past that would leave nothing to lay
            // out in, and any value that large is a bad reading rather than a real notch.
            float leftFraction = Mathf.Clamp(safe.xMin / width, 0f, 0.5f);
            float rightFraction = Mathf.Clamp((width - safe.xMax) / width, 0f, 0.5f);
            float topFraction = Mathf.Clamp((height - safe.yMax) / height, 0f, 0.5f);
            float bottomFraction = Mathf.Clamp(safe.yMin / height, 0f, 0.5f);

            UIDocument[] documents = FindObjectsByType<UIDocument>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            bool everyDocumentReady = documents.Length > 0;

            for (int documentIndex = 0; documentIndex < documents.Length; documentIndex++)
            {
                UIDocument document = documents[documentIndex];

                if (document == null)
                {
                    continue;
                }

                VisualElement root = document.rootVisualElement;

                if (root == null)
                {
                    continue;
                }

                float rootWidth = root.resolvedStyle.width;
                float rootHeight = root.resolvedStyle.height;

                // Before the first layout pass the root has no resolved size. Skipping is
                // correct rather than falling back to the screen - a guessed inset would be
                // visibly wrong for a frame - but it means this pass was incomplete, and
                // everyDocumentReady is what brings Update back to finish the job.
                if (float.IsNaN(rootWidth) || float.IsNaN(rootHeight) ||
                    rootWidth <= 0f || rootHeight <= 0f)
                {
                    everyDocumentReady = false;
                    continue;
                }

                root.style.paddingLeft = leftFraction * rootWidth + _extraInset;
                root.style.paddingRight = rightFraction * rootWidth + _extraInset;
                root.style.paddingTop = topFraction * rootHeight + _extraInset;
                root.style.paddingBottom = bottomFraction * rootHeight + _extraInset;
            }

            _applied = everyDocumentReady;
        }
    }
}
