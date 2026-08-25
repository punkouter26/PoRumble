using System.Collections.Generic;
using PoRumble.Models;
using Unity.Cinemachine;
using UnityEngine;
using VContainer;

namespace PoRumble.Views
{
    /// <summary>
    /// Frames the fight. Follows the centre of mass of everyone still standing and tightens as
    /// the field thins, so ten boxers read as a brawl and the last two read as a duel.
    ///
    /// The camera was previously a fixed transform pointed at the whole 40x40 ring, which
    /// meant the fighters were always small and always far away no matter how few were left.
    ///
    /// Deliberately drives only a follow target and an orthographic size rather than using a
    /// CinemachineTargetGroup: the bounding box is computed straight from the models, which
    /// are the authority on where boxers are, and Cinemachine still supplies the damping.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpectatorCameraView : MonoBehaviour
    {
        [Tooltip("The camera whose orthographic size is driven. Optional: leave empty to " +
                 "move the follow target without rescaling.")]
        [SerializeField] private CinemachineCamera _camera;

        [Tooltip("Transform the Cinemachine camera follows. Moved to the centre of the fight.")]
        [SerializeField] private Transform _followTarget;

        [Header("Framing")]
        [Tooltip("Closest the camera will pull in, for the final one-on-one.")]
        [SerializeField] private float _minOrthographicSize = 6f;
        [Tooltip("Widest the camera will pull out, for a full ten-way brawl. Sits above what " +
                 "the ring itself allows on a landscape screen, so the ring-fit rule is what " +
                 "actually binds; it only matters as a backstop on very tall displays.")]
        [SerializeField] private float _maxOrthographicSize = 45f;
        [Tooltip("Padding around the fighters, in world units, so nobody sits on the edge.")]
        [SerializeField] private float _framingPadding = 4.5f;

        [Tooltip("How far past the ropes the camera may look, in world units. The ring " +
                 "dressing - posts, turnbuckles, corner stools - lives just outside the " +
                 "canvas and should stay visible; beyond that there is nothing but void.")]
        [SerializeField] private float _outsideRingMargin = 4f;

        [Header("Damping")]
        [Tooltip("How quickly the framing catches up. Low values drift; high values snap.")]
        [SerializeField] private float _positionDamping = 2.5f;
        [SerializeField] private float _zoomDamping = 1.8f;

        [Header("Player bias")]
        [Tooltip("How strongly the framing favours the human boxer over the rest of the " +
                 "field. 0 frames everyone equally; 1 follows the player alone.")]
        [Range(0f, 1f)]
        [SerializeField] private float _playerBias = 0.35f;

        private MatchModel _match;
        private BoxerSpawnPoints _spawnPoints;

        private Vector2 _smoothedCenter;
        private float _smoothedSize;
        private bool _initialised;

        [Inject]
        public void Construct(MatchModel match, BoxerSpawnPoints spawnPoints)
        {
            _match = match;
            _spawnPoints = spawnPoints;
        }

        /// <summary>
        /// Framed in LateUpdate so the boxers have already been moved this frame. Doing it in
        /// Update would frame where everyone was last frame, which shows up as camera judder
        /// during fast exchanges.
        /// </summary>
        private void LateUpdate()
        {
            if (_match == null || _followTarget == null)
            {
                return;
            }

            if (!TryMeasureFight(out Vector2 center, out float extent))
            {
                return;
            }

            // The widest the camera may pull out before it starts showing the void outside
            // the ring. Derived from the aspect, because orthographic size is half-HEIGHT: on
            // a 2.2:1 phone screen a size that frames the ring vertically shows more than
            // twice the ring's width horizontally, which is most of what made the first
            // Android build look like it was pointed at a corner.
            Vector2 bounds = _match.ArenaHalfExtent + Vector2.one * _outsideRingMargin;
            float aspect = CurrentAspect();

            // The ring is square and no screen is, so one of two framings has to be chosen.
            //
            // Landscape crops: pull out only until the view is as wide as the ring, which
            // fills the screen and keeps the fighters large. Some of the ring's height is
            // off-frame, and the camera pans over it.
            //
            // Portrait letterboxes instead. Cropping a 0.56 aspect to fill would show barely
            // half the ring's width, so most of a ten-way brawl would be off-screen while the
            // HUD still claimed ten fighters were alive. Better to fit the whole ring and let
            // the spare height above and below carry the HUD.
            float cropToFill = Mathf.Min(bounds.y, bounds.x / aspect);
            float fitWhole = Mathf.Max(bounds.y, bounds.x / aspect);
            float maxByRing = aspect < 1f ? fitWhole : cropToFill;

            float desiredSize = Mathf.Clamp(
                extent + _framingPadding,
                _minOrthographicSize,
                Mathf.Min(_maxOrthographicSize, maxByRing));

            if (!_initialised)
            {
                _smoothedCenter = center;
                _smoothedSize = desiredSize;
                _initialised = true;
            }
            else
            {
                // Exponential smoothing on unscaled time: the knockout hold slows the world
                // right down, and a camera damped on scaled time would crawl through exactly
                // the moment it most needs to keep up with.
                float delta = Time.unscaledDeltaTime;
                _smoothedCenter = Vector2.Lerp(
                    _smoothedCenter, center, 1f - Mathf.Exp(-_positionDamping * delta));
                _smoothedSize = Mathf.Lerp(
                    _smoothedSize, desiredSize, 1f - Mathf.Exp(-_zoomDamping * delta));
            }

            Vector2 framed = ClampToRing(_smoothedCenter, _smoothedSize, bounds, aspect);
            _followTarget.position = new Vector3(framed.x, framed.y, _followTarget.position.z);

            if (_camera != null)
            {
                _camera.Lens.OrthographicSize = _smoothedSize;
            }
        }

        private static float CurrentAspect()
        {
            return Mathf.Max(0.1f, Screen.width / (float)Mathf.Max(1, Screen.height));
        }

        /// <summary>
        /// Keeps the visible rectangle inside the ring.
        ///
        /// Without this the camera simply centres on wherever the fighters are, so a scrap in
        /// a corner points it half out of the arena and fills the screen with empty backdrop.
        /// When the view is wider than the ring on an axis, it is centred on that axis instead
        /// of clamped - there is nothing to pan to.
        /// </summary>
        private static Vector2 ClampToRing(Vector2 center, float orthographicSize, Vector2 bounds, float aspect)
        {
            float halfHeight = orthographicSize;
            float halfWidth = orthographicSize * aspect;

            center.x = halfWidth >= bounds.x
                ? 0f
                : Mathf.Clamp(center.x, -bounds.x + halfWidth, bounds.x - halfWidth);

            center.y = halfHeight >= bounds.y
                ? 0f
                : Mathf.Clamp(center.y, -bounds.y + halfHeight, bounds.y - halfHeight);

            return center;
        }

        /// <summary>
        /// Finds the centre and half-extent of everyone still standing. Returns false when
        /// nobody is left, in which case the last framing is held rather than snapping to the
        /// origin the instant the match ends.
        /// </summary>
        private bool TryMeasureFight(out Vector2 center, out float extent)
        {
            center = Vector2.zero;
            extent = 0f;

            IReadOnlyList<BoxerModel> boxers = _match.Boxers;
            int humanId = _spawnPoints != null ? _spawnPoints.HumanBoxerId : -1;

            Vector2 min = new(float.MaxValue, float.MaxValue);
            Vector2 max = new(float.MinValue, float.MinValue);
            int aliveCount = 0;
            Vector2 playerPosition = Vector2.zero;
            bool playerAlive = false;

            for (int boxerIndex = 0; boxerIndex < boxers.Count; boxerIndex++)
            {
                BoxerModel boxer = boxers[boxerIndex];

                if (!boxer.IsAlive.Value)
                {
                    continue;
                }

                Vector2 position = boxer.Position;
                min = Vector2.Min(min, position);
                max = Vector2.Max(max, position);
                aliveCount++;

                if (boxer.Id == humanId)
                {
                    playerPosition = position;
                    playerAlive = true;
                }
            }

            if (aliveCount == 0)
            {
                return false;
            }

            center = (min + max) * 0.5f;

            // Lean toward the player so the person holding the controller is never the one
            // pushed to the edge of frame.
            if (playerAlive && _playerBias > 0f)
            {
                center = Vector2.Lerp(center, playerPosition, _playerBias);
            }

            Vector2 size = max - min;

            // Half-height drives orthographic size directly; width has to be divided by the
            // aspect first or a wide, flat spread would be framed far too tightly.
            extent = Mathf.Max(size.y * 0.5f, size.x * 0.5f / CurrentAspect());

            return true;
        }
    }
}
