using UnityEngine;

namespace PoRumble.Views
{
    /// <summary>
    /// Asks the platform for a frame rate rather than accepting the one it assumes.
    ///
    /// On Android, Unity defaults <see cref="Application.targetFrameRate"/> to 30 and
    /// <c>vSyncCount</c> is ignored entirely, so the quality tier's vsync setting does nothing
    /// and the phone build ran at 30fps on a 120Hz panel. Nothing errored and nothing looked
    /// broken - the chrome bar simply read "30 FPS" on a device with four times the headroom,
    /// which reads as a performance problem rather than as a default nobody had overridden.
    /// Optimized frame pacing was already on; Swappy paces to whatever target it is given, and
    /// it was being given 30.
    ///
    /// A View because the frame rate is a platform concern in exactly the way
    /// <see cref="SafeAreaView"/>'s insets are: no System has any business knowing what panel
    /// the game is being drawn on, and nothing in Models or Systems changes when the answer
    /// does.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FrameRateView : MonoBehaviour
    {
        [Tooltip("Highest frame rate to ask for. The panel's own rate is used when it is lower. " +
                 "60 rather than the 120 a modern phone offers: the simulation steps at 50Hz, so " +
                 "the frames above 60 are redraws of an unchanged world bought with battery and " +
                 "heat.")]
        [SerializeField] private int _maxFrameRate = 60;

        /// <summary>
        /// The panel's rate as it was last applied. A phone changes refresh rate underneath a
        /// running app - adaptive panels drop to 60 or 10 to save power, and the user can change
        /// the display mode from the notification shade - so the target is re-asked for rather
        /// than set once at startup.
        /// </summary>
        private int _lastPanelRate = -1;

        private void OnEnable()
        {
            Apply();
        }

        private void Update()
        {
            Apply();
        }

        private void Apply()
        {
            int panelRate = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);

            if (panelRate == _lastPanelRate)
            {
                return;
            }

            _lastPanelRate = panelRate;

            // A panel that reports nothing useful - which the Editor and some emulators do -
            // leaves the cap standing on its own rather than producing a target of zero, which
            // Unity reads as "uncapped" and a phone reads as "render until the battery is flat".
            int target = panelRate > 0 ? Mathf.Min(panelRate, _maxFrameRate) : _maxFrameRate;

            if (Application.targetFrameRate == target)
            {
                return;
            }

            Application.targetFrameRate = target;

            Debug.Log(
                $"{nameof(FrameRateView)}: panel reports {panelRate}Hz, target frame rate set " +
                $"to {target}.");
        }
    }
}
