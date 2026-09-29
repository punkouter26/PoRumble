using UnityEngine;

namespace PoRumble.Views
{
    /// <summary>
    /// Puts the ears at a height proportional to the frame, so the stereo image is the width
    /// of the screen.
    ///
    /// The ring is flat and the camera looks straight down on it, so how far a punch pans is
    /// set by the angle from the ears to it - which depends on how high the ears are. With the
    /// listener fixed at the camera's z, a punch at the edge of a tight duel shot and one at
    /// the edge of a wide ten-way shot sat at wildly different angles: the tight shot panned
    /// hard off a few units of movement and the wide shot barely panned at all. Raising the
    /// ears with the orthographic size makes a punch at the edge of the screen pan the same
    /// amount at every zoom, and the extra height makes a wide shot sound further away, which
    /// is what a wide shot is.
    ///
    /// Lives on the Ears object under the main camera, which carries the scene's one
    /// AudioListener. A View: it reads the camera and writes only its own transform.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    [RequireComponent(typeof(AudioListener))]
    public sealed class ListenerRigView : MonoBehaviour
    {
        [Tooltip("The camera whose zoom sets the ears' height. Empty uses the parent camera.")]
        [SerializeField] private Camera _camera;

        [Tooltip("Height of the ears above the canvas per unit of orthographic size. Around 1 " +
                 "puts a punch at the edge of a portrait screen about 30 degrees off centre.")]
        [SerializeField] private float _heightPerOrthoSize = 1.1f;

        [Tooltip("The lowest the ears go, so a very tight shot still has some height to pan from.")]
        [SerializeField] private float _minHeight = 5f;

        private Transform _transform;
        private Transform _cameraTransform;

        private void Awake()
        {
            _transform = transform;

            if (_camera == null)
            {
                _camera = GetComponentInParent<Camera>();
            }

            if (_camera != null)
            {
                _cameraTransform = _camera.transform;
            }
        }

        /// <summary>
        /// After the Cinemachine brain has placed the camera, so the ears are over this frame's
        /// shot rather than the last one's.
        /// </summary>
        private void LateUpdate()
        {
            if (_camera == null || !_camera.orthographic)
            {
                return;
            }

            Vector3 over = _cameraTransform.position;
            float height = Mathf.Max(_minHeight, _camera.orthographicSize * _heightPerOrthoSize);

            // The canvas is at z 0 and the camera looks down +z, so height is negative z.
            _transform.position = new Vector3(over.x, over.y, -height);
        }
    }
}
